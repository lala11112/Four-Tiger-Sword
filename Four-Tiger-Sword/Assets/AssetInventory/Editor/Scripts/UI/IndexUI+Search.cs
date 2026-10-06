using ImpossibleRobert.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AudioTool;
using Newtonsoft.Json;
#if !AUDIO_TOOL_NOAUDIO
using JD.EditorAudioUtils;
#endif
using UnityEditor;
using UnityEditor.UIElements;
#if !USE_TUTORIALS
using UnityEditor.PackageManager;
#endif
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;
#pragma warning disable CS0618 // Type or member is obsolete

namespace AssetInventory
{
    public partial class IndexUI
    {
        private const float DRAG_THRESHOLD = 5f; // pixels
        private const float DRAG_DELAY = 0.5f; // seconds
        private const string SearchRootClass = "ai-search-root";
        private const string SearchBodyClass = "ai-search-body";
        private const string SearchBodyWithCollapsedLeadingPaneClass = "ai-search-body-with-collapsed-leading-pane";
        private const string SearchBodyWithCollapsedTrailingPaneClass = "ai-search-body-with-collapsed-trailing-pane";
        private const string SearchMainPaneClass = "ai-search-main-pane";
        private const string ResultPaneHostClass = "ai-result-pane-host";
        private const string SearchTreeClass = "ai-search-tree";
        private const string SearchGridProjectBadgeClass = "ai-result-grid-badge-project";
        private const string SearchGridHiddenBadgeClass = "ai-result-grid-badge-danger";
        private const string SearchWorkspaceButtonClass = "ai-search-workspace-button";
        private const string SearchControlsClass = "ai-search-controls";
        private const string SearchRowClass = "ai-search-row";
        private const string SearchActionWrapperClass = "ai-search-action-wrapper";
        private const string SearchActionGroupClass = "ai-search-action-group";
        private const string SearchLabelClass = "ai-search-label";
        private const string SearchFieldClass = "ai-search-field-native";
        private const string SearchGoClass = "ai-search-go";
        private const string SearchSaveClass = "ai-search-save";
        private const string SearchAuxButtonClass = "ai-search-aux-button";
        private const string SearchTypePopupClass = "ai-search-type-popup";
        private const string SearchExpertPopupClass = "ai-search-expert-popup";
        private const string SearchHintClass = "ai-search-hint";
        private const string SearchPickerInstructionClass = "ai-search-picker-instruction";
        private const string SearchVariablesClass = "ai-search-variables";
        private const string SearchVariableRowClass = "ai-search-variable-row";
        private const string SearchVariableLabelClass = "ai-search-variable-label";
        private const string SearchVariableFieldClass = "ai-search-variable-field";
        private const string SearchVariableMenuClass = "ai-search-variable-menu";
        private const string SearchErrorClass = "ai-search-error";
        private const string SearchEmptyStateClass = "ai-search-empty-state";
        private const string SearchFooterClass = "ai-search-footer";
        private const string NarrowDetailsMainClass = "ai-narrow-details-main";
        private const string NarrowDetailsViewClass = "ai-narrow-details-view";
        private const string NarrowDetailsActionClass = "ai-narrow-details-action";
        private const string NarrowDetailsSelectionClass = "ai-narrow-details-selection";
        private const string NarrowDetailsBackClass = "ai-narrow-details-back";
        private const string NarrowDetailsFooterClass = "ai-narrow-details-footer";
        private static readonly string[] SearchGridDetailOptions = {"Tiny", "Compact", "Standard", "Detailed"};
        private static readonly int[] SearchGridDetailPresetSizes = {56, 88, 150, 230};

        private enum InMemoryModeState
        {
            None,
            Init,
            Active
        }

        // customizable interaction modes, search mode will only show search tab contents and no actions except "Select"
        public bool searchMode;

        // will show additional workspace layer
        public bool workspaceMode;

        // special mode that will return accompanying textures to the selected one, trying to identify normal, metallic etc. 
        public bool textureMode;

        // will hide right-side inspector pane
        public bool hideDetailsPane;
        public bool hideMainNavigation;

        // will not select items in the project window upon selection
        public bool disablePings;

        // will cause clicking on a grid tile to return the selection to the caller and close the window
        public bool instantSelection;

        // locks the search to a specific type, e.g. "Prefabs" 
        public string fixedSearchType;

        // event handler during search mode
        protected Action<string> searchModeCallback;
        protected Action<Dictionary<string, string>> searchModeTextureCallback;

        private List<AssetInfo> _files;
        private List<AssetInfo> _filteredFiles;
        private bool _searchPreviewSessionInitialized;

        private GridControl SGrid
        {
            get
            {
                if (_sgrid == null)
                {
                    _sgrid = new GridControl();
                }
                return _sgrid;
            }
        }
        private GridControl _sgrid;

        [SerializeField] private CommonMultiColumnState searchColumnState;
        private int[] _searchColumnDisplayOrder;
        private TreeModel<AssetInfo> _searchTreeModel;
        private Dictionary<int, Texture2D> _filePreviewCache = new Dictionary<int, Texture2D>();
        private Dictionary<int, AssetInfo> _pendingVirtualPreviews = new Dictionary<int, AssetInfo>();
        private Dictionary<int, Texture2D> FilePreviewCache => _filePreviewCache ??= new Dictionary<int, Texture2D>();
        private Dictionary<int, AssetInfo> PendingVirtualPreviews => _pendingVirtualPreviews ??= new Dictionary<int, AssetInfo>();
        private bool _virtualPreviewRetryRunning;
        private ToolbarSearchField _nativeSearchField;
        private ToolbarSearchField _nativeSearchInMemoryField;
        private VisualElement _nativeSearchSavedSearches;
        private VisualElement _nativeSearchControls;
        private VisualElement _nativeSearchFilterChip;
        private Button _nativeSearchFilterChipLabel;
        private Button _nativeSearchFilterChipReset;
        private VisualElement _nativeSearchVariables;
        private Label _nativeSearchError;
        private CommonEmptyState _nativeSearchEmptyState;
        private PopupField<string> _nativeSearchTypePopup;
        private PopupField<string> _nativeSearchExpertPopup;
        private Button _nativeSearchInMemoryButton;
        private VisualElement _nativeSearchFooter;
        private VisualElement _nativeSearchViewModeControl;
        private CommonGridSizeControl _nativeSearchGridSizeControl;
        private PopupField<string> _nativeSearchTileDetailPopup;
        private Button _nativeSearchPreviewAnimationButton;
        private Label _nativeSearchFooterSummary;
        private CommonPaginationControl _nativeSearchPager;
        private VisualElement _nativeSearchScopeControl;
        private CommonResizableSidePaneLayout _nativeSearchPaneLayout;
        private VisualElement _nativeSearchNarrowMain;
        private VisualElement _nativeSearchNarrowDetails;
        private VisualElement _nativeSearchNarrowDetailsAction;
        private Label _nativeSearchNarrowDetailsSelection;
        private bool _nativeSearchNarrowDetailsOpen;
        private MultiColumnTreeView _nativeSearchTreeView;
        private NativeAssetTreeViewAdapter _nativeSearchTreeAdapter;
        private CommonSelectableGridView<AssetInfo> _nativeSearchGridView;
        private readonly List<int> _nativeSearchGridSelectionBuffer = new List<int>();
        private readonly List<AssetInfo> _nativeSearchGridSelectedItemsBuffer = new List<AssetInfo>();
        private readonly List<int> _nativeSearchSelection = new List<int>();
        private List<int> _pendingNativeSearchSelection;
        private bool _pendingNativeSearchRevealSelection;
        private bool _nativeSearchTreeRefreshPending;
        private double _lastNativeSearchPreviewRecoveryTime;
        private bool _syncingNativeSearchColumns;
        private bool _nativeSearchSavedSearchesDirty = true;
        private bool _nativeSearchSavedSearchesShowAdvanced;
        private bool _nativeSearchShowsInMemoryMode;
        private int _nativeSearchAdvancedVisibilityStateHash;
        private int _nativeSearchCompositionSignature = int.MinValue;
        private string _nativeSearchVariableSignature;
        private AssetFile _pendingSearchNavigationTarget;

        public Texture2D GetFilePreview(int fileId)
        {
            FilePreviewCache.TryGetValue(fileId, out Texture2D texture);
            return texture;
        }

        private CommonMultiColumnState EnsureSearchColumnState()
        {
            CommonMultiColumnState defaultState = SearchTreeViewControl.CreateDefaultMultiColumnState();
            CommonMultiColumnState columnState = AssetInventoryColumnLayoutCoordinator.Restore(
                AssetInventoryTableLayoutKind.Search,
                defaultState,
                AssetInventoryColumnLayoutCoordinator.GetSearchColumnKey,
                SearchTreeViewControl.GetSourceColumnIndex(AI.Config.sortField),
                AI.Config.sortDescending,
                out _searchColumnDisplayOrder,
                out int sortIndex,
                out bool sortDescending);
            int sortField = SearchTreeViewControl.GetSortField(sortIndex);
            if (sortField >= 0)
            {
                AI.Config.sortField = sortField;
                AI.Config.sortDescending = sortDescending;
            }
            searchColumnState = columnState;
            return searchColumnState;
        }

        private void PopulateSearchTreeContextMenu(GenericMenu menu, IReadOnlyList<AssetInfo> selection, int clickedIndex)
        {
            PopulateSearchGridContextMenu(menu, selection, clickedIndex);
        }

        private void OnSearchTreeSelectionChanged(IList<int> ids)
        {
            UpdateSearchSelectionChangedManually();

            if (ids == null || ids.Count == 0)
            {
                _selectedEntry = null;
                SGrid.SetBulkSelection(null);
                _requireSearchSelectionUpdate = true;
                ScheduleNativeSearchInspectorRebuild();
                RefreshNativeSearchNarrowDetailsAction();
                return;
            }

            // Resolve all selected ids to AssetInfo objects
            List<AssetInfo> selectedItems = ids
                .Select(id => _searchTreeModel?.Find(id))
                .Where(item => item != null)
                .ToList();

            // Populate bulk selection data for the inspector panel
            SGrid.SetBulkSelection(selectedItems);

            // Set single selection for detail view
            _selectedEntry = selectedItems.FirstOrDefault();
            _requireSearchSelectionUpdate = true;
            if (_selectedEntry == null) return;

            if (_selectedEntry.TreeId >= 0 && _filteredFiles != null && _selectedEntry.TreeId < _filteredFiles.Count)
            {
                SGrid.selectionTile = _selectedEntry.TreeId;
            }
            _searchInspectorTab = 0;
            ScheduleNativeSearchInspectorRebuild();
            RefreshNativeSearchNarrowDetailsAction();
        }

        private void OnSearchTreeDoubleClick(int id)
        {
            AssetInfo info = _searchTreeModel?.Find(id);
            if (info != null)
            {
                OnSearchDoubleClick(info);
            }
        }

        private void PopulateSearchGridContextMenu(GenericMenu menu, IReadOnlyList<AssetInfo> selection, int clickedIndex)
        {
            if (selection == null || selection.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No Selection"));
                return;
            }

            // Header with single selection name
            if (selection.Count == 1 && selection[0] != null)
            {
                menu.AddDisabledItem(new GUIContent(selection[0].FileName));
                menu.AddSeparator("");
            }

            // Import action (for asset packages/files that can be imported)
            List<AssetInfo> importable = selection
                .Where(info => info != null
                    && info.AssetSource != Asset.Source.Directory
                    && info.SafeName != Asset.NONE
                    && !info.IsAbandoned
                    && !info.InProject
                    && (info.IsDownloaded || IsDownloadable(info)))
                .Where(info => !AssetStore.IsInstalled(info))
                .ToList();

            bool needsDownload = importable.Any(info => !info.IsDownloaded);
            string actionName = searchMode ? "Select" : "Import";
            if (importable.Count > 0)
            {
                string caption = searchMode || importable.Count == 1 ? actionName : $"{actionName} {importable.Count} Files";
                if (needsDownload) caption += " (will download)";
                menu.AddItem(new GUIContent(caption), false, () =>
                {
                    if (searchMode)
                    {
                        ExecuteSingleAction();
                    }
                    else
                    {
                        ImportBulkFiles(importable);
                    }
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent(actionName));
            }

            // Open Create/Recreate AI Caption
            List<AssetInfo> aiCaptionTargets = selection
                .Where(info => info != null && !info.IsVirtual)
                .ToList();
            if (aiCaptionTargets.Count > 0 && AI.Actions.AICaptionsEnabled)
            {
                string aiCaptionLabel;
                if (aiCaptionTargets.Count == 1)
                {
                    bool hasCaption = !string.IsNullOrWhiteSpace(aiCaptionTargets[0].AICaption);
                    aiCaptionLabel = hasCaption ? "Recreate AI Caption" : "Create AI Caption";
                }
                else
                {
                    aiCaptionLabel = "Create AI Captions";
                }
                menu.AddItem(new GUIContent(aiCaptionLabel), false, () =>
                {
                    RecreateAICaptions(aiCaptionTargets);
                });
            }

            // Recreate Preview
            List<AssetInfo> previewable = selection
                .Where(info => info != null && !info.IsVirtual && PreviewManager.IsPreviewable(info.FileName, true, info))
                .ToList();
            if (previewable.Count > 0)
            {
                string previewLabel = previewable.Count == 1 ? "Recreate Preview" : "Recreate Previews";
                menu.AddItem(new GUIContent(previewLabel), false, () =>
                {
                    RecreatePreviews(previewable);
                });
            }

            // Remove from Project
            List<AssetInfo> removable = selection
                .Where(info => info != null && info.InProject)
                .ToList();
            if (removable.Count > 0)
            {
                string removeLabel = removable.Count == 1 ? "Remove from Project" : $"Remove {removable.Count} from Project";
                menu.AddItem(new GUIContent(removeLabel), false, () =>
                {
                    UninstallPackageUI.ShowWindow().Init(removable);
                });
            }

            // Hide / Unhide
            List<AssetInfo> hideable = selection.Where(info => info != null && !info.IsVirtual).ToList();
            if (hideable.Count > 0)
            {
                menu.AddSeparator("");
                bool anyVisible = hideable.Any(info => !info.Hidden);
                bool anyHidden = hideable.Any(info => info.Hidden);

                if (anyVisible)
                {
                    string hideLabel = hideable.Count == 1 ? "Hide from Results" : $"Hide {hideable.Count} from Results";
                    menu.AddItem(new GUIContent(hideLabel), false, () =>
                    {
                        Assets.SetFilesHidden(hideable.Select(i => i.Id).ToList(), true);
                        _requireSearchUpdate = true;
                    });
                }
                if (anyHidden)
                {
                    string unhideLabel = hideable.Count == 1 ? "Unhide" : $"Unhide {hideable.Count}";
                    menu.AddItem(new GUIContent(unhideLabel), false, () =>
                    {
                        Assets.SetFilesHidden(hideable.Select(i => i.Id).ToList(), false);
                        _requireSearchUpdate = true;
                    });
                }
            }
        }

        private InMemoryModeState _inMemoryMode = InMemoryModeState.None;
        private string _searchPhrase;
        private string _previousSearchPhrase;
        private string _searchPhraseInMemory;
        private string _searchWidth;
        private string _searchHeight;
        private string _searchLength;
        private string _searchSize;
        private bool _checkMaxWidth;
        private bool _checkMaxHeight;
        private bool _checkMaxLength;
        private bool _checkMaxSize;
        private string _searchVertexCount;
        private bool _checkMaxVertexCount;
        private int _selectedPublisher;
        private int _selectedCategory;
        private int _selectedExpertSearchField;
        private int _selectedAsset;
        private int _selectedPackageTypes = 1;
        private int _selectedPackageSRPs = 1;
        private int _selectedPackageTag;
        private int _selectedFileTag;
        private int _selectedPriceOption;
        private float _searchPrice;
        private int _selectedImageType;
        private int _selectedColorOption;
        private Color _selectedColor;
        private int _selectedHiddenFilter;
        private string[] _hiddenFilterOptions = {"Hide", "Show", "Only Hidden"};

        private Vector2 _searchScrollPos;
        private Vector2 _inspectorScrollPos;

        private int _resultCount;
        private int _originalResultCount;
        private int _curPage = 1;
        private int _pageCount;

        private CancellationTokenSource _textureLoading;
        private CancellationTokenSource _textureLoading2;
        private CancellationTokenSource _textureLoading3;
        private CancellationTokenSource _extraction;
        private Dictionary<AssetInfo, CancellationTokenSource> _dependencyCancellationTokens;

        private AssetInfo _selectedEntry;
        private Workspace _selectedWorkspace;

        private int _searchInspectorTab;
        private float _nextSearchTime;
        private float _nextVariableDetectionTime;
        private DateTime _lastTileSizeChange;
        private string _searchError;
        private bool _searchDone;
        private bool _lockSelection;
        private string _curOperation;
        private bool _pickerSelectionInProgress;
        private double _pickerSelectionStartTime;
        private int _fixedSearchTypeIdx;
        private bool _draggingPossible;
        private bool _dragging;
        private Vector2 _dragStartPosition;
        private float _dragStartTime;
        private bool _dragImportInProgress;
        private int _dragImportIndex;
        private int _dragImportCount;
        private double _dragImportStartTime;
        private string _dragImportMessage;
        private bool _keepSearchResultPage = true;
        private readonly Dictionary<string, Tuple<int, Color>> _assetFileBulkTags = new Dictionary<string, Tuple<int, Color>>();
        private AnimationPlayer _animationPlayer;
        private int _animatedTileIndex = -1;
        private AssetInfo _animatedEntry;

        // Multi-animation for visible tiles using the shared manager
        private readonly AnimationPlaybackManager<int> _visibleAnimations = new AnimationPlaybackManager<int>(
            maxConcurrentLoads: 3, 
            isEnabledCheck: () => AI.Config.playVisibleSearchAnimations
        );
        private Vector2 _lastSearchScrollPos;
        private float _lastViewHeight;
        private float _searchGridViewHeight; // Captured from the retained grid viewport.
        private int _visibleAnimationTriggerFrames; // Retry counter for loading animations after grid dimensions are ready

        private int _assetFileAMProjectCount;
        private int _assetFileAMCollectionCount;
        private int _assetFileAICaptionCount;

        // Cached project search results to avoid re-execution on page navigation
        private List<AssetInfo> _cachedProjectFiles;
        private List<AssetInfo> _cachedProjectOnlyFiles;
        private string _cachedProjectSearchKey;

        // Track the currently active saved search
        private int _activeSavedSearchIdBacking = -1;
        private int _activeSavedSearchId
        {
            get => _activeSavedSearchIdBacking;
            set
            {
                if (_activeSavedSearchIdBacking != value)
                {
                    _activeSavedSearchIdBacking = value;
                    // Reset restoration flag when active search changes
                    _variablesRestoredFromDb = false;
                }
            }
        }

        // Search query variables
        private Dictionary<string, SearchVariable> _searchVariables = new Dictionary<string, SearchVariable>();
        [NonSerialized] private bool _hasSearchVariables;
        [NonSerialized] private bool _variablesRestoredFromDb;

        private List<SavedSearch> Searches
        {
            get
            {
                if (_searches == null || !_searchesLoaded)
                {
                    _searches = DBAdapter.DB.Table<SavedSearch>().ToList();
                    _searchesLoaded = true;
                }
                return _searches;
            }
        }
        private List<SavedSearch> _searches;
        private bool _searchesLoaded;

        private List<Workspace> Workspaces
        {
            get
            {
                if (_workspaces == null || !_workspacesLoaded)
                {
                    _workspaces = DBAdapter.DB.Table<Workspace>().ToList();
                    _workspacesLoaded = true;
                }
                return _workspaces;
            }
        }
        private List<Workspace> _workspaces;
        private bool _workspacesLoaded;

        private void InitWorkspace()
        {
            if (!ShowWorkspaces() || AI.Config.workspace <= 0)
            {
                _selectedWorkspace = null;
                return;
            }
            SetWorkspace(Workspaces.FirstOrDefault(ws => ws.Id == AI.Config.workspace));
        }

        private void SetWorkspace(Workspace ws)
        {
            _selectedWorkspace = ws;
            List<WorkspaceSearch> searches = _selectedWorkspace?.LoadSearches();
            if (searches == null || searches.Count == 0)
            {
                // deactivate current in-memory mode if no searches are available
                _inMemoryMode = InMemoryModeState.None;
                _searchPhrase = "";
                _previousSearchPhrase = "";
                _requireSearchUpdate = true;
            }

            int oldWorkspace = AI.Config.workspace;
            AI.Config.workspace = ws == null ? 0 : ws.Id;
            if (oldWorkspace != AI.Config.workspace) AI.SaveConfig();
        }

        public void SetInitialSearch(string searchPhrase)
        {
            _searchPhrase = searchPhrase;
            _previousSearchPhrase = searchPhrase;
            AI.Config.tab = 0;
            _activeSavedSearchId = -1;
            DetectVariablesInSearchPhrase();
        }

        private void OnSearchDoubleClick(AssetInfo obj)
        {
            if ((searchMode || AI.Config.doubleClickAction > 0 || AI.Config.doubleClickAltAction > 0) && _selectedEntry != null)
            {
                if (searchMode)
                {
                    ExecuteSingleAction();
                }
                else
                {
                    int action = SGrid.LastClickAlt ? AI.Config.doubleClickAltAction : AI.Config.doubleClickAction;

                    AudioManager.StopAudio();
                    DisposeBlocking();

                    switch (action)
                    {
                        case 2:
                            _ = PerformCopyTo(_selectedEntry, _importFolder, false, true);
                            break;

                        case 3:
                            _ = PerformCopyTo(_selectedEntry, _importFolder);
                            break;

                        case 4:
                            Open(_selectedEntry);
                            break;
                    }
                }
            }
        }

        private void OnSearchKeyboardSelection(int selectionIndex)
        {
            int count = _filteredFiles?.Count ?? 0;
            if (count == 0) return;

            SGrid.LimitSelection(count);
            if (selectionIndex < 0 || selectionIndex >= count) selectionIndex = SGrid.selectionTile;
            _selectedEntry = _filteredFiles[selectionIndex];
            _requireSearchSelectionUpdate = true;
            ScheduleNativeSearchInspectorRebuild();
            DisposeAnimTexture();

            // Mark that selection was changed via keyboard navigation
            // Used event is thrown if user manually selected the entry
            UpdateSearchSelectionChangedManually();
        }

        private void UpdateSearchSelectionChangedManually()
        {
            Event evt = Event.current;
            _searchSelectionChangedManually = evt != null
                && (evt.type == EventType.Used
                    || evt.type == EventType.MouseDown
                    || evt.type == EventType.MouseUp
                    || evt.type == EventType.KeyDown);
        }

        private void RecreatePreviewEditor()
        {
            if (_isCleaningUp) return;

            Object previewObject = _selectedEntry.InProject ? AssetDatabase.LoadAssetAtPath<Object>(_selectedEntry.ProjectPath) : null;
            if (_previewEditor != null)
            {
                DestroyImmediate(_previewEditor);
                _previewEditor = null;
            }

            if (previewObject != null)
            {
                _previewEditor = Editor.CreateEditor(previewObject);
            }
        }

        private bool ShowWorkspaces()
        {
            return (workspaceMode || AI.Config.alwaysShowWorkspaces);
        }

        private void RefreshSearchField()
        {
            _nativeSearchField?.Blur();
            _nativeSearchInMemoryField?.Blur();
        }

        private void RefreshNativeSearchBody()
        {
            if (_nativeSearchBody == null) return;

            RestoreNativeSearchPreviewStateIfNeeded();

            bool showsInMemoryMode = _inMemoryMode != InMemoryModeState.None;
            if (_nativeSearchBody.childCount == 0 ||
                _nativeSearchShowsInMemoryMode != showsInMemoryMode ||
                _nativeSearchCompositionSignature != GetNativeSearchCompositionSignature() ||
                AssetInventoryUITK.AdvancedVisibilityStateChanged(ref _nativeSearchAdvancedVisibilityStateHash))
            {
                RebuildNativeSearchBody();
            }

            FlushNativeSearchTreeRefresh();
            RefreshNativeSearchGridView();
            if (_nativeSearchHierarchyActive && _requireHierarchyRebuild)
            {
                RefreshNativeSearchHierarchy();
            }
            RefreshNativeSearchHeaderState();
            RefreshNativeSearchInspector();
            _nativeSearchTreeAdapter?.RepaintCells();
        }

        private void RestoreNativeSearchPreviewStateIfNeeded()
        {
            VisualElement searchRoot = _nativeSearchBody;
            if (searchRoot == null || !searchRoot.ClassListContains(SearchRootClass))
            {
                searchRoot = rootVisualElement?.Q<VisualElement>(className: SearchRootClass);
            }

            CommonSelectableGridView<AssetInfo> retainedGrid = _nativeSearchGridView ?? searchRoot?.Q<CommonSelectableGridView<AssetInfo>>();
            if (retainedGrid == null || retainedGrid.ItemsSource == null || retainedGrid.ItemsSource.Count == 0) return;

            _nativeSearchGridView = retainedGrid;
            IList<AssetInfo> retainedItems = retainedGrid.ItemsSource;
            if (_filteredFiles == null)
            {
                _filteredFiles = retainedItems as List<AssetInfo> ?? new List<AssetInfo>(retainedItems);
            }
            if (!ReferenceEquals(_filteredFiles, retainedItems)) return;

            if (!SGrid.HasPreviewSlots || SGrid.PreviewCount != retainedItems.Count)
            {
                SGrid.ResetPreviews(retainedItems.Count);
                SGrid.Init(_assets, _filteredFiles, CalculateSearchBulkSelection);
            }

            bool hasVisiblePreview = false;
            retainedGrid.Query<Image>(name: "preview").ForEach(preview => hasVisiblePreview |= preview.image != null);
            if (hasVisiblePreview) return;

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastNativeSearchPreviewRecoveryTime < 2d) return;

            _lastNativeSearchPreviewRecoveryTime = now;
            UpdateSearchPreviews();
        }

        private void RebuildNativeSearchBody()
        {
            if (_nativeSearchBody == null) return;

            CaptureNativeSearchInspectorScroll();
            CaptureNativeSearchSidebarFiltersScroll();
            _nativeSearchBody.Clear();
            _nativeSearchBody.AddToClassList(SearchRootClass);

            if (searchMode)
            {
                Label instruction = AssetInventoryUITK.CreateCopyLabel(textureMode
                    ? "Select one image to import it."
                    : "Select one result to import it.");
                instruction.AddToClassList(SearchPickerInstructionClass);
                _nativeSearchBody.Add(instruction);
            }

            _nativeSearchSavedSearches = AssetInventoryUITK.CreateSavedSearchStrip();
            _nativeSearchBody.Add(_nativeSearchSavedSearches);

            _nativeSearchControls = AssetInventoryUITK.CreateSection();
            _nativeSearchControls.AddToClassList(SearchControlsClass);
            _nativeSearchFilterChip = null;
            _nativeSearchFilterChipLabel = null;
            _nativeSearchFilterChipReset = null;
            if (_inMemoryMode == InMemoryModeState.None)
            {
                _nativeSearchControls.Add(CreateNativeSearchMainRow());
            }
            else
            {
                _nativeSearchControls.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("asset.hints.inmemoryactive", () =>
                {
                    VisualElement hint = AssetInventoryUITK.CreateHelpBox(
                        $"In-Memory search is active. The {_originalResultCount:N0} results of the initial search are now the foundation for any subsequent, much faster, search.",
                        MessageType.Info);
                    hint.AddToClassList(SearchHintClass);
                    return hint;
                }, onVisibilityChanged: RebuildNativeSearchBody));
                _nativeSearchControls.Add(CreateNativeSearchInMemoryRow());
            }
            _nativeSearchBody.Add(_nativeSearchControls);

            _nativeSearchVariables = new VisualElement();
            _nativeSearchVariables.AddToClassList(SearchVariablesClass);
            _nativeSearchBody.Add(_nativeSearchVariables);

            _nativeSearchError = new Label();
            _nativeSearchError.AddToClassList(SearchErrorClass);
            _nativeSearchBody.Add(_nativeSearchError);

            VisualElement body = new VisualElement();
            body.AddToClassList(SearchBodyClass);

            _nativeSearchHierarchyTreeView = null;
            _nativeSearchHierarchyClearButton = null;
            _nativeSearchHierarchyEmpty = null;
            _nativeSearchSidebarFiltersScroll = null;
            _nativeSearchSidebarFiltersContent = null;
            _nativeSearchSidebarFiltersStateHash = int.MinValue;
            _nativeSearchHierarchyActive = !searchMode && AI.Config.showSearchHierarchySideBar;
            VisualElement hierarchyPane = !searchMode ? CreateNativeSearchHierarchyPane() : null;

            VisualElement mainPane = new VisualElement();
            mainPane.AddToClassList(SearchMainPaneClass);
            VisualElement resultPane = new VisualElement();
            resultPane.AddToClassList(ResultPaneHostClass);
            mainPane.Add(resultPane);
            _nativeSearchInspectorPane = null;
            _nativeSearchInspectorSettingsButton = null;
            _nativeSearchInspectorScroll = null;
            _nativeSearchInspectorScrollTab = int.MinValue;
            _nativeSearchInspectorContentStateHash = int.MinValue;
            _nativeSearchGridView = null;
            _nativeSearchNarrowMain = null;
            _nativeSearchNarrowDetails = null;
            _nativeSearchNarrowDetailsAction = null;
            _nativeSearchNarrowDetailsSelection = null;
            if (AI.Config.searchViewMode == 0)
            {
                _nativeSearchTreeView = CreateNativeSearchTreeView();
                _nativeSearchTreeView.AddToClassList(SearchTreeClass);
                PositionNativeSearchResult(_nativeSearchTreeView);
                resultPane.Add(_nativeSearchTreeView);
            }
            else
            {
                _nativeSearchTreeView = null;
                _nativeSearchTreeAdapter = null;
                _nativeSearchGridView = CreateNativeSearchGridView();
                PositionNativeSearchResult(_nativeSearchGridView);
                resultPane.Add(_nativeSearchGridView);
            }

            _nativeSearchEmptyState = CreateNativeSearchEmptyState();
            PositionNativeSearchResult(_nativeSearchEmptyState);
            resultPane.Add(_nativeSearchEmptyState);

            _nativeSearchFooter = CreateNativeSearchFooter();
            mainPane.Add(_nativeSearchFooter);

            VisualElement inspectorPane = null;
            if (!hideDetailsPane)
            {
                _nativeSearchInspectorPane = CreateNativeSearchInspectorPane();
                inspectorPane = _nativeSearchInspectorPane;
            }

            CommonResizableSidePaneLayout.PaneDefinition leading = hierarchyPane == null
                ? null
                : new CommonResizableSidePaneLayout.PaneDefinition
                {
                    Content = hierarchyPane,
                    PreferredWidth = GetNativeSearchHierarchyPaneWidth(),
                    MinimumWidth = 180f,
                    MaximumWidth = 480f,
                    IsOpen = AI.Config.showSearchHierarchySideBar,
                    StateChanged = OnNativeSearchHierarchyPaneStateChanged
                };
            CommonResizableSidePaneLayout.PaneDefinition trailing = inspectorPane == null
                ? null
                : new CommonResizableSidePaneLayout.PaneDefinition
                {
                    Content = inspectorPane,
                    PreferredWidth = GetNativeSearchInspectorPaneWidth(),
                    MinimumWidth = 220f,
                    MaximumWidth = 720f,
                    IsOpen = AI.Config.showSearchSideBar,
                    StateChanged = OnNativeSearchInspectorPaneStateChanged
                };
            CommonResizableSidePaneLayout.LayoutOptions layoutOptions = new CommonResizableSidePaneLayout.LayoutOptions
            {
                MainMinimumWidth = 320f,
                CompactThreshold = 280f,
                WideThreshold = 480f
            };
            bool useNarrowDetails = inspectorPane != null && UseNativeNarrowDetailsLayout();
            if (useNarrowDetails)
            {
                _nativeSearchPaneLayout = AssetInventoryUITK.CreateResizableSidePaneLayout(mainPane, leading, options: layoutOptions);
                _nativeSearchNarrowMain = _nativeSearchPaneLayout;
                _nativeSearchNarrowMain.AddToClassList(NarrowDetailsMainClass);
                body.Add(_nativeSearchNarrowMain);

                Button back = AssetInventoryUITK.CreateSecondaryButton("Results", CloseNativeSearchNarrowDetails);
                back.tooltip = "Return to the search results.";
                back.AddToClassList(NarrowDetailsBackClass);
                _nativeSearchInspectorPane.Leading.Insert(0, back);
                _nativeSearchNarrowDetails = inspectorPane;
                _nativeSearchNarrowDetails.AddToClassList(NarrowDetailsViewClass);
                _nativeSearchNarrowDetails.RegisterCallback<KeyDownEvent>(OnNativeSearchNarrowDetailsKeyDown);
                body.Add(_nativeSearchNarrowDetails);
                ApplyNativeSearchNarrowDetailsState();
            }
            else
            {
                _nativeSearchNarrowDetailsOpen = false;
                _nativeSearchPaneLayout = AssetInventoryUITK.CreateResizableSidePaneLayout(mainPane, leading, trailing, layoutOptions);
                body.Add(_nativeSearchPaneLayout);
            }
            RefreshNativeSearchPaneGutters();
            _nativeSearchBody.Add(body);

            _nativeSearchShowsInMemoryMode = _inMemoryMode != InMemoryModeState.None;
            _nativeSearchSavedSearchesDirty = true;
            _nativeSearchSavedSearchesShowAdvanced = ShowAdvanced();
            _nativeSearchAdvancedVisibilityStateHash = AssetInventoryUITK.GetAdvancedVisibilityStateHash();
            _nativeSearchCompositionSignature = GetNativeSearchCompositionSignature();
            _nativeSearchVariableSignature = null;
            RefreshNativeSearchHeaderState();
            RefreshNativeSearchInspector();
        }

        private CommonEmptyState CreateNativeSearchEmptyState()
        {
            CommonEmptyState empty = AssetInventoryUITK.CreateEmptyState(
                "Search your asset library",
                "Enter a name, path, tag, or phrase. Use the filters above when you need to narrow the result set.");
            empty.AddToClassList(SearchEmptyStateClass);
            return empty;
        }

        private int GetNativeSearchCompositionSignature()
        {
            unchecked
            {
                int hash = searchMode ? 1 : 0;
                hash = hash * 31 + (workspaceMode ? 1 : 0);
                hash = hash * 31 + (textureMode ? 1 : 0);
                hash = hash * 31 + (hideMainNavigation ? 1 : 0);
                hash = hash * 31 + (hideDetailsPane ? 1 : 0);
                hash = hash * 31 + (AI.Config.showSearchHierarchySideBar ? 1 : 0);
                hash = hash * 31 + (AI.Config.showSearchSideBar ? 1 : 0);
                hash = hash * 31 + AI.Config.searchViewMode;
                hash = hash * 31 + (fixedSearchType == null ? 0 : StringComparer.Ordinal.GetHashCode(fixedSearchType));
                hash = hash * 31 + (UseNativeNarrowDetailsLayout() ? 1 : 0);
                hash = hash * 31 + GetNativeSearchHeaderOptionsSignature(_types, _expertSearchFields);
                return hash;
            }
        }

        internal static int GetNativeSearchHeaderOptionsSignature(
            IReadOnlyList<string> types,
            IReadOnlyList<string> expertSearchFields)
        {
            unchecked
            {
                int hash = 17;
                hash = AddNativeSearchHeaderOptionsHash(hash, types);
                hash = AddNativeSearchHeaderOptionsHash(hash, expertSearchFields);
                return hash;
            }
        }

        private static int AddNativeSearchHeaderOptionsHash(int hash, IReadOnlyList<string> options)
        {
            unchecked
            {
                if (options == null) return hash * 31;

                hash = hash * 31 + options.Count + 1;
                for (int i = 0; i < options.Count; i++)
                {
                    hash = hash * 31 + (options[i] == null ? 0 : StringComparer.Ordinal.GetHashCode(options[i]));
                }
                return hash;
            }
        }

        private MultiColumnTreeView CreateNativeSearchTreeView()
        {
            CommonMultiColumnState columnState = EnsureSearchColumnState();
            _nativeSearchTreeAdapter = new NativeAssetTreeViewAdapter(
                columnState,
                "AI4.Search.ResultTree",
                true,
                (int)SearchTreeViewControl.Columns.FileName,
                () => AI.Config.searchListRowHeight,
                SearchTreeViewControl.CreateNativeRetainedCell,
                (element, info, sourceColumnIndex) => SearchTreeViewControl.BindNativeRetainedCell(element, info, sourceColumnIndex, this),
                SyncNativeSearchColumnState,
                OnNativeSearchSortChanged,
                PopulateSearchTreeContextMenu,
                SearchTreeViewControl.UnbindNativeRetainedCell,
                _searchColumnDisplayOrder);
            AssetInventoryColumnLayoutCoordinator.Register(
                AssetInventoryTableLayoutKind.Search,
                _nativeSearchTreeAdapter,
                searchColumnState,
                AssetInventoryColumnLayoutCoordinator.GetSearchColumnKey);
            _nativeSearchTreeAdapter.SelectionChanged += OnNativeSearchTreeSelectionChanged;
            _nativeSearchTreeAdapter.ItemChosen += OnSearchDoubleClick;
            SyncNativeSearchSortIndicator();
            _nativeSearchTreeAdapter.View.RegisterCallback<KeyDownEvent>(OnNativeSearchResultKeyDown, TrickleDown.TrickleDown);
            _nativeSearchTreeAdapter.SetRoot(_searchTreeModel?.Root, _nativeSearchSelection);
            UpdateNativeSearchTreeVisibility();
            return _nativeSearchTreeAdapter.View;
        }

        private CommonSelectableGridView<AssetInfo> CreateNativeSearchGridView()
        {
            CommonSelectableGridView<AssetInfo> grid = new CommonSelectableGridView<AssetInfo>(
                CreateNativeSearchGridTile,
                BindNativeSearchGridTile,
                AssetInventoryUITK.ResultGridClass)
            {
                AllowMultipleSelection = !searchMode
            };
            grid.SelectionChanged += OnNativeSearchGridSelectionChanged;
            grid.ItemActivated += OnNativeSearchGridItemActivated;
            grid.ContextRequested += OnNativeSearchGridContextRequested;
            grid.ItemPointerDown += OnNativeSearchGridPointerDown;
            grid.ItemPointerMove += OnNativeSearchGridPointerMove;
            grid.ItemPointerUp += OnNativeSearchGridPointerUp;
            grid.LayoutChanged += OnNativeSearchGridLayoutChanged;
            grid.ScrollOffsetChanged += OnNativeSearchGridScrollChanged;
            grid.RegisterCallback<KeyDownEvent>(OnNativeSearchResultKeyDown, TrickleDown.TrickleDown);
            grid.SetDisplayMode(GetNativeSearchGridDisplayMode());
            grid.SetLayout(
                AI.Config.searchTileSize,
                AI.Config.searchTileAspectRatio,
                AI.Config.tileMargin,
                AI.Config.enlargeTiles);
            return grid;
        }

        private CommonGridViewDisplayMode GetNativeSearchGridDisplayMode()
        {
            return CommonGridSizeControl.GetDefaultDisplayMode(AI.Config.searchTileSize);
        }

        private void SetNativeSearchGridSize(int size)
        {
            AI.Config.searchTileSize = size;
            _lastTileSizeChange = DateTime.Now;
            AI.SaveConfig();
            _nativeSearchGridSizeControl?.SetValueWithoutNotify(size);
            RefreshNativeSearchTileDetailPopup();
            _nativeSearchGridView?.SetDisplayMode(GetNativeSearchGridDisplayMode());
            _nativeSearchGridView?.SetLayout(
                AI.Config.searchTileSize,
                AI.Config.searchTileAspectRatio,
                AI.Config.tileMargin,
                AI.Config.enlargeTiles);
            TriggerVisibleAnimationsUpdate();
        }

        private void SetNativeSearchGridDetail(int optionIndex)
        {
            int clampedIndex = Mathf.Clamp(optionIndex, 0, SearchGridDetailPresetSizes.Length - 1);
            SetNativeSearchGridSize(SearchGridDetailPresetSizes[clampedIndex]);
        }

        private void RefreshNativeSearchTileDetailPopup()
        {
            if (_nativeSearchTileDetailPopup == null) return;

            int optionIndex = (int)GetNativeSearchGridDisplayMode();
            _nativeSearchTileDetailPopup.SetValueWithoutNotify(SearchGridDetailOptions[optionIndex]);
        }

        private VisualElement CreateNativeSearchGridTile()
        {
            return AssetInventoryUITK.CreateResultGridTile(
                AssetInventoryUITK.CreateResultGridBadge("In Project", "project-badge", SearchGridProjectBadgeClass),
                AssetInventoryUITK.CreateResultGridBadge("Hidden", "hidden-badge", SearchGridHiddenBadgeClass));
        }

        private void BindNativeSearchGridTile(VisualElement tile, AssetInfo info, int index)
        {
            Texture content = SGrid.GetPreview(index);
            Image preview = tile.Q<Image>("preview");
            preview.image = content;

            CommonGridViewDisplayMode displayMode = GetNativeSearchGridDisplayMode();
            Label label = tile.Q<Label>("label");
            bool showText = displayMode != CommonGridViewDisplayMode.Tiny;
            bool primaryIsPath = false;
            string primaryText = showText ? GetSearchTileText(info, out primaryIsPath) : string.Empty;
            AssetInventoryUITK.SetResultGridText(label, primaryText, primaryIsPath);
            label.style.display = showText && !string.IsNullOrWhiteSpace(label.text) ? DisplayStyle.Flex : DisplayStyle.None;

            Label subtitle = tile.Q<Label>("subtitle");
            bool showSubtitle = displayMode == CommonGridViewDisplayMode.Detailed;
            bool subtitleIsPath = false;
            string subtitleText = showSubtitle ? GetNativeSearchGridSubtitle(info, primaryText, out subtitleIsPath) : string.Empty;
            AssetInventoryUITK.SetResultGridText(subtitle, subtitleText, subtitleIsPath);
            subtitle.style.display = showSubtitle && !string.IsNullOrWhiteSpace(subtitle.text)
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            bool showStatusBadges = displayMode != CommonGridViewDisplayMode.Tiny;
            Label projectBadge = tile.Q<Label>("project-badge");
            projectBadge.style.display = showStatusBadges && info?.InProject == true
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            Label hiddenBadge = tile.Q<Label>("hidden-badge");
            hiddenBadge.style.display = info?.Hidden == true ? DisplayStyle.Flex : DisplayStyle.None;
            tile.tooltip = info?.GetPath(true) ?? string.Empty;
        }

        private string GetNativeSearchGridSubtitle(AssetInfo info, string primaryText, out bool isPath)
        {
            isPath = false;
            if (info == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(info.AICaption)
                && !string.Equals(info.AICaption, primaryText, StringComparison.OrdinalIgnoreCase))
            {
                return info.AICaption;
            }

            string normalizedShortPath = NormalizeSearchTileText(info.ShortPath);
            if (!string.IsNullOrWhiteSpace(normalizedShortPath)
                && !string.Equals(normalizedShortPath, primaryText, StringComparison.OrdinalIgnoreCase))
            {
                isPath = true;
                return normalizedShortPath;
            }

            string type = string.IsNullOrWhiteSpace(info.Type) ? "FILE" : info.Type.ToUpperInvariant();
            return $"{type} | {EditorUtility.FormatBytes(info.Size)}";
        }

        private void RefreshNativeSearchGridView()
        {
            if (_nativeSearchGridView == null) return;

            IList<AssetInfo> items = _filteredFiles ?? (IList<AssetInfo>)Array.Empty<AssetInfo>();
            if (!ReferenceEquals(_nativeSearchGridView.ItemsSource, items))
            {
                HashSet<string> selectedKeys = new HashSet<string>(StringComparer.Ordinal);
                if (_selectedEntry != null || SGrid.selectionCount > 1)
                {
                    if (SGrid.selectionItems != null)
                    {
                        for (int i = 0; i < SGrid.selectionItems.Count; i++)
                        {
                            AssetInfo selected = SGrid.selectionItems[i];
                            string selectedKey = GetNativeSearchGridItemKey(selected);
                            if (!string.IsNullOrEmpty(selectedKey)) selectedKeys.Add(selectedKey);
                        }
                    }
                }

                string activeKey = GetNativeSearchGridItemKey(_selectedEntry);
                List<int> selectedIndices = new List<int>();
                int activeIndex = -1;
                for (int i = 0; i < items.Count; i++)
                {
                    AssetInfo item = items[i];
                    if (item == null) continue;
                    string itemKey = GetNativeSearchGridItemKey(item);
                    if (selectedKeys.Contains(itemKey)) selectedIndices.Add(i);
                    if (!string.IsNullOrEmpty(activeKey) && string.Equals(itemKey, activeKey, StringComparison.Ordinal)) activeIndex = i;
                }

                if (activeIndex < 0 && selectedIndices.Count > 0) activeIndex = selectedIndices[0];

                _nativeSearchGridView.SetItems(items);
                _nativeSearchGridView.SetSelection(selectedIndices, activeIndex);
                if (activeIndex >= 0 && activeIndex < items.Count)
                {
                    AssetInfo restoredSelection = items[activeIndex];
                    if (!string.Equals(GetNativeSearchGridItemKey(_selectedEntry), GetNativeSearchGridItemKey(restoredSelection), StringComparison.Ordinal))
                    {
                        _selectedEntry = restoredSelection;
                        ScheduleNativeSearchInspectorRebuild();
                        ScheduleNativeSearchSelectionHandling(false);
                    }
                }
                else if (!_lockSelection)
                {
                    _selectedEntry = null;
                    ScheduleNativeSearchInspectorRebuild();
                    ScheduleNativeSearchSelectionHandling(false);
                }
            }
            else
            {
                _nativeSearchGridView.RefreshItems();
            }

            _nativeSearchGridView.SetLayout(
                AI.Config.searchTileSize,
                AI.Config.searchTileAspectRatio,
                AI.Config.tileMargin,
                AI.Config.enlargeTiles);
            _nativeSearchGridView.SetDisplayMode(GetNativeSearchGridDisplayMode());
            _nativeSearchGridView.style.display = items.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _searchScrollPos = _nativeSearchGridView.ScrollView.scrollOffset;
            float viewportHeight = _nativeSearchGridView.ViewportHeight;
            if (!float.IsNaN(viewportHeight) && viewportHeight > 0f)
            {
                _searchGridViewHeight = viewportHeight;
            }

            _nativeSearchGridView.GetSelectedIndices(_nativeSearchGridSelectionBuffer);
            SyncNativeSearchGridSelectionToBackend(_nativeSearchGridSelectionBuffer, _nativeSearchGridView.ActiveIndex);
        }

        private static string GetNativeSearchGridItemKey(AssetInfo info)
        {
            if (info == null) return string.Empty;
            if (info.Id > 0) return "file:" + info.Id;
            if (!string.IsNullOrEmpty(info.ProjectPath)) return "project:" + info.ProjectPath;
            return $"virtual:{info.AssetId}:{info.Guid}:{info.Path}";
        }

        private void OnNativeSearchGridSelectionChanged(IReadOnlyList<int> indices, int activeIndex)
        {
            SyncNativeSearchGridSelectionToBackend(indices, activeIndex);

            if (_filteredFiles != null && activeIndex >= 0 && activeIndex < _filteredFiles.Count)
            {
                OnSearchKeyboardSelection(activeIndex);
                ScheduleNativeSearchSelectionHandling(true);
            }
            else if (!_lockSelection)
            {
                _selectedEntry = null;
                _requireSearchSelectionUpdate = true;
                ScheduleNativeSearchInspectorRebuild();
                ScheduleNativeSearchSelectionHandling(false);
            }

            RefreshNativeSearchNarrowDetailsAction();
        }

        private void SyncNativeSearchGridSelectionToBackend(IReadOnlyList<int> indices, int activeIndex)
        {
            SGrid.SetVisualSelectionIndices(indices, activeIndex);
            _nativeSearchGridSelectedItemsBuffer.Clear();
            if (_filteredFiles != null && indices != null)
            {
                for (int i = 0; i < indices.Count; i++)
                {
                    int index = indices[i];
                    if (index >= 0 && index < _filteredFiles.Count) _nativeSearchGridSelectedItemsBuffer.Add(_filteredFiles[index]);
                }
            }
            SGrid.SetBulkSelection(_nativeSearchGridSelectedItemsBuffer);
        }

        private void OnNativeSearchGridItemActivated(AssetInfo info, int index, bool alt)
        {
            if (info == null) return;
            SGrid.LastClickAlt = alt;
            _selectedEntry = info;
            OnSearchDoubleClick(info);
        }

        private void OnNativeSearchGridContextRequested(AssetInfo info, int index)
        {
            GenericMenu menu = new GenericMenu();
            PopulateSearchGridContextMenu(menu, SGrid.selectionItems, index);
            menu.ShowAsContext();
        }

        private void OnNativeSearchGridPointerDown(AssetInfo info, int index, PointerDownEvent evt)
        {
            if (evt.button != 0 || AI.Config.disableDragDrop || AI.Config.tab != 0) return;
            _draggingPossible = true;
            _dragStartPosition = evt.position;
            _dragStartTime = Time.realtimeSinceStartup;
        }

        private void OnNativeSearchGridPointerMove(AssetInfo info, int index, PointerMoveEvent evt)
        {
            if ((evt.pressedButtons & 1) == 0 || !_draggingPossible || _dragging || _selectedEntry == null) return;
            float dragDistance = Vector2.Distance(evt.position, _dragStartPosition);
            float timeSinceStart = Time.realtimeSinceStartup - _dragStartTime;
            if (dragDistance < DRAG_THRESHOLD && timeSinceStart < DRAG_DELAY) return;

            _dragging = true;
            InitDragAndDrop();
            DragAndDrop.PrepareStartDrag();
            List<AssetInfo> draggedItems = SGrid.selectionCount > 0
                ? new List<AssetInfo>(SGrid.selectionItems)
                : new List<AssetInfo> {_selectedEntry};
            DragAndDrop.SetGenericData("AssetInfo", draggedItems);
            DragAndDrop.objectReferences = draggedItems
                .Where(item => !string.IsNullOrWhiteSpace(item.ProjectPath))
                .Select(item => AssetDatabase.LoadMainAssetAtPath(item.ProjectPath))
                .Where(item => item != null)
                .ToArray();
            DragAndDrop.StartDrag("Dragging " + _selectedEntry);
            evt.StopPropagation();
        }

        private void OnNativeSearchGridPointerUp(AssetInfo info, int index, PointerUpEvent evt)
        {
            _draggingPossible = false;
            if (_dragging) StopDragDrop();
        }

        private void OnNativeSearchGridLayoutChanged(int columns, float tileWidth, float tileHeight)
        {
            SGrid.SetLayoutMetrics(columns, tileHeight);
            float viewportHeight = _nativeSearchGridView?.ViewportHeight ?? 0f;
            if (!float.IsNaN(viewportHeight) && viewportHeight > 0f)
            {
                _searchGridViewHeight = viewportHeight;
            }
            TriggerVisibleAnimationsUpdate();
        }

        private void OnNativeSearchGridScrollChanged(Vector2 offset)
        {
            _searchScrollPos = offset;
            TriggerVisibleAnimationsUpdate();
        }

        private void OnNativeSearchTreeSelectionChanged(IList<int> ids)
        {
            _nativeSearchSelection.Clear();
            if (ids != null) _nativeSearchSelection.AddRange(ids);
            OnSearchTreeSelectionChanged(ids);
            ScheduleNativeSearchSelectionHandling(ids != null && ids.Count > 0);
        }

        private void OnNativeSearchResultKeyDown(KeyDownEvent evt)
        {
            if ((evt.modifiers & EventModifiers.Alt) == 0 || AI.Config.tab != 0) return;

            bool handled = false;
            if (evt.keyCode == KeyCode.LeftArrow && _pageCount > 1 && _curPage > 1)
            {
                SetPage(_curPage - 1);
                handled = true;
            }
            else if (evt.keyCode == KeyCode.RightArrow && _pageCount > 1 && _curPage < _pageCount)
            {
                SetPage(_curPage + 1);
                handled = true;
            }
            else
            {
                handled = HandleTagShortcut(evt.keyCode, evt.modifiers);
            }

            if (!handled) return;

            ScheduleNativeSearchInspectorRebuild();
            CommonUITK.ConsumeEvent(evt, true);
        }

        private void ScheduleNativeSearchSelectionHandling(bool manuallyChanged)
        {
            _searchSelectionChangedManually = manuallyChanged;
            _requireSearchSelectionUpdate = true;
            if (_selectionHandlerAdded) return;

            _selectionHandlerAdded = true;
            EditorApplication.delayCall += HandleSearchSelectionChanged;
        }

        private void SyncNativeSearchColumnState()
        {
            if (_syncingNativeSearchColumns || _nativeSearchTreeAdapter == null || searchColumnState == null) return;

            _syncingNativeSearchColumns = true;
            try
            {
                AssetInventoryColumnLayoutCoordinator.UpdateColumns(
                    AssetInventoryTableLayoutKind.Search,
                    _nativeSearchTreeAdapter,
                    searchColumnState,
                    AssetInventoryColumnLayoutCoordinator.GetSearchColumnKey);
            }
            finally
            {
                _syncingNativeSearchColumns = false;
            }
        }

        private void OnNativeSearchSortChanged(int sourceColumnIndex, bool descending)
        {
            int sortField = SearchTreeViewControl.GetSortField(sourceColumnIndex);
            if (sortField < 0 || AI.Config.sortField == sortField && AI.Config.sortDescending == descending) return;

            AI.Config.sortField = sortField;
            AI.Config.sortDescending = descending;
            AssetInventoryColumnLayoutCoordinator.UpdateSort(
                AssetInventoryTableLayoutKind.Search,
                _nativeSearchTreeAdapter,
                searchColumnState,
                AssetInventoryColumnLayoutCoordinator.GetSearchColumnKey,
                sourceColumnIndex,
                descending);
            CommitNativeSearchSetting(true, true);
        }

        private void SyncNativeSearchSortIndicator()
        {
            if (_nativeSearchTreeAdapter == null) return;

            int sourceColumnIndex = SearchTreeViewControl.GetSourceColumnIndex(AI.Config.sortField);
            _nativeSearchTreeAdapter.SyncSort(sourceColumnIndex, AI.Config.sortDescending);
        }

        private void QueueNativeSearchTreeRefresh(IList<int> selection, bool revealSelection)
        {
            if (_nativeSearchTreeAdapter == null || _nativeSearchTreeView == null) return;

            _pendingNativeSearchSelection = selection?.Distinct().ToList() ?? new List<int>();
            _pendingNativeSearchRevealSelection = revealSelection;
            _nativeSearchTreeRefreshPending = true;
        }

        private void FlushNativeSearchTreeRefresh()
        {
            if (!_nativeSearchTreeRefreshPending) return;

            _nativeSearchTreeRefreshPending = false;
            if (_nativeSearchTreeAdapter == null || _nativeSearchTreeView == null) return;

            _nativeSearchTreeAdapter.SetRoot(
                _searchTreeModel?.Root,
                _pendingNativeSearchSelection,
                _pendingNativeSearchRevealSelection);
            List<int> restoredSelection = _nativeSearchTreeAdapter.GetSelectedModelIds().ToList();
            _nativeSearchSelection.Clear();
            _nativeSearchSelection.AddRange(restoredSelection);
            OnSearchTreeSelectionChanged(restoredSelection);
            ScheduleNativeSearchSelectionHandling(false);
            UpdateNativeSearchTreeVisibility();
            RefreshNativeSearchResultControls();
            _nativeSearchTreeAdapter.RepaintCells();
        }

        private void UpdateNativeSearchTreeVisibility()
        {
            if (_nativeSearchTreeView == null) return;

            bool hasResults = _filteredFiles != null && _filteredFiles.Count > 0;
            _nativeSearchTreeView.style.display = hasResults ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private VisualElement CreateNativeSearchMainRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(SearchRowClass);
            row.AddToClassList(AssetInventoryUITK.CompactSearchToolbarClass);

            VisualElement searchBlock = AssetInventoryUITK.CreateAdvancedVisibilityBlock("search.actions.search", () =>
            {
                VisualElement group = new VisualElement();
                group.AddToClassList(SearchActionGroupClass);

                Label label = new Label("Search");
                label.AddToClassList(SearchLabelClass);
                group.Add(label);

                _nativeSearchField = new ToolbarSearchField
                {
                    value = _searchPhrase ?? string.Empty,
                    tooltip = "Search indexed assets and project files."
                };
                _nativeSearchField.AddToClassList(SearchFieldClass);
                _nativeSearchField.RegisterValueChangedCallback(evt => SetNativeSearchPhrase(evt.newValue));
                _nativeSearchField.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;

                    PerformSearch();
                    evt.StopPropagation();
                });
                group.Add(_nativeSearchField);

                Button go = AssetInventoryUITK.CreatePrimaryButton("Go", () => PerformSearch());
                go.tooltip = "Run the current search.";
                go.AddToClassList(SearchGoClass);
                group.Add(go);

                return group;
            }, alwaysShow: true, onVisibilityChanged: RebuildNativeSearchBody);
            searchBlock.AddToClassList(SearchActionWrapperClass);
            row.Add(searchBlock);

            _nativeSearchFilterChip = CreateNativeSearchFilterChip();
            row.Add(_nativeSearchFilterChip);

            row.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("asset.actions.savedsearches", () =>
            {
                Button save = null;
                save = AssetInventoryUITK.CreateIconButton(
                    "Save current search",
                    "d_saveas",
                    () =>
                    {
                        NameWindow.ShowAsDropDown(
                            CommonUITK.ToScreenDropdownAnchor(this, save),
                            string.IsNullOrEmpty(_searchPhrase) ? "My Search" : _searchPhrase,
                            SaveSearch);
                    });
                save.AddToClassList(SearchSaveClass);
                return save;
            }, onVisibilityChanged: RebuildNativeSearchBody));

            row.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("search.actions.assistant", () =>
            {
                Button examples = null;
                examples = AssetInventoryUITK.CreateIconButton("Open search examples and query help", "_Help", () =>
                {
                    AdvancedSearchUI.ShowDropdown(this, examples, (searchPhrase, searchType) =>
                    {
                        _searchPhrase = searchPhrase ?? string.Empty;
                        _previousSearchPhrase = _searchPhrase;
                        if (searchType == null)
                        {
                            AI.Config.searchType = 0;
                        }
                        else
                        {
                            int typeIdx = Array.IndexOf(_types, searchType);
                            if (typeIdx >= 0) AI.Config.searchType = typeIdx;
                        }
                        _activeSavedSearchId = -1;
                        _requireSearchUpdate = true;
                        RefreshNativeSearchHeaderState();
                    });
                });
                examples.AddToClassList(SearchAuxButtonClass);
                return examples;
            }, onVisibilityChanged: RebuildNativeSearchBody));

            _nativeSearchExpertPopup = CreateNativeExpertSearchPopup();
            row.Add(_nativeSearchExpertPopup);

            _nativeSearchTypePopup = CreateNativeSearchTypePopup();
            if (_nativeSearchTypePopup != null)
            {
                row.Add(_nativeSearchTypePopup);
            }

            _nativeSearchInMemoryButton = CreateNativeInMemoryButton();
            if (_nativeSearchInMemoryButton != null)
            {
                row.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("asset.actions.inmemorymode", () => _nativeSearchInMemoryButton, onVisibilityChanged: RebuildNativeSearchBody));
            }

            return row;
        }

        private VisualElement CreateNativeSearchFilterChip()
        {
            VisualElement chip = new VisualElement();
            chip.AddToClassList(PackagesFilterChipClass);

            _nativeSearchFilterChipLabel = AssetInventoryUITK.CreateSecondaryButton(string.Empty, OpenNativeSearchFilters);
            _nativeSearchFilterChipLabel.tooltip = "Open the active search filters.";
            _nativeSearchFilterChipLabel.AddToClassList(PackagesFilterChipLabelClass);
            chip.Add(_nativeSearchFilterChipLabel);

            _nativeSearchFilterChipReset = AssetInventoryUITK.CreateSecondaryButton("×", ResetNativeSearchFilters);
            _nativeSearchFilterChipReset.tooltip = "Reset all search filters";
            _nativeSearchFilterChipReset.AddToClassList(PackagesFilterChipResetClass);
            chip.Add(_nativeSearchFilterChipReset);

            RefreshNativeSearchFilterChip();
            return chip;
        }

        private void RefreshNativeSearchFilterChip()
        {
            if (_nativeSearchFilterChip == null) return;

            int count = GetActiveSearchFilterCount();
            _nativeSearchFilterChip.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_nativeSearchFilterChipLabel != null)
            {
                _nativeSearchFilterChipLabel.text = count == 1 ? "1 filter active" : $"{count:N0} filters active";
            }
        }

        private void OpenNativeSearchFilters()
        {
            if (IsNativeSearchFilterSidebarMode() &&
                _nativeSearchSidebarFiltersScroll != null &&
                _nativeSearchPaneLayout != null)
            {
                _nativeSearchPaneLayout.SetPaneOpen(CommonSidePane.Leading, true, true);
                RefreshNativeSearchSidebarFilters(true);
                return;
            }

            _searchInspectorTab = 1;
            _nativeSearchInspectorContentStateHash = int.MinValue;
            if (UseNativeNarrowDetailsLayout() && _nativeSearchNarrowDetails != null)
            {
                _nativeSearchNarrowDetailsOpen = true;
                ApplyNativeSearchNarrowDetailsState();
            }
            else
            {
                _nativeSearchPaneLayout?.SetPaneOpen(CommonSidePane.Trailing, true, true);
            }
            RefreshNativeSearchInspector();
        }

        private VisualElement CreateNativeSearchInMemoryRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(SearchRowClass);
            row.AddToClassList(AssetInventoryUITK.CompactSearchToolbarClass);

            VisualElement searchBlock = AssetInventoryUITK.CreateAdvancedVisibilityBlock("search.actions.search", () =>
            {
                VisualElement group = new VisualElement();
                group.AddToClassList(SearchActionGroupClass);

                Label label = new Label("Refine");
                label.AddToClassList(SearchLabelClass);
                group.Add(label);

                _nativeSearchInMemoryField = new ToolbarSearchField
                {
                    value = _searchPhraseInMemory ?? string.Empty,
                    tooltip = "Refine the current in-memory result set."
                };
                _nativeSearchInMemoryField.AddToClassList(SearchFieldClass);
                _nativeSearchInMemoryField.RegisterValueChangedCallback(evt =>
                {
                    _searchPhraseInMemory = evt.newValue ?? string.Empty;
                    _nextSearchTime = Time.realtimeSinceStartup + AI.Config.inMemorySearchDelay;
                });
                _nativeSearchInMemoryField.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;

                    UpdateFilteredFiles();
                    evt.StopPropagation();
                });
                group.Add(_nativeSearchInMemoryField);

                Button go = AssetInventoryUITK.CreatePrimaryButton("Go", UpdateFilteredFiles);
                go.tooltip = "Refine the results already loaded in memory.";
                go.AddToClassList(SearchGoClass);
                group.Add(go);

                return group;
            }, alwaysShow: true, onVisibilityChanged: RebuildNativeSearchBody);
            searchBlock.AddToClassList(SearchActionWrapperClass);
            row.Add(searchBlock);

            _nativeSearchInMemoryButton = CreateNativeInMemoryButton();
            if (_nativeSearchInMemoryButton != null)
            {
                row.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("asset.actions.inmemorymode", () => _nativeSearchInMemoryButton, onVisibilityChanged: RebuildNativeSearchBody));
            }

            return row;
        }

        private PopupField<string> CreateNativeSearchTypePopup()
        {
            if (_fixedSearchTypeIdx >= 0 || _types == null || _types.Length == 0) return null;

            int selectedIndex = Mathf.Clamp(AI.Config.searchType, 0, _types.Length - 1);
            PopupField<string> popup = new PopupField<string>(_types.ToList(), selectedIndex)
            {
                tooltip = "Limit search to a file type."
            };
            popup.AddToClassList(SearchTypePopupClass);
            popup.RegisterValueChangedCallback(evt =>
            {
                int newIndex = Array.IndexOf(_types, evt.newValue);
                if (newIndex < 0 || newIndex == AI.Config.searchType) return;

                AI.Config.searchType = newIndex;
                AI.SaveConfig();
                _activeSavedSearchId = -1;
                _nativeSearchSavedSearchesDirty = true;
                _requireSearchUpdate = true;
                _keepSearchResultPage = false;
                RefreshNativeSearchHeaderState();
            });
            return popup;
        }

        private PopupField<string> CreateNativeExpertSearchPopup()
        {
            List<string> fields = _expertSearchFields == null
                ? new List<string> {"-Add Field-"}
                : _expertSearchFields.ToList();
            PopupField<string> popup = new PopupField<string>(fields, Mathf.Clamp(_selectedExpertSearchField, 0, fields.Count - 1))
            {
                tooltip = "Insert an expert search field."
            };
            popup.AddToClassList(SearchExpertPopupClass);
            popup.RegisterValueChangedCallback(evt =>
            {
                int index = fields.IndexOf(evt.newValue);
                if (index < 0) return;

                _selectedExpertSearchField = index;
                string field = fields[index];
                if (!string.IsNullOrEmpty(field) && !field.StartsWith("-", StringComparison.Ordinal))
                {
                    SetNativeSearchPhrase((_searchPhrase ?? string.Empty) + field.Replace('/', '.'));
                    _nativeSearchField?.Focus();
                }
                _selectedExpertSearchField = 0;
                popup.SetValueWithoutNotify(fields[0]);
            });
            popup.style.display = IsNativeExpertSearchActive() ? DisplayStyle.Flex : DisplayStyle.None;
            return popup;
        }

        private Button CreateNativeInMemoryButton()
        {
            if (SearchScopeModel.IsProjectOnly(GetConfiguredSearchScope())) return null;

            Button button = AssetInventoryUITK.CreateIconButton(
                "High-Speed Mode: Load all current results into memory for extremely fast sub-searches.",
                "d_lighting",
                () => SetNativeInMemoryMode(_inMemoryMode == InMemoryModeState.None));
            button.AddToClassList(SearchAuxButtonClass);
            AssetInventoryUITK.SetSavedSearchActive(button, _inMemoryMode != InMemoryModeState.None);
            button.SetEnabled(_inMemoryMode != InMemoryModeState.None || _resultCount > 0);
            return button;
        }

        private void RefreshNativeSearchHeaderState()
        {
            if (_nativeSearchBody == null || _nativeSearchBody.childCount == 0) return;

            RefreshNativeSearchSavedSearches();

            string phrase = _searchPhrase ?? string.Empty;
            if (_nativeSearchField != null && _nativeSearchField.value != phrase)
            {
                _nativeSearchField.SetValueWithoutNotify(phrase);
            }

            string inMemoryPhrase = _searchPhraseInMemory ?? string.Empty;
            if (_nativeSearchInMemoryField != null && _nativeSearchInMemoryField.value != inMemoryPhrase)
            {
                _nativeSearchInMemoryField.SetValueWithoutNotify(inMemoryPhrase);
            }

            if (_nativeSearchTypePopup != null && _types != null && _types.Length > 0)
            {
                int selectedIndex = Mathf.Clamp(AI.Config.searchType, 0, _types.Length - 1);
                if (_nativeSearchTypePopup.index != selectedIndex)
                {
                    _nativeSearchTypePopup.SetValueWithoutNotify(_types[selectedIndex]);
                }
            }

            if (_nativeSearchExpertPopup != null)
            {
                _nativeSearchExpertPopup.style.display = IsNativeExpertSearchActive() ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_nativeSearchInMemoryButton != null)
            {
                AssetInventoryUITK.SetSavedSearchActive(_nativeSearchInMemoryButton, _inMemoryMode != InMemoryModeState.None);
                _nativeSearchInMemoryButton.SetEnabled(_inMemoryMode != InMemoryModeState.None || _resultCount > 0);
            }

            RefreshNativeSearchFilterChip();
            RefreshNativeSearchVariables();
            RefreshNativeSearchError();
            RefreshNativeSearchFooterState();
        }

        private void RefreshNativeSearchSavedSearches()
        {
            if (_nativeSearchSavedSearches == null) return;

            List<SavedSearch> searches = GetNativeSavedSearchesToDisplay();
            bool showAdvanced = ShowAdvanced();
            int expectedChildren = searches.Count + (ShowWorkspaces() ? 1 : 0);
            if (_nativeSearchSavedSearchesDirty ||
                _nativeSearchSavedSearches.childCount != expectedChildren ||
                _nativeSearchSavedSearchesShowAdvanced != showAdvanced)
            {
                RebuildNativeSearchSavedSearches(searches);
                return;
            }

            foreach (VisualElement child in _nativeSearchSavedSearches.Children())
            {
                Button button = AssetInventoryUITK.FindSavedSearchPill(child);
                if (button?.userData is SavedSearch search)
                {
                    AssetInventoryUITK.SetSavedSearchActive(button, search.Id == _activeSavedSearchId);
                }
            }
        }

        private void RebuildNativeSearchSavedSearches(List<SavedSearch> searches)
        {
            if (_nativeSearchSavedSearches == null) return;

            _nativeSearchSavedSearches.Clear();
            _nativeSearchSavedSearches.style.display = searches.Count == 0 && !ShowWorkspaces() ? DisplayStyle.None : DisplayStyle.Flex;
            _nativeSearchSavedSearchesShowAdvanced = ShowAdvanced();

            foreach (SavedSearch search in searches)
            {
                _nativeSearchSavedSearches.Add(CreateNativeSavedSearchPillGroup(search, _nativeSearchSavedSearchesShowAdvanced));
            }

            if (ShowWorkspaces())
            {
                Button workspace = null;
                string label = _selectedWorkspace == null ? "Workspace" : _selectedWorkspace.Name;
                workspace = AssetInventoryUITK.CreateSecondaryButton(label, () => ShowNativeWorkspaceMenu(workspace));
                workspace.tooltip = "Select or manage search workspaces.";
                workspace.AddToClassList(SearchWorkspaceButtonClass);
                _nativeSearchSavedSearches.Add(workspace);
            }

            _nativeSearchSavedSearchesDirty = false;
        }

        private VisualElement CreateNativeSavedSearchPillGroup(SavedSearch search, bool hasMenu)
        {
            return AssetInventoryUITK.CreateSavedSearchPillGroup(
                GetNativeSavedSearchLabel(search),
                search.SearchPhrase ?? string.Empty,
                search.Icon,
                search.Color,
                search.Id == _activeSavedSearchId,
                hasMenu,
                () => SelectNativeSavedSearch(search),
                anchor => ShowNativeSavedSearchMenu(search, anchor),
                search);
        }

        private List<SavedSearch> GetNativeSavedSearchesToDisplay()
        {
            if (ShowWorkspaces() && _selectedWorkspace != null && _selectedWorkspace.Searches != null)
            {
                return _selectedWorkspace.Searches
                    .OrderBy(ws => ws.OrderIdx)
                    .Select(ws => Searches.FirstOrDefault(s => s.Id == ws.SavedSearchId))
                    .Where(s => s != null)
                    .ToList();
            }

            return Searches.ToList();
        }

        private static string GetNativeSavedSearchLabel(SavedSearch search)
        {
            if (!string.IsNullOrWhiteSpace(search.Name)) return search.Name;
            if (!string.IsNullOrWhiteSpace(search.SearchPhrase)) return search.SearchPhrase;
            return "Search";
        }

        private void SelectNativeSavedSearch(SavedSearch search)
        {
            if (_activeSavedSearchId == search.Id)
            {
                ResetSearch(false, false);
                _requireSearchUpdate = true;
            }
            else
            {
                if (workspaceMode && AI.Config.wsSavedSearchInMemory)
                {
                    _inMemoryMode = InMemoryModeState.Init;
                }
                LoadSearch(search);
            }

            _nativeSearchSavedSearchesDirty = true;
            RefreshNativeSearchHeaderState();
        }

        private void ShowNativeSavedSearchMenu(SavedSearch search, VisualElement anchor)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Edit"), false, () =>
            {
                SavedSearchUI savedSearchUI = SavedSearchUI.ShowWindow();
                savedSearchUI.Init(search, OnNativeSavedSearchEdited);
            });
            menu.AddItem(new GUIContent("Override with Current Search"), false, () =>
            {
                OverrideSavedSearch(search);
                _nativeSearchSavedSearchesDirty = true;
                RefreshNativeSearchHeaderState();
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Delete"), false, () =>
            {
                if (!EditorUtility.DisplayDialog("Confirm", $"Do you really want to delete the saved search '{search.Name}'?", "Yes", "No")) return;

                DBAdapter.DB.Delete(search);
                Searches.Remove(search);
                DBAdapter.DB.Execute("delete from WorkspaceSearch where SavedSearchId = ?", search.Id);
                _selectedWorkspace?.LoadSearches();
                if (_activeSavedSearchId == search.Id) _activeSavedSearchId = -1;
                _nativeSearchSavedSearchesDirty = true;
                RefreshNativeSearchHeaderState();
            });
            CommonUITK.ShowGenericMenu(menu, anchor);
        }

        private void OnNativeSavedSearchEdited(SavedSearch search)
        {
            _nativeSearchSavedSearchesDirty = true;
            RefreshNativeSearchHeaderState();
        }

        private void ShowNativeWorkspaceMenu(VisualElement anchor)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("-No Workspace-"), _selectedWorkspace == null, () =>
            {
                SetWorkspace(null);
                OnNativeWorkspaceChanged();
            });
            if (Workspaces.Count > 0)
            {
                menu.AddSeparator("");
                foreach (Workspace ws in Workspaces)
                {
                    Workspace workspace = ws;
                    menu.AddItem(new GUIContent(workspace.Name), _selectedWorkspace != null && _selectedWorkspace.Id == workspace.Id, () =>
                    {
                        SetWorkspace(workspace);
                        OnNativeWorkspaceChanged();
                    });
                }
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("New"), false, () =>
            {
                NameWindow.ShowAsDropDown(CommonUITK.ToScreenDropdownAnchor(this, anchor), "My Workspace", value =>
                {
                    SaveWorkspace(value);
                    OnNativeWorkspaceChanged();
                });
            });
            if (_selectedWorkspace != null)
            {
                menu.AddItem(new GUIContent("Edit"), false, () =>
                {
                    WorkspaceUI workspaceUI = WorkspaceUI.ShowWindow();
                    workspaceUI.Init(_selectedWorkspace);
                });
                menu.AddItem(new GUIContent("Delete"), false, () =>
                {
                    if (!EditorUtility.DisplayDialog("Confirm", $"Do you really want to delete workspace '{_selectedWorkspace.Name}'?", "Yes", "No")) return;

                    Workspaces.Remove(_selectedWorkspace);
                    DBAdapter.DB.Execute("delete from WorkspaceSearch where WorkspaceId = ?", _selectedWorkspace.Id);
                    DBAdapter.DB.Delete(_selectedWorkspace);
                    SetWorkspace(null);
                    OnNativeWorkspaceChanged();
                });
            }
            CommonUITK.ShowGenericMenu(menu, anchor);
        }

        private void OnNativeWorkspaceChanged()
        {
            _nativeSearchSavedSearchesDirty = true;
            RefreshNativeSearchHeaderState();
            Repaint();
        }

        private void RefreshNativeSearchVariables()
        {
            if (_nativeSearchVariables == null) return;

            string signature = GetNativeSearchVariableSignature();
            if (_nativeSearchVariableSignature == signature && _nativeSearchVariables.childCount > 0)
            {
                RefreshNativeSearchVariableValues();
                return;
            }

            _nativeSearchVariables.Clear();
            _nativeSearchVariableSignature = signature;
            _nativeSearchVariables.style.display = _hasSearchVariables ? DisplayStyle.Flex : DisplayStyle.None;
            if (!_hasSearchVariables) return;

            foreach (KeyValuePair<string, SearchVariable> kvp in _searchVariables.OrderBy(v => v.Key))
            {
                SearchVariable variable = kvp.Value;
                VisualElement row = new VisualElement();
                row.AddToClassList(SearchVariableRowClass);
                Label label = new Label(kvp.Key + ":");
                label.AddToClassList(SearchVariableLabelClass);
                row.Add(label);

                TextField field = new TextField
                {
                    value = variable.currentValue ?? string.Empty,
                    userData = variable
                };
                field.AddToClassList(SearchVariableFieldClass);
                field.RegisterValueChangedCallback(evt =>
                {
                    variable.currentValue = evt.newValue ?? string.Empty;
                    _requireSearchUpdate = true;
                });
                row.Add(field);

                if (_activeSavedSearchId > 0)
                {
                    Button menuButton = null;
                    menuButton = AssetInventoryUITK.CreateIconButton(
                        "Variable options",
                        "icon dropdown",
                        () => ShowNativeVariableDropdown(variable, menuButton));
                    menuButton.AddToClassList(SearchVariableMenuClass);
                    row.Add(menuButton);
                }

                _nativeSearchVariables.Add(row);
            }
        }

        private void RefreshNativeSearchVariableValues()
        {
            foreach (TextField field in _nativeSearchVariables.Query<TextField>().ToList())
            {
                if (field.userData is SearchVariable variable)
                {
                    string value = variable.currentValue ?? string.Empty;
                    if (field.value != value)
                    {
                        field.SetValueWithoutNotify(value);
                    }
                }
            }
        }

        private string GetNativeSearchVariableSignature()
        {
            if (!_hasSearchVariables) return string.Empty;

            return string.Join("|", _searchVariables.OrderBy(v => v.Key).Select(v => v.Key)) + ":" + _activeSavedSearchId;
        }

        private void ShowNativeVariableDropdown(SearchVariable variable, VisualElement anchor)
        {
            GenericMenu menu = new GenericMenu();
            if (variable.options != null && variable.options.Count > 0)
            {
                menu.AddDisabledItem(new GUIContent("Predefined Options"));
                foreach (string option in variable.options)
                {
                    string capturedOption = option;
                    menu.AddItem(new GUIContent("  " + option), false, () =>
                    {
                        variable.currentValue = capturedOption;
                        _requireSearchUpdate = true;
                        RefreshNativeSearchVariables();
                    });
                }
                menu.AddSeparator("");
            }

            if (variable.currentValue != variable.defaultValue)
            {
                menu.AddItem(new GUIContent("Set Current as Default"), false, () =>
                {
                    variable.defaultValue = variable.currentValue;
                    PersistNativeVariableDefinitions();
                });
            }

            menu.AddItem(new GUIContent("Edit Options"), false, () =>
            {
                string currentOptions = variable.options != null && variable.options.Count > 0
                    ? string.Join(", ", variable.options)
                    : string.Empty;

                StringListWindow.ShowAsDropDown(
                    CommonUITK.ToScreenDropdownAnchor(this, anchor),
                    currentOptions,
                    ",",
                    optionsText =>
                    {
                        List<string> updatedOptions = new List<string>();
                        if (!string.IsNullOrWhiteSpace(optionsText))
                        {
                            updatedOptions = optionsText
                                .Split(',')
                                .Select(s => s.Trim())
                                .Where(s => !string.IsNullOrWhiteSpace(s))
                                .Distinct()
                                .ToList();
                        }

                        variable.options = updatedOptions;
                        PersistNativeVariableDefinitions();
                        _nativeSearchVariableSignature = null;
                        RefreshNativeSearchVariables();
                    },
                    "Options");
            });

            CommonUITK.ShowGenericMenu(menu, anchor);
        }

        private void PersistNativeVariableDefinitions()
        {
            if (_activeSavedSearchId <= 0) return;

            SavedSearch savedSearch = Searches.FirstOrDefault(s => s.Id == _activeSavedSearchId);
            if (savedSearch == null) return;

            savedSearch.VariableDefinitions = SerializeSearchVariables(_searchVariables);
            DBAdapter.DB.Update(savedSearch);
        }

        private void RefreshNativeSearchError()
        {
            if (_nativeSearchError == null) return;

            bool showError = !string.IsNullOrEmpty(_searchError);
            _nativeSearchError.text = showError ? $"Error: {_searchError}" : string.Empty;
            _nativeSearchError.style.display = showError ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private bool IsNativeExpertSearchActive()
        {
            return !string.IsNullOrEmpty(_searchPhrase) && _searchPhrase.StartsWith("=", StringComparison.Ordinal);
        }

        private void SetNativeSearchPhrase(string value)
        {
            string nextPhrase = value ?? string.Empty;
            if (nextPhrase == (_searchPhrase ?? string.Empty)) return;

            _searchPhrase = nextPhrase;
            if (_searchPhrase != _previousSearchPhrase)
            {
                _previousSearchPhrase = _searchPhrase;
                _nextSearchTime = Time.realtimeSinceStartup + AI.Config.searchDelay;
                _nextVariableDetectionTime = Time.realtimeSinceStartup + AI.Config.variableDetectionDelay;
                _activeSavedSearchId = -1;
                _nativeSearchSavedSearchesDirty = true;
            }

            RefreshNativeSearchHeaderState();
        }

        private void SetNativeInMemoryMode(bool enabled)
        {
            if (SearchScopeModel.IsProjectOnly(GetConfiguredSearchScope())) return;

            _inMemoryMode = enabled ? InMemoryModeState.Init : InMemoryModeState.None;
            _requireSearchUpdate = true;
            _searchPhraseInMemory = string.Empty;
            RefreshSearchField();
            RebuildNativeSearchBody();
            Repaint();
        }

        private void RunNativeSearchHeaderTimers(ref bool dirty)
        {
            if (_inMemoryMode == InMemoryModeState.None)
            {
                if (_nextSearchTime > 0 && Time.realtimeSinceStartup > _nextSearchTime)
                {
                    _nextSearchTime = 0;
                    if (AI.Config.searchAutomatically && !(_searchPhrase ?? string.Empty).StartsWith("=", StringComparison.Ordinal)) dirty = true;
                }

                if (!AI.Config.searchAutomatically && _nextVariableDetectionTime > 0 && Time.realtimeSinceStartup > _nextVariableDetectionTime)
                {
                    _nextVariableDetectionTime = 0;
                    DetectVariablesInSearchPhrase();
                    RefreshNativeSearchHeaderState();
                }
                return;
            }

            if (_nextSearchTime > 0 && Time.realtimeSinceStartup > _nextSearchTime)
            {
                _nextSearchTime = 0;
                UpdateFilteredFiles();
            }
        }

        private void RunNativeSearchLifecycle()
        {
            if (!IsNativeSearchShellActive()) return;

            if (!_lockSelection)
            {
                bool dirty = false;
                RestoreSearchVariableState(ref dirty);
                RunNativeSearchHeaderTimers(ref dirty);

                if (_sgrid == null || (SGrid.HasPreviewSlots && SGrid.PreviewCount > 0 && _files == null))
                {
                    PerformSearch();
                }

                if (!_searchPreviewSessionInitialized && _filteredFiles != null && _filteredFiles.Count > 0)
                {
                    UpdateSearchPreviews();
                }

                if (dirty)
                {
                    _requireSearchUpdate = true;
                    _keepSearchResultPage = false;
                }
            }

            RunSearchTabDelayedLogic(true);
        }

        private void RestoreSearchVariableState(ref bool dirty)
        {
            if (!_variablesRestoredFromDb && _activeSavedSearchId > 0 && _searchVariables.Count == 0 && !string.IsNullOrEmpty(_searchPhrase))
            {
                SavedSearch search = Searches.FirstOrDefault(value => value.Id == _activeSavedSearchId);
                if (search != null && !string.IsNullOrEmpty(search.VariableDefinitions))
                {
                    _searchVariables = DeserializeSearchVariables(search.VariableDefinitions);
                    _hasSearchVariables = _searchVariables.Count > 0;
                }
                _variablesRestoredFromDb = true;
            }

            if (!string.IsNullOrEmpty(_searchPhrase) && !_hasSearchVariables && VariableResolver.ContainsVariables(_searchPhrase))
            {
                DetectVariablesInSearchPhrase();
                dirty = true;
            }
        }

        private VisualElement CreateNativeSearchFooter()
        {
            CommonUITK.ThreeZoneLayout footer = AssetInventoryUITK.CreateNavigationFooterLayout();
            footer.Root.AddToClassList(SearchFooterClass);

            VisualElement leftGroup = footer.Left;
            leftGroup.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("search.actions.viewmode", () =>
            {
                _nativeSearchViewModeControl = AssetInventoryUITK.CreateSegmentedControl(GetPackageViewOptions(), AI.Config.searchViewMode, SelectNativeSearchViewMode);
                return _nativeSearchViewModeControl;
            }, onVisibilityChanged: RebuildNativeSearchBody));

            if (AI.Config.searchViewMode == 1)
            {
                leftGroup.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("search.actions.tilesize", () =>
                {
                    _nativeSearchGridSizeControl = AssetInventoryUITK.CreateGridSizeControl(
                        AI.Config.searchTileSize,
                        50,
                        300,
                        SetNativeSearchGridSize,
                        false);
                    return _nativeSearchGridSizeControl;
                }, onVisibilityChanged: RebuildNativeSearchBody));

                leftGroup.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("search.actions.previewanim", () =>
                {
                    _nativeSearchPreviewAnimationButton = AssetInventoryUITK.CreateNavigationFooterIconButton(
                        "Play all visible animated previews automatically.",
                        "d_PlayButton",
                        AI.Config.playVisibleSearchAnimations,
                        ToggleNativeSearchPreviewAnimations);
                    return _nativeSearchPreviewAnimationButton;
                }, onVisibilityChanged: RebuildNativeSearchBody));
            }
            VisualElement centerGroup = footer.Center;
            _nativeSearchFooterSummary = new Label();
            _nativeSearchFooterSummary.AddToClassList(AssetInventoryUITK.NavigationFooterSummaryClass);
            centerGroup.Add(_nativeSearchFooterSummary);

            _nativeSearchPager = AssetInventoryUITK.CreatePaginationControl(this);
            centerGroup.Add(_nativeSearchPager);

            if (!searchMode)
            {
                centerGroup.Add(AssetInventoryUITK.CreateAdvancedVisibilityBlock("search.actions.scope", () =>
                {
                    _nativeSearchScopeControl = CreateNativeSearchScopeControl(() =>
                    {
                        _requireSearchUpdate = true;
                        PerformSearch();
                        RefreshNativeSearchFooterState();
                    });
                    return _nativeSearchScopeControl;
                }, onVisibilityChanged: RebuildNativeSearchBody));
            }

            if (!hideDetailsPane && UseNativeNarrowDetailsLayout())
            {
                footer.Root.AddToClassList(NarrowDetailsFooterClass);
                _nativeSearchNarrowDetailsAction = new VisualElement();
                _nativeSearchNarrowDetailsAction.AddToClassList(NarrowDetailsActionClass);
                _nativeSearchNarrowDetailsSelection = new Label();
                _nativeSearchNarrowDetailsSelection.AddToClassList(NarrowDetailsSelectionClass);
                _nativeSearchNarrowDetailsAction.Add(_nativeSearchNarrowDetailsSelection);
                Button details = AssetInventoryUITK.CreateSecondaryButton("Details", OpenNativeSearchNarrowDetails);
                details.tooltip = "Open details for the current selection.";
                _nativeSearchNarrowDetailsAction.Add(details);
                footer.Right.Add(_nativeSearchNarrowDetailsAction);
            }
            RefreshNativeSearchFooterState();
            return footer.Root;
        }

        private void RefreshNativeSearchFooterState()
        {
            if (_nativeSearchFooter == null) return;

            AssetInventoryUITK.RefreshSegmentedControl(_nativeSearchViewModeControl, AI.Config.searchViewMode);
            RefreshNativeSearchScopeControl(_nativeSearchScopeControl);

            _nativeSearchGridSizeControl?.SetValueWithoutNotify(AI.Config.searchTileSize);
            RefreshNativeSearchTileDetailPopup();

            if (_nativeSearchPreviewAnimationButton != null)
            {
                AssetInventoryUITK.SetNavigationFooterButtonActive(_nativeSearchPreviewAnimationButton, AI.Config.playVisibleSearchAnimations);
            }
            RefreshNativeSearchNarrowDetailsAction();
            RefreshNativeSearchResultControls();
        }

        private void RefreshNativeSearchNarrowDetailsAction()
        {
            if (_nativeSearchNarrowDetailsAction == null) return;

            int selectionCount = SGrid.selectionItems?.Count ?? 0;
            if (selectionCount == 0 && _selectedEntry != null) selectionCount = 1;
            _nativeSearchNarrowDetailsAction.style.display = selectionCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_nativeSearchNarrowDetailsSelection == null || selectionCount == 0) return;

            _nativeSearchNarrowDetailsSelection.text = selectionCount == 1 && _selectedEntry != null
                ? _selectedEntry.FileName
                : $"{selectionCount:N0} assets selected";
            _nativeSearchNarrowDetailsSelection.tooltip = _nativeSearchNarrowDetailsSelection.text;
        }

        private void OpenNativeSearchNarrowDetails()
        {
            if (_nativeSearchNarrowMain == null || _nativeSearchNarrowDetails == null) return;
            if ((_selectedEntry == null) && (SGrid.selectionItems == null || SGrid.selectionItems.Count == 0)) return;

            _nativeSearchNarrowDetailsOpen = true;
            RefreshNativeSearchInspector();
            ApplyNativeSearchNarrowDetailsState();
            _nativeSearchNarrowDetails.Focus();
        }

        private void CloseNativeSearchNarrowDetails()
        {
            _nativeSearchNarrowDetailsOpen = false;
            ApplyNativeSearchNarrowDetailsState();
            if (_nativeSearchTreeView != null)
            {
                _nativeSearchTreeView.Focus();
            }
            else
            {
                _nativeSearchGridView?.Focus();
            }
        }

        private void ApplyNativeSearchNarrowDetailsState()
        {
            if (_nativeSearchNarrowMain == null || _nativeSearchNarrowDetails == null) return;

            _nativeSearchNarrowMain.style.display = _nativeSearchNarrowDetailsOpen ? DisplayStyle.None : DisplayStyle.Flex;
            _nativeSearchNarrowDetails.style.display = _nativeSearchNarrowDetailsOpen ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnNativeSearchNarrowDetailsKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape) return;

            CloseNativeSearchNarrowDetails();
            evt.StopPropagation();
        }

        private void RefreshNativeSearchResultControls()
        {
            bool hasResults = (SGrid.HasPreviewSlots && SGrid.PreviewCount > 0) ||
                (AI.Config.searchViewMode == 0 && _filteredFiles != null && _filteredFiles.Count > 0);

            RefreshNativeSearchEmptyState(hasResults);

            if (_nativeSearchFooterSummary != null)
            {
                _nativeSearchFooterSummary.text = hasResults && _pageCount <= 1 ? $"{_resultCount:N0} results" : string.Empty;
                _nativeSearchFooterSummary.style.display = hasResults && _pageCount <= 1 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_nativeSearchPager == null) return;
            _nativeSearchPager.SetState(
                _curPage,
                _pageCount,
                $"{_resultCount:N0} results in total",
                SetPage,
                hasResults);
        }

        private void RefreshNativeSearchEmptyState(bool hasResults)
        {
            if (_nativeSearchEmptyState == null) return;

            bool showEmpty = !hasResults && string.IsNullOrEmpty(_searchError);
            _nativeSearchEmptyState.style.display = showEmpty ? DisplayStyle.Flex : DisplayStyle.None;
            if (!showEmpty) return;

            if (_files == null)
            {
                _nativeSearchEmptyState.SetContent(
                    "Search your asset library",
                    "Enter a name, path, tag, or phrase. Use the filters above when you need to narrow the result set.");
                return;
            }

            bool showHiddenExtensions = AI.Config.excludeExtensions
                && string.IsNullOrEmpty(GetRawSearchType())
                && !string.IsNullOrWhiteSpace(AI.Config.excludedExtensions);
            string detail = showHiddenExtensions
                ? $"Hidden extensions: {FormatHiddenExtensionsForDisplay(AI.Config.excludedExtensions)}"
                : "Try a broader phrase or remove one of the active filters.";
            _nativeSearchEmptyState.SetContent("No matching assets", detail);
        }

        private void SelectNativeSearchViewMode(int index)
        {
            if (AI.Config.searchViewMode == index) return;

            AI.Config.searchViewMode = index;
            SGrid.DeselectAll();
            _nativeSearchSelection.Clear();
            _selectedEntry = null;
            AI.SaveConfig();
            RebuildNativeSearchBody();
        }

        private static float GetNativeSearchHierarchyPaneWidth()
        {
            return Mathf.Clamp(AI.Config.searchHierarchySideBarWidth, 180f, 480f);
        }

        private static float GetNativeSearchInspectorPaneWidth()
        {
            return Mathf.Clamp(AI.Config.searchInspectorWidth, 220f, 720f);
        }

        private void OnNativeSearchHierarchyPaneStateChanged(float width, bool isOpen)
        {
            AI.Config.searchHierarchySideBarWidth = width;
            AI.Config.showSearchHierarchySideBar = isOpen;
            AI.SaveConfig();
            RefreshNativeSearchPaneGutters();
            _nativeSearchHierarchyActive = !searchMode && isOpen;
            if (_nativeSearchHierarchyActive)
            {
                _requireHierarchyRebuild = true;
                RefreshNativeSearchHierarchy();
            }
            _nativeSearchCompositionSignature = GetNativeSearchCompositionSignature();
        }

        private void OnNativeSearchInspectorPaneStateChanged(float width, bool isOpen)
        {
            AI.Config.searchInspectorWidth = width;
            AI.Config.showSearchSideBar = isOpen;
            AI.SaveConfig();
            RefreshNativeSearchPaneGutters();
            if (isOpen) RefreshNativeSearchInspector();
            _nativeSearchCompositionSignature = GetNativeSearchCompositionSignature();
        }

        private void RefreshNativeSearchPaneGutters()
        {
            if (_nativeSearchPaneLayout == null || _nativeSearchPaneLayout.parent == null) return;

            VisualElement body = _nativeSearchPaneLayout.parent;
            bool hasLeadingPane = _nativeSearchPaneLayout.Q<VisualElement>(className: "ai-resizable-pane-leading") != null;
            bool hasTrailingPane = _nativeSearchPaneLayout.Q<VisualElement>(className: "ai-resizable-pane-trailing") != null;
            body.EnableInClassList(
                SearchBodyWithCollapsedLeadingPaneClass,
                hasLeadingPane && !_nativeSearchPaneLayout.IsPaneOpen(CommonSidePane.Leading));
            body.EnableInClassList(
                SearchBodyWithCollapsedTrailingPaneClass,
                hasTrailingPane && !_nativeSearchPaneLayout.IsPaneOpen(CommonSidePane.Trailing));
        }

        private void ToggleNativeSearchPreviewAnimations()
        {
            AI.Config.playVisibleSearchAnimations = !AI.Config.playVisibleSearchAnimations;
            AI.SaveConfig();
            if (AI.Config.playVisibleSearchAnimations)
            {
                TriggerVisibleAnimationsUpdate();
            }
            else
            {
                DisposeAllVisibleAnimations(true);
            }

            RefreshNativeSearchFooterState();
        }

        private void RunSearchTabDelayedLogic(bool force = false)
        {
            if (!force && !_allowLogic) return;

            if (_requireSearchUpdate && AI.Config.searchAutomatically)
            {
                if (!_searchHandlerAdded || EditorApplication.delayCall == null)
                {
                    _searchHandlerAdded = true;
                    EditorApplication.delayCall += PerformDelayedSearch;
                }
            }
            if (_requireSearchSelectionUpdate)
            {
                if (!_selectionHandlerAdded || EditorApplication.delayCall == null)
                {
                    _selectionHandlerAdded = true;
                    EditorApplication.delayCall += HandleSearchSelectionChanged;
                }
            }
        }

        private void PerformDelayedSearch()
        {
            _searchHandlerAdded = false;
            if (this == null || _isCleaningUp) return;

            PerformSearch(_keepSearchResultPage);
        }

        private bool SearchWithoutInput()
        {
            return workspaceMode ? AI.Config.wsSearchWithoutInput : AI.Config.searchWithoutInput;
        }

        private void HandleSearchSelectionChanged()
        {
            if (AI.DEBUG_MODE) Debug.LogWarning("HandleSearchSelectionChanged");

            _requireSearchSelectionUpdate = false;
            _selectionHandlerAdded = false;
            EditorApplication.delayCall -= HandleSearchSelectionChanged;

            AudioManager.StopAudio();
            DisposeAnimTexture();
            bool isAudio = AI.IsFileType(_selectedEntry?.Path, AI.AssetGroup.Audio);
            if (_selectedEntry != null)
            {
                _selectedEntry.Refresh();
                Assets.ResolveChildren(_selectedEntry, _assets);
                AI.GetObserver().SetPrioritized(new List<AssetInfo> {_selectedEntry});
                _selectedEntry.PackageDownloader?.RefreshState();

                _selectedEntry.CheckIfInProject();
                _selectedEntry.IsMaterialized = _selectedEntry.IsVirtual || Assets.IsMaterialized(_selectedEntry.ToAsset(), _selectedEntry);
                if (_selectedEntry.IsVirtual && _selectedEntry.Size == 0 && !string.IsNullOrEmpty(_selectedEntry.ProjectPath))
                {
                    FileInfo fi = new FileInfo(_selectedEntry.ProjectPath);
                    if (fi.Exists) _selectedEntry.Size = fi.Length;
                }
                if (!_selectedEntry.IsVirtual) _ = AssetUtils.LoadPackageTexture(_selectedEntry);

                // Stop all visible animations when selecting a single item, restoring their static previews
                DisposeAllVisibleAnimations(true);
                LoadAnimTexture(_selectedEntry);

                CalcDependenciesOnDemand(_selectedEntry);
                RecreatePreviewEditor();

                if (!_searchDone && AI.Config.pingSelected && _selectedEntry.InProject) PingAsset(_selectedEntry);
            }
            _searchDone = false;

            if (_searchSelectionChangedManually)
            {
                _searchSelectionChangedManually = false;
                _searchInspectorTab = 0;
                if (instantSelection)
                {
                    ExecuteSingleAction();
                }
                else if (AI.Config.autoPlayAudio && isAudio) PlayAudio(_selectedEntry);
            }
        }

        private void CalcDependenciesOnDemand(AssetInfo entry)
        {
            if (entry.IsVirtual)
            {
                if (entry.DependencyState == AssetInfo.DependencyStateOptions.Unknown && !string.IsNullOrEmpty(entry.ProjectPath))
                {
                    List<AssetFile> deps = ProjectDependencyAnalysis.GetDependencies(entry.ProjectPath, entry.AssetId);
                    entry.Dependencies = deps;
                    entry.MediaDependencies = deps;
                    entry.ScriptDependencies = new List<AssetFile>();
                    entry.CrossPackageDependencies = new List<Asset>();
                    entry.DependencyState = AssetInfo.DependencyStateOptions.Done;
                }
                return;
            }

            if (AI.Config.autoCalculateDependencies == 2)
            {
                // if entry is already materialized calculate dependencies immediately
                if ((entry.DependencyState == AssetInfo.DependencyStateOptions.Unknown || entry.DependencyState == AssetInfo.DependencyStateOptions.Partial) &&
                    entry.IsMaterialized &&
                    DependencyAnalysis.NeedsScan(entry.Type))
                {
                    // must run in same thread
                    _ = CalculateDependencies(entry);
                }
            }
        }

        private bool IsSearchFilterActive()
        {
            return GetActiveSearchFilterCount() > 0;
        }

        private int GetActiveSearchFilterCount()
        {
            int count = 0;
            if (_selectedImageType > 0) count++;
            if (!string.IsNullOrEmpty(_searchWidth)) count++;
            if (!string.IsNullOrEmpty(_searchHeight)) count++;
            if (!string.IsNullOrEmpty(_searchLength)) count++;
            if (!string.IsNullOrEmpty(_searchSize)) count++;
            if (!string.IsNullOrEmpty(_searchVertexCount)) count++;
            if (_selectedPackageTag > 0) count++;
            if (_selectedFileTag > 0) count++;
            if (_selectedAsset > 0) count++;
            if (_selectedPublisher > 0) count++;
            if (_selectedCategory > 0) count++;
            if (_selectedColorOption > 0) count++;
            if (_selectedPackageTypes != 1) count++;
            if (_selectedPackageSRPs != 1) count++;
            if (_selectedPriceOption > 0) count++;
            if (_selectedHiddenFilter > 0) count++;
            return count;
        }

        private bool IsProjectFilterActive()
        {
            return _selectedImageType > 0
                || !string.IsNullOrEmpty(_searchWidth)
                || !string.IsNullOrEmpty(_searchHeight)
                || !string.IsNullOrEmpty(_searchLength)
                || !string.IsNullOrEmpty(_searchSize)
                || !string.IsNullOrEmpty(_searchVertexCount);
        }

        private bool IsIndexOnlyFilterActive()
        {
            return _pendingSearchNavigationTarget?.Id > 0
                || _selectedPackageTag > 0
                || _selectedFileTag > 0
                || _selectedAsset > 0
                || _selectedPublisher > 0
                || _selectedCategory > 0
                || _selectedColorOption > 0
                || _selectedPackageTypes != 1
                || _selectedPackageSRPs != 1
                || _selectedPriceOption > 0
                || _selectedHiddenFilter > 0;
        }

        private ProjectAssetSearch.Options CreateProjectSearchOptions(bool ignoreExcludedExtensions)
        {
            return new ProjectAssetSearch.Options
            {
                SearchPhrase = _searchPhrase,
                RawSearchType = GetRawSearchType(),
                IgnoreExcludedExtensions = ignoreExcludedExtensions,
                SearchWidth = _searchWidth,
                CheckMaxWidth = _checkMaxWidth,
                SearchHeight = _searchHeight,
                CheckMaxHeight = _checkMaxHeight,
                SearchLength = _searchLength,
                CheckMaxLength = _checkMaxLength,
                SearchSize = _searchSize,
                CheckMaxSize = _checkMaxSize,
                SearchVertexCount = _searchVertexCount,
                CheckMaxVertexCount = _checkMaxVertexCount,
                SelectedImageType = _selectedImageType,
                ImageTypeOptions = _imageTypeOptions,
                SelectedGuid = _pendingSearchNavigationTarget?.Id > 0 ? null : _pendingSearchNavigationTarget?.Guid,
                MaxResults = AI.Config.maxProjectSearchResults > 0 ? AI.Config.maxProjectSearchResults : 0,
                SortField = AI.Config.sortField,
                SortDescending = AI.Config.sortDescending
            };
        }

        private string GetProjectSearchCacheKey(bool ignoreExcludedExtensions)
        {
            return $"{_searchPhrase}|{GetRawSearchType()}|{_searchWidth}|{_checkMaxWidth}|{_searchHeight}|{_checkMaxHeight}|{_searchLength}|{_checkMaxLength}|{_searchSize}|{_checkMaxSize}|{_searchVertexCount}|{_checkMaxVertexCount}|{_selectedImageType}|{AI.Config.maxProjectSearchResults}|{AI.Config.sortField}|{AI.Config.sortDescending}|{ignoreExcludedExtensions}|{AI.Config.excludeExtensions}|{AI.Config.searchType}|{AI.Config.excludedExtensions}|{_pendingSearchNavigationTarget?.Guid}";
        }

        private async Task<bool> EnsureDownloaded(AssetInfo info)
        {
            if (info.IsDownloaded) return true;

            AssetInfo downloadTarget = info.ParentInfo ?? info;
            if (downloadTarget.IsAbandoned)
            {
                Debug.LogError($"Cannot download '{downloadTarget.GetDisplayName()}' as it is an abandoned package.");
                return false;
            }

            if (downloadTarget.PackageDownloader == null)
            {
                downloadTarget.PackageDownloader = new AssetDownloader(downloadTarget);
            }
            if (!downloadTarget.PackageDownloader.IsDownloadSupported()) return false;

            AI.GetObserver().Attach(downloadTarget);
            _curOperation = $"Downloading {downloadTarget.GetDisplayName()}...";
            downloadTarget.PackageDownloader.Download(true);
            do
            {
                await Task.Delay(200);
                downloadTarget.PackageDownloader.RefreshState();
                float progress = downloadTarget.PackageDownloader.GetState().progress * 100f;
                _curOperation = $"Downloading {downloadTarget.GetDisplayName()}: {progress:N0}%...";
            } while (downloadTarget.IsDownloading());
            await Task.Delay(3000); // ensure all file operations have finished
            PackageDownloadCompletion.SyncPackage(downloadTarget);
            _curOperation = null;

            return downloadTarget.IsDownloaded;
        }

        private bool IsDownloadable(AssetInfo info)
        {
            if (info.IsDownloaded) return false;
            if (info.IsAbandoned) return false;
            AssetInfo downloadTarget = info.ParentInfo ?? info;
            return downloadTarget.AssetSource == Asset.Source.AssetStorePackage;
        }

        private async void ImportBulkFiles(List<AssetInfo> items)
        {
            // UI selection lists can change while downloads and imports yield back to the editor.
            List<AssetInfo> importItems = new List<AssetInfo>(items);
            _blockingInProgress = true;
            List<AssetImportCollision> collisions = new List<AssetImportCollision>();
            HashSet<string> displacedGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<AssetInfo> importedItems = new List<AssetInfo>();
            try
            {
                foreach (AssetInfo info in importItems)
                {
                    info.CheckIfInProject();
                    if (info.InProject) continue;

                    if (!info.IsDownloaded)
                    {
                        if (!await EnsureDownloaded(info))
                        {
                            Debug.LogWarning($"Skipping import of '{info.GetDisplayName()}': download failed.");
                            continue;
                        }
                    }

                    // Must be done consecutively to avoid IO conflicts.
                    AssetImportResult result = await Assets.CopyToWithResult(info, _importFolder, true, logCollisionWarnings: false);
                    collisions.AddRange(result.Collisions);
                    displacedGuids.UnionWith(result.DisplacedGuids);
                    if (!string.IsNullOrEmpty(result.ImportedPath)) importedItems.Add(info);
                }
            }
            finally
            {
                _blockingInProgress = false;
                RefreshImportedAssetStates(importedItems, displacedGuids);
                ShowImportCollisionSummary(collisions);
            }
        }

        private async void ExecuteSingleAction()
        {
            AssetInfo selectedEntry = _selectedEntry;
            if (selectedEntry == null) return;
            if (!selectedEntry.InProject && string.IsNullOrEmpty(_importFolder))
            {
                EditorUtility.DisplayDialog("Missing Target", "Select a target folder in the Project View first to proceed.", "OK");
                return;
            }

            if (!TryBeginPickerSelection(selectedEntry)) return;

            bool completed = false;
            Dictionary<string, string> textureResult = null;
            string selectedProjectPath = null;
            try
            {
                List<AssetInfo> files = new List<AssetInfo>();
                Dictionary<string, AssetInfo> identifiedTextures = null;
                if (textureMode)
                {
                    identifiedTextures = IdentifyTextures(selectedEntry);
                    files.AddRange(identifiedTextures.Values.Distinct());
                }
                else
                {
                    files.Add(selectedEntry);
                }

                foreach (AssetInfo info in files)
                {
                    info.CheckIfInProject();
                    if (!info.InProject)
                    {
                        // download on-demand
                        if (!info.IsDownloaded)
                        {
                            if (!await EnsureDownloaded(info)) return;
                        }

                        _curOperation = $"Extracting and importing '{info.FileName}'...";
                        RefreshNativeProgressOverlay();
                        AssetImportResult result = await Assets.CopyToWithResult(info, _importFolder, true, logCollisionWarnings: false);
                        RefreshImportedAssetStates(new[] {info}, result.DisplacedGuids);

                        if (result.HasCollisions)
                        {
                            ShowImportCollisionSummary(result.Collisions);
                            return;
                        }

                        if (!info.InProject)
                        {
                            Debug.LogError("The file could not be materialized into the project.");
                            return;
                        }
                    }
                }

                if (textureMode)
                {
                    textureResult = new Dictionary<string, string>();
                    foreach (KeyValuePair<string, AssetInfo> file in identifiedTextures)
                    {
                        textureResult.Add(file.Key, file.Value.ProjectPath);
                    }
                }
                else
                {
                    selectedProjectPath = selectedEntry.ProjectPath;
                }
                completed = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Asset selection failed for '{selectedEntry.FileName}': {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                EndPickerSelection();
            }

            if (!completed) return;

            Close();
            AudioManager.StopAudio();
            if (textureMode)
            {
                searchModeTextureCallback?.Invoke(textureResult);
            }
            else
            {
                searchModeCallback?.Invoke(selectedProjectPath);
            }
        }

        internal bool TryBeginPickerSelection(AssetInfo selectedEntry)
        {
            if (selectedEntry == null || _pickerSelectionInProgress || _blockingInProgress) return false;

            _pickerSelectionInProgress = true;
            _blockingInProgress = true;
            _lockSelection = true;
            _pickerSelectionStartTime = EditorApplication.timeSinceStartup;
            _curOperation = $"Preparing '{selectedEntry.FileName}'...";
            _needsRepaint = true;
            RefreshNativeProgressOverlay();
            return true;
        }

        internal void EndPickerSelection()
        {
            if (!_pickerSelectionInProgress) return;

            _pickerSelectionInProgress = false;
            _blockingInProgress = false;
            _lockSelection = false;
            _curOperation = null;
            _needsRepaint = true;
            RefreshNativeProgressOverlay();
        }

        private Dictionary<string, AssetInfo> IdentifyTextures(AssetInfo info)
        {
            TextureNameSuggester tns = new TextureNameSuggester();
            Dictionary<string, string> files = tns.SuggestFileNames(info.Path, path =>
            {
                string sep = info.Path.Contains("/") ? "/" : "\\";
                string toCheck = info.Path.Substring(0, info.Path.LastIndexOf(sep) + 1) + Path.GetFileName(path);
                AssetInfo ai = Assets.GetAssetByPath(toCheck, info.ToAsset());
                return ai?.Path; // capitalization could be different from actual validation request, so use result
            });

            Dictionary<string, AssetInfo> result = new Dictionary<string, AssetInfo>();
            foreach (KeyValuePair<string, string> file in files)
            {
                AssetInfo ai = Assets.GetAssetByPath(file.Value, info.ToAsset());
                if (ai != null) result.Add(file.Key, ai);
            }
            return result;
        }

        private async void RecreatePreviews(List<AssetInfo> infos)
        {
            _blockingInProgress = true;

            await AI.Actions.RunWithProgress<PreviewPipeline>(
                ActionHandler.ACTION_PREVIEWS_RECREATE,
                "Recreating previews",
                async imp =>
                {
                    if (await imp.RecreatePreviews(infos, false, null, false, req =>
                        {
                            if (infos.Count > 1) return;
                            if (req == null)
                            {
                                EditorUtility.DisplayDialog("Error", "Preview could not be created.", "OK");
                            }
                            else if (req.IncompatiblePipeline)
                            {
                                string message = string.IsNullOrWhiteSpace(req.FailureReason)
                                    ? "Preview could not be created. The item is incompatible to the currently used render pipeline."
                                    : $"Preview could not be created.\n\nCause: {req.FailureReason}";
                                EditorUtility.DisplayDialog("Pipeline Error", message, "OK");
                            }
                            else if (!string.IsNullOrWhiteSpace(req.FailureReason))
                            {
                                EditorUtility.DisplayDialog("Preview Recreation Failed", $"Preview could not be created.\n\nCause: {req.FailureReason}", "OK");
                            }
                        }) > 0) _requireSearchUpdate = true;
                });

            _blockingInProgress = false;
        }

        private async void RecreateAICaptions(List<AssetInfo> infos)
        {
            _blockingInProgress = true;

            await AI.Actions.RunWithProgress<CaptionCreator>(
                ActionHandler.ACTION_AI_CAPTIONS,
                "Creating selective AI captions",
                imp => imp.Run(infos));

            _requireSearchUpdate = true;
            _blockingInProgress = false;
        }

        private void LoadSearch(SavedSearch search)
        {
            _searchPhrase = search.SearchPhrase;
            _previousSearchPhrase = search.SearchPhrase;
            _selectedPackageTypes = search.PackageTypes;
            _selectedPackageSRPs = search.PackageSrPs;
            _selectedPriceOption = search.PriceOption;
            _searchPrice = search.Price;
            _selectedImageType = search.ImageType;
            _selectedColorOption = search.ColorOption;
            _selectedHiddenFilter = search.Hidden;
            _selectedColor = ImageUtils.FromHex(search.SearchColor);
            _searchWidth = search.Width;
            _searchHeight = search.Height;
            _searchLength = search.Length;
            _searchSize = search.Size;
            _checkMaxWidth = search.CheckMaxWidth;
            _checkMaxHeight = search.CheckMaxHeight;
            _checkMaxLength = search.CheckMaxLength;
            _checkMaxSize = search.CheckMaxSize;
            _searchVertexCount = search.VertexCount;
            _checkMaxVertexCount = search.CheckMaxVertexCount;

            // Restore dropdowns (match by ID if brackets exist, otherwise by string)
            AI.Config.searchType = string.IsNullOrWhiteSpace(search.Type) ? 0 : Mathf.Max(0, Array.FindIndex(_types, s => s.Split('/').LastOrDefault() == search.Type));
            _selectedPublisher = FindIndexByValue(_publisherNames, search.Publisher, splitPath: true);
            _selectedAsset = FindIndexByValue(_assetNames, search.Package, splitPath: true);
            _selectedCategory = FindIndexByValue(_categoryNames, search.Category, splitPath: false);
            _selectedPackageTag = FindIndexByValue(_tagNames, search.PackageTag, splitPath: false);
            _selectedFileTag = FindIndexByValue(_tagNames, search.FileTag, splitPath: false);

            // Load variable definitions
            if (!string.IsNullOrEmpty(search.VariableDefinitions))
            {
                _searchVariables = DeserializeSearchVariables(search.VariableDefinitions);
            }

            // Always detect variables from the search phrase to ensure UI renders correctly
            // This also handles the case where the phrase has variables but no stored definitions
            DetectVariablesInSearchPhrase();

            _activeSavedSearchId = search.Id;
            _variablesRestoredFromDb = true;
            _requireSearchUpdate = true;
            RefreshSearchField();
        }

        private void PopulateSavedSearchFromCurrentState(SavedSearch search)
        {
            search.SearchPhrase = _searchPhrase;
            search.PackageTypes = _selectedPackageTypes;
            search.PackageSrPs = _selectedPackageSRPs;
            search.PriceOption = _selectedPriceOption;
            search.Price = _searchPrice;
            search.ImageType = _selectedImageType;
            search.ColorOption = _selectedColorOption;
            search.SearchColor = "#" + ColorUtility.ToHtmlStringRGB(_selectedColor);
            search.Width = _searchWidth;
            search.Height = _searchHeight;
            search.Length = _searchLength;
            search.Size = _searchSize;
            search.CheckMaxWidth = _checkMaxWidth;
            search.CheckMaxHeight = _checkMaxHeight;
            search.CheckMaxLength = _checkMaxLength;
            search.CheckMaxSize = _checkMaxSize;
            search.VertexCount = _searchVertexCount;
            search.CheckMaxVertexCount = _checkMaxVertexCount;
            search.Hidden = _selectedHiddenFilter;

            // Store type (extract last component as full types don't have IDs)
            if (AI.Config.searchType > 0 && _types.Length > AI.Config.searchType)
            {
                search.Type = _types[AI.Config.searchType].Split('/').LastOrDefault();
            }
            else
            {
                search.Type = null;
            }

            // Store full selection strings (will extract IDs during restore if needed)
            search.Publisher = _selectedPublisher > 0 && _publisherNames.Length > _selectedPublisher
                ? _publisherNames[_selectedPublisher].Split('/').LastOrDefault()
                : null;

            search.Package = _selectedAsset > 0 && _assetNames.Length > _selectedAsset
                ? _assetNames[_selectedAsset].Split('/').LastOrDefault()
                : null;

            search.Category = _selectedCategory > 0 && _categoryNames.Length > _selectedCategory
                ? _categoryNames[_selectedCategory]
                : null;

            search.PackageTag = _selectedPackageTag > 0 && _tagNames.Length > _selectedPackageTag
                ? _tagNames[_selectedPackageTag]
                : null;

            search.FileTag = _selectedFileTag > 0 && _tagNames.Length > _selectedFileTag
                ? _tagNames[_selectedFileTag]
                : null;

            // Serialize variable metadata
            search.VariableDefinitions = SerializeSearchVariables(_searchVariables);
        }

        private void SaveSearch(string value)
        {
            SavedSearch search = new SavedSearch();
            search.Name = value;
            search.Color = ColorUtility.ToHtmlStringRGB(Random.ColorHSV());

            PopulateSavedSearchFromCurrentState(search);

            DBAdapter.DB.Insert(search);
            Searches.Add(search);
            _activeSavedSearchId = search.Id;
            _nativeSearchSavedSearchesDirty = true;

            // add to current workspace as well
            if (_selectedWorkspace != null)
            {
                WorkspaceSearch wsSearch = new WorkspaceSearch
                {
                    WorkspaceId = _selectedWorkspace.Id,
                    SavedSearchId = search.Id,
                    OrderIdx = _selectedWorkspace.Searches.Count
                };
                DBAdapter.DB.Insert(wsSearch);
                _selectedWorkspace.Searches.Add(wsSearch);
            }
        }

        private void OverrideSavedSearch(SavedSearch search)
        {
            PopulateSavedSearchFromCurrentState(search);
            DBAdapter.DB.Update(search);

            // Set as active search
            _activeSavedSearchId = search.Id;
            _variablesRestoredFromDb = true;
            _nativeSearchSavedSearchesDirty = true;
        }

        private void SaveWorkspace(string value)
        {
            Workspace ws = new Workspace();
            ws.Name = value;

            DBAdapter.DB.Insert(ws);
            Workspaces.Add(ws);
            _nativeSearchSavedSearchesDirty = true;

            WorkspaceUI workspaceUI = WorkspaceUI.ShowWindow();
            workspaceUI.Init(ws);
        }

        private async void PlayAudio(AssetInfo info)
        {
            // play instantly if no extraction is required
            if (_blockingInProgress)
            {
                if (Assets.IsMaterialized(info.ToAsset(), info)) await Assets.PlayAudio(info);
                return;
            }

            await Assets.PlayAudio(info, InitBlockingToken());
            DisposeBlocking();
        }

        private void OpenAudioEditor(AssetInfo info, string importFolder)
        {
            if (info == null || string.IsNullOrEmpty(importFolder)) return;

            AudioManager.StopAudio();

            // Use the AudioTool package with AssetInfoAudioSource bridge
            // Force CreateCopy mode when embedded in AssetInventory
            AssetInfoAudioSource audioSource = new AssetInfoAudioSource(info);
            AudioEditorUI window = AudioEditorUI.ShowWindow();
            window.Init(audioSource, importFolder);
        }

        private async void PingAsset(AssetInfo info)
        {
            if (disablePings) return;

            string projectPath = info.ProjectPath;
            if (!AssetUtils.IsAssetDatabasePath(projectPath))
            {
                info.ProjectPath = null;
                return;
            }

            // requires pauses in-between to allow editor to catch up
            EditorApplication.ExecuteMenuItem("Window/General/Project");
            await Task.Yield();

            Selection.activeObject = null;
            await Task.Yield();

            Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(projectPath);
            if (Selection.activeObject == null) info.ProjectPath = null; // probably got deleted again

            // For virtual assets, schedule a preview retry since pinging causes Unity to generate previews
            if (info.IsVirtual && _filteredFiles != null)
            {
                int idx = _filteredFiles.IndexOf(info);
                if (idx >= 0)
                {
                    PendingVirtualPreviews[idx] = info;
                    if (_textureLoading != null)
                    {
                        RetryPendingVirtualPreviews(_textureLoading.Token);
                    }
                }
            }
        }

        private async Task CalculateDependencies(AssetInfo info)
        {
            // If already calculating, don't start another calculation
            if (_dependencyCancellationTokens != null && _dependencyCancellationTokens.ContainsKey(info)) return;

            CancellationTokenSource cts = new CancellationTokenSource();
            if (_dependencyCancellationTokens == null) _dependencyCancellationTokens = new Dictionary<AssetInfo, CancellationTokenSource>();
            _dependencyCancellationTokens[info] = cts;

            try
            {
                await AI.CalculateDependencies(info, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is expected, don't log as error
                // Reset state if canceled
                if (info.DependencyState == AssetInfo.DependencyStateOptions.Calculating)
                {
                    info.DependencyState = AssetInfo.DependencyStateOptions.Unknown;
                }
            }
            finally
            {
                // Clean up the token source
                _dependencyCancellationTokens?.Remove(info);
                cts.Dispose();
            }
        }

        private void CancelDependencyCalculation(AssetInfo info)
        {
            if (_dependencyCancellationTokens == null || info == null) return;

            if (_dependencyCancellationTokens.TryGetValue(info, out CancellationTokenSource cts))
            {
                cts?.Cancel();
                _dependencyCancellationTokens.Remove(info);
                cts?.Dispose();

                // Reset state
                if (info.DependencyState == AssetInfo.DependencyStateOptions.Calculating)
                {
                    info.DependencyState = AssetInfo.DependencyStateOptions.Unknown;
                }
            }
        }

        private void ShowWhereUsed(AssetInfo info)
        {
            string path = info.ProjectPath;
            if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(info.Guid))
            {
                path = AssetDatabase.GUIDToAssetPath(info.Guid);
            }
            if (string.IsNullOrEmpty(path)) return;

            WhereUsedUI window = WhereUsedUI.ShowWindow();
            window.Init(path);
        }

        private void ShowProjectDependencies(AssetInfo info)
        {
            string path = info.ProjectPath;
            if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(info.Guid))
            {
                path = AssetDatabase.GUIDToAssetPath(info.Guid);
            }
            if (string.IsNullOrEmpty(path)) return;

            List<AssetFile> deps = ProjectDependencyAnalysis.GetDependencies(path, info.AssetId);
            info.Dependencies = deps;
            info.MediaDependencies = deps;
            info.ScriptDependencies = new List<AssetFile>();
            info.CrossPackageDependencies = new List<Asset>();
            info.SRPSupportPackage = null;
            info.DependencyState = AssetInfo.DependencyStateOptions.Done;

            DependenciesUI depUI = DependenciesUI.ShowWindow();
            depUI.Init(info, OpenAssetFileInSearch);
        }

        private async void Open(AssetInfo info)
        {
            if (!info.IsDownloaded && !info.IsMaterialized)
            {
                if (!await EnsureDownloaded(info)) return;
            }

            _blockingInProgress = true;
            string targetPath;
            if (info.InProject)
            {
                targetPath = info.ProjectPath;
            }
            else
            {
                targetPath = await Assets.EnsureMaterialized(info);
                if (info.Id == 0) _requireSearchUpdate = true; // was deleted
            }

            if (targetPath != null) EditorUtility.OpenWithDefaultApp(targetPath);
            _blockingInProgress = false;
        }

        private async void OpenExplorer(AssetInfo info)
        {
            if (!info.IsDownloaded && !info.IsMaterialized)
            {
                if (!await EnsureDownloaded(info)) return;
            }

            _blockingInProgress = true;
            string targetPath;
            if (info.InProject)
            {
                targetPath = info.ProjectPath;
            }
            else
            {
                targetPath = await Assets.EnsureMaterialized(info);
                if (info.Id == 0) _requireSearchUpdate = true; // was deleted
            }

            if (targetPath != null) EditorUtility.RevealInFinder(IOUtils.ToShortPath(targetPath));
            _blockingInProgress = false;
        }

        private async Task<string> CopyToAsync(AssetInfo info, string targetFolder, bool withDependencies = false, int scriptMode = 0, bool autoPing = true, bool fromDragDrop = false, bool reimport = false, bool addToScene = false, Vector3? worldPosition = null, Transform parentTransform = null)
        {
            if (_blockingInProgress) return null;

            _blockingInProgress = true;
            try
            {
                if (!info.IsDownloaded && !info.IsMaterialized)
                {
                    if (!await EnsureDownloaded(info)) return null;
                }

                AssetImportResult result = await Assets.CopyToWithResult(
                    info,
                    targetFolder,
                    withDependencies,
                    scriptMode,
                    fromDragDrop,
                    false,
                    reimport,
                    false,
                    false);
                string mainFile = result.ImportedPath;
                RefreshImportedAssetStates(new[] {info}, result.DisplacedGuids);
                ShowImportCollisionSummary(result.Collisions);
                if (mainFile != null)
                {
                    if (addToScene && AssetUtils.CanAddToScene(mainFile)) // auto ping would remove selection otherwise
                    {
                        if (worldPosition.HasValue)
                        {
                            AssetUtils.AddToScene(mainFile, worldPosition.Value, parentTransform);
                        }
                        else
                        {
                            AssetUtils.AddToScene(mainFile);
                        }
                    }
                    else
                    {
                        if (autoPing && AI.Config.pingImported) PingAsset(new AssetInfo().WithProjectPath(mainFile));
                    }
                }

                return mainFile;
            }
            finally
            {
                _blockingInProgress = false;
            }
        }

        private void RefreshImportedAssetStates(IEnumerable<AssetInfo> importedItems, IEnumerable<string> displacedGuids)
        {
            List<AssetInfo> imported = importedItems?.Where(item => item != null).ToList() ?? new List<AssetInfo>();
            HashSet<string> displaced = new HashSet<string>(
                displacedGuids?.Where(guid => !string.IsNullOrEmpty(guid)) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> importedPaths = imported
                .Where(item => !string.IsNullOrEmpty(item.Guid) && !string.IsNullOrEmpty(item.ProjectPath))
                .GroupBy(item => item.Guid, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last().ProjectPath, StringComparer.OrdinalIgnoreCase);

            if (_filteredFiles != null)
            {
                foreach (AssetInfo item in _filteredFiles)
                {
                    if (item == null || string.IsNullOrEmpty(item.Guid)) continue;
                    if (displaced.Contains(item.Guid))
                    {
                        item.CheckIfInProject();
                    }
                    else if (importedPaths.TryGetValue(item.Guid, out string projectPath))
                    {
                        item.ProjectPath = projectPath;
                    }
                }
            }

            if (SGrid.selectionItems != null) SGrid.SetBulkSelection(SGrid.selectionItems);
            RefreshNativeSearchGridView();
            _nativeSearchTreeAdapter?.RepaintCells();
            ScheduleNativeSearchInspectorRebuild();
            _needsRepaint = true;
        }

        private static void ShowImportCollisionSummary(IReadOnlyList<AssetImportCollision> collisions)
        {
            if (collisions == null || collisions.Count == 0) return;

            string message = Assets.FormatImportCollisionSummary(collisions) +
                "\n\nChange Settings > Import > Filename Conflicts to auto-rename or overwrite conflicting files.";
            EditorUtility.DisplayDialog("Import Conflicts", message, "OK");
        }

        private void SetPage(int newPage)
        {
            SetPage(newPage, false);
        }

        private void SetPage(int newPage, bool ignoreExcludedExtensions)
        {
            newPage = Mathf.Clamp(newPage, 1, _pageCount);
            if (newPage != _curPage)
            {
                _curPage = newPage;
                SGrid.DeselectAll();
                _searchScrollPos = Vector2.zero;
                if (_curPage > 0)
                {
                    if (_inMemoryMode == InMemoryModeState.Active)
                    {
                        UpdateFilteredFiles();

                        if (_filteredFiles.Count > 0)
                        {
                            SGrid.LimitSelection(_filteredFiles.Count);
                            _selectedEntry = _filteredFiles[SGrid.selectionTile];
                        }
                        _requireSearchSelectionUpdate = true;
                        StopAnimation();
                    }
                    else
                    {
                        PerformSearch(true, ignoreExcludedExtensions);
                    }
                }
            }
        }

        private void UpdateFilteredFiles()
        {
            StopSearchPreviewLoading();
            ClearFilePreviewCache();

            if (_inMemoryMode != InMemoryModeState.None)
            {
                int maxResults = GetMaxResults();
                _filteredFiles = BuildInMemoryFilteredPage(_files, _curPage, maxResults, out _resultCount);
                _pageCount = AssetUtils.GetPageCount(_resultCount, maxResults);
                if (_curPage > _pageCount)
                {
                    _curPage = 1;
                    _filteredFiles = BuildInMemoryFilteredPage(_files, _curPage, maxResults, out _resultCount);
                }
            }
            else
            {
                _filteredFiles = _files ?? new List<AssetInfo>();
                _pageCount = AssetUtils.GetPageCount(_resultCount, GetMaxResults());
            }

            DisposeSearchResultTextures();
            SGrid.ResetPreviews(_filteredFiles.Count);

            SGrid.Init(_assets, _filteredFiles, CalculateSearchBulkSelection);

            // Update tree model for list view
            UpdateSearchTreeModel();

            UpdateSearchPreviews();
        }

        private List<AssetInfo> BuildInMemoryFilteredPage(List<AssetInfo> source, int currentPage, int maxResults, out int resultCount)
        {
            resultCount = 0;
            if (source == null || source.Count == 0) return new List<AssetInfo>();

            int skip = (Math.Max(1, currentPage) - 1) * maxResults;
            int capacity = Math.Min(maxResults, source.Count);
            List<AssetInfo> result = new List<AssetInfo>(capacity);

            for (int i = 0; i < source.Count; i++)
            {
                AssetInfo file = source[i];
                if (!MatchesInMemorySearch(file, _searchPhraseInMemory)) continue;

                if (resultCount >= skip && result.Count < maxResults)
                {
                    result.Add(file);
                }
                resultCount++;
            }

            return result;
        }

        private static readonly char[] InMemorySearchSeparators = {' '};

        private bool MatchesInMemorySearch(AssetInfo file, string searchPhrase)
        {
            if (file == null) return false;
            if (string.IsNullOrWhiteSpace(searchPhrase)) return true;

            if (searchPhrase.StartsWith("~"))
            {
                string term = searchPhrase.Substring(1);
                return AnyConfiguredSearchFieldContains(file, term);
            }

            string[] fuzzyWords = searchPhrase.Split(InMemorySearchSeparators, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < fuzzyWords.Length; i++)
            {
                string word = fuzzyWords[i].Trim();
                if (string.IsNullOrWhiteSpace(word)) continue;

                bool isNeg = word.StartsWith("-");
                string term = isNeg || word.StartsWith("+") ? word.Substring(1) : word;
                if (string.IsNullOrWhiteSpace(term)) continue;

                if (isNeg)
                {
                    if (!NoConfiguredSearchFieldContains(file, term)) return false;
                }
                else if (!AnyConfiguredSearchFieldContains(file, term))
                {
                    return false;
                }
            }

            return true;
        }

        private bool AnyConfiguredSearchFieldContains(AssetInfo file, string term)
        {
            if (term == null) return false;

            switch (AI.Config.searchField)
            {
                case 0:
                    if (file.Path?.Contains(term, StringComparison.OrdinalIgnoreCase) == true) return true;
                    break;

                case 1:
                    if (file.FileName?.Contains(term, StringComparison.OrdinalIgnoreCase) == true) return true;
                    break;
            }

            if (AI.Config.searchAICaptions && AI.Actions.AICaptionsEnabled && file.AICaption?.Contains(term, StringComparison.OrdinalIgnoreCase) == true) return true;
            if (AI.Config.searchPackageNames && file.DisplayName?.Contains(term, StringComparison.OrdinalIgnoreCase) == true) return true;

            return false;
        }

        private bool NoConfiguredSearchFieldContains(AssetInfo file, string term)
        {
            return !AnyConfiguredSearchFieldContains(file, term);
        }

        private string GetSearchTileText(AssetInfo file, out bool isPath)
        {
            isPath = false;
            if (file == null) return "";

            string text = "";
            int tileTextToUse = AI.Config.tileText;
            if (tileTextToUse == 6 && string.IsNullOrEmpty(file.AICaption))
            {
                tileTextToUse = 4;
            }
            if (tileTextToUse == 0)
            {
                if (AI.Config.searchTileSize < 70)
                {
                    tileTextToUse = 1;
                }
                else if (AI.Config.searchTileSize < 90)
                {
                    tileTextToUse = 5;
                }
                else if (AI.Config.searchTileSize < 150)
                {
                    tileTextToUse = 4;
                }
                else
                {
                    tileTextToUse = 3;
                }
            }
            switch (tileTextToUse)
            {
                case 3:
                    text = file.ShortPath;
                    isPath = !string.IsNullOrWhiteSpace(text);
                    break;

                case 4:
                    text = file.FileName;
                    break;

                case 5:
                    text = Path.GetFileNameWithoutExtension(file.FileName);
                    break;

                case 6:
                    text = file.AICaption;
                    break;
            }
            return NormalizeSearchTileText(text);
        }

        private static string NormalizeSearchTileText(string text)
        {
            return text == null ? "" : text.Replace('/', Path.DirectorySeparatorChar);
        }

        private void UpdateSearchTreeModel()
        {
            // Create root element
            AssetInfo root = new AssetInfo();
            root.TreeId = -1;
            root.Depth = -1;
            root.TreeName = "Root";

            // Set tree properties for each file
            int id = 0;
            if (_filteredFiles == null) _filteredFiles = new List<AssetInfo>();
            foreach (AssetInfo file in _filteredFiles)
            {
                file.TreeId = id++;
                file.Depth = 0;
                file.TreeName = file.FileName;
            }

            // Create model with root + files
            List<AssetInfo> treeData = new List<AssetInfo>(_filteredFiles.Count + 1) {root};
            treeData.AddRange(_filteredFiles);

            _searchTreeModel = new TreeModel<AssetInfo>(treeData);
            _nativeSearchSelection.Clear();
            QueueNativeSearchTreeRefresh(Array.Empty<int>(), false);
        }

        private bool IsFilterApplicable(string filterName)
        {
            return AssetSearch.IsFilterApplicable(filterName, GetRawSearchType());
        }

        private string GetRawSearchType()
        {
            if (_types == null || _types.Length == 0) return null;

            int searchType = _fixedSearchTypeIdx >= 0 ? _fixedSearchTypeIdx : AI.Config.searchType;
            return searchType > 0 && _types.Length > searchType ? _types[searchType] : null;
        }

        private int GetSearchTypeIndex(string rawSearchType)
        {
            if (string.IsNullOrWhiteSpace(rawSearchType)) return 0;

            int exactIndex = Array.FindIndex(_types, type => string.Equals(type, rawSearchType, StringComparison.OrdinalIgnoreCase));
            if (exactIndex >= 0) return exactIndex;

            string leafType = rawSearchType.Split('/').LastOrDefault();
            if (string.IsNullOrWhiteSpace(leafType)) return 0;

            int leafIndex = Array.FindIndex(_types, type => string.Equals(type.Split('/').LastOrDefault(), leafType, StringComparison.OrdinalIgnoreCase));
            return leafIndex >= 0 ? leafIndex : 0;
        }

        private void SetCustomSearchTypeExtensions(string value)
        {
            string normalizedValue = Assets.NormalizeCustomSearchTypeExtensions(value);
            if (string.Equals(AI.Config.customSearchTypeExtensions, normalizedValue, StringComparison.Ordinal)) return;

            string rawSearchType = GetRawSearchType();
            AI.Config.customSearchTypeExtensions = normalizedValue;
            _types = Assets.LoadTypes();
            AI.Config.searchType = GetSearchTypeIndex(rawSearchType);
            _cachedProjectSearchKey = null;
            _curPage = 1;
            _requireSearchUpdate = true;
            AI.SaveConfig();
            Repaint();
        }

        private int GetMaxResults()
        {
            // Validate index bounds to prevent IndexOutOfRangeException from corrupted/outdated settings
            if (AI.Config.maxResults < 0 || AI.Config.maxResults >= _resultSizes.Length)
            {
                AI.Config.maxResults = 5; // Default: "100" results
            }

            string selectedSize = _resultSizes[AI.Config.maxResults];
            int.TryParse(selectedSize, out int maxResults);
            if (maxResults <= 0 || maxResults > AI.Config.maxResultsLimit) maxResults = AI.Config.maxResultsLimit;

            return maxResults;
        }

        private void PerformSearch(bool keepPage = false, bool ignoreExcludedExtensions = false)
        {
            if (AI.DEBUG_MODE) Debug.LogWarning("Perform Search");

            // Detect variables immediately before search if detection is pending
            if (_nextVariableDetectionTime > 0)
            {
                _nextVariableDetectionTime = 0;
                DetectVariablesInSearchPhrase();
            }

            _requireSearchUpdate = false;
            _searchHandlerAdded = false;
            _keepSearchResultPage = true;
            StopSearchPreviewLoading();
            StopAnimation();

            // check if something was searched for actually, good for reducing initial load time if user is not interested in seeing full catalog
            if (!SearchWithoutInput())
            {
                if (!IsSearchFilterActive() && string.IsNullOrWhiteSpace(_searchPhrase))
                {
                    _resultCount = 0;
                    _pageCount = 0;
                    _curPage = 1;
                    _filteredFiles = new List<AssetInfo>();
                    SGrid.ResetPreviews(0);
                    ClearFilePreviewCache();
                    return;
                }
            }

            // use shared AssetSearch to execute search logic once
            int lastCount = _resultCount;
            int maxResults = GetMaxResults();

            // Build variables dictionary for search execution
            Dictionary<string, string> searchVariables = null;
            if (_hasSearchVariables && _searchVariables.Count > 0)
            {
                searchVariables = new Dictionary<string, string>();
                foreach (KeyValuePair<string, SearchVariable> kvp in _searchVariables)
                {
                    searchVariables[kvp.Key] = kvp.Value.currentValue ?? kvp.Value.defaultValue ?? "";
                }
            }

            AssetSearch.Options opt = new AssetSearch.Options
            {
                SearchPhrase = _searchPhrase,
                SearchVariables = searchVariables,
                SelectedPackageSRPs = _selectedPackageSRPs,
                SelectedPriceOption = _selectedPriceOption,
                SearchPrice = _searchPrice,
                SearchWidth = _searchWidth,
                CheckMaxWidth = _checkMaxWidth,
                SearchHeight = _searchHeight,
                CheckMaxHeight = _checkMaxHeight,
                SearchLength = _searchLength,
                CheckMaxLength = _checkMaxLength,
                SearchSize = _searchSize,
                CheckMaxSize = _checkMaxSize,
                SearchVertexCount = _searchVertexCount,
                CheckMaxVertexCount = _checkMaxVertexCount,
                SelectedPackageTag = _selectedPackageTag,
                SelectedFileTag = _selectedFileTag,
                TagNames = _tagNames,
                Tags = _tags,
                SelectedPackageTypes = _selectedPackageTypes,
                SelectedPublisher = _selectedPublisher,
                PublisherNames = _publisherNames,
                SelectedAsset = _selectedAsset,
                SelectedAssetFileId = _pendingSearchNavigationTarget?.Id > 0 ? _pendingSearchNavigationTarget.Id : 0,
                AssetNames = _assetNames,
                SelectedCategory = _selectedCategory,
                CategoryNames = _categoryNames,
                SelectedColorOption = _selectedColorOption,
                SelectedColor = _selectedColor,
                SelectedImageType = _selectedImageType,
                ImageTypeOptions = _imageTypeOptions,
                SelectedPreviewFilter = AI.Config.previewVisibility,
                SelectedHiddenFilter = _selectedHiddenFilter,
                RawSearchType = GetRawSearchType(),
                IgnoreExcludedExtensions = ignoreExcludedExtensions,
                CurrentPage = _curPage,
                MaxResults = maxResults,
                InMemory = _inMemoryMode == InMemoryModeState.None ? AssetSearch.InMemoryMode.None : (_inMemoryMode == InMemoryModeState.Init ? AssetSearch.InMemoryMode.Init : AssetSearch.InMemoryMode.Active),
                AllAssets = _assets
            };

            SearchScope searchScope = GetConfiguredSearchScope();

            // Invalidate cached metadata for assets that were reimported
            if (AssetOriginPostprocessor.HasChanges)
            {
                ProjectMetadataCache.Invalidate(AssetOriginPostprocessor.ConsumeChangedGuids());
                _cachedProjectSearchKey = null;
                _cachedProjectFiles = null;
                _cachedProjectOnlyFiles = null;
            }

            if (SearchScopeModel.UsesIndexSearch(searchScope))
            {
                // All/Index: run indexed database search first, then optionally merge project results.
                AssetSearch.Result res = AssetSearch.Execute(opt);
                _searchError = res.Error;
                int indexCount = res.ResultCount;
                _originalResultCount = res.OriginalResultCount;
                _files = res.Files;
                if (_inMemoryMode != InMemoryModeState.None && res.InMemory == AssetSearch.InMemoryMode.None) _inMemoryMode = InMemoryModeState.None;
                if (_inMemoryMode == InMemoryModeState.Init) _inMemoryMode = InMemoryModeState.Active;

                // Skip project search when index-only filters are active that project results cannot satisfy
                bool hasIndexOnlyFilters = IsIndexOnlyFilterActive();
                if (SearchScopeModel.UsesProjectSearch(searchScope) && !hasIndexOnlyFilters)
                {
                    // Build cache key from project search parameters
                    string projectSearchKey = GetProjectSearchCacheKey(ignoreExcludedExtensions);

                    // Reuse cached project results if search parameters haven't changed (e.g. page navigation only)
                    if (_cachedProjectFiles == null || _cachedProjectSearchKey != projectSearchKey)
                    {
                        // Project search
                        ProjectAssetSearch.Options projOpt = CreateProjectSearchOptions(ignoreExcludedExtensions);
                        ProjectAssetSearch.Result projRes = ProjectAssetSearch.Execute(projOpt);

                        _cachedProjectFiles = projRes.Files;
                        _cachedProjectOnlyFiles = null;
                        _cachedProjectSearchKey = projectSearchKey;
                    }

                    // Derive index-excluded subset (lazy, since FindIndexedGuids depends on current index query)
                    if (_cachedProjectOnlyFiles == null)
                    {
                        // Find which project GUIDs already exist in the full index results (not just the current page)
                        List<string> projectGuids = _cachedProjectFiles
                            .Where(f => !string.IsNullOrEmpty(f.Guid))
                            .Select(f => f.Guid)
                            .ToList();
                        HashSet<string> indexedGuids = AssetSearch.FindIndexedGuids(opt, projectGuids);

                        // Filter to project-only files (not in index or no GUID)
                        _cachedProjectOnlyFiles = _cachedProjectFiles
                            .Where(f => string.IsNullOrEmpty(f.Guid) || !indexedGuids.Contains(f.Guid))
                            .ToList();
                    }

                    List<AssetInfo> projectOnlyFiles = _cachedProjectOnlyFiles;

                    if (_inMemoryMode != InMemoryModeState.None)
                    {
                        // In-memory mode: merge everything, UpdateFilteredFiles handles pagination
                        _files.AddRange(projectOnlyFiles);
                        _resultCount = _files.Count;
                        _originalResultCount = _resultCount;
                    }
                    else
                    {
                        // DB pagination: index results first, project-only files appended after
                        _resultCount = indexCount + projectOnlyFiles.Count;
                        _originalResultCount = _resultCount;

                        int offset = (opt.CurrentPage - 1) * maxResults;
                        if (offset < indexCount)
                        {
                            // Page starts within index results
                            int remainingSlots = maxResults - _files.Count;
                            if (remainingSlots > 0 && projectOnlyFiles.Count > 0)
                            {
                                // Last index page: fill remaining slots with project-only files
                                _files.AddRange(projectOnlyFiles.Take(remainingSlots));
                            }
                        }
                        else
                        {
                            // Page is entirely project-only files
                            int projectOffset = offset - indexCount;
                            _files = projectOnlyFiles.Skip(projectOffset).Take(maxResults).ToList();
                        }
                    }
                }
                else
                {
                    _resultCount = indexCount;
                }
            }
            else
            {
                // Project only
                string projectSearchKey = GetProjectSearchCacheKey(ignoreExcludedExtensions);
                if (_cachedProjectFiles == null || _cachedProjectSearchKey != projectSearchKey)
                {
                    ProjectAssetSearch.Options projOpt = CreateProjectSearchOptions(ignoreExcludedExtensions);
                    ProjectAssetSearch.Result projRes = ProjectAssetSearch.Execute(projOpt);

                    _cachedProjectFiles = projRes.Files;
                    _cachedProjectOnlyFiles = null;
                    _cachedProjectSearchKey = projectSearchKey;
                }

                _files = _cachedProjectFiles;
                _resultCount = _cachedProjectFiles.Count;
                _originalResultCount = _resultCount;
                _searchError = null;
            }

            _requireHierarchyRebuild = true;

            // pagination
            UpdateFilteredFiles();
            if (!keepPage && lastCount != _resultCount)
            {
                SetPage(1, ignoreExcludedExtensions);
            }
            else
            {
                SetPage(_curPage, ignoreExcludedExtensions);
            }
            _searchDone = true;
            ApplyPendingSearchNavigation();

            // Trigger visible animations update after search completes
            if (AI.Config.playVisibleSearchAnimations)
            {
                TriggerVisibleAnimationsUpdate();
            }
        }

        private void StopSearchPreviewLoading()
        {
            CancellationTokenSource previous = Interlocked.Exchange(ref _textureLoading, new CancellationTokenSource());
            CancelAndDispose(ref previous);
        }

        private void UpdateSearchPreviews()
        {
            StopSearchPreviewLoading();
            _searchPreviewSessionInitialized = true;
            LoadTextures(false, _textureLoading.Token); // TODO: should be true once pages endless scrolling is supported
        }

        private async void LoadAnimTexture(AssetInfo info)
        {
            _animatedTileIndex = SGrid.selectionTile;
            _animatedEntry = info;

            if (_animationPlayer != null)
            {
                _animationPlayer.Dispose();
                _animationPlayer = null;
            }

            string animPreviewFile = info.GetPreviewFile(Paths.GetPreviewFolder(), true);
            if (!File.Exists(animPreviewFile)) return;

            _animationPlayer = new AnimationPlayer(info.Guid);
            bool success = await _animationPlayer.LoadAnimation(info, Paths.GetPreviewFolder());
            if (!success)
            {
                _animationPlayer?.Dispose();
                _animationPlayer = null;
            }
        }

        /// <summary>
        /// Gets the range of tile indices that are currently visible in the scroll view.
        /// </summary>
        private void GetVisibleTileRange(float viewHeight, out int firstIndex, out int lastIndex)
        {
            firstIndex = 0;
            lastIndex = -1;

            if (!SGrid.HasPreviewSlots || SGrid.PreviewCount == 0) return;
            if (SGrid.ActualTileHeight <= 0 || SGrid.CellsPerRow <= 0) return;

            int firstVisibleRow = Mathf.FloorToInt(_searchScrollPos.y / SGrid.ActualTileHeight);
            int lastVisibleRow = Mathf.CeilToInt((_searchScrollPos.y + viewHeight) / SGrid.ActualTileHeight);

            // Add buffer rows for smoother loading
            firstVisibleRow = Mathf.Max(0, firstVisibleRow - 1);
            lastVisibleRow = lastVisibleRow + 1;

            firstIndex = firstVisibleRow * SGrid.CellsPerRow;
            lastIndex = Mathf.Min((lastVisibleRow + 1) * SGrid.CellsPerRow - 1, SGrid.PreviewCount - 1);
        }

        /// <summary>
        /// Updates visible animations when scroll position or viewport changes.
        /// </summary>
        private void UpdateVisibleAnimations(float viewHeight)
        {
            if (!AI.Config.playVisibleSearchAnimations) return;
            if (_filteredFiles == null || !SGrid.HasPreviewSlots) return;

            GetVisibleTileRange(viewHeight, out int firstVisible, out int lastVisible);
            if (lastVisible < 0) return;

            // Update visibility - dispose animations that scrolled out of view
            _visibleAnimations.UpdateVisibility(
                tileIndex => tileIndex >= firstVisible && tileIndex <= lastVisible,
                tileIndex => RestoreStaticPreviewAsync(tileIndex)
            );

            // Clear and rebuild the queue with visible tiles that need loading
            _visibleAnimations.ClearQueue();

            // Calculate how many new animations we can queue
            int availableSlots = AI.Config.maxVisibleSearchAnimations - _visibleAnimations.TotalActiveCount;

            // Queue animations for visible tiles that aren't loaded or loading
            for (int i = firstVisible; i <= lastVisible && availableSlots > 0; i++)
            {
                if (_visibleAnimations.IsActive(i)) continue;

                // Check if this tile has an animated preview
                if (i >= _filteredFiles.Count) continue;
                AssetInfo info = _filteredFiles[i];
                if (info == null) continue;

                if (!AnimationPlaybackManager<int>.HasAnimatedPreview(info)) continue;

                // Queue for loading
                if (_visibleAnimations.QueueAnimation(i, info))
                {
                    availableSlots--;
                }
            }

            // Process animation load queue
            _visibleAnimations.ProcessQueue();
        }

        private async void RestoreStaticPreviewAsync(int tileIndex)
        {
            if (_filteredFiles == null || tileIndex >= _filteredFiles.Count) return;
            if (!SGrid.HasPreviewSlots || tileIndex >= SGrid.PreviewCount) return;

            AssetInfo info = _filteredFiles[tileIndex];
            if (info == null) return;

            string previewFile = info.GetPreviewFile(Paths.GetPreviewFolder(), false);
            if (string.IsNullOrEmpty(previewFile) || !File.Exists(previewFile)) return;

            try
            {
                Texture2D staticTexture = await AssetUtils.LoadLocalTexture(
                    previewFile,
                    false,
                    (AI.Config.upscalePreviews && !AI.Config.upscaleLossless) ? AI.Config.upscaleSize : 0
                );

                if (staticTexture != null && SGrid.HasPreviewSlots && tileIndex < SGrid.PreviewCount)
                {
                    if (AI.Config.tileCornerRadius > 0)
                    {
                        Texture2D roundedTexture = staticTexture.WithRoundedCorners(AI.Config.tileCornerRadius);
                        ApplySearchGridImage(tileIndex, roundedTexture);
                        DestroyImmediate(staticTexture);
                    }
                    else
                    {
                        ApplySearchGridImage(tileIndex, staticTexture);
                    }
                }
            }
            catch
            {
                // Ignore errors during preview restoration
            }
        }

        private void DisposeAllVisibleAnimations(bool restoreStaticPreviews = false)
        {
            List<int> affectedTiles = _visibleAnimations.DisposeAll();

            // Restore static previews for all tiles that were animated
            if (restoreStaticPreviews && affectedTiles.Count > 0)
            {
                RestoreStaticPreviews(affectedTiles);
            }
        }

        private async void RestoreStaticPreviews(List<int> tileIndices)
        {
            if (_filteredFiles == null || !SGrid.HasPreviewSlots) return;

            foreach (int tileIndex in tileIndices)
            {
                if (tileIndex < 0 || tileIndex >= _filteredFiles.Count) continue;
                if (tileIndex >= SGrid.PreviewCount) continue;

                AssetInfo info = _filteredFiles[tileIndex];
                if (info == null) continue;

                string previewFile = info.GetPreviewFile(Paths.GetPreviewFolder(), false);
                if (string.IsNullOrEmpty(previewFile) || !File.Exists(previewFile)) continue;

                try
                {
                    Texture2D staticTexture = await AssetUtils.LoadLocalTexture(
                        previewFile,
                        false,
                        (AI.Config.upscalePreviews && !AI.Config.upscaleLossless) ? AI.Config.upscaleSize : 0
                    );

                    if (staticTexture != null && SGrid.HasPreviewSlots && tileIndex < SGrid.PreviewCount)
                    {
                        if (AI.Config.tileCornerRadius > 0)
                        {
                            Texture2D roundedTexture = staticTexture.WithRoundedCorners(AI.Config.tileCornerRadius);
                            ApplySearchGridImage(tileIndex, roundedTexture);
                            DestroyImmediate(staticTexture);
                        }
                        else
                        {
                            ApplySearchGridImage(tileIndex, staticTexture);
                        }
                    }
                }
                catch
                {
                    // Ignore errors during preview restoration
                }
            }
        }

        private void TriggerVisibleAnimationsUpdate()
        {
            // Force update on next frames by invalidating saved scroll position
            // and setting retry counter to try for several frames until grid dimensions are ready
            _lastSearchScrollPos = new Vector2(-1, -1);
            _lastViewHeight = -1;
            _visibleAnimationTriggerFrames = 5; // Try for 5 frames to account for grid layout delay
        }


        private async void LoadTextures(bool firstPageOnly, CancellationToken ct)
        {
            int chunkSize = AI.Config.previewChunkSize;

            List<AssetInfo> files;
            if (_filteredFiles == null)
            {
                files = new List<AssetInfo>();
            }
            else if (firstPageOnly)
            {
                int count = Math.Min(20 * 8, _filteredFiles.Count);
                files = _filteredFiles.GetRange(0, count);
            }
            else
            {
                files = new List<AssetInfo>(_filteredFiles);
            }

            for (int i = 0; i < files.Count; i += chunkSize)
            {
                try
                {
                    if (ct.IsCancellationRequested) return;

                    List<Task> tasks = new List<Task>();

                    int chunkEnd = Math.Min(i + chunkSize, files.Count);
                    for (int idx = i; idx < chunkEnd; idx++)
                    {
                        if (ct.IsCancellationRequested) return;

                        int localIdx = idx; // capture value
                        AssetInfo info = files.ElementAt(localIdx);

                        tasks.Add(ProcessAssetInfoAsync(info, localIdx, ct));
                    }

                    await Task.WhenAll(tasks).WithCancellation(ct);
                }
                catch (OperationCanceledException)
                {
                    // Task was canceled, exit the loop
                    return;
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error processing asset: {e}");
                }
            }

            if (PendingVirtualPreviews.Count > 0)
            {
                RetryPendingVirtualPreviews(ct);
            }
        }

        private async Task ProcessAssetInfoAsync(AssetInfo info, int idx, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // Virtual (in-project) assets use Unity's preview system instead of indexed previews
            if (info.IsVirtual)
            {
                await LoadVirtualAssetPreview(info, idx, ct);
                return;
            }

            string previewFile = null;
            if (info.HasPreview(true)) previewFile = AssetImporter.ValidatePreviewFile(info, Paths.GetPreviewFolder());
            if (previewFile == null || !info.HasPreview(true))
            {
                if (!AI.Config.showIconsForMissingPreviews) return;

                // check if well-known extension
                if (_staticPreviews.TryGetValue(info.Type, out string preview))
                {
                    ApplySearchGridImage(idx, EditorGUIUtility.IconContent(preview).image);
                }
                else
                {
                    ApplySearchGridImage(idx, EditorGUIUtility.IconContent("d_DefaultAsset Icon").image);
                }
                return;
            }

            Texture2D texture = await AssetUtils.LoadLocalTexture(
                previewFile,
                false,
                // _inMemoryMode != InMemoryModeState.None,
                (AI.Config.upscalePreviews && !AI.Config.upscaleLossless) ? AI.Config.upscaleSize : 0
            );
            ct.ThrowIfCancellationRequested();

            if (texture == null)
            {
                info.PreviewState = AssetFile.PreviewOptions.None;
                if (!info.IsVirtual) DBAdapter.DB.Execute("update AssetFile set PreviewState=? where Id=?", info.PreviewState, info.Id);
            }
            else if (SGrid.HasPreviewSlots && idx >= 0 && SGrid.PreviewCount > idx)
            {
                Texture2D textureToUse = texture;
                if (AI.Config.tileCornerRadius > 0)
                {
                    textureToUse = texture.WithRoundedCorners(AI.Config.tileCornerRadius);
                    // Destroy the original texture since we're using the rounded version
                    DestroyImmediate(texture);
                }

                // Store in file preview cache for TreeView access
                FilePreviewCache[info.Id] = textureToUse;
                ApplySearchGridImage(idx, textureToUse);
            }
        }

        private Texture2D CopyPreviewTexture(Texture2D preview)
        {
            RenderTexture rt = RenderTexture.GetTemporary(preview.width, preview.height, 0, RenderTextureFormat.ARGB32);
            UnityEngine.Graphics.Blit(preview, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D copy = new Texture2D(preview.width, preview.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, preview.width, preview.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            if (AI.Config.tileCornerRadius > 0)
            {
                Texture2D rounded = copy.WithRoundedCorners(AI.Config.tileCornerRadius);
                DestroyImmediate(copy);
                copy = rounded;
            }

            return copy;
        }

        private async Task LoadVirtualAssetPreview(AssetInfo info, int idx, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(info.ProjectPath) || !SGrid.HasPreviewSlots || idx >= SGrid.PreviewCount) return;

            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(info.ProjectPath);
            if (asset == null) return;

            // Try full preview first, fall back to thumbnail
            Texture2D preview = AssetPreview.GetAssetPreview(asset);

            // AssetPreview.GetAssetPreview can return null if not ready yet - retry a few times
            int retries = 0;
            while (preview == null && retries < 10 && !ct.IsCancellationRequested)
            {
                await Task.Delay(50, ct);
                preview = AssetPreview.GetAssetPreview(asset);
                retries++;
            }

            bool usedFallback = false;
            if (preview == null)
            {
                preview = AssetPreview.GetMiniThumbnail(asset);
                usedFallback = preview != null;
            }

            if (preview != null && SGrid.HasPreviewSlots && idx < SGrid.PreviewCount)
            {
                Texture2D copy = CopyPreviewTexture(preview);

                FilePreviewCache[info.Id] = copy;
                ApplySearchGridImage(idx, copy);

                if (usedFallback)
                {
                    PendingVirtualPreviews[idx] = info;
                }
                else
                {
                    info.PreviewState = AssetFile.PreviewOptions.Provided;
                }
            }
            else if (AI.Config.showIconsForMissingPreviews && SGrid.HasPreviewSlots && idx < SGrid.PreviewCount)
            {
                if (_staticPreviews.TryGetValue(info.Type, out string iconName))
                {
                    ApplySearchGridImage(idx, EditorGUIUtility.IconContent(iconName).image);
                }
                else
                {
                    ApplySearchGridImage(idx, EditorGUIUtility.IconContent("d_DefaultAsset Icon").image);
                }
                PendingVirtualPreviews[idx] = info;
            }
        }

        private async void RetryPendingVirtualPreviews(CancellationToken ct)
        {
            if (_virtualPreviewRetryRunning) return;
            _virtualPreviewRetryRunning = true;

            try
            {
                int maxIterations = 30;
                for (int iteration = 0; iteration < maxIterations && PendingVirtualPreviews.Count > 0; iteration++)
                {
                    await Task.Delay(1000, ct);
                    if (ct.IsCancellationRequested) return;

                    List<int> resolved = new List<int>();
                    foreach (KeyValuePair<int, AssetInfo> entry in PendingVirtualPreviews)
                    {
                        if (ct.IsCancellationRequested) return;

                        int idx = entry.Key;
                        AssetInfo info = entry.Value;

                        if (string.IsNullOrEmpty(info.ProjectPath) || !SGrid.HasPreviewSlots || idx >= SGrid.PreviewCount)
                        {
                            resolved.Add(idx);
                            continue;
                        }

                        Object asset = AssetDatabase.LoadAssetAtPath<Object>(info.ProjectPath);
                        if (asset == null)
                        {
                            resolved.Add(idx);
                            continue;
                        }

                        Texture2D preview = AssetPreview.GetAssetPreview(asset);
                        if (preview != null)
                        {
                            Texture2D copy = CopyPreviewTexture(preview);

                            // Destroy old cached texture
                            if (FilePreviewCache.TryGetValue(info.Id, out Texture2D oldTex) && oldTex != null)
                            {
                                DestroyImmediate(oldTex);
                            }

                            FilePreviewCache[info.Id] = copy;
                            if (SGrid.HasPreviewSlots && idx < SGrid.PreviewCount)
                            {
                                ApplySearchGridImage(idx, copy);
                            }
                            info.PreviewState = AssetFile.PreviewOptions.Provided;
                            resolved.Add(idx);
                        }
                        else if (!UnityEditorCompat.IsLoadingPreview(asset))
                        {
                            resolved.Add(idx);
                        }
                    }

                    foreach (int idx in resolved)
                    {
                        PendingVirtualPreviews.Remove(idx);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on new search
            }
            finally
            {
                _virtualPreviewRetryRunning = false;
            }
        }

        private void CalculateSearchBulkSelection()
        {
            _assetFileBulkTags.Clear();
            SGrid.selectionItems.ForEach(info => info.AssetTags?.ForEach(t =>
            {
                if (!_assetFileBulkTags.ContainsKey(t.Name)) _assetFileBulkTags.Add(t.Name, new Tuple<int, Color>(0, t.GetColor()));
                _assetFileBulkTags[t.Name] = new Tuple<int, Color>(_assetFileBulkTags[t.Name].Item1 + 1, _assetFileBulkTags[t.Name].Item2);
            }));
            _assetFileAMProjectCount = SGrid.selectionItems.Count(info => info.AssetSource == Asset.Source.AssetManager && string.IsNullOrEmpty(info.Location));
            _assetFileAMCollectionCount = SGrid.selectionItems.Count(info => info.AssetSource == Asset.Source.AssetManager && !string.IsNullOrEmpty(info.Location));
            _assetFileAICaptionCount = SGrid.selectionItems.Count(info => !string.IsNullOrWhiteSpace(info.AICaption));
        }

        public void OpenInSearch(AssetInfo info, bool force = false, bool showFilterTab = true, string searchPhrase = null)
        {
            if (info != null && info.Id <= 0) return;
            if (info != null && !force && info.FileCount <= 0) return;
            AssetInfo oldEntry = _selectedEntry;

            if (!EnsureSearchPackageIncluded(info)) return;
            ResetSearch(false, true);
            if (force) _selectedEntry = oldEntry;

            AI.Config.tab = 0;

            // Switch away from Project scope when drilling into a specific package since ProjectAssetSearch does not support metadata filters
            if (info != null && SearchScopeModel.IsProjectOnly(GetConfiguredSearchScope()))
            {
                AI.Config.searchScope = (int)SearchScope.All;
                AI.SaveConfig();
            }

            SetSearchAssetFilter(info);

            // Set custom search phrase if provided
            if (!string.IsNullOrEmpty(searchPhrase))
            {
                _searchPhrase = searchPhrase;
                _previousSearchPhrase = searchPhrase;
            }

            _curPage = 1;
            if (showFilterTab) _searchInspectorTab = 1;
            PerformSearch(); // search immediately as "search automatically" setting might be off
        }

        internal void OpenAssetFileInSearch(AssetFile file)
        {
            if (file == null) return;

            AssetFile target = file;
            if (target.Id > 0)
            {
                AssetFile indexedFile = DBAdapter.DB.Find<AssetFile>(target.Id);
                if (indexedFile != null) target = indexedFile;
            }
            else if (!string.IsNullOrEmpty(target.Guid))
            {
                AssetFile indexedFile = DBAdapter.DB.Find<AssetFile>(candidate => candidate.Guid == target.Guid);
                if (indexedFile != null) target = indexedFile;
            }

            if (!HasSearchNavigationIdentity(target))
            {
                ShowNotification(new GUIContent("Could not locate the selected file in Asset Inventory."));
                return;
            }

            AssetInfo package = null;
            if (target.Id > 0 && target.AssetId > 0)
            {
                package = _assets?.FirstOrDefault(asset => asset.AssetId == target.AssetId);
                if (package == null)
                {
                    Asset asset = DBAdapter.DB.Find<Asset>(target.AssetId);
                    if (asset != null) package = new AssetInfo(asset);
                }
            }

            if (!EnsureSearchPackageIncluded(package)) return;

            ResetSearch(false, false);
            _inMemoryMode = InMemoryModeState.None;
            _pendingSearchNavigationTarget = target;
            AI.Config.tab = 0;

            SearchScope searchScope = GetConfiguredSearchScope();
            bool needsIndex = target.Id > 0;
            bool needsProject = !needsIndex;
            if (needsIndex && !SearchScopeModel.UsesIndexSearch(searchScope))
            {
                AI.Config.searchScope = (int)SearchScope.All;
                AI.SaveConfig();
            }
            else if (needsProject && !SearchScopeModel.IsProjectOnly(searchScope))
            {
                AI.Config.searchScope = (int)SearchScope.Project;
                AI.SaveConfig();
            }

            SetSearchAssetFilter(package);
            if (target.Hidden) _selectedHiddenFilter = 1;

            string searchText = GetSearchNavigationTargetName(target);
            _searchPhrase = "~" + (searchText ?? string.Empty);
            _previousSearchPhrase = _searchPhrase;
            _curPage = 1;
            _searchInspectorTab = 0;
            PerformSearch(false, true);
            Show();
            Focus();
            Repaint();
        }

        internal static string GetSearchNavigationTargetName(AssetFile target)
        {
            if (target == null) return null;
            if (!string.IsNullOrWhiteSpace(target.FileName)) return target.FileName;

            string targetPath = !string.IsNullOrWhiteSpace(target.ProjectPath) ? target.ProjectPath : target.Path;
            string fileName = Path.GetFileName(targetPath);
            return !string.IsNullOrWhiteSpace(fileName) ? fileName : null;
        }

        internal static bool HasSearchNavigationIdentity(AssetFile target)
        {
            return target != null
                && (target.Id > 0
                    || !string.IsNullOrWhiteSpace(target.Guid)
                    || !string.IsNullOrWhiteSpace(target.ProjectPath)
                    || !string.IsNullOrWhiteSpace(target.Path));
        }

        internal static int FindSearchNavigationTargetIndex(IReadOnlyList<AssetInfo> results, AssetFile target)
        {
            if (results == null || target == null) return -1;

            if (target.Id > 0)
            {
                for (int i = 0; i < results.Count; i++)
                {
                    if (results[i]?.Id == target.Id) return i;
                }
            }

            if (!string.IsNullOrEmpty(target.Guid))
            {
                for (int i = 0; i < results.Count; i++)
                {
                    if (string.Equals(results[i]?.Guid, target.Guid, StringComparison.OrdinalIgnoreCase)) return i;
                }
            }

            string targetPath = !string.IsNullOrEmpty(target.ProjectPath) ? target.ProjectPath : target.Path;
            if (string.IsNullOrEmpty(targetPath)) return -1;
            for (int i = 0; i < results.Count; i++)
            {
                AssetInfo result = results[i];
                string resultPath = !string.IsNullOrEmpty(result?.ProjectPath) ? result.ProjectPath : result?.Path;
                if (string.Equals(resultPath, targetPath, StringComparison.OrdinalIgnoreCase)) return i;
            }

            return -1;
        }

        private void ApplyPendingSearchNavigation()
        {
            AssetFile target = _pendingSearchNavigationTarget;
            if (target == null) return;
            _pendingSearchNavigationTarget = null;

            int index = FindSearchNavigationTargetIndex(_filteredFiles, target);
            if (index < 0)
            {
                string targetName = GetSearchNavigationTargetName(target);
                string message = !string.IsNullOrWhiteSpace(targetName)
                    ? $"Could not find '{targetName}' in Asset Inventory."
                    : "Could not find the selected file in Asset Inventory.";
                ShowNotification(new GUIContent(message));
                return;
            }

            AssetInfo selected = _filteredFiles[index];
            _selectedEntry = selected;
            _searchInspectorTab = 0;
            SGrid.SetVisualSelectionIndices(new[] {index}, index);
            QueueNativeSearchTreeRefresh(new[] {selected.TreeId}, true);
            if (_nativeSearchGridView != null)
            {
                _nativeSearchGridView.SetSelection(new[] {index}, index);
                _nativeSearchGridView.ScrollToItem(index);
            }
            ScheduleNativeSearchInspectorRebuild();
            ScheduleNativeSearchSelectionHandling(false);
            RefreshNativeSearchNarrowDetailsAction();
            ShowNotification(new GUIContent($"Selected '{selected.FileName}'."));
        }

        private bool EnsureSearchPackageIncluded(AssetInfo info)
        {
            if (info == null || !info.Exclude) return true;
            if (!EditorUtility.DisplayDialog("Package is Excluded", "This package is currently excluded from the search. Should it be included again?", "Include Again", "Cancel"))
            {
                return false;
            }

            AI.SetAssetExclusion(info, false);
            ReloadLookups();
            return true;
        }

        private void SetSearchAssetFilter(AssetInfo info)
        {
            if (info == null)
            {
                _selectedAsset = 0;
                return;
            }

            string displayName = info.GetDisplayName().Replace("/", " ");
            if (info.SafeName == Asset.NONE)
            {
                _selectedAsset = 1;
            }
            else
            {
                _selectedAsset = Array.IndexOf(_assetNames, _assetNames.FirstOrDefault(assetName => assetName == displayName + $" [{info.AssetId}]"));
            }
            if (_selectedAsset < 0 && displayName.Length > 0)
            {
                _selectedAsset = Array.IndexOf(_assetNames, _assetNames.FirstOrDefault(assetName => assetName == displayName.Substring(0, 1) + "/" + displayName + $" [{info.AssetId}]"));
            }
            if (_selectedAsset < 0)
            {
                _selectedAsset = Array.IndexOf(_assetNames, _assetNames.FirstOrDefault(assetName => assetName.EndsWith(displayName + $" [{info.AssetId}]")));
            }

            if (info.AssetSource == Asset.Source.RegistryPackage && _selectedPackageTypes == 1) _selectedPackageTypes = 0;
        }

        private void ResetSearch(bool filterBarOnly, bool keepAssetType)
        {
            if (!filterBarOnly)
            {
                _searchPhrase = "";
                _previousSearchPhrase = "";
                if (!keepAssetType) AI.Config.searchType = 0;
            }

            _selectedEntry = null;
            _selectedAsset = 0;
            _selectedPackageTypes = 1;
            _selectedPackageSRPs = 1;
            _selectedPriceOption = 0;
            _searchPrice = 0f;
            _selectedImageType = 0;
            _selectedColorOption = 0;
            _selectedHiddenFilter = 0;
            _selectedColor = Color.clear;
            _selectedPackageTag = 0;
            _selectedFileTag = 0;
            _selectedPublisher = 0;
            _selectedCategory = 0;
            _searchHeight = "";
            _checkMaxHeight = false;
            _searchWidth = "";
            _checkMaxWidth = false;
            _searchLength = "";
            _checkMaxLength = false;
            _searchSize = "";
            _checkMaxSize = false;
            _searchVertexCount = "";
            _checkMaxVertexCount = false;

            // Clear active saved search when resetting
            _activeSavedSearchId = -1;
        }

        private async Task PerformCopyTo(AssetInfo info, string path, bool fromDragDrop = false, bool addToScene = false, Vector3? worldPosition = null, Transform parentTransform = null)
        {
            if (info.InProject && !addToScene) return;
            if (string.IsNullOrEmpty(path)) return;

            while (info.DependencyState == AssetInfo.DependencyStateOptions.Calculating) await Task.Yield();
            if (info.DependencyState == AssetInfo.DependencyStateOptions.Unknown) await CalculateDependencies(info);
            if (info.DependencySize > 0 && DependencyAnalysis.NeedsScan(info.Type))
            {
                await CopyToAsync(info, path, true, AI.Config.scriptImportMode, false, fromDragDrop, false, addToScene, worldPosition, parentTransform);
            }
            else
            {
                await CopyToAsync(info, path, false, 0, true, fromDragDrop, false, addToScene, worldPosition, parentTransform);
            }
        }

        private async Task RunDragImportOperation(List<AssetInfo> infos, Func<Task> operation)
        {
            if (infos == null || infos.Count == 0 || operation == null) return;
            if (_dragImportInProgress) return;

            BeginDragImportOperation(infos.Count);
            try
            {
                await operation();
            }
            catch (Exception e)
            {
                Debug.LogError($"Drag import failed: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                EndDragImportOperation();
            }
        }

        private void BeginDragImportOperation(int count)
        {
            _dragImportInProgress = true;
            _dragImportIndex = 0;
            _dragImportCount = Mathf.Max(1, count);
            _dragImportStartTime = EditorApplication.timeSinceStartup;
            _dragImportMessage = "Preparing import";
            _needsRepaint = true;
            RefreshNativeProgressOverlay();
        }

        private void SetDragImportItem(int index, AssetInfo info)
        {
            _dragImportIndex = Mathf.Clamp(index, 1, Mathf.Max(1, _dragImportCount));
            _dragImportMessage = info == null
                ? "Importing asset"
                : $"Importing {info.GetDisplayName()}";
            _needsRepaint = true;
            RefreshNativeProgressOverlay();
        }

        private void EndDragImportOperation()
        {
            _dragImportInProgress = false;
            _dragImportIndex = 0;
            _dragImportCount = 0;
            _dragImportMessage = null;
            _needsRepaint = true;
            RefreshNativeProgressOverlay();
        }

        private static List<AssetInfo> SnapshotDraggedAssetInfos()
        {
            List<AssetInfo> infos = DragAndDrop.GetGenericData("AssetInfo") as List<AssetInfo>;
            return infos == null ? null : infos.Where(info => info != null).ToList();
        }

        private void FinishDragDrop()
        {
            _dragging = false;
            _draggingPossible = false;
            GUIUtility.hotControl = 0;
            DeinitDragAndDrop();
        }

#if UNITY_6000_3_OR_NEWER
        private void InitDragAndDrop()
        {
            DragAndDrop.ProjectBrowserDropHandlerV2 dropHandler = OnProjectWindowDrop;
            if (!DragAndDrop.HasHandler("ProjectBrowser".GetHashCode(), dropHandler))
            {
                DragAndDrop.AddDropHandlerV2(dropHandler);
            }
        }

        private void DeinitDragAndDrop()
        {
            DragAndDrop.ProjectBrowserDropHandlerV2 dropHandler = OnProjectWindowDrop;
            if (DragAndDrop.HasHandler("ProjectBrowser".GetHashCode(), dropHandler))
            {
                DragAndDrop.RemoveDropHandlerV2(dropHandler);
            }
        }

        private DragAndDropVisualMode OnProjectWindowDrop(EntityId dragEntityId, string dropUponPath, bool perform)
        {
            return DoOnProjectWindowDrop(dropUponPath, perform);
        }

#else
        private void InitDragAndDrop()
        {
            DragAndDrop.ProjectBrowserDropHandler dropHandler = OnProjectWindowDrop;
            if (!DragAndDrop.HasHandler("ProjectBrowser".GetHashCode(), dropHandler))
            {
                DragAndDrop.AddDropHandler(dropHandler);
            }
        }

        private void DeinitDragAndDrop()
        {
            DragAndDrop.ProjectBrowserDropHandler dropHandler = OnProjectWindowDrop;
            if (DragAndDrop.HasHandler("ProjectBrowser".GetHashCode(), dropHandler))
            {
                DragAndDrop.RemoveDropHandler(dropHandler);
            }
        }

        private DragAndDropVisualMode OnProjectWindowDrop(int dragInstanceId, string dropUponPath, bool perform)
        {
            return DoOnProjectWindowDrop(dropUponPath, perform);
        }
#endif

        private DragAndDropVisualMode DoOnHierarchyDrop(Object dropTarget, Transform parentForDraggedObjects, bool perform)
        {
            List<AssetInfo> infos = SnapshotDraggedAssetInfos();
            if (infos == null || infos.Count == 0)
            {
                return DragAndDropVisualMode.None;
            }

            if (perform)
            {
                FinishDragDrop();

                // Use provided parent, or try to get GameObject from drop target
                Transform finalParent = parentForDraggedObjects;
                if (finalParent == null && dropTarget != null)
                {
                    GameObject targetGameObject = dropTarget as GameObject;
                    if (targetGameObject != null && targetGameObject.scene.IsValid())
                    {
                        finalParent = targetGameObject.transform;
                    }
                }

                // Determine world position: use parent's position if available, otherwise scene view pivot
                Vector3 worldPosition = Vector3.zero;
                if (finalParent != null)
                {
                    worldPosition = finalParent.position;
                }
                else
                {
                    SceneView sceneView = SceneView.lastActiveSceneView;
                    worldPosition = sceneView != null ? sceneView.pivot : Vector3.zero;
                }

                StartSceneDragImport(infos, worldPosition, finalParent);
                DragAndDrop.AcceptDrag();
            }

            return DragAndDropVisualMode.Copy;
        }

#if UNITY_6000_3_OR_NEWER
        private DragAndDropVisualMode OnHierarchyDrop(EntityId dropTargetEntityId, HierarchyDropFlags dropMode, Transform parentForDraggedObjects, bool perform)
        {
#if UNITY_6000_5_OR_NEWER
            Object dropTarget = dropTargetEntityId.IsValid() ? EditorUtility.EntityIdToObject(dropTargetEntityId) : null;
#else
            Object dropTarget = dropTargetEntityId.IsValid() ? EditorUtility.InstanceIDToObject(dropTargetEntityId.GetHashCode()) : null;
#endif
            return DoOnHierarchyDrop(dropTarget, parentForDraggedObjects, perform);
        }

        private DragAndDropVisualMode OnProjectBrowserDrop(EntityId dragEntityId, string dropUponPath, bool perform)
        {
            if (perform) StopDragDrop();
            return DragAndDropVisualMode.None;
        }
#endif

        private DragAndDropVisualMode DoOnProjectWindowDrop(string dropUponPath, bool perform)
        {
            if (perform && _dragging)
            {
                List<AssetInfo> infos = SnapshotDraggedAssetInfos();
                FinishDragDrop();

                if (infos != null && infos.Count > 0) // can happen in some edge asynchronous scenarios
                {
                    if (!string.IsNullOrEmpty(dropUponPath) && File.Exists(dropUponPath)) dropUponPath = Path.GetDirectoryName(dropUponPath);
                    _ = RunDragImportOperation(infos, async () => await PerformCopyToBulk(infos, dropUponPath));
                }
                DragAndDrop.AcceptDrag();
            }
            return DragAndDropVisualMode.Copy;
        }

        private async Task PerformCopyToBulk(List<AssetInfo> infos, string targetPath)
        {
            if (infos.Count == 0) return;

            for (int i = 0; i < infos.Count; i++)
            {
                AssetInfo info = infos[i];
                SetDragImportItem(i + 1, info);
                await PerformCopyTo(info, targetPath, true);
            }
            if (AI.Config.pingImported) PingAsset(infos[0]);
        }

        private void StartSceneDragImport(List<AssetInfo> infos, Vector3 worldPosition, Transform parentTransform)
        {
            if (infos == null || infos.Count == 0) return;

            Transform resolvedParent = AssetUtils.ResolveSceneParent(parentTransform);
            if (infos.All(info => info.InProject))
            {
                AddProjectAssetsToScene(infos, worldPosition, resolvedParent);
                return;
            }

            _ = RunDragImportOperation(infos, async () => await PerformSceneDrop(infos, worldPosition, resolvedParent));
        }

        private void AddProjectAssetsToScene(List<AssetInfo> infos, Vector3 worldPosition, Transform parentTransform)
        {
            foreach (AssetInfo info in infos)
            {
                if (!string.IsNullOrEmpty(info.ProjectPath) && AssetUtils.CanAddToScene(info.ProjectPath))
                {
                    AssetUtils.AddToScene(info.ProjectPath, worldPosition, parentTransform);
                }
            }
        }

        private async Task PerformSceneDrop(List<AssetInfo> infos, Vector3 worldPosition, Transform parentTransform)
        {
            if (infos == null || infos.Count == 0) return;

            for (int i = 0; i < infos.Count; i++)
            {
                AssetInfo info = infos[i];
                SetDragImportItem(i + 1, info);
                string projectPath = null;

                if (info.InProject)
                {
                    // Already imported, just add to scene
                    projectPath = info.ProjectPath;
                }
                else
                {
                    await PerformCopyTo(info, AI.GetImportFolder(), true, true, worldPosition, parentTransform);
                    continue;
                }

                // Add to scene if it's a type that can be instantiated
                if (!string.IsNullOrEmpty(projectPath) && AssetUtils.CanAddToScene(projectPath))
                {
                    AssetUtils.AddToScene(projectPath, worldPosition, parentTransform);
                }
            }
        }

        private DragAndDropVisualMode OnSceneDrop(Object dropUpon, Vector3 worldPosition, Vector2 viewportPosition, Transform parentForDraggedObjects, bool perform)
        {
            List<AssetInfo> infos = SnapshotDraggedAssetInfos();
            if (infos == null || infos.Count == 0)
            {
                return DragAndDropVisualMode.None;
            }

            if (perform)
            {
                FinishDragDrop();
                StartSceneDragImport(infos, worldPosition, parentForDraggedObjects);
                DragAndDrop.AcceptDrag();
            }

            return DragAndDropVisualMode.Copy;
        }

#if !UNITY_6000_3_OR_NEWER
        private DragAndDropVisualMode OnHierarchyDrop(int dropTargetInstanceID, HierarchyDropFlags dropMode, Transform parentForDraggedObjects, bool perform)
        {
            Object dropTarget = dropTargetInstanceID != 0 ? EditorUtility.InstanceIDToObject(dropTargetInstanceID) : null;
            return DoOnHierarchyDrop(dropTarget, parentForDraggedObjects, perform);
        }

        private DragAndDropVisualMode OnProjectBrowserDrop(int dragInstanceId, string dropUponPath, bool perform)
        {
            if (perform) StopDragDrop();
            return DragAndDropVisualMode.None;
        }
#endif

        private DragAndDropVisualMode OnInspectorDrop(Object[] targets, bool perform)
        {
            if (perform) StopDragDrop();
            return DragAndDropVisualMode.None;
        }

        private void StopDragDrop()
        {
            if (_dragging)
            {
                _dragging = false;
                GUIUtility.hotControl = 0; // otherwise scene gizmos are still blocked
                DeinitDragAndDrop();
            }
        }

        private void SearchUpdateLoop()
        {
            // Use the captured scroll view height (not current visible rect which may be wrong outside OnGUI)
            float viewHeight = _searchGridViewHeight > 0 ? _searchGridViewHeight : 400f; // fallback if not yet captured
            bool scrollChanged = _searchScrollPos != _lastSearchScrollPos || Math.Abs(viewHeight - _lastViewHeight) > 1f;

            // If scrolling while a single item is selected and visible animations are enabled,
            // stop the single selection and resume visible animations
            if (scrollChanged && AI.Config.playVisibleSearchAnimations && _animatedTileIndex >= 0)
            {
                StopSingleSelectionAnimation();
            }

            // Single selection animation (always works regardless of playVisibleSearchAnimations)
            if (_animationPlayer != null && _animationPlayer.IsLoaded
                && _animatedEntry != null
                && _animatedTileIndex >= 0 && SGrid.HasPreviewSlots && SGrid.PreviewCount > _animatedTileIndex)
            {
                // Get the current frame from the animation player
                Texture2D curTexture = _animationPlayer.GetCurrentFrame();

                if (curTexture != null && SGrid.GetPreview(_animatedTileIndex) != curTexture)
                {
                    // Only update if frame has changed (AnimationPlayer now caches frames)
                    ApplySearchGridImage(_animatedTileIndex, curTexture);
                }
            }

            // Multi-animation for all visible tiles (only when no single item is selected)
            if (AI.Config.playVisibleSearchAnimations && SGrid.HasPreviewSlots && SGrid.PreviewCount > 0 && _animatedTileIndex < 0)
            {
                // Trigger update on scroll change, pending trigger frames, OR if no animations are loaded yet
                bool gridIsReady = SGrid.ActualTileHeight > 0 && SGrid.CellsPerRow > 1 && viewHeight > 50;
                bool needsInitialLoad = _visibleAnimations.TotalActiveCount == 0 && gridIsReady;
                bool hasPendingTrigger = _visibleAnimationTriggerFrames > 0;

                if (scrollChanged || needsInitialLoad || hasPendingTrigger)
                {
                    // Only process after the retained grid has reported its layout dimensions.
                    if (gridIsReady)
                    {
                        _lastSearchScrollPos = _searchScrollPos;
                        _lastViewHeight = viewHeight;
                        UpdateVisibleAnimations(viewHeight);

                        // Clear trigger if animations started loading
                        if (_visibleAnimations.TotalActiveCount > 0)
                        {
                            _visibleAnimationTriggerFrames = 0;
                        }
                    }

                    // Decrement trigger counter each frame
                    if (hasPendingTrigger)
                    {
                        _visibleAnimationTriggerFrames--;
                    }
                }

                // Update all visible animation frames
                foreach (int tileIndex in _visibleAnimations.LoadedKeys)
                {
                    if (tileIndex < 0 || tileIndex >= SGrid.PreviewCount) continue;

                    // Skip if this is the single-selection animated tile (handled above)
                    if (tileIndex == _animatedTileIndex) continue;

                    Texture2D curTexture = _visibleAnimations.GetCurrentFrame(tileIndex);
                    if (curTexture != null && SGrid.GetPreview(tileIndex) != curTexture)
                    {
                        ApplySearchGridImage(tileIndex, curTexture);
                    }
                }
            }
        }

        private void ApplySearchGridImage(int index, Texture image)
        {
            if (!SGrid.HasPreviewSlots || index < 0 || index >= SGrid.PreviewCount) return;

            SGrid.SetPreview(index, image);
            _nativeSearchGridView?.RefreshItem(index);
        }

        private void DisposeSearchResultTextures()
        {
            _searchPreviewSessionInitialized = false;
            if (!SGrid.HasPreviewSlots) return;

            for (int i = 0; i < SGrid.PreviewCount; i++)
            {
                Texture preview = SGrid.GetPreview(i);
                if (preview != null)
                {
                    // Skip built-in Unity icons which shouldn't be destroyed
                    if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(preview)))
                    {
                        DestroyImmediate(preview);
                    }
                    SGrid.ClearPreview(i);
                }
            }

            DisposeAnimTexture();
            DisposeAllVisibleAnimations();
        }

        private void ClearFilePreviewCache()
        {
            // Destroy all cached textures before clearing to prevent memory leaks
            foreach (Texture2D texture in FilePreviewCache.Values)
            {
                if (texture != null)
                {
                    // Skip built-in Unity icons which shouldn't be destroyed
                    if (texture.name != "d_DefaultAsset Icon" &&
                        !AssetDatabase.GetAssetPath(texture).StartsWith("Library/"))
                    {
                        DestroyImmediate(texture);
                    }
                }
            }
            FilePreviewCache.Clear();
            PendingVirtualPreviews.Clear();
            ProjectMetadataCache.Clear();
        }

        private void StopSingleSelectionAnimation()
        {
            // Stop only the single selection animation, preserve visible animations
            if (_animationPlayer != null)
            {
                _animationPlayer.Dispose();
                _animationPlayer = null;
            }
            _animatedTileIndex = -1;
            _animatedEntry = null;
        }

        private void StopAnimation()
        {
            // Immediately stop animation by clearing state variables
            // This is synchronous and safe to call before grid contents are recreated
            StopSingleSelectionAnimation();

            // Also stop all visible animations
            DisposeAllVisibleAnimations();
        }

        private async void DisposeAnimTexture()
        {
            if (_animationPlayer != null)
            {
                // Capture state immediately to local variables
                AnimationPlayer animPlayer = _animationPlayer;
                AssetInfo animatedEntry = _animatedEntry;
                int tileIndex = _animatedTileIndex;

                // Clear instance fields FIRST to prevent race condition
                // If LoadAnimTexture() runs during async work, it won't be affected by our cleanup
                _animationPlayer = null;
                _animatedTileIndex = -1;
                _animatedEntry = null;

                // Restore the static preview before disposing (use local variables)
                if (animatedEntry != null && tileIndex >= 0 && SGrid.HasPreviewSlots && SGrid.PreviewCount > tileIndex)
                {
                    // Keep reference to the current animated frame (don't destroy it yet to avoid flicker)
                    Texture2D oldAnimFrame = null;
                    Texture preview = SGrid.GetPreview(tileIndex);
                    if (preview != null)
                    {
                        // Skip built-in Unity icons which shouldn't be destroyed
                        if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(preview)))
                        {
                            oldAnimFrame = preview as Texture2D;
                        }
                    }

                    // Load and restore the static preview
                    string previewFile = null;
                    if (animatedEntry.HasPreview(true))
                    {
                        previewFile = AssetImporter.ValidatePreviewFile(animatedEntry, Paths.GetPreviewFolder());
                    }

                    if (previewFile != null)
                    {
                        Texture2D staticTexture = await AssetUtils.LoadLocalTexture(
                            previewFile,
                            false,
                            (AI.Config.upscalePreviews && !AI.Config.upscaleLossless) ? AI.Config.upscaleSize : 0
                        );

                        // Re-check bounds after async operation in case the grid was resized
                        if (staticTexture != null && SGrid.HasPreviewSlots && SGrid.PreviewCount > tileIndex && tileIndex >= 0)
                        {
                            // Now destroy the old animated frame AFTER we have the static preview ready
                            if (oldAnimFrame != null)
                            {
                                DestroyImmediate(oldAnimFrame);
                            }

                            if (AI.Config.tileCornerRadius > 0)
                            {
                                Texture2D roundedTexture = staticTexture.WithRoundedCorners(AI.Config.tileCornerRadius);
                                ApplySearchGridImage(tileIndex, roundedTexture);
                                DestroyImmediate(staticTexture);
                            }
                            else
                            {
                                ApplySearchGridImage(tileIndex, staticTexture);
                            }
                        }
                        else if (staticTexture != null)
                        {
                            // Grid was resized during async operation, clean up the texture
                            DestroyImmediate(staticTexture);
                        }
                    }
                }

                // Destroy the captured animation player (not the instance field which may have new data)
                animPlayer?.Dispose();
            }
        }

        private void DetectVariablesInSearchPhrase()
        {
            if (string.IsNullOrEmpty(_searchPhrase))
            {
                _searchVariables.Clear();
                _hasSearchVariables = false;
                return;
            }

            // Find all variable references
            List<string> varNames = VariableResolver.FindVariableReferences(_searchPhrase);

            // Update existing variables or add new ones
            HashSet<string> currentVars = new HashSet<string>(varNames);

            // Remove variables that are no longer referenced
            List<string> toRemove = new List<string>();
            foreach (string key in _searchVariables.Keys)
            {
                if (!currentVars.Contains(key))
                {
                    toRemove.Add(key);
                }
            }
            foreach (string key in toRemove)
            {
                _searchVariables.Remove(key);
            }

            // Add new variables (keep existing ones unchanged to preserve user values)
            foreach (string varName in varNames)
            {
                if (!_searchVariables.ContainsKey(varName))
                {
                    _searchVariables[varName] = new SearchVariable
                    {
                        name = varName,
                        defaultValue = "",
                        currentValue = ""
                    };
                }
            }

            bool hadVariables = _hasSearchVariables;
            _hasSearchVariables = _searchVariables.Count > 0;

            // Trigger search update if variables were newly detected
            if (!hadVariables && _hasSearchVariables)
            {
                _requireSearchUpdate = true;
            }
        }

        private string SerializeSearchVariables(Dictionary<string, SearchVariable> variables)
        {
            if (variables == null || variables.Count == 0) return null;

            SearchVariableCollection collection = SearchVariableCollection.FromDictionary(variables);
            return collection.ToJson();
        }

        private Dictionary<string, SearchVariable> DeserializeSearchVariables(string json)
        {
            if (string.IsNullOrEmpty(json)) return new Dictionary<string, SearchVariable>();

            SearchVariableCollection collection = SearchVariableCollection.FromJson(json);
            return collection.Variables ?? new Dictionary<string, SearchVariable>();
        }

        internal static string FormatDependencyCount(AssetInfo info, bool showAdvanced)
        {
            int dependencyCount = GetDependencyCount(info);
            string result;

            if (showAdvanced && HasSeparatedDependencyCounts(info))
            {
                int mediaDependencyCount = info?.MediaDependencies?.Count ?? 0;
                int scriptDependencyCount = info?.ScriptDependencies?.Count ?? 0;
                string scriptDeps = scriptDependencyCount > 0 ? $" + {scriptDependencyCount} scripts" : string.Empty;
                result = $"{mediaDependencyCount}{scriptDeps}";
            }
            else
            {
                result = $"{dependencyCount}";
            }

            return IsIncompleteDependencyResult(info)
                ? $"{result} (incomplete)"
                : result;
        }

        internal static bool CanShowDependencyTree(AssetInfo info)
        {
            return info?.Dependencies != null && info.Dependencies.Count > 0 &&
                (info.DependencyState == AssetInfo.DependencyStateOptions.Done ||
                    IsIncompleteDependencyResult(info));
        }

        internal static string FormatHiddenExtensionsForDisplay(string extensions)
        {
            if (string.IsNullOrWhiteSpace(extensions)) return string.Empty;

            string[] parts = extensions
                .Split(new[] {';'}, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => !string.IsNullOrEmpty(part))
                .ToArray();

            return string.Join("; ", parts);
        }

        internal static bool IsIncompleteDependencyResult(AssetInfo info)
        {
            return HasDependencyRows(info) &&
                (info.DependencyState == AssetInfo.DependencyStateOptions.Partial ||
                    info.DependencyState == AssetInfo.DependencyStateOptions.NotPossible);
        }

        internal static bool HasDependencyRows(AssetInfo info)
        {
            return GetDependencyCount(info) > 0;
        }

        internal static int GetDependencyCount(AssetInfo info)
        {
            if (info?.Dependencies != null) return info.Dependencies.Count;
            return (info?.MediaDependencies?.Count ?? 0) + (info?.ScriptDependencies?.Count ?? 0);
        }

        private static bool HasSeparatedDependencyCounts(AssetInfo info)
        {
            return info?.MediaDependencies != null || info?.ScriptDependencies != null;
        }
    }
}
