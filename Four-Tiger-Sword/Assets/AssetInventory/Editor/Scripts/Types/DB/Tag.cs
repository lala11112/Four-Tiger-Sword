using System;
using SQLite;
using UnityEngine;

namespace AssetInventory
{
    /// <summary>Persistent hierarchical tag definition with its display name, parent relationship, and optional color.</summary>
    [Serializable]
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public sealed partial class Tag : IEquatable<Tag>
    {
        public static Color DefaultColor = UnityEngine.Color.white;

        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        [Indexed] [Collation("NOCASE")] public string Name { get; set; }
        public string Color { get; set; }
        public bool FromAssetStore { get; set; }
        public string Hotkey { get; set; }  // Stores the keyboard shortcut (e.g. "1", "a", etc.)
        [Indexed] public int? ParentId { get; set; }  // null = root-level tag, otherwise references parent Tag.Id

        public Tag()
        {
        }

        /// <summary>Creates a new tag definition with the supplied display name.</summary>
        public Tag(string name)
        {
            Name = name;
        }

        /// <summary>Returns the tag's configured HTML color, or the supplied or default fallback when no valid color is stored.</summary>
        public Color GetColor()
        {
            return ColorUtility.TryParseHtmlString(Color, out Color toUse) ? toUse : DefaultColor;
        }

        public bool Equals(Tag other)
        {
            return other?.Name == Name;
        }

        public override string ToString()
        {
            return $"Tag '{Name}' ({Color})";
        }
    }
}
