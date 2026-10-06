namespace AssetInventory
{
    internal enum PackageIndexingStatus
    {
        Excluded,
        Pausing,
        NotIncluded,
        IndexedWithoutFutureIndexing,
        Indexing,
        Incomplete,
        NeedsIndexing,
        Indexed
    }

    /// <summary>Derives package indexing eligibility and presentation from the existing package flags and live state.</summary>
    internal static class PackageIndexingPolicy
    {
        internal static bool HasNoIndex(AssetInfo info)
        {
            if (info == null) return false;
            if (info.NoIndex) return true;

            return info.ParentInfo?.NoIndex == true;
        }

        internal static bool HasNoIndex(Asset asset)
        {
            if (asset == null) return false;
            if (asset.NoIndex) return true;
            if (asset.ParentId <= 0) return false;

            asset.ParentAsset ??= DBAdapter.DB.Find<Asset>(asset.ParentId);
            return asset.ParentAsset != null && asset.ParentAsset.NoIndex;
        }

        internal static bool HasIndexedContent(AssetInfo info)
        {
            return info != null && info.FileCount > 0;
        }

        internal static bool HasIncompleteIndexing(AssetInfo info)
        {
            return info != null && (info.CurrentState == Asset.State.InProcess || info.CurrentState == Asset.State.SubInProcess);
        }

        internal static bool IsIndexingEnabled(AssetInfo info)
        {
            return info != null && !info.Exclude && !HasNoIndex(info);
        }

        internal static bool NeedsIndexing(AssetInfo info)
        {
            return IsIndexingEnabled(info) && (HasIncompleteIndexing(info) || !HasIndexedContent(info));
        }

        internal static bool IsInheritedNoIndex(AssetInfo info)
        {
            return info?.ParentInfo?.NoIndex == true;
        }

        internal static PackageIndexingStatus GetStatus(AssetInfo info, bool indexingActionRunning = false)
        {
            if (info == null) return PackageIndexingStatus.NeedsIndexing;
            if (info.Exclude) return PackageIndexingStatus.Excluded;

            if (HasNoIndex(info))
            {
                if (indexingActionRunning && HasIncompleteIndexing(info)) return PackageIndexingStatus.Pausing;
                return HasIndexedContent(info)
                    ? PackageIndexingStatus.IndexedWithoutFutureIndexing
                    : PackageIndexingStatus.NotIncluded;
            }

            if (HasIncompleteIndexing(info))
            {
                return indexingActionRunning ? PackageIndexingStatus.Indexing : PackageIndexingStatus.Incomplete;
            }
            return HasIndexedContent(info) ? PackageIndexingStatus.Indexed : PackageIndexingStatus.NeedsIndexing;
        }
    }
}
