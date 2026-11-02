using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RimTube.Core
{
    public enum MediaDelivery { Stream, Download, PreferSaved }
    public sealed class MediaChoice
    {
        public bool Video, AutoSubtitles;
        public int Height = 720;
        public MediaDelivery Delivery = MediaDelivery.Stream;
        public string Subtitle = "off";
        public MediaChoice Copy() => new MediaChoice { Video = Video, Height = Height, Delivery = Delivery, Subtitle = Subtitle, AutoSubtitles = AutoSubtitles };
        public void Validate()
        {
            if (Height < 144 || Height > 4320) throw new ArgumentException(LocalizedText.T("RimTube.TheQualityLimitMustBeBetween144pAnd"));
            if (!Regex.IsMatch(Subtitle ?? "", "^(off|[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8}){0,2})$")) throw new ArgumentException(LocalizedText.T("RimTube.EnterACaptionLanguageCodeSuchAsZh"));
            if (!Enum.IsDefined(typeof(MediaDelivery), Delivery)) throw new ArgumentException(LocalizedText.T("RimTube.UnsupportedMediaAcquisitionMode"));
        }
        public void Store(Track track)
        {
            Validate(); track.Metadata["media.video"] = Video.ToString(); track.Metadata["media.height"] = Height.ToString(CultureInfo.InvariantCulture);
            track.Metadata["media.delivery"] = Delivery.ToString(); track.Metadata["media.subtitle"] = Subtitle; track.Metadata["media.autoSubs"] = AutoSubtitles.ToString();
        }
        public static MediaChoice From(Track track)
        {
            var choice = new MediaChoice(); string value; bool flag; int height; MediaDelivery delivery;
            if (track.Metadata.TryGetValue("media.video", out value) && Boolean.TryParse(value, out flag)) choice.Video = flag;
            if (track.Metadata.TryGetValue("media.height", out value) && Int32.TryParse(value, out height)) choice.Height = Math.Max(144, Math.Min(4320, height));
            if (track.Metadata.TryGetValue("media.delivery", out value) && Enum.TryParse(value, out delivery) && Enum.IsDefined(typeof(MediaDelivery), delivery)) choice.Delivery = delivery;
            if (track.Metadata.TryGetValue("media.subtitle", out value) && Regex.IsMatch(value, "^(off|[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8}){0,2})$")) choice.Subtitle = value;
            if (track.Metadata.TryGetValue("media.autoSubs", out value) && Boolean.TryParse(value, out flag)) choice.AutoSubtitles = flag;
            return choice;
        }
        public string Format => Video ? "bestvideo[height<=" + Height.ToString(CultureInfo.InvariantCulture) + "]+bestaudio/best[height<=" + Height.ToString(CultureInfo.InvariantCulture) + "]" : "bestaudio/best";
        public string StreamFormat
        {
            get
            {
                if(!Video)return "bestaudio/best";
                string cap="[height<="+Height.ToString(CultureInfo.InvariantCulture)+"]";
                // Match audio streaming's manifest preference; direct formats can be advertised
                // yet return HTTP 403. Keep ordinary direct and combined formats as fallbacks.
                string audio="(bestaudio[protocol=m3u8_native]/bestaudio[protocol=m3u8]/bestaudio)";
                return "(bestvideo"+cap+"[protocol=m3u8_native]/bestvideo"+cap+"[protocol=m3u8]/bestvideo"+cap+")+"+audio+"/best"+cap;
            }
        }
    }
}
