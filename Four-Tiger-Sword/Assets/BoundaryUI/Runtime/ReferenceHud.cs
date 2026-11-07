using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
namespace BoundaryZeroUI {
public class ReferenceHud : MonoBehaviour {
 // Coordinates describe the padded exported parts, in the QHD top-left space.
 public const float HudScale=1.35f,MapScale=1.65f,FormScale=1.50f,MapX=2095,MapY=54,MapWidth=245,MapHeight=255;
 public const float FormsX=64,FormsY=1232,FormStep=84,SkillsX=2199.7f,SkillsY=1198,SkillStep=122;
 public RectTransform MinimapContent;
 public BoundaryGoldHud GoldHud { get; private set; }
 // Original public indices are stable: Q (0) is absent; E/R remain at 1/2.
 public readonly UnityEngine.UI.Image[] AbilityImages=new UnityEngine.UI.Image[3];
 RectTransform indicator,hp;GameObject interact,cross;CanvasGroup shortcuts;
 readonly Dictionary<string,TextMeshProUGUI> values=new Dictionary<string,TextMeshProUGUI>();
 readonly UnityEngine.UI.Image[] forms=new UnityEngine.UI.Image[5];
 float targetX;static readonly int[] order={0,3,4,2,1};
 static readonly Color Silver=new Color32(232,238,245,255),Crimson=new Color32(231,43,70,255),Ink=new Color32(13,21,33,235);
 static readonly Color[] Element={new Color32(231,43,70,255),new Color32(207,166,81,255),new Color32(232,238,245,255),new Color32(72,190,225,255),new Color32(83,175,122,255)};
 static SvgFrameGraphic Drawing(Transform p,string name,float x,float y,float w,float h,string svg){var g=UIFactory.Node(p,name,x,y,w,h).gameObject.AddComponent<SvgFrameGraphic>();g.Source=new TextAsset("<svg>"+svg+"</svg>");g.ViewBox=new Vector2(w,h);g.raycastTarget=false;return g;}
 public static ReferenceHud Build(RectTransform root,Action<string> dispatch){
  var r=UIFactory.Node(root,"ReferenceHUD",0,0,2560,1440);var hud=r.gameObject.AddComponent<ReferenceHud>();
  var frame=UIFactory.Node(r,"LegacyBladeFrame",0,0,2560,1440).gameObject.AddComponent<SvgFrameGraphic>();frame.Resource="BoundaryUI/Art/hud-frame";frame.ViewBox=new Vector2(2560,1440);frame.raycastTarget=false;
  // The crest retains the original suspended red seal, with a quieter PC-sized health rail.
  UIFactory.Art(r,"Seal",98,140,28,58);
  hud.hp=UIFactory.Node(r,"HealthFill",170,93,0,18);
  Drawing(hud.hp,"CrimsonHealth",0,0,458,18,"<path d='M15 0H458L443 18H0Z' fill='#E72B46'/>");
  hud.hp.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
  hud.values["health-text"]=UIFactory.Label(r,"",178,126,280,28,18,Silver);
  var strip=UIFactory.Node(r,"FiveForms",FormsX,FormsY,408,105);
  strip.localScale=Vector3.one*FormScale;
  for(int j=0;j<5;j++){
   int index=order[j];float x=2+j*FormStep;
   string color=ColorUtility.ToHtmlStringRGB(Element[index]);
   Drawing(strip,"ElementDiamond"+index,x,2,68,72,"<path d='M34 1L67 34L34 67L1 34Z' fill='#0D1521' fill-opacity='.88' stroke='#"+color+"' stroke-opacity='.72' stroke-width='1.4'/><path d='M34 7L61 34L34 61L7 34Z' fill='none' stroke='#E8EEF5' stroke-opacity='.18' stroke-width='1'/><path d='M34 0V9M34 59V68' stroke='#"+color+"' stroke-width='2'/>");
   hud.forms[index]=UIFactory.Glyph(strip,index,x+12,14,44);
   UIFactory.Label(strip,(j+1).ToString(),x+22,78,24,23,15,Silver,TextAlignmentOptions.Center);
  }
  hud.indicator=UIFactory.Node(strip,"ActiveForm",2,-3,68,78);
  Drawing(hud.indicator,"SelectionBrackets",0,0,68,78,"<path d='M0 24V9H15M53 9H68V24M0 54V69H15M53 69H68V54' fill='none' stroke='#E8EEF5' stroke-width='2'/><path d='M28 76H40' stroke='#E72B46' stroke-width='2'/>");
  hud.MinimapContent=UIFactory.Node(r,"MinimapContent",MapX+4*MapScale,MapY+19*MapScale,222*MapScale,222*MapScale);
  hud.MinimapContent.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
  hud.values["objective"]=UIFactory.Label(r,"",MapX,MapY+MapHeight*MapScale+16,MapWidth*MapScale,35,19,Silver);
  var skills=UIFactory.Node(r,"Skills",SkillsX,SkillsY,220,153);
  skills.localScale=Vector3.one*HudScale;
  // Preserve original public ability indices: slot 0 (Q) has no visual.
  for(int i=1;i<3;i++){
   float x=2+(i-1)*SkillStep;
   Drawing(skills,"SkillMount"+i,x,2,94,116,"<path d='M16 1H77L93 17V78L77 94H16L1 78V17Z' fill='#0D1521' fill-opacity='.76'/><circle cx='47' cy='47' r='39' fill='none' stroke='#E8EEF5' stroke-opacity='.45' stroke-width='1'/><path d='M0 26V15L15 0H36M58 94H79L94 79V68' fill='none' stroke='#E8EEF5' stroke-opacity='.8' stroke-width='1.5'/><path d='M37 105H57' stroke='#E72B46' stroke-width='2'/>");
   hud.AbilityImages[i]=UIFactory.Art(skills,"Ability"+i,x+12,14,70,70);
   hud.values["ability-"+i]=UIFactory.Label(skills,"",x+23,125,48,28,18,Silver,TextAlignmentOptions.Center);
  }
  var sh=UIFactory.Node(r,"Shortcuts",0,0,2560,1440);hud.shortcuts=sh.gameObject.AddComponent<CanvasGroup>();
  Shortcut(sh,"TAB","소지품",2005,MapY+MapHeight*MapScale+72,()=>dispatch("go:inventory"));Shortcut(sh,"ESC","메뉴",2260,MapY+MapHeight*MapScale+72,()=>dispatch("go:pause"));
  var hit=UIFactory.Node(r,"ShopInteract",1184,1030,192,54);hud.interact=hit.gameObject;
  var bg=UIFactory.Box(hit,"Surface",0,0,192,54,Ink,true);UIFactory.Border(hit,10,10,34,34,Silver);UIFactory.Label(hit,"E",10,10,34,34,20,Silver,TextAlignmentOptions.Center);UIFactory.Label(hit,"상점",61,12,110,34,21,Silver);
  var btn=hit.gameObject.AddComponent<UnityEngine.UI.Button>();btn.targetGraphic=bg;btn.onClick.AddListener(()=>dispatch("go:shop-buy"));
  var cr=UIFactory.Node(r,"Crosshair",1272,712,16,16);hud.cross=cr.gameObject;
  Drawing(cr,"Aim",0,0,16,16,"<path d='M8 0V4M8 12V16M0 8H4M12 8H16' stroke='#E8EEF5' stroke-opacity='.75' stroke-width='1'/>");
  hud.GoldHud=BoundaryGoldHud.Attach(r);
  hud.Apply(new UiState());return hud;
 }
 static void Shortcut(Transform p,string key,string title,float x,float y,Action action){var r=UIFactory.Node(p,title,x,y,218,34);var bg=UIFactory.Box(r,"Hit",0,0,218,34,new Color(13/255f,21/255f,33/255f,.68f),true);UIFactory.Border(r,0,0,50,28,new Color(.91f,.93f,.96f,.48f));UIFactory.Label(r,key,0,0,50,28,16,Silver,TextAlignmentOptions.Center);UIFactory.Label(r,title,63,0,145,30,18,Silver);var b=r.gameObject.AddComponent<UnityEngine.UI.Button>();b.targetGraphic=bg;b.onClick.AddListener(()=>action());}
 public void Apply(UiState state){if(state==null)return;int current=Mathf.Clamp(state.form,0,4);targetX=2+Array.IndexOf(order,current)*FormStep;for(int i=0;i<5;i++){forms[i].color=new Color(1,1,1,i==current?1:.80f);}values["health-text"].text=state.healthText??"";values["objective"].text=state.objective??"";for(int i=1;i<3;i++){int keyIndex=state.abilityKeys!=null&&state.abilityKeys.Length==2?i-1:i;values["ability-"+i].text=state.abilityKeys!=null&&keyIndex<state.abilityKeys.Length?state.abilityKeys[keyIndex]??"":"";}if(state.uiData!=null)foreach(var pair in state.uiData)if(values.ContainsKey(pair.Key))values[pair.Key].text=pair.Value??"";hp.sizeDelta=new Vector2(state.healthRatio.HasValue?Mathf.Clamp01(state.healthRatio.Value)*458:0,18);}
 public void SetModal(bool modal){if(interact)interact.SetActive(!modal);if(cross)cross.SetActive(!modal);if(shortcuts){shortcuts.alpha=modal?.45f:1;shortcuts.interactable=!modal;shortcuts.blocksRaycasts=!modal;}}
 void Update(){if(indicator){var p=indicator.anchoredPosition;p.x=Mathf.Lerp(p.x,targetX,1-Mathf.Exp(-Time.unscaledDeltaTime*15));indicator.anchoredPosition=p;}}
}
}


