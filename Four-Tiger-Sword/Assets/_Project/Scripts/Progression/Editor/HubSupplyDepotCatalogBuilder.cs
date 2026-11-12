using System;
using System.Collections.Generic;
using System.Linq;
using BoundaryZero.Inventory;
using BoundaryZero.SupplyDepot;
using UnityEditor;
using UnityEngine;

/// <summary>Uses current gameplay text and membership while retaining the delivered depot artwork.</summary>
public static class HubSupplyDepotCatalogBuilder
{
    public const string CatalogPath = "Assets/_Project/Generated/BoundaryInventory/HubSupplyDepotCatalog.asset";
    public const string ItemCatalogPath = "Assets/_Project/Generated/BoundaryInventory/ItemCatalog.asset";
    private const string SourcePath = "Assets/BoundaryZeroSupplyDepot/Settings/SupplyDepotCatalog.asset";

    public static SupplyDepotCatalog Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Build the supply catalog in Edit Mode.");
        var source = AssetDatabase.LoadAssetAtPath<SupplyDepotCatalog>(SourcePath);
        var gameplay = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemCatalogPath);
        if (source == null || gameplay == null || gameplay.items == null || gameplay.items.Length == 0)
            throw new InvalidOperationException("The supplied artwork or gameplay catalog is missing.");
        var art = source.items.ToDictionary(item => item.id, StringComparer.Ordinal);
        var entries = new List<SupplyDepotItem>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in gameplay.items)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.itemId) || !ids.Add(item.itemId))
                throw new InvalidOperationException("The gameplay catalog has an invalid or duplicate item.");
            if (!art.TryGetValue(item.itemId, out var original) || original.icon == null)
                throw new InvalidOperationException("Supply artwork is missing for " + item.itemId);
            var entry = new SupplyDepotItem
            {
                id = item.itemId, name = item.displayName, effect = ItemDisplayText.Effect(item),
                grade = item.grade, color = original.color, icon = original.icon
            };
            foreach (var set in gameplay.synergies ?? Array.Empty<ItemSynergyDefinition>())
            {
                if (set?.members == null || Array.IndexOf(set.members, item.itemId) < 0) continue;
                var tiers = (set.tiers ?? Array.Empty<ItemSynergyTier>()).Where(tier => tier != null)
                    .OrderBy(tier => tier.count).ToArray();
                var copy = new SupplyDepotSynergy
                {
                    id = set.id, name = set.displayName, icon = set.icon,
                    thresholds = tiers.Select(tier => tier.count).ToArray(),
                    compactLines = ItemPresentationText.CompactSynergy(tiers.Select(tier =>
                        new SynergyTier { count = tier.count, text = tier.text }))
                        .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                };
                if (copy.icon == null)
                    copy.icon = original.synergies.FirstOrDefault(value => value.id == set.id)?.icon;
                entry.synergies.Add(copy);
            }
            entries.Add(entry);
        }
        var catalog = AssetDatabase.LoadAssetAtPath<SupplyDepotCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<SupplyDepotCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        Undo.RecordObject(catalog, "Update Hub Supply Catalog");
        catalog.items = entries;
        catalog.currencyIcon = source.currencyIcon;
        catalog.frameSprite = source.frameSprite;
        catalog.backdrop = source.backdrop;
        catalog.font = source.font;
        catalog.price = 100;
        catalog.drawDuration = 4.2f;
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssetIfDirty(catalog);
        return catalog;
    }
}
