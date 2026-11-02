using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RimTube.Core
{
    public static class ToolDependency
    {
        public static Task<DependencyInfo> CheckAsync(string path, string name, CancellationToken token)
        {
            return Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new DependencyInfo { Message = LocalizedText.F("RimTube.ToolNotFound",name) };
                try
                {
                    bool ff=name=="ffmpeg"||name=="ffprobe";
                    var r = ProcessRunner.Run(Path.GetFullPath(path), new[] { ff?"-version":"--version" }, token, 10);
                    string version = (r.Output ?? "").Split('\n')[0].Trim();
                    if (version.Length > 160) version = version.Substring(0, 160);
                    bool valid = r.ExitCode == 0 && (name == "mpv" ? version.StartsWith("mpv ", StringComparison.OrdinalIgnoreCase)
                        :ff?version.StartsWith(name+" version ",StringComparison.OrdinalIgnoreCase)
                        :name=="Node.js"?System.Text.RegularExpressions.Regex.IsMatch(version,@"^v\d+\.\d+\.\d+$")
                        :name=="Deno"?version.StartsWith("deno ",StringComparison.OrdinalIgnoreCase)
                        :name=="yt-dlp"&&System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d{4}\.\d{2}\.\d{2}(\..+)?$"));
                    return new DependencyInfo { Available = valid, Version = valid ? version : "", Message = LocalizedText.F(valid?"RimTube.ToolAvailable":"RimTube.ToolVersionFailed",name) };
                }
                catch (OperationCanceledException) { throw; }
                catch { return new DependencyInfo { Message = LocalizedText.F("RimTube.ToolStartFailed",name) }; }
            }, token);
        }
    }
}
