using ImpossibleRobert.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static AssetInventory.AssetTreeViewControl;
using Button = UnityEngine.UIElements.Button;
using Columns = AssetInventory.AssetTreeViewControl.Columns;
using Debug = UnityEngine.Debug;
using DisplayStyle = UnityEngine.UIElements.DisplayStyle;
using Image = UnityEngine.UIElements.Image;
using KeyDownEvent = UnityEngine.UIElements.KeyDownEvent;
using Label = UnityEngine.UIElements.Label;
using MultiColumnTreeView = UnityEngine.UIElements.MultiColumnTreeView;
using PopupStringField = UnityEngine.UIElements.PopupField<string>;
using ScrollView = UnityEngine.UIElements.ScrollView;
using ScrollViewMode = UnityEngine.UIElements.ScrollViewMode;
using StringChangeEvent = UnityEngine.UIElements.ChangeEvent<string>;
using TextField = UnityEngine.UIElements.TextField;
using Toggle = UnityEngine.UIElements.Toggle;
using UQueryExtensions = UnityEngine.UIElements.UQueryExtensions;
using VisualElement = UnityEngine.UIElements.VisualElement;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace AssetInventory
{
    public partial class IndexUI
    {
        private static readonly ProfilerMarker ProfileMarkerBulk = new ProfilerMarker("Bulk Download State");
        private const string PackagesRootClass = "ai-packages-root";
        private const string PackagesBodyClass = "ai-packages-body";
        private const string PackagesLayoutWithCollapsedTrailingPaneClass = "ai-packages-layout-with-collapsed-trailing-pane";
        private const string PackagesControlsClass = "ai-packages-controls";
        private const string PackagesRowClass = "ai-packages-row";
        private const string PackagesActionWrapperClass = "ai-packages-action-wrapper";
        private const string PackagesActionGroupClass = "ai-packages-action-group";
        private const string PackagesSearchGroupClass = "ai-packages-search-group";
        private const string PackagesLabelClass = "ai-packages-label";
        private const string PackagesSearchFieldClass = "ai-packages-search-field";
        private const string PackagesGoClass = "ai-packages-go";
        private const string PackagesSaveWrapperClass = "ai-packages-save-wrapper";
        private const string PackagesSaveClass = "ai-packages-save";
        private const string PackagesFilterChipClass = "ai-packages-filter-chip";
        private const string PackagesFilterChipLabelClass = "ai-packages-filter-chip-label";
        private const string PackagesFilterChipResetClass = "ai-packages-filter-chip-reset";
        private const string PackagesTypeSelectorClass = "ai-packages-type-selector";
        private const string PackagesTypeSegmentsClass = "ai-packages-type-segments";
        private const string PackagesPopupClass = "ai-packages-popup";
        private const string PackagesSortDirectionClass = "ai-packages-sort-direction";
        private const string PackagesTreeActionClass = "ai-packages-tree-action";
        private const string PackagesTreeClass = "ai-packages-tree";
        private const string PackagesGridClass = "ai-packages-grid";
        private const string PackagesGridEmptyClass = "ai-packages-grid-empty";
        private const string PackagesFooterClass = "ai-packages-footer";
        private const string PackagesFooterMinimumClass = "ai-packages-footer-minimum";
        private const string PackagesEmptyStateClass = "ai-packages-empty-state";
        private const string PackagesGridFallbackPreviewClass = "ai-packages-grid-preview-fallback";
        private const string PackagesGridStatusContainerClass = "ai-packages-grid-statuses";
        private const string PackagesGridStatusActiveClass = "ai-packages-grid-status-active";
        private const string PackagesGridSuccessBadgeClass = "ai-result-grid-badge-success";
        private const string PackagesGridAccentBadgeClass = "ai-result-grid-badge-accent";
        private const string PackagesGridWarningBadgeClass = "ai-result-grid-badge-warning";
        private const string PackagesGridDangerBadgeClass = "ai-result-grid-badge-danger";
        private const string PackagesInspectorClass = "ai-package-inspector";
        private const string PackagesInspectorScrollClass = "ai-package-inspector-scroll";
        private const string PackagesInspectorContentClass = "ai-package-inspector-content";
        private const string PackagesInspectorFiltersClass = "ai-package-inspector-filters";
        private const string PackagesInspectorDetailsClass = "ai-package-inspector-details";
        private const string InspectorWrappedHintClass = "ai-inspector-wrapped-hint";
        private const string PackagesInspectorHeaderButtonClass = "ai-package-inspector-header-button";
        private const string PackagesInspectorSettingsActiveClass = "ai-package-inspector-settings-active";
        private const string PackagesInspectorActionsClass = "ai-package-inspector-actions";
        private const string PackagesInspectorInlineClass = "ai-package-inspector-inline";
        private const string PackagesInspectorCompactFieldClass = "ai-package-inspector-compact-field";
        private const string PackagesInspectorStatActionsClass = "ai-package-inspector-stat-actions";
        private const string PackagesNarrowDetailsActionClass = "ai-narrow-details-action";
        private const string PackagesNarrowDetailsSelectionClass = "ai-narrow-details-selection";
        private static readonly string[] PackageGridDetailOptions = {"Tiny", "Compact", "Standard", "Detailed"};
        private static readonly int[] PackageGridDetailPresetSizes = {56, 88, 150, 230};

        internal struct BulkPackageDownloadSummary
        {
            public int NotDownloaded;
            public int UpdateAvailable;
            public int PackageUpdateAvailable;
            public int UpdateAvailableButCustom;
            public int Downloading;
            public int Paused;
            public long RemainingBytes;
        }

        private sealed class PackageTileStatusElements
        {
            public Label[] Badges;
            public float[] ActiveWidths;
            public float[] BadgeWidths;
        }

        private PackageSearch.MaintenanceOption _selectedMaintenance;
        private int _visiblePackageCount;
        private int _selectedMedia;
        private string _assetSearchPhrase;

        private int _usedByCacheAssetId;
        private List<PackageInfo> _usedByCache;

        // New filter fields (prefixed with Pkg to avoid conflicts with Search fields)
        private int _selectedPkgTag;
        private int _selectedPkgPublisher;
        private int _selectedPkgCategory;
        private int _selectedPkgPriceOption;
        private float _pkgSearchPrice;
        private int _selectedPkgUpdateDateOption;
        private DateTime? _pkgUpdateBeforeDate;
        private DateTime? _pkgUpdateAfterDate;
        private int _selectedPkgPurchaseDateOption;
        private DateTime? _pkgPurchaseBeforeDate;
        private DateTime? _pkgPurchaseAfterDate;
        private int _selectedPkgSizeOption;
        private float _pkgSizeMB;
        private int _selectedPkgUnityVersionOption;
        private string[] _updateDateOptions;
        private string[] _purchaseDateOptions;
        private string[] _packageSizeOptions;
        private string[] _unityVersionOptions;

        // Saved package searches
        private int _activeSavedPackageSearchId = -1;
        private List<SavedPackageSearch> _packageSearches;
        private bool _packageSearchesLoaded;

        private List<SavedPackageSearch> PackageSearches
        {
            get
            {
                if (_packageSearches == null || !_packageSearchesLoaded)
                {
                    _packageSearches = DBAdapter.DB.Table<SavedPackageSearch>().ToList();
                    _packageSearchesLoaded = true;
                }
                return _packageSearches;
            }
        }

        private Vector2 _assetsScrollPos;
        private Vector2 _bulkScrollPos;
        private Vector2 _imageScrollPos;
        private Rect _mediaRect;
        private ScrollView _nativePackageMediaScroll;
        private int _nativePackageMediaScrollAssetId;
        private bool _focusNativePackageMediaAfterRebuild;
        private float _nextAssetSearchTime;
        private ToolbarSearchField _nativePackageSearchField;
        private VisualElement _nativePackageSavedSearches;
        private VisualElement _nativePackageControls;
        private BulkPackageDownloadProgressView _nativeBulkPackageDownloadProgress;
        private UnityEngine.UIElements.PopupField<string> _nativePackageSortPopup;
        private UnityEngine.UIElements.PopupField<string> _nativePackageGroupPopup;
        private Button _nativePackageSortDirectionButton;
        private VisualElement _nativePackageFilterChip;
        private Button _nativePackageFilterChipLabel;
        private Button _nativePackageFilterChipReset;
        private VisualElement _nativePackageTypeControl;
        private MultiColumnTreeView _nativePackageTreeView;
        private NativeAssetTreeViewAdapter _nativePackageTreeAdapter;
        private CommonSelectableGridView<AssetInfo> _nativePackageGridView;
        private CommonGridSizeControl _nativePackageGridSizeControl;
        private PopupStringField _nativePackageTileDetailPopup;
        private Label _nativePackageGridEmpty;
        private CommonTabbedPane _nativePackageInspectorPane;
        private Button _nativePackageInspectorSettingsButton;
        private ScrollView _nativePackageInspectorScroll;
        private int _nativePackageInspectorScrollTab = int.MinValue;
        private CommonResizableSidePaneLayout _nativePackagePaneLayout;
        private VisualElement _nativePackageNarrowMain;
        private VisualElement _nativePackageNarrowDetails;
        private VisualElement _nativePackageNarrowDetailsAction;
        private Label _nativePackageNarrowDetailsSelection;
        private bool _nativePackageNarrowDetailsOpen;
        private int _nativePackageInspectorContentStateHash = int.MinValue;
        private readonly List<int> _nativePackageGridSelectionBuffer = new List<int>();
        private readonly HashSet<int> _nativePackageGridSelectionIds = new HashSet<int>();
        private VisualElement _nativePackagesFooter;
        private VisualElement _nativePackageViewModeControl;
        private Label _nativePackageFooterSummary;
        private VisualElement _nativePackageScopeControl;
        private bool _nativePackageSavedSearchesDirty = true;
        private bool _nativePackageSavedSearchesShowAdvanced;
        private int _nativePackagesAdvancedVisibilityStateHash;
        private int _nativePackagesHeaderStateHash;
        private bool _syncingNativePackageColumns;
        private bool _nativePackageTreeRefreshPending;
        private bool _nativePackageGridRefreshPending;
        private bool _nativePackageGridRefreshScheduled;
        private bool _nativePackageSearchRefreshScheduled;
        private List<int> _pendingNativePackageSelection;
        private bool _pendingNativePackageRevealSelection;

        private Vector2 _packageScrollPos;
        private GridControl PGrid
        {
            get
            {
                if (_pgrid == null)
                {
                    _pgrid = new GridControl();
                }
                return _pgrid;
            }
        }
        private GridControl _pgrid;

        [SerializeField] private CommonMultiColumnState assetColumnState;
        [SerializeField] private BulkPackageDownloadSession _bulkPackageDownloadSession;
        private int[] _packageColumnDisplayOrder;
        private AssetTreeViewControl AssetTreeView
        {
            get
            {
                if (_assetTreeView == null)
                {
                    CommonMultiColumnState columnState = CreateDefaultMultiColumnState();
                    columnState.VisibleColumns = GetDefaultVisiblePackageTreeColumns();
                    columnState = AssetInventoryColumnLayoutCoordinator.Restore(
                        AssetInventoryTableLayoutKind.Packages,
                        columnState,
                        AssetInventoryColumnLayoutCoordinator.GetPackageColumnKey,
                        AI.Config.assetSorting,
                        AI.Config.sortAssetsDescending,
                        out _packageColumnDisplayOrder,
                        out int sortIndex,
                        out bool sortDescending);
                    assetColumnState = columnState;
                    AI.Config.assetSorting = sortIndex;
                    AI.Config.sortAssetsDescending = sortDescending;
                    _assetTreeView = new AssetTreeViewControl(AssetTreeModel, GetBackupCountForPackageList, this);
                }
                return _assetTreeView;
            }
        }

        private void FlushColumnLayouts()
        {
            _nativePackageTreeAdapter?.FlushPendingColumnState();
            _nativeSearchTreeAdapter?.FlushPendingColumnState();
            _nativeReportTreeAdapter?.FlushPendingColumnState();
            AssetInventoryColumnLayoutCoordinator.Unregister(_nativePackageTreeAdapter);
            AssetInventoryColumnLayoutCoordinator.Unregister(_nativeSearchTreeAdapter);
            AssetInventoryColumnLayoutCoordinator.Unregister(_nativeReportTreeAdapter);
            AssetInventoryColumnLayoutCoordinator.Flush();
        }

        internal static int[] GetDefaultVisiblePackageTreeColumns()
        {
            return new[] {(int)Columns.Name, (int)Columns.Tags, (int)Columns.Version, (int)Columns.Indexed};
        }

        private AssetTreeViewControl _assetTreeView;
        private readonly List<int> _assetTreeSelectedIds = new List<int>();

        private TreeModel<AssetInfo> AssetTreeModel
        {
            get
            {
                if (_assetTreeModel == null) _assetTreeModel = new TreeModel<AssetInfo>(new List<AssetInfo> {new AssetInfo().WithTreeData("Root", depth: -1)});
                return _assetTreeModel;
            }
        }
        private TreeModel<AssetInfo> _assetTreeModel;

        private AssetInfo _selectedTreeAsset;
        private List<AssetInfo> _selectedTreeAssets;

        private Dictionary<int, List<BackupInfo>> _cachedBackupState;
        private Dictionary<int, List<BackupInfo>> _backupCountState;

        private long _assetTreeSelectionSize;
        private long _assetTreeSubPackageCount;
        private float _assetTreeSelectionTotalCosts;
        private float _assetTreeSelectionStoreCosts;
        private readonly Dictionary<string, Tuple<int, Color>> _assetBulkTags = new Dictionary<string, Tuple<int, Color>>();
        private int _selectionGeneration;
        private int _packageDetailsTab;
        private bool _metadataEditMode;
        private int _packageInspectorTab;

        private void OnPackageListUpdated()
        {
            if (!AI.IsInitialized) return;
            if (_assets == null) return;

            _usedByCacheAssetId = 0;
            _usedByCache = null;

            RequireTreesRebuild();

            Dictionary<string, PackageInfo> packages = AssetStore.GetAllPackages();
            Dictionary<string, AssetInfo> registryPackagesByName = new Dictionary<string, AssetInfo>(StringComparer.Ordinal);
            foreach (AssetInfo asset in _assets)
            {
                if (asset.AssetSource != Asset.Source.RegistryPackage || string.IsNullOrEmpty(asset.SafeName)) continue;
                if (!registryPackagesByName.ContainsKey(asset.SafeName)) registryPackagesByName[asset.SafeName] = asset;
            }

            bool hasChanges = false;
            foreach (KeyValuePair<string, PackageInfo> package in packages)
            {
                if (!registryPackagesByName.TryGetValue(package.Value.name, out AssetInfo info))
                {
                    // new package found, persist
                    if (PackageImporter.Persist(package.Value))
                    {
                        hasChanges = true;
                    }
                    continue;
                }

                info.Refresh();
                if (package.Value.versions.latestCompatible != info.LatestVersion && !package.Value.versions.latestCompatible.ToLowerInvariant().Contains("pre"))
                {
                    AI.SetPackageVersion(info, package.Value);
                    hasChanges = true;
                }
            }
            if (hasChanges)
            {
                _requireLookupUpdate = ChangeImpact.Write;
                RequireTreesRebuild();
                CalculateAssetUsageAutomatically();
            }
            else if (!_usageCalculationDone)
            {
                CalculateAssetUsageAutomatically();
            }
        }

        private void OnTagsChanged()
        {
            _tags = Tagging.LoadTags();
            _tagNames = Assets.ExtractTagNames(_tags);
            _tagPopupItems = Assets.ExtractTagPopupItems(_tags);

            _requireAssetTreeRebuild = true;
        }

        private void OnActionsDone()
        {
            _backupCountState = null;
            ReloadLookups();
            RequireTreesRebuild();
        }

        private void PrepareBackupCountStateForPackageList()
        {
            _backupCountState = ShouldUseBackupCountStateForPackageList()
                ? AssetBackup.GatherState()
                : null;
        }

        private bool ShouldUseBackupCountStateForPackageList()
        {
            if (AI.Config.assetSorting == (int)Columns.BackupCount) return true;

            int backupCountColumn = (int)Columns.BackupCount;
            if (assetColumnState?.VisibleColumns.Contains(backupCountColumn) == true) return true;

            return AI.Config.packageTableLayout?.columns?.Any(column =>
                column != null &&
                column.key == "BuiltIn:" + Columns.BackupCount &&
                column.visible) == true;
        }

        private int? GetBackupCountForPackageList(AssetInfo info)
        {
            if (info == null) return null;
            int backupKey = AssetBackup.GetBackupKey(info);
            if (backupKey == 0 || info.ParentId > 0) return null;
            if (info.AssetSource == Asset.Source.RegistryPackage || info.AssetSource == Asset.Source.CurrentProject) return null;

            _backupCountState ??= AssetBackup.GatherState();
            return _backupCountState.TryGetValue(backupKey, out List<BackupInfo> backups) && backups != null
                ? backups.Count
                : 0;
        }

        private static void InstallPackage(AssetInfo info, string version)
        {
            info.ForceTargetVersion(version);

            ImportUI importUI = ImportUI.ShowWindow();
            importUI.Init(new List<AssetInfo> {info}, true);
        }

        private async void ReindexPackageNow(AssetInfo info)
        {
            if (info == null) return;
            if (PackageIndexingPolicy.IsInheritedNoIndex(info))
            {
                EditorUtility.DisplayDialog("Indexing Controlled by Parent", $"'{info.GetDisplayName()}' inherits its indexing setting from '{info.ParentInfo?.GetDisplayName()}'. Select the parent package to change participation.", "OK");
                return;
            }

            if (info.Exclude) AI.SetAssetExclusion(info, false);
            if (info.NoIndex) AI.SetAssetNoIndex(info, false);
            await AI.Actions.ReindexAsync(info);
            _requireLookupUpdate = ChangeImpact.Write;
            _requireSearchUpdate = true;
            _requireAssetTreeRebuild = true;
            ScheduleNativePackageDetailsRebuild();
        }

        private async void IncludeAndIndexPackagesNow(IEnumerable<AssetInfo> packages, bool includeExcluded = false)
        {
            List<AssetInfo> selection = packages?
                .Where(info => info != null && info.ParentId <= 0 && info.AssetSource != Asset.Source.CurrentProject)
                .GroupBy(info => info.AssetId)
                .Select(group => group.First())
                .ToList() ?? new List<AssetInfo>();
            if (selection.Count == 0)
            {
                EditorUtility.DisplayDialog("No Indexable Packages", "Select one or more top-level packages. Nested packages inherit indexing participation from their parent.", "OK");
                return;
            }

            selection = selection.Where(CanIndexPackageNow).ToList();
            if (selection.Count == 0)
            {
                EditorUtility.DisplayDialog("Package Source Unavailable", "The selected package sources are not currently available for indexing.", "OK");
                return;
            }

            List<AssetInfo> excluded = selection.Where(info => info.Exclude).ToList();
            if (!includeExcluded) selection = selection.Where(info => !info.Exclude).ToList();
            if (selection.Count == 0)
            {
                EditorUtility.DisplayDialog("Packages Excluded", "Include the selected packages again before adding them to indexing.", "OK");
                return;
            }

            List<AssetInfo> downloads = selection
                .Where(info => !info.IsDownloaded && IsOnDemandPackageSource(info) && HasAssetStoreDownloadMetadata(info))
                .ToList();

            foreach (AssetInfo info in selection)
            {
                if (includeExcluded && info.Exclude) AI.SetAssetExclusion(info, false);
                if (info.NoIndex) AI.SetAssetNoIndex(info, false, false);
            }
            AI.TriggerPackageRefresh();

            List<AssetInfo> locallyAvailable = selection.Except(downloads).Where(CanIndexPackageNow).ToList();
            foreach (AssetInfo info in locallyAvailable)
            {
                if (AI.Actions.CancellationRequested) break;
                await AI.Actions.ReindexAsync(info);
            }
            if (!AI.Actions.CancellationRequested && downloads.Count > 0)
            {
                await AI.Actions.DownloadAndIndexSelectedAsync(downloads);
            }

            if (excluded.Count > 0 && !includeExcluded)
            {
                Debug.Log($"Skipped {excluded.Count} excluded package{(excluded.Count == 1 ? string.Empty : "s")} while indexing the selection.");
            }
            _requireLookupUpdate = ChangeImpact.Write;
            _requireSearchUpdate = true;
            _requireAssetTreeRebuild = true;
            ScheduleNativePackageDetailsRebuild();
        }

        private static bool CanIndexPackageNow(AssetInfo info)
        {
            if (info == null || info.IsAbandoned || info.AssetSource == Asset.Source.CurrentProject) return false;
            if (info.AssetSource == Asset.Source.AssetStorePackage) return info.IsDownloaded || HasAssetStoreDownloadMetadata(info);
            if (info.AssetSource == Asset.Source.Synty) return info.IsDownloaded;
            if (info.AssetSource == Asset.Source.AssetManager) return AI.Actions.AssetManagerEnabled;
            if (info.AssetSource == Asset.Source.Directory) return true;
            return info.IsDownloaded;
        }

        private void SetFutureIndexing(IEnumerable<AssetInfo> packages, bool included)
        {
            List<AssetInfo> roots = packages?
                .Where(info => info != null && info.ParentId <= 0 && info.AssetSource != Asset.Source.CurrentProject)
                .GroupBy(info => info.AssetId)
                .Select(group => group.First())
                .ToList() ?? new List<AssetInfo>();
            foreach (AssetInfo info in roots)
            {
                AI.SetAssetNoIndex(info, !included, false);
            }
            AI.TriggerPackageRefresh();
            _requireAssetTreeRebuild = true;
            ScheduleNativePackageDetailsRebuild();
        }

        private void RemoveIndexedContent(IEnumerable<AssetInfo> packages)
        {
            List<AssetInfo> roots = packages?
                .Where(info => info != null && info.ParentId <= 0 && PackageIndexingPolicy.HasNoIndex(info) && PackageIndexingPolicy.HasIndexedContent(info))
                .GroupBy(info => info.AssetId)
                .Select(group => group.First())
                .ToList() ?? new List<AssetInfo>();
            if (roots.Count == 0) return;

            string message = $"Remove indexed content for {roots.Count} package{(roots.Count == 1 ? string.Empty : "s")}?\n\nSearch records, generated previews, captions, and extracted caches will be removed. Package records and downloaded source archives will be kept.";
            if (!EditorUtility.DisplayDialog("Remove Indexed Content", message, "Remove Indexed Content", "Cancel")) return;

            roots.ForEach(Assets.RemoveIndexedContent);
            _requireLookupUpdate = ChangeImpact.Write;
            _requireSearchUpdate = true;
            _requireAssetTreeRebuild = true;
            ScheduleNativePackageDetailsRebuild();
        }

        private static async void ShowInExplorer(AssetInfo info)
        {
            string location = await info.GetLocation(true, true);
            EditorUtility.RevealInFinder(location);
        }

        private async void ConnectToAssetStore(AssetInfo info, AssetDetails details)
        {
            AI.ConnectToAssetStore(info, details);
            await new AssetStoreImporter().FetchAssetsDetails(false, info.AssetId);
            _requireLookupUpdate = ChangeImpact.Write;
            _requireAssetTreeRebuild = true;
        }

        private async void RefreshMetadataAsync(List<AssetInfo> bulkAssets)
        {
            // Filter to parent packages only and convert to Asset objects
            List<Asset> assetsToRefresh = bulkAssets
                .Where(info => info.ParentId <= 0 && info.ForeignId > 0)
                .Select(info => info.ToAsset())
                .ToList();

            if (assetsToRefresh.Count == 0) return;

            // Use existing chunked processing with global progress tracking
            await AI.Actions.RunWithProgress<AssetStoreImporter>(
                ActionHandler.ACTION_ASSET_STORE_DETAILS,
                "Updating package details",
                imp => imp.FetchAssetsDetails(assetsToRefresh, forceUpdate: true, resetEtag: true));

            // Trigger UI refresh
            AI.TriggerPackageRefresh();
        }

        private void RefreshNativePackagesBody()
        {
            if (_nativePackagesBody == null) return;

            RunNativePackageHeaderTimers();

            int headerStateHash = GetNativePackagesHeaderStateHash();
            if (_nativePackagesBody.childCount == 0 ||
                _nativePackagesHeaderStateHash != headerStateHash ||
                AssetInventoryUITK.AdvancedVisibilityStateChanged(ref _nativePackagesAdvancedVisibilityStateHash))
            {
                RebuildNativePackagesBody();
            }

            if (_requireAssetTreeRebuild && EnsurePackageTreeDataReady())
            {
                TriggerNativePackageSearch();
            }

            FlushNativePackageTreeRefresh();
            RefreshNativePackageHeaderState();
            RefreshNativeBulkPackageDownloadProgress();
            RefreshNativePackageGridView();
            RefreshNativePackageInspector();
            _nativePackageTreeAdapter?.RepaintCells();
        }

        private void RebuildNativePackagesBody()
        {
            if (_nativePackagesBody == null) return;

            CaptureNativePackageInspectorScroll();
            _nativePackagesBody.Clear();
            _nativePackagesBody.AddToClassList(PackagesRootClass);

            if (HasIndexedPackages())
            {
                VisualElement savedSearchBlock = AssetInventoryUITK.CreateAdvancedVisibilityBlock("package.savedsearches", () =>
                {
                    _nativePackageSavedSearches = AssetInventoryUITK.CreateSavedSearchStrip();
                    return _nativePackageSavedSearches;
                }, onVisibilityChanged: RebuildNativePackagesBody);
                _nativePackagesBody.Add(savedSearchBlock);

                _nativePackageControls = AssetInventoryUITK.CreateSection();
                _nativePackageControls.AddToClassList(PackagesControlsClass);
                _nativePackageControls.Add(CreateNativePackageHeaderRow());
                _nativeBulkPackageDownloadProgress = new BulkPackageDownloadProgressView(DismissNativeBulkPackageDownloadProgress);
                _nativePackageControls.Add(_nativeBulkPackageDownloadProgress);
                _nativePackagesBody.Add(_nativePackageControls);
            }
            else
            {
                _nativePackageSavedSearches = null;
                _nativePackageControls = null;
                _nativeBulkPackageDownloadProgress = null;
            }

            _nativePackageGridView = null;
            _nativePackageGridEmpty = null;
            _nativePackageInspectorPane = null;
            _nativePackageInspectorSettingsButton = null;
            _nativePackageInspectorScroll = null;
            _nativePackageInspectorScrollTab = int.MinValue;
            _nativePackageInspectorContentStateHash = int.MinValue;
            _nativePackageNarrowMain = null;
            _nativePackageNarrowDetails = null;
            _nativePackageNarrowDetailsAction = null;
            _nativePackageNarrowDetailsSelection = null;
            if (HasIndexedPackages())
            {
                VisualElement mainPane = new VisualElement();
                mainPane.AddToClassList(PackagesBodyClass);
                VisualElement resultPane = new VisualElement();
                resultPane.AddToClassList(ResultPaneHostClass);
                mainPane.Add(resultPane);

                if (AI.Config.packageViewMode == 0)
                {
                    _nativePackageTreeView = CreateNativePackageTreeView();
                    _nativePackageTreeView.AddToClassList(PackagesTreeClass);
                    PositionNativePackageResult(_nativePackageTreeView);
                    resultPane.Add(_nativePackageTreeView);
                }
                else
                {
                    _nativePackageTreeView = null;
                    _nativePackageTreeAdapter = null;
                    _nativePackageGridView = CreateNativePackageGridView();
                    PositionNativePackageResult(_nativePackageGridView);
                    resultPane.Add(_nativePackageGridView);

                    _nativePackageGridEmpty = new Label("No matching packages");
                    _nativePackageGridEmpty.AddToClassList(PackagesGridEmptyClass);
                    PositionNativePackageResult(_nativePackageGridEmpty);
                    resultPane.Add(_nativePackageGridEmpty);
                }

                _nativePackagesFooter = CreateNativePackagesFooter();
                mainPane.Add(_nativePackagesFooter);

                _nativePackageInspectorPane = CreateNativePackageInspectorPane();
                CommonResizableSidePaneLayout.PaneDefinition trailing = new CommonResizableSidePaneLayout.PaneDefinition
                {
                    Content = _nativePackageInspectorPane,
                    PreferredWidth = GetNativePackageInspectorPaneWidth(),
                    MinimumWidth = 220f,
                    MaximumWidth = 720f,
                    IsOpen = AI.Config.showPackageSideBar,
                    StateChanged = OnNativePackageInspectorPaneStateChanged
                };
                CommonResizableSidePaneLayout.LayoutOptions layoutOptions = new CommonResizableSidePaneLayout.LayoutOptions
                {
                    MainMinimumWidth = 360f,
                    CompactThreshold = 280f,
                    WideThreshold = 480f
                };
                if (UseNativeNarrowDetailsLayout())
                {
                    _nativePackagePaneLayout = AssetInventoryUITK.CreateResizableSidePaneLayout(mainPane, options: layoutOptions);
                    _nativePackageNarrowMain = _nativePackagePaneLayout;
                    _nativePackageNarrowMain.AddToClassList(NarrowDetailsMainClass);
                    _nativePackagesBody.Add(_nativePackageNarrowMain);

                    Button back = AssetInventoryUITK.CreateSecondaryButton("Results", CloseNativePackageNarrowDetails);
                    back.tooltip = "Return to the package results.";
                    back.AddToClassList(NarrowDetailsBackClass);
                    _nativePackageInspectorPane.Leading.Insert(0, back);
                    _nativePackageNarrowDetails = _nativePackageInspectorPane;
                    _nativePackageNarrowDetails.AddToClassList(NarrowDetailsViewClass);
                    _nativePackageNarrowDetails.RegisterCallback<KeyDownEvent>(OnNativePackageNarrowDetailsKeyDown);
                    _nativePackagesBody.Add(_nativePackageNarrowDetails);
                    ApplyNativePackageNarrowDetailsState();
                }
                else
                {
                    _nativePackageNarrowDetailsOpen = false;
                    _nativePackagePaneLayout = AssetInventoryUITK.CreateResizableSidePaneLayout(mainPane, trailing: trailing, options: layoutOptions);
                    _nativePackagesBody.Add(_nativePackagePaneLayout);
                }
                RefreshNativePackagePaneGutter();
            }
            else
            {
                _nativePackageTreeView = null;
                _nativePackageTreeAdapter = null;
                CommonEmptyState emptyState = AssetInventoryUITK.CreateEmptyState(
                    "No indexed packages",
                    "Run the indexing actions in Settings to populate the package browser.",
                    AssetInventoryUITK.CreatePrimaryButton("Open Indexing Settings", () => SelectUITKTab(AssetInventoryTab.Settings)));
                emptyState.AddToClassList(PackagesEmptyStateClass);
                _nativePackagesBody.Add(emptyState);
            }

            if (!HasIndexedPackages())
            {
                _nativePackagesFooter = null;
            }

            _nativePackageSavedSearchesDirty = true;
            _nativePackageSavedSearchesShowAdvanced = ShowAdvanced();
            _nativePackagesAdvancedVisibilityStateHash = AssetInventoryUITK.GetAdvancedVisibilityStateHash();
            _nativePackagesHeaderStateHash = GetNativePackagesHeaderStateHash();
            RefreshNativePackageHeaderState();
            RefreshNativeBulkPackageDownloadProgress();
            RefreshNativePackageGridView();
            RefreshNativePackageInspector();
        }

        private static void PositionNativePackageResult(VisualElement element)
        {
            if (element == null) return;

            element.style.position = UnityEngine.UIElements.Position.Absolute;
            element.style.left = 0f;
            element.style.top = 0f;
            element.style.bottom = 0f;
            element.style.right = 0f;
        }

        private CommonTabbedPane CreateNativePackageInspectorPane()
        {
            CommonTabbedPane pane = AssetInventoryUITK.CreateTabbedInspectorPane();
            pane.AddToClassList(PackagesInspectorClass);
            pane.SetTabs(GetNativePackageInspectorTabs(), GetNativePackageInspectorTabIndex(), SelectNativePackageInspectorTab);

            pane.Trailing.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("package.actions.settings", () =>
            {
                _nativePackageInspectorSettingsButton = AssetInventoryUITK.CreateIconButton(
                    "Manage View",
                    "Settings",
                    SelectNativePackageInspectorSettings);
                _nativePackageInspectorSettingsButton.AddToClassList(PackagesInspectorHeaderButtonClass);
                return _nativePackageInspectorSettingsButton;
            }, inlineControls: true, onVisibilityChanged: RebuildNativePackagesBody));

            RebuildNativePackageInspectorContent();
            pane.schedule.Execute(RefreshNativePackageInspector).Every(500);
            return pane;
        }

        private string[] GetNativePackageInspectorTabs()
        {
            int activeFilterCount = GetActivePackageFilterCount();
            string filtersLabel = activeFilterCount > 0 ? $"Filters ({activeFilterCount:N0})" : "Filters";
            return new[]
            {
                "Details",
                filtersLabel,
                "Stats"
            };
        }

        private int GetNativePackageInspectorTabIndex()
        {
            return _packageInspectorTab >= 0 && _packageInspectorTab <= 2 ? _packageInspectorTab : -1;
        }

        private void SelectNativePackageInspectorTab(int index)
        {
            if (_packageInspectorTab == index) return;

            _packageInspectorTab = index;
            _nativePackageInspectorContentStateHash = int.MinValue;
            RefreshNativePackageInspector();
        }

        private void SelectNativePackageInspectorSettings()
        {
            if (_packageInspectorTab == -1) return;

            _packageInspectorTab = -1;
            _nativePackageInspectorContentStateHash = int.MinValue;
            RefreshNativePackageInspector();
        }

        private void RefreshNativePackageInspector()
        {
            if (_nativePackageInspectorPane == null) return;

            _nativePackageInspectorPane.SetTabs(
                GetNativePackageInspectorTabs(),
                GetNativePackageInspectorTabIndex(),
                SelectNativePackageInspectorTab);
            _nativePackageInspectorSettingsButton?.EnableInClassList(
                PackagesInspectorSettingsActiveClass,
                _packageInspectorTab == -1);

            int contentStateHash = GetNativePackageInspectorContentStateHash();
            if (_nativePackageInspectorContentStateHash != contentStateHash)
            {
                RebuildNativePackageInspectorContent();
            }
        }

        private int GetNativePackageInspectorContentStateHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + _packageInspectorTab;
                hash = hash * 31 + (_selectedTreeAsset?.AssetId ?? -1);
                hash = hash * 31 + (_selectedTreeAssets?.Count ?? 0);
                hash = hash * 31 + (ShowAdvanced() ? 1 : 0);
                hash = hash * 31 + (IsPackageFilterActive() ? 1 : 0);
                hash = hash * 31 + AI.Config.packagesListing;
                hash = hash * 31 + AI.Config.assetSRPs;
                hash = hash * 31 + AI.Config.assetDeprecation;
                hash = hash * 31 + _selectedPkgTag;
                hash = hash * 31 + _selectedPkgPublisher;
                hash = hash * 31 + _selectedPkgCategory;
                hash = hash * 31 + _selectedPkgUpdateDateOption;
                hash = hash * 31 + _selectedPkgPurchaseDateOption;
                hash = hash * 31 + _selectedPkgPriceOption;
                hash = hash * 31 + _selectedPkgSizeOption;
                hash = hash * 31 + _selectedPkgUnityVersionOption;
                hash = hash * 31 + (int)_selectedMaintenance;
                hash = hash * 31 + _pkgSearchPrice.GetHashCode();
                hash = hash * 31 + _pkgSizeMB.GetHashCode();
                hash = hash * 31 + (_pkgUpdateBeforeDate?.GetHashCode() ?? 0);
                hash = hash * 31 + (_pkgUpdateAfterDate?.GetHashCode() ?? 0);
                hash = hash * 31 + (_pkgPurchaseBeforeDate?.GetHashCode() ?? 0);
                hash = hash * 31 + (_pkgPurchaseAfterDate?.GetHashCode() ?? 0);
                hash = hash * 31 + (_stats?.AllPackages ?? 0);
                hash = hash * 31 + (_stats?.IndexedPackages ?? 0);
                hash = hash * 31 + (_stats?.TotalFiles ?? 0);
                hash = hash * 31 + AI.Config.currency;
                hash = hash * 31 + (AI.Config.searchPackageGroupNames ? 1 : 0);
                hash = hash * 31 + (AI.Config.searchPackageDescriptions ? 1 : 0);
                hash = hash * 31 + (int)AI.Config.packageTileStatuses;
                hash = hash * 31 + (AI.Config.alwaysShowPackageDetails ? 1 : 0);
                hash = hash * 31 + (AI.Config.projectDetailTabs ? 1 : 0);
                hash = hash * 31 + AI.Config.rowHeight;
                hash = hash * 31 + AI.Config.rowHeightMedia;
                hash = hash * 31 + (AI.Config.mediaMaintainAspect ? 1 : 0);
                hash = hash * 31 + (AI.Config.mediaSameWidth ? 1 : 0);
                hash = hash * 31 + AI.Config.mediaYFillRatio;
                hash = hash * 31 + AI.Config.mediaXSpacing;
                hash = hash * 31 + AI.Config.mediaCornerRadius;
                hash = hash * 31 + GetNativePackageDetailsStateHash();
                return hash;
            }
        }

        private void RebuildNativePackageInspectorContent()
        {
            if (_nativePackageInspectorPane == null) return;

            CaptureNativePackageInspectorScroll();
            _nativePackageMediaScroll = null;
            _nativePackageMediaScrollAssetId = 0;
            _nativePackageTileDetailPopup = null;
            _nativePackageInspectorPane.Body.Clear();
            ScrollView scroll;
            if (_packageInspectorTab == 0)
            {
                scroll = CreateNativePackageDetailsInspector();
                _nativePackageInspectorPane.Body.Add(scroll);
            }
            else
            {
                scroll = new ScrollView(ScrollViewMode.Vertical);
                scroll.AddToClassList(PackagesInspectorScrollClass);
                VisualElement content = new VisualElement();
                content.AddToClassList(PackagesInspectorContentClass);
                scroll.Add(content);
                _nativePackageInspectorPane.Body.Add(scroll);

                switch (_packageInspectorTab)
                {
                    case -1:
                        content.Add(CreateNativePackageInspectorSettings());
                        break;
                    case 1:
                        content.Add(CreateNativePackageInspectorFilters());
                        break;
                    case 2:
                        content.Add(CreateNativePackageInspectorStats());
                        break;
                }
            }

            _nativePackageInspectorScroll = scroll;
            _nativePackageInspectorScrollTab = _packageInspectorTab;
            _nativePackageInspectorContentStateHash = GetNativePackageInspectorContentStateHash();
            _nativeScrollViewState.Restore(GetNativePackageInspectorScrollKey(_packageInspectorTab), scroll);
        }

        private void CaptureNativePackageInspectorScroll()
        {
            if (_nativePackageInspectorScroll == null || _nativePackageInspectorScrollTab == int.MinValue) return;

            _nativeScrollViewState.Capture(
                GetNativePackageInspectorScrollKey(_nativePackageInspectorScrollTab),
                _nativePackageInspectorScroll);
            if (_nativePackageMediaScroll != null && _nativePackageMediaScrollAssetId > 0)
            {
                _nativeScrollViewState.Capture(
                    GetNativePackageMediaScrollKey(_nativePackageMediaScrollAssetId),
                    _nativePackageMediaScroll);
            }
        }

        private static string GetNativePackageInspectorScrollKey(int tab)
        {
            return "package-inspector:" + tab;
        }

        private static string GetNativePackageMediaScrollKey(int assetId)
        {
            return "package-media:" + assetId;
        }

        internal static MaskField CreatePackageTileStatusMaskField(
            PackageTileStatus current,
            Action<PackageTileStatus> onChanged)
        {
            const string tooltip = "Choose which package statuses appear as pills on grid tiles.";
            PackageTileStatus normalized = PackageTileStatusModel.NormalizeMask((int)current);
            MaskField field = new MaskField(PackageTileStatusModel.SelectorLabels.ToList(), (int)normalized)
            {
                tooltip = tooltip
            };
            field.RegisterValueChangedCallback(evt =>
                onChanged?.Invoke(PackageTileStatusModel.NormalizeMask(evt.newValue)));
            return field;
        }

        private VisualElement CreateNativePackageInspectorSettings()
        {
            VisualElement root = new VisualElement();
            CommonFormBuilder form = AssetInventoryUITK.CreateFormBuilder();

            VisualElement search = AssetInventoryUITK.CreateSection("Search");
            search.Add(form.CreateRow(
                "Currency",
                "Currency to show asset prices in.",
                CreateNativePackageInspectorPopup(_currencyOptions, AI.Config.currency, value =>
                {
                    AI.Config.currency = value;
                    CommitNativePackageInspectorSettings();
                })));
            search.Add(form.CreateToggleRow(
                "Search Group Names",
                AI.Config.searchPackageGroupNames,
                value =>
                {
                    AI.Config.searchPackageGroupNames = value;
                    CommitNativePackageInspectorSettings();
                },
                "Also search category, publisher, tag, and other package group names."));
            search.Add(form.CreateToggleRow(
                "Search Descriptions",
                AI.Config.searchPackageDescriptions,
                value =>
                {
                    AI.Config.searchPackageDescriptions = value;
                    CommitNativePackageInspectorSettings();
                },
                "Also search package descriptions in addition to package names."));
            root.Add(search);

            VisualElement presentation = AssetInventoryUITK.CreateSection("Presentation");
            _nativePackageTileDetailPopup = CreateNativePackageInspectorPopup(
                PackageGridDetailOptions,
                (int)GetNativePackageGridDisplayMode(),
                SetNativePackageGridDetail);
            presentation.Add(form.CreateRow(
                "Tile Detail",
                "Choose the overall information density and matching tile-size preset.",
                _nativePackageTileDetailPopup));
            presentation.Add(form.CreateRow(
                "Tile Status Pills",
                "Choose which package statuses appear as pills on grid tiles.",
                CreatePackageTileStatusMaskField(AI.Config.packageTileStatuses, value =>
                {
                    if (AI.Config.packageTileStatuses == value) return;

                    AI.Config.packageTileStatuses = value;
                    AI.SaveConfig();
                    _nativePackageGridView?.RefreshItems();
                    _nativePackageInspectorContentStateHash = GetNativePackageInspectorContentStateHash();
                })));
            presentation.Add(form.CreateToggleRow(
                "Details when Compact",
                AI.Config.alwaysShowPackageDetails,
                value =>
                {
                    AI.Config.alwaysShowPackageDetails = value;
                    LoadMediaOnDemand(_selectedTreeAsset);
                    CommitNativePackageInspectorSettings(true);
                },
                "Show media, descriptions, dependencies, and other details without expanding the inspector."));
            presentation.Add(form.CreateToggleRow(
                "Details in Tabs",
                AI.Config.projectDetailTabs,
                value =>
                {
                    AI.Config.projectDetailTabs = value;
                    CommitNativePackageInspectorSettings();
                },
                "Group extended package details into tabs instead of one continuous view."));
            root.Add(presentation);

            VisualElement gallery = AssetInventoryUITK.CreateSection("Gallery");
            gallery.Add(form.CreateIntegerRow(
                "Maximum Image Height",
                AI.Config.mediaHeight,
                value =>
                {
                    AI.Config.mediaHeight = Mathf.Clamp(value, 120, 720);
                    CommitNativePackageInspectorSettings();
                },
                "pixels",
                "Maximum height of the selected package gallery image. Wide images use less height to preserve their aspect ratio without empty space above and below."));
            gallery.Add(form.CreateIntegerRow(
                "Thumbnail Width",
                AI.Config.mediaThumbnailWidth,
                value =>
                {
                    AI.Config.mediaThumbnailWidth = Mathf.Clamp(value, 48, 320);
                    CommitNativePackageInspectorSettings();
                },
                "pixels",
                "Width of package gallery thumbnails."));
            gallery.Add(form.CreateIntegerRow(
                "Thumbnail Height",
                AI.Config.mediaThumbnailHeight,
                value =>
                {
                    AI.Config.mediaThumbnailHeight = Mathf.Clamp(value, 40, 240);
                    CommitNativePackageInspectorSettings();
                },
                "pixels",
                "Height of package gallery thumbnails."));
            root.Add(gallery);

            if (ShowAdvanced())
            {
                VisualElement tree = AssetInventoryUITK.CreateSection("Tree and Media");
                tree.Add(form.CreateToggleRow(
                    "Colored Tree Tags",
                    AI.Config.showColoredPackageTreeTags,
                    value =>
                    {
                        AI.Config.showColoredPackageTreeTags = value;
                        CommitNativePackageInspectorSettings();
                    },
                    "Render the Tags column with colored tag chips."));
                tree.Add(form.CreateIntegerRow(
                    "Row Height",
                    AI.Config.rowHeight,
                    value =>
                    {
                        AI.Config.rowHeight = Mathf.Max(16, value);
                        CommitNativePackageInspectorSettings();
                    },
                    "pixels",
                    "Tree row height when no media column is visible."));
                tree.Add(form.CreateIntegerRow(
                    "Media Row Height",
                    AI.Config.rowHeightMedia,
                    value =>
                    {
                        AI.Config.rowHeightMedia = Mathf.Max(16, value);
                        CommitNativePackageInspectorSettings();
                    },
                    "pixels",
                    "Tree row height when the media column is visible."));
                tree.Add(form.CreateToggleRow(
                    "Maintain Aspect",
                    AI.Config.mediaMaintainAspect,
                    value =>
                    {
                        AI.Config.mediaMaintainAspect = value;
                        CommitNativePackageInspectorSettings();
                    },
                    "Keep media images at their original aspect ratio."));
                tree.Add(form.CreateToggleRow(
                    "Align Media Width",
                    AI.Config.mediaSameWidth,
                    value =>
                    {
                        AI.Config.mediaSameWidth = value;
                        CommitNativePackageInspectorSettings(true);
                    },
                    "Use a consistent width for calmer media rows."));
                if (!AI.Config.mediaSameWidth)
                {
                    tree.Add(form.CreateIntegerRow(
                        "Media Height",
                        AI.Config.mediaYFillRatio,
                        value =>
                        {
                            AI.Config.mediaYFillRatio = Mathf.Clamp(value, 1, 100);
                            CommitNativePackageInspectorSettings();
                        },
                        "%",
                        "Vertical percentage of the row occupied by media."));
                }
                tree.Add(form.CreateIntegerRow(
                    "Media Spacing",
                    AI.Config.mediaXSpacing,
                    value =>
                    {
                        AI.Config.mediaXSpacing = Mathf.Max(0, value);
                        CommitNativePackageInspectorSettings();
                    },
                    "pixels",
                    "Horizontal spacing between media images."));
                tree.Add(form.CreateIntegerRow(
                    "Corner Radius",
                    AI.Config.mediaCornerRadius,
                    value =>
                    {
                        AI.Config.mediaCornerRadius = Mathf.Max(0, value);
                        CommitNativePackageInspectorSettings();
                    },
                    "pixels",
                    "Corner radius of media images."));
                root.Add(tree);
            }

            return root;
        }

        private VisualElement CreateNativePackageInspectorFilters()
        {
            VisualElement root = new VisualElement();
            root.AddToClassList(PackagesInspectorFiltersClass);
            CommonFormBuilder form = AssetInventoryUITK.CreateFormBuilder();

            VisualElement essentials = AssetInventoryUITK.CreateSection("Filters");
            essentials.Add(form.CreateRow("Packages", "Filter packages by their catalog source.", CreateNativePackageInspectorPopup(_packageListingOptions, AI.Config.packagesListing, value =>
            {
                AI.Config.packagesListing = value;
                CommitNativePackageFilterChange();
            })));
            essentials.Add(form.CreateRow(
                "Package Tag",
                "Show packages carrying a specific tag.",
                AssetInventoryUITK.CreateSearchablePopupField(
                    this,
                    _tagPopupItems,
                    _selectedPkgTag,
                    value =>
                    {
                        _selectedPkgTag = value;
                        CommitNativePackageFilterChange();
                    },
                    AI.Config.colorTagFilterClosedField,
                    treatSlashLiterally: true)));
            essentials.Add(form.CreateRow(
                "Condition",
                "Show packages matching an indexing, download, update, or catalog-maintenance condition.",
                CreateNativePackageInspectorPopup(_maintenanceOptions, (int)_selectedMaintenance, value =>
                {
                    _selectedMaintenance = (PackageSearch.MaintenanceOption)value;
                    CommitNativePackageFilterChange(true);
                })));
            if (_selectedMaintenance == PackageSearch.MaintenanceOption.Duplicate)
            {
                VisualElement actions = new VisualElement();
                actions.AddToClassList(PackagesInspectorActionsClass);
                Button older = AssetInventoryUITK.CreateSecondaryButton("Older", SelectOlderDuplicates);
                older.tooltip = "Select older versions from duplicate package groups.";
                actions.Add(older);
                Button newer = AssetInventoryUITK.CreateSecondaryButton("Newer", SelectNewerDuplicates);
                newer.tooltip = "Select newer versions from duplicate package groups.";
                actions.Add(newer);
                Button noMapping = AssetInventoryUITK.CreateSecondaryButton("No Mapping", SelectWithoutRelativeLocation);
                noMapping.tooltip = "Select duplicates whose storage location cannot be mapped.";
                actions.Add(noMapping);
                Button custom = AssetInventoryUITK.CreateSecondaryButton("Custom", SelectCustomPackages);
                custom.tooltip = "Select duplicates from custom package sources.";
                actions.Add(custom);
                essentials.Add(actions);
            }
            root.Add(essentials);

            VisualElement compatibility = AssetInventoryUITK.CreateSection("Compatibility");
            compatibility.Add(form.CreateRow("SRPs", "Filter by render-pipeline compatibility.", CreateNativePackageInspectorPopup(_srpOptions, AI.Config.assetSRPs, value =>
            {
                AI.Config.assetSRPs = value;
                CommitNativePackageFilterChange();
            })));
            compatibility.Add(form.CreateRow(
                "Deprecation",
                "Filter by deprecation status or China Store migration.",
                CreateNativePackageInspectorPopup(_deprecationOptions, AI.Config.assetDeprecation, value =>
                {
                    AI.Config.assetDeprecation = value;
                    CommitNativePackageFilterChange();
                })));
            root.Add(compatibility);

            VisualElement metadata = AssetInventoryUITK.CreateSection("Metadata");
            metadata.Add(form.CreateRow(
                "Publisher",
                "Show packages from a specific publisher.",
                AssetInventoryUITK.CreateSearchablePopupField(this, _publisherNames, _selectedPkgPublisher, value =>
                {
                    _selectedPkgPublisher = value;
                    CommitNativePackageFilterChange();
                })));
            metadata.Add(form.CreateRow(
                "Category",
                "Show packages in a specific category.",
                AssetInventoryUITK.CreateSearchablePopupField(this, _categoryNames, _selectedPkgCategory, value =>
                {
                    _selectedPkgCategory = value;
                    CommitNativePackageFilterChange();
                })));
            root.Add(metadata);

            VisualElement range = AssetInventoryUITK.CreateSection("Date and Size");
            range.Add(CreateNativePackageDateFilterRow(form, "Updated", _updateDateOptions, _selectedPkgUpdateDateOption, true));
            range.Add(CreateNativePackageDateFilterRow(form, "Purchased", _purchaseDateOptions, _selectedPkgPurchaseDateOption, false));
            range.Add(CreateNativePackagePriceFilterRow(form));
            range.Add(CreateNativePackageSizeFilterRow(form));
            range.Add(form.CreateRow("Unity Version", "Filter by the newest Unity version supported by a package.", CreateNativePackageInspectorPopup(_unityVersionOptions, _selectedPkgUnityVersionOption, value =>
            {
                _selectedPkgUnityVersionOption = value;
                CommitNativePackageFilterChange();
            })));
            root.Add(range);

            if (IsPackageFilterActive())
            {
                Button reset = AssetInventoryUITK.CreateSecondaryButton("Reset Filters", ResetNativePackageFilters);
                reset.tooltip = "Clear all package filters.";
                reset.AddToClassList(PackagesInspectorCompactFieldClass);
                root.Add(reset);
            }
            return root;
        }

        private VisualElement CreateNativePackageInspectorStats()
        {
            VisualElement section = AssetInventoryUITK.CreateSection();
            if (_stats == null)
            {
                section.Add(AssetInventoryUITK.CreateHelpBox("Statistics are not available yet."));
                return section;
            }

            AddNativePackageStat(section, "Catalogued", _stats.TotalPackages);
            AddNativePackageStat(section, "Indexing Enabled", _stats.IndexingEnabledPackages,
                "Packages included in future indexing runs.");
            AddNativePackageStat(section, "Indexed", $"{_stats.EnabledIndexedPackages:N0}/{_stats.IndexingEnabledPackages:N0}",
                "Indexed packages among those currently included in indexing.");
            AddNativePackageStat(section, "Needs Indexing", _stats.NeedsIndexingPackages);
            AddNativePackageStat(section, "Asset Store", _stats.PurchasedAssets);
            AddNativePackageStat(section, "Registries", _stats.RegistryPackages);
            AddNativePackageStat(section, "Other Sources", _stats.CustomPackages);
            AddNativePackageStat(section, "Deprecated", _stats.DeprecatedPackages);
            AddNativePackageStat(section, "Abandoned", _stats.AbandonedPackages);
            AddNativePackageStat(section, "Not Included", _stats.NoIndexPackages);
            if (_stats.IndexedWithoutFutureIndexingPackages > 0)
            {
                AddNativePackageStat(section, "Indexed, Future Off", _stats.IndexedWithoutFutureIndexingPackages);
            }
            AddNativePackageStat(section, "Sub-Packages", _stats.SubPackages);
            AddNativePackageStat(section, "Indexed Files", _stats.TotalFiles);

            if (_stats.ExcludedPackages > 0)
            {
                VisualElement row = AssetInventoryUITK.CreateKeyValueRow("Excluded", $"{_stats.ExcludedPackages:N0}");
                row.AddToClassList(PackagesInspectorStatActionsClass);
                Button show = AssetInventoryUITK.CreateIconButton(
                    "Show excluded packages",
                    "d_animationvisibilitytoggleon",
                    () => ShowPackageMaintenance(PackageSearch.MaintenanceOption.Excluded));
                row.Add(show);
                section.Add(row);
            }
            return section;
        }

        private VisualElement CreateNativePackageDateFilterRow(
            CommonFormBuilder form,
            string label,
            string[] options,
            int selectedOption,
            bool updatedDate)
        {
            PopupStringField popup = CreateNativePackageInspectorPopup(options, selectedOption, value =>
            {
                if (updatedDate) _selectedPkgUpdateDateOption = value;
                else _selectedPkgPurchaseDateOption = value;
                CommitNativePackageFilterChange(true);
            });

            if (selectedOption != 6 && selectedOption != 7) return form.CreateRow(label, null, popup);

            VisualElement inline = new VisualElement();
            inline.AddToClassList(PackagesInspectorInlineClass);
            inline.Add(popup);

            DateTime? current = updatedDate
                ? (selectedOption == 6 ? _pkgUpdateBeforeDate : _pkgUpdateAfterDate)
                : (selectedOption == 6 ? _pkgPurchaseBeforeDate : _pkgPurchaseAfterDate);
            TextField date = new TextField
            {
                value = current?.ToString("yyyy-MM-dd") ?? DateTime.Now.ToString("yyyy-MM-dd"),
                isDelayed = true,
                tooltip = "Date in YYYY-MM-DD format"
            };
            date.AddToClassList(PackagesInspectorCompactFieldClass);
            date.RegisterValueChangedCallback(evt =>
            {
                if (!DateTime.TryParse(evt.newValue, out DateTime parsedDate)) return;
                if (updatedDate)
                {
                    if (selectedOption == 6) _pkgUpdateBeforeDate = parsedDate;
                    else _pkgUpdateAfterDate = parsedDate;
                }
                else
                {
                    if (selectedOption == 6) _pkgPurchaseBeforeDate = parsedDate;
                    else _pkgPurchaseAfterDate = parsedDate;
                }
                CommitNativePackageFilterChange();
            });
            inline.Add(date);
            return form.CreateRow(label, null, inline);
        }

        private VisualElement CreateNativePackagePriceFilterRow(CommonFormBuilder form)
        {
            PopupStringField popup = CreateNativePackageInspectorPopup(_priceOptions, _selectedPkgPriceOption, value =>
            {
                _selectedPkgPriceOption = value;
                CommitNativePackageFilterChange(true);
            });
            if (_selectedPkgPriceOption != 4 && _selectedPkgPriceOption != 5) return form.CreateRow("Price", null, popup);

            VisualElement inline = new VisualElement();
            inline.AddToClassList(PackagesInspectorInlineClass);
            inline.Add(popup);
            UnityEngine.UIElements.FloatField price = new UnityEngine.UIElements.FloatField
            {
                value = _pkgSearchPrice,
                isDelayed = true
            };
            price.AddToClassList(PackagesInspectorCompactFieldClass);
            price.RegisterValueChangedCallback(evt =>
            {
                _pkgSearchPrice = Mathf.Max(0f, evt.newValue);
                CommitNativePackageFilterChange();
            });
            inline.Add(price);
            inline.Add(new Label(AI.Config.currency == 0 ? "EUR" : AI.Config.currency == 1 ? "USD" : "CNY"));
            return form.CreateRow("Price", null, inline);
        }

        private VisualElement CreateNativePackageSizeFilterRow(CommonFormBuilder form)
        {
            PopupStringField popup = CreateNativePackageInspectorPopup(_packageSizeOptions, _selectedPkgSizeOption, value =>
            {
                _selectedPkgSizeOption = value;
                CommitNativePackageFilterChange(true);
            });
            if (_selectedPkgSizeOption != 2 && _selectedPkgSizeOption != 3) return form.CreateRow("Package Size", null, popup);

            VisualElement inline = new VisualElement();
            inline.AddToClassList(PackagesInspectorInlineClass);
            inline.Add(popup);
            UnityEngine.UIElements.FloatField size = new UnityEngine.UIElements.FloatField
            {
                value = _pkgSizeMB,
                isDelayed = true
            };
            size.AddToClassList(PackagesInspectorCompactFieldClass);
            size.RegisterValueChangedCallback(evt =>
            {
                _pkgSizeMB = Mathf.Max(0f, evt.newValue);
                CommitNativePackageFilterChange();
            });
            inline.Add(size);
            inline.Add(new Label("MB"));
            return form.CreateRow("Package Size", null, inline);
        }

        private PopupStringField CreateNativePackageInspectorPopup(string[] options, int selectedIndex, Action<int> onChanged)
        {
            List<string> items = options?.ToList() ?? new List<string>();
            if (items.Count == 0) items.Add(string.Empty);
            PopupStringField popup = new PopupStringField(items, Mathf.Clamp(selectedIndex, 0, items.Count - 1));
            popup.RegisterValueChangedCallback(evt =>
            {
                int index = items.IndexOf(evt.newValue);
                if (index >= 0) onChanged?.Invoke(index);
            });
            return popup;
        }

        private void CommitNativePackageFilterChange(bool rebuildContent = false)
        {
            ClearActiveSavedPackageSearch();
            AI.SaveConfig();
            TriggerNativePackageSearch();
            RefreshNativePackageFilterChip();
            _nativePackageInspectorPane?.SetTabs(
                GetNativePackageInspectorTabs(),
                GetNativePackageInspectorTabIndex(),
                SelectNativePackageInspectorTab);
            CompleteNativePackageInspectorControlChange(rebuildContent);
        }

        private void CommitNativePackageInspectorSettings(bool rebuildContent = false)
        {
            AI.SaveConfig();
            TriggerNativePackageSearch();
            CompleteNativePackageInspectorControlChange(rebuildContent);
        }

        private void CompleteNativePackageInspectorControlChange(bool rebuildContent)
        {
            if (!rebuildContent)
            {
                _nativePackageInspectorContentStateHash = GetNativePackageInspectorContentStateHash();
                return;
            }

            _nativePackageInspectorContentStateHash = int.MinValue;
            _nativePackageInspectorPane?.schedule.Execute(RefreshNativePackageInspector).ExecuteLater(0);
        }

        private void ResetNativePackageFilters()
        {
            ResetPackageFilters();
            _nativePackageInspectorContentStateHash = int.MinValue;
            TriggerNativePackageSearch();
            RefreshNativePackageFilterChip();
            _nativePackageInspectorPane?.schedule.Execute(RefreshNativePackageInspector).ExecuteLater(0);
        }

        private static void AddNativePackageStat(VisualElement parent, string label, int value, string tooltip = null)
        {
            if (value <= 0 && label != "Total Packages") return;
            AddNativePackageStat(parent, label, $"{value:N0}", tooltip);
        }

        private static void AddNativePackageStat(VisualElement parent, string label, string value, string tooltip = null)
        {
            VisualElement row = AssetInventoryUITK.CreateKeyValueRow(label, value);
            row.tooltip = tooltip ?? value;
            parent.Add(row);
        }

        private MultiColumnTreeView CreateNativePackageTreeView()
        {
            AssetTreeViewControl renderer = AssetTreeView;
            IList<int> previousSelection = new List<int>(_assetTreeSelectedIds);
            _nativePackageTreeAdapter = new NativeAssetTreeViewAdapter(
                assetColumnState,
                renderer,
                "AI4.Packages.AssetTree",
                true,
                SyncNativePackageColumnState,
                OnNativePackageSortChanged,
                PopulatePackageGridContextMenu,
                _packageColumnDisplayOrder);
            AssetInventoryColumnLayoutCoordinator.Register(
                AssetInventoryTableLayoutKind.Packages,
                _nativePackageTreeAdapter,
                assetColumnState,
                AssetInventoryColumnLayoutCoordinator.GetPackageColumnKey);
            _nativePackageTreeAdapter.SelectionChanged += OnNativePackageTreeSelectionChanged;
            _nativePackageTreeAdapter.ItemChosen += info => OnAssetTreeDoubleClicked(info.TreeId);
            _nativePackageTreeAdapter.SyncSort(AI.Config.assetSorting, AI.Config.sortAssetsDescending);
            _nativePackageTreeAdapter.SetRoot(AssetTreeModel.Root, previousSelection);
            _nativePackageTreeAdapter.View.RegisterCallback<KeyDownEvent>(OnNativePackageTreeKeyDown);
            return _nativePackageTreeAdapter.View;
        }

        private CommonSelectableGridView<AssetInfo> CreateNativePackageGridView()
        {
            CommonSelectableGridView<AssetInfo> grid = new CommonSelectableGridView<AssetInfo>(
                CreateNativePackageGridTile,
                BindNativePackageGridTile,
                AssetInventoryUITK.ResultGridClass,
                PackagesGridClass);
            grid.SelectionChanged += OnNativePackageGridSelectionChanged;
            grid.ItemActivated += OnNativePackageGridItemActivated;
            grid.ContextRequested += OnNativePackageGridContextRequested;
            grid.LayoutChanged += OnNativePackageGridLayoutChanged;
            grid.ScrollOffsetChanged += offset => _packageScrollPos = offset;
            grid.RegisterCallback<KeyDownEvent>(OnNativePackageGridKeyDown);
            grid.SetDisplayMode(GetNativePackageGridDisplayMode());
            grid.SetLayout(AI.Config.packageTileSize, 1f, AI.Config.tileMargin, AI.Config.enlargeTiles);
            return grid;
        }

        private static CommonGridViewDisplayMode GetNativePackageGridDisplayMode()
        {
            return CommonGridSizeControl.GetDefaultDisplayMode(AI.Config.packageTileSize);
        }

        private VisualElement CreateNativePackageGridTile()
        {
            Label[] badges = new Label[PackageTileStatusModel.PriorityOrder.Count];
            for (int i = 0; i < badges.Length; i++)
            {
                PackageTileStatus status = PackageTileStatusModel.PriorityOrder[i];
                string modifierClass;
                switch (PackageTileStatusModel.GetTone(status))
                {
                    case PackageTileStatusTone.Danger:
                        modifierClass = PackagesGridDangerBadgeClass;
                        break;
                    case PackageTileStatusTone.Success:
                        modifierClass = PackagesGridSuccessBadgeClass;
                        break;
                    case PackageTileStatusTone.Warning:
                        modifierClass = PackagesGridWarningBadgeClass;
                        break;
                    default:
                        modifierClass = PackagesGridAccentBadgeClass;
                        break;
                }

                badges[i] = AssetInventoryUITK.CreateResultGridBadge(string.Empty, null, modifierClass);
                badges[i].style.display = DisplayStyle.None;
            }

            VisualElement tile = AssetInventoryUITK.CreateResultGridTile(badges);
            VisualElement statusContainer = UQueryExtensions.Q<VisualElement>(tile, "badges");
            statusContainer.userData = new PackageTileStatusElements
            {
                Badges = badges,
                ActiveWidths = new float[badges.Length],
                BadgeWidths = new float[badges.Length]
            };
            statusContainer.AddToClassList(PackagesGridStatusContainerClass);
            statusContainer.RegisterCallback<GeometryChangedEvent>(OnPackageTileStatusGeometryChanged);
            return tile;
        }

        private void BindNativePackageGridTile(VisualElement tile, AssetInfo info, int index)
        {
            Texture content = PGrid.GetPreview(index);
            Image preview = UQueryExtensions.Q<Image>(tile, "preview");
            Texture previewTexture = content != null ? content : info?.GetFallbackIcon();
            preview.image = previewTexture;
            bool isFallbackPreview = content == null ||
                (previewTexture != null && previewTexture.width <= 64 && previewTexture.height <= 64);
            preview.EnableInClassList(PackagesGridFallbackPreviewClass, isFallbackPreview);

            CommonGridViewDisplayMode displayMode = GetNativePackageGridDisplayMode();
            bool showTitle = displayMode != CommonGridViewDisplayMode.Tiny
                && AI.Config.packageTileSize >= AI.Config.noPackageTileTextBelow;
            Label label = UQueryExtensions.Q<Label>(tile, "label");
            label.text = showTitle ? info?.GetDisplayName() ?? string.Empty : string.Empty;
            label.style.display = showTitle ? DisplayStyle.Flex : DisplayStyle.None;

            Label subtitle = UQueryExtensions.Q<Label>(tile, "subtitle");
            bool showSubtitle = displayMode == CommonGridViewDisplayMode.Detailed;
            subtitle.text = showSubtitle ? GetNativePackageGridSubtitle(info) : string.Empty;
            subtitle.style.display = showSubtitle && !string.IsNullOrWhiteSpace(subtitle.text)
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            VisualElement statusContainer = UQueryExtensions.Q<VisualElement>(tile, "badges");
            PackageTileStatusElements statusElements = statusContainer?.userData as PackageTileStatusElements;
            Label[] badges = statusElements?.Badges;
            if (statusElements?.BadgeWidths != null)
            {
                Array.Clear(statusElements.BadgeWidths, 0, statusElements.BadgeWidths.Length);
            }
            bool anyStatusVisible = ApplyPackageTileStatusBadges(
                badges,
                info,
                AI.Config.packageTileStatuses,
                _assets,
                displayMode != CommonGridViewDisplayMode.Tiny);
            if (statusContainer != null)
            {
                statusContainer.style.display = anyStatusVisible ? DisplayStyle.Flex : DisplayStyle.None;
                ApplyPackageTileStatusOverflow(statusContainer);
            }

            tile.tooltip = info == null
                ? string.Empty
                : $"{info.GetDisplayName()}\n{GetNativePackageGridSubtitle(info).Replace("\n", " | ")}";
        }

        internal static bool ApplyPackageTileStatusBadges(
            Label[] badges,
            AssetInfo info,
            PackageTileStatus selectedStatuses,
            List<AssetInfo> allAssets,
            bool showBadges)
        {
            if (badges == null) return false;

            for (int i = 0; i < badges.Length; i++)
            {
                if (badges[i] == null) continue;

                badges[i].EnableInClassList(PackagesGridStatusActiveClass, false);
                badges[i].style.display = DisplayStyle.None;
            }
            if (!showBadges || info == null) return false;

            bool anyVisible = false;
            int count = Math.Min(badges.Length, PackageTileStatusModel.PriorityOrder.Count);
            for (int i = 0; i < count; i++)
            {
                Label badge = badges[i];
                if (badge == null) continue;

                PackageTileStatus status = PackageTileStatusModel.PriorityOrder[i];
                bool visible = PackageTileStatusModel.IsSelected(selectedStatuses, status)
                    && PackageTileStatusModel.IsActive(status, info, allAssets);
                if (!visible) continue;

                badge.text = PackageTileStatusModel.GetTileLabel(status, info);
                badge.tooltip = PackageTileStatusModel.GetTooltip(status, info);
                badge.EnableInClassList(PackagesGridStatusActiveClass, true);
                badge.style.display = DisplayStyle.Flex;
                anyVisible = true;
            }
            return anyVisible;
        }

        private static void OnPackageTileStatusGeometryChanged(GeometryChangedEvent evt)
        {
            ApplyPackageTileStatusOverflow(evt.target as VisualElement);
        }

        private static void ApplyPackageTileStatusOverflow(VisualElement statusContainer)
        {
            if (statusContainer == null || statusContainer.resolvedStyle.display == DisplayStyle.None) return;

            PackageTileStatusElements elements = statusContainer.userData as PackageTileStatusElements;
            if (elements?.Badges == null || elements.ActiveWidths == null || elements.BadgeWidths == null) return;

            float availableWidth = statusContainer.contentRect.width;
            if (availableWidth <= 0f) return;

            int activeCount = 0;
            for (int i = 0; i < elements.Badges.Length; i++)
            {
                Label badge = elements.Badges[i];
                if (badge == null || !badge.ClassListContains(PackagesGridStatusActiveClass)) continue;

                float badgeWidth = elements.BadgeWidths[i];
                if (badge.resolvedStyle.display != DisplayStyle.None || badgeWidth <= 0f)
                {
                    Vector2 textSize = badge.MeasureTextSize(
                        badge.text,
                        0f,
                        VisualElement.MeasureMode.Undefined,
                        0f,
                        VisualElement.MeasureMode.Undefined);
                    IResolvedStyle style = badge.resolvedStyle;
                    badgeWidth = textSize.x
                        + style.marginLeft
                        + style.marginRight
                        + style.paddingLeft
                        + style.paddingRight
                        + style.borderLeftWidth
                        + style.borderRightWidth;
                    elements.BadgeWidths[i] = badgeWidth;
                }
                elements.ActiveWidths[activeCount] = badgeWidth;
                activeCount++;
            }

            int visibleCount = GetFittingPackageTileStatusCount(
                elements.ActiveWidths,
                activeCount,
                availableWidth);
            int activeIndex = 0;
            for (int i = 0; i < elements.Badges.Length; i++)
            {
                Label badge = elements.Badges[i];
                if (badge == null || !badge.ClassListContains(PackagesGridStatusActiveClass)) continue;

                DisplayStyle display = activeIndex < visibleCount ? DisplayStyle.Flex : DisplayStyle.None;
                if (badge.style.display.value != display)
                {
                    badge.style.display = display;
                }
                activeIndex++;
            }
        }

        internal static int GetFittingPackageTileStatusCount(
            IReadOnlyList<float> badgeWidths,
            float availableWidth)
        {
            return GetFittingPackageTileStatusCount(
                badgeWidths,
                badgeWidths?.Count ?? 0,
                availableWidth);
        }

        private static int GetFittingPackageTileStatusCount(
            IReadOnlyList<float> badgeWidths,
            int badgeCount,
            float availableWidth)
        {
            if (badgeWidths == null || badgeCount <= 0 || availableWidth <= 0f) return 0;

            float usedWidth = 0f;
            int visibleCount = 0;
            int count = Math.Min(badgeCount, badgeWidths.Count);
            for (int i = 0; i < count; i++)
            {
                float badgeWidth = Mathf.Max(0f, badgeWidths[i]);
                if (usedWidth + badgeWidth > availableWidth) break;

                usedWidth += badgeWidth;
                visibleCount++;
            }
            return visibleCount;
        }

        private static string GetNativePackageGridSubtitle(AssetInfo info)
        {
            if (info == null) return string.Empty;

            string publisher = info.GetDisplayPublisher();
            string version = string.IsNullOrWhiteSpace(info.Version) ? "Version unknown" : $"Version {info.Version}";
            string size = info.PackageSize > 0 ? EditorUtility.FormatBytes(info.PackageSize) : "Size unknown";
            string details = $"{version} | {size}";
            return string.IsNullOrWhiteSpace(publisher) ? details : $"{publisher}\n{details}";
        }

        private void RefreshNativePackageGridView()
        {
            if (_nativePackageGridView == null) return;

            IList<AssetInfo> items = PGrid.packages ?? (IList<AssetInfo>)Array.Empty<AssetInfo>();
            if (!ReferenceEquals(_nativePackageGridView.ItemsSource, items) &&
                Event.current != null &&
                Event.current.type == EventType.Layout)
            {
                QueueNativePackageGridRefresh();
                return;
            }

            if (!ReferenceEquals(_nativePackageGridView.ItemsSource, items))
            {
                HashSet<int> selectedIds = new HashSet<int>();
                if (PGrid.selectionItems != null)
                {
                    for (int i = 0; i < PGrid.selectionItems.Count; i++)
                    {
                        AssetInfo selected = PGrid.selectionItems[i];
                        if (selected != null) selectedIds.Add(selected.AssetId);
                    }
                }

                int activeAssetId = -1;
                if (selectedIds.Count > 0 &&
                    PGrid.selectionTile >= 0 &&
                    PGrid.selectionTile < items.Count &&
                    items[PGrid.selectionTile] != null &&
                    selectedIds.Contains(items[PGrid.selectionTile].AssetId))
                {
                    activeAssetId = items[PGrid.selectionTile].AssetId;
                }
                List<int> selectedIndices = new List<int>();
                int activeIndex = -1;
                for (int i = 0; i < items.Count; i++)
                {
                    AssetInfo item = items[i];
                    if (item == null) continue;
                    if (selectedIds.Contains(item.AssetId)) selectedIndices.Add(i);
                    if (item.AssetId == activeAssetId) activeIndex = i;
                }

                _nativePackageGridView.SetItems(items);
                _nativePackageGridView.SetSelection(selectedIndices, activeIndex);
                if (_packageScrollPos != Vector2.zero)
                {
                    _nativePackageGridView.ScrollView.scrollOffset = _packageScrollPos;
                }
            }
            else
            {
                _nativePackageGridView.RefreshItems();
            }

            _nativePackageGridView.SetLayout(AI.Config.packageTileSize, 1f, AI.Config.tileMargin, AI.Config.enlargeTiles);
            _nativePackageGridView.SetDisplayMode(GetNativePackageGridDisplayMode());
            _nativePackageGridView.style.display = items.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_nativePackageGridEmpty != null)
            {
                _nativePackageGridEmpty.style.display = items.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void OnNativePackageGridSelectionChanged(IReadOnlyList<int> indices, int activeIndex)
        {
            PGrid.SetVisualSelectionIndices(indices, activeIndex);
            if (indices != null && indices.Count > 0) _packageInspectorTab = 0;
            HandleAssetGridSelectionChanged();
            RefreshNativePackageInspector();
            RefreshNativePackageNarrowDetailsAction();
        }

        private void OnNativePackageGridItemActivated(AssetInfo info, int index, bool alt)
        {
            if (info == null) return;
            PGrid.LastClickAlt = alt;
            OnPackageGridDoubleClicked(info);
        }

        private void OnNativePackageGridContextRequested(AssetInfo info, int index)
        {
            GenericMenu menu = new GenericMenu();
            PopulatePackageGridContextMenu(menu, PGrid.selectionItems, index);
            menu.ShowAsContext();
        }

        private void OnNativePackageGridLayoutChanged(int columns, float tileWidth, float tileHeight)
        {
            PGrid.SetLayoutMetrics(columns, tileHeight);
        }

        private void SyncNativePackageGridSelectionFromBackend()
        {
            if (_nativePackageGridView == null || PGrid.packages == null) return;

            _nativePackageGridSelectionIds.Clear();
            if (PGrid.selectionItems != null)
            {
                for (int i = 0; i < PGrid.selectionItems.Count; i++)
                {
                    AssetInfo selected = PGrid.selectionItems[i];
                    if (selected != null) _nativePackageGridSelectionIds.Add(selected.AssetId);
                }
            }

            _nativePackageGridSelectionBuffer.Clear();
            for (int i = 0; i < PGrid.packages.Count; i++)
            {
                AssetInfo item = PGrid.packages[i];
                if (item != null && _nativePackageGridSelectionIds.Contains(item.AssetId))
                {
                    _nativePackageGridSelectionBuffer.Add(i);
                }
            }

            int activeIndex = _nativePackageGridSelectionBuffer.Contains(PGrid.selectionTile)
                ? PGrid.selectionTile
                : -1;
            _nativePackageGridView.SetSelection(_nativePackageGridSelectionBuffer, activeIndex);
        }

        private void OnNativePackageTreeKeyDown(KeyDownEvent evt)
        {
            if (!HandleTagShortcut(evt.keyCode, evt.modifiers)) return;

            evt.StopPropagation();
        }

        private void OnNativePackageGridKeyDown(KeyDownEvent evt)
        {
            if (!HandleTagShortcut(evt.keyCode, evt.modifiers)) return;

            evt.StopPropagation();
        }

        private void OnNativePackageTreeSelectionChanged(IList<int> ids)
        {
            _assetTreeSelectedIds.Clear();
            _assetTreeSelectedIds.AddRange(ids);
            if (ids != null && ids.Any(id => id > 0)) _packageInspectorTab = 0;
            OnAssetTreeSelectionChanged(ids);
            RefreshNativePackageInspector();
            RefreshNativePackageNarrowDetailsAction();
        }

        private IList<int> GetCurrentPackageTreeSelection()
        {
            if (_nativePackageTreeAdapter != null) return _nativePackageTreeAdapter.GetSelectedModelIds();
            return new List<int>(_assetTreeSelectedIds);
        }

        private void SyncNativePackageColumnState()
        {
            if (_syncingNativePackageColumns || _nativePackageTreeAdapter == null || assetColumnState == null) return;

            _syncingNativePackageColumns = true;
            try
            {
                AssetInventoryColumnLayoutCoordinator.UpdateColumns(
                    AssetInventoryTableLayoutKind.Packages,
                    _nativePackageTreeAdapter,
                    assetColumnState,
                    AssetInventoryColumnLayoutCoordinator.GetPackageColumnKey);
            }
            finally
            {
                _syncingNativePackageColumns = false;
            }
        }

        private void OnNativePackageSortChanged(int sourceColumnIndex, bool descending)
        {
            if (AI.Config.assetSorting == sourceColumnIndex && AI.Config.sortAssetsDescending == descending) return;

            AI.Config.assetSorting = sourceColumnIndex;
            AI.Config.sortAssetsDescending = descending;
            AssetInventoryColumnLayoutCoordinator.UpdateSort(
                AssetInventoryTableLayoutKind.Packages,
                _nativePackageTreeAdapter,
                assetColumnState,
                AssetInventoryColumnLayoutCoordinator.GetPackageColumnKey,
                sourceColumnIndex,
                descending);
            TriggerNativePackageSearch();
            RefreshNativePackageHeaderState();
        }

        private void QueueNativePackageTreeRefresh(IList<int> selection, bool revealSelection)
        {
            if (_nativePackageTreeAdapter == null || _nativePackageTreeView == null) return;

            _pendingNativePackageSelection = selection?.Distinct().ToList() ?? new List<int>();
            _pendingNativePackageRevealSelection = revealSelection;
            _nativePackageTreeRefreshPending = true;
        }

        private void FlushNativePackageTreeRefresh()
        {
            if (!_nativePackageTreeRefreshPending) return;

            _nativePackageTreeRefreshPending = false;
            if (_nativePackageTreeAdapter == null || _nativePackageTreeView == null) return;

            _nativePackageTreeAdapter.SetRoot(
                AssetTreeModel.Root,
                _pendingNativePackageSelection,
                _pendingNativePackageRevealSelection);
            _nativePackageTreeAdapter.SyncSort(AI.Config.assetSorting, AI.Config.sortAssetsDescending);
            RefreshNativePackageFooterState();
            _nativePackageTreeAdapter.RepaintCells();
        }

        private void QueueNativePackageGridRefresh()
        {
            if (_nativePackageGridView == null) return;

            _nativePackageGridRefreshPending = true;
            if (_nativePackagesBody == null || _nativePackageGridRefreshScheduled) return;

            _nativePackageGridRefreshScheduled = true;
            _nativePackagesBody.schedule.Execute(() =>
            {
                _nativePackageGridRefreshScheduled = false;
                FlushNativePackageGridRefresh();
            }).ExecuteLater(0);
        }

        private void FlushNativePackageGridRefresh()
        {
            if (!_nativePackageGridRefreshPending) return;

            _nativePackageGridRefreshPending = false;
            RefreshNativePackageGridView();
        }

        private void ExpandNativePackageTree()
        {
            _nativePackageTreeAdapter?.ExpandAll();
        }

        private void CollapseNativePackageTree()
        {
            if (_nativePackageTreeAdapter == null) return;

            _nativePackageTreeAdapter.CollapseAll();
        }

        private VisualElement CreateNativePackageHeaderRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(PackagesRowClass);
            row.AddToClassList(AssetInventoryUITK.CompactSearchToolbarClass);

            VisualElement searchBlock = AssetInventoryUITK.CreateAdvancedVisibilityBlock("package.actions.search", () =>
            {
                VisualElement group = new VisualElement();
                group.AddToClassList(PackagesActionGroupClass);
                group.AddToClassList(PackagesSearchGroupClass);

                Label label = new Label("Search");
                label.AddToClassList(PackagesLabelClass);
                group.Add(label);

                _nativePackageSearchField = new ToolbarSearchField
                {
                    value = _assetSearchPhrase ?? string.Empty,
                    tooltip = "Search indexed packages."
                };
                _nativePackageSearchField.AddToClassList(PackagesSearchFieldClass);
                _nativePackageSearchField.RegisterCallback<StringChangeEvent>(evt => SetNativePackageSearchPhrase(evt.newValue));
                _nativePackageSearchField.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;

                    TriggerNativePackageSearch();
                    evt.StopPropagation();
                });
                group.Add(_nativePackageSearchField);

                Button go = AssetInventoryUITK.CreatePrimaryButton("Go", TriggerNativePackageSearch);
                go.tooltip = "Apply the current package search and filters.";
                go.AddToClassList(PackagesGoClass);
                group.Add(go);

                VisualElement saveBlock = AssetInventoryUITK.CreateAdvancedVisibilityBlock("package.actions.savedsearches", () =>
                {
                    Button save = null;
                    save = AssetInventoryUITK.CreateIconButton(
                        "Save current package filters",
                        "d_saveas",
                        () =>
                        {
                            NameWindow.ShowAsDropDown(
                                CommonUITK.ToScreenDropdownAnchor(this, save),
                                string.IsNullOrEmpty(_assetSearchPhrase) ? "My Package Search" : _assetSearchPhrase,
                                SavePackageSearch);
                        });
                    save.AddToClassList(PackagesSaveClass);
                    return save;
                }, onVisibilityChanged: RebuildNativePackagesBody);
                saveBlock.AddToClassList(PackagesSaveWrapperClass);
                group.Add(saveBlock);

                return group;
            }, onVisibilityChanged: RebuildNativePackagesBody);
            searchBlock.AddToClassList(PackagesActionWrapperClass);
            row.Add(searchBlock);

            _nativePackageFilterChip = CreateNativePackageFilterChip();
            row.Add(_nativePackageFilterChip);

            row.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("package.actions.typeselector", CreateNativePackageTypeSelector, onVisibilityChanged: RebuildNativePackagesBody));

            if (AI.Config.assetGrouping == 0 || AI.Config.packageViewMode == 1)
            {
                row.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("package.actions.sort", CreateNativePackageSortControls, onVisibilityChanged: RebuildNativePackagesBody));
            }

            if (AI.Config.packageViewMode == 0)
            {
                row.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("package.actions.group", CreateNativePackageGroupControls, onVisibilityChanged: RebuildNativePackagesBody));
            }

            return row;
        }

        private VisualElement CreateNativePackageFilterChip()
        {
            VisualElement chip = new VisualElement();
            chip.AddToClassList(PackagesFilterChipClass);

            _nativePackageFilterChipLabel = AssetInventoryUITK.CreateSecondaryButton(string.Empty, OpenNativePackageFilters);
            _nativePackageFilterChipLabel.tooltip = "Open the active package filters.";
            _nativePackageFilterChipLabel.AddToClassList(PackagesFilterChipLabelClass);
            chip.Add(_nativePackageFilterChipLabel);

            _nativePackageFilterChipReset = AssetInventoryUITK.CreateSecondaryButton("×", ResetNativePackageFilters);
            _nativePackageFilterChipReset.tooltip = "Reset all package filters";
            _nativePackageFilterChipReset.AddToClassList(PackagesFilterChipResetClass);
            chip.Add(_nativePackageFilterChipReset);

            RefreshNativePackageFilterChip();
            return chip;
        }

        private void RefreshNativePackageFilterChip()
        {
            if (_nativePackageFilterChip == null) return;

            int count = GetActivePackageFilterCount();
            _nativePackageFilterChip.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_nativePackageFilterChipLabel != null)
            {
                _nativePackageFilterChipLabel.text = count == 1 ? "1 filter active" : $"{count:N0} filters active";
            }
        }

        private void OpenNativePackageFilters()
        {
            _packageInspectorTab = 1;
            _nativePackageInspectorContentStateHash = int.MinValue;
            if (UseNativeNarrowDetailsLayout() && _nativePackageNarrowDetails != null)
            {
                _nativePackageNarrowDetailsOpen = true;
                ApplyNativePackageNarrowDetailsState();
            }
            else
            {
                _nativePackagePaneLayout?.SetPaneOpen(CommonSidePane.Trailing, true, true);
            }
            RefreshNativePackageInspector();
        }

        private VisualElement CreateNativePackageTypeSelector()
        {
            VisualElement group = new VisualElement();
            group.AddToClassList(PackagesActionGroupClass);
            group.AddToClassList(PackagesTypeSelectorClass);

            Label label = new Label("Types");
            label.AddToClassList(PackagesLabelClass);
            group.Add(label);

            _nativePackageTypeControl = AssetInventoryUITK.CreateSegmentedControl(
                _packageListingOptionsShort,
                AI.Config.packagesListing,
                SelectNativePackageListing);
            _nativePackageTypeControl.AddToClassList(PackagesTypeSegmentsClass);
            group.Add(_nativePackageTypeControl);

            return group;
        }

        private VisualElement CreateNativePackageSortControls()
        {
            VisualElement group = new VisualElement();
            group.AddToClassList(PackagesActionGroupClass);

            Label label = new Label("Sort");
            label.AddToClassList(PackagesLabelClass);
            group.Add(label);

            _nativePackageSortPopup = CreateNativePackagePopup(_packageSortOptions, AI.Config.assetSorting, "Specify how packages should be sorted.", PackagesPopupClass, SelectNativePackageSort);
            group.Add(_nativePackageSortPopup);

            _nativePackageSortDirectionButton = AssetInventoryUITK.CreateSecondaryButton(
                GetNativePackageSortDirectionLabel(),
                ToggleNativePackageSortDirection);
            _nativePackageSortDirectionButton.tooltip = GetNativePackageSortDirectionTooltip();
            _nativePackageSortDirectionButton.AddToClassList(PackagesSortDirectionClass);
            group.Add(_nativePackageSortDirectionButton);

            return group;
        }

        private VisualElement CreateNativePackageGroupControls()
        {
            VisualElement group = new VisualElement();
            group.AddToClassList(PackagesActionGroupClass);

            Label label = new Label("Group");
            label.AddToClassList(PackagesLabelClass);
            group.Add(label);

            _nativePackageGroupPopup = CreateNativePackagePopup(_groupByOptions, AI.Config.assetGrouping, "Select if packages should be grouped or not.", PackagesPopupClass, SelectNativePackageGroup);
            group.Add(_nativePackageGroupPopup);

            if (AI.Config.packageViewMode == 0)
            {
                AddPackageTreeExpansionControls(group, ExpandNativePackageTree, CollapseNativePackageTree);
            }

            return group;
        }

        internal static void AddPackageTreeExpansionControls(VisualElement group, Action expandAll, Action collapseAll)
        {
            Button expand = AssetInventoryUITK.CreateSecondaryButton("Expand", expandAll);
            expand.tooltip = "Expand all groups and sub-packages.";
            expand.AddToClassList(PackagesTreeActionClass);
            group.Add(expand);

            Button collapse = AssetInventoryUITK.CreateSecondaryButton("Collapse", collapseAll);
            collapse.tooltip = "Collapse all groups and sub-packages without clearing selection.";
            collapse.AddToClassList(PackagesTreeActionClass);
            group.Add(collapse);
        }

        private UnityEngine.UIElements.PopupField<string> CreateNativePackagePopup(string[] options, int selectedIndex, string tooltip, string className, Action<int> onChanged)
        {
            List<string> items = options?.ToList() ?? new List<string>();
            if (items.Count == 0) items.Add(string.Empty);
            int clampedIndex = Mathf.Clamp(selectedIndex, 0, items.Count - 1);
            UnityEngine.UIElements.PopupField<string> popup = new UnityEngine.UIElements.PopupField<string>(items, clampedIndex)
            {
                tooltip = tooltip
            };
            popup.AddToClassList(className);
            popup.RegisterCallback<StringChangeEvent>(evt =>
            {
                int index = items.IndexOf(evt.newValue);
                if (index < 0) return;

                onChanged?.Invoke(index);
            });
            return popup;
        }

        private void RefreshNativePackageHeaderState()
        {
            if (_nativePackagesBody == null || _nativePackagesBody.childCount == 0) return;

            RefreshNativePackageSavedSearches();

            string phrase = _assetSearchPhrase ?? string.Empty;
            if (_nativePackageSearchField != null && _nativePackageSearchField.value != phrase)
            {
                _nativePackageSearchField.SetValueWithoutNotify(phrase);
            }

            RefreshNativePackageListingButtons();
            RefreshNativePackagePopup(_nativePackageSortPopup, _packageSortOptions, AI.Config.assetSorting);
            RefreshNativePackagePopup(_nativePackageGroupPopup, _groupByOptions, AI.Config.assetGrouping);
            if (_nativePackageSortDirectionButton != null)
            {
                _nativePackageSortDirectionButton.text = GetNativePackageSortDirectionLabel();
                _nativePackageSortDirectionButton.tooltip = GetNativePackageSortDirectionTooltip();
            }

            RefreshNativePackageFilterChip();
            RefreshNativePackageFooterState();
        }

        private void RefreshNativePackageSavedSearches()
        {
            if (_nativePackageSavedSearches == null) return;

            int searchCount = PackageSearches.Count;
            bool showAdvanced = ShowAdvanced();
            if (_nativePackageSavedSearchesDirty ||
                _nativePackageSavedSearches.childCount != searchCount ||
                _nativePackageSavedSearchesShowAdvanced != showAdvanced)
            {
                RebuildNativePackageSavedSearches();
                return;
            }

            for (int i = 0; i < searchCount; i++)
            {
                VisualElement group = _nativePackageSavedSearches.ElementAt(i);
                Button button = AssetInventoryUITK.FindSavedSearchPill(group);
                if (button != null)
                {
                    AssetInventoryUITK.SetSavedSearchActive(button, PackageSearches[i].Id == _activeSavedPackageSearchId);
                }
            }
        }

        private void RebuildNativePackageSavedSearches()
        {
            if (_nativePackageSavedSearches == null) return;

            _nativePackageSavedSearches.Clear();
            _nativePackageSavedSearches.style.display = PackageSearches.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            _nativePackageSavedSearchesShowAdvanced = ShowAdvanced();
            foreach (SavedPackageSearch search in PackageSearches)
            {
                _nativePackageSavedSearches.Add(CreateNativeSavedPackageSearchPillGroup(search, _nativePackageSavedSearchesShowAdvanced));
            }
            _nativePackageSavedSearchesDirty = false;
        }

        private VisualElement CreateNativeSavedPackageSearchPillGroup(SavedPackageSearch search, bool hasMenu)
        {
            return AssetInventoryUITK.CreateSavedSearchPillGroup(
                GetNativeSavedPackageSearchLabel(search),
                search.SearchPhrase ?? string.Empty,
                search.Icon,
                search.Color,
                search.Id == _activeSavedPackageSearchId,
                hasMenu,
                () => SelectNativeSavedPackageSearch(search),
                anchor => ShowNativeSavedPackageSearchMenu(search, anchor),
                search);
        }

        private static string GetNativeSavedPackageSearchLabel(SavedPackageSearch search)
        {
            if (!string.IsNullOrWhiteSpace(search.Name)) return search.Name;
            if (!string.IsNullOrWhiteSpace(search.SearchPhrase)) return search.SearchPhrase;
            return "Package Search";
        }

        private void SelectNativeSavedPackageSearch(SavedPackageSearch search)
        {
            if (_activeSavedPackageSearchId == search.Id)
            {
                ResetPackageFilters();
                _activeSavedPackageSearchId = -1;
                _assetSearchPhrase = string.Empty;
            }
            else
            {
                LoadPackageSearch(search);
            }

            _nativePackageSavedSearchesDirty = true;
            TriggerNativePackageSearch();
            RefreshNativePackageHeaderState();
        }

        private void ShowNativeSavedPackageSearchMenu(SavedPackageSearch search, VisualElement anchor)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Edit"), false, () =>
            {
                SavedPackageSearchUI savedSearchUI = SavedPackageSearchUI.ShowWindow();
                savedSearchUI.Init(search, OnNativeSavedPackageSearchEdited);
            });
            menu.AddItem(new GUIContent("Override with Current Filters"), false, () =>
            {
                OverrideSavedPackageSearch(search);
                _nativePackageSavedSearchesDirty = true;
                RefreshNativePackageHeaderState();
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Delete"), false, () =>
            {
                DeletePackageSearch(search);
                _nativePackageSavedSearchesDirty = true;
                RefreshNativePackageHeaderState();
            });
            CommonUITK.ShowGenericMenu(menu, anchor);
        }

        private void OnNativeSavedPackageSearchEdited(SavedPackageSearch search)
        {
            _nativePackageSavedSearchesDirty = true;
            RefreshNativePackageHeaderState();
        }

        private void RefreshNativePackageListingButtons()
        {
            AssetInventoryUITK.RefreshSegmentedControl(_nativePackageTypeControl, AI.Config.packagesListing);
        }

        private static void RefreshNativePackagePopup(UnityEngine.UIElements.PopupField<string> popup, string[] options, int selectedIndex)
        {
            if (popup == null || options == null || options.Length == 0) return;

            int clampedIndex = Mathf.Clamp(selectedIndex, 0, options.Length - 1);
            if (popup.index != clampedIndex)
            {
                popup.SetValueWithoutNotify(options[clampedIndex]);
            }
        }

        private void SetNativePackageSearchPhrase(string value)
        {
            string nextPhrase = value ?? string.Empty;
            if (nextPhrase == (_assetSearchPhrase ?? string.Empty)) return;

            _assetSearchPhrase = nextPhrase;
            ClearActiveSavedPackageSearch();
            _nextAssetSearchTime = Time.realtimeSinceStartup + AI.Config.searchDelay;
            RefreshNativePackageHeaderState();
        }

        private void ClearActiveSavedPackageSearch()
        {
            if (_activeSavedPackageSearchId < 0) return;

            _activeSavedPackageSearchId = -1;
            _nativePackageSavedSearchesDirty = true;
        }

        private void TriggerNativePackageSearch()
        {
            _nextAssetSearchTime = 0;
            _requireAssetTreeRebuild = true;
            Repaint();

            if (_nativePackagesBody == null || _nativePackageSearchRefreshScheduled) return;

            _nativePackageSearchRefreshScheduled = true;
            _nativePackagesBody.schedule.Execute(() =>
            {
                _nativePackageSearchRefreshScheduled = false;
                if (!_requireAssetTreeRebuild) return;

                CreateAssetTree();
                FlushNativePackageTreeRefresh();
                RefreshNativePackageHeaderState();
                _nativePackageTreeAdapter?.RepaintCells();
            }).ExecuteLater(0);
        }

        private void RunNativePackageHeaderTimers()
        {
            if (_nextAssetSearchTime <= 0 || Time.realtimeSinceStartup <= _nextAssetSearchTime) return;

            TriggerNativePackageSearch();
        }

        private void SelectNativePackageListing(int index)
        {
            if (AI.Config.packagesListing == index) return;

            ClearActiveSavedPackageSearch();
            AI.Config.packagesListing = index;
            AI.SaveConfig();
            TriggerNativePackageSearch();
            RefreshNativePackageHeaderState();
        }

        private void SelectNativePackageSort(int index)
        {
            if (AI.Config.assetSorting == index) return;

            if (assetColumnState == null) _ = AssetTreeView;
            AI.Config.assetSorting = index;
            AssetInventoryColumnLayoutCoordinator.UpdateSort(
                AssetInventoryTableLayoutKind.Packages,
                null,
                assetColumnState,
                AssetInventoryColumnLayoutCoordinator.GetPackageColumnKey,
                index,
                AI.Config.sortAssetsDescending);
            AssetInventoryColumnLayoutCoordinator.Flush();
            TriggerNativePackageSearch();
            RefreshNativePackageHeaderState();
        }

        private void ToggleNativePackageSortDirection()
        {
            if (assetColumnState == null) _ = AssetTreeView;
            AI.Config.sortAssetsDescending = !AI.Config.sortAssetsDescending;
            AssetInventoryColumnLayoutCoordinator.UpdateSort(
                AssetInventoryTableLayoutKind.Packages,
                null,
                assetColumnState,
                AssetInventoryColumnLayoutCoordinator.GetPackageColumnKey,
                AI.Config.assetSorting,
                AI.Config.sortAssetsDescending);
            AssetInventoryColumnLayoutCoordinator.Flush();
            TriggerNativePackageSearch();
            RefreshNativePackageHeaderState();
        }

        private void SelectNativePackageGroup(int index)
        {
            if (AI.Config.assetGrouping == index) return;

            AI.Config.assetGrouping = index;
            AI.SaveConfig();
            TriggerNativePackageSearch();
            RebuildNativePackagesBody();
        }

        private static string GetNativePackageSortDirectionLabel()
        {
            return AI.Config.sortAssetsDescending ? "v" : "^";
        }

        private static string GetNativePackageSortDirectionTooltip()
        {
            return AI.Config.sortAssetsDescending
                ? "Currently descending. Click to sort ascending."
                : "Currently ascending. Click to sort descending.";
        }

        private bool HasIndexedPackages()
        {
            return _stats != null && _stats.AllPackages > 0;
        }

        private int GetNativePackagesHeaderStateHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (HasIndexedPackages() ? 1 : 0);
                hash = hash * 31 + AI.Config.packageViewMode;
                hash = hash * 31 + AI.Config.assetGrouping;
                hash = hash * 31 + (AI.Config.showPackageSideBar ? 1 : 0);
                hash = hash * 31 + (_packageSearches?.Count ?? 0);
                hash = hash * 31 + (UseNativeNarrowDetailsLayout() ? 1 : 0);
                return hash;
            }
        }

        private VisualElement CreateNativePackagesFooter()
        {
            CommonUITK.ThreeZoneLayout footer = AssetInventoryUITK.CreateNavigationFooterLayout();
            footer.Root.AddToClassList(PackagesFooterClass);
            footer.Root.RegisterCallback<GeometryChangedEvent>(evt =>
                footer.Root.EnableInClassList(PackagesFooterMinimumClass, evt.newRect.width < 700f));

            VisualElement leftGroup = footer.Left;
            leftGroup.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("package.actions.viewmode", () =>
            {
                _nativePackageViewModeControl = AssetInventoryUITK.CreateSegmentedControl(GetPackageViewOptions(), AI.Config.packageViewMode, SelectNativePackageViewMode);
                return _nativePackageViewModeControl;
            }, onVisibilityChanged: RebuildNativePackagesBody));

            if (AI.Config.packageViewMode == 1)
            {
                leftGroup.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("package.actions.tilesize", () =>
                {
                    _nativePackageGridSizeControl = AssetInventoryUITK.CreateGridSizeControl(
                        AI.Config.packageTileSize,
                        50,
                        300,
                        SetNativePackageGridSize,
                        false);
                    return _nativePackageGridSizeControl;
                }, onVisibilityChanged: RebuildNativePackagesBody));
            }
            VisualElement centerGroup = footer.Center;
            _nativePackageFooterSummary = new Label();
            _nativePackageFooterSummary.AddToClassList(AssetInventoryUITK.NavigationFooterSummaryClass);
            centerGroup.Add(_nativePackageFooterSummary);

            centerGroup.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("package.actions.scope", () =>
            {
                _nativePackageScopeControl = CreateNativeSearchScopeControl(() =>
                {
                    TriggerNativePackageSearch();
                    _requireSearchUpdate = true;
                    RefreshNativePackageFooterState();
                });
                return _nativePackageScopeControl;
            }, onVisibilityChanged: RebuildNativePackagesBody));

            if (UseNativeNarrowDetailsLayout())
            {
                footer.Root.AddToClassList(NarrowDetailsFooterClass);
                _nativePackageNarrowDetailsAction = new VisualElement();
                _nativePackageNarrowDetailsAction.AddToClassList(PackagesNarrowDetailsActionClass);
                _nativePackageNarrowDetailsSelection = new Label();
                _nativePackageNarrowDetailsSelection.AddToClassList(PackagesNarrowDetailsSelectionClass);
                _nativePackageNarrowDetailsAction.Add(_nativePackageNarrowDetailsSelection);
                Button details = AssetInventoryUITK.CreateSecondaryButton("Details", OpenNativePackageNarrowDetails);
                details.tooltip = "Open details for the current package selection.";
                _nativePackageNarrowDetailsAction.Add(details);
                footer.Right.Add(_nativePackageNarrowDetailsAction);
            }
            RefreshNativePackageFooterState();
            return footer.Root;
        }

        private void RefreshNativePackageFooterState()
        {
            if (_nativePackagesFooter == null) return;

            AssetInventoryUITK.RefreshSegmentedControl(_nativePackageViewModeControl, AI.Config.packageViewMode);
            RefreshNativeSearchScopeControl(_nativePackageScopeControl);

            _nativePackageGridSizeControl?.SetValueWithoutNotify(AI.Config.packageTileSize);
            if (_nativePackageFooterSummary != null)
            {
                _nativePackageFooterSummary.text = $"{_visiblePackageCount:N0} packages";
            }
            RefreshNativePackageNarrowDetailsAction();
        }

        private void RefreshNativePackageNarrowDetailsAction()
        {
            if (_nativePackageNarrowDetailsAction == null) return;

            int selectionCount = _selectedTreeAssets?.Count ?? 0;
            _nativePackageNarrowDetailsAction.style.display = selectionCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_nativePackageNarrowDetailsSelection == null || selectionCount == 0) return;

            _nativePackageNarrowDetailsSelection.text = selectionCount == 1 && _selectedTreeAsset != null
                ? _selectedTreeAsset.GetDisplayName()
                : $"{selectionCount:N0} packages selected";
            _nativePackageNarrowDetailsSelection.tooltip = _nativePackageNarrowDetailsSelection.text;
        }

        private void OpenNativePackageNarrowDetails()
        {
            if (_nativePackageNarrowMain == null || _nativePackageNarrowDetails == null) return;
            if (_selectedTreeAssets == null || _selectedTreeAssets.Count == 0) return;

            _nativePackageNarrowDetailsOpen = true;
            RefreshNativePackageInspector();
            ApplyNativePackageNarrowDetailsState();
            _nativePackageNarrowDetails.Focus();
        }

        private void CloseNativePackageNarrowDetails()
        {
            _nativePackageNarrowDetailsOpen = false;
            ApplyNativePackageNarrowDetailsState();
            if (_nativePackageTreeView != null)
            {
                _nativePackageTreeView.Focus();
            }
            else
            {
                _nativePackageGridView?.Focus();
            }
        }

        private void ApplyNativePackageNarrowDetailsState()
        {
            if (_nativePackageNarrowMain == null || _nativePackageNarrowDetails == null) return;

            _nativePackageNarrowMain.style.display = _nativePackageNarrowDetailsOpen ? DisplayStyle.None : DisplayStyle.Flex;
            _nativePackageNarrowDetails.style.display = _nativePackageNarrowDetailsOpen ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnNativePackageNarrowDetailsKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape) return;

            CloseNativePackageNarrowDetails();
            evt.StopPropagation();
        }

        private void SelectNativePackageViewMode(int index)
        {
            if (AI.Config.packageViewMode == index) return;

            AI.Config.packageViewMode = index;
            AI.SaveConfig();
            TriggerNativePackageSearch();
            RebuildNativePackagesBody();
        }

        private void SetNativePackageGridSize(int size)
        {
            AI.Config.packageTileSize = size;
            AI.SaveConfig();
            _nativePackageGridSizeControl?.SetValueWithoutNotify(size);
            RefreshNativePackageTileDetailPopup();
            _nativePackageGridView?.SetDisplayMode(GetNativePackageGridDisplayMode());
            _nativePackageGridView?.SetLayout(AI.Config.packageTileSize, 1f, AI.Config.tileMargin, AI.Config.enlargeTiles);
        }

        private void SetNativePackageGridDetail(int optionIndex)
        {
            int clampedIndex = Mathf.Clamp(optionIndex, 0, PackageGridDetailPresetSizes.Length - 1);
            SetNativePackageGridSize(PackageGridDetailPresetSizes[clampedIndex]);
        }

        private void RefreshNativePackageTileDetailPopup()
        {
            if (_nativePackageTileDetailPopup == null) return;

            int optionIndex = (int)GetNativePackageGridDisplayMode();
            _nativePackageTileDetailPopup.SetValueWithoutNotify(PackageGridDetailOptions[optionIndex]);
        }

        private static float GetNativePackageInspectorPaneWidth()
        {
            float width = Mathf.Clamp(AI.Config.packageInspectorWidth, 220f, 720f);
            if (AI.Config.expandPackageDetails && width < 480f) width = 600f;
            return width;
        }

        private void OnNativePackageInspectorPaneStateChanged(float width, bool isOpen)
        {
            bool showExpanded = width >= 480f;
            bool expansionChanged = AI.Config.expandPackageDetails != showExpanded;
            AI.Config.packageInspectorWidth = width;
            AI.Config.showPackageSideBar = isOpen;
            AI.Config.expandPackageDetails = showExpanded;
            AI.SaveConfig();
            RefreshNativePackagePaneGutter();
            _nativePackagesHeaderStateHash = GetNativePackagesHeaderStateHash();
            if (!isOpen) return;

            if (expansionChanged)
            {
                LoadMediaOnDemand(_selectedTreeAsset);
                _nativePackageInspectorContentStateHash = int.MinValue;
            }
            RefreshNativePackageInspector();
        }

        private void RefreshNativePackagePaneGutter()
        {
            if (_nativePackagePaneLayout == null) return;

            bool hasTrailingPane = _nativePackagePaneLayout.Q<VisualElement>(className: "ai-resizable-pane-trailing") != null;
            _nativePackagePaneLayout.EnableInClassList(
                PackagesLayoutWithCollapsedTrailingPaneClass,
                hasTrailingPane && !_nativePackagePaneLayout.IsPaneOpen(CommonSidePane.Trailing));
        }

        private void SelectOlderDuplicates()
        {
            SelectDuplicatesByAge(true);
        }

        private void SelectNewerDuplicates()
        {
            SelectDuplicatesByAge(false);
        }

        private void SelectDuplicatesByAge(bool selectOlder)
        {
            List<AssetInfo> assets = GetSelectablePackages();

            // Find duplicate groups by ForeignId
            IEnumerable<IGrouping<int, AssetInfo>> duplicateGroupsByForeignId = assets
                .Where(a => a.ForeignId > 0 && a.AssetSource != Asset.Source.Synty)
                .GroupBy(a => a.ForeignId)
                .Where(g => g.Count() > 1);

            // Find duplicate groups by Location
            IEnumerable<IGrouping<string, AssetInfo>> duplicateGroupsByLocation = assets
                .Where(a => !string.IsNullOrEmpty(a.Location))
                .GroupBy(a => a.Location)
                .Where(g => g.Count() > 1);

            HashSet<int> idsToSelect = new HashSet<int>();

            // Process ForeignId duplicate groups
            foreach (IGrouping<int, AssetInfo> group in duplicateGroupsByForeignId)
            {
                IEnumerable<AssetInfo> duplicatesToSelect = selectOlder
                    ? group.OrderBy(a => a.AssetId).Take(1) // Select older (first)
                    : group.OrderBy(a => a.AssetId).Skip(1); // Select newer (all but first)

                foreach (AssetInfo asset in duplicatesToSelect)
                {
                    idsToSelect.Add(asset.AssetId);
                }
            }

            // Process Location duplicate groups
            foreach (IGrouping<string, AssetInfo> group in duplicateGroupsByLocation)
            {
                IEnumerable<AssetInfo> duplicatesToSelect = selectOlder
                    ? group.OrderBy(a => a.AssetId).Take(1) // Select older (first)
                    : group.OrderBy(a => a.AssetId).Skip(1); // Select newer (all but first)

                foreach (AssetInfo asset in duplicatesToSelect)
                {
                    idsToSelect.Add(asset.AssetId);
                }
            }

            if (idsToSelect.Count > 0)
            {
                ApplyPackageSelection(assets.Where(a => idsToSelect.Contains(a.AssetId)).ToList());
            }
        }

        private void SelectWithoutRelativeLocation()
        {
            List<AssetInfo> assets = GetSelectablePackages();

            List<AssetInfo> selectedAssets = assets
                .Where(a => !string.IsNullOrEmpty(a.Location) && !a.Location.StartsWith("[ac]"))
                .ToList();

            if (selectedAssets.Count > 0)
            {
                ApplyPackageSelection(selectedAssets);
            }
        }

        private void SelectCustomPackages()
        {
            List<AssetInfo> selectedAssets = GetSelectablePackages().Where(a => a.AssetSource == Asset.Source.CustomPackage).ToList();

            if (selectedAssets.Count > 0)
            {
                ApplyPackageSelection(selectedAssets);
            }
        }

        private void ApplyPackageSelection(List<AssetInfo> selectedAssets)
        {
            if (selectedAssets == null || selectedAssets.Count == 0) return;

            if (AI.Config.packageViewMode == 0)
            {
                List<int> idsToSelect = selectedAssets.Select(a => a.AssetId).ToList();
                SelectPackageTreeItems(idsToSelect, true, true);
            }
            else
            {
                if (PGrid.packages == null) CreateAssetTree();
                PGrid.SetVisualBulkSelection(selectedAssets);
                HandleAssetGridSelectionChanged();
            }
        }

        private List<AssetInfo> GetSelectableRootPackages()
        {
            return GetSelectablePackages()
                .Where(info => info != null && info.AssetId > 0 && info.ParentId <= 0)
                .GroupBy(info => info.AssetId)
                .Select(group => group.First())
                .ToList();
        }

        private void SelectAllVisiblePackages()
        {
            ApplyPackageSelection(GetSelectableRootPackages());
        }

        private List<AssetInfo> GetSelectablePackages()
        {
            if (AI.Config.packageViewMode == 1)
            {
                if (PGrid.packages == null) CreateAssetTree();
                if (PGrid.packages != null) return PGrid.packages.Where(a => a != null && a.AssetId > 0).ToList();
            }

            return AssetTreeModel.GetData().Where(a => a.AssetId > 0).ToList();
        }

        private bool IsPackageFilterActive()
        {
            return GetActivePackageFilterCount() > 0;
        }

        private int GetActivePackageFilterCount()
        {
            int count = 0;
            if (AI.Config.packagesListing != 1) count++;
            if (AI.Config.assetDeprecation > 0) count++;
            if (_selectedMaintenance != PackageSearch.MaintenanceOption.All) count++;
            if (AI.Config.assetSRPs > 0) count++;
            if (_selectedPkgPriceOption > 0) count++;
            if (_selectedPkgTag > 0) count++;
            if (_selectedPkgPublisher > 0) count++;
            if (_selectedPkgCategory > 0) count++;
            if (_selectedPkgUpdateDateOption > 0) count++;
            if (_selectedPkgPurchaseDateOption > 0) count++;
            if (_selectedPkgSizeOption > 0) count++;
            if (_selectedPkgUnityVersionOption > 0) count++;
            return count;
        }

        private void ResetPackageFilters(bool setType = true)
        {
            ClearActiveSavedPackageSearch();
            if (setType) AI.Config.packagesListing = 1;
            AI.Config.assetDeprecation = 0;
            AI.Config.assetSRPs = 0;
            _selectedMaintenance = PackageSearch.MaintenanceOption.All;
            _selectedPkgPriceOption = 0;
            _pkgSearchPrice = 0f;
            _selectedPkgTag = 0;
            _selectedPkgPublisher = 0;
            _selectedPkgCategory = 0;
            _selectedPkgUpdateDateOption = 0;
            _pkgUpdateBeforeDate = null;
            _pkgUpdateAfterDate = null;
            _selectedPkgPurchaseDateOption = 0;
            _pkgPurchaseBeforeDate = null;
            _pkgPurchaseAfterDate = null;
            _selectedPkgSizeOption = 0;
            _pkgSizeMB = 0f;
            _selectedPkgUnityVersionOption = 0;
            _requireAssetTreeRebuild = true;

            AI.SaveConfig();
        }

        internal static BulkPackageDownloadSummary CalculateBulkPackageDownloadSummary(List<AssetInfo> bulkAssets, List<AssetInfo> allAssets)
        {
            BulkPackageDownloadSummary summary = new BulkPackageDownloadSummary();
            if (bulkAssets == null) return summary;

            foreach (AssetInfo info in bulkAssets)
            {
                if (info == null || info.ParentId != 0) continue;

                bool metadataUpdateAvailable = info.IsUpdateAvailable(allAssets, false);
                if (info.AssetSource == Asset.Source.RegistryPackage)
                {
                    if (info.IsUpdateAvailable()) summary.PackageUpdateAvailable++;
                    continue;
                }

                AssetDownloader.State? state = info.PackageDownloader?.GetState().state;
                if (state == AssetDownloader.State.Downloading)
                {
                    AssetDownloadState downloadState = info.PackageDownloader.GetState();
                    summary.Downloading++;
                    summary.RemainingBytes += downloadState.bytesTotal - downloadState.bytesDownloaded;
                    continue;
                }
                if (state == AssetDownloader.State.Paused)
                {
                    summary.Paused++;
                    continue;
                }

                if (IsBulkAssetStoreUpdateTarget(info, allAssets, state, metadataUpdateAvailable))
                {
                    summary.UpdateAvailable++;
                    continue;
                }

                if (IsBulkAssetStoreDownloadTarget(info, state))
                {
                    summary.NotDownloaded++;
                    continue;
                }

                if (info.AssetSource == Asset.Source.CustomPackage
                    && metadataUpdateAvailable
                    && state == AssetDownloader.State.Unknown)
                {
                    summary.UpdateAvailableButCustom++;
                }
            }

            return summary;
        }

        private static bool IsBulkAssetStoreUpdateTarget(AssetInfo info, List<AssetInfo> allAssets, AssetDownloader.State? state)
        {
            bool metadataUpdateAvailable = info != null && info.IsUpdateAvailable(allAssets, false);
            return IsBulkAssetStoreUpdateTarget(info, allAssets, state, metadataUpdateAvailable);
        }

        private static bool IsBulkAssetStoreUpdateTarget(AssetInfo info, List<AssetInfo> allAssets, AssetDownloader.State? state, bool metadataUpdateAvailable)
        {
            if (info == null || info.ParentId != 0) return false;
            if (!IsOnDemandPackageSource(info)) return false;
            if (info.IsAbandoned) return false;
            if (!HasAssetStoreDownloadMetadata(info)) return false;
            if (!info.IsDownloaded) return false;

            if (state == AssetDownloader.State.UpdateAvailable) return true;
            if (info.WasOutdated) return false;
            if (!metadataUpdateAvailable) return false;

            return state == null
                   || state == AssetDownloader.State.Initializing
                   || state == AssetDownloader.State.Unknown
                   || state == AssetDownloader.State.Downloaded;
        }

        private static bool IsBulkAssetStoreDownloadTarget(AssetInfo info, AssetDownloader.State? state)
        {
            if (info == null || info.ParentId != 0) return false;
            if (!IsOnDemandPackageSource(info)) return false;
            if (info.IsAbandoned) return false;
            if (!HasAssetStoreDownloadMetadata(info)) return false;
            if (info.IsDownloaded) return false;

            return state == null
                   || state == AssetDownloader.State.Initializing
                   || state == AssetDownloader.State.Unknown
                   || state == AssetDownloader.State.Unavailable;
        }

        private static bool HasAssetStoreDownloadMetadata(AssetInfo info)
        {
            if (info == null) return false;
            return info.AssetSource == Asset.Source.AssetStorePackage
                   && !string.IsNullOrEmpty(info.OriginalLocation)
                   && info.UploadId > 0;
        }

        private static bool IsOnDemandPackageSource(AssetInfo info)
        {
            return info != null && info.AssetSource == Asset.Source.AssetStorePackage;
        }

        private void StartBulkPackageDownloads(List<AssetInfo> targets, bool markWasOutdated)
        {
            if (targets == null || targets.Count == 0) return;

            List<BulkPackageDownloadTargetSpec> specs = new List<BulkPackageDownloadTargetSpec>(targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                AssetInfo info = targets[i];
                if (info == null || info.ParentId != 0) continue;
                specs.Add(BulkPackageDownloadTargetSpec.FromAsset(info));
            }
            if (specs.Count == 0) return;

            _bulkPackageDownloadSession ??= new BulkPackageDownloadSession();
            _bulkPackageDownloadSession.AddOrMerge(specs);
            for (int i = 0; i < targets.Count; i++)
            {
                StartBulkPackageDownload(targets[i], markWasOutdated);
            }

            RefreshNativeBulkPackageDownloadProgress();
        }

        private void RefreshNativeBulkPackageDownloadProgress()
        {
            if (_nativeBulkPackageDownloadProgress == null) return;
            if (_bulkPackageDownloadSession == null || _bulkPackageDownloadSession.TargetCount == 0)
            {
                _nativeBulkPackageDownloadProgress.Hide();
                return;
            }
            if (_assets == null) return;

            List<BulkPackageDownloadObservation> observations = new List<BulkPackageDownloadObservation>(_bulkPackageDownloadSession.TargetCount);
            for (int i = 0; i < _assets.Count; i++)
            {
                AssetInfo info = _assets[i];
                if (info == null || !_bulkPackageDownloadSession.Matches(info.AssetId, info.ForeignId)) continue;

                if (info.PackageDownloader == null) AI.GetObserver().Attach(info);
                observations.Add(BulkPackageDownloadObservation.FromAsset(info));
            }

            DateTime now = DateTime.UtcNow;
            BulkPackageDownloadProgressSnapshot snapshot = _bulkPackageDownloadSession.Refresh(observations, now);
            _nativeBulkPackageDownloadProgress.Apply(snapshot);
            if (_bulkPackageDownloadSession.ShouldClear(now)) DismissNativeBulkPackageDownloadProgress();
        }

        private void DismissNativeBulkPackageDownloadProgress()
        {
            _bulkPackageDownloadSession = null;
            _nativeBulkPackageDownloadProgress?.Hide();
        }

        private static void StartBulkPackageDownload(AssetInfo info, bool markWasOutdated)
        {
            if (info == null || info.ParentId != 0) return;

            AI.GetObserver().Attach(info);
            if (info.PackageDownloader == null || !info.PackageDownloader.IsDownloadSupported()) return;

            if (markWasOutdated)
            {
                info.WasOutdated = true;
                info.PackageDownloader.SetAsset(info);
            }

            info.PackageDownloader.Download(false);
        }

        private bool EnsurePackageTreeDataReady()
        {
            if (!_initDone || !AI.IsInitialized) return false;

            if (_assets == null)
            {
                UpdateStatistics(false);
            }
            if (_assets == null) return false;

            if (_tags == null || _tagNames == null || _publisherNames == null || _categoryNames == null)
            {
                ReloadLookups();
            }
            return _tags != null && _tagNames != null && _publisherNames != null && _categoryNames != null;
        }

        private void CreateAssetTree()
        {
            if (AI.DEBUG_MODE) Debug.LogWarning("CreateAssetTree");
            if (!EnsurePackageTreeDataReady()) return;

            // sync sort state between column headers and model
            if (assetColumnState != null)
            {
                try
                {
                    assetColumnState.SortedColumnIndex = AI.Config.assetSorting;
                    if (assetColumnState.SortedColumnIndex >= 0)
                    {
                        assetColumnState.Columns[assetColumnState.SortedColumnIndex].SortedAscending = !AI.Config.sortAssetsDescending;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to sync asset tree sort state: {e.Message}");
                }
            }

            _requireAssetTreeRebuild = false;
            _visiblePackageCount = 0;
            List<AssetInfo> data = new List<AssetInfo>();
            AssetInfo root = new AssetInfo().WithTreeData("Root", depth: -1);
            data.Add(root);
            PrepareBackupCountStateForPackageList();

            // Use PackageSearch to apply all filters
            PackageSearch.Options opt = new PackageSearch.Options
            {
                SearchPhrase = _assetSearchPhrase,
                SelectedPackageListing = AI.Config.packagesListing,
                SelectedSRPs = AI.Config.assetSRPs,
                SelectedDeprecation = AI.Config.assetDeprecation,
                SelectedMaintenance = _selectedMaintenance,
                OnlyInProject = SearchScopeModel.IsProjectOnly(GetConfiguredSearchScope()),
                UsedPackages = _usedPackages,
                AllAssets = _assets,
                SearchDescription = AI.Config.searchPackageDescriptions,
                SearchGroupNames = AI.Config.searchPackageGroupNames,
                CurrentGrouping = AI.Config.packageViewMode == 0 ? AI.Config.assetGrouping : 0,
                SelectedPriceOption = _selectedPkgPriceOption,
                SearchPrice = _pkgSearchPrice,
                SelectedPackageTag = _selectedPkgTag,
                TagNames = _tagNames,
                Tags = _tags,
                SelectedPublisher = _selectedPkgPublisher,
                PublisherNames = _publisherNames,
                SelectedCategory = _selectedPkgCategory,
                CategoryNames = _categoryNames,
                SelectedUpdateDateOption = _selectedPkgUpdateDateOption,
                UpdateBeforeDate = _pkgUpdateBeforeDate,
                UpdateAfterDate = _pkgUpdateAfterDate,
                SelectedPurchaseDateOption = _selectedPkgPurchaseDateOption,
                PurchaseBeforeDate = _pkgPurchaseBeforeDate,
                PurchaseAfterDate = _pkgPurchaseAfterDate,
                SelectedPackageSizeOption = _selectedPkgSizeOption,
                PackageSizeMB = _pkgSizeMB,
                SelectedUnityVersionOption = _selectedPkgUnityVersionOption
            };

            // Handle OnlyInProject calculation if needed
            if (!_usageCalculationDone || _usedPackages == null)
            {
                CalculateAssetUsageAutomatically();
            }

            PackageSearch.Result result = PackageSearch.Execute(opt);
            IEnumerable<AssetInfo> filteredAssets = result.Packages;

            string[] lastGroups = Array.Empty<string>();
            int catIdx = 0;
            IOrderedEnumerable<AssetInfo> orderedAssets;

            // grouping not supported for grid view
            int usedGrouping = AI.Config.packageViewMode == 0 ? AI.Config.assetGrouping : 0;
            switch (usedGrouping)
            {
                case 0: // none
                    orderedAssets = AddPackageOrdering(filteredAssets);
                    foreach (AssetInfo a in orderedAssets)
                    {
                        data.Add(a.WithTreeData(a.GetDisplayName(), a.AssetId));
                    }
                    break;

                case 2: // category
                    orderedAssets = filteredAssets.OrderBy(a => a.GetDisplayCategory(), new PathComparer())
                        .ThenBy(a => a.GetDisplayName(), StringComparer.OrdinalIgnoreCase);

                    string[] noCat = {"-no category-"};
                    foreach (AssetInfo info in orderedAssets)
                    {
                        // create hierarchy
                        string[] cats = string.IsNullOrEmpty(info.GetDisplayCategory()) ? noCat : info.GetDisplayCategory().Split('/');

                        lastGroups = AddCategorizedItem(cats, lastGroups, data, info, ref catIdx);
                    }
                    break;

                case 3: // publisher
                    IOrderedEnumerable<AssetInfo> orderedAssetsPub = filteredAssets.OrderBy(a => a.GetDisplayPublisher(), StringComparer.OrdinalIgnoreCase)
                        .ThenBy(a => a.GetDisplayName(), StringComparer.OrdinalIgnoreCase);

                    string[] noPub = {"-no publisher-"};
                    foreach (AssetInfo info in orderedAssetsPub)
                    {
                        // create hierarchy
                        string[] pubs = string.IsNullOrEmpty(info.GetDisplayPublisher()) ? noPub : new[] {info.GetDisplayPublisher()};

                        lastGroups = AddCategorizedItem(pubs, lastGroups, data, info, ref catIdx);
                    }
                    break;

                case 4: // tags
                    List<Tag> tags = Tagging.LoadTags();
                    foreach (Tag tag in tags)
                    {
                        IOrderedEnumerable<AssetInfo> taggedAssets = filteredAssets
                            .Where(a => a.PackageTags != null && a.PackageTags.Any(t => t.Name == tag.Name))
                            .OrderBy(a => a.GetDisplayName(), StringComparer.OrdinalIgnoreCase);

                        string[] cats = {tag.Name};
                        foreach (AssetInfo info in taggedAssets)
                        {
                            // create hierarchy
                            lastGroups = AddCategorizedItem(cats, lastGroups, data, info, ref catIdx);
                        }
                    }

                    IOrderedEnumerable<AssetInfo> remainingAssets = filteredAssets
                        .Where(a => a.PackageTags == null || a.PackageTags.Count == 0)
                        .OrderBy(a => a.GetDisplayName(), StringComparer.OrdinalIgnoreCase);
                    string[] untaggedCat = {"-untagged-"};
                    foreach (AssetInfo info in remainingAssets)
                    {
                        lastGroups = AddCategorizedItem(untaggedCat, lastGroups, data, info, ref catIdx);
                    }
                    break;

                case 5: // state
                    IOrderedEnumerable<AssetInfo> orderedAssetsState = filteredAssets.OrderBy(a => a.OfficialState)
                        .ThenBy(a => a.GetDisplayName(), StringComparer.OrdinalIgnoreCase);

                    string[] noState = {"-no state-"};
                    foreach (AssetInfo info in orderedAssetsState)
                    {
                        // create hierarchy
                        string[] pubs = info.OfficialState == Asset.OfficialStateType.None ? noState : new[] {info.OfficialState.ToString()};

                        lastGroups = AddCategorizedItem(pubs, lastGroups, data, info, ref catIdx);
                    }
                    break;

                case 6: // location
                    IOrderedEnumerable<AssetInfo> orderedAssetsLocation = filteredAssets.OrderBy(a => GetLocationDirectory(a.Location), StringComparer.OrdinalIgnoreCase)
                        .ThenBy(a => a.GetDisplayName(), StringComparer.OrdinalIgnoreCase);

                    string[] noLocation = {"-no location-"};
                    foreach (AssetInfo info in orderedAssetsLocation)
                    {
                        // create hierarchy
                        string[] pubs = string.IsNullOrEmpty(GetLocationDirectory(info.Location)) ? noLocation : new[] {GetLocationDirectory(info.Location)};

                        lastGroups = AddCategorizedItem(pubs, lastGroups, data, info, ref catIdx);
                    }
                    break;
            }

            _textureLoading2?.Cancel();
            _textureLoading2?.Dispose();
            _textureLoading2 = new CancellationTokenSource();

            if (AI.Config.packageViewMode == 0)
            {
                // re-add parents to sub-packages if they were filtered out
                ReAddMissingParents(filteredAssets, data);

                // reorder sub-packages
                ReorderSubPackages(data);

                if (_selectedMaintenance != PackageSearch.MaintenanceOption.Excluded)
                {
                    // add sub-packages from _assets where missing in data since we filtered them out initially
                    AddSubPackagesToTree(data);
                    AddSubPackagesToFeatures(data);
                }

                // remember selected asset IDs before rebuilding so we can restore visibility
                List<int> previousSelectionIds = null;
                if (_selectedTreeAsset != null && _selectedTreeAsset.AssetId > 0)
                {
                    previousSelectionIds = new List<int> {_selectedTreeAsset.AssetId};
                }
                else if (_selectedTreeAssets != null && _selectedTreeAssets.Count > 0)
                {
                    previousSelectionIds = _selectedTreeAssets
                        .Where(a => a != null && a.AssetId > 0)
                        .Select(a => a.AssetId)
                        .ToList();
                }

                AssetTreeModel.SetData(data, AI.Config.assetGrouping > 0);

                List<int> validSelectionIds = new List<int>();
                if (previousSelectionIds != null && previousSelectionIds.Count > 0)
                {
                    HashSet<int> availableIds = new HashSet<int>(data.Select(d => d.AssetId));
                    validSelectionIds.AddRange(previousSelectionIds.Where(id => availableIds.Contains(id)));
                }

                QueueNativePackageTreeRefresh(validSelectionIds, false);
                _assetTreeSelectedIds.Clear();
                _assetTreeSelectedIds.AddRange(validSelectionIds);
                HandleAssetTreeSelectionChanged(GetCurrentPackageTreeSelection());

                AssetUtils.LoadTextures(data, _textureLoading2.Token);
                _visiblePackageCount = data.Count(a => a.AssetId > 0 && a.ParentId == 0);
            }
            else
            {
                // grid does not support grouping or sub-packages
                List<AssetInfo> visiblePackages = data.Where(a => a.AssetId > 0 && a.ParentId == 0).ToList();
                List<AssetInfo> selectedPackages = _selectedTreeAssets?.Where(a => a != null && a.AssetId > 0).ToList();
                PGrid.ResetPreviews(visiblePackages.Count);
                PGrid.Init(_assets, visiblePackages, HandleAssetGridSelectionChanged);

                if (selectedPackages?.Count > 0)
                {
                    PGrid.SetVisualBulkSelection(selectedPackages);
                }
                QueueNativePackageGridRefresh();

                AssetUtils.LoadTextures(visiblePackages, _textureLoading2.Token, (idx, texture) =>
                {
                    // validate in case dataset changed in the meantime
                    bool currentItem = idx >= 0
                        && idx < visiblePackages.Count
                        && PGrid.packages != null
                        && idx < PGrid.packages.Count
                        && PGrid.packages[idx].AssetId == visiblePackages[idx].AssetId;
                    if (currentItem && PGrid.PreviewCount > idx)
                    {
                        PGrid.SetPreview(idx, texture);
                        _nativePackageGridView?.RefreshItem(idx);
                    }
                    _needsRepaint = true;
                });
                _visiblePackageCount = visiblePackages.Count;
            }
        }

        private void ReAddMissingParents(IEnumerable<AssetInfo> filteredAssets, List<AssetInfo> data)
        {
            foreach (AssetInfo info in filteredAssets.Where(a => a.ParentId > 0 && !data.Any(d => d.AssetId == a.ParentId)))
            {
                AssetInfo parent = _assets.FirstOrDefault(a => a.AssetId == info.ParentId);
                if (parent != null)
                {
                    data.Add(parent.WithTreeData(parent.GetDisplayName(), parent.AssetId));
                }
            }
        }

        private void AddSubPackagesToTree(List<AssetInfo> data)
        {
            if (_assets.Count == 0) return; // will cause invalid operation exception otherwise

            int maxChildDepth = _assets.Max(a => a.GetChildDepth());
            HashSet<int> existingAssetIds = new HashSet<int>(data.Select(d => d.AssetId));

            for (int depth = 1; depth <= maxChildDepth; depth++)
            {
                Dictionary<int, List<AssetInfo>> subAssets = _assets
                    .Where(a => !a.Exclude && a.GetChildDepth() == depth && !existingAssetIds.Contains(a.AssetId))
                    .OrderByDescending(a => a.GetDisplayName(), StringComparer.OrdinalIgnoreCase)
                    .GroupBy(a => a.ParentId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (KeyValuePair<int, List<AssetInfo>> pair in subAssets)
                {
                    int parentIndex = data.FindIndex(a => a.AssetId == pair.Key);
                    if (parentIndex < 0) continue;

                    foreach (AssetInfo asset in pair.Value)
                    {
                        asset.Depth = data[parentIndex].Depth + 1;
                        AssetInfo newAsset = asset.WithTreeData(asset.GetDisplayName(), asset.AssetId, asset.Depth);
                        data.Insert(parentIndex + 1, newAsset);
                        existingAssetIds.Add(newAsset.AssetId);
                    }
                }
            }
        }

        private void AddSubPackagesToFeatures(List<AssetInfo> data)
        {
            if (!AssetStore.IsMetadataAvailable()) return;

            for (int i = 0; i < data.Count; i++)
            {
                AssetInfo info = data[i];
                if (!info.IsFeaturePackage()) continue;

                PackageInfo pInfo = AssetStore.GetPackageInfo(info);
                if (pInfo?.dependencies == null) continue; // in case not loaded yet

                foreach (DependencyInfo dependency in pInfo.dependencies.OrderByDescending(d => d.name))
                {
                    AssetInfo package = _assets.FirstOrDefault(a => !a.Exclude && a.SafeName == dependency.name);
                    if (package != null)
                    {
                        AssetInfo newAsset = new AssetInfo(package.ToAsset()).WithTreeData(package.GetDisplayName(), package.AssetId, package.Depth + 1);
                        data.Insert(i + 1, newAsset);
                    }
                }
            }
        }

        private static void ReorderSubPackages(List<AssetInfo> data)
        {
            int maxChildDepth = data.Max(a => a.GetChildDepth());
            for (int depth = 1; depth <= maxChildDepth; depth++)
            {
                Dictionary<int, List<AssetInfo>> subAssets = data.Where(a => a.GetChildDepth() == depth)
                    .OrderBy(a => a.GetDisplayName(), StringComparer.OrdinalIgnoreCase)
                    .GroupBy(a => a.ParentId).ToDictionary(g => g.Key, g => g.ToList());
                foreach (KeyValuePair<int, List<AssetInfo>> pair in subAssets)
                {
                    // remove items at existing positions
                    pair.Value.ForEach(a =>
                    {
                        data.Remove(a);
                    });

                    // find item with id pair.Key and insert items afterward
                    int idx = data.FindIndex(a => a.AssetId == pair.Key);
                    if (idx >= 0)
                    {
                        pair.Value.ForEach(a =>
                        {
                            a.Depth = data[idx].Depth + 1;
                        });
                        data.InsertRange(idx + 1, pair.Value);
                    }
                }
            }
        }

        private string GetLocationDirectory(string location)
        {
            if (string.IsNullOrWhiteSpace(location)) return null;
            try
            {
                string[] arr = location.Split(Asset.SUB_PATH);
                return Path.GetDirectoryName(arr[0]);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static SemVer GetFirstUnityVersionOrDefault(AssetInfo asset)
        {
            if (asset == null)
            {
                return new SemVer("0.0.0");
            }

            if (string.IsNullOrWhiteSpace(asset.SupportedUnityVersions))
            {
                return new SemVer("0.0.0");
            }

            string[] parts = asset.SupportedUnityVersions.Split(',');
            if (parts.Length == 0)
            {
                return new SemVer("0.0.0");
            }

            string firstPart = parts[0].Trim();
            if (string.IsNullOrEmpty(firstPart))
            {
                return new SemVer("0.0.0");
            }

            try
            {
                return new SemVer(firstPart);
            }
            catch (Exception)
            {
                return new SemVer("0.0.0");
            }
        }

        private IOrderedEnumerable<AssetInfo> AddPackageOrdering(IEnumerable<AssetInfo> list)
        {
            IOrderedEnumerable<AssetInfo> result = null;
            bool asc = !AI.Config.sortAssetsDescending;
            switch (AI.Config.assetSorting)
            {
                case (int)Columns.AICaptions:
                    result = list.SortBy(a => a.AICaption, asc);
                    break;

                case (int)Columns.Backup:
                    result = list.SortBy(a => a.Backup, asc);
                    break;

                case (int)Columns.BackupCount:
                    result = list.SortBy(a => GetBackupCountForPackageList(a) ?? -1, asc);
                    break;

                case (int)Columns.NoIndex:
                    result = list.SortBy(a => a.NoIndex, asc);
                    break;

                case (int)Columns.SemanticIndex:
                    result = list.SortBy(a => a.IsSemanticIndexEnabled, asc);
                    break;

                case (int)Columns.CodeIndex:
                    result = list.SortBy(a => a.IsCodeIndexEnabled, asc);
                    break;

                case (int)Columns.Category:
                    result = list.SortBy(a => a.GetDisplayCategory(), asc, StringComparer.OrdinalIgnoreCase);
                    break;

                case (int)Columns.InternalState:
                    result = list.SortBy(a => a.CurrentState, asc);
                    break;

                case (int)Columns.Deprecated:
                    result = list.SortBy(a => a.IsDeprecated, asc);
                    break;

                case (int)Columns.Downloaded:
                    result = list.SortBy(a => a.IsDownloaded, asc);
                    break;

                case (int)Columns.ModifiedDate:
                case (int)Columns.ModifiedDateRelative:
                    result = list.SortBy(a => a.ModifiedDate, asc);
                    break;

                case (int)Columns.Exclude:
                    result = list.SortBy(a => a.Exclude, asc);
                    break;

                case (int)Columns.Extract:
                    result = list.SortBy(a => a.KeepExtracted, asc);
                    break;

                case (int)Columns.ForeignId:
                    result = list.SortBy(a => a.ForeignId, asc);
                    break;

                case (int)Columns.Popularity:
                    result = list.SortBy(a => a.Hotness, asc);
                    break;

                case (int)Columns.Indexed:
                    result = list.SortBy(a => AssetTreeViewControl.GetPackageIndexColumnSortBucket(a), asc);
                    break;

                case (int)Columns.FileCount:
                    result = list.SortBy(a => a.FileCount, asc);
                    break;

                case (int)Columns.License:
                    result = list.SortBy(a => a.License, asc);
                    break;

                case (int)Columns.Location:
                    result = list.SortBy(a => a.Location, asc);
                    break;

                case (int)Columns.Materialized:
                    result = list.SortBy(a => a.IsMaterialized, asc);
                    break;

                case (int)Columns.Name:
                    result = list.SortBy(a => a.GetDisplayName(), asc, StringComparer.OrdinalIgnoreCase);
                    break;

                case (int)Columns.Outdated:
                    result = list.SortBy(a => a.CurrentSubState == Asset.SubState.Outdated, asc);
                    break;

                case (int)Columns.Price:
                    result = list.SortBy(a => a.GetPrice(), asc);
                    break;

                case (int)Columns.Publisher:
                    result = list.SortBy(a => a.GetDisplayPublisher(), asc, StringComparer.OrdinalIgnoreCase);
                    break;

                case (int)Columns.PurchaseDate:
                case (int)Columns.PurchaseDateRelative:
                    result = list.SortBy(a => a.GetPurchaseDate(), asc);
                    break;

                case (int)Columns.Rating:
                    result = list.SortBy(a => a.AssetRating, asc).ThenSortBy(a => a.RatingCount, asc);
                    break;

                case (int)Columns.RatingCount:
                    result = list.SortBy(a => a.RatingCount, asc).ThenSortBy(a => a.AssetRating, asc);
                    break;

                case (int)Columns.Rules:
                    result = list.SortBy(a => AssetTreeViewControl.GetPackageRuleSortBucket(a), asc)
                        .ThenSortBy(a => AssetTreeViewControl.GetPackageRuleCount(a), asc);
                    break;

                case (int)Columns.ReleaseDate:
                case (int)Columns.ReleaseDateRelative:
                    result = list.SortBy(a => a.FirstRelease, asc);
                    break;

                case (int)Columns.Size:
                    result = list.SortBy(a => a.PackageSize, asc);
                    break;

                case (int)Columns.Source:
                    result = list.SortBy(a => a.AssetSource, asc);
                    break;

                case (int)Columns.State:
                    result = list.SortBy(a => a.OfficialState, asc);
                    break;

                case (int)Columns.Tags:
                    result = list.SortBy(a => string.Join(", ", a.PackageTags.Select(t => t.Name)), asc);
                    break;

                case (int)Columns.UnityVersions:
                    result = list.SortBy(a => GetFirstUnityVersionOrDefault(a), asc);
                    break;

                case (int)Columns.Update:
                    result = list.SortBy(a => a.AssetSource == Asset.Source.AssetStorePackage && a.IsUpdateAvailable(), asc);
                    break;

                case (int)Columns.UpdateDate:
                case (int)Columns.UpdateDateRelative:
                    result = list.SortBy(a => a.LastRelease, asc);
                    break;

                case (int)Columns.Version:
                    result = list.SortBy(a => new SemVer(a.GetVersion()), asc);
                    break;

                default:
                    int metaId = assetColumnState != null &&
                        AI.Config.assetSorting >= 0 &&
                        AI.Config.assetSorting < assetColumnState.ColumnCount
                            ? assetColumnState.Columns[AI.Config.assetSorting].UserData
                            : -1;
                    MetadataInfo metaDef = list.Where(a => a.PackageMetadata != null && a.PackageMetadata.Any(pm => pm.DefinitionId == metaId)).Select(a => a.PackageMetadata.First(pm => pm.DefinitionId == metaId)).FirstOrDefault();
                    if (metaDef != null)
                    {
                        switch (metaDef.Type)
                        {
                            case MetadataDefinition.DataType.Boolean:
                                result = list.SortBy(a =>
                                {
                                    MetadataInfo meta = a.PackageMetadata?.FirstOrDefault(m => m.DefinitionId == metaId);
                                    if (meta == null) return 0;
                                    return meta.BoolValue ? 1 : 0;
                                }, asc);
                                break;

                            case MetadataDefinition.DataType.Text:
                            case MetadataDefinition.DataType.BigText:
                            case MetadataDefinition.DataType.Url:
                            case MetadataDefinition.DataType.SingleSelect:
                            case MetadataDefinition.DataType.List:
                                result = list.SortBy(a =>
                                {
                                    MetadataInfo meta = a.PackageMetadata?.FirstOrDefault(m => m.DefinitionId == metaId);
                                    if (meta == null) return null;
                                    return meta.StringValue;
                                }, asc);
                                break;

                            case MetadataDefinition.DataType.Date:
                            case MetadataDefinition.DataType.DateTime:
                                result = list.SortBy(a =>
                                {
                                    MetadataInfo meta = a.PackageMetadata?.FirstOrDefault(m => m.DefinitionId == metaId);
                                    if (meta == null) return default;
                                    return meta.DateTimeValue;
                                }, asc);
                                break;

                            case MetadataDefinition.DataType.Number:
                                result = list.SortBy(a =>
                                {
                                    MetadataInfo meta = a.PackageMetadata?.FirstOrDefault(m => m.DefinitionId == metaId);
                                    if (meta == null) return 0;
                                    return meta.IntValue;
                                }, asc);
                                break;

                            case MetadataDefinition.DataType.DecimalNumber:
                                result = list.SortBy(a =>
                                {
                                    MetadataInfo meta = a.PackageMetadata?.FirstOrDefault(m => m.DefinitionId == metaId);
                                    if (meta == null) return 0f;
                                    return meta.FloatValue;
                                }, asc);
                                break;

                        }
                    }
                    break;

            }
            if (result == null) result = list.OrderBy(a => a.LastRelease);

            return result.ThenSortBy(a => a.GetDisplayName(), asc, StringComparer.OrdinalIgnoreCase);
        }

        private static string[] AddCategorizedItem(string[] cats, string[] lastCats, List<AssetInfo> data, AssetInfo info, ref int catIdx)
        {
            // find first difference to previous cat
            if (!ArrayUtility.ArrayEquals(cats, lastCats))
            {
                int firstDiff = 0;
                bool diffFound = false;
                for (int i = 0; i < Mathf.Min(cats.Length, lastCats.Length); i++)
                {
                    if (cats[i] != lastCats[i])
                    {
                        firstDiff = i;
                        diffFound = true;
                        break;
                    }
                }
                if (!diffFound) firstDiff = lastCats.Length;

                for (int i = firstDiff; i < cats.Length; i++)
                {
                    catIdx--;
                    AssetInfo catItem = new AssetInfo().WithTreeData(cats[i], catIdx, i);
                    data.Add(catItem);
                }
            }

            AssetInfo item = info.WithTreeData(info.GetDisplayName(), info.AssetId, cats.Length);
            data.Add(item);

            return cats;
        }

        private async void OpenInPackageView(AssetInfo info)
        {
            AI.Config.tab = 1;
            _assetSearchPhrase = "";

            ResetPackageFilters(false);

            if (AI.Config.packageViewMode == 0)
            {
                await SelectAndFrame(info);

                // ensure package is visible
                if (!AssetTreeModel.GetData().Contains(info))
                {
                    AI.Config.packagesListing = 0;
                    ResetPackageFilters(false);
                    await SelectAndFrame(info);
                }

                HandleAssetTreeSelectionChanged(GetCurrentPackageTreeSelection());
            }
            else
            {
                if (PGrid.packages == null) CreateAssetTree();
                PGrid.Select(info.GetRoot());
                HandleAssetGridSelectionChanged();
            }
        }

        private async Task SelectAndFrame(AssetInfo info)
        {
            await Task.Delay(100); // let the tree view update first
            SelectPackageTreeItems(new[] {info.AssetId}, true, false);
        }

        private void SelectPackageTreeItems(IList<int> ids, bool reveal, bool notify)
        {
            List<int> selection = ids?.Distinct().ToList() ?? new List<int>();
            _assetTreeSelectedIds.Clear();
            _assetTreeSelectedIds.AddRange(selection);

            if (_nativePackageTreeAdapter != null)
            {
                _nativePackageTreeAdapter.SetSelectionByModelIds(selection, reveal);
            }

            if (notify) OnAssetTreeSelectionChanged(selection);
        }

        private void HandleAssetTreeSelectionChanged(IList<int> ids)
        {
            AssetInfo oldSelection = _selectedTreeAsset;
            _selectedTreeAsset = null;
            _selectedTreeAssets = _selectedTreeAssets ?? new List<AssetInfo>();
            _selectedTreeAssets.Clear();

            if (ids.Count == 1 && ids[0] > 0)
            {
                _selectedTreeAsset = AssetTreeModel.Find(ids[0]);

                // restore single selections as otherwise after e.g. a download the selected package disappears from the inspector 
                if (_selectedTreeAsset == null) _selectedTreeAsset = oldSelection;

                if (_selectedTreeAsset != null)
                {
                    // refresh immediately for single selections to have all buttons correct at once
                    _selectedTreeAsset.Refresh();
                    _selectedTreeAsset.PackageDownloader?.RefreshState(true);

                    LoadMediaOnDemand(_selectedTreeAsset);

                    // Gather backup state once per asset selection for performance
                    if (AssetBackup.GetBackupKey(_selectedTreeAsset) != 0 &&
                        (_selectedTreeAsset.AssetSource == Asset.Source.AssetStorePackage ||
                            _selectedTreeAsset.AssetSource == Asset.Source.CustomPackage ||
                            _selectedTreeAsset.AssetSource == Asset.Source.Synty))
                    {
                        _cachedBackupState = AssetBackup.GatherState();
                    }
                    else
                    {
                        _cachedBackupState = null;
                    }
                }
            }
            else
            {
                _cachedBackupState = null;
            }

            // load all selected items but count each only once
            HashSet<int> seen = new HashSet<int>();
            foreach (int id in ids)
            {
                GatherTreeChildren(id, _selectedTreeAssets, seen, AssetTreeModel);
            }

            _assetBulkTags.Clear();

            // initialize download status, debounce for bulk selections to avoid expensive re-initialization on rapid clicks
            if (_selectedTreeAssets.Count <= 1)
            {
                AI.RegisterSelection(_selectedTreeAssets);
            }
            else
            {
                int generation = ++_selectionGeneration;
                EditorApplication.delayCall += () =>
                {
                    if (_selectionGeneration == generation) AI.RegisterSelection(_selectedTreeAssets);
                };
            }

            // merge tags
            _selectedTreeAssets.ForEach(info => info.PackageTags?.ForEach(t =>
            {
                if (_assetBulkTags.TryGetValue(t.Name, out Tuple<int, Color> existing))
                {
                    _assetBulkTags[t.Name] = new Tuple<int, Color>(existing.Item1 + 1, existing.Item2);
                }
                else
                {
                    _assetBulkTags.Add(t.Name, new Tuple<int, Color>(1, t.GetColor()));
                }
            }));

            _assetTreeSubPackageCount = 0;
            _assetTreeSelectionSize = 0;
            _assetTreeSelectionTotalCosts = 0;
            _assetTreeSelectionStoreCosts = 0;
            foreach (AssetInfo a in _selectedTreeAssets)
            {
                if (a.ParentId > 0)
                {
                    _assetTreeSubPackageCount++;
                }
                else
                {
                    _assetTreeSelectionSize += a.PackageSize;
                    float price = a.GetPrice();
                    _assetTreeSelectionTotalCosts += price;
                    if (a.AssetSource == Asset.Source.AssetStorePackage) _assetTreeSelectionStoreCosts += price;
                }
            }

            // refresh metadata automatically for single selections
            if (_selectedTreeAsset != null && AI.Config.autoRefreshMetadata && _selectedTreeAsset.ForeignId > 0 && (DateTime.Now - _selectedTreeAsset.LastOnlineRefresh).TotalHours >= AI.Config.metadataTimeout && AssetStoreAuthentication.Current.CanAutoRefresh(Application.isBatchMode))
            {
                _ = AI.Actions.FetchAssetDetails(true, _selectedTreeAsset.AssetId, _selectedTreeAsset.LastOnlineRefresh > DateTime.MinValue); // skip downstream events to avoid hick-ups
                _selectedTreeAsset.LastOnlineRefresh = DateTime.Now; // safety in case the above fails, e.g. for deleted packages
            }
        }

        private void HandleAssetGridSelectionChanged()
        {
            _selectedMedia = 0;
            _focusNativePackageMediaAfterRebuild = false;
            _selectedTreeAssets = PGrid.selectionItems?.Distinct().ToList() ?? new List<AssetInfo>();
            _selectedTreeAsset = _selectedTreeAssets.Count == 1 ? _selectedTreeAssets[0] : null;

            if (_selectedTreeAsset != null)
            {
                // refresh immediately for single selections to have all buttons correct at once
                _selectedTreeAsset.Refresh();
                _selectedTreeAsset.PackageDownloader?.RefreshState(true);

                LoadMediaOnDemand(_selectedTreeAsset);
            }

            _assetBulkTags.Clear();

            // initialize download status, debounce for bulk selections to avoid expensive re-initialization on rapid clicks
            if (_selectedTreeAssets.Count <= 1)
            {
                AI.RegisterSelection(_selectedTreeAssets);
            }
            else
            {
                int generation = ++_selectionGeneration;
                EditorApplication.delayCall += () =>
                {
                    if (_selectionGeneration == generation) AI.RegisterSelection(_selectedTreeAssets);
                };
            }

            // merge tags
            _selectedTreeAssets.ForEach(info => info.PackageTags?.ForEach(t =>
            {
                if (_assetBulkTags.TryGetValue(t.Name, out Tuple<int, Color> existing))
                {
                    _assetBulkTags[t.Name] = new Tuple<int, Color>(existing.Item1 + 1, existing.Item2);
                }
                else
                {
                    _assetBulkTags.Add(t.Name, new Tuple<int, Color>(1, t.GetColor()));
                }
            }));

            _assetTreeSubPackageCount = 0;
            _assetTreeSelectionSize = 0;
            _assetTreeSelectionTotalCosts = 0;
            _assetTreeSelectionStoreCosts = 0;
            foreach (AssetInfo a in _selectedTreeAssets)
            {
                if (a.ParentId == 0)
                {
                    _assetTreeSelectionSize += a.PackageSize;
                    float price = a.GetPrice();
                    _assetTreeSelectionTotalCosts += price;
                    if (a.AssetSource == Asset.Source.AssetStorePackage) _assetTreeSelectionStoreCosts += price;
                }
            }
            SyncNativePackageGridSelectionFromBackend();
        }

        private void LoadMediaOnDemand(AssetInfo info)
        {
            if (info == null) return;
            if (info.IsMediaLoading()) return;
            if (info.AllMedia != null) return; // already loaded

            if (AI.Config.expandPackageDetails || AI.Config.alwaysShowPackageDetails)
            {
                // clear all existing media to conserve memory
                if (AI.Config.packageViewMode == 0)
                {
                    AssetTreeModel.GetData().ForEach(d => d.DisposeMedia());
                }
                else
                {
                    PGrid.packages.ForEach(d => d.DisposeMedia());
                }
                MediaManager.Load(info);
            }
        }

        private void OnAssetTreeSelectionChanged(IList<int> ids)
        {
            _selectedMedia = 0;
            _focusNativePackageMediaAfterRebuild = false;
            HandleAssetTreeSelectionChanged(ids);
        }

        private void OnAssetTreeDoubleClicked(int id)
        {
            if (id <= 0) return;

            AssetInfo info = AssetTreeModel.Find(id);
            OpenInSearch(info);
        }

        private void OnPackageGridDoubleClicked(AssetInfo info)
        {
            OpenInSearch(info);
        }

        private void PopulatePackageGridContextMenu(GenericMenu menu, IReadOnlyList<AssetInfo> selection, int clickedIndex)
        {
            if (selection == null || selection.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No Selection"));
                return;
            }

            // Determine eligible items for reindex
            List<AssetInfo> reindexable = selection
                .Where(info => info != null && info.ParentId <= 0 && !info.IsAbandoned && info.AssetSource != Asset.Source.CurrentProject && CanIndexPackageNow(info))
                .ToList();

            // Determine eligible items for import
            List<AssetInfo> importable = selection
                .Where(info => info != null
                    && info.AssetSource != Asset.Source.Directory
                    && info.AssetSource != Asset.Source.CurrentProject
                    && info.SafeName != Asset.NONE
                    && !info.IsAbandoned
                    && (info.IsDownloaded || (IsOnDemandPackageSource(info) && HasAssetStoreDownloadMetadata(info))))
                .ToList();

            // If all selected are registry packages and already installed, skip import
            importable = importable
                .Where(info => !AssetStore.IsInstalled(info))
                .ToList();

            // Show package name header if only one item is selected
            if (selection.Count == 1 && selection[0] != null)
            {
                menu.AddDisabledItem(new GUIContent(selection[0].GetDisplayName()));
                menu.AddSeparator("");
            }

            bool hasActions = false;

            if (importable.Count > 0)
            {
                bool needsDl = importable.Any(info => !info.IsDownloaded);
                string dlSuffix = needsDl ? " (will download)" : "";
                string caption = importable.Count == 1 ? $"Import Package{dlSuffix}" : $"Import {importable.Count} Packages{dlSuffix}";
                menu.AddItem(new GUIContent(caption), false, () =>
                {
                    ImportUI importUI = ImportUI.ShowWindow();
                    importUI.Init(importable);
                });
                hasActions = true;
            }

            if (reindexable.Count > 0)
            {
                bool needsDl = reindexable.Any(info => !info.IsDownloaded);
                string dlSuffix = needsDl ? " (will download)" : "";
                bool includeExcluded = reindexable.Any(info => info.Exclude);
                bool include = reindexable.Any(PackageIndexingPolicy.HasNoIndex);
                string verb = includeExcluded ? "Include Again & Index" : include ? "Include & Index" : "Index";
                string reindexCaption = reindexable.Count == 1 ? $"{verb} Now{dlSuffix}" : $"{verb} {reindexable.Count} Packages Now{dlSuffix}";
                menu.AddItem(new GUIContent(reindexCaption), false, () =>
                {
                    IncludeAndIndexPackagesNow(reindexable, includeExcluded);
                });
                hasActions = true;
            }

            if (!hasActions)
            {
                menu.AddDisabledItem(new GUIContent("No actions available"));
            }
        }

        private void PopulateSavedPackageSearchFromCurrentState(SavedPackageSearch search)
        {
            search.SearchPhrase = _assetSearchPhrase;
            search.PackagesListing = AI.Config.packagesListing;
            search.SRPs = AI.Config.assetSRPs;
            search.Deprecation = AI.Config.assetDeprecation;
            search.Maintenance = (int)_selectedMaintenance;
            search.PriceOption = _selectedPkgPriceOption;
            search.Price = _pkgSearchPrice;
            search.PackageSizeOption = _selectedPkgSizeOption;
            search.PackageSizeMB = _pkgSizeMB;
            search.UpdateDateOption = _selectedPkgUpdateDateOption;
            search.UpdateBeforeDate = _pkgUpdateBeforeDate?.ToString("yyyy-MM-dd");
            search.UpdateAfterDate = _pkgUpdateAfterDate?.ToString("yyyy-MM-dd");
            search.PurchaseDateOption = _selectedPkgPurchaseDateOption;
            search.PurchaseBeforeDate = _pkgPurchaseBeforeDate?.ToString("yyyy-MM-dd");
            search.PurchaseAfterDate = _pkgPurchaseAfterDate?.ToString("yyyy-MM-dd");
            search.UnityVersionOption = _selectedPkgUnityVersionOption;

            // Store full selection strings (will extract IDs during restore if needed)
            search.Publisher = _selectedPkgPublisher > 0 && _publisherNames.Length > _selectedPkgPublisher
                ? _publisherNames[_selectedPkgPublisher].Split('/').LastOrDefault()
                : null;

            search.Category = _selectedPkgCategory > 0 && _categoryNames.Length > _selectedPkgCategory
                ? _categoryNames[_selectedPkgCategory]
                : null;

            search.PackageTag = _selectedPkgTag > 0 && _tagNames.Length > _selectedPkgTag
                ? _tagNames[_selectedPkgTag]
                : null;
        }

        private void SavePackageSearch(string name)
        {
            SavedPackageSearch search = new SavedPackageSearch();
            search.Name = name;
            search.Color = ColorUtility.ToHtmlStringRGB(UnityEngine.Random.ColorHSV());

            PopulateSavedPackageSearchFromCurrentState(search);

            DBAdapter.DB.Insert(search);
            PackageSearches.Add(search);
            _activeSavedPackageSearchId = search.Id;
            _packageSearchesLoaded = false; // Force reload
            InvalidateNativePackageHeader();
        }

        private void LoadPackageSearch(SavedPackageSearch search)
        {
            _assetSearchPhrase = search.SearchPhrase ?? string.Empty;
            AI.Config.packagesListing = search.PackagesListing;
            AI.Config.assetSRPs = search.SRPs;
            AI.Config.assetDeprecation = search.Deprecation;
            _selectedMaintenance = PackageSearch.NormalizeMaintenanceOption(search.Maintenance);
            _selectedPkgPriceOption = search.PriceOption;
            _pkgSearchPrice = search.Price;
            _selectedPkgSizeOption = search.PackageSizeOption;
            _pkgSizeMB = search.PackageSizeMB;
            _selectedPkgUpdateDateOption = search.UpdateDateOption;
            _selectedPkgPurchaseDateOption = search.PurchaseDateOption;
            _selectedPkgUnityVersionOption = search.UnityVersionOption;

            // Parse date strings
            if (!string.IsNullOrEmpty(search.UpdateBeforeDate) && DateTime.TryParse(search.UpdateBeforeDate, out DateTime beforeDate))
            {
                _pkgUpdateBeforeDate = beforeDate;
            }
            else
            {
                _pkgUpdateBeforeDate = null;
            }

            if (!string.IsNullOrEmpty(search.UpdateAfterDate) && DateTime.TryParse(search.UpdateAfterDate, out DateTime afterDate))
            {
                _pkgUpdateAfterDate = afterDate;
            }
            else
            {
                _pkgUpdateAfterDate = null;
            }

            if (!string.IsNullOrEmpty(search.PurchaseBeforeDate) && DateTime.TryParse(search.PurchaseBeforeDate, out DateTime purchaseBeforeDate))
            {
                _pkgPurchaseBeforeDate = purchaseBeforeDate;
            }
            else
            {
                _pkgPurchaseBeforeDate = null;
            }

            if (!string.IsNullOrEmpty(search.PurchaseAfterDate) && DateTime.TryParse(search.PurchaseAfterDate, out DateTime purchaseAfterDate))
            {
                _pkgPurchaseAfterDate = purchaseAfterDate;
            }
            else
            {
                _pkgPurchaseAfterDate = null;
            }

            // Restore dropdowns (match by ID if brackets exist, otherwise by string)
            _selectedPkgPublisher = FindIndexByValue(_publisherNames, search.Publisher, splitPath: true);
            _selectedPkgCategory = FindIndexByValue(_categoryNames, search.Category, splitPath: false);
            _selectedPkgTag = FindIndexByValue(_tagNames, search.PackageTag, splitPath: false);

            _activeSavedPackageSearchId = search.Id;
            _requireAssetTreeRebuild = true;
            AI.SaveConfig();
            InvalidateNativePackageHeader();
        }

        private void OverrideSavedPackageSearch(SavedPackageSearch search)
        {
            PopulateSavedPackageSearchFromCurrentState(search);
            DBAdapter.DB.Update(search);
            InvalidateNativePackageHeader();
        }

        private void DeletePackageSearch(SavedPackageSearch search)
        {
            if (EditorUtility.DisplayDialog("Delete Saved Search", $"Are you sure you want to delete the saved search '{search.Name}'?", "Delete", "Cancel"))
            {
                DBAdapter.DB.Delete(search);
                PackageSearches.Remove(search);
                _packageSearchesLoaded = false; // Force reload
                if (_activeSavedPackageSearchId == search.Id)
                {
                    _activeSavedPackageSearchId = -1;
                }
                InvalidateNativePackageHeader();
            }
        }

        private void InvalidateNativePackageHeader()
        {
            _nativePackageSavedSearchesDirty = true;
            if (_nativePackagesBody == null || !IsNativePackagesShellActive()) return;

            RefreshNativePackageHeaderState();
            Repaint();
        }
    }
}
