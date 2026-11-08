using System;
using UnityEngine;

public enum BoundaryAttackFamily { Any, Normal, Skill, NormalOrSkill }
public enum BoundaryItemStat
{
    MaxHpRatio, AttackRatio, DefenseRatio, CriticalChancePoints, CriticalDamagePoints,
    NormalAttackRateRatio, DamageRatio, IgnoreDefenseRatio, ShieldMaxHpRatio,
    ShieldGainRatio, IncomingDamageReductionRatio, HealMaxHpRatio, GoldFlat, GoldFraction
}
public enum BoundaryItemTrigger { WhileOwned, Kill, DamageTaken, CombatEntered, SkillHit, NormalHit, FirstLowHealth }

/// <summary>Ratios use 0.05 for 5%; critical points use 5 for +5 percentage points.</summary>
[Serializable]
public sealed class BoundaryItemEffect
{
    public string effectId;
    public BoundaryItemStat stat;
    public float amount;
    public BoundaryItemTrigger trigger;
    public BoundaryAttackFamily family;
    public ElementType element;
    public bool eliteOrBossOnly;
    public float targetHpMin = -1f, targetHpMax = -1f;
    public float selfHpMin = -1f, selfHpMax = -1f;
    public bool firstTargetHit, firstSkillHit, firstIncomingAttack, firstRewardKill;
    public bool requiresFullHealth;
    public float minimumHealthLossRatio;
    public int minimumBaseGold, maximumBaseGold;
    public float durationSeconds, cooldownSeconds;

    public bool IsValid()
    {
        if (!Enum.IsDefined(typeof(BoundaryItemStat), stat)
            || !Enum.IsDefined(typeof(BoundaryItemTrigger), trigger)
            || !Enum.IsDefined(typeof(BoundaryAttackFamily), family)
            || !ItemEffect.IsFinite(amount) || amount < 0f
            || !ItemEffect.IsFinite(durationSeconds) || durationSeconds < 0f
            || !ItemEffect.IsFinite(cooldownSeconds) || cooldownSeconds < 0f
            || !ItemEffect.IsFinite(minimumHealthLossRatio) || minimumHealthLossRatio < 0f
            || minimumBaseGold < 0 || maximumBaseGold < 0) return false;
        foreach (float threshold in new[] { targetHpMin, targetHpMax, selfHpMin, selfHpMax })
            if (!ItemEffect.IsFinite(threshold) || threshold < -1f || threshold > 1f) return false;
        return true;
    }
}

[Serializable]
public sealed class ItemSynergyTier
{
    public int count;
    public string text;
    public BoundaryItemEffect[] effects = Array.Empty<BoundaryItemEffect>();
}

[Serializable]
public sealed class ItemSynergyDefinition
{
    public string id, displayName, summary, description;
    public Sprite icon;
    public string[] members = Array.Empty<string>();
    public ItemSynergyTier[] tiers = Array.Empty<ItemSynergyTier>();
}

public readonly struct ItemHitContext
{
    public readonly int TargetId;
    public readonly float TargetHp, TargetMaxHp;
    public readonly bool EliteOrBoss, CanTrigger;
    public readonly BoundaryAttackFamily Family;
    public readonly ElementType Element;
    public readonly long CastId;
    public ItemHitContext(int targetId, float targetHp, float targetMaxHp, bool eliteOrBoss,
        BoundaryAttackFamily family, ElementType element, bool canTrigger = true, long castId = 0)
    {
        TargetId = targetId; TargetHp = targetHp; TargetMaxHp = targetMaxHp; EliteOrBoss = eliteOrBoss;
        Family = family; Element = element; CanTrigger = canTrigger; CastId = castId;
    }
}

public readonly struct ItemOutgoingModifiers
{
    public readonly float DamageMultiplier, CriticalChanceBonus, CriticalMultiplierBonus, IgnoreDefenseRatio;
    public ItemOutgoingModifiers(float damageMultiplier, float criticalChanceBonus,
        float criticalMultiplierBonus, float ignoreDefenseRatio)
    {
        DamageMultiplier = damageMultiplier; CriticalChanceBonus = criticalChanceBonus;
        CriticalMultiplierBonus = criticalMultiplierBonus; IgnoreDefenseRatio = ignoreDefenseRatio;
    }
}

public readonly struct ItemIncomingContext
{
    public readonly bool EnemyAttack, EliteOrBoss;
    public readonly float HpBefore, MaxHpBefore;
    public ItemIncomingContext(bool enemyAttack, bool eliteOrBoss, float hpBefore, float maxHpBefore)
    { EnemyAttack = enemyAttack; EliteOrBoss = eliteOrBoss; HpBefore = hpBefore; MaxHpBefore = maxHpBefore; }
}

public readonly struct ItemKillContext
{
    public readonly int TargetId, BaseGold, CombatEpoch;
    public readonly bool EliteOrBoss, CanTrigger, WasInCombat;
    public readonly BoundaryAttackFamily Family;
    public readonly float PlayerHpBefore, PlayerMaxHpBefore;
    public ItemKillContext(int targetId, bool eliteOrBoss, BoundaryAttackFamily family, bool canTrigger,
        int baseGold, float playerHpBefore, float playerMaxHpBefore, int combatEpoch, bool wasInCombat = false)
    {
        TargetId = targetId; EliteOrBoss = eliteOrBoss; Family = family; CanTrigger = canTrigger;
        BaseGold = baseGold; PlayerHpBefore = playerHpBefore; PlayerMaxHpBefore = playerMaxHpBefore;
        CombatEpoch = combatEpoch;
        WasInCombat = wasInCombat;
    }
}

public readonly struct ItemSynergyState
{
    public readonly ItemSynergyDefinition Definition;
    public readonly int OwnedCount;
    public readonly ItemSynergyTier ActiveTier;
    public ItemSynergyState(ItemSynergyDefinition definition, int ownedCount, ItemSynergyTier activeTier)
    { Definition = definition; OwnedCount = ownedCount; ActiveTier = activeTier; }
}
