using UnityEngine;
namespace BoundaryZeroUI {
// The same machined blade silhouette connects the old HUD and the desktop windows.
public sealed class LegacyWindowGraphic : UnityEngine.UI.MaskableGraphic {
 UnityEngine.UI.VertexHelper mesh; float w,h;
 static readonly Color Steel=new Color32(232,238,245,255),Red=new Color32(231,43,70,255);
 Vector2 P(float x,float y){return new Vector2(rectTransform.rect.xMin+x,rectTransform.rect.yMax-y);}
 void Poly(Color c,params Vector2[] p){int n=mesh.currentVertCount;foreach(var q in p)mesh.AddVert(P(q.x,q.y),c,Vector2.zero);for(int i=1;i<p.Length-1;i++)mesh.AddTriangle(n,n+i,n+i+1);}
 void Line(float x,float y,float xx,float yy,float width,Color c){var a=new Vector2(x,y);var b=new Vector2(xx,yy);var d=(b-a).normalized;var n=new Vector2(-d.y,d.x)*width*.5f;Poly(c,a+n,b+n,b-n,a-n);}
 void Stroke(float width,Color c,params Vector2[] p){for(int i=1;i<p.Length;i++)Line(p[i-1].x,p[i-1].y,p[i].x,p[i].y,width,c);}
 Color A(Color c,float a){c.a=a;return c;}
 protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh){
  vh.Clear();mesh=vh;w=rectTransform.rect.width;h=rectTransform.rect.height;
  Stroke(1.3f,A(Steel,.42f),new Vector2(24,114),new Vector2(24,66),new Vector2(66,24),new Vector2(w-114,24),new Vector2(w-70,44),new Vector2(w-24,44));
  Stroke(1,A(Steel,.25f),new Vector2(w-24,64),new Vector2(w-24,h-68),new Vector2(w-68,h-24),new Vector2(104,h-24),new Vector2(24,h-64),new Vector2(24,174));
  Stroke(3,Steel,new Vector2(26,106),new Vector2(26,66),new Vector2(66,26),new Vector2(207,26));
  Stroke(3,Steel,new Vector2(w-26,h-146),new Vector2(w-26,h-68),new Vector2(w-68,h-26),new Vector2(w-226,h-26));
  Poly(Steel,new Vector2(36,63),new Vector2(70,30),new Vector2(144,30),new Vector2(126,43),new Vector2(77,43),new Vector2(36,83));
  Poly(Red,new Vector2(211,24),new Vector2(326,24),new Vector2(310,31),new Vector2(204,31));
  Poly(Red,new Vector2(w-78,h-35),new Vector2(w-36,h-77),new Vector2(w-36,h-58),new Vector2(w-59,h-35));
  for(int i=0;i<3;i++)Poly(A(Steel,.7f-i*.17f),new Vector2(59+i*19,h-40),new Vector2(67+i*19,h-40),new Vector2(80+i*19,h-26),new Vector2(72+i*19,h-26));
  Line(40,190,40,h-112,1,A(Steel,.12f));Line(w-40,190,w-40,h-174,1,A(Steel,.12f));
  Line(w-160,25,w-120,25,2,Steel);
 }
}
}
