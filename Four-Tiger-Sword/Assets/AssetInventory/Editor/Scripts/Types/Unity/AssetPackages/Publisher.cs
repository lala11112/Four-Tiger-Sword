using System;

namespace AssetInventory
{
    /// <summary>Asset Store publisher identity and display label parsed from a Unity package header.</summary>
    [Serializable]
    public sealed class Publisher
    {
        public string id;
        public string name;
        public string externalRef;
        public string supportUrl;
        public string supportEmail;
        public string url;
        public string gaAccount;
        public string gaPrefix;
        public string slug; // used in updates

        public override string ToString()
        {
            return $"Publisher ({name})";
        }
    }
}
