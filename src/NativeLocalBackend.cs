using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RimTube.Core;
using UnityEngine;
using UnityEngine.Networking;
using Verse;

namespace RimTube
{
    // Reuses the native local decoder; engine calls are dispatched on the game thread.
    internal sealed class NativeLocalBackend : IPlaybackBackend
    {
        private sealed class Work { public Action Action; public TaskCompletionSource<bool> Done; public CancellationToken Token; }
        private readonly ConcurrentQueue<Work> queue = new ConcurrentQueue<Work>();
        private readonly MonoBehaviour host;
        private readonly AudioSource source;
        private readonly object gate = new object();
        private PlaybackSnapshot snapshot = new PlaybackSnapshot(BackendState.Idle);
        private UnityWebRequest request;
        private Coroutine loading;
        private TaskCompletionSource<bool> prepared;
        private CancellationTokenRegistration prepareCancellation;
        private volatile bool disposed;
        private int count;
        private bool started;
        private bool borrowedClip;
        private float clipGain=1;
        private float began;
        public string Id => "native-local";
        public PlaybackSnapshot Snapshot { get { lock (gate) return snapshot; } }
        public NativeLocalBackend(MonoBehaviour host)
        {
            this.host = host; source = host.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = false; source.spatialBlend = 0;
            source.ignoreListenerPause = true; source.ignoreListenerVolume = true;
            source.bypassEffects = true; source.bypassListenerEffects = true; source.bypassReverbZones = true;
        }
        private void State(BackendState state, string error = "")
        { lock (gate) snapshot = new PlaybackSnapshot(state, snapshot.SessionId, source.clip != null ? source.time : 0, source.clip != null ? source.clip.length : 0, source.volume, error: error); }
        private Task OnMain(Action action, CancellationToken token)
        {
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(NativeLocalBackend)); token.ThrowIfCancellationRequested();
                if (Interlocked.Increment(ref count) > 64) { Interlocked.Decrement(ref count); throw new MediaException(SourceErrorCode.BackendUnavailable, LocalizedText.T("RimTube.TooManyPlaybackOperationsTryAgainShortly")); }
                var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                queue.Enqueue(new Work { Action = action, Done = done, Token = token }); return done.Task;
            }
        }
        public void Pump()
        {
            Work work;
            for (int i = 0; i < 8 && queue.TryDequeue(out work); i++)
            {
                Interlocked.Decrement(ref count);
                if (disposed || work.Token.IsCancellationRequested) { work.Done.TrySetCanceled(); continue; }
                try { work.Action(); work.Done.TrySetResult(true); } catch (Exception ex) { work.Done.TrySetException(ex); }
            }
            if (disposed) return;
            if (Snapshot.State == BackendState.Playing)
            {
                if (!source.isPlaying && Time.realtimeSinceStartup - began > .35f) State(BackendState.Ended);
                else State(BackendState.Playing);
            }
        }
        public Task InitializeAsync(CancellationToken token) => OnMain(() => { }, token);
        public async Task PrepareAsync(PlaybackSession session, CancellationToken token)
        {
            if (session.HasVideo) throw new MediaException(SourceErrorCode.Unsupported, LocalizedText.T("RimTube.SwitchToTheMpvBackendToPlayVideo"));
            session.Validate();
            if (!session.IsLocal) throw new MediaException(SourceErrorCode.Unsupported, LocalizedText.T("RimTube.TheNativeBackendOnlySupportsImportedLocalAudio"));
            if(session.GameSongDef!="")
            {
                await OnMain(()=> {
                    Unload();var song=DefDatabase<SongDef>.GetNamedSilentFail(session.GameSongDef);
                    if(song?.clip==null||song.clip.length<=0) throw new MediaException(SourceErrorCode.NotFound,LocalizedText.T("RimTube.TheLoadedGameTrackWasNotFoundCheck"));
                    borrowedClip=true;source.clip=song.clip;started=false;
                    clipGain=float.IsNaN(song.volume)||float.IsInfinity(song.volume)?1:Math.Max(0,song.volume);
                    lock(gate) snapshot=new PlaybackSnapshot(BackendState.Ready,session.SessionId,duration:song.clip.length,volume:source.volume);
                },token).ConfigureAwait(false);
                return;
            }
            Task ready = null;
            await OnMain(() =>
            {
                Unload();
                lock (gate) snapshot = new PlaybackSnapshot(BackendState.Preparing, session.SessionId);
                prepared = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); ready = prepared.Task;
                var ticket = prepared; prepareCancellation = token.Register(() => ticket.TrySetCanceled());
                loading = host.StartCoroutine(Load(session.Location));
            }, token).ConfigureAwait(false);
            await ready.ConfigureAwait(false);
        }
        private IEnumerator Load(string path)
        {
            var type = Path.GetExtension(path).ToLowerInvariant() == ".wav" ? AudioType.WAV : Path.GetExtension(path).ToLowerInvariant() == ".mp3" ? AudioType.MPEG : AudioType.OGGVORBIS;
            request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type);
            ((DownloadHandlerAudioClip)request.downloadHandler).streamAudio = true; request.timeout = 60;
            yield return request.SendWebRequest();
#if RW17
            bool bad = request.result != UnityWebRequest.Result.Success;
#else
#pragma warning disable 0618
            bool bad = request.isNetworkError || request.isHttpError;
#pragma warning restore 0618
#endif
            AudioClip clip = null;
            if (!bad) { try { clip = DownloadHandlerAudioClip.GetContent(request); bad = clip == null || clip.length <= 0; } catch { bad = true; } }
            request.Dispose(); request = null; loading = null; prepareCancellation.Dispose();
            if (bad)
            {
                if (clip != null) UnityEngine.Object.Destroy(clip); State(BackendState.Failed, LocalizedText.T("RimTube.CouldNotDecodeTheLocalAudio"));
                prepared.TrySetException(new MediaException(SourceErrorCode.DecodeFailed, LocalizedText.T("RimTube.CouldNotDecodeTheLocalAudio"))); yield break;
            }
            source.clip = clip; started = false; State(BackendState.Ready); prepared.TrySetResult(true);
        }
        public Task PlayAsync(CancellationToken token) => ResumeAsync(token);
        public Task ResumeAsync(CancellationToken token) => OnMain(() => { if (source.clip == null) throw new MediaException(SourceErrorCode.NotFound, LocalizedText.T("RimTube.PrepareLocalAudioFirst")); if (started) source.UnPause(); else source.Play(); started = true; began = Time.realtimeSinceStartup; State(BackendState.Playing); }, token);
        public Task PauseAsync(CancellationToken token) => OnMain(() => { source.Pause(); State(BackendState.Paused); }, token);
        public Task StopAsync(CancellationToken token) => OnMain(() => { Unload(); State(BackendState.Stopped); }, token);
        public Task SeekAsync(double position, CancellationToken token) => OnMain(() => { if (source.clip != null) source.time = Mathf.Clamp((float)position, 0, Math.Max(0, source.clip.length - .05f)); State(Snapshot.State); }, token);
        public Task SetVolumeAsync(double volume, CancellationToken token) => OnMain(() => { source.volume = Mathf.Clamp01((float)volume*clipGain); State(Snapshot.State); }, token);
        private void Unload()
        {
            prepareCancellation.Dispose(); prepared?.TrySetCanceled();
            if (request != null) { request.Abort(); request.Dispose(); request = null; }
            if (loading != null) { host.StopCoroutine(loading); loading = null; }
            source.Stop(); var clip = source.clip; source.clip = null; if (clip != null && !borrowedClip) UnityEngine.Object.Destroy(clip); started = false;
            borrowedClip=false;clipGain=1;
        }
        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return; disposed = true; Unload(); State(BackendState.Disposed); UnityEngine.Object.Destroy(source);
                Work work; while (queue.TryDequeue(out work)) { Interlocked.Decrement(ref count); work.Done.TrySetCanceled(); }
            }
        }
    }
}
