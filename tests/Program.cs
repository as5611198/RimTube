using RimTube;
using RimTube.Core;
using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

internal static class Program
{
    static int passed;
    static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/tests/" + Guid.NewGuid().ToString("N")));
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); passed++; Console.WriteLine("PASS " + message); }
    static void Throws<T>(Action action, string name) where T : Exception
    { try { action(); throw new Exception("Expected " + typeof(T).Name + ": " + name); } catch (T) { passed++; Console.WriteLine("PASS " + name); } }
    static string Temp(string name) { string p = Path.Combine(Root, name); Directory.CreateDirectory(p); return p; }
    static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        if(args.Contains("--bundled-download-smoke")) return FullYoutubeTests.NetworkDownload(args[2],args[3]=="video",Path.GetFullPath(args[1]));
        if(args.Contains("--tool-update-network"))
        {
            try{Directory.CreateDirectory(Root);ToolUpdateTests.Network(Check,Root);Console.WriteLine("TOTAL "+passed+" official update assertions passed.");return 0;}
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
        if(args.Contains("--tool-update-tests"))
        {
            try{Directory.CreateDirectory(Root);ToolUpdateTests.Run(Check,Root);Console.WriteLine("TOTAL "+passed+" tool update assertions passed.");return 0;}
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
        if(args.Contains("--bundled-network-smoke"))
        {
            try{PortableRuntimeTests.Network(Check,Path.GetFullPath(args[1]),args[2]);Console.WriteLine("TOTAL "+passed+" bundled network assertions passed.");return 0;}
            catch(Exception e){Console.Error.WriteLine(YoutubeClient.CleanError(e.Message));return 1;}
        }
        if(args.Contains("--bundled-tools-smoke"))
        {
            try{Directory.CreateDirectory(Root);PortableRuntimeTests.Smoke(Check,Root,Path.GetFullPath(args.Last()));Console.WriteLine("TOTAL "+passed+" bundled smoke assertions passed.");return 0;}
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
        if(args.Contains("--portable-tests"))
        {
            try{Directory.CreateDirectory(Root);PortableRuntimeTests.Run(Check,Root);Console.WriteLine("TOTAL "+passed+" portable runtime assertions passed.");return 0;}
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
        if(args.Contains("--localization-tests"))
        {
            try {Directory.CreateDirectory(Root);LocalizationTests.Run(Check,Root);Console.WriteLine("TOTAL "+passed+" localization assertions passed.");return 0;}
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
        if(args.Contains("--media-collection-tests")||args.Contains("--media-removal-tests")||args.Contains("--subtitle-size-tests"))
        {
            try{
                Directory.CreateDirectory(Root);
                if(args.Contains("--media-collection-tests"))MediaLibraryTests.Collections(Check,Root);
                if(args.Contains("--media-removal-tests"))MediaLibraryTests.Removal(Check,Root);
                if(args.Contains("--subtitle-size-tests"))MediaLibraryTests.SubtitlePreferences(Check,Root);
                Console.WriteLine("TOTAL "+passed+" media library assertions passed.");return 0;
            }catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
        if(args.Contains("--bgm-tests"))
        {
            try { Directory.CreateDirectory(Root);ModernTests.BgmOwnership(Check,Root);Console.WriteLine("TOTAL "+passed+" BGM assertions passed.");return 0; }
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
        if(args.Contains("--format-selection-tests"))
        {
            try { StreamFormatTests.Run(Check,Root);Console.WriteLine("TOTAL "+passed+" format assertions passed.");return 0; }
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
        if(args.Contains("--selected-video-stream-test"))
        {int at=Array.IndexOf(args,"--selected-video-stream-test");return FullYoutubeTests.NetworkVideoAudio(args[at+1],int.Parse(args[at+2]),args[at+3]);}
        if(args.Contains("--caption-stream-test")) return FullYoutubeTests.NetworkCaptions(args.Last());
        if(args.Contains("--quality-tests"))
        {
            try {Directory.CreateDirectory(Root);PlaybackQualityTests.Run(Check,Root);Console.WriteLine("TOTAL "+passed+" quality assertions passed.");return 0;}
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
        if(args.Contains("--latency-stream-test")) return PlaybackLatencyTests.Live(args.Last());
        if(args.Contains("--latency-tests"))
        {
            try { Directory.CreateDirectory(Root); PlaybackLatencyTests.Run(Check,Root); Console.WriteLine("TOTAL "+passed+" latency assertions passed."); return 0; }
            catch(Exception e) { Console.Error.WriteLine(e); return 1; }
        }
        if(args.Contains("--ui-tests"))
        {
            try { Directory.CreateDirectory(Root); UiTests.Run(Check,Root); Console.WriteLine("TOTAL "+passed+" UI assertions passed."); return 0; }
            catch(Exception e) { Console.Error.WriteLine(e); return 1; }
        }
        if(args.Contains("--game-music-tests"))
        {
            try {Directory.CreateDirectory(Root);GameMusicTests.Run(Check,Root);Console.WriteLine("TOTAL "+passed+" game music assertions passed.");return 0;}
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
        if(args.Contains("--download-reuse-tests"))
        {
            try { Directory.CreateDirectory(Root); FullYoutubeTests.DownloadReuse(Check,Root);Console.WriteLine("TOTAL "+passed+" reuse assertions passed.");return 0; }
            catch(Exception e) { Console.Error.WriteLine(e);return 1; }
        }
        if(args.Contains("--experience-tests"))
        {
            try { Directory.CreateDirectory(Root); ModernTests.Experience(Check,Root); Console.WriteLine("TOTAL "+passed+" experience assertions passed."); return 0; }
            catch(Exception e) { Console.Error.WriteLine(e);return 1; }
        }
        if(args.Contains("--parallel-tests"))
        {
            try { Directory.CreateDirectory(Root); FullYoutubeTests.ParallelDownloads(Check,Root); Console.WriteLine("TOTAL "+passed+" parallel assertions passed."); return 0; }
            catch(Exception e) { Console.Error.WriteLine(e);return 1; }
        }
        if(args.Contains("--full-yt-download-audio-live")) return FullYoutubeTests.NetworkDownload(args.Last(),false);
        if(args.Contains("--full-yt-download-live")) return FullYoutubeTests.NetworkDownload(args.Last());
        if(args.Contains("--full-yt-metadata-live")) return FullYoutubeTests.NetworkMetadata(args.Last());
        if(args.Contains("--video-audio-stream-test")) return FullYoutubeTests.NetworkVideoAudio(args.Last());
        if (args.Contains("--full-backend-tests"))
        {
            try { Directory.CreateDirectory(Root); VideoSurfaceTests.Run(Check); FullYoutubeTests.Backend(Check,Root); Console.WriteLine("TOTAL " + passed + " video backend assertions passed."); return 0; }
            catch(Exception e) { Console.Error.WriteLine(e); return 1; }
        }
        if (args.Contains("--no-cookies") && !args.Contains("--flat-playlist") && (args.Contains("--no-simulate") || new[] {"MediaTest01", "SuffixSub01", "LiveMedia01", "HlsVideo001", "HlsVideo002", "HlsLive0001", "Upcoming001", "BadSubs0001", "SlowMedia01", "BadDownload"}.Any(id=>args.Last().EndsWith("="+id)))) return FullYoutubeTests.Fixture(args);
        if (args.Contains("--full-media-fixture")) return FullYoutubeTests.Fixture(args.Where(a=>a!="--full-media-fixture").ToArray());
        if (args.Contains("--full-yt-tests"))
        {
            try { Directory.CreateDirectory(Root); FullYoutubeTests.Run(Check, Root); Console.WriteLine("TOTAL " + passed + " full media assertions passed."); return 0; }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
        if (args.Contains("--yt-playlist-live")) return YoutubePlaylistTests.Live(args.Last());
        if (args.Contains("--flat-playlist") && args.Contains("--dump-single-json")) return YoutubePlaylistTests.Fixture(args);
        if (args.Contains("--yt-playlist-tests"))
        {
            try { Directory.CreateDirectory(Root); YoutubePlaylistTests.Run(Check, Root); Console.WriteLine("TOTAL " + passed + " playlist assertions passed."); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        if (args.Contains("--yt-stream-live")) return YoutubeStreamingTests.Live(args.Last());
        if (args.Contains("--dump-single-json") && args.Contains("--format")) return YoutubeStreamingTests.Fixture(args);
        if (args.Contains("--yt-tests"))
        {
            try { Directory.CreateDirectory(Root); YoutubeStreamingTests.Run(Check, Root); Console.WriteLine("TOTAL " + passed + " YouTube assertions passed."); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        if (args.Contains("--mpv-orphan"))
        {
            var backend = new MpvBackend(new Settings { MpvPath = args[1] });
            backend.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            File.WriteAllText(args[2], backend.ProcessId.ToString());
            Environment.Exit(0); // Deliberately bypass Dispose to exercise Windows job ownership.
        }
        if (args.Contains("--fixture-stall")) { Console.WriteLine("ready"); Console.Out.Flush(); Thread.Sleep(15000); return 0; }
        if (args.Contains("--fixture-echo")) { Console.WriteLine(String.Join("\n", args.Skip(1))); return 0; }
        if (args.Contains("--dump-single-json"))
        {
            Console.WriteLine("{\"id\":\"BaW_jenozKc\",\"title\":\"test\",\"duration\":10}"); return 0;
        }
        if (args.Contains("--output"))
        {
            string output = args[Array.IndexOf(args, "--output") + 1].Replace("%(ext)s", "ogg");
            File.WriteAllText(output, "fixture-media-not-a-playback-test"); Console.WriteLine("[download] 100%"); return 0;
        }
        if (args.Contains("-nostdin")) { Console.Error.WriteLine("Fixture input is invalid"); return 2; }
        if (args.Contains("--youtube-live")) return LiveYoutube(args.Last());
        if (args.Contains("--playlist-live")) return LivePlaylist(args.Last());
        try
        {
            Directory.CreateDirectory(Root);
            ToolUpdateTests.Run(Check,Root);PortableRuntimeTests.Run(Check,Root);LocalizationTests.Run(Check,Root);TestLibrary(); TestSync(); TestPolicy(); TestProcess(); TestJobs(); YoutubeStreamingTests.Run(Check, Root); YoutubePlaylistTests.Run(Check, Root); FullYoutubeTests.Run(Check,Root); ModernTests.Run(Check, Root, args); ModernTests.Experience(Check,Root); FullYoutubeTests.ParallelDownloads(Check,Root);FullYoutubeTests.DownloadReuse(Check,Root);GameMusicTests.Run(Check,Root);UiTests.Run(Check,Root);PlaybackLatencyTests.Run(Check,Root);PlaybackQualityTests.Run(Check,Root);
            ModernTests.BgmOwnership(Check,Root);
            MediaLibraryTests.Collections(Check,Root);MediaLibraryTests.Removal(Check,Root);MediaLibraryTests.SubtitlePreferences(Check,Root);
            Console.WriteLine("TOTAL " + passed + " assertions passed. Artifacts: " + Root); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static int LiveYoutube(string url)
    {
        string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        string bin = Path.Combine(project, "Tools/bin");
        var client = new YoutubeClient(new Settings { YtDlp = Path.Combine(bin, "yt-dlp.exe"), Ffmpeg = Path.Combine(bin, "ffmpeg.exe"), JsRuntime = Path.Combine(bin, "deno.exe") });
        string dir = Path.Combine(project, "artifacts/youtube-live"); Directory.CreateDirectory(dir);
        try
        {
            var info = client.Inspect(url, false, CancellationToken.None);
            Console.WriteLine("Metadata OK: " + info.Title);
            client.Download(new Track { Id = "yt_" + info.Id, Url = YoutubeClient.Normalize(url, false) }, dir, CancellationToken.None, Console.WriteLine);
            var r = ProcessRunner.Run(Path.Combine(bin, "ffprobe.exe"), new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1", Path.Combine(dir, "audio.ogg") }, CancellationToken.None, 30);
            Console.WriteLine("ffprobe: " + r.Output.Trim()); return r.ExitCode;
        }
        catch (Exception ex) { Console.Error.WriteLine(YoutubeClient.CleanError(ex.Message)); return 1; }
    }
    static int LivePlaylist(string url)
    {
        string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        string bin = Path.Combine(project, "Tools/bin");
        try
        {
            var client = new YoutubeClient(new Settings { YtDlp = Path.Combine(bin, "yt-dlp.exe"), Ffmpeg = Path.Combine(bin, "ffmpeg.exe"), JsRuntime = Path.Combine(bin, "deno.exe") });
            var info = client.Inspect(url, true, CancellationToken.None);
            if (info.Entries == null || info.Entries.Count == 0) throw new Exception("No playlist entries returned");
            var lib = new Library(Path.Combine(project, "artifacts/playlist-live/" + Guid.NewGuid().ToString("N")));
            var list = lib.Lists[0]; list.SourceUrl = YoutubeClient.Normalize(url, true);
            foreach (var entry in info.Entries)
            {
                if (entry == null || String.IsNullOrEmpty(entry.Id)) continue;
                lib.Tracks["yt_" + entry.Id] = new Track { Id = "yt_" + entry.Id, Title = entry.Title ?? entry.Id, Url = "https://www.youtube.com/watch?v=" + entry.Id };
            }
            var ids = lib.Tracks.Keys.ToList(); lib.ApplySync(list, Library.Diff(list, ids), true, false, true);
            var saved = new Library(lib.Root);
            var diff = Library.Diff(saved.Lists[0], ids);
            if (diff.Added.Count != 0 || diff.Removed.Count != 0 || diff.OrderChanged) throw new Exception("Saved playlist did not roundtrip");
            Console.WriteLine("Real playlist metadata/import/persistence/no-op sync PASS: " + info.Title + " (" + ids.Count + " entries). No batch audio downloaded.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(YoutubeClient.CleanError(ex.Message)); return 1; }
    }
    static void TestLibrary()
    {
        var lib = new Library(Temp("library"));
        var t = new Track { Id = "yt_BaW_jenozKc", Title = "測試 & <曲目>", FileName = "sample.ogg", Url = "https://www.youtube.com/watch?v=BaW_jenozKc" };
        File.WriteAllText(lib.AudioPath(t), "sample");
        lib.AddTo(lib.Lists[0], t); lib.AddTo(lib.Lists[0], t);
        lib.Options.Battle = BattleMode.Custom; lib.Options.Volume = .33f; lib.Options.YtDlp = @"I:\含 空格\yt-dlp.exe";
        lib.Save(); lib.Save();
        var read = new Library(lib.Root);
        Check(!read.ReadOnly && read.FindTrack(t.Id).Title == t.Title, "XML roundtrip preserves Unicode and escaped characters");
        Check(read.Lists[0].Tracks.Count == 1, "duplicate video is one library entry and one list item");
        Check(read.Options.Battle == BattleMode.Custom && Math.Abs(read.Options.Volume - .33f) < .0001f, "settings roundtrip");
        Check(File.Exists(lib.StateFile + ".bak"), "atomic saves retain previous state");
        read.Lists[0].Tracks.Clear(); read.Save();
        Check(File.Exists(read.AudioPath(read.FindTrack(t.Id))), "removing a list item preserves audio");
        read.DeleteAudio(read.FindTrack(t.Id));
        Check(read.FindTrack(t.Id) != null && !read.IsReady(t.Id), "manual audio deletion retains metadata for redownload");
        Throws<InvalidDataException>(() => lib.AudioPath(new Track { FileName = "../outside.ogg" }), "path traversal rejected");
        string future = Path.Combine(Temp("future"), "library.xml"); File.WriteAllText(future, "<RimTube schema=\"99\" />");
        var newer = new Library(Path.GetDirectoryName(future));
        Check(newer.ReadOnly, "future schema is protected from writes");
        Throws<InvalidOperationException>(() => newer.Save(), "future schema cannot be overwritten");
        Check(File.ReadAllText(future).Contains("99"), "future library remains intact");
        string damaged = Path.Combine(Temp("damaged"), "library.xml"); File.WriteAllText(damaged, "<bad");
        Check(new Library(Path.GetDirectoryName(damaged)).ReadOnly, "corrupt library does not silently become writable empty data");
        using (var first = new Library(Temp("lock")))
        {
            Check(first.AcquireWriteLock(), "first process acquires library writer lease");
            using (var second = new Library(first.Root)) Check(!second.AcquireWriteLock() && second.ReadOnly, "concurrent library writer becomes read-only");
        }
        using (var third = new Library(Path.Combine(Root, "lock"))) Check(third.AcquireWriteLock(), "writer lease releases when session closes");
    }
    static void TestSync()
    {
        var lib = new Library(Temp("sync")); var list = lib.Lists[0];
        foreach (string id in new[] { "a", "b", "local" }) { lib.AddTo(list, new Track { Id = id, Title = id, FileName = id + ".ogg" }); File.WriteAllText(Path.Combine(lib.MediaDir, id + ".ogg"), id); }
        list.SourceTracks.AddRange(new[] { "a", "b", "local" });
        var diff = Library.Diff(list, new[] { "b", "a", "c", "c" });
        Check(diff.Added.SequenceEqual(new[] { "c" }) && diff.Removed.SequenceEqual(new[] { "local" }) && diff.OrderChanged, "sync diff detects additions removals reorder and remote duplicates");
        lib.Tracks["c"] = new Track { Id = "c" };
        lib.ApplySync(list, diff, true, false, false);
        Check(list.Tracks.SequenceEqual(new[] { "a", "b", "local", "c" }), "default sync adds without deleting or reordering local entries");
        lib.ApplySync(list, Library.Diff(list, new[] { "b", "a", "c" }), false, false, true);
        Check(list.Tracks.SequenceEqual(new[] { "b", "a", "c", "local" }), "explicit reorder preserves local extras at end");
        lib.AddTo(list, new Track { Id = "manual_extra", Title = "manual addition" });
        lib.ApplySync(list, Library.Diff(list, new[] { "b", "a", "c" }), false, true, false);
        Check(!list.Tracks.Contains("local") && File.Exists(Path.Combine(lib.MediaDir, "local.ogg")), "sync removal never deletes audio");
        Check(list.Tracks.Contains("manual_extra"), "source removal preserves songs added manually outside the source");
        var restored = new Library(lib.Root).Lists[0];
        Check(restored.SourceTracks.Count == 3 && !restored.SourceTracks.Contains("manual_extra"), "source membership persists separately from manual additions");
        Check(Library.Diff(new Playlist { Tracks = new List<string> { "legacy_local" } }, Array.Empty<string>()).Removed.Count == 0, "unknown legacy source membership cannot imply deletion");
    }
    static void TestPolicy()
    {
        foreach (BattleMode mode in Enum.GetValues<BattleMode>())
        {
            foreach (bool danger in new[] { false, true })
            {
                Check(PlaybackPolicy.Decide(PlayIntent.Stopped, danger, true, mode, false, false) == PlayContext.Stopped, "manual stop has priority: " + mode + "/" + danger);
                Check(PlaybackPolicy.Decide(PlayIntent.Paused, danger, true, mode, false, false) == PlayContext.Paused, "manual pause has priority: " + mode + "/" + danger);
            }
        }
        Check(PlaybackPolicy.Decide(PlayIntent.Playing, true, true, BattleMode.Vanilla, false, false) == PlayContext.Vanilla, "battle hands off to vanilla");
        Check(PlaybackPolicy.Decide(PlayIntent.Playing, true, true, BattleMode.Continue, false, false) == PlayContext.General, "continue mode ignores danger");
        Check(PlaybackPolicy.Decide(PlayIntent.Playing, true, true, BattleMode.Custom, false, false) == PlayContext.Combat, "custom battle chooses combat list");
        Check(PlaybackPolicy.Decide(PlayIntent.Playing, true, false, BattleMode.Custom, false, false) == PlayContext.Vanilla, "missing combat audio falls back to vanilla");
        Check(PlaybackPolicy.Decide(PlayIntent.Playing, false, true, BattleMode.Custom, false, false) == PlayContext.General, "battle exit resumes general context");
        Check(PlaybackPolicy.Decide(PlayIntent.Playing, false, true, BattleMode.Custom, true, true) == PlayContext.Paused, "optional game pause suspends audio");
        Check(PlaybackPolicy.Decide(PlayIntent.Playing, false, true, BattleMode.Custom, true, false) == PlayContext.General, "default game pause does not pause music");
    }
    static void TestProcess()
    {
        string valid = "https://www.youtube.com/watch?v=BaW_jenozKc";
        Check(YoutubeClient.Normalize("https://youtu.be/BaW_jenozKc?si=foo", false) == valid, "short URL normalized");
        Check(YoutubeClient.Normalize(valid + "&list=PL1234567890", false) == valid, "single import cannot accidentally download playlist");
        Check(YoutubeClient.Normalize(valid + "&list=PL1234567890", true).Contains("/playlist?list="), "playlist explicitly normalized");
        Throws<ArgumentException>(() => YoutubeClient.Normalize("https://youtube.com.evil.test/watch?v=BaW_jenozKc", false), "lookalike host rejected");
        Throws<ArgumentException>(() => YoutubeClient.Normalize("file:///C:/secret", false), "local schemes rejected for YouTube import");
        Throws<ArgumentException>(() => YoutubeClient.Normalize("https://www.youtube.com@evil.test/watch?v=BaW_jenozKc", false), "userinfo URL spoof rejected");
        Throws<ArgumentException>(() => YoutubeClient.Normalize("--exec calc.exe", false), "command argument URL rejected");
        var info = YoutubeClient.Parse("{\"id\":\"BaW_jenozKc\",\"title\":\"中文\",\"duration\":3.5,\"entries\":[null,{\"id\":\"abcdefghijk\"}]}");
        Check(info.Title == "中文" && info.Entries.Count == 2 && info.Entries[0] == null, "metadata parser handles nullable playlist entries");
        string[] values = { "含 空格", "a\"b", @"C:\path with spaces\", "$(never-execute)", "& calc.exe" };
        var result = ProcessRunner.Run(Environment.ProcessPath, new[] { "--fixture-echo" }.Concat(values), CancellationToken.None, 10);
        Check(result.ExitCode == 0 && result.Output.TrimEnd('\r', '\n').Split('\n').Select(s => s.TrimEnd('\r')).SequenceEqual(values), "process arguments remain literal including quotes and trailing slash: " + System.Text.Json.JsonSerializer.Serialize(result.Output));
        using (var cancel = new CancellationTokenSource())
        {
            cancel.CancelAfter(250);
            Throws<OperationCanceledException>(() => ProcessRunner.Run(Environment.ProcessPath, new[] { "--fixture-stall" }, cancel.Token, 10), "running process can be canceled");
        }
        Throws<TimeoutException>(() => ProcessRunner.Run(Environment.ProcessPath, new[] { "--fixture-stall" }, CancellationToken.None, 1), "hung process has timeout");
        Check(!YoutubeClient.CleanError("ERROR https://example.test/access?token=secret").Contains("secret"), "tool errors redact access-bearing URLs");
    }
    static void PumpUntil(JobQueue queue, Func<bool> done)
    {
        var timer = Stopwatch.StartNew();
        while (!done() && timer.Elapsed.TotalSeconds < 10) { queue.Pump(); Thread.Sleep(10); }
        queue.Pump(); if (!done()) throw new Exception("queue timeout");
    }
    static void TestJobs()
    {
        var lib = new Library(Temp("jobs"));
        string original = Path.Combine(Temp("source"), "original.wav");
        // Valid PCM WAV fixture: tests local copy/commit, not playback in a game engine.
        using (var writer = new BinaryWriter(File.Create(original)))
        {
            int samples = 800;
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
            for (int i = 0; i < samples; i++) writer.Write((short)(Math.Sin(i * 2 * Math.PI * 440 / 8000) * 2000));
        }
        var t = new Track { Id = "local_test", Title = "本機測試" }; lib.AddTo(lib.Lists[0], t);
        using (var jobs = new JobQueue(lib))
        {
            jobs.Enqueue(t, original, true); jobs.Enqueue(t, original, true);
            Check(jobs.Items.Count == 1, "duplicate queued imports coalesce");
            PumpUntil(jobs, () => jobs.Items[0].State == JobState.Complete);
            Check(lib.IsReady(t.Id) && File.ReadAllBytes(lib.AudioPath(t)).SequenceEqual(File.ReadAllBytes(original)), "local import commits an intact playable-format file");
            lib.DeleteAudio(t);
            Check(File.Exists(original), "manual cache cleaning preserves original source file");
            jobs.Enqueue(t, original, true); var pending = jobs.Items.Last(); jobs.Cancel(pending); jobs.Pump();
            Check(pending.State == JobState.Canceled && !lib.IsReady(t.Id), "canceling queued work cannot commit audio");
        }
        lib.Options.YtDlp = Environment.ProcessPath; lib.Options.Ffmpeg = Environment.ProcessPath;
        var yt = new Track { Id = "yt_BaW_jenozKc", Title = "fixture", Url = "https://www.youtube.com/watch?v=BaW_jenozKc" }; lib.AddTo(lib.Lists[0], yt);
        using (var jobs = new JobQueue(lib))
        {
            Throws<MediaException>(() => jobs.Enqueue(yt, yt.Url, false), "YouTube audio jobs are rejected before any external process starts");
        }
        var missing = new Track { Id = "missing", Title = "missing" }; lib.AddTo(lib.Lists[0], missing);
        using (var jobs = new JobQueue(lib))
        {
            jobs.Enqueue(missing, original + ".missing", true);
            PumpUntil(jobs, () => jobs.Items.Last().State == JobState.Failed);
            Check(!lib.IsReady(missing.Id), "failed import does not create playable entry");
        }
        using (var jobs = new JobQueue(lib))
            Check(jobs.Items.Any(j => j.TrackId == missing.Id && j.State == JobState.Interrupted), "unfinished work survives restart for explicit retry");
        Check(!Directory.EnumerateDirectories(lib.TempDir).Any(), "finished and failed jobs clean their private temporary files");
    }
}
