using UnityEngine;
using UnityEngine.UI;
namespace BoundaryZeroUI {
/// <summary>Distinct, editable outcome objects, drawn in a 1000 x 720 design space.</summary>
public static class OutcomeSymbols {
 public static RectTransform Draw(Transform p,bool death,float x,float y,float w,float h){
  var r=UIFactory.Node(p,death?"BrokenBoundarySeal":"OpenedBoundaryGate",x,y,w,h);
  var art=r.gameObject.AddComponent<OutcomeSymbolGraphic>();art.Death=death;art.raycastTarget=false;return r;
 }
}
public sealed class OutcomeSymbolGraphic:UnityEngine.UI.MaskableGraphic {
 public bool Death;
 VertexHelper m;
 static readonly Color Silver=new Color32(220,232,241,255),Light=new Color32(248,251,255,255),Steel=new Color32(92,119,144,255),Dark=new Color32(24,42,61,255),Ink=new Color32(9,18,30,255),Red=new Color32(225,47,70,255),Wine=new Color32(90,21,42,255);
 Vector2 V(float x,float y){return new Vector2(x,y);}
 Vector2 P(Vector2 a){Rect r=rectTransform.rect;return new Vector2(r.xMin+a.x*r.width/1000,r.yMax-a.y*r.height/720);}
 void Poly(Color a,Color b,params Vector2[] v){int n=m.currentVertCount;for(int i=0;i<v.Length;i++)m.AddVert(P(v[i]),Color.Lerp(a,b,Mathf.Clamp01(v[i].y/720)),Vector2.zero);var ids=new System.Collections.Generic.List<int>();float area=0;for(int i=0;i<v.Length;i++){ids.Add(i);Vector2 a0=v[i],b0=v[(i+1)%v.Length];area+=a0.x*b0.y-b0.x*a0.y;}float sign=area>=0?1:-1;int guard=0;while(ids.Count>2&&guard++<v.Length*v.Length){bool cut=false;for(int k=0;k<ids.Count;k++){int ia=ids[(k+ids.Count-1)%ids.Count],ib=ids[k],ic=ids[(k+1)%ids.Count];Vector2 u=v[ia],z=v[ib],w=v[ic];if(Cross(z-u,w-z)*sign<=.001f)continue;bool inside=false;foreach(int q in ids){if(q==ia||q==ib||q==ic)continue;Vector2 t=v[q];if(Cross(z-u,t-u)*sign>=0&&Cross(w-z,t-z)*sign>=0&&Cross(u-w,t-w)*sign>=0){inside=true;break;}}if(inside)continue;m.AddTriangle(n+ia,n+ib,n+ic);ids.RemoveAt(k);cut=true;break;}if(!cut)break;}}
 static float Cross(Vector2 a,Vector2 b){return a.x*b.y-a.y*b.x;}
 void Line(float x,float y,float xx,float yy,float width,Color c){Vector2 a=V(x,y),b=V(xx,yy),d=(b-a).normalized,n=V(-d.y,d.x)*width*.5f;Poly(c,c,a-n,b-n,b+n,a+n);}
 void Box(float x,float y,float w,float h,Color a,Color b){Poly(a,b,V(x,y),V(x+w,y),V(x+w,y+h),V(x,y+h));}
 void Arc(float x,float y,float rx,float ry,float start,float end,float width,Color c){for(int i=0;i<48;i++){float a=Mathf.Lerp(start,end,i/48f)*Mathf.Deg2Rad,b=Mathf.Lerp(start,end,(i+1)/48f)*Mathf.Deg2Rad;Line(x+Mathf.Cos(a)*rx,y+Mathf.Sin(a)*ry,x+Mathf.Cos(b)*rx,y+Mathf.Sin(b)*ry,width,c);}}
 protected override void Awake(){base.Awake();raycastTarget=false;}
 protected override void OnPopulateMesh(VertexHelper vh){m=vh;m.Clear();if(Death)Seal();else Gate();}
 void Gate(){
  // Low, receding foundation and a restrained threshold light establish depth.
  Poly(Dark,Ink,V(170,596),V(730,596),V(826,657),V(88,657));
  Poly(Steel,Dark,V(170,596),V(730,596),V(749,609),V(150,609));
  Line(154,613,747,613,2,Silver);Line(256,645,652,645,2,new Color(.7f,.8f,.9f,.25f));
  // The central opening stays empty; these are two separate stone/metal uprights.
  Pier(210,false);Pier(686,true);
  // Wide double lintel with upturned, chamfered roof tips.
  Poly(Steel,Dark,V(116,182),V(794,182),V(831,151),V(804,221),V(130,221),V(94,151));
  Poly(Light,Steel,V(94,151),V(130,170),V(794,170),V(831,151),V(808,190),V(119,190));
  Line(139,194,786,194,2,Light);
  Poly(Silver,Steel,V(159,239),V(765,239),V(741,267),V(183,267));
  Poly(Dark,Ink,V(183,267),V(741,267),V(741,280),V(183,280));
  Box(300,244,320,3,Red,Wine);Box(436,250,50,9,Dark,Ink);
  // Slender inner door traces break upward: the boundary has opened.
  Line(318,300,318,531,2,new Color(.6f,.77f,.9f,.3f));Line(610,300,610,531,2,new Color(.6f,.77f,.9f,.3f));
  Line(328,576,597,576,3,Silver);Line(340,581,584,581,2,Red);
  for(int i=0;i<4;i++){float y=513-i*56,x=445+(i%2)*19;Poly(new Color(.85f,.94f,1,.78f-i*.12f),new Color(.4f,.65f,.85f,.25f),V(x,y),V(x+37,y-6),V(x+37,y-1),V(x,y+5));}
  Arc(465,586,235,56,12,168,1,new Color(.65f,.78f,.88f,.24f));
 }
 void Pier(float x,bool right){
  Poly(Steel,Dark,V(x,213),V(x+73,213),V(x+73,570),V(x+89,595),V(x-14,595),V(x,570));
  Poly(Light,Steel,V(x,213),V(x+13,224),V(x+13,570),V(x-1,584),V(x-14,595),V(x,570));
  Poly(Dark,Ink,V(x+60,224),V(x+73,213),V(x+73,570),V(x+89,595),V(x+60,580));
  Box(x+17,279,39,226,Dark,Ink);Box(x+22,289,3,205,Red,Wine);
  Line(x+32,314,x+49,314,2,Steel);Line(x+32,325,x+44,325,2,Silver);Line(x+42,325,x+42,357,2,Steel);Line(x+31,368,x+49,368,2,Steel);
  Box(x+16,243,42,12,Silver,Steel);Box(x+16,530,42,10,Silver,Steel);
  Line(x+16,555,x+58,555,1,Light);
 }
 void Seal(){
  // Broken suspension cord, with clearly separated loose ends.
  Arc(485,140,63,65,190,505,5,Wine);Arc(483,137,59,62,190,500,2,Red);
  Line(424,140,440,202,4,Red);Line(540,148,532,173,4,Red);Line(534,187,529,207,3,Red);
  // Upper plaque: irregular shoulders, inset ceramic and thick bevel facets.
  Poly(Steel,Dark,V(402,203),V(549,194),V(595,240),V(586,385),V(548,409),V(514,392),V(481,431),V(441,414),V(389,453),V(376,249));
  Poly(Light,Steel,V(402,203),V(549,194),V(540,211),V(411,219),V(393,255),V(389,430),V(376,249));
  Poly(Dark,Ink,V(549,194),V(595,240),V(586,385),V(568,396),V(575,248),V(540,211));
  Poly(Silver,Steel,V(411,219),V(540,211),V(575,248),V(568,390),V(547,397),V(513,380),V(479,418),V(440,401),V(402,431),V(393,255));
  // Detached lower shard follows the same crack, offset visibly down and right.
  Poly(Steel,Dark,V(421,474),V(461,447),V(500,463),V(535,423),V(569,440),V(607,415),V(592,573),V(545,621),V(446,605));
  Poly(Silver,Steel,V(432,481),V(462,460),V(502,478),V(537,438),V(570,454),V(591,440),V(577,565),V(540,604),V(456,590));
  Poly(Dark,Ink,V(591,440),V(607,415),V(592,573),V(545,621),V(540,604),V(577,565));
  Line(403,433,441,405,3,Light);Line(442,405,480,422,2,Red);Line(482,422,514,384,2,Red);Line(516,385,547,402,2,Light);
  Line(432,479,462,458,2,Red);Line(462,458,501,476,2,Red);Line(502,476,537,436,2,Light);
  // Abstract engraved seal strokes are inset into the ceramic, not legible text.
  Color rune=new Color32(52,71,89,255);
  Line(429,266,540,260,5,rune);Line(451,248,454,288,4,rune);Line(514,244,516,286,4,rune);
  Line(433,299,532,294,4,Red);Line(482,278,485,373,6,rune);
  Line(435,317,437,359,5,rune);Line(435,358,465,356,5,rune);Line(532,313,534,353,5,rune);Line(505,354,534,353,5,rune);
  Line(454,335,512,331,3,rune);Line(466,377,501,375,4,Red);
  Line(475,510,548,506,5,rune);Line(509,489,512,562,5,rune);Line(478,543,547,540,4,rune);Line(482,565,534,562,3,Red);
  Arc(482,248,13,8,0,360,2,Dark);
  Poly(Silver,Dark,V(392,507),V(409,495),V(407,531),V(395,543));
  Poly(Silver,Dark,V(594,611),V(616,596),V(610,628));
  Poly(Red,Wine,V(449,645),V(462,633),V(470,655),V(456,663));
  Arc(494,648,186,27,8,171,1,new Color(.6f,.74f,.86f,.18f));
 }
}
}

