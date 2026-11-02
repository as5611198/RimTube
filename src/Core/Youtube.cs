using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace RimTube.Core
{
    [DataContract]
    public sealed class YoutubeInfo
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "title")] public string Title;
        [DataMember(Name = "webpage_url")] public string WebpageUrl;
        [DataMember(Name = "duration")] public double? Duration;
        [DataMember(Name = "entries")] public List<YoutubeInfo> Entries;
        [DataMember(Name = "is_live")] public bool IsLive;
        [DataMember(Name = "live_status")] public string LiveStatus;
        [DataMember(Name = "availability")] public string Availability;
    }

    public sealed class ProcessResult { public string Output, Error; public int ExitCode; }

    public static class ProcessRunner
    {
        // Windows CommandLineToArgvW quoting: no shell and no interpolated command strings.
        public static string Quote(string s)
        {
            if (s == null || s.IndexOf('\0') >= 0 || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0) throw new ArgumentException(LocalizedText.T("RimTube.InvalidToolArguments"));
            var b = new StringBuilder("\""); int slashes = 0;
            foreach (char c in s)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') b.Append('\\', slashes * 2 + 1).Append(c);
                else b.Append('\\', slashes).Append(c);
                slashes = 0;
            }
            return b.Append('\\', slashes * 2).Append('"').ToString();
        }

        public static ProcessResult Run(string exe, IEnumerable<string> args, CancellationToken token, int timeoutSeconds, Action<string> progress = null)
        {
            token.ThrowIfCancellationRequested();
            var output = new StringBuilder(); var error = new StringBuilder();
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(exe, String.Join(" ", args.Select(Quote)))
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                process.OutputDataReceived += (s, e) => { if (e.Data != null) { lock (output) { if (output.Length < 32 * 1024 * 1024) output.AppendLine(e.Data); } progress?.Invoke(e.Data); } };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (error) { if (error.Length > 32000) error.Remove(0, 16000); error.AppendLine(e.Data); } };
                if (!process.Start()) throw new IOException(LocalizedText.T("RimTube.CouldNotStartTheExternalTool"));
                process.BeginOutputReadLine(); process.BeginErrorReadLine();
                var watch = Stopwatch.StartNew();
                while (!process.WaitForExit(100))
                {
                    if (token.IsCancellationRequested || watch.Elapsed.TotalSeconds > timeoutSeconds)
                    {
                        KillTree(process);
                        process.WaitForExit(5000);
                        token.ThrowIfCancellationRequested();
                        throw new TimeoutException(LocalizedText.T("RimTube.TheExternalToolTimedOutTryAgainLater"));
                    }
                }
                process.WaitForExit(); // Drain asynchronous stdout/stderr before reading their buffers.
                token.ThrowIfCancellationRequested();
                return new ProcessResult { Output = output.ToString(), Error = error.ToString(), ExitCode = process.ExitCode };
            }
        }

        private static void KillTree(Process process)
        {
            try
            {
#if NET10_0_OR_GREATER
                process.Kill(true);
#else
                // taskkill /T also terminates yt-dlp's ffmpeg child on .NET Framework.
                using (var killer = Process.Start(new ProcessStartInfo("taskkill.exe", "/PID " + process.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true }))
                    killer?.WaitForExit(5000);
#endif
            }
            catch { try { if (!process.HasExited) process.Kill(); } catch { } }
        }
    }

    public sealed class YoutubeClient
    {
        private readonly string yt, ffmpeg, js;
        public YoutubeClient(Settings settings)
        { yt = RequireExe(settings.YtDlp, "yt-dlp.exe"); ffmpeg = RequireExe(settings.Ffmpeg, "ffmpeg.exe"); js = settings.JsRuntime; }

        public static string Normalize(string input, bool playlist)
        {
            Uri uri;
            if (!Uri.TryCreate(input?.Trim(), UriKind.Absolute, out uri) || (uri.Scheme != "https" && uri.Scheme != "http") || !String.IsNullOrEmpty(uri.UserInfo))
                throw new ArgumentException(LocalizedText.T("RimTube.EnterAValidYouTubeURL"));
            string host = uri.IdnHost.ToLowerInvariant();
            if (host != "youtube.com" && host != "www.youtube.com" && host != "m.youtube.com" && host != "music.youtube.com" && host != "youtu.be")
                throw new ArgumentException(LocalizedText.T("RimTube.OnlyYouTubeAndYoutuBeURLsAreSupported"));
            var query = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string part in uri.Query.TrimStart('?').Split('&'))
            { int i = part.IndexOf('='); if (i > 0) query[Uri.UnescapeDataString(part.Substring(0, i))] = Uri.UnescapeDataString(part.Substring(i + 1)); }
            string value;
            if (playlist)
            {
                if (!query.TryGetValue("list", out value) || !Regex.IsMatch(value, "^[A-Za-z0-9_-]{8,200}$")) throw new ArgumentException(LocalizedText.T("RimTube.TheURLContainsNoValidPlaylistID"));
                return "https://www.youtube.com/playlist?list=" + value;
            }
            value = null;
            if (host == "youtu.be") value = uri.AbsolutePath.Trim('/').Split('/')[0];
            else if (uri.AbsolutePath == "/watch") query.TryGetValue("v", out value);
            else
            { string[] bits = uri.AbsolutePath.Trim('/').Split('/'); if (bits.Length == 2 && (bits[0] == "shorts" || bits[0] == "embed" || bits[0] == "live")) value = bits[1]; }
            if (value == null || !Regex.IsMatch(value, "^[A-Za-z0-9_-]{11}$")) throw new ArgumentException(LocalizedText.T("RimTube.TheURLContainsNoValidVideoID"));
            return "https://www.youtube.com/watch?v=" + value;
        }

        public YoutubeInfo Inspect(string url, bool playlist, CancellationToken token)
        { token.ThrowIfCancellationRequested(); throw new MediaException(SourceErrorCode.AuthorizationRequired, LocalizedText.T("RimTube.TheLegacyYouTubeParserIsDisabledUseThe")); }

        private YoutubeInfo InspectLegacyDisabled(string url, bool playlist, CancellationToken token)
        {
            var args = BaseArgs();
            args.AddRange(new[] { "--skip-download", "--dump-single-json", playlist ? "--yes-playlist" : "--no-playlist" });
            if (playlist) args.Add("--flat-playlist");
            args.Add("--"); args.Add(Normalize(url, playlist));
            var result = ProcessRunner.Run(yt, args, token, 180);
            if (result.ExitCode != 0) throw new IOException(CleanError(result.Error));
            return Parse(result.Output);
        }

        public static YoutubeInfo Parse(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (YoutubeInfo)new DataContractJsonSerializer(typeof(YoutubeInfo)).ReadObject(stream);
        }

        public void Download(Track track, string tempDirectory, CancellationToken token, Action<string> progress)
        { token.ThrowIfCancellationRequested(); throw new MediaException(SourceErrorCode.AuthorizationRequired, LocalizedText.T("RimTube.PermanentYouTubeDownloadsAreDisabledInThisLegacy")); }

        private void DownloadLegacyDisabled(Track track, string tempDirectory, CancellationToken token, Action<string> progress)
        {
            string url = Normalize(track.Url, false);
            var args = BaseArgs();
            args.AddRange(new[] { "--no-playlist", "--match-filter", "!is_live & !is_upcoming & duration <= 21600", "--newline", "--progress", "--no-simulate",
                "--format", "bestaudio/best", "--extract-audio", "--audio-format", "vorbis", "--audio-quality", "5", "--ffmpeg-location", Path.GetDirectoryName(ffmpeg),
                "--output", Path.Combine(tempDirectory, "audio.%(ext)s"), "--", url });
            var result = ProcessRunner.Run(yt, args, token, 3600, line => { if (line.StartsWith("[download]", StringComparison.Ordinal) || line.StartsWith("[ExtractAudio]", StringComparison.Ordinal)) progress?.Invoke(line); });
            if (result.ExitCode != 0) throw new IOException(CleanError(result.Error));
            if (!File.Exists(Path.Combine(tempDirectory, "audio.ogg"))) throw new IOException(LocalizedText.T("RimTube.TheToolDidNotProducePlayableOGGAudio"));
        }

        public string ConvertLocal(string source, string tempDirectory, CancellationToken token)
        {
            if (!File.Exists(source)) throw new FileNotFoundException(LocalizedText.T("RimTube.LocalAudioWasNotFound"), source);
            string target = Path.Combine(tempDirectory, "audio.ogg");
            var result = ProcessRunner.Run(ffmpeg, new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-i", Path.GetFullPath(source), "-vn", "-map_metadata", "-1", "-c:a", "libvorbis", "-q:a", "5", "-y", target }, token, 1800);
            if (result.ExitCode != 0 || !File.Exists(target)) throw new IOException(LocalizedText.T("RimTube.LocalAudioConversionFailed") + CleanError(result.Error));
            return target;
        }

        private List<string> BaseArgs()
        {
            var args = new List<string> { "--ignore-config", "--no-plugin-dirs", "--no-remote-components", "--no-cache-dir", "--encoding", "utf-8", "--socket-timeout", "20", "--retries", "2" };
            if (!String.IsNullOrWhiteSpace(js)) args.AddRange(new[] { "--js-runtimes", JavaScriptRuntime.ResolverArgument(js) });
            return args;
        }
        public static string RequireExe(string configured, string name)
        {
            if (String.IsNullOrWhiteSpace(configured) || !File.Exists(configured)) throw new FileNotFoundException(LocalizedText.F("RimTube.ToolPathRequired",name));
            return Path.GetFullPath(configured);
        }
        public static string CleanError(string text)
        {
            string safe = Regex.Replace(text ?? LocalizedText.T("RimTube.UnknownError"), @"https?://\S+", LocalizedText.T("RimTube.URL"));
            return safe.Length > 1400 ? safe.Substring(safe.Length - 1400) : safe.Trim();
        }
    }
}
