using ArpgHud.PC;
using TMPro;
using UnityEditor;
using UnityEngine;
namespace BoundaryZeroUI.Editor {
public static class BoundaryGoldWidgetBuilder {
 const string Path="Assets/BoundaryUI/Resources/BoundaryUI/GoldHUD";
 [MenuItem("Boundary UI/Build Gold Widgets")]
 public static void BuildWidgets(){
  System.IO.Directory.CreateDirectory(Path);
  AssetDatabase.Refresh();
  var balance=UIFactory.Node(null,"GoldBalance",46.9f,1144,360,48);
  balance.localScale=Vector3.one*1.2f;
  Draw(balance,"CutGoldCoin",17,8,30,30,BoundaryGoldFrameGraphic.DrawingKind.Coin);
  var label=Label(balance,57,5,265,35,UIFactory.Ivory);balance.gameObject.AddComponent<PentaGoldBalanceView>().Configure(label);
  Save(balance,"GoldBalance");
  var feed=UIFactory.Node(null,"GoldRewardFeed",0,0,400,170);
  feed.anchorMin=feed.anchorMax=new Vector2(1,.5f);feed.pivot=new Vector2(1,.5f);feed.anchoredPosition=new Vector2(-68,-82);
  var group=feed.gameObject.AddComponent<CanvasGroup>();group.interactable=false;group.blocksRaycasts=false;
  var rows=new PentaGoldRewardRow[3];
  for(int i=0;i<3;i++){
   var row=UIFactory.Node(feed,"RewardRow"+(i+1),0,0,400,48);
   Draw(row,"LightSilverRails",0,0,400,48,BoundaryGoldFrameGraphic.DrawingKind.Reward);
   Draw(row,"CutGoldCoin",25,9,30,30,BoundaryGoldFrameGraphic.DrawingKind.Coin);
   var value=Label(row,67,5,292,35,new Color32(225,195,130,255));
   rows[i]=row.gameObject.AddComponent<PentaGoldRewardRow>();rows[i].Configure(value,row.gameObject.AddComponent<CanvasGroup>());
  }
  feed.gameObject.AddComponent<PentaGoldRewardFeed>().Configure(rows,10,.4f,2,.3f);Save(feed,"GoldRewardFeed");AssetDatabase.SaveAssets();AssetDatabase.Refresh();
 }
 static TextMeshProUGUI Label(Transform parent,float x,float y,float w,float h,Color color){var t=UIFactory.Label(parent,"",x,y,w,h,22,color,TextAlignmentOptions.MidlineLeft);t.fontStyle=FontStyles.Normal;t.enableWordWrapping=false;t.overflowMode=TextOverflowModes.Ellipsis;t.raycastTarget=false;return t;}
 static void Draw(Transform p,string name,float x,float y,float w,float h,BoundaryGoldFrameGraphic.DrawingKind kind){var g=UIFactory.Node(p,name,x,y,w,h).gameObject.AddComponent<BoundaryGoldFrameGraphic>();g.Kind=kind;g.raycastTarget=false;}
 static void Save(RectTransform root,string name){PrefabUtility.SaveAsPrefabAsset(root.gameObject,Path+"/"+name+".prefab");Object.DestroyImmediate(root.gameObject);}
}
}
