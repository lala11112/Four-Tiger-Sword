using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace BoundaryZeroUI {
 public sealed class NativeCaptureQA:MonoBehaviour {
  readonly List<string> errors=new List<string>();readonly List<string> skipped=new List<string>();BoundaryUIController ui;string output;
  void Start(){string[] args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--capture-ui");if(i<0||i+1>=args.Length)return;output=args[i+1];Directory.CreateDirectory(output);Application.logMessageReceived+=Log;ui=GetComponent<BoundaryUIController>();StartCoroutine(Capture());}
  void Log(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+trace);}
  void Check(bool condition,string label){if(!condition)errors.Add("FAILED: "+label);}
  RectTransform ActiveRect(string name){return ui.GetComponentsInChildren<RectTransform>().FirstOrDefault(r=>r.name==name);}
  void CheckFullscreen(RectTransform root,string label){Check(root&&Mathf.Abs(root.rect.width-2560)<.1f&&Mathf.Abs(root.rect.height-1440)<.1f,label+" covers QHD canvas");}
  void CheckBlurCount(int expected,string label){
   var blurs=ui.GetComponentsInChildren<PopupBackdropBlur>();
   Check(blurs.Length==expected,label+" blur layer count "+expected);
   foreach(var blur in blurs){
    var graphic=blur.Graphic;var parent=blur.transform.parent;
    var shade=parent?parent.Find("ModalShade"):null;var popup=parent?parent.Cast<Transform>().FirstOrDefault(t=>t.name.StartsWith("Popup_")):null;
    Check(blur.IsSupported&&graphic&&graphic.enabled&&graphic.gameObject.activeInHierarchy,label+" active supported blur");
    Check(graphic&&graphic.name=="ModalBlur"&&shade&&popup&&blur.transform.GetSiblingIndex()<shade.GetSiblingIndex()&&shade.GetSiblingIndex()<popup.GetSiblingIndex(),label+" blur draws before shade and sharp popup");
    Check(graphic&&graphic.material&&Resources.LoadAll<Shader>("BoundaryUI").Contains(graphic.material.shader),label+" blur shader included in Resources");
   }
  }
  void CheckBlurPixels(Camera camera){
   var blur=ui.GetComponentInChildren<PopupBackdropBlur>();var canvas=ui.GetComponentInChildren<Canvas>();
   Check(camera&&canvas&&blur&&blur.Graphic,"blur pixel comparison prerequisites");if(!camera||!canvas||!blur||!blur.Graphic)return;
   var rt=RenderTexture.GetTemporary(2560,1440,24,RenderTextureFormat.ARGB32);
   var oldTarget=camera.targetTexture;var oldMask=camera.cullingMask;var oldMode=canvas.renderMode;var oldCamera=canvas.worldCamera;var oldDistance=canvas.planeDistance;var previous=RenderTexture.active;bool oldEnabled=blur.Graphic.enabled;Texture2D shot=null;
   try{
    canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;camera.targetTexture=rt;camera.cullingMask=-1;Canvas.ForceUpdateCanvases();
    shot=new Texture2D(2560,1440,TextureFormat.RGBA32,false);var samples=new Color32[2][];
    for(int pass=0;pass<2;pass++){blur.SetRenderingEnabled(pass==1);Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;shot.ReadPixels(new Rect(0,0,2560,1440),0,0);shot.Apply();samples[pass]=shot.GetPixels32();}
    CheckBlurSamples(samples,"camera");
    CheckPopupForegroundSharpness(samples);
   }finally{blur.SetRenderingEnabled(oldEnabled);camera.targetTexture=oldTarget;camera.cullingMask=oldMask;canvas.renderMode=oldMode;canvas.worldCamera=oldCamera;canvas.planeDistance=oldDistance;RenderTexture.active=previous;if(shot)Destroy(shot);RenderTexture.ReleaseTemporary(rt);Canvas.ForceUpdateCanvases();}
  }
  void CheckPopupForegroundSharpness(Color32[][] samples){
   double before=0,after=0;int edges=0;
   // Bright neutral text/detail edges inside the pause panel should retain contrast.
   // Excluding the panel border keeps this independent of the blurred background.
   for(int y=450;y<1000;y++)for(int x=850;x<1650;x++){
    int i=y*2560+x;var a=samples[0][i];if(a.r<160||Math.Abs(a.r-a.g)>45||Math.Abs(a.r-a.b)>45)continue;
    var ar=samples[0][i+1];var au=samples[0][i+2560];
    double sharp=Math.Abs(a.r-ar.r)+Math.Abs(a.g-ar.g)+Math.Abs(a.b-ar.b)+Math.Abs(a.r-au.r)+Math.Abs(a.g-au.g)+Math.Abs(a.b-au.b);
    if(sharp<100)continue;var b=samples[1][i];var br=samples[1][i+1];var bu=samples[1][i+2560];
    before+=sharp;after+=Math.Abs(b.r-br.r)+Math.Abs(b.g-br.g)+Math.Abs(b.b-br.b)+Math.Abs(b.r-bu.r)+Math.Abs(b.g-bu.g)+Math.Abs(b.b-bu.b);edges++;
   }
   Check(edges>20&&after>=before*.85,"popup foreground text/detail edges remain sharp");
   Debug.Log("BLUR_FOREGROUND_QA edges="+edges+" baseline="+before+" blurred="+after);
  }
  void CheckBlurSamples(Color32[][] samples,string mode){
    double sharpEnergy=0,blurEnergy=0;int changed=0,pink=0,visible=0;
    for(int y=2;y<1438;y+=2)for(int x=2;x<2558;x+=2){int index=y*2560+x;var a=samples[0][index];var b=samples[1][index];if(b.r>220&&b.b>220&&b.g<40)pink++;if(Math.Max(b.r,Math.Max(b.g,b.b))>90)visible++;
     // Pause occupies the center; only compare actual HUD/backdrop pixels outside its bounds.
     if(x>=650&&x<=1910&&y>=280&&y<=1160)continue;
     var ar=samples[0][index+1];var au=samples[0][index+2560];var br=samples[1][index+1];var bu=samples[1][index+2560];
     sharpEnergy+=Math.Abs(a.r-ar.r)+Math.Abs(a.g-ar.g)+Math.Abs(a.b-ar.b)+Math.Abs(a.r-au.r)+Math.Abs(a.g-au.g)+Math.Abs(a.b-au.b);
     blurEnergy+=Math.Abs(b.r-br.r)+Math.Abs(b.g-br.g)+Math.Abs(b.b-br.b)+Math.Abs(b.r-bu.r)+Math.Abs(b.g-bu.g)+Math.Abs(b.b-bu.b);
     if(Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)>6)changed++;
    }
    Check(sharpEnergy>1000&&blurEnergy<sharpEnergy*.95&&changed>100,mode+" popup blur reduces actual HUD/background edge energy");
    Check(visible>100&&pink<100,mode+" blur render contains visible UI without error shader");
    Debug.Log("BLUR_PIXEL_QA "+mode+" sharp="+sharpEnergy+" blurred="+blurEnergy+" changed="+changed);
  }
  IEnumerator CheckOverlayBlurPixels(){
   var blur=ui.GetComponentInChildren<PopupBackdropBlur>();var canvas=ui.GetComponentInChildren<Canvas>();
   Check(canvas&&blur&&blur.Graphic,"overlay blur comparison prerequisites");if(!canvas||!blur||!blur.Graphic)yield break;
   var oldMode=canvas.renderMode;var oldCamera=canvas.worldCamera;var oldDistance=canvas.planeDistance;bool oldEnabled=blur.Graphic.enabled;Texture2D shot=null;
   try{
    canvas.renderMode=RenderMode.ScreenSpaceOverlay;var samples=new Color32[2][];
    for(int pass=0;pass<2;pass++){
     blur.SetRenderingEnabled(pass==1);Canvas.ForceUpdateCanvases();yield return new WaitForEndOfFrame();
     shot=ScreenCapture.CaptureScreenshotAsTexture();Check(shot&&shot.width==2560&&shot.height==1440,"overlay capture has QHD dimensions");
     if(!shot||shot.width!=2560||shot.height!=1440)yield break;
     samples[pass]=shot.GetPixels32();Destroy(shot);shot=null;
    }
    // Hidden-player backbuffers can omit Overlay UI even with blur disabled.
    // This is an unavailable capture, not evidence that the blur passed or failed.
    if(!samples[0].Any(c=>Math.Max(c.r,Math.Max(c.g,c.b))>90)){
     const string reason="Overlay visual check unavailable: hidden-window baseline capture contains no UI. Camera-rendered build verified separately.";
     skipped.Add(reason);Debug.Log("BLUR_OVERLAY_SKIPPED "+reason);
    }else CheckBlurSamples(samples,"overlay");
   }finally{if(shot)Destroy(shot);blur.SetRenderingEnabled(oldEnabled);canvas.renderMode=oldMode;canvas.worldCamera=oldCamera;canvas.planeDistance=oldDistance;Canvas.ForceUpdateCanvases();}
  }
  IEnumerator Capture(){
   Screen.SetResolution(2560,1440,false);yield return null;yield return null;
   ui.Initialize();var previewCanvas=ui.GetComponentInChildren<Canvas>();var previewCamera=FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault(c=>c.gameObject.name=="UI Preview Camera");
   Check(!ui.ShowPreviewBackdrop||!previewCamera||(previewCanvas&&previewCanvas.renderMode==RenderMode.ScreenSpaceCamera&&previewCanvas.worldCamera==previewCamera&&previewCamera.allowMSAA),"preview canvas uses its multisampled camera");
   var hud=FindFirstObjectByType<ReferenceHud>(FindObjectsInactive.Include);var hudIdentity=hud.GetInstanceID();
   // Data slots must remain blank until supplied by the actual game systems.
   Check(hud.MinimapContent&&hud.MinimapContent.childCount==0,"minimap content starts empty");
   var hp=hud.transform.Find("HealthFill") as RectTransform;
   Check(hp&&Mathf.Approximately(hp.rect.width,0),"health fill starts empty");
   ui.SetHudData("128","850 / 1000",.85f,"외부 목표",new[]{"Q","E","R"});yield return null;
   Check(hp&&Mathf.Abs(hp.rect.width-458*.85f)<.1f,"external health ratio applied");
   var hudText=hud.GetComponentsInChildren<TMPro.TextMeshProUGUI>();
   Check(hudText.Any(t=>t.text=="850 / 1000")&&hudText.Any(t=>t.text=="128")&&hudText.Any(t=>t.text=="외부 목표"),"external HUD values applied");
   Check(!hud.AbilityImages[0]&&hud.AbilityImages[1]&&hud.AbilityImages[2],"only original E and R ability images exist");
   Check(!hudText.Any(t=>t.text=="Q")&&hudText.Any(t=>t.text=="E")&&hudText.Any(t=>t.text=="R"),"legacy ability keys omit Q and retain E/R");
   ui.SetHudData(abilityKeys:new[]{"CompactE","CompactR"});yield return null;
   Check(hudText.Any(t=>t.text=="CompactE")&&hudText.Any(t=>t.text=="CompactR"),"compact E/R ability keys applied");
   ui.State.uiData["ability-0"]="RemovedQ";ui.State.uiData["ability-1"]="BoundE";ui.State.uiData["ability-2"]="BoundR";hud.Apply(ui.State);
   Check(!hudText.Any(t=>t.text=="RemovedQ")&&hudText.Any(t=>t.text=="BoundE")&&hudText.Any(t=>t.text=="BoundR"),"original ability data bindings retained without Q");
   ui.State.uiData.Remove("ability-0");ui.State.uiData.Remove("ability-1");ui.State.uiData.Remove("ability-2");
   ui.SetHudData();yield return null;Check(hp&&Mathf.Approximately(hp.rect.width,0),"health resets to blank");
   ui.ShowScreen("main");yield return null;Canvas.ForceUpdateCanvases();var titleMenu=FindFirstObjectByType<TitleMenuPanel>();Check(titleMenu&&titleMenu.Choices.Length==4,"title has four menu choices");
   if(titleMenu){var choice=titleMenu.Choices[0];var choiceRect=(RectTransform)choice.transform;var point=RectTransformUtility.WorldToScreenPoint(previewCamera,choiceRect.TransformPoint(choiceRect.rect.center));var data=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){position=point};var hits=new List<UnityEngine.EventSystems.RaycastResult>();UnityEngine.EventSystems.EventSystem.current.RaycastAll(data,hits);Check(hits.Any(hit=>hit.gameObject==choice.gameObject||hit.gameObject.transform.IsChildOf(choice.transform)),"camera canvas pointer hits main menu choice");}
   titleMenu.Select(2);titleMenu.Select(1);Check(titleMenu.Selected==2,"disabled continue skipped");titleMenu.Choices[2].Activate();Check(ui.CurrentScreen=="settings","title setting action opens popup");ui.Dispatch("close");Check(ui.CurrentScreen=="main","title settings returns to main");
   ui.ShowScreen("hud");ui.Dispatch("go:shop-buy");Check(ui.CurrentScreen=="shop-buy","shop opens");ui.Dispatch("go:shop-sell");ui.Dispatch("close");Check(ui.CurrentScreen=="hud","shop tabs close to HUD");
   ui.ShowScreen("reward-upgrade");ui.Dispatch("reward:1");ui.Dispatch("go:pause");ui.Dispatch("go:settings");ui.Dispatch("setting:sound:37");ui.Dispatch("close");ui.Dispatch("resume");Check(ui.CurrentScreen=="reward-upgrade"&&ui.State.rewardChoice==1,"nested settings preserves reward selection");Check(Mathf.Approximately(ui.State.settings["sound"],37),"settings retained");
   // Popup effects follow the modal stack, including synchronous close/rebuild paths.
   ui.ShowScreen("hud");CheckBlurCount(0,"HUD");ui.Dispatch("go:pause");CheckBlurCount(1,"pause");
   CheckBlurPixels(previewCamera?previewCamera:FindFirstObjectByType<Camera>());
   yield return CheckOverlayBlurPixels();
   ui.Dispatch("go:settings");CheckBlurCount(2,"nested settings");ui.Dispatch("close");CheckBlurCount(1,"settings closed");ui.Dispatch("close");CheckBlurCount(0,"pause closed");
   // Shop lists have one owned grid, readable artwork and screen-specific navigation.
   foreach(string shop in new[]{"shop-buy","shop-sell","shop-synth"}){
    ui.State=new UiState();ui.ShowScreen(shop);yield return null;Canvas.ForceUpdateCanvases();
    bool buyShop=shop=="shop-buy",sellShop=shop=="shop-sell";float ownedWidth=buyShop?590:sellShop?1100:650,ownedX=buyShop?1184:0;int columns=sellShop?4:2;
    var popup=ActiveRect("Popup_"+shop);Check(popup&&Mathf.Abs(popup.localScale.x-1.08f)<.001f&&Mathf.Abs(popup.localScale.y-1.08f)<.001f,"item popup scale is 1.08 "+shop);
    if(popup){var corners=new Vector3[4];popup.GetWorldCorners(corners);var canvasRect=ui.GetComponentInChildren<Canvas>().transform as RectTransform;Check(corners.All(c=>{var v=canvasRect.InverseTransformPoint(c);return v.x>=canvasRect.rect.xMin-.1f&&v.x<=canvasRect.rect.xMax+.1f&&v.y>=canvasRect.rect.yMin-.1f&&v.y<=canvasRect.rect.yMax+.1f;}),"item popup stays within QHD canvas "+shop);}
    var dock=ActiveRect("ShopInventoryPanel");Check(dock&&Mathf.Abs(dock.rect.width-ownedWidth)<.1f&&Mathf.Abs(dock.anchoredPosition.x-ownedX)<.1f,"single owned panel has correct bounds "+shop);
    Check(ui.GetComponentsInChildren<UnityEngine.UI.ScrollRect>().Count(r=>r.name=="ShopOwnedScroll")==1&&ActiveRect("Popup_inventory")==null,"exactly one owned scroll inside shop "+shop);
    var scroll=dock?dock.GetComponentInChildren<UnityEngine.UI.ScrollRect>():null;Check(scroll&&scroll.vertical&&!scroll.horizontal&&scroll.content.rect.height>scroll.viewport.rect.height,"owned list scrolls independently "+shop);
    if(scroll){var cards=scroll.content.Cast<Transform>().Select(c=>c as RectTransform).Where(c=>c&&c.name.StartsWith("LargeItem")).ToArray();Check(cards.Length==ui.State.owned.Count&&cards.Count(c=>Mathf.Abs(c.anchoredPosition.y)<.1f)==columns,"owned grid column count "+shop);Check(cards.All(c=>c.GetComponentsInChildren<RawImage>().Any(art=>art.name.StartsWith("Item_")&&art.rectTransform.rect.width>=150&&art.rectTransform.rect.height>=150)),"owned card artwork is at least 150px "+shop);scroll.verticalNormalizedPosition=.25f;}
    if(buyShop){var stock=ActiveRect("ShopStockScroll");var stockScroll=stock?stock.GetComponent<UnityEngine.UI.ScrollRect>():null;Check(stockScroll!=null,"buy merchant grid remains separate from owned grid");if(stockScroll){var stockCards=stockScroll.content.Cast<Transform>().Select(c=>c as RectTransform).Where(c=>c&&c.name.StartsWith("LargeItem")).ToArray();Check(stockCards.Count(c=>Mathf.Abs(c.anchoredPosition.y)<.1f)==2&&stockCards.All(c=>c.GetComponentsInChildren<RawImage>().Any(art=>art.name.StartsWith("Item_")&&art.rectTransform.rect.width>=150)),"buy stock uses two readable card columns");}}
    int merchantSelection=ui.State.selected;ui.Dispatch("shop-inventory:item:7");yield return null;scroll=ActiveRect("ShopInventoryPanel").GetComponentInChildren<UnityEngine.UI.ScrollRect>();Check(Mathf.Abs(scroll.verticalNormalizedPosition-.25f)<.01f,"inspecting owned item preserves scroll position "+shop);
    Check(ui.State.shopInventorySelected==7,"owned item inspection selects clicked item "+shop);
    if(buyShop){Check(ui.State.shopInspectOwned&&ui.State.selected==merchantSelection,"owned inspection shares detail and preserves merchant selection");ui.Dispatch("shop-focus:stock");Check(!ui.State.shopInspectOwned&&ui.State.selected==merchantSelection,"stock focus restores merchant detail");ui.Dispatch("shop-inventory:item:4");ui.Dispatch("item:2");Check(!ui.State.shopInspectOwned&&ui.State.selected==2,"merchant click restores merchant detail");}
    else if(sellShop)Check(ui.State.selected==7,"sell selection follows owned inspection");
    else Check(ui.State.materials.Count==0,"synthesis card inspection does not add materials");
    ui.Dispatch("shop-inventory:category:4");Check(ui.State.shopInventoryCategory==4&&ui.State.shopInventorySelected>=0&&ui.State.Element(ui.State.shopInventorySelected)==4,"owned filter selects a matching item "+shop);Check(Mathf.Approximately(ui.State.shopInventoryScroll,1),"owned filter resets its own scroll "+shop);
    ui.HandleInventoryToggle();yield return null;
    if(buyShop){Check(ui.CurrentScreen==shop&&!ui.State.shopInventoryOpen&&!ActiveRect("ShopInventoryPanel"),"buy Tab collapses owned grid");var toggle=ActiveRect("ShopInventoryToggle");var button=toggle?toggle.GetComponentInChildren<UnityEngine.UI.Button>():null;Check(button&&button.interactable,"buy inventory toggle remains accessible");if(button)button.onClick.Invoke();yield return null;Check(ui.State.shopInventoryOpen&&ActiveRect("ShopInventoryPanel"),"buy button reopens owned grid");ui.HandleEscape();Check(ui.CurrentScreen==shop&&!ui.State.shopInventoryOpen,"buy first Escape collapses owned grid");ui.HandleEscape();Check(ui.CurrentScreen=="hud","buy second Escape returns to HUD");}
    else {Check(ui.CurrentScreen==shop&&ActiveRect("ShopInventoryPanel")&&ActiveRect("ShopInventoryToggle")==null,"sell and synthesis keep single owned grid on Tab "+shop);ui.HandleEscape();Check(ui.CurrentScreen=="hud","sell and synthesis Escape returns directly to HUD "+shop);}
   }
   ui.State=new UiState();ui.ShowScreen("shop-buy");ui.Dispatch("shop-inventory:item:4");ui.Dispatch("item:2");var purchaseScroll=ActiveRect("ShopInventoryPanel").GetComponentInChildren<UnityEngine.UI.ScrollRect>();purchaseScroll.verticalNormalizedPosition=.25f;int oldOwnedCount=ui.State.owned.Count;ui.Dispatch("buy");yield return null;Check(ui.State.owned.Count==oldOwnedCount+1&&ui.State.selected==2&&ui.State.shopInventorySelected==4&&!ui.State.shopInspectOwned,"purchase refresh preserves owned inspection and merchant focus");Check(Mathf.Abs(ActiveRect("ShopInventoryPanel").GetComponentInChildren<UnityEngine.UI.ScrollRect>().verticalNormalizedPosition-.25f)<.01f,"purchase refresh preserves owned scroll");
   ui.Dispatch("go:shop-sell");ui.Dispatch("shop-inventory:item:4");Check(ui.State.selected==4&&ui.State.shopInventorySelected==4,"sell target follows inspected owned item");var saleScroll=ActiveRect("ShopInventoryPanel").GetComponentInChildren<UnityEngine.UI.ScrollRect>();saleScroll.verticalNormalizedPosition=.25f;ui.Dispatch("sell");yield return null;Check(!ui.State.owned.Contains(4)&&ui.State.owned.Contains(ui.State.shopInventorySelected)&&ui.State.selected==ui.State.shopInventorySelected,"selling inspected item selects a remaining owned item");Check(Mathf.Abs(ActiveRect("ShopInventoryPanel").GetComponentInChildren<UnityEngine.UI.ScrollRect>().verticalNormalizedPosition-.25f)<.01f,"sale refresh preserves owned scroll");
   ui.State.owned.Clear();ui.Refresh();yield return null;Check(ui.State.shopInventorySelected==-1&&ActiveRect("ShopInventoryPanel")&&!ui.GetComponentsInChildren<RectTransform>().Any(r=>r.name.StartsWith("LargeItem")),"empty owned list has no stale card or selection");
   ui.State.owned.Add(3);ui.Dispatch("shop-inventory:category:4");yield return null;Check(ui.State.shopInventorySelected==-1&&ui.State.owned.Count==1&&!ui.GetComponentsInChildren<RectTransform>().Any(r=>r.name.StartsWith("LargeItem")),"empty filter hides cards while retaining owned item");ui.Dispatch("shop-inventory:category:-1");Check(ui.State.shopInventorySelected==3,"clearing inventory filter restores a valid inspected item");
   ui.State=new UiState();ui.ShowScreen("shop-synth");ui.Dispatch("shop-inventory:item:0");Check(ui.State.materials.Count==0,"synthesis inspection requires explicit material add");ui.Dispatch("material:0");Check(ui.State.materials.SequenceEqual(new[]{0}),"explicit synthesis material action adds inspected item");ui.Dispatch("material:0");Check(ui.State.materials.Count==0,"explicit synthesis material action removes selected item");ui.Dispatch("material:0");ui.Dispatch("material:1");ui.Dispatch("material:2");ui.Dispatch("out:3");ui.Dispatch("shop-inventory:item:4");ui.Dispatch("shop-inventory:category:4");Check(ui.State.materials.SequenceEqual(new[]{0,1,2})&&ui.State.outElement==3,"owned inspection preserves explicit synthesis recipe");ui.Dispatch("confirm:synth");ui.HandleInventoryToggle();Check(ui.CurrentScreen=="confirm","Tab does not bypass synthesis confirmation");ui.Dispatch("cancel");Check(ui.State.materials.Count==3&&ui.State.shopInventorySelected==4,"cancel keeps materials and owned inspection");ui.Dispatch("confirm:synth");ui.Dispatch("confirm");Check(ui.State.materials.Count==0&&ui.State.owned.Count==10&&ui.State.Grade(ui.State.selected)==4&&ui.State.Element(ui.State.selected)==3,"synthesis commits 3/3/2 to selected element grade 4");Check(ui.State.owned.Contains(ui.State.shopInventorySelected),"synthesis leaves a valid inspected item");
   ui.ShowScreen("hud");Check(FindFirstObjectByType<ReferenceHud>(FindObjectsInactive.Include).GetInstanceID()==hudIdentity,"HUD identity preserved");
   Check(BoundaryUIController.ScreenIds.Length==18&&!BoundaryUIController.ScreenIds.Contains("entry")&&!BoundaryUIController.ScreenIds.Contains("route")&&BoundaryUIController.ScreenIds.Contains("loading"),"catalogue has 18 active screens with loading and no entry/route");
   ui.ShowScreen("main");ui.ShowScreen("entry");Check(ui.CurrentScreen=="main","removed entry cannot open");ui.ShowScreen("route");Check(ui.CurrentScreen=="main","removed route cannot open");
   ui.ShowScreen("wheel");yield return null;Canvas.ForceUpdateCanvases();
   CheckFullscreen(ActiveRect("WheelOverlay"),"wheel overlay");var wheel=ActiveRect("WheelSurface");
   Check(wheel&&Mathf.Abs(wheel.rect.width-800)<.1f&&Mathf.Abs(wheel.rect.height-800)<.1f,"wheel is 800 square");
   if(wheel){var canvasRect=ui.GetComponentInChildren<Canvas>().transform as RectTransform;Vector3 center=canvasRect.InverseTransformPoint(wheel.TransformPoint(wheel.rect.center));Check(Vector2.Distance(center,canvasRect.rect.center)<.1f,"wheel center matches canvas center");}
   Check(!ui.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Any(t=>t.text.Contains("두 번째 타격 이후")),"wheel has no old sidebar explanation");
   if(wheel){var wc=wheel.GetComponentInParent<Canvas>();var cam=wc.renderMode==RenderMode.ScreenSpaceOverlay?null:wc.worldCamera;var expected=wheel.rect.center+new Vector2(0,240);var screenPoint=RectTransformUtility.WorldToScreenPoint(cam,wheel.TransformPoint(expected));Vector2 actual;bool converted=RectTransformUtility.ScreenPointToLocalPointInRectangle(wheel,screenPoint,cam,out actual);Vector2 direction=actual-wheel.rect.center;int sector=Mathf.FloorToInt(Mathf.Repeat(Mathf.Atan2(direction.x,direction.y)*Mathf.Rad2Deg+36,360)/72);Check(converted&&Vector2.Distance(actual,expected)<.1f&&Mathf.Abs(direction.magnitude-240)<.1f&&sector==0,"camera canvas wheel pointer remains aligned");}
   foreach(string result in new[]{"clear","death"}){
    ui.ShowScreen(result);yield return null;Canvas.ForceUpdateCanvases();
    var resultRoot=ActiveRect("FullScreenResult_"+result);
    Check(ui.CurrentScreen==result&&resultRoot!=null,"result uses full screen "+result);CheckFullscreen(resultRoot,result);
    Check(!hud.gameObject.activeInHierarchy&&hud.GetInstanceID()==hudIdentity,"persistent HUD hidden on "+result);
    Check(ActiveRect("Popup_"+result)==null,"result has no popup "+result);
    Check(!ui.GetComponentsInChildren<Button>().Any(b=>b.name=="Button_×"),"result has no close X "+result);
    ui.HandleEscape();Check(ui.CurrentScreen=="main"&&!hud.gameObject.activeInHierarchy,"result Esc returns to main "+result);
    ui.ShowScreen(result);yield return null;
    resultRoot=ActiveRect("FullScreenResult_"+result);
    var back=resultRoot?resultRoot.GetComponentsInChildren<Button>().FirstOrDefault(b=>b.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Any(t=>t.text.Contains("거점")||t.text.Contains("메인"))):null;
    Check(back!=null,"result main return button exists "+result);if(back)back.onClick.Invoke();
    Check(ui.CurrentScreen=="main"&&!hud.gameObject.activeInHierarchy,"result button returns to main "+result);
    ui.ShowScreen(result);ui.Dispatch("go:main");Check(ui.CurrentScreen=="main","result return action clears modal "+result);
   }
   ui.ShowScreen("reward-boss");ui.Dispatch("reward:1");ui.Dispatch("take-reward");yield return null;
   Check(ui.CurrentScreen=="clear"&&!hud.gameObject.activeInHierarchy&&ActiveRect("FullScreenResult_clear"),"boss reward opens fullscreen clear with HUD hidden");
   ui.State=new UiState();Check(!ui.State.loadingProgress.HasValue,"loading defaults to unknown progress");ui.ShowScreen("loading");yield return null;Canvas.ForceUpdateCanvases();
   CheckFullscreen(ActiveRect("FullScreenLoading"),"loading");Check(!hud.gameObject.activeInHierarchy&&ActiveRect("Popup_loading")==null,"loading hides HUD and is not popup");
   Check(!ui.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Any(t=>t.text.Contains("%")),"unknown loading has no invented percentage");
   var seal=ui.GetComponentInChildren<LoadingSealGraphic>();var loadingDisplay=ui.GetComponentInChildren<LegacyLoadingDisplay>();
   Check(seal&&!seal.Determinate&&loadingDisplay.Signal.gameObject.activeSelf&&!loadingDisplay.Track.gameObject.activeSelf,"unknown loading uses activity animation");
   ui.SetLoadingProgress(.42f,"외부 로딩 상태");yield return null;Check(ui.State.loadingProgress.HasValue&&Mathf.Approximately(ui.State.loadingProgress.Value,.42f),"external loading ratio applied");
   Check(ui.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Any(t=>t.text.Contains("외부 로딩 상태")),"external loading status applied");
   Check(seal&&seal.Determinate&&Mathf.Approximately(seal.Progress,.42f)&&Mathf.Abs(loadingDisplay.Track.rect.width-520*.42f)<.1f&&!loadingDisplay.Signal.gameObject.activeSelf,"measured progress drives seal and rail");
   ui.SetLoadingProgress(2f);Check(ui.State.loadingProgress.HasValue&&Mathf.Approximately(ui.State.loadingProgress.Value,1f),"loading ratio upper bound normalized");ui.SetLoadingProgress(-1f);Check(ui.State.loadingProgress.HasValue&&Mathf.Approximately(ui.State.loadingProgress.Value,0f),"loading ratio lower bound normalized");
   ui.SetLoadingProgress(null);ui.FinishLoading();Check(ui.CurrentScreen=="hud"&&hud.gameObject.activeInHierarchy,"loading completion restores HUD");
   ui.ShowScreen("main");yield return null;titleMenu=FindFirstObjectByType<TitleMenuPanel>();if(titleMenu)titleMenu.Choices[0].Activate();Check(ui.CurrentScreen=="loading","main new game opens loading immediately");ui.FinishLoading();
   ui.ShowScreen("reward-item");ui.Dispatch("reward:0");ui.Dispatch("take-reward");Check(ui.CurrentScreen=="loading","item reward proceeds through loading");ui.FinishLoading();Check(ui.CurrentScreen=="hud","item reward loading completes to HUD");
   for(int d=0;d<3;d++){
    ui.Design=d;ui.State=new UiState();string dir=Path.Combine(output,"set-"+(char)('a'+d));Directory.CreateDirectory(dir);
    foreach(string id in BoundaryUIController.ScreenIds){ui.ShowScreen(id);yield return null;yield return null;Canvas.ForceUpdateCanvases();yield return new WaitForEndOfFrame();
     CheckBlurCount(new[]{"main","hud","clear","death","loading","wheel"}.Contains(id)?0:1,"catalogue "+d+" "+id);
     foreach(var text in FindObjectsByType<TMPro.TextMeshProUGUI>(FindObjectsSortMode.None)){if(!text.gameObject.activeInHierarchy)continue;if(text.isTextOverflowing)Debug.Log("TEXT_OVERFLOW "+id+" "+text.text.Replace('\n',' '));}
     // Explicit offscreen rendering remains reliable when the test window is hidden.
     var canvas=ui.GetComponentInChildren<Canvas>();var camera=previewCamera?previewCamera:FindFirstObjectByType<Camera>();
     var descriptor=new RenderTextureDescriptor(5120,2880,RenderTextureFormat.ARGB32,24){msaaSamples=4};
     descriptor.msaaSamples=SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor);
     var rt=RenderTexture.GetTemporary(descriptor);var qhd=RenderTexture.GetTemporary(2560,1440,0,RenderTextureFormat.ARGB32);
     var scaler=canvas.GetComponent<CanvasScaler>();var oldScalerEnabled=scaler&&scaler.enabled;var oldScale=canvas.scaleFactor;var oldMode=canvas.renderMode;var oldCamera=canvas.worldCamera;var oldDistance=canvas.planeDistance;var oldMask=camera.cullingMask;var oldMSAA=camera.allowMSAA;var oldTarget=camera.targetTexture;var prev=RenderTexture.active;Texture2D shot=null;
     try{
      canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;camera.cullingMask=-1;camera.allowMSAA=true;camera.targetTexture=rt;if(scaler)scaler.enabled=false;canvas.scaleFactor=2;Canvas.ForceUpdateCanvases();camera.Render();
      // Sampling the default bindMS=false target resolves MSAA automatically.
      // At exactly half resolution, bilinear texel-center samples average each 2x2 block once.
      rt.filterMode=FilterMode.Bilinear;Graphics.Blit(rt,qhd);
      RenderTexture.active=qhd;shot=new Texture2D(2560,1440,TextureFormat.RGBA32,false);shot.ReadPixels(new Rect(0,0,2560,1440),0,0);shot.Apply();
      var pixels=shot.GetPixels32();int visible=0;byte minimum=255,maximum=0;for(int p=0;p<pixels.Length;p+=16){byte light=Math.Max(pixels[p].r,Math.Max(pixels[p].g,pixels[p].b));minimum=Math.Min(minimum,light);maximum=Math.Max(maximum,light);if(light>90)visible++;}Check(maximum-minimum>60&&visible>30,"capture contains visible UI "+d+" "+id);
      File.WriteAllBytes(Path.Combine(dir,id+".png"),shot.EncodeToPNG());Debug.Log("CAPTURED "+d+" "+id);
     }finally{
      RenderTexture.active=prev;if(shot)Destroy(shot);camera.targetTexture=oldTarget;camera.cullingMask=oldMask;camera.allowMSAA=oldMSAA;canvas.renderMode=oldMode;canvas.worldCamera=oldCamera;canvas.planeDistance=oldDistance;canvas.scaleFactor=oldScale;if(scaler)scaler.enabled=oldScalerEnabled;
      RenderTexture.ReleaseTemporary(rt);RenderTexture.ReleaseTemporary(qhd);Canvas.ForceUpdateCanvases();
     }
    }
   }
   ui.State=new UiState();ui.ShowScreen("shop-sell");foreach(int item in ui.State.owned.ToArray()){ui.Dispatch("item:"+item);ui.Dispatch("sell");}yield return null;Check(ui.State.owned.Count==0,"all items sold cleanly");
   File.WriteAllLines(Path.Combine(output,"runtime-qa.txt"),new[]{"Screens: "+(BoundaryUIController.ScreenIds.Length*3),"Resolution: "+Screen.width+"x"+Screen.height,"Errors: "+errors.Count,"Skipped checks: "+skipped.Count}.Concat(errors).Concat(skipped));
   Debug.Log("BOUNDARY_UI_CAPTURE_DONE errors="+errors.Count);Application.Quit(errors.Count==0?0:1);
  }
 }
}



