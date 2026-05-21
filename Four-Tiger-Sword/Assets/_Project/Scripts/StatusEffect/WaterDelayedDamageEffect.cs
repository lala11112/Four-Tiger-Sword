using UnityEngine;

/// <summary>
/// 수(水) 스킬: 피격된 적에게 일정 시간 후 추가 데미지를 가합니다.
/// StatusEffectHandler를 통해 관리됩니다.
/// </summary>
public class WaterDelayedDamageEffect : IStatusEffect
{
    private readonly float       _damage;
    private readonly float       _delay;
    private readonly ElementType _element;
    private readonly float       _critChance;
    private readonly float       _critMultiplier;

    private float      _elapsed;
    private bool       _damageDone;
    private IDamageable _damageable;
    private GameObject  _targetGO;

    public bool IsExpired => _damageDone;

    public WaterDelayedDamageEffect(float damage, float delay, ElementType element,
                                    float critChance, float critMultiplier)
    {
        _damage         = damage;
        _delay          = delay;
        _element        = element;
        _critChance     = critChance;
        _critMultiplier = critMultiplier;
    }

    public void OnApply(GameObject target)
    {
        _damageable = target.GetComponent<IDamageable>();
        _targetGO   = target;
        _elapsed    = 0f;
        _damageDone = false;
    }

    public void OnUpdate(float deltaTime)
    {
        if (_damageDone) return;

        _elapsed += deltaTime;
        if (_elapsed < _delay) return;

        _damageDone = true;

        if (_damageable == null || _targetGO == null) return;

        DamageManager.Apply(
            new HitInfo(_damage, _element, _critChance, _critMultiplier, poiseDamage: 0f),
            _damageable, _targetGO);
    }

    public void OnRemove(GameObject target) { }
}
