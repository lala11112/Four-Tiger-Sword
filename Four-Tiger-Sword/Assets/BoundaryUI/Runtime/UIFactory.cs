using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace BoundaryZeroUI {
public sealed class BrightUiScope : MonoBehaviour {}
public static class UIFactory {
 public static bool IsBright(Transform p){return p&&p.GetComponentInParent<BrightUiScope>()!=null;}
 public static Color Readable(Transform p,Color c){
  float a=c.a;
  if(Mathf.Abs(c.r-Ivory.r)+Mathf.Abs(c.g-Ivory.g)+Mathf.Abs(c.b-Ivory.b)<.02f)return new Color(Text.r,Text.g,Text.b,a);
  if(!IsBright(p))return c;
  if(c.r>.65f&&c.g<.48f)return new Color(1,.51f,.44f,a);
  if(c.r>.7f&&c.g>.42f&&c.b<.55f)return new Color(1,.79f,.40f,a);
  
  float hi=Mathf.Max(c.r,Mathf.Max(c.g,c.b)),lo=Mathf.Min(c.r,Mathf.Min(c.g,c.b));
  if(hi<.3f)return new Color(.95f,.98f,1,a);
  if(hi<.55f)return new Color(.85f,.88f,.89f,a);
  if(hi>.9f&&hi-lo<.20f)return new Color(.95f,.98f,1,a);
  return c;
 }
 public static Color Ivory=new Color32(232,238,245,255),Text=new Color32(240,244,249,255),Cyan=new Color32(72,190,225,255),Red=new Color32(231,43,70,255),Muted=new Color32(163,181,201,255),Ink=new Color32(13,21,33,255);
 public static Color Accent => Red;
 static TMP_FontAsset font;public static TMP_FontAsset Font{get{if(!font)font=Resources.Load<TMP_FontAsset>("BoundaryUI/Fonts/BoundaryFont");return font;}set{font=value;}}
 public static RectTransform Node(Transform p,string name,float x,float y,float w,float h){var g=new GameObject(name,typeof(RectTransform));var r=g.GetComponent<RectTransform>();r.SetParent(p,false);r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;}
 public static TextMeshProUGUI Label(Transform p,string text,float x,float y,float w,float h,float size=24,Color? color=null,TextAlignmentOptions align=TextAlignmentOptions.TopLeft){var r=Node(p,"Text",x,y,w,h);var t=r.gameObject.AddComponent<TextMeshProUGUI>();if(Font)t.font=Font;t.text=text??"";t.fontSize=size;t.color=Readable(p,color??new Color32(241,244,243,255));t.alignment=align;if(size>=28)t.fontStyle=FontStyles.Bold;t.raycastTarget=false;t.enableWordWrapping=true;return t;}
 public static Image Box(Transform p,string name,float x,float y,float w,float h,Color color,bool raycast=false){var r=Node(p,name,x,y,w,h);var a=r.gameObject.AddComponent<Image>();a.color=color;a.raycastTarget=raycast;return a;}
 public static void Line(Transform p,float x,float y,float w,float h,Color? color=null){Box(p,"Rule",x,y,w,h,color??new Color(Ivory.r,Ivory.g,Ivory.b,.24f));}
 public static void Border(Transform p,float x,float y,float w,float h,Color c,float thickness=1){Line(p,x,y,w,thickness,c);Line(p,x,y+h-thickness,w,thickness,c);Line(p,x,y,thickness,h,c);Line(p,x+w-thickness,y,thickness,h,c);}
 public static Button Button(Transform p,string text,float x,float y,float w,float h,Action callback,bool primary=false,bool enabled=true){
  var r=Node(p,"Button_"+text,x,y,w,h);
  DesignArt.Surface(r,0,0,w,h,primary?new Color(.24f,.065f,.10f,.75f):new Color(.08f,.12f,.19f,.64f),new Color(.025f,.045f,.08f,.55f));
  var hit=Box(r,"Hit",0,0,w,h,new Color(1,1,1,.001f),true);
  var light=DesignArt.Surface(r,0,0,w,h,new Color(.9f,.95f,1,.11f),new Color(.9f,.95f,1,0));var group=light.gameObject.AddComponent<CanvasGroup>();group.alpha=0;group.blocksRaycasts=false;
  if(primary){Line(r,0,12,3,h-24,Red);Line(r,14,h-2,Mathf.Min(65,w*.28f),2,Ivory);}
  var label=Label(r,text,12,0,w-24,h,22,Ivory,TextAlignmentOptions.Center);label.characterSpacing=1;
  var b=r.gameObject.AddComponent<UnityEngine.UI.Button>();b.targetGraphic=hit;b.interactable=enabled;b.transition=Selectable.Transition.None;if(callback!=null)b.onClick.AddListener(()=>callback());
  var fx=r.gameObject.AddComponent<UIFocusLight>();fx.Light=group;fx.Base=0;fx.Control=b;
  if(!enabled){var cg=r.gameObject.AddComponent<CanvasGroup>();cg.alpha=.34f;}return b;
 }
 public static RawImage Item(Transform p,int id,float x,float y,float size){id=((id%12)+12)%12;var r=Node(p,"Item_"+id,x,y,size,size);var a=r.gameObject.AddComponent<RawImage>();a.texture=Resources.Load<Texture2D>("BoundaryUI/Art/"+(id>=6?"items-extra":"items"));int n=((id%6)+6)%6;a.uvRect=new Rect(n%3/3f,n<3?.5f:0,1/3f,.5f);a.raycastTarget=false;return a;}
 public static Image Glyph(Transform p,int id,float x,float y,float size){return Art(p,"Glyph"+((id%6+6)%6),x,y,size,size);}
 public static Image Art(Transform p,string name,float x,float y,float w,float h){var a=Box(p,name,x,y,w,h,Color.white);a.sprite=Resources.Load<Sprite>("BoundaryUI/Art/"+name);a.preserveAspect=true;return a;}
 public static void Row(Transform p,string label,float x,float y,float w,string value=null){Label(p,label,x,y,w*.65f,38,22,Muted);if(value==null)Line(p,x+w-100,y+25,90,1,new Color(.86f,.9f,.89f,.3f));else Label(p,value,x+w*.65f,y,w*.35f,38,22,Ivory,TextAlignmentOptions.TopRight);Line(p,x,y+44,w,1);Line(p,x,y+44,18,1,new Color(.8f,.94f,.92f,.65f));}
 public static RectTransform Scroll(Transform p,string name,float x,float y,float w,float h,float contentHeight){var r=Node(p,name,x,y,w,h);var scroll=r.gameObject.AddComponent<ScrollRect>();var viewport=Node(r,"Viewport",0,0,w,h);var img=viewport.gameObject.AddComponent<Image>();img.color=Color.white;var mask=viewport.gameObject.AddComponent<Mask>();mask.showMaskGraphic=false;var content=Node(viewport,"Content",0,0,w,Mathf.Max(h,contentHeight));scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=35;return content;}
 public static RectTransform Popup(RectTransform layer,string id,string title,Action close,bool compact=false){
  bool itemWindow=id.StartsWith("shop-")||id=="inventory";
  float w=compact?1100:1920,h=compact?720:1120,scale=compact?1:itemWindow?1.08f:.88f;
  Box(layer,"ModalBlur",0,0,2560,1440,Color.white).gameObject.AddComponent<PopupBackdropBlur>();
  Box(layer,"ModalShade",0,0,2560,1440,new Color(.015f,.025f,.045f,.12f),true);
  var window=Node(layer,"Popup_"+id,(2560-w*scale)/2,(1440-h*scale)/2,w,h);window.localScale=Vector3.one*scale;window.gameObject.AddComponent<BrightUiScope>();
  DesignArt.Surface(window,34,40,w-58,h-58,new Color(.015f,.025f,.045f,.22f),new Color(.015f,.025f,.045f,.24f));
  DesignArt.Surface(window,29,29,w-58,h-58,new Color(18/255f,33/255f,51/255f,.91f),new Color(8/255f,17/255f,30/255f,.91f));
  Box(window,"WindowHit",29,29,w-58,h-58,new Color(1,1,1,.001f),true);
  var frame=Node(window,"LegacyWindowFrame",0,0,w,h).gameObject.AddComponent<LegacyWindowGraphic>();frame.raycastTarget=false;
  Line(window,compact?82:73,78,4,44,Red);Art(window,"Seal",compact?95:86,72,23,44);
  Label(window,title,compact?141:132,68,450,60,36,Ivory).characterSpacing=1;
  if(id.StartsWith("shop-")||id=="inventory"||id=="upgrade"){
   Label(window,"◇  금화",1220,83,150,32,19,Muted);Line(window,1360,111,80,1);Label(window,"◇  영구 재화",1480,83,190,32,19,Muted);Line(window,1670,111,80,1);
  }
  Line(window,compact?80:72,153,w-(compact?160:144),1,new Color(.76f,.84f,.95f,.22f));Line(window,compact?80:72,153,110,2,Ivory);Line(window,compact?195:187,153,42,2,Red);
  var cb=Button(window,"×",w-114,67,44,43,close);cb.GetComponentInChildren<TextMeshProUGUI>().fontSize=29;
  Label(window,"ESC",w-114,116,44,24,13,Muted,TextAlignmentOptions.Center);
  return Node(window,"Content",compact?82:73,171,compact?936:1774,compact?435:860);
 }
}
public class UIFocusLight:MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,IPointerDownHandler,IPointerUpHandler,ISelectHandler,IDeselectHandler {
 public CanvasGroup Light;public float Base;public Selectable Control;bool hover,pressed;
 public void OnPointerEnter(PointerEventData e){hover=true;}public void OnPointerExit(PointerEventData e){hover=false;pressed=false;}public void OnPointerDown(PointerEventData e){pressed=true;}public void OnPointerUp(PointerEventData e){pressed=false;}public void OnSelect(BaseEventData e){hover=true;}public void OnDeselect(BaseEventData e){hover=false;}
 void Update(){if(!Light)return;float target=Control&&!Control.IsInteractable()?0:pressed?.3f:hover?1:Base;Light.alpha=Mathf.Lerp(Light.alpha,target,1-Mathf.Exp(-Time.unscaledDeltaTime*14));}
}
}
