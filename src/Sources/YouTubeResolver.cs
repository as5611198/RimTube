using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimTube.Core;

namespace RimTube.Sources
{
    // This resolver only emits metadata. Audio is fetched by the playback backend.
    internal sealed class YouTubeResolver
    {
        private readonly string tool, js;
        public YouTubeResolver(Settings settings) { tool = settings.YtDlp; js = settings.JsRuntime; }
        // Populated by DataContractJsonSerializer.
#pragma warning disable 0649
        [DataContract] internal sealed class Info
        {
            [DataMember(Name="id")] public string Id;
            [DataMember(Name="title")] public string Title;
            [DataMember(Name="url")] public string Url;
            [DataMember(Name="duration")] public double? Duration;
            [DataMember(Name="availability")] public string Availability;
            [DataMember(Name="is_live")] public bool Live;
            [DataMember(Name="live_status")] public string LiveStatus;
            [DataMember(Name="vcodec")] public string VideoCodec;
            [DataMember(Name="acodec")] public string AudioCodec;
            [DataMember(Name="protocol")] public string Protocol;
            [DataMember(Name="http_headers")] public Dictionary<string,string> Headers;
            [DataMember(Name="requested_formats")] public List<Info> Formats;
            [DataMember(Name="height")] public int? Height;
            [DataMember(Name="subtitles")] public Dictionary<string,List<Caption>> Subtitles;
            [DataMember(Name="automatic_captions")] public Dictionary<string,List<Caption>> AutoCaptions;
            public string SelectedSubtitle;
            public string SelectedSubtitleKey;
            public bool SelectedAutoSubtitle;
        }
        [DataContract] internal sealed class Caption
        {
            [DataMember(Name="ext")] public string Extension;
            [DataMember(Name="url")] public string Url;
        }
#pragma warning restore 0649
        public Task<Info> ResolveAsync(string input, CancellationToken token, MediaChoice choice = null)
        {
            string url = YoutubeClient.Normalize(input, false);
            var selection = (choice ?? new MediaChoice()).Copy(); selection.Validate();
            return Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                if (String.IsNullOrWhiteSpace(tool) || !File.Exists(tool))
                    throw new MediaException(SourceErrorCode.MissingTool, LocalizedText.T("RimTube.SetTheYtDlpExePathInSettingsMessage"));
                var args = new List<string> { "--ignore-config", "--no-plugin-dirs", "--no-remote-components", "--no-cache-dir",
                    "--no-cookies", "--no-cookies-from-browser", "--no-js-runtimes", "--encoding", "utf-8", "--socket-timeout", "15", "--retries", "1",
                    "--extractor-retries", "1", "--no-playlist", "--skip-download", "--dump-single-json", "--format", selection.Video ? selection.StreamFormat : "bestaudio[protocol=m3u8_native]/bestaudio[protocol=m3u8]/bestaudio[protocol=https]/bestaudio" };
                if (!String.IsNullOrWhiteSpace(js))
                {
                    if (!File.Exists(js)) throw new MediaException(SourceErrorCode.MissingTool, LocalizedText.T("RimTube.TheConfiguredDenoExecutableWasNotFoundCorrect"));
                    args.Add("--js-runtimes"); args.Add(JavaScriptRuntime.ResolverArgument(js));
                }
                args.Add("--"); args.Add(url);
                ProcessResult result;
                try { result = ProcessRunner.Run(Path.GetFullPath(tool), args, token, 90); }
                catch (OperationCanceledException) { throw; }
                catch (TimeoutException) { throw new MediaException(SourceErrorCode.Timeout, LocalizedText.T("RimTube.YouTubeResolutionTimedOutCheckYourNetworkAnd")); }
                catch { throw new MediaException(SourceErrorCode.MissingTool, LocalizedText.T("RimTube.CouldNotStartYtDlpCheckTheTool")); }
                if (result.ExitCode != 0)
                {
                    string error = (result.Error ?? "").ToLowerInvariant();
                    bool restricted = error.Contains("sign in") || error.Contains("login") || error.Contains("private") || error.Contains("members-only")
                        || error.Contains("not available in your country") || error.Contains("age-restricted") || error.Contains("bot");
                    throw new MediaException(restricted ? SourceErrorCode.AuthorizationRequired : SourceErrorCode.NotFound,
                        restricted ? LocalizedText.T("RimTube.ThisYouTubeContentRequiresLoginOrHasAccess")
                        : LocalizedText.T("RimTube.YouTubeSuppliedNoPlayablePublicAudioCheckThe"));
                }
                Info info;
                try
                {
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(result.Output)))
                        info = (Info)new DataContractJsonSerializer(typeof(Info), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }).ReadObject(stream);
                }
                catch { throw new MediaException(SourceErrorCode.Incomplete, LocalizedText.T("RimTube.TheYouTubeToolReturnedIncompleteTrackInformationUpdate")); }
                if (info == null || info.Id != url.Substring(url.LastIndexOf('=') + 1))
                    throw new MediaException(SourceErrorCode.Incomplete, LocalizedText.T("RimTube.TheResolvedYouTubeTrackDoesNotMatchThe"));
                if (info.LiveStatus == "is_upcoming")
                    throw new MediaException(SourceErrorCode.Unsupported, LocalizedText.T("RimTube.ThisLiveStreamPremiereHasNotStartedTry"));
                if (!String.IsNullOrEmpty(info.Availability) && info.Availability != "public" && info.Availability != "unlisted")
                    throw new MediaException(SourceErrorCode.AuthorizationRequired, LocalizedText.T("RimTube.ThisYouTubeContentIsNotDirectlyAccessiblePublic"));
                // HLS audio playlists can defer codec discovery until the manifest is opened.
                bool manifestAudio = info.Protocol == "m3u8_native" || info.Protocol == "m3u8";
                if (!selection.Video && (info.VideoCodec != "none" || (!manifestAudio && String.IsNullOrEmpty(info.AudioCodec)) || info.AudioCodec == "none"))
                    throw new MediaException(SourceErrorCode.Unsupported, LocalizedText.T("RimTube.TheSourceProvidesNoSupportedSeparateAudioStream"));
                if (selection.Subtitle != "off")
                {
                    string selectedKey;
                    var caption=FindCaption(info.Subtitles,selection.Subtitle,out selectedKey);
                    if(caption==null&&selection.AutoSubtitles)
                    {caption=FindCaption(info.AutoCaptions,selection.Subtitle,out selectedKey);info.SelectedAutoSubtitle=caption!=null;}
                    if (caption != null)
                    {
                        Uri captionUrl;
                        if (!Uri.TryCreate(caption.Url, UriKind.Absolute, out captionUrl) || captionUrl.Scheme != "https" || !String.IsNullOrEmpty(captionUrl.UserInfo)
                            || (captionUrl.IdnHost != "www.youtube.com" && captionUrl.IdnHost != "youtube.com") || captionUrl.AbsolutePath != "/api/timedtext")
                            throw new MediaException(SourceErrorCode.Unsupported, LocalizedText.T("RimTube.UnsupportedCaptionSourceURL"));
                        info.SelectedSubtitle = caption.Url;
                        info.SelectedSubtitleKey=selectedKey;
                    }
                }
                Session(info, "yt_" + info.Id, selection.Video).Validate();
                return info;
            }, token);
        }
        private static Caption FindCaption(Dictionary<string,List<Caption>> available,string requested,out string selectedKey)
        {
            selectedKey=null;if(available==null)return null;
            string language=SubtitleLanguage.Normalize(requested);
            if(language=="") return null;
            var ordered=available.OrderBy(p=>String.Equals(p.Key,requested,StringComparison.OrdinalIgnoreCase)?0:SubtitleLanguage.Normalize(p.Key)==language?1:2);
            foreach(var item in ordered)
            {
                string normalized=SubtitleLanguage.Normalize(item.Key);
                bool generic=language.IndexOf('-')<0&&normalized.StartsWith(language+"-",StringComparison.Ordinal);
                if(!SubtitleLanguage.SameScript(normalized,language)&&!generic)continue;
                var caption=item.Value?.FirstOrDefault(c=>c.Extension=="vtt")??item.Value?.FirstOrDefault(c=>c.Extension=="srt");
                if(caption==null)continue;selectedKey=item.Key;return caption;
            }
            return null;
        }
        internal static PlaybackSession Session(Info info, string trackId, bool video = false)
        {
            var main = video && info.Formats != null ? info.Formats.FirstOrDefault(f => !String.IsNullOrEmpty(f.VideoCodec) && f.VideoCodec != "none") : info;
            // HLS can identify the audio codec only after mpv opens its manifest.
            // Keep that selected audio URL just as we do for audio-only HLS sessions.
            var audio = video && info.Formats != null ? info.Formats.FirstOrDefault(f => f.VideoCodec == "none" && f.AudioCodec != "none"
                && (!String.IsNullOrEmpty(f.AudioCodec) || f.Protocol == "m3u8_native" || f.Protocol == "m3u8")) : null;
            if (main == null || (video && (String.IsNullOrEmpty(main.VideoCodec) || main.VideoCodec == "none")))
                throw new MediaException(SourceErrorCode.Unsupported, LocalizedText.T("RimTube.TheSourceProvidesNoVideoForTheSelected"));
            DateTime expiry = DateTime.UtcNow.AddMinutes(20);
            if (String.IsNullOrEmpty(main.Url)) throw new MediaException(SourceErrorCode.Unsupported, LocalizedText.T("RimTube.TheSourceHasNoMediaLocation"));
            foreach (string location in new[] { main.Url, audio?.Url })
            {
                if (String.IsNullOrEmpty(location)) continue;
                Uri uri;
                if (!Uri.TryCreate(location, UriKind.Absolute, out uri) || uri.Scheme != "https" || !String.IsNullOrEmpty(uri.UserInfo)
                    || !(uri.IdnHost.EndsWith(".googlevideo.com", StringComparison.OrdinalIgnoreCase)
                        || ((uri.IdnHost == "www.youtube.com" || uri.IdnHost == "youtube.com") && uri.AbsolutePath.StartsWith("/api/manifest/", StringComparison.Ordinal))))
                    throw new MediaException(SourceErrorCode.Unsupported, LocalizedText.T("RimTube.TheYouTubeToolReturnedNoSupportedMediaLocation"));
                foreach (string part in uri.Query.TrimStart('?').Split('&'))
                {
                    if (!part.StartsWith("expire=", StringComparison.Ordinal)) continue;
                    long unix;
                    if (Int64.TryParse(part.Substring(7), out unix))
                    { try { var serverExpiry = new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(unix).AddSeconds(-30); if (serverExpiry < expiry) expiry = serverExpiry; } catch (ArgumentOutOfRangeException) { } }
                }
            }
            bool live = info.Live || info.LiveStatus == "is_live";
            return new PlaybackSession(trackId, main.Url, false, !live, true, expiry, info.Headers, video, live, audio?.Url, info.SelectedSubtitle);
        }
    }
}
