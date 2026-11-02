using System.Text.Json;
using RimTube.Core;
using RimTube.Sources;

internal static class YoutubePlaylistTests
{
    const string Url = "https://www.youtube.com/playlist?list=PLfixtureComplete";
    public static int Live(string url)
    {
        string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            var source = new YouTubeSourceAdapter(new Settings { YtDlp = Path.Combine(project, "tools/bin/yt-dlp.exe") });
            var preview = PlaylistReader.ReadAsync(source, url, timeout.Token).GetAwaiter().GetResult();
            Console.WriteLine("Playlist metadata: " + preview.Title + "; unique tracks=" + preview.Tracks.Count + "; complete=" + preview.Complete);
            Console.WriteLine(preview.Message);
            if (preview.Tracks.Count == 0) throw new Exception("No importable playlist metadata");
            string dir = Path.Combine(project, "artifacts/playlist-live/" + Guid.NewGuid().ToString("N"));
            using var lib = new Library(dir);
            var list = PlaylistImport.Apply(lib, null, url, preview);
            using var restored = new Library(dir);
            if (!restored.FindList(list.Id).Tracks.SequenceEqual(list.Tracks)) throw new Exception("Playlist persistence mismatch");
            Console.WriteLine("PASS imported playlist order survives reload; no audio downloaded; library=" + dir);
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("FAIL " + YoutubeClient.CleanError(e.Message)); return 1; }
    }
    public static int Fixture(string[] args)
    {
        if (!args.Contains("--flat-playlist") || !args.Contains("--skip-download") || !args.Contains("--ignore-config")
            || !args.Contains("--no-cookies") || !args.Contains("--no-cookies-from-browser") || !args.Contains("--no-cache-dir")
            || !args.Contains("--no-plugin-dirs") || !args.Contains("--no-remote-components") || args.Contains("--format")) return 23;
        string id = args.Last().Split('=').Last();
        if (id == "PLfixtureSlow") { Thread.Sleep(15000); return 0; }
        if (id == "PLfixtureDenied") { Console.Error.WriteLine("Private playlist https://secret.example/?token=hidden"); return 1; }
        object Entry(string video, string title = "測試歌曲") => new { id = video, title, duration = 12.5, availability = "public" };
        var entries = new List<object> { Entry("BaW_jenozKc"), Entry("abcdefghijk"), Entry("BaW_jenozKc"), Entry("lmnopqrstuv") };
        if (id == "PLfixturePartial") entries.Insert(1, null);
        if (id == "PLfixtureHidden") entries.Add(Entry("Private0001", "[Private video]"));
        if (id == "PLfixtureWarning") Console.Error.WriteLine("WARNING: Incomplete response https://secret.example/?token=hidden");
        if (id == "PLfixtureEmpty") entries.Clear();
        Console.WriteLine(JsonSerializer.Serialize(new { _type = "playlist", id = id == "PLfixtureWrong" ? "PLotherPlaylist" : id,
            title = "測試歌單", availability = "unlisted", playlist_count = entries.Count + (id == "PLfixtureTruncated" ? 1 : 0), entries }));
        return 0;
    }
    public static void Run(Action<bool,string> check, string root)
    {
        var source = new YouTubeSourceAdapter(Environment.ProcessPath);
        check((source.Capabilities & SourceCapabilities.PlaylistPages) != 0, "YouTube advertises playlist enumeration");
        var first = source.GetPlaylistPageAsync(Url, "", 2, CancellationToken.None).GetAwaiter().GetResult();
        check(first.Tracks.Count == 2 && first.NextPage != "" && !first.IsComplete, "YouTube snapshot supports bounded pages");
        var second = source.GetPlaylistPageAsync(Url, first.NextPage, 2, CancellationToken.None).GetAwaiter().GetResult();
        check(second.IsComplete && second.NextPage == "" && second.Tracks.Single().SourceId == "lmnopqrstuv", "snapshot pages preserve unique source order");
        var read = PlaylistReader.ReadAsync(source, Url, CancellationToken.None, 2).GetAwaiter().GetResult();
        check(read.Complete && read.Tracks.Count == 3 && read.Title == "測試歌單", "whole playlist preview contains unique metadata without resolving audio");
        check(read.Tracks.All(t => t.Url.StartsWith("https://www.youtube.com/watch?v=") && t.Duration == 12.5), "playlist metadata contains stable URLs and durations");
        foreach (string mode in new[] { "Partial", "Hidden", "Warning", "Truncated" })
        {
            var partial = PlaylistReader.ReadAsync(source, Url.Replace("Complete", mode), CancellationToken.None, 2).GetAwaiter().GetResult();
            check(!partial.Complete && partial.Tracks.Count == 3 && !partial.Message.Contains("secret"), "partial playlist retains valid tracks with sanitized status: " + mode);
        }
        var empty = PlaylistReader.ReadAsync(source, Url.Replace("Complete", "Empty"), CancellationToken.None).GetAwaiter().GetResult();
        check(empty.Complete && empty.Tracks.Count == 0, "genuinely empty playlist is distinct from failed lookup");
        foreach (string mode in new[] { "Denied", "Wrong" })
        {
            var failed = PlaylistReader.ReadAsync(source, Url.Replace("Complete", mode), CancellationToken.None).GetAwaiter().GetResult();
            check(!failed.Complete && failed.Tracks.Count == 0, "failed or substituted playlist cannot become an empty complete snapshot: " + mode);
        }
        using var cancellation = new CancellationTokenSource(250);
        try { PlaylistReader.ReadAsync(source, Url.Replace("Complete", "Slow"), cancellation.Token).GetAwaiter().GetResult(); check(false,"playlist cancellation"); }
        catch (OperationCanceledException) { check(true,"playlist enumeration cancels external tool"); }
        var stale = first.NextPage;
        try { source.GetPlaylistPageAsync(Url, stale, 2, CancellationToken.None).GetAwaiter().GetResult(); check(false,"stale cursor"); }
        catch (MediaException e) { check(e.Code == SourceErrorCode.Incomplete, "stale snapshot cursor cannot mix playlist generations"); }
        using var library = new Library(Path.Combine(root, "playlist-import"));
        var imported = PlaylistImport.Apply(library, null, Url, read);
        check(imported.Name == read.Title && imported.SourceId == read.SourceId && imported.SourceType == "youtube"
            && imported.Tracks.SequenceEqual(read.Tracks.Select(t => t.Id)), "preview import creates named playlist in source order");
        var existing = library.FindTrack(imported.Tracks[0]); existing.Category = "自訂分類"; existing.FileName = "legacy.ogg";
        File.WriteAllText(library.AudioPath(existing), "preserved");
        library.AddTo(imported, new Track { Id = "local_manual", SourceType = "local" });
        var smaller = new PlaylistReadResult { SourceId = read.SourceId, Title = read.Title, Complete = false };
        smaller.Tracks.Add(read.Tracks[1]);
        PlaylistImport.Apply(library, imported, Url, smaller, true, true, true);
        check(imported.Tracks.Count == 4 && imported.Tracks[0] == existing.Id && imported.SyncStatus == SyncState.Partial,
            "partial import cannot remove or reorder even when requested");
        smaller.Complete = true;
        PlaylistImport.Apply(library, imported, Url, smaller, false, true, true);
        check(imported.Tracks.SequenceEqual(new[] { read.Tracks[1].Id, "local_manual" }) && library.FindTrack(existing.Id) != null
            && File.Exists(library.AudioPath(existing)) && existing.Category == "自訂分類", "explicit complete sync preserves manual songs, global metadata and existing files");
        using var restored = new Library(library.Root);
        check(restored.FindList(imported.Id).SourceId == read.SourceId && !File.ReadAllText(library.StateFile).Contains("googlevideo"), "playlist import persists stable source without signed media URLs");
        var before = File.ReadAllText(library.StateFile);
        try { PlaylistImport.Apply(library, imported, Url, new PlaylistReadResult()); check(false,"empty failed preview"); }
        catch (InvalidOperationException) { check(File.ReadAllText(library.StateFile) == before, "failed empty preview cannot mutate saved library"); }
        library.Lists.Remove(imported);
        try { PlaylistImport.Apply(library, imported, Url, read); check(false,"removed destination"); }
        catch (InvalidOperationException) { check(!library.Lists.Contains(imported), "stale preview cannot resurrect a deleted playlist"); }
    }
}
