using System.Diagnostics;
using RimTube.Core;

internal static class UiTests
{
    public static void Run(Action<bool,string> check, string root)
    {
        using var lib = new Library(Path.Combine(root,"ui"));
        TextFit(check);
        VideoTimeline(check);
        var local = new Track { Id="local", Title="Alpha", SourceType="local", FileName="alpha.ogg" };
        File.WriteAllText(lib.AudioPath(local),"fixture");
        var yt = new Track { Id="yt_test", Title="Beta", SourceType="youtube", FileName="beta.ogg" };
        yt.Metadata["saved.audio"]="beta.ogg"; File.WriteAllText(lib.AudioPath(yt),"fixture");
        yt.Metadata["media.video"]="True"; yt.Metadata["media.live"]="True";
        var game = new Track { Id="game_test", Title="Battle", SourceType="game", Availability=SourceAvailability.Available };
        game.Metadata["game.kind"]="Core"; game.Metadata["game.source"]="RimWorld";
        var missing = new Track { Id="game_missing", Title="Gone", SourceType="game", Availability=SourceAvailability.Missing };
        missing.Metadata["game.kind"]="Mod"; missing.Metadata["game.source"]="Old mod";
        lib.AddTo(lib.Lists[0],local); lib.AddTo(lib.Lists[0],yt); lib.AddTo(lib.Lists[0],game);
        var inputs=UiCatalog.Capture(lib,new[]{game,missing});
        var catalog=UiCatalog.Build(lib,inputs);
        var query=new UiQuery();
        check(query.Select(catalog).Count==3,"library view excludes unimported game catalog rows");
        query.Storage=UiStorage.Saved;
        check(query.Select(catalog).Select(r=>r.Id).SequenceEqual(new[]{"local","yt_test"}),"saved filter excludes playable game assets");
        query.Storage=UiStorage.Any; query.Source="Core";
        check(query.Select(catalog).Single().Id=="game_test","source filter distinguishes Core music");
        query.GameCatalog=true; query.Source="Mod";
        check(query.Select(catalog).Single().Missing,"removed game source is retained as missing");
        query.GameCatalog=false; query.Source="YouTube";query.Media=UiMedia.Live;query.Search="BETA";
        check(query.Select(catalog).Single().Id=="yt_test","search source and live filters intersect without case sensitivity");
        var cached=query.Select(catalog); long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++) query.Select(catalog);
        check(ReferenceEquals(cached,query.Select(catalog))&&GC.GetAllocatedBytesForCurrentThread()-before<1024,"unchanged queries reuse results without per-call allocations");
        query.Search="no match";check(query.Select(catalog).Count==0,"search invalidation drops previous results");
        query.Search="";query.Source="";query.Media=UiMedia.Any;query.PlaylistIds=new[]{"yt_test","local"};
        check(query.Select(catalog).Select(r=>r.Id).SequenceEqual(new[]{"yt_test","local"}),"filtered playlist keeps user ordering");
        File.Delete(lib.AudioPath(local));
        check(catalog.Find("local").SavedAudio,"render snapshot does not re-probe files on reads");
        var rebuilt=UiCatalog.Build(lib,UiCatalog.Capture(lib,new[]{game,missing}));
        check(!rebuilt.Find("local").SavedAudio,"explicit refresh detects files removed outside the app");
        check(catalog.Find("game_test").IsGame&&!catalog.Find("game_test").SavedAudio&&!catalog.Find("game_test").SavedVideo,"native game music never offers saved media ownership");
        local.Title="changed after capture";
        check(catalog.Find("local").Title=="Alpha","catalog snapshots do not share mutable track display fields");
        var invalidDuration=new Track {Id="game_invalid_duration",Title="Unknown length",SourceType="game",Duration=double.NaN};
        var unusual=UiCatalog.Build(lib,UiCatalog.Capture(lib,new[]{invalidDuration}));
        check(unusual.Find(invalidDuration.Id).Detail.EndsWith("--:--"),"unknown duration cannot break the entire catalog build");
        var range=UiGeometry.VisibleRows(5000,620,310,62);
        check(range.First==10&&range.End==15,"virtualized list computes exact visible range without scanning rows");
        range=UiGeometry.VisibleRows(3,99999,310,62);check(range.First==3&&range.End==3,"stale scroll offsets produce a safe empty range");
        var box=UiGeometry.Clamp(new UiBox(1800,1000,800,500),640,480,320,180);
        check(box.X==0&&box.Y==0&&box.Width==640&&box.Height==480,"resolution shrink keeps the full window reachable");
        box=UiGeometry.Snap(new UiBox(6,9,360,80),1920,1080,16);
        check(box.X==0&&box.Y==0,"near-edge mini player snaps within threshold");
        var legacy=new Settings { Volume=.31f,MiniX=55,MiniY=88,ShowMini=false };
        string prefsPath=Path.Combine(root,"interface","ui.xml");
        var prefs=UiPreferences.Load(prefsPath,legacy);
        check(prefs.NewMediaChoice().Subtitle=="off"&&!prefs.NewMediaChoice().AutoSubtitles,"new media defaults never select captions automatically");
        check(!prefs.ShowMini&&prefs.Volume==.31f&&prefs.Mini.X==55,"first load migrates existing mini and volume preferences");
        prefs.Theme="medieval";prefs.Muted=true;prefs.MiniLocked=true;prefs.LastList="combat";prefs.Video=new UiBox(25,30,700,450);prefs.DefaultSubtitle="zh-TW";prefs.DefaultAutoSubtitles=true;prefs.Save(prefsPath);
        var restored=UiPreferences.Load(prefsPath,new Settings());
        check(restored.Theme=="medieval"&&restored.Muted&&restored.MiniLocked&&restored.Video.Width==700&&restored.LastList=="combat","UI preferences persist separately from library options");
        check(restored.NewMediaChoice().Subtitle=="zh-TW"&&restored.NewMediaChoice().AutoSubtitles,"caption defaults round-trip into new media choices without changing existing tracks");
        var shortcut=prefs.Shortcuts.Single(s=>s.Action=="play");
        check(shortcut.Matches(32,true,true,false)&&!shortcut.Matches(32,false,true,false)&&!shortcut.Matches(32,true,true,true),"shortcuts require exact modifiers and do not hijack plain game keys");
        shortcut.Key=112;shortcut.Alt=false;prefs.Save(prefsPath);
        check(UiPreferences.Load(prefsPath,null).Shortcuts.Single(s=>s.Action=="play").Matches(112,true,false,false),"custom shortcut combinations survive restart");
        File.WriteAllText(prefsPath,"broken");prefs=UiPreferences.Load(prefsPath,legacy);
        check(prefs.ReadOnly&&prefs.Warning!="","damaged UI preferences are protected with a visible warning");
        prefs.Save(prefsPath);check(File.ReadAllText(prefsPath)=="broken","autosave never overwrites damaged UI preferences");
        long revision=lib.Revision;lib.Save();check(lib.Revision>revision,"library mutations invalidate UI snapshots when saved");
        var firstColony=new Settings {GeneralList="colony-a",CombatList="battle-a",Battle=BattleMode.Custom,PauseWithGame=true,PauseCoveredVideo=false};
        var colony=ColonyPlaybackPreferences.Capture(firstColony);
        firstColony.GeneralList="changed";
        var secondColony=new Settings {Volume=.2f,Mode=PlayMode.Shuffle,Backend="native-local"};
        colony.Apply(secondColony);
        check(secondColony.GeneralList=="colony-a"&&secondColony.CombatList=="battle-a"&&secondColony.Battle==BattleMode.Custom&&secondColony.PauseWithGame&&!secondColony.PauseCoveredVideo,"colony settings restore independent playback roles and video cover behavior");
        check(secondColony.Volume==.2f&&secondColony.Backend=="native-local"&&secondColony.Mode==PlayMode.Shuffle,"colony restoration preserves global volume backend and playback mode");
        lib.Options.PauseCoveredVideo=true;lib.Save();
        using(var explicitChoice=new Library(lib.Root))
            check(explicitChoice.Options.PauseCoveredVideo,"existing explicit automatic-pause preference is preserved");
        var legacyState=System.Xml.Linq.XDocument.Load(lib.StateFile);
        legacyState.Root.Element("Settings").Attribute("pauseCoveredVideo").Remove();legacyState.Save(lib.StateFile);
        using(var legacyLibrary=new Library(lib.Root))
        {
            var freshColony=ColonyPlaybackPreferences.Capture(legacyLibrary.Options);
            var restoredColony=new Settings {PauseCoveredVideo=true};freshColony.Apply(restoredColony);
            check(!restoredColony.PauseCoveredVideo&&legacyLibrary.Tracks.Count==lib.Tracks.Count,"legacy library without cover preference keeps continuous playback when captured for a colony");
        }
        Benchmark(check,lib,root);
    }

    private static void Benchmark(Action<bool,string> check,Library lib,string root)
    {
        foreach(int size in new[]{500,5000})
        {
            var songs=Enumerable.Range(0,size).Select(i=>new Track {Id="game_"+i,Title="Song "+i,SourceType="game",Availability=SourceAvailability.Available}).ToArray();
            var catalog=UiCatalog.Build(lib,UiCatalog.Capture(lib,songs));
            var query=new UiQuery {GameCatalog=true};query.Select(catalog);
            for(int i=0;i<100000;i++) {query.Select(catalog);UiGeometry.VisibleRows(size,620,310,62);}
            var timer=new Stopwatch();long bytes=GC.GetAllocatedBytesForCurrentThread();timer.Start();
            for(int i=0;i<10000;i++) {query.Select(catalog);UiGeometry.VisibleRows(size,620,310,62);}
            timer.Stop();bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
            check(bytes<4096,"warm "+size+"-row queries have bounded allocations independent of library size");
            Console.WriteLine("UI-BENCH size="+size+" reads=10000 ms="+timer.Elapsed.TotalMilliseconds.ToString("F3")+" allocated="+bytes);
        }
    }
    private static void VideoTimeline(Action<bool,string> check)
    {
        var type=typeof(UiGeometry).Assembly.GetType("RimTube.Core.VideoTimeline");
        check(type!=null,"video timeline supports previewing a drag before sending a seek");
        dynamic seek=Activator.CreateInstance(type);
        check(!(bool)seek.Begin(.5),"unprepared video timeline cannot begin a seek");
        seek.Update("track-a","session-a",true,false,12d,120d);
        check((bool)seek.Begin(.25)&&(bool)seek.IsDragging&&(double)seek.DisplayPosition==30d,"timeline drag previews a target while playback position continues independently");
        seek.Update("track-a","session-a",true,false,13d,120d);
        check((double)seek.DisplayPosition==30d,"incoming progress does not overwrite the user's drag preview");
        seek.Move(2d);check((double)seek.DisplayPosition==120d,"dragging beyond the video bar clamps to its end");
        seek.Move(-1d);check((double)seek.DisplayPosition==0d,"dragging beyond the start clamps to zero");
        seek.Move(.6d);double target;
        check((bool)seek.Commit(out target)&&target==72d&&!(bool)seek.IsDragging,"release commits exactly the chosen preview position");
        check(!(bool)seek.Commit(out target),"repeated mouse-up events do not send duplicate seeks");
        seek.Begin(.3d);seek.Update("track-b","session-b",true,false,0d,120d);
        check(!(bool)seek.Commit(out target),"switching tracks cancels an old drag instead of seeking the new song");
        seek.Begin(.3d);seek.Update("track-b","session-c",true,false,0d,120d);
        check(!(bool)seek.Commit(out target),"quality or subtitle reload cancels a drag belonging to the previous session");
        seek.Update("live","live-session",true,true,50d,120d);
        check(!(bool)seek.Enabled&&!(bool)seek.Begin(.5d),"live video never presents a seekable VOD bar");
        foreach(double duration in new[]{0d,double.NaN,double.PositiveInfinity})
        {seek.Update("track-b","session-c",true,false,0d,duration);check(!(bool)seek.Enabled,"unknown or invalid duration disables timeline seeking");}
        seek.Update("track-b","session-c",true,false,10d,120d);seek.Begin(.4d);seek.Cancel();
        check(!(bool)seek.Commit(out target),"escape or closing the window cancels preview without a seek");
        seek.Update("track-b","session-c",true,false,10d,120d);
        // Use a typed delegate so reflection/dynamic binder allocations are excluded.
        var update=(Action<string,string,bool,bool,double,double>)type.GetMethod("Update").CreateDelegate(typeof(Action<string,string,bool,bool,double,double>),seek);
        for(int i=0;i<100;i++)update("track-b","session-c",true,false,10,120);
        long bytes=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++)update("track-b","session-c",true,false,10,120);
        check(GC.GetAllocatedBytesForCurrentThread()-bytes<1024,"video progress snapshot updates do not allocate per frame");
    }
    private static void TextFit(Action<bool,string> check)
    {
        var type=typeof(UiGeometry).Assembly.GetType("RimTube.Core.UiTextFitter");
        check(type!=null,"text fitting prevents centered names from clipping their first characters");
        dynamic fit=Activator.CreateInstance(type,new object[]{64});
        int measured=0;
        Func<string,float> width=s=>{measured++;return System.Globalization.StringInfo.ParseCombiningCharacters(s).Length*10;};
        string shortLabel=fit.Fit("上一首",48f,1,width);
        check(shortLabel=="上一首","narrow transport buttons retain all three CJK characters");
        string longLabel=fit.Fit("原版 RimWorld 【自動】",80f,1,width);
        check(longLabel=="原版 RimW…"&&width(longLabel)<=80,"long playlist names preserve their beginning and end with an ellipsis");
        string emoji=fit.Fit("A😀e\u0301BCDEF",45f,1,width);
        check(emoji=="A😀e\u0301…","truncation preserves surrogate pairs and combining marks");
        check((string)fit.Fit("字幕",5f,1,width)=="","tiny bounds do not draw a partially clipped glyph or ellipsis");
        int before=measured;
        for(int i=0;i<1000;i++)fit.Fit("上一首",48f,1,width);
        check(measured==before,"unchanged text and bounds do not repeat font measurement per OnGUI event");
        string wider=fit.Fit("原版 RimWorld 【自動】",200f,1,width);
        check(wider=="原版 RimWorld 【自動】","resizing invalidates truncation and restores the complete title");
        for(int i=0;i<500;i++)fit.Fit("track "+i,48f,1,width);
        check((int)fit.Count<=64,"text measurement cache stays bounded across large music libraries");
    }
}
