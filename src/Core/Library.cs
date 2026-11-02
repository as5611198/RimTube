using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace RimTube.Core
{
    public enum BattleMode { Vanilla, Continue, Custom }
    public enum PlayMode { Ordered, Shuffle, RepeatOne, RepeatAll }
    public enum PlayIntent { Stopped, Playing, Paused }
    public enum PlayContext { Stopped, Paused, General, Combat, Vanilla }

    public sealed class Track
    {
        public string Id = "", Title = "", Url = "", FileName = "", Error = "";
        public double Duration;
        public string SourceType = "", SourceId = "", Category = "";
        public SourceAvailability Availability = SourceAvailability.Unknown;
        public readonly Dictionary<string, string> Metadata = new Dictionary<string, string>();
        public bool IsYouTube { get { return SourceType == "youtube" || (Id ?? "").StartsWith("yt_", StringComparison.Ordinal); } }
    }

    public sealed class Playlist
    {
        public string Id = Guid.NewGuid().ToString("N"), Name = LocalizedText.T("RimTube.NewPlaylistMessage"), SourceUrl = "";
        public List<string> Tracks = new List<string>();
        public List<string> SourceTracks = new List<string>();
        public string SourceId = "", SourceType = "", SyncMessage = "";
        public PlaylistType Type = PlaylistType.Custom;
        public SyncState SyncStatus = SyncState.Never;
        public bool IsGameCatalog => SourceType=="game-catalog";
    }

    public sealed class Settings
    {
        public float Volume = .7f;
        public BattleMode Battle = BattleMode.Vanilla;
        public PlayMode Mode = PlayMode.Ordered;
        public bool ShowMini = true, PauseWithGame;
        public bool PauseCoveredVideo;
        public string GeneralList = "general", CombatList = "combat";
        private readonly ConfiguredToolPath ytDlp=new ConfiguredToolPath(),ffmpeg=new ConfiguredToolPath(),jsRuntime=new ConfiguredToolPath(),mpv=new ConfiguredToolPath();
        public string YtDlp {get=>ytDlp.Value;set=>ytDlp.Value=value;}
        public string Ffmpeg {get=>ffmpeg.Value;set=>ffmpeg.Value=value;}
        public string JsRuntime {get=>jsRuntime.Value;set=>jsRuntime.Value=value;}
        public string MpvPath {get=>mpv.Value;set=>mpv.Value=value;}
        internal string YtDlpOverride=>ytDlp.Override;
        internal string FfmpegOverride=>ffmpeg.Override;
        internal string JsRuntimeOverride=>jsRuntime.Override;
        internal string MpvOverride=>mpv.Override;
        private string bundledResolver="";
        public void ConfigureBundledTools(string modDirectory)
        {
            string bin=Path.Combine(Path.GetFullPath(modDirectory),"Tools","bin");
            bundledResolver=Path.Combine(bin,"yt-dlp.exe");
            ytDlp.Default(bundledResolver);ffmpeg.Default(Path.Combine(bin,"ffmpeg.exe"));
            jsRuntime.Default(Path.Combine(bin,"node.exe"));mpv.Default(Path.Combine(bin,"mpv.exe"));
        }
        public void ConfigureResolverUpdate(string verifiedPath)=>ytDlp.Default(String.IsNullOrEmpty(verifiedPath)?bundledResolver:verifiedPath);
        public float MiniX = 16, MiniY = 80;
        public string Backend = "mpv";
        public int CacheMiB = 32, BackCacheMiB = 4, CacheSeconds = 20;
        public int DownloadWorkers = 2;
    }

    public sealed class SyncDiff
    {
        public List<string> Added = new List<string>(), Removed = new List<string>(), Remote = new List<string>();
        public bool OrderChanged;
    }

    public static class PlaybackPolicy
    {
        public static PlayContext Decide(PlayIntent intent, bool danger, bool combatAvailable, BattleMode mode, bool paused, bool pauseWithGame)
        {
            if (intent == PlayIntent.Stopped) return PlayContext.Stopped;
            if (intent == PlayIntent.Paused || (paused && pauseWithGame)) return PlayContext.Paused;
            if (!danger || mode == BattleMode.Continue) return PlayContext.General;
            if (mode == BattleMode.Vanilla || !combatAvailable) return PlayContext.Vanilla;
            return PlayContext.Combat;
        }
    }

    public sealed class Library : IDisposable
    {
        public const int Schema = 2;
        public bool MigratedFromV1 { get; private set; }
        public long Revision { get; private set; }
        public readonly string Root;
        public readonly Dictionary<string, Track> Tracks = new Dictionary<string, Track>(StringComparer.Ordinal);
        public readonly List<Playlist> Lists = new List<Playlist>();
        public Settings Options = new Settings();
        public string LoadWarning = "";
        public bool ReadOnly;
        private FileStream writerLock;
        public string MediaDir { get { return Path.Combine(Root, "Audio"); } }
        public string VideoDir { get { return Path.Combine(Root, "Video"); } }
        public string TempDir { get { return Path.Combine(Root, "Temp"); } }
        public string StateFile { get { return Path.Combine(Root, "library.xml"); } }

        public Library(string root)
        {
            Root = Path.GetFullPath(root);
            Directory.CreateDirectory(MediaDir);
            Directory.CreateDirectory(VideoDir);
            Directory.CreateDirectory(TempDir);
            if (File.Exists(StateFile))
            {
                try { Load(StateFile); }
                catch (Exception ex)
                {
                    // Never overwrite a damaged library or a newer schema with an empty state.
                    ReadOnly = true;
                    LoadWarning = LocalizedText.T("RimTube.CouldNotReadTheLibraryOriginalFilesAre") + ex.Message;
                }
            }
            if (Lists.Count == 0)
            {
                Lists.Add(new Playlist { Id = "general", Name = LocalizedText.T("RimTube.EverydayMusic"), Type = PlaylistType.General });
                Lists.Add(new Playlist { Id = "combat", Name = LocalizedText.T("RimTube.CombatMusic"), Type = PlaylistType.Combat });
            }
        }

        public Playlist FindList(string id) { return Lists.FirstOrDefault(p => p.Id == id); }
        public bool AcquireWriteLock()
        {
            if (ReadOnly) return false;
            if (writerLock != null) return true;
            try
            {
                writerLock = new FileStream(Path.Combine(Root, ".writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch (IOException)
            {
                ReadOnly = true; LoadWarning = LocalizedText.T("RimTube.AnotherGameProcessIsUsingThisLibraryOpened");
                return false;
            }
        }
        public void Dispose() { writerLock?.Dispose(); writerLock = null; }
        public Track FindTrack(string id) { Track t; return id != null && Tracks.TryGetValue(id, out t) ? t : null; }
        public string AudioPath(Track t)
        {
            if (t == null || String.IsNullOrEmpty(t.FileName)) return "";
            if (Path.GetFileName(t.FileName) != t.FileName) throw new InvalidDataException(LocalizedText.T("RimTube.TheAudioFilenameContainsADisallowedPath"));
            return Path.Combine(MediaDir, t.FileName);
        }
        public bool IsReady(string id)
        {
            Track t = FindTrack(id);
            if(t?.SourceType=="game") return t.Availability==SourceAvailability.Available;
            return t != null && (t.IsYouTube ? SavedMedia.Ready(this, t, false) : !String.IsNullOrEmpty(t.FileName) && File.Exists(AudioPath(t)));
        }
        public List<string> Ready(Playlist list) { return list == null ? new List<string>() : list.Tracks.Where(IsReady).ToList(); }
        public Playlist ImportPlaylist(Playlist preferred=null)
        {
            if(ReadOnly) throw new InvalidOperationException(LoadWarning);
            if(preferred!=null&&!Lists.Contains(preferred)) throw new InvalidOperationException(LocalizedText.T("RimTube.TheTargetPlaylistWasRemoved"));
            var target=preferred??FindList(Options.GeneralList);
            if(target!=null&&!target.IsGameCatalog) return target;
            target=Lists.FirstOrDefault(p=>!p.IsGameCatalog&&p.Id=="general")??Lists.FirstOrDefault(p=>!p.IsGameCatalog);
            if(target==null){target=new Playlist {Name=LocalizedText.T("RimTube.ImportMedia")};Lists.Add(target);}
            return target;
        }

        public void AddTo(Playlist list, Track track)
        {
            if (ReadOnly) throw new InvalidOperationException(LoadWarning);
            if (list == null) throw new InvalidOperationException(LocalizedText.T("RimTube.SelectAPlaylistFirst"));
            if(list.IsGameCatalog) throw new InvalidOperationException(LocalizedText.T("RimTube.AutomaticSourcePlaylistsAreManagedByTheGame"));
            Track existing = FindTrack(track.Id);
            if (existing == null) Tracks.Add(track.Id, track);
            else
            {
                if (!String.IsNullOrWhiteSpace(track.Title)) existing.Title = track.Title;
                if (!String.IsNullOrWhiteSpace(track.Url)) existing.Url = track.Url;
            }
            if (!list.Tracks.Contains(track.Id)) list.Tracks.Add(track.Id);
        }

        public static SyncDiff Diff(Playlist list, IEnumerable<string> remote)
        {
            var ids = remote.Where(id => !String.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
            return new SyncDiff
            {
                Remote = ids,
                Added = ids.Except(list.Tracks).ToList(),
                Removed = list.SourceTracks.Where(list.Tracks.Contains).Except(ids).ToList(),
                OrderChanged = !list.Tracks.Where(ids.Contains).SequenceEqual(ids.Where(list.Tracks.Contains))
            };
        }

        public void ApplySync(Playlist list, SyncDiff diff, bool add, bool remove, bool reorder, bool complete = true)
        {
            if (ReadOnly) throw new InvalidOperationException(LoadWarning);
            if (!complete) { remove = false; reorder = false; }
            if (remove) list.Tracks.RemoveAll(id => diff.Removed.Contains(id));
            if (add) foreach (string id in diff.Added) if (!list.Tracks.Contains(id)) list.Tracks.Add(id);
            if (reorder)
                list.Tracks = diff.Remote.Where(list.Tracks.Contains).Concat(list.Tracks.Where(id => !diff.Remote.Contains(id))).Distinct().ToList();
            // Remember source membership separately from songs manually added by the player.
            // Declined removals remain managed and can be reviewed again at the next sync.
            list.SourceTracks = list.SourceTracks.Concat(diff.Remote).Distinct().Where(list.Tracks.Contains).ToList();
            list.SyncStatus = complete ? SyncState.Complete : SyncState.Partial;
            list.SyncMessage = complete ? LocalizedText.T("RimTube.SynchronizationComplete") : LocalizedText.T("RimTube.OnlyPartialResultsWereRetrievedExistingTracksAnd");
            Save();
        }

        public long CacheBytes()
        {
            return Directory.EnumerateFiles(MediaDir).Concat(Directory.EnumerateFiles(VideoDir)).Sum(p => new FileInfo(p).Length);
        }
        public void RemoveMedia(string id)=>MediaRemoval.Remove(this,id);
        public void DeleteVideo(Track track)
        {
            if (ReadOnly) throw new InvalidOperationException(LoadWarning);
            string path = SavedMedia.PathFor(this, track, true), sub = SavedMedia.SubtitlePath(this, track, true);
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(sub)) File.Delete(sub);
            track.Metadata.Remove("saved.video"); track.Metadata.Remove("saved.videoSub"); track.Metadata.Remove("saved.videoHeight"); Save();
        }

        public void DeleteAudio(Track t)
        {
            if (ReadOnly) throw new InvalidOperationException(LoadWarning);
            string path = AudioPath(t);
            string sub = t.IsYouTube ? SavedMedia.SubtitlePath(this, t, false) : "";
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(sub)) File.Delete(sub);
            t.Metadata.Remove("saved.audio"); t.Metadata.Remove("saved.audioSub");
            t.FileName = "";
            t.Error = LocalizedText.T("RimTube.TheAudioWasDeletedDownloadOrImportIt");
            Save();
        }

        public void Save()
        {
            if (ReadOnly) throw new InvalidOperationException(LoadWarning);
            var s = Options;
            var xml = new XDocument(new XElement("RimTube", new XAttribute("schema", Schema),
                new XElement("Settings", A("volume", s.Volume), A("battle", s.Battle), A("mode", s.Mode),
                    A("mini", s.ShowMini), A("pauseWithGame", s.PauseWithGame), A("pauseCoveredVideo", s.PauseCoveredVideo), A("general", s.GeneralList), A("combat", s.CombatList),
                    A("ytDlp", s.YtDlpOverride), A("ffmpeg", s.FfmpegOverride), A("js", s.JsRuntimeOverride), A("miniX", s.MiniX), A("miniY", s.MiniY),
                    A("backend", s.Backend), A("mpv", s.MpvOverride), A("cacheMiB", s.CacheMiB), A("backCacheMiB", s.BackCacheMiB), A("cacheSeconds", s.CacheSeconds), A("downloadWorkers", Math.Max(1, Math.Min(3, s.DownloadWorkers)))),
                new XElement("Tracks", Tracks.Values.OrderBy(t => t.Id).Select(t => new XElement("Track", A("id", t.Id),
                    A("title", t.Title), A("url", t.Url), A("file", t.FileName), A("duration", t.Duration), A("error", t.Error),
                    A("sourceType", t.SourceType), A("sourceId", t.SourceId), A("category", t.Category), A("availability", t.Availability),
                    new XElement("Metadata", t.Metadata.Select(m => new XElement("Item", A("key", m.Key), A("value", m.Value))))))),
                new XElement("Lists", Lists.Select(p => new XElement("List", A("id", p.Id), A("name", p.Name), A("source", p.SourceUrl),
                    A("sourceType", p.SourceType), A("sourceId", p.SourceId), A("type", p.Type), A("sync", p.SyncStatus), A("syncMessage", p.SyncMessage),
                    p.Tracks.Select(id => new XElement("Item", A("id", id))),
                    p.SourceTracks.Select(id => new XElement("SourceItem", A("id", id))))))));
            AtomicXml(StateFile, xml);
            ++Revision;
        }

        public static void AtomicXml(string path, XDocument xml)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { xml.Save(f); f.Flush(true); }
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private static XAttribute A(string name, object value) { return new XAttribute(name, Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""); }
        private static string S(XElement e, string name, string fallback = "") { return (string)e.Attribute(name) ?? fallback; }
        private static float F(XElement e, string name, float fallback) { float n; return float.TryParse(S(e, name), NumberStyles.Float, CultureInfo.InvariantCulture, out n) && !float.IsNaN(n) && !float.IsInfinity(n) ? n : fallback; }
        private static bool B(XElement e, string name, bool fallback) { bool n; return bool.TryParse(S(e, name), out n) ? n : fallback; }
        private static T E<T>(XElement e, string name, T fallback) where T : struct { T n; return Enum.TryParse(S(e, name), out n) && Enum.IsDefined(typeof(T), n) ? n : fallback; }

        private void Load(string path)
        {
            var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null };
            XDocument doc;
            using (var reader = System.Xml.XmlReader.Create(path, settings)) doc = XDocument.Load(reader);
            XElement root = doc.Root;
            int schema = (int?)root?.Attribute("schema") ?? -1;
            if (root == null || root.Name != "RimTube" || (schema != 1 && schema != Schema))
                throw new InvalidDataException(LocalizedText.T("RimTube.UnsupportedLibraryFormat"));
            MigratedFromV1 = schema == 1;
            XElement s = root.Element("Settings");
            if (s != null)
            {
                Options.Volume = Math.Max(0, Math.Min(1, F(s, "volume", .7f)));
                Options.Battle = E(s, "battle", BattleMode.Vanilla); Options.Mode = E(s, "mode", PlayMode.Ordered);
                Options.ShowMini = B(s, "mini", true); Options.PauseWithGame = B(s, "pauseWithGame", false);
                Options.PauseCoveredVideo = B(s,"pauseCoveredVideo",false);
                Options.GeneralList = S(s, "general", "general"); Options.CombatList = S(s, "combat", "combat");
                Options.YtDlp = S(s, "ytDlp"); Options.Ffmpeg = S(s, "ffmpeg"); Options.JsRuntime = S(s, "js");
                Options.MiniX = F(s, "miniX", 16); Options.MiniY = F(s, "miniY", 80);
                Options.Backend = S(s, "backend", "mpv"); Options.MpvPath = S(s, "mpv");
                Options.CacheMiB = (int)Math.Max(4, Math.Min(128, F(s, "cacheMiB", 32)));
                Options.BackCacheMiB = (int)Math.Max(0, Math.Min(32, F(s, "backCacheMiB", 4)));
                Options.CacheSeconds = (int)Math.Max(1, Math.Min(120, F(s, "cacheSeconds", 20)));
                Options.DownloadWorkers = (int)Math.Max(1, Math.Min(3, F(s, "downloadWorkers", 2)));
            }
            foreach (var e in root.Element("Tracks")?.Elements("Track") ?? Enumerable.Empty<XElement>())
            {
                var t = new Track { Id = S(e, "id"), Title = S(e, "title"), Url = S(e, "url"), FileName = S(e, "file"), Error = S(e, "error"), Duration = F(e, "duration", 0) };
                t.SourceType = S(e, "sourceType", t.IsYouTube ? "youtube" : "local");
                t.SourceId = S(e, "sourceId", t.Id.StartsWith("yt_", StringComparison.Ordinal) ? t.Id.Substring(3) : t.Id);
                t.Category = S(e, "category");
                t.Availability = E(e, "availability", t.IsYouTube ? SourceAvailability.AuthorizationRequired : SourceAvailability.Unknown);
                foreach (var item in e.Element("Metadata")?.Elements("Item") ?? Enumerable.Empty<XElement>()) t.Metadata[S(item, "key")] = S(item, "value");
                if (String.IsNullOrWhiteSpace(t.Id)) throw new InvalidDataException(LocalizedText.T("RimTube.TheTrackHasNoID"));
                AudioPath(t); Tracks.Add(t.Id, t);
            }
            foreach (var e in root.Element("Lists")?.Elements("List") ?? Enumerable.Empty<XElement>())
            {
                var p = new Playlist { Id = S(e, "id"), Name = S(e, "name"), SourceUrl = S(e, "source") };
                p.SourceType = S(e, "sourceType", String.IsNullOrEmpty(p.SourceUrl) ? "local" : "youtube"); p.SourceId = S(e, "sourceId");
                p.Type = E(e, "type", p.Id == "general" ? PlaylistType.General : p.Id == "combat" ? PlaylistType.Combat : PlaylistType.Custom);
                p.SyncStatus = E(e, "sync", SyncState.Never); p.SyncMessage = S(e, "syncMessage");
                if (String.IsNullOrWhiteSpace(p.Id) || Lists.Any(x => x.Id == p.Id)) throw new InvalidDataException(LocalizedText.T("RimTube.InvalidPlaylistID"));
                p.Tracks = e.Elements("Item").Select(i => S(i, "id")).Where(Tracks.ContainsKey).Distinct().ToList();
                p.SourceTracks = e.Elements("SourceItem").Select(i => S(i, "id")).Where(p.Tracks.Contains).Distinct().ToList();
                Lists.Add(p);
            }
        }
    }
}
