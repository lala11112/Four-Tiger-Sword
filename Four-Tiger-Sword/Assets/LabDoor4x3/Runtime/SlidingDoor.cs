using UnityEngine;

namespace LabDoor {
/// <summary>Moves two leaves in the root's local X direction. No input or trigger dependencies.</summary>
[DisallowMultipleComponent]
public sealed class SlidingDoor : MonoBehaviour {
    [SerializeField] Transform leftLeaf, rightLeaf;
    [SerializeField] Vector3 leftClosedPosition, rightClosedPosition;
    [SerializeField, Min(0)] float travel = 2.05f;
    [SerializeField, Min(0)] float duration = 1.2f;
    [SerializeField, Range(0,1)] float openAmount;
    [SerializeField] bool targetOpen;
    public float OpenAmount => openAmount;
    public float Travel => travel;
    public float Duration => duration;
    public Transform LeftLeaf => leftLeaf;
    public Transform RightLeaf => rightLeaf;
    public void Setup(Transform left, Transform right, float distance = 2.05f, float seconds = 1.2f) {
        leftLeaf=left; rightLeaf=right;
        leftClosedPosition=left.localPosition; rightClosedPosition=right.localPosition;
        travel=Mathf.Max(0,distance); duration=Mathf.Max(0,seconds);
        openAmount=0; targetOpen=false; Apply();
    }
    public void SetOpen(bool value) { targetOpen=value; if (!Application.isPlaying || duration<=0) SetOpenImmediate(value); }
    public void Open() => SetOpen(true);
    public void Close() => SetOpen(false);
    public void Toggle() => SetOpen(!targetOpen);
    public void SetOpenImmediate(bool value) { targetOpen=value; openAmount=value?1:0; Apply(); }
    void OnEnable() => Apply();
    void OnValidate() { travel=Mathf.Max(0,travel); duration=Mathf.Max(0,duration); Apply(); }
    void Update() {
        openAmount=duration<=0?(targetOpen?1:0):Mathf.MoveTowards(openAmount,targetOpen?1:0,Time.deltaTime/duration);
        Apply();
    }
    void Apply() {
        // Direct interpolation from serialized closed positions prevents accumulated drift.
        if(leftLeaf) leftLeaf.localPosition=leftClosedPosition+Vector3.left*(travel*openAmount);
        if(rightLeaf) rightLeaf.localPosition=rightClosedPosition+Vector3.right*(travel*openAmount);
    }
}
}
