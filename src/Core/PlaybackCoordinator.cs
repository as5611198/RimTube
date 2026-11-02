using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RimTube.Core
{
    // One in-flight operation; later intent cancels it instead of growing a command queue.
    public sealed class PlaybackCoordinator : IDisposable
    {
        private readonly Library library;
        private readonly SourceRegistry sources;
        private readonly IPlaybackBackend backend;
        private readonly Action<bool> bgm;
        private readonly Random random = new Random();
        private sealed class Bookmark { public string Id, ListId; public double Position; public List<string> Queue; }
        private readonly Bookmark general = new Bookmark(), combat = new Bookmark();
        private readonly HashSet<string> failed = new HashSet<string>();
        private enum Operation { Prepare, Play, Pause, Stop, Seek, Volume }
        private sealed class Outcome { public PlaybackSession Session; public Exception Error; public int Epoch; public string Track; public PlayContext Slot; public Operation Kind; public double Value; }
        private Task<Outcome> operation;
        private CancellationTokenSource cancellation;
        private int epoch;
        private bool needsStop, disposed, danger;
        private double lastDanger = Double.NegativeInfinity, lastVolume = -1;
        private object gameIdentity;
        private object sourceLookup, sourceLookupGame;
        private PlayContext slot = PlayContext.General, preparedSlot;
        private PlaybackSession session;
        private BackendState lastPublished = BackendState.Idle;
        public PlayIntent Intent { get; private set; }
        public string CurrentId { get; private set; }
        public string Error { get; private set; } = "";
        public string Status { get; private set; } = LocalizedText.T("RimTube.Stopped");
        public bool InCombat { get { return danger; } }
        public bool IsLoading { get { return operation != null && !operation.IsCompleted && operationKind == Operation.Prepare; } }
        public PlaybackSnapshot Snapshot { get { return backend.Snapshot; } }
        public double Position { get { return session != null && Snapshot.SessionId == session.SessionId ? Snapshot.Position : 0; } }
        public double Duration { get { return session != null && Snapshot.SessionId == session.SessionId ? Snapshot.Duration : 0; } }
        public bool CanSeek => session != null && session.CanSeek;
        public bool IsLive => session != null && session.IsLive;
        public bool CanRemoveMedia(string id)=>!disposed&&operation==null&&!needsStop&&CurrentId!=id&&session?.TrackId!=id;
        public event Action<PlaybackSnapshot> StateChanged;
        private Operation operationKind;
        private Bookmark Mark { get { return slot == PlayContext.Combat ? combat : general; } }

        public PlaybackCoordinator(Library library, SourceRegistry sources, IPlaybackBackend backend, Action<bool> bgmOwnership)
        { this.library = library; this.sources = sources; this.backend = backend; bgm = bgmOwnership ?? (_ => { }); }

        public void Play(string id = null, string listId = null)
        {
            if (disposed) return;
            if (id != null || listId != null)
            {
                Mark.ListId = listId;
                // Library playback has its own ordered queue; roles change only in Settings.
                var selected=library.FindTrack(id);
                bool video=selected!=null&&MediaChoice.From(selected).Video;
                Mark.Queue = listId == null ? library.Tracks.Values.Where(t=>MediaChoice.From(t).Video==video).OrderBy(t => t.Title).Select(t => t.Id).ToList() : null;
            }
            failed.Clear(); Error = "";
            if (id != null) { Capture(); Invalidate(true); Mark.Id = id; Mark.Position = 0; CurrentId = id; }
            Intent = PlayIntent.Playing;
        }
        public void Pause()
        {
            if (Intent == PlayIntent.Stopped || disposed) return;
            Intent = PlayIntent.Paused;
            if (IsLoading || (operation != null && operationKind == Operation.Play)) { Capture(); Invalidate(true); }
        }
        public void Stop()
        {
            if (disposed) return;
            sourceLookup=sourceLookupGame=null;
            Intent = PlayIntent.Stopped; Invalidate(true); general.Id = combat.Id = null;
            general.Position = combat.Position = 0; CurrentId = null; failed.Clear(); Status = LocalizedText.T("RimTube.Stopping");
            general.ListId = combat.ListId = null; general.Queue = combat.Queue = null;
        }
        public void ClearError() { Error = ""; }
        public void BeginSourceLookup(object request,object expectedGame)
        {
            if(disposed||request==null||expectedGame==null)return;
            sourceLookup=request;sourceLookupGame=expectedGame;
        }
        public void EndSourceLookup(object request)
        {
            if(!ReferenceEquals(sourceLookup,request))return;
            sourceLookup=sourceLookupGame=null;
        }
        public bool ChangeVideoQuality(string expectedTrackId,int height)
        {
            if(disposed||Intent==PlayIntent.Stopped||(CurrentId??Mark.Id)!=expectedTrackId) return false;
            var track=library.FindTrack(expectedTrackId);
            if(track==null||!track.IsYouTube) return false;
            var choice=MediaChoice.From(track);
            if(!choice.Video) return false;
            choice.Height=height;choice.Delivery=MediaDelivery.Stream;choice.Validate();
            Capture();choice.Store(track);failed.Remove(track.Id);Error="";Invalidate(true);
            return true;
        }
        public bool ChangeVideoSubtitles(string expectedTrackId,string language,bool automatic)
        {
            if(disposed||Intent==PlayIntent.Stopped||(CurrentId??Mark.Id)!=expectedTrackId)return false;
            var track=library.FindTrack(expectedTrackId);if(track==null||!track.IsYouTube)return false;
            var choice=MediaChoice.From(track);if(!choice.Video)return false;
            choice.Subtitle=language;choice.AutoSubtitles=automatic;choice.Delivery=MediaDelivery.Stream;choice.Validate();
            Capture();choice.Store(track);failed.Remove(track.Id);Error="";Invalidate(true);return true;
        }
        public void Next(bool previous = false) { if (!disposed) { Capture(); SelectNext(previous, true); } }
        public void Seek(double seconds)
        {
            if (disposed || Double.IsNaN(seconds) || Double.IsInfinity(seconds)) return;
            double position = Math.Max(0, Duration > 0 ? Math.Min(seconds, Math.Max(0, Duration - .05)) : seconds);
            Mark.Position = position;
            if (session == null || IsLoading) { Invalidate(true); return; }
            if (!session.CanSeek) { Error = LocalizedText.T("RimTube.ThisSourceDoesNotSupportSeeking"); return; }
            if (operation != null) { Error = LocalizedText.T("RimTube.ThePlayerIsBusyTrySeekingAgainShortly"); return; }
            Begin(Operation.Seek, token => backend.SeekAsync(position, token), position);
        }
        private void Invalidate(bool stop)
        {
            ++epoch; cancellation?.Cancel(); needsStop |= stop;
        }
        private void Capture()
        {
            if (session == null) return;
            var mark = preparedSlot == PlayContext.Combat ? combat : general;
            mark.Id = session.TrackId;
            if (Snapshot.SessionId == session.SessionId && Snapshot.Position >= 0) mark.Position = Snapshot.Position;
        }
        private bool Candidate(string id)
        {
            var track = library.FindTrack(id);
            if (track == null || failed.Contains(id)) return false;
            if(track.SourceType=="game"&&track.Availability!=SourceAvailability.Available) return false;
            if (track.IsYouTube && MediaChoice.From(track).Delivery != MediaDelivery.Stream && !MediaChoice.From(track).Video && library.IsReady(id)) return true;
            string type = String.IsNullOrEmpty(track.SourceType) ? track.IsYouTube ? "youtube" : "local" : track.SourceType;
            if (type == "local") return library.IsReady(id);
            try { return (sources.Get(type).Capabilities & SourceCapabilities.Sessions) != 0; }
            catch (MediaException) { return false; }
        }
        private List<string> Candidates()
        {
            var list = library.FindList(Mark.ListId ?? (slot == PlayContext.Combat ? library.Options.CombatList : library.Options.GeneralList));
            var ids = Mark.Queue ?? list?.Tracks;
            return ids == null ? new List<string>() : ids.Where(Candidate).Distinct().ToList();
        }
        public void Tick(PlaybackEnvironment environment)
        {
            try { TickCore(environment); }
            finally
            {
                var state = Snapshot;
                if (state.State != lastPublished) { lastPublished = state.State; StateChanged?.Invoke(state); }
            }
        }
        private void TickCore(PlaybackEnvironment environment)
        {
            if (disposed) return;
            Poll();
            if (gameIdentity != null && !ReferenceEquals(gameIdentity, environment.GameIdentity)) Stop();
            gameIdentity = environment.GameIdentity;
            if(!environment.GameActive||!ReferenceEquals(sourceLookupGame,environment.GameIdentity))sourceLookup=sourceLookupGame=null;
            if (!environment.GameActive && Intent != PlayIntent.Stopped) Stop();
            if (environment.Danger) lastDanger = environment.Time;
            danger = environment.GameActive && (environment.Danger || (danger && environment.Time - lastDanger < 3));
            var battleList = library.FindList(library.Options.CombatList);
            bool combatAvailable = battleList != null && battleList.Tracks.Any(Candidate);
            var desired = PlaybackPolicy.Decide(Intent, danger, combatAvailable, library.Options.Battle, environment.GamePaused, library.Options.PauseWithGame);
            bool videoCovered = library.Options.PauseCoveredVideo && environment.VideoCovered && session != null && session.HasVideo;
            if (videoCovered && desired != PlayContext.Stopped) desired = PlayContext.Paused;
            if ((desired == PlayContext.Paused || desired == PlayContext.Vanilla) && IsLoading) { Capture(); Invalidate(true); }
            UpdateOwnership(desired);
            if (operation != null) return;
            if (needsStop)
            {
                Begin(Operation.Stop, token => backend.StopAsync(token)); return;
            }
            if (desired == PlayContext.Stopped) { Status = LocalizedText.T("RimTube.Stopped"); bgm(sourceLookup!=null); return; }
            if (desired == PlayContext.Paused || desired == PlayContext.Vanilla)
            {
                Status = desired == PlayContext.Vanilla ? LocalizedText.T("RimTube.CombatVanillaMusic") : videoCovered && Intent == PlayIntent.Playing ? LocalizedText.T("RimTube.VideoCoveredAutomaticallyPaused") : LocalizedText.T("RimTube.Paused");
                if (Snapshot.State == BackendState.Playing || Snapshot.State == BackendState.Buffering) Begin(Operation.Pause, token => backend.PauseAsync(token));
                return;
            }
            if (slot != desired) { Capture(); Invalidate(true); slot = desired; return; }
            if (session != null && session.ExpiresUtc.HasValue && session.ExpiresUtc.Value <= DateTime.UtcNow)
            { Capture(); Invalidate(true); Error = LocalizedText.T("RimTube.TheSourceExpiredPreparingItAgain"); return; }
            if (session == null)
            {
                if (Mark.Id == null || failed.Contains(Mark.Id) || library.FindTrack(Mark.Id) == null)
                { Mark.Id = Candidates().FirstOrDefault(); Mark.Position = 0; }
                if (Mark.Id == null) { Intent = PlayIntent.Stopped; Status = LocalizedText.T("RimTube.NoPlayableSourcesInThePlaylist"); bgm(sourceLookup!=null); return; }
                StartPrepare(Mark.Id, Mark.Position, desired); return;
            }
            if (Snapshot.State == BackendState.Failed)
            {
                Error = String.IsNullOrEmpty(Snapshot.Error) ? LocalizedText.T("RimTube.ThePlaybackBackendDisconnectedCheckTheToolsAnd") : Snapshot.Error;
                if (Snapshot.Failure == SourceErrorCode.DecodeFailed || Snapshot.Failure == SourceErrorCode.Expired || Snapshot.Failure == SourceErrorCode.NotFound)
                { failed.Add(session.TrackId); Invalidate(true); }
                else Stop();
                return;
            }
            if (Snapshot.State == BackendState.Ended) { SelectNext(false, false); return; }
            double volume = Math.Max(0, Math.Min(1, library.Options.Volume * environment.MasterVolume));
            if (Math.Abs(volume - lastVolume) > .002)
            { Begin(Operation.Volume, token => backend.SetVolumeAsync(volume, token), volume); return; }
            if (Snapshot.State == BackendState.Ready || Snapshot.State == BackendState.Paused)
            {
                bgm(true); // Silence vanilla before the backend is allowed to emit audio.
                Begin(Operation.Play, token => backend.ResumeAsync(token)); return;
            }
            Status = Snapshot.State == BackendState.Buffering ? LocalizedText.T("RimTube.Buffering") : slot == PlayContext.Combat ? LocalizedText.T("RimTube.PlayingCombatPlaylist") : LocalizedText.T("RimTube.Playing");
        }
        private void UpdateOwnership(PlayContext desired)
        {
            var state = Snapshot.State;
            // Ownership follows playback intent across lookups, internal stops and pauses.
            // During an explicit stop/vanilla handoff, keep it until backend audio is silent.
            bool managed=desired!=PlayContext.Stopped&&desired!=PlayContext.Vanilla;
            bgm(managed||sourceLookup!=null||state == BackendState.Playing || state == BackendState.Buffering || (operation != null && operationKind == Operation.Play));
        }
        private void StartPrepare(string id, double position, PlayContext targetSlot)
        {
            var original = library.FindTrack(id);
            var track = new Track { Id = original.Id, SourceId = original.SourceId, SourceType = original.SourceType, Title = original.Title, Url = original.Url, FileName = original.FileName, Duration = original.Duration };
            foreach (var pair in original.Metadata) track.Metadata[pair.Key] = pair.Value;
            CurrentId = id; Status = LocalizedText.T("RimTube.PreparingPlaybackSource");
            int requestEpoch = epoch;
            cancellation = new CancellationTokenSource(); var token = cancellation.Token;
            operationKind = Operation.Prepare;
            operation = Task.Run(async () =>
            {
                var result = new Outcome { Epoch = requestEpoch, Track = id, Slot = targetSlot, Kind = Operation.Prepare };
                try
                {
                    var source = sources.Get(String.IsNullOrEmpty(track.SourceType) ? track.IsYouTube ? "youtube" : "local" : track.SourceType);
                    var choice = MediaChoice.From(track);
                    var next = track.IsYouTube && choice.Delivery != MediaDelivery.Stream && !choice.Video && library.IsReady(track.Id)
                        ? SavedMedia.Session(library, track, choice) : await source.CreateSessionAsync(track, token).ConfigureAwait(false);
                    next.Validate(); token.ThrowIfCancellationRequested();
                    await backend.InitializeAsync(token).ConfigureAwait(false);
                    await backend.PrepareAsync(next, token).ConfigureAwait(false);
                    if (position > 0 && next.CanSeek) await backend.SeekAsync(position, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested(); result.Session = next;
                }
                catch (Exception ex) { result.Error = ex; }
                return result;
            });
        }
        private void Begin(Operation kind, Func<CancellationToken, Task> action, double value = 0)
        {
            int requestEpoch = epoch; cancellation = new CancellationTokenSource(); var token = cancellation.Token; operationKind = kind;
            operation = Task.Run(async () =>
            {
                var result = new Outcome { Epoch = requestEpoch, Kind = kind, Value = value };
                try { token.ThrowIfCancellationRequested(); await action(token).ConfigureAwait(false); token.ThrowIfCancellationRequested(); }
                catch (Exception ex) { result.Error = ex; }
                return result;
            });
        }
        private void Poll()
        {
            if (operation == null || !operation.IsCompleted) return;
            var result = operation.GetAwaiter().GetResult(); operation = null; cancellation.Dispose(); cancellation = null;
            if (result.Epoch != epoch) return;
            if (result.Error != null)
            {
                if (result.Error is OperationCanceledException) return;
                Error = result.Error is MediaException ? result.Error.Message : LocalizedText.T("RimTube.PlaybackFailedCheckTheSourceAndPlaybackTools");
                var failure = result.Error as MediaException;
                if (result.Kind == Operation.Prepare && failure != null && failure.Code != SourceErrorCode.MissingTool && failure.Code != SourceErrorCode.BackendUnavailable && failure.Code != SourceErrorCode.Disconnected && failure.Code != SourceErrorCode.Timeout)
                {
                    failed.Add(result.Track); var t = library.FindTrack(result.Track); if (t != null) t.Error = Error;
                    session = null; needsStop = true;
                }
                else { Intent = PlayIntent.Stopped; needsStop = result.Kind != Operation.Stop; }
                if (result.Kind == Operation.Stop) { session = null; CurrentId = null; bgm(false); }
                return;
            }
            if (result.Kind == Operation.Stop) { needsStop = false; session = null; CurrentId = null; }
            else if (result.Kind == Operation.Prepare)
            {
                session = result.Session; preparedSlot = result.Slot; CurrentId = result.Track; lastVolume = -1;
                var t = library.FindTrack(result.Track); if (t != null) { t.Error = ""; if (Snapshot.Duration > 0) t.Duration = Snapshot.Duration; }
            }
            else if (result.Kind == Operation.Volume) lastVolume = result.Value;
        }
        private void SelectNext(bool previous, bool manual)
        {
            var ids = Candidates(); if (ids.Count == 0) { Stop(); return; }
            int index = ids.IndexOf(CurrentId ?? Mark.Id); string next;
            if (!manual && library.Options.Mode == PlayMode.RepeatOne) next = CurrentId;
            else if (library.Options.Mode == PlayMode.Shuffle)
            { var choices = ids.Where(id => ids.Count == 1 || id != CurrentId).ToList(); next = choices[random.Next(choices.Count)]; }
            else
            {
                int target = previous ? (index < 0 ? ids.Count - 1 : index - 1) : index + 1;
                if (target >= ids.Count && !manual && library.Options.Mode == PlayMode.Ordered && slot == PlayContext.General) { Stop(); return; }
                next = ids[(target % ids.Count + ids.Count) % ids.Count];
            }
            Invalidate(true); Mark.Id = next; Mark.Position = 0; CurrentId = next; Intent = PlayIntent.Playing;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; ++epoch; cancellation?.Cancel();
            backend.Dispose(); bgm(false); Intent = PlayIntent.Stopped;
            var remaining = cancellation;
            if (operation != null) operation.ContinueWith(_ => remaining?.Dispose(), TaskScheduler.Default);
            else remaining?.Dispose();
        }
    }
}
