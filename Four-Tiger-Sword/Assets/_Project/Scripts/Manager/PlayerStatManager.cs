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

    public event Action OnStatsChanged; // 스탯 바뀔 때 UI 등에 알려주는 이벤트

    // ── 런타임 HP/SP 상태 ────────────────────────────────────────────────────────
    private float _currentHp;
    private float _currentSp;
    private float _spRegenTimer;

    public float CurrentHp => _currentHp;
    public float CurrentSp => _currentSp;

    // ── 런타임 스테미나 상태 ──────────────────────────────────────────────────────
    private float _currentStamina;
    private float _staminaRegenTimer;

    public float CurrentStamina       => _currentStamina;
    public bool  HasEnoughStaminaForDash => _currentStamina >= (baseData != null ? baseData.dashStaminaCost : 25f);
    public bool  CanRun               => _currentStamina > 0f;

    /// <summary>피해를 받을 때마다 발생합니다. EarthForm 흡수 스탯 등이 구독합니다.</summary>
    public event Action<int, DamageType, bool> OnDamageTaken;

    private void Awake()
    {
        InitializeFromData(); // SO 데이터로 초기화
    }

    // === S (Situation): 초기화 ===
    private void InitializeFromData()
    {
        if (baseData == null) return; // SO 연결 안 했으면 무시

        baseValues[StatType.ST_HP] = baseData.baseHP;   // 체력
        baseValues[StatType.ST_SP] = baseData.baseSP;   // 영력
        baseValues[StatType.ST_STM] = baseData.baseSTM;   // 스테미나
        baseValues[StatType.ST_SP_REGEN] = baseData.baseSpRegen;    // 영력 회복
        baseValues[StatType.ST_ATK] = baseData.baseATK; // 공격력
        baseValues[StatType.ST_DEF] = baseData.baseDEF; // 방어력
        baseValues[StatType.ST_CRT] = baseData.baseCRT; // 치명타율
        baseValues[StatType.ST_CRTD] = baseData.baseCRTD; // 치명타 데미지
        baseValues[StatType.ST_PEN] = baseData.basePEN; // 관통력
        baseValues[StatType.ST_ELM_ATK] = baseData.baseElmAtk; // 속성 공격
        baseValues[StatType.ST_ELM_RES] = baseData.baseElmRes; // 속성 저항
        baseValues[StatType.ST_BRK] = baseData.baseBRK; // 강인도 파괴
        baseValues[StatType.ST_CDR] = baseData.baseCDR; // 스킬 쿨타임 감소
        baseValues[StatType.ST_ANOMALY] = baseData.baseAnomaly; // 상태이상 축적
        baseValues[StatType.ST_ANOMALY_RES] = baseData.baseAnomalyRes; // 상태이상 내성
        baseValues[StatType.ST_IN_GAGE] = baseData.baseInGage; // 인 게이지
        baseValues[StatType.ST_IN_GAGE_RATE] = baseData.baseInGageRate; // 인 게이지 충전 효율
        baseValues[StatType.ST_SPD] = baseData.baseSPD; // 이동속도
        baseValues[StatType.DEF_POISE] = baseData.baseDefPoise; // 방어 강인도      
        baseValues[StatType.KNOCKBACK_RESIST] = baseData.baseKnockbackResist; // 넉백 저항

        // Add/Mult 초기화
        foreach (StatType stat in Enum.GetValues(typeof(StatType)))
        {
            addValues[stat] = 0f; // 가산 버프 없음
            multValues[stat] = 0f; // 승산 버프 없음
        }

        RecalculateAll();   // 전체 스탯 한번 계산

        _currentHp       = GetStat(StatType.ST_HP);
        _currentSp       = GetStat(StatType.ST_SP);
        _currentStamina  = GetStat(StatType.ST_STM);
    }


    // === 버프 추가: 장비 장착, 스킬 버프 등에서 호출 ===
    public void AddModifier(StatType stat, float addValue, float multValue)
    {
        addValues[stat] += addValue; // 가산 값 누적 (예: +10 공격력)
        multValues[stat] += multValue; // 승산 값 누적 (예: +0.3 = 30% 증가)
        RecalculateStat(stat);  // 해당 스탯만 재계산
    }


    // === 버프 제거: 장비 해제, 버프 종료 시 호출 ===
    public void RemoveModifier(StatType stat, float addValue, float multValue)
    {
        addValues[stat] -= addValue; // 가산 값 제거 (예: -10 공격력)
        multValues[stat] -= multValue; // 승산 값 제거 (예: -0.3 = 30% 감소)
        RecalculateStat(stat); // 해당 스탯만 재계산
    }



    // === 스탯 재계산: 버프 적용 후 각 스탯별로 계산 ===
    private void RecalculateStat(StatType stat)
    {
        float baseVal = baseValues.GetValueOrDefault(stat, 0f); // 기본값 (SO에서 읽어온 원본)  
        float addVal = addValues.GetValueOrDefault(stat, 0f); // 가산 버프 합계
        float multVal = multValues.GetValueOrDefault(stat, 0f); // 승산 버프 합계

        // 계산식: (기본값 + 가산값) * (1 + 승산값)
        float result = (baseVal + addVal) * (1f + multVal);

        // === E (Exception): 예외 처리 ===
        result = ApplyClamp(stat, result); // 범위 제한 적용

        finalValues[stat] = result; // 최종값 저장
        OnStatsChanged?.Invoke(); // UI 등에 변경 알림
    }

    // === 전체 스탯 재계산 (초기화 시 사용) ===
    private void RecalculateAll()
    {
        foreach (StatType stat in Enum.GetValues(typeof(StatType)))
        {
            float baseVal = baseValues.GetValueOrDefault(stat, 0f);
            float addVal = addValues.GetValueOrDefault(stat, 0f);
            float multVal = multValues.GetValueOrDefault(stat, 0f);

            float result = (baseVal + addVal) * (1f + multVal);
            finalValues[stat] = ApplyClamp(stat, result);
        }

        OnStatsChanged?.Invoke();   // 전체 재계산 후 한번만 알림
    }

    // === 범위 제한: 특정 스탯이 말도 안 되는 값 되는 거 방지 ===
    private float ApplyClamp(StatType stat, float value)
    {
        return stat switch
        {
            StatType.ST_SPD => Mathf.Max(value, 2f), // 이동속도 최소 2 (너무 느려지면 안됨)
            StatType.ST_CRT => Mathf.Clamp(value, 0f, 100f), // 치명타율 0~100%
            StatType.KNOCKBACK_RESIST => Mathf.Clamp(value, 0f, 1f), // 넉백저항 0~1
            _ => value   // 나머지는 제한 없음
        };
    }

    // === 외부에서 최종 스탯 가져가기 (데미지 계산, UI 표시 등) ===
    public float GetStat(StatType stat)
    {
        return finalValues.GetValueOrDefault(stat, 0f);
    }

    // === 외부에서 기본 스탯 가져가기 (장비창 "기본값" 표시용) ===
    public float GetBaseStat(StatType stat)
    {
        return baseValues.GetValueOrDefault(stat, 0f);
    }

    // ── HP / TakeDamage ──────────────────────────────────────────────────────────

    /// <summary>피해를 적용합니다. PlayerController.TakeDamage에서 호출됩니다.</summary>
    public void TakeDamage(int damage, DamageType damageType, bool isCritical)
    {
        _currentHp = Mathf.Max(0f, _currentHp - damage);
        OnDamageTaken?.Invoke(damage, damageType, isCritical);
    }

    // ── SP 메서드 ────────────────────────────────────────────────────────────────

    /// <summary>SP를 소모합니다. 부족하면 false를 반환하고 소모하지 않습니다.</summary>
    public bool TryConsumeSp(float amount)
    {
        if (_currentSp < amount) return false;
        _currentSp = Mathf.Max(0f, _currentSp - amount);
        _spRegenTimer = baseData != null ? baseData.baseSpRegenDelay : 2f;
        return true;
    }

    /// <summary>SP를 외부에서 직접 추가합니다 (아이템, 보조 스킬 등).</summary>
    public void AddSp(float amount)
    {
        _currentSp = Mathf.Min(_currentSp + amount, GetStat(StatType.ST_SP));
    }

    /// <summary>현재 SP가 요구량 이상인지 확인합니다.</summary>
    public bool HasEnoughSp(float amount) => _currentSp >= amount;

    /// <summary>PlayerController.Update()에서 매 프레임 호출. SP 자동 회복을 처리합니다.</summary>
    public void UpdateSpRegen(float deltaTime)
    {
        if (_spRegenTimer > 0f)
        {
            _spRegenTimer -= deltaTime;
            return;
        }

        float maxSp = GetStat(StatType.ST_SP);
        if (_currentSp < maxSp)
            _currentSp = Mathf.Min(_currentSp + GetStat(StatType.ST_SP_REGEN) * deltaTime, maxSp);
    }

    // ── 스테미나 메서드 ──────────────────────────────────────────────────────────

    /// <summary>PlayerController.Update()에서 매 프레임 호출. 스테미나 자동 회복을 처리합니다.</summary>
    public void UpdateStamina(float deltaTime)
    {
        if (_staminaRegenTimer > 0f)
        {
            _staminaRegenTimer -= deltaTime;
            return;
        }

        float maxStamina = GetStat(StatType.ST_STM);
        if (_currentStamina < maxStamina)
            _currentStamina = Mathf.Min(_currentStamina + (baseData != null ? baseData.staminaRegenRate : 20f) * deltaTime, maxStamina);
    }

    /// <summary>대쉬 1회 시 PlayerDashState에서 호출됩니다.</summary>
    public void ConsumeStaminaForDash()
    {
        float cost = baseData != null ? baseData.dashStaminaCost : 25f;
        _currentStamina    = Mathf.Max(0f, _currentStamina - cost);
        _staminaRegenTimer = baseData != null ? baseData.staminaRegenDelay : 2f;
    }

    /// <summary>달리기 중 매 프레임 PlayerRunState에서 호출됩니다.</summary>
    public void ConsumeStaminaForRun(float deltaTime)
    {
        float costPerSec   = baseData != null ? baseData.runStaminaCostPerSecond : 10f;
        _currentStamina    = Mathf.Max(0f, _currentStamina - costPerSec * deltaTime);
        _staminaRegenTimer = baseData != null ? baseData.staminaRegenDelay : 2f;
    }

    // ── HUD 단축 속성 ────────────────────────────────────────────────────────────
    public float CurrentHP => _currentHp;
    public float MaxHP => GetStat(StatType.ST_HP);

    public float CurrentSP => _currentSp;
    public float MaxSP => GetStat(StatType.ST_SP);
}