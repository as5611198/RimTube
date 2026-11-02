using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RimTube.Core;
using UnityEngine;
using Verse;

namespace RimTube
{
    public sealed class UiPlaylist
    {
        public readonly string Id,Name,Label,Info,Tooltip;
        public readonly bool Automatic;
        public readonly string[] Tracks;
        public UiPlaylist(Playlist list,Settings options)
        {
            Id=list.Id;Name=LocalizedText.PlaylistName(list);Tracks=list.Tracks.ToArray();Automatic=list.IsGameCatalog;
            Label=Name+(Automatic?LocalizedText.T("RimTube.Auto"):"")+(Id==options.GeneralList?LocalizedText.T("RimTube.Everyday"):"")+(Id==options.CombatList?LocalizedText.T("RimTube.Combat"):"");
            Info=LocalizedText.F("RimTube.TrackCount",Tracks.Length)+" · "+(Automatic?list.SourceId+" · "+LocalizedText.Message(list.SyncMessage):String.IsNullOrEmpty(list.SourceUrl)?LocalizedText.T("RimTube.LocalPlaylist"):LocalizedText.SyncName(list.SyncStatus)+" · "+LocalizedText.Message(list.SyncMessage));
            Tooltip=Label+"\n"+Info;
        }
    }
    public sealed class UiJobRow
    {
        public readonly AudioJob Job;
        public readonly string Title,Progress;
        public readonly JobState State;
        private readonly string originalProgress;
        public bool Matches(AudioJob j)=>Job==j&&State==j.State&&originalProgress==j.Progress;
        public UiJobRow(AudioJob j){Job=j;State=j.State;Title=j.Title+" · "+Name(State);originalProgress=j.Progress;Progress=LocalizedText.Message(j.Progress);}
        private static string Name(JobState s)
        {switch(s){case JobState.Running:return L10n.T("RimTube.JobsActive");case JobState.Complete:return L10n.T("RimTube.Done");case JobState.Failed:return L10n.T("RimTube.Failed");case JobState.Canceled:return L10n.T("RimTube.Canceled");case JobState.Interrupted:return L10n.T("RimTube.Interrupted");default:return L10n.T("RimTube.Waiting");}}
    }
    // Main-thread update owner. OnGUI consumers only read snapshots or send explicit user actions.
    public sealed class UiSession : IDisposable
    {
        private readonly RimTubeApp app;
        private readonly string prefsPath;
        public readonly UiPreferences Prefs;
        public readonly UiQuery Query=new UiQuery();
        public UiCatalog Catalog {get;private set;}=UiCatalog.Empty;
        public IReadOnlyList<UiRow> Rows {get;private set;}=new UiRow[0];
        public UiPlaylist[] Playlists {get;private set;}=new UiPlaylist[0];
        public UiJobRow[] Jobs {get;private set;}=new UiJobRow[0];
        public string Section {get;private set;}
        public string SelectedList {get;private set;}
        public string Title {get;private set;}="RimTube";
        public string Transport {get;private set;}=LocalizedText.T("RimTube.Stopped");
        public string Footer {get;private set;}="";
        public string Notice {get;private set;}="";
        public string MiniTooltip {get;private set;}="";
        public string StorageLabel {get;private set;}=LocalizedText.T("RimTube.StorageNotCalculatedYet");
        public string CountLabel {get;private set;}=LocalizedText.T("RimTube.PreparingMusicLibrary");
        public string PlayLabel {get;private set;}=L10n.T("RimTube.Play");
        public string QualityLabel {get;private set;}=L10n.T("RimTube.Quality");
        public bool CanChangeQuality {get;private set;}
        public bool Building=>build!=null;
        public float CombatPulseUntil {get;private set;}
        public bool PanelOpen {get;private set;}
        public int GeometryRevision {get;private set;}
        private Task<UiCatalog> build;
        private Task<long> stats;
        private Library statsLibrary;
        private long statsRevision=-1,capturedStatsRevision;
        private static readonly string[] NoTracks=new string[0];
        private Library capturedLibrary,observedLibrary;
        private object observedLanguage;
        private long builtRevision=-1,capturedRevision,generation,capturedGeneration;
        private bool force=true,dirty,wasCombat,statsWanted;
        private float saveAt,nextLabels;
        private int shownCount=-1;
        public UiSession(RimTubeApp app)
        {
            this.app=app;prefsPath=PortableRuntime.InterfacePath(GenFilePaths.SaveDataFolderPath);Prefs=UiPreferences.Load(prefsPath,app.Library.Options);Section=Prefs.LastSection;SelectedList=Prefs.LastList;
            if(Section!="library"&&Section!="videos"&&Section!="list"&&Section!="game"&&Section!="jobs"&&Section!="settings") Section="library";
        }
        public void Changed(){dirty=true;saveAt=Time.realtimeSinceStartup+.8f;}
        public void Invalidate(){++generation;force=true;}
        public void Navigate(string section,string list=null)
        {
            if(Section!=section&&(section=="library"||section=="videos"))Query.Media=UiMedia.Any;
            Section=section;if(list!=null) SelectedList=list;Prefs.LastSection=Section;Prefs.LastList=SelectedList;Changed();
            if(section=="settings") statsWanted=true;
        }
        public void Refresh()
        {
            if(Section=="game"||Section=="list"&&app.Library.FindList(SelectedList)?.IsGameCatalog==true) app.RefreshGameMusic();
            Invalidate();statsWanted=Section=="settings";
        }
        public void ToggleMute(){Prefs.Muted=!Prefs.Muted;Changed();}
        public void TogglePlay(){if(app.Player.Intent==PlayIntent.Playing) app.Player.Pause();else app.Player.Play();}
        public void SetVolume(float value){Prefs.Volume=Mathf.Clamp01(value);if(value>0) Prefs.Muted=false;Changed();}
        public void SetSubtitleScale(float value){Prefs.SubtitleScale=Mathf.Clamp(value,1,3);Changed();}
        public void RestoreMini(){Prefs.ShowMini=true;Prefs.MiniLocked=false;Changed();}
        public void ResetPositions()
        {
            Prefs.Mini=new UiBox(16,80,370,48);Prefs.Video=new UiBox(80,140,720,480);Prefs.Panel=new UiBox(100,80,1100,740);
            ++GeometryRevision;Changed();
        }
        public void Minimize()
        {
            Prefs.ShowMini=true;Changed();ClosePanel();
        }
        public void ClosePanel()
        {
            Find.WindowStack.WindowOfType<PlayerWindow>()?.Close(false);
            GameUi.ClosePlayerTab();
        }
        public void SaveBox(string kind,Rect rect)
        {
            UiBox previous=kind=="mini"?Prefs.Mini:kind=="video"?Prefs.Video:Prefs.Panel;
            if(Math.Abs(previous.X-rect.x)<.5f&&Math.Abs(previous.Y-rect.y)<.5f&&Math.Abs(previous.Width-rect.width)<.5f&&Math.Abs(previous.Height-rect.height)<.5f) return;
            var box=new UiBox(rect.x,rect.y,rect.width,rect.height);
            if(kind=="mini") Prefs.Mini=box;else if(kind=="video") Prefs.Video=box;else Prefs.Panel=box;
            Changed();
        }
        public void Tick(bool panelOpen)
        {
            PanelOpen=panelOpen;
            var lib=app.Library;var now=Time.realtimeSinceStartup;
            if(!ReferenceEquals(observedLanguage,LanguageDatabase.activeLanguage))
            {
                observedLanguage=LanguageDatabase.activeLanguage;Jobs=new UiJobRow[0];shownCount=-1;nextLabels=0;StorageLabel=LocalizedText.Message(StorageLabel);Invalidate();
            }
            lib.Options.Volume=Prefs.Muted?0:Prefs.Volume;lib.Options.ShowMini=Prefs.ShowMini;
            if(!ReferenceEquals(observedLibrary,lib))
            {
                observedLibrary=lib;Catalog=UiCatalog.Empty;Rows=new UiRow[0];Playlists=new UiPlaylist[0];builtRevision=-1;statsRevision=-1;StorageLabel=LocalizedText.T("RimTube.StorageNotCalculatedYet");statsWanted=Section=="settings";Invalidate();
            }
            if(build!=null&&build.IsCompleted)
            {
                if(build.IsFaulted)
                {
                    var ignored=build.Exception;
                    if(ReferenceEquals(capturedLibrary,lib)&&capturedGeneration==generation){app.Error=LocalizedText.T("RimTube.CatalogUpdateFailedPreviousResultsWereKeptTry");builtRevision=capturedRevision;force=false;}
                }
                else if(ReferenceEquals(capturedLibrary,lib)&&capturedRevision==lib.Revision&&capturedGeneration==generation)
                {Catalog=build.Result;builtRevision=capturedRevision;force=false;}
                build=null;
            }
            if(panelOpen&&build==null&&(force||builtRevision!=lib.Revision))
            {
                // GameMusic.Tracks currently sorts on access: capture only when invalidated.
                var input=UiCatalog.Capture(lib,app.GameMusic.Tracks);
                capturedLibrary=lib;capturedRevision=lib.Revision;capturedGeneration=generation;
                Playlists=lib.Lists.Select(p=>new UiPlaylist(p,lib.Options)).ToArray();
                var owner=lib;build=Task.Run(()=>UiCatalog.Build(owner,input));
            }
            if(panelOpen)
            {
                Query.GameCatalog=Section=="game";Query.PlaylistIds=null;
                Query.Collection=Section=="library"?"music":Section=="videos"?"videos":"";
                if(Section=="list")
                {
                    Query.PlaylistIds=NoTracks;
                    for(int i=0;i<Playlists.Length;i++) if(Playlists[i].Id==SelectedList){Query.PlaylistIds=Playlists[i].Tracks;break;}
                }
                Rows=Query.Select(Catalog);
                if(shownCount!=Rows.Count){shownCount=Rows.Count;CountLabel=LocalizedText.F("RimTube.TrackCount",shownCount);}
            }
            if(stats!=null&&stats.IsCompleted)
            {
                if(stats.IsFaulted){var ignored=stats.Exception;if(ReferenceEquals(statsLibrary,lib)){StorageLabel=LocalizedText.T("RimTube.StorageCalculationFailedTryRefreshing");statsRevision=capturedStatsRevision;}}
                else if(ReferenceEquals(statsLibrary,lib)&&capturedStatsRevision==lib.Revision){StorageLabel=LocalizedText.T("RimTube.SavedAudioAndVideo")+(stats.Result/1048576d).ToString("0.0")+" MiB";statsRevision=capturedStatsRevision;}
                stats=null;
            }
            if(panelOpen&&Section=="settings"&&(statsWanted||statsRevision!=lib.Revision)&&stats==null)
            {statsWanted=false;var owner=lib;statsLibrary=lib;capturedStatsRevision=lib.Revision;stats=Task.Run(()=>owner.CacheBytes());}
            bool combat=app.Player.InCombat;
            if(combat&&!wasCombat) CombatPulseUntil=now+1;wasCombat=combat;
            bool visible=panelOpen || (Current.Game!=null&&Current.ProgramState==ProgramState.Playing&&(Prefs.ShowMini||app.Player.HasVideo&&app.Player.VideoVisible));
            if(visible&&now>=nextLabels)
            {
                nextLabels=now+.25f;
                Title=lib.FindTrack(app.Player.CurrentId)?.Title??LocalizedText.T("RimTube.NoTrackSelected");
                var currentTrack=lib.FindTrack(app.Player.CurrentId);
                CanChangeQuality=currentTrack?.IsYouTube==true&&MediaChoice.From(currentTrack).Video;
                QualityLabel=CanChangeQuality?L10n.T("RimTube.QualityPrefix")+MediaChoice.From(currentTrack).Height+"p":L10n.T("RimTube.Quality");
                PlayLabel=app.Player.Intent==PlayIntent.Playing?L10n.T("RimTube.Pause"):L10n.T("RimTube.Play");
                Transport=(combat?LocalizedText.T("RimTube.CombatMessage"):"")+app.Player.Status+"  "+Ui.Time(app.Player.Position)+" / "+(app.Player.IsLive?"LIVE":Ui.Time(app.Player.Duration));
                Notice=LocalizedText.Message(!String.IsNullOrEmpty(app.Error)?app.Error:!String.IsNullOrEmpty(app.Jobs.LastError)?app.Jobs.LastError:!String.IsNullOrEmpty(app.Player.Error)?app.Player.Error:Prefs.Warning);
                if(panelOpen)
                {
                    if(Section=="jobs") UpdateJobs();
                    Footer=LocalizedText.F("RimTube.PlayerFooter",LocalizedText.BackendName(app.Player.Snapshot.State),app.Jobs.ActiveCount,StorageLabel);
                }
                MiniTooltip=Title+"\n"+Transport+(Notice==""?"":LocalizedText.T("RimTube.NNote")+Notice);
            }
            if(dirty&&now>=saveAt) Flush();
        }
        private void UpdateJobs()
        {
            var items=app.Jobs.Items;
            if(Jobs.Length!=items.Count) Jobs=new UiJobRow[items.Count];
            for(int i=0;i<items.Count;i++)
            {
                var job=items[i];var row=Jobs[i];
                if(row==null||!row.Matches(job)) Jobs[i]=new UiJobRow(job);
            }
        }
        public void ClearNotice(){app.Error="";app.Jobs.LastError="";app.Player.ClearError();Notice=Prefs.Warning;}
        private void Flush(){dirty=false;try{Prefs.Save(prefsPath);}catch(Exception e){app.Error=LocalizedText.T("RimTube.CouldNotSaveInterfacePreferences")+e.Message;}}
        public void Dispose()
        {
            if(dirty) Flush();
            build?.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            stats?.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
