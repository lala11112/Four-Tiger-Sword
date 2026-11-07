using UnityEngine;
namespace EasternLab.GateAddon {
public enum GateState { Closed, Opening, Open, Closing }
/// <summary>Panel-operated boundary. The solid aperture remains blocked until conversion completes.</summary>
[ExecuteAlways]
public sealed class GateActivation : MonoBehaviour {
 public GateTransit transit;
 public Renderer apertureRenderer;
 public Renderer spatialWindowRenderer;
 public int apertureMaterialIndex;
 public BoxCollider apertureBlocker, clearanceVolume;
 public Renderer statusRenderer;
 public int statusMaterialIndex;
 public Texture closedStatus, openingStatus, openStatus;
 [Min(.01f)] public float openingDuration=2.4f, closingDuration=2.4f;
 public GateState State {get;private set;}=GateState.Closed;
 public float NormalizedOpenAmount {get;private set;}
 public float normalizedOpenAmount => NormalizedOpenAmount;
 public bool CanTraverse => isActiveAndEnabled && State==GateState.Open&&(!transit||transit.HasDestination);
 public string LastResult {get;private set;}="경계 닫힘 · 오른쪽 단말에서 경계를 개방하세요.";
 MaterialPropertyBlock apertureProperties,statusProperties;
 static readonly int OpenAmount=Shader.PropertyToID("_OpenAmount"), BaseMap=Shader.PropertyToID("_BaseMap"),MainTex=Shader.PropertyToID("_MainTex"),EmissionMap=Shader.PropertyToID("_EmissionMap");
 void OnEnable(){State=GateState.Closed;NormalizedOpenAmount=0;LastResult="경계 닫힘 · 오른쪽 단말에서 경계를 개방하세요.";ApplyVisuals();SetBlocked(true);}
 void OnValidate(){openingDuration=Mathf.Max(.01f,openingDuration);closingDuration=Mathf.Max(.01f,closingDuration);if(!Application.isPlaying){State=GateState.Closed;NormalizedOpenAmount=0;ApplyVisuals();}}
 public bool Open(){if(!Application.isPlaying||!isActiveAndEnabled||State!=GateState.Closed)return false;State=GateState.Opening;LastResult="경계 개방 중 · 현실 공간을 사이버 공간으로 변환합니다.";SetBlocked(true);ApplyVisuals();return true;}
 public bool Close(){if(!Application.isPlaying||!isActiveAndEnabled||State!=GateState.Open)return false;if(HasTravellerInClearance()){LastResult="통로 안에 사람이 있습니다. 통로를 비운 뒤 경계를 닫으세요.";return false;}State=GateState.Closing;LastResult="경계 폐쇄 중 · 통과할 수 없습니다.";SetBlocked(true);ApplyVisuals();return true;}
 void Update(){if(!Application.isPlaying)return;if(State==GateState.Open){SetBlocked(!CanTraverse);LastResult=CanTraverse?"경계 개방 완료 · 문 안쪽으로 걸어 들어가세요.":"경계 개방 완료 · 이동 목적지를 설정해야 통과할 수 있습니다.";}if(State==GateState.Opening){NormalizedOpenAmount=Mathf.MoveTowards(NormalizedOpenAmount,1,Time.deltaTime/openingDuration);if(NormalizedOpenAmount>=1){State=GateState.Open;LastResult=CanTraverse?"경계 개방 완료 · 문 안쪽으로 걸어 들어가세요.":"경계 개방 완료 · 이동 목적지를 설정해야 통과할 수 있습니다.";SetBlocked(!CanTraverse);}ApplyVisuals();}else if(State==GateState.Closing){NormalizedOpenAmount=Mathf.MoveTowards(NormalizedOpenAmount,0,Time.deltaTime/closingDuration);if(NormalizedOpenAmount<=0){State=GateState.Closed;LastResult="경계 닫힘 · 오른쪽 단말에서 경계를 개방하세요.";}ApplyVisuals();}}
 void SetBlocked(bool blocked){if(apertureBlocker)apertureBlocker.enabled=blocked;}
 public bool HasTravellerInClearance(){
  var volume=clearanceVolume?clearanceVolume:apertureBlocker;if(!volume)return false;
  Physics.SyncTransforms();var t=volume.transform;var scale=t.lossyScale;var half=Vector3.Scale(volume.size*.5f,new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z)));
  // Query only live actors, so the terminal and structural gate geometry never prevent closing.
  foreach(var hit in Physics.OverlapBox(t.TransformPoint(volume.center),half,t.rotation,~0,QueryTriggerInteraction.Collide)){
   var cc=hit.GetComponentInParent<CharacterController>();if(cc&&cc.enabled&&cc.gameObject.activeInHierarchy)return true;
   var body=hit.attachedRigidbody;if(body&&body.gameObject.activeInHierarchy&&!hit.isTrigger)return true;
  }return false;
 }
 public void ApplyVisuals(){
  // The front seal is absent once open; the actual spatial view lives behind the throat.
  if(apertureRenderer)apertureRenderer.enabled=State!=GateState.Open;
  if(spatialWindowRenderer)spatialWindowRenderer.enabled=NormalizedOpenAmount>0;
  if(apertureRenderer&&apertureMaterialIndex>=0&&apertureMaterialIndex<apertureRenderer.sharedMaterials.Length){if(apertureProperties==null)apertureProperties=new MaterialPropertyBlock();apertureRenderer.GetPropertyBlock(apertureProperties,apertureMaterialIndex);apertureProperties.SetFloat(OpenAmount,NormalizedOpenAmount);apertureRenderer.SetPropertyBlock(apertureProperties,apertureMaterialIndex);}
  var texture=State==GateState.Open?openStatus:(State==GateState.Opening||State==GateState.Closing?openingStatus:closedStatus);
  if(statusRenderer&&texture&&statusMaterialIndex>=0&&statusMaterialIndex<statusRenderer.sharedMaterials.Length){if(statusProperties==null)statusProperties=new MaterialPropertyBlock();statusRenderer.GetPropertyBlock(statusProperties,statusMaterialIndex);statusProperties.SetTexture(BaseMap,texture);statusProperties.SetTexture(MainTex,texture);statusProperties.SetTexture(EmissionMap,texture);statusRenderer.SetPropertyBlock(statusProperties,statusMaterialIndex);}
 }
}
}
