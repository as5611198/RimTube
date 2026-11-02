using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using RimTube.Core;

namespace RimTube.Sources
{
    public sealed class DownloadedMedia
    {
        public string MediaPath = "", SubtitlePath = "";
    }
    public sealed class MediaDownloader
    {
        private readonly Settings settings;
        public MediaDownloader(Settings settings) { this.settings = settings; }
        public DownloadedMedia Download(Track track, MediaChoice choice, string directory, CancellationToken token, Action<string> progress = null)
        {
            choice = choice.Copy(); choice.Validate(); token.ThrowIfCancellationRequested();
            string url = YoutubeClient.Normalize(track.Url, false);
            string tool = YoutubeClient.RequireExe(settings.YtDlp, "yt-dlp.exe");
            string ffmpeg = YoutubeClient.RequireExe(settings.Ffmpeg, "ffmpeg.exe");
            // Refuse unbounded live recording. Never reuse transient stream URLs for a download job.
            var info = new YouTubeResolver(settings).ResolveAsync(url, token, choice).GetAwaiter().GetResult();
            if (info.Live || info.LiveStatus == "is_live") throw new MediaException(SourceErrorCode.Unsupported, LocalizedText.T("RimTube.UseStreamingForLiveContentUnlimitedRecordingJobs"));
            Directory.CreateDirectory(directory);
            var args = new List<string> { "--ignore-config", "--no-plugin-dirs", "--no-remote-components", "--no-cache-dir", "--no-cookies", "--no-cookies-from-browser",
                "--no-js-runtimes", "--encoding", "utf-8", "--socket-timeout", "15", "--retries", "1", "--extractor-retries", "1",
                "--no-playlist", "--no-simulate", "--newline", "--progress", "--no-overwrites", "--no-mtime", "--no-write-info-json", "--no-write-thumbnail",
                "--match-filter", "!is_live & !is_upcoming", "--format", choice.Format,
                "--postprocessor-args", "ffmpeg:-threads 2",
                "--ffmpeg-location", Path.GetDirectoryName(ffmpeg), "--output", Path.Combine(Path.GetFullPath(directory), "media.%(ext)s") };
            if (!String.IsNullOrWhiteSpace(settings.JsRuntime)) args.AddRange(new[] { "--js-runtimes", JavaScriptRuntime.ResolverArgument(YoutubeClient.RequireExe(settings.JsRuntime, "JavaScript")) });
            if (choice.Video) args.AddRange(new[] { "--merge-output-format", "mkv" });
            else args.AddRange(new[] { "--extract-audio", "--audio-format", "vorbis", "--audio-quality", "5" });
            if (choice.Subtitle != "off"&&!String.IsNullOrEmpty(info.SelectedSubtitleKey))
            {
                args.AddRange(new[] { "--write-subs", "--sub-langs", "^"+System.Text.RegularExpressions.Regex.Escape(info.SelectedSubtitleKey)+"$", "--sub-format", "vtt/srt/best" });
                if (info.SelectedAutoSubtitle) args.Add("--write-auto-subs");
            }
            args.Add("--"); args.Add(url);
            var response = ProcessRunner.Run(tool, args, token, 3600,
                line => { if (line.StartsWith("[download]", StringComparison.Ordinal)) progress?.Invoke(YoutubeClient.CleanError(line)); });
            token.ThrowIfCancellationRequested();
            if (response.ExitCode != 0) throw new MediaException(SourceErrorCode.NotFound, LocalizedText.T("RimTube.MediaDownloadFailedExistingCopiesWerePreservedCheck"));
            var extensions = choice.Video ? new[] { ".mkv", ".mp4", ".webm" } : new[] { ".ogg" };
            string media = Directory.EnumerateFiles(directory).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == "media" && extensions.Contains(Path.GetExtension(p).ToLowerInvariant()));
            if (media == null || new FileInfo(media).Length == 0) throw new MediaException(SourceErrorCode.Incomplete, LocalizedText.T("RimTube.TheDownloadDidNotProduceCompleteMediaExisting"));
            string subtitle = Directory.EnumerateFiles(directory).FirstOrDefault(p => Path.GetFileName(p).StartsWith("media.", StringComparison.Ordinal)
                && (p.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)));
            return new DownloadedMedia { MediaPath = media, SubtitlePath = subtitle ?? "" };
        }
    }
}
