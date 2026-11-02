using System.Reflection;
using System.Xml.Linq;
using RimTube.Core;

internal static class MediaLibraryTests
{
    public static void Collections(Action<bool,string> check,string root)
    {
        using var lib=new Library(Path.Combine(root,"collections"));
        var song=new Track {Id="song",SourceType="youtube",Title="Song"};
        var audioLive=new Track {Id="audio-live",SourceType="youtube",Title="Radio"};audioLive.Metadata["media.live"]="True";
        var video=new Track {Id="video",SourceType="youtube",Title="Video"};video.Metadata["media.video"]="True";
        var live=new Track {Id="video-live",SourceType="youtube",Title="Stream"};live.Metadata["media.video"]="True";live.Metadata["media.live"]="True";
        foreach(var track in new[]{song,audioLive,video,live})lib.AddTo(lib.Lists[0],track);
        var catalog=UiCatalog.Build(lib,UiCatalog.Capture(lib,Array.Empty<Track>()));var q=new UiQuery();
        var scope=typeof(UiQuery).GetField("Collection");check(scope!=null,"music and video collections have independent scope from optional filters");
        scope.SetValue(q,"music");check(q.Select(catalog).Select(r=>r.Id).Order().SequenceEqual(new[]{"audio-live","song"}),"all music excludes streaming video while retaining audio live streams");
        var cached=q.Select(catalog);scope.SetValue(q,"videos");
        check(q.Select(catalog).Select(r=>r.Id).Order().SequenceEqual(new[]{"video","video-live"}),"all videos includes streaming videos and live video without downloading");
        check(!ReferenceEquals(cached,q.Select(catalog)),"switching collection invalidates cached query results");
        q.Storage=UiStorage.Saved;check(q.Select(catalog).Count==0,"saved-video filter excludes undownloaded streams");
        q.Storage=UiStorage.Any;q.PlaylistIds=new[]{live.Id,song.Id};scope.SetValue(q,"");
        check(q.Select(catalog).Select(r=>r.Id).SequenceEqual(new[]{live.Id,song.Id}),"playlists can still contain both media types in user order");
        scope.SetValue(q,"videos");cached=q.Select(catalog);
        for(int i=0;i<100000;i++)q.Select(catalog);
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++)q.Select(catalog);
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Console.WriteLine("COLLECTION-BENCH bytes="+allocated+" same="+ReferenceEquals(cached,q.Select(catalog)));
        check(ReferenceEquals(cached,q.Select(catalog))&&allocated<1024,"collection scope adds no allocations to unchanged GUI queries");
    }
    public static void SubtitlePreferences(Action<bool,string> check,string root)
    {
        var scale=typeof(UiPreferences).GetField("SubtitleScale");check(scale!=null,"subtitle size is a saved interface preference");
        string path=Path.Combine(root,"subtitle-size","ui.xml");var prefs=new UiPreferences();
        check((float)scale.GetValue(prefs)>=1,"subtitle default never shrinks the existing font");
        scale.SetValue(prefs,2f);prefs.Save(path);var saved=UiPreferences.Load(path,null);
        check((float)scale.GetValue(saved)==2,"subtitle zoom survives restart independently of media choice");
        var doc=XDocument.Load(path);doc.Root.SetAttributeValue("subtitleScale","999");doc.Save(path);
        check((float)scale.GetValue(UiPreferences.Load(path,null))==3,"damaged high subtitle size is clamped to 300 percent");
        doc.Root.SetAttributeValue("subtitleScale","NaN");doc.Save(path);
        check((float)scale.GetValue(UiPreferences.Load(path,null))>=1,"invalid subtitle size falls back to a readable default");
        doc.Root.Attribute("subtitleScale").Remove();doc.Save(path);
        check(!UiPreferences.Load(path,null).ReadOnly,"existing UI preference files migrate without changing schema or losing settings");
    }
    public static void Removal(Action<bool,string> check,string root)
    {
        var remove=typeof(Library).GetMethod("RemoveMedia");check(remove!=null,"removing library media owns file cleanup as well as references");
        using var lib=new Library(Path.Combine(root,"media-removal"));
        void Remove(string id){try{remove.Invoke(lib,new object[]{id});}catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();}}
        var track=new Track {Id="yt_remove",SourceType="youtube",FileName="audio.ogg"};
        track.Metadata["saved.audio"]="audio.ogg";track.Metadata["saved.audioSub"]="audio.vtt";
        track.Metadata["saved.video"]="video.mkv";track.Metadata["saved.videoSub"]="video.vtt";
        foreach(string file in new[]{"audio.ogg","audio.vtt"})File.WriteAllText(Path.Combine(lib.MediaDir,file),"fixture");
        foreach(string file in new[]{"video.mkv","video.vtt"})File.WriteAllText(Path.Combine(lib.VideoDir,file),"fixture");
        lib.AddTo(lib.Lists[0],track);lib.AddTo(lib.Lists[1],track);lib.Lists[0].SourceTracks.Add(track.Id);lib.Save();
        lib.Lists[0].Tracks.Remove(track.Id);lib.Save();
        check(File.Exists(lib.AudioPath(track))&&lib.FindTrack(track.Id)!=null,"removing from one playlist preserves files and other playlists");
        Remove(track.Id);
        check(lib.FindTrack(track.Id)==null&&lib.Lists.All(p=>!p.Tracks.Contains(track.Id)&&!p.SourceTracks.Contains(track.Id)),"library removal clears every playlist and sync membership");
        check(!Directory.EnumerateFiles(lib.MediaDir).Any()&&!Directory.EnumerateFiles(lib.VideoDir).Any(),"library removal deletes saved audio video and both subtitle files");
        using(var reload=new Library(lib.Root))check(reload.FindTrack(track.Id)==null,"removed media stays removed after library reload");
        var local=new Track {Id="local-remove",SourceType="local",FileName="imported.ogg"};lib.AddTo(lib.Lists[0],local);
        string original=Path.Combine(root,"original.ogg");File.WriteAllText(original,"original");File.WriteAllText(lib.AudioPath(local),"copy");
        var shared=new Track {Id="shared",SourceType="local",FileName=local.FileName};lib.AddTo(lib.Lists[1],shared);lib.Save();
        Remove(local.Id);check(File.Exists(lib.AudioPath(shared))&&File.Exists(original),"removing media preserves shared managed files and the original imported source");
        Remove(shared.Id);check(!File.Exists(Path.Combine(lib.MediaDir,"imported.ogg"))&&File.Exists(original),"last reference removes the managed copy while preserving the external original");
        var invalid=new Track {Id="invalid-remove",SourceType="local",FileName="../original.ogg"};lib.AddTo(lib.Lists[0],invalid);lib.Save();
        try{Remove(invalid.Id);check(false,"invalid path");}catch(InvalidDataException){check(lib.FindTrack(invalid.Id)!=null&&File.Exists(original),"invalid paths fail before deleting files or removing library references");}
        lib.Tracks.Remove(invalid.Id);lib.Lists[0].Tracks.Remove(invalid.Id);
        var game=new Track {Id="game-remove",SourceType="game"};lib.AddTo(lib.Lists[0],game);
        try{Remove(game.Id);check(false,"game assets");}catch(InvalidOperationException){check(lib.FindTrack(game.Id)!=null,"game assets cannot be deleted through media removal");}
        var locked=new Track {Id="locked-remove",SourceType="local",FileName="locked.ogg"};lib.AddTo(lib.Lists[0],locked);File.WriteAllText(lib.AudioPath(locked),"locked");lib.Save();
        using(var file=new FileStream(lib.AudioPath(locked),FileMode.Open,FileAccess.Read,FileShare.None))
        {try{Remove(locked.Id);check(false,"locked media");}catch(IOException){check(lib.FindTrack(locked.Id)!=null&&lib.Lists[0].Tracks.Contains(locked.Id),"locked files retain their library and playlist references for retry");}}
        Remove(locked.Id);check(lib.FindTrack(locked.Id)==null&&!File.Exists(Path.Combine(lib.MediaDir,"locked.ogg")),"removal succeeds after the playback file handle is released");
        var rollback=new Track {Id="rollback-remove",SourceType="youtube",FileName="rollback.ogg"};rollback.Metadata["saved.video"]="rollback.mkv";
        lib.AddTo(lib.Lists[0],rollback);File.WriteAllText(lib.AudioPath(rollback),"audio");File.WriteAllText(Path.Combine(lib.VideoDir,"rollback.mkv"),"video");lib.Save();
        using(var file=new FileStream(Path.Combine(lib.VideoDir,"rollback.mkv"),FileMode.Open,FileAccess.Read,FileShare.None))
        {try{Remove(rollback.Id);check(false,"partial staging failure");}catch(IOException){check(File.Exists(lib.AudioPath(rollback))&&lib.FindTrack(rollback.Id)!=null,"failure staging the second file restores the first and keeps references");}}
        using(var state=new FileStream(lib.StateFile,FileMode.Open,FileAccess.Read,FileShare.None))
        {try{Remove(rollback.Id);check(false,"save failure");}catch(IOException){check(File.Exists(lib.AudioPath(rollback))&&File.Exists(Path.Combine(lib.VideoDir,"rollback.mkv"))&&lib.Lists[0].Tracks.Contains(rollback.Id)&&lib.FindTrack(rollback.Id)!=null,"library save failure restores every staged file and all references");}}
        check(!Directory.EnumerateDirectories(lib.TempDir).Any(),"successful cleanup and rollback leave no removal staging directories");
        lib.ReadOnly=true;
        try{Remove(rollback.Id);check(false,"readonly deletion");}catch(InvalidOperationException){check(File.Exists(lib.AudioPath(rollback))&&lib.FindTrack(rollback.Id)!=null,"readonly libraries reject deletion before staging files");}
    }
}
