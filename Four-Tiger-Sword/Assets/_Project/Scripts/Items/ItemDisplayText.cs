using System;
using System.Collections.Generic;
using System.Text;

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
                if (tier != null) tiers.Add(new BoundaryZero.Inventory.SynergyTier { count = tier.count, text = tier.text });
            sets.Add(new BoundaryZero.Inventory.SynergyDefinition { id = set.id, name = set.displayName,
                effect = set.description, summary = set.summary, tiers = tiers.ToArray() });
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
                description = item.description, effect = item.effectText, primarySynergy = item.primarySynergyId,
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
        var text = new StringBuilder(item.description ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(item.effectText))
        {
            if (text.Length > 0) text.Append("\n\n");
            text.Append("개별 효과\n").Append(item.effectText);
        }
        var sets = new List<ItemSynergyDefinition>();
        var names = new List<string>();
        if (catalog != null)
        {
            foreach (var set in catalog.synergies ?? Array.Empty<ItemSynergyDefinition>())
            {
                if (set?.members == null || Array.IndexOf(set.members, item.itemId) < 0) continue;
                sets.Add(set);
                names.Add(set.displayName);
            }
        }
        else if (item.synergyNames != null) names.AddRange(item.synergyNames);
        if (names.Count > 0)
        {
            if (text.Length > 0) text.Append("\n\n");
            text.Append("시너지\n").Append(string.Join(" · ", names));
            text.Append("\n서로 다른 2·4·6종 보유 시 해당 단계 적용");
            foreach (var synergy in sets)
            {
                text.Append("\n\n").Append(synergy.displayName);
                if (synergy.tiers != null)
                    foreach (var tier in synergy.tiers)
                        if (tier != null) text.Append("\n").Append(tier.count).Append("세트: ").Append(tier.text);
            }
        }
        return text.ToString();
    }
}
