using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
namespace BoundaryZeroUI {
public class BoundaryUIController:MonoBehaviour {
 public static readonly string[] ScreenIds={"main","pause","settings","hud","shop-buy","shop-sell","shop-synth","inventory","upgrade","wheel","reward-upgrade","reward-item","reward-boss","clear","death","event","confirm","loading"};
 public UiState State=new UiState(); public event Action<string> ActionRequested;
 public bool IsBrowserOpen {get{return viewer;}}
 public bool PreviewLoadingTransition=true; int loadingTicket;
 public bool ShowPreviewBackdrop=true; public int Design; public string CurrentScreen {get{return stack.Count>0?stack[stack.Count-1].id:baseScreen;}}
 class Entry {public string id; public int selected,tab,category,reward;public string notice;}
 readonly List<Entry> stack=new List<Entry>();string baseScreen="main";RectTransform canvasRoot,screenLayer,modalLayer,viewerLayer;ReferenceHud hud;bool initialized,viewer,wheelHold;int wheelPrior;bool enhancedPrior;
 UiState renderedState;bool resetOwnedScroll;
 static readonly Dictionary<string,string> titles=new Dictionary<string,string>{{"clear","탐사 완료"},{"death","탐사 종료"},{"pause","일시 정지"},{"settings","환경 설정"},{"shop-buy","경계 상점"},{"shop-sell","경계 상점"},{"shop-synth","유물 합성"},{"inventory","소지품"},{"upgrade","영구 강화"},{"wheel","오행 전환"},{"reward-upgrade","강화 선택"},{"reward-item","아이템 보상"},{"reward-boss","수호자의 유산"},{"event","데이터 잔향"},{"confirm","선택 확인"},{"loading","로딩"}};
 void Awake(){Initialize();}
 RectTransform Layer(string name,Transform parent){var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;return r;}
 public void Initialize(){if(initialized)return;initialized=true;var go=new GameObject("BoundaryZeroCanvas",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster));go.transform.SetParent(transform,false);canvasRoot=go.GetComponent<RectTransform>();var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
 var previewCamera=ShowPreviewBackdrop?FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault(c=>c.gameObject.name=="UI Preview Camera"):null;
 if(previewCamera){previewCamera.allowMSAA=true;previewCamera.cullingMask=-1;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=previewCamera;canvas.planeDistance=1;}
 var scale=go.GetComponent<UnityEngine.UI.CanvasScaler>();scale.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scale.referenceResolution=new Vector2(2560,1440);scale.matchWidthOrHeight=.5f;
 var design=new GameObject("BoundaryDesignViewport",typeof(RectTransform)).GetComponent<RectTransform>();design.SetParent(canvasRoot,false);design.gameObject.AddComponent<BoundaryDesignViewport>();canvasRoot=design;
 if(FindFirstObjectByType<EventSystem>()==null)new GameObject("EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));if(ShowPreviewBackdrop)LuminousBackdrop.Build(Layer("PreviewAtmosphere",canvasRoot),false);hud=ReferenceHud.Build(Layer("PersistentHUD",canvasRoot),Dispatch);screenLayer=Layer("Screen",canvasRoot);modalLayer=Layer("ModalStack",canvasRoot);viewerLayer=Layer("ScreenBrowser",canvasRoot);Render();}
 Entry Snapshot(string id){return new Entry{id=id,selected=State.selected,tab=State.tab,category=State.category,reward=State.rewardChoice,notice=State.notice};}
 void Restore(Entry e){State.selected=e.selected;State.tab=e.tab;State.category=e.category;State.rewardChoice=e.reward;State.notice=e.notice;}
 static bool IsBaseScreen(string id){return id=="main"||id=="hud"||id=="clear"||id=="death"||id=="loading";}
 public void ShowScreen(string id){Initialize();if(!ScreenIds.Contains(id))return;stack.Clear();wheelHold=false;loadingTicket++;baseScreen=IsBaseScreen(id)?id:"hud";if(baseScreen!=id)stack.Add(Snapshot(id));if(id=="wheel"){wheelPrior=State.form;enhancedPrior=State.enhanced;State.wheelHover=-1;State.wheelOutside=false;}Render();}
 void Open(string id){if(!ScreenIds.Contains(id)||id==CurrentScreen)return;if(IsBaseScreen(id)){ShowScreen(id);return;}int i=stack.FindIndex(e=>e.id==id);if(i>=0){stack.RemoveRange(i+1,stack.Count-i-1);Restore(stack[i]);}else if(id.StartsWith("shop-")&&CurrentScreen.StartsWith("shop-"))stack[stack.Count-1].id=id;else{if(stack.Count>0)stack[stack.Count-1]=Snapshot(CurrentScreen);stack.Add(Snapshot(id));}State.notice="";Render();}
 void Close(){if(CurrentScreen=="clear"||CurrentScreen=="death"||CurrentScreen=="loading"){ShowScreen("main");return;}if(CurrentScreen=="wheel"){CancelWheel();return;}if(stack.Count>0){stack.RemoveAt(stack.Count-1);if(stack.Count>0)Restore(stack[stack.Count-1]);Render();}else Open(baseScreen=="hud"?"pause":"main");}
 void Clear(RectTransform r){foreach(Transform child in r){child.gameObject.SetActive(false);Destroy(child.gameObject);}}
 public void Refresh(){Render();}
 public BoundaryGoldHud GoldHud {get{Initialize();return hud.GoldHud;}}
 public void SetGold(int balance){GoldHud.SetGold(balance);}
 public void ShowGoldGain(int amount){GoldHud.ShowGoldGain(amount);}
 void NormalizeShopInventory(){var visible=State.owned.Where(i=>State.shopInventoryCategory<0||State.Element(i)==State.shopInventoryCategory);if(!visible.Contains(State.shopInventorySelected))State.shopInventorySelected=visible.DefaultIfEmpty(-1).First();if(State.shopInventorySelected<0||!State.shopInventoryOpen)State.shopInspectOwned=false;if(CurrentScreen=="shop-sell")State.selected=State.shopInventorySelected;}
 void Render(){
  if(!initialized)return;
  var oldOwnedScroll=GetComponentsInChildren<UnityEngine.UI.ScrollRect>().FirstOrDefault(s=>s.name=="ShopOwnedScroll");
  if(ReferenceEquals(renderedState,State)&&oldOwnedScroll&&!resetOwnedScroll)State.shopInventoryScroll=oldOwnedScroll.verticalNormalizedPosition;
  if(resetOwnedScroll)State.shopInventoryScroll=1;resetOwnedScroll=false;
  NormalizeShopInventory();Clear(screenLayer);Clear(modalLayer);hud.gameObject.SetActive(baseScreen=="hud");hud.Apply(State);hud.SetModal(stack.Count>0);if(baseScreen!="hud")CompleteViews.Build(baseScreen,screenLayer,State,Dispatch,Design);
  for(int i=0;i<stack.Count;i++){string id=stack[i].id;var layer=Layer("Modal_"+id,modalLayer);var content=id=="wheel"?UIFactory.Node(layer,"WheelOverlay",0,0,2560,1440):UIFactory.Popup(layer,id,titles.ContainsKey(id)?titles[id]:id,Close,id=="confirm"||id=="pause");if(id=="wheel")content.gameObject.AddComponent<BrightUiScope>();CompleteViews.Build(id,content,State,Dispatch,Design);if(i<stack.Count-1){var group=layer.gameObject.AddComponent<CanvasGroup>();group.interactable=false;group.blocksRaycasts=false;}}
  var ownedScroll=GetComponentsInChildren<UnityEngine.UI.ScrollRect>().FirstOrDefault(s=>s.name=="ShopOwnedScroll");if(ownedScroll){Canvas.ForceUpdateCanvases();ownedScroll.verticalNormalizedPosition=Mathf.Clamp01(State.shopInventoryScroll);}renderedState=State;
  if(viewer)BuildBrowser();
 }
 bool SynthReady(){return State.materials.Count==3&&State.materials.Distinct().Count()==3&&State.materials.All(State.owned.Contains)&&State.materials.Select(State.Grade).OrderBy(x=>x).SequenceEqual(new[]{2,3,3});}
 int NextItem(int template){int n=Math.Max(14,State.owned.Count>0?State.owned.Max():14)+1;while(n%12!=((template%12)+12)%12)n++;return n;}
 void Confirm(string mode){if(mode=="synth"&&!SynthReady()){State.notice="서로 다른 3등급·3등급·2등급 재료가 필요합니다.";Render();return;}State.confirmMode=mode;Open("confirm");}
 public void Dispatch(string action){if(string.IsNullOrEmpty(action))return;ActionRequested?.Invoke(action);var parts=action.Split(':');string k=parts[0],arg=parts.Length>1?parts[1]:"";int n;int.TryParse(arg,out n);
 if(k=="shop-inventory"){
  if(!CurrentScreen.StartsWith("shop-"))return;
  if(arg=="toggle"){if(CurrentScreen!="shop-buy")return;State.shopInventoryOpen=!State.shopInventoryOpen;if(!State.shopInventoryOpen)State.shopInspectOwned=false;}
  else if(parts.Length>2&&int.TryParse(parts[2],out n)){
   if(arg=="item"&&State.owned.Contains(n)&&(State.shopInventoryCategory<0||State.Element(n)==State.shopInventoryCategory)){State.shopInventorySelected=n;if(CurrentScreen=="shop-buy")State.shopInspectOwned=true;}
   else if(arg=="category"&&n>=-1&&n<5){State.shopInventoryCategory=n;resetOwnedScroll=true;}
  }
  Render();return;
 }
 if(k=="shop-focus"){if(CurrentScreen=="shop-buy"&&arg=="stock"){State.shopInspectOwned=false;Render();}return;}
 if(k=="go"){Open(arg);return;}if(k=="close"||k=="back"||k=="cancel"||k=="resume"){Close();return;}if(k=="enter"){BeginLoading();return;}if(k=="confirm"&&arg!=""){Confirm(arg);return;}
 switch(k){case "item":State.selected=n;if(CurrentScreen=="shop-buy")State.shopInspectOwned=false;break;case "category":State.category=n;break;case "tab":State.tab=n;break;case "out":State.outElement=n;break;case "reward":State.rewardChoice=n;break;case "form":State.form=n;State.enhanced=false;break;
 case "material":if(State.materials.Contains(n))State.materials.Remove(n);else if(State.owned.Contains(n)&&State.materials.Count<3){int g=State.Grade(n),limit=g==3?2:g==2?1:0;if(State.materials.Count(x=>State.Grade(x)==g)<limit)State.materials.Add(n);else State.notice="3등급 두 개와 2등급 한 개를 선택하세요.";}State.materials.Sort((a,b)=>State.Grade(b).CompareTo(State.Grade(a)));break;
 case "buy":int next=NextItem(State.selected);State.owned.Add(next);State.grades[next]=State.Grade(State.selected);State.elements[next]=State.Element(State.selected);State.notice="구매 완료";break;
 case "sell":if(State.owned.Remove(State.selected)){State.materials.Remove(State.selected);State.selected=State.owned.Count>0?State.owned[0]:-1;State.notice="판매 완료";}break;
 case "reroll":State.roll++;State.notice="새로고침 완료";break;case "heal":if(!State.heal){State.heal=true;State.notice="회복 완료";}break;
 case "synth":case "synthesize":Confirm("synth");return;case "upgrade":State.pendingUpgrade=n;Confirm("upgrade");return;case "apply":State.notice="적용 완료";break;case "reset":State.settings=UiState.DefaultSettings();State.mode="전체 화면";State.notice="초기화 완료";break;
 case "take-reward":if(State.rewardChoice<0){State.notice="보상을 선택해 주세요.";break;}if(CurrentScreen=="reward-upgrade"){State.rewardChoice=-1;ShowScreen("reward-item");return;}if(CurrentScreen=="reward-boss"){ShowScreen("clear");return;}next=NextItem(State.rewardChoice+6);State.owned.Add(next);State.elements[next]=(State.rewardChoice+6)%5;State.grades[next]=3;BeginLoading();return;
 case "confirm":if(State.confirmMode=="synth"){if(!SynthReady()){State.notice="합성 재료를 다시 확인해 주세요.";break;}foreach(int item in State.materials)State.owned.Remove(item);next=NextItem(State.outElement);State.owned.Add(next);State.grades[next]=4;State.elements[next]=State.outElement;State.materials.Clear();Close();State.selected=next;State.notice="4등급 합성 완료";}else if(State.confirmMode=="upgrade"){Close();State.notice="";}else{if(State.confirmMode=="abandon"){State.owned.Clear();State.materials.Clear();State.enhanced=false;}ShowScreen("main");return;}break;
 case "event":ShowScreen("hud");return;default:if(k=="setting"&&parts.Length>2){float v;if(float.TryParse(parts[2],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out v)){if(arg=="mode")State.mode=new[]{"전체 화면","창 모드","테두리 없는 창"}[Mathf.Clamp((int)v,0,2)];else State.settings[arg]=v;}}return;}Render();}
 void BeginLoading(){State.loadingProgress=null;State.loadingStatus="";ShowScreen("loading");if(PreviewLoadingTransition)StartCoroutine(PreviewLoad(loadingTicket));}
 IEnumerator PreviewLoad(int ticket){yield return new WaitForSecondsRealtime(2.2f);if(ticket==loadingTicket&&CurrentScreen=="loading")FinishLoading();}
 public void SetLoadingProgress(float? progress,string status=null){State.loadingProgress=progress.HasValue&&!float.IsNaN(progress.Value)&&!float.IsInfinity(progress.Value)?(float?)Mathf.Clamp01(progress.Value):null;if(status!=null)State.loadingStatus=status;foreach(var display in screenLayer.GetComponentsInChildren<LegacyLoadingDisplay>())display.Apply(State.loadingProgress,State.loadingStatus);}
 public void FinishLoading(){if(CurrentScreen=="loading")ShowScreen("hud");}
 // abilityKeys accepts compact [E,R] or legacy [Q,E,R]; Q is ignored.
 public void SetHudData(string data="",string healthText="",float? healthRatio=null,string objective="",string[] abilityKeys=null){State.data=data;State.healthText=healthText;State.healthRatio=healthRatio;State.objective=objective;if(abilityKeys!=null)State.abilityKeys=abilityKeys;hud.Apply(State);}
 void Update(){if(!initialized)return;if(Input.GetKeyDown(KeyCode.F1)){viewer=!viewer;Clear(viewerLayer);if(viewer)BuildBrowser();}if(Input.GetKeyDown(KeyCode.Escape))HandleEscape();if(Input.GetKeyDown(KeyCode.E)&&CurrentScreen=="hud")Open("shop-buy");if(Input.GetKeyDown(KeyCode.Tab))HandleInventoryToggle();if(Input.GetMouseButtonDown(1)&&(CurrentScreen=="hud"||CurrentScreen=="wheel")){wheelPrior=State.form;enhancedPrior=State.enhanced;wheelHold=true;if(CurrentScreen!="wheel")Open("wheel");}if(wheelHold&&CurrentScreen=="wheel"){WheelPoint();if(Input.GetMouseButtonUp(1))CommitWheel();}}
 public void HandleInventoryToggle(){if(viewer)return;if(CurrentScreen=="shop-buy"){Dispatch("shop-inventory:toggle");return;}if(CurrentScreen=="inventory")Close();else if(CurrentScreen=="hud")Open("inventory");}
 public void HandleEscape(){if(viewer){viewer=false;Clear(viewerLayer);}else if(CurrentScreen=="shop-buy"&&State.shopInventoryOpen){State.shopInventoryOpen=false;State.shopInspectOwned=false;Render();}else if(CurrentScreen=="wheel")CancelWheel();else if(CurrentScreen=="hud"||CurrentScreen.StartsWith("reward-"))Open("pause");else Close();}
 void WheelPoint(){var surface=FindWheelSurface();if(surface==null)return;Vector2 p;var canvas=surface.GetComponentInParent<Canvas>();var inputCamera=canvas&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;RectTransformUtility.ScreenPointToLocalPointInRectangle(surface,Input.mousePosition,inputCamera,out p);var rect=surface.rect;Vector2 center=rect.center;Vector2 d=p-center;float dist=d.magnitude*800/Mathf.Min(rect.width,rect.height);State.wheelOutside=dist>330;State.wheelHover=dist<156||State.wheelOutside?-1:Mathf.FloorToInt(Mathf.Repeat(Mathf.Atan2(d.x,d.y)*Mathf.Rad2Deg+36,360)/72);foreach(var image in surface.GetComponentsInChildren<UnityEngine.UI.Image>()){if(image.name.StartsWith("WheelSector")){int index;if(int.TryParse(image.name.Substring(11),out index))image.color=index==State.wheelHover?new Color(.91f,.17f,.27f,.16f):new Color(.9f,.94f,1f,0);}}foreach(var t in modalLayer.GetComponentsInChildren<TMPro.TextMeshProUGUI>())if(t.name=="WheelStatus")t.text=State.wheelOutside?"영역 밖 · 취소":State.wheelHover<0||State.wheelHover==State.form?"기본 3타 유지":new[]{"화","토","금","수","목"}[State.wheelHover]+" · 강화 3타";}
 RectTransform FindWheelSurface(){foreach(var r in modalLayer.GetComponentsInChildren<RectTransform>())if(r.name=="WheelSurface")return r;return null;}
 void CommitWheel(){wheelHold=false;if(State.wheelOutside){CancelWheel();return;}if(State.wheelHover<0||State.wheelHover==State.form)State.enhanced=false;else{State.form=State.wheelHover;State.enhanced=true;}if(CurrentScreen=="wheel")stack.RemoveAt(stack.Count-1);ActionRequested?.Invoke(State.enhanced?"stance:enhanced":"stance:base");Render();}
 void CancelWheel(){wheelHold=false;State.form=wheelPrior;State.enhanced=enhancedPrior;if(CurrentScreen=="wheel"&&stack.Count>0)stack.RemoveAt(stack.Count-1);Render();}
 void OnApplicationFocus(bool focus){if(!focus&&CurrentScreen=="wheel")CancelWheel();}
 void BuildBrowser(){Clear(viewerLayer);var panel=new GameObject("BrowserPanel",typeof(RectTransform),typeof(UnityEngine.UI.Image)).GetComponent<RectTransform>();panel.SetParent(viewerLayer,false);panel.anchorMin=panel.anchorMax=new Vector2(0,1);panel.pivot=new Vector2(0,1);panel.anchoredPosition=new Vector2(24,-24);panel.sizeDelta=new Vector2(650,1070);panel.GetComponent<UnityEngine.UI.Image>().color=new Color(.025f,.035f,.045f,.98f);BrowserLabel(panel,"UI 둘러보기 · F1 닫기",20,15,610,50);for(int i=0;i<ScreenIds.Length;i++){string id=ScreenIds[i];BrowserButton(panel,titles.ContainsKey(id)?titles[id]:id=="main"?"메인 메뉴":id=="hud"?"HUD":id=="clear"?"클리어":"사망",20,80+i*43,610,39,()=>ShowScreen(id));}for(int i=0;i<3;i++){int d=i;BrowserButton(panel,"디자인 "+(char)('A'+i),20+i*205,920,195,48,()=>{Design=d;Render();});}BrowserLabel(panel,"E 상점   Tab 소지품   Esc 뒤로\n우클릭 유지 → 드래그 → 놓기",20,980,610,75);}
 void BrowserLabel(RectTransform p,string text,float x,float y,float w,float h){var r=new GameObject(text,typeof(RectTransform),typeof(TMPro.TextMeshProUGUI)).GetComponent<RectTransform>();r.SetParent(p,false);r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);var t=r.GetComponent<TMPro.TextMeshProUGUI>();t.text=text;t.font=UIFactory.Font;t.fontSize=24;t.color=Color.white;t.raycastTarget=false;}
 void BrowserButton(RectTransform p,string text,float x,float y,float w,float h,Action click){var r=new GameObject(text,typeof(RectTransform),typeof(UnityEngine.UI.Image),typeof(UnityEngine.UI.Button)).GetComponent<RectTransform>();r.SetParent(p,false);r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);r.GetComponent<UnityEngine.UI.Image>().color=new Color(.12f,.17f,.2f);r.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(()=>click());BrowserLabel(r,text,12,2,w-20,h-2);}
}
}





