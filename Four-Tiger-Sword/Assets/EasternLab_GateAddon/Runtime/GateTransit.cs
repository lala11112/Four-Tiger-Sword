using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
namespace EasternLab.GateAddon {
/// <summary>A directional, aperture checked walking portal. Optional panel-controlled activation prerequisite.</summary>
[RequireComponent(typeof(BoxCollider))]
public sealed class GateTransit : MonoBehaviour {
 public GateActivation activation;
 public Material transitionMaterial;
 public Transform playerRoot;
 public Camera playerCamera;
 [Tooltip("Optional destination scene; must be present and enabled in Build Settings. An invalid name blocks traversal.")]
 public string targetScene;
 [Tooltip("Let an onTraverse listener own destination scene loading instead of loading it here.")]
 public bool externalSceneLoading;
 public bool HasDestination => !string.IsNullOrWhiteSpace(targetScene)?Application.CanStreamedLevelBeLoaded(targetScene):(portalExitFrame||demoExit);
 public bool IsDestinationConfigured => HasDestination;
 public Transform demoExit;
 public Transform portalExitFrame;
 public float crossingPlaneOffset=0;
 public bool preservePortalPose=true, playConversionEffect=true;
 public Vector3 CrossingPlanePosition => transform.TransformPoint(new Vector3(0,0,crossingPlaneOffset));
 public UnityEvent onTraverse=new UnityEvent();
 public float apertureRadius=1.745f, armDistance=.20f, crossingThreshold=.025f, cooldown=.8f;
 public string destinationLabel="연결된 목적지";
 public int TraversalCount {get;private set;}
 public string LastResult {get;private set;}="통로 연결 정상";
 public Transform LastTraveller {get;private set;}
 sealed class Track {public Transform actor;public CharacterController cc;public Rigidbody rb;public float previous;public bool armed; public readonly HashSet<int> colliders=new HashSet<int>();}
 readonly Dictionary<int,Track> tracks=new Dictionary<int,Track>();
 static readonly Dictionary<int,float> blockedUntil=new Dictionary<int,float>();
 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetCooldowns(){blockedUntil.Clear();}
 void Awake(){var box=GetComponent<BoxCollider>();box.isTrigger=true;if(activation&&!activation.transit)activation.transit=this;}
 void OnDisable(){tracks.Clear();}
 void OnTriggerEnter(Collider other){Register(other);}
 void OnTriggerStay(Collider other){Register(other);}
 void OnTriggerExit(Collider other){
  var actor=Resolve(other,out _,out _);if(!actor)return;
  int id=actor.GetInstanceID();if(!tracks.TryGetValue(id,out var state))return;
  Evaluate(id,state);state.colliders.Remove(other.GetInstanceID());
  if(state.colliders.Count==0)tracks.Remove(id);
 }
 Transform Resolve(Collider c,out CharacterController cc,out Rigidbody rb){
  cc=c.GetComponentInParent<CharacterController>();rb=c.attachedRigidbody;
  var actor=cc?cc.transform:(rb?rb.transform:null);
  if(!actor)return null;
  if(playerRoot)return actor==playerRoot||actor.IsChildOf(playerRoot)?actor:null;
  var camera=playerCamera?playerCamera:Camera.main;
  if(camera&&camera.isActiveAndEnabled&&camera.targetTexture==null){
   var cameraController=camera.GetComponentInParent<CharacterController>();
   var cameraBody=camera.GetComponentInParent<Rigidbody>();
   if((cameraController&&actor==cameraController.transform)||(cameraBody&&actor==cameraBody.transform))return actor;
  }
  for(var parent=actor;parent;parent=parent.parent)if(parent.CompareTag("Player"))return actor;
  return null;
 }
 void Register(Collider other){var actor=Resolve(other,out var cc,out var rb);if(!actor)return;int id=actor.GetInstanceID();if(tracks.TryGetValue(id,out var existing)){existing.colliders.Add(other.GetInstanceID());return;}float d=SignedDistance(actor);var track=new Track{actor=actor,cc=cc,rb=rb,previous=d,armed=(!activation||activation.CanTraverse)&&d>=armDistance};track.colliders.Add(other.GetInstanceID());tracks[id]=track;}
 void LateUpdate(){if(tracks.Count==0)return;var ids=new List<int>(tracks.Keys);foreach(int id in ids)if(tracks.TryGetValue(id,out var state))Evaluate(id,state);}
 float SignedDistance(Transform actor){return transform.InverseTransformPoint(actor.position).z-crossingPlaneOffset;}
 bool IsCooling(int id){return blockedUntil.TryGetValue(id,out float until)&&Time.time<until;}
 void Evaluate(int id,Track s){
  if(!s.actor){tracks.Remove(id);return;}float d=SignedDistance(s.actor);
  if(!HasDestination){s.previous=d;s.armed=false;LastResult="목적지를 연결한 뒤 통과할 수 있습니다.";return;}
  if(activation&&!activation.CanTraverse){s.previous=d;s.armed=false;return;}
  if(IsCooling(id)){s.previous=d;s.armed=false;return;}
  if(d>=armDistance)s.armed=true;
  if(s.armed&&d<=-crossingThreshold&&d<s.previous){
   s.armed=false;
   if(FitsAperture(s.cc,s.rb)){tracks.Remove(id);blockedUntil[id]=Time.time+cooldown;Traverse(s);return;}
  }
  s.previous=d;
 }
 public bool FitsAperture(CharacterController cc,Rigidbody rb=null){
  if(cc){var scale=cc.transform.lossyScale;float r=cc.radius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.z));float half=Mathf.Max(0,cc.height*Mathf.Abs(scale.y)*.5f-r);var center=cc.transform.TransformPoint(cc.center);return SphereFits(center+cc.transform.up*half,r)&&SphereFits(center-cc.transform.up*half,r);}
  if(rb){var colliders=rb.GetComponentsInChildren<Collider>();bool hasSolid=false;foreach(var c in colliders){if(c.isTrigger||!c.enabled||!c.gameObject.activeInHierarchy)continue;hasSolid=true;var b=c.bounds;foreach(float x in new[]{b.min.x,b.max.x})foreach(float y in new[]{b.min.y,b.max.y})foreach(float z in new[]{b.min.z,b.max.z})if(!SphereFits(new Vector3(x,y,z),0))return false;}return hasSolid;}
  return false;
 }
 bool SphereFits(Vector3 center,float radius){var q=transform.InverseTransformPoint(center);return Mathf.Sqrt(q.x*q.x+q.y*q.y)+radius/Mathf.Max(.0001f,Mathf.Min(Mathf.Abs(transform.lossyScale.x),Mathf.Abs(transform.lossyScale.y)))<=apertureRadius-.001f;}
 void Traverse(Track s){
  TraversalCount++;LastTraveller=s.actor;
  if(playConversionEffect)CyberTransition.PlayOn(s.actor,transitionMaterial);
  if(!string.IsNullOrWhiteSpace(targetScene)&&Application.CanStreamedLevelBeLoaded(targetScene)){LastResult="연결된 목적지로 이동합니다.";onTraverse.Invoke();if(!externalSceneLoading)SceneManager.LoadScene(targetScene);return;}
  if(portalExitFrame&&preservePortalPose){
   // Frames face their approach space. The half turn carries a walking pose into the destination.
   var source=transform.localToWorldMatrix*Matrix4x4.Translate(new Vector3(0,0,crossingPlaneOffset));
   var mapping=portalExitFrame.localToWorldMatrix*Matrix4x4.Rotate(Quaternion.Euler(0,180,0))*source.inverse;
   var position=mapping.MultiplyPoint3x4(s.actor.position);
   var rotation=portalExitFrame.rotation*Quaternion.Euler(0,180,0)*Quaternion.Inverse(transform.rotation)*s.actor.rotation;
   Vector3 velocity=s.rb?s.rb.linearVelocity:Vector3.zero;
   Teleport(s.actor,s.cc,s.rb,position,rotation);
   if(s.rb)s.rb.linearVelocity=mapping.MultiplyVector(velocity);
   LastResult=destinationLabel+" · 공간 연결 통과 완료";
  }
  else if(demoExit){Teleport(s.actor,s.cc,s.rb,demoExit.position,demoExit.rotation);LastResult=destinationLabel+" · 지정한 목적지로 이동했습니다.";}else LastResult="통과 이벤트를 전달했습니다.";
  onTraverse.Invoke();
 }
 public static void Teleport(Transform actor,CharacterController cc,Rigidbody rb,Vector3 position,Quaternion rotation){
  bool enabled=cc&&cc.enabled;if(enabled)cc.enabled=false;
  if(rb){rb.position=position;rb.rotation=rotation;rb.linearVelocity=Vector3.zero;rb.angularVelocity=Vector3.zero;}else actor.SetPositionAndRotation(position,rotation);
  if(enabled)cc.enabled=true;Physics.SyncTransforms();
 }
}
}

