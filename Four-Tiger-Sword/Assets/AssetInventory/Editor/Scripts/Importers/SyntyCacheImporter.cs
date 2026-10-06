using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ImpossibleRobert.Common;

namespace AssetInventory
{
    /// <summary>Discovers locally downloaded Synty Importer packages and indexes new or changed archives without using online services.</summary>
    public sealed class SyntyCacheImporter : AssetImporter
    {
        /// <summary>Creates a transient package-folder context for the Synty Importer cache, discovers valid archives, and indexes changed packages.</summary>
        public async Task Run()
        {
            if (AI.Config == null || !AI.Config.syntyFeatureEnabled) return;

            string root = SyntyCache.Root;
            CurrentMain = "Discovering Synty Importer cache";
            MainCount = 1;
            MainProgress = 0;
            MetaProgress.Report(ProgressId, MainProgress, MainCount, CurrentMain);
            if (!Directory.Exists(root))
            {
                MainProgress = MainCount;
                MetaProgress.Report(ProgressId, MainProgress, MainCount, "Synty Importer cache not found");
                return;
            }

            string[] packages = await Task.Run(() => IOUtils.GetFilesSafe(root, "*.unitypackage", SearchOption.TopDirectoryOnly)
                .Where(path => SyntyCache.IsValidPackage(path))
                .Where(path => !SyntyCache.IsImporterPartialActive(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray());

            FolderSpec spec = CreateTransientFolderSpec(root);
            UnityPackageImporter importer = new UnityPackageImporter();
            HashSet<int> candidateIds = new HashSet<int>();
            bool tagsChanged = false;
            MainCount = Math.Max(1, packages.Length);
            for (int i = 0; i < packages.Length; i++)
            {
                if (CancellationRequested) break;

                string package = packages[i].Replace("\\", "/");
                SetProgress("Discovering " + Path.GetFileName(package), i + 1);
                Asset asset = importer.HandlePackage(false, package, i, false, sourceOverride: Asset.Source.Synty);
                if (asset == null) continue;

                if (ApplyPackageTags(spec, asset)) tagsChanged = true;
                if (TryApplyTypeTag(asset)) tagsChanged = true;
                if (asset.NoIndex)
                {
                    asset.CurrentState = Asset.State.Done;
                    DBAdapter.DB.Update(asset);
                }
                else if (asset.Id > 0 && !asset.Exclude && (asset.CurrentState == Asset.State.InProcess || asset.CurrentState == Asset.State.SubInProcess))
                {
                    candidateIds.Add(asset.Id);
                }
                if (i % 10 == 0) await Task.Yield();
            }
            if (tagsChanged) Tagging.LoadAssignments();

            List<int> candidates = candidateIds.ToList();
            if (candidates.Count > 0)
            {
                CurrentMain = "Preparing Synty package indexing";
                MainCount = candidates.Count;
                MainProgress = 0;
                MetaProgress.Report(ProgressId, MainProgress, MainCount, CurrentMain);
            }
            for (int i = 0; i < candidates.Count; i++)
            {
                if (CancellationRequested) break;

                Asset asset = DBAdapter.DB.Find<Asset>(candidates[i]);
                SetProgress("Indexing " + (asset?.DisplayName ?? asset?.SafeName ?? "Synty package"), i + 1);
                Task indexing = importer.IndexDetails(candidates[i]);
                while (!indexing.IsCompleted)
                {
                    importer.CancellationRequested = CancellationRequested;
                    CurrentSub = importer.CurrentSub ?? importer.CurrentMain;
                    SubCount = importer.CurrentSub == null ? importer.MainCount : importer.SubCount;
                    SubProgress = importer.CurrentSub == null ? importer.MainProgress : importer.SubProgress;
                    await Task.WhenAny(indexing, Task.Delay(100));
                }
                await indexing;
            }
            CurrentSub = null;
            SubCount = 0;
            SubProgress = 0;
            AI.TriggerPackageRefresh();
        }

        internal static FolderSpec CreateTransientFolderSpec(string root)
        {
            return new FolderSpec(root)
            {
                folderType = 0,
                enabled = true,
                assignTag = true,
                tag = "Synty",
                createPreviews = true,
                removeOrphans = false,
                attachToPackage = true
            };
        }

        internal static void ClearAssetStoreLinkMetadata()
        {
            DBAdapter.DB.Execute("update Asset set ForeignId=0, PublisherId=0, UploadId=0, CompatibilityInfo=null, BIRPCompatible=0, URPCompatible=0, HDRPCompatible=0, PackageDependencies=null, AssetRating=0, RatingCount=0, Hotness=0, Requirements=null, ReleaseNotes=null, PurchaseDate=0, IsHidden=0, ETag=null, LastOnlineRefresh=0 where AssetSource=?", Asset.Source.Synty);
        }

        private static bool TryApplyTypeTag(Asset asset)
        {
            if (!UnityPackageImporter.TryParseSyntyFilename(asset.OriginalLocationKey, out string group, out _, out _, out _)) return false;
            string type = ImpossibleRobert.Common.StringUtils.CamelCaseToWords(group.Replace("_", " ").ToLowerInvariant()).Trim();
            if (!string.IsNullOrWhiteSpace(type)) type = char.ToUpperInvariant(type[0]) + type.Substring(1);
            return Tagging.AddAssignment(asset.Id, "Synty/" + type, TagAssignment.Target.Package, false);
        }
    }
}
