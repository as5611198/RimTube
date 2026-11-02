using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimTube.Core;
using UnityEngine;
using Verse;

namespace RimTube
{
    // Metadata only. The window polls task completion in WindowUpdate, never in OnGUI.
    internal sealed class CaptionPreview : IDisposable
    {
        private readonly RimTubeApp app;
        private readonly Library library;
        private readonly AudioPlayer player;
        private readonly string url;
        private readonly MediaChoice choice;
        private readonly CancellationTokenSource cancel=new CancellationTokenSource();
        private Task<Track> request;
        public Track Result {get;private set;}
        public string Status {get;private set;}=L10n.T("RimTube.CaptionLoading");
        public CaptionPreview(RimTubeApp app,string url,MediaChoice choice){this.app=app;library=app.Library;player=app.Player;this.url=url;this.choice=choice.Copy();}
        public void Start()
        {
            if(request!=null)return;
            try{request=player.InspectYouTubeAsync(url,choice,cancel.Token);}
            catch{Status=L10n.T("RimTube.CaptionLookupFailed");}
        }
        public void Tick()
        {
            if(request==null||!request.IsCompleted)return;
            var completed=request;request=null;
            if(completed.IsFaulted){var ignored=completed.Exception;Status=L10n.T("RimTube.CaptionRetry");return;}
            if(completed.IsCanceled)return;
            if(!ReferenceEquals(app,RimTubeMod.App)||!ReferenceEquals(library,app.Library)||!ReferenceEquals(player,app.Player)){Status=L10n.T("RimTube.PlayerChanged");return;}
            Result=completed.Result;
            var manual=Languages(Result,"media.subtitleManualLanguages");var automatic=Languages(Result,"media.subtitleAutoLanguages");
            Status=L10n.F("RimTube.CaptionAvailable",manual.Length,automatic.Length);
        }
        public static string[] Languages(Track track,string key)
        {string value;return track!=null&&track.Metadata.TryGetValue(key,out value)?value.Split(',').Select(SubtitleLanguage.Normalize).Where(s=>s!="").Distinct().ToArray():new string[0];}
        public void Dispose()
        {
            cancel.Cancel();
            if(request!=null)request.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            cancel.Dispose();
        }
    }
    public sealed class SubtitleOptionsWindow : Window
    {
        private readonly RimTubeApp app;
        private readonly Track track;
        private readonly CaptionPreview preview;
        private readonly List<Tuple<string,bool,string>> choices=new List<Tuple<string,bool,string>>();
        private readonly HashSet<string> selectedLanguages=new HashSet<string>(StringComparer.Ordinal);
        private Track shown;
        private Vector2 scroll;
        public override Vector2 InitialSize=>new Vector2(490,550);
        public SubtitleOptionsWindow(Track track)
        {
            app=RimTubeMod.App;this.track=track;
            var choice=MediaChoice.From(track);choice.Delivery=MediaDelivery.Stream;choice.Video=true;
            preview=new CaptionPreview(app,track.Url,choice);
            doCloseX=true;draggable=true;forcePause=false;closeOnAccept=false;
            Build(track);
        }
        public override void PreOpen(){base.PreOpen();preview.Start();}
        public override void WindowUpdate()
        {
            base.WindowUpdate();preview.Tick();
            if(!ReferenceEquals(app,RimTubeMod.App)){Close(false);return;}
            if(preview.Result!=null&&!ReferenceEquals(shown,preview.Result))Build(preview.Result);
        }
        private void Build(Track source)
        {
            shown=source;choices.Clear();selectedLanguages.Clear();
            string selected=MediaChoice.From(track).Subtitle;
            var manual=CaptionPreview.Languages(source,"media.subtitleManualLanguages");
            var automatic=CaptionPreview.Languages(source,"media.subtitleAutoLanguages");
            foreach(string code in manual.OrderBy(Priority).ThenBy(s=>s,StringComparer.Ordinal))choices.Add(Tuple.Create(code,false,code+L10n.T("RimTube.ManualCaption")));
            foreach(string code in automatic.Where(a=>!manual.Any(m=>SubtitleLanguage.SameScript(m,a)||a.IndexOf('-')<0&&m.StartsWith(a+"-",StringComparison.Ordinal))).OrderBy(Priority).ThenBy(s=>s,StringComparer.Ordinal))choices.Add(Tuple.Create(code,true,code+L10n.T("RimTube.AutoCaption")));
            if(manual.Length==0&&automatic.Length==0)
                foreach(string code in CaptionPreview.Languages(source,"media.subtitleLanguages"))choices.Add(Tuple.Create(code,MediaChoice.From(track).AutoSubtitles,code+L10n.T("RimTube.SourceCaption")));
            foreach(var item in choices)if(SubtitleLanguage.SameScript(selected,item.Item1))selectedLanguages.Add(item.Item1);
        }
        private static int Priority(string code)=>code=="zh-TW"||code=="zh-Hant"?0:code=="en"||code=="en-US"?1:2;
        public override void DoWindowContents(Rect rect)
        {
            UiTheme.Label(new Rect(0,0,rect.width-30,30),L10n.T("RimTube.SubtitleOptions"));
            UiTheme.OneLine(new Rect(0,34,rect.width,26),track.Title);
            UiTheme.Label(new Rect(0,68,rect.width,42),preview.Status,true);
            if(Ui.Click(new Rect(0,112,rect.width,30),L10n.F("RimTube.SubtitleSize",(int)Math.Round(app.Interface.Prefs.SubtitleScale*100))))
                Ui.Menu(new[]{1f,1.25f,1.5f,1.75f,2f,2.5f,3f},value=>(int)Math.Round(value*100)+"%",app.Interface.SetSubtitleScale);
            TooltipHandler.TipRegion(new Rect(0,112,rect.width,30),L10n.T("RimTube.SubtitleSizeHint"));
            if(Ui.Click(new Rect(0,150,rect.width,30),L10n.T("RimTube.SubtitleOff"),!app.Player.SubtitlesVisible))
            {if(app.Player.CurrentId==track.Id)app.Player.ShowSubtitles(false);Close(false);}
            var bounds=new Rect(0,190,rect.width,Math.Max(60,rect.height-250));
            Widgets.BeginScrollView(bounds,ref scroll,new Rect(0,0,bounds.width-20,Math.Max(bounds.height,choices.Count*36)));
            try
            {
                var range=UiGeometry.VisibleRows(choices.Count,scroll.y,bounds.height,36);
                for(int i=range.First;i<range.End;i++)
                {
                    var item=choices[i];
                    if(Ui.Click(new Rect(0,i*36,bounds.width-24,30),item.Item3,app.Player.SubtitlesVisible&&selectedLanguages.Contains(item.Item1)))
                        app.QueueUi(()=>{app.ChangeVideoSubtitles(track.Id,item.Item1,item.Item2);Close(false);});
                }
                if(choices.Count==0&&preview.Result!=null)UiTheme.Label(new Rect(4,4,bounds.width-30,60),L10n.T("RimTube.NoCaptions"),true);
            }
            finally{Widgets.EndScrollView();}
            UiTheme.Label(new Rect(0,rect.height-50,rect.width,48),L10n.T("RimTube.CaptionFooter"),true);
        }
        public override void PostClose(){preview.Dispose();base.PostClose();}
    }
}
