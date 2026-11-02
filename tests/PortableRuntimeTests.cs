using System.Reflection;
using System.Xml.Linq;
using RimTube.Core;
using RimTube.Sources;

internal static class PortableRuntimeTests
{
    public static void Run(Action<bool,string> check,string root)
    {
        var type=typeof(Library).Assembly.GetType("RimTube.Core.PortableRuntime");
        check(type!=null,"portable runtime resolves paths without Verse or development folders");
        string Call(string method,params object[] args)=>(string)type.GetMethod(method).Invoke(null,args);
        string profile=Path.Combine(root,"使用者 profile"),mod=Path.Combine(root,"Workshop mods","RimTube");
        Directory.CreateDirectory(mod);
        string data=Call("DataDirectory",profile,mod,"");
        check(data==Path.Combine(profile,"RimTube","Library"),"clean install stores library in game user profile");
        check(Call("InterfacePath",profile)==Path.Combine(profile,"RimTube","Interface","ui.xml"),"global UI preferences stay independent of selected library");
        string custom=Path.Combine(root,"custom data");
        check(Call("DataDirectory",profile,mod,custom)==custom,"explicit existing library location remains supported");
        foreach(string path in new[]{mod,Path.Combine(mod,"UserData"),"relative-folder"})
        {
            try{Call("DataDirectory",profile,mod,path);check(false,"unsafe data directory must be rejected");}
            catch(TargetInvocationException e){check(e.InnerException is ArgumentException,"relative or mod-owned library location is rejected");}
        }
        check(Call("DataDirectory",profile,mod,mod+"-data")==mod+"-data","a sibling data folder is not mistaken for the mod folder");
        var jsType=typeof(Settings).Assembly.GetType("RimTube.Core.JavaScriptRuntime");
        check(jsType!=null,"JS runtime supports bundled Node and custom Deno");
        string Runtime(string file)=>(string)jsType.GetMethod("ResolverArgument").Invoke(null,new object[]{file});
        check(Runtime(Path.Combine(mod,"node.exe"))=="node:"+Path.GetFullPath(Path.Combine(mod,"node.exe")),"Node path is explicitly passed as Node to the resolver");
        check(Runtime(Path.Combine(mod,"deno.exe"))=="deno:"+Path.GetFullPath(Path.Combine(mod,"deno.exe")),"custom Deno paths keep their runtime identity");
        var configure=typeof(Settings).GetMethod("ConfigureBundledTools");
        check(configure!=null,"tool defaults can be initialized from installed mod root");
        using(var library=new Library(data))
        {
            configure.Invoke(library.Options,new object[]{mod});
            check(library.Options.YtDlp==Path.Combine(mod,"Tools","bin","yt-dlp.exe"),"YouTube resolver is found inside installed mod without PATH");
            check(library.Options.MpvPath==Path.Combine(mod,"Tools","bin","mpv.exe"),"player path is relative to installed mod root");
            check(library.Options.Ffmpeg==Path.Combine(mod,"Tools","bin","ffmpeg.exe")&&library.Options.JsRuntime==Path.Combine(mod,"Tools","bin","node.exe"),"download and JS dependencies use the source-audited bundled defaults");
            library.Save();
            var settings=XDocument.Load(library.StateFile).Root.Element("Settings");
            check(new[]{"ytDlp","mpv","ffmpeg","js"}.All(k=>(string)settings.Attribute(k)==""),"automatic tool paths are never persisted as machine-specific overrides");
            string displayedPath=library.Options.YtDlp;library.Options.YtDlp=displayedPath;library.Save();
            check((string)XDocument.Load(library.StateFile).Root.Element("Settings").Attribute("ytDlp")=="","editing settings without changing automatic value stays portable");
            string moved=Path.Combine(root,"moved mod");
            using(var restored=new Library(data))
            {
                configure.Invoke(restored.Options,new object[]{moved});
                check(restored.Options.YtDlp==Path.Combine(moved,"Tools","bin","yt-dlp.exe"),"moving or updating the mod relocates automatic tools on restart");
                string owned=Path.Combine(root,"my tools","custom-yt.exe");
                restored.Options.YtDlp=owned;configure.Invoke(restored.Options,new object[]{mod});restored.Save();
                check(restored.Options.YtDlp==owned,"explicit player-owned tool overrides survive package changes");
                check((string)XDocument.Load(restored.StateFile).Root.Element("Settings").Attribute("ytDlp")==owned,"explicit tool overrides survive library serialization");
                check(!File.Exists(restored.Options.MpvPath),"missing bundled files remain visibly missing instead of searching unrelated tools");
                restored.Options.YtDlp="";
                check(restored.Options.YtDlp==Path.Combine(mod,"Tools","bin","yt-dlp.exe"),"clearing an override restores the bundled tool");
            }
        }
    }

    public static void Smoke(Action<bool,string> check,string root,string mod)
    {
        string profile=Path.Combine(root,"profile"),data=PortableRuntime.DataDirectory(profile,mod,"");
        using var library=new Library(data);
        library.Options.ConfigureBundledTools(mod);
        string previousPath=Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH","");
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(50));var token=timeout.Token;
            foreach(var entry in new[]{(library.Options.YtDlp,"yt-dlp"),(library.Options.MpvPath,"mpv"),(library.Options.Ffmpeg,"ffmpeg"),(library.Options.JsRuntime,JavaScriptRuntime.DisplayName(library.Options.JsRuntime)),(Path.Combine(Path.GetDirectoryName(library.Options.Ffmpeg),"ffprobe.exe"),"ffprobe")})
            {
                var dependency=ToolDependency.CheckAsync(entry.Item1,entry.Item2,token).GetAwaiter().GetResult();
                check(dependency.Available,"relocated bundle starts "+entry.Item2+" without PATH: "+dependency.Version);
            }
            string js=Path.Combine(root,"runtime.js");File.WriteAllText(js,"console.log('RimTube runtime ready');");
            var deno=ProcessRunner.Run(library.Options.JsRuntime,JavaScriptRuntime.IsNode(library.Options.JsRuntime)?new[]{js}:new[]{"run","--no-config","--no-lock","--no-remote",js},token,10);
            check(deno.ExitCode==0&&deno.Output.Contains("RimTube runtime ready"),"bundled JS runtime executes local script without downloads");
            string video=Path.Combine(root,"bundled-video.mp4"),audio=Path.Combine(root,"bundled-audio.wav"),subtitle=Path.Combine(root,"caption.vtt");
            ModernTests.Wave(audio,3);File.WriteAllText(subtitle,"WEBVTT\n\n00:00.000 --> 00:03.000\nRimTube bundled fixture\n");
            var generated=ProcessRunner.Run(library.Options.Ffmpeg,new[]{"-nostdin","-hide_banner","-loglevel","error","-f","lavfi","-i","testsrc=size=160x90:rate=5:duration=3","-c:v","libx264","-threads","1","-pix_fmt","yuv420p","-y",video},token,20);
            check(generated.ExitCode==0&&File.Exists(video),"bundled FFmpeg generates tiny video with one CPU thread");
            string merged=Path.Combine(root,"merged.mkv");
            var merge=ProcessRunner.Run(library.Options.Ffmpeg,new[]{"-nostdin","-hide_banner","-loglevel","error","-i",video,"-i",audio,"-map","0:v","-map","1:a","-c","copy","-y",merged},token,20);
            check(merge.ExitCode==0&&File.Exists(merged),"bundled download postprocessor can merge separate audio and video");
            using var backend=new MpvBackend(library.Options,headless:true);
            backend.InitializeAsync(token).GetAwaiter().GetResult();
            backend.PrepareAsync(new PlaybackSession("bundled-fixture",merged,true,true,true,video:true,subtitleLocation:subtitle),token).GetAwaiter().GetResult();
            string tracks=backend.DiagnosticPropertyAsync("track-list",token).GetAwaiter().GetResult();
            check(tracks.Contains("video")&&tracks.Contains("audio")&&tracks.Contains("sub"),"relocated bundled mpv decodes merged audio/video and captions via IPC");
            backend.PlayAsync(token).GetAwaiter().GetResult();Thread.Sleep(300);
            check(backend.Snapshot.State==BackendState.Playing&&backend.Snapshot.Position>0,"relocated bundled playback timeline advances with no game or GPU output");
            library.Save();
            check(!File.ReadAllText(library.StateFile).Contains(mod),"clean library does not store the temporary installed tool directory");
        }
        finally{Environment.SetEnvironmentVariable("PATH",previousPath);}
    }

    public static void Network(Action<bool,string> check,string mod,string url)
    {
        var options=new Settings();options.ConfigureBundledTools(mod);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));var token=timeout.Token;
        using var source=new YouTubeSourceAdapter(options);
        var choice=new MediaChoice{Video=true,Height=144,Subtitle="off"};
        var track=source.GetTrackAsync(url,token,choice).GetAwaiter().GetResult();
        check(track.Availability==SourceAvailability.Available,"bundled resolver and JS runtime inspect the user-provided public video");
        var session=source.CreateSessionAsync(track,token).GetAwaiter().GetResult();
        check(session.HasVideo&&!String.IsNullOrWhiteSpace(session.AudioLocation),"bundled source resolves separate video and audio without cookie/login configuration");
        using var backend=new MpvBackend(options,headless:true);
        backend.InitializeAsync(token).GetAwaiter().GetResult();backend.PrepareAsync(session,token).GetAwaiter().GetResult();
        string tracks=backend.DiagnosticPropertyAsync("track-list",token).GetAwaiter().GetResult();
        check(tracks.Contains("video")&&tracks.Contains("audio"),"bundled mpv prepares actual online video and audio without the development tool folder");
    }
}
