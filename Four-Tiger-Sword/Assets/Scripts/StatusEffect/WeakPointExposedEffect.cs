using UnityEngine;

/// <summary>금(金) 궁극기: 모든 공격이 치명타로 적중하는 '약점 노출' 상태를 부여합니다.</summary>
public class WeakPointExposedEffect : IStatusEffect
{
    private readonly float _duration;
    private float _elapsed;

    private EnemyStatHandler _stats;

    public bool IsExpired => _elapsed >= _duration;

    public WeakPointExposedEffect(float duration)
    {
        _duration = duration;
    }

    public void OnApply(GameObject target)
    {
        _stats = target.GetComponent<EnemyStatHandler>();
        _stats?.SetWeakPoint(true);

        Debug.Log($"<color=yellow>[약점 노출] {target.name} {_duration}초간 강제 크리티컬!</color>");
    }

    public void OnUpdate(float deltaTime) => _elapsed += deltaTime;

    public void OnRemove(GameObject target)
    {
        _stats?.SetWeakPoint(false);
    }
}
