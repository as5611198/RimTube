using System;
using System.Collections.Generic;
using System.Globalization;

namespace RimTube.Core
{
    public sealed class UiTextFitter
    {
        private struct Key : IEquatable<Key>
        {
            public string Text;public int Width,Font;
            public bool Equals(Key other)=>Width==other.Width&&Font==other.Font&&String.Equals(Text,other.Text,StringComparison.Ordinal);
            public override bool Equals(object other)=>other is Key&&Equals((Key)other);
            public override int GetHashCode(){unchecked{return (Text.GetHashCode()*397^Width)*397^Font;}}
        }
        private readonly Dictionary<Key,string> cache=new Dictionary<Key,string>();
        private readonly int capacity;
        public int Count=>cache.Count;
        public UiTextFitter(int capacity=1024){this.capacity=Math.Max(16,capacity);}
        public void Clear()=>cache.Clear();
        public string Fit(string text,float width,int font,Func<string,float> measure)
        {
            if(String.IsNullOrEmpty(text)||float.IsNaN(width)||width<=0) return "";
            int quantized=(int)Math.Min(1000000,Math.Floor(width*2));
            var key=new Key {Text=text,Width=quantized,Font=font};string result;
            if(cache.TryGetValue(key,out result))return result;
            string line=text.IndexOfAny(new[]{'\r','\n','\t'})<0?text:text.Replace('\r',' ').Replace('\n',' ').Replace('\t',' ');
            float available=quantized/2f;
            result=line;
            if(measure(line)>available)
            {
                result="";
                if(measure("…")<=available)
                {
                    int[] elements=StringInfo.ParseCombiningCharacters(line);
                    int low=0,high=elements.Length-1;result="…";
                    while(low<=high)
                    {
                        int count=low+(high-low)/2;
                        string candidate=line.Substring(0,elements[count])+"…";
                        if(measure(candidate)<=available){result=candidate;low=count+1;}else high=count-1;
                    }
                }
            }
            if(cache.Count>=capacity)cache.Clear();cache.Add(key,result);return result;
        }
    }
}
