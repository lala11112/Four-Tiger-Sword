using UnityEngine;
namespace BoundaryZeroUI {
/// <summary>Editable vector rails derived from the blade HUD's slanted silver contours.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class BoundaryGoldFrameGraphic : UnityEngine.UI.MaskableGraphic {
 public enum DrawingKind { Balance, Reward, Coin }
 public DrawingKind Kind;
 static readonly Color Silver=new Color32(232,238,245,255), Gold=new Color32(207,166,81,255), Red=new Color32(231,43,70,255);
 protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh){
  vh.Clear(); float w=rectTransform.rect.width,h=rectTransform.rect.height;
  if(Kind==DrawingKind.Coin){
   Vector2[] outer={new Vector2(9,1),new Vector2(21,1),new Vector2(29,9),new Vector2(29,21),new Vector2(21,29),new Vector2(9,29),new Vector2(1,21),new Vector2(1,9)};
   for(int i=0;i<8;i++){var a=outer[i];var b=outer[(i+1)%8];Line(vh,a,b,1.5f,Gold);Line(vh,Vector2.Lerp(new Vector2(15,15),a,.72f),Vector2.Lerp(new Vector2(15,15),b,.72f),.65f,new Color(Gold.r,Gold.g,Gold.b,.5f));}
   Poly(vh,Gold,new Vector2(16,7),new Vector2(11,15),new Vector2(16,15),new Vector2(13,23),new Vector2(21,12),new Vector2(16,12));return;
  }
  bool reward=Kind==DrawingKind.Reward;
  Poly(vh,new Color(13/255f,21/255f,33/255f,reward?.70f:.86f),new Vector2(12,4),new Vector2(w-6,4),new Vector2(w-29,h-6),new Vector2(0,h-6));
  Line(vh,new Vector2(12,4),new Vector2(w-6,4),1.2f,new Color(Silver.r,Silver.g,Silver.b,.8f));
  Line(vh,new Vector2(w-6,4),new Vector2(w-29,h-6),1.5f,Silver);
  Line(vh,new Vector2(9,h-6),new Vector2(w-36,h-6),1,new Color(Silver.r,Silver.g,Silver.b,.46f));
  Line(vh,new Vector2(reward?w*.55f:35,9),new Vector2(w-21,9),.7f,new Color(Silver.r,Silver.g,Silver.b,.25f));
  Line(vh,new Vector2(w-10,15),new Vector2(w-32,h-1),1,new Color(Silver.r,Silver.g,Silver.b,.55f));
  if(!reward){Line(vh,new Vector2(12,0),new Vector2(99,0),1,new Color(Silver.r,Silver.g,Silver.b,.55f));}
  for(int i=0;i<3;i++){float x=w-82+i*12;Poly(vh,new Color(Silver.r,Silver.g,Silver.b,.42f),new Vector2(x,h-4),new Vector2(x+7,h-4),new Vector2(x+3,h),new Vector2(x-4,h));}
  Poly(vh,Red,new Vector2(w-11,1),new Vector2(w-4,1),new Vector2(w-10,8),new Vector2(w-17,8));
 }
 void Line(UnityEngine.UI.VertexHelper vh,Vector2 a,Vector2 b,float width,Color c){var n=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;Poly(vh,c,a+n,b+n,b-n,a-n);}
 void Poly(UnityEngine.UI.VertexHelper vh,Color c,params Vector2[] points){int start=vh.currentVertCount;var r=rectTransform.rect;foreach(var p in points)vh.AddVert(new Vector3(r.xMin+p.x,r.yMax-p.y),c,Vector2.zero);for(int i=1;i<points.Length-1;i++)vh.AddTriangle(start,start+i,start+i+1);}
 protected override void Awake(){base.Awake();raycastTarget=false;}
}
}
