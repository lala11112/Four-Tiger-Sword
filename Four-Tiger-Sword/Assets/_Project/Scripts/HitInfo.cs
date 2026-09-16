using UnityEngine;
public readonly struct HitInfo
{
    public readonly float   BaseDamage;
    public readonly ElementType Element;
    public readonly float CriticalChance;
    public readonly float CriticalMultiplier;

    /// <summary>경직(Poise) 데미지 배율. 기본값 1.0 (화: 1.5, 금 스킬: 강제 붕괴)</summary>
    public readonly float PoiseDamageMultiplier;

    /// <summary>방어력만 무시합니다. 속성 상성과 치명타는 유지합니다.</summary>
    public readonly bool ArmorPierce;
    /// <summary>속성 상성, 치명타, 방어력을 모두 무시하는 고정 피해. 무적/가드/보호막은 유지합니다.</summary>
    public readonly bool TrueDamage;
    public readonly object Source;

    public readonly Vector3 Power;

    public readonly float PoiseDamage;

    public readonly StaggerResistLevel StaggerResistLevel;

    /// <summary>false로 설정하면 이 공격은 플레이어의 패링으로 막을 수 없습니다.</summary>
    public readonly bool IsParryable;

    public HitInfo(float damage, ElementType element,
                   float critChance, float critMultiplier,
                   float poiseDamageMultiplier = 1f,
                   bool  armorPierce = false, Vector3 power = default, float poiseDamage = 0f,
                   StaggerResistLevel staggerResistLevel = StaggerResistLevel.NONE,
                   bool isParryable = true, object source = null, bool trueDamage = false)
    {
        BaseDamage            = damage;
        Element               = element;
        CriticalChance        = critChance;
        CriticalMultiplier    = critMultiplier;
        PoiseDamageMultiplier = poiseDamageMultiplier;
        ArmorPierce           = armorPierce;
        Power                 = power;
        PoiseDamage           = poiseDamage;
        StaggerResistLevel    = staggerResistLevel;
        IsParryable           = isParryable;
        Source = source;
        TrueDamage = trueDamage;
    }
}
