using RimWorld;
using Verse;

namespace RimTube
{
    internal static class GameUi
    {
        // Find.MainTabsRoot performs a direct cast and is unsafe on entry/transition roots.
        public static MainTabsRoot MainTabs => (Find.UIRoot as UIRoot_Play)?.mainButtonsRoot?.tabs;

        public static void ClosePlayerTab()
        {
            var tabs = MainTabs;
            if (tabs == null) return;
            var button = DefDatabase<MainButtonDef>.GetNamedSilentFail("RimTube");
            if (button != null && tabs.OpenTab == button) tabs.ToggleTab(button);
        }
    }
}
