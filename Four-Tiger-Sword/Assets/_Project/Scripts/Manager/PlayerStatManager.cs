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
}