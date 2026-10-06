using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImpossibleRobert.Common;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public sealed class AssetStoreImporter : AssetImporter
    {
        private const string URL_PURCHASES = "https://packages-v2.unity.com/-/api/purchases";
        private const int PAGE_SIZE = 100; // more is not supported by Asset Store
        private const string DIAG_PURCHASES = "Purchases.json";

        private readonly Func<int, string, Task<APIResponse<AssetDetails>>> _fetchDetails;
        private readonly Func<string, Task<APIResponse<AssetPurchases>>> _fetchPurchases;
        private readonly Func<bool> _canRequest;
        internal bool RefreshFailed { get; private set; }

        public AssetStoreImporter() : this((id, etag) => AssetStore.RetrieveAssetDetailsResponse(id, etag),
            uri => AssetStore.FetchStoreAPI<AssetPurchases>(uri), () => AssetStoreAuthentication.Current.CanRequest()) { }

        internal AssetStoreImporter(Func<int, string, Task<APIResponse<AssetDetails>>> fetchDetails,
            Func<string, Task<APIResponse<AssetPurchases>>> fetchPurchases, Func<bool> canRequest)
        {
            _fetchDetails = fetchDetails;
            _fetchPurchases = fetchPurchases;
            _canRequest = canRequest;
        }

        private sealed class SyntyAuthoritySnapshot
        {
            private readonly string _location;
            private readonly string _originalLocation;
            private readonly string _originalLocationKey;
            private readonly string _safeName;
            private readonly string _safePublisher;
            private readonly string _safeCategory;
            private readonly string _slug;
            private readonly string _version;
            private readonly string _license;
            private readonly string _licenseLocation;
            private readonly long _packageSize;
            private readonly Asset.OfficialStateType _officialState;
            private readonly Asset.SubState _currentSubState;

            internal SyntyAuthoritySnapshot(Asset asset)
            {
                _location = asset.Location;
                _originalLocation = asset.OriginalLocation;
                _originalLocationKey = asset.OriginalLocationKey;
                _safeName = asset.SafeName;
                _safePublisher = asset.SafePublisher;
                _safeCategory = asset.SafeCategory;
                _slug = asset.Slug;
                _version = asset.Version;
                _license = asset.License;
                _licenseLocation = asset.LicenseLocation;
                _packageSize = asset.PackageSize;
                _officialState = asset.OfficialState;
                _currentSubState = asset.CurrentSubState;
            }

            internal void Restore(Asset asset)
            {
                asset.AssetSource = Asset.Source.Synty;
                asset.ParentId = 0;
                asset.Location = _location;
                asset.OriginalLocation = _originalLocation;
                asset.OriginalLocationKey = _originalLocationKey;
                asset.SafeName = _safeName;
                asset.SafePublisher = _safePublisher;
                asset.SafeCategory = _safeCategory;
                asset.Slug = _slug;
                asset.Version = _version;
                asset.License = _license;
                asset.LicenseLocation = _licenseLocation;
                asset.PackageSize = _packageSize;
                asset.OfficialState = _officialState;
                asset.CurrentSubState = _currentSubState;
                asset.UploadId = 0;
                asset.Registry = null;
                asset.Repository = null;
                asset.PackageSource = 0;
            }
        }

        private static Asset.OfficialStateType ConvertStateStringToEnum(string state)
        {
            if (string.IsNullOrWhiteSpace(state))
            {
                return Asset.OfficialStateType.None;
            }

            string normalizedState = state.ToLowerInvariant().Trim();
            switch (normalizedState)
            {
                case "published":
                    return Asset.OfficialStateType.Published;
                case "deprecated":
                    return Asset.OfficialStateType.Deprecated;
                case "disabled":
                    return Asset.OfficialStateType.Disabled;
                default:
                    Debug.LogWarning($"Unknown OfficialState value: '{state}'. Defaulting to None.");
                    return Asset.OfficialStateType.None;
            }
        }

        public async Task<AssetPurchases> FetchOnlineAssets()
        {
            AssetPurchases assets = await RetrievePurchases();
            if (assets == null) return null; // happens if token was invalid 

            RestartProgress("Updating purchases");
            MainCount = assets.results.Count;
            MainProgress = 1;

            bool tagsChanged = false;
            try
            {
                // store for later troubleshooting
                File.WriteAllText(Path.Combine(Paths.GetStorageFolder(), DIAG_PURCHASES), JsonConvert.SerializeObject(assets, Formatting.Indented));

                // Process in chunks for optimal performance and editor responsiveness
                int chunkSize = AI.Config.purchaseBatchSize;
                for (int chunkStart = 0; chunkStart < MainCount; chunkStart += chunkSize)
                {
                    int chunkEnd = Math.Min(chunkStart + chunkSize, MainCount);

                    // Wrap each chunk in a database transaction for maximum performance
                    DBAdapter.DB.RunInTransaction(() =>
                    {
                        for (int i = chunkStart; i < chunkEnd; i++)
                        {
                            MainProgress = i + 1;
                            MetaProgress.Report(ProgressId, i + 1, MainCount, string.Empty);
                            if (CancellationRequested) break;

                            AssetPurchase purchase = assets.results[i];

                            // update all known assets with that foreignId to support updating duplicate assets as well 
                            List<Asset> existingAssets = DBAdapter.DB.Table<Asset>().Where(a => a.ForeignId == purchase.packageId && a.AssetSource != Asset.Source.Synty).ToList();
                            PackageIdentityReconciler.PromoteReappearingPurchaseCacheRows(purchase.packageId, existingAssets);
                            if (existingAssets.Count == 0 || existingAssets.Count(a => a.AssetSource == Asset.Source.AssetStorePackage
                                    || (a.AssetSource == Asset.Source.RegistryPackage && a.ForeignId > 0)) == 0)
                            {
                                // create new asset on-demand or if only available as a separate custom asset so far
                                Asset asset = purchase.ToAsset();
                                asset.SafeName = purchase.CalculatedSafeName;
                                if (AI.Config.excludeByDefault) asset.Exclude = true;
                                if (AI.Config.noIndexByDefault) asset.NoIndex = true;
                                if (AI.Config.extractByDefault) asset.KeepExtracted = true;
                                if (AI.Config.captionByDefault) asset.UseAI = true;
                                if (AI.Config.backupByDefault) asset.Backup = true;
                                Persist(asset);
                                existingAssets.Add(asset);
                            }

                            for (int i2 = 0; i2 < existingAssets.Count; i2++)
                            {
                                Asset asset = existingAssets[i2];

                                // temporarily store guessed safe name to ensure locally indexed files are mapped correctly
                                // will be overridden in detail run
                                asset.DisplayName = purchase.displayName.Trim();
                                asset.ForeignId = purchase.packageId;
                                if (!string.IsNullOrEmpty(purchase.grantTime))
                                {
                                    if (DateTime.TryParse(purchase.grantTime, out DateTime result))
                                    {
                                        asset.PurchaseDate = result;
                                    }
                                }
                                if (purchase.isHidden && AI.Config.excludeHidden) asset.Exclude = true;

                                if (string.IsNullOrEmpty(asset.SafeName)) asset.SafeName = purchase.CalculatedSafeName;

                                // override data with local truth in case header information exists
                                if (File.Exists(asset.GetLocation(true)))
                                {
                                    AssetHeader header = UnityPackageImporter.ReadHeader(asset.GetLocation(true), true);
                                    UnityPackageImporter.ApplyHeader(header, asset);
                                }

                                Persist(asset);

                                // handle tags
                                if (purchase.tagging != null)
                                {
                                    foreach (string tag in purchase.tagging)
                                    {
                                        if (tag.ToLowerInvariant() == "#bin") continue;
                                        if (Tagging.AddAssignment(asset.Id, tag, TagAssignment.Target.Package, true)) tagsChanged = true;
                                    }
                                }
                            }
                        }
                    });

                    // Let the editor breathe after each chunk
                    await Task.Yield();
                    if (CancellationRequested) break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not update purchases: {e.Message}");
                return null;
            }

            if (tagsChanged)
            {
                Tagging.LoadTags();
                Tagging.LoadAssignments();
            }

            return CancellationRequested ? null : assets;
        }

        public async Task<bool> FetchAssetsDetails(bool forceUpdate = false, int assetId = 0, bool resetEtag = false)
        {
            List<Asset> assets;
            if (assetId > 0)
            {
                assets = DBAdapter.DB.Table<Asset>()
                    .Where(a => a.Id == assetId && a.ForeignId > 0)
                    .ToList();
            }
            else
            {
                // SQLite.net does not support date subtraction like done here, so we have to do it in memory in a second step
                List<Asset> dbAssets = DBAdapter.DB.Table<Asset>()
                    .Where(a => a.ForeignId > 0)
                    .OrderBy(a => a.LastOnlineRefresh)
                    .ToList();

                assets = dbAssets
                    .Where(a => forceUpdate || (DateTime.Now - a.LastOnlineRefresh).TotalDays >= AI.Config.assetStoreRefreshCycle)
                    .ToList();
            }

            assets = assets
                .Where(a => a.AssetSource != Asset.Source.Synty || AI.Config.syntyLinkAssetStoreMetadata)
                .ToList();
            return await FetchAssetsDetailsInternal(assets, forceUpdate && resetEtag);
        }

        public async Task<bool> FetchAssetsDetails(List<Asset> assets, bool forceUpdate = false, bool resetEtag = false)
        {
            assets = assets?
                .Where(a => a.AssetSource != Asset.Source.Synty || AI.Config.syntyLinkAssetStoreMetadata)
                .ToList() ?? new List<Asset>();

            return await FetchAssetsDetailsInternal(assets, forceUpdate && resetEtag);
        }

        private async Task<bool> FetchAssetsDetailsInternal(List<Asset> assets, bool resetEtag)
        {
            bool requireReload = false;

            string previewFolder = Paths.GetPreviewFolder();

            SemaphoreSlim semaphore = new SemaphoreSlim(AI.Config.maxConcurrentUnityRequests);
            List<Task> tasks = new List<Task>();

            MainCount = assets.Count;
            for (int i = 0; i < assets.Count; i++)
            {
                int assetIndex = i;
                Asset asset = assets[assetIndex];
                int id = asset.ForeignId;
                if (id <= 0) continue;

                SetProgress(asset.DisplayName, i + 1);
                if (i % 5 == 0) await Task.Yield(); // let editor breathe
                if (CancellationRequested) break;

                await semaphore.WaitAsync();
                if (CancellationRequested || !_canRequest())
                {
                    RefreshFailed = true;
                    semaphore.Release();
                    break;
                }

                async Task ProcessAsset(Asset currentAsset, int curAssetId, int currentAssetIndex)
                {
                    try
                    {
                        bool linkedSynty = currentAsset.AssetSource == Asset.Source.Synty;

                        APIResponse<AssetDetails> response = await _fetchDetails(curAssetId, resetEtag ? null : currentAsset.ETag);
                        if (!response.Succeeded)
                        {
                            RefreshFailed = true;
                            return;
                        }
                        AssetDetails details = response.Data;
                        DateTime oldLastUpdate = currentAsset.LastUpdate;
                        currentAsset = DBAdapter.DB.Find<Asset>(a => a.Id == currentAsset.Id); // reload in case it was changed in the meantime
                        if (currentAsset == null) return;
                        linkedSynty = currentAsset.AssetSource == Asset.Source.Synty;
                        if (linkedSynty && (AI.Config == null || !AI.Config.syntyLinkAssetStoreMetadata || currentAsset.ForeignId != curAssetId)) return;
                        SyntyAuthoritySnapshot syntyAuthority = linkedSynty ? new SyntyAuthoritySnapshot(currentAsset) : null;
                        if (response.Status == APIResponseStatus.NotModified)
                        {
                            currentAsset.LastOnlineRefresh = DateTime.Now;
                            DBAdapter.DB.Update(currentAsset);
                            return;
                        }
                        currentAsset.LastUpdate = oldLastUpdate;

                        if (!string.IsNullOrEmpty(details.packageName) && currentAsset.AssetSource != Asset.Source.RegistryPackage && currentAsset.AssetSource != Asset.Source.Synty)
                        {
                            // special case of registry packages listed on asset store
                            // registry package could already exist so make sure to only have one entry
                            Asset existing = DBAdapter.DB.Find<Asset>(a => a.SafeName == details.packageName && a.AssetSource == Asset.Source.RegistryPackage);
                            if (existing != null)
                            {
                                // Keep the stricter participation choice when package identities merge.
                                existing.NoIndex |= HasNoIndex(currentAsset);
                                DBAdapter.DB.Delete(currentAsset);
                                assets[currentAssetIndex] = existing;
                                currentAsset = existing;
                            }
                            currentAsset.AssetSource = Asset.Source.RegistryPackage;
                            currentAsset.SafeName = details.packageName;
                            currentAsset.ForeignId = curAssetId;
                        }

                        // check if disabled, then download links are not available anymore, deprecated would still work
                        DownloadInfo downloadDetails = null;
                        Asset.OfficialStateType assetState = ConvertStateStringToEnum(details.state);
                        if (currentAsset.AssetSource == Asset.Source.AssetStorePackage && assetState != Asset.OfficialStateType.Disabled)
                        {
                            downloadDetails = await AssetStore.RetrieveAssetDownloadInfo(curAssetId, code =>
                            {
                                // if unauthorized then seat was removed again for that user, mark asset as custom
                                if (code == 403 && currentAsset.OfficialState == Asset.OfficialStateType.Published)
                                {
                                    currentAsset.AssetSource = Asset.Source.CustomPackage;
                                    currentAsset.OriginalLocationKey = null;
                                    DBAdapter.DB.Execute("UPDATE Asset set OriginalLocationKey=null, AssetSource=? where Id=?", Asset.Source.CustomPackage, currentAsset.Id);

                                    Debug.Log($"No more access to {currentAsset}. Seat was probably removed. Switching asset source to custom and disabling download possibility.");
                                    requireReload = true;
                                }
                            });
                            if (currentAsset.AssetSource == Asset.Source.AssetStorePackage && (downloadDetails == null || string.IsNullOrEmpty(downloadDetails.filename_safe_package_name)))
                            {
                                RefreshFailed = true;
                                if (_canRequest()) Debug.Log($"Could not fetch download detail information for '{currentAsset.SafeName}'");
                                return;
                            }
                            else if (downloadDetails != null)
                            {
                                if (int.TryParse(downloadDetails.upload_id, out int uploadId)) currentAsset.UploadId = uploadId;
                                currentAsset.SafeName = downloadDetails.filename_safe_package_name;
                                currentAsset.SafeCategory = downloadDetails.filename_safe_category_name;
                                currentAsset.SafePublisher = downloadDetails.filename_safe_publisher_name;
                                currentAsset.OriginalLocation = downloadDetails.url;
                                currentAsset.OriginalLocationKey = downloadDetails.key;
                                if (currentAsset.AssetSource == Asset.Source.AssetStorePackage
                                    && !string.IsNullOrEmpty(currentAsset.GetLocation(true))
                                    && !Paths.AreEquivalentPaths(currentAsset.GetCalculatedLocation(), currentAsset.GetLocation(true)))
                                {
                                    currentAsset.CurrentSubState = Asset.SubState.Outdated;
                                }
                                else
                                {
                                    currentAsset.CurrentSubState = Asset.SubState.None;
                                }
                            }
                        }

                        currentAsset.LastOnlineRefresh = DateTime.Now;
                        currentAsset.OfficialState = ConvertStateStringToEnum(details.state);
                        currentAsset.ETag = details.ETag;
                        currentAsset.DisplayName = details.name;
                        currentAsset.DisplayPublisher = details.productPublisher?.name;
                        currentAsset.DisplayCategory = details.category?.name;
                        if (details.properties != null && details.properties.ContainsKey("firstPublishedDate") && DateTime.TryParse(details.properties["firstPublishedDate"], out DateTime firstPublishedDate))
                        {
                            currentAsset.FirstRelease = firstPublishedDate;
                        }
                        if (int.TryParse(details.publisherId, out int publisherId)) currentAsset.PublisherId = publisherId;

                        // prices
                        if (details.productRatings != null)
                        {
                            NumberStyles style = NumberStyles.Number;
                            CultureInfo culture = CultureInfo.CreateSpecificCulture("en-US");

                            AssetPrice eurPrice = details.productRatings.FirstOrDefault(r => r.currency?.ToLowerInvariant() == "eur");
                            if (eurPrice != null && float.TryParse(AI.Config.showOriginalPrice ? eurPrice.originalPrice : eurPrice.finalPrice, style, culture, out float eur)) currentAsset.PriceEur = eur;
                            AssetPrice usdPrice = details.productRatings.FirstOrDefault(r => r.currency?.ToLowerInvariant() == "usd");
                            if (usdPrice != null && float.TryParse(AI.Config.showOriginalPrice ? usdPrice.originalPrice : usdPrice.finalPrice, style, culture, out float usd)) currentAsset.PriceUsd = usd;
                            AssetPrice yenPrice = details.productRatings.FirstOrDefault(r => r.currency?.ToLowerInvariant() == "cny");
                            if (yenPrice != null && float.TryParse(AI.Config.showOriginalPrice ? yenPrice.originalPrice : yenPrice.finalPrice, style, culture, out float yen)) currentAsset.PriceCny = yen;
                        }

                        if (string.IsNullOrEmpty(currentAsset.SafeName)) currentAsset.SafeName = AssetUtils.GuessSafeName(details.name ?? currentAsset.DisplayName ?? string.Empty);
                        currentAsset.Description = details.description;
                        currentAsset.Requirements = string.Join(", ", details.requirements ?? Array.Empty<string>());
                        currentAsset.Keywords = string.Join(", ", details.keyWords ?? Array.Empty<string>());
                        currentAsset.SupportedUnityVersions = string.Join(", ", details.supportedUnityVersions ?? Array.Empty<string>());
                        currentAsset.Revision = details.revision;
                        currentAsset.Slug = details.slug;
                        currentAsset.LatestVersion = details.version?.name;

                        currentAsset.LastRelease = details.version?.publishedDate ?? details.updatedTime ?? DateTime.MinValue; // can happen for deprecated assets, their version published date will be 0 or empty

                        // store updatedTime here as well since publishedDate is the date of the last upload, which can be way off for deprecated assets
                        // only store newer dates since updatedTime can be older than the update date coming from update API
                        if (details.updatedTime.HasValue && details.updatedTime.Value > currentAsset.LastUpdate) currentAsset.LastUpdate = details.updatedTime.Value;

                        if (details.productReview != null)
                        {
                            if (float.TryParse(details.productReview.ratingAverage, NumberStyles.Float, CultureInfo.InvariantCulture, out float rating)) currentAsset.AssetRating = rating;
                            if (int.TryParse(details.productReview.ratingCount, out int ratingCount)) currentAsset.RatingCount = ratingCount;
                            if (float.TryParse(details.productReview.hotness, NumberStyles.Float, CultureInfo.InvariantCulture, out float hotness)) currentAsset.Hotness = hotness;
                        }

                        currentAsset.CompatibilityInfo = details.compatibilityInfo;
                        currentAsset.ReleaseNotes = details.publishNotes;
                        currentAsset.KeyFeatures = details.keyFeatures;
                        if (details.uploads != null)
                        {
                            // use size of download for latest Unity version, usually good enough approximation
                            KeyValuePair<string, UploadInfo> upload = details.uploads
                                .OrderBy(pair => new SemVer(pair.Key))
                                .LastOrDefault();
                            if (upload.Value != null)
                            {
                                if (currentAsset.PackageSize == 0 && long.TryParse(upload.Value.downloadSize, out long size))
                                {
                                    currentAsset.PackageSize = size;
                                }

                                // store SRP info
                                if (upload.Value.srps != null)
                                {
                                    currentAsset.BIRPCompatible = upload.Value.srps.Contains("standard");
                                    currentAsset.URPCompatible = upload.Value.srps.Contains("lightweight");
                                    currentAsset.HDRPCompatible = upload.Value.srps.Contains("hd");
                                }

                                // parse and prepare dependencies
                                if (upload.Value.dependencies != null && upload.Value.dependencies.Length > 0)
                                {
                                    List<Dependency> deps = new List<Dependency>();
                                    foreach (string link in upload.Value.dependencies)
                                    {
                                        Dependency dep = new Dependency();
                                        dep.location = link;

                                        // try to resolve more information about the dependency
                                        string[] arr = dep.location.Split('-');
                                        if (int.TryParse(arr[arr.Length - 1], out dep.id))
                                        {
                                            AssetDetails depDetails = await AssetStore.RetrieveAssetDetails(dep.id);
                                            if (depDetails != null)
                                            {
                                                dep.name = depDetails.name;
                                            }
                                        }

                                        deps.Add(dep);
                                    }
                                    currentAsset.PackageDependencies = JsonConvert.SerializeObject(deps);
                                }
                                else
                                {
                                    currentAsset.PackageDependencies = null;
                                }
                            }
                        }

                        // linked but not-purchased packages should not contain null for safe_names for search filters to work
                        if (downloadDetails == null && currentAsset.AssetSource == Asset.Source.CustomPackage)
                        {
                            // safe entries must not contain forward slashes due to sub-menu construction
                            if (string.IsNullOrWhiteSpace(currentAsset.SafePublisher)) currentAsset.SafePublisher = AssetUtils.GuessSafeName(currentAsset.DisplayPublisher?.Replace("/", " ") ?? string.Empty);
                            if (string.IsNullOrWhiteSpace(currentAsset.SafeCategory)) currentAsset.SafeCategory = AssetUtils.GuessSafeName(currentAsset.DisplayCategory?.Replace("/", " ") ?? string.Empty);
                        }

                        // override data with local truth in case header information exists
                        if (File.Exists(currentAsset.GetLocation(true)))
                        {
                            AssetHeader header = UnityPackageImporter.ReadHeader(currentAsset.GetLocation(true), true);
                            UnityPackageImporter.ApplyHeader(header, currentAsset);
                        }

                        if (linkedSynty)
                        {
                            syntyAuthority.Restore(currentAsset);
                        }

                        DBAdapter.DB.Update(currentAsset);
                        PersistMedia(currentAsset, details);
                        ApplyOverrides(currentAsset);

                        // load package icon on demand
                        string icon = details.mainImage?.icon;
                        if (!string.IsNullOrWhiteSpace(icon) && string.IsNullOrWhiteSpace(currentAsset.GetPreviewFile(previewFolder)))
                        {
                            _ = AssetUtils.LoadImageAsync(icon, currentAsset.GetPreviewFile(previewFolder, false)).ContinueWith(task =>
                            {
                                if (task.Exception != null)
                                {
                                    Debug.LogError($"Failed to download image from {icon}: {task.Exception.Message}");
                                }
                                else
                                {
                                    AI.TriggerPackageImageRefresh(currentAsset);
                                }
                            }, TaskScheduler.FromCurrentSynchronizationContext());
                        }
                    }
                    catch (Exception e)
                    {
                        RefreshFailed = true;
                        Debug.LogError($"Error fetching asset details for '{currentAsset}': {e.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }

                tasks.Add(ProcessAsset(asset, id, assetIndex));
            }

            // Await all tasks to complete
            await Task.WhenAll(tasks);
            if (CancellationRequested) RefreshFailed = true;
            semaphore.Dispose();

            return requireReload;
        }

        public async Task<List<Asset>> FetchAssetUpdates(bool forceUpdate = false)
        {
            List<Asset> itemsToUpdate = new List<Asset>();

            List<Asset> assets = DBAdapter.DB.Table<Asset>()
                .Where(a => a.ForeignId > 0 && a.ParentId <= 0)
                .OrderBy(a => a.LastOnlineRefresh)
                .ToList();
            assets = assets
                .Where(a => a.AssetSource != Asset.Source.Synty || AI.Config.syntyLinkAssetStoreMetadata)
                .ToList();

            // check only those which are outside of refresh window
            if (!forceUpdate)
            {
                assets = assets
                    .Where(a => (DateTime.Now - a.LastOnlineRefresh).TotalHours >= AI.Config.metadataTimeout)
                    .ToList();
            }

            if (assets.Count == 0) return itemsToUpdate;

            List<Asset> assetsWithUploadId = assets
                .Where(a => a.UploadId > 0)
                .ToList();
            List<Asset> assetsWithoutUploadId = assets
                .Where(a => a.UploadId <= 0)
                .ToList();

            if (assetsWithoutUploadId.Count > 0)
            {
                await FetchAssetsDetails(assetsWithoutUploadId, forceUpdate);
            }

            if (assetsWithUploadId.Count == 0) return itemsToUpdate;
            if (!_canRequest())
            {
                RefreshFailed = true;
                return null;
            }

#if UNITY_6000_3_OR_NEWER
            int chunkSize = 30; // API limit, needs to be lower since otherwise connection throws "unreadable" errors, might be temporary curl issue in Unity alpha
#else
            int chunkSize = 100; // API limit
#endif
            int totalChunks = (int)Math.Ceiling((double)assetsWithUploadId.Count / chunkSize);
            MainCount = totalChunks;
            SetProgress("Fetching Asset Store updates...", 0);

            for (int i = 0; i < totalChunks; i += AI.Config.maxConcurrentUnityRequests)
            {
                if (CancellationRequested || !_canRequest())
                {
                    RefreshFailed = true;
                    break;
                }
                List<Task<(List<Asset> chunk, AssetUpdateResult update)>> currentBatch = new List<Task<(List<Asset>, AssetUpdateResult)>>();

                for (int j = i; j < i + AI.Config.maxConcurrentUnityRequests && j < totalChunks; j++)
                {
                    int skipCount = j * chunkSize;
                    List<Asset> chunk = assetsWithUploadId.Skip(skipCount).Take(chunkSize).ToList();

                    async Task<(List<Asset>, AssetUpdateResult)> ProcessChunk(List<Asset> chunkToProcess)
                    {
                        try
                        {
                            AssetUpdateResult update = await AssetStore.RetrieveAssetUpdates(chunkToProcess);
                            return (chunkToProcess, update);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"Error fetching asset updates: {e.Message}");
                            return (chunkToProcess, null);
                        }
                    }

                    currentBatch.Add(ProcessChunk(chunk));
                }

                (List<Asset> chunk, AssetUpdateResult update)[] batchResults = await Task.WhenAll(currentBatch);
                if (batchResults.Any(r => r.update?.result?.results == null)) RefreshFailed = true;
                if (batchResults.All(r => r.update?.result?.results == null)) return null;

                for (int k = 0; k < batchResults.Length; k++)
                {
                    if (CancellationRequested) break;

                    SetProgress("Fetching Asset Store updates...", i + k + 1);

                    (List<Asset> chunk, AssetUpdateResult update) = batchResults[k];
                    if (update?.result?.results == null) continue;

                    // initialize with all items that were not returned and never fetched
                    List<Asset> chunkUpdates = chunk
                        .Where(a => !update.result.results.Any(r => r.id == a.ForeignId.ToString()))
                        .Where(a => a.OfficialState == Asset.OfficialStateType.None)
                        .ToList();

                    // add items with newer publish date
                    update.result.results.ForEach(r =>
                    {
                        Asset info = chunk.FirstOrDefault(a => a.ForeignId.ToString() == r.id);
                        if (info == null) return;
                        if (string.IsNullOrEmpty(r.published_at))
                        {
                            chunkUpdates.Add(info);
                            return;
                        }

                        if (DateTime.TryParse(r.published_at, out DateTime publishedAt) && (publishedAt - info.LastUpdate).Days > 0)
                        {
                            info.LastUpdate = publishedAt; // set here so we can reuse it later during update since details update date might be older than this one here for some reason
                            chunkUpdates.Add(info);
                        }
                    });

                    itemsToUpdate.AddRange(chunkUpdates);

                    // Update LastOnlineRefresh for items in chunk that are not are already up-to-date
                    HashSet<int> updateIds = new HashSet<int>(chunkUpdates.Select(a => a.Id));
                    List<int> idsToRefresh = chunk.Where(a => !updateIds.Contains(a.Id)).Select(a => a.Id).ToList();

                    if (idsToRefresh.Count > 0)
                    {
                        string idList = string.Join(",", idsToRefresh);
                        DBAdapter.DB.Execute($"UPDATE Asset SET LastOnlineRefresh = ? WHERE Id IN ({idList})", DateTime.Now);
                    }
                }

                if (CancellationRequested) break;
            }

            return itemsToUpdate;
        }

        private async Task<AssetPurchases> RetrievePurchases()
        {
            RestartProgress("Fetching purchases");
            AssetPurchases result = await DoRetrievePurchases();
            if (result == null || CancellationRequested) return null;

            RestartProgress("Fetching hidden purchases");
            AssetPurchases hiddenResult = await DoRetrievePurchases("&status=hidden");
            if (hiddenResult == null || CancellationRequested) return null;

            if (result != null && hiddenResult?.results != null)
            {
                HashSet<int> existingIds = new HashSet<int>(result.results.Select(p => p.packageId));
                result.results.AddRange(hiddenResult.results.Where(p => existingIds.Add(p.packageId)));
            }

            return result;
        }

        private async Task<AssetPurchases> DoRetrievePurchases(string urlSuffix = "")
        {
            MainCount = 1;
            MainProgress = 1;

            if (!_canRequest() || CancellationRequested) return null;
            APIResponse<AssetPurchases> response = await _fetchPurchases($"{URL_PURCHASES}?offset=0&limit={PAGE_SIZE}{urlSuffix}");
            AssetPurchases result = response.Data;
            if (!response.Succeeded || result?.results == null) return null;

            // if more results than page size retrieve rest as well and merge
            // Unity's web client can only run on the main thread
            if (result != null && result.total > PAGE_SIZE)
            {
                int pageCount = AssetUtils.GetPageCount(result.total, PAGE_SIZE) - 1;
                MainCount = pageCount + 1;

                for (int i = 1; i <= pageCount; i += AI.Config.maxConcurrentUnityRequests)
                {
                    if (CancellationRequested || !_canRequest()) return null;
                    List<Task<APIResponse<AssetPurchases>>> currentBatch = new List<Task<APIResponse<AssetPurchases>>>();

                    for (int j = i; j < i + AI.Config.maxConcurrentUnityRequests && j <= pageCount; j++)
                    {
                        int offset = j * PAGE_SIZE;
                        currentBatch.Add(_fetchPurchases($"{URL_PURCHASES}?offset={offset}&limit={PAGE_SIZE}{urlSuffix}"));
                    }
                    APIResponse<AssetPurchases>[] pageResults = await Task.WhenAll(currentBatch);
                    if (CancellationRequested || pageResults.Any(page => !page.Succeeded || page.Data?.results == null)) return null;

                    for (int k = 0; k < pageResults.Length; k++)
                    {
                        MainProgress = i + k + 1;
                        MetaProgress.Report(ProgressId, i + k + 1, pageCount + 1, string.Empty);
                        if (CancellationRequested) break;

                        result.results.AddRange(pageResults[k].Data.results);
                    }
                }
            }
            return !CancellationRequested && result.results.Count == result.total ? result : null;
        }

        private void PersistMedia(Asset asset, AssetDetails details)
        {
            List<AssetMedia> existing = DBAdapter.DB.Query<AssetMedia>("select * from AssetMedia where AssetId=?", asset.Id).ToList();

            // handle main image
            if (!string.IsNullOrWhiteSpace(details.mainImage?.url)) StoreMedia(existing, new AssetMedia {AssetId = asset.Id, Type = "main", Url = details.mainImage.url});
            if (!string.IsNullOrWhiteSpace(details.mainImage?.icon)) StoreMedia(existing, new AssetMedia {AssetId = asset.Id, Type = "icon", Url = details.mainImage.icon});
            if (!string.IsNullOrWhiteSpace(details.mainImage?.icon25)) StoreMedia(existing, new AssetMedia {AssetId = asset.Id, Type = "icon25", Url = details.mainImage.icon25});
            if (!string.IsNullOrWhiteSpace(details.mainImage?.icon75)) StoreMedia(existing, new AssetMedia {AssetId = asset.Id, Type = "icon75", Url = details.mainImage.icon75});
            if (!string.IsNullOrWhiteSpace(details.mainImage?.small)) StoreMedia(existing, new AssetMedia {AssetId = asset.Id, Type = "small", Url = details.mainImage.small});
            if (!string.IsNullOrWhiteSpace(details.mainImage?.small_v2)) StoreMedia(existing, new AssetMedia {AssetId = asset.Id, Type = "small_v2", Url = details.mainImage.small_v2});
            if (!string.IsNullOrWhiteSpace(details.mainImage?.big)) StoreMedia(existing, new AssetMedia {AssetId = asset.Id, Type = "big", Url = details.mainImage.big});
            if (!string.IsNullOrWhiteSpace(details.mainImage?.big_v2)) StoreMedia(existing, new AssetMedia {AssetId = asset.Id, Type = "big_v2", Url = details.mainImage.big_v2});
            if (!string.IsNullOrWhiteSpace(details.mainImage?.facebook)) StoreMedia(existing, new AssetMedia {AssetId = asset.Id, Type = "facebook", Url = details.mainImage.facebook});

            // handle screenshots & videos
            AssetImage[] images = details.images ?? Array.Empty<AssetImage>();
            for (int i = 0; i < images.Length; i++)
            {
                AssetImage img = images[i];
                StoreMedia(existing, new AssetMedia {Order = i, AssetId = asset.Id, Type = img.type, Url = img.imageUrl, ThumbnailUrl = img.thumbnailUrl, Width = img.width, Height = img.height, WebpUrl = img.webpUrl});
            }

            // TODO: remove outdated
        }

        private void StoreMedia(List<AssetMedia> existing, AssetMedia media)
        {
            AssetMedia match = existing.FirstOrDefault(m => m.Type == media.Type && m.Url == media.Url);
            if (match == null)
            {
                DBAdapter.DB.Insert(media);
                existing.Add(media);
            }
            else
            {
                media.Id = match.Id;
                DBAdapter.DB.Update(media);
            }
        }
    }
}
