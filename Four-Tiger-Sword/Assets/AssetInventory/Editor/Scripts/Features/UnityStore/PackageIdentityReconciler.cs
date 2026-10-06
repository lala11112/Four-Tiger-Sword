using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetInventory
{
    internal static class PackageIdentityReconciler
    {
        internal static int PromoteReappearingPurchaseCacheRows(int foreignId, IReadOnlyList<Asset> assets)
        {
            if (foreignId <= 0 || assets == null || assets.Count == 0) return 0;

            bool hasLinkedRegistryPackage = assets.Any(asset =>
                asset.ForeignId == foreignId
                && asset.ParentId == 0
                && asset.AssetSource == Asset.Source.RegistryPackage);
            if (hasLinkedRegistryPackage) return 0;

            List<Asset> promotableAssets = assets
                .Where(asset => asset.AssetSource == Asset.Source.CustomPackage && IsAssetStoreCacheLocation(asset.Location))
                .Where(asset => asset.ForeignId == foreignId && asset.ParentId == 0)
                .ToList();

            foreach (Asset asset in promotableAssets)
            {
                asset.AssetSource = Asset.Source.AssetStorePackage;
                asset.CurrentSubState = Asset.SubState.None;
                DBAdapter.DB.Update(asset);
            }
            return promotableAssets.Count;
        }

        internal static Asset AssignLocationOrMerge(Asset asset, string targetLocation)
        {
            if (asset == null) return null;

            string storedLocation = Paths.MakeRelative(targetLocation);
            if (string.IsNullOrWhiteSpace(storedLocation)) return asset;

            Asset existing = FindSameFilePackage(asset, storedLocation);
            if (existing != null)
            {
                return MergeDuplicateIntoKeeper(existing, asset, true);
            }

            asset.SetLocation(storedLocation);
            DBAdapter.DB.Update(asset);
            return asset;
        }

        internal static Asset LoadPreferredTopLevelAsset(int foreignId)
        {
            if (foreignId <= 0) return null;

            List<Asset> assets = DBAdapter.DB.Table<Asset>()
                .Where(asset => asset.ForeignId == foreignId && asset.ParentId == 0 && asset.AssetSource != Asset.Source.Synty)
                .ToList();

            return assets
                .OrderByDescending(asset => asset.AssetSource == Asset.Source.AssetStorePackage)
                .ThenByDescending(asset => !string.IsNullOrWhiteSpace(asset.Location))
                .ThenByDescending(CountFiles)
                .ThenBy(asset => asset.Id)
                .FirstOrDefault();
        }

        internal static List<List<Asset>> LoadExactDuplicateGroups()
        {
            List<Asset> candidates = DBAdapter.DB.Table<Asset>()
                .Where(asset => asset.ParentId == 0
                                && asset.ForeignId > 0
                                && !string.IsNullOrEmpty(asset.Location)
                                && (asset.AssetSource == Asset.Source.AssetStorePackage || asset.AssetSource == Asset.Source.CustomPackage))
                .ToList();

            return candidates
                .GroupBy(asset => $"{asset.ForeignId}:{NormalizeLocationKey(asset.Location)}")
                .Where(group => group.Count() > 1)
                .Select(group => group.OrderBy(asset => asset.Id).ToList())
                .ToList();
        }

        internal static List<Asset> LoadExactDuplicateIssues()
        {
            return LoadExactDuplicateGroups()
                .SelectMany(group => group)
                .OrderBy(asset => asset.ForeignId)
                .ThenBy(asset => NormalizeLocationKey(asset.Location))
                .ThenBy(asset => asset.Id)
                .ToList();
        }

        internal static int RepairExactDuplicatePackageEntries()
        {
            int merged = 0;
            List<List<Asset>> groups = LoadExactDuplicateGroups();

            foreach (List<Asset> group in groups)
            {
                Asset keeper = ChooseKeeper(group);
                bool promoteToAssetStore = group.Any(asset => asset.AssetSource == Asset.Source.AssetStorePackage);

                foreach (Asset duplicate in group.Where(asset => asset.Id != keeper.Id).OrderBy(asset => asset.Id))
                {
                    Asset latestKeeper = DBAdapter.DB.Find<Asset>(keeper.Id);
                    Asset latestDuplicate = DBAdapter.DB.Find<Asset>(duplicate.Id);
                    if (latestKeeper == null || latestDuplicate == null) continue;

                    keeper = MergeDuplicateIntoKeeper(latestKeeper, latestDuplicate, promoteToAssetStore);
                    merged++;
                }
            }

            return merged;
        }

        internal static Asset MergeDuplicateIntoKeeper(Asset keeper, Asset duplicate, bool promoteToAssetStore)
        {
            if (keeper == null) return duplicate;
            if (duplicate == null || keeper.Id == duplicate.Id) return keeper;

            DBAdapter.DB.RunInTransaction(() => MergeDuplicateIntoKeeperCore(keeper, duplicate, promoteToAssetStore));

            return DBAdapter.DB.Find<Asset>(keeper.Id);
        }

        private static void MergeDuplicateIntoKeeperCore(Asset keeper, Asset duplicate, bool promoteToAssetStore)
        {
            MergeAssetFields(keeper, duplicate, promoteToAssetStore);
            DBAdapter.DB.Update(keeper);

            MergePackageAssignments(duplicate.Id, keeper.Id);
            MergeAssetFiles(duplicate.Id, keeper.Id);
            MergeAssetMedia(duplicate.Id, keeper.Id);
            ReparentChildren(duplicate, keeper);

            DBAdapter.DB.Delete<Asset>(duplicate.Id);
        }

        private static Asset FindSameFilePackage(Asset asset, string storedLocation)
        {
            if (asset.ForeignId <= 0) return null;

            List<Asset> candidates = DBAdapter.DB.Table<Asset>()
                .Where(candidate => candidate.ForeignId == asset.ForeignId
                                    && candidate.ParentId == asset.ParentId
                                    && candidate.Id != asset.Id
                                    && !string.IsNullOrEmpty(candidate.Location)
                                    && (candidate.AssetSource == Asset.Source.AssetStorePackage || candidate.AssetSource == Asset.Source.CustomPackage))
                .ToList();

            return candidates
                .Where(candidate => AreSameLocation(candidate.Location, storedLocation))
                .OrderByDescending(CountFiles)
                .ThenBy(candidate => candidate.Id)
                .FirstOrDefault();
        }

        private static Asset ChooseKeeper(List<Asset> duplicates)
        {
            return duplicates
                .OrderByDescending(CountFiles)
                .ThenByDescending(asset => !string.IsNullOrWhiteSpace(asset.Location))
                .ThenBy(asset => asset.Id)
                .First();
        }

        private static bool IsAssetStoreCacheLocation(string location)
        {
            if (string.IsNullOrWhiteSpace(location)) return false;

            string assetCachePrefix = $"{AI.TAG_START}ac{AI.TAG_END}";
            if (location.StartsWith(assetCachePrefix, StringComparison.OrdinalIgnoreCase)) return true;

            string expanded = Paths.DeRel(location, true) ?? location;
            string cacheFolder = Paths.GetAssetCacheFolder();
            if (string.IsNullOrWhiteSpace(expanded) || string.IsNullOrWhiteSpace(cacheFolder)) return false;

            string normalizedExpanded = NormalizeLocationKey(expanded);
            string normalizedCacheFolder = NormalizeLocationKey(cacheFolder);

            return normalizedExpanded == normalizedCacheFolder
                   || normalizedExpanded.StartsWith(normalizedCacheFolder + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool AreSameLocation(string left, string right)
        {
            return string.Equals(NormalizeLocationKey(left), NormalizeLocationKey(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeLocationKey(string location)
        {
            if (string.IsNullOrWhiteSpace(location)) return string.Empty;

            string expanded = Paths.DeRel(location, true) ?? location;
            return expanded.Replace("\\", "/").TrimEnd('/').ToLowerInvariant();
        }

        private static int CountFiles(Asset asset)
        {
            if (asset == null || asset.Id <= 0) return 0;
            return DBAdapter.DB.ExecuteScalar<int>("select count(*) from AssetFile where AssetId=?", asset.Id);
        }

        private static void MergeAssetFields(Asset keeper, Asset duplicate, bool promoteToAssetStore)
        {
            if (promoteToAssetStore) keeper.AssetSource = Asset.Source.AssetStorePackage;

            keeper.Exclude = keeper.Exclude || duplicate.Exclude;
            keeper.NoIndex = keeper.NoIndex || duplicate.NoIndex;
            keeper.Backup = keeper.Backup || duplicate.Backup;
            keeper.KeepExtracted = keeper.KeepExtracted || duplicate.KeepExtracted;
            keeper.UseAI = keeper.UseAI || duplicate.UseAI;
            keeper.UseSemanticIndex = MergeNullableFlag(keeper.UseSemanticIndex, duplicate.UseSemanticIndex);
            keeper.UseCodeIndex = MergeNullableFlag(keeper.UseCodeIndex, duplicate.UseCodeIndex);

            keeper.SafeName = PreferExisting(keeper.SafeName, duplicate.SafeName);
            keeper.DisplayName = PreferExisting(keeper.DisplayName, duplicate.DisplayName);
            keeper.SafePublisher = PreferExisting(keeper.SafePublisher, duplicate.SafePublisher);
            keeper.DisplayPublisher = PreferExisting(keeper.DisplayPublisher, duplicate.DisplayPublisher);
            keeper.SafeCategory = PreferExisting(keeper.SafeCategory, duplicate.SafeCategory);
            keeper.DisplayCategory = PreferExisting(keeper.DisplayCategory, duplicate.DisplayCategory);
            keeper.Description = PreferExisting(keeper.Description, duplicate.Description);
            keeper.KeyFeatures = PreferExisting(keeper.KeyFeatures, duplicate.KeyFeatures);
            keeper.CompatibilityInfo = PreferExisting(keeper.CompatibilityInfo, duplicate.CompatibilityInfo);
            keeper.SupportedUnityVersions = PreferExisting(keeper.SupportedUnityVersions, duplicate.SupportedUnityVersions);
            keeper.Keywords = PreferExisting(keeper.Keywords, duplicate.Keywords);
            keeper.PackageDependencies = PreferExisting(keeper.PackageDependencies, duplicate.PackageDependencies);
            keeper.Version = PreferExisting(keeper.Version, duplicate.Version);
            keeper.LatestVersion = PreferExisting(keeper.LatestVersion, duplicate.LatestVersion);
            keeper.License = PreferExisting(keeper.License, duplicate.License);
            keeper.LicenseLocation = PreferExisting(keeper.LicenseLocation, duplicate.LicenseLocation);
            keeper.Requirements = PreferExisting(keeper.Requirements, duplicate.Requirements);
            keeper.ReleaseNotes = PreferExisting(keeper.ReleaseNotes, duplicate.ReleaseNotes);
            keeper.OriginalLocation = PreferExisting(keeper.OriginalLocation, duplicate.OriginalLocation);
            keeper.OriginalLocationKey = PreferExisting(keeper.OriginalLocationKey, duplicate.OriginalLocationKey);
            keeper.Registry = PreferExisting(keeper.Registry, duplicate.Registry);
            keeper.Repository = PreferExisting(keeper.Repository, duplicate.Repository);
            keeper.Slug = PreferExisting(keeper.Slug, duplicate.Slug);
            keeper.ETag = PreferExisting(keeper.ETag, duplicate.ETag);

            if (duplicate.AssetSource == Asset.Source.AssetStorePackage)
            {
                CopyOnlineFields(keeper, duplicate);
            }

            if (keeper.PackageSize <= 0) keeper.PackageSize = duplicate.PackageSize;
            if (keeper.PublisherId <= 0) keeper.PublisherId = duplicate.PublisherId;
            if (keeper.Revision <= 0) keeper.Revision = duplicate.Revision;
            if (keeper.UploadId <= 0) keeper.UploadId = duplicate.UploadId;
            if (keeper.PurchaseDate == DateTime.MinValue) keeper.PurchaseDate = duplicate.PurchaseDate;
            if (keeper.FirstRelease == DateTime.MinValue) keeper.FirstRelease = duplicate.FirstRelease;
            if (keeper.LastRelease == DateTime.MinValue) keeper.LastRelease = duplicate.LastRelease;
            if (duplicate.LastUpdate > keeper.LastUpdate) keeper.LastUpdate = duplicate.LastUpdate;
            if (duplicate.LastOnlineRefresh > keeper.LastOnlineRefresh) keeper.LastOnlineRefresh = duplicate.LastOnlineRefresh;
            if (keeper.AssetRating <= 0f) keeper.AssetRating = duplicate.AssetRating;
            if (keeper.RatingCount <= 0) keeper.RatingCount = duplicate.RatingCount;
            if (keeper.Hotness <= 0f) keeper.Hotness = duplicate.Hotness;
            if (keeper.PriceEur <= 0f) keeper.PriceEur = duplicate.PriceEur;
            if (keeper.PriceUsd <= 0f) keeper.PriceUsd = duplicate.PriceUsd;
            if (keeper.PriceCny <= 0f) keeper.PriceCny = duplicate.PriceCny;
            if (keeper.OfficialState == Asset.OfficialStateType.None) keeper.OfficialState = duplicate.OfficialState;
            if (!keeper.BIRPCompatible) keeper.BIRPCompatible = duplicate.BIRPCompatible;
            if (!keeper.URPCompatible) keeper.URPCompatible = duplicate.URPCompatible;
            if (!keeper.HDRPCompatible) keeper.HDRPCompatible = duplicate.HDRPCompatible;

            keeper.CurrentSubState = Asset.SubState.None;
        }

        private static bool? MergeNullableFlag(bool? keeper, bool? duplicate)
        {
            if (keeper == false || duplicate == false) return false;
            if (keeper == true || duplicate == true) return true;
            return null;
        }

        private static void CopyOnlineFields(Asset keeper, Asset duplicate)
        {
            keeper.DisplayName = PreferIncoming(keeper.DisplayName, duplicate.DisplayName);
            keeper.DisplayPublisher = PreferIncoming(keeper.DisplayPublisher, duplicate.DisplayPublisher);
            keeper.DisplayCategory = PreferIncoming(keeper.DisplayCategory, duplicate.DisplayCategory);
            keeper.Description = PreferIncoming(keeper.Description, duplicate.Description);
            keeper.KeyFeatures = PreferIncoming(keeper.KeyFeatures, duplicate.KeyFeatures);
            keeper.CompatibilityInfo = PreferIncoming(keeper.CompatibilityInfo, duplicate.CompatibilityInfo);
            keeper.SupportedUnityVersions = PreferIncoming(keeper.SupportedUnityVersions, duplicate.SupportedUnityVersions);
            keeper.Keywords = PreferIncoming(keeper.Keywords, duplicate.Keywords);
            keeper.PackageDependencies = PreferIncoming(keeper.PackageDependencies, duplicate.PackageDependencies);
            keeper.LatestVersion = PreferIncoming(keeper.LatestVersion, duplicate.LatestVersion);
            keeper.Requirements = PreferIncoming(keeper.Requirements, duplicate.Requirements);
            keeper.ReleaseNotes = PreferIncoming(keeper.ReleaseNotes, duplicate.ReleaseNotes);
            keeper.OriginalLocation = PreferIncoming(keeper.OriginalLocation, duplicate.OriginalLocation);
            keeper.OriginalLocationKey = PreferIncoming(keeper.OriginalLocationKey, duplicate.OriginalLocationKey);
            keeper.Slug = PreferIncoming(keeper.Slug, duplicate.Slug);
            keeper.ETag = PreferIncoming(keeper.ETag, duplicate.ETag);

            if (duplicate.PublisherId > 0) keeper.PublisherId = duplicate.PublisherId;
            if (duplicate.Revision > 0) keeper.Revision = duplicate.Revision;
            if (duplicate.UploadId > 0) keeper.UploadId = duplicate.UploadId;
            if (duplicate.OfficialState != Asset.OfficialStateType.None) keeper.OfficialState = duplicate.OfficialState;
        }

        private static string PreferExisting(string keeperValue, string duplicateValue)
        {
            return string.IsNullOrWhiteSpace(keeperValue) && !string.IsNullOrWhiteSpace(duplicateValue)
                ? duplicateValue
                : keeperValue;
        }

        private static string PreferIncoming(string keeperValue, string duplicateValue)
        {
            return string.IsNullOrWhiteSpace(duplicateValue) ? keeperValue : duplicateValue;
        }

        private static void MergePackageAssignments(int duplicateAssetId, int keeperAssetId)
        {
            MergeTagAssignments(TagAssignment.Target.Package, duplicateAssetId, keeperAssetId);
            MergeMetadataAssignments(MetadataAssignment.Target.Package, duplicateAssetId, keeperAssetId);
        }

        private static void MergeAssetFiles(int duplicateAssetId, int keeperAssetId)
        {
            List<AssetFile> keeperFiles = DBAdapter.DB.Table<AssetFile>().Where(file => file.AssetId == keeperAssetId).ToList();
            List<AssetFile> duplicateFiles = DBAdapter.DB.Table<AssetFile>().Where(file => file.AssetId == duplicateAssetId).ToList();
            bool changedFiles = false;

            foreach (AssetFile duplicateFile in duplicateFiles)
            {
                AssetFile existing = FindMatchingFile(keeperFiles, duplicateFile);
                if (existing != null)
                {
                    MergeFileFields(existing, duplicateFile);
                    MergeTagAssignments(TagAssignment.Target.Asset, duplicateFile.Id, existing.Id);
                    MergeMetadataAssignments(MetadataAssignment.Target.Asset, duplicateFile.Id, existing.Id);
                    DBAdapter.DB.Delete<AssetFile>(duplicateFile.Id);
                    changedFiles = true;
                    continue;
                }

                duplicateFile.AssetId = keeperAssetId;
                if (duplicateFile.PreviewState != AssetFile.PreviewOptions.UseOriginal)
                {
                    duplicateFile.PreviewState = AssetFile.PreviewOptions.RedoMissing;
                }
                DBAdapter.DB.Update(duplicateFile);
                keeperFiles.Add(duplicateFile);
                changedFiles = true;
            }

            if (changedFiles)
            {
                DBAdapter.DB.Execute("update Asset set CurrentState=? where Id=?", Asset.State.InProcess, keeperAssetId);
            }
        }

        private static AssetFile FindMatchingFile(List<AssetFile> keeperFiles, AssetFile duplicateFile)
        {
            if (!string.IsNullOrWhiteSpace(duplicateFile.Guid))
            {
                AssetFile byGuid = keeperFiles.FirstOrDefault(file => string.Equals(file.Guid, duplicateFile.Guid, StringComparison.Ordinal));
                if (byGuid != null) return byGuid;
            }

            return keeperFiles.FirstOrDefault(file => string.Equals(file.Path, duplicateFile.Path, StringComparison.OrdinalIgnoreCase));
        }

        private static void MergeFileFields(AssetFile keeper, AssetFile duplicate)
        {
            keeper.SourcePath = PreferExisting(keeper.SourcePath, duplicate.SourcePath);
            keeper.FileVersion = PreferExisting(keeper.FileVersion, duplicate.FileVersion);
            keeper.FileStatus = PreferExisting(keeper.FileStatus, duplicate.FileStatus);
            keeper.AICaption = PreferExisting(keeper.AICaption, duplicate.AICaption);
            keeper.FileData = PreferExisting(keeper.FileData, duplicate.FileData);

            if (keeper.Size <= 0) keeper.Size = duplicate.Size;
            if (keeper.Width <= 0) keeper.Width = duplicate.Width;
            if (keeper.Height <= 0) keeper.Height = duplicate.Height;
            if (keeper.Length <= 0f) keeper.Length = duplicate.Length;
            if (keeper.Hue < 0f) keeper.Hue = duplicate.Hue;
            if (keeper.PreviewState == AssetFile.PreviewOptions.None) keeper.PreviewState = duplicate.PreviewState;
            keeper.Hidden = keeper.Hidden || duplicate.Hidden;

            DBAdapter.DB.Update(keeper);
        }

        private static void MergeAssetMedia(int duplicateAssetId, int keeperAssetId)
        {
            List<AssetMedia> keeperMedia = DBAdapter.DB.Table<AssetMedia>().Where(media => media.AssetId == keeperAssetId).ToList();
            List<AssetMedia> duplicateMedia = DBAdapter.DB.Table<AssetMedia>().Where(media => media.AssetId == duplicateAssetId).ToList();

            foreach (AssetMedia duplicate in duplicateMedia)
            {
                bool exists = keeperMedia.Any(media =>
                    string.Equals(media.Type, duplicate.Type, StringComparison.Ordinal)
                    && string.Equals(media.Url, duplicate.Url, StringComparison.Ordinal)
                    && string.Equals(media.ThumbnailUrl, duplicate.ThumbnailUrl, StringComparison.Ordinal));

                if (exists)
                {
                    DBAdapter.DB.Delete<AssetMedia>(duplicate.Id);
                    continue;
                }

                duplicate.AssetId = keeperAssetId;
                DBAdapter.DB.Update(duplicate);
                keeperMedia.Add(duplicate);
            }
        }

        private static void ReparentChildren(Asset duplicate, Asset keeper)
        {
            List<Asset> duplicateChildren = DBAdapter.DB.Table<Asset>().Where(asset => asset.ParentId == duplicate.Id).ToList();
            foreach (Asset child in duplicateChildren)
            {
                string reparentedLocation = ReparentChildLocation(child.Location, duplicate.Location, keeper.Location);
                Asset existingChild = DBAdapter.DB.Table<Asset>()
                    .Where(asset => asset.ParentId == keeper.Id && asset.ForeignId == child.ForeignId)
                    .ToList()
                    .FirstOrDefault(asset => AreSameLocation(asset.Location, reparentedLocation));

                if (existingChild != null)
                {
                    MergeDuplicateIntoKeeperCore(existingChild, child, false);
                    continue;
                }

                child.ParentId = keeper.Id;
                child.Location = reparentedLocation;
                DBAdapter.DB.Update(child);
            }
        }

        private static string ReparentChildLocation(string childLocation, string duplicateLocation, string keeperLocation)
        {
            if (string.IsNullOrWhiteSpace(childLocation) || string.IsNullOrWhiteSpace(duplicateLocation) || string.IsNullOrWhiteSpace(keeperLocation)) return childLocation;
            if (!childLocation.StartsWith(duplicateLocation + Asset.SUB_PATH, StringComparison.OrdinalIgnoreCase)) return childLocation;

            return keeperLocation + childLocation.Substring(duplicateLocation.Length);
        }

        private static void MergeTagAssignments(TagAssignment.Target target, int duplicateTargetId, int keeperTargetId)
        {
            List<TagAssignment> keeperAssignments = DBAdapter.DB.Table<TagAssignment>()
                .Where(assignment => assignment.TagTarget == target && assignment.TargetId == keeperTargetId)
                .ToList();
            HashSet<int> keeperTagIds = keeperAssignments.Select(assignment => assignment.TagId).ToHashSet();

            List<TagAssignment> duplicateAssignments = DBAdapter.DB.Table<TagAssignment>()
                .Where(assignment => assignment.TagTarget == target && assignment.TargetId == duplicateTargetId)
                .ToList();

            foreach (TagAssignment duplicate in duplicateAssignments)
            {
                if (keeperTagIds.Contains(duplicate.TagId))
                {
                    DBAdapter.DB.Delete<TagAssignment>(duplicate.Id);
                    continue;
                }

                duplicate.TargetId = keeperTargetId;
                DBAdapter.DB.Update(duplicate);
                keeperTagIds.Add(duplicate.TagId);
            }
        }

        private static void MergeMetadataAssignments(MetadataAssignment.Target target, int duplicateTargetId, int keeperTargetId)
        {
            List<MetadataAssignment> keeperAssignments = DBAdapter.DB.Table<MetadataAssignment>()
                .Where(assignment => assignment.MetadataTarget == target && assignment.TargetId == keeperTargetId)
                .ToList();
            HashSet<int> keeperMetadataIds = keeperAssignments.Select(assignment => assignment.MetadataId).ToHashSet();

            List<MetadataAssignment> duplicateAssignments = DBAdapter.DB.Table<MetadataAssignment>()
                .Where(assignment => assignment.MetadataTarget == target && assignment.TargetId == duplicateTargetId)
                .ToList();

            foreach (MetadataAssignment duplicate in duplicateAssignments)
            {
                if (keeperMetadataIds.Contains(duplicate.MetadataId))
                {
                    DBAdapter.DB.Delete<MetadataAssignment>(duplicate.Id);
                    continue;
                }

                duplicate.TargetId = keeperTargetId;
                DBAdapter.DB.Update(duplicate);
                keeperMetadataIds.Add(duplicate.MetadataId);
            }
        }
    }
}
