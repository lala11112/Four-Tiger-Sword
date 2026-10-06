using ImpossibleRobert.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Database;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public partial class IndexUI : BasicEditorUI
    {
        private const float CHECK_INTERVAL = 5;
        private const float NATIVE_DETAILS_DRILL_IN_THRESHOLD = 900f;

        private bool UseNativeNarrowDetailsLayout()
        {
            return position.width < NATIVE_DETAILS_DRILL_IN_THRESHOLD;
        }

        private readonly Dictionary<string, string> _staticPreviews = new Dictionary<string, string>
        {
            {"cs", "cs Script Icon"},
            {"php", "TextAsset Icon"},
            {"cg", "TextAsset Icon"},
            {"cginc", "TextAsset Icon"},
            {"js", "d_Js Script Icon"},
            {"prefab", "d_Prefab Icon"},
            {"png", "d_RawImage Icon"},
            {"jpg", "d_RawImage Icon"},
            {"gif", "d_RawImage Icon"},
            {"tga", "d_RawImage Icon"},
            {"tiff", "d_RawImage Icon"},
            {"ico", "d_RawImage Icon"},
            {"bmp", "d_RawImage Icon"},
            {"fbx", "d_PrefabModel Icon"},
            {"dll", "dll Script Icon"},
            {"meta", "MetaFile Icon"},
            {"unity", "d_SceneAsset Icon"},
            {"asset", "EditorSettings Icon"},
            {"txt", "TextScriptImporter Icon"},
            {"md", "TextScriptImporter Icon"},
            {"doc", "TextScriptImporter Icon"},
            {"docx", "TextScriptImporter Icon"},
            {"pdf", "TextScriptImporter Icon"},
            {"rtf", "TextScriptImporter Icon"},
            {"readme", "TextScriptImporter Icon"},
            {"chm", "TextScriptImporter Icon"},
            {"compute", "ComputeShader Icon"},
            {"shader", "Shader Icon"},
            {"shadergraph", "Shader Icon"},
            {"shadersubgraph", "Shader Icon"},
            {"mat", "d_Material Icon"},
            {"wav", "AudioImporter Icon"},
            {"mp3", "AudioImporter Icon"},
            {"ogg", "AudioImporter Icon"},
            {"xml", "UxmlScript Icon"},
            {"html", "UxmlScript Icon"},
            {"uss", "UssScript Icon"},
            {"css", "StyleSheet Icon"},
            {"json", "StyleSheet Icon"},
            {"exr", "d_ReflectionProbe Icon"}
        };

        private enum ChangeImpact
        {
            None,
            ReadOnly,
            Write
        }

        internal static string[] assetFields = new[]
        {
            "Asset/AssetRating", "Asset/AssetSource", "Asset/Backup", "Asset/BIRPCompatible", "Asset/CompatibilityInfo", "Asset/CurrentState", "Asset/CurrentSubState", "Asset/Description", "Asset/DisplayCategory", "Asset/DisplayName", "Asset/DisplayPublisher", "Asset/ETag", "Asset/Exclude",
            "Asset/FirstRelease", "Asset/ForeignId", "Asset/HDRPCompatible", "Asset/Hotness", "Asset/Hue", "Asset/Id", "Asset/IsHidden", "Asset/IsLatestVersion", "Asset/KeepExtracted", "Asset/KeyFeatures", "Asset/Keywords", "Asset/LastOnlineRefresh", "Asset/LastRelease", "Asset/LatestVersion",
            "Asset/License", "Asset/LicenseLocation", "Asset/Location", "Asset/NoIndex", "Asset/OriginalLocation", "Asset/OriginalLocationKey", "Asset/PackageDependencies", "Asset/PackageSize", "Asset/PackageSource", "Asset/ParentId", "Asset/PriceCny", "Asset/PriceEur", "Asset/PriceUsd",
            "Asset/PublisherId", "Asset/PurchaseDate", "Asset/RatingCount", "Asset/Registry", "Asset/ReleaseNotes", "Asset/Repository", "Asset/Requirements", "Asset/Revision", "Asset/SafeCategory", "Asset/SafeName",
            "Asset/SafePublisher", "Asset/Slug", "Asset/SupportedUnityVersions", "Asset/UpdateStrategy", "Asset/UploadId", "Asset/URPCompatible", "Asset/UseAI", "Asset/UseCodeIndex", "Asset/UseSemanticIndex", "Asset/Version",
            "AssetFile/AssetId", "AssetFile/FileName", "AssetFile/FileVersion", "AssetFile/FileStatus", "AssetFile/Guid", "AssetFile/Height", "AssetFile/Hue", "AssetFile/Id", "AssetFile/Length", "AssetFile/Path", "AssetFile/PreviewState", "AssetFile/Size", "AssetFile/SourcePath", "AssetFile/Type", "AssetFile/Width",
            "Tag/Color", "Tag/FromAssetStore", "Tag/Id", "Tag/Name",
            "TagAssignment/Id", "TagAssignment/TagId", "TagAssignment/TagTarget", "TagAssignment/TagTargetId"
        };

        internal static readonly string[] FolderTypes = {"Unity Packages", "Media Folder", "Archives", "Dev Packages"};
        internal static readonly string[] MediaTypes = {"-All Media-", "-All Files-", string.Empty, "Audio", "Images", "Models", string.Empty, "-Custom File Pattern-"};

        private List<Tag> _tags;
        private string[] _assetNames;
        private string[] _tagNames;
        private SearchablePopup.PopupItem[] _tagPopupItems;
        private string[] _publisherNames;
        private string[] _colorOptions;
        private string[] _categoryNames;
        private string[] _types;
        private string[] _resultSizes;
        private string[] _sortFields;
        private string[] _searchFields;
        private string[] _tileTitle;
        private string[] _dependencyOptions;
        private string[] _scriptImportOptions;
        private string[] _previewOptions;
        private string[] _doubleClickOptions;
        private string[] _packageSortOptions;
        private string[] _groupByOptions;
        private string[] _packageListingOptions;
        private string[] _imageTypeOptions;
        private GUIContent[] _packageListingOptionsShort;
        private GUIContent[] _packageViewOptions;
        private GUIContent[] _searchScopeOptions;
        private SearchScope[] _searchScopeValues;
        private string[] _deprecationOptions;
        private string[] _srpOptions;
        private string[] _priceOptions;
        private string[] _maintenanceOptions;
        private string[] _importDestinationOptions;
        private string[] _importStructureOptions;
        private string[] _importCollisionOptions;
        private string[] _assetCacheLocationOptions;
        private string[] _expertSearchFields;
        private string[] _currencyOptions;
        private readonly string[] _logOptions = {"Media Downloads", "Image Resizing", "Audio Parsing", "Package Parsing", "Custom Actions", "Preview Creation"};
        private string[] _blipOptions;
        private readonly string[] _aiBackendOptions = {"Blip", "Ollama", "LM Studio"};
        private string[] _browserTypeOptions;

        private int _lastTab = -1;
        private string _newTag;
        private int _lastMainProgress;
        private string _importFolder;
        private bool _blockingInProgress;
        private bool _needsRepaint;

        private string[] _pvSelection;
        private string _pvSelectedPath;
        private string _pvSelectedFolder;
        private InventoryStats _stats;
        private int _availablePackageUpdates;
        private int _activePackageDownloads;

        private List<AssetInfo> _assets;

        internal static bool HasOpenInstances => _openInstanceCount > 0;
        private static int _openInstanceCount;
        private static int _cacheObserverOwnerCount;
        private static int _scriptsReloaded;
        private static bool? _cachedVersionMismatch;
        private static int? _cachedDatabaseVersionNumber;
        private bool _registeredOpenInstance;
        private bool _registeredCacheObserverOwner;
        private bool _requireAssetTreeRebuild;
        private bool _requireReportTreeRebuild;
        private ChangeImpact _requireLookupUpdate;
        private bool _requireSearchUpdate;
        private bool _requireSearchSelectionUpdate;
        private bool _searchSelectionChangedManually;
        private DateTime _lastCheck;
        private bool _initDone;
        private bool _updateAvailable;
        private AssetDetails _onlineInfo;
        private bool _allowLogic;
        private Editor _previewEditor;

        private bool _searchHandlerAdded;
        private bool _selectionHandlerAdded;
        private bool _isCleaningUp;
        private bool _runtimeHooksRegistered;
        private bool _uitkShellActive;

        private void Init()
        {
            if (_initDone) return;
            _initDone = true;

            _fixedSearchTypeIdx = -1;
            ResetCachedDatabaseVersionCheck();
            AI.Init();

            _blockingInProgress = false;
            _dependencyCancellationTokens = new Dictionary<AssetInfo, CancellationTokenSource>();

            if (_requireLookupUpdate == ChangeImpact.None) _requireLookupUpdate = ChangeImpact.ReadOnly;
            _requireSearchUpdate = true;
            _requireAssetTreeRebuild = true;

            _ = CheckForToolUpdates();
            _ = CheckForAssetUpdates();
        }

        internal static bool ShouldLoadStatisticsBeforeLookupReload(bool assetsLoaded, bool lookupReloadPending)
        {
            return !assetsLoaded && !lookupReloadPending;
        }

        private bool IsLookupReloadPending()
        {
            return _requireLookupUpdate != ChangeImpact.None || _resultSizes == null || _resultSizes.Length == 0;
        }

        private void OnEnable()
        {
            _registeredOpenInstance = false;
            _registeredCacheObserverOwner = false;
            _runtimeHooksRegistered = false;
            RegisterOpenInstance();

            if (_usageCalculationInProgress && _usageCalculation == null) _usageCalculationInProgress = false; // process was interrupted
            _pvSelection = null;
            _initDone = false;
            _isCleaningUp = false;
            _searchPreviewSessionInitialized = false;

            QueueDelayedInit();
        }

        private void QueueDelayedInit()
        {
            EditorApplication.delayCall -= DelayedInit;
            EditorApplication.delayCall += DelayedInit;
        }

        private void DelayedInit()
        {
            if (this == null || _isCleaningUp) return;

            RunDeferredInit();
        }

        private void RunDeferredInit()
        {
            Init();
            RegisterRuntimeHooks();

            AudioTool.AudioManager.StopAudio();
            if (!AI.IsInitialized) return;

            AssetStore.FillBufferOnDemand(true);
            if (!searchMode) SuggestOptimization();
            if (ShowWorkspaces()) InitWorkspace();

            RegisterCacheObserverOwner();
        }

        private void RegisterRuntimeHooks()
        {
            if (_runtimeHooksRegistered) return;

            _runtimeHooksRegistered = true;
            EditorApplication.update += UpdateLoop;
            AI.Actions.OnActionsDone += OnActionsDone;
            AI.Actions.OnActionsInitialized += OnActionsInitialized;
            AI.OnPackageImageLoaded -= OnPackageImageLoaded;
            AI.OnPackageImageLoaded += OnPackageImageLoaded;
            AI.OnPackagesUpdated -= OnPackagesUpdated;
            AI.OnPackagesUpdated += OnPackagesUpdated;
            AI.OnDatabaseSwitched -= OnDatabaseSwitched;
            AI.OnDatabaseSwitched += OnDatabaseSwitched;
            AI.OnDatabaseReloaded -= OnDatabaseReloaded;
            AI.OnDatabaseReloaded += OnDatabaseReloaded;
            Tagging.OnTagsChanged -= OnTagsChanged;
            Tagging.OnTagsChanged += OnTagsChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorSceneManager.sceneOpened += OnSceneLoaded;
            ImportUI.OnImportDone += OnImportDone;
            RemovalUI.OnUninstallDone += OnImportDone;
            MaintenanceUI.OnMaintenanceDone += OnMaintenanceDone;
            UpgradeUtil.OnUpgradeDone += OnMaintenanceDone;
            AssetStore.OnPackageListUpdated -= OnPackageListUpdated;
            AssetStore.OnPackageListUpdated += OnPackageListUpdated;
            AssetDatabase.importPackageCompleted += ImportCompleted;
            AssetDownloaderUtils.OnDownloadFinished -= OnDownloadFinished;
            AssetDownloaderUtils.OnDownloadFinished += OnDownloadFinished;
            Events.registeredPackages += OnRegisteredPackages;
#if UNITY_6000_3_OR_NEWER
            DragAndDrop.AddDropHandlerV2(OnSceneDrop);
            DragAndDrop.AddDropHandlerV2(OnHierarchyDrop);
            DragAndDrop.AddDropHandlerV2(OnProjectBrowserDrop);
            DragAndDrop.AddDropHandlerV2(OnInspectorDrop);
#else
            DragAndDrop.AddDropHandler(OnSceneDrop);
            DragAndDrop.AddDropHandler(OnHierarchyDrop);
            DragAndDrop.AddDropHandler(OnProjectBrowserDrop);
            DragAndDrop.AddDropHandler(OnInspectorDrop);
#endif
        }

        private void UnregisterRuntimeHooks()
        {
            if (!_runtimeHooksRegistered) return;

            _runtimeHooksRegistered = false;
            EditorApplication.update -= UpdateLoop;
            AI.Actions.OnActionsDone -= OnActionsDone;
            AI.Actions.OnActionsInitialized -= OnActionsInitialized;
            AI.OnPackageImageLoaded -= OnPackageImageLoaded;
            AI.OnPackagesUpdated -= OnPackagesUpdated;
            AI.OnDatabaseSwitched -= OnDatabaseSwitched;
            AI.OnDatabaseReloaded -= OnDatabaseReloaded;
            Tagging.OnTagsChanged -= OnTagsChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            EditorSceneManager.sceneOpened -= OnSceneLoaded;
            ImportUI.OnImportDone -= OnImportDone;
            RemovalUI.OnUninstallDone -= OnImportDone;
            MaintenanceUI.OnMaintenanceDone -= OnMaintenanceDone;
            UpgradeUtil.OnUpgradeDone -= OnMaintenanceDone;
            AssetStore.OnPackageListUpdated -= OnPackageListUpdated;
            AssetDatabase.importPackageCompleted -= ImportCompleted;
            AssetDownloaderUtils.OnDownloadFinished -= OnDownloadFinished;
            Events.registeredPackages -= OnRegisteredPackages;
#if UNITY_6000_3_OR_NEWER
            DragAndDrop.RemoveDropHandlerV2(OnSceneDrop);
            DragAndDrop.RemoveDropHandlerV2(OnHierarchyDrop);
            DragAndDrop.RemoveDropHandlerV2(OnProjectBrowserDrop);
            DragAndDrop.RemoveDropHandlerV2(OnInspectorDrop);
#else
            DragAndDrop.RemoveDropHandler(OnSceneDrop);
            DragAndDrop.RemoveDropHandler(OnHierarchyDrop);
            DragAndDrop.RemoveDropHandler(OnProjectBrowserDrop);
            DragAndDrop.RemoveDropHandler(OnInspectorDrop);
#endif
        }

        private void OnDisable()
        {
            UnregisterOpenInstance();

            EditorApplication.delayCall -= DelayedInit;
            EditorApplication.delayCall -= PerformDelayedSearch;
            _searchHandlerAdded = false;
            UnregisterRuntimeHooks();

            AudioTool.AudioManager.StopAudio();
            UnregisterCacheObserverOwner();

            // Clean up preview editor to prevent PreviewRenderUtility leak during assembly reload
            CleanupPreviewEditor();

            CancelAndDispose(ref _textureLoading);
            CancelAndDispose(ref _textureLoading2);
            CancelAndDispose(ref _textureLoading3);
            CancelAndDispose(ref _extraction);

            // Cancel and dispose all dependency calculation tokens
            if (_dependencyCancellationTokens != null)
            {
                foreach (KeyValuePair<AssetInfo, CancellationTokenSource> kvp in _dependencyCancellationTokens)
                {
                    kvp.Value?.Cancel();
                    kvp.Value?.Dispose();
                }
                _dependencyCancellationTokens.Clear();
            }

            if (_initDone && AI.IsInitialized) FlushColumnLayouts();

            // Dispose preview textures to prevent memory leaks
            DisposeSearchResultTextures();
            ClearFilePreviewCache();
        }

        internal static void CancelAndDispose(ref CancellationTokenSource source)
        {
            CancellationTokenSource current = Interlocked.Exchange(ref source, null);
            if (current == null) return;

            try
            {
                current.Cancel();
            }
            finally
            {
                current.Dispose();
            }
        }

        private void OnDestroy()
        {
            UnregisterOpenInstance();
            UnregisterCacheObserverOwner();
            EditorApplication.delayCall -= DelayedInit;
            EditorApplication.delayCall -= PerformDelayedSearch;

            // Final cleanup when window is closed (not just disabled)
            DisposeSearchResultTextures();
            ClearFilePreviewCache();

            // Cleanup any remaining resources
            if (_animationPlayer != null)
            {
                _animationPlayer.Dispose();
                _animationPlayer = null;
            }
        }

        private void RegisterOpenInstance()
        {
            if (_registeredOpenInstance) return;

            _registeredOpenInstance = true;
            _openInstanceCount++;
        }

        private void UnregisterOpenInstance()
        {
            if (!_registeredOpenInstance) return;

            _registeredOpenInstance = false;
            _openInstanceCount = Mathf.Max(0, _openInstanceCount - 1);
        }

        private void RegisterCacheObserverOwner()
        {
            if (_registeredCacheObserverOwner || this is ResultPickerUI) return;

            _registeredCacheObserverOwner = true;
            _cacheObserverOwnerCount++;
            if (_cacheObserverOwnerCount == 1) AI.StartCacheObserver();
        }

        private void UnregisterCacheObserverOwner()
        {
            if (!_registeredCacheObserverOwner) return;

            _registeredCacheObserverOwner = false;
            _cacheObserverOwnerCount = Mathf.Max(0, _cacheObserverOwnerCount - 1);
            if (_cacheObserverOwnerCount == 0 && AI.IsInitialized) AI.StopCacheObserver();
        }

        private void UpdateLoop()
        {
            if (_isCleaningUp) return;

            RunNativeShellLifecycle();
            if (!_initDone || !AI.IsInitialized || AI.Config == null || Application.isPlaying) return;
            SearchUpdateLoop();
        }

        private void RunNativeShellLifecycle()
        {
            if (!_uitkShellActive || !_initDone || Application.isPlaying) return;

            AI.ResetShowAdvancedCache();
            if (_scriptsReloaded > 0)
            {
                _requireAssetTreeRebuild = true;
                _requireReportTreeRebuild = true;
                _requireSearchUpdate = true;
                _requireLookupUpdate = ChangeImpact.Write;
                _scriptsReloaded--;
                _calculatingFolderSizes = false;
            }

            if (!AI.IsInitialized || AI.Config == null) return;

            bool lookupReloadPending = IsLookupReloadPending();
            if (ShouldLoadStatisticsBeforeLookupReload(_assets != null, lookupReloadPending)) UpdateStatistics(false);
            _importFolder = DetermineImportFolder();

            if (lookupReloadPending)
            {
                ReloadLookups(_requireLookupUpdate == ChangeImpact.Write || _requireLookupUpdate == ChangeImpact.None);
            }

            if (_lastTileSizeChange != DateTime.MinValue && (DateTime.Now - _lastTileSizeChange).TotalMilliseconds > 300f)
            {
                if (AI.Config.tileText == 0) _requireSearchUpdate = true;
                _lastTileSizeChange = DateTime.MinValue;
            }

            if (_assets != null && (DateTime.Now - _lastCheck).TotalSeconds > CHECK_INTERVAL)
            {
                _availablePackageUpdates = _assets.Count(asset => asset.ParentId == 0 && asset.IsUpdateAvailable(_assets, false));
                _activePackageDownloads = AI.GetObserver().DownloadCount;
                _lastCheck = DateTime.Now;
            }

            if (hideMainNavigation) AI.Config.tab = (int)AssetInventoryTab.Search;
            NormalizeMainTab();
            CheckProjectViewSelection();

            if (IsNativeReportingShellActive() && _requireReportTreeRebuild) CreateReportTree();
            RunNativeSearchLifecycle();
        }

        private void SuggestOptimization()
        {
            // check if last optimization (stored as "yyyy-MM-dd HH:mm:ss" string) was more than a month ago
            AppProperty lastOptimization = DBAdapter.DB.Find<AppProperty>("LastOptimization");
            if (lastOptimization == null || string.IsNullOrWhiteSpace(lastOptimization.Value) || !DateTime.TryParse(lastOptimization.Value, out DateTime lastOpt))
            {
                OptimizeDatabase(true);
                return;
            }
            if ((DateTime.Now - lastOpt).TotalDays < AI.Config.dbOptimizationPeriod) return;

            // check if last optimization request (stored as "yyyy-MM-dd HH:mm:ss" string) was more than a day ago
            AppProperty lastOptRequest = DBAdapter.DB.Find<AppProperty>("LastOptimizationRequest");
            if (lastOptRequest == null || (DateTime.TryParse(lastOptRequest.Value, out DateTime lastOptReq) && (DateTime.Now - lastOptReq).TotalDays > AI.Config.dbOptimizationReminderPeriod))
            {
                lastOptRequest = new AppProperty("LastOptimizationRequest", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                DBAdapter.DB.InsertOrReplace(lastOptRequest);

                if (EditorUtility.DisplayDialog("Asset Inventory Maintenance", "It is recommended to optimize the database regularly to ensure fast search results. Should it be done now?", "OK", "Not Now"))
                {
                    OptimizeDatabase();
                }
            }
        }

        private void RequireTreesRebuild()
        {
            _requireAssetTreeRebuild = true;
            if (_usageCalculationDone) _requireReportTreeRebuild = true;
        }

        private void OnPackagesUpdated()
        {
            _requireLookupUpdate = ChangeImpact.Write;
            _requireSearchUpdate = true;
            RequireTreesRebuild();
            _usageCalculationDone = false;
        }

        private void OnDatabaseSwitched()
        {
            ResetCachedDatabaseVersionCheck();
            ReloadAfterDatabaseSwitch();
        }

        private void OnDatabaseReloaded()
        {
            ReloadDatabaseData(false);
        }

        private void OnMaintenanceDone()
        {
            ResetCachedDatabaseVersionCheck();
            _searches = null;
            _requireLookupUpdate = ChangeImpact.Write;
            _requireSearchUpdate = true;
            RequireTreesRebuild();
        }

        private void OnDownloadFinished(int foreignId)
        {
            PackageDownloadCompletion.SyncVisiblePackages(_assets, foreignId);
            PackageDownloadCompletion.SyncVisiblePackages(_selectedTreeAssets, foreignId);

            if (_selectedEntry != null && _selectedEntry.GetRoot().AssetSource != Asset.Source.Synty && _selectedEntry.GetRoot().ForeignId == foreignId)
            {
                PackageDownloadCompletion.SyncPackage(_selectedEntry);
            }
            if (_selectedTreeAsset != null && _selectedTreeAsset.GetRoot().AssetSource != Asset.Source.Synty && _selectedTreeAsset.GetRoot().ForeignId == foreignId)
            {
                PackageDownloadCompletion.SyncPackage(_selectedTreeAsset);
            }

            _requireSearchUpdate = true;
            RequireTreesRebuild();
        }

        private async void OnPackageImageLoaded(Asset asset)
        {
            AssetInfo info = _assets?.FirstOrDefault(a => a.Id == asset.Id);
            if (info == null) return;

            await AssetUtils.LoadPackageTexture(info);
            _requireAssetTreeRebuild = true;
        }

        private void OnSceneLoaded(Scene scene, OpenSceneMode mode)
        {
            // otherwise previews will be empty
            _requireSearchUpdate = true;
            _requireAssetTreeRebuild = true;
        }

        private void ImportCompleted(string packageName)
        {
            OnImportDone();
        }

        private void OnRegisteredPackages(PackageRegistrationEventArgs obj)
        {
            OnImportDone();
        }

        private void OnImportDone()
        {
            AssetStore.GatherProjectMetadata();

            _requireLookupUpdate = ChangeImpact.ReadOnly;
            RequireTreesRebuild();
            _usageCalculationDone = false;
        }

        private void CleanupPreviewEditor()
        {
            _isCleaningUp = true;

            EditorApplication.delayCall -= PerformDelayedSearch;
            EditorApplication.delayCall -= HandleSearchSelectionChanged;
            _searchHandlerAdded = false;
            _requireSearchSelectionUpdate = false;

            if (_previewEditor != null)
            {
                DestroyImmediate(_previewEditor);
                _previewEditor = null;
            }
        }

        private void OnBeforeAssemblyReload()
        {
            CleanupPreviewEditor();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            AudioTool.AudioManager.StopAudio();

            // Clean up preview editor before assembly reload to prevent PreviewRenderUtility leak
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                CleanupPreviewEditor();
            }

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                // will crash editor otherwise
                _textureLoading?.Cancel();
                _textureLoading2?.Cancel();
                _textureLoading3?.Cancel();
            }

            // UI will have lost all preview textures during play mode
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                _requireSearchUpdate = true;
                _requireAssetTreeRebuild = true;
            }
        }

        private GUIContent[] GetPackageViewOptions()
        {
            if (_packageViewOptions == null)
            {
                _packageViewOptions = new[]
                {
                    CommonUIStyles.IconContent("VerticalLayoutGroup Icon", "d_VerticalLayoutGroup Icon", "|List"),
                    CommonUIStyles.IconContent("GridLayoutGroup Icon", "d_GridLayoutGroup Icon", "|Grid")
                };
            }
            return _packageViewOptions;
        }

        private void ReloadLookups(bool force = true)
        {
            if (AI.DEBUG_MODE) Debug.LogWarning("Reload Lookups");

            _requireLookupUpdate = ChangeImpact.None;
            _resultSizes = new[] {"-all-", string.Empty, "10", "25", "50", "100", "250", "500", "1000", "1500", "2000", "2500", "3000", "4000", "5000"};
            _searchFields = new[] {"Asset Path", "File Name"};
            _sortFields = new[] {"-unsorted (fast)-", string.Empty, "Asset Path", "File Name", "Size", "Type", "Length", "Width", "Height", "Color", "Category", "Last Updated", "Rating", "#Reviews"};
            _packageSortOptions = Enum.GetNames(typeof (AssetTreeViewControl.Columns)).Select(StringUtils.CamelCaseToWords).ToArray();
            _groupByOptions = new[] {"-none-", string.Empty, "Category", "Publisher", "Tag", "State", "Location"};
            _colorOptions = new[] {"-all-", string.Empty, "matching"};
            _tileTitle = new[] {"-Intelligent-", "-None-", string.Empty, "Asset Path", "File Name", "File Name without Extension", "AI Caption or File Name"};
            _dependencyOptions = new[] {"-never-", string.Empty, "Upon Selection"};
            _scriptImportOptions = new[] {"-Never Import-", string.Empty, "Direct Only", "Extended Analysis", "All Scripts"};
            _previewOptions = new[] {"-all-", string.Empty, "Only With Preview", "Only Without Preview"};
            _doubleClickOptions = new[] {"-none-", string.Empty, "Import + Add to Scene", "Import", "Open"};
            _packageListingOptions = new[] {"-all-", "-all except registry packages-", "Only Asset Store Packages", "Only Registry Packages", "Only Custom Packages", "Only Media Folders", "Only Archives", "Only Asset Manager", "Only Synty Packages"};
            _packageListingOptionsShort = new[]
            {
                new GUIContent("All", "Show packages from every source."),
                new GUIContent("No Reg", "Show all packages except Unity registry packages."),
                new GUIContent("Store", "Show only Asset Store packages."),
                new GUIContent("Reg", "Show only Unity registry packages."),
                new GUIContent("Cust", "Show only custom packages."),
                new GUIContent("Media", "Show only media folders."),
                new GUIContent("Arch", "Show only archives."),
                new GUIContent("AM", "Show only Unity Asset Manager packages."),
                new GUIContent("Synty", "Show only packages discovered through Synty Importer compatibility.")
            };
            GetPackageViewOptions();
            RefreshSearchScopeOptions();
            _deprecationOptions = new[] {"-all-", string.Empty, "Exclude Deprecated", "Show Only Deprecated", string.Empty, "Exclude Affected (China Store)", "Show Only Affected (China Store)"};
            _srpOptions = new[] {"-all-", "-current-", string.Empty, "BIRP", "URP", "HDRP"};
            _priceOptions = new[] {"-all-", "-free-", "-paid-", string.Empty, "<=", ">="};
            _maintenanceOptions = new[] {"-all-", string.Empty, "Update Available", "Outdated in Unity Cache", "Disabled by Unity", "Custom Asset Store Link", "Indexed", "Not Indexed", "Custom Registry", "Downloaded", "Downloading", "Not Downloaded", "Duplicate", "Marked for Backup", "Not Marked for Backup", "Marked for AI", "Not Marked for AI", "Deleted", "Excluded", "With Sub-Packages", "Incompatible Packages", "Fixable Incompatibilities", "Unfixable Incompatibilities", "Marked for Semantic Index", "Not Marked for Semantic Index", "Marked for Code Index", "Not Marked for Code Index", "Not Included in Indexing", "Indexing Enabled", "Needs Indexing", string.Empty, string.Empty, string.Empty, "Synty Cached"};
            _updateDateOptions = new[] {"-all-", string.Empty, "Last Week", "Last Month", "Last Year", string.Empty, "Before...", "After..."};
            _purchaseDateOptions = new[] {"-all-", string.Empty, "Last Week", "Last Month", "Last Year", string.Empty, "Before...", "After..."};
            _packageSizeOptions = new[] {"-all-", string.Empty, "<=", ">="};
            _unityVersionOptions = new[] {"-all-", string.Empty, "Unity 2019 or older", "Unity 2020 or older", "Unity 2021 or older", "Unity 2022 or older", "Unity 2023 or older", "Unity 6000 or older"};
            _importDestinationOptions = new[] {"Into Folder Selected in Project View", "Into Assets Root", "Into Specific Folder"};
            _importStructureOptions = new[] {"All Files Flat in Target Folder", "Keep Original Folder Structure"};
            _importCollisionOptions = new[] {"Auto-Rename", "Warn and Skip", "Overwrite"};
            _assetCacheLocationOptions = new[] {"Automatic", "Custom Folder"};
            _currencyOptions = new[] {"EUR", "USD", "CNY"};
            _blipOptions = new[] {"Small (1Gb)", "Large (1.8Gb)"};
            _browserTypeOptions = new[] {"System Default", "Custom"};
            _imageTypeOptions = new List<string> {"-all-", string.Empty}.Concat(TextureNameSuggester.suffixPatterns.Keys.Select(StringUtils.CamelCaseToWords)).ToArray();
            _expertSearchFields = new List<string> {"-Add Field-", string.Empty}.Concat(assetFields).ToArray();

            UpdateStatistics(force);
            AssetStore.FillBufferOnDemand();

            _assetNames = Assets.ExtractAssetNames(_assets, true);
            _publisherNames = Assets.ExtractPublisherNames(_assets);
            _categoryNames = Assets.ExtractCategoryNames(_assets);
            _tagNames = Assets.ExtractTagNames(_tags);
            _tagPopupItems = Assets.ExtractTagPopupItems(_tags);

            _types = Assets.LoadTypes();
            if (AI.Config.searchType < 0 || AI.Config.searchType >= _types.Length)
            {
                AI.Config.searchType = 0;
            }
            if (!string.IsNullOrWhiteSpace(fixedSearchType))
            {
                _fixedSearchTypeIdx = Array.IndexOf(_types, fixedSearchType);
            }
        }

        private SearchScope GetConfiguredSearchScope()
        {
            SearchScope normalizedScope = SearchScopeModel.Normalize(AI.Config.searchScope, AI.Config.showIndexSearchScope);
            if ((int)normalizedScope != AI.Config.searchScope)
            {
                AI.Config.searchScope = (int)normalizedScope;
                AI.SaveConfig();
            }
            return normalizedScope;
        }

        private void RefreshSearchScopeOptions()
        {
            _searchScopeValues = SearchScopeModel.GetToolbarScopes(AI.Config.showIndexSearchScope);
            _searchScopeOptions = _searchScopeValues.Select(GetSearchScopeContent).ToArray();
        }

        private GUIContent GetSearchScopeContent(SearchScope searchScope)
        {
            switch (searchScope)
            {
                case SearchScope.Project:
                    return new GUIContent("Project", "Search only the current project");
                case SearchScope.Index:
                    return new GUIContent("Index", "Search only the index without scanning the current project");
                default:
                    return new GUIContent("All", "Search both index and current project");
            }
        }

        private VisualElement CreateNativeSearchScopeControl(Action onChanged)
        {
            RefreshSearchScopeOptions();

            SearchScope currentScope = GetConfiguredSearchScope();
            int selectedIndex = Array.IndexOf(_searchScopeValues, currentScope);
            if (selectedIndex < 0) selectedIndex = 0;

            return AssetInventoryUITK.CreateSegmentedControl(_searchScopeOptions, selectedIndex, index =>
            {
                SelectNativeSearchScope(index, onChanged);
            });
        }

        private void RefreshNativeSearchScopeControl(VisualElement control)
        {
            if (control == null) return;

            RefreshSearchScopeOptions();
            SearchScope currentScope = GetConfiguredSearchScope();
            int selectedIndex = Array.IndexOf(_searchScopeValues, currentScope);
            AssetInventoryUITK.RefreshSegmentedControl(control, selectedIndex < 0 ? 0 : selectedIndex);
        }

        private void SelectNativeSearchScope(int selectedIndex, Action onChanged)
        {
            RefreshSearchScopeOptions();
            if (_searchScopeValues == null || _searchScopeValues.Length == 0) return;

            int clampedIndex = Mathf.Clamp(selectedIndex, 0, _searchScopeValues.Length - 1);
            SearchScope scope = _searchScopeValues[clampedIndex];
            if (GetConfiguredSearchScope() == scope) return;

            AI.Config.searchScope = (int)scope;
            AI.SaveConfig();
            onChanged?.Invoke();
        }

        public void ReloadAfterDatabaseSwitch()
        {
            ReloadDatabaseData(true);
        }

        private void ReloadDatabaseData(bool databaseSwitched)
        {
            int selectedWorkspaceId = _selectedWorkspace?.Id ?? AI.Config.workspace;

            _searchesLoaded = false;
            _searches = null;
            _workspacesLoaded = false;
            _workspaces = null;
            _packageSearchesLoaded = false;
            _packageSearches = null;
            _codeSearchesLoaded = false;
            _codeSearches = null;

            if (databaseSwitched)
            {
                _activeSavedSearchId = -1;
                _activeSavedPackageSearchId = -1;
                _activeSavedCodeSearchId = -1;
                _selectedWorkspace = null;
            }

            _nativeSearchSavedSearchesDirty = true;
            _nativePackageSavedSearchesDirty = true;
            _nativeCodeSavedSearchesDirty = true;
            _nativeCodeResultsDirty = true;

            _cachedProjectSearchKey = null;
            _cachedProjectFiles = null;
            _cachedProjectOnlyFiles = null;

            ClearFilePreviewCache();
            _cachedBackupState = null;
            ResetCachedDatabaseVersionCheck();

            _requireLookupUpdate = ChangeImpact.None;
            ReloadLookups(true);
            _dbSize = DBAdapter.GetDBSize();

            if (!databaseSwitched)
            {
                if (_activeSavedSearchId > 0 && Searches.All(search => search.Id != _activeSavedSearchId))
                {
                    _activeSavedSearchId = -1;
                }
                if (_activeSavedPackageSearchId > 0 && PackageSearches.All(search => search.Id != _activeSavedPackageSearchId))
                {
                    _activeSavedPackageSearchId = -1;
                }
                if (_activeSavedCodeSearchId > 0 && CodeSearches.All(search => search.Id != _activeSavedCodeSearchId))
                {
                    _activeSavedCodeSearchId = -1;
                }

                if (selectedWorkspaceId > 0)
                {
                    SetWorkspace(Workspaces.FirstOrDefault(workspace => workspace.Id == selectedWorkspaceId));
                }
                else
                {
                    _selectedWorkspace = null;
                }
            }

            _lastCheck = DateTime.MinValue;
            _requireAssetTreeRebuild = true;
            _requireReportTreeRebuild = true;
            _requireSearchUpdate = true;

            PerformSearch();
            if (_codeSearchResult != null) ExecuteCodeSearch(true);

            MarkUITKShellDirty();
        }

        [DidReloadScripts(2)]
        private static void DidReloadScripts()
        {
            _scriptsReloaded++;
            ResetCachedDatabaseVersionCheck();
        }

        internal static void ResetCachedDatabaseVersionCheck()
        {
            _cachedVersionMismatch = null;
            _cachedDatabaseVersionNumber = null;
        }

        internal static bool HasDatabaseVersionMismatchForCurrentConnection()
        {
            EnsureDatabaseVersionCheckCached();
            return _cachedVersionMismatch.GetValueOrDefault();
        }

        private static int GetCachedDatabaseVersionNumber()
        {
            EnsureDatabaseVersionCheckCached();
            return _cachedDatabaseVersionNumber.GetValueOrDefault();
        }

        private static void EnsureDatabaseVersionCheckCached()
        {
            if (_cachedVersionMismatch != null) return;

            _cachedDatabaseVersionNumber = null;
            try
            {
                AppProperty dbVersion = DBAdapter.DB?.Find<AppProperty>("Version");
                if (dbVersion != null && int.TryParse(dbVersion.Value, out int dbVersionNumber))
                {
                    _cachedDatabaseVersionNumber = dbVersionNumber;
                    _cachedVersionMismatch = dbVersionNumber > UpgradeUtil.CURRENT_DB_VERSION;
                }
                else
                {
                    _cachedVersionMismatch = false;
                }
            }
            catch
            {
                _cachedVersionMismatch = false;
            }
        }

        private string DetermineImportFolder()
        {
            // determine import targets
            switch (AI.Config.importDestination)
            {
                case 0:
                    return _pvSelectedFolder;

                case 2:
                    return AI.Config.importFolder;

                default:
                    return "Assets";

            }
        }

        private void CheckProjectViewSelection()
        {
            if (_pvSelection != null && Selection.assetGUIDs != null && _pvSelection.SequenceEqual(Selection.assetGUIDs))
            {
                return;
            }

            _pvSelection = Selection.assetGUIDs;
            _pvSelectedPath = null;
            if (_pvSelection != null && _pvSelection.Length > 0)
            {
                _pvSelectedPath = AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs[0]);
                if (_pvSelectedPath.StartsWith("Packages"))
                {
                    _pvSelectedPath = null;
                    _pvSelectedFolder = null;
                }
                else
                {
                    _pvSelectedFolder = Directory.Exists(_pvSelectedPath) ? _pvSelectedPath : Path.GetDirectoryName(_pvSelectedPath);
                    if (!string.IsNullOrWhiteSpace(_pvSelectedFolder)) _pvSelectedFolder = _pvSelectedFolder.Replace('/', Path.DirectorySeparatorChar);
                }
            }
        }

        private void NormalizeMainTab()
        {
            AssetInventoryTab tab = Enum.IsDefined(typeof(AssetInventoryTab), AI.Config.tab)
                ? (AssetInventoryTab)AI.Config.tab
                : AssetInventoryTab.Search;

            if (tab == AssetInventoryTab.Code && !IsCodeSearchTabVisible())
            {
                tab = AssetInventoryTab.Search;
            }

            AI.Config.tab = (int)tab;
        }

        private static bool IsCodeSearchTabVisible()
        {
            return AI.Actions.CodeSearchEnabled;
        }

        private static List<MainTabItem> BuildVisibleMainTabs()
        {
            List<MainTabItem> result = new List<MainTabItem>
            {
                new MainTabItem(AssetInventoryTab.Search, "Search")
            };
            if (IsCodeSearchTabVisible())
            {
                result.Add(new MainTabItem(AssetInventoryTab.Code, "Code"));
            }
            result.Add(new MainTabItem(AssetInventoryTab.Packages, "Packages"));
            result.Add(new MainTabItem(AssetInventoryTab.Reporting, "Reporting"));
            result.Add(new MainTabItem(AssetInventoryTab.Settings, "Settings" + (AI.Actions.AnyActionsInProgress ? " (indexing)" : "")));
            return result;
        }

        private readonly struct MainTabItem
        {
            public readonly AssetInventoryTab Tab;
            public readonly string Label;

            public MainTabItem(AssetInventoryTab tab, string label)
            {
                Tab = tab;
                Label = label;
            }
        }

        private void ShowPackageMaintenance(PackageSearch.MaintenanceOption maintenanceOption)
        {
            AI.Config.tab = (int)AssetInventoryTab.Packages;
            _selectedMaintenance = maintenanceOption;
            _requireAssetTreeRebuild = true;
            _packageInspectorTab = 1;
            AI.SaveConfig();
        }

        private double _nextReviewPromptAttempt;

        private void TryShowReviewPrompt()
        {
            if (!_initDone || !AI.IsInitialized || _isCleaningUp || _nativeShellContentBlocked || searchMode || instantSelection) return;
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (focusedWindow != this || _blockingInProgress || _dragImportInProgress || _pickerSelectionInProgress || AI.Actions.AnyActionsInProgress) return;
            if (!ReviewPrompt.IsPending(AI.Config) || EditorApplication.timeSinceStartup < _nextReviewPromptAttempt) return;

            // Back off if settings cannot be saved, without losing the pending prompt.
            _nextReviewPromptAttempt = EditorApplication.timeSinceStartup + 30;
            ReviewPrompt.TryShow(AI.Config, AI.TrySaveConfig, ShowInterstitial);
        }

        private void ShowInterstitial()
        {
            if (EditorUtility.DisplayDialog("Your Support Counts", "This message will only appear once. Thanks for using Asset Inventory! I hope you enjoy using it.\n\n" +
                    "Developing a rather ground-braking asset like this as a solo-dev requires a huge amount of time and work.\n\n" +
                    "Please consider leaving a review and spreading the word. This is so important on the Asset Store and is the only way to make asset development viable.\n\n"
                    , "Leave Review", "Maybe Later"))
            {
                AI.OpenURL(AI.ASSET_STORE_LINK);
            }
        }

        private void GatherTreeChildren(int id, List<AssetInfo> result, HashSet<int> seen, TreeModel<AssetInfo> treeModel)
        {
            AssetInfo info = treeModel.Find(id);
            if (info == null) return;

            GatherTreeChildrenRecursive(info, result, seen);
        }

        private void GatherTreeChildrenRecursive(TreeElement node, List<AssetInfo> result, HashSet<int> seen)
        {
            if (node is AssetInfo info && info.Id != 0 && seen.Add(info.TreeId)) result.Add(info);
            if (node.HasChildren)
            {
                foreach (TreeElement child in node.Children)
                {
                    GatherTreeChildrenRecursive(child, result, seen);
                }
            }
        }

        private bool HandleTagShortcut(KeyCode keyCode, EventModifiers modifiers)
        {
            if ((modifiers & EventModifiers.Alt) == 0) return false;

            string keyStr = keyCode.ToString().ToLower();
            if (keyStr.StartsWith("alpha")) keyStr = keyStr.Substring(5);
            if (keyStr.Length != 1 || !char.IsLetterOrDigit(keyStr[0])) return false;

            List<Tag> tags = Tagging.LoadTags();
            Tag matchingTag = tags.Find(t => t.Hotkey == keyStr);
            if (matchingTag == null) return false;

            bool isRemoving = (modifiers & EventModifiers.Shift) != 0;
            if (isRemoving)
            {
                switch (AI.Config.tab)
                {
                    case 0:
                        Tagging.RemoveAssetAssignments(_sgrid.selectionItems, matchingTag.Name, true);
                        CalculateSearchBulkSelection();
                        break;

                    case 1:
                        Tagging.RemovePackageAssignments(_selectedTreeAssets, matchingTag.Name, true);
                        break;
                }
            }
            else
            {
                switch (AI.Config.tab)
                {
                    case 0:
                        Tagging.AddAssignments(_sgrid.selectionItems, matchingTag.Name, TagAssignment.Target.Asset, true);
                        CalculateSearchBulkSelection();
                        break;

                    case 1:
                        Tagging.AddAssignments(_selectedTreeAssets, matchingTag.Name, TagAssignment.Target.Package, true);
                        break;
                }
            }

            _requireAssetTreeRebuild = true;
            return true;
        }

        private CancellationToken InitBlockingToken()
        {
            _blockingInProgress = true;
            InitBlocking();
            return _extraction.Token;
        }

        private void DisposeBlocking()
        {
            CancellationTokenSource completed = Interlocked.Exchange(ref _extraction, null);
            completed?.Dispose();
            _blockingInProgress = false;
        }

        private void InitBlocking()
        {
            CancelAndDispose(ref _extraction);
            _extraction = new CancellationTokenSource();
        }

        private async Task CheckForToolUpdates()
        {
            _updateAvailable = false;
            if (Application.isBatchMode) return;

            await Task.Delay(2000); // let remainder of window initialize first
            if (!AssetStoreAuthentication.Current.CanAutoRefresh(Application.isBatchMode, true)) return;

            _onlineInfo = await AssetStore.RetrieveAssetDetails(AI.ASSET_STORE_ID, null, true);
            if (_onlineInfo == null) return;

            _updateAvailable = new SemVer(_onlineInfo.version.name) > new SemVer(AI.VERSION);
        }

        private async Task CheckForAssetUpdates()
        {
            if (Application.isBatchMode) return;
            await Task.Delay(2500); // let remainder of window initialize first

            if (!AI.IsInitialized || (!AI.Config.autoRefreshPurchases && !AI.Config.autoRefreshMetadata)) return;
            if (!AssetStoreAuthentication.Current.CanAutoRefresh(Application.isBatchMode)) return;

            if (AI.Config.autoRefreshPurchases)
            {
                if (AI.Config.lastPurchasesUpdate != DateTime.MinValue && (DateTime.Now - AI.Config.lastPurchasesUpdate).TotalHours < AI.Config.purchasesRefreshPeriod)
                {
                    // no need to check again
                }
                else
                {
                    await AI.Actions.RunAction(ActionHandler.ACTION_ASSET_STORE_PURCHASES);
                }
            }

            if (AI.Config.autoRefreshMetadata && AssetStoreAuthentication.Current.CanAutoRefresh(Application.isBatchMode))
            {
                if (AI.Config.lastMetadataUpdate != DateTime.MinValue && (DateTime.Now - AI.Config.lastMetadataUpdate).TotalHours < AI.Config.metadataTimeout)
                {
                    // no need to check again
                }
                else
                {
                    await AI.Actions.RunAction(ActionHandler.ACTION_ASSET_STORE_DETAILS);
                }
            }
        }

        private void CreateDebugReport()
        {
            string reportFile = Path.Combine(Paths.GetStorageFolder(), "DebugReport.log");
            File.WriteAllText(reportFile, AI.CreateDebugReport());
            EditorUtility.RevealInFinder(reportFile);
        }

        private void OnInspectorUpdate()
        {
            TryShowReviewPrompt();

            // Only repaint when there's actual state change, avoiding unnecessary redraws
            if (_needsRepaint || _requireSearchUpdate || _requireAssetTreeRebuild || _requireReportTreeRebuild
                || _requireLookupUpdate != ChangeImpact.None || _blockingInProgress || _dragImportInProgress || _animationPlayer?.IsLoaded == true)
            {
                if (_needsRepaint) _nativeSearchGridView?.RefreshItems();
                _needsRepaint = false;
                Repaint();
            }
        }

        // Shared saved search methods
        private delegate void OnSearchButtonClick();

        private delegate void OnSearchSettingsClick(GenericMenu menu);

        private int FindIndexByValue(string[] items, string value, bool splitPath = false)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;

            // Extract ID from value if it has brackets
            string valueId = null;
            int valueBracketStart = value.LastIndexOf('[');
            if (valueBracketStart > 0)
            {
                valueId = value.Substring(valueBracketStart + 1, value.Length - valueBracketStart - 2);
            }

            return Mathf.Max(0, Array.FindIndex(items, s =>
            {
                string itemToCheck = splitPath ? s.Split('/').LastOrDefault() : s;

                // If we have an ID, try to match by ID
                if (valueId != null)
                {
                    int itemBracketStart = itemToCheck.LastIndexOf('[');
                    if (itemBracketStart > 0)
                    {
                        string itemId = itemToCheck.Substring(itemBracketStart + 1, itemToCheck.Length - itemBracketStart - 2);
                        return itemId == valueId;
                    }
                }

                // Otherwise fall back to exact string match
                return itemToCheck == value;
            }));
        }
    }
}
