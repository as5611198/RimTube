using System;
using RimTube.Core;
using UnityEngine;
using Verse;

namespace RimTube
{
    internal static class UiHotkeys
    {
        public static void Handle(UiSession ui)
        {
            var e=Event.current;
            if(e==null||e.type!=EventType.KeyDown||GUIUtility.keyboardControl!=0||Find.WindowStack.IsOpen<ShortcutWindow>()) return;
            foreach(var binding in ui.Prefs.Shortcuts)
            {
                if(!binding.Matches((int)e.keyCode,e.control,e.alt,e.shift)) continue;
                RimTubeMod.App.QueueUi(()=>Execute(ui,binding.Action));e.Use();break;
            }
        }
        private static void Execute(UiSession ui,string action)
        {
            switch(action)
            {
                case "panel": if(ui.PanelOpen) ui.ClosePanel();else RimTubeMod.OpenPlayer();break;
                case "play":ui.TogglePlay();break;
                case "next":RimTubeMod.App.Player.Next();break;
                case "mute":ui.ToggleMute();break;
                case "restore":ui.RestoreMini();break;
                case "lock":ui.Prefs.MiniLocked=!ui.Prefs.MiniLocked;ui.Prefs.ShowMini=true;ui.Changed();break;
            }
        }
        public static string Name(string action)
        {switch(action){case "panel":return L10n.T("RimTube.HotkeyPanel");case "play":return L10n.T("RimTube.HotkeyPlay");case "next":return L10n.T("RimTube.Next");case "mute":return L10n.T("RimTube.Mute");case "restore":return L10n.T("RimTube.HotkeyRestore");default:return L10n.T("RimTube.HotkeyLock");}}
        public static string Label(UiShortcut key)=>key.Key==0?L10n.T("RimTube.NotSet"):(key.Control?"Ctrl + ":"")+(key.Alt?"Alt + ":"")+(key.Shift?"Shift + ":"")+((KeyCode)key.Key).ToString();
    }
    internal sealed class ShortcutWindow : Window
    {
        private readonly UiShortcut binding;
        public override Vector2 InitialSize=>new Vector2(520,200);
        public ShortcutWindow(UiShortcut binding){this.binding=binding;doCloseX=true;forcePause=false;closeOnAccept=false;}
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0,0,rect.width,30),LocalizedText.T("RimTube.SetShortcut")+UiHotkeys.Name(binding.Action));
            Widgets.Label(new Rect(0,45,rect.width,70),LocalizedText.T("RimTube.PressANewKeyCombinationEscCancelsBackspace"));
            var e=Event.current;if(e.type!=EventType.KeyDown) return;
            if(e.keyCode==KeyCode.Escape){e.Use();Close(false);return;}
            if(e.keyCode==KeyCode.LeftControl||e.keyCode==KeyCode.RightControl||e.keyCode==KeyCode.LeftAlt||e.keyCode==KeyCode.RightAlt||e.keyCode==KeyCode.LeftShift||e.keyCode==KeyCode.RightShift||e.keyCode==KeyCode.None) return;
            int code=e.keyCode==KeyCode.Backspace?0:(int)e.keyCode;
            foreach(var other in RimTubeMod.App.Interface.Prefs.Shortcuts)
                if(other!=binding&&other.Matches(code,e.control,e.alt,e.shift)){RimTubeMod.App.Error=LocalizedText.T("RimTube.ThisCombinationIsAlreadyAssignedToAnotherRimTube");e.Use();return;}
            binding.Key=code;binding.Control=e.control;binding.Alt=e.alt;binding.Shift=e.shift;RimTubeMod.App.Interface.Changed();e.Use();Close(false);
        }
    }
}
