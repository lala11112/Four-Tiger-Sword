using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace AssetInventory
{
    public sealed class UnityPackageDownloadImporter : AssetImporter
    {
        public IEnumerator IndexOnline(Action callback)
        {
            return IndexOnline(null, callback);
        }

        /// <summary>Downloads and indexes only the supplied package identities, bypassing automatic size scheduling.</summary>
        internal IEnumerator IndexOnline(IReadOnlyCollection<int> selectedAssetIds, Action callback)
        {
            HashSet<int> selectedIds = selectedAssetIds != null ? new HashSet<int>(selectedAssetIds) : null;
            bool explicitSelection = selectedIds != null;
            List<AssetInfo> packages = Assets.Load()
                .Where(info =>
                    info.AssetSource == Asset.Source.AssetStorePackage
                    && !info.Exclude
                    && info.ParentId <= 0
                    && !info.IsAbandoned && (explicitSelection || !info.IsIndexed || info.CurrentState == Asset.State.SubInProcess) && info.OfficialState != Asset.OfficialStateType.None
                    && (!explicitSelection || selectedIds.Contains(info.AssetId))
                    && !info.IsDownloaded)
                .ToList();

            for (int i = 0; i < packages.Count; i++)
            {
                if (CancellationRequested) break;
                AssetInfo info = packages[i];

                // Skip packages marked as no-index
                if (PackageIndexingPolicy.HasNoIndex(info)) continue;

                MainCount = packages.Count;
                SetProgress(info.GetDisplayName(), i + 1);

                if (!CanDownload(info, !explicitSelection)) continue;

                // trigger already next one in background
                AssetInfo nextInfo = i < packages.Count - 1 ? packages[i + 1] : null;
                if (nextInfo != null && !PackageIndexingPolicy.HasNoIndex(nextInfo) && CanDownload(nextInfo, !explicitSelection) && !nextInfo.IsDownloading())
                {
                    nextInfo.PackageDownloader.Download(true);
                }

                yield return DownloadAsset(info);
                if (CancellationRequested) break;
                if (!info.IsDownloaded) continue;

                int i2 = i;
                Task task = AI.Actions.RunWithProgress<UnityPackageImporter>(
                    ActionHandler.ACTION_ASSET_STORE_CACHE_INDEX,
                    "Indexing downloaded package",
                    imp =>
                    {
                        imp.HandlePackage(true, Paths.DeRel(info.Location), i2);
                        return imp.IndexDetails(info.AssetId);
                    });
                yield return new WaitWhile(() => !task.IsCompleted);

                // remove again
                yield return RemoveDownload(info.ToAsset());

                info.Refresh();
            }

            callback?.Invoke();
        }
    }
}
