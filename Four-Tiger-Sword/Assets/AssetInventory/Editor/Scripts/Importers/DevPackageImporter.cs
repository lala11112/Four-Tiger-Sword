using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEditor.PackageManager;

namespace AssetInventory
{
    /// <summary>Importer for discovering, indexing, and persisting dev package content in the Asset Inventory catalog.</summary>
    public sealed class DevPackageImporter : AssetImporter
    {
        private const int BREAK_INTERVAL = 30;

        /// <summary>Indexes a Unity package development folder using the supplied source rules.</summary>
        public async Task Index(FolderSpec spec)
        {
            if (string.IsNullOrEmpty(spec.location)) return;

            string fullLocation = spec.GetLocation(true);
            bool treatAsUnityProject = spec.detectUnityProjects && AssetUtils.IsUnityProject(fullLocation);
            string scanPath = treatAsUnityProject ? Path.Combine(fullLocation, "Assets") : fullLocation;
            string[] files = IOUtils.GetFiles(scanPath, new[] {"package.json"}, SearchOption.AllDirectories).ToArray();
            string[] excludedDirectories = StringUtils.Split(spec.excludedDirectories, new[] {';', ','});

            MainCount = files.Length;
            MainProgress = 1; // small hack to trigger UI update in the end

            for (int i = 0; i < files.Length; i++)
            {
                if (CancellationRequested) break;
                if (i % BREAK_INTERVAL == 0) await Task.Yield(); // let editor breath in case many files are already indexed
                await AI.Cooldown.Do();

                string package = files[i];
                if (IsIgnoredPathBelowScanRoot(package, fullLocation)) continue;
                if (IsExcludedDirectory(package, excludedDirectories)) continue;
                Asset asset = await HandlePackage(package);
                if (asset == null) continue;

                MetaProgress.Report(ProgressId, i + 1, files.Length, package);
                MainCount = files.Length;
                CurrentMain = asset.DisplayName + " (" + EditorUtility.FormatBytes(asset.PackageSize) + ")";
                MainProgress = i + 1;

                // Skip packages marked as no-index but mark as done
                if (HasNoIndex(asset))
                {
                    MarkDone(asset);
                    ApplyPackageTags(spec, asset);
                    continue;
                }

                await Task.Yield();
                await IndexPackage(asset, spec);
                await Task.Yield();

                if (CancellationRequested) break;

                ApplyPackageTags(spec, asset);
            }
        }

        private async Task<Asset> HandlePackage(string package)
        {
            Package info = PackageImporter.ReadPackageFile(package);
            if (info == null) return null;

            // create asset
            Asset asset = await PackageImporter.CreateAsset(info, package, PackageSource.Local);
            if (asset == null) return null;

            // handle tags
            bool tagsChanged = PackageImporter.ApplyTags(asset, info, false);

            asset.CurrentState = Asset.State.InProcess;
            UpdateOrInsert(asset);

            if (tagsChanged)
            {
                Tagging.LoadTags();
                Tagging.LoadAssignments();
            }

            return asset;
        }

        /// <summary>Indexes the files and package metadata of the supplied development-package record.</summary>
        public async Task IndexDetails(Asset asset)
        {
            MainCount = 1;
            CurrentMain = "Indexing dev package";

            FolderSpec importSpec = GetDefaultImportSpec();
            importSpec.createPreviews = true; // TODO: derive from additional folder settings
            await IndexPackage(asset, importSpec);
        }

        private async Task IndexPackage(Asset asset, FolderSpec spec)
        {
            FolderSpec importSpec = GetDefaultImportSpec();
            importSpec.location = asset.GetLocation(true);
            importSpec.createPreviews = spec.createPreviews;

            await AI.Actions.RunWithProgress<MediaImporter>(
                ActionHandler.ACTION_MEDIA_FOLDERS_INDEX,
                "Updating files index",
                imp => imp.Index(importSpec, asset, false, true));

            if (!CancellationRequested) MarkDone(asset);
        }
    }
}
