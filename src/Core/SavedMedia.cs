using System;
using System.IO;

namespace RimTube.Core
{
    public static class SavedMedia
    {
        public static bool Matches(Library library, Track track, MediaChoice choice)
        {
            if (!Ready(library, track, choice.Video) || new FileInfo(PathFor(library, track, choice.Video)).Length == 0) return false;
            string prefix=choice.Video ? "saved.video" : "saved.audio", value;
            if (choice.Video && (!track.Metadata.TryGetValue(prefix+"Height",out value) || value!=choice.Height.ToString(System.Globalization.CultureInfo.InvariantCulture))) return false;
            if (choice.Subtitle=="off") return true;
            if (!track.Metadata.TryGetValue(prefix+"Subtitle",out value) || value!=choice.Subtitle) return false;
            if (!track.Metadata.TryGetValue(prefix+"AutoSubs",out value) || value!=choice.AutoSubtitles.ToString()) return false;
            if (!track.Metadata.TryGetValue(prefix+"Sub",out value) || String.IsNullOrEmpty(value)) return true; // Source had no requested captions.
            string subtitle=SubtitlePath(library,track,choice.Video);
            return subtitle!="" && new FileInfo(subtitle).Length>0;
        }
        public static string PathFor(Library library, Track track, bool video)
        {
            string filename;
            if (!track.Metadata.TryGetValue(video ? "saved.video" : "saved.audio", out filename) || String.IsNullOrEmpty(filename)) return "";
            if (filename != Path.GetFileName(filename) || filename.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
                throw new InvalidDataException(LocalizedText.T("RimTube.InvalidSavedMediaPath"));
            if (!video && filename != track.FileName) return "";
            return Path.Combine(video ? library.VideoDir : library.MediaDir, filename);
        }
        public static bool Ready(Library library, Track track, bool video)
        { string path = PathFor(library, track, video); return path != "" && File.Exists(path); }
        public static string SubtitlePath(Library library, Track track, bool video)
        {
            string filename;
            if (!track.Metadata.TryGetValue(video ? "saved.videoSub" : "saved.audioSub", out filename) || String.IsNullOrEmpty(filename)) return "";
            if (filename != Path.GetFileName(filename) || filename.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) throw new InvalidDataException(LocalizedText.T("RimTube.InvalidSavedCaptionPath"));
            string path = Path.Combine(video ? library.VideoDir : library.MediaDir, filename);
            return File.Exists(path) ? path : "";
        }
        public static PlaybackSession Session(Library library, Track track, MediaChoice choice)
        {
            string path = PathFor(library, track, choice.Video);
            if (path == "" || !File.Exists(path)) throw new MediaException(SourceErrorCode.NotFound, LocalizedText.T("RimTube.NoLocalCopyExistsForThisMediaType"));
            return new PlaybackSession(track.Id, path, true, true, true, video: choice.Video,
                subtitleLocation: choice.Subtitle == "off" ? "" : SubtitlePath(library, track, choice.Video));
        }
    }
}
