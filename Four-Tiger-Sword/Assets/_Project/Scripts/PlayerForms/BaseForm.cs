using UnityEngine;

public abstract partial class BaseForm : IForm
{
    protected PlayerController   _playerController;
    protected WeaponActionDataSO _weaponActionData;

    protected int   _comboStep    = 0;
    protected int   _skillStep    = 0;
    protected int   _ultimateStep = 0;
    protected float _timer        = 0;

    protected LayerMask _enemyLayer;

    protected Transform _softTarget;
    private const float SoftTargetSearchRadius = 10f;
    private const float SoftTargetAngle        = 180f;
    private const float SoftTargetRotationSpeed = 540f; // degrees/sec

    public virtual ElementType Element => ElementType.ELEMENT_NONE;

    protected enum ActionType { Attack, AirAttack, Skill, Ultimate }
    protected ActionType _currentAction = ActionType.Attack;

    // ── SP 비용 & 쿨타임 ─────────────────────────────────────────────────────
    public virtual float SkillSpCost      => 0f;
    public virtual float SkillCooldown    => 5f;
    public virtual float UltimateSpCost   => 0f;
    public virtual float UltimateCooldown => 30f;

    private float _skillCooldownTimer    = 0f;
    private float _ultimateCooldownTimer = 0f;

    public bool CanSkill    => _skillCooldownTimer    <= 0f && _playerController.StatManager.HasEnoughSp(SkillSpCost);
    public bool CanUltimate => _ultimateCooldownTimer <= 0f && _playerController.StatManager.HasEnoughSp(UltimateSpCost);

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
        if (_skillCooldownTimer    > 0f) _skillCooldownTimer    -= Time.deltaTime;
        if (_ultimateCooldownTimer > 0f) _ultimateCooldownTimer -= Time.deltaTime;
        OnTick(Time.deltaTime);
    }

    protected virtual void OnTick(float deltaTime) { }
    protected virtual void OnHitEnemy(GameObject enemy) { }
}
