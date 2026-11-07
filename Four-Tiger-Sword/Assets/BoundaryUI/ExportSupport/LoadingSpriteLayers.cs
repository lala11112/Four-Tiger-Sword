#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;

namespace BoundaryZeroUI.Editor {
/// <summary>A broken, machined boundary seal. Progress illuminates only measured work.</summary>
public sealed class LoadingSpriteLayers : TitleMeshGraphic {
 public int Part;
 public int Segment;

 static readonly Color Silver = new Color32(232,238,245,255);
 static readonly Color Steel = new Color32(87,112,139,255);
 static readonly Color Ink = new Color32(9,17,29,255);
 static readonly Color Red = new Color32(231,43,70,255);


 Vector2 Rad(float radius,float degrees,Vector2 offset){float a=degrees*Mathf.Deg2Rad;return new Vector2(1280+Mathf.Cos(a)*radius,720+Mathf.Sin(a)*radius)+offset;}
 void Band(float inner,float outer,float start,float end,Color low,Color high,Vector2 offset,int steps=24){
  for(int n=0;n<steps;n++){float a=Mathf.Lerp(start,end,(float)n/steps),b=Mathf.Lerp(start,end,(float)(n+1)/steps);Quad(Rad(outer,a,offset),Rad(outer,b,offset),Rad(inner,b,offset),Rad(inner,a,offset),high,high,low,low);}
 }
 void Line(float radius,float start,float end,float width,Color color,Vector2 offset){Band(radius-width*.5f,radius+width*.5f,start,end,color,color,offset,Mathf.Max(2,Mathf.CeilToInt(Mathf.Abs(end-start)/3)));}
 Color Alpha(Color c,float a){c.a=a;return c;}
 protected override void OnPopulateMesh(VertexHelper vh){
  mesh=vh;vh.Clear();float t=0;Vector2 zero=Vector2.zero;
  // Recessed fixed housing: broad dark facets hold the floating metal shutters.
  if(Part==0){
  for(int i=0;i<5;i++){
   float a=-112+i*72;
   Band(354,534,a,a+62,Ink,new Color32(24,39,57,255),zero);
   Line(536,a+3,a+59,2,Alpha(Steel,.45f),zero);
   Line(349,a+4,a+57,3,Alpha(Steel,.35f),zero);
  }
  }
  // Each shutter has its own small axial separation; the silhouette never spins.
  for(int i=0;i<5;i++){
   if((Part!=1 && Part!=2) || i!=Segment)continue;
   float a=-112+i*72;
   float breath=9+5*Mathf.Sin(t*.65f+i*.8f);
   Vector2 offset=Rad(breath,a+31,zero)-new Vector2(1280,720);
   if(Part==1){
   Band(387,510,a+1,a+58,new Color32(29,43,62,255),new Color32(111,133,154,255),offset);
   Band(400,499,a+3,a+56,Steel,new Color32(172,189,205,255),offset);
   Band(487,499,a+3,a+55,new Color32(178,195,212,255),Silver,offset);
   Band(398,411,a+5,a+56,Ink,Steel,offset);
   // Slanted leading knife and trailing cut expose the dark mechanism beneath.
   Poly(Silver,Steel,Rad(499,a+3,offset),Rad(474,a-1,offset),Rad(395,a+10,offset),Rad(411,a+13,offset));
   Poly(Steel,Ink,Rad(499,a+56,offset),Rad(510,a+60,offset),Rad(421,a+64,offset),Rad(400,a+56,offset));
   Line(475,a+10,a+48,2,Alpha(Silver,.65f),offset);
   Line(425,a+14,a+52,8,Ink,offset);
   Line(425,a+14,a+52,3,Alpha(Red,.20f),offset);
   }
   if(Part==2)Line(425,a+14,a+52,3,Red,offset);
   // Three inset vents accent the machined surface, away from the luminous channel.
   if(Part==1)
   for(int j=0;j<3;j++){
    float ang=a+37+j*4;
    Stroke(Rad(450,ang,offset),Rad(465,ang+1.6f,offset),3,Alpha(Ink,.8f));
   }
  }
  // A separate, restrained index mechanism moves inside the stationary shutters.
  if(Part==3){
  float rotation=t*5;
  for(int i=0;i<3;i++){
   float a=rotation+i*120;
   Line(324,a,a+83,2,Alpha(Steel,.65f),zero);
   Line(324,a+78,a+83,4,Silver,zero);
  }
  }
  if(Part==4){
  for(int i=0;i<20;i++){
   float a=i*18;
   Stroke(Rad(294,a,zero),Rad(i%5==0?306:300,a,zero),i%5==0?3:2,Alpha(Steel,i%5==0?.8f:.35f));
  }
  }
  // The core is an abstract interlocked seal, with no letters or repeated title emblem.
  if(Part==5){
  for(int i=0;i<5;i++){
   float a=-54+i*72;
   Poly(Steel,Ink,Rad(218,a,zero),Rad(146,a+19,zero),Rad(122,a+44,zero),Rad(206,a+54,zero),Rad(181,a+67,zero),Rad(83,a+41,zero),Rad(107,a+11,zero));
   Poly(Silver,Steel,Rad(210,a,zero),Rad(138,a+19,zero),Rad(113,a+44,zero),Rad(91,a+40,zero),Rad(117,a+13,zero));
   Stroke(Rad(184,a+52,zero),Rad(126,a+42,zero),3,Alpha(Silver,.5f));
  }
  }
  if(Part==6){
  // Crimson seams join the core through negative space; pulses indicate activity only.
  float signal=.65f+.20f*Mathf.Sin(t*1.8f);
  for(int i=0;i<5;i++){
   float a=-54+i*72;
   Stroke(Rad(54,a-10,zero),Rad(101,a-10,zero),4,Alpha(Red,signal));
   Stroke(Rad(224,a+21,zero),Rad(253,a+21,zero),3,Alpha(Red,.65f));
  }
  Poly(Alpha(Red,.85f),Alpha(Red,.85f),new Vector2(1280,693),new Vector2(1307,720),new Vector2(1280,747),new Vector2(1253,720));
  Poly(Ink,Ink,new Vector2(1280,705),new Vector2(1295,720),new Vector2(1280,735),new Vector2(1265,720));
  }
 }
}
}


#endif

