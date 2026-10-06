using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AssetInventory
{
    internal static class PackageDownloadCompletion
    {
        internal static bool SyncPackage(AssetInfo info)
        {
            if (info == null) return false;

            AssetInfo root = info.GetRoot();
            if (root == null) return false;

            try
            {
                bool synced = ApplyLatestDatabaseState(root);
                AssetDownloader downloader = root.PackageDownloader;
                if (downloader != null)
                {
                    downloader.SetAsset(root);
                    downloader.IsDirty = true;
                    downloader.RefreshState(true);
                    synced = ApplyLatestDatabaseState(root) || synced;
                    KeepPollingUntilFinal(root);
                }
                else
                {
                    root.Refresh(true);
                }

                return synced;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Asset Inventory could not synchronize completed download state for '{root.GetDisplayName()}': {e.Message}");
                KeepPollingUntilFinal(root);
                return false;
            }
        }

        internal static int SyncVisiblePackages(IEnumerable<AssetInfo> assets, int foreignId)
        {
            if (assets == null || foreignId <= 0) return 0;

            int synced = 0;
            HashSet<int> syncedAssetIds = new HashSet<int>();
            HashSet<AssetInfo> syncedReferences = new HashSet<AssetInfo>();

            foreach (AssetInfo info in assets)
            {
                AssetInfo root = info?.GetRoot();
                if (root == null || root.AssetSource == Asset.Source.Synty || root.ForeignId != foreignId) continue;

                if (root.AssetId > 0)
                {
                    if (!syncedAssetIds.Add(root.AssetId)) continue;
                }
                else if (!syncedReferences.Add(root))
                {
                    continue;
                }

                if (SyncPackage(root)) synced++;
            }

            return synced;
        }

        private static bool ApplyLatestDatabaseState(AssetInfo root)
        {
            Asset latest = LoadLatestAsset(root);
            if (latest == null)
            {
                root.Refresh(true);
                return false;
            }

            root.CopyFrom(latest);
            root.Refresh(true);
            return true;
        }

        private static Asset LoadLatestAsset(AssetInfo root)
        {
            if (root == null) return null;

            if (root.AssetId > 0)
            {
                Asset asset = DBAdapter.DB.Find<Asset>(root.AssetId);
                if (asset != null) return asset;
            }

            if (root.ForeignId <= 0) return null;

            return PackageIdentityReconciler.LoadPreferredTopLevelAsset(root.ForeignId);
        }

        private static void KeepPollingUntilFinal(AssetInfo root)
        {
            AssetDownloader downloader = root?.PackageDownloader;
            if (downloader == null) return;

            AssetDownloader.State state = downloader.GetState().state;
            if (state != AssetDownloader.State.Downloaded
                && state != AssetDownloader.State.UpdateAvailable
                && root.IsDownloaded)
            {
                downloader.GetState().SetState(root.IsUpdateAvailable() ? AssetDownloader.State.UpdateAvailable : AssetDownloader.State.Downloaded);
                return;
            }

            if (state != AssetDownloader.State.Downloaded && state != AssetDownloader.State.UpdateAvailable)
            {
                downloader.IsDirty = true;
            }
        }
    }
}
