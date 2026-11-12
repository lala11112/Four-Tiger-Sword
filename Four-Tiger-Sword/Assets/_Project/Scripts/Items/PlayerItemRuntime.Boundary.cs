using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class PlayerItemRuntime
{
    private sealed class BoundarySource
    {
        public string Key;
        public BoundaryItemEffect Effect;
        public float Cooldown, Remaining;
        public bool Active;
        public readonly object ShieldSource = new();
    }

    private readonly Dictionary<string, BoundarySource> _boundarySources = new(StringComparer.Ordinal);
    private readonly List<ItemSynergyState> _synergyStates = new();
    private readonly HashSet<int> _normalTargetsHit = new();
    private readonly HashSet<int> _directTargetsHit = new();
    private readonly HashSet<long> _skillCastsHit = new();
    private readonly HashSet<string> _firstLowHealth = new(StringComparer.Ordinal);
    private readonly HashSet<int> _rewardKillEpochs = new();
    private readonly HashSet<(int target, int epoch)> _processedKills = new();
    private bool _inCombat;
    private bool _receivedEnemyAttack;
    private bool _reachedLowHealth;
    private int _combatEpoch;
    private double _goldFraction;

    public int CombatEpoch => _combatEpoch;
    public bool IsInCombat => _inCombat;
    public IReadOnlyList<ItemSynergyState> SynergyStates => _synergyStates.AsReadOnly();
    public float NormalAttackRateMultiplier => Mathf.Max(0.1f,
        1f + PassiveBoundaryStat(BoundaryItemStat.NormalAttackRateRatio));
    public float SkillCooldownReductionRatio => Mathf.Clamp(
        PassiveBoundaryStat(BoundaryItemStat.SkillCooldownReductionRatio), 0f, .40f);
    public double GoldFractionRemainder => _goldFraction;

    private bool BoundaryAvailable => _initialized && !_suspended && _stats != null && _stats.CurrentHp > 0f;

    private Dictionary<string, int> CaptureActiveSynergyTiers()
    {
        if (SynergyTierActivated == null) return null;
        var tiers = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var state in _synergyStates)
            if (state.ActiveTier != null && !string.IsNullOrEmpty(state.Definition?.id))
                tiers[state.Definition.id] = state.ActiveTier.count;
        return tiers;
    }

    private void NotifyActivatedSynergyTiers(Dictionary<string, int> previousTiers)
    {
        if (previousTiers == null || SynergyTierActivated == null) return;
        var activated = new List<ItemSynergyState>();
        foreach (var state in _synergyStates)
        {
            if (state.ActiveTier == null || string.IsNullOrEmpty(state.Definition?.id)) continue;
            previousTiers.TryGetValue(state.Definition.id, out int previous);
            if (state.ActiveTier.count > previous) activated.Add(state);
        }
        var callbacks = SynergyTierActivated.GetInvocationList();
        foreach (var state in activated)
            foreach (Action<ItemSynergyState> callback in callbacks)
            {
                try { callback(state); }
                catch (Exception error) { Debug.LogWarning("[Items] Synergy activation observer failed: " + error, this); }
            }
    }

    private void SyncBoundarySources()
    {
        var wanted = new Dictionary<string, BoundaryItemEffect>(StringComparer.Ordinal);
        foreach (var item in _ownedItems)
        {
            if (item == null || item.boundaryEffects == null) continue;
            for (int index = 0; index < item.boundaryEffects.Length; index++)
                AddWanted(wanted, "item:" + item.itemId + ":" + index, item.boundaryEffects[index]);
        }
        _synergyStates.Clear();
        if (catalog != null && catalog.synergies != null)
            foreach (var synergy in catalog.synergies)
            {
                if (synergy == null) continue;
                int count = 0;
                var counted = new HashSet<string>(StringComparer.Ordinal);
                foreach (string member in synergy.members ?? Array.Empty<string>())
                    if (counted.Add(member) && Contains(member)) count++;
                ItemSynergyTier tier = null;
                foreach (var candidate in synergy.tiers ?? Array.Empty<ItemSynergyTier>())
                    if (candidate != null && candidate.count <= count && (tier == null || candidate.count > tier.count))
                        tier = candidate;
                _synergyStates.Add(new ItemSynergyState(synergy, count, tier));
                if (tier?.effects == null) continue;
                for (int index = 0; index < tier.effects.Length; index++)
                    // Tier changes replace amounts while preserving the synergy's cooldown and shield source.
                    AddWanted(wanted, "synergy:" + synergy.id + ":" + index, tier.effects[index]);
            }
        foreach (var pair in _boundarySources)
            if (!wanted.ContainsKey(pair.Key))
            {
                if (pair.Value.Active) _stats?.RemoveShield(pair.Value.ShieldSource);
                pair.Value.Active = false;
                pair.Value.Remaining = 0f;
                // Keep cooldown tombstones so selling and reacquiring cannot bypass a cooldown.
            }
        foreach (var pair in wanted)
        {
            if (_boundarySources.TryGetValue(pair.Key, out var existing)) { existing.Effect = pair.Value; existing.Active = true; }
            else _boundarySources.Add(pair.Key, new BoundarySource { Key = pair.Key, Effect = pair.Value, Active = true });
        }
    }

    private static void AddWanted(Dictionary<string, BoundaryItemEffect> wanted, string key, BoundaryItemEffect effect)
    {
        if (effect != null && effect.IsValid()) wanted[key] = effect;
    }

    private float PassiveBoundaryStat(BoundaryItemStat stat)
    {
        if (!_initialized || _suspended) return 0f;
        double total = 0d;
        foreach (var source in _boundarySources.Values)
            if (source.Active && source.Effect.stat == stat && source.Effect.trigger == BoundaryItemTrigger.WhileOwned
                && IsUnconditional(source.Effect))
                total += source.Effect.amount;
        return SafeTotal(total);
    }

    private static float SafeTotal(double total)
        => (float)Math.Max(-float.MaxValue / 4d, Math.Min(float.MaxValue / 4d, total));

    private static bool IsUnconditional(BoundaryItemEffect effect)
        => effect.family == BoundaryAttackFamily.Any && effect.element == ElementType.ELEMENT_NONE
            && !effect.eliteOrBossOnly && effect.targetHpMin < 0f && effect.targetHpMax < 0f
            && effect.selfHpMin < 0f && effect.selfHpMax < 0f && !effect.firstTargetHit && !effect.firstSkillHit;

    private static bool FamilyMatches(BoundaryAttackFamily filter, BoundaryAttackFamily actual)
        => filter == BoundaryAttackFamily.Any || filter == actual
            || filter == BoundaryAttackFamily.NormalOrSkill
                && (actual == BoundaryAttackFamily.Normal || actual == BoundaryAttackFamily.Skill);

    private static float HpRatio(float current, float maximum) => maximum > 0f ? Mathf.Clamp01(current / maximum) : 0f;
    private static bool Within(float ratio, float minimum, float maximum)
        => (minimum < 0f || ratio >= minimum - 0.000001f) && (maximum < 0f || ratio <= maximum + 0.000001f);
    private bool SelfCondition(BoundaryItemEffect effect)
        => _stats != null && Within(HpRatio(_stats.CurrentHp, _stats.MaxHp), effect.selfHpMin, effect.selfHpMax);

    private bool HitCondition(BoundarySource source, ItemHitContext context)
    {
        var effect = source.Effect;
        if (!FamilyMatches(effect.family, context.Family)) return false;
        if (effect.element != ElementType.ELEMENT_NONE && effect.element != context.Element) return false;
        if (effect.eliteOrBossOnly && !context.EliteOrBoss) return false;
        if (!Within(HpRatio(context.TargetHp, context.TargetMaxHp), effect.targetHpMin, effect.targetHpMax)
            || !SelfCondition(effect)) return false;
        if (effect.firstTargetHit && (!context.CanTrigger || !_inCombat
            || (effect.family == BoundaryAttackFamily.Normal ? _normalTargetsHit : _directTargetsHit).Contains(context.TargetId))) return false;
        if (effect.firstSkillHit && (!context.CanTrigger || context.CastId == 0
            || _skillCastsHit.Contains(context.CastId))) return false;
        return true;
    }

    public ItemOutgoingModifiers EvaluateOutgoing(ItemHitContext context)
    {
        if (!BoundaryAvailable) return new ItemOutgoingModifiers(1f, 0f, 0f, 0f);
        double damage = 0d, chance = 0d, critical = 0d, defense = 0d;
        foreach (var source in _boundarySources.Values)
        {
            var effect = source.Effect;
            if (!source.Active || effect.trigger != BoundaryItemTrigger.WhileOwned || !HitCondition(source, context)) continue;
            // Permanent critical stats are already part of the player's attack snapshot.
            bool conditional = !IsUnconditional(effect);
            if (effect.stat == BoundaryItemStat.DamageRatio) damage += effect.amount;
            else if (effect.stat == BoundaryItemStat.IgnoreDefenseRatio) defense += effect.amount;
            else if (conditional && effect.stat == BoundaryItemStat.CriticalChancePoints) chance += effect.amount / 100d;
            else if (conditional && effect.stat == BoundaryItemStat.CriticalDamagePoints) critical += effect.amount / 100d;
        }
        return new ItemOutgoingModifiers(Mathf.Max(0f, 1f + SafeTotal(damage)), SafeTotal(chance),
            SafeTotal(critical), Mathf.Clamp01(SafeTotal(defense)));
    }

    public void CommitOutgoing(ItemHitContext context, float healthDamage, float shieldDamage)
    {
        if (!BoundaryAvailable || !context.CanTrigger || healthDamage + shieldDamage <= 0f) return;
        foreach (var source in new List<BoundarySource>(_boundarySources.Values))
        {
            if (!source.Active || !HitCondition(source, context)) continue;
            var effect = source.Effect;
            if (effect.trigger == BoundaryItemTrigger.SkillHit && context.Family == BoundaryAttackFamily.Skill
                || effect.trigger == BoundaryItemTrigger.NormalHit && context.Family == BoundaryAttackFamily.Normal)
                Activate(source);
        }
        if (context.Family == BoundaryAttackFamily.Skill && context.CastId != 0) _skillCastsHit.Add(context.CastId);
        if (_inCombat)
        {
            _directTargetsHit.Add(context.TargetId);
            if (context.Family == BoundaryAttackFamily.Normal) _normalTargetsHit.Add(context.TargetId);
        }
    }

    public float EvaluateIncoming(ItemIncomingContext context)
    {
        if (!BoundaryAvailable || !context.EnemyAttack) return 1f;
        double reduction = 0d;
        foreach (var source in _boundarySources.Values)
        {
            var effect = source.Effect;
            if (!source.Active || effect.stat != BoundaryItemStat.IncomingDamageReductionRatio
                || effect.trigger != BoundaryItemTrigger.WhileOwned && source.Remaining <= 0f
                || effect.eliteOrBossOnly && !context.EliteOrBoss
                || !Within(HpRatio(context.HpBefore, context.MaxHpBefore), effect.selfHpMin, effect.selfHpMax)
                || effect.firstIncomingAttack && (!_inCombat || _receivedEnemyAttack)) continue;
            reduction += effect.amount;
        }
        return Mathf.Clamp01(1f - SafeTotal(reduction));
    }

    public void CommitIncoming(ItemIncomingContext context, float healthDamage, float shieldDamage)
    {
        if (!BoundaryAvailable || !context.EnemyAttack || healthDamage + shieldDamage <= 0f) return;
        foreach (var source in new List<BoundarySource>(_boundarySources.Values))
        {
            var effect = source.Effect;
            if (!source.Active || effect.trigger != BoundaryItemTrigger.DamageTaken || healthDamage <= 0f
                || effect.eliteOrBossOnly && !context.EliteOrBoss
                || !Within(HpRatio(context.HpBefore, context.MaxHpBefore), effect.selfHpMin, effect.selfHpMax)
                || (context.MaxHpBefore > 0f ? healthDamage / context.MaxHpBefore : 0f)
                    + 0.000001f < effect.minimumHealthLossRatio) continue;
            Activate(source);
        }
        if (_inCombat) _receivedEnemyAttack = true;
        CheckFirstLowHealth();
    }

    public void SetCombatState(bool inCombat)
    {
        if (_inCombat == inCombat) return;
        _inCombat = inCombat;
        if (!inCombat) return;
        _combatEpoch++;
        _normalTargetsHit.Clear(); _directTargetsHit.Clear(); _receivedEnemyAttack = false; _reachedLowHealth = false;
        _firstLowHealth.Clear();
        if (!BoundaryAvailable) return;
        foreach (var source in new List<BoundarySource>(_boundarySources.Values))
            if (source.Active && source.Effect.trigger == BoundaryItemTrigger.CombatEntered) Activate(source);
        CheckFirstLowHealth();
    }

    private void CheckFirstLowHealth()
    {
        if (!_inCombat || !BoundaryAvailable || _reachedLowHealth || HpRatio(_stats.CurrentHp, _stats.MaxHp) > .40f) return;
        foreach (var source in new List<BoundarySource>(_boundarySources.Values))
            if (source.Active && source.Effect.trigger == BoundaryItemTrigger.FirstLowHealth && !_firstLowHealth.Contains(source.Key)
                && SelfCondition(source.Effect))
            { _firstLowHealth.Add(source.Key); Activate(source); }
        _reachedLowHealth = true;
    }

    /// <summary>Call once from the confirmed death reward, with HP captured before any kill healing.</summary>
    public int ProcessKill(ItemKillContext context)
    {
        if (!BoundaryAvailable || !context.CanTrigger || !_processedKills.Add((context.TargetId, context.CombatEpoch))) return 0;
        bool firstRewardKill = context.WasInCombat && context.BaseGold > 0 && _rewardKillEpochs.Add(context.CombatEpoch);
        double gold = 0d, fraction = 0d;
        foreach (var source in new List<BoundarySource>(_boundarySources.Values))
        {
            var effect = source.Effect;
            if (!source.Active || effect.trigger != BoundaryItemTrigger.Kill || source.Cooldown > 0f
                || !FamilyMatches(effect.family, context.Family)
                || effect.eliteOrBossOnly && !context.EliteOrBoss
                || !Within(HpRatio(context.PlayerHpBefore, context.PlayerMaxHpBefore), effect.selfHpMin, effect.selfHpMax)
                || effect.requiresFullHealth && context.PlayerHpBefore < context.PlayerMaxHpBefore - 0.0001f
                || context.BaseGold < effect.minimumBaseGold
                || effect.maximumBaseGold > 0 && context.BaseGold > effect.maximumBaseGold
                || effect.firstRewardKill && !firstRewardKill) continue;
            bool goldEffect = effect.stat == BoundaryItemStat.GoldFlat || effect.stat == BoundaryItemStat.GoldFraction;
            if (goldEffect && context.BaseGold <= 0) continue;
            if (effect.stat == BoundaryItemStat.GoldFlat) gold += effect.amount;
            else if (effect.stat == BoundaryItemStat.GoldFraction) fraction += Math.Round(effect.amount, 6);
            else Activate(source);
        }
        _goldFraction = Math.Round(_goldFraction + Math.Max(0, context.BaseGold) * fraction, 9);
        // Float-authored percentages such as 0.06 must pay exactly at an integer boundary.
        double whole = Math.Floor(_goldFraction + 0.000001d);
        _goldFraction = Math.Max(0d, _goldFraction - whole);
        return (int)Math.Min(int.MaxValue, Math.Max(0d, Math.Floor(gold + whole + 0.000001d)));
    }

    private void Activate(BoundarySource source)
    {
        if (source.Cooldown > 0f || !BoundaryAvailable) return;
        var effect = source.Effect;
        if (effect.stat == BoundaryItemStat.ShieldMaxHpRatio)
            _stats.GrantShield(source.ShieldSource, _stats.MaxHp * effect.amount, effect.durationSeconds);
        else if (effect.stat == BoundaryItemStat.HealMaxHpRatio) _stats.Heal(_stats.MaxHp * effect.amount);
        else if (effect.stat == BoundaryItemStat.IncomingDamageReductionRatio)
            source.Remaining = effect.durationSeconds;
        source.Cooldown = effect.cooldownSeconds;
    }

    private void TickBoundary(float deltaTime)
    {
        foreach (var source in _boundarySources.Values)
        {
            source.Cooldown = Mathf.Max(0f, source.Cooldown - deltaTime);
            source.Remaining = Mathf.Max(0f, source.Remaining - deltaTime);
        }
        CheckFirstLowHealth();
    }

    private void ClearBoundaryTransient()
    {
        foreach (var source in _boundarySources.Values)
        { source.Remaining = 0f; _stats?.RemoveShield(source.ShieldSource); }
        _inCombat = false;
    }

    private void ResetBoundaryForCatalogChange()
    {
        ClearBoundaryTransient();
        _boundarySources.Clear(); _synergyStates.Clear();
        _normalTargetsHit.Clear(); _directTargetsHit.Clear(); _skillCastsHit.Clear();
        _firstLowHealth.Clear(); _rewardKillEpochs.Clear(); _processedKills.Clear();
        _receivedEnemyAttack = false; _reachedLowHealth = false; _combatEpoch = 0; _goldFraction = 0d;
    }
}
