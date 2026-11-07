using UnityEngine;

public abstract partial class BaseForm
{
    private float _attackItemMultiplier = 1f, _attackSourceMultiplier = 1f;
    private float _attackStat, _attackCritChance, _attackCritMultiplier;
    private bool UsesAttackSnapshot => _currentAction == ActionType.Attack
        || _currentAction == ActionType.AirAttack || _currentAction == ActionType.SwitchAttack;
    protected PlayerAttackCategory CurrentAttackCategory => _currentAction switch
    {
        ActionType.Attack => PlayerAttackCategory.Normal,
        ActionType.AirAttack => PlayerAttackCategory.Normal,
        ActionType.SwitchAttack => PlayerAttackCategory.SwitchAttack,
        ActionType.Skill => PlayerAttackCategory.Skill,
        ActionType.Ultimate => PlayerAttackCategory.Ultimate,
        ActionType.Counter => PlayerAttackCategory.Counter,
        ActionType.HeavyAttack => PlayerAttackCategory.Heavy,
        _ => PlayerAttackCategory.Other,
    };
    protected float DamageAttackStat => UsesAttackSnapshot ? _attackStat
        : LiveAttackStat;
    protected float DamageCriticalChance => UsesAttackSnapshot ? _attackCritChance
        : _playerController.StatManager.GetStat(StatType.ST_CRT) / 100f;
    protected float DamageCriticalMultiplier => UsesAttackSnapshot ? _attackCritMultiplier
        : _playerController.StatManager.GetStat(StatType.ST_CRTD) / 100f;
    protected float DamageItemMultiplier => UsesAttackSnapshot ? _attackItemMultiplier : 1f;
    protected float DamageSourceMultiplier => UsesAttackSnapshot ? _attackSourceMultiplier : CaptureSourceDamageMultiplier();

    protected void SnapshotAttackDamage()
    {
        _attackStat = LiveAttackStat;
        _attackCritChance = _playerController.StatManager.GetStat(StatType.ST_CRT) / 100f;
        _attackCritMultiplier = _playerController.StatManager.GetStat(StatType.ST_CRTD) / 100f;
        _attackSourceMultiplier = CaptureSourceDamageMultiplier();
        var items = _playerController.GetComponent<PlayerItemRuntime>();
        _attackItemMultiplier = items == null ? 1f : _currentAction == ActionType.SwitchAttack
            ? items.GetSwitchDamageMultiplier(Element) : items.GetNormalDamageMultiplier(Element);
        _attackItemMultiplier = Mathf.Max(0f, _attackItemMultiplier);
    }

    private float CaptureSourceDamageMultiplier()
    {
        float multiplier = Mathf.Max(0f, OutgoingDamageMultiplier);
        if (Element != ElementType.ELEMENT_NONE)
            multiplier *= 1f + Mathf.Max(0f, _playerController.StatManager.GetStat(StatType.ST_ELM_ATK)) / 100f;
        return multiplier;
    }

    protected FormEffectRunner Effects => _playerController.GetComponent<FormEffectRunner>()
        ?? _playerController.gameObject.AddComponent<FormEffectRunner>();

    protected HitInfo EffectHit(float multiplier, float extraDamage = 0f, bool trueDamage = false,
        WeaponActionData step = null, HitEvent hitEvent = null, bool secondary = false, Vector3 power = default)
        => new HitInfo(multiplier * (secondary ? LiveAttackStat : DamageAttackStat) + extraDamage,
            Element, trueDamage ? 0f : secondary ? _playerController.StatManager.GetStat(StatType.ST_CRT) / 100f : DamageCriticalChance,
            secondary ? _playerController.StatManager.GetStat(StatType.ST_CRTD) / 100f : DamageCriticalMultiplier, trueDamage: trueDamage,
            source: _playerController, impactSound: secondary ? ResolveSecondaryHitSound() : ResolveHitSound(step, hitEvent),
            power: power, staggerResistLevel: power.sqrMagnitude > 0f && step != null ? step.StaggerResistLevel : StaggerResistLevel.NONE,
            category: secondary ? PlayerAttackCategory.Secondary : CurrentAttackCategory,
            itemDamageMultiplier: secondary ? 1f : DamageItemMultiplier,
            sourceDamageMultiplier: secondary ? CaptureSourceDamageMultiplier() : DamageSourceMultiplier,
            hasSourceDamageSnapshot: true);

    protected Vector3 CustomKnockbackPower(WeaponActionData step, HitEvent hitEvent, Transform target)
    {
        if (step?.Balance?.EnableV2 != true && hitEvent?.Balance?.EnableV2 != true) return Vector3.zero;
        float force = hitEvent?.Balance?.EnableV2 == true && hitEvent.OverrideKnockbackForce
            ? hitEvent.KnockbackForce : step.KnockbackForce;
        Vector3 direction = target != null ? target.position - _playerController.transform.position : _playerController.transform.forward;
        if (direction.sqrMagnitude < .001f) direction = _playerController.transform.forward;
        return KnockbackMotion.Power(direction, force);
    }

    protected static FormTargetEffects TargetEffects(GameObject target)
        => target.GetComponent<FormTargetEffects>() ?? target.AddComponent<FormTargetEffects>();

    protected virtual bool DealsTrueDamage => false;
    public virtual float OutgoingDamageMultiplier => 1f;
    public virtual float IncomingDamageMultiplier => 1f;
    protected virtual void OnDamageDealt(GameObject target, DamageResult result, WeaponActionData step = null, HitEvent hitEvent = null)
    {
        if (!result.Applied) return;
        NotifyFinalComboHit(target);
        ApplyChainVulnerability(target, result);
        OnHitEnemy(target, step, hitEvent);
    }
    public virtual bool BlocksIncomingDamage(float damage, Vector3 incomingPower, object source = null) => false;
    public virtual void CleanupTransientEffects() => RemoveChainBuff();

    // A stage boundary clears world targets/effects while player buffs and resources persist.
    public virtual void CleanupStageEffects()
    {
        _softTarget = null;
        _softTargetCollider = null;
        _motionStep = null;
        if (_playerController != null) ClearHitTargets();
    }

    protected float FirstHitTime(WeaponActionData step)
    {
        float time = step.HitStartTime;
        if (step.HitEvents != null && step.HitEvents.Count > 0)
        {
            time = float.MaxValue;
            foreach (var hit in step.HitEvents)
                if (hit != null) time = Mathf.Min(time, hit.StartTime);
        }
        return Mathf.Max(0f, time);
    }

    protected HitEvent FirstHitEvent(WeaponActionData step, out int index)
    {
        index = -1;
        HitEvent first = null;
        if (step?.HitEvents == null) return null;
        for (int i = 0; i < step.HitEvents.Count; i++)
        {
            HitEvent candidate = step.HitEvents[i];
            if (candidate == null || (first != null && candidate.StartTime >= first.StartTime)) continue;
            first = candidate;
            index = i;
        }
        return first;
    }
}
