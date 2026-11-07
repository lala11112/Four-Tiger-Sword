using System;
using TMPro;
using UnityEngine;
namespace BoundaryZeroUI {
public static class LegacyMainView {
 static readonly Color Silver=new Color32(232,238,245,255),Red=new Color32(231,43,70,255),Muted=new Color32(139,159,180,255);
 static TextMeshProUGUI Type(Transform p,string text,float x,float y,float w,float h,float size,bool korean=false,Color? color=null){var t=UIFactory.Label(p,text,x,y,w,h,size,color??Silver);var f=Resources.Load<TMP_FontAsset>("BoundaryUI/Fonts/"+(korean?"TitleKorean":"TitleLatin"));if(f)t.font=f;t.fontStyle=FontStyles.Normal;t.characterSpacing=korean?2:3;return t;}
 public static void Build(RectTransform p,Action<string> dispatch,int design){
 TitleArt.Background(p,design);
 TitleArt.Blade(p,166,238,1120,660);
 Type(p,"경계",366,857,760,109,77,true,Silver);
 UIFactory.Line(p,376,981,66,5,Red);
 TitleArt.Wordmark(p,364,1002,810,208);
 float mx=1600,my=470;
 var menuRoot=UIFactory.Node(p,"TitleMenu",0,0,2560,1440);var menu=menuRoot.gameObject.AddComponent<TitleMenuPanel>();menu.Choices=new TitleMenuChoice[4];
 string[] names={"새 게임","이어하기","설정","종료"},actions={"enter","","go:settings","confirm:quit"};
 for(int i=0;i<4;i++){int index=i;var row=UIFactory.Node(menuRoot,"MainButton"+i,mx,my+i*130,700,103);var hit=UIFactory.Box(row,"Pointer",0,0,700,103,new Color(1,1,1,.001f),true);
 var band=UIFactory.Node(row,"SelectionBand",0,0,700,103);band.gameObject.AddComponent<TitleMenuBand>().raycastTarget=false;var group=band.gameObject.AddComponent<CanvasGroup>();group.alpha=i==0?1:0;group.blocksRaycasts=false;
 var label=Type(row,names[i],110,26,440,52,32,true,Silver);
 var choice=hit.gameObject.AddComponent<TitleMenuChoice>();choice.Menu=menu;choice.Index=i;choice.Available=i!=1;choice.Chosen=i==0;choice.Accent=group;choice.Label=label;choice.Click=()=>dispatch(actions[index]);menu.Choices[i]=choice;
 UIFactory.Line(row,110,102,545,1,new Color(.56f,.65f,.76f,.15f));
 }
 menu.Select(0);
 }
}
}
