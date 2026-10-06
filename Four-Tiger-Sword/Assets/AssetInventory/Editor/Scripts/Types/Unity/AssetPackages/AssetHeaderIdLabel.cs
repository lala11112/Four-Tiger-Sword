using System;

namespace AssetInventory
{
    /// <summary>Identifier and display label pair used for publisher and category entries in parsed Unity package headers.</summary>
    [Serializable]
    public sealed class AssetHeaderIdLabel
    {
        public string id;
        public string label;

        public override string ToString()
        {
            return $"Asset Header Label ({id}, {label})";
        }
    }
}
