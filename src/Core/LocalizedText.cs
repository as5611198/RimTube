using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace RimTube.Core
{
    // The game publishes a complete immutable snapshot on language changes.
    // Workers never access Verse, Unity or mutable translation caches.
    public static partial class LocalizedText
    {
        private static Dictionary<string,string> current;
        public static IEnumerable<string> Keys => defaults.Keys;
        public static void Publish(IDictionary<string,string> translations)
        {
            var next=translations==null?null:new Dictionary<string,string>(translations,StringComparer.Ordinal);
            Volatile.Write(ref current,next);
        }
        public static string T(string key)
        {
            string value;var snapshot=Volatile.Read(ref current);
            if(snapshot!=null&&snapshot.TryGetValue(key,out value)&&!String.IsNullOrEmpty(value))return value;
            return defaults.TryGetValue(key,out value)?value:key;
        }
        public static string F(string key,params object[] args)=>String.Format(CultureInfo.CurrentCulture,T(key),args);
        public static string Message(string text)
        {
            string key;return text!=null&&messageKeys.TryGetValue(text,out key)?T(key):text??"";
        }
        public static string PlaylistName(Playlist list)
        {
            if(list==null)return T("RimTube.Unassigned");
            string name=list.Name??"";
            if(list.Id=="general"&&(name==defaults["RimTube.EverydayMusic"]||name=="Everyday music"))return T("RimTube.EverydayMusic");
            if(list.Id=="combat"&&(name==defaults["RimTube.CombatMusic"]||name=="Combat music"))return T("RimTube.CombatMusic");
            if(list.IsGameCatalog)
            {
                if(list.SourceId=="ludeon.rimworld")return T("RimTube.BaseGameRimWorld");
                foreach(string prefix in new[]{defaults["RimTube.Mod"],"Mod · "})
                    if(name.StartsWith(prefix,StringComparison.Ordinal))return T("RimTube.Mod")+name.Substring(prefix.Length);
            }
            return name;
        }
        public static string BackendName(BackendState state)
        {
            switch(state)
            {
                case BackendState.Idle:return T("RimTube.StateIdle");
                case BackendState.Initializing:return T("RimTube.StateInitializing");
                case BackendState.Preparing:return T("RimTube.StatePreparing");
                case BackendState.Ready:return T("RimTube.StateReady");
                case BackendState.Playing:return T("RimTube.Playing");
                case BackendState.Paused:return T("RimTube.Paused");
                case BackendState.Buffering:return T("RimTube.Buffering");
                case BackendState.Ended:return T("RimTube.StateEnded");
                case BackendState.Stopped:return T("RimTube.Stopped");
                case BackendState.Failed:return T("RimTube.Failed");
                default:return T("RimTube.StateDisposed");
            }
        }
        public static string SyncName(SyncState state)
        {
            switch(state)
            {
                case SyncState.Complete:return T("RimTube.SyncComplete");
                case SyncState.Partial:return T("RimTube.SyncPartial");
                case SyncState.Failed:return T("RimTube.Failed");
                case SyncState.AdapterRequired:return T("RimTube.SyncAdapterRequired");
                default:return T("RimTube.SyncNever");
            }
        }
    }
}
