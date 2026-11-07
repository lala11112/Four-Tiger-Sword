using UnityEngine;
namespace BoundaryZeroUI {
public enum InterfaceSymbol { Health, Attack, Coins, LegacyCurrency, Upgrade }
/// <summary>Semantic interface marks, independent of the five elemental glyphs.</summary>
public static class InterfaceSymbols {
 public static RectTransform Draw(Transform parent,InterfaceSymbol kind,float x,float y,float size){
  var r=UIFactory.Node(parent,"Symbol_"+kind,x,y,size,size);
  var graphic=r.gameObject.AddComponent<InterfaceSymbolGraphic>();graphic.Kind=kind;graphic.raycastTarget=false;return r;
 }
}
public sealed class InterfaceSymbolGraphic : UnityEngine.UI.MaskableGraphic {
 public InterfaceSymbol Kind;
 static readonly Color Silver=new Color32(232,238,245,255), Steel=new Color32(112,141,170,255), Red=new Color32(231,43,70,255), Ink=new Color32(13,21,33,255);
 UnityEngine.UI.VertexHelper mesh;
 Vector2 P(float x,float y){var r=rectTransform.rect;return new Vector2(r.xMin+x*r.width,r.yMax-y*r.height);}
 void Tri(Color c,Vector2 a,Vector2 b,Vector2 d){int n=mesh.currentVertCount;mesh.AddVert(P(a.x,a.y),c,Vector2.zero);mesh.AddVert(P(b.x,b.y),c,Vector2.zero);mesh.AddVert(P(d.x,d.y),c,Vector2.zero);mesh.AddTriangle(n,n+1,n+2);}
 void Poly(Color c,params float[] xy){for(int i=2;i<xy.Length/2;i++)Tri(c,new Vector2(xy[0],xy[1]),new Vector2(xy[(i-1)*2],xy[(i-1)*2+1]),new Vector2(xy[i*2],xy[i*2+1]));}
 void Stroke(Color c,float width,params float[] xy){for(int i=0;i<xy.Length-2;i+=2){Vector2 a=new Vector2(xy[i],xy[i+1]),b=new Vector2(xy[i+2],xy[i+3]),v=(b-a).normalized;Vector2 n=new Vector2(-v.y,v.x)*width*.5f;Tri(c,a+n,b+n,b-n);Tri(c,a+n,b-n,a-n);}}
 void Chip(float cx,float cy,float radius,Color edge){
  float[] hex=new float[14];for(int i=0;i<=6;i++){float a=(30+i*60)*Mathf.Deg2Rad;hex[i*2]=cx+Mathf.Cos(a)*radius;hex[i*2+1]=cy+Mathf.Sin(a)*radius;}
  Stroke(edge,.022f,hex);Stroke(Steel,.012f,cx-radius*.67f,cy+radius*.35f,cx,cy+radius*.72f,cx+radius*.67f,cy+radius*.35f);
 }
 protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh){vh.Clear();mesh=vh;
  switch(Kind){
   case InterfaceSymbol.Health:
    // Armored vital core: shield silhouette with a faceted heart inset.
    Poly(Ink,.18f,.20f,.5f,.11f,.82f,.20f,.77f,.63f,.5f,.89f,.23f,.63f);
    Stroke(Silver,.025f,.18f,.20f,.5f,.11f,.82f,.20f,.77f,.63f,.5f,.89f,.23f,.63f,.18f,.20f);
    Stroke(Steel,.012f,.25f,.25f,.5f,.18f,.75f,.25f,.70f,.59f,.5f,.79f,.30f,.59f,.25f,.25f);
    Poly(Silver,.5f,.39f,.61f,.29f,.70f,.34f,.68f,.47f,.5f,.66f);
    Poly(Steel,.5f,.39f,.39f,.29f,.30f,.34f,.32f,.47f,.5f,.66f);
    Poly(Red,.49f,.26f,.54f,.26f,.54f,.51f,.49f,.57f);
    break;
   case InterfaceSymbol.Attack:
    // Long single-edged blade, guard, and wrapped grip.
    Poly(Silver,.78f,.08f,.72f,.35f,.41f,.66f,.33f,.59f,.64f,.24f);
    Poly(Steel,.78f,.08f,.64f,.24f,.33f,.59f,.37f,.61f,.69f,.29f);
    Stroke(Ink,.012f,.73f,.17f,.38f,.60f);
    Poly(Red,.24f,.53f,.49f,.73f,.46f,.77f,.21f,.57f);
    Poly(Silver,.30f,.64f,.36f,.69f,.22f,.87f,.16f,.82f);
    Stroke(Ink,.017f,.25f,.70f,.31f,.74f,.22f,.76f,.28f,.80f);
    Poly(Steel,.14f,.80f,.23f,.87f,.19f,.93f,.10f,.86f);
    break;
   case InterfaceSymbol.Coins:
    // Stacked hexagonal minted chips, with a crimson mint mark.
    Chip(.39f,.62f,.25f,Steel);Chip(.61f,.51f,.25f,Steel);
    Poly(Ink,.27f,.25f,.5f,.12f,.73f,.25f,.73f,.51f,.5f,.64f,.27f,.51f);
    Chip(.5f,.38f,.265f,Silver);
    Poly(Silver,.5f,.22f,.60f,.28f,.60f,.46f,.5f,.52f,.40f,.46f,.40f,.28f);
    Poly(Ink,.5f,.28f,.55f,.31f,.55f,.43f,.5f,.46f,.45f,.43f,.45f,.31f);
    Stroke(Red,.026f,.5f,.32f,.5f,.42f);
    break;
   case InterfaceSymbol.LegacyCurrency:
    // Open faceted relic ring with a suspended red keystone.
    Stroke(Silver,.055f,.34f,.18f,.17f,.36f,.17f,.63f,.36f,.83f,.64f,.83f,.83f,.63f,.83f,.36f,.66f,.18f);
    Stroke(Steel,.018f,.34f,.28f,.27f,.39f,.27f,.59f,.40f,.72f,.60f,.72f,.73f,.59f,.73f,.39f,.66f,.28f);
    Poly(Silver,.5f,.08f,.61f,.23f,.5f,.39f,.39f,.23f);
    Poly(Red,.5f,.12f,.56f,.23f,.5f,.33f,.46f,.23f);
    Stroke(Red,.022f,.5f,.43f,.5f,.62f);Poly(Silver,.5f,.65f,.55f,.70f,.5f,.75f,.45f,.70f);
    break;
   case InterfaceSymbol.Upgrade:
    Stroke(Silver,.105f,.25f,.48f,.5f,.23f,.75f,.48f);
    Poly(Silver,.45f,.35f,.55f,.35f,.55f,.74f,.45f,.74f);
    Stroke(Steel,.018f,.5f,.22f,.5f,.62f);Stroke(Red,.035f,.27f,.78f,.5f,.88f,.73f,.78f);
    break;
  }
  mesh=null;
 }
}
}


