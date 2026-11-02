using RimTube.Core;
using System;
using System.Collections.Generic;
using System.IO;
using RimTube.Sources;
using Verse;

namespace RimTube
{
    internal static class GameMusicCatalog
    {
        public static IEnumerable<GameSongInfo> Read()
        {
            foreach(var song in DefDatabase<SongDef>.AllDefsListForReading)
            {
                var pack=song.modContentPack;
                yield return new GameSongInfo {
                    DefName=song.defName,Title=String.IsNullOrWhiteSpace(song.label)?Path.GetFileName(song.clipPath??song.defName):song.LabelCap.ToString(),
                    PackageId=pack?.PackageId??"unknown",SourceName=pack?.Name??LocalizedText.T("RimTube.UnknownSource"),
                    Kind=pack!=null&&pack.IsCoreMod?"Core":pack!=null&&pack.IsOfficialMod?"DLC":"Mod",
                    Tense=song.tense,Available=song.clip!=null&&song.clip.length>0,Duration=song.clip!=null?song.clip.length:0
                };
            }
        }
    }
}
