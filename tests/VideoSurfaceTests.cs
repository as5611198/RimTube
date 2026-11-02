using System.Runtime.InteropServices;
using RimTube.Core;

// Hidden Win32 test host: does not load RimWorld or create a visible video window.
internal static class VideoSurfaceTests
{
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr CreateWindowEx(int ex, string cls, string title, uint style, int x,int y,int w,int h,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd,int index);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetFocus();
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hwnd);

    public static void Run(Action<bool,string> check)
    {
        IntPtr host=CreateWindowEx(0,"STATIC","RimTube hidden test host",0x00CF0000,0,0,320,240,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        if(host==IntPtr.Zero) throw new Exception("Cannot create hidden Win32 test host");
        try
        {
            using var surface=new VideoSurface(host);
            check(surface.Handle!=IntPtr.Zero && !IsWindowEnabled(surface.Handle),"native video surface cannot receive keyboard or mouse input");
            check((GetWindowLong(surface.Handle,-20)&0x08000000)!=0,"native video surface has no-activation style");
            IntPtr foreground=GetForegroundWindow(),focus=GetFocus();
            SetFocus(surface.Handle);
            check(GetFocus()==focus,"disabled native surface refuses keyboard focus");
            check(surface.Place(10,10,160,90,320,240),"native surface can be positioned in hidden test host");
            IntPtr handle=surface.Handle;
            for(int i=0;i<5;i++) { surface.Hide(); surface.Place(10,10,160,90,320,240); }
            check(surface.Handle==handle && GetFocus()==focus && GetForegroundWindow()==foreground,"five hide/show cycles reuse native surface without activating it");
        }
        finally { DestroyWindow(host); }
    }
}
