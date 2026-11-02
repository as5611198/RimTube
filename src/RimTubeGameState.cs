using RimTube.Core;
using Verse;

namespace RimTube
{
    // Library contents are shared; colony playback roles belong to the save.
    public sealed class RimTubeGameState : GameComponent
    {
        private ColonyPlaybackPreferences prefs=new ColonyPlaybackPreferences();
        private bool configured;
        public RimTubeGameState(Game game) {}
        public override void StartedNewGame(){configured=true;Apply();}
        public override void LoadedGame(){Apply();}
        public void Apply()
        {
            var app=RimTubeMod.App;if(app==null) return;
            if(!configured) {Capture();return;}
            prefs.Apply(app.Library.Options);app.Interface?.Invalidate();
        }
        private void Capture()
        {
            var app=RimTubeMod.App;if(app==null) return;
            prefs=ColonyPlaybackPreferences.Capture(app.Library.Options);configured=true;
        }
        public override void ExposeData()
        {
            if(Scribe.mode==LoadSaveMode.Saving) Capture();
            Scribe_Values.Look(ref configured,"configured",false);
            Scribe_Values.Look(ref prefs.General,"general","general");Scribe_Values.Look(ref prefs.Combat,"combat","combat");
            Scribe_Values.Look(ref prefs.Battle,"battle",BattleMode.Vanilla);Scribe_Values.Look(ref prefs.PauseWithGame,"pauseWithGame",false);
            Scribe_Values.Look(ref prefs.PauseCoveredVideo,"pauseCoveredVideo",false);
        }
    }
}
