using UnityEngine;

namespace EasternLab.GateAddon {
/// <summary>Optional local-player-only data conversion, played immediately before transport.</summary>
[DisallowMultipleComponent]
public sealed class CyberTransition : MonoBehaviour {
 public Material transitionMaterial;
 public Camera playerCamera;
 [Min(.1f)] public float duration=.75f;

 public static bool IsLocalTraveller(Transform traveller,Camera supplied=null){
  if(!traveller)return false;
  var source=supplied?supplied:Camera.main;
  if(source&&source.isActiveAndEnabled&&source.targetTexture==null){
   if(source.transform.IsChildOf(traveller))return true;
   // Common external third-person rigs target a CharacterController. Avoid tinting for physics props.
   if(traveller.GetComponent<CharacterController>()&&source==Camera.main)return true;
  }
  foreach(var camera in traveller.GetComponentsInChildren<Camera>(false))if(camera.isActiveAndEnabled&&camera.targetTexture==null)return true;
  return false;
 }
 public static void PlayOn(Transform traveller,Material fallback=null) {
  if(!traveller)return;
  var effect=traveller.GetComponent<CyberTransition>();
  if(effect&&effect.isActiveAndEnabled){effect.Play();return;}
  if(fallback&&fallback.shader&&fallback.shader.isSupported&&IsLocalTraveller(traveller))CyberTransitionPlayback.Begin(fallback,.75f);
 }

 public void Play() {
  if(!transitionMaterial||!transitionMaterial.shader||!transitionMaterial.shader.isSupported)return;
  // A physics traveller without its own active first-person camera must not tint the viewer.
  var cameras=GetComponentsInChildren<Camera>(false);
  bool local=playerCamera&&playerCamera.isActiveAndEnabled&&playerCamera.targetTexture==null;
  foreach(var camera in cameras)if(camera.isActiveAndEnabled&&camera.targetTexture==null){local=true;break;}
  if(!local)return;
  CyberTransitionPlayback.Begin(transitionMaterial,Mathf.Max(.1f,duration));
 }
}

// Separate lifetime keeps the short reveal alive when the traveller's scene is unloaded.
internal sealed class CyberTransitionPlayback : MonoBehaviour {
 static CyberTransitionPlayback active;
 static readonly int Progress=Shader.PropertyToID("_Progress");
 Material ownedMaterial;
 float elapsed,playDuration;

 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
 static void ResetStatic(){active=null;}

 internal static void Begin(Material source,float duration) {
  if(active){active.Restart(source,duration);return;}
  var root=new GameObject("CyberConversion",typeof(RectTransform),typeof(Canvas));
  DontDestroyOnLoad(root);
  var canvas=root.GetComponent<Canvas>();
  canvas.renderMode=RenderMode.ScreenSpaceOverlay;
  canvas.sortingOrder=200;
  var overlay=new GameObject("DataReconstruction",typeof(RectTransform),typeof(UnityEngine.UI.Image));
  overlay.transform.SetParent(root.transform,false);
  var rect=overlay.GetComponent<RectTransform>();
  rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;
  rect.offsetMin=Vector2.zero;rect.offsetMax=Vector2.zero;
  var image=overlay.GetComponent<UnityEngine.UI.Image>();
  image.raycastTarget=false;
  active=root.AddComponent<CyberTransitionPlayback>();
  active.Restart(source,duration);
 }

 void Restart(Material source,float duration) {
  if(ownedMaterial)Destroy(ownedMaterial);
  ownedMaterial=new Material(source){name="CyberConversion (Runtime)"};
  GetComponentInChildren<UnityEngine.UI.Image>().material=ownedMaterial;
  elapsed=0;playDuration=duration;
  ownedMaterial.SetFloat(Progress,0);
 }
 void Update(){
  elapsed+=Time.unscaledDeltaTime;
  if(elapsed>=playDuration){Destroy(gameObject);return;}
  if(ownedMaterial)ownedMaterial.SetFloat(Progress,elapsed/playDuration);
 }
 void OnDestroy(){if(ownedMaterial)Destroy(ownedMaterial);if(active==this)active=null;}
}
}
