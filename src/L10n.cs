using System;
using System.Collections.Generic;
using Verse;
using RimTube.Core;

namespace RimTube
{
    internal static class L10n
    {
        private static object language;
        private static readonly Dictionary<string,string> cache=new Dictionary<string,string>(StringComparer.Ordinal);
        public static void Refresh()
        {
            var current=LanguageDatabase.activeLanguage;
            if(ReferenceEquals(current,language))return;
            language=current;cache.Clear();
            var snapshot=new Dictionary<string,string>(StringComparer.Ordinal);
            if(current!=null)foreach(var key in LocalizedText.Keys)snapshot[key]=key.Translate().ToString();
            LocalizedText.Publish(snapshot);
        }
        public static string T(string key)
        {
            Refresh();var current=LanguageDatabase.activeLanguage;
            string value;if(cache.TryGetValue(key,out value))return value;
            value=current==null?key:key.Translate().ToString();cache[key]=value;return value;
        }
        public static string F(string key,params object[] args)=>String.Format(System.Globalization.CultureInfo.CurrentCulture,T(key),args);
    }
}
