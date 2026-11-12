using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public static class PlayerStatsReadModel
{
    public static IReadOnlyList<StatType> Stats { get; } = Array.AsReadOnly(new[]
    {
        StatType.ST_HP,
        StatType.ST_ATK,
        StatType.ST_ATK_SPD,
        StatType.ST_CRT,
        StatType.ST_CRTD,
        StatType.ST_DEF,
        StatType.KNOCKBACK_RESIST
    });

    public readonly struct Entry
    {
        public string Label { get; }
        public float Current { get; }
        public float Base { get; }
        public float Delta => Current - Base;
        public string ValueText { get; }
        public string BaseText { get; }
        public string DeltaText { get; }
        public IReadOnlyList<Contribution> Contributions { get; }

        internal Entry(string label, StatType stat, float current, float baseValue, IReadOnlyList<Contribution> contributions)
        {
            Label = label;
            Current = current;
            Base = baseValue;
            ValueText = Format(stat, current);
            BaseText = Format(stat, baseValue);
            DeltaText = Format(stat, current - baseValue, signed: true);
            Contributions = contributions;
        }
    }

    public readonly struct Contribution
    {
        public string Label { get; }
        public float Value { get; }
        public string ValueText { get; }

        internal Contribution(string label, StatType stat, float value)
        {
            Label = label;
            Value = value;
            ValueText = Format(stat, value, signed: true);
        }
    }

    public static Entry Read(PlayerStatManager manager, StatType stat)
    {
        if (manager == null) throw new ArgumentNullException(nameof(manager));
        string label = stat switch
        {
            StatType.ST_HP => "체력",
            StatType.ST_ATK => "공격력",
            StatType.ST_ATK_SPD => "공격 속도",
            StatType.ST_CRT => "치명타 확률",
            StatType.ST_CRTD => "치명타 피해",
            StatType.ST_DEF => "방어력",
            StatType.KNOCKBACK_RESIST => "넉백 저항",
            _ => throw new ArgumentOutOfRangeException(nameof(stat))
        };

        float current = manager.GetStat(stat);
        float baseValue = manager.GetBaseStat(stat);
        var (workshop, engraving, item, other) = manager.GetStatContributions(stat);
        if (stat == StatType.ST_ATK_SPD)
        {
            float itemRate = manager.TryGetComponent(out PlayerItemRuntime items) ? items.NormalAttackRateMultiplier : 1f;
            float normalAttackRate = Mathf.Max(0.1f, current * itemRate);
            item += normalAttackRate - current;
            current = normalAttackRate;
        }

        var contributions = new List<Contribution>(4);
        AddContribution(contributions, "강화", stat, workshop);
        AddContribution(contributions, "각인", stat, engraving);
        AddContribution(contributions, "아이템", stat, item);
        AddContribution(contributions, "추가 효과", stat, other);
        return new Entry(label, stat, current, baseValue, contributions.AsReadOnly());
    }

    private static void AddContribution(List<Contribution> contributions, string label, StatType stat, float value)
    {
        if (DisplayNumber(stat, value) != 0d)
            contributions.Add(new Contribution(label, stat, value));
    }

    private static double DisplayNumber(StatType stat, float value)
    {
        double displayValue = stat == StatType.KNOCKBACK_RESIST ? value * 100d : value;
        return Math.Round(displayValue, 2, MidpointRounding.AwayFromZero);
    }

    private static string Format(StatType stat, float value, bool signed = false)
    {
        double number = DisplayNumber(stat, value);
        if (signed && number == 0d) return "—";
        string suffix = stat switch
        {
            StatType.ST_ATK_SPD => "×",
            StatType.ST_CRT or StatType.ST_CRTD or StatType.KNOCKBACK_RESIST => signed ? "%p" : "%",
            _ => ""
        };
        string format = stat == StatType.ST_ATK_SPD ? "0.00" : "#,##0.##";
        string sign = signed && number > 0d ? "+" : "";
        return sign + number.ToString(format, CultureInfo.InvariantCulture) + suffix;
    }
}
