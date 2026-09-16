using UnityEngine;

public abstract partial class BaseForm : IForm
{
    private static bool HasSteps(System.Collections.Generic.List<WeaponActionData> steps)
    {
        if (steps == null || steps.Count == 0) return false;
        foreach (var step in steps)
            if (step == null) return false;
        return true;
    }
    protected PlayerController _playerController;
    protected WeaponActionDataSO _weaponActionData;

    protected int _comboStep = 0;
    protected int _skillStep = 0;
    protected int _ultimateStep = 0;
    protected float _timer = 0;

    protected LayerMask _enemyLayer;

    protected Transform _softTarget;
    private const float SoftTargetSearchRadius = 10f;
    private const float SoftTargetAngle = 180f;
    private const float SoftTargetRotationSpeed = 540f; // degrees/sec

    public virtual ElementType Element => ElementType.ELEMENT_NONE;

    protected enum ActionType { Attack, AirAttack, Skill, Ultimate, Counter }
    protected ActionType _currentAction = ActionType.Attack;

    // ── SP 비용 & 쿨타임 ─────────────────────────────────────────────────────
    public virtual float SkillSpCost => 0f;
    public virtual float SkillCooldown => 5f;

    private float _skillCooldownTimer = 0f;
    private float _ultimateCooldownTimer;
    public float UltimateCooldownRemaining => _ultimateCooldownTimer;

    /// <summary>스킬 사용 가능 여부: 쿨타임이 끝나고 SP가 충분한지 확인합니다.</summary>
    public bool CanSkill => HasSteps(_weaponActionData?.SkillSteps)
        && _skillCooldownTimer <= 0f && _playerController.StatManager.HasEnoughSp(SkillSpCost);

    /// <summary>필살기 사용 가능 여부: 필살기 게이지가 가득 찼는지 확인합니다 (SP/쿨타임 무관).</summary>
    public bool CanUltimate => HasSteps(_weaponActionData?.UltimateSteps)
        && _ultimateCooldownTimer <= 0f && _playerController.StatManager.IsUltimateGaugeReady;

    /// <summary>공격 속도 배율. 1.0 = 기본, 2.0 = 2배 빠름. 스탯 시스템에서 읽어옵니다.</summary>
    protected float AttackSpeed => _playerController?.StatManager?.GetStat(StatType.ST_ATK_SPD) ?? 1f;

    public BaseForm(WeaponActionDataSO weaponActionData) => _weaponActionData = weaponActionData;

    public virtual void Equip(PlayerController playerController)
    {
        _playerController = playerController;
        _enemyLayer = LayerMask.GetMask("Enemy");
    }

    public virtual void Unequip(PlayerController playerController) { }

    public void UpdateCooldowns()
    {
        if (_skillCooldownTimer > 0f) _skillCooldownTimer -= Time.deltaTime;
        if (_ultimateCooldownTimer > 0f) _ultimateCooldownTimer -= Time.deltaTime;
        OnTick(Time.deltaTime);
    }

    public void UpdateSkillCooldownUI()
    {
        PlayerUIManager.Instance?.UpdateSkillCoolTime(_skillCooldownTimer, SkillCooldown);
    }

    protected virtual void OnTick(float deltaTime) { }
    protected virtual void OnHitEnemy(GameObject enemy)
    {
        SpawnHitVFX(enemy.transform.position);
        PlayHitSound(enemy.transform.position);
        _playerController.StatManager.AddUltimateGauge(1);

    }
}
