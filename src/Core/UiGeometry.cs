using System;

namespace RimTube.Core
{
    public struct UiBox
    {
        public float X, Y, Width, Height;
        public UiBox(float x,float y,float width,float height) { X=x;Y=y;Width=width;Height=height; }
    }
    public struct UiRowRange { public int First,End; public UiRowRange(int first,int end){First=first;End=end;} }
    public static class UiGeometry
    {
        private static float Finite(float value,float fallback) => float.IsNaN(value)||float.IsInfinity(value)?fallback:value;
        public static UiBox Clamp(UiBox box,float screenWidth,float screenHeight,float minWidth=120,float minHeight=40)
        {
            float sw=Math.Max(1,Finite(screenWidth,1)),sh=Math.Max(1,Finite(screenHeight,1));
            float w=Math.Min(sw,Math.Max(minWidth,Finite(box.Width,minWidth))),h=Math.Min(sh,Math.Max(minHeight,Finite(box.Height,minHeight)));
            return new UiBox(Math.Max(0,Math.Min(sw-w,Finite(box.X,0))),Math.Max(0,Math.Min(sh-h,Finite(box.Y,0))),w,h);
        }
        public static UiBox Snap(UiBox box,float width,float height,float threshold=16)
        {
            box=Clamp(box,width,height);
            if(box.X<=threshold) box.X=0; else if(width-box.X-box.Width<=threshold) box.X=width-box.Width;
            if(box.Y<=threshold) box.Y=0; else if(height-box.Y-box.Height<=threshold) box.Y=height-box.Height;
            return box;
        }
        public static UiRowRange VisibleRows(int count,float offset,float height,float rowHeight)
        {
            if(count<=0||rowHeight<=0||height<=0) return new UiRowRange(0,0);
            double start=Math.Floor(Math.Max(0,Finite(offset,0))/rowHeight);
            int first=(int)Math.Min(count,start);
            int end=(int)Math.Min(count,Math.Ceiling((Math.Max(0,Finite(offset,0))+Finite(height,0))/rowHeight));
            return new UiRowRange(first,Math.Max(first,end));
        }
    }
}
