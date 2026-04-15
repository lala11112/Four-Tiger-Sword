using UnityEngine;

/// <summary>금(金) 스킬: 적의 방어력을 일정량 감소시킵니다.</summary>
public class DefenseReductionEffect : IStatusEffect
{
    private readonly float _reductionAmount;
    private readonly float _duration;
    private float _elapsed;

    private EnemyStatHandler _stats;

    public bool IsExpired => _elapsed >= _duration;

    public DefenseReductionEffect(float reductionAmount, float duration)
    {
        _reductionAmount = reductionAmount;
        _duration        = duration;
    }

    public void OnApply(GameObject target)
    {
        _stats = target.GetComponent<EnemyStatHandler>();
        _stats?.ReduceDefense(_reductionAmount);

        Debug.Log($"<color=silver>[방어감소] {target.name} 방어력 -{_reductionAmount} ({_duration}초)</color>");
    }

    public void OnUpdate(float deltaTime) => _elapsed += deltaTime;

    public void OnRemove(GameObject target)
    {
        _stats?.RestoreDefense(_reductionAmount);
    }
}
