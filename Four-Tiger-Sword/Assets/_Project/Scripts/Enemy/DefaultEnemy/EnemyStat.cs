using System;
using System.Collections.Generic;
using UnityEngine;

public class EnemyStat : MonoBehaviour
{
    [SerializeField] private EnemyData baseData;

    // 스탯 저장소
    private Dictionary<EnemyStatType, float> baseValues = new(); // 기본값 (SO에서 읽어온 원본)
    private Dictionary<EnemyStatType, float> addValues = new();  // 가산 버프 합계 (+10, +20 등)
    private Dictionary<EnemyStatType, float> multValues = new(); // 승산 버프 합계 (+30% 등)
    private Dictionary<EnemyStatType, float> finalValues = new(); // 최종 계산된 값 (실제 사용하는 값)

    public event Action OnStatsChanged; // 스탯 바뀔 때 알려주는 이벤트

    // ── 런타임 HP 상태 ───────────────────────────────────────────────────────────
    public float CurrentHp;

    public float MaxHp => GetStat(EnemyStatType.HP);
    public bool IsDead => CurrentHp <= 0f;

    // ── EnemyData 전용 비수치 필드 ───────────────────────────────────────────────
    public ElementType ElementType => baseData != null ? baseData.elementType : ElementType.ELEMENT_NONE;
    public StaggerResistLevel StaggerResistLevel => baseData != null ? baseData.staggerResistLevel : StaggerResistLevel.NONE;
    public MonsterSkillDataSO MonsterSkillData => baseData != null ? baseData.monsterSkillData : null;

    public event Action<float, ElementType, bool> OnDamageTaken;

    private void Awake()
    {
        InitializeFromData();
    }

    // === 초기화: SO 데이터로 스탯 세팅 ===
    private void InitializeFromData()
    {
        if (baseData == null) return;

        baseValues[EnemyStatType.HP]                  = baseData.maxHp;
        baseValues[EnemyStatType.ATK]                 = baseData.baseAtk;
        baseValues[EnemyStatType.DEF]                 = baseData.baseDef;
        baseValues[EnemyStatType.CombatRange]         = baseData.combatRange;
        baseValues[EnemyStatType.CombatExitRange]     = baseData.combatExitRange;
        baseValues[EnemyStatType.KnockbackResistance] = baseData.knockbackResistance;
        baseValues[EnemyStatType.KnockbackDuration]   = baseData.knockbackDuration;
        baseValues[EnemyStatType.AttackCooldown]      = baseData.attackCooldown;

        foreach (EnemyStatType stat in Enum.GetValues(typeof(EnemyStatType)))
        {
            addValues[stat] = 0f;
            multValues[stat] = 0f;
        }

        RecalculateAll();

        CurrentHp = GetStat(EnemyStatType.HP);
    }


    // === 버프 추가: 상태이상, 외부 효과 등에서 호출 ===
    public void AddModifier(EnemyStatType stat, float addValue, float multValue)
    {
        addValues[stat] += addValue;
        multValues[stat] += multValue;
        RecalculateStat(stat);
    }


    // === 버프 제거: 버프 종료 시 호출 ===
    public void RemoveModifier(EnemyStatType stat, float addValue, float multValue)
    {
        addValues[stat] -= addValue;
        multValues[stat] -= multValue;
        RecalculateStat(stat);
    }


    // === 스탯 재계산: 버프 적용 후 해당 스탯만 재계산 ===
    private void RecalculateStat(EnemyStatType stat)
    {
        float baseVal = baseValues.GetValueOrDefault(stat, 0f);
        float addVal  = addValues.GetValueOrDefault(stat, 0f);
        float multVal = multValues.GetValueOrDefault(stat, 0f);

        // 계산식: (기본값 + 가산값) * (1 + 승산값)
        float result = (baseVal + addVal) * (1f + multVal);
        result = ApplyClamp(stat, result);

        finalValues[stat] = result;
        OnStatsChanged?.Invoke();
    }

    // === 전체 스탯 재계산 (초기화 시 사용) ===
    private void RecalculateAll()
    {
        foreach (EnemyStatType stat in Enum.GetValues(typeof(EnemyStatType)))
        {
            float baseVal = baseValues.GetValueOrDefault(stat, 0f);
            float addVal  = addValues.GetValueOrDefault(stat, 0f);
            float multVal = multValues.GetValueOrDefault(stat, 0f);

            float result = (baseVal + addVal) * (1f + multVal);
            finalValues[stat] = ApplyClamp(stat, result);
        }

        OnStatsChanged?.Invoke();
    }

    // === 범위 제한: 말도 안 되는 값 방지 ===
    private float ApplyClamp(EnemyStatType stat, float value)
    {
        return stat switch
        {
            EnemyStatType.HP                  => Mathf.Max(value, 0f),
            EnemyStatType.KnockbackResistance => Mathf.Max(value, 0f),
            EnemyStatType.KnockbackDuration   => Mathf.Max(value, 0f),
            EnemyStatType.AttackCooldown      => Mathf.Max(value, 0.1f), // 0 이하 방지
            _ => value
        };
    }

    // === 외부에서 최종 스탯 가져가기 (데미지 계산 등) ===
    public float GetStat(EnemyStatType stat)
    {
        return finalValues.GetValueOrDefault(stat, 0f);
    }

    // === 외부에서 기본 스탯 가져가기 (원본값 확인용) ===
    public float GetBaseStat(EnemyStatType stat)
    {
        return baseValues.GetValueOrDefault(stat, 0f);
    }

    // ── TakeDamage ───────────────────────────────────────────────────────────────

    /// <summary>피해를 적용합니다. Enemy.TakeDamage에서 호출됩니다.</summary>
    public void TakeDamage(float damage, ElementType damageType, bool isCritical)
    {
        CurrentHp = Mathf.Max(0f, CurrentHp - damage);
        OnDamageTaken?.Invoke(damage, damageType, isCritical);
    }

    // ── HUD 단축 속성 ────────────────────────────────────────────────────────────
    public float MaxHP => GetStat(EnemyStatType.HP);
}
