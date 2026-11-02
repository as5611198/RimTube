using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using RimTube.Core;
using RimTube.Sources;

namespace RimTube
{
    public enum JobState { Queued, Running, Complete, Failed, Canceled, Interrupted }
    public sealed class DownloadBatchResult { public int Added, Reused, Pending; }
    public sealed class AudioJob
    {
        public string Id = Guid.NewGuid().ToString("N"), TrackId = "", Input = "", Title = "";
        public bool Local, ExplicitYouTube;
        public MediaChoice Choice = new MediaChoice();
        public volatile JobState State;
        public volatile string Progress = LocalizedText.T("RimTube.WaitingMessage");
        internal CancellationTokenSource Cancellation;
    }

    public sealed class JobQueue : IDisposable
    {
        public readonly List<AudioJob> Items = new List<AudioJob>();
        private readonly Library library;
        private readonly ConcurrentQueue<Action> completions = new ConcurrentQueue<Action>();
        private readonly Dictionary<AudioJob,Task> running = new Dictionary<AudioJob,Task>();
        private volatile bool disposed;
        public string LastError = "";
        public event Action<AudioJob> Completed;
        public bool Busy { get { return running.Count != 0; } }
        public int ActiveCount => running.Count;
        public int Parallelism => Math.Max(1, Math.Min(3, library.Options.DownloadWorkers));
        public JobQueue(Library state)
        {
            library = state;
            string path = Path.Combine(state.Root, "jobs.xml");
            if (!File.Exists(path)) return;
            try
            {
                var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null };
                XDocument doc; using (var reader = System.Xml.XmlReader.Create(path, settings)) doc = XDocument.Load(reader);
                foreach (var e in doc.Root.Elements("Job"))
                    Items.Add(new AudioJob { Id = (string)e.Attribute("id"), TrackId = (string)e.Attribute("track"), Input = (string)e.Attribute("input"), Title = (string)e.Attribute("title"), Local = (bool?)e.Attribute("local") ?? false, ExplicitYouTube = (bool?)e.Attribute("youtubeDownload") ?? false,
                        Choice = ReadChoice(e),
                        State = JobState.Interrupted, Progress = LocalizedText.T("RimTube.TheLastSessionEndedBeforeCompletionRetryTo") });
                foreach (var job in Items.Where(j => !j.Local && !j.ExplicitYouTube)) { job.State = JobState.Failed; job.Progress = LocalizedText.T("RimTube.LegacyYouTubeDownloadJobsAreDisabledSourceMetadata"); }
            }
            catch (Exception ex) { LastError = LocalizedText.T("RimTube.CouldNotReadTheDownloadQueue") + ex.Message; }
        }

        public void Enqueue(Track track, string input, bool local, bool persist = true)
        {
            if (!local || track.IsYouTube) throw new MediaException(SourceErrorCode.Unsupported, LocalizedText.T("RimTube.CreateDownloadsExplicitlyThroughYouTubeMediaOptionsThis"));
            if (library.ReadOnly) throw new InvalidOperationException(library.LoadWarning);
            if (library.IsReady(track.Id) || Items.Any(j => j.TrackId == track.Id && (j.State == JobState.Queued || j.State == JobState.Running))) return;
            Items.Add(new AudioJob { TrackId = track.Id, Title = track.Title, Input = input, Local = local, State = JobState.Queued });
            if (persist) Save();
        }
        public AudioJob EnqueueDownload(Track track, MediaChoice choice, bool persist = true)
        {
            if (library.ReadOnly) throw new InvalidOperationException(library.LoadWarning);
            if (!track.IsYouTube) throw new InvalidOperationException(LocalizedText.T("RimTube.SelectAYouTubeTrack"));
            choice = choice.Copy(); choice.Validate();
            string url = YoutubeClient.Normalize(track.Url, false);
            if (SavedMedia.Matches(library,track,choice))
                return new AudioJob { TrackId=track.Id,Title=track.Title,Input=url,ExplicitYouTube=true,Choice=choice,State=JobState.Complete,Progress=LocalizedText.T("RimTube.AlreadySavedDuplicateDownloadSkipped") };
            var pending = Items.FirstOrDefault(j => j.TrackId == track.Id && j.ExplicitYouTube && j.Choice.Video == choice.Video
                && (!choice.Video || j.Choice.Height == choice.Height) && j.Choice.Subtitle == choice.Subtitle && j.Choice.AutoSubtitles == choice.AutoSubtitles
                && (j.State == JobState.Queued || j.State == JobState.Running));
            if (pending != null) return pending;
            var job = new AudioJob { TrackId = track.Id, Title = track.Title, Input = url, ExplicitYouTube = true, Choice = choice, State = JobState.Queued };
            Items.Add(job); if (persist) Save(); return job;
        }
        public DownloadBatchResult EnqueuePlaylist(Playlist playlist,MediaChoice choice)
        {
            if (library.ReadOnly) throw new InvalidOperationException(library.LoadWarning);
            if (!library.Lists.Contains(playlist)) throw new InvalidOperationException(LocalizedText.T("RimTube.ThePlaylistWasRemoved"));
            choice.Validate();var result=new DownloadBatchResult();
            var localChoice=choice.Copy();localChoice.Delivery=MediaDelivery.PreferSaved;
            foreach(var track in playlist.Tracks.Distinct().Select(library.FindTrack).Where(t=>t!=null&&t.IsYouTube))
            {
                int before=Items.Count;var job=EnqueueDownload(track,choice,false);
                if(job.State==JobState.Complete) ++result.Reused;
                else if(Items.Count>before) ++result.Added;
                else ++result.Pending;
                localChoice.Store(track);
            }
            Save();library.Save();return result;
        }
        private static MediaChoice ReadChoice(XElement e)
        {
            var track = new Track();
            foreach (var item in e.Elements("Choice")) track.Metadata[(string)item.Attribute("key")] = (string)item.Attribute("value") ?? "";
            return MediaChoice.From(track);
        }
        public void EnqueueRange(IEnumerable<Track> tracks)
        {
            foreach (var track in tracks) if (track.IsYouTube && !String.IsNullOrEmpty(track.Url)) EnqueueDownload(track, MediaChoice.From(track));
            Save();
        }
        public void Flush() { Save(); }
        public void Cancel(AudioJob job)
        {
            if (job.State == JobState.Running) { job.Progress = LocalizedText.T("RimTube.Canceling"); job.Cancellation?.Cancel(); }
            else if (job.State == JobState.Queued) { job.State = JobState.Canceled; job.Progress = LocalizedText.T("RimTube.Canceled"); }
            Save();
        }
        public void Retry(AudioJob job)
        {
            if (!job.Local && !job.ExplicitYouTube) throw new MediaException(SourceErrorCode.AuthorizationRequired, LocalizedText.T("RimTube.LegacyYouTubeDownloadJobsAreDisabledAndWill"));
            if (job.State == JobState.Running || job.State == JobState.Queued || (!job.ExplicitYouTube && library.IsReady(job.TrackId))) return;
            if (Items.Any(j => j != job && j.TrackId == job.TrackId && (j.State == JobState.Queued || j.State == JobState.Running))) return;
            job.State = JobState.Queued; job.Progress = LocalizedText.T("RimTube.WaitingToRetry"); Save();
        }
        public void Pump()
        {
            Action callback;
            while (completions.TryDequeue(out callback))
            {
                try { callback(); } catch (Exception ex) { LastError = ex.Message; }
            }
            if (disposed || library.ReadOnly) return;
            while (running.Count < Parallelism)
            {
                // Different settings for the same track still commit in request order.
                var job = Items.FirstOrDefault(j => j.State == JobState.Queued && !running.Keys.Any(a => a.TrackId == j.TrackId));
                if (job == null) break;
                Track t = library.FindTrack(job.TrackId);
                if (t == null) { job.State = JobState.Failed; job.Progress = LocalizedText.T("RimTube.TheTrackNoLongerExists"); Save(); continue; }
                if(job.ExplicitYouTube && SavedMedia.Matches(library,t,job.Choice))
                {
                    job.State=JobState.Complete;job.Progress=LocalizedText.T("RimTube.AlreadySavedDuplicateDownloadSkipped");Save();
                    try { Completed?.Invoke(job); } catch { LastError=LocalizedText.T("RimTube.MediaSavedStartPlaybackManually"); }
                    continue;
                }
                job.Cancellation = new CancellationTokenSource(); job.State = JobState.Running; job.Progress = LocalizedText.T("RimTube.PreparingMedia"); Save();
                var options = new Settings { YtDlp = library.Options.YtDlp, Ffmpeg = library.Options.Ffmpeg, JsRuntime = library.Options.JsRuntime };
                // Workers own temp files; only main-thread completions commit to the library.
                var track = new Track { Id = t.Id, Title = t.Title, Url = t.Url };
                running[job] = Task.Run(() => Execute(job, track, options));
            }
        }

        private void Execute(AudioJob job, Track track, Settings options)
        {
            string dir = Path.Combine(library.TempDir, Guid.NewGuid().ToString("N"));
            string prepared = null, subtitle = "";
            try
            {
                if (!job.Local && !job.ExplicitYouTube) throw new MediaException(SourceErrorCode.AuthorizationRequired, LocalizedText.T("RimTube.YouTubeDownloadsAreDisabledInThisLegacyEntry"));
                Directory.CreateDirectory(dir);
                var token = job.Cancellation.Token;
                if (job.Local)
                {
                    if (!File.Exists(job.Input)) throw new FileNotFoundException(LocalizedText.T("RimTube.OriginalAudioNotFoundSelectTheFileAgain"));
                    string ext = Path.GetExtension(job.Input).ToLowerInvariant();
                    if (ext == ".ogg" || ext == ".wav" || ext == ".mp3")
                    {
                        prepared = Path.Combine(dir, "audio" + ext);
                        using (var source = File.OpenRead(job.Input))
                        using (var dest = new FileStream(prepared, FileMode.CreateNew, FileAccess.Write))
                        {
                            byte[] buffer = new byte[131072]; int count;
                            while ((count = source.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); dest.Write(buffer, 0, count); }
                        }
                    }
                    else
                    {
                        // Local conversion does not require yt-dlp.
                        var result = ProcessRunner.Run(YoutubeClient.RequireExe(options.Ffmpeg, "ffmpeg.exe"),
                            new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-i", Path.GetFullPath(job.Input), "-vn", "-map_metadata", "-1", "-c:a", "libvorbis", "-q:a", "5", "-y", Path.Combine(dir, "audio.ogg") }, token, 1800);
                        if (result.ExitCode != 0) throw new IOException(YoutubeClient.CleanError(result.Error));
                        prepared = Path.Combine(dir, "audio.ogg");
                    }
                }
                else
                {
                    var download = new MediaDownloader(options).Download(track, job.Choice, dir, token, p => job.Progress = p);
                    prepared = download.MediaPath; subtitle = download.SubtitlePath;
                }
                token.ThrowIfCancellationRequested();
                if (!File.Exists(prepared) || new FileInfo(prepared).Length == 0) throw new IOException(LocalizedText.T("RimTube.TheAudioFileIsMissingOrEmpty"));
                string ready = prepared;
                completions.Enqueue(() =>
                {
                    try
                    {
                        if (disposed || job.Cancellation.IsCancellationRequested) { job.State = JobState.Canceled; job.Progress = LocalizedText.T("RimTube.Canceled"); return; }
                        Track current = library.FindTrack(job.TrackId);
                        if (current == null) throw new IOException(LocalizedText.T("RimTube.TheImportedTrackWasNotFound"));
                        string filename = Guid.NewGuid().ToString("N") + Path.GetExtension(ready);
                        bool video = job.ExplicitYouTube && job.Choice.Video;
                        string destination = Path.Combine(video ? library.VideoDir : library.MediaDir, filename);
                        string subDestination = "";
                        string previous = current.FileName;
                        var previousMetadata = new Dictionary<string,string>(current.Metadata);
                        File.Move(ready, destination);
                        try
                        {
                            if (!String.IsNullOrEmpty(subtitle) && File.Exists(subtitle))
                            {
                                subDestination = Path.Combine(video ? library.VideoDir : library.MediaDir, Guid.NewGuid().ToString("N") + Path.GetExtension(subtitle));
                                File.Move(subtitle, subDestination);
                            }
                            if (video) current.Metadata["saved.video"] = filename;
                            else current.FileName = filename;
                            if (job.ExplicitYouTube)
                            {
                                current.Metadata[video ? "saved.video" : "saved.audio"] = filename;
                                current.Metadata[video ? "saved.videoSub" : "saved.audioSub"] = String.IsNullOrEmpty(subDestination) ? "" : Path.GetFileName(subDestination);
                                current.Metadata[video ? "saved.videoHeight" : "saved.audioQuality"] = video ? job.Choice.Height.ToString() : "vorbis";
                                current.Metadata[video ? "saved.videoSubtitle" : "saved.audioSubtitle"] = job.Choice.Subtitle;
                                current.Metadata[video ? "saved.videoAutoSubs" : "saved.audioAutoSubs"] = job.Choice.AutoSubtitles.ToString();
                            }
                            current.Error = ""; library.Save();
                        }
                        catch
                        {
                            current.FileName = previous; current.Metadata.Clear(); foreach (var pair in previousMetadata) current.Metadata[pair.Key] = pair.Value;
                            File.Delete(destination); if (subDestination != "") File.Delete(subDestination); throw;
                        }
                        job.State = JobState.Complete; job.Progress = LocalizedText.T("RimTube.Complete");
                        try { Completed?.Invoke(job); } catch { LastError = LocalizedText.T("RimTube.MediaSavedButAutomaticPlaybackFailedStartIt"); }
                    }
                    catch (Exception ex) { job.State = JobState.Failed; job.Progress = ex.Message; LastError = ex.Message; }
                    finally { Finish(job, dir); }
                });
                prepared = null; // Completion owns this directory until the main-thread commit.
            }
            catch (Exception ex)
            {
                completions.Enqueue(() =>
                {
                    job.State = ex is OperationCanceledException ? JobState.Canceled : JobState.Failed;
                    job.Progress = ex is OperationCanceledException ? LocalizedText.T("RimTube.Canceled") : ex.Message;
                    Track current = library.FindTrack(job.TrackId); if (current != null && job.State == JobState.Failed) current.Error = ex.Message;
                    Finish(job, dir);
                });
            }
        }

        private void Finish(AudioJob job, string dir)
        {
            CleanTemp(dir);
            job.Cancellation?.Dispose(); job.Cancellation = null;
            running.Remove(job);
            Save();
        }
        private void CleanTemp(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                string parent = Path.GetFullPath(library.TempDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (full.StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(full)) Directory.Delete(full, true);
            }
            catch { /* In-use leftovers are confined to Temp; never delete the music library. */ }
        }
        private void Save()
        {
            if (library.ReadOnly) return;
            Library.AtomicXml(Path.Combine(library.Root, "jobs.xml"), new XDocument(new XElement("Jobs", Items
                .Where(j => j.State != JobState.Complete && j.State != JobState.Canceled)
                .Select(j => new XElement("Job", new XAttribute("id", j.Id), new XAttribute("track", j.TrackId), new XAttribute("title", j.Title), new XAttribute("input", j.Input), new XAttribute("local", j.Local), new XAttribute("youtubeDownload", j.ExplicitYouTube), ChoiceXml(j.Choice))))));
        }
        private static IEnumerable<XElement> ChoiceXml(MediaChoice choice)
        {
            var track = new Track(); choice.Store(track);
            return track.Metadata.Select(p => new XElement("Choice", new XAttribute("key", p.Key), new XAttribute("value", p.Value)));
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var job in running.Keys) job.Cancellation?.Cancel();
            try { Task.WaitAll(running.Values.ToArray(), 6000); } catch { }
            Action callback;
            while (completions.TryDequeue(out callback)) { try { callback(); } catch { } }
            try { Save(); } catch (Exception ex) { LastError = ex.Message; }
        }
    }
}
