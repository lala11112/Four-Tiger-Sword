using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
namespace BoundaryZeroUI { public static partial class CompleteViews {
    static object OwnedView(object s) {
        var view=new Hashtable();foreach(string key in new[]{"owned","grades","elements","materials"})view[key]=Read(s,key);
        view["selected"]=Num(s,"shopInventorySelected",-1);view["category"]=Num(s,"shopInventoryCategory",-1);return view;
    }
    static List<int> FilteredOwned(object s) {var ids=List(s,"owned");int cat=Num(s,"category",-1);ids.RemoveAll(i=>cat>=0&&El(s,i)!=cat);return ids;}
    static void LargeItemTile(Transform p,object s,int i,float x,float y,float w,float h,Action<string> inspect) {
        bool selected=Num(s,"selected",-1)==i;var r=Card(p,"LargeItem"+i,x,y,w,h,selected);
        B(r,"",0,0,w,h,inspect,"item:"+i);float art=Mathf.Min(170,h-72);
        UIFactory.Item(r,i,(w-art)/2,4,art);UIFactory.Glyph(r,El(s,i),w-37,12,23);
        T(r,Name(i),12,h-62,w-24,30,24,UIFactory.Ivory,TextAlignmentOptions.Top);
        T(r,Grade(s,i)+"등급 · "+Forms[El(s,i)]+(List(s,"materials").Contains(i)?" · 재료":""),12,h-31,w-24,25,18,UIFactory.Muted,TextAlignmentOptions.Top);
    }
    static void LargeItemGrid(Transform p,object s,List<int> ids,string name,float x,float y,float w,float h,int cols,float tileH,Action<string> inspect) {
        const float gap=12;float tw=(w-gap*(cols-1))/cols;var content=UIFactory.Scroll(p,name,x,y,w,h,Mathf.Max(h,Mathf.Ceil(ids.Count/(float)cols)*(tileH+gap)));
        for(int j=0;j<ids.Count;j++)LargeItemTile(content,s,ids[j],j%cols*(tw+gap),j/cols*(tileH+gap),tw,tileH,inspect);
        if(ids.Count==0)T(content,"표시할 아이템이 없습니다.",0,60,w,60,24,UIFactory.Muted,TextAlignmentOptions.Top);
    }
    static void LargeItemDetail(Transform p,object s,string name,float x,float y,float w,float h,float art,string footer,string action,Action<string> send,bool enabled=true,string price="") {
        var panel=UIFactory.Node(p,name,x,y,w,h);DesignArt.Surface(panel,0,0,w,h,new Color32(24,26,31,255),new Color32(12,14,18,255));
        int i=Num(s,"selected",-1);bool valid=i>=0;
        if(valid){float size=Mathf.Min(art,w-48);DesignArt.Halo(panel,(w-size)/2,5,size,new Color(ElementColor(El(s,i)).r,ElementColor(El(s,i)).g,ElementColor(El(s,i)).b,.22f));UIFactory.Item(panel,i,(w-size)/2,6,size);
            T(panel,Name(i),28,art+20,w-56,51,34);UIFactory.Glyph(panel,El(s,i),28,art+78,27);T(panel,Forms[El(s,i)]+" · "+Grade(s,i)+"등급",68,art+80,w-96,29,20,UIFactory.Muted);
            UIFactory.Row(panel,"효과",28,art+126,w-56);UIFactory.Row(panel,"시너지",28,art+182,w-56,"오행 공명");if(price!="")UIFactory.Row(panel,price,28,h-116,w-56);
        }else T(panel,"아이템을 선택하세요.",24,230,w-48,60,26,UIFactory.Muted,TextAlignmentOptions.Top);
        if(footer!="")B(panel,footer,(w-260)/2,h-64,260,56,send,action,true,valid&&enabled);
    }
    static void ShopOwned(Transform p,object s,Action<string> send) {
        var dock=UIFactory.Node(p,"ShopInventoryPanel",1184,95,590,714);var view=OwnedView(s);Action<string> own=a=>send("shop-inventory:"+a);
        Sec(dock,"소지품",0,0,590,List(s,"owned").Count+"개");Category(dock,view,0,58,590,own);LargeItemGrid(dock,view,FilteredOwned(view),"ShopOwnedScroll",0,118,590,596,2,238,own);
    }
    static void ShopSplit(RectTransform p,object s,string id,Action<string> send) {
        if(id=="shop-sell") {var view=OwnedView(s);Action<string> own=a=>send("shop-inventory:"+a);var panel=UIFactory.Node(p,"ShopInventoryPanel",0,95,1100,714);
            Sec(panel,"판매할 유물",0,0,1100,List(s,"owned").Count+"개");Category(panel,view,0,58,1100,own);LargeItemGrid(panel,view,FilteredOwned(view),"ShopOwnedScroll",0,118,1100,596,4,238,own);
            LargeItemDetail(p,view,"ShopTradeDetail",1140,95,634,714,360,"판매하기","sell",send,true,"판매 가격");Notice(p,s,1140,823,634);return;}
        bool open=Flag(s,"shopInventoryOpen");float stockW=open?550:1000;Sec(p,"상인의 물품",0,95,stockW);Category(p,s,0,155,stockW,send);
        var ids=new List<int>();for(int i=0;i<5;i++)ids.Add((i+Num(s,"roll"))%12);int cat=Num(s,"category",-1);ids.RemoveAll(i=>cat>=0&&El(s,i)!=cat);
        LargeItemGrid(p,s,ids,"ShopStockScroll",0,210,stockW,425,open?2:3,238,send);
        float serviceW=(stockW-20)/2;UIFactory.Row(p,"새로고침 비용",0,658,serviceW);UIFactory.Row(p,"회복 비용",serviceW+20,658,serviceW);
        B(p,"새로고침",0,748,serviceW,56,send,"reroll");B(p,Flag(s,"heal")?"회복 완료":"체력 회복",serviceW+20,748,serviceW,56,send,"heal",false,!Flag(s,"heal"));
        bool ownFocus=open&&Flag(s,"shopInspectOwned");object detail=ownFocus?OwnedView(s):s;float dx=open?590:1040,dw=open?550:734;
        LargeItemDetail(p,detail,"ShopTradeDetail",dx,95,dw,714,open?300:360,ownFocus?"상품 보기":"구매하기",ownFocus?"shop-focus:stock":"buy",send,ownFocus||ids.Contains(Num(s,"selected",-1)),ownFocus?"":"구매 가격");Notice(p,s,dx,823,dw);if(open)ShopOwned(p,s,send);
    }
    static void InventoryLarge(RectTransform p,object s,Action<string> send) {
        Sec(p,"보유 아이템",0,0,1010,List(s,"owned").Count+"개");Category(p,s,0,62,1010,send);LargeItemGrid(p,s,FilteredOwned(s),"InventoryOwnedScroll",0,130,1010,680,3,248,send);
        LargeItemDetail(p,s,"InventoryItemDetail",1050,0,724,810,420,"","",send);Notice(p,s,1050,823,724);
    }
    static void SynthSplit(RectTransform p,object s,Action<string> send) {
        var view=OwnedView(s);Action<string> own=a=>send("shop-inventory:"+a);var panel=UIFactory.Node(p,"ShopInventoryPanel",0,95,650,714);
        Sec(panel,"소지품",0,0,650,List(s,"owned").Count+"개");Category(panel,view,0,58,650,own);LargeItemGrid(panel,view,FilteredOwned(view),"ShopOwnedScroll",0,118,650,596,2,248,own);
        var mats=List(s,"materials");int selected=Num(view,"selected",-1);bool included=mats.Contains(selected);LargeItemDetail(p,view,"SynthesisItemDetail",690,95,500,714,340,included?"재료 제외":"재료 추가","material:"+selected,send,included||(selected>=0&&Grade(s,selected)<4&&mats.Count<3));
        int output=Num(s,"outElement",Num(s,"output"));var result=UIFactory.Node(p,"SynthesisResult",1230,95,544,714);Sec(result,"합성 재료",0,0,544,"서로 다른 유물 3개");
        for(int i=0;i<3;i++){var socket=Card(result,"MaterialSocket"+i,i*184,65,176,191,mats.Count>i);if(mats.Count>i){int item=mats[i];UIFactory.Item(socket,item,28,2,120);T(socket,Name(item),6,120,164,30,18,null,TextAlignmentOptions.Top);B(socket,"제외",35,153,106,34,send,"material:"+item);}else T(socket,"＋",0,44,176,90,48,UIFactory.Muted,TextAlignmentOptions.Top);}
        DesignArt.Halo(result,192,280,160,new Color(ElementColor(output).r,ElementColor(output).g,ElementColor(output).b,.25f));UIFactory.Glyph(result,output,214,301,116);T(result,"4등급 유물",20,443,504,49,34,null,TextAlignmentOptions.Top);
        for(int i=0;i<5;i++)B(result,Forms[i],22+i*102,510,92,44,send,"out:"+i,output==i);UIFactory.Row(result,"결과 속성",22,576,500,Forms[output%5]);B(result,"합성하기",122,650,300,56,send,"confirm:synth",true,mats.Count==3);Notice(p,s,1230,823,544);
    }
}}

