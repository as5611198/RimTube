using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RimTube.Core;

namespace RimTube.Sources
{
    // Called on catalog refresh, never by OnGUI or the per-frame playback tick.
    public static class GameMusicPlaylists
    {
        public static void Sync(Library library,IEnumerable<Track> catalog)
        {
            if(library.ReadOnly) return;
            var current=catalog.Where(t=>t.SourceType=="game").GroupBy(t=>t.Id).Select(g=>g.First()).ToArray();
            var present=new HashSet<string>(current.Select(t=>t.Id),StringComparer.Ordinal);
            foreach(var saved in library.Tracks.Values)
                if(saved.SourceType=="game"&&!present.Contains(saved.Id)) saved.Availability=SourceAvailability.Missing;
            foreach(var info in current)
            {
                var saved=library.FindTrack(info.Id);
                if(saved==null){library.Tracks.Add(info.Id,info);continue;}
                if(ReferenceEquals(saved,info)) continue;
                saved.Title=info.Title;saved.Duration=info.Duration;saved.Availability=info.Availability;
                saved.SourceType="game";saved.SourceId=info.SourceId;
                foreach(var pair in info.Metadata) saved.Metadata[pair.Key]=pair.Value;
            }
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var group in current.GroupBy(Package,StringComparer.OrdinalIgnoreCase).OrderBy(g=>g.Key,StringComparer.Ordinal))
            {
                string package=group.Key.ToLowerInvariant();seen.Add(package);
                var list=library.Lists.FirstOrDefault(p=>p.IsGameCatalog&&String.Equals(p.SourceId,package,StringComparison.OrdinalIgnoreCase));
                if(list==null)
                {
                    string id;
                    using(var hash=SHA256.Create()) id="game_list_"+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(package))).Replace("-","").ToLowerInvariant();
                    // Never take ownership of a pre-existing user playlist with a colliding id.
                    if(library.FindList(id)!=null) id=Guid.NewGuid().ToString("N");
                    var sample=group.First();string kind=Value(sample,"game.kind","Mod"),name=Value(sample,"game.source",package);
                    list=new Playlist {Id=id,SourceType="game-catalog",SourceId=package,Name=kind=="Core"?LocalizedText.T("RimTube.BaseGameRimWorld"):(kind=="DLC"?"DLC · ":LocalizedText.T("RimTube.Mod"))+name};
                    library.Lists.Add(list);
                }
                list.Tracks=group.OrderBy(t=>t.Title,StringComparer.OrdinalIgnoreCase).ThenBy(t=>t.Id,StringComparer.Ordinal).Select(t=>t.Id).ToList();
                list.SourceTracks=new List<string>(list.Tracks);list.SyncStatus=SyncState.Complete;
                int missing=group.Count(t=>t.Availability!=SourceAvailability.Available);
                list.SyncMessage=missing>0?LocalizedText.F("RimTube.SourceUpdateMissing",missing):LocalizedText.T("RimTube.UpdatedBySourceMessage");
            }
            foreach(var list in library.Lists)
                if(list.IsGameCatalog&&!seen.Contains(list.SourceId)) list.SyncMessage=LocalizedText.T("RimTube.SourceNotLoadedPlaylistAndTrackReferencesPreserved");
        }
        private static string Package(Track t)=>Value(t,"game.package","unknown").ToLowerInvariant();
        private static string Value(Track t,string key,string fallback)
        {string value;return t.Metadata.TryGetValue(key,out value)&&!String.IsNullOrWhiteSpace(value)?value:fallback;}
    }
}
