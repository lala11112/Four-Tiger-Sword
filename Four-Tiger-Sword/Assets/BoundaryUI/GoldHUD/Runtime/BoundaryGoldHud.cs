using ArpgHud.PC;
using UnityEngine;
namespace BoundaryZeroUI {
public sealed class BoundaryGoldHud : MonoBehaviour {
 public PentaGoldBalanceView Balance {get;private set;}
 public PentaGoldRewardFeed Feed {get;private set;}
 public static BoundaryGoldHud Attach(RectTransform root){
  var existing=root.GetComponent<BoundaryGoldHud>();if(existing)return existing;
  var balance=Resources.Load<GameObject>("BoundaryUI/GoldHUD/GoldBalance");var feed=Resources.Load<GameObject>("BoundaryUI/GoldHUD/GoldRewardFeed");
  if(!balance||!feed){Debug.LogError("Build Boundary gold widgets before attaching the HUD.");return null;}
  var hud=root.gameObject.AddComponent<BoundaryGoldHud>();
  hud.Balance=Instantiate(balance,root,false).GetComponent<PentaGoldBalanceView>();
  hud.Feed=Instantiate(feed,root,false).GetComponent<PentaGoldRewardFeed>();return hud;
 }
 public void SetGold(int value){if(Balance)Balance.SetGold(value);}
 public void ShowGoldGain(int amount){if(Feed)Feed.ShowGoldGain(amount);}
}
}
