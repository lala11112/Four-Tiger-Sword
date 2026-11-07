using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace BoundaryZeroUI
{
    // Screen content stays native and individually interactive. The frame owns modal chrome.
    public static partial class CompleteViews
    {
        static readonly string[] Forms={"화","토","금","수","목"};
        static readonly string[] Names={"적층 인장","퇴적 코어","백철 잔편","환류 고리","결속 부적","경계 데이터편","주홍 기록지","압축 용기","백철 환","청류 관","생장 접점","삼각 기록체"};
        static object Read(object o,string key,object fallback=null)
        {
            if(o==null)return fallback;
            if(o is IDictionary dict)return dict.Contains(key)?dict[key]:fallback;
            var s=o as UiState;if(s==null)return fallback;
            switch(key){case "shopInspectOwned":return s.shopInspectOwned;case "shopInventoryOpen":return s.shopInventoryOpen;case "shopInventorySelected":return s.shopInventorySelected;case "shopInventoryCategory":return s.shopInventoryCategory;case "selected":return s.selected;case "category":return s.category;case "owned":return s.owned;case "materials":return s.materials;case "grades":return s.grades;case "elements":return s.elements;case "roll":return s.roll;case "heal":return s.heal;case "outElement":return s.outElement;case "tab":return s.tab;case "settings":return s.settings;case "mode":return s.mode;case "form":return s.form;case "rewardChoice":return s.rewardChoice;case "confirmMode":return s.confirmMode;case "notice":return s.notice;default:return fallback;}
        }
        static int Num(object s,string key,int def=0){try{return Convert.ToInt32(Read(s,key,def));}catch{return def;}}
        static bool Flag(object s,string key){try{return Convert.ToBoolean(Read(s,key,false));}catch{return false;}}
        static string Str(object s,string key,string def=""){return Convert.ToString(Read(s,key,def));}
        static List<int> List(object s,string key){var a=new List<int>();if(Read(s,key) is IEnumerable items)foreach(var v in items)try{a.Add(Convert.ToInt32(v));}catch{}return a;}
        static int Indexed(object s,string key,int i,int def){var v=Read(s,key);if(v is IDictionary d&&d.Contains(i))return Convert.ToInt32(d[i]);if(v is IList l&&i>=0&&i<l.Count)return Convert.ToInt32(l[i]);return def;}
        static int Grade(object s,int i){return Indexed(s,"grades",i,i==2?2:3);}
        static int El(object s,int i){return Indexed(s,"elements",i,Math.Abs(i)%5);}
        static string Name(int i){return Names[Math.Abs(i)%Names.Length];}
        static Color Shade(float a=.11f){return new Color(214/255f,220/255f,230/255f,a);}
        static void T(Transform p,string text,float x,float y,float w,float h,float size=24,Color? color=null,TextAlignmentOptions align=TextAlignmentOptions.TopLeft){UIFactory.Label(p,text,x,y,w,h,size,color,align);}
        static void B(Transform p,string text,float x,float y,float w,float h,Action<string> send,string action,bool primary=false,bool enabled=true){
 if(text.Length>0){if(action.StartsWith("category:")||action.StartsWith("tab:")||action.StartsWith("go:shop-")){
 var r=UIFactory.Node(p,"Tab_"+text,x,y,w,h);var hit=UIFactory.Box(r,"Hit",0,0,w,h,new Color(1,1,1,.001f),true);
 if(primary){DesignArt.Surface(r,0,0,w,h,new Color(UIFactory.Ivory.r,UIFactory.Ivory.g,UIFactory.Ivory.b,.10f),new Color(UIFactory.Ivory.r,UIFactory.Ivory.g,UIFactory.Ivory.b,.025f));UIFactory.Line(r,8,h-2,w-16,2,UIFactory.Red);}
 var tabLabel=UIFactory.Label(r,text,0,0,w,h,24,UIFactory.Ivory,TextAlignmentOptions.Center);if(primary)tabLabel.color=UIFactory.Ivory;
 var b=r.gameObject.AddComponent<UnityEngine.UI.Button>();b.targetGraphic=hit;b.interactable=enabled;b.onClick.AddListener(()=>send(action));
 }else UIFactory.Button(p,text,x,y,w,h,()=>send(action),primary,enabled);return;}
 var image=UIFactory.Box(p,"Action_"+action,x,y,w,h,new Color(1,1,1,.001f),true);var button=image.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;button.interactable=enabled;button.onClick.AddListener(()=>send(action));
 var light=DesignArt.Surface(p,x,y,w,h,new Color(214/255f,220/255f,230/255f,.24f),new Color(214/255f,220/255f,230/255f,0));var group=light.gameObject.AddComponent<CanvasGroup>();group.alpha=0;group.blocksRaycasts=false;
 var fx=image.gameObject.AddComponent<UIFocusLight>();fx.Light=group;fx.Control=button;
 }
        static void Sec(Transform p,string title,float x,float y,float w,string sub=""){
 UIFactory.Line(p,x,y+7,3,18,UIFactory.Red);T(p,title,x+15,y,w-15,35,24,UIFactory.Ivory);
 if(sub!="")T(p,sub,x+w*.45f,y+4,w*.55f,25,18,UIFactory.Muted,TextAlignmentOptions.TopRight);
 UIFactory.Line(p,x,y+45,w,1);UIFactory.Line(p,x,y+45,Mathf.Min(70,w*.2f),2,new Color(246/255f,244/255f,237/255f,.8f));
 }
        static void Notice(Transform p,object s,float x,float y,float w){T(p,Str(s,"notice"),x,y,w,70,20,UIFactory.Red);}
        static RectTransform Card(Transform p,string name,float x,float y,float w,float h,bool selected=false){
 var r=UIFactory.Node(p,name,x,y,w,h);DesignArt.Surface(r,0,0,w,h,selected?new Color32(49,25,30,255):new Color32(24,26,31,255),new Color32(12,14,18,255));
 UIFactory.Line(r,12,0,w-24,1,selected?UIFactory.Ivory:new Color(186/255f,201/255f,201/255f,.5f));
 UIFactory.Line(r,0,h-2,w-12,selected?2:1,selected?UIFactory.Ivory:new Color(186/255f,201/255f,201/255f,.3f));
 UIFactory.Line(r,w-2,10,1,Mathf.Min(32,h*.2f),new Color(246/255f,244/255f,237/255f,.7f));
 if(selected){UIFactory.Line(r,0,12,3,h-24,UIFactory.Red);UIFactory.Line(r,w-19,5,12,3,UIFactory.Red);}
 return r;}
        static Color ElementColor(int id){return new[]{new Color32(241,75,61,255),new Color32(247,196,76,255),new Color32(246,244,237,255),new Color32(75,207,255,255),new Color32(71,191,105,255)}[Mathf.Clamp(id,0,4)];}
        static void Category(Transform p,object s,float x,float y,float w,Action<string> send)
        {for(int j=0;j<6;j++){int i=j-1;B(p,j==0?"전체":Forms[i],x+j*w/6,y,w/6-7,44,send,"category:"+i,Num(s,"category",-1)==i);}}
        static void ShopNav(Transform p,object s,string id,Action<string> send)
        {var ids=new[]{"shop-buy","shop-sell","shop-synth"};var names=new[]{"구매","판매","합성"};for(int i=0;i<3;i++)B(p,names[i],i*160,0,135,57,send,"go:"+ids[i],id==ids[i]);if(id=="shop-buy"){var toggle=UIFactory.Node(p,"ShopInventoryToggle",1514,0,260,57);B(toggle,"소지품 · Tab",0,0,260,57,send,"shop-inventory:toggle",Flag(s,"shopInventoryOpen"));}UIFactory.Line(p,0,65,1774,1);}
        static void Tile(Transform p,object s,int i,float x,float y,float w,float h,Action<string> send,string mode="item",bool compact=false){
 bool chosen=Num(s,"selected")==i||List(s,"materials").Contains(i);Color tint=ElementColor(El(s,i));
 var r=Card(p,"Item"+i,x,y,w,h,chosen);B(r,"",0,0,w,h,send,mode+":"+i);
 if(compact){UIFactory.Item(r,i,3,3,64);T(r,Grade(s,i)+"등급",80,8,w-130,22,15,UIFactory.Muted);T(r,Name(i),80,31,w-130,32,20,UIFactory.Ivory);UIFactory.Glyph(r,El(s,i),w-42,20,26);}
 else{
  if(chosen){UIFactory.Box(r,"SelectedCaptionTint",1,h-54,w-2,53,new Color(UIFactory.Ivory.r,UIFactory.Ivory.g,UIFactory.Ivory.b,.065f));UIFactory.Line(r,1,h-2,Mathf.Min(52,w-2),2,UIFactory.Ivory);}
  T(r,Grade(s,i)+"등급",10,7,w-20,25,16,UIFactory.Muted);UIFactory.Line(r,w-27,12,12,3,tint);float art=Mathf.Min(112,h-68);UIFactory.Item(r,i,(w-art)/2,25,art);
  var itemLabel=UIFactory.Label(r,Name(i),4,h-50,w-8,27,18,UIFactory.Ivory,TextAlignmentOptions.Top);
  var elementLabel=UIFactory.Label(r,Forms[El(s,i)],4,h-24,w-8,22,16,tint,TextAlignmentOptions.Top);
  if(chosen)itemLabel.color=UIFactory.Ivory;
 }
 }
        static void Grid(Transform p,object s,List<int> ids,float x,float y,float w,float h,int columns,float tileH,Action<string> send,string mode="item",bool compact=false)
        {float gap=8;float tw=(w-gap*(columns-1))/columns;float total=Mathf.Ceil(ids.Count/(float)columns)*(tileH+gap);var scroll=UIFactory.Scroll(p,"ItemScroll",x,y,w,h,Mathf.Max(h,total));for(int j=0;j<ids.Count;j++)Tile(scroll,s,ids[j],j%columns*(tw+gap),j/columns*(tileH+gap),tw,tileH,send,mode,compact);}
        static void Preview(Transform p,object s,float x,float y,float w,float h,float size=410){
 int selected=Num(s,"selected",-1);if(selected<0)return;Color tint=ElementColor(El(s,selected));float halo=Mathf.Min(w*.89f,h*.79f);
 DesignArt.Halo(p,x+(w-halo)/2,y+(h-halo)/2-20,halo,new Color(tint.r,tint.g,tint.b,.24f));
 UIFactory.Item(p,selected,x+(w-size)/2,y+(h-size)/2-24,size);
 }
        static void Detail(Transform p,object s,string mode,float x,float y,float w,Action<string> send)
        {
            int i=Num(s,"selected",-1);
            if(i<0 || ((mode=="inventory"||mode=="shop-sell")&&!List(s,"owned").Contains(i))){T(p,"보유한 아이템이 없습니다.",x+30,y+250,w-30,100,24,UIFactory.Muted,TextAlignmentOptions.Top);if(mode=="shop-sell")B(p,"판매하기",x+30,y+650,240,62,send,"sell",true,false);return;}
            DesignArt.Surface(p,x+12,y,w-12,720,new Color32(24,26,31,255),new Color32(12,14,18,255));UIFactory.Line(p,x,y,1,720);x+=30;w-=30;
            UIFactory.Glyph(p,El(s,i),x,y+20,34);T(p,Forms[El(s,i)]+" · "+Grade(s,i)+"등급",x+47,y+25,w-47,32,19,UIFactory.Muted);T(p,Name(i),x,y+82,w,70,40);
            UIFactory.Row(p,"효과",x,y+205,w);UIFactory.Row(p,"시너지",x,y+275,w,"오행 공명");
            if(mode=="inventory"){}
            else{UIFactory.Row(p,mode=="shop-buy"?"구매 가격":"판매 가격",x,y+395,w);B(p,mode=="shop-buy"?"구매하기":"판매하기",x,y+650,240,62,send,mode=="shop-buy"?"buy":"sell",true);}
            Notice(p,s,x,y+735,w);
        }
        public static void Build(string id,RectTransform parent,UiState s,Action<string> dispatch,int design)
        {
            switch(id){case "main":Main(parent,dispatch,design);break;case "hud":break;case "pause":Pause(parent,dispatch,design);break;case "confirm":Confirm(parent,s,dispatch);break;case "settings":Settings(parent,s,dispatch,design);break;case "shop-buy":case "shop-sell":Shop(parent,s,id,dispatch,design);break;case "shop-synth":Synth(parent,s,dispatch);break;case "inventory":Inventory(parent,s,dispatch,design);break;case "upgrade":Upgrade(parent,s,dispatch);break;case "wheel":Wheel(parent,s,dispatch);break;case "reward-upgrade":case "reward-item":case "reward-boss":Rewards(parent,s,id,dispatch,design);break;case "event":Event(parent,dispatch,design);break;case "clear":case "death":Result(parent,id,dispatch);break;case "loading":LegacyFullScreens.BuildLoading(parent,s);break;}
        }

        static void Wheel(RectTransform p,object s,Action<string> send)
        {
            UIFactory.Box(p,"WheelDim",0,0,2560,1440,new Color(.015f,.03f,.055f,.16f),true);
            var surface=UIFactory.Node(p,"WheelSurface",880,320,800,800);
            DesignArt.Halo(surface,0,0,800,new Color(.84f,.88f,.94f,.55f));
            Circle(surface,400,400,330,UIFactory.Ivory);Circle(surface,400,400,156,UIFactory.Ivory);
            for(int i=0;i<5;i++){
                float a=(i*72-126)*Mathf.Deg2Rad;Segment(surface,400+156*Mathf.Cos(a),400+156*Mathf.Sin(a),400+330*Mathf.Cos(a),400+330*Mathf.Sin(a));
                float angle=i*72*Mathf.Deg2Rad,x=400+Mathf.Sin(angle)*245,y=400-Mathf.Cos(angle)*245;
                var sector=UIFactory.Box(surface,"WheelSector"+i,x-85,y-85,170,170,Color.clear);
                var tint=ElementColor(i);DesignArt.Halo(sector.transform,30,4,110,new Color(tint.r,tint.g,tint.b,.30f));
                UIFactory.Glyph(sector.transform,i,40,12,90);T(sector.transform,Forms[i],0,115,170,40,25,null,TextAlignmentOptions.Top);
            }
            UIFactory.Glyph(surface,Num(s,"form"),367,300,65);
            T(surface,"오행 전환",275,397,250,45,24,null,TextAlignmentOptions.Top);
            UIFactory.Label(surface,"",250,449,300,40,19,UIFactory.Muted,TextAlignmentOptions.Center).gameObject.name="WheelStatus";
        }
        static void Segment(Transform p,float x1,float y1,float x2,float y2)
        {float dx=x2-x1,dy=y2-y1;var r=UIFactory.Box(p,"WheelLine",x1,y1,Mathf.Sqrt(dx*dx+dy*dy),1,UIFactory.IsBright(p)?new Color(.82f,.86f,.92f,.72f):new Color(246/255f,244/255f,237/255f,.6f)).rectTransform;r.localRotation=Quaternion.Euler(0,0,-Mathf.Atan2(dy,dx)*Mathf.Rad2Deg);}
        static void Circle(Transform p,float x,float y,float radius,Color color){const int steps=120;for(int i=0;i<steps;i++){float a=i*Mathf.PI*2/steps,b=(i+1)*Mathf.PI*2/steps;Segment(p,x+Mathf.Cos(a)*radius,y+Mathf.Sin(a)*radius,x+Mathf.Cos(b)*radius,y+Mathf.Sin(b)*radius);}}

    }
}





