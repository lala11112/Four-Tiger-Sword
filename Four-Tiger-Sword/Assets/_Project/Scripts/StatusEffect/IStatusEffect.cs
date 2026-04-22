using UnityEngine;

/// <summary>
/// 모든 상태이상 효과의 공통 계약 (OCP: 새 효과는 이 인터페이스 구현만으로 추가 가능)
/// </summary>
public interface IStatusEffect
{
    bool IsExpired { get; }
    void OnApply(GameObject target);
    void OnUpdate(float deltaTime);
    void OnRemove(GameObject target);
}
