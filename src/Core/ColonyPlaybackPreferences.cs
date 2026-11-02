namespace RimTube.Core
{
    public sealed class ColonyPlaybackPreferences
    {
        public string General="general",Combat="combat";
        public BattleMode Battle=BattleMode.Vanilla;
        public bool PauseWithGame,PauseCoveredVideo;
        public static ColonyPlaybackPreferences Capture(Settings s)=>new ColonyPlaybackPreferences {General=s.GeneralList,Combat=s.CombatList,Battle=s.Battle,PauseWithGame=s.PauseWithGame,PauseCoveredVideo=s.PauseCoveredVideo};
        public void Apply(Settings s){s.GeneralList=General;s.CombatList=Combat;s.Battle=Battle;s.PauseWithGame=PauseWithGame;s.PauseCoveredVideo=PauseCoveredVideo;}
    }
}
