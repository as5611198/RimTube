using System;
using System.Linq;

namespace RimTube.Core
{
    public static class PlaylistImport
    {
        public static Playlist Apply(Library library, Playlist target, string url, PlaylistReadResult preview, bool add = true, bool remove = false, bool reorder = false)
        {
            if (library.ReadOnly) throw new InvalidOperationException(library.LoadWarning);
            string source = YoutubeClient.Normalize(url, true), id = source.Substring(source.LastIndexOf('=') + 1);
            if (preview == null || preview.SourceId != id || (!preview.Complete && preview.Tracks.Count == 0))
                throw new InvalidOperationException(LocalizedText.T("RimTube.TheSourcePreviewIsIncompleteAndHasNo"));
            if (target != null && !library.Lists.Contains(target)) throw new InvalidOperationException(LocalizedText.T("RimTube.TheTargetPlaylistWasRemovedQueryItAgain"));
            if (target != null && !String.IsNullOrEmpty(target.SourceUrl) && YoutubeClient.Normalize(target.SourceUrl, true) != source)
                throw new InvalidOperationException(LocalizedText.T("RimTube.ThePlaylistSourceChangedQueryItAgain"));
            target = target ?? new Playlist { Name = preview.Title, SourceUrl = source };
            foreach (var track in preview.Tracks)
            {
                var existing = library.FindTrack(track.Id);
                if (existing == null) library.Tracks.Add(track.Id, track);
                else
                {
                    existing.Title = track.Title; existing.Url = track.Url; existing.Duration = track.Duration;
                    existing.SourceType = track.SourceType; existing.SourceId = track.SourceId;
                    // Categories, custom metadata, local files and last play result remain owned by the library.
                }
            }
            if (!library.Lists.Contains(target)) library.Lists.Add(target);
            target.SourceUrl = source; target.SourceId = id; target.SourceType = "youtube";
            library.ApplySync(target, Library.Diff(target, preview.Tracks.Select(t => t.Id)), add, remove, reorder, preview.Complete);
            return target;
        }
    }
}
