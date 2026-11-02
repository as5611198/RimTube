using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RimTube.Core
{
    public sealed class ResolverRelease
    {
        public string Version;
        public long ExecutableBytes,NoticeBytes;
        public string Url(string asset)=>"https://github.com/yt-dlp/yt-dlp/releases/download/"+Version+"/"+asset;
    }
    public static class ResolverToolUpdater
    {
        [DataContract] private sealed class Release
        {
            [DataMember(Name="tag_name")] public string Tag {get;set;}
            [DataMember(Name="draft")] public bool Draft {get;set;}
            [DataMember(Name="prerelease")] public bool Prerelease {get;set;}
            [DataMember(Name="assets")] public List<Asset> Assets {get;set;}
        }
        [DataContract] private sealed class Asset
        {
            [DataMember(Name="name")] public string Name {get;set;}
            [DataMember(Name="size")] public long Size {get;set;}
        }
        public static ResolverRelease ParseRelease(string json)
        {
            Release release;
            try
            {
                using(var input=new MemoryStream(Encoding.UTF8.GetBytes(json)))release=(Release)new DataContractJsonSerializer(typeof(Release)).ReadObject(input);
                if(release.Draft||release.Prerelease||!Regex.IsMatch(release.Tag??"",@"^\d{4}\.\d{2}\.\d{2}$"))throw new InvalidDataException();
                Asset One(string name,long max)
                {
                    var assets=release.Assets?.Where(a=>a.Name==name).ToArray();
                    if(assets==null||assets.Length!=1||assets[0].Size<=0||assets[0].Size>max)throw new InvalidDataException();
                    return assets[0];
                }
                var exe=One("yt-dlp.exe",ResolverToolStore.MaximumExecutableBytes);var notices=One("yt-dlp_win.zip",ResolverToolStore.MaximumExecutableBytes);
                One("SHA2-256SUMS",128*1024);
                return new ResolverRelease{Version=release.Tag,ExecutableBytes=exe.Size,NoticeBytes=notices.Size};
            }
            catch(Exception e) when(e is SerializationException||e is ArgumentException||e is InvalidDataException||e is NullReferenceException)
            {throw new InvalidDataException(LocalizedText.T("RimTube.UpdateReleaseInvalid"),e);}
        }
        public static string ExpectedHash(string checksums,string name)
        {
            var lines=(checksums??"").Split('\n').Select(l=>Regex.Match(l.Trim(),@"^([0-9a-fA-F]{64}) [ *](.+)$")).Where(m=>m.Success&&m.Groups[2].Value==name).ToArray();
            if(lines.Length!=1)throw new InvalidDataException(LocalizedText.T("RimTube.UpdateChecksumInvalid"));
            return lines[0].Groups[1].Value.ToLowerInvariant();
        }
        public static async Task<string> UpdateAsync(ResolverToolStore store,CancellationToken token)
        {
            using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token))
            using(var client=new HttpClient())
            {
                deadline.CancelAfter(TimeSpan.FromMinutes(3));token=deadline.Token;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("RimTube/0.2.0");
                client.Timeout=TimeSpan.FromSeconds(45);
                string json=Encoding.UTF8.GetString(await Download(client,"https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest",1024*1024,token).ConfigureAwait(false));
                var release=ParseRelease(json);
                string sums=Encoding.UTF8.GetString(await Download(client,release.Url("SHA2-256SUMS"),128*1024,token).ConfigureAwait(false));
                byte[] executable=await Download(client,release.Url("yt-dlp.exe"),release.ExecutableBytes,token).ConfigureAwait(false);
                byte[] noticesZip=await Download(client,release.Url("yt-dlp_win.zip"),release.NoticeBytes,token).ConfigureAwait(false);
                if(executable.LongLength!=release.ExecutableBytes||noticesZip.LongLength!=release.NoticeBytes)throw new InvalidDataException(LocalizedText.T("RimTube.UpdateChecksumInvalid"));
                Verify(executable,ExpectedHash(sums,"yt-dlp.exe"));Verify(noticesZip,ExpectedHash(sums,"yt-dlp_win.zip"));
                string license=Encoding.UTF8.GetString(await Download(client,"https://raw.githubusercontent.com/yt-dlp/yt-dlp/"+release.Version+"/LICENSE",128*1024,token).ConfigureAwait(false));
                string notices=ReadNotices(noticesZip,license,release.Version);
                using(var input=new MemoryStream(executable))
                    store.Install(release.Version,input,ExpectedHash(sums,"yt-dlp.exe"),notices,path=>
                    {
                        var result=ProcessRunner.Run(path,new[]{"--version"},token,15);
                        return result.ExitCode==0?result.Output.Trim():"";
                    },token);
                return release.Version;
            }
        }
        private static void Verify(byte[] bytes,string expected)
        {
            using(var sha=System.Security.Cryptography.SHA256.Create())
                if(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()!=expected)throw new InvalidDataException(LocalizedText.T("RimTube.UpdateChecksumInvalid"));
        }
        private static async Task<byte[]> Download(HttpClient client,string url,long maximum,CancellationToken token)
        {
            using(var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                if(response.Content.Headers.ContentLength>maximum)throw new InvalidDataException(LocalizedText.T("RimTube.UpdateTooLarge"));
                using(var source=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using(var target=new MemoryStream())
                {
                    byte[] buffer=new byte[65536];int read;
                    while((read=await source.ReadAsync(buffer,0,buffer.Length,token).ConfigureAwait(false))>0)
                    {
                        if(target.Length+read>maximum)throw new InvalidDataException(LocalizedText.T("RimTube.UpdateTooLarge"));
                        target.Write(buffer,0,read);
                    }
                    return target.ToArray();
                }
            }
        }
        private static string ReadNotices(byte[] archive,string license,string version)
        {
            var text=new StringBuilder("yt-dlp "+version+"\nhttps://github.com/yt-dlp/yt-dlp\n\n"+license+"\n");int count=0;
            using(var input=new MemoryStream(archive))using(var zip=new ZipArchive(input,ZipArchiveMode.Read))
                foreach(var entry in zip.Entries)
                {
                    if(entry.Name.Length==0||entry.FullName.IndexOf("license",StringComparison.OrdinalIgnoreCase)<0)continue;
                    if(entry.Length>1024*1024||text.Length+entry.Length>3*1024*1024)throw new InvalidDataException(LocalizedText.T("RimTube.UpdateNoticesInvalid"));
                    using(var reader=new StreamReader(entry.Open()))text.Append("\n=== "+entry.FullName+" ===\n").Append(reader.ReadToEnd());
                    count++;
                }
            if(count==0)throw new InvalidDataException(LocalizedText.T("RimTube.UpdateNoticesInvalid"));
            return text.ToString();
        }
    }
}
