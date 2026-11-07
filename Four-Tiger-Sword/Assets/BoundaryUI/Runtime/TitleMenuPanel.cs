using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace BoundaryZeroUI {
public sealed class TitleMenuPanel : MonoBehaviour {
 public TitleMenuChoice[] Choices;public int Selected;BoundaryUIController controller;
 public void Select(int n){if(n<0||n>=Choices.Length||!Choices[n].Available)return;Selected=n;for(int i=0;i<Choices.Length;i++)Choices[i].Chosen=i==n;}
 void Start(){controller=GetComponentInParent<BoundaryUIController>();Select(Selected);}
 void Step(int delta){for(int i=1;i<=Choices.Length;i++){int n=(Selected+delta*i+Choices.Length*3)%Choices.Length;if(Choices[n].Available){Select(n);return;}}}
 void Update(){if(!controller)controller=GetComponentInParent<BoundaryUIController>();if(!controller||controller.CurrentScreen!="main"||controller.IsBrowserOpen)return;if(Input.GetKeyDown(KeyCode.DownArrow)||Input.GetKeyDown(KeyCode.S))Step(1);if(Input.GetKeyDown(KeyCode.UpArrow)||Input.GetKeyDown(KeyCode.W))Step(-1);if(Input.GetKeyDown(KeyCode.Return)||Input.GetKeyDown(KeyCode.KeypadEnter))Choices[Selected].Activate();}
}
public sealed class TitleMenuChoice : MonoBehaviour,IPointerEnterHandler,IPointerClickHandler {
 public TitleMenuPanel Menu;public int Index;public bool Available=true,Chosen;public CanvasGroup Accent;public TextMeshProUGUI Label,SubLabel;public Action Click;
 public void Activate(){if(Available)Click?.Invoke();}
 public void OnPointerEnter(PointerEventData e){if(Available)Menu.Select(Index);}
 public void OnPointerClick(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)Activate();}
 void Update(){float a=Chosen&&Available?1:0;Accent.alpha=Mathf.Lerp(Accent.alpha,a,1-Mathf.Exp(-Time.unscaledDeltaTime*18));Label.color=Available?Color.Lerp(new Color(.953f,.965f,.969f,.88f),new Color32(232,238,245,255),Accent.alpha):new Color(.62f,.69f,.73f,.36f);if(SubLabel)SubLabel.color=Available?Color.Lerp(new Color(.953f,.965f,.969f,.64f),new Color(232/255f,238/255f,245/255f,.82f),Accent.alpha):new Color(.62f,.69f,.73f,.32f);}
}
public sealed class TitleMenuBand : MaskableGraphic {
 protected override void OnPopulateMesh(VertexHelper v) {
  v.Clear();var r=rectTransform.rect;float w=r.width,h=r.height;
  Vector3 P(float x,float y)=>new Vector3(r.xMin+x,r.yMax-y,0);
  void Quad(float x1,float y1,float x2,float y2,float x3,float y3,float x4,float y4,Color tint) {
   int n=v.currentVertCount;Color c=tint*color;
   v.AddVert(P(x1,y1),c,Vector2.zero);v.AddVert(P(x2,y2),c,Vector2.zero);v.AddVert(P(x3,y3),c,Vector2.zero);v.AddVert(P(x4,y4),c,Vector2.zero);
   v.AddTriangle(n,n+1,n+2);v.AddTriangle(n,n+2,n+3);
  }
  Quad(30,0,w,0,w-30,h,0,h,new Color(8/255f,15/255f,20/255f,.34f));
  Quad(30,0,w,0,w-30,h,0,h,new Color(232/255f,238/255f,245/255f,.065f));
  Color frame=new Color(232/255f,238/255f,245/255f,.16f);
  Quad(30,0,w,0,w-1,1,29.6f,1,frame);
  Quad(0,h-1,w-29.6f,h-1,w-30,h,0,h,frame);
  Quad(30,0,31,0,1,h,0,h,frame);
  Quad(w-1,0,w,0,w-30,h,w-31,h,frame);
  Color cyan=new Color(231/255f,43/255f,70/255f);
  Quad(30,0,130,0,129,2,29.4f,2,new Color(232/255f,238/255f,245/255f,.65f));
  Quad(w-67,18,w-55,18,w-55,30,w-67,30,new Color(231/255f,43/255f,70/255f,.68f));
  int n=v.currentVertCount;Color marker=cyan*color;
  v.AddVert(P(64,h*.33f),marker,Vector2.zero);v.AddVert(P(64,h*.67f),marker,Vector2.zero);v.AddVert(P(84,h*.5f),marker,Vector2.zero);v.AddTriangle(n,n+1,n+2);
 }
}
}

