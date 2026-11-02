using System.Text.Json;
using RimTube.Core;
using RimTube.Sources;

internal static class YoutubeStreamingTests
{
    public static int Fixture(string[] args)
    {
        string url = args.Last();
        if (!args.Contains("--skip-download") || !args.Contains("--no-playlist") || !args.Contains("--ignore-config")
            || !args.Contains("--no-plugin-dirs") || !args.Contains("--no-remote-components") || !args.Contains("--no-cache-dir")
            || args.Any(a => a == "--cookies" || a == "--cookies-from-browser" || a == "--extract-audio" || a == "--output")) return 23;
        string id = url.Split('=').Last();
        PlaybackLatencyTests.CountFixtureResolve(id);
        if (id == "SlowSource1") { Thread.Sleep(15000); return 0; }
        if (id == "Restricted1") { Console.Error.WriteLine("Sign in required https://private.example/?token=secret"); return 1; }
        var data = new {
            id = id == "WrongId0001" ? "Different01" : id, title = "授權測試曲目", duration = 60, vcodec = id == "VideoOnly01" ? "vp9" : "none",
            acodec = id == "HlsAudio001" || id == "NoCodec0001" ? null : "opus", protocol = id == "HlsAudio001" ? "m3u8_native" : "https",
            url = id == "BadMedia001" ? "https://untrusted.example/media" : "https://media.googlevideo.com/videoplayback?expire=" + (id == "Expired0001" ? "1" : "4102444800") + "&sig=" + Guid.NewGuid().ToString("N"),
            availability = "public", is_live = id == "IsLive00001", live_status = "not_live",
            http_headers = new Dictionary<string,string> { ["User-Agent"] = id == "BadHeader01" ? "bad\r\nCookie: secret" : "RimTube fixture", ["Cookie"] = "must-not-forward" }
        };
        Console.WriteLine(JsonSerializer.Serialize(data)); return 0;
    }

    public static void Run(Action<bool,string> check, string root)
    {
        var source = new YouTubeSourceAdapter(Environment.ProcessPath);
        check((source.Capabilities & SourceCapabilities.Sessions) != 0, "YouTube adapter advertises resolved streaming sessions");
        var track = source.GetTrackAsync("https://youtu.be/BaW_jenozKc", CancellationToken.None).GetAwaiter().GetResult();
        check(track.Title == "授權測試曲目" && track.SourceId == "BaW_jenozKc" && track.Duration == 60, "YouTube metadata comes from resolver and keeps stable identity");
        var one = source.CreateSessionAsync(track, CancellationToken.None).GetAwaiter().GetResult();
        var two = source.CreateSessionAsync(track, CancellationToken.None).GetAwaiter().GetResult();
        check(!one.IsLocal && one.Authorized && one.Location != two.Location && two.ExpiresUtc > DateTime.UtcNow, "each YouTube play resolves a fresh expiring media session");
        check(one.Headers.Count == 1 && one.Headers["User-Agent"] == "RimTube fixture", "session carries safe playback headers without cookies");
        var library = new Library(Path.Combine(root, "youtube-stream")); library.AddTo(library.Lists[0], track); library.Save();
        string saved = File.ReadAllText(library.StateFile);
        check(!saved.Contains("googlevideo") && !saved.Contains("sig=") && !Directory.EnumerateFiles(library.MediaDir).Any(), "YouTube persistence stores metadata without media URLs or downloaded audio");
        void Fails(string id, SourceErrorCode code, string name)
        {
            try { source.GetTrackAsync("https://youtu.be/" + id, CancellationToken.None).GetAwaiter().GetResult(); check(false,name); }
            catch (MediaException e) { check(e.Code == code && !e.Message.Contains("token=secret"),name); }
        }
        Fails("Restricted1", SourceErrorCode.AuthorizationRequired, "restricted YouTube content fails without credentials or sensitive URL logging");
        Fails("BadHeader01", SourceErrorCode.Unsupported, "resolver header injection is rejected");
        Fails("WrongId0001", SourceErrorCode.Incomplete, "resolver cannot substitute another YouTube video");
        Fails("Expired0001", SourceErrorCode.Expired, "expired YouTube media is rejected before playback");
        Fails("BadMedia001", SourceErrorCode.Unsupported, "YouTube sessions reject unrelated media hosts");
        Fails("VideoOnly01", SourceErrorCode.Unsupported, "YouTube resolver rejects combined video formats");
        Fails("NoCodec0001", SourceErrorCode.Unsupported, "unknown direct audio codec still fails source validation");
        var hls = source.GetTrackAsync("https://youtu.be/HlsAudio001", CancellationToken.None).GetAwaiter().GetResult();
        check(!source.CreateSessionAsync(hls,CancellationToken.None).GetAwaiter().GetResult().HasVideo,
            "audio-only HLS with codec deferred to manifest plays without being rejected as missing audio");
        var live = source.GetTrackAsync("https://youtu.be/IsLive00001", CancellationToken.None).GetAwaiter().GetResult();
        var liveSession = source.CreateSessionAsync(live,CancellationToken.None).GetAwaiter().GetResult();
        check(liveSession.IsLive && !liveSession.CanSeek, "public live audio is supported without VOD seek");
        var native = new YouTubeSourceAdapter(new Settings(), false);
        try { native.CreateSessionAsync(track, CancellationToken.None).GetAwaiter().GetResult(); check(false, "native backend must reject online source"); }
        catch (MediaException e) { check(e.Code == SourceErrorCode.Unsupported && (native.Capabilities & SourceCapabilities.Sessions) == 0, "native backend excludes online candidates before starting resolver"); }
        using var cancel = new CancellationTokenSource(250);
        try { source.GetTrackAsync("https://youtu.be/SlowSource1", cancel.Token).GetAwaiter().GetResult(); check(false,"cancel resolver"); }
        catch (OperationCanceledException) { check(true,"YouTube resolution supports cancellation"); }
    }

    public static int Live(string url)
    {
        string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../"));
        var settings = new Settings { YtDlp=Path.Combine(project,"tools/bin/yt-dlp.exe"), JsRuntime=Path.Combine(project,"tools/bin/deno.exe"), MpvPath=Path.Combine(project,"tools/bin/mpv/mpv.exe") };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        try
        {
            var source = new YouTubeSourceAdapter(settings);
            var track = source.GetTrackAsync(url,timeout.Token).GetAwaiter().GetResult();
            Console.WriteLine("Metadata OK: " + track.SourceId + " · " + track.Title + " · " + track.Duration);
            var session = source.CreateSessionAsync(track,timeout.Token).GetAwaiter().GetResult();
            Console.WriteLine("Fresh stream session OK; safe headers=" + session.Headers.Count);
            using var backend = new MpvBackend(settings);
            backend.InitializeAsync(timeout.Token).GetAwaiter().GetResult();
            backend.PrepareAsync(session,timeout.Token).GetAwaiter().GetResult();
            backend.SetVolumeAsync(0,timeout.Token).GetAwaiter().GetResult();
            backend.PlayAsync(timeout.Token).GetAwaiter().GetResult();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (backend.Snapshot.Position < 5 && watch.Elapsed.TotalSeconds < 45 && backend.Snapshot.State != BackendState.Failed) Thread.Sleep(100);
            if (backend.Snapshot.Position < 5) throw new Exception("Live media did not advance: " + backend.Snapshot.State + " " + backend.Snapshot.Error);
            Console.WriteLine("PASS real YouTube audio plays through mpv; position=" + backend.Snapshot.Position.ToString("F1"));
            backend.PauseAsync(timeout.Token).GetAwaiter().GetResult();
            backend.SeekAsync(20,timeout.Token).GetAwaiter().GetResult();
            backend.ResumeAsync(timeout.Token).GetAwaiter().GetResult(); Thread.Sleep(1500);
            if (backend.Snapshot.Position < 20) throw new Exception("Live seek did not advance");
            Console.WriteLine("PASS real YouTube pause/seek/resume; position=" + backend.Snapshot.Position.ToString("F1"));
            backend.StopAsync(timeout.Token).GetAwaiter().GetResult();
            Console.WriteLine("PASS no permanent audio download requested"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL " + YoutubeClient.CleanError(ex.Message)); return 1; }
    }
}
