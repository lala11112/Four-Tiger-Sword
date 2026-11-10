using System;
using TMPro;
using UnityEngine;

namespace BoundaryZero.Inventory
{
    [CreateAssetMenu(menuName = "Boundary Zero/Inventory Catalog")]
    public sealed class InventoryCatalog : ScriptableObject
    {
        public TextAsset json;
        public SpriteBinding[] itemIcons = Array.Empty<SpriteBinding>();
        public SpriteBinding[] synergyIcons = Array.Empty<SpriteBinding>();
        public TMP_FontAsset font;
        public Sprite windowFrame;
        public Sprite seal;

        public InventoryData ReadData() =>
            json != null ? JsonUtility.FromJson<InventoryData>(json.text) ?? new InventoryData() : new InventoryData();

        internal Sprite ItemIcon(string id) => FindIcon(itemIcons, id);
        internal Sprite SynergyIcon(string id) => FindIcon(synergyIcons, id);

        private static Sprite FindIcon(SpriteBinding[] bindings, string id)
        {
            if (bindings != null)
                foreach (var binding in bindings)
                    if (binding.id == id)
                        return binding.sprite;
            return null;
        }
    }

    [Serializable]
    public struct SpriteBinding
    {
        public string id;
        public Sprite sprite;
        public SpriteBinding(string id, Sprite sprite) { this.id = id; this.sprite = sprite; }
    }

    [Serializable]
    public sealed class InventoryData
    {
        public ItemDefinition[] items = Array.Empty<ItemDefinition>();
        public SynergyDefinition[] synergies = Array.Empty<SynergyDefinition>();
    }

    [Serializable]
    public sealed class ItemDefinition
    {
        public string id;
        public string name;
        public string description;
        public string effect;
        public int grade;
        public string[] synergies = Array.Empty<string>();
        public string primarySynergy;
    }

    [Serializable]
    public sealed class SynergyDefinition
    {
        public string id;
        public string name;
        public string effect;
        public string summary;
        public SynergyTier[] tiers = Array.Empty<SynergyTier>();
    }

    [Serializable]
    public sealed class SynergyTier
    {
        public int count;
        public string text;
    }

    public readonly struct SynergyState
    {
        public string Id { get; }
        public int Count { get; }
        public int OwnedCount => Count;
        public int RequiredCount { get; }
        public int DisplayCount => Math.Min(RequiredCount, Count);
        public int ActiveTier { get; }
        public int NextTier { get; }
        public string ActiveEffect { get; }
        public bool IsActive => ActiveTier > 0;

        internal SynergyState(string id, int count, int requiredCount, int activeTier, int nextTier, string activeEffect)
        {
            Id = id;
            Count = count;
            RequiredCount = requiredCount;
            ActiveTier = activeTier;
            NextTier = nextTier;
            ActiveEffect = activeEffect;
        }
    }
}
