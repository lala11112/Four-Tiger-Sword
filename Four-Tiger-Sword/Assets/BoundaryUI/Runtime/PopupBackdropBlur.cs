using UnityEngine;

namespace BoundaryZeroUI {
 [RequireComponent(typeof(UnityEngine.UI.Image))]
 public sealed class PopupBackdropBlur : MonoBehaviour {
  public UnityEngine.UI.Image Graphic { get; private set; }
  public bool IsSupported { get; private set; }
  Material blurMaterial,verticalMaterial;
  UnityEngine.UI.Image verticalGraphic;
  Canvas rootCanvas;
  static readonly int BlurPixels = Shader.PropertyToID("_BlurPixels");
  static readonly int BlurDirection = Shader.PropertyToID("_BlurDirection");

  void Awake() {
   Graphic=GetComponent<UnityEngine.UI.Image>();
   Graphic.raycastTarget=false;
   var canvas=GetComponentInParent<Canvas>();
   rootCanvas=canvas?canvas.rootCanvas:null;
   var shader=Resources.Load<Shader>("BoundaryUI/PopupBackdropBlur");
   IsSupported=shader&&shader.isSupported&&UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline==null;
   if(!IsSupported){Graphic.enabled=false;return;}
   blurMaterial=new Material(shader){name="Popup backdrop blur",hideFlags=HideFlags.HideAndDontSave};
   blurMaterial.SetVector(BlurDirection,new Vector4(1,0,0,0));
   Graphic.material=blurMaterial;
   var vertical=new GameObject("BlurVertical",typeof(RectTransform),typeof(UnityEngine.UI.Image));
   var rect=vertical.GetComponent<RectTransform>();rect.SetParent(transform,false);
   rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
   verticalGraphic=vertical.GetComponent<UnityEngine.UI.Image>();verticalGraphic.raycastTarget=false;
   verticalMaterial=new Material(shader){name="Popup backdrop blur vertical",hideFlags=HideFlags.HideAndDontSave};
   verticalMaterial.SetVector(BlurDirection,new Vector4(0,1,0,0));verticalGraphic.material=verticalMaterial;
   SyncRadius();
  }
  void OnEnable(){Canvas.willRenderCanvases+=SyncRadius;}
  void OnDisable(){Canvas.willRenderCanvases-=SyncRadius;}
  public void SetRenderingEnabled(bool value){if(Graphic)Graphic.enabled=value&&IsSupported;if(verticalGraphic)verticalGraphic.enabled=value&&IsSupported;}
  void SyncRadius(){float pixels=rootCanvas?rootCanvas.scaleFactor:1f;if(blurMaterial)blurMaterial.SetFloat(BlurPixels,pixels);if(verticalMaterial)verticalMaterial.SetFloat(BlurPixels,pixels);}
  void OnDestroy(){if(blurMaterial)Destroy(blurMaterial);if(verticalMaterial)Destroy(verticalMaterial);}
 }
}
