using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.UIElements;
using static AssetInventory.AssetTreeViewControl;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace AssetInventory
{
    public partial class IndexUI
    {
        private bool _usageCalculationInProgress;
        private bool _usageCalculationDone;
        private AssetUsage _usageCalculation;
        private Vector2 _reportScrollPos;

        private List<AssetInfo> _assetUsage;
        private Dictionary<int, AssetInfo> _usedPackages;
        private List<AssetInfo> _paidPackages;
        private List<AssetInfo> _identifiedFiles;
        private List<AssetInfo> _selectedReportEntries;
        private List<string> _licenses;
        private AssetInfo _selectedReportEntry;
        private AssetInfo _selectedReportFile;

        private long _reportTreeSubPackageCount;
        private long _reportTreeSelectionSize;
        private readonly Dictionary<string, Tuple<int, Color>> _reportBulkTags = new Dictionary<string, Tuple<int, Color>>();
        private HashSet<int> _reportPackageTreeIds = new HashSet<int>();

        private const string ReportingRootClass = "ai-reporting-root";
        private const string ReportingSummaryClass = "ai-reporting-summary";
        private const string ReportingMetricClass = "ai-reporting-metric";
        private const string ReportingMetricLabelClass = "ai-reporting-metric-label";
        private const string ReportingMetricValueClass = "ai-reporting-metric-value";
        private const string ReportingMetricWideClass = "ai-reporting-metric-wide";
        private const string ReportingSplitClass = "ai-reporting-split";
        private const string ReportingLeftClass = "ai-reporting-left";
        private const string ReportingTreeClass = "ai-reporting-tree";
        private const string ReportingTreeContentClass = "ai-reporting-tree-content";
        private const string ReportingEmptyClass = "ai-reporting-empty";
        private const string ReportingEmptyButtonClass = "ai-reporting-empty-button";
        private const string ReportingSidebarClass = "ai-reporting-sidebar";
        private const string ReportingActionsClass = "ai-reporting-actions";
        private const string ReportingActionButtonClass = "ai-reporting-action-button";
        private const string ReportingProgressLabelClass = "ai-reporting-progress-label";
        private const string ReportingProgressDetailClass = "ai-reporting-progress-detail";
        private const string ReportingSelectionContainerClass = "ai-reporting-selection-container";
        private const string ReportingDetailClass = "ai-reporting-detail";
        private const string ReportingDetailActionsClass = "ai-reporting-detail-actions";
        private const string ReportingDetailActionButtonClass = "ai-reporting-detail-action-button";
        private const string ReportingDetailTagListClass = "ai-reporting-detail-tags";
        private const string ReportingDetailTagClass = "ai-reporting-detail-tag";

        private Label _nativeReportingProjectFiles;
        private Label _nativeReportingPackages;
        private Label _nativeReportingFiles;
        private Label _nativeReportingPaidPackages;
        private Label _nativeReportingLicenses;
        private ProgressBar _nativeReportingProgressBar;
        private Label _nativeReportingProgressDetail;
        private Button _nativeReportingStopButton;
        private VisualElement _nativeReportingTreeHost;
        private MultiColumnTreeView _nativeReportTreeView;
        private NativeAssetTreeViewAdapter _nativeReportTreeAdapter;
        private ScrollView _nativeReportingSelectionContainer;
        private int _nativeReportingAdvancedVisibilityStateHash;
        private int _nativeReportingTreeMode = -1;
        private bool _nativeReportingRenderedInProgress;

        [SerializeField] private CommonMultiColumnState reportColumnState;
        private int[] _reportColumnDisplayOrder;
        private AssetTreeViewControl ReportTreeView
        {
            get
            {
                if (_reportTreeView == null)
                {
                    EnsureReportColumnState();
                    _reportTreeView = new AssetTreeViewControl(ReportTreeModel, GetBackupCountForPackageList, this);

                    // Clear selection state after domain reload
                    _selectedReportEntry = null;
                    _selectedReportFile = null;
                    _selectedReportEntries?.Clear();
                }
                return _reportTreeView;
            }
        }
        private AssetTreeViewControl _reportTreeView;
        private readonly List<int> _reportTreeSelectedIds = new List<int>();

        private TreeModel<AssetInfo> ReportTreeModel
        {
            get
            {
                if (_reportTreeModel == null) _reportTreeModel = new TreeModel<AssetInfo>(new List<AssetInfo> {new AssetInfo().WithTreeData("Root", depth: -1)});
                return _reportTreeModel;
            }
        }
        private TreeModel<AssetInfo> _reportTreeModel;

        private CommonMultiColumnState EnsureReportColumnState()
        {
            CommonMultiColumnState defaultState = CreateDefaultMultiColumnState();
            defaultState.VisibleColumns = new[]
            {
                (int)AssetTreeViewControl.Columns.Name,
                (int)AssetTreeViewControl.Columns.FileCount,
                (int)AssetTreeViewControl.Columns.License,
                (int)AssetTreeViewControl.Columns.Version
            };
            CommonMultiColumnState columnState = AssetInventoryColumnLayoutCoordinator.Restore(
                AssetInventoryTableLayoutKind.Reporting,
                defaultState,
                AssetInventoryColumnLayoutCoordinator.GetPackageColumnKey,
                -1,
                false,
                out _reportColumnDisplayOrder,
                out _,
                out _);
            reportColumnState = columnState;
            return reportColumnState;
        }

        private void RefreshNativeReportingBody()
        {
            if (_nativeReportingBody == null) return;

            if (_nativeReportingBody.childCount == 0 ||
                AssetInventoryUITK.AdvancedVisibilityStateChanged(ref _nativeReportingAdvancedVisibilityStateHash))
            {
                RebuildNativeReportingBody();
            }

            UpdateNativeReportingSummary();
            if (_nativeReportingRenderedInProgress != _usageCalculationInProgress)
            {
                RebuildNativeReportingBody();
                UpdateNativeReportingSummary();
            }
            UpdateNativeReportingActions();
            RefreshNativeReportingTreeHost();
            _nativeReportTreeAdapter?.RepaintCells();
            _nativeReportingSelectionContainer?.MarkDirtyRepaint();
        }

        private void RebuildNativeReportingBody()
        {
            _nativeScrollViewState.Capture("reporting-selection", _nativeReportingSelectionContainer);
            _nativeReportingBody.Clear();
            _nativeReportingBody.AddToClassList(ReportingRootClass);

            _nativeReportingBody.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("reporting.hints.intro", () =>
                AssetInventoryUITK.CreateHelpBox(
                    "Reporting identifies packages used by the current project from asset GUIDs. Unity 2023+ origin tracking gives exact package matches; older imports can identify the package while the exact version may be ambiguous.",
                    MessageType.Info),
                onVisibilityChanged: RebuildNativeReportingBody));

            _nativeReportingProjectFiles = null;
            _nativeReportingPackages = null;
            _nativeReportingFiles = null;
            _nativeReportingPaidPackages = null;
            _nativeReportingLicenses = null;
            _nativeReportingBody.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("reporting.overview", () =>
            {
                VisualElement summary = new VisualElement();
                summary.AddToClassList(ReportingSummaryClass);
                summary.Add(CreateNativeReportingMetric("Project Files", out _nativeReportingProjectFiles));
                summary.Add(CreateNativeReportingMetric("Identified Packages", out _nativeReportingPackages));
                summary.Add(CreateNativeReportingMetric("Identified Files", out _nativeReportingFiles));
                summary.Add(CreateNativeReportingMetric("Paid Packages", out _nativeReportingPaidPackages));
                VisualElement licenses = CreateNativeReportingMetric("Used Licenses", out _nativeReportingLicenses);
                licenses.AddToClassList(ReportingMetricWideClass);
                summary.Add(licenses);
                return summary;
            }, onVisibilityChanged: RebuildNativeReportingBody));

            VisualElement split = new VisualElement();
            split.AddToClassList(ReportingSplitClass);
            _nativeReportingBody.Add(split);

            VisualElement left = new VisualElement();
            left.AddToClassList(ReportingLeftClass);
            split.Add(left);

            _nativeReportingTreeHost = new VisualElement();
            _nativeReportingTreeHost.AddToClassList(ReportingTreeClass);
            left.Add(_nativeReportingTreeHost);
            _nativeReportingTreeMode = -1;
            RefreshNativeReportingTreeHost();

            VisualElement sidebar = new VisualElement();
            sidebar.AddToClassList(ReportingSidebarClass);
            split.Add(sidebar);

            sidebar.Add(CreateNativeReportingActionsPanel());

            _nativeReportingSelectionContainer = new ScrollView(ScrollViewMode.Vertical);
            _nativeReportingSelectionContainer.AddToClassList(ReportingSelectionContainerClass);
            sidebar.Add(_nativeReportingSelectionContainer);
            RefreshNativeReportingSelectionDetails();
            _nativeScrollViewState.Restore("reporting-selection", _nativeReportingSelectionContainer);

            _nativeReportingAdvancedVisibilityStateHash = AssetInventoryUITK.GetAdvancedVisibilityStateHash();
            _nativeReportingRenderedInProgress = _usageCalculationInProgress;
        }

        private VisualElement CreateNativeReportingMetric(string label, out Label valueLabel)
        {
            VisualElement metric = new VisualElement();
            metric.AddToClassList(ReportingMetricClass);

            Label labelElement = new Label(label);
            labelElement.AddToClassList(ReportingMetricLabelClass);
            metric.Add(labelElement);

            valueLabel = new Label();
            valueLabel.AddToClassList(ReportingMetricValueClass);
            metric.Add(valueLabel);

            return metric;
        }

        private void UpdateNativeReportingSummary()
        {
            int assetUsageCount = _assetUsage?.Count ?? 0;
            int identifiedFilesCount = _identifiedFiles?.Count ?? 0;
            int identifiedPackagesCount = _usedPackages?.Count ?? 0;
            int paidPackagesCount = _paidPackages?.Count ?? 0;

            if (_nativeReportingProjectFiles != null) _nativeReportingProjectFiles.text = $"{assetUsageCount:N0}";
            if (_nativeReportingPackages != null) _nativeReportingPackages.text = assetUsageCount > 0 ? $"{identifiedPackagesCount:N0}" : "None";
            if (_nativeReportingFiles != null)
            {
                _nativeReportingFiles.text = assetUsageCount > 0
                    ? $"{identifiedFilesCount:N0} ({Mathf.RoundToInt((float)identifiedFilesCount / assetUsageCount * 100f)}%)"
                    : "None";
            }
            if (_nativeReportingPaidPackages != null) _nativeReportingPaidPackages.text = $"{paidPackagesCount:N0}";
            if (_nativeReportingLicenses != null) _nativeReportingLicenses.text = _licenses != null && _licenses.Count > 0 ? string.Join(", ", _licenses) : "n/a";
        }

        private void RefreshNativeReportingTreeHost()
        {
            if (_nativeReportingTreeHost == null) return;

            bool hasResults = _usedPackages != null && _usedPackages.Count > 0;
            int treeMode = hasResults ? 1 : (_usageCalculationInProgress ? 2 : 0);
            if (_nativeReportingTreeMode == treeMode && _nativeReportingTreeHost.childCount > 0)
            {
                return;
            }

            _nativeReportingTreeHost.Clear();
            _nativeReportingTreeMode = treeMode;

            if (hasResults)
            {
                _nativeReportTreeView = CreateNativeReportTreeView();
                _nativeReportingTreeHost.Add(_nativeReportTreeView);
                return;
            }

            _nativeReportTreeView = null;
            _nativeReportTreeAdapter = null;
            _nativeReportingTreeHost.Add(CreateNativeReportingEmptyState(_usageCalculationInProgress));
        }

        private void ScheduleNativeReportingTreeHostRefresh()
        {
            _nativeReportingTreeMode = -1;
            if (_nativeReportingTreeHost == null) return;

            _nativeReportingTreeHost.schedule.Execute(RefreshNativeReportingTreeHost).ExecuteLater(0);
        }

        private MultiColumnTreeView CreateNativeReportTreeView()
        {
            CommonMultiColumnState columnState = EnsureReportColumnState();

            _nativeReportTreeAdapter = new NativeAssetTreeViewAdapter(
                columnState,
                ReportTreeView,
                "AI4.Reporting.AssetTree",
                false,
                SyncNativeReportColumnState,
                displayOrder: _reportColumnDisplayOrder);
            AssetInventoryColumnLayoutCoordinator.Register(
                AssetInventoryTableLayoutKind.Reporting,
                _nativeReportTreeAdapter,
                reportColumnState,
                AssetInventoryColumnLayoutCoordinator.GetPackageColumnKey);
            _nativeReportTreeAdapter.SelectionChanged += OnNativeReportTreeSelectionChanged;
            _nativeReportTreeAdapter.ItemChosen += info => OnReportTreeDoubleClicked(info.TreeId);

            MultiColumnTreeView treeView = _nativeReportTreeAdapter.View;
            treeView.AddToClassList(ReportingTreeContentClass);
            _nativeReportTreeAdapter.SetRoot(ReportTreeModel.Root, _reportTreeSelectedIds);

            return treeView;
        }

        private void OnNativeReportTreeSelectionChanged(IList<int> ids)
        {
            _reportTreeSelectedIds.Clear();
            _reportTreeSelectedIds.AddRange(ids);
            OnReportTreeSelectionChanged(ids);
        }

        private void SyncNativeReportColumnState()
        {
            if (_nativeReportTreeAdapter == null || reportColumnState == null) return;

            AssetInventoryColumnLayoutCoordinator.UpdateColumns(
                AssetInventoryTableLayoutKind.Reporting,
                _nativeReportTreeAdapter,
                reportColumnState,
                AssetInventoryColumnLayoutCoordinator.GetPackageColumnKey);
        }

        private VisualElement CreateNativeReportingEmptyState(bool inProgress)
        {
            Button identify = null;
            if (!inProgress)
            {
                identify = AssetInventoryUITK.CreateSecondaryButton("Identify Used Packages", CalculateAssetUsage);
                identify.AddToClassList(ReportingEmptyButtonClass);
            }

            CommonEmptyState empty = AssetInventoryUITK.CreateEmptyState(
                inProgress ? "Identifying packages..." : "No report data yet",
                inProgress
                    ? "Results will appear as soon as package identification finishes."
                    : "Analyze the current project to see which indexed packages are used by your assets.",
                identify);
            empty.AddToClassList(ReportingEmptyClass);
            return empty;
        }

        private VisualElement CreateNativeReportingActionsPanel()
        {
            _nativeReportingProgressBar = null;
            _nativeReportingProgressDetail = null;
            _nativeReportingStopButton = null;

            VisualElement section = AssetInventoryUITK.CreateSection("Actions");
            section.AddToClassList(ReportingActionsClass);

            if (_usageCalculationInProgress)
            {
                _nativeReportingStopButton = AssetInventoryUITK.CreateSecondaryButton("Stop Identification", () =>
                {
                    if (_usageCalculation != null)
                    {
                        _usageCalculation.CancellationRequested = true;
                    }
                    UpdateNativeReportingActions();
                });
                _nativeReportingStopButton.AddToClassList(ReportingActionButtonClass);
                section.Add(_nativeReportingStopButton);

                Label progressLabel = new Label("Identification Progress");
                progressLabel.AddToClassList(ReportingProgressLabelClass);
                section.Add(progressLabel);

                _nativeReportingProgressBar = AssetInventoryUITK.CreateProgressBar(GetNativeReportingProgressTitle(), GetNativeReportingProgressValue());
                section.Add(_nativeReportingProgressBar);

                _nativeReportingProgressDetail = new Label();
                _nativeReportingProgressDetail.AddToClassList(ReportingProgressDetailClass);
                section.Add(_nativeReportingProgressDetail);
            }
            else
            {
                Button identify = AssetInventoryUITK.CreatePrimaryButton("Identify Used Packages", CalculateAssetUsage);
                identify.AddToClassList(ReportingActionButtonClass);
                section.Add(identify);
            }

            section.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("reporting.actions.export", () =>
            {
                Button export = AssetInventoryUITK.CreateSecondaryButton("Export Data...", OpenReportExportWindow);
                export.AddToClassList(ReportingActionButtonClass);
                return export;
            }, onVisibilityChanged: RebuildNativeReportingBody));

            section.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("reporting.actions.freebies", () =>
            {
                Button freebies = AssetInventoryUITK.CreateSecondaryButton("Find Freebies...", () => FreebieUI.ShowWindow());
                freebies.AddToClassList(ReportingActionButtonClass);
                return freebies;
            }, onVisibilityChanged: RebuildNativeReportingBody));

            UpdateNativeReportingActions();
            return section;
        }

        private void UpdateNativeReportingActions()
        {
            if (!_usageCalculationInProgress) return;

            if (_nativeReportingStopButton != null)
            {
                _nativeReportingStopButton.SetEnabled(_usageCalculation != null && !_usageCalculation.CancellationRequested);
            }
            if (_nativeReportingProgressBar != null)
            {
                _nativeReportingProgressBar.value = GetNativeReportingProgressValue();
                _nativeReportingProgressBar.title = GetNativeReportingProgressTitle();
            }
            if (_nativeReportingProgressDetail != null)
            {
                _nativeReportingProgressDetail.text = _usageCalculation?.CurrentMain ?? string.Empty;
            }
        }

        private string GetNativeReportingProgressTitle()
        {
            if (_usageCalculation == null || _usageCalculation.MainCount <= 0) return "0/0";
            return $"{_usageCalculation.MainProgress}/{_usageCalculation.MainCount}";
        }

        private float GetNativeReportingProgressValue()
        {
            if (_usageCalculation == null || _usageCalculation.MainCount <= 0) return 0f;
            return _usageCalculation.MainProgress / (float)_usageCalculation.MainCount;
        }

        private void ScheduleNativeReportingSelectionRefresh()
        {
            if (_nativeReportingSelectionContainer == null) return;

            _nativeReportingSelectionContainer.schedule.Execute(RefreshNativeReportingSelectionDetails).ExecuteLater(0);
        }

        private void RefreshNativeReportingSelectionDetails()
        {
            if (_nativeReportingSelectionContainer == null) return;

            _nativeReportingSelectionContainer.Clear();
            _nativeReportingSelectionContainer.AddToClassList(ReportingDetailClass);

            if (_selectedReportFile != null)
            {
                _nativeReportingSelectionContainer.Add(CreateNativeReportingFileDetails(_selectedReportFile));
            }
            else if (_selectedReportEntry != null)
            {
                _nativeReportingSelectionContainer.Add(CreateNativeReportingPackageDetails(_selectedReportEntry));
            }
            else if (_selectedReportEntries != null && _selectedReportEntries.Count > 0)
            {
                _nativeReportingSelectionContainer.Add(CreateNativeReportingBulkDetails());
            }
        }

        private VisualElement CreateNativeReportingPackageDetails(AssetInfo info)
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Package");
            section.AddToClassList(ReportingDetailClass);

            AddNativeReportingDetailRow(section, null, "Name", info.GetDisplayName(), info.Location);
            AddNativeReportingDetailRow(section, "package.id", "Id", info.SafeName, info.SafeName);
            AddNativeReportingDetailRow(section, "package.version", "Version", info.GetVersion(true));
            AddNativeReportingDetailRow(section, "package.license", "License", info.License, info.LicenseLocation);
            AddNativeReportingDetailRow(section, "package.publisher", "Publisher", info.GetDisplayPublisher());
            AddNativeReportingDetailRow(section, "package.category", "Category", info.GetDisplayCategory());
            if (info.PackageSize > 0)
            {
                AddNativeReportingDetailRow(section, "package.size", "Size", EditorUtility.FormatBytes(info.PackageSize));
            }
            if (!string.IsNullOrWhiteSpace(info.SupportedUnityVersions))
            {
                AddNativeReportingDetailRow(section, "package.unityversions", "Unity", info.SupportedUnityVersions);
            }
            if (info.AssetSource != Asset.Source.CurrentProject)
            {
                AddNativeReportingDetailRow(section, "package.price", "Price", info.GetPrice() > 0 ? info.GetPriceText() : "Free");
            }
            if ((ShowAdvanced() || AI.Config.tab == (int)AssetInventoryTab.Packages) && info.AssetSource != Asset.Source.CurrentProject)
            {
                AddNativeReportingDetailRow(section, "package.indexedfiles", "Indexed Files", $"{info.FileCount:N0}");
            }
            if (info.ChildInfoCount > 0)
            {
                AddNativeReportingDetailRow(section, "package.childcount", info.AssetSource == Asset.Source.AssetManager ? "Collections" : "Sub-Packages", $"{info.ChildInfoCount:N0}");
            }
            AddNativeReportingDetailRow(section, "package.source", "Source", FormatNativeReportingAssetSource(info));

            VisualElement actions = CreateNativeReportingDetailActions();
            actions.Add(CreateNativeReportingDetailButton("Open in Search", () => OpenReportSelectionInSearch(info)));
            if (info.AssetSource == Asset.Source.Synty && !string.IsNullOrWhiteSpace(info.OriginalLocation))
            {
                actions.Add(CreateNativeReportingDetailButton("Product Page", () => AI.OpenURL(info.OriginalLocation)));
            }
            if (info.ForeignId > 0 && (info.AssetSource == Asset.Source.AssetStorePackage || info.AssetSource == Asset.Source.Synty))
            {
                actions.Add(CreateNativeReportingDetailButton("Asset Store", () => AI.OpenStoreURL(info.GetAssetStoreLink())));
            }
            actions.Add(CreateNativeReportingDetailButton("Export...", OpenReportExportWindow));
            section.Add(actions);

            return section;
        }

        private VisualElement CreateNativeReportingFileDetails(AssetInfo info)
        {
            VisualElement section = AssetInventoryUITK.CreateSection("File");
            section.AddToClassList(ReportingDetailClass);

            string path = info.GetPath(true);
            AddNativeReportingDetailRow(section, null, "Name", string.IsNullOrWhiteSpace(path) ? info.FileName : Path.GetFileName(path), path);
            AddNativeReportingDetailRow(section, "asset.location", "Location", string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path), path);
            AddNativeReportingDetailRow(section, "asset.status", "Status", info.FileStatus);
            AddNativeReportingDetailRow(section, "asset.size", "Size", info.Size > 0 ? EditorUtility.FormatBytes(info.Size) : null);
            if (info.Width > 0)
            {
                AddNativeReportingDetailRow(section, "asset.dimensions", "Dimensions", $"{info.Width:N0} x {info.Height:N0} pixels");
            }
            if (info.Length > 0)
            {
                AddNativeReportingDetailRow(section, "asset.length", "Length", StringUtils.FormatDuration(info.Length));
            }
            if (ShowAdvanced() || info.InProject)
            {
                AddNativeReportingDetailRow(section, null, "In Project", info.InProject ? "Yes" : "No");
            }
            if (ShouldShowNativeReportingDependencyRow(info))
            {
                AddNativeReportingDetailRow(section, "asset.dependencies", "Dependencies", FormatNativeReportingDependencyState(info));
            }

            VisualElement actions = CreateNativeReportingDetailActions();
            actions.Add(CreateNativeReportingDetailButton("Open in Search", () => OpenReportSelectionInSearch(info)));
            if (info.InProject)
            {
                actions.Add(CreateNativeReportingDetailButton("Ping", () => PingAsset(info)));
                actions.Add(CreateNativeReportingDetailButton("Where Used", () => ShowWhereUsed(info)));
            }
            if (CanShowDependencyTree(info))
            {
                actions.Add(CreateNativeReportingDetailButton("Dependencies", () =>
                {
                    DependenciesUI depUI = DependenciesUI.ShowWindow();
                    depUI.Init(info, OpenAssetFileInSearch);
                }));
            }
            section.Add(actions);

            return section;
        }

        private VisualElement CreateNativeReportingBulkDetails()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Selection");
            section.AddToClassList(ReportingDetailClass);

            int rootPackageCount = Mathf.Max(0, _selectedReportEntries.Count - (int)_reportTreeSubPackageCount);
            AddNativeReportingDetailRow(section, "package.bulk.count", "Selected Items", $"{rootPackageCount:N0}");
            if (_reportTreeSubPackageCount > 0)
            {
                AddNativeReportingDetailRow(section, "package.bulk.childcount", "Sub-Packages", $"{_reportTreeSubPackageCount:N0}");
            }
            AddNativeReportingDetailRow(section, "package.bulk.size", "Size on Disk", EditorUtility.FormatBytes(_reportTreeSelectionSize));

            if (_reportBulkTags.Count > 0)
            {
                VisualElement tags = new VisualElement();
                tags.AddToClassList(ReportingDetailTagListClass);
                foreach (KeyValuePair<string, Tuple<int, Color>> tag in _reportBulkTags.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                {
                    Label pill = AssetInventoryUITK.CreateStatusPill($"{tag.Key} ({tag.Value.Item1:N0})");
                    pill.AddToClassList(ReportingDetailTagClass);
                    pill.style.backgroundColor = new StyleColor(new Color(tag.Value.Item2.r, tag.Value.Item2.g, tag.Value.Item2.b, 0.22f));
                    tags.Add(pill);
                }
                section.Add(tags);
            }

            VisualElement actions = CreateNativeReportingDetailActions();
            actions.Add(CreateNativeReportingDetailButton("Export Selection...", OpenReportExportWindow));
            section.Add(actions);

            return section;
        }

        private void AddNativeReportingDetailRow(VisualElement section, string key, string label, string value, string tooltip = null)
        {
            if (section == null || string.IsNullOrWhiteSpace(value)) return;

            VisualElement row = AssetInventoryUITK.CreateKeyValueRow(label, value);
            row.tooltip = tooltip ?? value;
            if (string.IsNullOrWhiteSpace(key))
            {
                section.Add(row);
                return;
            }

            section.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock(key, () => row, onVisibilityChanged: ScheduleNativeReportingSelectionRefresh));
        }

        private VisualElement CreateNativeReportingDetailActions()
        {
            VisualElement actions = new VisualElement();
            actions.AddToClassList(ReportingDetailActionsClass);
            return actions;
        }

        private static Button CreateNativeReportingDetailButton(string text, Action click)
        {
            Button button = AssetInventoryUITK.CreateSecondaryButton(text, click);
            button.AddToClassList(ReportingDetailActionButtonClass);
            return button;
        }

        private static string FormatNativeReportingAssetSource(AssetInfo info)
        {
            if (info == null) return string.Empty;
            if (info.AssetSource == Asset.Source.Synty) return "Synty Importer";
            return info.AssetSource == Asset.Source.AssetStorePackage ? "Asset Store" : StringUtils.CamelCaseToWords(info.AssetSource.ToString());
        }

        private string FormatNativeReportingDependencyState(AssetInfo info)
        {
            switch (info.DependencyState)
            {
                case AssetInfo.DependencyStateOptions.Done:
                case AssetInfo.DependencyStateOptions.Partial:
                case AssetInfo.DependencyStateOptions.NotPossible:
                    return FormatDependencyCount(info, ShowAdvanced());
                case AssetInfo.DependencyStateOptions.Calculating:
                    return "Calculating...";
                case AssetInfo.DependencyStateOptions.Failed:
                    return "Failed to determine";
                case AssetInfo.DependencyStateOptions.Unknown:
                    return "Not calculated";
                default:
                    return "Cannot determine";
            }
        }

        private static bool ShouldShowNativeReportingDependencyRow(AssetInfo info)
        {
            if (info == null) return false;
            return info.InProject || HasDependencyRows(info) || info.DependencyState != AssetInfo.DependencyStateOptions.Unknown;
        }

        private void OpenReportSelectionInSearch(AssetInfo info)
        {
            if (info == null) return;

            if (_reportPackageTreeIds.Contains(info.TreeId))
            {
                OpenInSearch(info, true, true);
                return;
            }

            string searchPhrase = info.FileName;
            if (string.IsNullOrEmpty(searchPhrase) && !string.IsNullOrEmpty(info.Path))
            {
                searchPhrase = Path.GetFileName(info.Path);
            }

            AssetInfo package = ReportTreeModel.Find(info.AssetId);
            OpenInSearch(package ?? info, true, true, searchPhrase);
        }

        private void OpenReportExportWindow()
        {
            ExportUI exportUI = ExportUI.ShowWindow();
            exportUI.Init(GetReportExportList(), false, 1, reportColumnState?.VisibleColumns);
        }

        private List<AssetInfo> GetReportExportList()
        {
            if (_selectedReportEntries != null && _selectedReportEntries.Count > 1)
            {
                return _selectedReportEntries;
            }

            // Filter only for meaningful assets, since this is the overall database export.
            IEnumerable<AssetInfo> sourceAssets = _assets ?? Enumerable.Empty<AssetInfo>();
            return sourceAssets
                .Where(a => a.AssetSource == Asset.Source.AssetStorePackage ||
                    a.AssetSource == Asset.Source.CustomPackage ||
                    a.AssetSource == Asset.Source.RegistryPackage)
                .ToList();
        }

        private async void CalculateAssetUsage()
        {
            if (_usageCalculationInProgress) return;
            if (_assets == null) return;
            _usageCalculationInProgress = true;

            bool packageDataAvailable = false;
            try
            {
                List<AssetInfo> allAssets = _assets.Where(asset => asset != null).ToList();

                _usageCalculation = new AssetUsage();
                _assetUsage = await _usageCalculation.Calculate() ?? new List<AssetInfo>();
                _assetUsage = _assetUsage.Where(asset => asset != null).ToList();

                _identifiedFiles = _assetUsage.Where(info => info.CurrentState != Asset.State.Unknown).ToList();

                // add installed packages (sync path avoids async wait)
                Dictionary<string, PackageInfo> packageCollection = AssetStore.GetProjectPackagesSync();
                if (packageCollection != null)
                {
                    packageDataAvailable = true;
                    int unmatchedCount = 0;
                    foreach (PackageInfo packageInfo in packageCollection.Values.Where(info => info != null))
                    {
                        if (packageInfo.source == PackageSource.BuiltIn) continue;

                        AssetInfo matchedAsset = allAssets.FirstOrDefault(info => info.SafeName == packageInfo.name);
                        if (matchedAsset == null)
                        {
                            // Debug.Log($"Registry package '{packageInfo.name}' is not yet indexed, information will be incomplete.");
                            matchedAsset = new AssetInfo();
                            matchedAsset.AssetSource = Asset.Source.RegistryPackage;
                            matchedAsset.SafeName = packageInfo.name;
                            matchedAsset.DisplayName = packageInfo.displayName;
                            matchedAsset.Version = packageInfo.version;
                            matchedAsset.Id = int.MaxValue - unmatchedCount;
                            matchedAsset.AssetId = int.MaxValue - unmatchedCount;
                            unmatchedCount++;
                        }
                        _assetUsage.Add(matchedAsset);
                    }
                }
                Assets.ResolveParents(_assetUsage, allAssets);

                _usedPackages = _assetUsage.GroupBy(a => a.AssetId).Select(a => a.First()).ToDictionary(a => a.AssetId, a => a);

                // Restore correct DisplayName for sub-packages whose names were
                // overwritten by the origin overlay (which reports the parent's name)
                foreach (AssetInfo package in _usedPackages.Values)
                {
                    if (package.ParentId > 0)
                    {
                        AssetInfo indexed = allAssets.FirstOrDefault(a => a.AssetId == package.AssetId);
                        if (indexed != null)
                        {
                            if (!string.IsNullOrEmpty(indexed.DisplayName)) package.DisplayName = indexed.DisplayName;
                            if (!string.IsNullOrEmpty(indexed.SafeName)) package.SafeName = indexed.SafeName;
                        }
                    }
                }

                // Ensure parents of identified sub-packages are present in the
                // usage data so that the tree hierarchy is always complete.
                // Clone parents to avoid mutating the shared _assets objects
                // (whose ChildInfos contains grid-context sub-packages).
                List<AssetInfo> parentsToAdd = new List<AssetInfo>();
                foreach (AssetInfo package in _usedPackages.Values)
                {
                    if (package.ParentId > 0 && !_usedPackages.ContainsKey(package.ParentId))
                    {
                        AssetInfo parent = allAssets.FirstOrDefault(a => a.AssetId == package.ParentId);
                        if (parent != null && !parentsToAdd.Any(p => p.AssetId == parent.AssetId))
                        {
                            AssetInfo parentClone = new AssetInfo(parent.ToAsset());
                            parentClone.ResetChildInfos();
                            parentsToAdd.Add(parentClone);
                        }
                    }
                }
                foreach (AssetInfo parentClone in parentsToAdd)
                {
                    _assetUsage.Add(parentClone);
                    _usedPackages[parentClone.AssetId] = parentClone;
                }
                if (parentsToAdd.Count > 0)
                {
                    Assets.ResolveParents(parentsToAdd, allAssets);
                }

                // Enrich used packages with identified files
                foreach (AssetInfo file in _identifiedFiles)
                {
                    if (_usedPackages.TryGetValue(file.AssetId, out AssetInfo package))
                    {
                        package.AddChildInfo(file);
                    }
                }

                // Find unidentified files (files under Assets/ not matched to any package)
                HashSet<string> identifiedGuids = new HashSet<string>(_identifiedFiles.Where(a => !string.IsNullOrEmpty(a.Guid)).Select(a => a.Guid));
                string[] allGuids = AssetDatabase.FindAssets("", new[] {"Assets"});
                List<AssetInfo> unidentifiedFiles = new List<AssetInfo>();

                foreach (string guid in allGuids)
                {
                    if (!identifiedGuids.Contains(guid))
                    {
                        string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                        if (!string.IsNullOrEmpty(assetPath) && !AssetDatabase.IsValidFolder(assetPath)
                            && !assetPath.Contains(AI.TEMP_FOLDER) && !assetPath.Contains(UnityPreviewGenerator.PREVIEW_FOLDER))
                        {
                            AssetInfo unidentifiedFile = new AssetInfo();
                            unidentifiedFile.Guid = guid;
                            unidentifiedFile.Path = assetPath;
                            unidentifiedFile.FileName = Path.GetFileName(assetPath);
                            unidentifiedFile.Id = guid.GetHashCode();
                            unidentifiedFiles.Add(unidentifiedFile);
                        }
                    }
                }

                // Create artificial "-Unidentified-" package if there are unidentified files
                if (unidentifiedFiles.Count > 0)
                {
                    AssetInfo unidentifiedPackage = new AssetInfo();
                    unidentifiedPackage.DisplayName = "-Unidentified-";
                    unidentifiedPackage.SafeName = "-Unidentified-";
                    unidentifiedPackage.AssetSource = Asset.Source.CustomPackage;
                    unidentifiedPackage.Id = -1;
                    unidentifiedPackage.AssetId = -1;
                    unidentifiedPackage.ChildInfos = unidentifiedFiles;
                    _assetUsage.Add(unidentifiedPackage);
                    _usedPackages[-1] = unidentifiedPackage;
                }

                _paidPackages = _usedPackages.Where(a => a.Value.GetPrice() > 0).Select(a => a.Value).ToList();
                _licenses = new List<string> {"Standard Unity Asset Store EULA"};
                _licenses.AddRange(_usedPackages.Values.Where(info => info != null && !string.IsNullOrWhiteSpace(info.License)).Select(info => info.License).Distinct());
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not calculate asset usage: {e.Message}");
            }

            _requireReportTreeRebuild = true;
            _requireAssetTreeRebuild = true;
            _usageCalculationInProgress = false;
            // Only mark as done if package data was available, otherwise allow retry when packages become available
            _usageCalculationDone = packageDataAvailable;
        }

        private void CalculateAssetUsageAutomatically()
        {
            if (!SearchScopeModel.ShouldAutoCalculateAssetUsage(GetConfiguredSearchScope())) return;
            CalculateAssetUsage();
        }

        private void CreateReportTree()
        {
            _requireReportTreeRebuild = false;
            List<AssetInfo> data = new List<AssetInfo>();
            AssetInfo root = new AssetInfo().WithTreeData("Root", depth: -1);
            data.Add(root);

            _reportPackageTreeIds.Clear();

            if (_assetUsage != null)
            {
                // apply filters
                IEnumerable<AssetInfo> filteredAssets = _assetUsage.GroupBy(a => a.AssetId).Select(a => a.First()).Where(a => !string.IsNullOrEmpty(a.GetDisplayName()));

                IOrderedEnumerable<AssetInfo> orderedAssets = filteredAssets.OrderBy(a => a.GetDisplayName(), StringComparer.OrdinalIgnoreCase);

                // First pass: add all packages without file hierarchies so that
                // ReorderSubPackages can safely rearrange them without orphaning
                // file/folder nodes or matching file nodes via AssetId lookups.
                foreach (AssetInfo package in orderedAssets)
                {
                    AI.GetObserver().Attach(package);

                    // Store identified file count in FileCount for display in column
                    package.FileCount = package.ChildInfoCount;
                    data.Add(package.WithTreeData(package.GetDisplayName(), package.AssetId, depth: 0));
                    _reportPackageTreeIds.Add(package.AssetId);
                }

                // re-add parents to sub-packages if they were filtered out
                ReAddMissingParents(orderedAssets, data);

                // track any re-added parent TreeIds
                foreach (AssetInfo item in data)
                {
                    if (item.Depth >= 0 && !_reportPackageTreeIds.Contains(item.TreeId))
                    {
                        _reportPackageTreeIds.Add(item.TreeId);
                    }
                }

                // reorder sub-packages
                ReorderSubPackages(data);

                // Second pass: insert file hierarchies under each package using its
                // final depth. Iterate backwards so earlier indices stay stable.
                int folderIdCounter = -100; // Negative IDs for folder nodes to avoid conflicts
                int fileIdCounter = int.MaxValue / 2; // Counts down; uses middle range to avoid collision with
                                                      // both normal package AssetIds (low) and unmatched registry
                                                      // packages (near int.MaxValue)
                for (int packageIdx = data.Count - 1; packageIdx >= 1; packageIdx--)
                {
                    AssetInfo package = data[packageIdx];
                    if (package.AssetSource == Asset.Source.RegistryPackage) continue;
                    if (!package.HasChildInfos) continue;

                    int baseDepth = package.Depth;

                    // Build folder structure from file paths
                    List<AssetInfo> fileHierarchy = BuildReportFileHierarchy(package.EnumerateChildInfos(), baseDepth, ref folderIdCounter, ref fileIdCounter);

                    data.InsertRange(packageIdx + 1, fileHierarchy);
                }
            }

            ReportTreeModel.SetData(data, true);
            _reportTreeSelectedIds.Clear();
            if (_nativeReportTreeAdapter != null)
            {
                _nativeReportTreeAdapter.SetRoot(ReportTreeModel.Root, _reportTreeSelectedIds);
                _nativeReportTreeAdapter.CollapseAll();
            }
            OnReportTreeSelectionChanged(new List<int>());

            _textureLoading3?.Cancel();
            _textureLoading3?.Dispose();
            _textureLoading3 = new CancellationTokenSource();
            AssetUtils.LoadTextures(data, _textureLoading3.Token);

            ScheduleNativeReportingTreeHostRefresh();
        }

        internal static List<AssetInfo> BuildReportFileHierarchy(IEnumerable<AssetInfo> files, int baseDepth, ref int folderIdCounter, ref int fileIdCounter)
        {
            ReportFolderNode root = new ReportFolderNode(string.Empty);
            foreach (AssetInfo file in files.OrderBy(GetReportFilePath, StringComparer.OrdinalIgnoreCase))
            {
                AddReportFile(root, file);
            }

            List<AssetInfo> result = new List<AssetInfo>();
            AppendReportFolder(root, result, baseDepth, ref folderIdCounter, ref fileIdCounter);
            return result;
        }

        private static void AddReportFile(ReportFolderNode root, AssetInfo file)
        {
            string[] parts = GetReportPathParts(file);
            ReportFolderNode current = root;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                string folderName = parts[i];
                if (!current.Folders.TryGetValue(folderName, out ReportFolderNode folder))
                {
                    folder = new ReportFolderNode(folderName);
                    current.Folders.Add(folderName, folder);
                }

                folder.FileCount++;
                current = folder;
            }

            string fileName = parts.Length > 0 ? parts[parts.Length - 1] : GetReportFilePath(file);
            current.Files.Add(new ReportFileNode(file, fileName));
        }

        private static void AppendReportFolder(ReportFolderNode folder, List<AssetInfo> result, int depth, ref int folderIdCounter, ref int fileIdCounter)
        {
            foreach (ReportFolderNode childFolder in folder.Folders.Values)
            {
                folderIdCounter--;
                string folderName = childFolder.FileCount > 0 ? $"{childFolder.Name} ({childFolder.FileCount:N0})" : childFolder.Name;
                result.Add(new AssetInfo().WithTreeData(folderName, folderIdCounter, depth: depth + 1));
                AppendReportFolder(childFolder, result, depth + 1, ref folderIdCounter, ref fileIdCounter);
            }

            folder.Files.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
            foreach (ReportFileNode file in folder.Files)
            {
                fileIdCounter--;
                AssetInfo fileNode = new AssetInfo(file.Info).WithTreeData(file.Name, fileIdCounter, depth: depth + 1);
                fileNode.FileCount = 0;
                result.Add(fileNode);
            }
        }

        private static string[] GetReportPathParts(AssetInfo file)
        {
            string filePath = GetReportFilePath(file).Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(filePath)) return new[] {"Unknown"};
            return filePath.Split(new[] {'/'}, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string GetReportFilePath(AssetInfo file)
        {
            if (!string.IsNullOrEmpty(file.ProjectPath)) return file.ProjectPath;
            if (!string.IsNullOrEmpty(file.Path)) return file.Path;
            if (!string.IsNullOrEmpty(file.FileName)) return file.FileName;
            return "Unknown";
        }

        private sealed class ReportFolderNode
        {
            public readonly string Name;
            public readonly SortedDictionary<string, ReportFolderNode> Folders = new SortedDictionary<string, ReportFolderNode>(StringComparer.OrdinalIgnoreCase);
            public readonly List<ReportFileNode> Files = new List<ReportFileNode>();
            public int FileCount;

            public ReportFolderNode(string name)
            {
                Name = name;
            }
        }

        private sealed class ReportFileNode
        {
            public readonly AssetInfo Info;
            public readonly string Name;

            public ReportFileNode(AssetInfo info, string name)
            {
                Info = info;
                Name = name;
            }
        }

        private void OnReportTreeDoubleClicked(int id)
        {
            if (id <= 0) return;

            AssetInfo info = ReportTreeModel.Find(id);
            string searchPhrase = null;

            // If this is a file (not a package), find the parent package and use filename as search phrase
            if (info != null && !_reportPackageTreeIds.Contains(id))
            {
                // Extract filename for search phrase
                searchPhrase = info.FileName;
                if (string.IsNullOrEmpty(searchPhrase) && !string.IsNullOrEmpty(info.Path))
                {
                    searchPhrase = System.IO.Path.GetFileName(info.Path);
                }

                // Files have AssetId pointing to the parent package
                // Packages are stored with AssetId as their tree id
                info = ReportTreeModel.Find(info.AssetId);
            }

            OpenInSearch(info, true, true, searchPhrase);
        }

        private void OnReportTreeSelectionChanged(IList<int> ids)
        {
            _selectedReportEntry = null;
            _selectedReportFile = null;
            _selectedReportEntries = _selectedReportEntries ?? new List<AssetInfo>();
            _selectedReportEntries.Clear();

            if (ids.Count == 1 && ids[0] > 0)
            {
                AssetInfo selected = ReportTreeModel.Find(ids[0]);
                if (selected != null)
                {
                    if (_reportPackageTreeIds.Contains(ids[0]))
                    {
                        // Package or sub-package selected
                        _selectedReportEntry = selected;
                        _selectedReportEntry.Refresh();
                    }
                    else
                    {
                        // File selected
                        _selectedReportFile = selected;
                        _selectedReportFile.Refresh();
                        _selectedReportFile.CheckIfInProject();
                        _selectedReportFile.IsMaterialized = Assets.IsMaterialized(_selectedReportFile.ToAsset(), _selectedReportFile);
                        CalcDependenciesOnDemand(_selectedReportFile);
                        if (AI.Config.pingSelected && _selectedReportFile.InProject) PingAsset(_selectedReportFile);
                    }
                }
            }

            // load all selected items but count each only once
            HashSet<int> seen = new HashSet<int>();
            foreach (int id in ids)
            {
                GatherTreeChildren(id, _selectedReportEntries, seen, ReportTreeModel);
            }
            // Filter to only include actual packages (any depth), not folders or file nodes
            _selectedReportEntries = _selectedReportEntries.Where(a => _reportPackageTreeIds.Contains(a.TreeId)).ToList();

            _reportBulkTags.Clear();
            _selectedReportEntries.ForEach(info => info.PackageTags?.ForEach(t =>
            {
                if (!_reportBulkTags.ContainsKey(t.Name)) _reportBulkTags.Add(t.Name, new Tuple<int, Color>(0, t.GetColor()));
                _reportBulkTags[t.Name] = new Tuple<int, Color>(_reportBulkTags[t.Name].Item1 + 1, _reportBulkTags[t.Name].Item2);
            }));

            _reportTreeSubPackageCount = _selectedReportEntries.Count(a => a.ParentId > 0);
            _reportTreeSelectionSize = _selectedReportEntries.Sum(a => a.PackageSize);
            ScheduleNativeReportingSelectionRefresh();
        }
    }
}
