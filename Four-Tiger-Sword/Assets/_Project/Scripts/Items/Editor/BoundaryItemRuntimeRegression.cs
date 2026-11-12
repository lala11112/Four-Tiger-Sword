using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Behavior checks against every reviewed item mapping, without touching scene or saved assets.</summary>
public static class BoundaryItemRuntimeRegression
{
    private static int _checks;

    [MenuItem("Tools/BoundaryZERO/Inventory/Run Runtime Regression")]
    public static string Run()
    {
        _checks = 0;
        foreach (string id in BoundaryInventoryContent.Grades.Keys) VerifyMappedItem(id);
        VerifyConditionalCommit();
        VerifyAllDamagePassives();
        VerifySkillCooldowns();
        VerifySynergyTiers();
        VerifyShieldAndIncomingOrdering();
        VerifyDamageThresholdBoundaries();
        VerifyLowHealthDirectDamage();
        VerifyGoldAndCombatLifetime();
        VerifyFirstRewardRequiresCapturedCombat();
        VerifyReacquisitionAndMidCombatAcquisition();
        string result = $"Boundary inventory runtime: all {BoundaryInventoryContent.Grades.Count} item mappings; {_checks} checks passed.";
        Debug.Log(result);
        return result;
    }

    private static void VerifyMappedItem(string id)
    {
        using var fixture = new Fixture();
        var item = fixture.Definition(id, BoundaryInventoryContent.Effects(id));
        fixture.Catalog.items = new[] { item };
        Check(fixture.Items.TryAddItem(id), id + " grant");
        fixture.Items.SetCombatState(true);
        foreach (var effect in item.boundaryEffects)
        {
            Check(effect.IsValid(), id + " valid typed effect");
            var family = effect.family == BoundaryAttackFamily.Any || effect.family == BoundaryAttackFamily.NormalOrSkill
                ? BoundaryAttackFamily.Normal : effect.family;
            var element = effect.element == ElementType.ELEMENT_NONE ? ElementType.ELEMENT_FIRE : effect.element;
            float targetRatio = MatchingRatio(effect.targetHpMin, effect.targetHpMax);
            var hit = new ItemHitContext(1, targetRatio * 1000f, 1000f, true, family, element, true, 1);
            float selfRatio = MatchingRatio(effect.selfHpMin, effect.selfHpMax);
            if (effect.selfHpMin >= 0 || effect.selfHpMax >= 0)
                fixture.Stats.ConsumeHealth(fixture.Stats.CurrentHp - fixture.Stats.MaxHp * selfRatio);
            switch (effect.stat)
            {
                case BoundaryItemStat.MaxHpRatio:
                    Near(fixture.Stats.MaxHp, 1000f * (1f + effect.amount), id + " maxHP");
                    Near(fixture.Stats.CurrentHp, 1000f, id + " maxHP does not heal");
                    break;
                case BoundaryItemStat.AttackRatio:
                    Near(fixture.Stats.GetStat(StatType.ST_ATK), 100f * (1f + effect.amount), id + " attack"); break;
                case BoundaryItemStat.DefenseRatio:
                    Near(fixture.Stats.GetStat(StatType.ST_DEF), 100f * (1f + effect.amount), id + " defense"); break;
                case BoundaryItemStat.NormalAttackRateRatio:
                    Near(fixture.Items.NormalAttackRateMultiplier, 1f + effect.amount, id + " normal rate");
                    Near(fixture.Stats.GetStat(StatType.ST_ATK_SPD), 1f, id + " global rate unchanged"); break;
                case BoundaryItemStat.CriticalChancePoints:
                    if (effect.family == BoundaryAttackFamily.Any)
                        Near(fixture.Stats.GetStat(StatType.ST_CRT), 10f + effect.amount, id + " permanent crit");
                    else
                    {
                        Near(fixture.Stats.GetStat(StatType.ST_CRT), 10f, id + " conditional crit stays out of stats");
                        Near(fixture.Items.EvaluateOutgoing(hit).CriticalChanceBonus, effect.amount / 100f, id + " same-hit crit");
                    }
                    break;
                case BoundaryItemStat.CriticalDamagePoints:
                    if (effect.family == BoundaryAttackFamily.Any)
                        Near(fixture.Stats.GetStat(StatType.ST_CRTD), 150f + effect.amount, id + " permanent crit damage");
                    else Near(fixture.Items.EvaluateOutgoing(hit).CriticalMultiplierBonus, effect.amount / 100f, id + " same-hit crit damage");
                    break;
                case BoundaryItemStat.DamageRatio:
                    Near(fixture.Items.EvaluateOutgoing(hit).DamageMultiplier, 1f + effect.amount, id + " matching damage");
                    CheckDamageMismatch(fixture, effect, hit, id);
                    break;
                case BoundaryItemStat.IgnoreDefenseRatio:
                    Near(fixture.Items.EvaluateOutgoing(hit).IgnoreDefenseRatio, effect.amount, id + " matching penetration");
                    break;
                case BoundaryItemStat.ShieldGainRatio:
                    fixture.Stats.GrantShield(new object(), 100f, 4f);
                    Near(fixture.Stats.Shield, 100f * (1f + effect.amount), id + " native shield amplification"); break;
                case BoundaryItemStat.ShieldMaxHpRatio:
                    TriggerShield(fixture, effect, hit);
                    Near(fixture.Stats.Shield, fixture.Stats.MaxHp * effect.amount, id + " trigger shield");
                    break;
                case BoundaryItemStat.IncomingDamageReductionRatio:
                    if (effect.trigger == BoundaryItemTrigger.DamageTaken)
                        fixture.Items.CommitIncoming(new ItemIncomingContext(true, true, 1000f, 1000f), 250f, 0f);
                    else if (effect.trigger == BoundaryItemTrigger.NormalHit)
                        fixture.Items.CommitOutgoing(hit, 10f, 0f);
                    Near(fixture.Items.EvaluateIncoming(new ItemIncomingContext(true, true,
                        fixture.Stats.MaxHp * selfRatio, fixture.Stats.MaxHp)), 1f - effect.amount, id + " incoming reduction");
                    Near(fixture.Items.EvaluateIncoming(new ItemIncomingContext(false, true, 100f, 1000f)), 1f, id + " ignores HP costs");
                    if (effect.durationSeconds > 0f)
                    {
                        fixture.Items.Tick(effect.durationSeconds + .01f);
                        Near(fixture.Items.EvaluateIncoming(new ItemIncomingContext(true, true, 1000f, 1000f)), 1f, id + " expires");
                    }
                    break;
                case BoundaryItemStat.HealMaxHpRatio:
                    fixture.Stats.ConsumeHealth(100f);
                    fixture.Items.ProcessKill(Kill(fixture, 2, family, 10));
                    Near(fixture.Stats.CurrentHp, 900f + 1000f * effect.amount, id + " kill healing"); break;
                case BoundaryItemStat.GoldFlat:
                    int baseGold = effect.maximumBaseGold > 0 ? effect.maximumBaseGold : 10;
                    Near(fixture.Items.ProcessKill(Kill(fixture, 2, family, baseGold)), effect.amount, id + " gold bonus");
                    Near(fixture.Items.ProcessKill(Kill(fixture, 2, family, baseGold)), 0f, id + " one kill once");
                    if (effect.firstRewardKill)
                        Near(fixture.Items.ProcessKill(Kill(fixture, 3, family, baseGold)), 0f, id + " first kill per combat");
                    if (effect.maximumBaseGold > 0)
                        Near(fixture.Items.ProcessKill(Kill(fixture, 4, family, effect.maximumBaseGold + 1)), 0f, id + " gold upper bound");
                    break;
                case BoundaryItemStat.GoldFraction:
                    Near(fixture.Items.ProcessKill(Kill(fixture, 2, family, 10)), 0f, id + " fractional first kill");
                    Near(fixture.Items.ProcessKill(Kill(fixture, 3, family, 10)), 1f, id + " fractional second kill");
                    Near((float)fixture.Items.GoldFractionRemainder, .6f, id + " fractional carry");
                    break;
            }
        }
        Check(fixture.Items.TryRemoveItem(id), id + " removal");
        Near(fixture.Stats.GetStat(StatType.ST_ATK), 100f, id + " removed attack source");
        Near(fixture.Items.NormalAttackRateMultiplier, 1f, id + " removed normal rate");
        Near(fixture.Stats.MaxHp, 1000f, id + " removed maxHP");
    }

    private static void TriggerShield(Fixture fixture, BoundaryItemEffect effect, ItemHitContext hit)
    {
        switch (effect.trigger)
        {
            case BoundaryItemTrigger.Kill: fixture.Items.ProcessKill(Kill(fixture, 2, hit.Family, 10)); break;
            case BoundaryItemTrigger.DamageTaken:
                fixture.Items.CommitIncoming(new ItemIncomingContext(true, true, 1000f, 1000f), 200f, 0f); break;
            case BoundaryItemTrigger.SkillHit:
                fixture.Items.CommitOutgoing(new ItemHitContext(1, 500f, 1000f, true,
                    BoundaryAttackFamily.Skill, hit.Element, true, 1), 10f, 0f); break;
            case BoundaryItemTrigger.FirstLowHealth: fixture.Items.Tick(.01f); break;
        }
    }

    private static void CheckDamageMismatch(Fixture fixture, BoundaryItemEffect effect, ItemHitContext matching, string id)
    {
        if (effect.family != BoundaryAttackFamily.Any)
        {
            var opposite = effect.family == BoundaryAttackFamily.NormalOrSkill ? BoundaryAttackFamily.Any
                : effect.family == BoundaryAttackFamily.Normal ? BoundaryAttackFamily.Skill : BoundaryAttackFamily.Normal;
            Near(fixture.Items.EvaluateOutgoing(new ItemHitContext(1, matching.TargetHp, 1000f, true,
                opposite, matching.Element, true, 1)).DamageMultiplier, 1f, id + " attack family mismatch");
        }
        if (effect.eliteOrBossOnly)
            Near(fixture.Items.EvaluateOutgoing(new ItemHitContext(1, matching.TargetHp, 1000f, false,
                matching.Family, matching.Element, true, 1)).DamageMultiplier, 1f, id + " ordinary target mismatch");
        if (effect.targetHpMin >= 0 || effect.targetHpMax >= 0)
        {
            float mismatch = effect.targetHpMin >= 0f ? Mathf.Max(0f, effect.targetHpMin - .01f) : effect.targetHpMax + .01f;
            Near(fixture.Items.EvaluateOutgoing(new ItemHitContext(1, mismatch * 1000f, 1000f, true,
                matching.Family, matching.Element, true, 1)).DamageMultiplier, 1f, id + " HP threshold mismatch");
        }
    }

    private static void VerifyConditionalCommit()
    {
        using var fixture = new Fixture();
        fixture.Own("I46", "I66", "I71");
        fixture.Items.SetCombatState(true);
        var normal = new ItemHitContext(1, 1000f, 1000f, true, BoundaryAttackFamily.Normal, ElementType.ELEMENT_FIRE, true, 1);
        Near(fixture.Items.EvaluateOutgoing(normal).DamageMultiplier, 1.28f, "first normal + elite direct");
        fixture.Items.CommitOutgoing(normal, 0f, 0f);
        Near(fixture.Items.EvaluateOutgoing(normal).DamageMultiplier, 1.28f, "rejected hit does not consume first flags");
        fixture.Items.CommitOutgoing(normal, 10f, 0f);
        Near(fixture.Items.EvaluateOutgoing(normal).DamageMultiplier, 1f, "positive hit commits first flags");
        var skill = new ItemHitContext(2, 1000f, 1000f, false, BoundaryAttackFamily.Skill, ElementType.ELEMENT_FIRE, true, 7);
        Near(fixture.Items.EvaluateOutgoing(skill).DamageMultiplier, 1.07f, "first skill hit");
        fixture.Items.CommitOutgoing(skill, 0f, 10f);
        Near(fixture.Items.EvaluateOutgoing(skill).DamageMultiplier, 1f, "shield hit consumes first skill cast");
        Near(fixture.Items.EvaluateOutgoing(new ItemHitContext(3, 1000f, 1000f, false,
            BoundaryAttackFamily.Skill, ElementType.ELEMENT_FIRE, true, 7)).DamageMultiplier, 1f, "AoE second target same cast");
        fixture.Items.SetCombatState(false); fixture.Items.SetCombatState(true);
        Near(fixture.Items.EvaluateOutgoing(normal).DamageMultiplier, 1.28f, "new combat resets per-target flags");
    }

    private static void VerifyAllDamagePassives()
    {
        using var fixture = new Fixture();
        var player = fixture.AttachPlayer();
        var damageItem = fixture.Definition("I46", BoundaryInventoryContent.RevisedItemEffects("I46"));
        var eliteItem = fixture.Definition("I65", BoundaryInventoryContent.RevisedItemEffects("I65"));
        var skillItem = fixture.Definition("I72", BoundaryInventoryContent.RevisedItemEffects("I72"));
        var shieldItem = fixture.Definition("I34", BoundaryInventoryContent.RevisedItemEffects("I34"));
        var killItem = fixture.Definition("legacy-kill-heal", BoundaryInventoryContent.Effects("I68"));
        fixture.Catalog.items = new[] { damageItem, eliteItem, skillItem, shieldItem, killItem };
        var members = new[] { damageItem.itemId, eliteItem.itemId };
        fixture.Catalog.synergies = new[] {
            Synergy("S14", "ignoreDefensePct", 6),
            Synergy("S15", "damagePct", 4)
        };
        foreach (var item in fixture.Catalog.items) Check(fixture.Items.TryAddItem(item.itemId), item.itemId + " grant");
        fixture.Items.SetCombatState(true);
        var target = fixture.AddDefendedTarget();
        var probe = new DamageProbe();
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var rank = typeof(Enemy).GetField("enemyRank", flags);
        var random = UnityEngine.Random.state;
        var activeSettings = typeof(CombatSettings).GetProperty(nameof(CombatSettings.Active));
        var previousSettings = CombatSettings.Active;
        try
        {
            activeSettings.SetValue(null, fixture.NeutralCombatSettings());
            foreach (var enemyRank in new[] { EnemyRank.Normal, EnemyRank.Elite, EnemyRank.Boss })
            {
                rank.SetValue(target, enemyRank);
                foreach (var category in new[] { PlayerAttackCategory.Normal, PlayerAttackCategory.Other, PlayerAttackCategory.Secondary })
                {
                    float expected = (enemyRank == EnemyRank.Normal ? 110f : 120f) / 1.86f;
                    Near(DealDamage(category), expected, enemyRank + " " + category + " passives applied once");
                    Check(probe.Context.CanTrigger == (category == PlayerAttackCategory.Normal),
                        category + " keeps its original trigger eligibility");
                }
            }

            Near(fixture.Stats.Shield, 0f, "secondary damage cannot grant a skill-hit shield");
            Near(DealDamage(PlayerAttackCategory.Skill), 125f / 1.86f, "skill-specific damage stacks on an authored skill");
            Near(fixture.Stats.Shield, 30f, "authored skill grants its shield");
            DealDamage(PlayerAttackCategory.Secondary);
            fixture.Stats.ConsumeHealth(100f);
            var death = probe.Context;
            fixture.Items.ProcessKill(new ItemKillContext(1, true, death.Family, death.CanTrigger,
                0, 900f, 1000f, death.CombatEpoch, death.WasInCombat));
            Near(fixture.Stats.CurrentHp, 900f, "additional damage cannot grant kill healing");
            fixture.Items.ProcessKill(Kill(fixture, 1, BoundaryAttackFamily.Skill, 0));
            Near(fixture.Stats.CurrentHp, 905f, "additional damage cannot consume a direct kill receipt");
        }
        finally
        {
            activeSettings.SetValue(null, previousSettings);
            UnityEngine.Random.state = random;
        }

        ItemSynergyDefinition Synergy(string id, string key, int amount) => new ItemSynergyDefinition {
            id = id, members = members, tiers = new[] { new ItemSynergyTier { count = 2,
                effects = BoundaryInventoryContent.SynergyEffects(id, 2,
                    new Newtonsoft.Json.Linq.JObject { [key] = amount }, revised: true) } }
        };

        float DealDamage(PlayerAttackCategory category)
            => DamageManager.Apply(new HitInfo(100f, ElementType.ELEMENT_NONE, 0f, 1f, source: player,
                category: category, castId: 7), probe, target.gameObject).HealthDamage;
    }

    private sealed class DamageProbe : IDamageable
    {
        public ItemDeathContext Context;
        public DamageResult TakeDamage(float damage, ElementType damageType, bool isCritical, Vector3 power,
            float poiseDamage, StaggerResistLevel staggerResistLevel, bool isParryable, object source)
        {
            Context = ItemDamageContext.Current;
            return new DamageResult(DamageOutcome.Applied, damage);
        }
    }

    private static void VerifySkillCooldowns()
    {
        using var fixture = new Fixture();
        var player = fixture.AttachPlayer();
        var form = new CooldownProbeForm();
        form.Equip(player);
        fixture.Catalog.items = new[] {
            fixture.Definition("I71", BoundaryInventoryContent.RevisedItemEffects("I71")),
            fixture.Definition("I120", BoundaryInventoryContent.RevisedItemEffects("I120")),
            fixture.Definition("cap", new BoundaryItemEffect {
                stat = BoundaryItemStat.SkillCooldownReductionRatio, amount = .45f })
        };
        Check(fixture.Items.TryAddItem("I71") && fixture.Items.TryAddItem("I120"), "cooldown items acquired");
        Near(fixture.Items.SkillCooldownReductionRatio, .08f, "cooldown reductions add");
        form.EndSkill();
        form.StartUltimateCooldown();
        Near(form.SkillCooldownRemaining, 4.6f, "E timer starts at the effective duration");
        Near(form.UltimateCooldownRemaining, 27.6f, "R timer starts at the effective duration");
        Near(form.SkillCooldownDuration, 4.6f, "E display duration uses reduction");
        Near(form.UltimateCooldownDuration, 27.6f, "R display duration uses reduction");
        var advance = typeof(BaseForm).GetMethod("AdvanceSkillCooldowns",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        advance.Invoke(form, new object[] { .92f });
        Near(form.SkillCooldownRemaining, 3.68f, "E timer tracks real elapsed seconds");
        Near(form.UltimateCooldownRemaining, 26.68f, "R timer tracks real elapsed seconds");
        Check(fixture.Items.TryAddItem("cap"), "cap fixture item acquired");
        Near(fixture.Items.SkillCooldownReductionRatio, .40f, "cooldown reduction caps at 40 percent");
        Near(form.SkillCooldownRemaining, 2.4f, "acquisition scales remaining E work");
        Near(form.UltimateCooldownRemaining, 17.4f, "acquisition scales remaining R work");
        Check(fixture.Items.TryRemoveItem("cap") && fixture.Items.TryRemoveItem("I71")
            && fixture.Items.TryRemoveItem("I120"), "cooldown items removed");
        Near(form.SkillCooldownRemaining, 4f, "removal restores E remaining base seconds");
        Near(form.UltimateCooldownRemaining, 29f, "removal restores R remaining base seconds");
        advance.Invoke(form, new object[] { 60f });
        Near(form.SkillCooldownRemaining, 0f, "E cooldown clamps at zero");
        Near(form.UltimateCooldownRemaining, 0f, "R cooldown clamps at zero");
    }

    private sealed class CooldownProbeForm : BaseForm
    {
        public CooldownProbeForm() : base(null) { }
        public void StartUltimateCooldown() => ConsumeUltimateResources();
    }

    private static void VerifySynergyTiers()
    {
        using var fixture = new Fixture();
        var definitions = new List<ItemDefinition>();
        for (int i = 0; i < 9; i++) definitions.Add(fixture.Definition("member" + i));
        fixture.Catalog.items = definitions.ToArray();
        fixture.Catalog.synergies = new[] { new ItemSynergyDefinition { id = "test",
            members = definitions.ConvertAll(item => item.itemId).ToArray(), tiers = new[] {
                Tier(2, .05f), Tier(4, .10f), Tier(6, .18f), Tier(8, .26f) } } };
        var activations = new List<int>();
        fixture.Items.SynergyTierActivated += state => activations.Add(state.ActiveTier.count);
        var incoming = new ItemIncomingContext(true, false, 1000f, 1000f);
        for (int i = 0; i < definitions.Count; i++)
        {
            Check(fixture.Items.TryAddItem(definitions[i].itemId), "synergy member grant " + i);
            float ratio = i + 1 >= 8 ? .26f : i + 1 >= 6 ? .18f : i + 1 >= 4 ? .10f : i + 1 >= 2 ? .05f : 0f;
            Near(fixture.Stats.GetStat(StatType.ST_ATK), 100f * (1f + ratio), "highest tier replacement " + (i + 1));
            Check(fixture.Items.SynergyStates[0].OwnedCount == i + 1, "synergy unique count " + i);
            if (i == 5)
            {
                fixture.Items.CommitIncoming(incoming, 10f, 0f);
                Near(fixture.Stats.Shield, 50f, "six-piece shield amount");
            }
        }
        Check(string.Join(",", activations) == "2,4,6,8", "each newly reached tier notified once");
        fixture.Items.CommitIncoming(incoming, 10f, 0f);
        Near(fixture.Stats.Shield, 50f, "eight-piece promotion preserves the existing shield cooldown");
        fixture.Items.Tick(10f);
        fixture.Items.CommitIncoming(incoming, 10f, 0f);
        Near(fixture.Stats.Shield, 60f, "eight-piece shield amount after cooldown");
        Check(fixture.Items.TryRemoveItem("member8") && fixture.Items.TryRemoveItem("member7")
            && fixture.Items.SynergyStates[0].ActiveTier.count == 6, "losing the eighth piece restores the six-piece tier");
        Near(fixture.Stats.GetStat(StatType.ST_ATK), 118f, "eight-piece bonus is replaced on downgrade");
        fixture.Items.TryRemoveItem("member6"); fixture.Items.TryRemoveItem("member5");
        Near(fixture.Stats.GetStat(StatType.ST_ATK), 110f, "tier downgrade");
        static ItemSynergyTier Tier(int count, float amount) => new ItemSynergyTier { count = count,
            effects = new[] {
                new BoundaryItemEffect { stat = BoundaryItemStat.AttackRatio, amount = amount },
                new BoundaryItemEffect { stat = BoundaryItemStat.ShieldMaxHpRatio, amount = .02f + .005f * count,
                    trigger = BoundaryItemTrigger.DamageTaken, durationSeconds = 4, cooldownSeconds = 10 }
            } };
    }

    private static void VerifyShieldAndIncomingOrdering()
    {
        using var fixture = new Fixture();
        fixture.Own("I31", "I33", "I77", "I78", "I81");
        fixture.Items.SetCombatState(true);
        var incoming = new ItemIncomingContext(true, false, 1000f, 1000f);
        Near(fixture.Items.EvaluateIncoming(incoming), .88f, "first attack reduction before damage");
        fixture.Items.CommitIncoming(incoming, 0f, 200f);
        Near(fixture.Stats.Shield, 0f, "shield-only loss does not trigger HP-loss effects");
        Near(fixture.Items.EvaluateIncoming(incoming), 1f, "first incoming consumed even on shield loss");
        fixture.Items.CommitIncoming(incoming, 200f, 0f);
        Near(fixture.Stats.Shield, 44f, "I31 amplified shield after actualHP loss");
        Near(fixture.Items.EvaluateIncoming(incoming), .92f, "I78 begins after triggering hit");
        fixture.Items.CommitIncoming(incoming, 200f, 0f);
        Near(fixture.Stats.Shield, 44f, "cooldown prevents stacking shield");
        fixture.Items.CommitOutgoing(new ItemHitContext(1, 1000f, 1000f, false,
            BoundaryAttackFamily.Normal, ElementType.ELEMENT_FIRE, false, 1), 10f, 0f);
        Near(fixture.Items.EvaluateIncoming(incoming), .92f, "additional damage cannot trigger normal hit proc");
        fixture.Items.CommitOutgoing(new ItemHitContext(1, 1000f, 1000f, false,
            BoundaryAttackFamily.Normal, ElementType.ELEMENT_FIRE, true, 1), 10f, 0f);
        Near(fixture.Items.EvaluateIncoming(incoming), .87f, "independent reduction sources add");
        fixture.Items.TryRemoveItem("I31");
        Near(fixture.Stats.Shield, 0f, "item removal removes only owned shield source");
    }

    private static void VerifyGoldAndCombatLifetime()
    {
        using var fixture = new Fixture();
        fixture.Own("I89", "I90", "I91", "I92", "I93", "I94", "I68");
        fixture.Catalog.synergies = new[] { new ItemSynergyDefinition { id = "S19", members = new[] { "I89", "I90" },
            tiers = new[] { new ItemSynergyTier { count = 2, effects = new[] { new BoundaryItemEffect {
                stat = BoundaryItemStat.GoldFraction, amount = .06f, trigger = BoundaryItemTrigger.Kill, minimumBaseGold = 1 } } } } } };
        fixture.Items.RecalculateForCurrentForm();
        fixture.Items.SetCombatState(true);
        var finalKill = Kill(fixture, 100, BoundaryAttackFamily.Normal, 10);
        fixture.Items.SetCombatState(false); // Last enemy's death observer may clear combat first.
        int first = fixture.Items.ProcessKill(finalKill);
        Near(first, 11f, "gold item sum before fractional payout, after combat close");
        int second = fixture.Items.ProcessKill(Kill(fixture, 101, BoundaryAttackFamily.Skill, 10));
        Near(second, 9f, "first kill one-use and fractional carry");
        Near((float)fixture.Items.GoldFractionRemainder, .2f, "fraction excludes bonus gold");
        var additional = new ItemKillContext(102, true, BoundaryAttackFamily.Any, false, 10, 1000, 1000, fixture.Items.CombatEpoch);
        Near(fixture.Items.ProcessKill(additional), 0f, "additional kills do not trigger bonuses");
        Near((float)fixture.Items.GoldFractionRemainder, .2f, "additional kill no fractional proc");
        fixture.Stats.ConsumeHealth(5f);
        var injured = new ItemKillContext(103, true, BoundaryAttackFamily.Normal, true, 10, 995f, 1000f, fixture.Items.CombatEpoch);
        fixture.Items.Tick(20f);
        Near(fixture.Items.ProcessKill(injured), 6f, "full HP tests pre-kill heal snapshot");
        Near(fixture.Stats.CurrentHp, 1000f, "kill heal after gold predicate");
    }

    private static void VerifyFirstRewardRequiresCapturedCombat()
    {
        using var fixture = new Fixture();
        fixture.Own("I91");
        Near(fixture.Items.ProcessKill(Kill(fixture, 700, BoundaryAttackFamily.Normal, 10)), 0f,
            "I91 does not activate before the first combat");
        fixture.Items.SetCombatState(true);
        var finalKill = Kill(fixture, 701, BoundaryAttackFamily.Normal, 10);
        fixture.Items.SetCombatState(false);
        Near(fixture.Items.ProcessKill(finalKill), 3f, "I91 retains captured combat through the final death callback");
        Near(fixture.Items.ProcessKill(Kill(fixture, 702, BoundaryAttackFamily.Normal, 10)), 0f,
            "I91 does not activate on a new out-of-combat kill");
        fixture.Items.SetCombatState(true);
        fixture.Items.SetCombatState(false);
        Near(fixture.Items.ProcessKill(Kill(fixture, 703, BoundaryAttackFamily.Normal, 10)), 0f,
            "An empty completed combat does not lend its first reward to an outside kill");
        fixture.Items.SetCombatState(true);
        Near(fixture.Items.ProcessKill(Kill(fixture, 704, BoundaryAttackFamily.Normal, 10)), 3f,
            "Outside kills do not consume the next combat's first reward");
        Near(fixture.Items.ProcessKill(Kill(fixture, 705, BoundaryAttackFamily.Normal, 10)), 0f,
            "An active combat's first reward is still one-use");
    }

    private static void VerifyDamageThresholdBoundaries()
    {
        using var fixture = new Fixture();
        fixture.Own("I78");
        fixture.Items.SetCombatState(true);
        var incoming = new ItemIncomingContext(true, false, 1000f, 1000f);
        fixture.Items.CommitIncoming(incoming, 199.9f, 0f);
        Near(fixture.Items.EvaluateIncoming(incoming), 1f, "I78 below exact 20 percent does not proc");
        fixture.Items.CommitIncoming(incoming, 200f, 0f);
        Near(fixture.Items.EvaluateIncoming(incoming), .92f, "I78 exact 20 percent is inclusive");
    }

    private static void VerifyLowHealthDirectDamage()
    {
        using var fixture = new Fixture();
        fixture.Own("I75");

        foreach (var family in new[] { BoundaryAttackFamily.Normal, BoundaryAttackFamily.Skill })
        {
            Near(DamageMultiplier(family, 249f), 1.09f, $"I75 boosts {family} below 25 percent target HP");
            Near(DamageMultiplier(family, 250f), 1.09f, $"I75 boosts {family} at exactly 25 percent target HP");
            Near(DamageMultiplier(family, 251f), 1f, $"I75 leaves {family} above 25 percent target HP unchanged");
        }
        Near(DamageMultiplier(BoundaryAttackFamily.Any, 249f, false), 1f, "I75 excludes secondary damage");

        float DamageMultiplier(BoundaryAttackFamily family, float targetHp, bool canTrigger = true)
            => fixture.Items.EvaluateOutgoing(new ItemHitContext(1, targetHp, 1000f, false,
                family, ElementType.ELEMENT_FIRE, canTrigger, 1)).DamageMultiplier;
    }

    private static void VerifyReacquisitionAndMidCombatAcquisition()
    {
        using var fixture = new Fixture();
        var definitions = new List<ItemDefinition>();
        foreach (string id in new[] { "I30", "I46", "I66", "I71", "I77", "I91" })
            definitions.Add(fixture.Definition(id, BoundaryInventoryContent.Effects(id)));
        fixture.Catalog.items = definitions.ToArray();
        fixture.Items.SetCombatState(true);
        var normal = new ItemHitContext(500, 1000f, 1000f, true, BoundaryAttackFamily.Normal, ElementType.ELEMENT_FIRE, true, 1);
        var skill = new ItemHitContext(501, 1000f, 1000f, true, BoundaryAttackFamily.Skill, ElementType.ELEMENT_FIRE, true, 2);
        fixture.Items.CommitOutgoing(normal, 10f, 0f);
        fixture.Items.CommitOutgoing(skill, 10f, 0f);
        fixture.Items.CommitIncoming(new ItemIncomingContext(true, false, 1000f, 1000f), 10f, 0f);
        fixture.Items.ProcessKill(Kill(fixture, 600, BoundaryAttackFamily.Normal, 10));
        foreach (string id in new[] { "I46", "I66", "I71", "I77", "I91" }) fixture.Items.TryAddItem(id);
        Near(fixture.Items.EvaluateOutgoing(normal).DamageMultiplier, 1f, "late acquisition is not first target hit");
        Near(fixture.Items.EvaluateOutgoing(skill).DamageMultiplier, 1f, "late acquisition is not first skill cast hit");
        Near(fixture.Items.EvaluateIncoming(new ItemIncomingContext(true, false, 1000f, 1000f)), 1f, "late acquisition is not first incoming");
        Near(fixture.Items.ProcessKill(Kill(fixture, 601, BoundaryAttackFamily.Normal, 10)), 0f, "late acquisition is not first reward kill");
        fixture.Items.TryAddItem("I30");
        fixture.Items.ProcessKill(Kill(fixture, 602, BoundaryAttackFamily.Normal, 10));
        Near(fixture.Stats.Shield, 30f, "kill shield initial cooldown");
        fixture.Items.TryRemoveItem("I30"); fixture.Items.TryAddItem("I30");
        fixture.Items.ProcessKill(Kill(fixture, 603, BoundaryAttackFamily.Normal, 10));
        Near(fixture.Stats.Shield, 0f, "sell and reacquire cannot reset cooldown");
        fixture.Items.Tick(5f);
        fixture.Items.ProcessKill(Kill(fixture, 604, BoundaryAttackFamily.Normal, 10));
        Near(fixture.Stats.Shield, 30f, "reacquired source cooldown expires");
        fixture.Items.SetCombatState(false); fixture.Items.SetCombatState(true);
        Near(fixture.Items.EvaluateOutgoing(skill).DamageMultiplier, 1.18f,
            "same cast cannot regain I71 across combat while per-target I66 resets");
    }

    private static ItemKillContext Kill(Fixture fixture, int targetId, BoundaryAttackFamily family, int baseGold)
        => new ItemKillContext(targetId, true, family, true, baseGold, fixture.Stats.CurrentHp,
            fixture.Stats.MaxHp, fixture.Items.CombatEpoch, fixture.Items.IsInCombat);
    private static float MatchingRatio(float minimum, float maximum)
        => maximum >= 0f ? maximum : minimum >= 0f ? minimum : 1f;
    private static void Near(float actual, float expected, string label)
        => Check(!float.IsNaN(actual) && Mathf.Abs(actual - expected) < .001f, label + ": " + actual + " != " + expected);
    private static void Check(bool pass, string label)
    { _checks++; if (!pass) throw new InvalidOperationException("Boundary item regression: " + label); }

    private sealed class Fixture : IDisposable
    {
        private readonly List<Object> _objects = new();
        private readonly GameObject _root;
        public readonly PlayerStatManager Stats;
        public readonly PlayerItemRuntime Items;
        public readonly ItemCatalog Catalog;
        public Fixture()
        {
            _root = new GameObject("Boundary item regression") { hideFlags = HideFlags.HideAndDontSave };
            _root.SetActive(false);
            var data = ScriptableObject.CreateInstance<PlayerStatData>(); _objects.Add(data);
            data.baseHP = 1000; data.baseATK = 100; data.baseDEF = 100; data.baseCRT = 10; data.baseCRTD = 150;
            data.baseAtkSpd = 1;
            Stats = _root.AddComponent<PlayerStatManager>(); Stats.Initialize(data);
            Catalog = ScriptableObject.CreateInstance<ItemCatalog>(); _objects.Add(Catalog); Catalog.projectId = "boundary-runtime-test";
            Items = _root.AddComponent<PlayerItemRuntime>(); Items.Catalog = Catalog;
            Items.Initialize(Stats, () => ElementType.ELEMENT_FIRE);
        }
        public PlayerController AttachPlayer()
        {
            var player = _root.AddComponent<PlayerController>();
            typeof(PlayerController).GetProperty(nameof(PlayerController.StatManager)).SetValue(player, Stats);
            typeof(PlayerController).GetProperty(nameof(PlayerController.ItemRuntime)).SetValue(player, Items);
            return player;
        }
        public Enemy AddDefendedTarget()
        {
            var go = new GameObject("Boundary item damage target") { hideFlags = HideFlags.HideAndDontSave };
            _objects.Add(go);
            go.SetActive(false);
            var enemy = go.AddComponent<Enemy>();
            enemy.enabled = false;
            var stats = go.GetComponent<EnemyStat>();
            var data = ScriptableObject.CreateInstance<EnemyData>();
            data.maxHp = 1000;
            data.baseDef = 100f;
            _objects.Add(data);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(EnemyStat).GetField("baseData", flags).SetValue(stats, data);
            typeof(EnemyStat).GetMethod("InitializeFromData", flags).Invoke(stats, null);
            go.SetActive(true);
            return enemy;
        }

        public CombatSettings NeutralCombatSettings()
        {
            var table = ScriptableObject.CreateInstance<ElementEffectiveManager>();
            _objects.Add(table);
            foreach (ElementType attack in Enum.GetValues(typeof(ElementType)))
                foreach (ElementType defense in Enum.GetValues(typeof(ElementType)))
                    table.elementEffectiveList.Add(new ElementEffective { AttackType = attack, DefenseType = defense, multiplier = 1f });
            table.Initialize();
            var settings = _root.AddComponent<CombatSettings>();
            typeof(CombatSettings).GetField("_elementTable",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(settings, table);
            return settings;
        }

        public ItemDefinition Definition(string id, params BoundaryItemEffect[] effects)
        {
            var definition = ScriptableObject.CreateInstance<ItemDefinition>(); _objects.Add(definition);
            definition.projectId = Catalog.projectId; definition.itemId = id; definition.candidateKey = "test:" + id;
            definition.boundaryEffects = effects; return definition;
        }
        public void Own(params string[] ids)
        {
            var definitions = new List<ItemDefinition>();
            foreach (string id in ids) definitions.Add(Definition(id, BoundaryInventoryContent.Effects(id)));
            Catalog.items = definitions.ToArray();
            foreach (string id in ids) Check(Items.TryAddItem(id), "fixture grant " + id);
        }
        public void Dispose()
        { Object.DestroyImmediate(_root); foreach (var value in _objects) Object.DestroyImmediate(value); }
    }
}
