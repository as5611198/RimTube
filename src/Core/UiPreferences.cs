using System;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace RimTube.Core
{
    public sealed class UiShortcut
    {
        public string Action;
        public int Key;
        public bool Control,Alt,Shift;
        public UiShortcut(string action,int key,bool control=true,bool alt=false,bool shift=false){Action=action;Key=key;Control=control;Alt=alt;Shift=shift;}
        public bool Matches(int key,bool control,bool alt,bool shift)=>Key!=0&&Key==key&&Control==control&&Alt==alt&&Shift==shift;
    }
    // Global UI preferences are independent of the selected library and saved colony.
    public sealed class UiPreferences
    {
        public string Theme="rust",LastSection="library",LastList="general";
        public string DefaultSubtitle="off";
        public bool DefaultAutoSubtitles;
        public MediaChoice NewMediaChoice()=>new MediaChoice {Subtitle=DefaultSubtitle,AutoSubtitles=DefaultAutoSubtitles};
        public bool ShowMini=true,MiniLocked,MiniSnap=true,MiniFade,Muted,LockVideoAspect=true;
        public float Volume=.7f,Opacity=.85f,SubtitleScale=1.5f;
        public UiBox Mini=new UiBox(16,80,370,48),Video=new UiBox(80,140,720,480),Panel=new UiBox(100,80,1080,720);
        public readonly UiShortcut[] Shortcuts={new UiShortcut("panel",109),new UiShortcut("play",32,true,true),new UiShortcut("next",275,true,true),new UiShortcut("mute",109,true,true),new UiShortcut("restore",114,true,true),new UiShortcut("lock",108,true,true)};
        public bool ReadOnly {get;private set;}
        public string Warning {get;private set;}="";
        public static UiPreferences Load(string path,Settings legacy)
        {
            var p=new UiPreferences();
            if(legacy!=null) {p.Volume=legacy.Volume;p.ShowMini=legacy.ShowMini;p.Mini.X=legacy.MiniX;p.Mini.Y=legacy.MiniY;}
            if(!File.Exists(path)) return p;
            try
            {
                var settings=new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null};XDocument doc;
                using(var reader=XmlReader.Create(path,settings)) doc=XDocument.Load(reader);
                var e=doc.Root;
                if(e==null||e.Name!="RimTubeUI"||(int?)e.Attribute("schema")!=1) throw new InvalidDataException(LocalizedText.T("RimTube.UnsupportedInterfaceSettingsFormat"));
                p.Theme=S(e,"theme","rust");if(p.Theme!="rust"&&p.Theme!="glitter"&&p.Theme!="medieval"&&p.Theme!="tribal") p.Theme="rust";
                p.LastSection=S(e,"section","library");p.LastList=S(e,"list","general");
                p.ShowMini=B(e,"mini",true);p.MiniLocked=B(e,"locked",false);p.MiniSnap=B(e,"snap",true);p.MiniFade=B(e,"fade",false);
                p.Muted=B(e,"muted",false);p.LockVideoAspect=B(e,"aspect",true);
                string subtitle=S(e,"defaultSubtitle","off");p.DefaultSubtitle=subtitle=="off"?"off":SubtitleLanguage.Normalize(subtitle);if(p.DefaultSubtitle=="")p.DefaultSubtitle="off";
                p.DefaultAutoSubtitles=B(e,"defaultAutoSubtitles",false);
                p.SubtitleScale=Math.Max(1,Math.Min(3,F(e,"subtitleScale",1.5f)));
                p.Volume=Math.Max(0,Math.Min(1,F(e,"volume",.7f)));p.Opacity=Math.Max(.4f,Math.Min(1,F(e,"opacity",.85f)));
                p.Mini=Box(e.Element("Mini"),p.Mini);p.Video=Box(e.Element("Video"),p.Video);p.Panel=Box(e.Element("Panel"),p.Panel);
                foreach(var key in e.Elements("Key")) foreach(var binding in p.Shortcuts) if(binding.Action==S(key,"action",""))
                {int code;if(int.TryParse(S(key,"code",""),out code)&&code>=0&&code<1000) binding.Key=code;binding.Control=B(key,"control",false);binding.Alt=B(key,"alt",false);binding.Shift=B(key,"shift",false);}
            }
            catch {p.ReadOnly=true;p.Warning=LocalizedText.T("RimTube.CouldNotReadInterfaceSettingsTheOriginalFile");}
            return p;
        }
        public void Save(string path)
        {
            if(ReadOnly) return;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var root=new XElement("RimTubeUI",A("schema",1),A("theme",Theme),A("section",LastSection),A("list",LastList),
                A("mini",ShowMini),A("locked",MiniLocked),A("snap",MiniSnap),A("fade",MiniFade),A("muted",Muted),A("aspect",LockVideoAspect),
                A("defaultSubtitle",DefaultSubtitle),A("defaultAutoSubtitles",DefaultAutoSubtitles),A("subtitleScale",SubtitleScale),
                A("volume",Volume),A("opacity",Opacity),Element("Mini",Mini),Element("Video",Video),Element("Panel",Panel));
            foreach(var key in Shortcuts) root.Add(new XElement("Key",A("action",key.Action),A("code",key.Key),A("control",key.Control),A("alt",key.Alt),A("shift",key.Shift)));
            Library.AtomicXml(path,new XDocument(root));
        }
        private static XElement Element(string name,UiBox b)=>new XElement(name,A("x",b.X),A("y",b.Y),A("w",b.Width),A("h",b.Height));
        private static UiBox Box(XElement e,UiBox fallback)=>e==null?fallback:new UiBox(F(e,"x",fallback.X),F(e,"y",fallback.Y),F(e,"w",fallback.Width),F(e,"h",fallback.Height));
        private static XAttribute A(string n,object v)=>new XAttribute(n,Convert.ToString(v,CultureInfo.InvariantCulture)??"");
        private static string S(XElement e,string n,string f)=>(string)e.Attribute(n)??f;
        private static bool B(XElement e,string n,bool f){bool v;return bool.TryParse(S(e,n,""),out v)?v:f;}
        private static float F(XElement e,string n,float f){float v;return float.TryParse(S(e,n,""),NumberStyles.Float,CultureInfo.InvariantCulture,out v)&&!float.IsNaN(v)&&!float.IsInfinity(v)?v:f;}
    }
}
