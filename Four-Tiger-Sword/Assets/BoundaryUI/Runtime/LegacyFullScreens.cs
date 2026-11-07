using System;
using TMPro;
using UnityEngine;
namespace BoundaryZeroUI {
public static class LegacyFullScreens {
 static readonly Color Silver=new Color32(232,238,245,255), Red=new Color32(231,43,70,255), Muted=new Color32(139,159,180,255);
 static TextMeshProUGUI Type(Transform p,string text,float x,float y,float w,float h,float size,bool korean=false,Color? color=null){var t=UIFactory.Label(p,text,x,y,w,h,size,color??Silver);var f=Resources.Load<TMP_FontAsset>("BoundaryUI/Fonts/"+(korean?"TitleKorean":"TitleLatin"));if(korean&&size<40){if(UIFactory.Font)t.font=UIFactory.Font;}else if(f)t.font=f;t.fontStyle=FontStyles.Normal;t.characterSpacing=korean?1:3;return t;}
 public static void BuildResult(RectTransform p,string id,Action<string> send){
  p=UIFactory.Node(p,"FullScreenResult_"+id,0,0,2560,1440);bool clear=id=="clear";LuminousBackdrop.Build(p,true);
  OutcomeSymbols.Draw(p,!clear,120,270,1120,806);
  UIFactory.Line(p,1240,380,1,700,new Color(.65f,.75f,.86f,.13f));
  Type(p,clear?"경계 돌파":"탐사 종료",1372,365,1000,128,94,true);
  UIFactory.Line(p,1380,521,100,4,Red);UIFactory.Line(p,1500,522,830,1,new Color(.65f,.75f,.86f,.25f));
  string[] rows=clear?new[]{"처치한 적","격파한 보스","획득 영구 재화","클리어 보너스"}:new[]{"처치한 적","격파한 보스","획득 영구 재화"};
  for(int i=0;i<rows.Length;i++){float y=620+i*79;Type(p,rows[i],1380,y,650,45,25,true,Muted);var value=Type(p,"",2050,y,280,45,29);value.name="ResultValue_"+i;value.alignment=TextAlignmentOptions.TopRight;UIFactory.Line(p,1380,y+56,950,1,new Color(.65f,.75f,.86f,.16f));}
  var b=UIFactory.Button(p,"거점 귀환",1380,1030,950,94,()=>send("go:main"),true);b.name="Button_거점 귀환";
 }
 public static void BuildLoading(RectTransform p,UiState state){
  p=UIFactory.Node(p,"FullScreenLoading",0,0,2560,1440);LuminousBackdrop.Build(p,true);
  var seal=UIFactory.Node(p,"BoundaryConnectionSeal",160,-10,2240,1260).gameObject.AddComponent<LoadingSealGraphic>();
  var status=UIFactory.Label(p,"",160,1238,1300,50,23,Muted);status.alignment=TextAlignmentOptions.BottomLeft;
  var label=UIFactory.Label(p,"불러오는 중",1860,1210,350,40,24,Silver);label.fontStyle=FontStyles.Normal;label.characterSpacing=2;
  UIFactory.Line(p,1860,1272,520,2,new Color(.65f,.75f,.86f,.24f));
  var fill=UIFactory.Box(p,"Progress",1860,1271,0,4,Silver).rectTransform;
  var signal=UIFactory.Box(p,"LoadingActivity",1860,1271,64,4,Red).rectTransform;
  var percent=Type(p,"",2260,1210,120,43,23);percent.alignment=TextAlignmentOptions.TopRight;
  var display=p.gameObject.AddComponent<LegacyLoadingDisplay>();display.Track=fill;display.Signal=signal;display.Status=status;display.Percentage=percent;display.Seal=seal;display.Apply(state.loadingProgress,state.loadingStatus);
 }
}
public sealed class LegacyLoadingDisplay:MonoBehaviour {
 public RectTransform Track,Signal;public TextMeshProUGUI Status,Percentage;public LoadingSealGraphic Seal;
 const float TrackWidth=520;
 public void Apply(float? progress,string status){
  bool known=progress.HasValue&&!float.IsNaN(progress.Value)&&!float.IsInfinity(progress.Value);float value=known?Mathf.Clamp01(progress.Value):0;
  if(Track){Track.gameObject.SetActive(known);Track.sizeDelta=new Vector2(TrackWidth*value,4);}
  if(Signal)Signal.gameObject.SetActive(!known);
  if(Status)Status.text=status??"";
  if(Percentage)Percentage.text=known?Mathf.RoundToInt(value*100)+"%":"";
  if(Seal){Seal.Determinate=known;Seal.Progress=value;Seal.SetVerticesDirty();}
 }
 void Update(){if(Signal&&Signal.gameObject.activeSelf){float x=(.5f-.5f*Mathf.Cos(Time.unscaledTime*1.8f))*(TrackWidth-64);Signal.anchoredPosition=new Vector2(1860+x,-1271);}}
}
}
