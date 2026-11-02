using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RimTube.Core;

namespace RimTube.Sources
{
    // One flat metadata snapshot per import: paging never mixes changing remote lists.
    internal sealed class YouTubePlaylistResolver
    {
        internal const int MaxTracks = 10000;
        private readonly string tool;
        public YouTubePlaylistResolver(Settings settings) { tool = settings.YtDlp; }
#pragma warning disable 0649
        [DataContract] private sealed class Info
        {
            [DataMember(Name = "_type")] public string Type;
            [DataMember(Name = "id")] public string Id;
            [DataMember(Name = "title")] public string Title;
            [DataMember(Name = "availability")] public string Availability;
            [DataMember(Name = "playlist_count")] public int? Count;
            [DataMember(Name = "entries")] public List<YoutubeInfo> Entries;
        }
#pragma warning restore 0649
        public Task<PlaylistReadResult> ReadAsync(string input, CancellationToken token)
        {
            string url = YoutubeClient.Normalize(input, true), id = url.Substring(url.LastIndexOf('=') + 1);
            return Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                if (String.IsNullOrWhiteSpace(tool) || !File.Exists(tool))
                    throw new MediaException(SourceErrorCode.MissingTool, LocalizedText.T("RimTube.SetTheYtDlpExePathInSettings"));
                var args = new List<string> { "--ignore-config", "--no-plugin-dirs", "--no-remote-components", "--no-cache-dir",
                    "--no-cookies", "--no-cookies-from-browser", "--no-js-runtimes", "--encoding", "utf-8", "--socket-timeout", "15",
                    "--retries", "1", "--extractor-retries", "1", "--yes-playlist", "--flat-playlist", "--skip-download", "--dump-single-json",
                    "--playlist-end", (MaxTracks + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), "--", url };
                ProcessResult response;
                try { response = ProcessRunner.Run(Path.GetFullPath(tool), args, token, 120); }
                catch (OperationCanceledException) { throw; }
                catch (TimeoutException) { throw new MediaException(SourceErrorCode.Timeout, LocalizedText.T("RimTube.YouTubePlaylistLookupTimedOutTryAgainLater")); }
                catch { throw new MediaException(SourceErrorCode.MissingTool, LocalizedText.T("RimTube.CouldNotStartYtDlpCheckTheTool")); }
                Info info;
                try
                {
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(response.Output)))
                        info = (Info)new DataContractJsonSerializer(typeof(Info)).ReadObject(stream);
                }
                catch { throw new MediaException(SourceErrorCode.Incomplete, LocalizedText.T("RimTube.CouldNotReadTheYouTubePlaylistCheckPublic")); }
                if (info == null || info.Id != id || info.Type != "playlist" || info.Entries == null)
                    throw new MediaException(SourceErrorCode.Incomplete, LocalizedText.T("RimTube.YouTubeReturnedIncompletePlaylistIdentificationOrContentThe"));
                if (!String.IsNullOrEmpty(info.Availability) && info.Availability != "public" && info.Availability != "unlisted")
                    throw new MediaException(SourceErrorCode.AuthorizationRequired, LocalizedText.T("RimTube.ThisPlaylistRequiresLoginOrHasAccessRestrictions"));
                var result = new PlaylistReadResult { SourceId = id, Title = String.IsNullOrWhiteSpace(info.Title) ? LocalizedText.T("RimTube.YouTubePlaylistMessage") : info.Title,
                    Complete = response.ExitCode == 0 && String.IsNullOrWhiteSpace(response.Error)
                        && info.Entries.Count <= MaxTracks && (!info.Count.HasValue || info.Count.Value == info.Entries.Count) };
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in info.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (entry == null || !Regex.IsMatch(entry.Id ?? "", "^[A-Za-z0-9_-]{11}$")
                        || entry.LiveStatus == "is_upcoming"
                        || entry.Title == "[Private video]" || entry.Title == "[Deleted video]"
                        || (!String.IsNullOrEmpty(entry.Availability) && entry.Availability != "public" && entry.Availability != "unlisted"))
                    { result.Complete = false; continue; }
                    if (!seen.Add(entry.Id)) continue;
                    if (result.Tracks.Count >= MaxTracks) { result.Complete = false; break; }
                    result.Tracks.Add(new Track { Id = "yt_" + entry.Id, SourceId = entry.Id, SourceType = "youtube",
                        Url = "https://www.youtube.com/watch?v=" + entry.Id, Title = String.IsNullOrWhiteSpace(entry.Title) ? "YouTube · " + entry.Id : entry.Title,
                        Duration = entry.Duration.HasValue && entry.Duration.Value > 0 && !Double.IsInfinity(entry.Duration.Value) ? entry.Duration.Value : 0,
                        Availability = SourceAvailability.Unknown });
                }
                result.Message = result.Complete ? LocalizedText.T("RimTube.TheFullPlaylistWasReadAudioStreamsAre")
                    : LocalizedText.T("RimTube.ThePlaylistContainsHiddenUnavailableEntriesSourceWarnings");
                return result;
            }, token);
        }
    }
}
