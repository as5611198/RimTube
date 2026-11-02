using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RimTube.Core
{
    [Flags]
    public enum SourceCapabilities { None = 0, Identity = 1, Metadata = 2, PlaylistPages = 4, Sessions = 8, LocalFiles = 16, Seek = 32 }
    public enum SourceAvailability { Unknown, Available, Missing, AuthorizationRequired, Unavailable }
    public enum SourceErrorCode { MissingTool, AuthorizationRequired, NotFound, Expired, Unsupported, Incomplete, BackendUnavailable, DecodeFailed, Disconnected, Timeout }
    public enum BackendState { Idle, Initializing, Preparing, Ready, Playing, Paused, Buffering, Ended, Stopped, Failed, Disposed }
    public enum PlaylistType { General, Combat, Custom }
    public enum SyncState { Never, Complete, Partial, Failed, AdapterRequired }

    public sealed class MediaException : Exception
    {
        public SourceErrorCode Code { get; private set; }
        public MediaException(SourceErrorCode code, string message) : base(message) { Code = code; }
    }
    public sealed class DependencyInfo
    {
        public bool Available;
        public string Version = "", Message = "";
    }
    public sealed class PlaylistPage
    {
        public string SourcePlaylistId = "", Title = "", NextPage = "";
        public readonly List<Track> Tracks = new List<Track>();
        public bool IsComplete, IsPartial;
        public string Message = "";
    }
    // Ephemeral media locations live only in sessions, never in the persistent Track model.
    public sealed class PlaybackSession
    {
        public string SessionId { get; private set; } = Guid.NewGuid().ToString("N");
        public string TrackId { get; private set; }
        public string Location { get; private set; }
        public bool IsLocal { get; private set; }
        public bool CanSeek { get; private set; }
        public bool Authorized { get; private set; }
        public DateTime? ExpiresUtc { get; private set; }
        public bool HasVideo { get; private set; }
        public bool IsLive { get; private set; }
        public string AudioLocation { get; private set; }
        public string SubtitleLocation { get; private set; }
        public string GameSongDef {get;private set;}
        public System.Collections.ObjectModel.ReadOnlyDictionary<string,string> Headers { get; private set; }
        public PlaybackSession(string trackId, string location, bool local, bool seek, bool authorized, DateTime? expiresUtc = null, IDictionary<string,string> headers = null,
            bool video = false, bool live = false, string audioLocation = "", string subtitleLocation = "", string gameSongDef = "")
        {
            TrackId = trackId; Location = location; IsLocal = local; CanSeek = seek; Authorized = authorized; ExpiresUtc = expiresUtc;
            HasVideo = video; IsLive = live; AudioLocation = audioLocation ?? ""; SubtitleLocation = subtitleLocation ?? "";
            GameSongDef=gameSongDef??"";
            var safe = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            if (headers != null) foreach (var pair in headers)
            {
                if (!String.Equals(pair.Key, "User-Agent", StringComparison.OrdinalIgnoreCase)
                    && !String.Equals(pair.Key, "Referer", StringComparison.OrdinalIgnoreCase)
                    && !String.Equals(pair.Key, "Origin", StringComparison.OrdinalIgnoreCase)
                    && !String.Equals(pair.Key, "Accept", StringComparison.OrdinalIgnoreCase)
                    && !String.Equals(pair.Key, "Accept-Language", StringComparison.OrdinalIgnoreCase)) continue;
                if (pair.Value == null || pair.Value.Length > 4096 || pair.Value.IndexOfAny(new[] {'\r','\n','\0'}) >= 0)
                    throw new MediaException(SourceErrorCode.Unsupported, LocalizedText.T("RimTube.TheSourceContainsInvalidPlaybackHeaders"));
                safe[pair.Key] = pair.Value;
            }
            Headers = new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(safe);
        }
        public void Validate()
        {
            if (!Authorized) throw new MediaException(SourceErrorCode.AuthorizationRequired, LocalizedText.T("RimTube.ThisSourceHasNotBeenApprovedForThis"));
            if (ExpiresUtc.HasValue && ExpiresUtc.Value <= DateTime.UtcNow) throw new MediaException(SourceErrorCode.Expired, LocalizedText.T("RimTube.ThePlaybackSourceHasExpiredPrepareItAgain"));
            if (String.IsNullOrWhiteSpace(Location)) throw new MediaException(SourceErrorCode.NotFound, LocalizedText.T("RimTube.ThePlaybackSourceIsMissing"));
            if(GameSongDef!="")
            {
                if(!IsLocal||HasVideo||AudioLocation!=""||SubtitleLocation!=""||Location!="rimworld-song:"+GameSongDef)
                    throw new MediaException(SourceErrorCode.Unsupported,LocalizedText.T("RimTube.InvalidGameMusicSession"));
                return;
            }
            if (!IsLocal)
            {
                Uri uri;
                if (!Uri.TryCreate(Location, UriKind.Absolute, out uri) || (uri.Scheme != "http" && uri.Scheme != "https") || !String.IsNullOrEmpty(uri.UserInfo))
                    throw new MediaException(SourceErrorCode.Unsupported, LocalizedText.T("RimTube.UnsupportedNetworkMediaProtocol"));
            }
            foreach (string extra in new[] { AudioLocation, SubtitleLocation })
            {
                if (String.IsNullOrEmpty(extra)) continue;
                if (IsLocal) { if (!System.IO.Path.IsPathRooted(extra)) throw new MediaException(SourceErrorCode.Unsupported, LocalizedText.T("RimTube.InvalidLocalSupplementaryMediaPath")); }
                else { Uri uri; if (!Uri.TryCreate(extra, UriKind.Absolute, out uri) || uri.Scheme != "https" || !String.IsNullOrEmpty(uri.UserInfo)) throw new MediaException(SourceErrorCode.Unsupported, LocalizedText.T("RimTube.InvalidSupplementaryMediaURL")); }
            }
        }
    }
    public sealed class PlaybackSnapshot
    {
        public readonly BackendState State;
        public readonly string SessionId, Error;
        public readonly double Position, Duration, Volume;
        public readonly long BufferedBytes;
        public readonly SourceErrorCode? Failure;
        public PlaybackSnapshot(BackendState state, string session = "", double position = 0, double duration = 0, double volume = 0, long bufferedBytes = 0, string error = "", SourceErrorCode? failure = null)
        { State = state; SessionId = session; Position = position; Duration = duration; Volume = volume; BufferedBytes = bufferedBytes; Error = error; Failure = failure; }
    }
    public interface IMediaSource
    {
        string Id { get; }
        SourceCapabilities Capabilities { get; }
        bool CanHandle(string input);
        Task<Track> GetTrackAsync(string input, CancellationToken token);
        Task<PlaylistPage> GetPlaylistPageAsync(string source, string continuation, int pageSize, CancellationToken token);
        Task<PlaybackSession> CreateSessionAsync(Track track, CancellationToken token);
        Task<DependencyInfo> CheckDependencyAsync(CancellationToken token);
    }
    public interface IPlaybackBackend : IDisposable
    {
        string Id { get; }
        PlaybackSnapshot Snapshot { get; }
        Task InitializeAsync(CancellationToken token);
        Task PrepareAsync(PlaybackSession session, CancellationToken token);
        Task PlayAsync(CancellationToken token);
        Task PauseAsync(CancellationToken token);
        Task ResumeAsync(CancellationToken token);
        Task StopAsync(CancellationToken token);
        Task SeekAsync(double seconds, CancellationToken token);
        Task SetVolumeAsync(double volume, CancellationToken token);
    }
    public sealed class SourceRegistry
    {
        private readonly Dictionary<string, IMediaSource> sources = new Dictionary<string, IMediaSource>(StringComparer.OrdinalIgnoreCase);
        public void Register(IMediaSource source) { sources[source.Id] = source; }
        public IMediaSource Get(string id)
        {
            IMediaSource result;
            if (!sources.TryGetValue(id ?? "", out result)) throw new MediaException(SourceErrorCode.Unsupported, LocalizedText.T("RimTube.TheAdapterForThisMusicSourceIsNot"));
            return result;
        }
        public IMediaSource Identify(string input)
        {
            foreach (var source in sources.Values) if (source.CanHandle(input)) return source;
            throw new MediaException(SourceErrorCode.Unsupported, LocalizedText.T("RimTube.CouldNotIdentifyThisSource"));
        }
    }
    public sealed class PlaybackEnvironment
    {
        public bool GameActive, Danger, GamePaused, VideoCovered;
        public double Time, MasterVolume = 1;
        public object GameIdentity;
    }
}
