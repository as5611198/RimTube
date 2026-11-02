using System;
using System.Linq;
using RimTube.Core;
using UnityEngine;
using Verse;

namespace RimTube
{
    public sealed class YouTubeOpenWindow : Window
    {
        private readonly string input, listId;
        private readonly Track track;
        private readonly Playlist batch;
        private readonly Library library;
        private MediaChoice choice;
        private readonly CaptionPreview captionPreview;
        public override Vector2 InitialSize => new Vector2(720, 500);
        public YouTubeOpenWindow(string url, Track existing = null, string list = null, Playlist batch = null)
        {
            input = url; track = existing; listId = list; this.batch = batch; library = RimTubeMod.App.Library;
            choice = existing == null ? RimTubeMod.App.Interface.Prefs.NewMediaChoice() : MediaChoice.From(existing);
            if(existing==null&&RimTubeMod.App.Interface.Section=="videos")choice.Video=true;
            if(batch==null)captionPreview=new CaptionPreview(RimTubeMod.App,input,choice);
            doCloseX = true; draggable = true; forcePause = false;
        }
        public override void PreOpen(){base.PreOpen();captionPreview?.Start();}
        public override void WindowUpdate(){base.WindowUpdate();captionPreview?.Tick();}
        public override void PostClose(){captionPreview?.Dispose();base.PostClose();}
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0, 0, rect.width - 30, 32), batch == null ? L10n.T("RimTube.YouTubeOptions") : LocalizedText.T("RimTube.DownloadPlaylist") + batch.Name);
            UiTheme.Label(new Rect(0, 40, rect.width, 36), captionPreview?.Result?.Title??track?.Title ?? (batch == null ? input : LocalizedText.F("RimTube.BatchDownloadSummary",batch.Tracks.Count,RimTubeMod.App.Jobs.Parallelism)),true);
            if(captionPreview!=null)UiTheme.OneLine(new Rect(0,78,rect.width,20),captionPreview.Status,true);
            Ui.Button(new Rect(0, 100, rect.width, 32), L10n.T("RimTube.ContentPrefix") + (choice.Video ? L10n.T("RimTube.VideoAndAudio") : L10n.T("RimTube.MusicOnly")), () => choice.Video = !choice.Video);
            Ui.Button(new Rect(0, 144, rect.width, 32), LocalizedText.F("RimTube.QualityLimitDetail",choice.Height),
                () => Ui.Menu(new[] {144,240,360,480,720,1080,1440,2160,4320}, h=>h+"p", h=>choice.Height=h));
            Widgets.Label(new Rect(0, 190, 120, 28), L10n.T("RimTube.SubtitleLanguage"));
            choice.Subtitle = Widgets.TextField(new Rect(125, 186, rect.width - 245, 30), choice.Subtitle);
            Ui.Button(new Rect(rect.width - 110, 186, 110, 30), L10n.T("RimTube.SelectLanguage"), () =>
            {
                var available=captionPreview?.Result??track;
                string langs; var values = available != null && available.Metadata.TryGetValue("media.subtitleLanguages", out langs)
                    ? new[] {"off"}.Concat(langs.Split(',').Select(SubtitleLanguage.Normalize).Where(s=>s!="")).Distinct() : new[] {"off","zh-TW","zh-Hant","zh-Hans","en","ja","ko"};
                Ui.Menu(values, s=>s=="off" ? L10n.T("RimTube.SubtitleOff") : s, s=>choice.Subtitle=s);
            });
            Widgets.CheckboxLabeled(new Rect(0, 228, rect.width, 28), L10n.T("RimTube.CaptionFallback"), ref choice.AutoSubtitles);
            Widgets.Label(new Rect(0, 270, rect.width, 66), LocalizedText.T("RimTube.CaptionsUseLanguagesSuppliedByTheSourceLive"));
            if (batch != null)
            {
                Ui.Button(new Rect(rect.width-180, rect.height-42, 180, 34), L10n.T("RimTube.DownloadQueue"), () =>
                {
                    EnsureLibrary(); choice.Validate();
                    RimTubeMod.App.DownloadPlaylist(batch,choice);
                    Close();
                });
            }
            else
            {
                if (track != null)
                {
                    Ui.Button(new Rect(0, 340, rect.width/2-5, 30), L10n.T("RimTube.DeleteAudio"), () => Ui.Confirm(LocalizedText.T("RimTube.DeleteTheLocalMusicCopyPlaylistsAndVideo"), ()=>RimTubeMod.App.DeleteAudio(track)));
                    Ui.Button(new Rect(rect.width/2+5, 340, rect.width/2-5, 30), L10n.T("RimTube.DeleteVideo"), () => Ui.Confirm(LocalizedText.T("RimTube.DeleteTheLocalVideoCopyPlaylistsAndMusic"), ()=>RimTubeMod.App.DeleteVideo(track)));
                }
                Ui.Button(new Rect(0, rect.height-84, rect.width/2-5, 32), L10n.T("RimTube.StreamNow"), () => Accept(MediaDelivery.Stream,true));
                Ui.Button(new Rect(rect.width/2+5, rect.height-84, rect.width/2-5, 32), L10n.T("RimTube.DownloadPlay"), () => Accept(MediaDelivery.Download,true));
                Ui.Button(new Rect(0, rect.height-42, rect.width/2-5, 32), L10n.T("RimTube.DownloadOnly"), () => Accept(MediaDelivery.Download,false));
                if (track != null) Ui.Button(new Rect(rect.width/2+5, rect.height-42, rect.width/2-5, 32), L10n.T("RimTube.PreferSaved"), () => Accept(MediaDelivery.PreferSaved,true));
            }
        }
        private void EnsureLibrary() { if (!ReferenceEquals(RimTubeMod.App.Library, library)) throw new InvalidOperationException(LocalizedText.T("RimTube.TheLibraryChangedOpenMediaOptionsAgain")); }
        private void Accept(MediaDelivery delivery, bool play)
        {
            EnsureLibrary(); choice.Delivery = delivery; choice.Validate(); var app=RimTubeMod.App;
            if (play && choice.Video && library.Options.Backend != "mpv") throw new InvalidOperationException(LocalizedText.T("RimTube.SwitchToTheMpvBackendForVideo"));
            if (track == null) app.Inspect(input,false,choice:choice.Copy(),autoplay:play);
            else
            {
                choice.Store(track); app.Save();
                if (delivery == MediaDelivery.Download) app.DownloadTrack(track,choice,play,listId);
                else if (play) app.PlayTrack(track,listId);
            }
            Close();
        }
    }

    public sealed class VideoWindow : Window
    {
        private const float HeaderHeight=38;
        private float ControlsHeight=>controlsExpanded?112:40;
        private bool controlsExpanded;
        private readonly VideoTimelineWidget timeline;
        private static readonly int[] QualityHeights={144,240,360,480,720,1080,1440,2160,4320};
        private readonly RimTubeApp app;
        private bool maximized;
        private Rect restore;
        private Rect lastPlaced;
        private bool placed;
        private bool wasCovered;
        private float nextPlace;
        private int geometryRevision;
        // Keep raw coverage for other UI consumers; continuous playback never hides the picture.
        public static bool ShouldAutoHide => RimTubeMod.App?.Library?.Options.PauseCoveredVideo==true && Covered;
        public static bool Covered
        {
            get
            {
                if(GameUi.MainTabs?.OpenTab!=null) return true;
                var windows=Find.WindowStack?.Windows;
                if(windows==null) return false;
                for(int i=0;i<windows.Count;i++)
                {var w=windows[i];if(w.IsOpen&&!(w is VideoWindow)&&!(w is MiniWindow)&&(w.layer==WindowLayer.Dialog||w is FloatMenu)) return true;}
                return false;
            }
        }
        protected override float Margin=>10;
        public override Vector2 InitialSize => new Vector2(720,480);
        public VideoWindow()
        {
            app=RimTubeMod.App;timeline=new VideoTimelineWidget(app); resizeable=true; forcePause=false;
            preventCameraMotion=false; absorbInputAroundWindow=false; focusWhenOpened=false;
            doCloseX=false; closeOnAccept=false; closeOnCancel=false;
            doWindowBackground=false;layer=WindowLayer.GameUI;soundAppear=null;soundClose=null;
        }
        public override void PreOpen(){base.PreOpen();windowRect=UiTheme.Fit(app.Interface.Prefs.Video,360,280);}
        public override void WindowUpdate()
        {
            base.WindowUpdate();
            if(!ReferenceEquals(app,RimTubeMod.App)) {Close(false);return;}
            if(geometryRevision!=app.Interface.GeometryRevision){geometryRevision=app.Interface.GeometryRevision;maximized=false;controlsExpanded=false;timeline.Cancel();windowRect=UiTheme.Fit(app.Interface.Prefs.Video,360,240);placed=false;}
            if(!maximized&&app.Interface.Prefs.LockVideoAspect)
            {
                float ratio=app.Player.VideoAspect;
                windowRect.height=(windowRect.width-2*Margin)/ratio+2*Margin+HeaderHeight+ControlsHeight;
            }
            windowRect=UiTheme.Fit(new UiBox(windowRect.x,windowRect.y,windowRect.width,windowRect.height),360,240);
            if(!maximized) app.Interface.SaveBox("video",windowRect);
            bool covered=ShouldAutoHide;
            if(covered){app.Player.Video?.Hide();placed=false;}
            else if(app.Player.Video!=null&&app.Player.Video.Handle!=IntPtr.Zero)
            {
                Rect target=new Rect(windowRect.x+Margin,windowRect.y+Margin+HeaderHeight,windowRect.width-2*Margin,Math.Max(40,windowRect.height-2*Margin-HeaderHeight-ControlsHeight));
                if(!placed||target!=lastPlaced||Time.realtimeSinceStartup>=nextPlace)
                {placed=app.Player.Video.Place(target.x,target.y,target.width,target.height,UI.screenWidth,UI.screenHeight);lastPlaced=target;nextPlace=Time.realtimeSinceStartup+1;}
            }
            wasCovered=covered;timeline.Update();
        }
        public override void DoWindowContents(Rect rect)
        {
            if (!ReferenceEquals(app,RimTubeMod.App)) { Close(false); return; }
            var previousFont=Text.Font; var previousAnchor=Text.Anchor;var previousColor=GUI.color;
            try
            {
                Text.Font=GameFont.Small; Text.Anchor=TextAnchor.UpperLeft;GUI.color=Color.white;
                UiHotkeys.Handle(app.Interface);
                UiTheme.Surface(new Rect(-Margin,-Margin,windowRect.width,windowRect.height));
                UiTheme.OneLine(new Rect(0,0,rect.width-72,30),app.Interface.Title);
                TooltipHandler.TipRegion(new Rect(0,0,rect.width-72,30),app.Interface.Title);
                if(Ui.Click(new Rect(rect.width-64,0,64,28),L10n.T(controlsExpanded?"RimTube.Less":"RimTube.More"),controlsExpanded)) ToggleControls();
                TooltipHandler.TipRegion(new Rect(rect.width-64,0,64,28),L10n.T("RimTube.VideoControlsHint"));
                var title=new Rect(0,0,Math.Max(20,rect.width-72),30);
                if(Event.current.type==EventType.MouseDown&&Event.current.clickCount==2&&title.Contains(Event.current.mousePosition)) {ToggleSize();Event.current.Use();}
                GUI.DragWindow(title);
                Rect picture=new Rect(0,HeaderHeight,rect.width,Math.Max(40,rect.height-HeaderHeight-ControlsHeight));
                if(wasCovered) UiTheme.Label(picture,L10n.T("RimTube.VideoCovered"),true);
                else if(app.Player.Video==null||app.Player.Video.Handle==IntPtr.Zero) UiTheme.Label(picture,L10n.T("RimTube.VideoUnavailable"),true);
                if(controlsExpanded) DrawExpandedControls(rect);
                float y=rect.height-34;
                if(Ui.Click(new Rect(0,y,56,28),app.Interface.PlayLabel)) app.Interface.TogglePlay();
                float timeWidth=app.Player.Duration>=3600?146:108;
                var bar=new Rect(64,y,Math.Max(30,rect.width-timeWidth-72),28);
                timeline.Draw(bar,new Rect(rect.width-timeWidth,y,timeWidth,28));
            }
            finally { Text.Font=previousFont; Text.Anchor=previousAnchor;GUI.color=previousColor; }
        }
        private void ToggleControls()
        {
            timeline.Cancel();float before=ControlsHeight;controlsExpanded=!controlsExpanded;
            if(!maximized)windowRect.height+=ControlsHeight-before;
            placed=false;
        }
        private void DrawExpandedControls(Rect rect)
        {
            float y=rect.height-ControlsHeight+4;
            if(Ui.Click(new Rect(0,y,64,28),L10n.T("RimTube.Library"))) RimTubeMod.OpenPlayer();
            if(Ui.Click(new Rect(70,y,32,28),"CC",app.Player.SubtitlesVisible))
            {
                if(app.Player.HasSubtitles)app.Player.ShowSubtitles(!app.Player.SubtitlesVisible);
                else OpenSubtitleOptions();
            }
            TooltipHandler.TipRegion(new Rect(70,y,32,28),L10n.T("RimTube.CaptionToggleHint"));
            if(Ui.Click(new Rect(108,y,24,28),"▾"))OpenSubtitleOptions();
            var qualityRect=new Rect(138,y,Math.Max(40,rect.width-178),28);
            bool enabled=GUI.enabled;
            try
            {
                GUI.enabled=enabled&&app.Interface.CanChangeQuality;
                if(Ui.Click(qualityRect,app.Interface.QualityLabel))
                {
                    string currentId=app.Player.CurrentId;
                    Ui.Menu(QualityHeights,h=>h+"p",h=>app.ChangeVideoQuality(currentId,h));
                }
            }
            finally{GUI.enabled=enabled;}
            TooltipHandler.TipRegion(qualityRect,L10n.T("RimTube.QualityHint"));
            if(Ui.Click(new Rect(rect.width-32,y,32,28),"⋯")) OpenActions();
            TooltipHandler.TipRegion(new Rect(rect.width-32,y,32,28),L10n.T("RimTube.More"));
            var state=app.Interface;var prefs=state.Prefs;y+=34;
            if(Ui.Click(new Rect(0,y,76,26),prefs.Muted?L10n.T("RimTube.Unmute"):L10n.T("RimTube.Mute"))) state.ToggleMute();
            var volumeRect=new Rect(84,y+2,Math.Max(40,rect.width-84),24);
            float volume=Widgets.HorizontalSlider(volumeRect,prefs.Volume,0,1);
            if(Math.Abs(volume-prefs.Volume)>.001f) state.SetVolume(volume);
            TooltipHandler.TipRegion(volumeRect,L10n.T("RimTube.Volume"));
        }
        private void OpenActions()
        {
            string trackId=app.Player.CurrentId;
            Ui.Menu(new[]{"options","audio","size","stop"},action=>
                L10n.T(action=="options"?"RimTube.YouTubeOptions":action=="audio"?"RimTube.AudioOnly":action=="size"?(maximized?"RimTube.Restore":"RimTube.Maximize"):"RimTube.Stop"),action=>
            {
                if(!ReferenceEquals(app,RimTubeMod.App))return;
                if(action=="options")
                {
                    var track=app.Library.FindTrack(trackId);
                    if(track?.IsYouTube==true&&app.Player.CurrentId==trackId)Find.WindowStack.Add(new YouTubeOpenWindow(track.Url,track));
                }
                else if(action=="audio"){app.Player.ShowVideo(false);app.Interface.Prefs.ShowMini=true;app.Interface.Changed();Close(false);}
                else if(action=="size")ToggleSize();
                else app.Player.Stop();
            });
        }
        private void OpenSubtitleOptions()
        {
            string id=app.Player.CurrentId;
            app.QueueUi(()=>{var track=app.Library.FindTrack(id);if(track?.IsYouTube==true)Find.WindowStack.Add(new SubtitleOptionsWindow(track));});
        }
        private void ToggleSize()
        {
            timeline.Cancel();
            if(maximized) windowRect=restore;
            else{restore=windowRect;windowRect=new Rect(24,70,Math.Max(360,UI.screenWidth-48),Math.Max(240,UI.screenHeight-140));}
            maximized=!maximized;placed=false;
        }
        public override void Notify_ResolutionChanged(){windowRect=UiTheme.Fit(app.Interface.Prefs.Video,360,240);maximized=false;placed=false;}
        public override void PostClose() { timeline.Cancel();app.Player.Video?.Hide(); base.PostClose(); }
    }
}
