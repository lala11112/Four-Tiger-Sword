using System.Collections.Generic;
namespace BoundaryZeroUI {
[System.Serializable] public class UiState {
 public int form=3,selected=0,tab=0,category=-1,outElement=3,rewardChoice=-1,roll=0,pendingUpgrade=0,wheelHover=-1;
 public bool enhanced=false,heal=false,wheelOutside=false;
 public bool shopInventoryOpen=true;
 public bool shopInspectOwned=false;
 public int shopInventorySelected=-1,shopInventoryCategory=-1;
 public float shopInventoryScroll=1;
 public List<int> owned=new List<int>{0,1,2,3,4,5,6,7,8,9,10,11},materials=new List<int>();
 public Dictionary<int,int> grades=new Dictionary<int,int>{{2,2}},elements=new Dictionary<int,int>();
 public Dictionary<string,float> settings=DefaultSettings();
 public string mode="전체 화면",notice="",confirmMode="abandon",data="",healthText="",objective="";
 public float? healthRatio=null,loadingProgress=null;
 public string loadingStatus="";
 public string[] abilityKeys=new string[]{"",""};
 public Dictionary<string,string> uiData=new Dictionary<string,string>();
 public static Dictionary<string,float> DefaultSettings(){return new Dictionary<string,float>{{"brightness",65},{"sound",70},{"music",55},{"sensitivity",50}};}
 public int Grade(int i){return grades.ContainsKey(i)?grades[i]:i==2?2:3;}
 public int Element(int i){return elements.ContainsKey(i)?elements[i]:((i%5)+5)%5;}
}
}

