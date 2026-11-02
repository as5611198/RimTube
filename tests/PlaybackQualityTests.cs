using System.Diagnostics;
using System.Reflection;
using RimTube.Core;

internal static class PlaybackQualityTests
{
    public static void Run(Action<bool,string> check,string root)
    {
        using var lib=new Library(Path.Combine(root,"quality"));
        var first=new Track {Id="yt_quality1",SourceType="youtube",Title="First"};
        var second=new Track {Id="yt_quality2",SourceType="youtube",Title="Second"};
        new MediaChoice {Video=true,Delivery=MediaDelivery.PreferSaved,Subtitle="en"}.Store(first);
        new MediaChoice {Video=true}.Store(second);
        var list=new Playlist {Id="quality",Tracks=new List<string>{first.Id,second.Id}};lib.Lists.Add(list);
        lib.Tracks[first.Id]=first;lib.Tracks[second.Id]=second;
        var source=new Source();var registry=new SourceRegistry();registry.Register(source);
        using var backend=new Backend();using var player=new PlaybackCoordinator(lib,registry,backend,_=>{});
        var env=new PlaybackEnvironment {GameActive=true,GameIdentity=new object(),MasterVolume=1};
        void Until(Func<bool> done,string message)
        {var timer=Stopwatch.StartNew();while(!done()&&timer.ElapsedMilliseconds<3000){player.Tick(env);Thread.Sleep(2);}check(done(),message);}
        void Pump(){for(int i=0;i<30;i++){player.Tick(env);Thread.Sleep(2);}}
        player.Play(first.Id,list.Id);Until(()=>player.Snapshot.State==BackendState.Playing,"quality fixture begins playing");
        var change=typeof(PlaybackCoordinator).GetMethod("ChangeVideoQuality");
        check(change!=null,"playing video supports a direct quality change");
        bool Change(string id,int height)=>(bool)change.Invoke(player,new object[]{id,height});
        backend.Position(17);string original=backend.Snapshot.SessionId;
        check(Change(first.Id,480),"direct quality request accepted for active video");
        Until(()=>player.Snapshot.State==BackendState.Playing&&player.Snapshot.SessionId!=original,"quality change reloads selected video");
        check(Math.Abs(player.Position-17)<.01&&source.Height==480&&source.Delivery==MediaDelivery.Stream,"quality reload preserves timestamp and resolves requested stream instead of saved quality");
        check(MediaChoice.From(first).Subtitle=="en"&&player.Intent==PlayIntent.Playing,"quality reload preserves captions and playback intent");
        var subtitles=typeof(PlaybackCoordinator).GetMethod("ChangeVideoSubtitles");
        check(subtitles!=null,"playing video supports direct subtitle-language selection");
        bool ChangeSubtitles(string id,string language,bool automatic)=>(bool)subtitles.Invoke(player,new object[]{id,language,automatic});
        backend.Position(19);original=backend.Snapshot.SessionId;
        check(ChangeSubtitles(first.Id,"zh-TW",false),"manual subtitle choice accepted while video plays");
        Until(()=>player.Snapshot.State==BackendState.Playing&&player.Snapshot.SessionId!=original,"subtitle-language change reloads source");
        check(Math.Abs(player.Position-19)<.01&&source.Subtitle=="zh-TW"&&source.Height==480,"subtitle switch keeps playback position and quality");
        backend.End();Until(()=>player.CurrentId==second.Id&&player.Snapshot.State==BackendState.Playing,"quality change retains explicit playlist progression");
        check(!Change(first.Id,360)&&MediaChoice.From(second).Height==720,"stale quality menu cannot alter the next video");
        Pump();backend.Position(9);player.Pause();Until(()=>player.Snapshot.State==BackendState.Paused,"quality fixture pauses");
        check(Change(second.Id,1080),"paused video accepts next quality");Pump();
        check(player.Intent==PlayIntent.Paused&&player.Snapshot.State!=BackendState.Playing,"quality selection never resumes a manually paused video");
        player.Play();Until(()=>player.Snapshot.State==BackendState.Playing&&source.Height==1080,"resuming paused video applies selected quality");
        check(Math.Abs(player.Position-9)<.01,"paused quality change keeps its bookmark");
        player.Stop();Pump();check(!Change(second.Id,144),"quality menu cannot revive stopped playback");
        check(!ChangeSubtitles(second.Id,"en",true),"subtitle menu cannot revive stopped playback");
        second.Metadata["media.live"]="True";player.Play(second.Id,list.Id);
        Until(()=>player.Snapshot.State==BackendState.Playing,"live quality fixture begins");backend.Position(22);original=backend.Snapshot.SessionId;
        Change(second.Id,240);Until(()=>player.Snapshot.State==BackendState.Playing&&player.Snapshot.SessionId!=original,"live quality switch reconnects");
        check(player.IsLive&&!player.CanSeek&&player.Position==0,"live quality change returns to live feed without seeking old timestamp");
        original=backend.Snapshot.SessionId;
        try {Change(second.Id,100);check(false,"invalid quality");}
        catch(TargetInvocationException e) when(e.InnerException is ArgumentException){check(backend.Snapshot.SessionId==original&&MediaChoice.From(second).Height==240,"invalid quality leaves playback and saved selection intact");}
    }
    private sealed class Source : IMediaSource
    {
        public string Id=>"youtube";public SourceCapabilities Capabilities=>SourceCapabilities.Sessions;
        public int Height;public MediaDelivery Delivery;public string Subtitle;
        public bool CanHandle(string input)=>false;
        public Task<PlaybackSession> CreateSessionAsync(Track track,CancellationToken token)
        {token.ThrowIfCancellationRequested();var choice=MediaChoice.From(track);Height=choice.Height;Delivery=choice.Delivery;Subtitle=choice.Subtitle;bool live=track.Metadata.ContainsKey("media.live");return Task.FromResult(new PlaybackSession(track.Id,"https://example.invalid/video",false,!live,true,video:true,live:live));}
        public Task<Track> GetTrackAsync(string s,CancellationToken t)=>throw new NotSupportedException();
        public Task<PlaylistPage> GetPlaylistPageAsync(string s,string c,int n,CancellationToken t)=>throw new NotSupportedException();
        public Task<DependencyInfo> CheckDependencyAsync(CancellationToken t)=>throw new NotSupportedException();
    }
    private sealed class Backend : IPlaybackBackend
    {
        public string Id=>"quality-fixture";public PlaybackSnapshot Snapshot{get;private set;}=new PlaybackSnapshot(BackendState.Idle);
        private Task State(BackendState state){Snapshot=new PlaybackSnapshot(state,Snapshot.SessionId,Snapshot.Position,120,Snapshot.Volume);return Task.CompletedTask;}
        public void Position(double value){Snapshot=new PlaybackSnapshot(Snapshot.State,Snapshot.SessionId,value,120,Snapshot.Volume);}
        public void End()=>State(BackendState.Ended);
        public Task InitializeAsync(CancellationToken t)=>Task.CompletedTask;
        public Task PrepareAsync(PlaybackSession s,CancellationToken t){t.ThrowIfCancellationRequested();Snapshot=new PlaybackSnapshot(BackendState.Ready,s.SessionId,duration:120);return Task.CompletedTask;}
        public Task PlayAsync(CancellationToken t)=>State(BackendState.Playing);
        public Task ResumeAsync(CancellationToken t)=>PlayAsync(t);
        public Task PauseAsync(CancellationToken t)=>State(BackendState.Paused);
        public Task StopAsync(CancellationToken t)=>State(BackendState.Stopped);
        public Task SeekAsync(double p,CancellationToken t){Position(p);return Task.CompletedTask;}
        public Task SetVolumeAsync(double v,CancellationToken t){Snapshot=new PlaybackSnapshot(Snapshot.State,Snapshot.SessionId,Snapshot.Position,120,v);return Task.CompletedTask;}
        public void Dispose(){}
    }
}
