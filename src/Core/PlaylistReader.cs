using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RimTube.Core
{
    public sealed class PlaylistReadResult
    {
        public readonly List<Track> Tracks = new List<Track>();
        public string SourceId = "", Title = "", Message = "";
        public bool Complete;
    }
    // Enumerate into a preview first. Failed/partial pages never masquerade as a complete empty list.
    public static class PlaylistReader
    {
        public static async Task<PlaylistReadResult> ReadAsync(IMediaSource source, string url, CancellationToken token, int pageSize = 100, int maxTracks = 100000)
        {
            token.ThrowIfCancellationRequested();
            if ((source.Capabilities & SourceCapabilities.PlaylistPages) == 0) throw new MediaException(SourceErrorCode.Unsupported, LocalizedText.T("RimTube.ThisSourceDoesNotSupportOnlinePlaylistEnumeration"));
            var result = new PlaylistReadResult(); var cursors = new HashSet<string>(); var ids = new HashSet<string>(); string next = "";
            pageSize = Math.Max(1, Math.Min(500, pageSize)); maxTracks = Math.Max(1, Math.Min(100000, maxTracks));
            try
            {
                for (int pages = 0; pages < 10000; pages++)
                {
                    token.ThrowIfCancellationRequested();
                    if (!cursors.Add(next)) throw new MediaException(SourceErrorCode.Incomplete, LocalizedText.T("RimTube.TheSourceReturnedTheSamePageAgainSynchronization"));
                    var page = await source.GetPlaylistPageAsync(url, next, pageSize, token).ConfigureAwait(false);
                    if (page == null || String.IsNullOrWhiteSpace(page.SourcePlaylistId)) throw new MediaException(SourceErrorCode.Incomplete, LocalizedText.T("RimTube.TheSourceReturnedAnIncompletePlaylist"));
                    if (pages == 0) { result.SourceId = page.SourcePlaylistId; result.Title = page.Title; }
                    else if (page.SourcePlaylistId != result.SourceId) throw new MediaException(SourceErrorCode.Incomplete, LocalizedText.T("RimTube.TheSourcePlaylistIdentityChangedSynchronizationStopped"));
                    bool invalid = false;
                    foreach (var track in page.Tracks)
                    {
                        if (track == null || String.IsNullOrWhiteSpace(track.Id)) { invalid = true; continue; }
                        if (ids.Add(track.Id)) result.Tracks.Add(track);
                        if (result.Tracks.Count >= maxTracks) { result.Message = LocalizedText.T("RimTube.ThePreviewLimitWasReachedTheOriginalPlaylist"); return result; }
                    }
                    if (invalid || page.IsPartial) { result.Message = String.IsNullOrEmpty(page.Message) ? LocalizedText.T("RimTube.SomeSourceEntriesAreUnavailableExistingTracksAnd") : page.Message; return result; }
                    if (page.IsComplete && String.IsNullOrEmpty(page.NextPage)) { result.Complete = true; result.Message = LocalizedText.T("RimTube.TheCompletePlaylistIsReadyYouCanPreview"); return result; }
                    if (String.IsNullOrEmpty(page.NextPage)) throw new MediaException(SourceErrorCode.Incomplete, LocalizedText.T("RimTube.TheSourceOmittedTheNextPageTheOriginal"));
                    next = page.NextPage;
                }
                result.Message = LocalizedText.T("RimTube.TooManySourcePagesEnumerationStoppedTheOriginal");
            }
            catch (OperationCanceledException) { throw; }
            catch (MediaException ex) { result.Message = ex.Message; }
            catch { result.Message = LocalizedText.T("RimTube.ThePlaylistSourceDisconnectedTheOriginalPlaylistWas"); }
            return result;
        }
    }
}
