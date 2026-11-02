using System;
using System.Threading;
using System.Threading.Tasks;

namespace RimTube.Core
{
    public sealed class RoutingPlaybackBackend : IPlaybackBackend
    {
        private readonly IPlaybackBackend primary,native;
        public IPlaybackBackend Active {get;private set;}
        private bool disposed;
        public RoutingPlaybackBackend(IPlaybackBackend primary,IPlaybackBackend native) {this.primary=primary;this.native=native;Active=primary;}
        public string Id=>Active.Id;
        public PlaybackSnapshot Snapshot=>Active.Snapshot;
        public Task InitializeAsync(CancellationToken token) {if(disposed) throw new ObjectDisposedException(nameof(RoutingPlaybackBackend));token.ThrowIfCancellationRequested();return Task.CompletedTask;}
        public async Task PrepareAsync(PlaybackSession session,CancellationToken token)
        {
            session.Validate();var next=String.IsNullOrEmpty(session.GameSongDef)?primary:native;
            if(!ReferenceEquals(next,Active)) {await Active.StopAsync(token).ConfigureAwait(false);Active=next;}
            await next.InitializeAsync(token).ConfigureAwait(false);
            await next.PrepareAsync(session,token).ConfigureAwait(false);
        }
        public Task PlayAsync(CancellationToken token)=>Active.PlayAsync(token);
        public Task ResumeAsync(CancellationToken token)=>Active.ResumeAsync(token);
        public Task PauseAsync(CancellationToken token)=>Active.PauseAsync(token);
        public Task StopAsync(CancellationToken token)=>Active.StopAsync(token);
        public Task SeekAsync(double position,CancellationToken token)=>Active.SeekAsync(position,token);
        public Task SetVolumeAsync(double volume,CancellationToken token)=>Active.SetVolumeAsync(volume,token);
        public void Dispose() {if(disposed)return;disposed=true;primary.Dispose();if(!ReferenceEquals(primary,native))native.Dispose();}
    }
}
