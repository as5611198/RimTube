using System;
using RimTube.Core;
using UnityEngine;
using Verse;

namespace RimTube
{
    public sealed class TrackDetailsWindow : Window
    {
        private readonly Track track;
        private readonly Library library;
        private readonly bool game;
        private readonly string sourceInfo;
        private string title, category;
        public override Vector2 InitialSize => new Vector2(680, 370);
        public TrackDetailsWindow(Track track)
        {
            this.track=track;library=RimTubeMod.App.Library;game=track.SourceType=="game";
            title=track.Title;category=track.Category;doCloseX=true;draggable=true;forcePause=false;
            string source;sourceInfo=game?(track.Metadata.TryGetValue("game.source",out source)?source:LocalizedText.T("RimTube.GameMusicMessage")):track.IsYouTube?"YouTube":LocalizedText.T("RimTube.LocalAudio");
        }
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0, 0, rect.width - 30, 28), LocalizedText.T("RimTube.TrackDetails"));
            Widgets.Label(new Rect(0,42,70,28),LocalizedText.T("RimTube.Title"));
            if(game) Widgets.Label(new Rect(76,40,rect.width-76,30),title);else title=Widgets.TextField(new Rect(76,40,rect.width-76,30),title);
            Widgets.Label(new Rect(0,82,70,28),LocalizedText.T("RimTube.Category"));
            if(game) Widgets.Label(new Rect(76,80,rect.width-76,30),LocalizedText.T("RimTube.GameMusicMessage"));else category=Widgets.TextField(new Rect(76,80,rect.width-76,30),category);
            Widgets.Label(new Rect(0,126,rect.width,100),sourceInfo+" · "+track.SourceId+"\n"+track.Url+"\n"+Ui.Time((float)track.Duration));
            Widgets.Label(new Rect(0,230,rect.width,55),game?LocalizedText.T("RimTube.UsesLoadedGameMusicDefinitionsGameAssetsAre"):track.IsYouTube?LocalizedText.T("RimTube.SourceSavedUseMediaOptionsToChooseStreaming"):LocalizedText.T("RimTube.LibraryCopiesWorkOfflineDeletingACopyDoes"));
            Ui.Button(new Rect(rect.width - 110, rect.height - 40, 110, 32),game?L10n.T("RimTube.Close"):L10n.T("RimTube.Save"), () =>
            {
                if(game){Close();return;}
                if(!ReferenceEquals(RimTubeMod.App.Library,library)||library.FindTrack(track.Id)!=track) throw new InvalidOperationException(LocalizedText.T("RimTube.TheLibraryOrTrackChangedOpenThisWindow"));
                if (RimTubeMod.App.Library.ReadOnly) throw new InvalidOperationException(RimTubeMod.App.Library.LoadWarning);
                if (!String.IsNullOrWhiteSpace(title)) track.Title = title.Trim(); track.Category = category.Trim(); RimTubeMod.App.Save(); Close();
            });
        }
    }
}
