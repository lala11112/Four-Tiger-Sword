using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEngine;
using UnityEngine.UI;
namespace BoundaryZeroUI {
// SVG is interpreted into ordinary Canvas triangles; no rendered frame textures.
public class SvgFrameGraphic : MaskableGraphic {
 public TextAsset Source; public string Resource; public Vector2 ViewBox=new Vector2(1672,941);
 struct Style {public Color fill,stroke; public float width,opacity,fillOpacity,strokeOpacity;}
 protected override void OnPopulateMesh(VertexHelper vh){vh.Clear();var src=Source?Source:Resources.Load<TextAsset>(Resource??"");if(!src)return;try{var doc=new XmlDocument();doc.LoadXml(src.text); Walk(doc.DocumentElement,new Style{fill=Color.black,stroke=Color.clear,width=1,opacity=1,fillOpacity=1,strokeOpacity=1},vh);}catch(Exception e){Debug.LogWarning("Frame geometry: "+e.Message);}}
 float F(string s,float d=0){float f;return float.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out f)?f:d;}
 Color C(string s,Color d){if(string.IsNullOrEmpty(s))return d;if(s=="none"||s.StartsWith("url"))return Color.clear;if(s=="currentColor"||s.Contains("--ivory"))return UIFactory.Ivory;Color c;return ColorUtility.TryParseHtmlString(s,out c)?c:d;}
 void Walk(XmlElement e,Style s,VertexHelper vh){if(e.LocalName=="defs"||e.LocalName=="filter"||e.LocalName=="pattern")return;s.fill=C(e.GetAttribute("fill"),s.fill);s.stroke=C(e.GetAttribute("stroke"),s.stroke);s.width=F(e.GetAttribute("stroke-width"),s.width);s.opacity*=F(e.GetAttribute("opacity"),1);s.fillOpacity=F(e.GetAttribute("fill-opacity"),s.fillOpacity);s.strokeOpacity=F(e.GetAttribute("stroke-opacity"),s.strokeOpacity);var fc=s.fill;fc.a*=s.opacity*s.fillOpacity;var sc=s.stroke;sc.a*=s.opacity*s.strokeOpacity;
 if(e.LocalName=="path"){foreach(var p in Parse(e.GetAttribute("d"))){if(fc.a>0)Fill(vh,p,fc);if(sc.a>0)Stroke(vh,p,s.width,sc);}}
 else if(e.LocalName=="rect"){float x=F(e.GetAttribute("x")),y=F(e.GetAttribute("y")),w=F(e.GetAttribute("width")),h=F(e.GetAttribute("height"));var p=new List<Vector2>{new Vector2(x,y),new Vector2(x+w,y),new Vector2(x+w,y+h),new Vector2(x,y+h),new Vector2(x,y)};if(fc.a>0)Fill(vh,p,fc);if(sc.a>0)Stroke(vh,p,s.width,sc);}
 else if(e.LocalName=="circle"){var p=new List<Vector2>();float x=F(e.GetAttribute("cx")),y=F(e.GetAttribute("cy")),r=F(e.GetAttribute("r"));for(int i=0;i<=48;i++){float a=i*Mathf.PI*2/48;p.Add(new Vector2(x+Mathf.Cos(a)*r,y+Mathf.Sin(a)*r));}if(fc.a>0)Fill(vh,p,fc);if(sc.a>0)Stroke(vh,p,s.width,sc);}
 foreach(XmlNode n in e.ChildNodes)if(n is XmlElement)Walk((XmlElement)n,s,vh);}
 List<List<Vector2>> Parse(string d){var result=new List<List<Vector2>>();var tokens=Regex.Matches(d,@"[MLHVZmlhvz]|[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?");List<Vector2> p=null;Vector2 cur=Vector2.zero,start=cur;char cmd='M';int i=0;while(i<tokens.Count){string t=tokens[i].Value;if(char.IsLetter(t[0])){cmd=t[0];i++;if(char.ToUpperInvariant(cmd)=='Z'){if(p!=null&&p.Count>0)p.Add(start);cur=start;continue;}}bool rel=char.IsLower(cmd);char c=char.ToUpperInvariant(cmd);if(i>=tokens.Count)break;float a=F(tokens[i++].Value);if(c=='H')cur.x=rel?cur.x+a:a;else if(c=='V')cur.y=rel?cur.y+a:a;else {if(i>=tokens.Count)break;float b=F(tokens[i++].Value);cur=rel?cur+new Vector2(a,b):new Vector2(a,b);}if(c=='M'){p=new List<Vector2>();result.Add(p);start=cur;cmd=rel?'l':'L';}if(p==null){p=new List<Vector2>();result.Add(p);}p.Add(cur);}return result;}
 Vector2 P(Vector2 p){var r=rectTransform.rect;return new Vector2(r.xMin+p.x/ViewBox.x*r.width,r.yMax-p.y/ViewBox.y*r.height);}
 void V(VertexHelper vh,Vector2 p,Color c){vh.AddVert(P(p),c,Vector2.zero);}
 void Stroke(VertexHelper vh,List<Vector2> p,float width,Color c){for(int i=1;i<p.Count;i++){var a=p[i-1];var b=p[i];var v=b-a;if(v.sqrMagnitude<.0001f)continue;var n=new Vector2(-v.y,v.x).normalized*width*.5f;int k=vh.currentVertCount;V(vh,a+n,c);V(vh,b+n,c);V(vh,b-n,c);V(vh,a-n,c);vh.AddTriangle(k,k+1,k+2);vh.AddTriangle(k,k+2,k+3);}}
 float Cross(Vector2 a,Vector2 b,Vector2 c){return (b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);}
 void Fill(VertexHelper vh,List<Vector2> original,Color c){var p=new List<Vector2>(original);if(p.Count>2&&(p[0]-p[p.Count-1]).sqrMagnitude<.001f)p.RemoveAt(p.Count-1);if(p.Count<3)return;float area=0;for(int j=0;j<p.Count;j++){var a=p[j];var b=p[(j+1)%p.Count];area+=a.x*b.y-b.x*a.y;}if(area<0)p.Reverse();var ix=new List<int>();int k=vh.currentVertCount;for(int j=0;j<p.Count;j++){V(vh,p[j],c);ix.Add(j);}int guard=0;while(ix.Count>2&&guard++<p.Count*p.Count){bool found=false;for(int j=0;j<ix.Count;j++){int a=ix[(j+ix.Count-1)%ix.Count],b=ix[j],cc=ix[(j+1)%ix.Count];if(Cross(p[a],p[b],p[cc])<=0)continue;bool inside=false;foreach(int q in ix){if(q==a||q==b||q==cc)continue;if(Cross(p[a],p[b],p[q])>=0&&Cross(p[b],p[cc],p[q])>=0&&Cross(p[cc],p[a],p[q])>=0){inside=true;break;}}if(inside)continue;vh.AddTriangle(k+a,k+b,k+cc);ix.RemoveAt(j);found=true;break;}if(!found)break;}}
}
}

