using System;
using System.Collections.Generic;
using System.Linq;
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
        => string.IsNullOrWhiteSpace(primarySynergyName) ? Stars(grade) : $"{Stars(grade)} · {primarySynergyName}";

    public static string Stars(int grade) => ItemPresentationText.Stars(grade);

    public static string Effect(ItemDefinition item) => item == null ? string.Empty : LiteralEffect(item.effectText);

    public static string SynergyMembership(ItemDefinition item, ItemCatalog catalog)
    {
        if (item == null) return string.Empty;
        var names = MatchingSets(item, catalog).Select(s => s.displayName).ToArray();
        if (names.Length == 0) names = item.synergyNames?.Length > 0 ? item.synergyNames : new[] { item.primarySynergyName };
        return LiteralEffect(string.Join(" · ", names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct()));
    }

    public static string SynergyDescription(ItemDefinition item, ItemCatalog catalog)
        => SynergyDescription(item, catalog, null, false);

    public static string SynergyDescription(ItemDefinition item, ItemCatalog catalog, IEnumerable<ItemSynergyState> ownedStates)
        => SynergyDescription(item, catalog, ownedStates, true);

    private static string SynergyDescription(ItemDefinition item, ItemCatalog catalog,
        IEnumerable<ItemSynergyState> ownedStates, bool showActivation)
    {
        if (item == null) return string.Empty;
        var states = (ownedStates ?? Array.Empty<ItemSynergyState>()).ToArray();
        var blocks = MatchingSets(item, catalog).Select(set =>
        {
            var tiers = (set.tiers ?? Array.Empty<ItemSynergyTier>()).Where(t => t != null)
                .Select(t => new SynergyTier { count = t.count, text = t.text }).ToArray();
            if (showActivation)
            {
                var state = states.FirstOrDefault(s => s.Definition != null && s.Definition.id == set.id);
                bool active = tiers.Any(t => t.count > 0 && t.count <= state.OwnedCount);
                return "<b>" + ItemPresentationText.StateText(set.displayName, active) + "</b>\n"
                    + SynergyEffectDescription(set, state.OwnedCount);
            }
            return "<color=#F4B976><b>" + LiteralEffect(set.displayName) + "</b></color>\n"
                + "<size=85%><color=#BCCEE0>" + ItemPresentationText.TierCounts(tiers) + "</color></size>\n"
                + LiteralEffect(ItemPresentationText.CompactSynergy(tiers));
        }).ToArray();
        return blocks.Length > 0 ? string.Join("\n\n", blocks) : SynergyMembership(item, catalog);
    }

    public static string SynergyEffectDescription(ItemSynergyDefinition set, int ownedCount)
    {
        if (set == null) return string.Empty;
        var tiers = (set.tiers ?? Array.Empty<ItemSynergyTier>()).Where(t => t != null)
            .Select(t => new SynergyTier { count = t.count, text = t.text }).ToArray();
        return "<size=85%>" + ItemPresentationText.TierCounts(tiers, ownedCount) + "</size>\n"
            + ItemPresentationText.CompactSynergy(tiers, ownedCount);
    }

    private static IEnumerable<ItemSynergyDefinition> MatchingSets(ItemDefinition item, ItemCatalog catalog)
        => (catalog?.synergies ?? Array.Empty<ItemSynergyDefinition>())
            .Where(set => set?.members != null && Array.IndexOf(set.members, item.itemId) >= 0);

    public static string Description(ItemDefinition item, ItemCatalog catalog = null)
        => Describe(item, catalog, false);

    public static string Description(ItemDefinition item, ItemCatalog catalog, IEnumerable<ItemSynergyState> ownedStates)
        => Describe(item, catalog, false, ownedStates, true);

    internal static string RewardDescription(ItemDefinition item, ItemCatalog catalog)
        => Describe(item, catalog, true);

    private static string Describe(ItemDefinition item, ItemCatalog catalog, bool reward,
        IEnumerable<ItemSynergyState> ownedStates = null, bool showActivation = false)
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

        var synergy = showActivation ? SynergyDescription(item, catalog, ownedStates) : SynergyDescription(item, catalog);
        if (!string.IsNullOrWhiteSpace(synergy))
        {
            AppendSynergyHeading(text, reward);
            text.Append(synergy);
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
        .Replace("평타", "기본 공격").Replace("스킬 재사용 대기시간", "스킬 쿨타임");

    internal static string LiteralEffect(string text) => (NormalizeCombatTerms(text) ?? string.Empty)
        .Replace("<", "<noparse><</noparse>");
}
