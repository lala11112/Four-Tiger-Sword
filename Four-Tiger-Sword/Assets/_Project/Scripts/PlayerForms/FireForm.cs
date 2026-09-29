using UnityEngine;

public class FireForm : BaseForm, IToggleUltimateForm
{
    private readonly FireFormActionDataSO _fireData;

    public override ElementType Element => ElementType.ELEMENT_FIRE;
    public override float SkillSpCost => 200f;
    public override float SkillCooldown => 8f;
    public bool IsUltimateActive { get; private set; }
    public override bool CanUltimate => !IsUltimateActive && _weaponActionData != null && UltimateResourcesReady;
    public float SkillBuffRemaining { get; private set; }
    public bool IsSkillBuffActive => SkillBuffRemaining > 0f;
    public float CurrentSkillRadius => IsUltimateActive
        ? Mathf.Max(_fireData.FireSkillRadius, _fireData.FireEnhancedSkillRadius)
        : Mathf.Max(0.1f, _fireData.FireSkillRadius);
    public override float OutgoingDamageMultiplier => IsUltimateActive ? 1f + _outgoingBonus : 1f;
    public override float IncomingDamageMultiplier => IsUltimateActive ? 1f + _incomingBonus : 1f;
    private float _skillAttackBonus, _attackSpeedBonus, _outgoingBonus, _incomingBonus;
    private float _normalLifeSteal, _skillLifeSteal;

    public FireForm(FireFormActionDataSO data) : base(data) { _fireData = data; }
    public override void Equip(PlayerController player)
    {
        base.Equip(player);
        PlayerUIManager.Instance?.FireElement?.SetActive(false);
        player.WeaponManager?.FireWeapon?.SetActive(true);
    }
    public override void Unequip(PlayerController player)
    {
        DeactivateUltimate();
        PlayerUIManager.Instance?.FireElement?.SetActive(true);
        player.WeaponManager?.FireWeapon?.SetActive(false);
    }
    protected override void OnHitEnemy(GameObject enemy)
    {
        base.OnHitEnemy(enemy);
        TargetEffects(enemy).AddFireStack(EffectHit(2.5f), _fireData.FireExplosionDelay);
    }
    protected override void OnDamageDealt(GameObject target, DamageResult result)
    {
        base.OnDamageDealt(target, result);
        if (!IsUltimateActive || !result.Applied) return;
        float rate = _currentAction == ActionType.Attack ? _normalLifeSteal
            : _currentAction == ActionType.Skill ? _skillLifeSteal : 0f;
        _playerController.StatManager.Heal(Mathf.Max(0f, result.HealthDamage) * rate);
    }
    public override void BeginSkill()
    {
        base.BeginSkill();
        RemoveSkillBuff();
        SkillBuffRemaining = Mathf.Max(0f, _fireData.FireSkillBuffDuration);
        if (!IsSkillBuffActive) return;
        _skillAttackBonus = Mathf.Max(0f, _fireData.FireSkillAttackBonusPercent) / 100f;
        if (_skillAttackBonus > 0f)
            _playerController.StatManager.AddModifier(StatType.ST_ATK, 0f, _skillAttackBonus);
    }
    protected override int QueryOverlap(WeaponActionData step, Vector3 center)
        => _currentAction == ActionType.Skill
            ? Physics.OverlapSphereNonAlloc(center, CurrentSkillRadius, _overlapBuffer, _enemyLayer)
            : base.QueryOverlap(step, center);

    public override void BeginUltimate()
    {
        if (IsUltimateActive) { DeactivateUltimate(); return; }
        ConsumeUltimateResources();
        _currentAction = ActionType.Ultimate;
        IsUltimateActive = true;
        _outgoingBonus = Mathf.Max(0f, _fireData.FireUltimateDamageDealtBonusPercent) / 100f;
        _incomingBonus = Mathf.Max(0f, _fireData.FireUltimateDamageTakenBonusPercent) / 100f;
        _attackSpeedBonus = Mathf.Max(0f, _fireData.FireUltimateAttackSpeedBonusPercent) / 100f;
        _normalLifeSteal = Mathf.Clamp01(_fireData.FireUltimateNormalLifeStealPercent / 100f);
        _skillLifeSteal = Mathf.Max(_normalLifeSteal, Mathf.Clamp01(_fireData.FireUltimateSkillLifeStealPercent / 100f));
        if (_attackSpeedBonus > 0f)
            _playerController.StatManager.AddModifier(StatType.ST_ATK_SPD, 0f, _attackSpeedBonus);
    }
    // The ultimate enables a combat stance, then returns control to normal attacks immediately.
    public override void UpdateUltimate(out bool isComplete) => isComplete = true;

    public void DeactivateUltimate()
    {
        if (!IsUltimateActive) return;
        IsUltimateActive = false;
        if (_attackSpeedBonus > 0f && _playerController != null)
            _playerController.StatManager.RemoveModifier(StatType.ST_ATK_SPD, 0f, _attackSpeedBonus);
        _attackSpeedBonus = _outgoingBonus = _incomingBonus = _normalLifeSteal = _skillLifeSteal = 0f;
        UpdateCombatAnimationSpeed();
    }
    protected override void OnTick(float dt)
    {
        if (_playerController == null) return;
        if (!_playerController.isActiveAndEnabled || _playerController.StatManager.CurrentHp <= 0f)
        {
            CleanupTransientEffects();
            return;
        }
        if (IsSkillBuffActive)
        {
            SkillBuffRemaining = Mathf.Max(0f, SkillBuffRemaining - dt);
            if (!IsSkillBuffActive) RemoveSkillBuff();
        }
        UpdateCombatAnimationSpeed();
    }
    private void UpdateCombatAnimationSpeed()
    {
        if (_playerController?.FormManager?.CurrentForm != this) return;
        var state = _playerController.StateMachine.CurrentState;
        if (state is PlayerAttackState || state is PlayerSkillState || state is PlayerAirAttackState || state is PlayerCounterState)
            _playerController.Animator.speed = AttackSpeed;
    }
    private void RemoveSkillBuff()
    {
        if (_skillAttackBonus > 0f && _playerController != null)
            _playerController.StatManager.RemoveModifier(StatType.ST_ATK, 0f, _skillAttackBonus);
        _skillAttackBonus = 0f;
        SkillBuffRemaining = 0f;
    }
    public override void CleanupTransientEffects()
    {
        DeactivateUltimate();
        RemoveSkillBuff();
    }
}
