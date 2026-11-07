using UnityEngine;
using UnityEngine.UI;
namespace BoundaryZeroUI {
public class LuminousBackdrop:TitleMeshGraphic {
 public bool Menu=true;
 public static RectTransform Build(Transform p,bool menu){var r=UIFactory.Node(p,menu?"Legacy_Title_Atmosphere":"Legacy_UI_Atmosphere",0,0,2560,1440);r.gameObject.AddComponent<LuminousBackdrop>().Menu=menu;return r;}
 static Vector2 V(float x,float y){return new Vector2(x,y);}
 Color Field(float x,float y){float light=Mathf.Exp(-2*((x-1100)*(x-1100)/1900000f+(y-580)*(y-580)/1200000f));return Color.Lerp(new Color32(13,21,33,255),new Color32(32,49,69,255),light*.85f);}
 protected override void OnPopulateMesh(VertexHelper vh){mesh=vh;vh.Clear();for(int y=0;y<1440;y+=40)for(int x=0;x<2560;x+=40)Quad(V(x,y),V(x+40,y),V(x+40,y+40),V(x,y+40),Field(x,y),Field(x+40,y),Field(x+40,y+40),Field(x,y+40));
 Color edge=new Color(.64f,.75f,.86f,.065f),red=new Color(231/255f,43/255f,70/255f,Menu?.32f:.13f);
 // A cropped architectural blade plane is kept exclusively at the left edge.
 Poly(new Color(.19f,.28f,.39f,.12f),new Color(.1f,.17f,.26f,.04f),V(-100,1000),V(580,-100),V(760,-100),V(100,1440),V(-100,1440));
 Stroke(V(-120,1350),V(630,-100),1.5f,edge);Stroke(V(-100,1400),V(660,-100),1,edge);
 Poly(red,red,V(-40,1160),V(355,340),V(330,443),V(-40,1260));
 Stroke(V(90,93),V(540,93),1,new Color(.65f,.76f,.88f,.18f));Stroke(V(90,93),V(90,145),1,new Color(.65f,.76f,.88f,.18f));
 Stroke(V(90,1335),V(385,1335),1,edge);Stroke(V(2300,1335),V(2460,1335),1,edge);
 for(int i=0;i<5;i++)Stroke(V(2290+i*22,110),V(2300+i*22,92),2,i==0?red:edge);
 }
}
}
