using System;
using System.Linq;
using RimTube.Core;
using UnityEngine;
using Verse;

namespace RimTube
{
    internal sealed class SettingsPanel
    {
        private Vector2 scroll;
        private string dataPath="";
        private RimTubeApp App=>RimTubeMod.App;
        private UiSession State=>App.Interface;
        public void Draw(Rect rect)
        {
            float w=rect.width-20;var p=State.Prefs;var s=App.Library.Options;
            Widgets.BeginScrollView(rect,ref scroll,new Rect(0,0,w,2020));
            try
            {
                UiTheme.Label(new Rect(0,0,w,28),L10n.T("RimTube.UiPreferences"));
                if(Ui.Click(new Rect(0,34,w,30),L10n.T("RimTube.ThemePrefix")+UiTheme.Current.Name+" · "+UiTheme.Current.Flavor))
                    Ui.Menu(UiTheme.All,t=>t.Name,t=>{p.Theme=t.Id;State.Changed();});
                bool changed=false;
                changed|=Check(74,w,L10n.T("RimTube.ShowMini"),ref p.ShowMini);
                changed|=Check(108,w,L10n.T("RimTube.SnapMini"),ref p.MiniSnap);
                changed|=Check(142,w,L10n.T("RimTube.FadeMini"),ref p.MiniFade);
                changed|=Check(176,w,L10n.T("RimTube.LockMini"),ref p.MiniLocked);
                changed|=Check(210,w,L10n.T("RimTube.KeepAspect"),ref p.LockVideoAspect);
                UiTheme.Label(new Rect(0,248,170,26),L10n.T("RimTube.MiniOpacity"));
                float opacity=Widgets.HorizontalSlider(new Rect(176,248,Math.Max(80,w-176),24),p.Opacity,.4f,1);
                if(Math.Abs(opacity-p.Opacity)>.001f){p.Opacity=opacity;changed=true;}
                if(Ui.Click(new Rect(0,284,w/2-4,30),L10n.T("RimTube.RestoreMini"))) State.RestoreMini();
                if(Ui.Click(new Rect(w/2+4,284,w/2-4,30),L10n.T("RimTube.ResetWindows")))
                State.ResetPositions();
                UiTheme.Label(new Rect(0,330,w,26),LocalizedText.T("RimTube.ShortcutsClickThenPressANewCombinationDisabled"),true);
                for(int i=0;i<p.Shortcuts.Length;i++)
                {
                    var binding=p.Shortcuts[i];float y=364+i*34;
                    UiTheme.Label(new Rect(0,y,w-220,28),UiHotkeys.Name(binding.Action));
                    if(Ui.Click(new Rect(w-214,y,214,28),UiHotkeys.Label(binding))) Find.WindowStack.Add(new ShortcutWindow(binding));
                }
                UiTheme.Label(new Rect(0,578,w,28),L10n.T("RimTube.ColonySettings"));
                if(Ui.Click(new Rect(0,614,w,30),L10n.T("RimTube.CombatPrefix")+Ui.BattleName(s.Battle)))
                    Ui.Menu(Enum.GetValues(typeof(BattleMode)).Cast<BattleMode>(),Ui.BattleName,value=>{s.Battle=value;App.Save();});
                if(Ui.Click(new Rect(0,652,w,30),L10n.T("RimTube.GeneralListPrefix")+LocalizedText.PlaylistName(App.Library.FindList(s.GeneralList))))
                    Ui.Menu(State.Playlists,v=>v.Name,v=>{s.GeneralList=v.Id;App.Save();});
                if(Ui.Click(new Rect(0,690,w,30),L10n.T("RimTube.CombatListPrefix")+LocalizedText.PlaylistName(App.Library.FindList(s.CombatList))))
                    Ui.Menu(State.Playlists,v=>v.Name,v=>{s.CombatList=v.Id;App.Save();});
                if(Check(730,w,L10n.T("RimTube.PauseWithGame"),ref s.PauseWithGame)) App.QueueUi(App.Save);
                if(Ui.Click(new Rect(0,766,w,30),s.PauseCoveredVideo?L10n.T("RimTube.CoverPause"):L10n.T("RimTube.CoverContinue")))
                    Ui.Menu(new[]{false,true},v=>v?L10n.T("RimTube.CoverPauseChoice"):L10n.T("RimTube.CoverContinueChoice"),v=>{s.PauseCoveredVideo=v;App.Save();});
                UiTheme.Label(new Rect(0,812,w,26),L10n.T("RimTube.MediaTools"));
                if(dataPath=="") dataPath=App.Library.Root;
                dataPath=Widgets.TextField(new Rect(0,846,w-100,30),dataPath);
                if(Ui.Click(new Rect(w-94,846,94,30),L10n.T("RimTube.SwitchFolder"))) App.QueueUi(()=>App.SwitchData(dataPath));
                UiTheme.Label(new Rect(0,882,w-126,28),State.StorageLabel,true);
                if(Ui.Click(new Rect(w-120,882,120,28),L10n.T("RimTube.RefreshStorage"))) State.Refresh();
                s.YtDlp=PathField(924,w,"yt-dlp.exe",s.YtDlp);s.MpvPath=PathField(962,w,"mpv.exe",s.MpvPath);
                s.Ffmpeg=PathField(1000,w,"ffmpeg.exe",s.Ffmpeg);s.JsRuntime=PathField(1038,w,L10n.T("RimTube.JsRuntime"),s.JsRuntime);
                if(Ui.Click(new Rect(0,1080,w/2-4,30),L10n.T("RimTube.ApplySettings"))) App.QueueUi(App.ApplyPlaybackSettings);
                if(Ui.Click(new Rect(w/2+4,1080,w/2-4,30),L10n.T("RimTube.CheckTools"))) App.CheckTools();
                UiTheme.Label(new Rect(0,1120,w,94),App.ToolsInfo,true);
                if(Ui.Click(new Rect(0,1220,w,30),s.Backend=="native-local"?L10n.T("RimTube.BackendNative"):L10n.T("RimTube.BackendMpv")))
                    Ui.Menu(new[]{"mpv","native-local"},v=>v=="mpv"?"mpv":L10n.T("RimTube.NativeBackend"),v=>{s.Backend=v;App.ApplyPlaybackSettings();});
                if(Ui.Click(new Rect(0,1260,w,30),LocalizedText.F("RimTube.ConcurrentDownloadsDetail",App.Jobs.Parallelism)))
                    Ui.Menu(new[]{1,2,3},v=>LocalizedText.F("RimTube.TrackCount",v),v=>{s.DownloadWorkers=v;App.Save();});
                UiTheme.Label(new Rect(0,1304,w,28),LocalizedText.F("RimTube.StreamBufferDetail",s.CacheMiB,s.BackCacheMiB));
                s.CacheMiB=(int)Widgets.HorizontalSlider(new Rect(0,1338,w,24),s.CacheMiB,4,128);
                UiTheme.Label(new Rect(0,1380,w,110),LocalizedText.T("RimTube.HiddenVideoContinuesAudioAndDecodingStopReleases"),true);
                UiTheme.Label(new Rect(0,1500,w,28),L10n.T("RimTube.CaptionDefaults"));
                if(Ui.Click(new Rect(0,1536,w,30),L10n.T("RimTube.CaptionDefaultPrefix")+(p.DefaultSubtitle=="off"?L10n.T("RimTube.NoAutoSelection"):p.DefaultSubtitle)))
                    Ui.Menu(new[]{"off","zh-TW","zh-Hant","zh-Hans","en","ja","ko"},s=>s=="off"?L10n.T("RimTube.NoAutoCaptions"):s,s=>{p.DefaultSubtitle=s;State.Changed();});
                changed|=Check(1574,w,L10n.T("RimTube.DefaultCaptionFallback"),ref p.DefaultAutoSubtitles);
                if(Ui.Click(new Rect(0,1612,w,30),L10n.F("RimTube.SubtitleSize",(int)Math.Round(p.SubtitleScale*100))))
                    Ui.Menu(new[]{1f,1.25f,1.5f,1.75f,2f,2.5f,3f},value=>(int)Math.Round(value*100)+"%",State.SetSubtitleScale);
                TooltipHandler.TipRegion(new Rect(0,1612,w,30),L10n.T("RimTube.SubtitleSizeHint"));
                UiTheme.Label(new Rect(0,1660,w,100),LocalizedText.T("RimTube.AllFourThemesShareTheSameControlsAutomatic"),true);
                if(Ui.Click(new Rect(0,1770,w/2-4,30),L10n.T(App.UpdatingResolver?"RimTube.UpdateWorking":"RimTube.UpdateResolver")))App.QueueUi(()=>App.UpdateResolver());
                if(Ui.Click(new Rect(w/2+4,1770,w/2-4,30),L10n.T("RimTube.RevertResolver")))App.QueueUi(()=>App.UpdateResolver(true));
                UiTheme.Label(new Rect(0,1810,w,80),App.ResolverUpdateInfo,true);
                UiTheme.Label(new Rect(0,1896,w,100),L10n.T("RimTube.UpdateResolverHint"),true);
                if(changed) State.Changed();
            }
            finally{Widgets.EndScrollView();}
        }
        private static bool Check(float y,float w,string label,ref bool value)
        {bool before=value;Widgets.CheckboxLabeled(new Rect(0,y,w,28),label,ref value);return before!=value;}
        private static string PathField(float y,float w,string label,string value)
        {UiTheme.Label(new Rect(0,y+3,98,28),label);return Widgets.TextField(new Rect(104,y,w-104,30),value);}
    }
}
