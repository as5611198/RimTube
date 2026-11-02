using System;
using RimTube.Core;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using System.Linq;

namespace RimTube.Sources
{
    public sealed class LocalFileSource : IMediaSource
    {
        private readonly Library library;
        public LocalFileSource(Library library) { this.library = library; }
        public string Id => "local";
        public SourceCapabilities Capabilities => SourceCapabilities.Identity | SourceCapabilities.Metadata | SourceCapabilities.Sessions | SourceCapabilities.LocalFiles | SourceCapabilities.Seek;
        public bool CanHandle(string input) => !String.IsNullOrEmpty(input) && Path.IsPathRooted(input) && File.Exists(input);
        public Task<Track> GetTrackAsync(string input, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!CanHandle(input)) throw new MediaException(SourceErrorCode.NotFound, LocalizedText.T("RimTube.LocalAudioWasNotFound"));
            var file = new FileInfo(Path.GetFullPath(input)); string id;
            using (var hash = SHA256.Create())
                id = "local_" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(file.FullName.ToUpperInvariant() + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks))).Replace("-", "").ToLowerInvariant();
            return Task.FromResult(new Track { Id = id, SourceId = id, Title = Path.GetFileNameWithoutExtension(input), SourceType = Id, Availability = SourceAvailability.Available });
        }
        public Task<PlaylistPage> GetPlaylistPageAsync(string source, string continuation, int pageSize, CancellationToken token)
        { token.ThrowIfCancellationRequested(); throw new MediaException(SourceErrorCode.Unsupported, LocalizedText.T("RimTube.LocalSourcesDoNotProvideOnlinePlaylists")); }
        public Task<PlaybackSession> CreateSessionAsync(Track track, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (track == null || track.IsYouTube || !library.IsReady(track.Id)) throw new MediaException(SourceErrorCode.NotFound, LocalizedText.T("RimTube.ImportedLocalAudioWasNotFoundImportIt"));
            return Task.FromResult(new PlaybackSession(track.Id, library.AudioPath(track), true, true, true));
        }
        public Task<DependencyInfo> CheckDependencyAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(new DependencyInfo { Available = true, Message = LocalizedText.T("RimTube.LocalMusicLibraryAvailable") }); }
    }

    // Optional public-content resolver. It never downloads audio or reads credentials.
    public sealed class YouTubeSourceAdapter : IMediaSource, IDisposable
    {
        private readonly string tool;
        private readonly YouTubeResolver resolver;
        private readonly bool streaming;
        private readonly Library library;
        private readonly YouTubePlaylistResolver playlists;
        private readonly Func<DateTime> utcNow;
        private readonly object handoffGate = new object();
        private sealed class ResolutionHandoff
        {
            public YouTubeResolver.Info Info;
            public string Key;
            public DateTime Until;
        }
        private ResolutionHandoff handoff;
        private Timer handoffTimer;
        private bool disposed;
        private readonly object playlistGate = new object();
        private PlaylistReadResult playlistSnapshot;
        private string playlistSource, playlistKey;
        public YouTubeSourceAdapter(string tool) : this(new Settings { YtDlp = tool }) { }
        public YouTubeSourceAdapter(Settings settings, bool enableStreaming = true, Library library = null)
            : this(settings, enableStreaming, library, () => DateTime.UtcNow) { }
        internal YouTubeSourceAdapter(Settings settings, bool enableStreaming, Library library, Func<DateTime> utcNow)
        { this.utcNow = utcNow; this.library = library; tool = settings.YtDlp; resolver = new YouTubeResolver(settings); playlists = new YouTubePlaylistResolver(settings); streaming = enableStreaming; }
        public string Id => "youtube";
        public SourceCapabilities Capabilities => SourceCapabilities.Identity | SourceCapabilities.Metadata | SourceCapabilities.PlaylistPages | (streaming ? SourceCapabilities.Sessions | SourceCapabilities.Seek : SourceCapabilities.None);
        public bool CanHandle(string input)
        { try { YoutubeClient.Normalize(input, false); return true; } catch { try { YoutubeClient.Normalize(input, true); return true; } catch { return false; } } }
        public static Track Identify(string input)
        {
            string url = YoutubeClient.Normalize(input, false), id = url.Substring(url.LastIndexOf('=') + 1);
            return new Track { Id = "yt_" + id, SourceId = id, SourceType = "youtube", Url = url, Title = "YouTube · " + id, Availability = SourceAvailability.Unknown };
        }
        public Task<Track> GetTrackAsync(string input, CancellationToken token) => GetTrackAsync(input, token, new MediaChoice());
        public async Task<Track> GetTrackAsync(string input, CancellationToken token, MediaChoice choice)
        {
            var selection = (choice ?? new MediaChoice()).Copy();
            var track = Identify(input); selection.Store(track);
            var info = await resolver.ResolveAsync(track.Url, token, selection).ConfigureAwait(false);
            track.Title = String.IsNullOrWhiteSpace(info.Title) ? track.Title : info.Title;
            track.Duration = info.Duration.HasValue && info.Duration.Value > 0 && !Double.IsInfinity(info.Duration.Value) ? info.Duration.Value : 0;
            track.Availability = SourceAvailability.Available; track.Metadata["media.live"] = (info.Live || info.LiveStatus == "is_live").ToString();
            track.Metadata["media.subtitleLanguages"] = String.Join(",", (info.Subtitles?.Keys ?? Enumerable.Empty<string>()).Concat(info.AutoCaptions?.Keys ?? Enumerable.Empty<string>()).Select(SubtitleLanguage.Normalize).Where(s=>s!="").Distinct().Take(300));
            track.Metadata["media.subtitleManualLanguages"]=String.Join(",",(info.Subtitles?.Keys??Enumerable.Empty<string>()).Select(SubtitleLanguage.Normalize).Where(s=>s!="").Distinct().Take(300));
            track.Metadata["media.subtitleAutoLanguages"]=String.Join(",",(info.AutoCaptions?.Keys??Enumerable.Empty<string>()).Select(SubtitleLanguage.Normalize).Where(s=>s!="").Distinct().Take(300));
            token.ThrowIfCancellationRequested();
            if (streaming && selection.Delivery != MediaDelivery.Download) OfferHandoff(track.Url, selection, info);
            return track;
        }
        private static string HandoffKey(string url, MediaChoice choice) => url + "\n" + choice.Video + "\n"
            + choice.Height.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + choice.Delivery + "\n" + choice.Subtitle + "\n" + choice.AutoSubtitles;
        private void ClearHandoff()
        {
            handoff = null;
            handoffTimer?.Dispose(); handoffTimer = null;
        }
        private void OfferHandoff(string url, MediaChoice choice, YouTubeResolver.Info info)
        {
            var candidate = YouTubeResolver.Session(info, "yt_" + info.Id, choice.Video);
            lock (handoffGate)
            {
                ClearHandoff();
                if (disposed || candidate.ExpiresUtc <= DateTime.UtcNow.AddSeconds(5)) return;
                var offered = new ResolutionHandoff { Info = info, Key = HandoffKey(url, choice), Until = utcNow().AddSeconds(30) };
                handoff = offered;
                // Exactly one RAM entry, used once. The timer also releases unused signed URLs.
                handoffTimer = new Timer(_ =>
                {
                    lock (handoffGate) if (ReferenceEquals(handoff, offered)) ClearHandoff();
                }, null, 30000, Timeout.Infinite);
            }
        }
        private YouTubeResolver.Info TakeHandoff(string url, MediaChoice choice)
        {
            lock (handoffGate)
            {
                var offered = handoff;
                ClearHandoff();
                return !disposed && offered != null && offered.Key == HandoffKey(url, choice) && utcNow() < offered.Until ? offered.Info : null;
            }
        }
        public async Task<PlaylistPage> GetPlaylistPageAsync(string source, string continuation, int pageSize, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string url = YoutubeClient.Normalize(source, true);
            pageSize = Math.Max(1, Math.Min(500, pageSize));
            PlaylistReadResult fresh = null;
            if (String.IsNullOrEmpty(continuation)) fresh = await playlists.ReadAsync(url, token).ConfigureAwait(false);
            lock (playlistGate)
            {
                token.ThrowIfCancellationRequested();
                int offset = 0;
                if (fresh != null) { playlistSnapshot = fresh; playlistSource = url; playlistKey = Guid.NewGuid().ToString("N"); }
                else
                {
                    string[] parts = (continuation ?? "").Split(':');
                    if (playlistSnapshot == null || playlistSource != url || parts.Length != 2 || parts[0] != playlistKey
                        || !Int32.TryParse(parts[1], out offset) || offset <= 0 || offset >= playlistSnapshot.Tracks.Count)
                        throw new MediaException(SourceErrorCode.Incomplete, LocalizedText.T("RimTube.ThePlaylistPreviewExpiredReadItAgainThe"));
                }
                var snapshot = playlistSnapshot;
                bool last = offset + pageSize >= snapshot.Tracks.Count;
                var page = new PlaylistPage { SourcePlaylistId = snapshot.SourceId, Title = snapshot.Title,
                    IsComplete = last && snapshot.Complete, IsPartial = last && !snapshot.Complete, Message = snapshot.Message,
                    NextPage = last ? "" : playlistKey + ":" + (offset + pageSize).ToString(System.Globalization.CultureInfo.InvariantCulture) };
                page.Tracks.AddRange(snapshot.Tracks.Skip(offset).Take(pageSize));
                return page;
            }
        }
        public async Task<PlaybackSession> CreateSessionAsync(Track track, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (track == null || !track.IsYouTube) throw new MediaException(SourceErrorCode.Unsupported, LocalizedText.T("RimTube.ThisIsNotAYouTubeTrack"));
            var choice = MediaChoice.From(track);
            if (library != null && choice.Delivery != MediaDelivery.Stream)
            {
                if (SavedMedia.Ready(library, track, choice.Video)) return SavedMedia.Session(library, track, choice);
                if (choice.Delivery == MediaDelivery.Download) throw new MediaException(SourceErrorCode.NotFound, LocalizedText.T("RimTube.FinishDownloadingFirstOrSelectStreaming"));
            }
            if (!streaming) throw new MediaException(SourceErrorCode.Unsupported, LocalizedText.T("RimTube.SelectTheMpvBackendInSettingsForYouTube"));
            string url = YoutubeClient.Normalize(track.Url, false);
            var info = TakeHandoff(url, choice);
            if (info != null)
            {
                var prepared = YouTubeResolver.Session(info, track.Id, choice.Video);
                if (prepared.ExpiresUtc > DateTime.UtcNow.AddSeconds(5))
                { token.ThrowIfCancellationRequested(); prepared.Validate(); return prepared; }
            }
            info = await resolver.ResolveAsync(url, token, choice).ConfigureAwait(false);
            return YouTubeResolver.Session(info, track.Id, choice.Video);
        }
        public void Dispose() { lock (handoffGate) { disposed = true; ClearHandoff(); } }
        public Task<DependencyInfo> CheckDependencyAsync(CancellationToken token) => ToolDependency.CheckAsync(tool, "yt-dlp", token);
    }

}
