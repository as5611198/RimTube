using System;
using RimTube.Core;
using UnityEngine;
using Verse;

namespace RimTube
{
    internal sealed class VideoTimelineWidget
    {
        private readonly RimTubeApp app;
        private readonly VideoTimeline timeline=new VideoTimeline();
        private string trackId,sessionId,timeLabel="--:-- / --:--";
        private bool live;
        private int ownedControl,shownPosition=-1,shownDuration=-1;
        private bool shownLive;
        public VideoTimelineWidget(RimTubeApp app){this.app=app;}
        // Read the event-maintained playback snapshot; never query mpv from Draw.
        public void Update()
        {
            var player=app.Player;var snapshot=player.Snapshot;
            trackId=player.CurrentId;sessionId=snapshot.SessionId;live=player.IsLive;
            timeline.Update(trackId,sessionId,player.CanSeek,live,player.Position,player.Duration);
            if(!Application.isFocused)Cancel();
            RefreshLabel();
        }
        private void RefreshLabel()
        {
            int position=(int)Math.Min(86400000,timeline.DisplayPosition);
            float length=app.Player.Duration;
            int duration=Single.IsNaN(length)||Single.IsInfinity(length)?0:(int)Math.Min(86400000,Math.Max(0,length));
            if(position==shownPosition&&duration==shownDuration&&live==shownLive)return;
            shownPosition=position;shownDuration=duration;shownLive=live;
            timeLabel=live?"LIVE":Ui.Time(position)+" / "+(duration>0?Ui.Time(duration):"--:--");
        }
        public void Draw(Rect bar,Rect label)
        {
            int control=GUIUtility.GetControlID(731204,FocusType.Passive,bar);
            var e=Event.current;
            if(!timeline.IsDragging&&ownedControl!=0)Release();
            if(GUI.enabled&&timeline.Enabled&&e.type==EventType.MouseDown&&e.button==0&&bar.Contains(e.mousePosition)&&GUIUtility.hotControl==0)
            {
                timeline.Begin((e.mousePosition.x-bar.x)/Math.Max(1,bar.width));
                ownedControl=control;GUIUtility.hotControl=control;e.Use();
            }
            if(ownedControl!=0&&GUIUtility.hotControl!=ownedControl){timeline.Cancel();ownedControl=0;}
            if(ownedControl==control)
            {
                if(e.type==EventType.MouseDrag){timeline.Move((e.mousePosition.x-bar.x)/Math.Max(1,bar.width));e.Use();}
                else if(e.rawType==EventType.MouseUp&&e.button==0)
                {
                    timeline.Move((e.mousePosition.x-bar.x)/Math.Max(1,bar.width));double target;
                    if(timeline.Commit(out target))
                    {
                        string expectedTrack=trackId,expectedSession=sessionId;
                        app.QueueUi(()=>
                        {
                            if(ReferenceEquals(app,RimTubeMod.App)&&app.Player.CurrentId==expectedTrack&&app.Player.Snapshot.SessionId==expectedSession&&app.Player.CanSeek&&!app.Player.IsLive)
                                app.Player.Seek((float)target);
                        });
                    }
                    Release();e.Use();
                }
                else if(e.type==EventType.Ignore||e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape)
                {Cancel();if(e.type==EventType.KeyDown)e.Use();}
            }
            RefreshLabel();
            if(e.type==EventType.Repaint)
            {
                var theme=UiTheme.Current;float y=bar.y+bar.height/2;
                var muted=theme.Muted;muted.a=.35f;
                Widgets.DrawBoxSolid(new Rect(bar.x,y-2,bar.width,4),muted);
                float fill=(float)timeline.Fraction*bar.width;
                if(timeline.Enabled)
                {
                    Widgets.DrawBoxSolid(new Rect(bar.x,y-2,fill,4),theme.Accent);
                    if(Mouse.IsOver(bar)||timeline.IsDragging)Widgets.DrawBoxSolid(new Rect(bar.x+Math.Max(0,Math.Min(bar.width-8,fill-4)),y-5,8,10),theme.Text);
                }
            }
            TooltipHandler.TipRegion(bar,L10n.T(live?"RimTube.LiveSeekHint":timeline.Enabled?"RimTube.SeekHint":"RimTube.SeekUnavailable"));
            UiTheme.OneLine(label,timeLabel,!timeline.Enabled);
        }
        private void Release(){if(ownedControl!=0&&GUIUtility.hotControl==ownedControl)GUIUtility.hotControl=0;ownedControl=0;}
        public void Cancel(){timeline.Cancel();Release();}
    }
}
