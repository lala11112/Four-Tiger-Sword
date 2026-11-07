using System;
using TMPro;
using UnityEngine;
namespace BoundaryZeroUI { public static partial class CompleteViews {
        static Color StoryTint(Color c,float alpha){return new Color(c.r,c.g,c.b,alpha);}
        static void StoryStamp(Transform p,string text,float x,float y,Color c)
        {UIFactory.Border(p,x,y,54,54,StoryTint(c,.8f),2);T(p,text,x+5,y+8,44,40,25,c,TextAlignmentOptions.Top);UIFactory.Line(p,x+7,y+48,40,2,c);}
        static void StoryIndex(Transform p,string a,string b,float x,float y,float w)
        {T(p,a,x,y,w,26,16,UIFactory.Red);T(p,b,x,y+32,w,40,22,UIFactory.Muted);UIFactory.Line(p,x,y+90,w,1);}
        static void StoryMenu(Transform p,string title,string sub,string index,float x,float y,float w,Action<string> send,string action,bool primary=false,bool enabled=true)
        {var r=UIFactory.Node(p,"Menu_"+title,x,y,w,100);if(primary){UIFactory.Box(r,"ActiveWash",0,0,w,100,new Color(.62f,.08f,.12f,.12f));UIFactory.Line(r,0,0,4,100,UIFactory.Red);}T(r,index,20,15,50,26,16,enabled?UIFactory.Red:StoryTint(UIFactory.Muted,.4f));T(r,title,83,8,w-170,49,primary?34:29,enabled?UIFactory.Ivory:StoryTint(UIFactory.Muted,.35f));T(r,sub,85,58,w-150,26,15,enabled?UIFactory.Muted:StoryTint(UIFactory.Muted,.3f));T(r,"↗",w-45,26,35,40,26,enabled?UIFactory.Red:StoryTint(UIFactory.Muted,.3f));UIFactory.Line(r,20,99,w-20,1,StoryTint(UIFactory.Ivory,.2f));B(r,"",0,0,w,100,send,action,primary,enabled);}
        static TMPro.TextMeshProUGUI TitleType(Transform p,string text,float x,float y,float w,float h,float size,bool korean=false,Color? color=null){
            var t=UIFactory.Label(p,text,x,y,w,h,size,color??UIFactory.Ivory);var font=Resources.Load<TMP_FontAsset>("BoundaryUI/Fonts/"+(korean?"TitleKorean":"TitleLatin"));if(font)t.font=font;t.fontStyle=FontStyles.Normal;t.characterSpacing=korean?3:4;return t;
        }
        static void Main(RectTransform p,Action<string> send,int d)
        {
            LegacyMainView.Build(p,send,d);
        }
        static void Event(RectTransform p,Action<string> send,int d)
        {
            var relic=DesignArt.Surface(p,0,62,505,650,new Color(.095f,.10f,.12f,1),new Color(.035f,.04f,.055f,1));DesignArt.Traces(relic,20,20,465,610,StoryTint(UIFactory.Red,.22f));DesignArt.Halo(relic,37,105,430,UIFactory.Red);UIFactory.Item(relic,11,63,145,380);
            float sx=575,sw=1199;
            for(int i=0;i<2;i++){float w=(sw-30)/2;var r=DesignArt.Surface(p,sx+i*(w+30),190,w,400,new Color(.095f,.10f,.12f,1),new Color(.035f,.04f,.055f,1));T(r,i==0?"기록에 응답한다":"잔향을 남겨 둔다",28,55,w-56,50,29);UIFactory.Row(r,"이익",28,155,w-56);UIFactory.Row(r,"대가",28,225,w-56);T(r,"↗",w-60,330,35,38,28,UIFactory.Red);B(r,"",0,0,w,400,send,"event:"+i);}
        }
        static void Result(RectTransform p,string id,Action<string> send){LegacyFullScreens.BuildResult(p,id,send);}
}}
