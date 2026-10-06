using System;
using UnityEngine;

namespace AssetInventory
{
    /// <summary>Joined tag assignment and tag-definition data for a package or indexed file.</summary>
    [Serializable]
    // used to contain results of join calls
    public sealed class TagInfo : TagAssignment
    {
        public string Name { get; set; }
        public string Color { get; set; }

        /// <summary>Returns the tag result's configured HTML color, or the supplied or default fallback when no valid color is stored.</summary>
        public Color GetColor()
        {
            if (ColorUtility.TryParseHtmlString(Color, out Color toUse)) return toUse;
            return Tag.DefaultColor;
        }

        public override string ToString()
        {
            return $"Tag Info '{Name}' ('{TagTarget}', {TargetId})";
        }
    }
}
