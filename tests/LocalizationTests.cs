using System.Reflection;
using RimTube.Core;

internal static class LocalizationTests
{
    public static void Run(Action<bool,string> check,string root)
    {
        var type=typeof(Library).Assembly.GetType("RimTube.Core.LocalizedText");
        check(type!=null,"core localization is available without Verse or Unity");
        var publish=type.GetMethod("Publish");var lookup=type.GetMethod("T");
        Action<IDictionary<string,string>> set=values=>publish.Invoke(null,new object[]{values});
        Func<string,string> text=key=>(string)lookup.Invoke(null,new object[]{key});
        try
        {
            var values=new Dictionary<string,string>{{"RimTube.Play","Play"}};
            set(values);values["RimTube.Play"]="mutated";
            check(text("RimTube.Play")=="Play","language publication snapshots input for worker-safe lookup");
            check(text("RimTube.NonexistentKey")=="RimTube.NonexistentKey","unknown localization key has visible fallback");
            check(text("RimTube.Pause")=="暫停","missing locale entry falls back to built-in Traditional Chinese");
            set(new Dictionary<string,string>{{"RimTube.Play","播放"}});
            check(text("RimTube.Play")=="播放","language switch replaces previously cached translations");
            var resource=System.Xml.Linq.XDocument.Load(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../mod/Common/Languages/English/Keyed/RimTube.xml")));
            var english=resource.Root.Elements().ToDictionary(e=>e.Name.LocalName,e=>e.Value);
            set(english);
            var row=new UiRow(new Track{Id="local",Title="保留自訂曲名",SourceType="local"},true,true,false);
            check(row.Title=="保留自訂曲名"&&row.Detail=="Local import · Music · Saved music · --:--","catalog worker localizes built-in labels while retaining track titles");
            foreach(BackendState state in Enum.GetValues<BackendState>())
                check(!LocalizedText.BackendName(state).StartsWith("RimTube.")&&!LocalizedText.BackendName(state).Any(c=>c>=0x4e00&&c<=0x9fff),"backend state localized: "+state);
            check(LocalizedText.F("RimTube.BatchDownloadResult",2,3,4)=="Added: 2; already saved: 3; queued: 4. Saved copies will be preferred.","download summary uses one complete format template");
            check(LocalizedText.Message("已保存，略過重複下載")=="Already saved; duplicate download skipped","stored job diagnostics follow the current locale without rewriting files");
            try{new MediaChoice{Height=1}.Validate();check(false,"invalid quality must fail");}
            catch(ArgumentException e){check(e.Message==english["RimTube.TheQualityLimitMustBeBetween144pAnd"],"Core validation errors use the published English resources");}
            check(ToolDependency.CheckAsync("","mpv",CancellationToken.None).GetAwaiter().GetResult().Message=="Could not find mpv. Set the full tool path.","background dependency checks use a complete English template");
            string name="我的歌單 My playlist";
            using(var lib=new Library(Path.Combine(root,"locale-data")))
            {
                lib.Lists.Add(new Playlist{Id="custom",Name=name});lib.Save();
                check(LocalizedText.PlaylistName(lib.FindList("general"))=="Everyday music","default playlist name follows English locale");
                check(LocalizedText.PlaylistName(lib.FindList("custom"))==name,"custom playlist display retains mixed-language name");
                set(new Dictionary<string,string>{{"RimTube.Play","Play"}});
                check(lib.FindList("custom").Name==name,"language switch preserves user playlist names");
            }
        }
        finally{set(null);}
    }
}
