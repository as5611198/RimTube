using RimTube.Core;
using UnityEngine;
using Verse;

namespace RimTube
{
    internal sealed class UiTheme
    {
        private static readonly UiTextFitter TextFits=new UiTextFitter();
        private static readonly System.Func<string,float> MeasureText=s=>Verse.Text.CalcSize(s).x;
        private static object textLanguage;
        private static float textScale;
        public readonly string Id;
        private readonly string nameKey,flavorKey;
        public string Name=>L10n.T(nameKey);
        public string Flavor=>L10n.T(flavorKey);
        public readonly Color Background,Panel,Accent,Text,Muted;
        private UiTheme(string id,string name,string flavor,Color bg,Color panel,Color accent,Color text)
        {Id=id;nameKey=name;flavorKey=flavor;Background=bg;Panel=panel;Accent=accent;Text=text;Muted=new Color(text.r*.72f,text.g*.72f,text.b*.72f,1);}
        public static readonly UiTheme[] All={
            new UiTheme("rust","RimTube.Rust","RimTube.RustFlavor",new Color(.105f,.12f,.11f),new Color(.15f,.17f,.15f),new Color(.78f,.49f,.25f),new Color(.9f,.91f,.85f)),
            new UiTheme("glitter","RimTube.Glitter","RimTube.GlitterFlavor",new Color(.075f,.11f,.16f),new Color(.11f,.17f,.23f),new Color(.3f,.78f,.88f),new Color(.86f,.94f,.98f)),
            new UiTheme("medieval","RimTube.Medieval","RimTube.MedievalFlavor",new Color(.17f,.13f,.09f),new Color(.23f,.18f,.12f),new Color(.78f,.65f,.37f),new Color(.95f,.88f,.72f)),
            new UiTheme("tribal","RimTube.Tribal","RimTube.TribalFlavor",new Color(.14f,.13f,.12f),new Color(.21f,.19f,.16f),new Color(.75f,.34f,.25f),new Color(.92f,.87f,.78f))};
        public static UiTheme Current
        {
            get {string id=RimTubeMod.App?.Interface?.Prefs.Theme;for(int i=0;i<All.Length;i++) if(All[i].Id==id) return All[i];return All[0];}
        }
        // Solid primitives reuse the game's white texture. No per-theme textures or materials.
        public static void Surface(Rect rect,bool inset=false,float alpha=1)
        {
            var theme=Current;var color=inset?theme.Panel:theme.Background;color.a=alpha;Widgets.DrawBoxSolid(rect,color);
            Widgets.DrawBoxSolid(new Rect(rect.x,rect.y,rect.width,2),theme.Accent);
        }
        public static bool Button(Rect rect,string label,bool active=false,bool showTooltip=true)
        {
            var old=GUI.color;var anchor=Verse.Text.Anchor;bool wrap=Verse.Text.WordWrap;
            try
            {
                var theme=Current;
                bool enabled=GUI.enabled,hover=enabled&&Mouse.IsOver(rect),pressed=hover&&GUIUtility.hotControl!=0;
                if(Event.current.type==EventType.Repaint)
                {
                    GUI.color=Color.white;
                    var fill=Color.Lerp(theme.Panel,theme.Accent,pressed?.42f:active?.32f:hover?.18f:0);
                    var edge=Color.Lerp(theme.Panel,theme.Accent,active||hover?.95f:.55f);
                    PaintButton(rect,theme.Id,fill,edge,theme.Background,enabled?1:.45f);
                }
                GUI.color=enabled?theme.Text:theme.Muted;
                Verse.Text.Anchor=TextAnchor.MiddleCenter;Verse.Text.WordWrap=false;
                float padding=rect.width<=72?4:9;
                var textRect=new Rect(rect.x+padding,rect.y+(pressed?1:0),Mathf.Max(0,rect.width-2*padding),rect.height);
                string fitted=FitText(label,textRect.width);
                Widgets.Label(textRect,fitted);
                if(showTooltip&&fitted!=label) TooltipHandler.TipRegion(rect,label);
                // Native input handling only; the original button texture is never drawn.
                return Widgets.ButtonInvisible(rect)&&enabled;
            }
            finally{GUI.color=old;Verse.Text.Anchor=anchor;Verse.Text.WordWrap=wrap;}
        }
        private static void Block(float x,float y,float w,float h,Color color,float opacity)
        {if(w<=0||h<=0)return;color.a*=opacity;Widgets.DrawBoxSolid(new Rect(x,y,w,h),color);}
        private static void PaintButton(Rect r,string skin,Color fill,Color edge,Color shadow,float opacity)
        {
            float x=r.x,y=r.y,w=r.width,h=r.height;
            if(skin=="glitter")
            {
                // Chamfered holographic plate and illuminated rails.
                Block(x+5,y,w-10,h,shadow,opacity);Block(x,y+5,w,h-10,shadow,opacity);
                Block(x+5,y+2,w-10,h-4,fill,opacity);Block(x+2,y+5,w-4,h-10,fill,opacity);
                Block(x+7,y,w-14,1,edge,opacity);Block(x+7,y+h-1,w-14,1,edge,opacity);
                Block(x,y+7,2,h-14,edge,opacity);Block(x+w-2,y+7,2,h-14,edge,opacity);
                Block(x+6,y+4,10,2,edge,opacity);Block(x+w-16,y+h-6,10,2,edge,opacity);
            }
            else if(skin=="medieval")
            {
                // Brass frame, recessed parchment and four corner studs.
                Block(x,y,w,h,edge,opacity);Block(x+1,y+1,w-2,h-2,shadow,opacity);
                Block(x+4,y+3,w-8,h-6,fill,opacity);Block(x+5,y+3,w-10,1,edge,opacity*.65f);
                Block(x+5,y+h-4,w-10,1,shadow,opacity);
                Block(x+2,y+2,3,3,edge,opacity);Block(x+w-5,y+2,3,3,edge,opacity);
                Block(x+2,y+h-5,3,3,edge,opacity);Block(x+w-5,y+h-5,3,3,edge,opacity);
            }
            else if(skin=="tribal")
            {
                // Notched stone plaque, bone pins and a red ochre side mark.
                Block(x+3,y,w-6,h,shadow,opacity);Block(x,y+4,w,h-8,shadow,opacity);
                Block(x+4,y+2,w-8,h-4,fill,opacity);Block(x+2,y+5,w-4,h-10,fill,opacity);
                Block(x+3,y+2,3,h-4,edge,opacity);Block(x+8,y+h-3,w-16,1,edge,opacity*.55f);
                Block(x+w-7,y+5,2,3,Current.Muted,opacity);Block(x+w-7,y+h-8,2,3,Current.Muted,opacity);
                Block(x+3,y+5,6,2,edge,opacity);Block(x+3,y+h-7,6,2,edge,opacity);
            }
            else
            {
                // Riveted industrial plate with an inset face and copper stripe.
                Block(x,y,w,h,shadow,opacity);Block(x,y,w,1,edge,opacity);
                Block(x+1,y+2,w-2,h-4,fill,opacity);Block(x+1,y+2,3,h-4,edge,opacity);
                Block(x+5,y+h-2,w-6,1,edge,opacity*.5f);
                Block(x+7,y+4,2,2,edge,opacity);Block(x+w-6,y+4,2,2,edge,opacity);
                Block(x+7,y+h-6,2,2,edge,opacity);Block(x+w-6,y+h-6,2,2,edge,opacity);
            }
        }
        public static void Label(Rect rect,string text,bool muted=false)
        {
            var old=GUI.color;try{GUI.color=muted?Current.Muted:Current.Text;Widgets.Label(rect,text);}finally{GUI.color=old;}
        }
        public static void OneLine(Rect rect,string text,bool muted=false)
        {
            bool wrap=Verse.Text.WordWrap;try{Verse.Text.WordWrap=false;Label(rect,FitText(text,rect.width),muted);}finally{Verse.Text.WordWrap=wrap;}
        }
        private static string FitText(string text,float width)
        {
            var language=LanguageDatabase.activeLanguage;float scale=Prefs.UIScale;
            if(!ReferenceEquals(language,textLanguage)||scale!=textScale){TextFits.Clear();textLanguage=language;textScale=scale;}
            return TextFits.Fit(text,width,(int)Verse.Text.Font,MeasureText);
        }
        public static Rect Rect(UiBox b)=>new Rect(b.X,b.Y,b.Width,b.Height);
        public static Rect Fit(UiBox b,float minWidth,float minHeight)=>Rect(UiGeometry.Clamp(b,UI.screenWidth,UI.screenHeight,minWidth,minHeight));
    }
}
