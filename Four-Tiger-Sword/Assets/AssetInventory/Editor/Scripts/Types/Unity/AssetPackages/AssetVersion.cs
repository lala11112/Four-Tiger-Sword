using System;

namespace AssetInventory
{
    /// <summary>Asset Store version metadata containing the release name, revision, upload identity, publishing date, size, and supported Unity versions.</summary>
    [Serializable]
    public sealed class AssetVersion
    {
        public string id;
        public string name;
        public DateTime? publishedDate;

        public override string ToString()
        {
            return $"Asset Version ({name})";
        }
    }
}
