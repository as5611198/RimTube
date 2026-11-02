using System;
using System.Text.RegularExpressions;

namespace RimTube.Core
{
    public static class SubtitleLanguage
    {
        public static string Normalize(string sourceKey)
        {
            string code=sourceKey??"";
            if(!Regex.IsMatch(code,"^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8}){0,2}$")) code=Regex.Replace(code,"-[A-Za-z0-9_-]{11}$","");
            if(code.EndsWith("-orig",StringComparison.OrdinalIgnoreCase)) code=code.Substring(0,code.Length-5);
            if(!Regex.IsMatch(code,"^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8}){0,2}$")) return "";
            var parts=code.Split('-');parts[0]=parts[0].ToLowerInvariant();
            for(int i=1;i<parts.Length;i++) parts[i]=parts[i].Length==2?parts[i].ToUpperInvariant():parts[i].Length==4?Char.ToUpperInvariant(parts[i][0])+parts[i].Substring(1).ToLowerInvariant():parts[i].ToLowerInvariant();
            return String.Join("-",parts);
        }
        public static bool SameScript(string a,string b)
        {
            a=Normalize(a);b=Normalize(b);
            if(a==""||b=="") return false;
            return a==b||Traditional(a)&&Traditional(b)||Simplified(a)&&Simplified(b);
        }
        private static bool Traditional(string code)=>code=="zh-TW"||code=="zh-Hant";
        private static bool Simplified(string code)=>code=="zh-CN"||code=="zh-Hans";
    }
}
