using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimTube.Core;
using UnityEngine;
using Verse;

namespace RimTube
{
    public sealed class SyncWindow : Window
    {
        private readonly PlaylistReadResult remote;
        private readonly Library library;
        private readonly object game;
        private readonly string url;
        private readonly Playlist target;
        private readonly List<Track> tracks = new List<Track>();
        private readonly SyncDiff diff;
        private readonly List<string> lines;
        private readonly bool incomplete;
        private bool add = true, remove, reorder;
        private Vector2 scroll;
        public override Vector2 InitialSize { get { return new Vector2(760, 650); } }
        public SyncWindow(PlaylistReadResult info, string source, Playlist existing)
        {
            remote = info; url = source; library = RimTubeMod.App.Library; game = Current.Game;
            target = existing;
            doCloseX = true; draggable = true; forcePause = false; absorbInputAroundWindow = true;
            tracks.AddRange(info.Tracks);
            incomplete = !info.Complete;
            diff = Library.Diff(target ?? new Playlist(), tracks.Select(t => t.Id));
            var names = tracks.ToDictionary(t => t.Id, t => t.Title);
            var added = new HashSet<string>(diff.Added);
            lines = diff.Added.Select(id => "+ " + names[id])
                .Concat(diff.Removed.Select(id => "− " + (library.FindTrack(id)?.Title ?? id)))
                .Concat(tracks.Where(t => !added.Contains(t.Id)).Select(t => "  " + t.Title)).ToList();
        }
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0, 0, rect.width - 25, 32), LocalizedText.T("RimTube.PlaylistPreview") + (target==null?remote.Title:LocalizedText.PlaylistName(target)));
            Widgets.Label(new Rect(0, 40, rect.width, 64), LocalizedText.F("RimTube.PlaylistPreviewSummary",tracks.Count,diff.Added.Count,diff.Removed.Count,LocalizedText.T(diff.OrderChanged?"RimTube.Changed":"RimTube.Unchanged")));
            Widgets.CheckboxLabeled(new Rect(0, 104, rect.width, 28), LocalizedText.T("RimTube.AddNewTracksFromSource"), ref add);
            if (incomplete)
            {
                remove = false; reorder = false;
                Widgets.Label(new Rect(0, 140, rect.width, 68), remote.Message);
            }
            else
            {
                Widgets.CheckboxLabeled(new Rect(0, 140, rect.width, 28), LocalizedText.T("RimTube.RemoveEntriesDeletedAtSourceKeepAudioFiles"), ref remove);
                Widgets.CheckboxLabeled(new Rect(0, 176, rect.width, 28), LocalizedText.T("RimTube.MatchSourceOrderKeepLocalExtrasAtThe"), ref reorder);
            }
            Widgets.Label(new Rect(0, 212, rect.width, 28), LocalizedText.T("RimTube.OnlyPlaylistMetadataIsSavedYouTubeAudioIs"));
            Rect bounds = new Rect(0, 252, rect.width, Math.Max(70, rect.height - 306));
            Widgets.BeginScrollView(bounds, ref scroll, new Rect(0, 0, bounds.width - 20, Math.Max(bounds.height, lines.Count * 28)));
            try { var range=UiGeometry.VisibleRows(lines.Count,scroll.y,bounds.height,28);for(int i=range.First;i<range.End;i++) Widgets.Label(new Rect(0,i*28,bounds.width-24,28),lines[i]); }
            finally { Widgets.EndScrollView(); }
            Ui.Button(new Rect(rect.width - 350, rect.height - 40, 110, 32), L10n.T("RimTube.Cancel"), () => Close());
            Ui.Button(new Rect(rect.width - 232, rect.height - 40, 110, 32), LocalizedText.T("RimTube.ApplyChanges"), () => Apply());
            Ui.Button(new Rect(rect.width - 114, rect.height - 40, 114, 32), LocalizedText.T("RimTube.ApplyAndPlay"), () => Apply(true));
        }
        public void Apply(bool play = false)
        {
            var app = RimTubeMod.App; var lib = app.Library;
            if (!ReferenceEquals(lib, library)) throw new InvalidOperationException(LocalizedText.T("RimTube.TheLibraryChangedReadThePlaylistAgain"));
            if (play && lib.Options.Backend != "mpv") throw new InvalidOperationException(LocalizedText.T("RimTube.SwitchToTheMpvBackendForYouTubeStreaming"));
            var destination = PlaylistImport.Apply(lib, target, url, remote, add, remove, reorder);
            Close();
            if (play && destination.Tracks.Count > 0 && ReferenceEquals(game, Current.Game) && game != null
                && Current.ProgramState == ProgramState.Playing && !LongEventHandler.AnyEventNowOrWaiting)
            { app.Player.Play(destination.Tracks[0], destination.Id); app.Save(); }
        }
    }

    public sealed class AudioFileWindow : Window
    {
        private readonly Action<IEnumerable<string>> selected;
        private string path = "", error = "";
        private string[] dirs = new string[0], files = new string[0];
        private string[] labels=new string[0];
        private Vector2 scroll;
        private static readonly HashSet<string> Extensions = new HashSet<string>(new[] { ".ogg", ".wav", ".mp3", ".flac", ".m4a", ".aac", ".wma", ".aiff", ".opus" }, StringComparer.OrdinalIgnoreCase);
        public override Vector2 InitialSize { get { return new Vector2(780, 620); } }
        public AudioFileWindow(Action<IEnumerable<string>> callback)
        {
            selected = callback; doCloseX = true; draggable = true; forcePause = false;
            path = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (!Directory.Exists(path)) path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Browse();
        }
        private void Browse()
        {
            try
            {
                path = Path.GetFullPath(path.Trim().Trim('"'));
                if (File.Exists(path)) { selected(new[] { path }); Close(); return; }
                dirs = Directory.GetDirectories(path).OrderBy(p => p).ToArray();
                files = Directory.GetFiles(path).Where(p => Extensions.Contains(Path.GetExtension(p))).OrderBy(p => p).ToArray();
                labels=dirs.Select(p=>"[資料夾] "+Path.GetFileName(p)).Concat(files.Select(Path.GetFileName)).ToArray();
                error = ""; scroll = Vector2.zero;
            }
            catch (Exception ex) { error = ex.Message; dirs = files = labels = new string[0]; }
        }
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0, 0, rect.width, 28), "選擇音樂：複製到音樂庫，保留原檔。");
            path = Widgets.TextField(new Rect(0, 38, rect.width - 190, 30), path);
            Ui.Button(new Rect(rect.width - 182, 38, 84, 30), "前往／匯入", Browse);
            Ui.Button(new Rect(rect.width - 90, 38, 90, 30), "上一層", () => { var parent = Directory.GetParent(path); if (parent != null) { path = parent.FullName; Browse(); } });
            Rect bounds = new Rect(0, 80, rect.width, rect.height - 145);
            Widgets.BeginScrollView(bounds, ref scroll, new Rect(0, 0, bounds.width - 20, Math.Max(bounds.height, (dirs.Length + files.Length) * 34)));
            try
            {
                var range=UiGeometry.VisibleRows(labels.Length,scroll.y,bounds.height,34);
                for(int i=range.First;i<range.End;i++)
                {
                    int index=i;
                    if(Ui.Click(new Rect(0,i*34,bounds.width-24,30),labels[i]))
                    {
                        if(index<dirs.Length){string folder=dirs[index];RimTubeMod.App.QueueUi(()=>{path=folder;Browse();});}
                        else{string file=files[index-dirs.Length];RimTubeMod.App.QueueUi(()=>{selected(new[]{file});Close();});}
                    }
                }
            }
            finally { Widgets.EndScrollView(); }
            Widgets.Label(new Rect(0, rect.height - 54, rect.width - 200, 48), error);
            Ui.Button(new Rect(rect.width - 190, rect.height - 46, 190, 34), "匯入此資料夾全部音樂", () => { if (files.Length == 0) throw new InvalidOperationException("沒有音訊檔案。"); selected(files); Close(); });
        }
    }
}
