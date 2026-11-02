using RimTube.Sources;
using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using RimTube.Core;

internal static class ModernTests
{
    static Action<bool, string> Check;
    static readonly CancellationToken None = CancellationToken.None;
    public static void Wave(string path, int duration = 30)
    {
        using var w = new BinaryWriter(File.Create(path)); int rate = 24000, count = rate * duration;
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + count * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(count * 2);
        for (int i = 0; i < count; i++) w.Write((short)(Math.Sin(i * Math.PI * 2 * 440 / rate) * 1000));
    }
    public static void Run(Action<bool, string> check, string root, string[] args)
    {
        Check = check;
        string dir = Path.Combine(root, "modern"); var lib = new Library(dir);
        var a = new Track { Id = "local_a", Title = "甲", FileName = "a.wav", SourceType = "local" };
        var b = new Track { Id = "local_b", Title = "乙", FileName = "b.wav", SourceType = "local" };
        lib.AddTo(lib.Lists[0], a); lib.AddTo(lib.Lists[1], b); Wave(lib.AudioPath(a)); Wave(lib.AudioPath(b));
        a.Category = "測試分類"; a.Metadata["artist"] = "RimTube fixture"; lib.Save();
        var roundtrip = new Library(dir);
        Check(roundtrip.FindTrack(a.Id).Category == a.Category && roundtrip.FindTrack(a.Id).Metadata["artist"] == "RimTube fixture", "custom categories and metadata survive restart");
        var old = XDocument.Load(lib.StateFile); old.Root.SetAttributeValue("schema", "1");
        foreach (var e in old.Root.Element("Tracks").Elements())
        { e.Attribute("sourceType")?.Remove(); e.Attribute("sourceId")?.Remove(); e.Attribute("availability")?.Remove(); e.Attribute("category")?.Remove(); e.Element("Metadata")?.Remove(); }
        old.Save(lib.StateFile);
        var migrated = new Library(dir); Check(migrated.MigratedFromV1 && migrated.IsReady(a.Id) && migrated.FindTrack(a.Id).SourceType == "local", "real v1 fields migrate without losing local audio or playlists");
        var localInfo = new LocalFileSource(lib).GetTrackAsync(lib.AudioPath(a), None).GetAwaiter().GetResult();
        Check(localInfo.Id.StartsWith("local_") && localInfo.SourceId == localInfo.Id && localInfo.Title == "a", "local source produces usable stable track identity");
        migrated.Save(); Check((int)XDocument.Load(lib.StateFile).Root.Attribute("schema") == 2, "migration saves versioned schema 2");
        var yt = new Track { Id = "yt_BaW_jenozKc", FileName = "old.ogg" }; lib.AddTo(lib.Lists[0], yt); File.WriteAllText(lib.AudioPath(yt), "legacy");
        Check(!lib.IsReady(yt.Id) && File.Exists(lib.AudioPath(yt)), "old YouTube files preserved without being treated as authorized playback");
        lib.Lists[0].SourceTracks.Add(a.Id);
        lib.ApplySync(lib.Lists[0], Library.Diff(lib.Lists[0], Array.Empty<string>()), true, true, true, false);
        Check(lib.Lists[0].Tracks.Contains(a.Id) && lib.Lists[0].SyncStatus == SyncState.Partial, "partial sync cannot remove source tracks even when removal requested");
        var registry = new SourceRegistry(); registry.Register(new LocalFileSource(lib));
        var youtube = new YouTubeSourceAdapter(""); registry.Register(youtube);
        var identity = YouTubeSourceAdapter.Identify("https://youtu.be/BaW_jenozKc");
        Check(identity.SourceId == "BaW_jenozKc" && (youtube.Capabilities & SourceCapabilities.Sessions) != 0, "YouTube identity remains stable with optional streaming capability");
        try { youtube.CreateSessionAsync(identity, None).GetAwaiter().GetResult(); Check(false, "missing YT resolver"); }
        catch (MediaException e) { Check(e.Code == SourceErrorCode.MissingTool, "YouTube sessions report missing optional resolver"); }
        Check(!youtube.CheckDependencyAsync(None).GetAwaiter().GetResult().Available, "missing optional tool is a recoverable result");
        var pages = new PagedFixture();
        var complete = PlaylistReader.ReadAsync(pages, "owned-fixture", None).GetAwaiter().GetResult();
        Check(complete.Complete && complete.Tracks.Select(t => t.Id).SequenceEqual(new[] { "one", "two" }), "paged source collects ordered unique IDs before sync preview");
        pages.FailSecond = true;
        var partial = PlaylistReader.ReadAsync(pages, "owned-fixture", None).GetAwaiter().GetResult();
        Check(!partial.Complete && partial.Tracks.Count == 1, "page failure retains partial preview without claiming complete snapshot");
        pages.FailSecond = false; pages.Loop = true;
        Check(!PlaylistReader.ReadAsync(pages, "owned-fixture", None).GetAwaiter().GetResult().Complete, "repeating pagination cursor terminates without deleting library data");
        using (var backend = new FakeBackend())
        using (var coordinator = new PlaybackCoordinator(lib, registry, backend, value => backend.Owned = value))
        {
            var env = new PlaybackEnvironment { GameActive = true, GameIdentity = new object(), MasterVolume = .5 };
            void Until(Func<bool> done)
            {
                var time = Stopwatch.StartNew();
                while (!done() && time.Elapsed.TotalSeconds < 4) { env.Time += .01; coordinator.Tick(env); Thread.Sleep(2); }
                Check(done(), "coordinator transition: " + backend.Snapshot.State + "/" + coordinator.CurrentId);
            }
            coordinator.Play(a.Id); Until(() => backend.Snapshot.State == BackendState.Playing);
            Check(backend.Owned && Math.Abs(backend.Snapshot.Volume - lib.Options.Volume * .5) < .001, "BGM is acquired before play and game master volume is propagated");
            backend.Position(9); lib.Options.Battle = BattleMode.Custom; env.Danger = true;
            Until(() => coordinator.CurrentId == b.Id && backend.Snapshot.State == BackendState.Playing);
            env.Danger = false; env.Time += 4;
            Until(() => coordinator.CurrentId == a.Id && backend.Snapshot.State == BackendState.Playing);
            Check(coordinator.Position >= 9, "battle exit restores original song and position");
            coordinator.Pause(); Until(() => (backend.Snapshot.State == BackendState.Paused || backend.Snapshot.State == BackendState.Stopped) && backend.Owned);
            env.Danger = true; coordinator.Tick(env); Check(coordinator.Intent == PlayIntent.Paused && backend.Owned, "battle cannot override manual pause or release the music manager");
            coordinator.Play(); Until(() => backend.Snapshot.State == BackendState.Playing);
            env.GameIdentity = new object(); Until(() => coordinator.Intent == PlayIntent.Stopped && backend.Snapshot.State == BackendState.Stopped);
            coordinator.Play(a.Id); Until(() => coordinator.IsLoading); coordinator.Stop();
            Until(() => backend.Snapshot.State == BackendState.Stopped && !coordinator.IsLoading);
            for (int i = 0; i < 20; i++) coordinator.Tick(env);
            Check(coordinator.Intent == PlayIntent.Stopped && !backend.Owned, "stop during preparation cannot revive playback");
        }
        if (args.Contains("--mpv")) TestMpv(lib, a, args.Contains("--soak")).GetAwaiter().GetResult();
        TestPlaylistPlayback(root);
        using (var remoteBackend = new FakeBackend())
        {
            registry.Register(new SessionFixture());
            var remote = new Track { Id = "remote-fixture", SourceType = "fixture" }; lib.Tracks[remote.Id] = remote;
            lib.Lists[1].Tracks.Clear(); lib.Lists[1].Tracks.Add(remote.Id); lib.Options.Battle = BattleMode.Custom;
            using var coordinator = new PlaybackCoordinator(lib, registry, remoteBackend, own => remoteBackend.Owned = own);
            var env = new PlaybackEnvironment { GameActive = true, GameIdentity = new object(), Danger = true };
            coordinator.Play(a.Id); var timer = Stopwatch.StartNew();
            while ((coordinator.CurrentId != remote.Id || remoteBackend.Snapshot.State != BackendState.Playing) && timer.Elapsed.TotalSeconds < 4)
            { coordinator.Tick(env); Thread.Sleep(5); }
            Check(coordinator.CurrentId == remote.Id && remoteBackend.Snapshot.State == BackendState.Playing, "combat coordinator accepts source sessions independently of local audio files");
        }
    }
    static void TestPlaylistPlayback(string root)
    {
        using var library = new Library(Path.Combine(root, "playlist-playback"));
        var source = new YouTubeSourceAdapter(Environment.ProcessPath);
        var preview = PlaylistReader.ReadAsync(source, "https://www.youtube.com/playlist?list=PLfixtureComplete", None).GetAwaiter().GetResult();
        var list = PlaylistImport.Apply(library, null, "https://www.youtube.com/playlist?list=PLfixtureComplete", preview);
        var registry = new SourceRegistry(); registry.Register(source);
        using var backend = new FakeBackend();
        using var player = new PlaybackCoordinator(library, registry, backend, own => backend.Owned = own);
        var env = new PlaybackEnvironment { GameActive = true, GameIdentity = new object() };
        void Until(Func<bool> done)
        {
            var watch = Stopwatch.StartNew();
            while (!done() && watch.Elapsed.TotalSeconds < 5) { player.Tick(env); Thread.Sleep(5); }
            Check(done(), "playlist playback transition: " + player.CurrentId + "/" + player.Snapshot.State);
        }
        library.Options.Mode = PlayMode.Ordered;
        player.Play(list.Tracks[0], list.Id); Until(() => player.Snapshot.State == BackendState.Playing);
        backend.End(); Until(() => player.CurrentId == list.Tracks[1] && player.Snapshot.State == BackendState.Playing);
        Check(player.Intent == PlayIntent.Playing, "imported YouTube playlist advances automatically after EOF");
        var previousSession = player.Snapshot.SessionId;
        player.Next(); Until(() => player.CurrentId == list.Tracks[2] && player.Snapshot.State == BackendState.Playing && player.Snapshot.SessionId != previousSession);
        backend.End(); Until(() => player.Intent == PlayIntent.Stopped && !backend.Owned && player.Snapshot.State == BackendState.Stopped);
        Check(!backend.Owned, "ordered playlist releases BGM when final song ends");
        library.Options.Mode = PlayMode.RepeatAll;
        player.Play(list.Tracks[2], list.Id); Until(() => player.Snapshot.State == BackendState.Playing);
        backend.End(); Until(() => player.CurrentId == list.Tracks[0] && player.Snapshot.State == BackendState.Playing);
        Check(player.Intent == PlayIntent.Playing, "repeat-all wraps imported playlist to first song");
        library.Options.Mode = PlayMode.Shuffle;
        previousSession = player.Snapshot.SessionId;
        player.Next(); Until(() => player.CurrentId != list.Tracks[0] && player.Snapshot.State == BackendState.Playing && player.Snapshot.SessionId != previousSession);
        Check(list.Tracks.Contains(player.CurrentId), "shuffle chooses another imported playlist track");
    }
    public static void Experience(Action<bool,string> check,string root)
    {
        using var library=new Library(Path.Combine(root,"experience"));
        var alpha=new Track {Id="alpha",Title="Alpha",SourceType="fixture"};
        var beta=new Track {Id="beta",Title="Beta",SourceType="fixture"};
        var other=new Track {Id="other",Title="Other",SourceType="fixture"};
        library.Tracks[alpha.Id]=alpha; library.Tracks[beta.Id]=beta; library.AddTo(library.Lists[0],other);
        var registry=new SourceRegistry();registry.Register(new SessionFixture());
        using var backend=new FakeBackend();
        using var player=new PlaybackCoordinator(library,registry,backend,own=>backend.Owned=own);
        var env=new PlaybackEnvironment {GameActive=true,GameIdentity=new object()};
        void Until(Func<bool> done,string message)
        {
            var watch=Stopwatch.StartNew();
            while(!done()&&watch.Elapsed.TotalSeconds<4){player.Tick(env);Thread.Sleep(3);}
            check(done(),message);
        }
        player.Play(alpha.Id);Until(()=>player.Snapshot.State==BackendState.Playing,"library track starts independently of configured general list");
        backend.End();Until(()=>player.CurrentId==beta.Id&&player.Snapshot.State==BackendState.Playing,"library playback advances to next library title instead of configured playlist");
        var list=new Playlist {Id="explicit",Tracks=new List<string>{beta.Id,alpha.Id}};library.Lists.Add(list);
        string configured=library.Options.GeneralList;
        string before=player.Snapshot.SessionId;
        player.Play(beta.Id,list.Id);Until(()=>player.CurrentId==beta.Id&&player.Snapshot.State==BackendState.Playing&&player.Snapshot.SessionId!=before,"explicit list starts");
        check(library.Options.GeneralList==configured,"playing a list does not replace the configured general-music role");
        backend.End();Until(()=>player.CurrentId==alpha.Id&&player.Snapshot.State==BackendState.Playing,"explicit playlist still advances in its own order");
        backend.End();Until(()=>player.Intent==PlayIntent.Stopped&&player.Snapshot.State==BackendState.Stopped,"ordered list stops after final track");
        var covered=typeof(PlaybackEnvironment).GetField("VideoCovered");
        check(covered!=null,"coordinator accepts temporary video occlusion independently of manual intent");
        alpha.Metadata["media.video"]="True";
        player.Play(alpha.Id);Until(()=>player.Snapshot.State==BackendState.Playing,"video fixture starts");
        covered.SetValue(env,true);
        for(int i=0;i<20;i++){player.Tick(env);Thread.Sleep(3);}
        check(player.Snapshot.State==BackendState.Playing,"new library defaults keep covered video playing");
        library.Options.PauseCoveredVideo=true;
        covered.SetValue(env,true);Until(()=>player.Snapshot.State==BackendState.Paused,"covering video pauses backend");
        check(player.Intent==PlayIntent.Playing,"temporary cover does not change manual play intent");
        covered.SetValue(env,false);Until(()=>player.Snapshot.State==BackendState.Playing,"uncovering video resumes it");
        library.Options.PauseCoveredVideo=false;covered.SetValue(env,true);
        for(int i=0;i<20;i++){player.Tick(env);Thread.Sleep(3);}
        check(player.Snapshot.State==BackendState.Playing,"continue-video preference ignores temporary coverage");
        library.Save();using(var restored=new Library(library.Root)) check(!restored.Options.PauseCoveredVideo,"video coverage preference survives restart");
        library.Options.PauseCoveredVideo=true;
        covered.SetValue(env,true);Until(()=>player.Snapshot.State==BackendState.Paused,"cover can pause again");
        player.Pause();covered.SetValue(env,false);
        for(int i=0;i<30;i++){player.Tick(env);Thread.Sleep(3);}
        check(player.Intent==PlayIntent.Paused&&player.Snapshot.State==BackendState.Paused,"manual pause while covered prevents automatic resume");
        covered.SetValue(env,true);player.Stop();covered.SetValue(env,false);
        Until(()=>player.Intent==PlayIntent.Stopped&&player.Snapshot.State==BackendState.Stopped,"stop while covered cannot revive playback");
        covered.SetValue(env,true);player.Play(beta.Id);Until(()=>player.Snapshot.State==BackendState.Playing,"audio-only playback ignores video occlusion");
        backend.End();Until(()=>player.CurrentId==other.Id&&player.Snapshot.State==BackendState.Playing,"music library advances to the next audio track");
        backend.End();Until(()=>player.Intent==PlayIntent.Stopped&&player.Snapshot.State==BackendState.Stopped,"music library playback ends without selecting a video");
        covered.SetValue(env,false);player.Play(alpha.Id);Until(()=>player.Snapshot.State==BackendState.Playing,"video library track begins");
        backend.End();Until(()=>player.Intent==PlayIntent.Stopped&&player.Snapshot.State==BackendState.Stopped,"video library playback never advances into music entries");
    }

    public static void BgmOwnership(Action<bool,string> check,string root)
    {
        using var library=new Library(Path.Combine(root,"bgm-ownership"));
        var a=new Track {Id="yt_bgm1",SourceType="fixture",Title="A"};
        var b=new Track {Id="yt_bgm2",SourceType="fixture",Title="B"};
        new MediaChoice {Video=true}.Store(a);new MediaChoice {Video=true}.Store(b);
        library.AddTo(library.Lists[0],a);library.AddTo(library.Lists[0],b);
        var source=new GatedSource();var registry=new SourceRegistry();registry.Register(source);
        using var backend=new FakeBackend();var changes=new List<bool>();
        using var player=new PlaybackCoordinator(library,registry,backend,own=>{backend.Owned=own;changes.Add(own);});
        var env=new PlaybackEnvironment {GameActive=true,GameIdentity=new object(),MasterVolume=1};
        void Until(Func<bool> done)
        {var watch=Stopwatch.StartNew();while(!done()&&watch.ElapsedMilliseconds<3000){player.Tick(env);Thread.Sleep(2);}check(done(),"BGM transition "+player.Snapshot.State);}
        void Pump(){for(int i=0;i<20;i++){player.Tick(env);Thread.Sleep(2);}}
        player.Play(a.Id,library.Lists[0].Id);player.Tick(env);
        check(backend.Owned,"BGM is acquired on first play intent before source resolution or backend load");
        var removable=typeof(PlaybackCoordinator).GetMethod("CanRemoveMedia");
        check(removable!=null,"media cleanup can wait for the coordinator to release playback handles");
        bool CanRemove(string id)=>(bool)removable.Invoke(player,new object[]{id});
        check(!CanRemove(a.Id),"media cleanup is blocked during in-flight source preparation");
        Until(()=>source.Entered.Task.IsCompleted);Pump();
        check(backend.Owned&&!changes.Contains(false),"slow source resolution never gives vanilla BGM a gap");
        source.Release.TrySetResult(true);Until(()=>backend.Snapshot.State==BackendState.Playing);
        changes.Clear();player.Next();Until(()=>player.CurrentId==b.Id&&backend.Snapshot.State==BackendState.Playing);
        check(!changes.Contains(false),"internal stop between tracks retains BGM control");
        Pump();check(CanRemove(a.Id)&&!CanRemove(b.Id),"only an inactive track can have its files removed during playback");
        changes.Clear();string previous=player.Snapshot.SessionId;player.ChangeVideoQuality(b.Id,360);
        Until(()=>backend.Snapshot.State==BackendState.Playing&&player.Snapshot.SessionId!=previous);
        check(!changes.Contains(false),"quality reload retains BGM through stopping and preparing");
        player.Pause();Until(()=>player.Intent==PlayIntent.Paused&&(backend.Snapshot.State==BackendState.Paused||backend.Snapshot.State==BackendState.Stopped));Pump();
        check(backend.Owned,"manual pause keeps RimTube in charge until explicit stop");
        player.Play();Until(()=>backend.Snapshot.State==BackendState.Playing);
        library.Options.Battle=BattleMode.Vanilla;env.Danger=true;
        Until(()=>backend.Snapshot.State==BackendState.Paused&&!backend.Owned);
        check(player.Intent==PlayIntent.Playing,"explicit vanilla battle setting still hands BGM back without changing play intent");
        env.Danger=false;env.Time+=4;Until(()=>backend.Snapshot.State==BackendState.Playing);
        player.Stop();check(!CanRemove(b.Id),"stopping intent alone does not permit deletion before backend stop completes");
        Until(()=>!backend.Owned&&backend.Snapshot.State==BackendState.Stopped);Pump();
        check(CanRemove(b.Id),"cleanup is allowed once backend stop is fully drained");
        var begin=typeof(PlaybackCoordinator).GetMethod("BeginSourceLookup");
        var end=typeof(PlaybackCoordinator).GetMethod("EndSourceLookup");
        check(begin!=null&&end!=null,"autoplay metadata lookup can retain BGM before a track exists");
        var first=new object();var second=new object();
        begin.Invoke(player,new[]{first,env.GameIdentity});player.Tick(env);
        check(backend.Owned&&player.Intent==PlayIntent.Stopped,"autoplay lookup alone suppresses vanilla without starting a track");
        begin.Invoke(player,new[]{second,env.GameIdentity});end.Invoke(player,new[]{first});player.Tick(env);
        check(backend.Owned,"stale lookup completion cannot release a newer request");
        end.Invoke(player,new[]{second});player.Tick(env);check(!backend.Owned,"failed or canceled lookup returns BGM when no playback intent remains");
        begin.Invoke(player,new[]{first,env.GameIdentity});player.Tick(env);env.GameIdentity=new object();player.Tick(env);
        check(!backend.Owned,"changing games cancels pending lookup ownership");
        begin.Invoke(player,new[]{second,env.GameIdentity});player.Tick(env);player.Stop();Pump();
        check(!backend.Owned,"manual stop clears pending autoplay ownership");
        source.Fail=true;player.Play(a.Id,library.Lists[0].Id);
        Until(()=>player.Intent==PlayIntent.Stopped&&!backend.Owned);
        check(player.Error!="","exhausting failed sources releases BGM and retains an error");
        begin.Invoke(player,new[]{first,env.GameIdentity});player.Tick(env);env.GameActive=false;player.Tick(env);
        check(!backend.Owned,"leaving the game releases pending lookup ownership");
    }
    sealed class GatedSource : IMediaSource
    {
        public readonly TaskCompletionSource<bool> Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Fail;
        public string Id=>"fixture";
        public SourceCapabilities Capabilities=>SourceCapabilities.Sessions;
        public bool CanHandle(string s)=>false;
        public async Task<PlaybackSession> CreateSessionAsync(Track track,CancellationToken token)
        {Entered.TrySetResult(true);await Release.Task.WaitAsync(token);if(Fail)throw new MediaException(SourceErrorCode.NotFound,"fixture unavailable");return new PlaybackSession(track.Id,"https://example.invalid/video",false,true,true,video:true);}
        public Task<Track> GetTrackAsync(string s,CancellationToken t)=>throw new NotSupportedException();
        public Task<PlaylistPage> GetPlaylistPageAsync(string s,string c,int n,CancellationToken t)=>throw new NotSupportedException();
        public Task<DependencyInfo> CheckDependencyAsync(CancellationToken t)=>throw new NotSupportedException();
    }
    sealed class SessionFixture : IMediaSource
    {
        public string Id => "fixture";
        public SourceCapabilities Capabilities => SourceCapabilities.Sessions;
        public bool CanHandle(string s) => false;
        public Task<PlaybackSession> CreateSessionAsync(Track track, CancellationToken t) { t.ThrowIfCancellationRequested(); return Task.FromResult(new PlaybackSession(track.Id, "http://127.0.0.1/not-opened-by-fake-backend", false, true, true,video:MediaChoice.From(track).Video)); }
        public Task<Track> GetTrackAsync(string s, CancellationToken t) => throw new NotSupportedException();
        public Task<PlaylistPage> GetPlaylistPageAsync(string s, string c, int n, CancellationToken t) => throw new NotSupportedException();
        public Task<DependencyInfo> CheckDependencyAsync(CancellationToken t) => Task.FromResult(new DependencyInfo { Available = true });
    }
    static async Task TestMpv(Library lib, Track track, bool soak)
    {
        string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        lib.Options.MpvPath = Path.Combine(project, "tools/bin/mpv/mpv.exe");
        var ytTool = await new YouTubeSourceAdapter(Path.Combine(project, "tools/bin/yt-dlp.exe")).CheckDependencyAsync(None);
        Check(ytTool.Available, "optional yt-dlp dependency version detected without network extraction");
        var wrongTool = await ToolDependency.CheckAsync(lib.Options.MpvPath, "yt-dlp", None);
        Check(!wrongTool.Available, "wrong executable identity is rejected by dependency check");
        using (var recovery = new MpvBackend(lib.Options))
        {
            var sources = new SourceRegistry(); sources.Register(new LocalFileSource(lib));
            using var coordinator = new PlaybackCoordinator(lib, sources, recovery, _ => { });
            var env = new PlaybackEnvironment { GameActive = true, GameIdentity = new object(), MasterVolume = 0 };
            async Task Until(Func<bool> done)
            {
                var timer = Stopwatch.StartNew();
                while (!done() && timer.Elapsed.TotalSeconds < 15) { env.Time += .01; coordinator.Tick(env); await Task.Delay(5); }
                if (!done()) throw new Exception("Recovery: " + coordinator.Status + "/" + coordinator.Error + "/" + coordinator.Snapshot.State + "/" + coordinator.Intent);
            }
            coordinator.Play(track.Id); await Until(() => recovery.Snapshot.State == BackendState.Playing);
            Process.GetProcessById(recovery.ProcessId).Kill();
            await Until(() => coordinator.Intent == PlayIntent.Stopped && !String.IsNullOrEmpty(coordinator.Error));
            coordinator.Play(track.Id); await Until(() => recovery.Snapshot.State == BackendState.Playing);
            Check(true, "real coordinator recovers after child crash and player immediately retries");
            Process.GetProcessById(recovery.ProcessId).Kill();
            await Until(() => coordinator.Intent == PlayIntent.Stopped && recovery.Snapshot.State == BackendState.Stopped);
            for (int i = 0; i < 20; i++) { coordinator.Tick(env); await Task.Delay(5); }
            coordinator.Play(track.Id); await Until(() => recovery.Snapshot.State == BackendState.Playing);
            Check(true, "real coordinator also recovers when player waits for stop before retrying");
        }
        using var backend = new MpvBackend(lib.Options); var source = new LocalFileSource(lib);
        await backend.InitializeAsync(None); int pid = backend.ProcessId;
        Check(pid > 0, "real mpv process and named pipe initialized");
        var session = await source.CreateSessionAsync(track, None); await backend.PrepareAsync(session, None);
        await backend.SetVolumeAsync(0, None); await backend.PlayAsync(None); await Task.Delay(1200);
        Check(backend.Snapshot.State == BackendState.Playing && backend.Snapshot.Position > .2 && backend.Snapshot.Duration > 20, "real mpv decodes original PCM and advances playback");
        await backend.PauseAsync(None); double at = backend.Snapshot.Position; await Task.Delay(350);
        Check(backend.Snapshot.State == BackendState.Paused && Math.Abs(backend.Snapshot.Position - at) < .15, "real mpv pause holds position");
        await backend.SeekAsync(12, None); await Task.Delay(250); Check(Math.Abs(backend.Snapshot.Position - 12) < .3, "real mpv seek");
        await backend.ResumeAsync(None); await Task.Delay(350); Check(backend.Snapshot.Position > 12.1, "real mpv resume");
        for (int i = 0; i < 5; i++) { await backend.PrepareAsync(await source.CreateSessionAsync(track, None), None); await backend.PlayAsync(None); }
        Check(backend.ProcessId == pid, "five playback switches reuse one child process");
        await backend.SeekAsync(29.5, None); await Task.Delay(1300); Check(backend.Snapshot.State == BackendState.Ended, "real mpv EOF reported");
        await backend.StopAsync(None); Check(backend.Snapshot.State == BackendState.Stopped, "real mpv stop");
        Process.GetProcessById(pid).Kill(); await Task.Delay(350); Check(backend.Snapshot.State == BackendState.Failed, "unexpected mpv exit is reported");
        await backend.StopAsync(None); // Coordinator stops before the player retries after a crash.
        await backend.InitializeAsync(None); pid = backend.ProcessId;
        await backend.PrepareAsync(await source.CreateSessionAsync(track, None), None);
        Check(backend.Snapshot.State == BackendState.Ready, "stop after backend crash does not conceal a stale IPC connection on retry");
        backend.Dispose(); await Task.Delay(200);
        bool alive; try { alive = !Process.GetProcessById(pid).HasExited; } catch { alive = false; }
        Check(!alive, "dispose terminates owned mpv process");
        string pidFile = Path.Combine(lib.Root, "orphan-pid.txt");
        var parent = await Task.Run(() => ProcessRunner.Run(Environment.ProcessPath, new[] { "--mpv-orphan", lib.Options.MpvPath, pidFile }, None, 20));
        int orphan = Int32.Parse(File.ReadAllText(pidFile)); await Task.Delay(300);
        try { alive = !Process.GetProcessById(orphan).HasExited; } catch { alive = false; }
        Check(parent.ExitCode == 0 && !alive, "Windows job terminates mpv even when parent skips Dispose");
        using (var network = new MpvBackend(new Settings { MpvPath = lib.Options.MpvPath, CacheMiB = 4, BackCacheMiB = 1, CacheSeconds = 120 }))
        {
            string media = Path.Combine(lib.Root, "owned-stream.wav"); Wave(media, 600);
            using var server = new FixtureServer(media);
            await network.InitializeAsync(None);
            await network.PrepareAsync(new PlaybackSession("own-stream", server.Url, false, true, true, headers: new Dictionary<string,string> { ["User-Agent"] = "RimTube session header fixture" }), None);
            await network.SetVolumeAsync(0, None); await network.PlayAsync(None); await Task.Delay(1500);
            Check(network.Snapshot.Position > .3 && server.SentBytes < new FileInfo(media).Length, "authorized loopback stream plays before full file transfer");
            Check(server.LastUserAgent == "RimTube session header fixture", "real mpv sends source session headers to media server");
            await Task.Delay(1500);
            Check(network.Snapshot.BufferedBytes > 524288 && network.Snapshot.BufferedBytes <= 4 * 1048576L + 131072, "real mpv reports active forward cache within configured packet buffer bound");
            await network.PrepareAsync(new PlaybackSession("header-reset", server.Url, false, true, true), None);
            Check(server.LastUserAgent != "RimTube session header fixture", "switching media resets previous source headers");
            string ephemeral = server.Url + "?temporary-key=not-to-save";
            _ = new PlaybackSession("ephemeral", ephemeral, false, true, true); lib.Save();
            Check(!File.ReadAllText(lib.StateFile).Contains("temporary-key"), "ephemeral media locations never enter persistent library");
            using var cancellation = new CancellationTokenSource(100);
            try { await network.PrepareAsync(new PlaybackSession("stall", server.Url + "stall", false, false, true), cancellation.Token); Check(false, "prepare cancellation required"); }
            catch (OperationCanceledException) { Check(true, "stalled network preparation is cancellable"); }
            await network.InitializeAsync(None); await network.PrepareAsync(await source.CreateSessionAsync(track, None), None);
            Check(network.Snapshot.State == BackendState.Ready, "canceled preparation can recover using a fresh backend session");
            string bad = Path.Combine(lib.Root, "invalid.wav"); File.WriteAllText(bad, "invalid audio");
            try { await network.PrepareAsync(new PlaybackSession("bad", bad, true, true, true), None); Check(false, "bad media must fail"); }
            catch (MediaException e) { Check(e.Code == SourceErrorCode.DecodeFailed, "invalid audio returns a decode failure"); }
        }
        if (soak)
        {
            using var sustained = new MpvBackend(lib.Options); await sustained.InitializeAsync(None);
            string longFile = Path.Combine(lib.Root, "soak.wav"); Wave(longFile, 90);
            await sustained.PrepareAsync(new PlaybackSession("soak", longFile, true, true, true), None); await sustained.SetVolumeAsync(0, None); await sustained.PlayAsync(None);
            await Task.Delay(60000);
            Check(sustained.Snapshot.State == BackendState.Playing && sustained.Snapshot.Position > 59, "60-second sustained playback remains active and advances normally");
        }
        using var missing = new MpvBackend(new Settings());
        try { await missing.InitializeAsync(None); Check(false, "missing mpv must fail"); }
        catch (MediaException e) { Check(e.Code == SourceErrorCode.MissingTool, "missing mpv has actionable error"); }
    }
    sealed class PagedFixture : IMediaSource
    {
        public bool FailSecond, Loop;
        public string Id => "authorized-test-fixture";
        public SourceCapabilities Capabilities => SourceCapabilities.PlaylistPages;
        public bool CanHandle(string s) => s == "owned-fixture";
        public Task<PlaylistPage> GetPlaylistPageAsync(string s, string cursor, int size, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (FailSecond && cursor != "") throw new IOException("fixture interruption");
            var page = new PlaylistPage { SourcePlaylistId = "own", Title = "Own media", NextPage = cursor == "" || Loop ? "page2" : "", IsComplete = cursor != "" && !Loop };
            page.Tracks.Add(new Track { Id = cursor == "" ? "one" : "two" }); return Task.FromResult(page);
        }
        public Task<Track> GetTrackAsync(string s, CancellationToken t) => throw new NotSupportedException();
        public Task<PlaybackSession> CreateSessionAsync(Track s, CancellationToken t) => throw new NotSupportedException();
        public Task<DependencyInfo> CheckDependencyAsync(CancellationToken t) => Task.FromResult(new DependencyInfo { Available = true });
    }
    sealed class FixtureServer : IDisposable
    {
        readonly System.Net.Sockets.TcpListener listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        readonly CancellationTokenSource stop = new();
        readonly string file;
        long sent;
        public long SentBytes => Interlocked.Read(ref sent);
        public string Url { get; }
        public string LastUserAgent = "";
        public FixtureServer(string file)
        {
            this.file = file; listener.Start(); Url = "http://127.0.0.1:" + ((System.Net.IPEndPoint)listener.LocalEndpoint).Port + "/media.wav";
            _ = Accept();
        }
        async Task Accept()
        {
            try { while (!stop.IsCancellationRequested) { var client = await listener.AcceptTcpClientAsync(stop.Token); _ = Serve(client); } } catch (OperationCanceledException) { }
        }
        async Task Serve(System.Net.Sockets.TcpClient client)
        {
            using (client) try
            {
                var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                string first = await reader.ReadLineAsync(stop.Token), line; long start = 0; bool range = false;
                while (!String.IsNullOrEmpty(line = await reader.ReadLineAsync(stop.Token)))
                {
                    if (line.StartsWith("User-Agent:", StringComparison.OrdinalIgnoreCase)) LastUserAgent = line.Substring(11).Trim();
                    if (line.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase)) { range = true; Int64.TryParse(line.Substring(13).Split('-')[0], out start); }
                }
                if (first.Contains("stall")) { await Task.Delay(20000, stop.Token); return; }
                using var input = File.OpenRead(file); input.Position = start;
                string header = "HTTP/1.1 " + (range ? "206 Partial Content" : "200 OK") + "\r\nContent-Type: audio/wav\r\nAccept-Ranges: bytes\r\nContent-Length: " + (input.Length - start) + "\r\n";
                if (range) header += "Content-Range: bytes " + start + "-" + (input.Length - 1) + "/" + input.Length + "\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header + "Connection: close\r\n\r\n"), stop.Token);
                byte[] chunk = new byte[65536]; int n;
                while ((n = await input.ReadAsync(chunk, stop.Token)) > 0)
                { await stream.WriteAsync(chunk.AsMemory(0, n), stop.Token); Interlocked.Add(ref sent, n); await Task.Delay(5, stop.Token); }
            }
            catch (Exception e) when (e is IOException || e is OperationCanceledException || e is ObjectDisposedException) { }
        }
        public void Dispose() { stop.Cancel(); listener.Stop(); }
    }
    sealed class FakeBackend : IPlaybackBackend
    {
        public string Id => "fake";
        public bool Owned;
        public PlaybackSnapshot Snapshot { get; private set; } = new PlaybackSnapshot(BackendState.Idle);
        void State(BackendState state) => Snapshot = new PlaybackSnapshot(state, Snapshot.SessionId, Snapshot.Position, 30, Snapshot.Volume);
        public void Position(double n) => Snapshot = new PlaybackSnapshot(Snapshot.State, Snapshot.SessionId, n, 30, Snapshot.Volume);
        public void End() => State(BackendState.Ended);
        public Task InitializeAsync(CancellationToken t) { t.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public async Task PrepareAsync(PlaybackSession s, CancellationToken t) { await Task.Delay(20, t); Snapshot = new PlaybackSnapshot(BackendState.Ready, s.SessionId, duration: 30); }
        public Task PlayAsync(CancellationToken t) => ResumeAsync(t);
        public Task ResumeAsync(CancellationToken t) { t.ThrowIfCancellationRequested(); if (!Owned) throw new Exception("BGM overlap"); State(BackendState.Playing); return Task.CompletedTask; }
        public Task PauseAsync(CancellationToken t) { State(BackendState.Paused); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken t) { State(BackendState.Stopped); return Task.CompletedTask; }
        public Task SeekAsync(double p, CancellationToken t) { Position(p); return Task.CompletedTask; }
        public Task SetVolumeAsync(double v, CancellationToken t) { Snapshot = new PlaybackSnapshot(Snapshot.State, Snapshot.SessionId, Snapshot.Position, 30, v); return Task.CompletedTask; }
        public void Dispose() { State(BackendState.Disposed); }
    }
}
