using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BossData_", menuName = "SainGum/Monster/BossData")]
public class BossData : EnemyData
{
    [Header("보스 기본 정보")]
    public string bossId; // 보스 고유 ID
    public string bossName; // 보스 이름
    [Header("페이즈 부활 시간")]
    [Min(0f)] public float phaseDownSeconds = 1f;
    [Min(0f)] public float phaseHoldSeconds = .5f;
    [Min(.01f)] public float phaseReviveSeconds = 1.5f;
    // Shared stats live in this same EnemyData asset. Motion, perception and poise
    // settings live on their actual components, rather than duplicate data fields.

    [Header("그로기")]
    public float groggyDamageMult = 1.5f; // 그로기 상태 데미지 배율

    [Header("오행 약점 & 저항")]
    public bool enableWeakness;
    public ElementType weaknessElement; // 약점 속성
    public float weakDamageMult = 1.5f; // 약점 피격 시 데미지 배율
    public bool enableResistance;
    public ElementType resistElement; // 저항 속성
    public float resistDamageMult = 0.5f; // 저항 피격 시 데미지 배율

    [Header("상태이상 면역")]
    public List<string> statusImmunityList = new List<string>(); // 면역 상태이상 목록

    [Header("보호막")]
    [Min(0f)] public float initialShield;

    [Header("보상")]
    public BossRewardGroupSO rewardGroup;

    public bool IsImmuneTo(string effectId)
        => statusImmunityList != null && statusImmunityList.Contains(effectId);

    public bool IsImmuneToStatus(Type effectType)
    {
        string id = effectType == typeof(BindEffect) ? "Bind"
            : effectType == typeof(SlowEffect) ? "Slow"
            : effectType == typeof(AirborneEffect) ? "Airborne"
            : effectType == typeof(SilenceEffect) ? "Silence"
            : effectType == typeof(VulnerabilityEffect) || effectType == typeof(WaterChainVulnerabilityEffect) ? "Vulnerability"
            : effectType == typeof(DefenseReductionEffect) ? "DefenseReduction"
            : effectType == typeof(WeakPointExposedEffect) ? "WeakPointExposed"
            : effectType == typeof(WaterDelayedDamageEffect) ? "WaterDelayedDamage" : null;
        return id != null && IsImmuneTo(id);
    }

    public float ResolveElementMultiplier(ElementType incoming, float globalMultiplier)
    {
        if (enableWeakness && incoming == weaknessElement) return weakDamageMult;
        if (enableResistance && incoming == resistElement) return resistDamageMult;
        return globalMultiplier;
    }
}
