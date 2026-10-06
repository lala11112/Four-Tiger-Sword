using ImpossibleRobert.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor.PackageManager;
using UnityEngine;

namespace AssetInventory
{
    /// <summary>Importer for discovering, indexing, and persisting package content in the Asset Inventory catalog.</summary>
    public sealed class PackageImporter : AssetImporter
    {
        private const float MAX_META_DATA_WAIT_TIME = 30f;
        private const int BREAK_INTERVAL = 50;

        /// <summary>Reads a package.json file and creates or refreshes the corresponding registry-package catalog record.</summary>
        public async Task IndexRough(string path, bool fromAssetStore)
        {
            // pass 1: find latest cached packages
            string[] packages = await Task.Run(() => Directory.GetFiles(path, "package.json", SearchOption.AllDirectories));

            bool tagsChanged = false;
            MainCount = packages.Length;
            for (int i = 0; i < packages.Length; i++)
            {
                if (CancellationRequested) break;

                string package = packages[i].Replace("\\", "/");
                if (i % BREAK_INTERVAL == 0) await Task.Yield(); // let editor breath

                Package info = ReadPackageFile(package);
                if (info == null) continue;

                // create asset
                Asset asset = await CreateAsset(info, package, PackageSource.Unknown);
                if (asset == null) continue;

                // update progress only if really doing work to save refresh time in UI
                SetProgress($"{info.name} - {info.version}", i + 1);

                // handle tags
                tagsChanged = tagsChanged || ApplyTags(asset, info, fromAssetStore);

                // registry
                float maxWaitTime = Time.realtimeSinceStartup + MAX_META_DATA_WAIT_TIME;
                while (!AssetStore.IsMetadataAvailable() && Time.realtimeSinceStartup < maxWaitTime) await Task.Delay(25);
                if (AssetStore.IsMetadataAvailable())
                {
                    PackageInfo resolved = AssetStore.GetPackageInfo(info.name);
                    if (resolved != null) asset.CopyFrom(resolved);
                }

                asset.CurrentState = Asset.State.InProcess;
                UpdateOrInsert(asset);
            }

            // pass 2: check for project packages which are not cached, e.g. git packages
            if (!CancellationRequested)
            {
                RestartProgress("Discovering additional packages");
                Dictionary<string, PackageInfo> packageCollection = AssetStore.GetProjectPackages();
                if (packageCollection != null)
                {
                    List<PackageInfo> projectPackages = packageCollection.Values.ToList();
                    MainCount = projectPackages.Count;
                    for (int i = 0; i < projectPackages.Count; i++)
                    {
                        if (CancellationRequested) break;

                        PackageInfo package = projectPackages[i];
                        if (package.source == PackageSource.BuiltIn) continue;

                        if (i % BREAK_INTERVAL == 0) await Task.Yield(); // let editor breath

                        // create asset
                        Asset asset = new Asset(package);

                        // skip unchanged or older 
                        Asset existing = Fetch(asset);
                        if (existing != null)
                        {
                            if (new SemVer(existing.Version) >= new SemVer(asset.Version)) continue;
                            asset = existing.CopyFrom(package);
                        }
                        else
                        {
                            if (AI.Config.excludeByDefault) asset.Exclude = true;
                            if (AI.Config.noIndexByDefault) asset.NoIndex = true;
                            if (AI.Config.extractByDefault) asset.KeepExtracted = true;
                            if (AI.Config.captionByDefault) asset.UseAI = true;
                            if (AI.Config.backupByDefault) asset.Backup = true;
                        }

                        // update progress only if really doing work to save refresh time in UI
                        SetProgress($"{asset.SafeName} - {asset.Version}", i + 1);

                        if (!string.IsNullOrWhiteSpace(asset.Location)) asset.PackageSize = await IOUtils.GetFolderSize(asset.Location);

                        asset.CurrentState = Asset.State.InProcess;
                        UpdateOrInsert(asset);
                    }
                }
                else
                {
                    Debug.LogWarning("Could not retrieve list of project packages to scan.");
                }
            }

            if (tagsChanged)
            {
                Tagging.LoadTags();
                Tagging.LoadAssignments();
            }
        }

        /// <summary>Applies package-manager keywords and configured source tags to the supplied catalog package.</summary>
        public static bool ApplyTags(Asset asset, Package info, bool fromAssetStore)
        {
            bool tagsChanged = false;
            if (AI.Config.importPackageKeywordsAsTags && info.keywords != null)
            {
                foreach (string tag in info.keywords)
                {
                    if (Tagging.AddAssignment(asset.Id, tag, TagAssignment.Target.Package, fromAssetStore)) tagsChanged = true;
                }
            }
            return tagsChanged;
        }

        /// <summary>Creates and persists a catalog package record from parsed registry package metadata and its package source.</summary>
        public static async Task<Asset> CreateAsset(Package info, string package, PackageSource source)
        {
            Asset asset = new Asset(info);
            asset.PackageSource = source;
            asset.SetLocation(Path.GetDirectoryName(package));

            // skip unchanged or older 
            Asset existing = Fetch(asset);
            if (existing != null)
            {
                if (existing.CurrentState == Asset.State.Done && new SemVer(existing.Version) >= new SemVer(asset.Version)) return null;
                asset = existing.CopyFrom(info);
            }
            else
            {
                if (AI.Config.excludeByDefault) asset.Exclude = true;
                if (AI.Config.noIndexByDefault) asset.NoIndex = true;
                if (AI.Config.extractByDefault) asset.KeepExtracted = true;
                if (AI.Config.captionByDefault) asset.UseAI = true;
                if (AI.Config.backupByDefault) asset.Backup = true;
            }

            asset.PackageSize = await IOUtils.GetFolderSize(asset.Location);

            return asset;
        }

        /// <summary>Refreshes dependency, sample, repository, author, version, and file metadata for one or all registry packages.</summary>
        public async Task IndexDetails(int assetId = 0)
        {
            FolderSpec importSpec = GetDefaultImportSpec();

            List<Asset> assets;
            if (assetId == 0)
            {
                assets = DBAdapter.DB.Table<Asset>().Where(a => a.AssetSource == Asset.Source.RegistryPackage && a.CurrentState != Asset.State.Done).ToList();
            }
            else
            {
                assets = DBAdapter.DB.Table<Asset>().Where(a => a.Id == assetId && a.AssetSource == Asset.Source.RegistryPackage).ToList();
            }

            MainCount = assets.Count;
            for (int i = 0; i < assets.Count; i++)
            {
                Asset asset = assets[i];
                if (CancellationRequested) break;

                // Skip packages marked as no-index but mark as done
                if (HasNoIndex(asset))
                {
                    MarkDone(asset);
                    continue;
                }

                SetProgress($"{asset.SafeName} - {asset.Version}", i + 1);

                // TODO: factually incorrect as indexed version does not need to correspond to latest version
                if (Directory.Exists(asset.GetLocation(true)))
                {
                    // preserve hidden paths before removing old files
                    List<string> hiddenPaths = Assets.GetHiddenPaths(asset.Id);

                    // remove old files
                    DBAdapter.DB.Execute("delete from AssetFile where AssetId=?", asset.Id);

                    importSpec.location = asset.GetLocation(true);

                    await AI.Actions.RunWithProgress<MediaImporter>(
                        ActionHandler.ACTION_MEDIA_FOLDERS_INDEX,
                        "Updating files index",
                        imp => imp.Index(importSpec, asset, false, true));

                    // restore hidden paths and apply metadata patterns
                    Assets.RestoreHiddenPaths(asset.Id, hiddenPaths);
                    Assets.ApplyHidePatterns(asset.Id);
                }
                if (CancellationRequested) break; // do not mark done otherwise

                MarkDone(asset);
            }
        }

        /// <summary>Parses package.json from the supplied package directory, returning null when the descriptor cannot be read.</summary>
        public static Package ReadPackageFile(string package)
        {
            Package info;
            try
            {
                // Use ReadAllTextWithShare to avoid locking package.json files in Unity cache
                info = JsonConvert.DeserializeObject<Package>(IOUtils.ReadAllTextWithShare(package), new JsonSerializerSettings
                {
                    Error = (_, error) =>
                    {
                        if (AI.Config.LogPackageParsing)
                        {
                            Debug.Log($"Field inside package manifest '{package}' is malformed. This data will be ignored: {error.ErrorContext.Path}");
                        }
                        error.ErrorContext.Handled = true;
                    }
                });
            }
            catch (Exception e)
            {
                Debug.LogError($"Package manifest inside '{package}' is malformed and could not be read: {e.Message}");
                return null;
            }
            if (info == null)
            {
                Debug.LogError($"Could not read package manifest: {package}");
                return null;
            }

            return info;
        }

        /// <summary>Creates or updates the catalog record for an installed Unity Package Manager package.</summary>
        public static bool Persist(PackageInfo package)
        {
            Asset asset = new Asset(package);
            Asset existing = Fetch(asset);
            if (existing != null) return false;

            Persist(asset);

            return true;
        }
    }
}
