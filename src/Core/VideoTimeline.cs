using System;

namespace RimTube.Core
{
    // A drag changes only its preview. The caller sends one seek after Commit.
    public sealed class VideoTimeline
    {
        private string track,session;
        private double position,duration,fraction;
        public bool Enabled {get;private set;}
        public bool IsDragging {get;private set;}
        public double DisplayPosition=>IsDragging?fraction*duration:position;
        public double Fraction=>duration>0?DisplayPosition/duration:0;
        private static bool Finite(double value)=>!Double.IsNaN(value)&&!Double.IsInfinity(value);
        public void Update(string trackId,string sessionId,bool canSeek,bool live,double current,double length)
        {
            bool available=canSeek&&!live&&Finite(length)&&length>0&&!String.IsNullOrEmpty(trackId)&&!String.IsNullOrEmpty(sessionId);
            if(!available||track!=trackId||session!=sessionId)Cancel();
            track=trackId;session=sessionId;Enabled=available;
            duration=Finite(length)&&length>0?length:0;
            position=Finite(current)?Math.Max(0,Math.Min(duration,current)):0;
        }
        public bool Begin(double value)
        {if(!Enabled||!Finite(value))return false;IsDragging=true;Move(value);return true;}
        public void Move(double value)
        {if(IsDragging&&Finite(value))fraction=Math.Max(0,Math.Min(1,value));}
        public bool Commit(out double target)
        {
            target=DisplayPosition;
            if(!Enabled||!IsDragging)return false;
            IsDragging=false;return true;
        }
        public void Cancel(){IsDragging=false;}
    }
}
