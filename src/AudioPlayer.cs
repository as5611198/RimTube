using RimTube.Sources;
using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimTube.Core;
using RimWorld;
using UnityEngine;
using UnityEngine.Networking;
using Verse;

namespace RimTube
{
    internal sealed class BgmLease
    {
        private MusicManagerPlay manager;
        private bool previousDisabled;
        public void Acquire()
        {
            var next = Find.MusicManagerPlay;
            if (next == manager) return;
            Release();
            if (next == null) return;
            manager = next; previousDisabled = next.disabled;
            next.Stop(); // 1.5 ForceSilenceFor does not stop the current song.
            next.disabled = true;
        }
        public void Release()
        {
            if (manager == null) return;
            manager.disabled = previousDisabled;
            if (!previousDisabled) manager.ScheduleNewSong();
            manager = null;
        }
    }

    // Game bridge: all game API access and BGM ownership stay on the main thread.
    public sealed class AudioPlayer : IDisposable
    {
        private readonly PlaybackCoordinator coordinator;
        private readonly IPlaybackBackend backend;
        private readonly MpvBackend mpv;
        private readonly RoutingPlaybackBackend router;
        private readonly NativeLocalBackend native;
        private readonly YouTubeSourceAdapter youtube;
        private readonly BgmLease bgm = new BgmLease();
        private readonly VideoSurface video;
        private Task videoChange,subtitleSizeChange;
        private bool requestedVideo = true;
        public bool HasVideo => mpv!=null&&ReferenceEquals(router.Active,mpv)&&mpv.HasVideo;
        public bool VideoVisible => requestedVideo;
        public bool HasSubtitles => HasVideo&&mpv.HasSubtitles;
        public bool SubtitlesVisible => HasVideo&&mpv.SubtitlesVisible;
        public float VideoAspect => (float)(mpv?.VideoAspect??(16d/9));
        public VideoSurface Video => video;
        public PlayIntent Intent => coordinator.Intent;
        public string CurrentId => coordinator.CurrentId;
        public string Error => coordinator.Error;
        public string Status => coordinator.Status;
        public bool IsLoading => coordinator.IsLoading;
        public bool InCombat => coordinator.InCombat;
        public float Position => (float)coordinator.Position;
        public float Duration => (float)coordinator.Duration;
        public bool CanSeek => coordinator.CanSeek;
        public bool IsLive => coordinator.IsLive;
        public bool CanRemoveMedia(string id)=>coordinator.CanRemoveMedia(id);
        public PlaybackSnapshot Snapshot => coordinator.Snapshot;
        public int ExternalProcessId => mpv?.ProcessId ?? 0;
        public event Action ManualAction;
        public AudioPlayer(MonoBehaviour host, Library library,GameMusicSource gameMusic=null)
        {
            var sources = new SourceRegistry(); sources.Register(new LocalFileSource(library));
            youtube = new YouTubeSourceAdapter(library.Options, library.Options.Backend != "native-local", library); sources.Register(youtube);
            sources.Register(gameMusic??new GameMusicSource(GameMusicCatalog.Read()));
            native=new NativeLocalBackend(host);
            IPlaybackBackend primary=native;
            if(library.Options.Backend!="native-local") {video=new VideoSurface();mpv=new MpvBackend(library.Options,video.Handle);primary=mpv;}
            backend=router=new RoutingPlaybackBackend(primary,native);
            coordinator = new PlaybackCoordinator(library, sources, backend, own => { if (own) bgm.Acquire(); else bgm.Release(); });
        }
        public void Play(string id = null, string listId = null) { ManualAction?.Invoke(); coordinator.Play(id, listId); }
        public Task<Track> InspectYouTubeAsync(string input, MediaChoice choice, CancellationToken token) => youtube.GetTrackAsync(input, token, choice);
        public void BeginSourceLookup(object request,object game)=>coordinator.BeginSourceLookup(request,game);
        public void EndSourceLookup(object request)=>coordinator.EndSourceLookup(request);
        public void Pause() { ManualAction?.Invoke(); coordinator.Pause(); }
        public void Stop() { ManualAction?.Invoke(); coordinator.Stop(); }
        public void Next(bool previous = false) { ManualAction?.Invoke(); coordinator.Next(previous); }
        public void Seek(float position) => coordinator.Seek(position);
        public void ClearError() => coordinator.ClearError();
        public bool ChangeVideoQuality(string expectedTrackId,int height)
        {
            bool changed=coordinator.ChangeVideoQuality(expectedTrackId,height);
            if(changed) ManualAction?.Invoke();
            return changed;
        }
        public bool ChangeVideoSubtitles(string expectedTrackId,string language,bool automatic)
        {
            bool changed=coordinator.ChangeVideoSubtitles(expectedTrackId,language,automatic);
            if(changed){if(mpv!=null)mpv.SubtitlesRequested=true;ManualAction?.Invoke();}return changed;
        }
        public void ShowVideo(bool show)
        {
            requestedVideo = show;
            if (!show) video?.Hide();
        }
        public void ShowSubtitles(bool show)
        {
            if (mpv == null) return;
            var task = mpv.SetSubtitlesVisibleAsync(show, CancellationToken.None);
            task.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }
        public void Tick()
        {
            native?.Pump();
            if(subtitleSizeChange!=null&&subtitleSizeChange.IsCompleted){if(subtitleSizeChange.IsFaulted){var ignored=subtitleSizeChange.Exception;}subtitleSizeChange=null;}
            float subtitleScale=RimTubeMod.App?.Interface?.Prefs.SubtitleScale??1.5f;
            if(mpv!=null&&subtitleSizeChange==null&&Math.Abs(mpv.SubtitleScale-subtitleScale)>.001)
                subtitleSizeChange=mpv.SetSubtitleScaleAsync(subtitleScale,CancellationToken.None);
            // LoadGame queues a long event before ProgramState changes. During that
            // interval Game/Scenario/Maps may already be torn down or only partly loaded.
            bool active = !LongEventHandler.AnyEventNowOrWaiting && Current.Game != null
                && Current.ProgramState == ProgramState.Playing && Current.Game.Scenario != null;
            coordinator.Tick(new PlaybackEnvironment { GameActive = active, GameIdentity = Current.Game,
                Danger = active && Find.MusicManagerPlay != null && Find.MusicManagerPlay.DangerMusicMode,
                GamePaused = active && Find.TickManager != null && Find.TickManager.Paused,
                VideoCovered = active && HasVideo && requestedVideo && VideoWindow.ShouldAutoHide,
                Time = Time.realtimeSinceStartup, MasterVolume = Prefs.VolumeMaster });
            if (!HasVideo || !active || !VideoVisible) video?.Hide();
            if (videoChange != null && videoChange.IsCompleted) { if (videoChange.IsFaulted) { var ignored = videoChange.Exception; } videoChange = null; }
            if (mpv != null && videoChange == null && !coordinator.IsLoading && mpv.VideoVisible != requestedVideo)
                videoChange = mpv.SetVideoVisibleAsync(requestedVideo, CancellationToken.None);
        }
        public void Dispose() { coordinator.Dispose(); youtube.Dispose(); video?.Dispose(); bgm.Release(); }
    }
}
