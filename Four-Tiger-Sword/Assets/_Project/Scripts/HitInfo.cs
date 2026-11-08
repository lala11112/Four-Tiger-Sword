using UnityEngine;

public enum PlayerAttackCategory { Other, Normal, SwitchAttack, Skill, Ultimate, Counter, Heavy, Secondary }
public enum DamageOrigin { AuthoredAction, AdditionalEffect, Environmental }

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
    public readonly CombatSoundCue ImpactSound;
    public readonly PlayerAttackCategory Category;
    public readonly float ItemDamageMultiplier;
    public readonly float SourceDamageMultiplier;
    public readonly bool HasSourceDamageSnapshot;
    /// <summary>Explicit player attack contact. Persistent and secondary damage leave this false.</summary>
    public readonly bool IsDirectPlayerHit;
    public readonly long CastId;
    public readonly DamageOrigin Origin;

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
                   bool isParryable = true, object source = null, bool trueDamage = false,
                   CombatSoundCue impactSound = default,
                   PlayerAttackCategory category = PlayerAttackCategory.Other, float itemDamageMultiplier = 1f,
                   float sourceDamageMultiplier = 1f, bool hasSourceDamageSnapshot = false,
                   bool isDirectPlayerHit = false, long castId = 0,
                   DamageOrigin origin = DamageOrigin.AuthoredAction)
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
        ImpactSound = impactSound;
        Category = category;
        ItemDamageMultiplier = itemDamageMultiplier;
        SourceDamageMultiplier = sourceDamageMultiplier;
        HasSourceDamageSnapshot = hasSourceDamageSnapshot;
        IsDirectPlayerHit = isDirectPlayerHit;
        CastId = castId;
        Origin = category == PlayerAttackCategory.Secondary ? DamageOrigin.AdditionalEffect : origin;
    }

    public HitInfo WithDirectPlayerHit(bool isDirectPlayerHit = true)
        => new HitInfo(BaseDamage, Element, CriticalChance, CriticalMultiplier, PoiseDamageMultiplier,
            ArmorPierce, Power, PoiseDamage, StaggerResistLevel, IsParryable, Source, TrueDamage,
            ImpactSound, Category, ItemDamageMultiplier, SourceDamageMultiplier, HasSourceDamageSnapshot,
            isDirectPlayerHit && Category != PlayerAttackCategory.Secondary, CastId, Origin);

    public HitInfo WithoutDirectPlayerHit() => WithDirectPlayerHit(false);
}
