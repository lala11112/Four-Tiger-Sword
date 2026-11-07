using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
namespace BoundaryZeroUI { public static partial class CompleteViews {
        static void Pause(RectTransform p,Action<string> send,int d)
        {
            string[] labels={"계속","설정","런 포기","저장 후 종료"},actions={"resume","go:settings","confirm:abandon","confirm:savequit"};for(int i=0;i<4;i++)B(p,labels[i],123+i%2*360,110+i/2*110,330,78,send,actions[i],i==0);
        }
        static void Confirm(RectTransform p,object s,Action<string> send)
        {string mode=Str(s,"confirmMode");bool sy=mode=="synth",up=mode=="upgrade",quit=mode=="quit",save=mode=="savequit";T(p,sy?"선택한 유물을 합성할까요?":up?"영구 강화를 진행할까요?":quit?"게임을 종료할까요?":save?"저장 후 종료할까요?":"이번 탐사를 포기할까요?",0,70,936,60,34,null,TextAlignmentOptions.Top);T(p,sy?"재료 3개를 소모해 선택 속성의 4등급 유물을 얻습니다.":up?"영구 재화를 소모하며, 다음 탐사에도 적용됩니다.":quit?"":save?"현재 탐사 상태가 저장됩니다.":"런 아이템 · 강화 · 골드는 초기화됩니다.\n획득 영구 재화는 유지되며 실적은 정산하지 않습니다.",0,150,936,110,21,UIFactory.Muted,TextAlignmentOptions.Top);B(p,"취소",180,300,255,65,send,"cancel");B(p,sy?"합성 확정":up?"강화 확정":quit?"종료":save?"저장 후 종료":"포기하고 귀환",465,300,290,65,send,"confirm",true);}
        static void Shop(RectTransform p,object s,string id,Action<string> send,int d)
        {ShopNav(p,s,id,send);ShopSplit(p,s,id,send);}
        static void Inventory(RectTransform p,object s,Action<string> send,int d)
        {InventoryLarge(p,s,send);}
        static void Synth(RectTransform p,object s,Action<string> send)
        {ShopNav(p,s,"shop-synth",send);SynthSplit(p,s,send);}        static void Settings(RectTransform p,object s,Action<string> send,int d)
        {
            int tab=Num(s,"tab");float nx=d==1?1410:d==2?70:32,ny=d==2?0:30,sx=d==1?32:d==2?280:442,sy=d==2?125:40,sw=d==2?1200:d==1?1260:1260;
            DesignArt.Traces(p,sx,sy+15,sw,650,new Color(214/255f,220/255f,230/255f,.32f));
            for(int i=0;i<3;i++)B(p,new[]{"화면","사운드","조작"}[i],nx+(d==2?i*245:0),ny+(d==2?0:i*115),d==2?190:300,80,send,"tab:"+i,tab==i);if(d!=2)UIFactory.Line(p,d==1?1370:372,25,1,740);Sec(p,new[]{"화면 설정","사운드 설정","조작 설정"}[tab],sx,sy,sw);
            var settings=Read(s,"settings");string[] labels=tab==0?new[]{"화면 모드","밝기"}:tab==1?new[]{"전체 음량","음악 음량"}:new[]{"마우스 감도"};string[] keys=tab==0?new[]{"mode","brightness"}:tab==1?new[]{"sound","music"}:new[]{"sensitivity"};
            for(int i=0;i<keys.Length;i++){float y=sy+85+i*115;T(p,labels[i],sx,y+20,250,45,24);if(keys[i]=="mode"){string mode=Str(s,"mode","전체 화면");string[] modes={"전체 화면","창 모드","테두리 없는 창"};for(int j=0;j<3;j++)B(p,modes[j],sx+300+j*285,y+5,265,64,send,"setting:mode:"+j,mode==modes[j]||Num(settings,"mode",-1)==j);}else Slider(p,sx+300,y+25,670,Num(settings,keys[i],70),keys[i],send);UIFactory.Line(p,sx,y+95,sw,1);}
            if(tab==2){string[] n={"이동","오행 전환","상호작용","소지품","일시 정지"},v={"W / A / S / D","우클릭 유지 · 드래그 · 놓기","E","Tab","Esc"};for(int i=0;i<5;i++){float ky=sy+210+i*65;T(p,n[i],sx,ky+5,280,38,22,UIFactory.Muted);float kw=i==0?285:i==1?430:110;DesignArt.Surface(p,sx+sw-kw,ky-2,kw,44,new Color32(24,26,31,255),new Color32(12,14,18,255));T(p,v[i],sx+sw-kw+10,ky+5,kw-20,32,20,UIFactory.Ivory,TextAlignmentOptions.Top);UIFactory.Line(p,sx,ky+50,sw,1,new Color(186/255f,201/255f,201/255f,.3f));}}
            float ay=tab==2?sy+610:sy+345;B(p,"적용",sx,ay,235,65,send,"apply",true);B(p,"초기화",sx+265,ay,235,65,send,"reset");B(p,"돌아가기",sx+530,ay,235,65,send,"back");Notice(p,s,sx,ay+95,sw);
        }
        static void Slider(Transform p,float x,float y,float w,float value,string key,Action<string> send)
        {
            var r=UIFactory.Node(p,"SettingSlider_"+key,x,y,w,36);for(int tick=0;tick<=10;tick++)UIFactory.Line(r,tick*w/10,29,1,tick%5==0?7:4,new Color(186/255f,201/255f,201/255f,.55f));var slider=r.gameObject.AddComponent<UnityEngine.UI.Slider>();slider.minValue=0;slider.maxValue=100;slider.wholeNumbers=true;
            UIFactory.Box(r,"HitArea",0,0,w,36,new Color(1,1,1,.001f),true);UIFactory.Box(r,"Track",0,16,w,4,new Color(186/255f,201/255f,201/255f,.4f));
            var fillArea=UIFactory.Node(r,"FillArea",0,16,w,4);var fill=UIFactory.Box(fillArea,"Fill",0,0,w,4,UIFactory.Red);fill.rectTransform.anchorMin=Vector2.zero;fill.rectTransform.anchorMax=Vector2.one;fill.rectTransform.offsetMin=fill.rectTransform.offsetMax=Vector2.zero;
            var handleArea=UIFactory.Node(r,"HandleArea",7,0,w-14,36);var handle=UIFactory.Box(handleArea,"Handle",0,0,14,36,UIFactory.Ivory,true);handle.rectTransform.pivot=new Vector2(.5f,.5f);handle.rectTransform.anchoredPosition=Vector2.zero;handle.rectTransform.sizeDelta=new Vector2(14,0);
            slider.fillRect=fill.rectTransform;slider.handleRect=handle.rectTransform;slider.targetGraphic=handle;slider.direction=UnityEngine.UI.Slider.Direction.LeftToRight;slider.value=value;var output=UIFactory.Label(p,value.ToString("0"),x+w+25,y,w*.12f,40,20,UIFactory.Red);slider.onValueChanged.AddListener(v=>{output.text=v.ToString("0");send("setting:"+key+":"+v.ToString("0",CultureInfo.InvariantCulture));});
        }
        static void Upgrade(RectTransform p,object s,Action<string> send)
        {
            UIFactory.Line(p,260,242,1220,1,new Color(214/255f,220/255f,230/255f,.5f));
            for(int i=0;i<3;i++){float x=i*600+20,w=530;Color accent=i==1?UIFactory.Red:UIFactory.Ivory;DesignArt.Surface(p,x,92,w,657,new Color32(24,26,31,255),new Color32(12,14,18,255));DesignArt.Traces(p,x+25,110,w-50,240,accent);DesignArt.Halo(p,x+161,148,210,accent);InterfaceSymbols.Draw(p,new[]{InterfaceSymbol.Health,InterfaceSymbol.Attack,InterfaceSymbol.Coins}[i],x+199,184,135);T(p,new[]{"최대 체력","공격력","시작 골드"}[i],x+32,361,w-64,64,38,null,TextAlignmentOptions.Top);UIFactory.Line(p,x+42,440,w-84,1,new Color(246/255f,244/255f,237/255f,.5f));
                string[] rows={"현재 단계","다음 단계","효과 증가량","필요 영구 재화"};for(int j=0;j<4;j++){if(j<2)UIFactory.Box(p,"ProgressSocket",x+32,466+j*57,7,7,j==0?UIFactory.Ivory:accent);UIFactory.Row(p,rows[j],x+54,453+j*57,w-108);}B(p,"강화하기",x+115,711,300,62,send,"upgrade:"+i,true);}
            Notice(p,s,0,812,1300);B(p,"돌아가기",1470,795,290,62,send,"close");
        }
        static void Rewards(RectTransform p,object s,string id,Action<string> send,int d)
        {
            bool boss=id=="reward-boss",up=id=="reward-upgrade";int count=id=="reward-item"?2:3,selected=Num(s,"rewardChoice",-1);float gap=count==2?90:48,w=count==2?680:(1774-gap*2)/3,start=count==2?(1774-w*2-gap)/2:0;
            for(int i=0;i<count;i++){float x=start+i*(w+gap),y=45,h=650;Color accent=selected==i?UIFactory.Red:UIFactory.Ivory;var r=Card(p,"Reward"+i,x,y,w,h,selected==i);UIFactory.Line(r,30,108,w-60,1,new Color(.78f,.81f,.86f,.22f));B(r,"",0,0,w,h,send,"reward:"+i,selected==i);if(!boss)T(r,up?(i==0?"공통 강화":"형태 전용 강화"):"지속 유물 · 3등급",30,35,w-60,30,18,UIFactory.Muted);float size=count==2?240:208,ax=(w-size)/2;DesignArt.Halo(r,ax-25,106,size+50,new Color(accent.r,accent.g,accent.b,.28f));if(up){if(i==0)InterfaceSymbols.Draw(r,InterfaceSymbol.Health,ax,130,size);else UIFactory.Glyph(r,new[]{5,0,2}[i],ax,130,size);}else if(boss&&i!=1)InterfaceSymbols.Draw(r,i==0?InterfaceSymbol.Coins:InterfaceSymbol.LegacyCurrency,ax,130,size);else UIFactory.Item(r,boss?7:i+6,ax,130,size);T(r,boss?new[]{"금화","아이템","영구 재화"}[i]:up?new[]{"굳건한 생명","깊어지는 화기","날카로운 금기"}[i]:Name(i+6),30,360,w-60,64,36,null,TextAlignmentOptions.Top);UIFactory.Row(r,up?"현재 단계":boss?"획득 수량":"획득 효과",30,441,w-60);if(up)UIFactory.Row(r,"다음 단계",30,492,w-60);UIFactory.Line(r,30,h-46,w-60,1,selected==i?UIFactory.Ivory:new Color(accent.r,accent.g,accent.b,.5f));if(selected==i)T(r,"선택됨",30,h-35,w-60,30,18,accent,TextAlignmentOptions.TopRight);}
            B(p,"선택 확정",1460,766,300,65,send,"take-reward",true,selected>=0);
        }
}}




