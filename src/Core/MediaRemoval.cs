using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace RimTube.Core
{
    // Called only after the playback bridge has released this track and its jobs are idle.
    internal static class MediaRemoval
    {
        private static readonly StringComparer Paths=StringComparer.OrdinalIgnoreCase;
        public static void Remove(Library library,string id)
        {
            if(library.ReadOnly)throw new InvalidOperationException(library.LoadWarning);
            var track=library.FindTrack(id);if(track==null)return;
            if(track.SourceType=="game")throw new InvalidOperationException(LocalizedText.T("RimTube.GameMusicAssetsCannotBeDeletedFromThe"));
            var owned=Locations(library,track).ToArray();
            var shared=new HashSet<string>(library.Tracks.Values.Where(t=>t.Id!=id&&t.SourceType!="game").SelectMany(t=>Locations(library,t)),Paths);
            var files=owned.Where(p=>!shared.Contains(p)&&File.Exists(p)).ToArray();
            CheckPath(library,library.TempDir);
            string staging=Path.Combine(library.TempDir,"remove-"+Guid.NewGuid().ToString("N"));
            var moved=new List<Tuple<string,string>>();
            var lists=library.Lists.Select(p=>Tuple.Create(p,p.Tracks.ToList(),p.SourceTracks.ToList())).ToArray();
            bool committed=false;
            try
            {
                Directory.CreateDirectory(staging);
                new XDocument(new XElement("MediaRemoval",new XAttribute("track",id),files.Select((file,index)=>
                    new XElement("File",new XAttribute("staged",index),new XAttribute("original",file))))).Save(Path.Combine(staging,"files.xml"));
                foreach(string file in files)
                {
                    string temporary=Path.Combine(staging,moved.Count.ToString());
                    File.Move(file,temporary);moved.Add(Tuple.Create(file,temporary));
                }
                foreach(var list in library.Lists){list.Tracks.RemoveAll(t=>t==id);list.SourceTracks.RemoveAll(t=>t==id);}
                library.Tracks.Remove(id);library.Save();committed=true;
            }
            finally
            {
                if(!committed)
                {
                    library.Tracks[id]=track;
                    foreach(var list in lists){list.Item1.Tracks=list.Item2;list.Item1.SourceTracks=list.Item3;}
                    foreach(var file in moved)if(File.Exists(file.Item2))File.Move(file.Item2,file.Item1);
                }
                else foreach(var file in moved)File.Delete(file.Item2);
                if(Directory.Exists(staging))File.Delete(Path.Combine(staging,"files.xml"));
                if(Directory.Exists(staging)&&!Directory.EnumerateFileSystemEntries(staging).Any())Directory.Delete(staging);
            }
        }
        private static IEnumerable<string> Locations(Library library,Track track)
        {
            // Local imports are copied into MediaDir; their original source is never a target.
            var paths=new[]{library.AudioPath(track),SavedMedia.PathFor(library,track,true),
                SavedMedia.SubtitlePath(library,track,false),SavedMedia.SubtitlePath(library,track,true)};
            return paths.Where(p=>!String.IsNullOrEmpty(p)).Select(p=>CheckPath(library,p)).Distinct(Paths).ToArray();
        }
        private static string CheckPath(Library library,string path)
        {
            string full=Path.GetFullPath(path),root=Path.GetFullPath(library.Root);
            if(!full.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException(LocalizedText.T("RimTube.TheMediaPathIsOutsideTheLibrary"));
            for(string part=full;part!=null;part=Path.GetDirectoryName(part))
            {
                if((File.Exists(part)||Directory.Exists(part))&&(File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0)
                    throw new InvalidDataException(LocalizedText.T("RimTube.MediaDeletionCannotTraverseDirectoryOrFileLinks"));
                if(Paths.Equals(part,root))break;
            }
            return full;
        }
    }
}
