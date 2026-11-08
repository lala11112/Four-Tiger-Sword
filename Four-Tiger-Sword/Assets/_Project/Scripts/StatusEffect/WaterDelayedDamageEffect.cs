using UnityEngine;
using System.Collections;

/// <summary>
/// 수(水) 스킬: 피격된 적에게 일정 시간 후 추가 데미지를 가합니다.
/// StatusEffectHandler를 통해 관리됩니다.
/// </summary>
public class WaterDelayedDamageEffect : IStatusEffect
{
    private readonly float _damage;
    private readonly float _delay;
    private readonly ElementType _element;
    private readonly float _critChance;
    private readonly float _critMultiplier;
    private readonly GameObject _vfxPrefab;
    private readonly CombatSoundCue _impactSound;
    private readonly PlayerController _owner;

    private float _elapsed;
    private bool _damageDone;
    private IDamageable _damageable;
    private GameObject _targetGO;
    private Boss _boss;
    private int _phaseGeneration;

    public bool IsExpired => _damageDone;

    public WaterDelayedDamageEffect(float damage, float delay, ElementType element,
                                    float critChance, float critMultiplier,
                                    GameObject vfxPrefab = null, CombatSoundCue impactSound = default,
                                    PlayerController owner = null)
    {
        _damage = damage;
        _delay = delay;
        _element = element;
        _critChance = critChance;
        _critMultiplier = critMultiplier;
        _vfxPrefab = vfxPrefab;
        _impactSound = impactSound;
        _owner = owner;
    }

    public void OnApply(GameObject target)
    {
        _damageable = target.GetComponent<IDamageable>();
        _targetGO = target;
        _boss = target.GetComponent<Boss>();
        _phaseGeneration = _boss != null ? _boss.PhaseGeneration : 0;
        _elapsed = 0f;
        _damageDone = false;
    }

    public void OnUpdate(float deltaTime)
    {
        if (_damageDone) return;
        if (!CanApplyPendingDamage()) { _damageDone = true; return; }

        _elapsed += deltaTime;
        if (_elapsed < _delay) return;

        _damageDone = true;

        var enemy = _targetGO.GetComponent<Enemy>();
        if (enemy != null) enemy.StartCoroutine(DamageApply());
    }

    public void OnRemove(GameObject target) { }

    private IEnumerator DamageApply()
    {
        for (int i = 0; i < 5; i++)
        {
            if (!CanApplyPendingDamage()) yield break;
            yield return new WaitForSeconds(0.3f);
            if (!CanApplyPendingDamage()) yield break;
            DamageManager.Apply(
                new HitInfo(_damage, _element, _critChance, _critMultiplier, poiseDamage: 0f,
                    impactSound: _impactSound, category: PlayerAttackCategory.Secondary, source: _owner),
                _damageable, _targetGO);

            // The hit itself may deplete a phase; its old scheduled VFX must not cross that boundary.
            if (_targetGO == null || !_targetGO.activeInHierarchy || !IsCurrentPhase()) yield break;
            if (_vfxPrefab != null)
                StageTransientObject.Track(Object.Instantiate(_vfxPrefab, _targetGO.transform.position, Quaternion.identity));
        }
    }

    private bool CanApplyPendingDamage() => _damageable != null && _targetGO != null
        && _targetGO.activeInHierarchy && _targetGO.GetComponent<Enemy>()?.IsDead != true
        && IsCurrentPhase();

    private bool IsCurrentPhase() => _boss == null
        || (!_boss.IsTransitioning && _boss.PhaseGeneration == _phaseGeneration);
}
