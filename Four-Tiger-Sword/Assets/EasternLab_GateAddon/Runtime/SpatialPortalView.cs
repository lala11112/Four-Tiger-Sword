using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EasternLab.GateAddon {
/// <summary>A perspective-correct window into real geometry at exitFrame. Local XY is the aperture, +Z faces the visitor.</summary>
[ExecuteAlways, DisallowMultipleComponent]
public sealed class SpatialPortalView : MonoBehaviour {
 public Transform exitFrame;
 public Renderer windowRenderer;
 public GateActivation activation;
 [Min(64)] public int maximumWidth=960, maximumHeight=540;
 [Min(0)] public float clipPlaneOffset=.015f;
 // Assign the existing player camera; MainCamera is only a fallback.
 public Camera playerCamera;
 // Remote geometry must exclusively use this mask (never include shared player/world layers).
 public LayerMask remoteCullingMask=1<<30;
 // Serialized destination renderers remain hidden outside the private portal render.
 public Renderer[] fieldRenderers=new Renderer[0];
 void ApplyFieldVisibility(bool visible){foreach(var renderer in fieldRenderers)if(renderer)renderer.forceRenderingOff=!visible;}
 public Color voidColor=new Color(.003f,.009f,.025f,1f);
 public int RenderCount {get;private set;}
 public Vector3 LastSourceCameraPosition {get;private set;}
 public Camera PortalCamera => portalCamera;
 public RenderTexture PortalTexture => portalTexture;
 Camera portalCamera;
 RenderTexture portalTexture;
 MaterialPropertyBlock properties;
 readonly Plane[] sourceFrustum=new Plane[6];
 bool rendering;
 static readonly int PortalTex=Shader.PropertyToID("_PortalTex"), Projector=Shader.PropertyToID("_PortalProjector");
 void OnEnable(){ApplyFieldVisibility(false);}
 void LateUpdate(){if(Application.isPlaying){var source=playerCamera?playerCamera:Camera.main;ApplyFieldVisibility(false);RenderForCamera(source);}}
 void OnDisable(){ApplyFieldVisibility(false);Release();}
 void OnDestroy(){Release();}
 void OnValidate(){maximumWidth=Mathf.Max(64,maximumWidth);maximumHeight=Mathf.Max(64,maximumHeight);clipPlaneOffset=Mathf.Max(0,clipPlaneOffset);}
 /// <summary>Call immediately before a manual/editor capture. Force bypasses closed-gate and offscreen checks, but still requires an enabled window.</summary>
 public void RenderForCamera(Camera source,bool force=false){
  if(rendering||!isActiveAndEnabled||!source||source==portalCamera||!exitFrame||!windowRenderer||!windowRenderer.enabled||!windowRenderer.gameObject.activeInHierarchy)return;
  if(!force){
   if(activation&&activation.NormalizedOpenAmount<=0)return;
   GeometryUtility.CalculateFrustumPlanes(source,sourceFrustum);
   if(!GeometryUtility.TestPlanesAABB(sourceFrustum,windowRenderer.bounds))return;
  }
  float aspect=Mathf.Max(.01f,source.aspect);
  int width=Mathf.Max(1,Mathf.Min(maximumWidth,Mathf.RoundToInt(maximumHeight*aspect)));
  int height=Mathf.Max(1,Mathf.RoundToInt(width/aspect));
  EnsureResources(width,height);
  portalCamera.CopyFrom(source);
  // The destination field has its own void, independent of the visitor camera's skybox/background.
  portalCamera.clearFlags=CameraClearFlags.SolidColor;
  portalCamera.backgroundColor=voidColor;
  portalCamera.enabled=false;
  portalCamera.targetTexture=portalTexture;
  portalCamera.rect=new Rect(0,0,1,1);
  portalCamera.cullingMask=remoteCullingMask;
  portalCamera.allowMSAA=false;
  // Matching uniform parent scale is supported; use equal-scale, unsheared source and exit frames.
  // Half-turn maps the source front to the destination interior.
  Matrix4x4 mapping=exitFrame.localToWorldMatrix*Matrix4x4.Rotate(Quaternion.Euler(0,180,0))*transform.worldToLocalMatrix;
  Matrix4x4 pose=mapping*source.transform.localToWorldMatrix;
  portalCamera.transform.SetPositionAndRotation(pose.GetColumn(3),Quaternion.LookRotation(pose.GetColumn(2),pose.GetColumn(1)));
  portalCamera.ResetWorldToCameraMatrix();
  portalCamera.projectionMatrix=source.projectionMatrix;
  // Retain the destination's +Z half-space (the actual room) and discard geometry between camera and entry.
  Vector3 point=exitFrame.position+exitFrame.forward*clipPlaneOffset;
  Vector3 normal=exitFrame.forward;
  Matrix4x4 view=portalCamera.worldToCameraMatrix;
  Vector3 cameraPoint=view.MultiplyPoint(point), cameraNormal=view.MultiplyVector(normal).normalized;
  Vector4 plane=new Vector4(cameraNormal.x,cameraNormal.y,cameraNormal.z,-Vector3.Dot(cameraPoint,cameraNormal));
  portalCamera.projectionMatrix=portalCamera.CalculateObliqueMatrix(plane);
  // Project source geometry into the render-target convention. Shader sampling also handles UV-origin differences.
  Matrix4x4 projector=GL.GetGPUProjectionMatrix(source.projectionMatrix,true)*source.worldToCameraMatrix;
  if(properties==null)properties=new MaterialPropertyBlock();
  windowRenderer.GetPropertyBlock(properties);
  properties.SetTexture(PortalTex,portalTexture);
  properties.SetMatrix(Projector,projector);
  windowRenderer.SetPropertyBlock(properties);
  var request=new UniversalRenderPipeline.SingleCameraRequest{destination=portalTexture};
  bool wasEnabled=windowRenderer.enabled;
  rendering=true;
  try{
   windowRenderer.enabled=false;
   ApplyFieldVisibility(true);
   RenderPipeline.SubmitRenderRequest(portalCamera,request);
   LastSourceCameraPosition=source.transform.position;
   RenderCount++;
  }finally{ApplyFieldVisibility(false);windowRenderer.enabled=wasEnabled;rendering=false;}
 }
 void EnsureResources(int width,int height){
  if(!portalCamera){
   var go=new GameObject("Spatial portal render camera"){hideFlags=HideFlags.HideAndDontSave};
   portalCamera=go.AddComponent<Camera>();portalCamera.enabled=false;
   var data=go.AddComponent<UniversalAdditionalCameraData>();
   data.renderType=CameraRenderType.Base;data.renderPostProcessing=false;data.renderShadows=false;
   data.requiresColorOption=CameraOverrideOption.Off;data.requiresDepthOption=CameraOverrideOption.Off;
  }
  if(portalTexture&&portalTexture.width==width&&portalTexture.height==height)return;
  if(portalTexture){portalCamera.targetTexture=null;portalTexture.Release();DestroyResource(portalTexture);}
  portalTexture=new RenderTexture(width,height,24,RenderTextureFormat.DefaultHDR){name="Spatial portal live 3D",hideFlags=HideFlags.HideAndDontSave,antiAliasing=1,useMipMap=false,autoGenerateMips=false,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
  portalTexture.Create();
 }
 void Release(){
  if(portalCamera){portalCamera.targetTexture=null;DestroyResource(portalCamera.gameObject);portalCamera=null;}
  if(portalTexture){portalTexture.Release();DestroyResource(portalTexture);portalTexture=null;}
 }
 static void DestroyResource(Object resource){if(Application.isPlaying)Destroy(resource);else DestroyImmediate(resource);}
}
}
