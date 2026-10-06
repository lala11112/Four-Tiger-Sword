using System;

namespace AssetInventory
{
    /// <summary>Parsed Unity package header metadata used to associate imported archives with Asset Store identity and publisher/category records.</summary>
    [Serializable]
    public sealed class AssetHeader
    {
        public AssetHeaderIdType link;
        public string unity_version;
        public string pubdate;
        public string version;
        public string description;
        public string upload_id;
        public string version_id;
        public AssetHeaderIdLabel category;
        public string publishnotes;
        public string id;
        public string title;
        public AssetHeaderIdLabel publisher;

        public override string ToString()
        {
            return $"Asset Header ({title}, {version})";
        }
    }
}
