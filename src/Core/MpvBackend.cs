using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace RimTube.Core
{
    public sealed class MpvBackend : IPlaybackBackend
    {
        private readonly string path;
        private readonly int cache, back, seconds;
        private readonly IntPtr videoWindow;
        private readonly bool headless;
        private readonly Action<string> diagnostics;
        private IntPtr fallbackWindow;
        public bool HasVideo { get; private set; }
        private double videoAspect=16d/9;
        public double VideoAspect {get{lock(gate)return videoAspect;}}
        private bool hasSubtitles,subtitlesVisible,subtitlesRequested=true;
        private double subtitleScale=1.5;
        public double SubtitleScale {get{lock(gate)return subtitleScale;}}
        public bool HasSubtitles {get{lock(gate)return hasSubtitles;}}
        public bool SubtitlesVisible {get{lock(gate)return hasSubtitles&&subtitlesVisible;}}
        public bool SubtitlesRequested {get{lock(gate)return subtitlesRequested;}set{lock(gate)subtitlesRequested=value;}}
        public bool VideoVisible { get; private set; } = true;
        private readonly object gate = new object();
        private readonly SemaphoreSlim commands = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<long, TaskCompletionSource<XElement>> pending = new ConcurrentDictionary<long, TaskCompletionSource<XElement>>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private OwnedProcess owned;
        private NamedPipeClientStream pipe;
        private Task reader;
        private TaskCompletionSource<bool> loaded;
        private long sequence, entry = -1;
        private bool disposed, preparing;
        private volatile bool healthy;
        private PlaybackSnapshot snapshot = new PlaybackSnapshot(BackendState.Idle);
        public string Id => "mpv";
        public PlaybackSnapshot Snapshot { get { lock (gate) return snapshot; } }
        public int ProcessId { get { lock (gate) { try { return owned?.Process?.Id ?? 0; } catch { return 0; } } } }
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint id);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DataContract] private sealed class Command { [DataMember(Name = "command")] public object[] Values; [DataMember(Name = "request_id")] public long Id; }
        public MpvBackend(Settings options, IntPtr videoWindow = default(IntPtr), bool headless = false,Action<string> diagnostics=null)
        { this.diagnostics=diagnostics;this.videoWindow = videoWindow; this.headless = headless; path = options.MpvPath; cache = Math.Max(4, Math.Min(128, options.CacheMiB)); back = Math.Max(0, Math.Min(32, options.BackCacheMiB)); seconds = Math.Max(1, Math.Min(120, options.CacheSeconds)); }
        private static TaskCompletionSource<T> Completion<T>() => new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        private void Set(BackendState state, string error = "", SourceErrorCode? failure = null)
        { lock (gate) snapshot = new PlaybackSnapshot(state, snapshot.SessionId, snapshot.Position, snapshot.Duration, snapshot.Volume, snapshot.BufferedBytes, error, failure); }
        private void EnsureAlive() { if (disposed) throw new ObjectDisposedException(nameof(MpvBackend)); }
        public async Task InitializeAsync(CancellationToken token)
        {
            EnsureAlive(); token.ThrowIfCancellationRequested();
            if (healthy && pipe != null && pipe.IsConnected && owned?.Process != null && !owned.Process.HasExited && Snapshot.State != BackendState.Failed) return;
            CloseProcess(); Set(BackendState.Initializing);
            var dependency = await ToolDependency.CheckAsync(path, "mpv", token).ConfigureAwait(false);
            if (!dependency.Available || !dependency.Version.StartsWith("mpv ", StringComparison.OrdinalIgnoreCase))
                throw new MediaException(SourceErrorCode.MissingTool, LocalizedText.T("RimTube.MpvIsNotConfiguredOrItsVersionCheck"));
            string name = "RimTube-" + Guid.NewGuid().ToString("N");
            var args = new List<string> { "--no-config", "--load-scripts=no", "--ytdl=no", "--idle=yes", "--pause=yes", "--vid=no", "--hwdec=no", "--vd-lavc-threads=2", "--audio-display=no", "--force-window=no", "--terminal=no", "--input-terminal=no", "--input-default-bindings=no", "--cache=yes", "--cache-on-disk=no", "--cache-secs=" + seconds, "--demuxer-max-bytes=" + cache + "MiB", "--demuxer-max-back-bytes=" + back + "MiB", "--volume=0", "--input-ipc-server=\\\\.\\pipe\\" + name };
            if (videoWindow != IntPtr.Zero) args.Add("--wid=" + videoWindow.ToInt64().ToString(CultureInfo.InvariantCulture));
            // The game owns input. This also applies to the fallback standalone window.
            args.AddRange(new[] { "--input-vo-keyboard=no", "--input-cursor=no", "--input-cursor-passthrough=yes", "--input-builtin-bindings=no", "--window-dragging=no", "--focus-on=never" });
            if (headless) { args.Add("--vo=null"); args.Add("--ao=null"); }
            try
            {
                lock (gate)
                {
                    EnsureAlive();
                    owned = new OwnedProcess(new ProcessStartInfo(Path.GetFullPath(path), String.Join(" ", args.Select(ProcessRunner.Quote))) { UseShellExecute = false, CreateNoWindow = true });
                    pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
                }
                await pipe.ConnectAsync(8000, token).ConfigureAwait(false);
                uint server;
                if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out server) || server != (uint)ProcessId) throw new IOException("IPC identity mismatch");
                healthy = true;
                var currentPipe = pipe;
                reader = Task.Run(() => ReadAsync(currentPipe, lifetime.Token));
                if(diagnostics!=null)await RequestAsync(new object[]{"request_log_messages","warn"},token).ConfigureAwait(false);
                int i = 0;
                foreach (string property in new[] { "time-pos", "duration", "pause", "paused-for-cache", "volume", "demuxer-cache-state", "video-params", "track-list", "sub-visibility" })
                    await RequestAsync(new object[] { "observe_property", ++i, property }, token).ConfigureAwait(false);
                Set(BackendState.Idle);
            }
            catch (OperationCanceledException) { CloseProcess(); throw; }
            catch { CloseProcess(); Set(BackendState.Failed, LocalizedText.T("RimTube.CouldNotConnectToMpvCheckTheTool")); throw new MediaException(SourceErrorCode.BackendUnavailable, Snapshot.Error); }
        }
        public async Task PrepareAsync(PlaybackSession session, CancellationToken token)
        {
            EnsureAlive(); session.Validate(); token.ThrowIfCancellationRequested();
            if (session.IsLocal && !File.Exists(session.Location)) throw new MediaException(SourceErrorCode.NotFound, LocalizedText.T("RimTube.LocalAudioWasNotFound"));
            await StopAsync(token).ConfigureAwait(false);
            // Reset per-source headers even when the next song is local.
            await RequestAsync(new object[] { "set_property", "http-header-fields", session.Headers.Select(h => h.Key + ": " + h.Value).ToArray() }, token).ConfigureAwait(false);
            HasVideo = session.HasVideo;
            lock(gate) videoAspect=16d/9;
            await RequestAsync(new object[] { "set_property", "vid", session.HasVideo ? (object)"auto" : "no" }, token).ConfigureAwait(false);
            await RequestAsync(new object[] { "set_property", "audio-files", String.IsNullOrEmpty(session.AudioLocation) ? new string[0] : new[] { session.AudioLocation } }, token).ConfigureAwait(false);
            await RequestAsync(new object[] { "set_property", "sub-files", String.IsNullOrEmpty(session.SubtitleLocation) ? new string[0] : new[] { session.SubtitleLocation } }, token).ConfigureAwait(false);
            await RequestAsync(new object[] { "set_property", "sid", String.IsNullOrEmpty(session.SubtitleLocation) ? "no" : "auto" }, token).ConfigureAwait(false);
            await RequestAsync(new object[] { "set_property", "sub-visibility", SubtitlesRequested }, token).ConfigureAwait(false);
            await RequestAsync(new object[] { "set_property", "sub-scale", SubtitleScale }, token).ConfigureAwait(false);
            await RequestAsync(new object[] { "set_property", "pause", true }, token).ConfigureAwait(false);
            lock (gate)
            {
                preparing = true; entry = -1; loaded = Completion<bool>();
                snapshot = new PlaybackSnapshot(BackendState.Preparing, session.SessionId, volume: snapshot.Volume);
            }
            try
            {
                await RequestAsync(new object[] { "loadfile", session.Location, "replace" }, token).ConfigureAwait(false);
                await AwaitAsync(loaded.Task, token, 15000).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                lock (gate) { preparing = false; Set(BackendState.Ready); }
                UpdateFallbackVisibility();
            }
            catch (OperationCanceledException) { CloseProcess(); Set(BackendState.Stopped); throw; }
            catch (MediaException) { Set(BackendState.Failed, LocalizedText.T("RimTube.CouldNotOpenTheAudioCheckItsFormat")); throw; }
        }
        public Task PlayAsync(CancellationToken token) => ResumeAsync(token);
        public async Task ResumeAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); Set(BackendState.Playing); await RequestAsync(new object[] { "set_property", "pause", false }, token).ConfigureAwait(false); }
        public async Task PauseAsync(CancellationToken token)
        { await RequestAsync(new object[] { "set_property", "pause", true }, token).ConfigureAwait(false); Set(BackendState.Paused); }
        public async Task StopAsync(CancellationToken token)
        {
            EnsureAlive(); token.ThrowIfCancellationRequested();
            if (healthy && pipe != null && pipe.IsConnected && owned?.Process != null && !owned.Process.HasExited && Snapshot.State != BackendState.Failed)
                await RequestAsync(new object[] { "stop" }, token).ConfigureAwait(false);
            else CloseProcess();
            lock (gate) { preparing = false; entry = -1; HasVideo = false; hasSubtitles=false; snapshot = new PlaybackSnapshot(BackendState.Stopped, volume: snapshot.Volume); }
        }
        public async Task SeekAsync(double position, CancellationToken token)
        {
            if (Double.IsNaN(position) || Double.IsInfinity(position)) throw new ArgumentException(LocalizedText.T("RimTube.InvalidPlaybackPosition"));
            await RequestAsync(new object[] { "seek", Math.Max(0, position), "absolute+exact" }, token).ConfigureAwait(false);
        }
        public async Task SetVolumeAsync(double volume, CancellationToken token)
        {
            if (Double.IsNaN(volume) || Double.IsInfinity(volume)) throw new ArgumentException(LocalizedText.T("RimTube.InvalidVolume"));
            await RequestAsync(new object[] { "set_property", "volume", Math.Max(0, Math.Min(1, volume)) * 100 }, token).ConfigureAwait(false);
        }
        public Task SetVideoVisibleAsync(bool visible, CancellationToken token)
        {
            EnsureAlive(); token.ThrowIfCancellationRequested();
            // Presentation is hidden by VideoSurface, not by tearing down the video decoder.
            // Re-enabling vid after audio has advanced needs a new keyframe and causes black frames.
            VideoVisible = visible;
            UpdateFallbackVisibility();
            return Task.CompletedTask;
        }
        private void UpdateFallbackVisibility()
        {
            if (headless || videoWindow != IntPtr.Zero || !HasVideo) return;
            lock (gate)
            {
                var process = owned?.Process;
                if (process == null || process.HasExited) return;
                uint windowProcess;
                if (fallbackWindow == IntPtr.Zero || !IsWindow(fallbackWindow)
                    || GetWindowThreadProcessId(fallbackWindow, out windowProcess) == 0 || windowProcess != (uint)process.Id)
                { process.Refresh(); fallbackWindow = process.MainWindowHandle; }
                if (fallbackWindow != IntPtr.Zero) ShowWindow(fallbackWindow, VideoVisible ? 4 : 0);
            }
        }
        public async Task SetSubtitlesVisibleAsync(bool visible, CancellationToken token)
        {
            SubtitlesRequested=visible;
            await RequestAsync(new object[] { "set_property", "sub-visibility", visible }, token).ConfigureAwait(false);
            lock(gate) subtitlesVisible=visible;
        }
        public async Task<string> DiagnosticPropertyAsync(string name, CancellationToken token)
        {
            if (name != "vid" && name != "sid" && name != "video-params" && name != "track-list" && name != "audio-params" && name != "sub-text" && name != "sub-visibility" && name != "sub-scale") throw new ArgumentException(LocalizedText.T("RimTube.UnsupportedDiagnosticProperty"));
            var response = await RequestAsync(new object[] { "get_property", name }, token).ConfigureAwait(false);
            return response.Element("data")?.ToString() ?? "";
        }
        public async Task SetSubtitleScaleAsync(double value,CancellationToken token)
        {
            EnsureAlive();token.ThrowIfCancellationRequested();
            if(Double.IsNaN(value)||Double.IsInfinity(value))throw new ArgumentException(LocalizedText.T("RimTube.InvalidCaptionSize"));
            lock(gate)subtitleScale=Math.Max(1,Math.Min(3,value));
            if(healthy)await RequestAsync(new object[]{"set_property","sub-scale",SubtitleScale},token).ConfigureAwait(false);
        }
        private async Task<XElement> RequestAsync(object[] values, CancellationToken token)
        {
            EnsureAlive(); token.ThrowIfCancellationRequested();
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
            {
                timeout.CancelAfter(8000);
                bool acquired = false; long id = 0;
                try
                {
                    await commands.WaitAsync(timeout.Token).ConfigureAwait(false); acquired = true;
                    if (pipe == null || !pipe.IsConnected) throw new IOException();
                    id = Interlocked.Increment(ref sequence); var completion = Completion<XElement>(); pending[id] = completion;
                    byte[] bytes;
                    using (var buffer = new MemoryStream())
                    { new DataContractJsonSerializer(typeof(Command), new[] { typeof(string[]) }).WriteObject(buffer, new Command { Values = values, Id = id }); buffer.WriteByte(10); bytes = buffer.ToArray(); }
                    await pipe.WriteAsync(bytes, 0, bytes.Length, timeout.Token).ConfigureAwait(false);
                    using (timeout.Token.Register(() => completion.TrySetCanceled()))
                    {
                        var response = await completion.Task.ConfigureAwait(false);
                        if ((string)response.Element("error") != "success")
                        {
                            ReportDiagnostic("command "+values[0]+(values[0] as string=="set_property"?" "+values[1]:"")+": "+(string)response.Element("error"));
                            throw new MediaException(SourceErrorCode.DecodeFailed, LocalizedText.T("RimTube.MpvCouldNotCompleteTheOperationCheckThe"));
                        }
                        return response;
                    }
                }
                catch (OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested(); lifetime.Token.ThrowIfCancellationRequested();
                    throw new MediaException(SourceErrorCode.Timeout, LocalizedText.T("RimTube.MpvTimedOutStopPlaybackAndTryAgain"));
                }
                catch (IOException) { throw new MediaException(SourceErrorCode.Disconnected, LocalizedText.T("RimTube.DisconnectedFromMpvStartPlaybackAgain")); }
                finally { TaskCompletionSource<XElement> ignored; if (id != 0) pending.TryRemove(id, out ignored); if (acquired) commands.Release(); }
            }
        }
        private static async Task AwaitAsync(Task task, CancellationToken token, int milliseconds)
        {
            using (var timer = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var delay = Task.Delay(milliseconds, timer.Token);
                if (await Task.WhenAny(task, delay).ConfigureAwait(false) != task) { token.ThrowIfCancellationRequested(); throw new MediaException(SourceErrorCode.Timeout, LocalizedText.T("RimTube.AudioPreparationTimedOutTryAgainOrChoose")); }
                timer.Cancel(); await task.ConfigureAwait(false);
            }
        }
        private async Task ReadAsync(NamedPipeClientStream connection, CancellationToken token)
        {
            try
            {
                byte[] block = new byte[4096]; using (var line = new MemoryStream())
                {
                    int count;
                    while ((count = await connection.ReadAsync(block, 0, block.Length, token).ConfigureAwait(false)) > 0)
                        for (int i = 0; i < count; i++)
                        {
                            if (block[i] == 10)
                            {
                                if (line.Length > 0) Handle(line.ToArray()); line.SetLength(0);
                            }
                            else { if (line.Length >= 65536) throw new IOException("IPC frame too large"); line.WriteByte(block[i]); }
                        }
                }
            }
            catch { /* Never expose media URLs from raw IPC errors. */ }
            finally
            {
                lock (gate)
                    if (!disposed && ReferenceEquals(pipe, connection))
                    {
                        healthy = false;
                        Set(BackendState.Failed, LocalizedText.T("RimTube.MpvExitedOrDisconnectedCheckTheToolAnd"), SourceErrorCode.Disconnected); loaded?.TrySetException(new MediaException(SourceErrorCode.Disconnected, snapshot.Error));
                        foreach (var task in pending.Values) task.TrySetException(new MediaException(SourceErrorCode.Disconnected, LocalizedText.T("RimTube.DisconnectedFromMpv")));
                    }
            }
        }
        private void ReportDiagnostic(string message)
        {
            if(diagnostics==null)return;
            try{diagnostics(YoutubeClient.CleanError(message));}catch{/* Diagnostic observers cannot interrupt IPC. */}
        }
        private void Handle(byte[] data)
        {
            XElement json;
            using (var input = JsonReaderWriterFactory.CreateJsonReader(data, new XmlDictionaryReaderQuotas { MaxDepth = 16, MaxStringContentLength = 65536, MaxArrayLength = 65536 })) json = XElement.Load(input);
            long requestId;
            if (Int64.TryParse((string)json.Element("request_id"), out requestId))
            { TaskCompletionSource<XElement> task; if (pending.TryGetValue(requestId, out task)) task.TrySetResult(json); return; }
            string ev = (string)json.Element("event");
            if(ev=="log-message")
            {ReportDiagnostic((string)json.Element("prefix")+": "+(string)json.Element("text"));return;}
            lock (gate)
            {
                if (ev == "start-file") { Int64.TryParse((string)json.Element("playlist_entry_id"), out entry); }
                else if (ev == "file-loaded" && preparing) loaded?.TrySetResult(true);
                else if (ev == "end-file")
                {
                    long ended; Int64.TryParse((string)json.Element("playlist_entry_id"), out ended);
                    if (ended != entry) return;
                    string reason = (string)json.Element("reason");
                    if (reason == "error") { Set(BackendState.Failed, LocalizedText.T("RimTube.TheAudioCannotBeDecodedOrItsSource"), SourceErrorCode.DecodeFailed); loaded?.TrySetException(new MediaException(SourceErrorCode.DecodeFailed, snapshot.Error)); }
                    else if (reason == "eof" && !preparing) Set(BackendState.Ended);
                }
                else if (ev == "property-change")
                {
                    string name = (string)json.Element("name"); var value = json.Element("data"); if (value == null) return;
                    if(name=="track-list") {hasSubtitles=value.Elements().Any(t=>(string)t.Element("type")=="sub");return;}
                    if(name=="sub-visibility") {subtitlesVisible=value.Value=="true";return;}
                    if(name=="video-params")
                    {
                        if(!HasVideo){videoAspect=16d/9;return;}
                        double w,h;
                        if(Double.TryParse((string)(value.Element("dw")??value.Element("w")),NumberStyles.Float,CultureInfo.InvariantCulture,out w)
                            &&Double.TryParse((string)(value.Element("dh")??value.Element("h")),NumberStyles.Float,CultureInfo.InvariantCulture,out h)&&w>0&&h>0)
                            videoAspect=Math.Max(.1,Math.Min(10,w/h));
                        return;
                    }
                    double number; Double.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
                    var s = snapshot; BackendState state = s.State;
                    if (name == "paused-for-cache" && (state == BackendState.Playing || state == BackendState.Buffering)) state = value.Value == "true" ? BackendState.Buffering : BackendState.Playing;
                    long bytes = s.BufferedBytes;
                    if (name == "demuxer-cache-state") { long n; if (Int64.TryParse((string)value.Element("fw-bytes"), out n)) bytes = n; }
                    snapshot = new PlaybackSnapshot(state, s.SessionId, name == "time-pos" ? Math.Max(0, number) : s.Position, name == "duration" ? Math.Max(0, number) : s.Duration, name == "volume" ? number / 100 : s.Volume, bytes, s.Error, s.Failure);
                }
            }
        }
        private void CloseProcess()
        {
            NamedPipeClientStream connection; OwnedProcess child; TaskCompletionSource<bool> preparation;
            lock (gate)
            {
                connection = pipe; pipe = null; healthy = false;
                child = owned; owned = null; preparation = loaded; loaded = null;
                fallbackWindow = IntPtr.Zero;
            }
            // Closing overlapped IO can complete reader callbacks. Never hold their snapshot lock.
            connection?.Dispose(); child?.Dispose(); preparation?.TrySetCanceled();
        }
        public void Dispose()
        {
            lock (gate) { if (disposed) return; disposed = true; }
            lifetime.Cancel(); CloseProcess(); Set(BackendState.Disposed);
        }
    }
}
