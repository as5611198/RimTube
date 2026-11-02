using System.Text.Json;
using RimTube.Core;

internal static class StreamFormatTests
{
    public static void Run(Action<bool,string> check,string root)
    {
        string project=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../"));
        string tool=Path.Combine(project,"tools/bin/yt-dlp.exe");
        Directory.CreateDirectory(root);
        object Format(string id,string protocol,int? height,string video,string audio)=>new {
            format_id=id,protocol,height,vcodec=video,acodec=audio,ext=height.HasValue?"mp4":"m4a",
            url="https://media.googlevideo.com/fixture/"+id,tbr=height??128
        };
        var hlsVideo=Format("hls720","m3u8_native",720,"avc1","none");
        var directVideo=Format("direct1080","https",1080,"avc1","none");
        var hlsAudio=Format("hlsAudio","m3u8_native",null,"none","mp4a");
        var directAudio=Format("directAudio","https",null,"none","opus");
        string[] Select(object[] formats,int height)
        {
            string file=Path.Combine(root,"format-fixture.json");
            File.WriteAllText(file,JsonSerializer.Serialize(new {id="Fixture0001",title="offline format fixture",extractor="youtube",formats}));
            var result=ProcessRunner.Run(tool,new[]{"--ignore-config","--no-plugin-dirs","--no-remote-components","--no-cache-dir","--load-info-json",file,"--skip-download","--dump-single-json","--format",new MediaChoice {Video=true,Height=height}.StreamFormat},CancellationToken.None,15);
            if(result.ExitCode!=0)throw new Exception("Offline format fixture failed: "+YoutubeClient.CleanError(result.Error));
            using var json=JsonDocument.Parse(result.Output);
            return json.RootElement.TryGetProperty("requested_formats",out var selected)?selected.EnumerateArray().Select(f=>f.GetProperty("format_id").GetString()).ToArray():new[]{json.RootElement.GetProperty("format_id").GetString()};
        }
        check(Select(new[]{hlsVideo,directVideo,hlsAudio,directAudio},1080).SequenceEqual(new[]{"hls720","hlsAudio"}),"video prefers available HLS video and audio over direct HTTPS, within requested ceiling");
        check(Select(new[]{directVideo,hlsAudio,directAudio},1080).SequenceEqual(new[]{"direct1080","hlsAudio"}),"video without HLS retains HLS audio and direct video fallback");
        check(Select(new[]{hlsVideo,directAudio},720).SequenceEqual(new[]{"hls720","directAudio"}),"HLS video can pair with direct audio when HLS audio is unavailable");
        check(Select(new[]{directVideo,directAudio},1080).SequenceEqual(new[]{"direct1080","directAudio"}),"direct-only sources retain split audio/video fallback");
        var low=Format("hls360","m3u8",360,"avc1","none");
        check(Select(new[]{hlsVideo,low,hlsAudio},360).SequenceEqual(new[]{"hls360","hlsAudio"}),"HLS selection respects quality cap and supports both manifest protocols");
        var muxed=Format("combined360","https",360,"avc1","mp4a");
        check(Select(new[]{muxed},360).Single()=="combined360","combined video/audio remains the last fallback");
    }
}
