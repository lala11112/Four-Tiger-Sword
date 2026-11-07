using UnityEngine;

namespace BoundaryZeroUI {
/// <summary>Fits the complete QHD design uniformly inside the parent Canvas's logical rect.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class BoundaryDesignViewport : MonoBehaviour {
 public const float DesignWidth=2560f, DesignHeight=1440f;
 RectTransform design;
 RectTransform parent;
 Vector2 previousSize=new Vector2(-1,-1);

 public void Configure(){
  design=transform as RectTransform;
  parent=transform.parent as RectTransform;
  previousSize=new Vector2(-1,-1);
  Fit();
 }
 void Awake(){Configure();}
 void OnEnable(){Configure();}
 void OnTransformParentChanged(){Configure();}
 void LateUpdate(){Fit();}
 void Fit(){
  if(!design)design=transform as RectTransform;
  if(!parent||!design)return;
  Vector2 size=parent.rect.size;
  // CanvasScaler may settle after Awake; retry invalid dimensions on the next frame.
  if(size.x<=0||size.y<=0)return;
  if(size==previousSize)return;
  previousSize=size;
  design.anchorMin=design.anchorMax=new Vector2(.5f,.5f);
  design.pivot=new Vector2(.5f,.5f);
  design.sizeDelta=new Vector2(DesignWidth,DesignHeight);
  design.anchoredPosition=Vector2.zero;
  design.localRotation=Quaternion.identity;
  float scale=Mathf.Min(size.x/DesignWidth,size.y/DesignHeight);
  design.localScale=new Vector3(scale,scale,1);
 }
}
}
