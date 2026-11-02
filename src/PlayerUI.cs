using System;
using System.Collections.Generic;
using System.Linq;
using RimTube.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTube
{
    public sealed class MusicTab : MainTabWindow
    {
        public override Vector2 RequestedTabSize=>new Vector2(Math.Min(1120,UI.screenWidth-36),Math.Min(740,UI.screenHeight-100));
        private readonly PlayerView view=new PlayerView();
        public MusicTab(){forcePause=false;}
        public override void DoWindowContents(Rect rect){view.Draw(rect);}
        public override void PostClose(){base.PostClose();RimTubeMod.App?.QueueUi(()=>RimTubeMod.App.Save());}
    }
    public sealed class PlayerWindow : Window
    {
        private readonly PlayerView view=new PlayerView();
        private int geometryRevision;
        public override Vector2 InitialSize=>new Vector2(1100,740);
        public PlayerWindow(){doCloseX=false;draggable=true;resizeable=true;forcePause=false;doWindowBackground=false;}
        public override void PreOpen(){base.PreOpen();windowRect=UiTheme.Fit(RimTubeMod.App.Interface.Prefs.Panel,820,540);}
        public override void DoWindowContents(Rect rect)
        {
            UiTheme.Surface(new Rect(-Margin,-Margin,windowRect.width,windowRect.height));view.Draw(rect);
        }
        public override void WindowUpdate()
        {
            base.WindowUpdate();
            if(geometryRevision!=RimTubeMod.App.Interface.GeometryRevision){geometryRevision=RimTubeMod.App.Interface.GeometryRevision;windowRect=UiTheme.Fit(RimTubeMod.App.Interface.Prefs.Panel,820,540);}
            windowRect=UiTheme.Fit(new UiBox(windowRect.x,windowRect.y,windowRect.width,windowRect.height),820,540);
            RimTubeMod.App?.Interface?.SaveBox("panel",windowRect);
        }
        public override void Notify_ResolutionChanged(){windowRect=UiTheme.Fit(RimTubeMod.App.Interface.Prefs.Panel,820,540);}
        public override void PostClose(){base.PostClose();RimTubeMod.App?.QueueUi(()=>RimTubeMod.App.Save());}
    }
    internal static class Ui
    {
        public static bool Click(Rect rect,string text,bool active=false,bool showTooltip=true)=>UiTheme.Button(rect,text,active,showTooltip);
        public static void Button(Rect rect,string text,Action action){if(Click(rect,text)) RimTubeMod.App.QueueUi(action);}
        public static void Confirm(string text,Action action){Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text,()=>RimTubeMod.App.QueueUi(action),true));}
        public static void Menu<T>(IEnumerable<T> items,Func<T,string> name,Action<T> action)
        {
            var owner=RimTubeMod.App.Library;
            var options=items.Select(item=>new FloatMenuOption(name(item),()=>RimTubeMod.App.QueueUi(()=>
            {if(!ReferenceEquals(owner,RimTubeMod.App.Library))throw new InvalidOperationException(LocalizedText.T("RimTube.TheLibraryChangedOpenTheMenuAgain"));action(item);}))).ToList();
            if(options.Count>0) Find.WindowStack.Add(new FloatMenu(options));
        }
        public static string Time(float value)
        {
            if(float.IsNaN(value)||float.IsInfinity(value)) return "--:--";
            var t=TimeSpan.FromSeconds(Math.Max(0,Math.Min(value,86400000)));
            return value>=3600?t.ToString(@"h\:mm\:ss"):t.ToString(@"m\:ss");
        }
        public static string ModeName(PlayMode mode)
        {switch(mode){case PlayMode.Shuffle:return L10n.T("RimTube.Shuffle");case PlayMode.RepeatOne:return L10n.T("RimTube.RepeatOne");case PlayMode.RepeatAll:return L10n.T("RimTube.RepeatAll");default:return L10n.T("RimTube.Ordered");}}
        public static string BattleName(BattleMode mode)
        {switch(mode){case BattleMode.Continue:return L10n.T("RimTube.ContinueBattle");case BattleMode.Custom:return L10n.T("RimTube.CustomBattle");default:return L10n.T("RimTube.VanillaBattle");}}
    }
    public sealed class PlayerView
    {
        private readonly LibraryPanel library=new LibraryPanel();
        private readonly SettingsPanel settings=new SettingsPanel();
        private Vector2 navScroll,jobScroll;
        private RimTubeApp App=>RimTubeMod.App;
        private UiSession State=>App.Interface;
        public void Draw(Rect area)
        {
            if(App?.Interface==null){Widgets.Label(area,L10n.T("RimTube.Initializing"));return;}
            var oldFont=Text.Font;var oldAnchor=Text.Anchor;var oldColor=GUI.color;
            try
            {
                Text.Font=GameFont.Small;Text.Anchor=TextAnchor.UpperLeft;GUI.color=Color.white;
                UiHotkeys.Handle(State);
                UiTheme.Surface(area);
                float w=area.width;
                Text.Font=GameFont.Medium;
                UiTheme.Label(new Rect(10,7,w-312,32),L10n.T("RimTube.LibraryTitle"));Text.Font=GameFont.Small;
                if(Ui.Click(new Rect(w-294,7,82,28),L10n.T("RimTube.Mini"))) State.Minimize();
                if(Ui.Click(new Rect(w-206,7,82,28),L10n.T("RimTube.Video"))){App.Player.ShowVideo(true);State.ClosePanel();}
                if(Ui.Click(new Rect(w-118,7,82,28),L10n.T("RimTube.HideAll"))) App.QueueUi(App.HideAll);
                if(Ui.Click(new Rect(w-30,7,28,28),"×")) State.ClosePanel();
                DrawTransport(new Rect(10,46,w-20,72));
                float bodyHeight=Math.Max(100,area.height-184);
                float navWidth=Math.Max(210,Math.Min(290,w*.23f));
                Rect nav=new Rect(10,126,navWidth,bodyHeight),body=new Rect(navWidth+22,126,Math.Max(200,w-navWidth-32),bodyHeight);
                Navigation(nav);
                GUI.BeginGroup(body);
                try
                {
                    if(State.Section=="settings") settings.Draw(new Rect(0,0,body.width,body.height));
                    else if(State.Section=="jobs") DrawJobs(new Rect(0,0,body.width,body.height));
                    else library.Draw(new Rect(0,0,body.width,body.height));
                }
                finally{GUI.EndGroup();}
                UiTheme.Label(new Rect(10,area.height-50,w-20,22),State.Footer,true);
                UiTheme.Label(new Rect(10,area.height-27,w-90,25),State.Notice);
                if(State.Notice!=""&&Ui.Click(new Rect(w-72,area.height-29,62,25),L10n.T("RimTube.Clear"))) State.ClearNotice();
            }
            finally{Text.Font=oldFont;Text.Anchor=oldAnchor;GUI.color=oldColor;}
        }
        private void DrawTransport(Rect rect)
        {
            var player=App.Player;var prefs=State.Prefs;float x=rect.x,y=rect.y;
            UiTheme.OneLine(new Rect(x,y,rect.width-210,24),State.Title);
            TooltipHandler.TipRegion(new Rect(x,y,rect.width-210,24),State.Title);
            UiTheme.Label(new Rect(x+rect.width-208,y,208,24),State.Transport,true);
            y+=30;
            if(Ui.Click(new Rect(x,y,56,28),L10n.T("RimTube.PreviousShort"))) App.QueueUi(()=>App.Player.Next(true));
            if(Ui.Click(new Rect(x+62,y,56,28),State.PlayLabel)) State.TogglePlay();
            if(Ui.Click(new Rect(x+124,y,56,28),L10n.T("RimTube.Next"))) App.QueueUi(()=>App.Player.Next());
            if(Ui.Click(new Rect(x+186,y,56,28),L10n.T("RimTube.Stop"))) player.Stop();
            if(Ui.Click(new Rect(x+250,y,94,28),Ui.ModeName(App.Library.Options.Mode)))
                Ui.Menu(Enum.GetValues(typeof(PlayMode)).Cast<PlayMode>(),Ui.ModeName,m=>{App.Library.Options.Mode=m;App.Save();});
            if(Ui.Click(new Rect(x+352,y,70,28),prefs.Muted?L10n.T("RimTube.SoundOn"):L10n.T("RimTube.Mute"))) State.ToggleMute();
            float volume=Widgets.HorizontalSlider(new Rect(x+432,y+3,110,24),prefs.Volume,0,1);
            if(Math.Abs(volume-prefs.Volume)>.001f) State.SetVolume(volume);
            float seekWidth=rect.width-558;
            if(seekWidth>60&&player.Duration>0&&player.CanSeek)
            {
                float pos=Widgets.HorizontalSlider(new Rect(x+558,y+3,seekWidth,24),player.Position,0,player.Duration);
                if(Math.Abs(pos-player.Position)>1) App.QueueUi(()=>player.Seek(pos));
            }
        }
        private void Navigation(Rect rect)
        {
            UiTheme.Surface(rect,true);
            float width=rect.width-16,x=rect.x+8,y=rect.y+8;
            if(Ui.Click(new Rect(x,y,width,30),L10n.T("RimTube.AllMusic"),State.Section=="library")) State.Navigate("library");
            if(Ui.Click(new Rect(x,y+36,width,30),L10n.T("RimTube.AllVideos"),State.Section=="videos")) State.Navigate("videos");
            if(Ui.Click(new Rect(x,y+72,width,30),L10n.T("RimTube.GameMusic"),State.Section=="game")) State.Navigate("game");
            UiTheme.Label(new Rect(x,y+113,width,24),L10n.T("RimTube.Playlists"),true);
            Rect bounds=new Rect(x,y+142,width,Math.Max(40,rect.height-262));
            var lists=State.Playlists;
            Widgets.BeginScrollView(bounds,ref navScroll,new Rect(0,0,width-18,Math.Max(bounds.height,lists.Length*36)));
            try
            {
                var range=UiGeometry.VisibleRows(lists.Length,navScroll.y,bounds.height,36);
                for(int i=range.First;i<range.End;i++)
                {
                    var list=lists[i];var row=new Rect(0,i*36,width-20,30);
                    if(Ui.Click(row,list.Label,State.Section=="list"&&State.SelectedList==list.Id,false)) State.Navigate("list",list.Id);
                    TooltipHandler.TipRegion(row,list.Tooltip);
                }
            }
            finally{Widgets.EndScrollView();}
            y=rect.yMax-100;
            if(Ui.Click(new Rect(x,y,width,28),L10n.T("RimTube.NewPlaylist"))) Find.WindowStack.Add(new PlaylistNameWindow(null));
            if(Ui.Click(new Rect(x,y+34,width,28),L10n.T("RimTube.Jobs"),State.Section=="jobs")) State.Navigate("jobs");
            if(Ui.Click(new Rect(x,y+68,width,28),L10n.T("RimTube.Settings"),State.Section=="settings")) State.Navigate("settings");
        }
        private void DrawJobs(Rect rect)
        {
            UiTheme.Label(new Rect(0,0,rect.width,30),LocalizedText.T("RimTube.DownloadImportJobsCurrentPlaybackContinuesAfterCompletion"));
            var jobs=State.Jobs;var bounds=new Rect(0,38,rect.width,rect.height-38);
            Widgets.BeginScrollView(bounds,ref jobScroll,new Rect(0,0,rect.width-20,Math.Max(bounds.height,jobs.Length*70)));
            try
            {
                var range=UiGeometry.VisibleRows(jobs.Length,jobScroll.y,bounds.height,70);
                for(int i=range.First;i<range.End;i++)
                {
                    var row=jobs[i];float y=i*70;
                    UiTheme.Label(new Rect(4,y,rect.width-134,27),row.Title);UiTheme.Label(new Rect(4,y+30,rect.width-134,36),row.Progress,true);
                    if(row.State==JobState.Running||row.State==JobState.Queued)
                    {if(Ui.Click(new Rect(rect.width-120,y+16,92,28),L10n.T("RimTube.Cancel"))) App.QueueUi(()=>App.Jobs.Cancel(row.Job));}
                    else if(row.State!=JobState.Complete&&Ui.Click(new Rect(rect.width-120,y+16,92,28),L10n.T("RimTube.Retry"))) App.QueueUi(()=>App.Jobs.Retry(row.Job));
                }
            }
            finally{Widgets.EndScrollView();}
        }
    }
    internal sealed class PlaylistNameWindow : Window
    {
        private string name="";private readonly string listId;
        public override Vector2 InitialSize=>new Vector2(440,190);
        public PlaylistNameWindow(Playlist list){listId=list?.Id;name=list?.Name??"";doCloseX=true;forcePause=false;closeOnAccept=false;}
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0,0,rect.width,28),listId==null?L10n.T("RimTube.CreatingPlaylist"):L10n.T("RimTube.RenamePlaylist"));
            name=Widgets.TextField(new Rect(0,40,rect.width,30),name);
            if(Ui.Click(new Rect(rect.width-100,90,100,30),L10n.T("RimTube.Save"))) RimTubeMod.App.QueueUi(()=>
            {
                var app=RimTubeMod.App;if(String.IsNullOrWhiteSpace(name)) throw new InvalidOperationException(LocalizedText.T("RimTube.EnterAPlaylistName"));
                if(app.Library.ReadOnly) throw new InvalidOperationException(app.Library.LoadWarning);
                var list=listId==null?new Playlist():app.Library.FindList(listId);
                if(list==null) throw new InvalidOperationException(LocalizedText.T("RimTube.ThePlaylistWasRemoved"));
                list.Name=name.Trim();if(listId==null) app.Library.Lists.Add(list);app.Save();app.Interface.Navigate("list",list.Id);Close();
            });
        }
    }
}
