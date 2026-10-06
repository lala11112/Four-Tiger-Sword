using System;

namespace AssetInventory
{
    /// <summary>Asset Store category identity and display label parsed from a Unity package header.</summary>
    [Serializable]
    public sealed class Category
    {
        public string id;
        public string name;
        public string slug;

        public override string ToString()
        {
            return $"Category ({name})";
        }
    }
}
