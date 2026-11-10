using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Reviewed, typed translation of the 2026-11-08 handoff; never parses gameplay from prose.</summary>
internal static class BoundaryInventoryContent
{
    internal const string ProjectId = "boundaryzero-inventory-2026-11-08";
    internal const string LegacySourceRevision = "2026-11-08";
    internal const string ExpandedSourceRevision = "2026-11-10";
    internal const int LegacyRevision = 20261108;
    internal const int ExpandedRevision = 20261110;
    internal static readonly Dictionary<string, int> LegacyGrades = CreateGrades(false);
    internal static readonly Dictionary<string, int> Grades = CreateGrades(true);

    private static Dictionary<string, int> CreateGrades(bool expanded)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        Add(1, "I13 I41 I47 I51 I66 I68 I71 I75 I77 I89 I91 I93 I94");
        Add(2, "I01 I04 I06 I07 I08 I15 I17 I18 I19 I20 I21 I22 I31 I32 I42 I44 I46 I48 I57 I60 I63 I67 I69 I73 I78 I90");
        Add(3, "I02 I05 I16 I30 I33 I43 I50 I61 I62 I64 I65 I72 I92 I100");
        Add(4, "I09 I14 I34 I56 I81 I95");
        if (expanded)
        {
            Add(1, "I101 I103 I105 I113 I119 I121");
            Add(2, "I107 I109 I111 I115 I117");
            Add(3, "I102 I104 I106 I108 I110 I114 I120 I122");
            Add(4, "I112 I116 I118");
        }
        return result;
        void Add(int grade, string ids)
        {
            foreach (string id in ids.Split(' ')) result.Add(id, grade);
        }
    }

    internal static BoundaryItemEffect[] Effects(string id)
    {
        switch (id)
        {
            case "I01": return One(id, BoundaryItemStat.MaxHpRatio, .05f);
            case "I02": return One(id, BoundaryItemStat.CriticalChancePoints, 8, BoundaryAttackFamily.Normal, targetMax: .30f);
            case "I04": return One(id, BoundaryItemStat.DamageRatio, .04f, BoundaryAttackFamily.NormalOrSkill, ElementType.ELEMENT_FIRE);
            case "I05": return One(id, BoundaryItemStat.DamageRatio, .08f, BoundaryAttackFamily.Normal, ElementType.ELEMENT_FIRE);
            case "I06": return One(id, BoundaryItemStat.CriticalDamagePoints, 10);
            case "I07": return One(id, BoundaryItemStat.DamageRatio, .08f, element: ElementType.ELEMENT_FIRE, elite: true);
            case "I08": return One(id, BoundaryItemStat.DamageRatio, .07f, BoundaryAttackFamily.Skill, ElementType.ELEMENT_FIRE);
            case "I09": return One(id, BoundaryItemStat.MaxHpRatio, .08f);
            case "I100": return One(id, BoundaryItemStat.CriticalDamagePoints, 15, BoundaryAttackFamily.Normal, targetMax: .25f);
            case "I13": return One(id, BoundaryItemStat.CriticalChancePoints, 2);
            case "I14": return One(id, BoundaryItemStat.NormalAttackRateRatio, .05f);
            case "I15": return One(id, BoundaryItemStat.DamageRatio, .12f, BoundaryAttackFamily.Normal, targetMin: .80f);
            case "I16": return new[] {
                E(id + ":water", BoundaryItemStat.DamageRatio, .04f, element: ElementType.ELEMENT_WATER),
                E(id + ":fire", BoundaryItemStat.DamageRatio, .04f, element: ElementType.ELEMENT_FIRE) };
            case "I17": return One(id, BoundaryItemStat.DamageRatio, .08f, BoundaryAttackFamily.Normal, ElementType.ELEMENT_WATER);
            case "I18": return One(id, BoundaryItemStat.DamageRatio, .07f, BoundaryAttackFamily.Skill, ElementType.ELEMENT_WATER);
            case "I19": return One(id, BoundaryItemStat.DamageRatio, .10f, element: ElementType.ELEMENT_WATER, targetMax: .30f);
            case "I20": return One(id, BoundaryItemStat.DamageRatio, .05f, element: ElementType.ELEMENT_WATER);
            case "I21": return One(id, BoundaryItemStat.DamageRatio, .08f, element: ElementType.ELEMENT_WATER, elite: true);
            case "I22": return One(id, BoundaryItemStat.DamageRatio, .10f, element: ElementType.ELEMENT_FIRE, targetMin: .80f);
            case "I30": return One(id, BoundaryItemStat.ShieldMaxHpRatio, .03f, trigger: BoundaryItemTrigger.Kill, duration: 4, cooldown: 5);
            case "I31": return One(id, BoundaryItemStat.ShieldMaxHpRatio, .04f, trigger: BoundaryItemTrigger.DamageTaken, duration: 4, cooldown: 10, minimumLoss: .15f);
            case "I32": return One(id, BoundaryItemStat.ShieldMaxHpRatio, .06f, trigger: BoundaryItemTrigger.CombatEntered, duration: 5, cooldown: 15);
            case "I33": return One(id, BoundaryItemStat.ShieldGainRatio, .10f);
            case "I34": return One(id, BoundaryItemStat.ShieldMaxHpRatio, .03f, trigger: BoundaryItemTrigger.SkillHit, duration: 3, cooldown: 8);
            case "I41": return One(id, BoundaryItemStat.DamageRatio, .14f, BoundaryAttackFamily.Normal, targetMax: .20f);
            case "I42": return One(id, BoundaryItemStat.DamageRatio, .13f, BoundaryAttackFamily.Skill, targetMax: .30f);
            case "I43": return One(id, BoundaryItemStat.DamageRatio, .06f, BoundaryAttackFamily.NormalOrSkill, targetMax: .30f);
            case "I44": return One(id, BoundaryItemStat.DamageRatio, .12f, BoundaryAttackFamily.NormalOrSkill, elite: true, targetMax: .30f);
            case "I46": return One(id, BoundaryItemStat.DamageRatio, .10f, BoundaryAttackFamily.Normal, firstTarget: true);
            case "I47": return One(id, BoundaryItemStat.DamageRatio, .15f, BoundaryAttackFamily.Skill, targetMin: .90f);
            case "I48": return One(id, BoundaryItemStat.DamageRatio, .06f, BoundaryAttackFamily.Normal, selfMin: .80f);
            case "I50": return One(id, BoundaryItemStat.ShieldMaxHpRatio, .06f, trigger: BoundaryItemTrigger.FirstLowHealth, selfMax: .40f, duration: 5);
            case "I51": return One(id, BoundaryItemStat.IncomingDamageReductionRatio, .07f, selfMax: .30f);
            case "I56": return One(id, BoundaryItemStat.AttackRatio, .06f);
            case "I57": return One(id, BoundaryItemStat.CriticalChancePoints, 5, BoundaryAttackFamily.Normal, elite: true);
            case "I60": return One(id, BoundaryItemStat.IgnoreDefenseRatio, .20f, targetMin: .80f);
            case "I61": return One(id, BoundaryItemStat.IgnoreDefenseRatio, .12f, BoundaryAttackFamily.Normal);
            case "I62": return One(id, BoundaryItemStat.IgnoreDefenseRatio, .12f, BoundaryAttackFamily.Skill);
            case "I63": return One(id, BoundaryItemStat.IgnoreDefenseRatio, .18f, targetMax: .30f);
            case "I64": return One(id, BoundaryItemStat.IgnoreDefenseRatio, .07f, BoundaryAttackFamily.NormalOrSkill);
            case "I65": return One(id, BoundaryItemStat.IgnoreDefenseRatio, .14f, BoundaryAttackFamily.NormalOrSkill, elite: true);
            case "I66": return One(id, BoundaryItemStat.DamageRatio, .18f, BoundaryAttackFamily.NormalOrSkill, elite: true, firstTarget: true);
            case "I67": return One(id, BoundaryItemStat.DamageRatio, .11f, BoundaryAttackFamily.Normal, elite: true, targetMax: .50f);
            case "I68": return One(id, BoundaryItemStat.HealMaxHpRatio, .005f, trigger: BoundaryItemTrigger.Kill, elite: true, cooldown: 20);
            case "I69": return One(id, BoundaryItemStat.IncomingDamageReductionRatio, .06f, elite: true);
            case "I71": return One(id, BoundaryItemStat.DamageRatio, .07f, BoundaryAttackFamily.Skill, firstSkill: true);
            case "I72": return One(id, BoundaryItemStat.DamageRatio, .05f, BoundaryAttackFamily.Skill);
            case "I73": return One(id, BoundaryItemStat.DamageRatio, .08f, BoundaryAttackFamily.Skill, elite: true);
            case "I75": return One(id, BoundaryItemStat.DamageRatio, .09f, BoundaryAttackFamily.Skill, targetMax: .25f);
            case "I77": return One(id, BoundaryItemStat.IncomingDamageReductionRatio, .12f, firstIncoming: true);
            case "I78": return One(id, BoundaryItemStat.IncomingDamageReductionRatio, .08f, trigger: BoundaryItemTrigger.DamageTaken, duration: 3, minimumLoss: .20f);
            case "I81": return One(id, BoundaryItemStat.IncomingDamageReductionRatio, .05f, trigger: BoundaryItemTrigger.NormalHit, duration: 1.5f);
            case "I89": return One(id, BoundaryItemStat.GoldFlat, 1, trigger: BoundaryItemTrigger.Kill, minimumGold: 1, maximumGold: 10);
            case "I90": return One(id, BoundaryItemStat.GoldFlat, 2, trigger: BoundaryItemTrigger.Kill, fullHealth: true, minimumGold: 1);
            case "I91": return One(id, BoundaryItemStat.GoldFlat, 3, trigger: BoundaryItemTrigger.Kill, firstReward: true, minimumGold: 1);
            case "I92": return One(id, BoundaryItemStat.GoldFlat, 4, trigger: BoundaryItemTrigger.Kill, elite: true, minimumGold: 1);
            case "I93": return One(id, BoundaryItemStat.GoldFlat, 1, BoundaryAttackFamily.Skill, trigger: BoundaryItemTrigger.Kill, minimumGold: 1);
            case "I94": return One(id, BoundaryItemStat.GoldFlat, 1, BoundaryAttackFamily.Normal, trigger: BoundaryItemTrigger.Kill, minimumGold: 1);
            case "I95": return One(id, BoundaryItemStat.DamageRatio, .07f, BoundaryAttackFamily.Normal);
            case "I101": return One(id, BoundaryItemStat.CriticalChancePoints, 4, family: BoundaryAttackFamily.Skill);
            case "I102": return One(id, BoundaryItemStat.CriticalDamagePoints, 12);
            case "I103": return One(id, BoundaryItemStat.AttackRatio, .03f);
            case "I104": return One(id, BoundaryItemStat.NormalAttackRateRatio, .04f);
            case "I105": return One(id, BoundaryItemStat.DefenseRatio, .05f);
            case "I106": return new[] {
                E(id + ":hp", BoundaryItemStat.MaxHpRatio, .04f),
                E(id + ":defense", BoundaryItemStat.DefenseRatio, .04f) };
            case "I107": return One(id, BoundaryItemStat.DamageRatio, .06f, family: BoundaryAttackFamily.NormalOrSkill, element: ElementType.ELEMENT_WATER);
            case "I108": return One(id, BoundaryItemStat.DamageRatio, .11f, element: ElementType.ELEMENT_WATER, targetMin: .8f);
            case "I109": return One(id, BoundaryItemStat.DamageRatio, .05f, element: ElementType.ELEMENT_FIRE);
            case "I110": return One(id, BoundaryItemStat.DamageRatio, .11f, element: ElementType.ELEMENT_FIRE, targetMax: .3f);
            case "I111": return One(id, BoundaryItemStat.ShieldMaxHpRatio, .04f, trigger: BoundaryItemTrigger.CombatEntered, duration: 4, cooldown: 12);
            case "I112": return One(id, BoundaryItemStat.ShieldGainRatio, .14f);
            case "I113": return One(id, BoundaryItemStat.DamageRatio, .1f, family: BoundaryAttackFamily.Normal, targetMax: .25f);
            case "I114": return One(id, BoundaryItemStat.DamageRatio, .11f, family: BoundaryAttackFamily.NormalOrSkill, targetMax: .2f);
            case "I115": return One(id, BoundaryItemStat.IgnoreDefenseRatio, .08f, family: BoundaryAttackFamily.Normal);
            case "I116": return One(id, BoundaryItemStat.IgnoreDefenseRatio, .18f, family: BoundaryAttackFamily.NormalOrSkill, targetMax: .4f);
            case "I117": return One(id, BoundaryItemStat.DamageRatio, .06f, family: BoundaryAttackFamily.NormalOrSkill, elite: true);
            case "I118": return One(id, BoundaryItemStat.IncomingDamageReductionRatio, .08f, elite: true);
            case "I119": return One(id, BoundaryItemStat.DamageRatio, .03f, family: BoundaryAttackFamily.Skill);
            case "I120": return One(id, BoundaryItemStat.DamageRatio, .09f, family: BoundaryAttackFamily.Skill, targetMin: .7f);
            case "I121": return One(id, BoundaryItemStat.GoldFraction, .08f, trigger: BoundaryItemTrigger.Kill, minimumGold: 1);
            case "I122": return One(id, BoundaryItemStat.GoldFlat, 2, trigger: BoundaryItemTrigger.Kill, minimumGold: 8);
            default: throw new InvalidDataException("검토되지 않은 아이템 ID: " + id);
        }
    }

    private static BoundaryItemEffect[] One(string id, BoundaryItemStat stat, float amount,
        BoundaryAttackFamily family = BoundaryAttackFamily.Any, ElementType element = ElementType.ELEMENT_NONE,
        bool elite = false, float targetMin = -1, float targetMax = -1, float selfMin = -1, float selfMax = -1,
        BoundaryItemTrigger trigger = BoundaryItemTrigger.WhileOwned, float duration = 0, float cooldown = 0,
        float minimumLoss = 0, bool firstTarget = false, bool firstSkill = false, bool firstIncoming = false,
        bool firstReward = false, bool fullHealth = false, int minimumGold = 0, int maximumGold = 0)
        => new[] { E(id, stat, amount, family, element, elite, targetMin, targetMax, selfMin, selfMax,
            trigger, duration, cooldown, minimumLoss, firstTarget, firstSkill, firstIncoming, firstReward,
            fullHealth, minimumGold, maximumGold) };

    internal static BoundaryItemEffect E(string id, BoundaryItemStat stat, float amount,
        BoundaryAttackFamily family = BoundaryAttackFamily.Any, ElementType element = ElementType.ELEMENT_NONE,
        bool elite = false, float targetMin = -1, float targetMax = -1, float selfMin = -1, float selfMax = -1,
        BoundaryItemTrigger trigger = BoundaryItemTrigger.WhileOwned, float duration = 0, float cooldown = 0,
        float minimumLoss = 0, bool firstTarget = false, bool firstSkill = false, bool firstIncoming = false,
        bool firstReward = false, bool fullHealth = false, int minimumGold = 0, int maximumGold = 0)
        => new BoundaryItemEffect { effectId = id, stat = stat, amount = amount, family = family,
            element = element, eliteOrBossOnly = elite, targetHpMin = targetMin, targetHpMax = targetMax,
            selfHpMin = selfMin, selfHpMax = selfMax, trigger = trigger, durationSeconds = duration,
            cooldownSeconds = cooldown, minimumHealthLossRatio = minimumLoss, firstTargetHit = firstTarget,
            firstSkillHit = firstSkill, firstIncomingAttack = firstIncoming, firstRewardKill = firstReward,
            requiresFullHealth = fullHealth, minimumBaseGold = minimumGold, maximumBaseGold = maximumGold };

    internal static BoundaryItemEffect[] SynergyEffects(string id, int count, JObject values)
    {
        if (count is not (2 or 4 or 6 or 8)) throw new InvalidDataException("시너지 단계는 2/4/6/8입니다.");
        int index = count / 2 - 1;
        // Keep source identity across tier changes so a promotion cannot reset a cooldown.
        string key = id;
        switch (id)
        {
            case "S01":
                Keys(values, "critChanceBonusPoints", "critDamageMultiplierBonusPoints");
                return new[] {
                    E(key + ":chance", BoundaryItemStat.CriticalChancePoints, V("critChanceBonusPoints", 5, 10, 15, 20)),
                    E(key + ":damage", BoundaryItemStat.CriticalDamagePoints, V("critDamageMultiplierBonusPoints", 10, 20, 35, 50)) };
            case "S02":
                Keys(values, "attackPowerBonusPct", "normalAttackRateBonusPct");
                return new[] { E(key + ":attack", BoundaryItemStat.AttackRatio, V("attackPowerBonusPct", 5, 10, 18, 26) / 100),
                    E(key + ":rate", BoundaryItemStat.NormalAttackRateRatio, V("normalAttackRateBonusPct", 4, 8, 15, 22) / 100) };
            case "S03":
                Keys(values, "maxHpBonusPct", "defenseBonusPct");
                return new[] { E(key + ":hp", BoundaryItemStat.MaxHpRatio, V("maxHpBonusPct", 8, 16, 25, 34) / 100),
                    E(key + ":defense", BoundaryItemStat.DefenseRatio, V("defenseBonusPct", 10, 20, 30, 40) / 100) };
            case "S04": Keys(values, "waterDamageBonusPct"); return One(key, BoundaryItemStat.DamageRatio, V("waterDamageBonusPct", 5, 10, 15, 20) / 100, element: ElementType.ELEMENT_WATER);
            case "S05": Keys(values, "fireDamageBonusPct"); return One(key, BoundaryItemStat.DamageRatio, V("fireDamageBonusPct", 5, 10, 15, 20) / 100, element: ElementType.ELEMENT_FIRE);
            case "S08": Keys(values, "shieldMaxHpPct"); return One(key, BoundaryItemStat.ShieldMaxHpRatio, V("shieldMaxHpPct", 3, 4, 5, 6) / 100, trigger: BoundaryItemTrigger.DamageTaken, duration: 4, cooldown: 10);
            case "S10": Keys(values, "damagePct"); return One(key, BoundaryItemStat.DamageRatio, V("damagePct", 4, 6, 8, 10) / 100, BoundaryAttackFamily.NormalOrSkill, targetMax: .30f);
            case "S14": Keys(values, "ignoreDefensePct"); return One(key, BoundaryItemStat.IgnoreDefenseRatio, V("ignoreDefensePct", 6, 9, 12, 15) / 100, BoundaryAttackFamily.NormalOrSkill);
            case "S15": Keys(values, "damagePct"); return One(key, BoundaryItemStat.DamageRatio, V("damagePct", 4, 6, 8, 10) / 100, BoundaryAttackFamily.NormalOrSkill, elite: true);
            case "S16": Keys(values, "percent"); return One(key, BoundaryItemStat.DamageRatio, V("percent", 4, 8, 12, 16) / 100, BoundaryAttackFamily.Skill);
            case "S19": Keys(values, "percent"); return One(key, BoundaryItemStat.GoldFraction, V("percent", 6, 12, 18, 24) / 100, trigger: BoundaryItemTrigger.Kill, minimumGold: 1);
            default: throw new InvalidDataException("검토되지 않은 시너지 ID: " + id);
        }

        float V(string field, params float[] expected)
        {
            var token = values[field];
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
                throw new InvalidDataException("시너지 숫자 필드 누락: " + key + "/" + field);
            float actual = (float)token;
            if (float.IsNaN(actual) || float.IsInfinity(actual) || Math.Abs(actual - expected[index]) > .0001f)
                throw new InvalidDataException("검토한 시너지 수치와 다릅니다: " + key + "/" + field);
            return actual;
        }
    }

    private static void Keys(JObject values, params string[] keys)
    {
        if (values == null || !values.Properties().Select(p => p.Name).OrderBy(s => s)
            .SequenceEqual(keys.OrderBy(s => s))) throw new InvalidDataException("지원하지 않는 시너지 수치 필드입니다.");
    }
}
