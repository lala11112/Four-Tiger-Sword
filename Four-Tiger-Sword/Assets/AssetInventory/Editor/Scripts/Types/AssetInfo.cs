using ImpossibleRobert.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Networking;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

#if USE_ASSET_MANAGER && USE_CLOUD_IDENTITY
using Unity.Cloud.Assets;
#endif

namespace AssetInventory
{
    /// <summary>
    /// Combines a catalog file record with its owning package metadata, indexing state, tags,
    /// dependencies, and preview data for search results and package workflows.
    /// </summary>
    [Serializable]
    public sealed class AssetInfo : AssetFile
    {
        public enum ImportStateOptions
        {
            Unknown = 0,
            Queued = 1,
            Missing = 2,
            Importing = 3,
            Imported = 4,
            Uninstalling = 5,
            Uninstalled = 6,
            Failed = 7,
            Cancelled = 8
        }

        public enum DependencyStateOptions
        {
            Unknown = 0,
            Calculating = 1,
            Done = 2,
            NotPossible = 3,
            Failed = 4,
            Partial = 5
        }

        public int ParentId { get; set; }
        public Asset.Source AssetSource { get; set; }
        public string Location { get; set; }
        public string OriginalLocation { get; set; }
        public string OriginalLocationKey { get; set; }
        public string Registry { get; set; }
        public string Repository { get; set; }
        public PackageSource PackageSource { get; set; }
        public int ForeignId { get; set; }
        public long PackageSize { get; set; }
        public string SafeName { get; set; }
        public string DisplayName { get; set; }
        public string SafePublisher { get; set; }
        public string DisplayPublisher { get; set; }
        public string SafeCategory { get; set; }
        public string DisplayCategory { get; set; }
        public int PublisherId { get; set; }
        public Asset.State CurrentState { get; set; }
        public Asset.SubState CurrentSubState { get; set; }
        public string Slug { get; set; }
        public int Revision { get; set; }
        public string Description { get; set; }
        public string KeyFeatures { get; set; }
        public string CompatibilityInfo { get; set; }
        public string SupportedUnityVersions { get; set; }
        public bool BIRPCompatible { get; set; }
        public bool URPCompatible { get; set; }
        public bool HDRPCompatible { get; set; }
        public string Keywords { get; set; }
        public string PackageDependencies { get; set; }
        public string Version { get; set; }
        public string LatestVersion { get; set; }
        public Asset.Strategy UpdateStrategy { get; set; }
        public string License { get; set; }
        public string LicenseLocation { get; set; }
        public DateTime LastRelease { get; set; }
        public DateTime LastUpdate { get; set; } // e.g. deprecation, metadata...
        public DateTime PurchaseDate { get; set; }
        public DateTime FirstRelease { get; set; }
        public float AssetRating { get; set; }
        public int RatingCount { get; set; }
        public float Hotness { get; set; }
        public float PriceEur { get; set; }
        public float PriceUsd { get; set; }
        public float PriceCny { get; set; }
        public string Requirements { get; set; }
        public string ReleaseNotes { get; set; }
        public Asset.OfficialStateType OfficialState { get; set; }
        public bool IsHidden { get; set; }
        public bool Exclude { get; set; }
        public bool NoIndex { get; set; }
        public bool Backup { get; set; }
        public bool KeepExtracted { get; set; }
        public bool UseAI { get; set; }
        public bool? UseSemanticIndex { get; set; }
        public bool? UseCodeIndex { get; set; }
        public int UploadId { get; set; }
        public string ETag { get; set; }
        public DateTime LastOnlineRefresh { get; set; }
        public int FileCount { get; set; }
        public long UncompressedSize { get; set; }

        // runtime only
        internal AssetDownloader PackageDownloader { get; set; }
        internal AssetOrigin Origin { get; set; }

        [field: NonSerialized] public AssetInfo ParentInfo { get; set; }
        public bool IsSemanticIndexEnabled => UseSemanticIndex.GetValueOrDefault(true);
        public bool IsCodeIndexEnabled => UseCodeIndex.GetValueOrDefault(true);

        [NonSerialized] private List<AssetInfo> _childInfos;
        public List<AssetInfo> ChildInfos
        {
            get => _childInfos ??= new List<AssetInfo>();
            set => _childInfos = value;
        }

        internal bool ChildInfosAllocated => _childInfos != null;
        internal bool HasChildInfos => _childInfos != null && _childInfos.Count > 0;
        internal int ChildInfoCount => _childInfos?.Count ?? 0;

        internal IEnumerable<AssetInfo> EnumerateChildInfos()
        {
            return _childInfos ?? Enumerable.Empty<AssetInfo>();
        }

        internal AssetInfo FirstChildInfoOrDefault(Func<AssetInfo, bool> predicate)
        {
            return _childInfos == null ? null : _childInfos.FirstOrDefault(predicate);
        }

        internal void AddChildInfo(AssetInfo childInfo)
        {
            if (childInfo == null) return;

            (_childInfos ??= new List<AssetInfo>()).Add(childInfo);
        }

        internal void ResetChildInfos()
        {
            _childInfos = null;
        }

        [field: NonSerialized] internal AssetInfo SRPOriginalBackup { get; set; }
        internal Asset SRPSupportPackage { get; set; }
        internal AssetFile SRPMainReplacement { get; set; }
        internal List<AssetFile> SRPSupportFiles { get; set; }
        internal bool SRPUsed { get; set; }

        internal Texture2D PreviewTexture { get; set; }
        public bool IsIndexed => FileCount > 0 && (CurrentState == Asset.State.Done || CurrentState == Asset.State.New || CurrentState == Asset.State.SubInProcess); // new is set when deleting local package file
        public bool IsDeprecated => OfficialState == Asset.OfficialStateType.Deprecated;
        public bool IsAbandoned => OfficialState == Asset.OfficialStateType.Disabled;

        private bool? _isMaterialized;
        public bool IsMaterialized
        {
            get
            {
                if (_isMaterialized == null) _isMaterialized = Assets.IsMaterialized(ToAsset());
                return _isMaterialized ?? false;
            }
            set => _isMaterialized = value;
        }
        public ImportStateOptions ImportState { get; set; }
        public DependencyStateOptions DependencyState { get; set; } = DependencyStateOptions.Unknown;
        public List<AssetFile> Dependencies { get; set; }
        public List<AssetFile> MediaDependencies { get; set; }
        public List<AssetFile> ScriptDependencies { get; set; }
        public List<AssetFile> ExtendedScriptDependencies { get; set; }
        public List<Asset> CrossPackageDependencies { get; set; }
        public long DependencySize { get; set; }
        internal bool WasOutdated { get; set; }
        public List<AssetMedia> AllMedia { get; set; }
        public List<AssetMedia> Media { get; set; }

        private bool _tagsDone;
        private bool _metadataDone;
        private List<TagInfo> _packageTags;
        private List<TagInfo> _assetTags;
        private List<MetadataInfo> _packageMetadata;
        private int _tagHash;
        private int _metadataHash;
        private string _packageSamplesLoaded;
        private IEnumerable<UnityEditor.PackageManager.UI.Sample> _packageSamples;
        private List<Dependency> _packageDependencies;
        [field: NonSerialized] private List<AssetInfo> _packageUsageDependencies;

        public List<TagInfo> PackageTags
        {
            get
            {
                EnsureTagsLoaded();
                return _packageTags;
            }
        }
        internal void SetTagsDirty() => _tagsDone = false;

        public List<MetadataInfo> PackageMetadata
        {
            get
            {
                EnsureMetadataLoaded();
                return _packageMetadata;
            }
        }
        internal void SetMetadataDirty() => _metadataDone = false;

        public List<TagInfo> AssetTags
        {
            get
            {
                EnsureTagsLoaded();
                return _assetTags;
            }
        }

        /// <summary>Returns package identifiers declared by this package, resolving stored dependency metadata when necessary.</summary>
        public List<Dependency> GetPackageDependencies()
        {
            if (string.IsNullOrEmpty(PackageDependencies)) return null;
            if (_packageDependencies == null || _packageDependencies.Count == 0)
            {
                _packageDependencies = JsonConvert.DeserializeObject<List<Dependency>>(PackageDependencies);
            }
            return _packageDependencies != null && _packageDependencies.Count > 0 ? _packageDependencies : null;
        }

        /// <summary>Returns indexed packages that depend on this package from the supplied catalog snapshot.</summary>
        public List<AssetInfo> GetPackageUsageDependencies(List<AssetInfo> assets)
        {
            if (assets == null || assets.Count == 0) return null;
            if (_packageUsageDependencies == null)
            {
                _packageUsageDependencies = assets.Where(a => a.GetPackageDependencies() != null && a.GetPackageDependencies().Any(d => (ForeignId > 0 && d.id == ForeignId) || d.location == SafeName)).ToList();
            }
            return _packageUsageDependencies != null && _packageUsageDependencies.Count > 0 ? _packageUsageDependencies : null;
        }

        public DateTime? ModifiedDate
        {
            get
            {
                if (ParentInfo != null) return ParentInfo.ModifiedDate;
                if (_modifiedDate != null) return _modifiedDate.Value;

                if (!IsDownloaded) return null;
                if (AssetSource != Asset.Source.AssetStorePackage
                    && AssetSource != Asset.Source.CustomPackage
                    && AssetSource != Asset.Source.Synty
                    && AssetSource != Asset.Source.Archive) return null;

                FileInfo fi = new FileInfo(GetLocation(true));
                _modifiedDate = fi.LastWriteTime;

                return _modifiedDate;
            }
        }

        public DateTime? CreatedDate
        {
            get
            {
                if (ParentInfo != null) return ParentInfo.CreatedDate;
                if (_createdDate != null) return _createdDate.Value;

                if (!IsDownloaded) return null;
                if (AssetSource != Asset.Source.AssetStorePackage
                    && AssetSource != Asset.Source.CustomPackage
                    && AssetSource != Asset.Source.Synty
                    && AssetSource != Asset.Source.Archive) return null;

                FileInfo fi = new FileInfo(GetLocation(true));
                _createdDate = fi.CreationTime;

                return _createdDate;
            }
        }

        public bool IsDownloaded
        {
            get
            {
                if (ParentInfo != null) return ParentInfo.IsDownloaded;
                if (_downloaded != null) return _downloaded.Value;

                // special asset types
                if (AssetSource == Asset.Source.RegistryPackage
                    || AssetSource == Asset.Source.AssetManager
                    || AssetSource == Asset.Source.CurrentProject
                    || ((AssetSource == Asset.Source.Archive || AssetSource == Asset.Source.CustomPackage) && File.Exists(GetLocation(true)))
                    || (AssetSource == Asset.Source.Directory && Directory.Exists(GetLocation(true))))
                {
                    _downloaded = true;
                    return _downloaded.Value;
                }

                // "none" is a special case, it's a placeholder for assets that are not attached
                if (SafeName == Asset.NONE)
                {
                    _downloaded = true;
                    return _downloaded.Value;
                }

                // check for missing location
                string location = GetLocation(true);
                if (string.IsNullOrEmpty(location))
                {
                    _downloaded = false;
                    return _downloaded.Value;
                }

                // check for missing file
                if (!File.Exists(location))
                {
                    _downloaded = false;
                    return _downloaded.Value;
                }

                if (AssetSource == Asset.Source.Synty && !SyntyCache.IsValidPackage(location))
                {
                    _downloaded = false;
                    return _downloaded.Value;
                }

                // due to Unity bug verify downloaded asset is indeed asset in question, could be multi-versioned asset
                if (AssetSource == Asset.Source.AssetStorePackage || AssetSource == Asset.Source.Synty || AI.Config.showCustomPackageUpdates)
                {
                    AssetHeader header = UnityPackageImporter.ReadHeader(location, true);
                    if (header != null)
                    {
                        if ((AssetSource != Asset.Source.Synty || AI.Config.syntyLinkAssetStoreMetadata) && int.TryParse(header.id, out int id))
                        {
                            if (id != ForeignId)
                            {
                                _downloaded = false;
                                _downloadedActual = header.version;
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(header.unity_version))
                        {
                            _downloadedCompatible = new SemVer(header.unity_version).WithOnlyMinor() <= new SemVer(Application.unityVersion);
                            _downloadedUnityVersion = header.unity_version;
                        }
                    }
                }
                _downloaded = _downloaded == null;
                return _downloaded.Value;
            }
        }
        private bool? _downloaded;
        private DateTime? _createdDate;
        private DateTime? _modifiedDate;
        private bool? _updateAvailable;
        private bool? _updateAvailableForced;
        private bool? _updateAvailableList;
        private bool? _updateAvailableListForced;

        public string DownloadedActual
        {
            get
            {
                if (ParentInfo != null) return ParentInfo.DownloadedActual;
                if (_downloaded == null) _ = IsDownloaded;
                return _downloadedActual;
            }
        }
        private string _downloadedActual;

        public bool IsDownloadedCompatible
        {
            get
            {
                if (ParentInfo != null) return ParentInfo.IsDownloadedCompatible;
                if (_downloaded == null) _ = IsDownloaded;
                return _downloadedCompatible;
            }
        }
        private bool _downloadedCompatible = true;

        public string DownloadededUnityVersion
        {
            get
            {
                if (ParentInfo != null) return ParentInfo.DownloadededUnityVersion;
                if (_downloaded == null) _ = IsDownloaded;
                return _downloadedUnityVersion;
            }
        }
        private string _downloadedUnityVersion;

        /// <summary>Reports whether the package's declared compatibility includes the current Unity editor version.</summary>
        public bool IsCurrentUnitySupported()
        {
            if (string.IsNullOrWhiteSpace(SupportedUnityVersions)) return true;
            string[] arr = SupportedUnityVersions.Split(new[] {", "}, StringSplitOptions.None); // 2019.4 C# syntax

            return new SemVer(arr[0]).WithOnlyMinor() <= new SemVer(Application.unityVersion);
        }

        private string _forcedTargetVersion;
        private string _forcedUnityPackageVersion;
        private string _cachedBackupPath;

        public AssetInfo()
        {
        }

        /// <summary>Creates a workflow view from the supplied persistent package record.</summary>
        public AssetInfo(Asset asset)
        {
            CopyFrom(asset);
        }

        /// <summary>Creates an independent workflow view by copying the supplied asset information.</summary>
        public AssetInfo(AssetInfo info)
        {
            CopyFrom(info, false);
        }

        /// <summary>Returns the top-level package result associated with this file or nested package.</summary>
        public AssetInfo GetRoot()
        {
            if (ParentId > 0 && ParentInfo != null && (ForeignId == 0 || ForeignId == ParentInfo.ForeignId))
            {
                return ParentInfo.GetRoot();
            }
            return this;
        }

        internal bool IsMediaLoading()
        {
            return AllMedia != null && AllMedia.Any(m => m.IsDownloading);
        }

        internal void DisposeMedia()
        {
            DisposeMedia(AllMedia);
            AllMedia = null;

            DisposeMedia(Media);
            Media = null;
        }

        internal void DisposeMedia(List<AssetMedia> media)
        {
            media?.ForEach(m =>
            {
                if (m.Texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(m.Texture);
                    m.Texture = null;
                }
                if (m.ThumbnailTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(m.ThumbnailTexture);
                    m.ThumbnailTexture = null;
                }
            });
        }

        private void EnsureTagsLoaded()
        {
            if (!_tagsDone || Tagging.TagHash != _tagHash)
            {
                if (!IsPackage()) _assetTags = Tagging.GetAssetTags(Id);
                _packageTags = Tagging.GetPackageTags(AssetId);
                _tagsDone = true;
                _tagHash = Tagging.TagHash;
            }
        }

        private void EnsureMetadataLoaded()
        {
            if (!_metadataDone || Metadata.MetadataHash != _metadataHash)
            {
                _packageMetadata = Metadata.GetPackageMetadata(AssetId);
                _metadataDone = true;
                _metadataHash = Metadata.MetadataHash;
            }
        }

        /// <summary>Reports whether the package is installed only as a transitive dependency of another package.</summary>
        public bool IsIndirectPackageDependency()
        {
            if (AssetSource != Asset.Source.RegistryPackage) return false;

            PackageInfo pInfo = AssetStore.GetInstalledPackage(this);
            return pInfo != null && !pInfo.isDirectDependency;
        }

        /// <summary>Reports whether package metadata declares importable Unity samples.</summary>
        public bool HasSamples()
        {
            IEnumerable<UnityEditor.PackageManager.UI.Sample> packageSamples = GetSamples();
            return packageSamples != null && packageSamples.Any();
        }

        /// <summary>Returns sample definitions declared by the installed package metadata.</summary>
        public IEnumerable<UnityEditor.PackageManager.UI.Sample> GetSamples()
        {
            if (AssetSource != Asset.Source.RegistryPackage) return null;

            string installedPackageVersion = InstalledPackageVersion();
            if (installedPackageVersion == null) return null;

            if (_packageSamplesLoaded != installedPackageVersion)
            {
                _packageSamplesLoaded = installedPackageVersion;
                _packageSamples = UnityEditor.PackageManager.UI.Sample.FindByPackage(SafeName, installedPackageVersion);
            }
            return _packageSamples;
        }

        /// <summary>Returns the version currently installed in the open project, or an empty value when absent.</summary>
        public string InstalledPackageVersion()
        {
            if (AssetSource != Asset.Source.RegistryPackage) return null;

            PackageInfo pInfo = AssetStore.GetInstalledPackage(this);
            return pInfo?.version;
        }

        /// <summary>Returns the version Asset Inventory would install under the package's update strategy.</summary>
        public string TargetPackageVersion()
        {
            if (AssetSource != Asset.Source.RegistryPackage) return null;
            if (PackageSource == PackageSource.Local || PackageSource == PackageSource.LocalTarball) return null;
            if (!string.IsNullOrEmpty(_forcedTargetVersion)) return _forcedTargetVersion;

            PackageInfo pInfo = AssetStore.GetPackageInfo(this);
            if (pInfo == null) return null;

            switch (UpdateStrategy)
            {
                case Asset.Strategy.LatestStableCompatible:
                    return pInfo.versions.compatible.LastOrDefault(p => !p.ToLowerInvariant().Contains("pre") && !p.ToLowerInvariant().Contains("exp"));

                case Asset.Strategy.LatestCompatible:
                    return pInfo.versions.compatible.LastOrDefault();

                case Asset.Strategy.Recommended:
                    return string.IsNullOrWhiteSpace(GetVerifiedVersion(pInfo)) ? null : GetVerifiedVersion(pInfo);

                case Asset.Strategy.RecommendedOrLatestStableCompatible:
                    if (string.IsNullOrWhiteSpace(GetVerifiedVersion(pInfo)))
                    {
                        return pInfo.versions.compatible.LastOrDefault(p => !p.ToLowerInvariant().Contains("pre") && !p.ToLowerInvariant().Contains("exp"));
                    }
                    return GetVerifiedVersion(pInfo);

                case Asset.Strategy.Manually:
                    return null;
            }

            return null;
        }

        #if UNITY_2022_2_OR_NEWER
        private string GetVerifiedVersion(PackageInfo pInfo) => pInfo.versions.recommended;
        #else
        private string GetVerifiedVersion(PackageInfo pInfo) => pInfo.versions.verified;
        #endif

        /// <summary>Returns the user-facing package or file name, with optional version decoration.</summary>
        public string GetDisplayName(bool extended = false)
        {
            string result = string.IsNullOrEmpty(DisplayName) ? SafeName : DisplayName;
            if (extended && AssetSource == Asset.Source.RegistryPackage && !string.IsNullOrWhiteSpace(InstalledPackageVersion())) result += " - " + InstalledPackageVersion();
            return result;
        }

        /// <summary>Returns the normalized publisher name suitable for UI and export output.</summary>
        public string GetDisplayPublisher() => string.IsNullOrEmpty(DisplayPublisher) ? SafePublisher : DisplayPublisher;
        /// <summary>Returns the normalized category name suitable for UI and export output.</summary>
        public string GetDisplayCategory() => string.IsNullOrEmpty(DisplayCategory) ? SafeCategory : DisplayCategory;

        /// <summary>Returns the known purchase date using catalog and Asset Store metadata fallbacks.</summary>
        public DateTime GetPurchaseDate()
        {
            if (PurchaseDate.Year > 1) return PurchaseDate;

            return CreatedDate ?? DateTime.MinValue;
        }

        /// <summary>Returns cached changelog text for the requested version when available.</summary>
        public string GetChangeLog(string versionOverride = null)
        {
            if (string.IsNullOrWhiteSpace(ReleaseNotes) && Registry == Asset.UNITY_REGISTRY)
            {
                SemVer version = new SemVer(string.IsNullOrEmpty(versionOverride) ? Version : versionOverride);
                return $"https://docs.unity3d.com/Packages/{SafeName}@{version.Major}.{version.Minor}/changelog/CHANGELOG.html";
            }
            return ReleaseNotes;
        }

        /// <summary>Returns the changelog URL associated with the requested version when available.</summary>
        public string GetChangeLogURL(string versionOverride = null)
        {
            string changeLog = GetChangeLog(versionOverride);

            return StringUtils.IsUrl(changeLog) ? changeLog : null;
        }

        /// <summary>Resolves the package or file location, optionally preferring extracted or override storage.</summary>
        public string GetLocation(bool expanded)
        {
            // Check if a specific backup version is forced for unity packages
            if (!string.IsNullOrWhiteSpace(_forcedUnityPackageVersion) &&
                (AssetSource == Asset.Source.AssetStorePackage || AssetSource == Asset.Source.CustomPackage) &&
                ForeignId > 0)
            {
                // Use cached path to avoid repeated file system scans
                if (_cachedBackupPath == null)
                {
                    _cachedBackupPath = GetLocationForVersion(_forcedUnityPackageVersion);
                }

                if (!string.IsNullOrWhiteSpace(_cachedBackupPath) && System.IO.File.Exists(_cachedBackupPath))
                {
                    return expanded ? _cachedBackupPath : Paths.MakeRelative(_cachedBackupPath);
                }
            }

            return expanded ? Paths.DeRel(Location) : Location;
        }

        /// <summary>Returns the display or normalized package version according to the requested formatting policy.</summary>
        public string GetVersion(bool returnIndexedIfNone = false)
        {
            if (AssetSource == Asset.Source.RegistryPackage)
            {
                string installedPackageVersion = InstalledPackageVersion();
                return installedPackageVersion == null ? (returnIndexedIfNone ? Version : null) : installedPackageVersion;
            }

            return GetRoot().Version;
        }

        // keep in sync with copy in Asset
        /// <summary>Resolves the package or file location, optionally preferring extracted or override storage.</summary>
        public async Task<string> GetLocation(bool expanded, bool resolveParent)
        {
            // Check if a specific backup version is forced for unity packages
            if (!string.IsNullOrWhiteSpace(_forcedUnityPackageVersion) &&
                (AssetSource == Asset.Source.AssetStorePackage || AssetSource == Asset.Source.CustomPackage) &&
                ForeignId > 0)
            {
                // Use cached path to avoid repeated file system scans
                if (_cachedBackupPath == null)
                {
                    _cachedBackupPath = GetLocationForVersion(_forcedUnityPackageVersion);
                }

                if (!string.IsNullOrWhiteSpace(_cachedBackupPath) && System.IO.File.Exists(_cachedBackupPath))
                {
                    return expanded ? _cachedBackupPath : Paths.MakeRelative(_cachedBackupPath);
                }
            }

            string archivePath = Location;
            if (resolveParent && ParentId > 0)
            {
                Asset parentAsset = ParentInfo?.ToAsset();
                if (parentAsset == null) parentAsset = DBAdapter.DB.Find<Asset>(ParentId);
                if (parentAsset == null)
                {
                    Debug.LogError($"Could not resolve parent asset of '{GetDisplayName()}'.");
                }
                else
                {
                    if (!Assets.IsMaterialized(parentAsset)) await Assets.Extract(parentAsset);

                    string[] arr = Location.Split(Asset.SUB_PATH);
                    AssetFile parentAssetFile = DBAdapter.DB.Query<AssetFile>("select * from AssetFile where AssetId=? and Path=?", ParentId, arr.Last()).FirstOrDefault();
                    if (parentAssetFile == null)
                    {
                        Debug.LogError($"Could not resolve package in parent index {arr.Last()}. This usually means the parent package changed or was reorganized and this is an orphan. Reindex the parent package to resolve this.");
                        return null;
                    }
                    archivePath = await Assets.EnsureMaterialized(parentAsset, parentAssetFile);
                }
            }
            return expanded ? Paths.DeRel(archivePath) : archivePath;
        }

        /// <summary>Updates the transient resolved location without moving files or changing the persisted source record.</summary>
        public void SetLocation(string location)
        {
            if (location == null)
            {
                Location = null;
                return;
            }
            Location = Paths.MakeRelative(location);
        }

        internal bool IsLocationUnmappedRelative()
        {
            return Paths.IsRel(Location) && Paths.DeRel(Location, true) == null;
        }

        /// <summary>Reports whether a newer compatible package version is available under the current update strategy.</summary>
        public bool IsUpdateAvailable(bool force = true)
        {
            // quick checks can remain uncached
            if (ParentId > 0) return false;
            if (WasOutdated) return false;
            if (AssetSource == Asset.Source.Synty) return false;

            if (force && _updateAvailableForced != null) return _updateAvailableForced.Value;
            if (!force && _updateAvailable != null) return _updateAvailable.Value;

            if (IsAbandoned || IsDeprecated)
            {
                _updateAvailable = false;
                _updateAvailableForced = false;
                return false;
            }

            // registry packages should only flag update if inside current project and compatible
            if (AssetSource == Asset.Source.RegistryPackage)
            {
                if (!AI.Config.showIndirectPackageUpdates)
                {
                    PackageInfo pInfo = AssetStore.GetInstalledPackage(this);
                    if (pInfo != null && !pInfo.isDirectDependency)
                    {
                        _updateAvailable = false;
                        _updateAvailableForced = false;
                        return false;
                    }
                }
                bool packageUpdateAvailable = InstalledPackageVersion() != null && TargetPackageVersion() != null && InstalledPackageVersion() != TargetPackageVersion();

                _updateAvailable = packageUpdateAvailable;
                _updateAvailableForced = packageUpdateAvailable;

                return packageUpdateAvailable;
            }

            // custom packages are typically treated as not updateable
            if (!force && AssetSource == Asset.Source.CustomPackage && !AI.Config.showCustomPackageUpdates)
            {
                _updateAvailable = false;
                return false;
            }

            // check for missing version information
            if (string.IsNullOrWhiteSpace(Version) || string.IsNullOrWhiteSpace(LatestVersion))
            {
                _updateAvailable = false;
                _updateAvailableForced = false;
                return false;
            }

            // compare versions
            bool updateAvailable = new SemVer(Version) < new SemVer(LatestVersion);
            if (force)
            {
                _updateAvailableForced = updateAvailable;
            }
            else
            {
                _updateAvailable = updateAvailable;
            }
            return updateAvailable;
        }

        /// <summary>Reports whether a newer compatible package version is available under the current update strategy.</summary>
        public bool IsUpdateAvailable(List<AssetInfo> assets, bool force = true)
        {
            if (ParentId > 0) return false;
            if (force && _updateAvailableListForced != null) return _updateAvailableListForced.Value;
            if (!force && _updateAvailableList != null) return _updateAvailableList.Value;

            bool isOlderVersion = IsUpdateAvailable(force);
            if (isOlderVersion && assets != null && AssetSource == Asset.Source.AssetStorePackage)
            {
                // if asset in that version is already loaded don't flag as update available
                if (assets.Any(a => a.AssetSource == Asset.Source.AssetStorePackage && a.ForeignId == ForeignId && a.Version == LatestVersion && !string.IsNullOrEmpty(a.GetLocation(true))))
                {
                    if (force)
                    {
                        _updateAvailableListForced = false;
                    }
                    else
                    {
                        _updateAvailableList = false;
                    }
                    return false;
                }
            }
            if (force)
            {
                _updateAvailableListForced = isOlderVersion;
            }
            else
            {
                _updateAvailableList = isOlderVersion;
            }
            return isOlderVersion;
        }

        /// <summary>Reports whether the package downloader currently has an active operation for this package.</summary>
        public bool IsDownloading()
        {
            if (ParentInfo != null) return ParentInfo.IsDownloading();
            return PackageDownloader != null && PackageDownloader.GetState().state == AssetDownloader.State.Downloading;
        }

        // duplicated from Asset to avoid thousands of unnecessary casts
        internal string GetCalculatedLocation()
        {
            if (AssetSource == Asset.Source.Synty) return SyntyCache.GetPackagePath(OriginalLocationKey);
            if (string.IsNullOrEmpty(SafePublisher) || string.IsNullOrEmpty(SafeCategory) || string.IsNullOrEmpty(SafeName)) return null;

            try
            {
                return System.IO.Path.Combine(Paths.GetAssetCacheFolder(), SafePublisher, SafeCategory, SafeName + ".unitypackage").Replace("\\", "/");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to calculate location for '{GetDisplayName()}' with publisher '{SafePublisher}', category '{SafeCategory}' and name '{SafeName}': {e.Message}");
                return null;
            }
        }

        internal AssetInfo WithTreeData(string name, int id = 0, int depth = 0)
        {
            m_Name = name;
            m_ID = id;
            m_Depth = depth;

            return this;
        }

        internal AssetInfo WithTreeId(int id)
        {
            m_ID = id;

            return this;
        }

        internal AssetInfo WithProjectPath(string path)
        {
            ProjectPath = path;

            return this;
        }

        /// <summary>Reports whether the record represents an optional feature package of another Unity package.</summary>
        public bool IsFeaturePackage()
        {
            return AssetSource == Asset.Source.RegistryPackage && SafeName != null && SafeName.StartsWith("com.unity.feature.");
        }

        /// <summary>Returns installed dependency records that belong to this feature package, excluding unrelated packages from the supplied snapshot.</summary>
        public List<AssetInfo> GetInstalledFeaturePackageContent(List<AssetInfo> allAssets)
        {
            List<AssetInfo> result = new List<AssetInfo>();
            if (AssetSource != Asset.Source.RegistryPackage) return result;

            PackageInfo pInfo = AssetStore.GetPackageInfo(this);
            if (pInfo != null && pInfo.dependencies != null)
            {
                foreach (DependencyInfo dependency in pInfo.dependencies)
                {
                    AssetInfo package = allAssets.FirstOrDefault(a => a.SafeName == dependency.name);
                    if (package != null && package.InstalledPackageVersion() != null) result.Add(package);
                }
            }

            return result;
        }

        internal Texture GetFallbackIcon()
        {
            Texture result = null;
            if (AssetSource == Asset.Source.RegistryPackage)
            {
                if (IsFeaturePackage())
                {
                    result = CommonUIStyles.IconContent("Asset Store@2x", "d_Asset Store@2x").image;
                }
                else
                {
                    result = CommonUIStyles.IconContent("Package Manager@2x", "d_Package Manager@2x").image;
                }
            }
            else if (AssetSource == Asset.Source.Archive)
            {
                result = CommonUIStyles.IconContent("FilterByType@2x", "d_FilterByType@2x").image;
            }
            else if (AssetSource == Asset.Source.Directory)
            {
                result = CommonUIStyles.IconContent("Folder Icon", "d_Folder Icon").image;
            }
            else if (AssetSource == Asset.Source.CustomPackage || AssetSource == Asset.Source.Synty)
            {
                result = CommonUIStyles.IconContent("ModelImporter Icon", "d_ModelImporter Icon").image;
            }
            else if (AssetSource == Asset.Source.AssetManager)
            {
                result = CommonUIStyles.IconContent("CloudConnect", "d_CloudConnect").image;
            }

            return result;
        }

        /// <summary>Creates a persistent package record from this combined search result without modifying the catalog.</summary>
        public Asset ToAsset()
        {
            // TODO: reflection?
            Asset result = new Asset
            {
                AssetSource = AssetSource,
                DisplayCategory = DisplayCategory,
                SafeCategory = SafeCategory,
                CurrentState = CurrentState,
                CurrentSubState = CurrentSubState,
                Id = AssetId,
                ParentId = ParentId,
                Slug = Slug,
                Revision = Revision,
                Registry = Registry,
                Repository = Repository,
                PackageSource = PackageSource,
                Description = Description,
                KeyFeatures = KeyFeatures,
                CompatibilityInfo = CompatibilityInfo,
                SupportedUnityVersions = SupportedUnityVersions,
                Keywords = Keywords,
                PackageDependencies = PackageDependencies,
                Version = Version,
                LatestVersion = LatestVersion,
                UpdateStrategy = UpdateStrategy,
                License = License,
                LicenseLocation = LicenseLocation,
                PurchaseDate = PurchaseDate,
                LastRelease = LastRelease,
                LastUpdate = LastUpdate,
                FirstRelease = FirstRelease,
                AssetRating = AssetRating,
                RatingCount = RatingCount,
                Hotness = Hotness,
                PriceEur = PriceEur,
                PriceUsd = PriceUsd,
                PriceCny = PriceCny,
                Requirements = Requirements,
                ReleaseNotes = ReleaseNotes,
                OfficialState = OfficialState,
                IsHidden = IsHidden,
                Exclude = Exclude,
                NoIndex = NoIndex,
                UploadId = UploadId,
                ETag = ETag,
                OriginalLocation = OriginalLocation,
                OriginalLocationKey = OriginalLocationKey,
                ForeignId = ForeignId,
                SafeName = SafeName,
                DisplayName = DisplayName,
                PackageSize = PackageSize,
                SafePublisher = SafePublisher,
                DisplayPublisher = DisplayPublisher,
                PublisherId = PublisherId,
                Backup = Backup,
                KeepExtracted = KeepExtracted,
                UseAI = UseAI,
                UseSemanticIndex = UseSemanticIndex,
                UseCodeIndex = UseCodeIndex,
                LastOnlineRefresh = LastOnlineRefresh
            };
            result.SetLocation(Location);
            if (ParentInfo != null) result.ParentAsset = ParentInfo.ToAsset();

            return result;
        }

        /// <summary>Returns the best available product, package, or local-source URL for this result.</summary>
        public string GetItemLink()
        {
            if (AssetSource == Asset.Source.Synty) return OriginalLocation;
            return GetAssetStoreLink();
        }

        /// <summary>Returns the linked Unity Asset Store URL independently of the package's primary catalog source.</summary>
        public string GetAssetStoreLink()
        {
            if (ForeignId <= 0) return null;
            return $"https://assetstore.unity.com/packages/slug/{ForeignId}";
        }

        /// <summary>Returns the Asset Store publisher URL when publisher metadata is available.</summary>
        public string GetPublisherLink()
        {
            return $"https://assetstore.unity.com/publishers/{PublisherId}";
        }

        /// <summary>Returns the Unity Cloud Asset Manager organization URL for this record, or null when it is not an Asset Manager asset.</summary>
        public string GetAMOrganizationUrl()
        {
            if (AssetSource != Asset.Source.AssetManager) return null;

            return $"{AI.CLOUD_HOME_URL}/{OriginalLocationKey}";
        }

        /// <summary>Returns the Unity Cloud Asset Manager project URL for this record, or null when project identity is unavailable.</summary>
        public string GetAMProjectUrl()
        {
            if (AssetSource != Asset.Source.AssetManager) return null;

            Asset root = ToAsset().GetRootAsset();

            return $"{GetAMOrganizationUrl()}/projects/{root.SafeName}/assets";
        }

        /// <summary>Returns the Unity Cloud Asset Manager collection URL for this record, or null when it has no collection.</summary>
        public string GetAMCollectionUrl()
        {
            if (AssetSource != Asset.Source.AssetManager) return null;

            return $"{GetAMProjectUrl()}/collectionPath/{UnityWebRequest.EscapeURL(Location)}";
        }

        /// <summary>Returns the Unity Cloud Asset Manager asset URL for this record, or null when asset identity is unavailable.</summary>
        public string GetAMAssetUrl()
        {
            if (AssetSource != Asset.Source.AssetManager) return null;

            return $"{GetAMProjectUrl()}?assetId={Guid}:{FileVersion}";
        }

#if USE_ASSET_MANAGER && USE_CLOUD_IDENTITY
        /// <summary>Maps this record's stored Asset Manager type identifier to the corresponding Unity Cloud asset type.</summary>
        public AssetType GetAMAssetType()
        {
            AssetType result = AssetType.Other;

            if (AI.TypeGroups[AI.AssetGroup.Audio].Contains(Type))
            {
                result = AssetType.Audio;
            }
            else if (AI.TypeGroups[AI.AssetGroup.Models].Contains(Type))
            {
                result = AssetType.Model_3D;
            }
            else if (AI.TypeGroups[AI.AssetGroup.Images].Contains(Type))
            {
                result = AssetType.Asset_2D;
            }
            else if (AI.TypeGroups[AI.AssetGroup.Materials].Contains(Type))
            {
                result = AssetType.Material;
            }
            else if (AI.TypeGroups[AI.AssetGroup.Scripts].Contains(Type))
            {
                result = AssetType.Script;
            }
            else if (AI.TypeGroups[AI.AssetGroup.Videos].Contains(Type))
            {
                result = AssetType.Video;
            }

            return result;
        }
#endif

        internal int GetChildDepth()
        {
            if (ParentId == 0 || ParentInfo == null) return 0;
            return ParentInfo.GetChildDepth() + 1;
        }

        /// <summary>Returns the current numeric Asset Store price in the stored currency, using sale pricing when active.</summary>
        public float GetPrice()
        {
            switch (AI.Config.currency)
            {
                case 0: return PriceEur;
                case 1: return PriceUsd;
                case 2: return PriceCny;
            }
            return 0;
        }

        /// <summary>Formats the current or supplied Asset Store price for display, including the known currency symbol.</summary>
        public string GetPriceText()
        {
            return GetPriceText(GetPrice());
        }

        /// <summary>Formats the current or supplied Asset Store price for display, including the known currency symbol.</summary>
        public string GetPriceText(float priceVal)
        {
            if (priceVal <= 0) return "free";

            string price = priceVal.ToString("N2");
            switch (AI.Config.currency)
            {
                case 0: return $"€{price}";
                case 1: return $"${price}";
                case 2: return $"¥{price}";
            }

            return price;
        }

        internal void Refresh(bool downloadStateOnly = false)
        {
            ParentInfo?.Refresh(downloadStateOnly);

            _downloaded = null;
            _downloadedActual = null;
            _createdDate = null;
            _modifiedDate = null;
            _updateAvailable = null;
            _updateAvailableForced = null;
            _updateAvailableList = null;
            _updateAvailableListForced = null;
            _forcedTargetVersion = null;
            // Note: _forcedUnityPackageVersion is NOT cleared here to preserve user's version selection
            // Note: _cachedBackupPath is NOT cleared here to preserve performance

            PackageDownloader?.SetAsset(this);

            if (downloadStateOnly) return;

            WasOutdated = false;
        }

        internal void ForceTargetVersion(string newVersion)
        {
            _forcedTargetVersion = newVersion;
        }

        public string ForcedUnityPackageVersion
        {
            get { return _forcedUnityPackageVersion; }
            set
            {
                _forcedUnityPackageVersion = value;
                _cachedBackupPath = null; // Clear cache when version changes
            }
        }

        /// <summary>Resolves the source or backup path containing the requested package version, or null when that version is unavailable locally.</summary>
        public string GetLocationForVersion(string version, Dictionary<int, List<BackupInfo>> backupState = null)
        {
            if (string.IsNullOrWhiteSpace(version)) return GetLocation(true);

            int backupKey = AssetBackup.GetBackupKey(this);
            if (backupKey == 0) return GetLocation(true);

            Dictionary<int, List<BackupInfo>> state = backupState;
            if (state == null)
            {
                state = AssetBackup.GatherState();
            }

            if (state != null && state.TryGetValue(backupKey, out List<BackupInfo> backupVersions))
            {
                BackupInfo backupInfo = backupVersions.FirstOrDefault(b => b.version == version);
                if (backupInfo != null && !string.IsNullOrWhiteSpace(backupInfo.location))
                {
                    return backupInfo.location;
                }
            }

            return GetLocation(true);
        }

        internal int GetFolderSpecType()
        {
            if (IsArchive()) return 2;
            if (IsUnityPackage()) return 0;

            return 1;
        }

        /// <summary>Reports whether this result represents a package-level record rather than an indexed file.</summary>
        public bool IsPackage()
        {
            return string.IsNullOrEmpty(FileName);
        }

        internal AssetInfo CopyFrom(Asset asset, AssetFile af = null)
        {
            if (asset != null)
            {
                // take over all asset properties
                foreach (PropertyInfo assetProp in typeof (Asset).GetProperties())
                {
                    if (!assetProp.CanRead) continue;
                    PropertyInfo thisProp = GetType().GetProperty(assetProp.Name);
                    if (thisProp == null || !thisProp.CanWrite) continue; // Property doesn't exist or isn't writable in 'this'

                    object value = assetProp.GetValue(asset);
                    thisProp.SetValue(this, value);
                }
                AssetId = asset.Id;
                if (ParentInfo != null && ParentInfo.AssetId != AssetId)
                {
                    ParentInfo = null;
                    ResetChildInfos();
                }
            }

            if (af != null)
            {
                // take over all asset file properties
                foreach (PropertyInfo pi in typeof (AssetFile).GetProperties())
                {
                    if (!pi.CanWrite) continue;

                    pi.SetValue(this, pi.GetValue(af));
                }
            }

            return this;
        }

        internal AssetInfo CopyFrom(AssetInfo info, bool publicOnly)
        {
            // take over all asset properties
            BindingFlags flags = publicOnly ?
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public :
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (PropertyInfo pi in typeof (AssetInfo).GetProperties(flags))
            {
                if (!pi.CanWrite) continue;
                if (pi.Name == nameof(ChildInfos))
                {
                    _childInfos = info._childInfos;
                    continue;
                }

                pi.SetValue(this, pi.GetValue(info));
            }

            return this;
        }

        public override string ToString()
        {
            if (IsPackage())
            {
                return $"Asset Package '{GetDisplayName()}' ({AssetId}, {FileCount} files)";
            }
            return $"Asset Info '{FileName}' ({GetDisplayName()})'";
        }
    }
}
