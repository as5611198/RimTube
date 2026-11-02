using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimTube.Core;

namespace RimTube.Sources
{
    public sealed class GameSongInfo
    {
        public string DefName="",Title="",PackageId="",SourceName="",Kind="Mod";
        public bool Available,Tense;
        public double Duration;
    }
    // Only plain snapshots cross into source workers; all game API stays in GameMusicCatalog.
    public sealed class GameMusicSource : IMediaSource
    {
        private readonly object gate=new object();
        private Dictionary<string,Track> catalog=new Dictionary<string,Track>();
        public string Id=>"game";
        public SourceCapabilities Capabilities=>SourceCapabilities.Metadata|SourceCapabilities.Sessions|SourceCapabilities.Seek;
        public IReadOnlyList<Track> Tracks { get { lock(gate) return catalog.Values.OrderBy(t=>t.Title).ToArray(); } }
        public GameMusicSource(IEnumerable<GameSongInfo> entries) { Refresh(entries); }
        public void Refresh(IEnumerable<GameSongInfo> entries)
        {
            var next=new Dictionary<string,Track>();
            foreach(var info in entries)
            {
                if(String.IsNullOrWhiteSpace(info.DefName)) continue;
                string id;
                using(var hash=SHA256.Create()) id="game_"+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(info.PackageId.ToLowerInvariant()+"|"+info.DefName))).Replace("-","").ToLowerInvariant();
                var track=new Track {Id=id,SourceId=info.DefName,SourceType=Id,Title=String.IsNullOrWhiteSpace(info.Title)?info.DefName:info.Title,Duration=info.Duration,Availability=info.Available?SourceAvailability.Available:SourceAvailability.Missing};
                track.Metadata["game.package"]=info.PackageId;track.Metadata["game.source"]=info.SourceName;track.Metadata["game.kind"]=info.Kind;track.Metadata["game.tense"]=info.Tense.ToString();
                next[id]=track;
            }
            lock(gate) catalog=next;
        }
        public bool CanHandle(string input)=>input!=null&&input.StartsWith("game_",StringComparison.Ordinal);
        public Task<Track> GetTrackAsync(string input,CancellationToken token)
        { token.ThrowIfCancellationRequested();lock(gate) {Track t;if(!catalog.TryGetValue(input,out t)) throw new MediaException(SourceErrorCode.NotFound,LocalizedText.T("RimTube.TheGameMusicSourceIsDisabledOrRemoved"));return Task.FromResult(t);} }
        public async Task<PlaybackSession> CreateSessionAsync(Track track,CancellationToken token)
        {
            var current=await GetTrackAsync(track.Id,token).ConfigureAwait(false);
            if(current.Availability!=SourceAvailability.Available) throw new MediaException(SourceErrorCode.NotFound,LocalizedText.T("RimTube.ThisGameTrackHasNoLoadedAudioCheck"));
            return new PlaybackSession(track.Id,"rimworld-song:"+current.SourceId,true,true,true,gameSongDef:current.SourceId);
        }
        public Task<PlaylistPage> GetPlaylistPageAsync(string input,string cursor,int size,CancellationToken token)=>throw new MediaException(SourceErrorCode.Unsupported,LocalizedText.T("RimTube.UseTheGameMusicCatalog"));
        public Task<DependencyInfo> CheckDependencyAsync(CancellationToken token)=>Task.FromResult(new DependencyInfo {Available=true,Message=LocalizedText.T("RimTube.UsesLoadedGameMusicNoExternalToolsNeeded")});
    }
}
