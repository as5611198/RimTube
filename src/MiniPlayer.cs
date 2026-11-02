using System;
using RimTube.Core;
using UnityEngine;
using Verse;

namespace RimTube
{
    public sealed class MiniWindow : Window
    {
        private bool expanded;
        private float touched;
        private UiBox compact;
        private Rect positioned;
        private int geometryRevision;
        protected override float Margin=>8;
        public override Vector2 InitialSize=>new Vector2(370,48);
        public MiniWindow()
        {
            forcePause=false;preventCameraMotion=false;absorbInputAroundWindow=false;
            doCloseX=false;closeOnAccept=false;closeOnCancel=false;focusWhenOpened=false;
            doWindowBackground=false;drawShadow=false;layer=WindowLayer.GameUI;
            soundAppear=null;soundClose=null;
        }
        public override void PreOpen()
        {
            base.PreOpen();var p=RimTubeMod.App.Interface.Prefs;
            compact=p.Mini;compact.Width=370;compact.Height=48;windowRect=UiTheme.Fit(compact,250,48);positioned=windowRect;touched=Time.realtimeSinceStartup;
        }
        public override void WindowUpdate()
        {
            base.WindowUpdate();var state=RimTubeMod.App.Interface;
            if(geometryRevision!=state.GeometryRevision){geometryRevision=state.GeometryRevision;compact=state.Prefs.Mini;windowRect=UiTheme.Fit(compact,250,48);positioned=windowRect;}
            if(Math.Abs(windowRect.x-positioned.x)>.5f||Math.Abs(windowRect.y-positioned.y)>.5f){compact.X=windowRect.x;compact.Y=windowRect.y;}
            bool hover=windowRect.Contains(UI.MousePositionOnUIInverted);
            if(hover) touched=Time.realtimeSinceStartup;
            expanded=hover||Time.realtimeSinceStartup-touched<.7f;
            compact=UiGeometry.Clamp(compact,UI.screenWidth,UI.screenHeight,250,48);
            if(state.Prefs.MiniSnap&&!Input.GetMouseButton(0)) compact=UiGeometry.Snap(compact,UI.screenWidth,UI.screenHeight);
            var box=compact;box.Height=expanded?116:48;
            windowRect=UiTheme.Fit(box,250,48);
            positioned=windowRect;
            // Persist the compact footprint, not a transient hover expansion.
            state.SaveBox("mini",UiTheme.Rect(compact));
        }
        public override void DoWindowContents(Rect rect)
        {
            var app=RimTubeMod.App;var state=app.Interface;var p=state.Prefs;
            var font=Text.Font;var anchor=Text.Anchor;var color=GUI.color;
            try
            {
                Text.Font=GameFont.Small;Text.Anchor=TextAnchor.UpperLeft;
                UiHotkeys.Handle(state);
                float opacity=p.MiniFade&&!expanded?.45f:p.Opacity;
                UiTheme.Surface(new Rect(-Margin,-Margin,windowRect.width,windowRect.height),false,opacity);
                UiTheme.OneLine(new Rect(0,2,rect.width-74,28),state.Title);
                TooltipHandler.TipRegion(new Rect(0,0,rect.width-74,30),state.MiniTooltip);
                if(state.Notice!="") Widgets.DrawBoxSolid(new Rect(-Margin,0,3,30),new Color(.85f,.7f,.3f));
                if(Ui.Click(new Rect(rect.width-70,0,70,28),state.PlayLabel)) state.TogglePlay();
                if(Time.realtimeSinceStartup<state.CombatPulseUntil)
                    Widgets.DrawBoxSolid(new Rect(-Margin,windowRect.height-Margin-3,windowRect.width,3),new Color(.72f,.3f,.22f));
                if(expanded)
                {
                    if(Ui.Click(new Rect(0,36,50,26),L10n.T("RimTube.PreviousShort"))) app.QueueUi(()=>app.Player.Next(true));
                    if(Ui.Click(new Rect(56,36,50,26),L10n.T("RimTube.NextShort"))) app.QueueUi(()=>app.Player.Next());
                    if(Ui.Click(new Rect(112,36,50,26),p.Muted?L10n.T("RimTube.SoundOn"):L10n.T("RimTube.Mute"))) state.ToggleMute();
                    if(Ui.Click(new Rect(168,36,50,26),L10n.T("RimTube.Library"))) RimTubeMod.OpenPlayer();
                    if(Ui.Click(new Rect(224,36,50,26),L10n.T("RimTube.Video"))) app.Player.ShowVideo(true);
                    if(Ui.Click(new Rect(280,36,Math.Max(44,rect.width-280),26),L10n.T("RimTube.Hide"))){p.ShowMini=false;state.Changed();}
                    float volume=Widgets.HorizontalSlider(new Rect(0,72,rect.width-78,22),p.Volume,0,1);
                    if(Math.Abs(volume-p.Volume)>.001f) state.SetVolume(volume);
                    if(Ui.Click(new Rect(rect.width-72,70,72,26),L10n.T("RimTube.LockThrough"))){p.MiniLocked=true;state.Changed();}
                }
                GUI.DragWindow(new Rect(0,0,rect.width-76,30));
            }
            finally{Text.Font=font;Text.Anchor=anchor;GUI.color=color;}
        }
        public override void Notify_ResolutionChanged(){compact=RimTubeMod.App.Interface.Prefs.Mini;windowRect=UiTheme.Fit(compact,250,48);positioned=windowRect;}
        public static void DrawLocked(UiSession state)
        {
            var p=state.Prefs;var app=RimTubeMod.App;
            if(!p.ShowMini||!p.MiniLocked||state.PanelOpen||(app.Player.HasVideo&&app.Player.VideoVisible)||Event.current.type!=EventType.Repaint||VideoWindow.Covered) return;
            // Pure Repaint primitives: no GUI.Window, control IDs, mouse handlers or HWND changes.
            var box=p.Mini;box.Width=370;box.Height=48;Rect rect=UiTheme.Fit(box,250,48);
            Text.Font=GameFont.Small;Text.Anchor=TextAnchor.UpperLeft;
            UiTheme.Surface(rect,false,p.MiniFade?.45f:p.Opacity);
            UiTheme.OneLine(new Rect(rect.x+8,rect.y+10,rect.width-16,28),state.Title);
        }
    }
}
