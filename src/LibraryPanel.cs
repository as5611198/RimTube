using System;
using System.Collections.Generic;
using System.Linq;
using RimTube.Core;
using UnityEngine;
using Verse;

namespace RimTube
{
    internal sealed class LibraryPanel
    {
        private string input="",lastSection="",lastList="";
        private Vector2 scroll;
        private static readonly string[] Sources={"","YouTube","Local","Core","DLC","Mod"};
        private RimTubeApp App=>RimTubeMod.App;
        private UiSession State=>App.Interface;
        public void Draw(Rect rect)
        {
            if(lastSection!=State.Section||lastList!=State.SelectedList)
            {lastSection=State.Section;lastList=State.SelectedList;scroll=Vector2.zero;}
            bool game=State.Section=="game",playlist=State.Section=="list";
            var list=playlist?App.Library.FindList(State.SelectedList):null;
            float w=rect.width;
            UiTheme.OneLine(new Rect(0,0,w-180,28),game?LocalizedText.T("RimTube.GameMusicBaseGameDLCActiveMods"):playlist?(list==null?LocalizedText.T("RimTube.PlaylistRemoved"):LocalizedText.PlaylistName(list)):L10n.T(State.Section=="videos"?"RimTube.VideoLibraryHeading":"RimTube.MusicLibraryHeading"));
            UiTheme.Label(new Rect(w-176,0,70,28),State.CountLabel,true);
            if(Ui.Click(new Rect(w-100,0,100,28),State.Building?L10n.T("RimTube.Refreshing"):L10n.T("RimTube.Refresh"))) App.QueueUi(State.Refresh);
            if(game)
            {
                UiTheme.Label(new Rect(0,34,w,44),LocalizedText.T("RimTube.OnlyMusicDefinitionsFromActiveContentAreListed"),true);
            }
            else if(playlist)
            {
                if(list!=null)
                {
                    UiTheme.Label(new Rect(0,34,w-110,36),FindInfo(list.Id),true);
                    if(Ui.Click(new Rect(w-104,34,104,28),L10n.T("RimTube.PlaylistActions"))) PlaylistMenu(list);
                }
            }
            else
            {
                input=Widgets.TextField(new Rect(0,34,Math.Max(90,w-294),30),input);
                TooltipHandler.TipRegion(new Rect(0,34,Math.Max(90,w-294),30),LocalizedText.T("RimTube.PasteAYouTubeVideoOrPlaylistURL"));
                if(App.Inspecting)
                {if(Ui.Click(new Rect(w-286,34,88,30),L10n.T("RimTube.CancelResolve"))) App.CancelInspect();}
                else if(Ui.Click(new Rect(w-286,34,88,30),L10n.T("RimTube.YouTubeMedia"))) Find.WindowStack.Add(new YouTubeOpenWindow(input));
                if(Ui.Click(new Rect(w-192,34,88,30),L10n.T("RimTube.YouTubePlaylist"))) App.QueueUi(()=>App.Inspect(input,true));
                if(Ui.Click(new Rect(w-98,34,98,30),L10n.T("RimTube.LocalImport"))) App.QueueUi(()=>Find.WindowStack.Add(new AudioFileWindow(paths=>App.ImportLocal(paths,App.Library.FindList(App.Library.Options.GeneralList)??App.Library.Lists[0]))));
            }
            UiTheme.Label(new Rect(0,82,70,26),L10n.T("RimTube.Search"));
            string next=Widgets.TextField(new Rect(72,78,w-72,30),State.Query.Search);
            if(next!=State.Query.Search){State.Query.Search=next;scroll=Vector2.zero;}
            float cw=(w-16)/3;
            if(Ui.Click(new Rect(0,116,cw,28),SourceName(State.Query.Source)))
                Ui.Menu(Sources,SourceName,value=>{State.Query.Source=value;scroll=Vector2.zero;});
            if(Ui.Click(new Rect(cw+8,116,cw,28),StorageName(State.Query.Storage)))
                Ui.Menu(Enum.GetValues(typeof(UiStorage)).Cast<UiStorage>(),StorageName,value=>{State.Query.Storage=value;scroll=Vector2.zero;});
            if(Ui.Click(new Rect((cw+8)*2,116,cw,28),MediaName(State.Query.Media)))
                Ui.Menu(Enum.GetValues(typeof(UiMedia)).Cast<UiMedia>(),MediaName,value=>{State.Query.Media=value;scroll=Vector2.zero;});
            Rect bounds=new Rect(0,154,w,Math.Max(50,rect.height-154));
            DrawRows(bounds,list,game);
        }
        private string FindInfo(string id)
        {foreach(var p in State.Playlists) if(p.Id==id) return p.Info;return "";}
        private static string SourceName(string value)
        {switch(value){case "YouTube":return L10n.T("RimTube.SourceYouTube");case "Local":return L10n.T("RimTube.SourceLocal");case "Core":return L10n.T("RimTube.SourceCore");case "DLC":return L10n.T("RimTube.SourceDlc");case "Mod":return L10n.T("RimTube.SourceMod");default:return L10n.T("RimTube.SourceAll");}}
        private static string StorageName(UiStorage value)
        {switch(value){case UiStorage.Saved:return L10n.T("RimTube.StorageSaved");case UiStorage.Remote:return L10n.T("RimTube.StorageRemote");case UiStorage.Missing:return L10n.T("RimTube.StorageMissing");default:return L10n.T("RimTube.StorageAll");}}
        private static string MediaName(UiMedia value)
        {switch(value){case UiMedia.Audio:return L10n.T("RimTube.TypeMusic");case UiMedia.Video:return L10n.T("RimTube.TypeVideo");case UiMedia.Live:return L10n.T("RimTube.TypeLive");default:return L10n.T("RimTube.TypeAll");}}
        private void DrawRows(Rect bounds,Playlist list,bool game)
        {
            var rows=State.Rows;
            float contentHeight=Math.Max(bounds.height,rows.Count*62);
            scroll.y=Mathf.Clamp(scroll.y,0,Math.Max(0,contentHeight-bounds.height));
            Widgets.BeginScrollView(bounds,ref scroll,new Rect(0,0,bounds.width-20,contentHeight));
            try
            {
                var range=UiGeometry.VisibleRows(rows.Count,scroll.y,bounds.height,62);
                float w=bounds.width-20;
                for(int i=range.First;i<range.End;i++)
                {
                    var row=rows[i];float y=i*62;var line=new Rect(0,y,w,60);
                    if((i&1)==0) Widgets.DrawBoxSolid(line,UiTheme.Current.Panel);
                    if(App.Player.CurrentId==row.Id) Widgets.DrawBoxSolid(new Rect(0,y,3,60),UiTheme.Current.Accent);
                    UiTheme.OneLine(new Rect(8,y+4,w-192,26),row.Title,row.Missing);
                    UiTheme.OneLine(new Rect(8,y+31,w-192,25),row.Detail,true);
                    TooltipHandler.TipRegion(new Rect(6,y,w-190,60),row.Tooltip);
                    if(Ui.Click(new Rect(w-180,y+16,52,28),L10n.T("RimTube.Play"))) App.QueueUi(()=>Play(row,list));
                    if(Ui.Click(new Rect(w-122,y+16,54,28),L10n.T("RimTube.Add"))) AddMenu(row);
                    if(Ui.Click(new Rect(w-62,y+16,56,28),L10n.T("RimTube.More"))) RowMenu(row,list);
                    if(Event.current.type==EventType.MouseDown&&Event.current.button==1&&line.Contains(Event.current.mousePosition))
                    {RowMenu(row,list);Event.current.Use();}
                }
                if(rows.Count==0) UiTheme.Label(new Rect(12,12,w-24,80),State.Building?LocalizedText.T("RimTube.BuildingCatalogSnapshot"):LocalizedText.T("RimTube.NoMatchingTracksClearFiltersImportMediaOr"),true);
            }
            finally{Widgets.EndScrollView();}
        }
        private Track TrackFor(UiRow row)
        {
            if(!row.InLibrary) return row.Track;
            return App.Library.FindTrack(row.Id)??throw new InvalidOperationException(LocalizedText.T("RimTube.TheTrackWasRemovedRefreshTheList"));
        }
        private void Play(UiRow row,Playlist list)
        {
            var track=TrackFor(row);
            if(row.IsGame&&!row.InLibrary) App.AddGameMusic(track,play:true);
            else App.PlayTrack(track,list?.Id);
        }
        private void AddMenu(UiRow row)
        {
            var choices=new List<FloatMenuOption>();
            if(row.IsGame&&!row.InLibrary) choices.Add(new FloatMenuOption(LocalizedText.T("RimTube.AddToLibrary"),()=>App.QueueUi(()=>App.AddGameMusic(TrackFor(row)))));
            foreach(var snapshot in State.Playlists)
            {
                if(snapshot.Automatic) continue;
                string id=snapshot.Id;
                choices.Add(new FloatMenuOption(LocalizedText.T("RimTube.AddTo")+snapshot.Label,()=>App.QueueUi(()=>
                {
                    var list=App.Library.FindList(id);if(list==null) throw new InvalidOperationException(LocalizedText.T("RimTube.ThePlaylistWasRemoved"));
                    if(row.IsGame) App.AddGameMusic(TrackFor(row),list);
                    else{App.Library.AddTo(list,TrackFor(row));App.Save();}
                })));
            }
            if(choices.Count>0) Find.WindowStack.Add(new FloatMenu(choices));
        }
        private void RowMenu(UiRow row,Playlist list)
        {
            var options=new List<FloatMenuOption>();
            options.Add(new FloatMenuOption(row.IsYouTube?LocalizedText.T("RimTube.MediaOptionsStreamDownloadQualityCaptions"):LocalizedText.T("RimTube.TrackDetails"),()=>App.QueueUi(()=>
            {var track=TrackFor(row);if(row.IsYouTube) Find.WindowStack.Add(new YouTubeOpenWindow(track.Url,track,list?.Id));else Find.WindowStack.Add(new TrackDetailsWindow(track));})));
            if(list!=null&&!list.IsGameCatalog)
            {
                string id=list.Id;
                options.Add(new FloatMenuOption(LocalizedText.T("RimTube.RemoveFromThisPlaylistKeepFiles"),()=>App.QueueUi(()=>{Writable();var current=App.Library.FindList(id);if(current==null)return;current.Tracks.Remove(row.Id);App.Save();})));
                options.Add(new FloatMenuOption(LocalizedText.T("RimTube.MoveUpInPlaylist"),()=>App.QueueUi(()=>Move(id,row.Id,-1))));
                options.Add(new FloatMenuOption(LocalizedText.T("RimTube.MoveDownInPlaylist"),()=>App.QueueUi(()=>Move(id,row.Id,1))));
            }
            if(row.InLibrary&&!row.IsGame)
                options.Add(new FloatMenuOption(L10n.T("RimTube.RemoveMedia"),()=>Ui.Confirm(L10n.F("RimTube.RemoveMediaConfirm",row.Title),()=>App.RemoveMedia(row.Id))));
            if(!row.IsGame&&row.SavedAudio)
                options.Add(new FloatMenuOption(LocalizedText.T("RimTube.DeleteSavedMusicCopy"),()=>Ui.Confirm(LocalizedText.T("RimTube.DeleteOnlyRimTubeSSavedMusicCopyNPlaylists"),()=>App.DeleteAudio(TrackFor(row)))));
            if(!row.IsGame&&row.SavedVideo)
                options.Add(new FloatMenuOption(LocalizedText.T("RimTube.DeleteSavedVideoCopy"),()=>Ui.Confirm(LocalizedText.T("RimTube.DeleteOnlyRimTubeSSavedVideoCopyNPlaylists"),()=>App.DeleteVideo(TrackFor(row)))));
            Find.WindowStack.Add(new FloatMenu(options));
        }
        private void Writable(){if(App.Library.ReadOnly) throw new InvalidOperationException(App.Library.LoadWarning);}
        private void Move(string listId,string trackId,int step)
        {
            Writable();var list=App.Library.FindList(listId);if(list==null)return;
            int from=list.Tracks.IndexOf(trackId),to=from+step;if(from<0||to<0||to>=list.Tracks.Count)return;
            list.Tracks.RemoveAt(from);list.Tracks.Insert(to,trackId);App.Save();
        }
        private void PlaylistMenu(Playlist list)
        {
            string id=list.Id;
            var options=new List<FloatMenuOption>{
                new FloatMenuOption(L10n.T("RimTube.SetGeneral"),()=>App.QueueUi(()=>{Writable();App.Library.Options.GeneralList=id;App.Save();})),
                new FloatMenuOption(L10n.T("RimTube.SetCombat"),()=>App.QueueUi(()=>{Writable();App.Library.Options.CombatList=id;App.Library.Options.Battle=BattleMode.Custom;App.Save();})),
                new FloatMenuOption(L10n.T("RimTube.Rename"),()=>Find.WindowStack.Add(new PlaylistNameWindow(list)))};
            if(list.IsGameCatalog)
                options.Add(new FloatMenuOption(L10n.T("RimTube.RefreshSource"),()=>App.QueueUi(()=>App.RefreshGameMusic())));
            else if(!String.IsNullOrEmpty(list.SourceUrl))
            {
                options.Add(new FloatMenuOption(L10n.T("RimTube.ManualSync"),()=>App.QueueUi(()=>App.Inspect(list.SourceUrl,true,list))));
                options.Add(new FloatMenuOption(LocalizedText.T("RimTube.DownloadPlaylistSkipSavedCopies"),()=>Find.WindowStack.Add(new YouTubeOpenWindow(list.SourceUrl,batch:list))));
            }
            if(!list.IsGameCatalog) options.Add(new FloatMenuOption(LocalizedText.T("RimTube.DeletePlaylistKeepTracksAndFiles"),()=>Ui.Confirm(LocalizedText.T("RimTube.DeleteThisPlaylistTracksAndAllFilesWill"),()=>
            {
                Writable();if(id==App.Library.Options.GeneralList||id==App.Library.Options.CombatList) throw new InvalidOperationException(LocalizedText.T("RimTube.AssignAnotherEverydayCombatPlaylistFirst"));
                App.Library.Lists.Remove(list);App.Save();State.Navigate("library");
            })));
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
