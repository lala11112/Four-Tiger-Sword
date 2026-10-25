 using System;
using System.Collections.Generic;
using UnityEngine;

public class PlayerStatManager : MonoBehaviour
{
    [SerializeField] private PlayerStatData baseData;

    // 스탯 저장소
    private Dictionary<StatType, float> baseValues = new(); // 기본값 (SO에서 읽어온 원본)
    private Dictionary<StatType, float> addValues = new(); // 가산 버프 합계 (+10, +20 등)
    private Dictionary<StatType, float> multValues = new(); // 승산 버프 합계 (+30% 등)
    private Dictionary<StatType, float> finalValues = new(); // 최종 계산된 값 (실제 사용하는 값)

    private bool _initialized;
    private readonly Dictionary<object, ItemResourceModifiers> _itemResourceModifiers = new(ReferenceSourceComparer.Instance);

    private readonly struct ItemResourceModifiers
    {
        public readonly float MaxHpRatio, MaxSpAdd, SpRegenAdd, MaxSpCap, SpRegenCap;
        public ItemResourceModifiers(float hpRatio, float spAdd, float regenAdd, float spCap, float regenCap)
        { MaxHpRatio = hpRatio; MaxSpAdd = spAdd; SpRegenAdd = regenAdd; MaxSpCap = spCap; SpRegenCap = regenCap; }
    }

    private sealed class ReferenceSourceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceSourceComparer Instance = new();
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object source) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(source);
    }

    public event Action OnStatsChanged; // 스탯 바뀔 때 UI 등에 알려주는 이벤트

    // ── 런타임 HP/SP 상태 ────────────────────────────────────────────────────────
    private float _currentHp;
    private float _currentSp;
    private float _spRegenTimer;

    public float CurrentHp { get { EnsureInitialized(); return _currentHp; } }
    public float CurrentSp { get { EnsureInitialized(); return _currentSp; } }

    // ── 런타임 필살기 게이지 상태 ─────────────────────────────────────────────────
    private float _currentUltimateGauge;

    public float CurrentUltimateGauge => _currentUltimateGauge;
    public float MaxUltimateGauge     => baseData != null ? baseData.maxUltimateGauge : 100f;

    /// <summary>필살기 게이지가 최대치에 도달했을 때 true를 반환합니다.</summary>
    public bool IsUltimateGaugeReady  => _currentUltimateGauge >= MaxUltimateGauge;

    /// <summary>필살기 게이지가 변경될 때마다 발생합니다. (현재량, 최대량) UI 갱신용.</summary>
    public event Action<float, float> OnUltimateGaugeChanged;

    /// <summary>피해를 받을 때마다 발생합니다. EarthForm 흡수 스탯 등이 구독합니다.</summary>
    public event Action<float, ElementType, bool> OnDamageTaken;

    /// <summary>HP가 변경될 때마다 발생합니다. (현재HP, 최대HP) UI 갱신용.</summary>
    public event Action<float, float> OnHpChanged;
    public event Action<float, float> OnSpChanged;

    private void Awake()
    {
        EnsureInitialized();
    }

    /// <summary>Initializes base data and resource state explicitly for a new player or regression fixture.</summary>
    public void Initialize(PlayerStatData data)
    {
        baseData = data;
        baseValues.Clear();
        addValues.Clear();
        multValues.Clear();
        finalValues.Clear();
        _itemResourceModifiers.Clear();
        _shields.Clear();
        _spRegenTimer = 0f;
        _initialized = false;
        EnsureInitialized();
    }

    public void EnsureInitialized()
    {
        if (!_initialized) InitializeFromData();
    }

    // === S (Situation): 초기화 ===
    private void InitializeFromData()
    {
        _initialized = true;

        baseValues[StatType.ST_HP] = baseData != null ? baseData.baseHP : 100f;
        baseValues[StatType.ST_SP] = baseData != null ? baseData.baseSP : 100f;
        baseValues[StatType.ST_SP_REGEN] = baseData != null ? baseData.baseSpRegen : 20f;
        baseValues[StatType.ST_ATK] = baseData != null ? baseData.baseATK : 10f;
        baseValues[StatType.ST_DEF] = baseData != null ? baseData.baseDEF : 5f;
        baseValues[StatType.ST_CRT] = baseData != null ? baseData.baseCRT : 5f;
        baseValues[StatType.ST_CRTD] = baseData != null ? baseData.baseCRTD : 150f;
        baseValues[StatType.ST_PEN] = baseData != null ? baseData.basePEN : 0f;
        baseValues[StatType.ST_ELM_ATK] = baseData != null ? baseData.baseElmAtk : 0f;
        baseValues[StatType.ST_ELM_RES] = baseData != null ? baseData.baseElmRes : 0f;
        baseValues[StatType.ST_BRK] = baseData != null ? baseData.baseBRK : 1f;
        baseValues[StatType.ST_CDR] = baseData != null ? baseData.baseCDR : 0f;
        baseValues[StatType.ST_ANOMALY] = baseData != null ? baseData.baseAnomaly : 0f;
        baseValues[StatType.ST_ANOMALY_RES] = baseData != null ? baseData.baseAnomalyRes : 0f;
        baseValues[StatType.ST_IN_GAGE] = baseData != null ? baseData.baseInGage : 0f;
        baseValues[StatType.ST_IN_GAGE_RATE] = baseData != null ? baseData.baseInGageRate : 1f;
        baseValues[StatType.ST_SPD] = baseData != null ? baseData.baseSPD : 5f;
        baseValues[StatType.DEF_POISE] = baseData != null ? baseData.baseDefPoise : 50f;
        baseValues[StatType.KNOCKBACK_RESIST] = baseData != null ? baseData.baseKnockbackResist : 0.2f;
        baseValues[StatType.ST_ATK_SPD] = baseData != null ? baseData.baseAtkSpd : 1f;

        // Add/Mult 초기화
        foreach (StatType stat in Enum.GetValues(typeof(StatType)))
        {
            addValues[stat] = 0f; // 가산 버프 없음
            multValues[stat] = 0f; // 승산 버프 없음
        }

        RecalculateAll(false);

        _currentHp             = GetStat(StatType.ST_HP);
        _currentSp             = GetStat(StatType.ST_SP);
        _currentUltimateGauge  = 0f;
        OnStatsChanged?.Invoke();
        OnHpChanged?.Invoke(_currentHp, MaxHp);
        OnSpChanged?.Invoke(_currentSp, MaxSp);
    }


    // === 버프 추가: 장비 장착, 스킬 버프 등에서 호출 ===
    public void AddModifier(StatType stat, float addValue, float multValue)
    {
        EnsureInitialized();
        if (!IsFinite(addValue) || !IsFinite(multValue)) return;
        addValues[stat] += addValue; // 가산 값 누적 (예: +10 공격력)
        multValues[stat] += multValue; // 승산 값 누적 (예: +0.3 = 30% 증가)
        RecalculateStat(stat);  // 해당 스탯만 재계산
    }


    // === 버프 제거: 장비 해제, 버프 종료 시 호출 ===
    public void RemoveModifier(StatType stat, float addValue, float multValue)
    {
        EnsureInitialized();
        if (!IsFinite(addValue) || !IsFinite(multValue)) return;
        addValues[stat] -= addValue; // 가산 값 제거 (예: -10 공격력)
        multValues[stat] -= multValue; // 승산 값 제거 (예: -0.3 = 30% 감소)
        RecalculateStat(stat); // 해당 스탯만 재계산
    }



    /// <summary>Replaces one item's owner contribution atomically without touching form/skill modifiers.</summary>
    public void SetItemResourceModifiers(object source, float maxHpRatio, float maxSpAdd, float spRegenAdd,
        float maxSpCap = 200f, float spRegenCap = 40f)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (!IsFinite(maxHpRatio) || !IsFinite(maxSpAdd) || !IsFinite(spRegenAdd)
            || !IsFinite(maxSpCap) || !IsFinite(spRegenCap) || maxSpCap <= 0f || spRegenCap <= 0f)
            throw new ArgumentException("Item resource modifiers and positive caps must be finite.");
        EnsureInitialized();
        _itemResourceModifiers[source] = new ItemResourceModifiers(maxHpRatio, maxSpAdd, spRegenAdd, maxSpCap, spRegenCap);
        RecalculateAll();
    }

    public void RemoveItemResourceModifiers(object source)
    {
        if (source != null && _itemResourceModifiers.Remove(source)) RecalculateAll();
    }

    // === 스탯 재계산: 버프 적용 후 각 스탯별로 계산 ===
    private void RecalculateStat(StatType stat)
    {
        float previousMaxHp = finalValues.GetValueOrDefault(StatType.ST_HP);
        float previousMaxSp = finalValues.GetValueOrDefault(StatType.ST_SP);
        finalValues[stat] = CalculateStat(stat);
        PublishStatChanges(previousMaxHp, previousMaxSp);
    }

    // === 전체 스탯 재계산 (초기화 시 사용) ===
    private void RecalculateAll(bool publish = true)
    {
        float previousMaxHp = finalValues.GetValueOrDefault(StatType.ST_HP);
        float previousMaxSp = finalValues.GetValueOrDefault(StatType.ST_SP);
        foreach (StatType stat in Enum.GetValues(typeof(StatType)))
            finalValues[stat] = CalculateStat(stat);
        if (publish) PublishStatChanges(previousMaxHp, previousMaxSp);
    }

    private float CalculateStat(StatType stat)
    {
        double add = addValues.GetValueOrDefault(stat), mult = multValues.GetValueOrDefault(stat);
        foreach (var modifiers in _itemResourceModifiers.Values)
        {
            if (stat == StatType.ST_HP) mult += modifiers.MaxHpRatio;
            else if (stat == StatType.ST_SP) add += modifiers.MaxSpAdd;
            else if (stat == StatType.ST_SP_REGEN) add += modifiers.SpRegenAdd;
        }
        double result = (baseValues.GetValueOrDefault(stat) + add) * (1d + mult);
        return ApplyClamp(stat, (float)Math.Max(-float.MaxValue, Math.Min(float.MaxValue, result)));
    }

    private void PublishStatChanges(float previousMaxHp, float previousMaxSp)
    {
        float hpBefore = _currentHp, spBefore = _currentSp;
        _currentHp = Mathf.Clamp(_currentHp, 0f, MaxHp);
        _currentSp = Mathf.Clamp(_currentSp, 0f, MaxSp);
        OnStatsChanged?.Invoke();
        if (previousMaxHp != MaxHp || hpBefore != _currentHp) OnHpChanged?.Invoke(_currentHp, MaxHp);
        if (previousMaxSp != MaxSp || spBefore != _currentSp) OnSpChanged?.Invoke(_currentSp, MaxSp);
    }

    // === 범위 제한: 특정 스탯이 말도 안 되는 값 되는 거 방지 ===
    private float ApplyClamp(StatType stat, float value)
    {
        if (!IsFinite(value)) value = 0f;
        if (stat == StatType.ST_HP || stat == StatType.ST_SP || stat == StatType.ST_SP_REGEN)
            value = Mathf.Max(0f, value);
        foreach (var modifiers in _itemResourceModifiers.Values)
        {
            if (stat == StatType.ST_SP) value = Mathf.Min(value, modifiers.MaxSpCap);
            else if (stat == StatType.ST_SP_REGEN) value = Mathf.Min(value, modifiers.SpRegenCap);
        }
        return stat switch
        {
            StatType.ST_SPD => Mathf.Max(value, 2f), // 이동속도 최소 2 (너무 느려지면 안됨)
            StatType.ST_CRT => Mathf.Clamp(value, 0f, 100f), // 치명타율 0~100%
            StatType.KNOCKBACK_RESIST => Mathf.Clamp(value, 0f, 1f), // 넉백저항 0~1
            StatType.ST_ATK_SPD => Mathf.Max(value, 0.1f), // 공격속도 최소 0.1 (0 이하 방지)
            _ => value   // 나머지는 제한 없음
        };
    }

    // === 외부에서 최종 스탯 가져가기 (데미지 계산, UI 표시 등) ===
    public float GetStat(StatType stat)
    {
        EnsureInitialized();
        return finalValues.GetValueOrDefault(stat, 0f);
    }

    // === 외부에서 기본 스탯 가져가기 (장비창 "기본값" 표시용) ===
    public float GetBaseStat(StatType stat)
    {
        EnsureInitialized();
        return baseValues.GetValueOrDefault(stat, 0f);
    }

    // ── HP / TakeDamage ──────────────────────────────────────────────────────────

    /// <summary>피해를 적용합니다. PlayerController.TakeDamage에서 호출됩니다.</summary>
    public DamageResult TakeDamage(float damage, ElementType damageType, bool isCritical)
    {
        EnsureInitialized();
        if (GetComponent<PlayerController>()?.IsStageTransitioning == true) return default;
        if (_currentHp <= 0f || damage < 0f || float.IsNaN(damage) || float.IsInfinity(damage)) return default;
        float hpBefore = _currentHp;
        float absorbed = AbsorbWithShields(Mathf.Max(0f, damage));
        damage = Mathf.Max(0f, damage - absorbed);
        _currentHp = Mathf.Max(0f, _currentHp - damage);
        float healthDamage = hpBefore - _currentHp;
        if (healthDamage > 0f) OnDamageTaken?.Invoke(healthDamage, damageType, isCritical);
        OnHpChanged?.Invoke(_currentHp, GetStat(StatType.ST_HP));
        return new DamageResult(DamageOutcome.Applied, healthDamage, absorbed, _currentHp <= 0f);
    }

    // ── SP 메서드 ────────────────────────────────────────────────────────────────

    /// <summary>SP를 소모합니다. 부족하면 false를 반환하고 소모하지 않습니다.</summary>
    public bool TryConsumeSp(float amount)
    {
        EnsureInitialized();
        if (!IsFinite(amount) || amount < 0f || _currentSp < amount) return false;
        if (amount == 0f) return true;
        _currentSp = Mathf.Max(0f, _currentSp - amount);
        float delay = baseData != null ? baseData.baseSpRegenDelay : 1f;
        _spRegenTimer = IsFinite(delay) ? Mathf.Max(0f, delay) : 1f;
        OnSpChanged?.Invoke(_currentSp, GetStat(StatType.ST_SP));
        return true;
    }

    /// <summary>SP를 외부에서 직접 추가합니다 (아이템, 보조 스킬 등).</summary>
    public void AddSp(float amount)
    {
        EnsureInitialized();
        if (!IsFinite(amount) || amount <= 0f) return;
        _currentSp = Mathf.Min(_currentSp + amount, GetStat(StatType.ST_SP));
        OnSpChanged?.Invoke(_currentSp, GetStat(StatType.ST_SP));
    }

    /// <summary>현재 SP가 요구량 이상인지 확인합니다.</summary>
    public bool HasEnoughSp(float amount) => IsFinite(amount) && amount >= 0f && CurrentSp >= amount;

    /// <summary>PlayerController.Update()에서 매 프레임 호출. SP 자동 회복을 처리합니다.</summary>
    public void UpdateSpRegen(float deltaTime)
    {
        EnsureInitialized();
        if (GetComponent<PlayerController>()?.IsStageTransitioning == true) return;
        if (!IsFinite(deltaTime) || deltaTime <= 0f || _currentHp <= 0f) return;
        if (_spRegenTimer > 0f)
        {
            float waiting = Mathf.Min(_spRegenTimer, deltaTime);
            _spRegenTimer -= waiting;
            deltaTime -= waiting;
            if (deltaTime <= 0f) return;
        }

        float maxSp = GetStat(StatType.ST_SP);
        if (_currentSp < maxSp)
        {
            _currentSp = Mathf.Min(_currentSp + GetStat(StatType.ST_SP_REGEN) * deltaTime, maxSp);
            OnSpChanged?.Invoke(_currentSp, maxSp);
        }
    }

    // ── 필살기 게이지 메서드 ──────────────────────────────────────────────────────

    /// <summary>필살기 게이지를 지정한 양만큼 충전합니다. 최대치를 초과하지 않습니다.</summary>
    public void AddUltimateGauge(float amount)
    {
        EnsureInitialized();
        if (!IsFinite(amount) || amount <= 0f) return;
        _currentUltimateGauge = Mathf.Min(_currentUltimateGauge + amount, MaxUltimateGauge);
        OnUltimateGaugeChanged?.Invoke(_currentUltimateGauge, MaxUltimateGauge);
    }

    /// <summary>필살기 사용 시 호출합니다. 현재 게이지를 전부 소모합니다.</summary>
    public void ConsumeAllUltimateGauge()
    {
        _currentUltimateGauge = 0f;
        OnUltimateGaugeChanged?.Invoke(_currentUltimateGauge, MaxUltimateGauge);
    }

    // ── HUD 단축 속성 ────────────────────────────────────────────────────────────
    public float MaxHp => GetStat(StatType.ST_HP);
    public float MaxSp => GetStat(StatType.ST_SP);

    private sealed class ShieldLayer
    {
        public object Source;
        public float Amount, Remaining;
    }
    private readonly List<ShieldLayer> _shields = new();
    private readonly object _defaultShieldSource = new();
    public float Shield
    {
        get { float total = 0f; foreach (var layer in _shields) total += layer.Amount; return total; }
    }
    public void GrantShield(float amount, float duration)
        => GrantShield(_defaultShieldSource, Mathf.Max(GetShield(_defaultShieldSource), amount), duration);

    /// <summary>출처별 방어막. 같은 출처는 교체하고 다른 출처는 독립적으로 유지합니다.</summary>
    public void GrantShield(object source, float amount, float duration)
    {
        if (source == null) return;
        RemoveShield(source);
        if (amount <= 0f || duration <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)
            || float.IsNaN(duration) || float.IsInfinity(duration)) return;
        _shields.Add(new ShieldLayer { Source = source, Amount = amount, Remaining = duration });
    }
    public float GetShield(object source) => _shields.Find(layer => ReferenceEquals(layer.Source, source))?.Amount ?? 0f;
    public float GetShieldRemaining(object source) => _shields.Find(layer => ReferenceEquals(layer.Source, source))?.Remaining ?? 0f;
    public void RemoveShield(object source) => _shields.RemoveAll(layer => ReferenceEquals(layer.Source, source));
    private float AbsorbWithShields(float damage)
    {
        float remaining = damage;
        // 먼저 부여된 방어막부터 소모합니다.
        for (int i = 0; i < _shields.Count && remaining > 0f;)
        {
            var layer = _shields[i];
            float absorbed = Mathf.Min(layer.Amount, remaining);
            layer.Amount -= absorbed;
            remaining -= absorbed;
            if (layer.Amount <= 0f) _shields.RemoveAt(i); else i++;
        }
        return damage - remaining;
    }
    private void UpdateShields(float dt)
    {
        for (int i = _shields.Count - 1; i >= 0; i--)
        {
            _shields[i].Remaining -= Mathf.Max(0f, dt);
            if (_shields[i].Remaining <= 0f) _shields.RemoveAt(i);
        }
    }
    public void Heal(float amount)
    {
        EnsureInitialized();
        if (_currentHp <= 0f || !IsFinite(amount) || amount <= 0f) return;
        _currentHp = Mathf.Min(MaxHp, _currentHp + amount);
        OnHpChanged?.Invoke(_currentHp, MaxHp);
    }
    /// <summary>체력을 비용으로 소모합니다. 보호막과 피격 이벤트를 거치지 않으며 최소 체력을 남깁니다.</summary>
    public void ConsumeHealth(float amount, float minimumHp = 1f)
    {
        EnsureInitialized();
        if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)
            || float.IsNaN(minimumHp) || float.IsInfinity(minimumHp)) return;
        float cost = Mathf.Min(amount, Mathf.Max(0f, _currentHp - Mathf.Max(0f, minimumHp)));
        if (cost <= 0f) return;
        _currentHp -= cost;
        OnHpChanged?.Invoke(_currentHp, MaxHp);
    }
    private void Update()
    {
        if (GetComponent<PlayerController>()?.IsStageTransitioning == true) return;
        UpdateShields(Time.deltaTime);
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
