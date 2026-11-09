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
            items.Add(new BoundaryZero.Inventory.ItemDefinition { id = item.itemId, name = item.displayName,
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
    {
        if (item == null) return string.Empty;
        var text = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(item.effectText))
            text.Append(ItemDescriptionDivider.EffectHeading).Append(LiteralEffect(item.effectText).TrimEnd());

        bool hasSynergy = false;
        foreach (var set in catalog?.synergies ?? Array.Empty<ItemSynergyDefinition>())
        {
            if (set?.members == null || Array.IndexOf(set.members, item.itemId) < 0) continue;
            if (!hasSynergy)
            {
                ItemDescriptionDivider.AppendSynergyHeading(text, text.Length > 0);
                hasSynergy = true;
            }
            else text.Append('\n');
            text.Append("<b>").Append(LiteralEffect(set.displayName)).Append("</b>\n");
            foreach (var tier in set.tiers ?? Array.Empty<ItemSynergyTier>())
                if (tier != null)
                    text.Append(tier.count).Append("세트 · ").Append(LiteralEffect(tier.text)).Append('\n');
        }
        if (!hasSynergy && item.synergyNames?.Length > 0)
        {
            ItemDescriptionDivider.AppendSynergyHeading(text, text.Length > 0);
            text.Append(LiteralEffect(string.Join(" · ", item.synergyNames)));
        }
        return text.ToString().TrimEnd();
    }

    private static string NormalizeCombatTerms(string text) => text?.Replace("평타 공격속도", "기본 공격 속도")
        .Replace("평타", "기본 공격");

    private static string LiteralEffect(string text) => (NormalizeCombatTerms(text) ?? string.Empty)
        .Replace("<", "<noparse><</noparse>");
}
