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
    private const float SoftTargetMaxHeight = 2f;
    private const float SoftTargetRotationSpeed = 540f; // degrees/sec

    public virtual ElementType Element => ElementType.ELEMENT_NONE;

    protected enum ActionType { Attack, AirAttack, Skill, Ultimate, Counter, HeavyAttack, SwitchAttack }
    protected ActionType _currentAction = ActionType.Attack;
    private static long _nextItemCastId;
    protected long ItemCastId { get; private set; }
    private void BeginItemCast() => ItemCastId = System.Threading.Interlocked.Increment(ref _nextItemCastId);

    // ── 쿨타임 ───────────────────────────────────────────────────────────────
    public virtual float SkillCooldown => 5f;

    // Keep timers in base seconds so ownership changes preserve the progress of an active cooldown.
    private float _skillCooldownTimer;
    private float _ultimateCooldownTimer;
    private float SkillCooldownTimeScale => 1f - (_playerController?.ItemRuntime?.SkillCooldownReductionRatio ?? 0f);
    private float BaseUltimateCooldown => Mathf.Max(0f, _weaponActionData != null ? _weaponActionData.UltimateCooldown : 30f);
    public float SkillCooldownRemaining => _skillCooldownTimer * SkillCooldownTimeScale;
    public float SkillCooldownDuration => Mathf.Max(0f, SkillCooldown) * SkillCooldownTimeScale;
    public float UltimateCooldownRemaining => _ultimateCooldownTimer * SkillCooldownTimeScale;
    public float UltimateCooldownDuration => BaseUltimateCooldown * SkillCooldownTimeScale;

    /// <summary>스킬 동작 데이터가 유효하고 쿨타임이 끝났는지 확인합니다.</summary>
    public bool CanSkill => HasSteps(_weaponActionData?.SkillSteps)
        && _skillCooldownTimer <= 0f;

    public virtual bool CanSwitchAttack
    {
        get
        {
            if (!HasSteps(_weaponActionData?.SwitchAttackSteps)) return false;
            foreach (var step in _weaponActionData.SwitchAttackSteps)
                if (!IsUsableNormalStep(step)) return false;
            return true;
        }
    }

    /// <summary>필살기 게이지가 가득 차고 쿨타임이 끝났는지 확인합니다.</summary>
    public virtual bool CanUltimate => HasSteps(_weaponActionData?.UltimateSteps)
        && UltimateResourcesReady;
    protected bool UltimateResourcesReady => _ultimateCooldownTimer <= 0f
        && _playerController.StatManager.IsUltimateGaugeReady;

    /// <summary>공격 속도 배율. 1.0 = 기본, 2.0 = 2배 빠름. 스탯 시스템에서 읽어옵니다.</summary>
    protected float AttackSpeed => Mathf.Max(0.1f,
        (_playerController?.StatManager?.GetStat(StatType.ST_ATK_SPD) ?? 1f)
        * (ItemDamageContext.Family(CurrentAttackCategory) == BoundaryAttackFamily.Normal
            ? _playerController?.ItemRuntime?.NormalAttackRateMultiplier ?? 1f : 1f));

    protected float LiveAttackStat => _playerController.StatManager.GetStat(StatType.ST_ATK);
    // Some authored actions use their fixed timeline instead of ordinary attack-speed buffs.
    protected float FixedActionSpeed => 1f;

    public BaseForm(WeaponActionDataSO weaponActionData)
    {
        _weaponActionData = weaponActionData;
    }

    public virtual void Equip(PlayerController playerController)
    {
        _playerController = playerController;
        _enemyLayer = LayerMask.GetMask("Enemy");
    }

    public virtual void Unequip(PlayerController playerController) { }

    public void UpdateCooldowns()
    {
        AdvanceSkillCooldowns(Time.deltaTime);
        UpdateChainBuff(Time.deltaTime);
        OnTick(Time.deltaTime);
    }

    private void AdvanceSkillCooldowns(float deltaTime)
    {
        if (_skillCooldownTimer <= 0f && _ultimateCooldownTimer <= 0f) return;
        float elapsed = deltaTime / SkillCooldownTimeScale;
        _skillCooldownTimer = Mathf.Max(0f, _skillCooldownTimer - elapsed);
        _ultimateCooldownTimer = Mathf.Max(0f, _ultimateCooldownTimer - elapsed);
    }

    public void UpdateSkillCooldownUI()
    {
        PlayerUIManager.Instance?.UpdateSkillCoolTime(SkillCooldownRemaining, SkillCooldownDuration);
    }

    protected virtual void OnTick(float deltaTime) { }
    protected virtual void OnHitEnemy(GameObject enemy, WeaponActionData step = null, HitEvent hitEvent = null)
    {
        SpawnHitVFX(enemy, step, hitEvent);
        _playerController.StatManager.AddUltimateGauge(1);

    }
}
