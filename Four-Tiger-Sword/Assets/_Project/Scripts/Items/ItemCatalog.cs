using System;
using UnityEngine;

[CreateAssetMenu(fileName = "ItemCatalog", menuName = "Four Tiger Sword/Items/Item Catalog")]
public sealed class ItemCatalog : ScriptableObject
{
    public string projectId;
    public int contentRevision;
    public string contentStatus;
    [HideInInspector, TextArea] public string sourceJson;
    [HideInInspector, TextArea] public string manifestJson;
    public string catalogSha256;
    public string manifestSha256;
    public ItemDefinition[] items = Array.Empty<ItemDefinition>();
    public ItemSynergyDefinition[] synergies = Array.Empty<ItemSynergyDefinition>();

    public ItemDefinition Find(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId) || items == null) return null;
        ItemDefinition result = null;
        foreach (var item in items)
        {
            if (item == null || !string.Equals(item.itemId, itemId, StringComparison.Ordinal)) continue;
            // A malformed catalog must never grant an arbitrary duplicate ID.
            if (result != null) return null;
            result = item;
        }
        return result;
    }
}
