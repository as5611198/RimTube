using System.Diagnostics;
using System.Text.Json;
using RimTube.Core;
using RimTube.Sources;

internal static class PlaybackLatencyTests
{
    private const string CounterVariable = "RIMTUBE_TEST_RESOLVE_COUNT_PATH";
    public static void CountFixtureResolve(string id)
    {
        string path=Environment.GetEnvironmentVariable(CounterVariable);
        if(!String.IsNullOrEmpty(path)) File.AppendAllText(path,id+Environment.NewLine);
    }
    public static void Run(Action<bool,string> check,string root)
    {
        string counter=Path.Combine(root,"resolver-count.txt");
        string previous=Environment.GetEnvironmentVariable(CounterVariable);
        Environment.SetEnvironmentVariable(CounterVariable,counter);
        try
        {
            using var source=new YouTubeSourceAdapter(Environment.ProcessPath);
            var track=source.GetTrackAsync("https://youtu.be/BaW_jenozKc",CancellationToken.None).GetAwaiter().GetResult();
            var first=source.CreateSessionAsync(track,CancellationToken.None).GetAwaiter().GetResult();
            check(File.ReadAllLines(counter).Length==1,"metadata-to-first-play resolves YouTube only once");
            var second=source.CreateSessionAsync(track,CancellationToken.None).GetAwaiter().GetResult();
            check(File.ReadAllLines(counter).Length==2&&first.Location!=second.Location,"handoff is consumed once and replay resolves fresh media");
            EdgeCases(check,root,counter);
        }
        finally {Environment.SetEnvironmentVariable(CounterVariable,previous);}
    }
    private static void EdgeCases(Action<bool,string> check,string root,string counter)
    {
        int Count()=>File.ReadAllLines(counter).Length;
        var settings=new Settings {YtDlp=Environment.ProcessPath};
        DateTime now=DateTime.UtcNow;
        using var timed=new YouTubeSourceAdapter(settings,true,null,()=>now);
        var track=timed.GetTrackAsync("https://youtu.be/BaW_jenozKc",CancellationToken.None).GetAwaiter().GetResult();
        int count=Count();now=now.AddSeconds(30);
        timed.CreateSessionAsync(track,CancellationToken.None).GetAwaiter().GetResult();
        check(Count()==count+1,"30-second expiry forces fresh resolution at the boundary");
        using var source=new YouTubeSourceAdapter(settings);
        foreach(var change in new Action<MediaChoice>[] {c=>c.Height=360,c=>c.Subtitle="en",c=>c.AutoSubtitles=true,c=>c.Video=false,c=>c.Delivery=MediaDelivery.PreferSaved})
        {
            var choice=new MediaChoice {Video=true};
            var video=source.GetTrackAsync("https://youtu.be/MediaTest01",CancellationToken.None,choice).GetAwaiter().GetResult();
            count=Count();change(choice);choice.Store(video);
            source.CreateSessionAsync(video,CancellationToken.None).GetAwaiter().GetResult();
            check(Count()==count+1,"changed playback options cannot reuse the metadata handoff");
        }
        track=source.GetTrackAsync("https://youtu.be/BaW_jenozKc",CancellationToken.None).GetAwaiter().GetResult();
        count=Count();using var cancel=new CancellationTokenSource();cancel.Cancel();
        try {source.CreateSessionAsync(track,cancel.Token).GetAwaiter().GetResult();check(false,"canceled play must not resolve");}
        catch(OperationCanceledException){check(Count()==count,"canceled playback does not consume or resolve pending media");}
        source.CreateSessionAsync(track,CancellationToken.None).GetAwaiter().GetResult();
        check(Count()==count,"valid playback can consume handoff after canceled attempt");
        track=source.GetTrackAsync("https://youtu.be/BaW_jenozKc",CancellationToken.None).GetAwaiter().GetResult();
        count=Count();
        Task.WhenAll(source.CreateSessionAsync(track,CancellationToken.None),source.CreateSessionAsync(track,CancellationToken.None)).GetAwaiter().GetResult();
        check(Count()==count+1,"concurrent playback requests consume the one-shot handoff only once");
        track=source.GetTrackAsync("https://youtu.be/BaW_jenozKc",CancellationToken.None).GetAwaiter().GetResult();
        source.GetTrackAsync("https://youtu.be/IsLive00001",CancellationToken.None).GetAwaiter().GetResult();
        count=Count();source.CreateSessionAsync(track,CancellationToken.None).GetAwaiter().GetResult();
        check(Count()==count+1,"only newest metadata occupies the single RAM slot");
        track=source.GetTrackAsync("https://youtu.be/BaW_jenozKc",CancellationToken.None).GetAwaiter().GetResult();
        using var separate=new YouTubeSourceAdapter(settings);
        count=Count();separate.CreateSessionAsync(track,CancellationToken.None).GetAwaiter().GetResult();
        check(Count()==count+1,"separate player adapters never share signed media handoffs");
        using var library=new Library(Path.Combine(root,"handoff-persistence"));
        library.AddTo(library.Lists[0],track);library.Save();
        string saved=File.ReadAllText(library.StateFile);
        check(!saved.Contains("googlevideo")&&!saved.Contains("sig="),"handoff never persists signed media URLs in the library");
    }
    public static int Live(string url)
    {
        string project=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../"));
        var options=new Settings {YtDlp=Path.Combine(project,"tools/bin/yt-dlp.exe"),JsRuntime=Path.Combine(project,"tools/bin/deno.exe"),MpvPath=Path.Combine(project,"tools/bin/mpv/mpv.exe")};
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(65));var token=timeout.Token;
        try
        {
            using var source=new YouTubeSourceAdapter(options);
            var watch=Stopwatch.StartNew();
            var track=source.GetTrackAsync(url,token).GetAwaiter().GetResult();
            double metadataMs=watch.Elapsed.TotalMilliseconds;watch.Restart();
            var session=source.CreateSessionAsync(track,token).GetAwaiter().GetResult();
            double handoffMs=watch.Elapsed.TotalMilliseconds;watch.Restart();
            using var backend=new MpvBackend(options,headless:true);
            backend.InitializeAsync(token).GetAwaiter().GetResult();
            double initializeMs=watch.Elapsed.TotalMilliseconds;watch.Restart();
            backend.PrepareAsync(session,token).GetAwaiter().GetResult();
            double prepareMs=watch.Elapsed.TotalMilliseconds;watch.Restart();
            backend.SetVolumeAsync(0,token).GetAwaiter().GetResult();backend.PlayAsync(token).GetAwaiter().GetResult();
            while(backend.Snapshot.Position<1&&watch.Elapsed.TotalSeconds<10){token.ThrowIfCancellationRequested();Thread.Sleep(50);}
            if(backend.Snapshot.Position<1)throw new Exception("Playback did not advance after handoff.");
            Console.WriteLine(JsonSerializer.Serialize(new {MetadataMs=Math.Round(metadataMs,1),HandoffMs=Math.Round(handoffMs,3),InitializeMs=Math.Round(initializeMs,1),PrepareMs=Math.Round(prepareMs,1),ResolveTotalMs=Math.Round(metadataMs+handoffMs,1),Live=session.IsLive}));
            Console.WriteLine("PASS public stream advances after RAM handoff; null audio/video outputs; no game launch.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine("FAIL "+YoutubeClient.CleanError(error.Message));return 1;}
    }
}
