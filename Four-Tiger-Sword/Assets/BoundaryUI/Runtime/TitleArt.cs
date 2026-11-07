using UnityEngine;
using UnityEngine.UI;
namespace BoundaryZeroUI {
public static class TitleArt {
 public static RectTransform Background(Transform p,int variant=0){return LuminousBackdrop.Build(p,true);}
 public static RectTransform Wordmark(Transform p,float x,float y,float w,float h){var r=UIFactory.Node(p,"ZERO_CustomWordmark",x,y,w,h);r.gameObject.AddComponent<ZeroWordmarkGraphic>();return r;}
 public static RectTransform Blade(Transform p,float x,float y,float w,float h){var r=UIFactory.Node(p,"LegacyBladeIdentity",x,y,w,h);r.gameObject.AddComponent<LegacyBladeGraphic>();return r;}
}
public abstract class TitleMeshGraphic:UnityEngine.UI.MaskableGraphic {
 protected VertexHelper mesh;
 protected Vector2 P(float x,float y){var r=rectTransform.rect;return new Vector2(r.xMin+x/2560*r.width,r.yMax-y/1440*r.height);}
 protected void Quad(Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color ca,Color cb,Color cc,Color cd){int n=mesh.currentVertCount;mesh.AddVert(P(a.x,a.y),ca,Vector2.zero);mesh.AddVert(P(b.x,b.y),cb,Vector2.zero);mesh.AddVert(P(c.x,c.y),cc,Vector2.zero);mesh.AddVert(P(d.x,d.y),cd,Vector2.zero);mesh.AddTriangle(n,n+1,n+2);mesh.AddTriangle(n,n+2,n+3);}
 protected void Q(float x,float y,float w,float h,Color a,Color b){Quad(new Vector2(x,y),new Vector2(x+w,y),new Vector2(x+w,y+h),new Vector2(x,y+h),a,a,b,b);}
 protected void Poly(Color a,Color b,params Vector2[] p){
 int n=mesh.currentVertCount;for(int i=0;i<p.Length;i++)mesh.AddVert(P(p[i].x,p[i].y),Color.Lerp(a,b,p[i].y/1440),Vector2.zero);
 var ids=new System.Collections.Generic.List<int>();float area=0;for(int i=0;i<p.Length;i++){ids.Add(i);Vector2 u=p[i],v=p[(i+1)%p.Length];area+=u.x*v.y-v.x*u.y;}float sign=area>=0?1:-1;
 int guard=0;while(ids.Count>2&&guard++<p.Length*p.Length){bool cut=false;for(int k=0;k<ids.Count;k++){int ia=ids[(k+ids.Count-1)%ids.Count],ib=ids[k],ic=ids[(k+1)%ids.Count];Vector2 u=p[ia],v=p[ib],w=p[ic];if(Cross(v-u,w-v)*sign<=.001f)continue;bool inside=false;for(int j=0;j<ids.Count;j++){int q=ids[j];if(q==ia||q==ib||q==ic)continue;Vector2 z=p[q];if(Cross(v-u,z-u)*sign>=0&&Cross(w-v,z-v)*sign>=0&&Cross(u-w,z-w)*sign>=0){inside=true;break;}}if(inside)continue;mesh.AddTriangle(n+ia,n+ib,n+ic);ids.RemoveAt(k);cut=true;break;}if(!cut)break;}
 }
 static float Cross(Vector2 a,Vector2 b){return a.x*b.y-a.y*b.x;}
 protected void Stroke(Vector2 a,Vector2 b,float width,Color c){Vector2 d=(b-a).normalized;Vector2 v=new Vector2(-d.y,d.x)*width*.5f;Quad(a-v,b-v,b+v,a+v,c,c,c,c);}
 protected Color C(float r,float g,float b,float a=1){return new Color(r,g,b,a);}
 protected override void Awake(){base.Awake();raycastTarget=false;}
}
public class TitleBackdropGraphic:LuminousBackdrop {}
public class LegacyBladeGraphic:TitleMeshGraphic {
 static Vector2 V(float x,float y){return new Vector2(x,y);}
 protected override void OnPopulateMesh(VertexHelper vh){mesh=vh;vh.Clear();Color silver=new Color32(232,238,245,255),steel=new Color32(106,130,154,255),red=new Color32(231,43,70,255),ink=new Color32(13,21,33,255);
 // The split blade silhouette recalls the original HUD's sharp, asymmetric silver crest.
 Poly(steel,ink,V(395,1130),V(1180,230),V(1540,110),V(1190,565),V(2310,170),V(1770,650),V(1310,782),V(1020,1340),V(925,827));
 Poly(silver,steel,V(190,1090),V(1050,315),V(1410,110),V(1015,655),V(2320,205),V(1680,674),V(1185,765),V(795,1220),V(880,755));
 Poly(Color.white,silver,V(190,1090),V(1050,315),V(1410,110),V(1015,655),V(1195,475),V(1025,475));
 Poly(ink,ink,V(670,850),V(1065,520),V(935,747),V(1305,615),V(1112,755),V(906,795),V(795,1020));
 Poly(red,new Color32(124,18,44,255),V(1105,770),V(1315,703),V(1190,1310),V(1060,1410));
 Poly(silver,steel,V(1320,705),V(1470,657),V(1290,1150),V(1245,1198));
 Poly(silver,steel,V(593,806),V(833,592),V(748,795),V(416,1097));
 Poly(red,red,V(1550,365),V(1615,342),V(1720,257),V(1656,280));
 Poly(red,red,V(1650,328),V(1715,305),V(1820,220),V(1756,243));
 }
}
public class ZeroWordmarkGraphic:TitleMeshGraphic {
 static Vector2 V(float x,float y){return new Vector2(x,y);}
 void Face(float ox,params Vector2[] p){Vector2[] q=new Vector2[p.Length];for(int i=0;i<p.Length;i++)q[i]=V((p[i].x+ox)*2.035f+60+(240-p[i].y)*.38f,p[i].y*4.9f+110);Poly(new Color32(245,248,252,255),new Color32(162,181,200,255),q);}
 void Ring(float ox,bool r=false){Vector2[] a={V(0,35),V(35,0),V(236,0),V(270,35),V(270,r?108:205),V(235,r?144:240),V(35,r?144:240),V(0,r?108:205)},b={V(62,59),V(77,44),V(192,44),V(208,59),V(208,r?85:181),V(192,r?100:196),V(77,r?100:196),V(62,r?85:181)};for(int i=0;i<8;i++){int j=(i+1)%8;Face(ox,a[i],a[j],b[j],b[i]);}}
 protected override void OnPopulateMesh(VertexHelper vh){mesh=vh;vh.Clear();Face(0,V(0,0),V(272,0),V(260,47),V(61,193),V(272,193),V(258,240),V(-14,240),V(0,193),V(199,47),V(0,47));Face(298,V(0,0),V(64,0),V(64,240),V(0,240));Face(298,V(51,0),V(267,0),V(249,47),V(51,47));Face(298,V(51,96),V(235,96),V(217,140),V(51,140));Face(298,V(51,193),V(267,193),V(249,240),V(51,240));Ring(598,true);Face(598,V(0,108),V(62,108),V(62,240),V(0,240));Face(598,V(139,132),V(208,132),V(284,240),V(210,240));Ring(913);}
}
}


