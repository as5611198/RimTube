using RimTube.Sources;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimTube.Core;
using UnityEngine;
using Verse;

namespace RimTube
{
    public sealed class BootstrapSettings : ModSettings
    {
        public string DataDirectory = "";
        public override void ExposeData() { Scribe_Values.Look(ref DataDirectory, "dataDirectory", ""); }
    }

    public sealed class RimTubeMod : Mod
    {
        public static RimTubeMod Instance;
        public static RimTubeApp App;
        public readonly BootstrapSettings Config;
        public RimTubeMod(ModContentPack content) : base(content)
        {
            Instance = this; Config = GetSettings<BootstrapSettings>();
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    var obj = new GameObject("RimTube"); UnityEngine.Object.DontDestroyOnLoad(obj);
                    obj.AddComponent<RimTubeHost>();
                }
                catch (Exception ex) { Log.Error(LocalizedText.T("RimTube.RimTubeInitializationFailed") + ex); }
            });
        }
        public string DataPath
        {
            get
            {
                return PortableRuntime.DataDirectory(GenFilePaths.SaveDataFolderPath,Content.RootDir,Config.DataDirectory);
            }
        }
        public override string SettingsCategory() { return "RimTube"; }
        public override void DoSettingsWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 60), LocalizedText.T("RimTube.RimTubeMusicPlayerNOpenTheFullPlayerTo"));
            if (UiTheme.Button(new Rect(rect.x, rect.y + 75, 240, 36), LocalizedText.T("RimTube.OpenPlayer"))) OpenPlayer();
            if (App != null) Widgets.Label(new Rect(rect.x, rect.y + 130, rect.width, 90), LocalizedText.T("RimTube.MusicData") + App.Library.Root + "\n" + App.Player.Status);
        }
        public static void OpenPlayer()
        {
            if (App == null) return;
            if (!Find.WindowStack.IsOpen<PlayerWindow>()) Find.WindowStack.Add(new PlayerWindow());
        }
    }

    [DefaultExecutionOrder(-200)]
    public sealed class RimTubeHost : MonoBehaviour
    {
        private MiniWindow mini;
        private VideoWindow videoWindow;
        public void Start()
        {
            L10n.Refresh();
            try { RimTubeMod.App = new RimTubeApp(this, RimTubeMod.Instance.DataPath); }
            catch (Exception ex) { Log.Error(LocalizedText.T("RimTube.RimTubeLibraryInitializationFailed") + ex); }
        }
        public void Update()
        {
            L10n.Refresh();
            var app = RimTubeMod.App;
            if (app == null) return;
            try
            {
                app.Tick();
                var button=DefDatabase<RimWorld.MainButtonDef>.GetNamedSilentFail("RimTube");
                bool panel=Find.WindowStack?.IsOpen<PlayerWindow>()==true || (button!=null&&GameUi.MainTabs?.OpenTab==button);
                app.Interface.Tick(panel);
                bool show = app.Interface.Prefs.ShowMini && !app.Interface.Prefs.MiniLocked && !panel && !(app.Player.HasVideo&&app.Player.VideoVisible) && Current.Game != null && Current.ProgramState == ProgramState.Playing && !LongEventHandler.AnyEventNowOrWaiting;
                if (show && (mini == null || !mini.IsOpen)) { mini = new MiniWindow(); Find.WindowStack.Add(mini); }
                if (!show && mini != null && mini.IsOpen) mini.Close(false);
                bool showVideo = app.Player.HasVideo && app.Player.VideoVisible && Current.Game != null && Current.ProgramState == ProgramState.Playing && !LongEventHandler.AnyEventNowOrWaiting;
                if (showVideo && (videoWindow == null || !videoWindow.IsOpen)) { videoWindow = new VideoWindow(); Find.WindowStack.Add(videoWindow); }
                if (!showVideo && videoWindow != null && videoWindow.IsOpen) videoWindow.Close(false);
            }
            catch (Exception ex)
            {
                app.Player.Stop(); app.Error = ex.Message;
                Log.ErrorOnce(LocalizedText.T("RimTube.RimTubePlayerError") + ex, 18261031);
            }
        }
        public void OnGUI()
        {
            var app=RimTubeMod.App;
            if(app==null||Current.Game==null||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting) return;
            var matrix=GUI.matrix;var color=GUI.color;var font=Text.Font;var anchor=Text.Anchor;int depth=GUI.depth;
            try{UI.ApplyUIScale();GUI.depth=-50;UiHotkeys.Handle(app.Interface);MiniWindow.DrawLocked(app.Interface);}
            finally{GUI.matrix=matrix;GUI.color=color;Text.Font=font;Text.Anchor=anchor;GUI.depth=depth;}
        }
        public void OnApplicationQuit() { RimTubeMod.App?.Dispose(); }
        public void OnDestroy() { RimTubeMod.App?.Dispose(); RimTubeMod.App = null; }
    }

    public sealed class RimTubeApp : IDisposable
    {
        public Library Library { get; private set; }
        public JobQueue Jobs { get; private set; }
        public AudioPlayer Player { get; private set; }
        public GameMusicSource GameMusic {get;private set;}
        public UiSession Interface {get;private set;}
        public string Error = "", ToolsInfo = "";
        public string ResolverUpdateInfo {get;private set;}="";
        public bool UpdatingResolver {get;private set;}
        public bool Inspecting { get; private set; }
        private readonly MonoBehaviour host;
        private readonly ResolverToolStore resolverTools;
        private string pendingResolverPath;
        private readonly ConcurrentQueue<Action> callbacks = new ConcurrentQueue<Action>();
        private readonly Queue<Action> uiCommands=new Queue<Action>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private bool checkingTools;
        private CancellationTokenSource inspection;
        private bool disposed;
        private long playRevision, downloadRevision;
        private string autoplayJob, autoplayList;
        private object downloadGame;
        private Library downloadLibrary;
        private string pendingRemoval;
        private Library removalLibrary;
        public RimTubeApp(MonoBehaviour owner, string directory) { host = owner; resolverTools=new ResolverToolStore(GenFilePaths.SaveDataFolderPath);OpenData(directory); Interface=new UiSession(this); }
        private void OpenData(string directory)
        {
            AttachData(new Library(PortableRuntime.DataDirectory(GenFilePaths.SaveDataFolderPath,RimTubeMod.Instance.Content.RootDir,directory)));
        }
        private void AttachData(Library state)
        {
            Library = state; Library.AcquireWriteLock(); Error = Library.LoadWarning;
            Library.Options.ConfigureBundledTools(RimTubeMod.Instance.Content.RootDir);
            Library.Options.ConfigureResolverUpdate(resolverTools.ActivePath);
            GameMusic=new GameMusicSource(new GameSongInfo[0]);RefreshGameMusic(false);
            Jobs = new JobQueue(Library); Jobs.Completed += DownloadComplete; Player = new AudioPlayer(host, Library,GameMusic); Player.ManualAction += HandleManualAction;
        }
        public void SwitchData(string directory)
        {
            if (UpdatingResolver||pendingRemoval!=null||Inspecting || Jobs.Items.Any(j => j.State == JobState.Running || j.State == JobState.Queued)) throw new InvalidOperationException(LocalizedText.T("RimTube.FinishMediaDeletionAndCompleteOrCancelImport"));
            string full = PortableRuntime.DataDirectory(GenFilePaths.SaveDataFolderPath,RimTubeMod.Instance.Content.RootDir,directory);
            if (String.Equals(full, Library.Root, StringComparison.OrdinalIgnoreCase)) return;
            // Validate destination before releasing the current session.
            var next = new Library(full);
            if (!next.AcquireWriteLock()) { next.Dispose(); throw new InvalidOperationException(next.LoadWarning); }
            try { Save(); } catch { next.Dispose(); throw; }
            Player.Dispose(); Jobs.Dispose(); Library.Dispose();
            AttachData(next); RimTubeMod.Instance.Config.DataDirectory = full; RimTubeMod.Instance.WriteSettings();
            Current.Game?.GetComponent<RimTubeGameState>()?.Apply();
        }
        public void Tick()
        {
            for(int i=0;i<8&&uiCommands.Count>0;i++) if(!disposed) Safe(uiCommands.Dequeue());
            Action callback; for (int i = 0; i < 16 && callbacks.TryDequeue(out callback); i++) { if (!disposed) Safe(callback); }
            if(pendingResolverPath!=null&&!ResolverBusy())
            {
                Library.Options.ConfigureResolverUpdate(pendingResolverPath);pendingResolverPath=null;
                ApplyPlaybackSettings();ResolverUpdateInfo=LocalizedText.T("RimTube.UpdateApplied");
            }
            Jobs.Pump(); Player.Tick();
            if(pendingRemoval!=null)
            {
                if(!ReferenceEquals(removalLibrary,Library))pendingRemoval=null;
                else
                {
                    if(Player.CurrentId==pendingRemoval)Player.Stop();
                    if(Player.CanRemoveMedia(pendingRemoval))
                    {string id=pendingRemoval;pendingRemoval=null;Safe(()=>{EnsureNoMediaJobs(id);Library.RemoveMedia(id);Interface.Invalidate();Error="";});}
                }
            }
        }
        public void QueueUi(Action action)
        {
            if(disposed) return;
            if(uiCommands.Count>=64){Error=LocalizedText.T("RimTube.TheOperationQueueIsBusyTryAgainShortly");return;}
            var library=Library;
            uiCommands.Enqueue(()=>{if(ReferenceEquals(library,Library)) action();else Error=LocalizedText.T("RimTube.TheLibraryChangedTryTheOperationAgain");});
        }
        public void Safe(Action action)
        {
            try { action(); }
            catch (Exception ex) { Error = ex.Message; Log.Warning("[RimTube] " + ex.Message); }
        }
        public void Save() { Library.Save(); }
        public void ImportLocal(IEnumerable<string> paths, Playlist list)
        {
            list=Library.ImportPlaylist(list);
            foreach (string path in paths)
            {
                var file = new FileInfo(Path.GetFullPath(path));
                if (!file.Exists) throw new FileNotFoundException(LocalizedText.T("RimTube.LocalAudioNotFound") + file.Name);
                string id;
                using (var hash = SHA256.Create())
                    id = "local_" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(file.FullName.ToUpperInvariant() + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks))).Replace("-", "").ToLowerInvariant();
                var t = new Track { Id = id, Title = Path.GetFileNameWithoutExtension(file.Name), SourceType = "local", SourceId = id, Availability = SourceAvailability.Available };
                Library.AddTo(list, t); Jobs.Enqueue(t, file.FullName, true, false);
            }
            Save(); Jobs.Flush();
        }
        public void Inspect(string input, bool playlist, Playlist target = null, MediaChoice choice = null, bool autoplay = true)
        {
            if (Library.ReadOnly) throw new InvalidOperationException(Library.LoadWarning);
            string url = YoutubeClient.Normalize(input, playlist);
            if (playlist)
            {
                CancelInspect();
                var list = target ?? Library.Lists.FirstOrDefault(p => p.SourceUrl == url);
                var state = Library;
                var source = new YouTubeSourceAdapter(Library.Options);
                var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                inspection = request; Inspecting = true; Error = LocalizedText.T("RimTube.ReadingYouTubePlaylist");
                var token = request.Token;
                Task.Run(async () =>
                {
                    try
                    {
                        var preview = await PlaylistReader.ReadAsync(source, url, token).ConfigureAwait(false);
                        callbacks.Enqueue(() =>
                        {
                            if (token.IsCancellationRequested || !ReferenceEquals(inspection, request) || !ReferenceEquals(Library, state)) return;
                            if (list != null && !Library.Lists.Contains(list)) { Error = LocalizedText.T("RimTube.TheTargetPlaylistWasRemovedQueryItAgain"); return; }
                            if (!preview.Complete && preview.Tracks.Count == 0)
                            {
                                Error = preview.Message;
                                if (list != null) { list.SyncStatus = SyncState.Failed; list.SyncMessage = preview.Message; Save(); }
                                return;
                            }
                            Error = ""; Find.WindowStack.Add(new SyncWindow(preview, url, list));
                        });
                    }
                    catch (OperationCanceledException) { }
                    catch { callbacks.Enqueue(() => { if (ReferenceEquals(inspection, request) && !token.IsCancellationRequested) Error = LocalizedText.T("RimTube.YouTubePlaylistLookupFailedCheckTheToolsAnd"); }); }
                    finally { callbacks.Enqueue(() => { if (ReferenceEquals(inspection, request)) { Inspecting = false; inspection = null; } request.Dispose(); }); }
                });
                return;
            }
            else
            {
                CancelInspect();
                if (autoplay && Library.Options.Backend != "mpv" && (choice == null || choice.Delivery != MediaDelivery.Download)) throw new InvalidOperationException(LocalizedText.T("RimTube.SwitchToTheMpvBackendInSettingsFor"));
                var player = Player;
                var state = Library;
                var game = Current.Game;
                string listId = Library.ImportPlaylist(target).Id;
                var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                inspection = request; Inspecting = true; Error = LocalizedText.T("RimTube.GettingYouTubeTrackInformation");
                if(autoplay)player.BeginSourceLookup(request,game);
                var token = request.Token;
                Task.Run(async () =>
                {
                    try
                    {
                        var track = await player.InspectYouTubeAsync(url, choice ?? new MediaChoice(), token).ConfigureAwait(false);
                        callbacks.Enqueue(() =>
                        {
                            if (token.IsCancellationRequested || !ReferenceEquals(inspection, request) || !ReferenceEquals(Library, state)) return;
                            var destination = Library.FindList(listId);
                            if (destination == null) { Error = LocalizedText.T("RimTube.TheTargetPlaylistWasRemovedAddTheTrack"); return; }
                            Library.AddTo(destination, track);
                            var saved = Library.FindTrack(track.Id); saved.Duration = track.Duration;
                            saved.Availability = track.Availability; saved.SourceType = "youtube"; saved.SourceId = track.SourceId; saved.Error = "";
                            foreach (var pair in track.Metadata) saved.Metadata[pair.Key] = pair.Value;
                            Save(); Error = "";
                            if (MediaChoice.From(saved).Delivery == MediaDelivery.Download) DownloadTrack(saved, MediaChoice.From(saved), autoplay && ReferenceEquals(Current.Game, game) && game != null && !LongEventHandler.AnyEventNowOrWaiting, listId);
                            else if (autoplay && ReferenceEquals(Current.Game, game) && game != null && Current.ProgramState == ProgramState.Playing && !LongEventHandler.AnyEventNowOrWaiting)
                            { PrepareVideoView(saved); Player.ShowVideo(true); Player.Play(track.Id, listId); }
                        });
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        string message = ex is MediaException ? ex.Message : LocalizedText.T("RimTube.YouTubeLookupFailedCheckThePlaybackToolsAnd");
                        callbacks.Enqueue(() => { if (ReferenceEquals(inspection, request) && !token.IsCancellationRequested) Error = message; });
                    }
                    finally
                    {
                        callbacks.Enqueue(() => { player.EndSourceLookup(request); if (ReferenceEquals(inspection, request)) { Inspecting = false; inspection = null; } request.Dispose(); });
                    }
                });
                return;
            }
        }
        private void HandleManualAction() { CancelInspect(); ++playRevision; autoplayJob = null; }
        public void DownloadTrack(Track track, MediaChoice choice, bool play = false, string list = null)
        {
            if (play) { Player.Stop(); choice = choice.Copy(); choice.Delivery = MediaDelivery.Download; choice.Store(track); Save(); }
            var job = Jobs.EnqueueDownload(track, choice);
            if (play && job.State==JobState.Complete)
            { Error="";PrepareVideoView(track);Player.ShowVideo(true);Player.Play(track.Id,list);return; }
            if (play) { autoplayJob = job.Id; autoplayList = list; downloadGame = Current.Game; downloadLibrary = Library; downloadRevision = playRevision; }
            Error = LocalizedText.T("RimTube.DownloadQueuedViewProgressOrCancelItIn");
        }
        private void DownloadComplete(AudioJob job)
        {
            if (autoplayJob != job.Id || playRevision != downloadRevision || !ReferenceEquals(Library, downloadLibrary)) return;
            autoplayJob = null; Error = "";
            if (downloadGame != null && ReferenceEquals(Current.Game, downloadGame) && Current.ProgramState == ProgramState.Playing && !LongEventHandler.AnyEventNowOrWaiting)
            { PrepareVideoView(Library.FindTrack(job.TrackId)); Player.ShowVideo(true); Player.Play(job.TrackId, autoplayList); }
        }
        public void PlayTrack(Track track, string list = null)
        {
            if(track.Id==pendingRemoval)throw new InvalidOperationException(LocalizedText.T("RimTube.ThisMediaIsBeingDeletedPleaseWait"));
            var choice = MediaChoice.From(track);
            if (track.IsYouTube && choice.Delivery == MediaDelivery.Download && !SavedMedia.Ready(Library, track, choice.Video))
                DownloadTrack(track, choice, true, list);
            else { PrepareVideoView(track); Player.ShowVideo(true); Player.Play(track.Id, list); }
        }
        public void ChangeVideoQuality(string expectedTrackId,int height)
        {
            if(Library.ReadOnly) throw new InvalidOperationException(Library.LoadWarning);
            if(!Player.ChangeVideoQuality(expectedTrackId,height)) throw new InvalidOperationException(LocalizedText.T("RimTube.TheCurrentVideoChangedOrStoppedOpenThe"));
            Save();
        }
        public void ChangeVideoSubtitles(string expectedTrackId,string language,bool automatic)
        {
            if(Library.ReadOnly)throw new InvalidOperationException(Library.LoadWarning);
            if(!Player.ChangeVideoSubtitles(expectedTrackId,language,automatic))throw new InvalidOperationException(LocalizedText.T("RimTube.TheCurrentVideoChangedOrStoppedOpenCaption"));
            Save();
        }
        private void PrepareVideoView(Track track)
        {
            if (track == null || !MediaChoice.From(track).Video) return;
            Find.WindowStack.WindowOfType<PlayerWindow>()?.Close(false);
            GameUi.ClosePlayerTab();
        }
        public void DownloadPlaylist(Playlist playlist, MediaChoice choice)
        {
            var result=Jobs.EnqueuePlaylist(playlist,choice);
            Error=LocalizedText.F("RimTube.BatchDownloadResult",result.Added,result.Reused,result.Pending);
        }
        public void RefreshGameMusic(bool save=true)
        {
            GameMusic.Refresh(GameMusicCatalog.Read());
            GameMusicPlaylists.Sync(Library,GameMusic.Tracks);
            if(save&&!Library.ReadOnly) Save();
        }
        public void AddGameMusic(Track track,Playlist list=null,bool play=false)
        {
            if(Library.ReadOnly) throw new InvalidOperationException(Library.LoadWarning);
            if(list!=null&&!Library.Lists.Contains(list)) throw new InvalidOperationException(LocalizedText.T("RimTube.ThePlaylistWasRemoved"));
            if(track.SourceType!="game") throw new InvalidOperationException(LocalizedText.T("RimTube.ThisIsNotGameMusic"));
            if(track.Availability!=SourceAvailability.Available) throw new InvalidOperationException(LocalizedText.T("RimTube.AudioForThisSourceHasNotLoaded"));
            if(list!=null) Library.AddTo(list,track);
            else if(!Library.Tracks.ContainsKey(track.Id)) Library.Tracks.Add(track.Id,track);
            Save();if(play) PlayTrack(Library.FindTrack(track.Id),list?.Id);
        }
        public void DeleteVideo(Track track)
        {
            if (Jobs.Items.Any(j=>j.TrackId==track.Id && (j.State==JobState.Queued || j.State==JobState.Running))) throw new InvalidOperationException(LocalizedText.T("RimTube.CompleteOrCancelThisVideoSDownloadFirst"));
            if (Player.CurrentId == track.Id) Player.Stop();
            Library.DeleteVideo(track);
            Interface?.Invalidate();
        }
        private void EnsureNoMediaJobs(string id)
        {
            if(Jobs.Items.Any(j=>j.TrackId==id&&(j.State==JobState.Queued||j.State==JobState.Running)))
                throw new InvalidOperationException(LocalizedText.T("RimTube.CompleteOrCancelThisTrackSDownloadImport"));
        }
        public void RemoveMedia(string id)
        {
            if(Library.ReadOnly)throw new InvalidOperationException(Library.LoadWarning);
            var track=Library.FindTrack(id);if(track==null)return;
            if(track.SourceType=="game")throw new InvalidOperationException(LocalizedText.T("RimTube.GameMusicAssetsCannotBeDeletedFromThe"));
            EnsureNoMediaJobs(id);
            if(pendingRemoval!=null)throw new InvalidOperationException(LocalizedText.T("RimTube.WaitForTheCurrentMediaDeletionToFinish"));
            if(Player.CanRemoveMedia(id)){Library.RemoveMedia(id);Interface.Invalidate();return;}
            if(Player.CurrentId==id)Player.Stop();
            pendingRemoval=id;removalLibrary=Library;Error=L10n.T("RimTube.RemovingMedia");
        }
        public void HideAll()
        {
            Library.Options.ShowMini = false;
            if(Interface!=null){Interface.Prefs.ShowMini=false;Interface.Changed();}
            Player.ShowVideo(false);
            var full = Find.WindowStack.WindowOfType<PlayerWindow>(); full?.Close(false);
            GameUi.ClosePlayerTab();
            Save();
        }
        public void CancelInspect() { var pending = inspection; inspection = null; Inspecting = false; if (pending != null) { Player.EndSourceLookup(pending); pending.Cancel(); Error = ""; } }
        public static Track FromYoutube(YoutubeInfo info)
        {
            if (info == null || !System.Text.RegularExpressions.Regex.IsMatch(info.Id ?? "", "^[A-Za-z0-9_-]{11}$")) throw new InvalidDataException(LocalizedText.T("RimTube.TheSourceTrackHasNoValidVideoID"));
            return new Track { Id = "yt_" + info.Id, Title = info.Title ?? info.Id, Url = "https://www.youtube.com/watch?v=" + info.Id, Duration = info.Duration ?? 0, SourceType = "youtube", SourceId = info.Id, Availability = SourceAvailability.AuthorizationRequired };
        }
        public void DeleteAudio(Track track)
        {
            if (Player.CurrentId == track.Id) Player.Stop();
            if (Jobs.Items.Any(j => j.TrackId == track.Id && (j.State == JobState.Running || j.State == JobState.Queued))) throw new InvalidOperationException(LocalizedText.T("RimTube.CancelThisTrackSDownloadFirst"));
            Library.DeleteAudio(track);
        }
        public void ApplyPlaybackSettings()
        {
            HandleManualAction(); Player.Dispose(); Player = new AudioPlayer(host, Library,GameMusic); Player.ManualAction += HandleManualAction; Save();
        }
        private bool ResolverBusy()=>Inspecting||checkingTools||Player.IsLoading||Player.Intent!=PlayIntent.Stopped||Jobs.Items.Any(j=>j.State==JobState.Queued||j.State==JobState.Running);
        public void UpdateResolver(bool revert=false)
        {
            if(UpdatingResolver)return;
            if(ResolverBusy()){ResolverUpdateInfo=LocalizedText.T("RimTube.UpdateStopFirst");return;}
            UpdatingResolver=true;ResolverUpdateInfo=LocalizedText.T(revert?"RimTube.UpdateReverting":"RimTube.UpdateDownloading");
            Task.Run(async()=>
            {
                try
                {
                    if(revert)resolverTools.Revert();
                    else await ResolverToolUpdater.UpdateAsync(resolverTools,lifetime.Token).ConfigureAwait(false);
                    string selected=resolverTools.ActivePath;
                    callbacks.Enqueue(()=>{pendingResolverPath=selected;ResolverUpdateInfo=LocalizedText.T("RimTube.UpdateReady");});
                }
                catch(OperationCanceledException){callbacks.Enqueue(()=>ResolverUpdateInfo=LocalizedText.T("RimTube.UpdateCancelled"));}
                catch(Exception ex){string message=YoutubeClient.CleanError(ex.Message);callbacks.Enqueue(()=>ResolverUpdateInfo=LocalizedText.F("RimTube.UpdateFailed",message));}
                finally{callbacks.Enqueue(()=>UpdatingResolver=false);}
            });
        }
        public void CheckTools()
        {
            if (checkingTools || disposed) return;
            checkingTools = true;
            string mpv = Library.Options.MpvPath, yt = Library.Options.YtDlp,ffmpeg=Library.Options.Ffmpeg,deno=Library.Options.JsRuntime;
            var token = lifetime.Token;
            Task.Run(async () =>
            {
                try
                {
                    var m = await ToolDependency.CheckAsync(mpv, "mpv", token).ConfigureAwait(false);
                    var y = await new YouTubeSourceAdapter(yt).CheckDependencyAsync(token).ConfigureAwait(false);
                    var f=await ToolDependency.CheckAsync(ffmpeg,"ffmpeg",token).ConfigureAwait(false);
                    var d=await ToolDependency.CheckAsync(deno,JavaScriptRuntime.DisplayName(deno),token).ConfigureAwait(false);
                    var probe=await ToolDependency.CheckAsync(String.IsNullOrWhiteSpace(ffmpeg)?"":Path.Combine(Path.GetDirectoryName(ffmpeg),"ffprobe.exe"),"ffprobe",token).ConfigureAwait(false);
                    string text=String.Join("\n",new[]{m.Message+" "+m.Version,y.Message+" "+y.Version,f.Message+" "+f.Version,d.Message+" "+d.Version,probe.Message+" "+probe.Version});
                    callbacks.Enqueue(() => ToolsInfo = text);
                }
                catch (OperationCanceledException) { }
                finally { callbacks.Enqueue(() => checkingTools = false); }
            });
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; lifetime.Cancel(); CancelInspect(); Player.Dispose(); Jobs.Dispose();
            Interface?.Dispose();
            try { if (!Library.ReadOnly) Save(); } catch (Exception ex) { Log.Warning(LocalizedText.T("RimTube.RimTubeSaveFailed") + ex.Message); }
            Library.Dispose();
        }
    }
}
