using UnityEngine;

/// <summary>
/// 목(木) 전용 스택 데이터. 최대 스택 달성 시 데미지 없이 속박(Bind)만 적용합니다.
/// StackDataSO를 상속해 OnExplosion만 재정의합니다. (OCP)
/// </summary>
[CreateAssetMenu(fileName = "WoodBindStackData", menuName = "StackData/WoodBindStackData")]
public class WoodBindStackDataSO : StackDataSO
{
    [Tooltip("속박 지속 시간 (초)")]
    public float bindDuration = 30f;
    [SerializeField] private GameObject _vfxPrefab;

    // stackType은 반드시 Wood여야 합니다.
    // StackDataSO의 stackType이 기본값(Fire=0)으로 생성되는 것을 방지합니다.
    private void OnEnable()    => stackType = StackType.Wood;
    private void OnValidate()  => stackType = StackType.Wood;

    public override void OnExplosion(GameObject target)
    {
        var statusHandler = target.GetComponent<StatusEffectHandler>();
        statusHandler?.Apply(new BindEffect(bindDuration, _vfxPrefab));

        //Debug.Log($"<color=green>[목 스택] {target.name} {bindDuration}초 속박!</color>");
    }
}
