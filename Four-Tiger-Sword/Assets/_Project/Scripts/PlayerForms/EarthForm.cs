using UnityEngine;
using System.Collections.Generic;

public class EarthForm : BaseForm
{
    private readonly EarthFormActionDataSO _earthData;

    public override ElementType Element => ElementType.ELEMENT_EARTH;
    public override float SkillSpCost => Mathf.Max(0f, _earthData.SkillSpiritCost);
    public override float SkillCooldown => Mathf.Max(0f, _earthData.SkillCooldownSeconds);
    public override bool CanUltimate => _earthData != null && UltimateResourcesReady;
    private readonly object _skillShieldSource = new(), _ultimateShieldSource = new();
    private readonly HashSet<IDamageable> _dashTargets = new();
    private Vector3 _dashDirection, _sweepStart, _sweepEnd;
    private float _dashElapsed;
    private bool _skillActive;
    public float SkillShield => _playerController?.StatManager.GetShield(_skillShieldSource) ?? 0f;
    public float UltimateShield => _playerController?.StatManager.GetShield(_ultimateShieldSource) ?? 0f;
    public float SkillShieldRemaining => _playerController?.StatManager.GetShieldRemaining(_skillShieldSource) ?? 0f;
    public float UltimateShieldRemaining => _playerController?.StatManager.GetShieldRemaining(_ultimateShieldSource) ?? 0f;
    public float UltimateBuffRemaining { get; private set; }
    public bool IsUltimateDamageBuffActive => UltimateBuffRemaining > 0f && (SkillShield > 0f || UltimateShield > 0f);
    public override float OutgoingDamageMultiplier => IsUltimateDamageBuffActive
        ? 1f + Mathf.Max(0f, _earthData.UltimateShieldDamageBonusPercent) / 100f : 1f;
    private bool _guardCounterReady;
    public bool GuardCounterReady => _guardCounterReady;
    public EarthForm(EarthFormActionDataSO data) : base(data) { _earthData = data; }
    public override void Equip(PlayerController player)
    {
        base.Equip(player);
        PlayerUIManager.Instance?.EarthElement?.SetActive(false);
    }
    public override void Unequip(PlayerController player)
    {
        _skillActive = false;
        UltimateBuffRemaining = 0f;
        PlayerUIManager.Instance?.EarthElement?.SetActive(true);
    }
    public override void BeginAttack()
    {
        _guardCounterReady = false;
        base.BeginAttack();
    }
    public override void BeginCounter()
    {
        _guardCounterReady = false;
        base.BeginCounter();
        // TODO: 저스트 가드 올려베기 반격 모션.
        // _playerController.Animator.CrossFade("EarthCounter", 0.1f);
    }
    public override bool BlocksIncomingDamage(float damage, Vector3 incomingPower, object attackSource = null)
    {
        bool guarding = _playerController.StateMachine.CurrentState is PlayerAttackState
            && _comboStep == 0 && _timer >= 10f / 60f && _timer <= 22f / 60f;
        Vector3 toSource = -incomingPower;
        if (attackSource is EnemyAction source && source.Owner != null)
            toSource = source.Owner.transform.position - _playerController.transform.position;
        if (!guarding || toSource.sqrMagnitude < 0.001f
            || Vector3.Dot(_playerController.transform.forward, toSource.normalized) <= 0f) return false;
        _guardCounterReady = true;
        _playerController.OnParrySuccess();
        (attackSource as EnemyAction)?.OnParried();
        return true;
    }
    protected override void OnHitEnemy(GameObject target)
    {
        base.OnHitEnemy(target);
        if (_currentAction == ActionType.Counter)
        {
            var effects = target.GetComponent<StatusEffectHandler>() ?? target.AddComponent<StatusEffectHandler>();
            effects.Apply(new AirborneEffect(1.5f));
        }
    }
    public override void BeginSkill()
    {
        base.BeginSkill();
        _playerController.StatManager.GrantShield(_skillShieldSource, _earthData.ShieldAmount, _earthData.ShieldDuration);
        _dashElapsed = 0f;
        _skillActive = true;
        _dashTargets.Clear();
        _softTarget = null;
        _dashDirection = Vector3.ProjectOnPlane(_playerController.transform.forward, Vector3.up).normalized;
        _playerController.Animator.speed = 1f;
    }
    public override void UpdateSkill(out bool isComplete) => AdvanceSkill(Time.deltaTime, out isComplete);
    private void AdvanceSkill(float dt, out bool isComplete)
    {
        var step = _earthData.SkillSteps != null && _earthData.SkillSteps.Count > 0 ? _earthData.SkillSteps[0] : null;
        if (!_skillActive || step == null) { isComplete = true; return; }
        float duration = Mathf.Max(0.01f, _earthData.SkillDashDuration);
        float previous = _dashElapsed;
        _dashElapsed += Mathf.Max(0f, dt);
        Vector3 offset = _playerController.transform.rotation * _earthData.SkillDashHitOffset;
        _sweepStart = _playerController.transform.position + offset;
        float distance = Mathf.Max(0f, _earthData.SkillDashDistance)
            * (Mathf.Clamp01(_dashElapsed / duration) - Mathf.Clamp01(previous / duration));
        var motion = _dashDirection * distance;
        motion.y = _playerController.VerticalVelocity * Mathf.Max(0f, dt);
        _playerController.Controller.Move(motion);
        _sweepEnd = _playerController.transform.position + offset;
        if (previous < duration && dt > 0f)
            ExecuteHit(step, Mathf.Max(0f, _earthData.SkillDashDamageMultiplier), step.PoiseDamage, _dashTargets);
        isComplete = _dashElapsed >= Mathf.Max(duration, step.Duration);
    }
    protected override int QueryOverlap(WeaponActionData step, Vector3 center)
        => _currentAction == ActionType.Skill
            ? Physics.OverlapCapsuleNonAlloc(_sweepStart, _sweepEnd, Mathf.Max(0.1f, _earthData.SkillDashHitRadius), _overlapBuffer, _enemyLayer)
            : base.QueryOverlap(step, center);
    public override void EndSkill()
    {
        _skillActive = false;
        _dashTargets.Clear();
        base.EndSkill();
    }
    public override void BeginUltimate()
    {
        ConsumeUltimateResources();
        _currentAction = ActionType.Ultimate;
        _playerController.StatManager.GrantShield(_ultimateShieldSource, _earthData.UltimateShieldAmount, _earthData.UltimateShieldDuration);
        UltimateBuffRemaining = Mathf.Max(0f, _earthData.UltimateBuffDuration);
    }
    public override void UpdateUltimate(out bool isComplete)
    {
        isComplete = true;
    }
    protected override void OnTick(float dt)
    {
        if (_playerController == null) return;
        if (!_playerController.isActiveAndEnabled || _playerController.StatManager.CurrentHp <= 0f)
        { CleanupTransientEffects(); return; }
        UltimateBuffRemaining = Mathf.Max(0f, UltimateBuffRemaining - Mathf.Max(0f, dt));
    }
    public override void CleanupTransientEffects()
    {
        _skillActive = false;
        _dashTargets.Clear();
        UltimateBuffRemaining = 0f;
        _playerController?.StatManager.RemoveShield(_skillShieldSource);
        _playerController?.StatManager.RemoveShield(_ultimateShieldSource);
    }
}
