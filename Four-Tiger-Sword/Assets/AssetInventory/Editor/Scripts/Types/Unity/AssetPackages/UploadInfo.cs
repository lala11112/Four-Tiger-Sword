using System;

namespace AssetInventory
{
    /// <summary>Asset Store upload metadata containing package version, archive keys, file count, download size, render pipelines, and dependencies.</summary>
    [Serializable]
    public sealed class UploadInfo
    {
        public string assetCount;
        public string downloadSize;
        public string downloadS3key;
        public string uploadS3key;
        public string versionNumber;
        public string[] srps;
        public string[] dependencies;

        public override string ToString()
        {
            return $"Upload Info ({downloadSize} bytes, {assetCount} files)";
        }
    }
}
