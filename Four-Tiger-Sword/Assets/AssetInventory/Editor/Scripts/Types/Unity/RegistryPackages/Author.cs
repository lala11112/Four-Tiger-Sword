using System;

namespace AssetInventory
{
    /// <summary>Unity registry package author metadata containing the publisher name, email address, and website.</summary>
    [Serializable]
    public sealed class Author
    {
        public string name;
        public string email;
        public string url;

        public override string ToString()
        {
            return $"Package Author '{name}'";
        }
    }
}
