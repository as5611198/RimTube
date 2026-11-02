using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace RimTube.Core
{
    public enum UiStorage {Any,Saved,Remote,Missing}
    public enum UiMedia {Any,Audio,Video,Live}
    public sealed class UiCatalogInput
    {
        public readonly Track Track;
        public readonly bool InLibrary;
        public UiCatalogInput(Track track,bool inLibrary){Track=track;InLibrary=inLibrary;}
    }
    public sealed class UiRow
    {
        public readonly Track Track;
        public string Id=>Track.Id;
        public readonly string Title,Source,SourceLabel,Detail,Tooltip,Category;
        public readonly bool InLibrary,IsGame,IsYouTube,SavedAudio,SavedVideo,Video,Live,Missing;
        public UiRow(Track t,bool member,bool audio,bool video)
        {
            Track=t;Title=t.Title??"";Category=t.Category??"";InLibrary=member;
            IsGame=t.SourceType=="game";IsYouTube=t.IsYouTube;SavedAudio=!IsGame&&audio;SavedVideo=!IsGame&&video;
            Source=IsGame?Value(t,"game.kind","Mod"):IsYouTube?"YouTube":"Local";
            SourceLabel=IsGame?Value(t,"game.source",LocalizedText.T("RimTube.UnknownSource")):IsYouTube?"YouTube":LocalizedText.T("RimTube.SourceLocalImport");
            bool flag; Video=bool.TryParse(Value(t,"media.video","False"),out flag)&&flag;Live=bool.TryParse(Value(t,"media.live","False"),out flag)&&flag;
            Missing=IsGame?t.Availability!=SourceAvailability.Available:!IsYouTube&&!SavedAudio;
            string state=IsGame?(Missing?LocalizedText.T("RimTube.SourceNotLoaded"):LocalizedText.T("RimTube.GameAsset")):(SavedAudio&&SavedVideo?LocalizedText.T("RimTube.SavedMusicAndVideo"):SavedVideo?LocalizedText.T("RimTube.SavedVideo"):SavedAudio?LocalizedText.T("RimTube.SavedMusic"):IsYouTube?LocalizedText.T("RimTube.OnlineSource"):LocalizedText.T("RimTube.LocalCopyMissing"));
            string duration=t.Duration>0?TimeSpan.FromSeconds(Math.Min(t.Duration,TimeSpan.MaxValue.TotalSeconds-1)).ToString(t.Duration>=3600?@"h\:mm\:ss":@"m\:ss"):"--:--";
            Detail=SourceLabel+" · "+(Live?LocalizedText.T("RimTube.Live"):Video?LocalizedText.T("RimTube.Video"):LocalizedText.T("RimTube.Music"))+" · "+state+" · "+duration;
            Tooltip=Title+"\n"+Detail+(Category!=""?LocalizedText.T("RimTube.NCategory")+Category:"")+(IsGame?"\n"+Value(t,"game.package","")+(Value(t,"game.tense","")=="True"?LocalizedText.T("RimTube.OriginalCombatTrack"):""):"")+(String.IsNullOrEmpty(t.Error)?"":"\n"+t.Error);
        }
        private static string Value(Track t,string key,string fallback){string value;return t.Metadata.TryGetValue(key,out value)?value:fallback;}
    }
    public sealed class UiCatalog
    {
        private readonly Dictionary<string,UiRow> byId;
        public readonly IReadOnlyList<UiRow> Rows;
        public static readonly UiCatalog Empty=new UiCatalog(new UiRow[0]);
        private UiCatalog(UiRow[] rows){Rows=rows;byId=rows.ToDictionary(r=>r.Id,StringComparer.Ordinal);}
        public UiRow Find(string id){UiRow row;return id!=null&&byId.TryGetValue(id,out row)?row:null;}
        // Main-thread capture: deep-copy mutable Track dictionaries once per invalidation.
        public static UiCatalogInput[] Capture(Library library,IEnumerable<Track> game)
        {
            var result=new Dictionary<string,UiCatalogInput>(StringComparer.Ordinal);
            foreach(var t in library.Tracks.Values) result[t.Id]=new UiCatalogInput(Clone(t),true);
            foreach(var t in game)
            {
                UiCatalogInput old;
                result[t.Id]=new UiCatalogInput(Clone(t),result.TryGetValue(t.Id,out old)&&old.InLibrary);
            }
            return result.Values.ToArray();
        }
        private static Track Clone(Track t)
        {
            var next=new Track {Id=t.Id,Title=t.Title,Url=t.Url,FileName=t.FileName,Error=t.Error,Duration=t.Duration,SourceType=t.SourceType,SourceId=t.SourceId,Category=t.Category,Availability=t.Availability};
            foreach(var pair in t.Metadata) next.Metadata[pair.Key]=pair.Value;
            return next;
        }
        // Worker-only path. Never call from OnGUI; inputs no longer share mutable library tracks.
        public static UiCatalog Build(Library library,IEnumerable<UiCatalogInput> inputs)
        {
            var rows=new List<UiRow>();
            foreach(var input in inputs)
            {
                var t=input.Track;bool audio=false,video=false;
                if(t.SourceType!="game")
                {
                    try {audio=NonEmpty(t.IsYouTube?SavedMedia.PathFor(library,t,false):library.AudioPath(t));video=t.IsYouTube&&NonEmpty(SavedMedia.PathFor(library,t,true));}
                    catch(IOException) {} catch(UnauthorizedAccessException) {} catch(ArgumentException) {}
                }
                rows.Add(new UiRow(t,input.InLibrary,audio,video));
            }
            return new UiCatalog(rows.OrderBy(r=>r.Title,StringComparer.OrdinalIgnoreCase).ThenBy(r=>r.Id,StringComparer.Ordinal).ToArray());
        }
        private static bool NonEmpty(string path)=>!String.IsNullOrEmpty(path)&&File.Exists(path)&&new FileInfo(path).Length>0;
    }
    // One result cache per query, bounded to the active selection; no cache of historical searches.
    public sealed class UiQuery
    {
        public string Search="",Source="",Collection="";
        public bool GameCatalog;
        public UiStorage Storage;
        public UiMedia Media;
        public IReadOnlyList<string> PlaylistIds;
        private UiCatalog last;
        private string search,source,collection;
        private bool game;
        private UiStorage storage;
        private UiMedia media;
        private IReadOnlyList<string> playlist;
        private IReadOnlyList<UiRow> result=new UiRow[0];
        public IReadOnlyList<UiRow> Select(UiCatalog catalog)
        {
            if(ReferenceEquals(catalog,last)&&search==Search&&source==Source&&collection==Collection&&game==GameCatalog&&storage==Storage&&media==Media&&ReferenceEquals(playlist,PlaylistIds)) return result;
            last=catalog;search=Search;source=Source;collection=Collection;game=GameCatalog;storage=Storage;media=Media;playlist=PlaylistIds;
            IEnumerable<UiRow> rows=playlist==null?catalog.Rows:playlist.Select(catalog.Find).Where(r=>r!=null);
            result=rows.Where(Matches).ToArray();return result;
        }
        private bool Matches(UiRow row)
        {
            if(game?!row.IsGame:!row.InLibrary) return false;
            if(collection=="music"&&row.Video||collection=="videos"&&!row.Video)return false;
            if(!String.IsNullOrEmpty(source)&&row.Source!=source) return false;
            if(!String.IsNullOrEmpty(search)&&row.Title.IndexOf(search,StringComparison.OrdinalIgnoreCase)<0&&row.Category.IndexOf(search,StringComparison.OrdinalIgnoreCase)<0&&row.SourceLabel.IndexOf(search,StringComparison.OrdinalIgnoreCase)<0) return false;
            bool saved=row.SavedAudio||row.SavedVideo;
            if(storage==UiStorage.Saved&&!saved||storage==UiStorage.Remote&&(!row.IsYouTube||saved)||storage==UiStorage.Missing&&!row.Missing) return false;
            return media==UiMedia.Any||media==UiMedia.Live&&row.Live||media==UiMedia.Video&&row.Video&&!row.Live||media==UiMedia.Audio&&!row.Video&&!row.Live;
        }
    }
}
