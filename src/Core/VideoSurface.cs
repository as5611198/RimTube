using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RimTube.Core
{
    // Native child surface; mpv owns all decoding/rendering and A/V synchronisation.
    public sealed class VideoSurface : IDisposable
    {
        public IntPtr Handle { get; private set; }
        private readonly IntPtr parent;
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(int ex, string cls, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
        private static IntPtr CurrentWindow()
        {
            using (var process = Process.GetCurrentProcess()) return process.MainWindowHandle;
        }
        public VideoSurface() : this(CurrentWindow()) { }
        public VideoSurface(IntPtr parentWindow)
        {
            parent = parentWindow;
            if (parent == IntPtr.Zero) return;
            // A disabled child and its descendants cannot take keyboard focus or mouse input.
            // WS_EX_NOACTIVATE also protects the game's focus when the surface is shown.
            Handle = CreateWindowEx(0x08000000, "STATIC", "RimTube video", 0x40000000 | 0x08000000 | 0x04000000 | 0x02000000,
                0, 0, 16, 16, parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }
        public bool Place(float x, float y, float width, float height, float uiWidth, float uiHeight)
        {
            Rect client;
            if (Handle == IntPtr.Zero || uiWidth <= 0 || uiHeight <= 0 || !GetClientRect(parent, out client)) return false;
            float scaleX = (client.Right - client.Left) / uiWidth, scaleY = (client.Bottom - client.Top) / uiHeight;
            SetWindowPos(Handle, IntPtr.Zero, (int)(x * scaleX), (int)(y * scaleY), Math.Max(1, (int)(width * scaleX)), Math.Max(1, (int)(height * scaleY)), 0x0010 | 0x0040);
            ShowWindow(Handle, 4); return true;
        }
        public void Hide() { if (Handle != IntPtr.Zero) ShowWindow(Handle, 0); }
        public void Dispose() { if (Handle != IntPtr.Zero) { DestroyWindow(Handle); Handle = IntPtr.Zero; } }
    }
}
