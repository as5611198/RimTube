using RimTube.Core;
using RimTube.Sources;

internal static class GameMusicTests
{
    public static void Run(Action<bool,string> check,string root)
    {
        var infos=new[] {
            new GameSongInfo {DefName="CoreSong",Title="原版曲",PackageId="ludeon.rimworld",SourceName="Core",Kind="Core",Available=true,Duration=90},
            new GameSongInfo {DefName="DlcSong",Title="DLC 曲",PackageId="ludeon.rimworld.royalty",SourceName="Royalty",Kind="DLC",Available=true},
            new GameSongInfo {DefName="ModSong",Title="模組曲",PackageId="example.music",SourceName="Music mod",Available=true,Tense=true},
            new GameSongInfo {DefName="MissingClip",PackageId="example.broken",Available=false}
        };
        var source=new GameMusicSource(infos);
        AutoPlaylists(check,root,infos);
        check(source.Tracks.Count==4&&source.Tracks.Select(t=>t.Metadata["game.kind"]).Distinct().Count()==3,"game music catalog distinguishes Core, DLC and loaded mods");
        var song=source.Tracks.Single(t=>t.SourceId=="ModSong");
        check(song.Metadata["game.tense"]=="True"&&song.Metadata["game.source"]=="Music mod","game catalog retains source attribution and combat hint");
        var session=source.CreateSessionAsync(song,CancellationToken.None).GetAwaiter().GetResult();session.Validate();
        check(session.IsLocal&&session.GameSongDef=="ModSong"&&!session.HasVideo,"game source prepares native song reference without extracting a media file");
        using var library=new Library(Path.Combine(root,"game-music"));library.AddTo(library.Lists[0],song);library.Save();
        check(library.IsReady(song.Id)&&!Directory.EnumerateFiles(library.MediaDir).Any(),"game music is ready without a downloaded or copied audio file");
        using(var restored=new Library(library.Root)) check(restored.FindTrack(song.Id).SourceType=="game"&&restored.FindTrack(song.Id).SourceId=="ModSong","game-song playlist references survive library restart");
        try {source.CreateSessionAsync(source.Tracks.Single(t=>t.SourceId=="MissingClip"),CancellationToken.None).GetAwaiter().GetResult();check(false,"missing clip");}
        catch(MediaException e){check(e.Code==SourceErrorCode.NotFound,"unloaded game clips fail with a source message");}
        string stable=song.Id;source.Refresh(infos.Reverse());
        check(source.Tracks.Single(t=>t.SourceId=="ModSong").Id==stable,"game song identity is stable across scan order");
        source.Refresh(infos.Where(i=>i.DefName!="ModSong"));
        try {source.CreateSessionAsync(song,CancellationToken.None).GetAwaiter().GetResult();check(false,"removed mod");}
        catch(MediaException e){check(e.Code==SourceErrorCode.NotFound&&library.FindTrack(stable)!=null,"removed mod fails safely while saved playlist entry is retained");}
        var events=new List<string>();using var primary=new TraceBackend("primary",events);using var native=new TraceBackend("native",events);
        using var router=new RoutingPlaybackBackend(primary,native);
        router.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        router.PrepareAsync(session,CancellationToken.None).GetAwaiter().GetResult();
        router.ResumeAsync(CancellationToken.None).GetAwaiter().GetResult();
        check(primary.Initialized==0&&native.Initialized==1&&router.Snapshot.State==BackendState.Playing,"native game-song playback does not require initializing optional mpv");
        router.PauseAsync(CancellationToken.None).GetAwaiter().GetResult();router.SeekAsync(12,CancellationToken.None).GetAwaiter().GetResult();
        check(router.Snapshot.State==BackendState.Paused&&router.Snapshot.Position==12,"game song pause and seek use active native backend");
        var local=new PlaybackSession("local","C:\\fixture.wav",true,true,true);
        router.PrepareAsync(local,CancellationToken.None).GetAwaiter().GetResult();
        check(events.IndexOf("native.stop")<events.IndexOf("primary.prepare")&&ReferenceEquals(router.Active,primary),"switching away from game song stops native backend before preparing primary");
        router.ResumeAsync(CancellationToken.None).GetAwaiter().GetResult();int start=events.Count;
        router.PrepareAsync(session,CancellationToken.None).GetAwaiter().GetResult();
        check(events.Skip(start).First()=="primary.stop"&&ReferenceEquals(router.Active,native),"switching back to game music stops primary playback before native prepare");
        try {new PlaybackSession("bad","https://example.com",false,true,true,gameSongDef:"ModSong").Validate();check(false,"bad native reference");}
        catch(MediaException e){check(e.Code==SourceErrorCode.Unsupported,"native song references cannot disguise remote media");}
    }
    private static void AutoPlaylists(Action<bool,string> check,string root,GameSongInfo[] infos)
    {
        var type=typeof(GameMusicSource).Assembly.GetType("RimTube.Sources.GameMusicPlaylists");
        check(type!=null,"loaded game music automatically builds per-source playlists");
        var sync=type.GetMethod("Sync");
        using var lib=new Library(Path.Combine(root,"auto-game-lists"));
        var local=new Track {Id="local-keep",SourceType="local",Title="Local"};
        var manual=new Playlist {Name="原版",Tracks=new List<string>{local.Id}};lib.Lists.Add(manual);lib.Tracks[local.Id]=local;
        var entries=infos.Concat(new[]{
            new GameSongInfo {DefName="CoreTwo",PackageId="LUDEON.RIMWORLD",SourceName="Core",Kind="Core",Available=false},
            new GameSongInfo {DefName="OtherModSong",PackageId="another.music",SourceName="Music mod",Available=true}
        }).ToArray();
        var source=new GameMusicSource(entries);
        void Sync()=>sync.Invoke(null,new object[]{lib,source.Tracks});
        Sync();
        var generated=lib.Lists.Where(p=>p.SourceType=="game-catalog").ToArray();
        check(generated.Length==5,"Core DLC and individual mod packages each receive a playlist");
        var core=generated.Single(p=>p.SourceId=="ludeon.rimworld");
        check(core.Name=="原版 RimWorld"&&core.Tracks.Count==2,"Core groups package case variants and includes every registered song");
        check(generated.Count(p=>p.Name=="模組 · Music mod")==2&&generated.Select(p=>p.Id).Distinct().Count()==5,"same mod display name cannot merge different package identities");
        check(manual.Tracks.SequenceEqual(new[]{local.Id})&&lib.Options.GeneralList=="general"&&lib.Options.CombatList=="combat","automatic lists preserve manual playlists and configured playback roles");
        var ids=generated.Select(p=>p.Id).OrderBy(x=>x).ToArray();int tracks=lib.Tracks.Count;
        Sync();check(lib.Lists.Count==8&&lib.Tracks.Count==tracks&&core.Tracks.Distinct().Count()==2,"repeated catalog sync creates no duplicate lists or references");
        var mod=generated.Single(p=>p.SourceId=="example.music");string modSong=mod.Tracks.Single();
        string stableModId=mod.Id;
        try {lib.AddTo(core,local);check(false,"managed playlist members");}
        catch(InvalidOperationException){check(!core.Tracks.Contains(local.Id),"manual additions cannot contaminate automatically managed source membership");}
        lib.Options.GeneralList=core.Id;
        var importMethod=typeof(Library).GetMethod("ImportPlaylist");
        check(importMethod!=null,"imports can choose a writable playlist when automatic music is assigned as general BGM");
        var imports=(Playlist)importMethod.Invoke(lib,new object[]{null});lib.AddTo(imports,local);
        check(!imports.IsGameCatalog&&imports.Tracks.Contains(local.Id)&&!core.Tracks.Contains(local.Id),"new imported media never contaminates a game source playlist used for BGM");
        string removedCore=source.Tracks.Single(t=>t.SourceId=="CoreSong").Id;
        manual.Tracks.Add(modSong);manual.Tracks.Add(removedCore);mod.Name="我的模組歌單";
        lib.Save();using(var restored=new Library(lib.Root))
            check(restored.Lists.Where(p=>p.SourceType=="game-catalog").Select(p=>p.Id).OrderBy(x=>x).SequenceEqual(ids),"generated list ownership and identities survive restart");
        source.Refresh(entries.Where(i=>i.PackageId!="example.music"&&i.DefName!="CoreSong").Concat(new[]{new GameSongInfo {DefName="CoreThree",PackageId="ludeon.rimworld",Kind="Core",Available=true}}));
        Sync();
        check(core.Tracks.Count==2&&!core.Tracks.Contains(removedCore),"catalog refresh adds and removes members only in managed source lists");
        check(mod.Tracks.Single()==modSong&&lib.FindTrack(modSong).Availability==SourceAvailability.Missing&&mod.SyncMessage.Contains("未載入"),"unloaded mod retains playlist and references with a missing-source status");
        check(manual.Tracks.Contains(removedCore)&&manual.Tracks.Contains(modSong)&&lib.FindTrack(removedCore).Availability==SourceAvailability.Missing,"unloaded songs remain referenced by manual playlists");
        source.Refresh(entries.Reverse());Sync();
        check(mod.Name=="我的模組歌單"&&mod.Id==stableModId&&lib.FindTrack(modSong).Availability==SourceAvailability.Available,"re-enabled source revives the same renamed playlist");
        int before=lib.Lists.Count;lib.ReadOnly=true;source.Refresh(entries.Concat(new[]{new GameSongInfo {DefName="NewMod",PackageId="new.mod",Available=true}}));Sync();
        check(lib.Lists.Count==before,"read-only library is not mutated by automatic playlist creation");
        lib.ReadOnly=false;
        check(!Directory.EnumerateFiles(lib.MediaDir).Any()&&!Directory.EnumerateFiles(lib.VideoDir).Any(),"automatic game playlists never copy or export game media");
    }
    private sealed class TraceBackend : IPlaybackBackend
    {
        private readonly List<string> events;
        public string Id{get;}
        public int Initialized;
        public PlaybackSnapshot Snapshot{get;private set;}=new PlaybackSnapshot(BackendState.Idle);
        public TraceBackend(string id,List<string> events){Id=id;this.events=events;}
        private Task Set(BackendState state,CancellationToken token){token.ThrowIfCancellationRequested();Snapshot=new PlaybackSnapshot(state,Snapshot.SessionId,Snapshot.Position,90,Snapshot.Volume);return Task.CompletedTask;}
        public Task InitializeAsync(CancellationToken token){token.ThrowIfCancellationRequested();++Initialized;return Task.CompletedTask;}
        public Task PrepareAsync(PlaybackSession session,CancellationToken token){token.ThrowIfCancellationRequested();events.Add(Id+".prepare");Snapshot=new PlaybackSnapshot(BackendState.Ready,session.SessionId,duration:90);return Task.CompletedTask;}
        public Task PlayAsync(CancellationToken token)=>Set(BackendState.Playing,token);
        public Task ResumeAsync(CancellationToken token)=>PlayAsync(token);
        public Task PauseAsync(CancellationToken token)=>Set(BackendState.Paused,token);
        public Task StopAsync(CancellationToken token){events.Add(Id+".stop");return Set(BackendState.Stopped,token);}
        public Task SeekAsync(double position,CancellationToken token){token.ThrowIfCancellationRequested();Snapshot=new PlaybackSnapshot(Snapshot.State,Snapshot.SessionId,position,90,Snapshot.Volume);return Task.CompletedTask;}
        public Task SetVolumeAsync(double volume,CancellationToken token)=>Task.CompletedTask;
        public void Dispose(){Snapshot=new PlaybackSnapshot(BackendState.Disposed);}
    }
}
