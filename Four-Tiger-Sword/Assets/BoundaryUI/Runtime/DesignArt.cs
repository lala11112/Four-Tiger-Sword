using UnityEngine;
using UnityEngine.UI;

namespace BoundaryZeroUI {
/// <summary>Resolution independent, stencil-aware visual language for Boundary Zero.</summary>
public static class DesignArt {
 static RectTransform Make(Transform p,string name,float x,float y,float w,float h,int kind,Color a,Color b,bool flag=false) {
  var r=UIFactory.Node(p,name,x,y,w,h);var g=r.gameObject.AddComponent<BoundaryDesignGraphic>();
  g.Kind=kind;g.A=a;g.B=b;g.Flag=flag;g.raycastTarget=false;return r;
 }
 public static RectTransform Surface(Transform p,float x,float y,float w,float h,Color top,Color bottom,bool cut=true) {
  bool bright=UIFactory.IsBright(p);
  if(bright&&top.a>=.3f&&top.r+top.g+top.b<1.5f){
   bool active=top.r>top.g*1.5f&&top.r>.15f;
   float ta=top.a>.98f?.73f:top.a,ba=bottom.a>.98f?.58f:bottom.a;
   top=active?new Color(.20f,.065f,.105f,ta):new Color(.071f,.129f,.20f,ta);
   bottom=active?new Color(.095f,.035f,.06f,ba):new Color(.031f,.067f,.118f,ba);
  }
  var r=Make(p,"BrushedGlass",x,y,w,h,0,top,bottom,cut);r.GetComponent<BoundaryDesignGraphic>().BrightGlass=bright;return r;
 }
 public static RectTransform Halo(Transform p,float x,float y,float size,Color accent) {return Make(p,"InterruptedEnergyOrbit",x,y,size,size,1,accent,UIFactory.Ivory);}
 public static RectTransform Hero(Transform p,float x,float y,float w,float h,bool broken=false) {return Make(p,broken?"FracturedBladeSeal":"CeremonialBladeSeal",x,y,w,h,2,UIFactory.Red,UIFactory.Ivory,broken);}
 public static RectTransform Backdrop(Transform p,string mood) {var r=Make(p,"Atmosphere_"+mood,0,0,2560,1440,3,mood=="death"?new Color(.09f,.035f,.045f):new Color(.063f,.114f,.145f),new Color(.020f,.043f,.063f),mood=="death");r.GetComponent<BoundaryDesignGraphic>().ClearMood=mood=="clear";return r;}
 public static RectTransform Outcome(Transform p,float x,float y,float size) {return Make(p,"CompleteFivePhaseSeal",x,y,size,size,5,UIFactory.Red,UIFactory.Ivory);}
 public static RectTransform Traces(Transform p,float x,float y,float w,float h,Color color) {return Make(p,"ConductiveLattice",x,y,w,h,4,color,UIFactory.Ivory);}
}

/// <summary>Uses the stock UI material, including masking, CanvasGroup alpha and tint.</summary>
public sealed class BoundaryDesignGraphic : MaskableGraphic {
 public int Kind;public Color A=Color.white,B=Color.white;public bool Flag,ClearMood,BrightGlass;
 VertexHelper mesh;float W,H;
 static Color Alpha(Color c,float a){c.a*=a;return c;}
 static Color C(float r,float g,float b,float a=1){return new Color(r,g,b,a);}
 Vector2 P(float x,float y){return new Vector2(rectTransform.rect.xMin+x,rectTransform.rect.yMax-y);}
 void V(Vector2 p,Color c){mesh.AddVert(P(p.x,p.y),c*color,Vector2.zero);}
 void Tri(Vector2 a,Vector2 b,Vector2 c,Color ca,Color cb,Color cc){int n=mesh.currentVertCount;V(a,ca);V(b,cb);V(c,cc);mesh.AddTriangle(n,n+1,n+2);}
 void Poly(Color c,params Vector2[] p){for(int i=1;i<p.Length-1;i++)Tri(p[0],p[i],p[i+1],c,c,c);}
 void Quad(float x,float y,float w,float h,Color top,Color bottom){int n=mesh.currentVertCount;V(new Vector2(x,y),top);V(new Vector2(x+w,y),top);V(new Vector2(x+w,y+h),bottom);V(new Vector2(x,y+h),bottom);mesh.AddTriangle(n,n+1,n+2);mesh.AddTriangle(n,n+2,n+3);}
 void Line(Vector2 a,Vector2 b,float width,Color c){Vector2 n=(b-a).normalized;n=new Vector2(-n.y,n.x)*width*.5f;Poly(c,a+n,b+n,b-n,a-n);}
 void Stroke(float width,Color c,params Vector2[] p){for(int i=1;i<p.Length;i++)Line(p[i-1],p[i],width,c);}
 void Arc(Vector2 center,float rx,float ry,float start,float end,float width,Color c,int steps=80){Vector2 last=Vector2.zero;int count=Mathf.Max(4,Mathf.CeilToInt(steps*(end-start)/360));for(int i=0;i<=count;i++){float rad=Mathf.Lerp(start,end,(float)i/count)*Mathf.Deg2Rad;Vector2 q=center+new Vector2(Mathf.Cos(rad)*rx,Mathf.Sin(rad)*ry);if(i>0)Line(last,q,width,c);last=q;}}
 void Glow(Vector2 center,float rx,float ry,Color c){const int rings=20,steps=64;for(int j=0;j<rings;j++){float r0=(float)j/rings,r1=(float)(j+1)/rings;Color c0=Alpha(c,Mathf.Pow(1-r0,2)),c1=Alpha(c,Mathf.Pow(1-r1,2));for(int i=0;i<steps;i++){float a=i*2*Mathf.PI/steps,b=(i+1)*2*Mathf.PI/steps;Vector2 p0=center+new Vector2(Mathf.Cos(a)*rx*r0,Mathf.Sin(a)*ry*r0),p1=center+new Vector2(Mathf.Cos(b)*rx*r0,Mathf.Sin(b)*ry*r0),p2=center+new Vector2(Mathf.Cos(b)*rx*r1,Mathf.Sin(b)*ry*r1),p3=center+new Vector2(Mathf.Cos(a)*rx*r1,Mathf.Sin(a)*ry*r1);Tri(p0,p1,p2,c0,c0,c1);Tri(p0,p2,p3,c0,c1,c1);}}}
 void Diamond(Vector2 p,float s,Color c){Poly(c,p+new Vector2(0,-s),p+new Vector2(s,0),p+new Vector2(0,s),p+new Vector2(-s,0));}
 protected override void OnPopulateMesh(VertexHelper vh){vh.Clear();mesh=vh;W=rectTransform.rect.width;H=rectTransform.rect.height;if(W<=0||H<=0)return;switch(Kind){case 0:Glass();break;case 1:Orbit();break;case 2:Blade();break;case 3:Atmosphere();break;case 4:Lattice();break;case 5:CompletedSeal();break;}}
 void Glass(){
  float cut=Flag?Mathf.Min(19,H*.17f):0;Color middle=Color.Lerp(A,B,.5f);
  Tri(new Vector2(cut,0),new Vector2(W,0),new Vector2(W,H*.5f),A,A,middle);
  Tri(new Vector2(cut,0),new Vector2(W,H*.5f),new Vector2(0,H*.5f),A,middle,middle);
  Tri(new Vector2(cut,0),new Vector2(0,H*.5f),new Vector2(0,cut),A,middle,A);
  Tri(new Vector2(0,H*.5f),new Vector2(W,H*.5f),new Vector2(W-cut,H),middle,middle,B);
  Tri(new Vector2(0,H*.5f),new Vector2(W-cut,H),new Vector2(0,H),middle,B,B);
  Tri(new Vector2(W,H*.5f),new Vector2(W,H-cut),new Vector2(W-cut,H),middle,B,B);
  if(A.a<.3f)return;
  Stroke(1,Alpha(UIFactory.Ivory,.39f),new Vector2(0,H*.45f),new Vector2(0,cut),new Vector2(cut,0),new Vector2(W,0));
  Stroke(1,Alpha(UIFactory.Ivory,.24f),new Vector2(W,0),new Vector2(W,H-cut),new Vector2(W-cut,H),new Vector2(0,H));
  if(W>250){Line(new Vector2(cut+4,4),new Vector2(cut+48,4),2,Alpha(UIFactory.Ivory,.65f));Stroke(1.5f,Alpha(UIFactory.Ivory,.7f),new Vector2(W-39,H-4),new Vector2(W-cut-3,H-4),new Vector2(W-4,H-cut-3));}
 }
 void Orbit(){
  Vector2 c=new Vector2(W*.5f,H*.5f);float r=Mathf.Min(W,H)*.42f;
  Glow(c,r*1.17f,r*1.17f,Alpha(A,.11f));
  Arc(c,r,r,16,143,1,Alpha(B,.42f));Arc(c,r,r,190,306,1,Alpha(B,.31f));
  Arc(c,r*.94f,r*.94f,212,302,4,Alpha(A,.13f));Arc(c,r*.94f,r*.94f,214,276,1.5f,Alpha(A,.9f));
  Arc(c,r*1.05f,r*1.05f,32,79,3,Alpha(B,.45f));Arc(c,r*.72f,r*.72f,6,118,.7f,Alpha(A,.25f));
  for(int i=0;i<64;i++){float a=i*360f/64;if((i>12&&i<20)||(i>37&&i<46))continue;float v=a*Mathf.Deg2Rad;Vector2 d=new Vector2(Mathf.Cos(v),Mathf.Sin(v));Line(c+d*r*1.1f,c+d*r*(i%4==0?1.13f:1.115f),1,Alpha(B,i%4==0?.54f:.18f));}
  Diamond(c+new Vector2(-r,0),4,Alpha(A,.9f));Diamond(c+new Vector2(r*.707f,-r*.707f),4,C(.98f,.29f,.22f,.9f));
 }
 void Atmosphere(){
  Quad(0,0,W,H,A,B);
  if(ClearMood){Glow(new Vector2(W*.29f,H*.40f),W*.58f,H*.85f,C(.078f,.749f,.682f,.40f));Glow(new Vector2(W*.26f,H*.33f),W*.24f,H*.48f,C(.96f,.99f,1,.32f));}
  Glow(new Vector2(W*.67f,H*.39f),W*.48f,H*.8f,Flag?C(.851f,.259f,.220f,.34f):C(.078f,.749f,.682f,.16f));
  Glow(new Vector2(W*.78f,H*.35f),W*.22f,H*.55f,C(.94f,.98f,1,.08f));
  Poly(C(.06f,.075f,.10f,.20f),new Vector2(0,0),new Vector2(W*.29f,0),new Vector2(W*.47f,H),new Vector2(0,H));
  Poly(C(.078f,.749f,.682f,.035f),new Vector2(W*.36f,0),new Vector2(W*.42f,0),new Vector2(W*.71f,H),new Vector2(W*.65f,H));
  Poly(C(.96f,.99f,1,.025f),new Vector2(W*.74f,0),new Vector2(W*.78f,0),new Vector2(W*.42f,H),new Vector2(W*.38f,H));
  Vector2 focus=new Vector2(W*.72f,H*.43f);
  Arc(focus,H*.57f,H*.57f,124,274,1,C(.94f,.98f,1,.22f));Arc(focus,H*.585f,H*.585f,191,269,3,C(.078f,.749f,.682f,.10f));
  Arc(focus,H*.47f,H*.47f,-75,52,1,C(.32f,.86f,.88f,.13f));
  for(int i=0;i<23;i++){float x=W*.48f+i*43;float y=H*.07f+(i%4)*8;Line(new Vector2(x,y),new Vector2(x+16,y),1,C(.94f,.98f,1,.23f));}
  for(int i=0;i<40;i++){float x=(i*193+71)%2560,y=(i*331+117)%1440;Diamond(new Vector2(x,y),i%7==0?2:1,C(.62f,.89f,.86f,i%7==0?.35f:.13f));}
  Stroke(1,C(.94f,.98f,1,.24f),new Vector2(W*.59f,0),new Vector2(W*.59f,H*.12f),new Vector2(W*.61f,H*.15f),new Vector2(W*.84f,H*.15f));
  Quad(0,H*.78f,W,H*.22f,C(.07f,.09f,.12f,0),C(.07f,.09f,.12f,.45f));
 }
 void Lattice(){
  Color faint=Alpha(UIFactory.Ivory,.055f),bright=Alpha(UIFactory.Ivory,.14f);
  for(int i=0;i<3;i++){float x=W*(.25f+i*.2f);Stroke(1,faint,new Vector2(x,H*.11f),new Vector2(x+H*.24f,H*.35f),new Vector2(x+H*.24f,H*.61f));}
  Stroke(1,bright,new Vector2(0,H*.82f),new Vector2(W*.12f,H*.82f),new Vector2(W*.12f+18,H*.82f-18),new Vector2(W*.25f,H*.82f-18));
  for(int i=0;i<3;i++){float x=W-65+i*15;Poly(Alpha(UIFactory.Red,.28f),new Vector2(x,H-9),new Vector2(x+6,H-9),new Vector2(x+14,H-1),new Vector2(x+8,H-1));}
 }
 Vector2 BladeP(float x,float y){return new Vector2(W*(.46f+x*.095f+(y-.5f)*.23f),H*(.09f+y*.84f));}
 void Facet(Color c,params Vector2[] p){Vector2[] q=new Vector2[p.Length];for(int i=0;i<p.Length;i++)q[i]=BladeP(p[i].x,p[i].y);Poly(c,q);}
 void BladeLine(float x,float y,float xx,float yy,float width,Color c){Line(BladeP(x,y),BladeP(xx,yy),width,c);}
 float BladeWidth(float y){return y<.78f?Mathf.Lerp(.69f,.35f,(y-.30f)/.48f):Mathf.Lerp(.35f,0,(y-.78f)/.20f);}
 Color Metal(float x,float y){
  float light=Mathf.Exp(-Mathf.Pow((y-.435f)/.078f,2))*.65f+Mathf.Exp(-Mathf.Pow((y-.735f)/.12f,2))*.22f;
  Color baseTone=x<0?Color.Lerp(C(.43f,.48f,.53f),C(.86f,.91f,.94f),light):Color.Lerp(C(.16f,.23f,.30f),C(.48f,.64f,.69f),light);
  float bevel=Mathf.Exp(-Mathf.Pow((x+.91f)/.065f,2))*.85f;
  float ridge=Mathf.Exp(-Mathf.Pow((x+.025f)/.055f,2))*.8f;
  float rim=Mathf.Exp(-Mathf.Pow((x-.95f)/.045f,2))*.65f;
  baseTone=Color.Lerp(baseTone,C(.97f,.99f,1),Mathf.Max(bevel,ridge));
  return Color.Lerp(baseTone,C(.46f,.94f,.95f),rim*(.45f+light*.55f));
 }
 void PolishedBlade(){
  float[] xs={-1,-.94f,-.89f,-.82f,-.59f,-.24f,-.075f,-.025f,.025f,.10f,.43f,.85f,.94f,1};
  float[] ys={.30f,.35f,.39f,.415f,.435f,.46f,.50f,.58f,.68f,.74f,.78f,.84f,.91f,.98f};
  for(int j=0;j<ys.Length-1;j++)for(int i=0;i<xs.Length-1;i++){
   float x0=xs[i],x1=xs[i+1],y0=ys[j],y1=ys[j+1],w0=BladeWidth(y0),w1=BladeWidth(y1);
   Vector2 p0=BladeP(x0*w0,y0),p1=BladeP(x1*w0,y0),p2=BladeP(x1*w1,y1),p3=BladeP(x0*w1,y1);
   Tri(p0,p1,p2,Metal(x0,y0),Metal(x1,y0),Metal(x1,y1));Tri(p0,p2,p3,Metal(x0,y0),Metal(x1,y1),Metal(x0,y1));
  }
  // Inlaid channels are machined into the surface, with a shadow and a one-sided light lip.
  for(int side=-1;side<=1;side+=2){
   float f=side<0?-.53f:.67f;
   BladeLine(f*BladeWidth(.34f),.34f,f*BladeWidth(.76f),.76f,2.1f,C(.025f,.10f,.13f,.74f));
   BladeLine(f*BladeWidth(.34f)+.017f,.34f,f*BladeWidth(.76f)+.017f,.76f,.65f,C(.73f,.90f,.84f,.62f));
  }
  for(int i=0;i<86;i++){
   float y=.335f+i*.005f;float width=BladeWidth(y);float x=-width*.77f+((i*17)%29)/29f*width*.52f;
   BladeLine(x,y,x+width*.19f,y+.0006f,.45f,C(.85f,.96f,.9f,i%5==0?.14f:.055f));
  }
  // Tapered reflection, rather than a constant-width neon stripe.
  Tri(BladeP(-.67f,.34f),BladeP(-.60f,.39f),BladeP(-.25f,.77f),C(1,1,1,0),C(1,1,1,.7f),C(1,1,1,0));
  Glow(BladeP(-.58f,.43f),W*.055f,H*.07f,C(.80f,.98f,.93f,.22f));
 }
 void Rune(Vector2 center,float size,int index,Color ink){
  // Deliberately geometric seal script: paired gates, interrupted spine and bound strokes.
  float s=size;Stroke(1.3f,ink,center+new Vector2(-s*.35f,-s*.4f),center+new Vector2(s*.28f,-s*.4f),center+new Vector2(s*.28f,-s*.12f),center+new Vector2(-s*.18f,-s*.12f),center+new Vector2(-s*.18f,s*.32f));
  Line(center+new Vector2(0,-s*.58f),center+new Vector2(0,s*.55f),1.2f,ink);
  Line(center+new Vector2(-s*.4f,s*.18f),center+new Vector2(s*.4f,s*.18f),1.2f,ink);
  if(index%2==0)Line(center+new Vector2(s*.29f,s*.18f),center+new Vector2(s*.29f,s*.44f),1.2f,ink);
  else Line(center+new Vector2(-s*.39f,s*.42f),center+new Vector2(s*.1f,s*.42f),1.2f,ink);
  Diamond(center+new Vector2(s*.5f,-s*.44f),1.4f,ink);
 }
 void CompletedSeal(){
  Vector2 c=new Vector2(W*.5f,H*.5f);float s=Mathf.Min(W,H),r=s*.32f;
  Glow(c,s*.49f,s*.49f,C(.65f,.75f,.90f,.11f));Glow(c,s*.21f,s*.21f,C(.92f,.95f,1,.12f));
  Arc(c,r,r,0,360,.8f,Alpha(B,.46f));Arc(c,r*.91f,r*.91f,0,360,.7f,Alpha(A,.36f));
  Color[] phase={C(.97f,.35f,.25f),C(.72f,.79f,.83f),C(.93f,.97f,.98f),C(.35f,.87f,.94f),C(.51f,.85f,.66f)};
  for(int i=0;i<5;i++){
   float a=-90+i*72,rad=a*Mathf.Deg2Rad;Vector2 dir=new Vector2(Mathf.Cos(rad),Mathf.Sin(rad));
   Vector2 tangent=new Vector2(-dir.y,dir.x),q=c+dir*r*1.18f;
   Arc(c,r*1.08f,r*1.08f,a-22,a+22,3,Alpha(phase[i],.48f));
   Arc(c,r*.97f,r*.97f,a-28,a+28,1.5f,Alpha(phase[i],.92f));
   Poly(C(.07f,.20f,.23f,.94f),q-dir*22,q+tangent*22,q+dir*22,q-tangent*22);
   Stroke(1,Alpha(B,.66f),q-dir*23,q+tangent*23,q+dir*23,q-tangent*23,q-dir*23);
   Rune(q,25,i,phase[i]);
   Vector2 inner=c+dir*r*.42f,outer=c+dir*r*.80f;
   Line(inner,outer,.8f,Alpha(phase[i],.48f));Diamond(c+dir*r*.78f,3,phase[i]);
   for(int j=0;j<7;j++){float ang=(a-22+j*7.3f)*Mathf.Deg2Rad;Vector2 d=new Vector2(Mathf.Cos(ang),Mathf.Sin(ang));Line(c+d*r*1.31f,c+d*r*(j==3?1.36f:1.33f),1,Alpha(B,.5f));}
  }
  Vector2 top=c+new Vector2(0,-s*.19f),left=c+new Vector2(-s*.115f,0),right=c+new Vector2(s*.115f,0),bottom=c+new Vector2(0,s*.19f),spine=c+new Vector2(-s*.018f,-s*.018f);
  Tri(top,left,spine,C(.86f,1,.9f),C(.24f,.65f,.67f),C(.91f,1,.89f));
  Tri(top,spine,right,C(.86f,1,.9f),C(.91f,1,.89f),C(.08f,.35f,.41f));
  Tri(left,bottom,spine,C(.24f,.65f,.67f),C(.08f,.40f,.45f),C(.91f,1,.89f));
  Tri(spine,bottom,right,C(.91f,1,.89f),C(.08f,.40f,.45f),C(.04f,.19f,.25f));
  Stroke(1.2f,C(.80f,1,.91f,.95f),top,left,bottom);Stroke(1,Alpha(A,.8f),top,right,bottom);
  Line(top,spine,1.3f,C(1,1,.90f));Line(spine,bottom,.8f,C(.62f,1,.96f,.7f));
  Glow(c,s*.08f,s*.10f,C(.78f,1,.89f,.35f));
  for(int i=0;i<5;i++){float a=(i*72-54)*Mathf.Deg2Rad;Vector2 q=c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r*.66f;Diamond(q,2,C(.80f,.99f,.88f,.8f));}
 }
 void Blade(){
  Color ivory=C(.92f,.96f,.97f),steel=C(.48f,.57f,.64f),dark=C(.15f,.20f,.26f),cyan=Flag?C(.91f,.33f,.25f):A;
  Vector2 center=new Vector2(W*.52f,H*.51f);float radius=Mathf.Min(W*.43f,H*.37f);
  Glow(center,W*.49f,H*.49f,Alpha(cyan,.15f));
  Glow(new Vector2(W*.63f,H*.41f),W*.25f,H*.35f,C(.85f,.93f,.96f,.13f));
  Arc(center,radius,radius*.82f,14,142,1,Alpha(ivory,.40f));Arc(center,radius,radius*.82f,169,279,1,Alpha(ivory,.30f));
  Arc(center,radius*1.045f,radius*.855f,196,280,5,Alpha(cyan,.13f));Arc(center,radius*1.045f,radius*.855f,201,266,1.5f,Alpha(cyan,.8f));
  Arc(center,radius*.8f,radius*.30f,155,335,1,Alpha(cyan,.55f));
  for(int i=0;i<17;i++){float t=i/16f,a=(t*190+45)*Mathf.Deg2Rad;Vector2 d=new Vector2(Mathf.Cos(a),Mathf.Sin(a)*.82f);Line(center+d*radius*1.075f,center+d*radius*(i%4==0?1.11f:1.09f),1,Alpha(ivory,.42f));}
  // Cast silhouette and an ivory/cold-steel bevel, articulated along a single axis.
  Facet(C(.015f,.06f,.075f,.3f),new Vector2(-.85f,.32f),new Vector2(.2f,.12f),new Vector2(.96f,.34f),new Vector2(.59f,.80f),new Vector2(0,1.03f),new Vector2(-.5f,.79f));
  Facet(dark,new Vector2(-.78f,.29f),new Vector2(0,.08f),new Vector2(.78f,.29f),new Vector2(.44f,.79f),new Vector2(0,1),new Vector2(-.44f,.79f));
  Facet(ivory,new Vector2(-.69f,.30f),new Vector2(0,.10f),new Vector2(-.05f,.76f),new Vector2(0,.98f),new Vector2(-.35f,.78f));
  Facet(C(.27f,.43f,.46f),new Vector2(0,.10f),new Vector2(.69f,.30f),new Vector2(.35f,.78f),new Vector2(0,.98f),new Vector2(-.05f,.76f));
  Facet(C(.66f,.73f,.66f),new Vector2(-.55f,.31f),new Vector2(0,.17f),new Vector2(-.1f,.77f),new Vector2(-.26f,.75f));
  Facet(C(.14f,.32f,.36f),new Vector2(.10f,.21f),new Vector2(.53f,.32f),new Vector2(.26f,.74f),new Vector2(.03f,.85f));
  PolishedBlade();
  BladeLine(-.69f,.30f,-.35f,.78f,1.8f,C(.97f,1,1,.9f));BladeLine(-.35f,.78f,0,.98f,1.4f,Alpha(ivory,.9f));
  BladeLine(.69f,.30f,.35f,.78f,1.4f,Alpha(cyan,.9f));BladeLine(0,.13f,-.05f,.77f,1,C(.95f,1,.92f,.75f));
  // Seal housing: a dark jade hilt, enamel vermilion lock, nested metal shoulder plates.
  Facet(C(.04f,.12f,.15f),new Vector2(-.26f,.02f),new Vector2(.26f,.02f),new Vector2(.30f,.29f),new Vector2(-.30f,.29f));
  for(int i=0;i<6;i++){float y=.045f+i*.032f;BladeLine(-.25f,y,.26f,y+.018f,2,C(.68f,.75f,.80f,.65f));}
  Facet(C(.77f,.83f,.87f),new Vector2(-1.15f,.24f),new Vector2(-.30f,.20f),new Vector2(0,.28f),new Vector2(-.68f,.34f),new Vector2(-1.15f,.30f));
  Facet(C(.30f,.46f,.48f),new Vector2(1.15f,.24f),new Vector2(.30f,.20f),new Vector2(0,.28f),new Vector2(.68f,.34f),new Vector2(1.15f,.30f));
  Tri(BladeP(-1.15f,.24f),BladeP(-.30f,.20f),BladeP(-.68f,.34f),C(.23f,.34f,.35f),C(.95f,.98f,1),C(.43f,.56f,.53f));
  Tri(BladeP(-1.15f,.24f),BladeP(-.68f,.34f),BladeP(-1.15f,.30f),C(.23f,.34f,.35f),C(.43f,.56f,.53f),C(.11f,.24f,.27f));
  Tri(BladeP(1.15f,.24f),BladeP(.30f,.20f),BladeP(.68f,.34f),C(.08f,.22f,.27f),C(.68f,.80f,.72f),C(.18f,.40f,.44f));
  BladeLine(-1.15f,.24f,-.30f,.20f,2,ivory);BladeLine(.30f,.20f,1.15f,.24f,1.5f,Alpha(ivory,.9f));
  Facet(C(.69f,.17f,.12f),new Vector2(0,.19f),new Vector2(.28f,.25f),new Vector2(0,.33f),new Vector2(-.28f,.25f));
  Facet(C(1,.40f,.25f),new Vector2(0,.19f),new Vector2(.28f,.25f),new Vector2(0,.26f));
  Diamond(BladeP(0,.25f),Mathf.Min(W,H)*.009f,C(1,.93f,.86f));
  for(int i=0;i<5;i++)Rune(BladeP(.17f,.37f+i*.074f),Mathf.Min(W*.040f,H*.031f),i,Alpha(cyan,.85f));
  // An interrupted ribbon passes behind the seal and over the blade tip.
  Arc(new Vector2(W*.52f,H*.57f),radius*1.14f,radius*.28f,-8,152,3,Alpha(cyan,.13f));
  Arc(new Vector2(W*.52f,H*.57f),radius*1.14f,radius*.28f,19,119,1.3f,Alpha(cyan,.85f));
  Arc(new Vector2(W*.51f,H*.37f),radius*.92f,radius*.20f,190,346,1,Alpha(ivory,.44f));
  for(int i=0;i<5;i++){float xx=W*(.18f+i*.165f),yy=H*(.35f+(i%2)*.31f);Diamond(new Vector2(xx,yy),i==2?3:2,Alpha(cyan,.75f));Line(new Vector2(xx-7,yy),new Vector2(xx+7,yy),.7f,Alpha(cyan,.45f));}
  // A suspended paper-metal seal balances the blade without a character illustration.
  Vector2 seal=new Vector2(W*.78f,H*.68f);Line(BladeP(1.05f,.29f),seal+new Vector2(0,-28),.8f,C(.80f,.86f,.89f,.5f));
  Poly(C(.69f,.22f,.16f,.90f),seal+new Vector2(-16,-30),seal+new Vector2(16,-30),seal+new Vector2(16,28),seal+new Vector2(4,23),seal+new Vector2(-16,30));
  Rune(seal,19,3,C(.98f,.95f,.90f));
  if(Flag){
   Facet(C(.08f,.14f,.16f),new Vector2(-.62f,.58f),new Vector2(-.22f,.55f),new Vector2(.14f,.61f),new Vector2(.48f,.57f),new Vector2(.53f,.60f),new Vector2(.13f,.65f),new Vector2(-.24f,.59f),new Vector2(-.61f,.62f));
   BladeLine(-.6f,.58f,-.22f,.55f,2,C(1,.38f,.24f));BladeLine(-.22f,.55f,.14f,.61f,2,C(1,.53f,.35f));
   for(int i=0;i<7;i++){Vector2 q=BladeP((i%3-1)*.7f,.64f+i*.035f)+new Vector2(i%2==0?-W*.10f:W*.13f,0);Poly(Alpha(steel,.85f),q,q+new Vector2(11,-9),q+new Vector2(4,15));}
  }
 }
}
}
