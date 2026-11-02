using RimTube;
using RimTube.Core;
using RimTube.Sources;
using System.Text.Json;

internal static class FullYoutubeTests
{
    public static int NetworkDownload(string url, bool video = true,string bundledMod=null)
    {
        string project=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../"));
        string dir=Path.Combine(project,"artifacts/full-media-network/"+Guid.NewGuid().ToString("N"));
        using var library=new Library(dir);
        library.Options.YtDlp=Path.Combine(project,"tools/bin/yt-dlp.exe"); library.Options.JsRuntime=Path.Combine(project,"tools/bin/deno.exe");
        library.Options.Ffmpeg=Path.Combine(project,"tools/bin/ffmpeg.exe"); library.Options.MpvPath=Path.Combine(project,"tools/bin/mpv/mpv.exe");
        if(bundledMod!=null)library.Options.ConfigureBundledTools(bundledMod);
        var track=YouTubeSourceAdapter.Identify(url); library.AddTo(library.Lists[0],track);
        var choice=new MediaChoice { Video=video,Height=144,Delivery=MediaDelivery.Download }; choice.Store(track);
        try
        {
            using(var queue=new JobQueue(library))
            {
                var job=queue.EnqueueDownload(track,choice); var watch=System.Diagnostics.Stopwatch.StartNew();
                while(job.State!=JobState.Complete && job.State!=JobState.Failed && watch.Elapsed.TotalSeconds<150) {queue.Pump();Thread.Sleep(50);}
                if(job.State!=JobState.Complete) throw new Exception("Download state="+job.State+" "+job.Progress);
            }
            Console.WriteLine("PASS real " + (video ? "low-resolution video" : "audio extraction") + " download commits; bytes="+new FileInfo(SavedMedia.PathFor(library,track,video)).Length);
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var source=new YouTubeSourceAdapter(new Settings(),true,library);
            var session=source.CreateSessionAsync(track,timeout.Token).GetAwaiter().GetResult();
            using var backend=new MpvBackend(library.Options,headless:true);
            backend.InitializeAsync(timeout.Token).GetAwaiter().GetResult(); backend.PrepareAsync(session,timeout.Token).GetAwaiter().GetResult();
            if(!session.IsLocal || backend.Snapshot.Duration<1) throw new Exception("Saved video failed offline prepare");
            Console.WriteLine("PASS offline local " + (video ? "video" : "audio") + " prepares in mpv without resolver; duration="+backend.Snapshot.Duration);
            Console.WriteLine("No game, visible video window, or GPU decoding; artifacts="+dir);return 0;
        }
        catch(Exception e) { Console.Error.WriteLine("FAIL "+YoutubeClient.CleanError(e.Message));return 1; }
    }
    public static int NetworkMetadata(string url)
    {
        string project=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../"));
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(100));
        try
        {
            var source=new YouTubeSourceAdapter(new Settings { YtDlp=Path.Combine(project,"tools/bin/yt-dlp.exe"),JsRuntime=Path.Combine(project,"tools/bin/deno.exe") });
            var track=source.GetTrackAsync(url,timeout.Token,new MediaChoice { Video=true,Height=144,Subtitle="en",AutoSubtitles=true }).GetAwaiter().GetResult();
            var session=source.CreateSessionAsync(track,timeout.Token).GetAwaiter().GetResult();
            Console.WriteLine("PASS real video metadata and selected resolution resolve: "+track.Title+"; split audio="+(session.AudioLocation!="")+"; subtitle available="+(session.SubtitleLocation!="")+"; live="+session.IsLive);
            Console.WriteLine("Metadata only; no game, playback, or media download."); return 0;
        }
        catch(Exception e) { Console.Error.WriteLine("FAIL "+YoutubeClient.CleanError(e.Message));return 1; }
    }
    public static int NetworkVideoAudio(string url,int height=144,string subtitle="off")
    {
        string project=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../"));
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(70));
        var token=timeout.Token;
        try
        {
            var options=new Settings { YtDlp=Path.Combine(project,"tools/bin/yt-dlp.exe"), JsRuntime=Path.Combine(project,"tools/bin/deno.exe"), MpvPath=Path.Combine(project,"tools/bin/mpv/mpv.exe") };
            var source=new YouTubeSourceAdapter(options);
            var track=source.GetTrackAsync(url,token,new MediaChoice {Video=true,Height=height,Subtitle=subtitle}).GetAwaiter().GetResult();
            var session=source.CreateSessionAsync(track,token).GetAwaiter().GetResult();
            if (!session.HasVideo || String.IsNullOrEmpty(session.AudioLocation)) throw new Exception("Video session lost its separate audio stream.");
            Console.WriteLine("PASS real source retains both video and separate audio; live="+session.IsLive);
            int diagnosticCount=0;
            using var backend=new MpvBackend(options,headless:true,diagnostics:line=>{if(Interlocked.Increment(ref diagnosticCount)<=16)Console.WriteLine("MPV "+line);});
            backend.InitializeAsync(token).GetAwaiter().GetResult();
            backend.PrepareAsync(session,token).GetAwaiter().GetResult();
            backend.SetVolumeAsync(.25,token).GetAwaiter().GetResult();
            backend.PlayAsync(token).GetAwaiter().GetResult();
            var watch=System.Diagnostics.Stopwatch.StartNew();
            bool audio=false, video=false, decoded=false;
            while (watch.Elapsed.TotalSeconds<12)
            {
                var tracks=System.Xml.Linq.XElement.Parse(backend.DiagnosticPropertyAsync("track-list",token).GetAwaiter().GetResult());
                audio=tracks.Elements().Any(t=>(string)t.Element("type")=="audio"&&(string)t.Element("selected")=="true");
                video=tracks.Elements().Any(t=>(string)t.Element("type")=="video"&&(string)t.Element("selected")=="true");
                if(audio&&video&&backend.Snapshot.Position>=.5)
                {
                    var parameters=System.Xml.Linq.XElement.Parse(backend.DiagnosticPropertyAsync("audio-params",token).GetAwaiter().GetResult());
                    int sampleRate;
                    decoded=Int32.TryParse((string)parameters.Element("samplerate"),out sampleRate)&&sampleRate>0;
                }
                if(audio&&video&&decoded&&backend.Snapshot.Position>=1) break;
                Thread.Sleep(100);
            }
            if(!audio||!video||!decoded||backend.Snapshot.Position<1) throw new Exception("Video/audio tracks did not decode and advance together.");
            Console.WriteLine("PASS real mpv selects video and audio, decodes audio samples and advances; volume="+backend.Snapshot.Volume.ToString("F2"));
            Console.WriteLine("Headless null outputs; no game launch, GPU output, permanent media download or speaker-hearing claim.");
            return 0;
        }
        catch(Exception error) { Console.Error.WriteLine("FAIL "+YoutubeClient.CleanError(error.Message));return 1; }
    }
    public static int NetworkCaptions(string url)
    {
        string project=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../"));
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(65));var token=timeout.Token;
        try
        {
            var options=new Settings {YtDlp=Path.Combine(project,"tools/bin/yt-dlp.exe"),JsRuntime=Path.Combine(project,"tools/bin/deno.exe"),MpvPath=Path.Combine(project,"tools/bin/mpv/mpv.exe")};
            using var source=new YouTubeSourceAdapter(options);
            var track=source.GetTrackAsync(url,token,new MediaChoice {Video=true,Height=144,Subtitle="zh-TW",AutoSubtitles=false}).GetAwaiter().GetResult();
            var session=source.CreateSessionAsync(track,token).GetAwaiter().GetResult();
            if(session.SubtitleLocation=="") throw new Exception("Requested Traditional Chinese manual caption was not selected.");
            using var client=new System.Net.Http.HttpClient {MaxResponseContentBufferSize=2097152};
            foreach(var header in session.Headers)client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key,header.Value);
            string vtt=client.GetStringAsync(session.SubtitleLocation,token).GetAwaiter().GetResult();
            var cue=System.Text.RegularExpressions.Regex.Match(vtt,@"(?m)^(\d{2}:\d{2}:\d{2}\.\d{3}) -->");
            if(!vtt.StartsWith("WEBVTT")||!cue.Success)throw new Exception("Caption source did not return a WebVTT cue.");
            double cueTime=TimeSpan.Parse(cue.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture).TotalSeconds+.1;
            using var backend=new MpvBackend(options,headless:true);backend.InitializeAsync(token).GetAwaiter().GetResult();
            backend.PrepareAsync(session,token).GetAwaiter().GetResult();backend.SeekAsync(cueTime,token).GetAwaiter().GetResult();backend.PlayAsync(token).GetAwaiter().GetResult();
            bool chinese=false,selected=false;var timer=System.Diagnostics.Stopwatch.StartNew();
            while(timer.Elapsed.TotalSeconds<10)
            {
                var tracks=System.Xml.Linq.XElement.Parse(backend.DiagnosticPropertyAsync("track-list",token).GetAwaiter().GetResult());
                selected=tracks.Elements().Any(t=>(string)t.Element("type")=="sub"&&(string)t.Element("selected")=="true");
                var text=System.Xml.Linq.XElement.Parse(backend.DiagnosticPropertyAsync("sub-text",token).GetAwaiter().GetResult()).Value;
                chinese=System.Text.RegularExpressions.Regex.IsMatch(text,"[\u4e00-\u9fff]");
                if(selected&&chinese) break;Thread.Sleep(100);
            }
            if(!selected||!chinese)throw new Exception("mpv did not decode and select Chinese subtitle text.");
            Console.WriteLine("PASS public video resolves zh-TW manual caption with suffixed source key");
            Console.WriteLine("PASS caption endpoint returns WebVTT; bytes="+System.Text.Encoding.UTF8.GetByteCount(vtt));
            Console.WriteLine("PASS real mpv selects caption track and decodes Chinese subtitle text at cue="+cueTime.ToString("F3"));
            Console.WriteLine("144p headless null outputs; no game, GPU output, permanent media or signed-URL persistence.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine("FAIL "+YoutubeClient.CleanError(e.Message));return 1;}
    }
    public static void Backend(Action<bool,string> check,string root)
    {
        string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../"));
        string video=Path.Combine(root,"tiny-video.mp4"),audio=Path.Combine(root,"tiny-audio.wav"),sub=Path.Combine(root,"tiny-caption.vtt");
        File.WriteAllText(sub,"WEBVTT\n\n00:00.000 --> 00:03.000\nRimTube subtitle fixture\n");
        ModernTests.Wave(audio,3);
        var generated=ProcessRunner.Run(Path.Combine(project,"tools/bin/ffmpeg.exe"),new[] {"-nostdin","-hide_banner","-loglevel","error","-f","lavfi","-i","testsrc=size=160x90:rate=5:duration=3","-c:v","libx264","-threads","1","-pix_fmt","yuv420p","-y",video},CancellationToken.None,20);
        check(generated.ExitCode==0 && File.Exists(video),"tiny 160x90 video fixture generated with one CPU thread");
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(35)); var token=timeout.Token;
        using var backend=new MpvBackend(new Settings {MpvPath=Path.Combine(project,"tools/bin/mpv/mpv.exe")},headless:true);
        backend.InitializeAsync(token).GetAwaiter().GetResult();
        backend.PrepareAsync(new PlaybackSession("tiny",video,true,true,true,video:true,audioLocation:audio,subtitleLocation:sub),token).GetAwaiter().GetResult();
        var tracks=backend.DiagnosticPropertyAsync("track-list",token).GetAwaiter().GetResult();
        check(tracks.Contains("video") && tracks.Contains("audio") && tracks.Contains("sub"),"real mpv loads video, external audio and captions together");
        var changeScale=typeof(MpvBackend).GetMethod("SetSubtitleScaleAsync");
        check(changeScale!=null,"real video backend supports live subtitle sizing");
        string beforeScale=backend.Snapshot.SessionId;
        ((Task)changeScale.Invoke(backend,new object[]{2d,token})).GetAwaiter().GetResult();
        var scaleValue=System.Xml.Linq.XElement.Parse(backend.DiagnosticPropertyAsync("sub-scale",token).GetAwaiter().GetResult());
        check(double.Parse(scaleValue.Value,System.Globalization.CultureInfo.InvariantCulture)==2&&backend.Snapshot.SessionId==beforeScale,"subtitle size applies to actual mpv without replacing the video session");
        backend.PlayAsync(token).GetAwaiter().GetResult();
        Thread.Sleep(350);
        backend.SetVideoVisibleAsync(false,token).GetAwaiter().GetResult();
        string vid=backend.DiagnosticPropertyAsync("vid",token).GetAwaiter().GetResult();
        check(!vid.Contains("false") && !vid.Contains(">no<"),"hiding the picture retains the selected video track to avoid decoder restart and black frames");
        double hiddenAt=backend.Snapshot.Position;
        Thread.Sleep(300);
        check(backend.Snapshot.Position>hiddenAt && backend.Snapshot.State==BackendState.Playing,"audio timeline keeps advancing while picture is hidden");
        backend.SetVideoVisibleAsync(true,token).GetAwaiter().GetResult();
        check(backend.Snapshot.State==BackendState.Playing && backend.Snapshot.Position>=hiddenAt,"showing the picture preserves the active playback session");
        backend.SetSubtitlesVisibleAsync(false,token).GetAwaiter().GetResult();
        string sid=backend.DiagnosticPropertyAsync("sid",token).GetAwaiter().GetResult();
        Console.WriteLine("DIAG disabled sid="+sid);
        check(!sid.Contains("no")&&!sid.Contains("false"),"CC-off retains selected subtitle track for immediate re-enable");
        check(!backend.SubtitlesVisible&&!backend.DiagnosticPropertyAsync("sub-visibility",token).GetAwaiter().GetResult().Contains("true"),"CC-off updates display state without unloading captions");
        backend.SetSubtitlesVisibleAsync(true,token).GetAwaiter().GetResult();
        check(backend.HasSubtitles&&backend.SubtitlesVisible,"CC-on restores the loaded caption track without a source reload");
        backend.SetSubtitlesVisibleAsync(false,token).GetAwaiter().GetResult();
        backend.PrepareAsync(new PlaybackSession("hidden-caption-reload",video,true,true,true,video:true,audioLocation:audio,subtitleLocation:sub),token).GetAwaiter().GetResult();
        check(!backend.SubtitlesVisible,"CC-off survives media reload such as a quality change");
        scaleValue=System.Xml.Linq.XElement.Parse(backend.DiagnosticPropertyAsync("sub-scale",token).GetAwaiter().GetResult());
        check(double.Parse(scaleValue.Value,System.Globalization.CultureInfo.InvariantCulture)==2,"subtitle size survives quality and source reloads");
        backend.PrepareAsync(new PlaybackSession("audio",audio,true,true,true),token).GetAwaiter().GetResult();
        tracks=backend.DiagnosticPropertyAsync("track-list",token).GetAwaiter().GetResult();
        check(!tracks.Contains("video") && !tracks.Contains("sub") && !backend.HasVideo,"switching to audio clears video, external audio and old subtitles");
        backend.PlayAsync(token).GetAwaiter().GetResult(); Thread.Sleep(300);
        check(backend.Snapshot.State==BackendState.Playing,"audio recovers after video and caption state reset");
        string portrait=Path.Combine(root,"tiny-portrait.mp4");
        var generatedPortrait=ProcessRunner.Run(Path.Combine(project,"tools/bin/ffmpeg.exe"),new[]{"-nostdin","-hide_banner","-loglevel","error","-f","lavfi","-i","testsrc=size=90x160:rate=1:duration=1","-c:v","libx264","-threads","1","-pix_fmt","yuv420p","-y",portrait},token,20);
        check(generatedPortrait.ExitCode==0,"tiny portrait video fixture generated with one CPU thread");
        backend.PrepareAsync(new PlaybackSession("portrait",portrait,true,true,true,video:true),token).GetAwaiter().GetResult();
        var aspectWait=System.Diagnostics.Stopwatch.StartNew();
        while(Math.Abs(backend.VideoAspect-9d/16)>.01&&aspectWait.ElapsedMilliseconds<1000) Thread.Sleep(5);
        check(Math.Abs(backend.VideoAspect-9d/16)<.01,"PiP receives portrait source ratio from subscribed mpv events instead of assuming 16:9");
        backend.PrepareAsync(new PlaybackSession("audio-again",audio,true,true,true),token).GetAwaiter().GetResult();
        check(Math.Abs(backend.VideoAspect-16d/9)<.01,"new sources clear the previous video's aspect ratio");
        using var library=new Library(Path.Combine(root,"real-video-cover"));
        var track=new Track {Id="yt_video",SourceType="prepared-video"};new MediaChoice {Video=true}.Store(track);library.AddTo(library.Lists[0],track);
        var sources=new SourceRegistry();sources.Register(new VideoFixtureSource(video,audio,sub));
        using var actual=new MpvBackend(new Settings {MpvPath=Path.Combine(project,"tools/bin/mpv/mpv.exe")},headless:true);
        using var player=new PlaybackCoordinator(library,sources,actual,_=>{});
        var env=new PlaybackEnvironment {GameActive=true,GameIdentity=new object(),MasterVolume=0};
        void Until(Func<bool> done,string message)
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();
            while(!done()&&watch.Elapsed.TotalSeconds<6){player.Tick(env);Thread.Sleep(5);}
            check(done(),message);
        }
        player.Play(track.Id);Until(()=>actual.Snapshot.State==BackendState.Playing,"real video coordinator begins playback");
        string activeSession=actual.Snapshot.SessionId;
        env.VideoCovered=true;
        double startedAt=actual.Snapshot.Position;
        var continuous=System.Diagnostics.Stopwatch.StartNew();
        while(continuous.ElapsedMilliseconds<400){player.Tick(env);Thread.Sleep(5);}
        check(actual.Snapshot.State==BackendState.Playing&&actual.Snapshot.Position>startedAt+.15,"default covered video keeps advancing in real mpv");
        check(actual.HasVideo&&actual.Snapshot.SessionId==activeSession,"continuous covered playback keeps video track and original session");
        library.Options.PauseCoveredVideo=true;
        env.VideoCovered=true;Until(()=>actual.Snapshot.State==BackendState.Paused,"real mpv automatically pauses covered video");
        double pausedAt=actual.Snapshot.Position;Thread.Sleep(200);
        check(Math.Abs(actual.Snapshot.Position-pausedAt)<.15&&player.Intent==PlayIntent.Playing,"real covered video holds its timeline without changing user intent");
        env.VideoCovered=false;Until(()=>actual.Snapshot.State==BackendState.Playing,"real mpv resumes uncovered video");
        check(actual.Snapshot.SessionId==activeSession,"automatic cover pause and resume keep the original mpv session");
        library.Options.PauseCoveredVideo=false;
        for(int i=0;i<8;i++){player.Tick(env);Thread.Sleep(5);}
        actual.SeekAsync(.5,token).GetAwaiter().GetResult();
        Until(()=>actual.Snapshot.Position>=.4&&actual.Snapshot.Position<1,"real quality fixture seeks to a playback bookmark");
        double bookmark=actual.Snapshot.Position;
        check(player.ChangeVideoQuality(track.Id,480),"real video accepts direct quality reload");
        Until(()=>actual.Snapshot.State==BackendState.Playing&&actual.Snapshot.SessionId!=activeSession,"real mpv quality reload resumes the same track");
        check(actual.Snapshot.Position>=bookmark-.15&&actual.Snapshot.Position<bookmark+1&&player.CurrentId==track.Id,"real mpv quality reload restores the bookmark instead of restarting at zero");
        string reloadedTracks=actual.DiagnosticPropertyAsync("track-list",token).GetAwaiter().GetResult();
        check(reloadedTracks.Contains("video")&&reloadedTracks.Contains("audio")&&actual.HasVideo,"quality reload retains both decoded video and audio tracks");
        // Drain the completed Play operation before testing a steady paused drag.
        for(int i=0;i<8;i++){player.Tick(env);Thread.Sleep(5);}
        player.Pause();Until(()=>actual.Snapshot.State==BackendState.Paused,"video timeline fixture pauses without replacing its session");
        for(int i=0;i<8;i++){player.Tick(env);Thread.Sleep(5);}
        var timeline=new VideoTimeline();
        timeline.Update(player.CurrentId,player.Snapshot.SessionId,player.CanSeek,player.IsLive,player.Position,player.Duration);
        activeSession=actual.Snapshot.SessionId;double previewBefore=player.Position;
        timeline.Begin(.55);timeline.Move(.6);
        check(Math.Abs(player.Position-previewBefore)<.05,"dragging video timeline leaves real mpv paused at its original position until release");
        double target;check(timeline.Commit(out target),"video timeline release produces one real seek target");
        player.Seek(target);
        Until(()=>Math.Abs(actual.Snapshot.Position-target)<.15,"video timeline target reaches the real mpv video/audio timeline");
        check(player.Intent==PlayIntent.Paused&&actual.Snapshot.State==BackendState.Paused&&actual.Snapshot.SessionId==activeSession,"timeline seeking preserves manual pause and the active video session");
    }
    private sealed class VideoFixtureSource : IMediaSource
    {
        private readonly string video,audio,sub;
        public VideoFixtureSource(string video,string audio,string sub){this.video=video;this.audio=audio;this.sub=sub;}
        public string Id=>"prepared-video";
        public SourceCapabilities Capabilities=>SourceCapabilities.Sessions;
        public bool CanHandle(string input)=>false;
        public Task<PlaybackSession> CreateSessionAsync(Track track,CancellationToken token)=>Task.FromResult(new PlaybackSession(track.Id,video,true,true,true,video:true,audioLocation:audio,subtitleLocation:sub));
        public Task<Track> GetTrackAsync(string input,CancellationToken token)=>throw new NotSupportedException();
        public Task<PlaylistPage> GetPlaylistPageAsync(string input,string cursor,int size,CancellationToken token)=>throw new NotSupportedException();
        public Task<DependencyInfo> CheckDependencyAsync(CancellationToken token)=>Task.FromResult(new DependencyInfo {Available=true});
    }
    public static int Fixture(string[] args)
    {
        if (!args.Contains("--ignore-config") || !args.Contains("--no-cookies") || !args.Contains("--no-cookies-from-browser")
            || !args.Contains("--no-playlist") || args.Contains("--exec")) return 44;
        string id = args.Last().Split('=').Last();
        PlaybackLatencyTests.CountFixtureResolve(id);
        bool video = args.Contains("--format") && args[Array.IndexOf(args,"--format")+1].Contains("bestvideo");
        string media = "https://media.googlevideo.com/videoplayback?expire=4102444800&sig=secret";
        bool hlsAudio = id == "HlsVideo001" || id == "HlsVideo002" || id == "HlsLive0001";
        bool live = id == "LiveMedia01" || id == "HlsLive0001";
        string protocol = hlsAudio ? id == "HlsVideo002" ? "m3u8" : "m3u8_native" : "https";
        var info = new {
            id, title = "完整媒體測試", duration = live ? (double?)null : 12,
            is_live = live, live_status = live ? "is_live" : id == "Upcoming001" ? "is_upcoming" : "not_live",
            availability = "public", url = video ? null : media, vcodec = "none", acodec = "opus", protocol = "https",
            requested_formats = video ? new[] {
                new {url = media, vcodec="avc1", acodec="none", protocol, height=360},
                new {url = media + "&audio=1", vcodec="none", acodec=hlsAudio ? null : "opus", protocol, height=0} } : null,
            subtitles = new Dictionary<string,object> { [id=="SuffixSub01"?"zh-TW-_QAj65iJXPo":"zh-TW"] = new[] { new { ext="vtt", url= id == "BadSubs0001" ? "file:///C:/secret" : "https://www.youtube.com/api/timedtext?v=" + id + "&sig=secret" } } },
            automatic_captions = new Dictionary<string,object> { ["en"] = new[] { new { ext="vtt", url="https://www.youtube.com/api/timedtext?v=" + id + "&lang=en" } } },
            http_headers = new Dictionary<string,string> { ["User-Agent"] = "RimTube fixture" }
        };
        if (args.Contains("--skip-download")) { Console.WriteLine(JsonSerializer.Serialize(info)); return 0; }
        if(id=="SuffixSub01"&&(!args.Contains("--sub-langs")||args[Array.IndexOf(args,"--sub-langs")+1]!="^"+System.Text.RegularExpressions.Regex.Escape("zh-TW-_QAj65iJXPo")+"$"))
        {Console.Error.WriteLine("Expected full anchored caption source key");return 1;}
        string output = args[Array.IndexOf(args,"--output") + 1];
        if(id.StartsWith("SlowPar",StringComparison.Ordinal)) {File.WriteAllText(Path.Combine(Path.GetDirectoryName(output),"started.marker"),"started");Thread.Sleep(15000);return 0;}
        if (id == "SlowMedia01") { Thread.Sleep(15000); return 0; }
        if (id == "BadDownload") { Console.Error.WriteLine("ERROR https://secret.example/?token=hidden"); return 1; }
        string file = output.Replace("%(ext)s",video ? "mkv" : "ogg");
        ModernTests.Wave(file,1);
        if (args.Contains("--write-subs") || args.Contains("--write-auto-subs")) File.WriteAllText(Path.ChangeExtension(file,"zh-TW.vtt"),"WEBVTT\n\n00:00.000 --> 00:01.000\n字幕測試\n");
        Console.WriteLine("[download] 100%"); return 0;
    }
    public static void Run(Action<bool,string> check, string root)
    {
        check(SubtitleLanguage.Normalize("zh-TW-_QAj65iJXPo")=="zh-TW"&&SubtitleLanguage.Normalize("en-orig")=="en","caption identifiers normalize source suffix and original-language marker");
        check(SubtitleLanguage.SameScript("zh-TW","zh-Hant")&&SubtitleLanguage.SameScript("zh-CN","zh-Hans"),"caption aliases match within the same Chinese script");
        check(!SubtitleLanguage.SameScript("zh-TW","zh-CN")&&SubtitleLanguage.Normalize("invalid_language")=="","caption normalization keeps Chinese scripts distinct and rejects invalid menu values");
        var track = YouTubeSourceAdapter.Identify("https://youtu.be/MediaTest01");
        var choice = new MediaChoice { Video=true, Height=360, Delivery=MediaDelivery.Download, Subtitle="zh-TW", AutoSubtitles=true };
        choice.Store(track);
        var copy = MediaChoice.From(track);
        check(copy.Video && copy.Height==360 && copy.Delivery==MediaDelivery.Download && copy.Subtitle=="zh-TW" && copy.AutoSubtitles,
            "media options round-trip independently of signed media URLs");
        var source = new YouTubeSourceAdapter(new Settings { YtDlp=Environment.ProcessPath });
        var suffixed=source.GetTrackAsync("https://youtu.be/SuffixSub01",CancellationToken.None,new MediaChoice {Video=true,Subtitle="zh-TW"}).GetAwaiter().GetResult();
        check(source.CreateSessionAsync(suffixed,CancellationToken.None).GetAwaiter().GetResult().SubtitleLocation.Contains("timedtext"),"Traditional Chinese selection matches YouTube caption keys with source suffixes");
        check(suffixed.Metadata["media.subtitleLanguages"].Split(',').Contains("zh-TW")&&!suffixed.Metadata["media.subtitleLanguages"].Contains("_QAj"),"caption language menu exposes valid language codes instead of source identifiers");
        var metadata = source.GetTrackAsync(track.Url, CancellationToken.None, choice).GetAwaiter().GetResult();
        choice.Delivery=MediaDelivery.Stream; choice.Store(metadata);
        var session = source.CreateSessionAsync(metadata,CancellationToken.None).GetAwaiter().GetResult();
        check(session.HasVideo && session.AudioLocation.Contains("audio=1") && session.SubtitleLocation.Contains("timedtext"),
            "video resolution returns split A/V and selected subtitle in ephemeral session");
        foreach (string id in new[] {"HlsVideo001", "HlsVideo002", "HlsLive0001"})
        {
            var hls = source.GetTrackAsync("https://youtu.be/" + id, CancellationToken.None, choice).GetAwaiter().GetResult();
            var hlsSession = source.CreateSessionAsync(hls, CancellationToken.None).GetAwaiter().GetResult();
            check(hlsSession.HasVideo && hlsSession.AudioLocation.Contains("audio=1"),
                "split HLS video retains audio when codec is deferred to manifest: " + id);
            if (id == "HlsLive0001") check(hlsSession.IsLive && !hlsSession.CanSeek, "deferred live HLS audio keeps live playback semantics");
        }
        var live = source.GetTrackAsync("https://youtu.be/LiveMedia01",CancellationToken.None,choice).GetAwaiter().GetResult();
        var liveSession = source.CreateSessionAsync(live,CancellationToken.None).GetAwaiter().GetResult();
        check(liveSession.IsLive && !liveSession.CanSeek, "public live video streams without pretending seekable VOD");
        foreach(string id in new[] {"Upcoming001","BadSubs0001"})
        {
            try { source.GetTrackAsync("https://youtu.be/"+id,CancellationToken.None,choice).GetAwaiter().GetResult(); check(false,"invalid source"); }
            catch(MediaException e) { check(e.Code==SourceErrorCode.Unsupported,"upcoming or invalid caption URL is rejected: "+id); }
        }
        choice.Subtitle="en";
        var auto = source.GetTrackAsync(track.Url,CancellationToken.None,choice).GetAwaiter().GetResult();
        check(source.CreateSessionAsync(auto,CancellationToken.None).GetAwaiter().GetResult().SubtitleLocation.Contains("lang=en"), "automatic captions are optional fallback for requested language");
        choice.Subtitle="off"; choice.Store(auto);
        check(source.CreateSessionAsync(auto,CancellationToken.None).GetAwaiter().GetResult().SubtitleLocation=="", "subtitle-off avoids loading caption URL");
        using var library = new Library(Path.Combine(root,"full-media"));
        library.AddTo(library.Lists[0],metadata); library.Save();
        var saved = File.ReadAllText(library.StateFile);
        check(!saved.Contains("googlevideo") && !saved.Contains("timedtext") && !saved.Contains("sig=secret"), "video URLs, captions URLs and signatures never persist");
        library.Options.YtDlp = Environment.ProcessPath; library.Options.Ffmpeg = Environment.ProcessPath;
        var suffixDownload=new MediaDownloader(library.Options).Download(suffixed,new MediaChoice {Video=true,Subtitle="zh-TW"},Path.Combine(root,"suffix-caption-download"),CancellationToken.None);
        check(File.Exists(suffixDownload.SubtitlePath),"caption download matches the full anchored suffixed source key");
        using (var jobs = new JobQueue(library))
        {
            var downloadChoice = new MediaChoice { Video=true, Height=360, Delivery=MediaDelivery.Download, Subtitle="zh-TW" };
            var job = jobs.EnqueueDownload(metadata,downloadChoice);
            check(ReferenceEquals(job,jobs.EnqueueDownload(metadata,downloadChoice)), "duplicate explicit video downloads coalesce");
            void Pump(Func<bool> done)
            {
                var watch=System.Diagnostics.Stopwatch.StartNew();
                while(!done() && watch.Elapsed.TotalSeconds<12) { jobs.Pump(); Thread.Sleep(10); }
                jobs.Pump(); check(done(),"download queue transition");
            }
            Pump(()=>job.State==JobState.Complete || job.State==JobState.Failed);
            check(job.State==JobState.Complete && SavedMedia.Ready(library,metadata,true) && SavedMedia.SubtitlePath(library,metadata,true)!="", "video download atomically commits media and subtitle into separate storage");
            downloadChoice.Store(metadata);
            var offlineSource = new YouTubeSourceAdapter(new Settings(),true,library);
            var local=offlineSource.CreateSessionAsync(metadata,CancellationToken.None).GetAwaiter().GetResult();
            check(local.IsLocal && local.HasVideo && local.SubtitleLocation.EndsWith(".vtt"), "download-first playback opens saved video without contacting resolver");
            var audioChoice=new MediaChoice { Delivery=MediaDelivery.Download };
            var audioJob=jobs.EnqueueDownload(metadata,audioChoice);
            Pump(()=>audioJob.State==JobState.Complete || audioJob.State==JobState.Failed);
            check(audioJob.State==JobState.Complete && library.IsReady(metadata.Id) && SavedMedia.Ready(library,metadata,true), "audio and video copies coexist without deleting each other");
            audioChoice.Store(metadata);
            var savedAudio = new YouTubeSourceAdapter(new Settings(),false,library).CreateSessionAsync(metadata,CancellationToken.None).GetAwaiter().GetResult();
            check(savedAudio.IsLocal && !savedAudio.HasVideo,"saved YouTube audio can use local-only backend without a resolver");
            var slow=YouTubeSourceAdapter.Identify("https://youtu.be/SlowMedia01"); library.AddTo(library.Lists[0],slow);
            var slowJob=jobs.EnqueueDownload(slow,audioChoice); jobs.Pump(); jobs.Cancel(slowJob);
            Pump(()=>slowJob.State==JobState.Canceled);
            check(!library.IsReady(slow.Id) && !Directory.EnumerateDirectories(library.TempDir).Any(), "cancelled download cannot commit partial files and cleans private temp");
            var liveJob=jobs.EnqueueDownload(live,audioChoice); library.AddTo(library.Lists[0],live);
            Pump(()=>liveJob.State==JobState.Failed);
            check(liveJob.Progress.Contains("直播"), "download queue refuses unbounded live recording");
            var changed = downloadChoice.Copy(); changed.Height=720;
            var one=jobs.EnqueueDownload(metadata,downloadChoice);
            var different=jobs.EnqueueDownload(metadata,changed);
            check(!ReferenceEquals(one,different),"different resolution requests are not silently coalesced into old download");
            jobs.Cancel(one); jobs.Cancel(different);
        }
        using(var reopen = new JobQueue(library))
            check(reopen.Items.Any(j=>j.ExplicitYouTube && j.State==JobState.Interrupted),"explicit unfinished downloads persist without automatically restarting");
        var shutdown = new JobQueue(library);
        var exitTrack=YouTubeSourceAdapter.Identify("https://youtu.be/SlowMedia01"); library.AddTo(library.Lists[0],exitTrack);
        var exitJob=shutdown.EnqueueDownload(exitTrack,new MediaChoice()); shutdown.Pump();
        Thread.Sleep(100); shutdown.Dispose();
        check(exitJob.State==JobState.Canceled && !Directory.EnumerateDirectories(library.TempDir).Any(),"queue disposal cancels active resolver and drains temp cleanup without a game");
        string oldVideo=SavedMedia.PathFor(library,metadata,true);
        metadata.Metadata["saved.video"]="../escape.mkv";
        try { SavedMedia.PathFor(library,metadata,true); check(false,"saved video traversal"); }
        catch(InvalidDataException) { check(File.Exists(oldVideo),"saved-media path traversal is rejected without touching existing files"); }
        metadata.Metadata["saved.video"]=Path.GetFileName(oldVideo);
        library.DeleteVideo(metadata);
        check(!File.Exists(oldVideo) && library.IsReady(metadata.Id),"deleting saved video preserves independent audio and playlist metadata");
    }
    public static void ParallelDownloads(Action<bool,string> check,string root)
    {
        using var library=new Library(Path.Combine(root,"parallel-downloads"));
        library.Options.YtDlp=Environment.ProcessPath;library.Options.Ffmpeg=Environment.ProcessPath;
        using var queue=new JobQueue(library);
        var jobs=new List<AudioJob>();
        foreach(string id in new[]{"Parallel001","Parallel002","Parallel003"})
        {
            var track=YouTubeSourceAdapter.Identify("https://youtu.be/"+id);library.AddTo(library.Lists[0],track);
            jobs.Add(queue.EnqueueDownload(track,new MediaChoice()));
        }
        queue.Pump();
        check(jobs.Count(j=>j.State==JobState.Running)==2&&jobs.Count(j=>j.State==JobState.Queued)==1,"download queue starts exactly two independent jobs by default");
        queue.Cancel(jobs[0]);
        var watch=System.Diagnostics.Stopwatch.StartNew();
        while(jobs.Any(j=>j.State==JobState.Queued||j.State==JobState.Running)&&watch.Elapsed.TotalSeconds<15){queue.Pump();Thread.Sleep(10);}
        check(jobs[0].State==JobState.Canceled&&jobs.Skip(1).All(j=>j.State==JobState.Complete),"cancelling one parallel download leaves other jobs able to commit");
        check(!Directory.EnumerateDirectories(library.TempDir).Any(),"parallel completions and cancellation clean all private temporary directories");
        library.Options.DownloadWorkers=3;library.Save();
        using(var reopened=new Library(library.Root)) check(reopened.Options.DownloadWorkers==3,"parallel download preference survives library reload");
        var shared=YouTubeSourceAdapter.Identify("https://youtu.be/Parallel004");library.AddTo(library.Lists[0],shared);
        var independent=YouTubeSourceAdapter.Identify("https://youtu.be/Parallel005");library.AddTo(library.Lists[0],independent);
        var first=queue.EnqueueDownload(shared,new MediaChoice {Height=144});
        var second=queue.EnqueueDownload(shared,new MediaChoice {Subtitle="zh-TW"});
        var third=queue.EnqueueDownload(independent,new MediaChoice());
        var committed=new List<string>();queue.Completed+=job=>committed.Add(job.Id);
        queue.Pump();
        check(first.State==JobState.Running&&second.State==JobState.Queued&&third.State==JobState.Running,"different versions of one track serialize without blocking other tracks");
        library.Options.DownloadWorkers=1;queue.Pump();
        check(first.State!=JobState.Canceled&&third.State!=JobState.Canceled,"lowering parallelism does not cancel active jobs");
        watch.Restart();
        while((second.State!=JobState.Complete||third.State!=JobState.Complete)&&watch.Elapsed.TotalSeconds<15){queue.Pump();Thread.Sleep(10);}
        check(first.State==JobState.Complete&&second.State==JobState.Complete&&third.State==JobState.Complete&&committed.IndexOf(first.Id)<committed.IndexOf(second.Id),"same-track versions commit in request order under parallel work");
        library.Options.DownloadWorkers=99;check(queue.Parallelism==3,"parallel queue caps invalid high settings at three workers");
        library.Options.DownloadWorkers=0;check(queue.Parallelism==1,"parallel queue keeps at least one worker");
        library.Options.DownloadWorkers=2;
        var shutdown=new JobQueue(library);var slow=new List<AudioJob>();
        foreach(string id in new[]{"SlowPar0001","SlowPar0002"})
        {
            var track=YouTubeSourceAdapter.Identify("https://youtu.be/"+id);library.AddTo(library.Lists[0],track);
            slow.Add(shutdown.EnqueueDownload(track,new MediaChoice()));
        }
        shutdown.Pump();watch.Restart();
        while(Directory.GetFiles(library.TempDir,"started.marker",SearchOption.AllDirectories).Length<2&&watch.Elapsed.TotalSeconds<8) Thread.Sleep(20);
        bool started=Directory.GetFiles(library.TempDir,"started.marker",SearchOption.AllDirectories).Length==2;
        shutdown.Dispose();
        check(started&&slow.All(j=>j.State==JobState.Canceled)&&!Directory.EnumerateDirectories(library.TempDir).Any(),"disposing a parallel queue cancels both active downloader processes and drains cleanup");
    }
    public static void DownloadReuse(Action<bool,string> check,string root)
    {
        using var library=new Library(Path.Combine(root,"download-reuse"));
        library.Options.YtDlp=Environment.ProcessPath;library.Options.Ffmpeg=Environment.ProcessPath;
        using var queue=new JobQueue(library);
        var track=YouTubeSourceAdapter.Identify("https://youtu.be/MediaTest01");library.AddTo(library.Lists[0],track);
        var audio=new MediaChoice();
        var original=queue.EnqueueDownload(track,audio);
        void Pump(AudioJob job)
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();
            while(job.State!=JobState.Complete&&job.State!=JobState.Failed&&watch.Elapsed.TotalSeconds<10){queue.Pump();Thread.Sleep(10);}
            check(job.State==JobState.Complete,"reuse fixture download completes");
        }
        Pump(original);
        string path=SavedMedia.PathFor(library,track,false),before=File.ReadAllText(library.StateFile);
        int jobs=queue.Items.Count;
        var repeated=queue.EnqueueDownload(track,audio);
        check(repeated.State==JobState.Complete&&queue.Items.Count==jobs,"completed audio is reused without adding a duplicate download job");
        audio.Height=144;
        check(queue.EnqueueDownload(track,audio).State==JobState.Complete,"audio ignores video-resolution changes when matching saved copy");
        check(SavedMedia.PathFor(library,track,false)==path&&File.ReadAllText(library.StateFile)==before,"reuse preserves saved filename and library without a new commit");
        var captions=new MediaChoice {Subtitle="zh-TW",AutoSubtitles=true};
        var subs=queue.EnqueueDownload(track,captions);check(subs.State==JobState.Queued,"different subtitle selection schedules a new copy");Pump(subs);
        check(queue.EnqueueDownload(track,captions).State==JobState.Complete,"same saved caption selection is reusable");
        File.Delete(SavedMedia.SubtitlePath(library,track,false));
        var missingSub=queue.EnqueueDownload(track,captions);check(missingSub.State==JobState.Queued,"missing requested subtitle file is downloaded again");queue.Cancel(missingSub);
        check(queue.EnqueueDownload(track,new MediaChoice()).State==JobState.Complete,"subtitle-off can reuse audio saved with captions");
        var videoChoice=new MediaChoice {Video=true,Height=360};
        var video=queue.EnqueueDownload(track,videoChoice);check(video.State==JobState.Queued,"saved audio does not count as a saved video");Pump(video);
        check(queue.EnqueueDownload(track,videoChoice).State==JobState.Complete,"same video resolution is reused");
        var larger=queue.EnqueueDownload(track,new MediaChoice {Video=true,Height=720});check(larger.State==JobState.Queued,"different requested video resolution schedules a download");queue.Cancel(larger);
        File.WriteAllBytes(SavedMedia.PathFor(library,track,true),Array.Empty<byte>());
        var empty=queue.EnqueueDownload(track,videoChoice);check(empty.State==JobState.Queued,"empty saved files do not suppress download");queue.Cancel(empty);
        File.Delete(SavedMedia.PathFor(library,track,false));
        var missing=queue.EnqueueDownload(track,new MediaChoice());check(missing.State==JobState.Queued,"missing audio file does not suppress download");queue.Cancel(missing);

        // Exercise the same batch entry point as the playlist download button.
        File.WriteAllText(SavedMedia.PathFor(library,track,false),"restored saved fixture");
        var added=YouTubeSourceAdapter.Identify("https://youtu.be/BatchNew001");library.AddTo(library.Lists[0],added);
        var pendingTrack=YouTubeSourceAdapter.Identify("https://youtu.be/BatchPen001");library.AddTo(library.Lists[0],pendingTrack);
        var pending=queue.EnqueueDownload(pendingTrack,new MediaChoice());
        check(ReferenceEquals(pending,queue.EnqueueDownload(pendingTrack,new MediaChoice {Height=144})),"pending audio ignores video-resolution changes without adding a second job");
        library.AddTo(library.Lists[0],new Track {Id="local-batch",SourceType="local"});
        library.Lists[0].Tracks.Add(track.Id); // Defensively tolerate legacy duplicate playlist rows.
        int beforeBatch=queue.Items.Count;
        var batch=queue.EnqueuePlaylist(library.Lists[0],new MediaChoice());
        check(batch.Reused==1&&batch.Added==1&&batch.Pending==1&&queue.Items.Count==beforeBatch+1,"playlist batch counts unique saved, new and pending YouTube tracks and ignores local entries");
        batch=queue.EnqueuePlaylist(library.Lists[0],new MediaChoice());
        check(batch.Reused==1&&batch.Added==0&&batch.Pending==2&&queue.Items.Count==beforeBatch+1,"repeating a playlist batch while work is queued does not duplicate pending downloads");
        check(new[]{track,added,pendingTrack}.All(t=>MediaChoice.From(t).Delivery==MediaDelivery.PreferSaved),"batch download stores saved-copy playback preference for every selected YouTube track");
        Pump(pending);Pump(queue.Items.Single(j=>j.TrackId==added.Id));
        int afterBatch=queue.Items.Count;
        batch=queue.EnqueuePlaylist(library.Lists[0],new MediaChoice());
        check(batch.Reused==3&&batch.Added==0&&batch.Pending==0&&queue.Items.Count==afterBatch,"repeating a completed playlist batch reuses all three downloads without new jobs");
        var shared=new Playlist {Name="Shared songs"};library.Lists.Add(shared);library.AddTo(shared,added);
        check(queue.EnqueuePlaylist(shared,new MediaChoice()).Reused==1&&queue.Items.Count==afterBatch,"the same saved song is reused across different playlists");
        using(var restored=new Library(library.Root))
        using(var restoredQueue=new JobQueue(restored))
            check(restoredQueue.EnqueueDownload(restored.FindTrack(added.Id),new MediaChoice()).State==JobState.Complete,"saved download reuse survives library and queue reload");

        var late=YouTubeSourceAdapter.Identify("https://youtu.be/BatchLate01");library.AddTo(library.Lists[0],late);
        var queued=queue.EnqueueDownload(late,new MediaChoice());
        late.FileName="late-restored.ogg";late.Metadata["saved.audio"]=late.FileName;
        File.WriteAllText(SavedMedia.PathFor(library,late,false),"restored after enqueue");library.Save();
        int completed=0;queue.Completed+=job=>{if(ReferenceEquals(job,queued))++completed;};
        queue.Pump();queue.Pump();
        check(queued.State==JobState.Complete&&queue.ActiveCount==0&&completed==1,"execution-time recheck skips an already restored copy and notifies playback exactly once");
        check(late.FileName=="late-restored.ogg"&&!Directory.EnumerateDirectories(library.TempDir).Any(),"skipped queued download preserves the restored file without a worker or temporary directory");
    }
}
