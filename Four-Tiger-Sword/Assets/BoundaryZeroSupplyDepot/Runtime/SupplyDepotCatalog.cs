using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoundaryZero.SupplyDepot
{
    [CreateAssetMenu(menuName = "Boundary Zero/Supply Depot Catalog")]
    public sealed class SupplyDepotCatalog : ScriptableObject
    {
        public List<SupplyDepotItem> items = new List<SupplyDepotItem>();
        public Sprite currencyIcon;
        public Sprite frameSprite;
        public Sprite backdrop;
        public TMPro.TMP_FontAsset font;
        [Min(0)] public int price = 100;
        [Min(0.01f)] public float drawDuration = 4.2f;
    }

    [Serializable]
    public sealed class SupplyDepotItem
    {
        public string id;
        public string name;
        [TextArea] public string effect;
        public int grade;
        public Color color = Color.white;
        public Sprite icon;
        public List<SupplyDepotSynergy> synergies = new List<SupplyDepotSynergy>();
    }

    [Serializable]
    public sealed class SupplyDepotSynergy
    {
        public string id;
        public string name;
        public Sprite icon;
        public string[] compactLines = Array.Empty<string>();
        public int[] thresholds = Array.Empty<int>();
    }
}
