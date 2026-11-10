using System;
using System.Collections.Generic;
using System.Text;
using BoundaryZero.Inventory;

/// <summary>Shared authored item copy for commerce and gameplay presentation.</summary>
public static class ItemDisplayText
{
    /// <summary>Adapts the gameplay catalog to the package view without consulting its fallback JSON.</summary>
    public static BoundaryZero.Inventory.InventoryData ToInventoryData(ItemCatalog catalog)
    {
        var data = new BoundaryZero.Inventory.InventoryData();
        if (catalog == null) return data;
        var memberships = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var sets = new List<BoundaryZero.Inventory.SynergyDefinition>();
        foreach (var set in catalog.synergies ?? Array.Empty<ItemSynergyDefinition>())
        {
            if (set == null || string.IsNullOrEmpty(set.id)) continue;
            var tiers = new List<BoundaryZero.Inventory.SynergyTier>();
            foreach (var tier in set.tiers ?? Array.Empty<ItemSynergyTier>())
                if (tier != null) tiers.Add(new BoundaryZero.Inventory.SynergyTier { count = tier.count, text = NormalizeCombatTerms(tier.text) });
            sets.Add(new BoundaryZero.Inventory.SynergyDefinition { id = set.id, name = set.displayName,
                effect = NormalizeCombatTerms(set.description), summary = NormalizeCombatTerms(set.summary), tiers = tiers.ToArray() });
            foreach (var member in set.members ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(member)) continue;
                if (!memberships.TryGetValue(member, out var tags)) memberships.Add(member, tags = new List<string>());
                if (!tags.Contains(set.id)) tags.Add(set.id);
            }
        }
        var items = new List<BoundaryZero.Inventory.ItemDefinition>();
        foreach (var item in catalog.items ?? Array.Empty<ItemDefinition>())
        {
            if (item == null || string.IsNullOrEmpty(item.itemId)) continue;
            items.Add(new BoundaryZero.Inventory.ItemDefinition { id = item.itemId, name = item.displayName, grade = item.grade,
                effect = NormalizeCombatTerms(item.effectText), primarySynergy = item.primarySynergyId,
                synergies = memberships.TryGetValue(item.itemId, out var tags) ? tags.ToArray() : Array.Empty<string>() });
        }
        data.items = items.ToArray();
        data.synergies = sets.ToArray();
        return data;
    }

    public static string Meta(ItemDefinition item)
        => item == null ? string.Empty : Meta(item.grade, item.primarySynergyName);

    public static string Meta(int grade, string primarySynergyName)
        => string.IsNullOrWhiteSpace(primarySynergyName) ? $"{grade}등급" : $"{grade}등급 · {primarySynergyName}";

    public static string Description(ItemDefinition item, ItemCatalog catalog = null)
        => Describe(item, catalog, false);

    internal static string RewardDescription(ItemDefinition item, ItemCatalog catalog)
        => Describe(item, catalog, true);

    private static string Describe(ItemDefinition item, ItemCatalog catalog, bool reward)
    {
        if (item == null) return string.Empty;
        var text = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(item.effectText))
        {
            text.Append(reward
                ? "<mark=#34506977><color=#D2E7FC><b>  개별 효과  </b></color></mark>\n"
                : ItemDescriptionDivider.EffectHeading);
            text.Append(LiteralEffect(item.effectText).TrimEnd());
        }

        bool hasSynergy = false;
        foreach (var set in catalog?.synergies ?? Array.Empty<ItemSynergyDefinition>())
        {
            if (set?.members == null || Array.IndexOf(set.members, item.itemId) < 0) continue;
            if (!hasSynergy)
            {
                AppendSynergyHeading(text, reward);
                hasSynergy = true;
            }
            else text.Append('\n');
            text.Append(reward ? "<color=#F4B976><b>" : "<b>")
                .Append(LiteralEffect(set.displayName)).Append(reward ? "</b></color>\n" : "</b>\n");
            foreach (var tier in set.tiers ?? Array.Empty<ItemSynergyTier>())
                if (tier != null)
                {
                    if (reward) text.Append("<color=#BCCEE0><b>");
                    text.Append(tier.count).Append("세트");
                    if (reward) text.Append("</b></color>");
                    text.Append(" · ").Append(LiteralEffect(tier.text)).Append('\n');
                }
        }
        if (!hasSynergy && item.synergyNames?.Length > 0)
        {
            AppendSynergyHeading(text, reward);
            text.Append(LiteralEffect(string.Join(" · ", item.synergyNames)));
        }
        return text.ToString().TrimEnd();
    }

    private static void AppendSynergyHeading(StringBuilder text, bool reward)
    {
        if (!reward)
        {
            ItemDescriptionDivider.AppendSynergyHeading(text, text.Length > 0);
            return;
        }
        if (text.Length > 0) text.Append("\n\n");
        text.Append("<mark=#78413377><color=#F9C48A><b>  시너지 효과  </b></color></mark>\n");
    }

    private static string NormalizeCombatTerms(string text) => text?.Replace("평타 공격속도", "기본 공격 속도")
        .Replace("평타", "기본 공격");

    internal static string LiteralEffect(string text) => (NormalizeCombatTerms(text) ?? string.Empty)
        .Replace("<", "<noparse><</noparse>");
}
