using System;
using UnityEngine;

public class PlayerStat : IDamageable
{
    private PlayerController _playerController;
    // ── HP ───────────────────────────────────────────────────────────────────
    public float MaxHp      { get; private set; }
    public float CurrentHp  { get; private set; }

    // ── SP (영력) ─────────────────────────────────────────────────────────────
    public float MaxSp         { get; private set; }
    public float CurrentSp     { get; private set; }
    public float SpRegenRate   { get; private set; }  // 초당 회복량
    public float SpRegenDelay  { get; private set; }  // 소모 후 회복 대기 시간
    private float _spRegenTimer;

    // ── 전투 스탯 (추후 확장) ──────────────────────────────────────────────────
    public float Attack         { get; private set; }
    public float Defense        { get; private set; }
    public float Critical       { get; private set; }
    public float CriticalDamage { get; private set; }
    public float DefenseIgnore  { get; private set; }

    public float ElmAtk      { get; private set; }
    public float ElmRes      { get; private set; }
    public float Break       { get; private set; }
    public float CDR         { get; private set; }
    public float Anomaly     { get; private set; }
    public float AnomalyRes  { get; private set; }

    public float Speed        { get; private set; }
    public float DefPoise     { get; private set; }
    public float KnockbackRes { get; private set; }

    // ── 생성자 ────────────────────────────────────────────────────────────────
    public PlayerStat(float maxHp, float maxSp, PlayerController playerController, float spRegenRate = 50f, float spRegenDelay = 2f)
    {
        _playerController = playerController;
        MaxHp        = maxHp;
        CurrentHp    = maxHp;
        MaxSp        = maxSp;
        CurrentSp    = maxSp;
        SpRegenRate  = spRegenRate;
        SpRegenDelay = spRegenDelay;
    }

    // ── SP 메서드 ─────────────────────────────────────────────────────────────

    /// <summary>SP를 소모합니다. 부족하면 false를 반환하고 소모하지 않습니다.</summary>
    public bool TryConsumeSp(float amount)
    {
        if (CurrentSp < amount) return false;

        CurrentSp      = Mathf.Max(0f, CurrentSp - amount);
        _spRegenTimer  = SpRegenDelay;
        return true;
    }

    /// <summary>SP를 외부에서 직접 추가합니다 (아이템, 보조 스킬 등).</summary>
    public void AddSp(float amount)
    {
        CurrentSp = Mathf.Min(CurrentSp + amount, MaxSp);
    }

    /// <summary>PlayerController.Update() 에서 매 프레임 호출. SP 자동 회복을 처리합니다.</summary>
    public void UpdateSpRegen(float deltaTime)
    {
        if (_spRegenTimer > 0f)
        {
            _spRegenTimer -= deltaTime;
            return;
        }

        if (CurrentSp < MaxSp)
            CurrentSp = Mathf.Min(CurrentSp + SpRegenRate * deltaTime, MaxSp);
    }

    /// <summary>현재 SP가 요구량 이상인지 확인합니다.</summary>
    public bool HasEnoughSp(float amount) => CurrentSp >= amount;

    // ── IDamageable ───────────────────────────────────────────────────────────

    /// <summary>피해를 받을 때마다 발생합니다. EarthForm의 흡수 스탯이 구독합니다.</summary>
    public event Action<int, DamageType, bool> OnDamageTaken;

    public void TakeDamage(int damage, DamageType damageType, bool isCritical, Vector3 power = default, float poiseDamage = 20f)
    {
        if (_playerController.IsDashing) return;
        CurrentHp = Mathf.Max(0f, CurrentHp - damage);
        OnDamageTaken?.Invoke(damage, damageType, isCritical);
    }
}
