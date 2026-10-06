using System;
using System.Collections.Generic;
using System.Linq;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AssetInventory
{
    public sealed partial class DependenciesUI : BasicEditorUI
    {
        private const string DependenciesRootClass = "ai-dependencies-root";
        private const string DependenciesSubtitleClass = "ai-dependencies-subtitle";
        private const string DependenciesViewBarClass = "ai-dependencies-view-bar";
        private const string DependenciesViewButtonClass = "ai-dependencies-view-button";
        private const string DependenciesViewButtonActiveClass = "ai-dependencies-view-button-active";
        private const string DependenciesContentClass = "ai-dependencies-content";
        private const string DependenciesListClass = "ai-dependencies-list";
        private const string DependenciesRowClass = "ai-dependencies-row";
        private const string DependenciesHeaderClass = "ai-dependencies-row-header";
        private const string DependenciesFileRowClass = "ai-dependencies-file-row";
        private const string DependenciesStatusIconClass = "ai-dependencies-status-icon";
        private const string DependenciesPathClass = "ai-dependencies-path";
        private const string DependenciesScriptPathClass = "ai-dependencies-path-script";
        private const string DependenciesInventoryButtonClass = "ai-dependencies-inventory-button";
        private const string DependenciesSearchBarClass = "ai-dependencies-search-bar";
        private const string DependenciesGraphFooterClass = "ai-dependencies-graph-footer";
        private const string DependenciesGraphContainerClass = "ai-dependencies-graph-container";
        private const string DependenciesGraphBodyClass = "ai-dependencies-graph-body";
        private const string DependenciesGraphInspectorClass = "ai-dependencies-graph-inspector";
        private const string DependenciesSearchClass = "ai-dependencies-search";
        private const string DependenciesGraphPreviewClass = "ai-dependencies-graph-preview";

        private AssetInfo _info;
        private string _dependencyTypes;
        private bool _uitkActive;
        private VisualElement _graphInspector;
        private DependencyGraphNode _selectedGraphNode;
        private string _dependencySearchText = string.Empty;
        private Action<AssetFile> _showInInventory;

        // Virtualization caches
        private List<ListViewEntry> _listViewEntries;
        private List<ListViewEntry> _visibleListViewEntries;
        private ListView _dependencyList;
        private HashSet<int> _scriptDependencyIds;
        private Dictionary<int, Asset> _crossPackageDict;
        private static GUIContent _iconInstalled;
        private static GUIContent _iconImport;

        private struct ListViewEntry
        {
            public bool IsHeader;
            public string HeaderText;
            public AssetFile File;
            public string DisplayText;
            public bool IsScriptDependency;
        }

        public static DependenciesUI ShowWindow()
        {
            DependenciesUI window = GetWindow<DependenciesUI>("Asset Dependencies");
            window.minSize = new Vector2(500, 200);

            return window;
        }

        public void Init(AssetInfo info)
        {
            Init(info, null);
        }

        internal void Init(AssetInfo info, Action<AssetFile> showInInventory)
        {
            _info = info;
            _showInInventory = showInInventory;
            _serializedAssetInfoId = info?.Id ?? -1;

            if (_info?.Dependencies != null)
            {
                _info.Dependencies.ForEach(i => i.CheckIfInProject());
                _dependencyTypes = string.Join(", ", _info.Dependencies
                    .OrderBy(f => f.Type).GroupBy(f => f.Type)
                    .Select(g => g.Count() + " " + g.Key + " (" + EditorUtility.FormatBytes(g.Sum(f => f.Size)) + ")"));
            }

            // Mark graph for rebuild
            _graphNeedsRebuild = true;
            _selectedGraphNode = null;
            _graphRenderer?.SelectNode(null, false);

            // Build virtualization caches
            RebuildListViewCache();
            BuildIfReady();
        }

        private void RebuildListViewCache()
        {
            if (_info?.Dependencies == null)
            {
                _listViewEntries = null;
                _visibleListViewEntries = null;
                return;
            }

            // Build lookup dictionaries
            _scriptDependencyIds = new HashSet<int>(_info.ScriptDependencies?.Select(f => f.Id) ?? Enumerable.Empty<int>());
            _crossPackageDict = _info.CrossPackageDependencies?.ToDictionary(a => a.Id) ?? new Dictionary<int, Asset>();

            // Pre-build all entries with cached display strings
            _listViewEntries = new List<ListViewEntry>(_info.Dependencies.Count + _crossPackageDict.Count + 1);

            int? curAssetId = null;
            Asset mainAsset = _info.ToAsset();
            int srpSupportId = _info.SRPSupportPackage?.Id ?? -1;

            foreach (AssetFile file in _info.Dependencies)
            {
                // Add header when asset changes
                if (!curAssetId.HasValue || file.AssetId != curAssetId.Value)
                {
                    curAssetId = file.AssetId;
                    string headerText;
                    if (file.AssetId < 0)
                    {
                        headerText = PackageNode.GetDefaultName(file.AssetId);
                    }
                    else
                    {
                        Asset curAsset;
                        if (!_crossPackageDict.TryGetValue(file.AssetId, out curAsset))
                        {
                            curAsset = mainAsset;
                        }
                        headerText = !string.IsNullOrWhiteSpace(curAsset.DisplayName) ? curAsset.DisplayName : curAsset.SafeName;
                    }

                    _listViewEntries.Add(new ListViewEntry
                    {
                        IsHeader = true,
                        HeaderText = headerText
                    });
                }

                // Add file entry with pre-computed display text
                bool fromSupport = srpSupportId == file.AssetId;
                string displayText = file.Path + " (" + EditorUtility.FormatBytes(file.Size) + (fromSupport ? ", SRP Override" : "") + ")";

                _listViewEntries.Add(new ListViewEntry
                {
                    IsHeader = false,
                    File = file,
                    DisplayText = displayText,
                    IsScriptDependency = _scriptDependencyIds.Contains(file.Id)
                });
            }

            RebuildVisibleListViewCache();
        }

        private void OnEnable()
        {
            // Handle domain reload - try to restore state
            if (_serializedAssetInfoId != 0 && _info == null)
            {
                // Try to reload the asset info
                // This would need access to the asset database/cache
                // For now, just mark that we need to rebuild
                _graphNeedsRebuild = true;
            }
        }

        private void CreateGUI()
        {
            _uitkActive = true;
            BuildContent();
        }

        private void BuildIfReady()
        {
            if (_uitkActive && rootVisualElement != null && rootVisualElement.childCount > 0)
            {
                BuildContent();
            }
        }

        private void BuildContent()
        {
            VisualElement root = rootVisualElement;
            if (root == null) return;

            _dependencyList = null;
            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);
            root.AddToClassList(DependenciesRootClass);

            if (_info == null && _serializedAssetInfoId != 0)
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("Asset data was lost during script recompile. Please reopen the dependencies window.", MessageType.Warning));
                root.Add(AssetInventoryUITK.CreateFlexibleSpacer());
                root.Add(CreateCloseFooter());
                return;
            }

            if (_info == null || _info.Id == 0)
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("Select an asset and trigger the dependency scan to see its dependencies broken down here.", MessageType.Warning));
                root.Add(AssetInventoryUITK.CreateFlexibleSpacer());
                return;
            }

            root.Add(CreateHeader());
            root.Add(CreateSummarySection());
            root.Add(CreateDependencySearchBar());
            root.Add(CreateViewBar());

            VisualElement content = new VisualElement();
            content.AddToClassList(DependenciesContentClass);
            root.Add(content);

            if (_viewMode == ViewMode.Graph)
            {
                content.Add(CreateGraphPanel());
            }
            else
            {
                content.Add(CreateDependencyListPanel());
            }
        }

        private VisualElement CreateCloseFooter()
        {
            VisualElement footer = AssetInventoryUITK.CreateWindowFooter();
            footer.Add(AssetInventoryUITK.CreateSecondaryButton("Close Window", Close));
            return footer;
        }

        private VisualElement CreateHeader()
        {
            Label subtitle = AssetInventoryUITK.CreateCopyLabel($"'{_info.FileName}' in asset '{_info.GetDisplayName()}'");
            subtitle.AddToClassList(DependenciesSubtitleClass);
            return subtitle;
        }

        private VisualElement CreateSummarySection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Overview");

            string dependencyCountText = IndexUI.FormatDependencyCount(_info, false);
            int crossPackageCount = _info.CrossPackageDependencies?.Count ?? 0;
            string dependenciesText = crossPackageCount > 1
                ? $"{dependencyCountText} across {crossPackageCount + 1:N0} packages"
                : dependencyCountText;
            section.Add(AssetInventoryUITK.CreateKeyValueRow("Dependencies", dependenciesText));

            if (IndexUI.IsIncompleteDependencyResult(_info))
            {
                section.Add(AssetInventoryUITK.CreateHelpBox("Dependency analysis did not complete. The list and graph show only dependencies found before the scan stopped.", MessageType.Warning));
            }

            if (_info.SRPSupportPackage != null && _info.SRPSupportPackage.Id > 0)
            {
                section.Add(AssetInventoryUITK.CreateKeyValueRow("SRP Support", _info.SRPSupportPackage.DisplayName));
            }

            if (ShowAdvanced())
            {
                section.Add(AssetInventoryUITK.CreateKeyValueRow("File Types", _dependencyTypes));
            }

            section.Add(AssetInventoryUITK.CreateKeyValueRow("Asset Size", EditorUtility.FormatBytes(_info.Size)));
            long dependencySize = _info.Dependencies?.Sum(f => f.Size) ?? 0;
            section.Add(AssetInventoryUITK.CreateKeyValueRow("Dependencies Size", EditorUtility.FormatBytes(dependencySize)));

            if (_info.Dependencies != null && _info.Dependencies.Any(f => f.InProject))
            {
                long remainingSize = _info.Dependencies.Where(f => !f.InProject).Sum(f => f.Size);
                section.Add(AssetInventoryUITK.CreateKeyValueRow("Remaining", EditorUtility.FormatBytes(remainingSize)));
            }

            return section;
        }

        private VisualElement CreateViewBar()
        {
            VisualElement bar = new VisualElement();
            bar.AddToClassList(DependenciesViewBarClass);
            bar.Add(AssetInventoryUITK.CreateSegmentedControl(
                new[] {new GUIContent("Graph"), new GUIContent("List")},
                _viewMode == ViewMode.Graph ? 0 : 1,
                index => SetViewMode(index == 0 ? ViewMode.Graph : ViewMode.List)));

            return bar;
        }

        private void SetViewMode(ViewMode viewMode)
        {
            if (_viewMode == viewMode) return;

            _viewMode = viewMode;
            if (_viewMode == ViewMode.Graph)
            {
                InitializeGraph();
                _needsInitialFrame = true;
            }
            BuildContent();
        }

        internal VisualElement CreateDependencyListPanel()
        {
            // The dependency model can be updated while the graph view is active.
            // Rebuild from the same live model before composing the list.
            RebuildListViewCache();

            if (_listViewEntries == null || _listViewEntries.Count == 0)
            {
                return AssetInventoryUITK.CreateHelpBox("No dependencies to display.", MessageType.Info);
            }

            _dependencyList = new ListView(
                _visibleListViewEntries,
                32f,
                CreateDependencyRow,
                BindDependencyRow)
            {
                fixedItemHeight = 32f,
                horizontalScrollingEnabled = false,
                reorderable = false,
                selectionType = SelectionType.None,
                showAddRemoveFooter = false,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                showBorder = true,
                showBoundCollectionSize = false,
                showFoldoutHeader = false,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight
            };
            _dependencyList.AddToClassList(DependenciesListClass);
            return _dependencyList;
        }

        private void RebuildVisibleListViewCache()
        {
            if (_listViewEntries == null)
            {
                _visibleListViewEntries = null;
                return;
            }

            string searchText = _dependencySearchText?.Trim();
            if (string.IsNullOrEmpty(searchText))
            {
                _visibleListViewEntries = _listViewEntries;
                return;
            }

            List<ListViewEntry> filtered = new List<ListViewEntry>();
            for (int index = 0; index < _listViewEntries.Count;)
            {
                ListViewEntry header = _listViewEntries[index];
                int groupStart = header.IsHeader ? index + 1 : index;
                int groupEnd = groupStart;
                while (groupEnd < _listViewEntries.Count && !_listViewEntries[groupEnd].IsHeader) groupEnd++;

                bool headerMatches = header.IsHeader && ContainsIgnoreCase(header.HeaderText, searchText);
                int headerIndex = filtered.Count;
                if (header.IsHeader) filtered.Add(header);

                for (int fileIndex = groupStart; fileIndex < groupEnd; fileIndex++)
                {
                    ListViewEntry entry = _listViewEntries[fileIndex];
                    if (headerMatches || DependencyEntryMatches(entry, searchText)) filtered.Add(entry);
                }

                if (header.IsHeader && filtered.Count == headerIndex + 1) filtered.RemoveAt(headerIndex);
                index = groupEnd;
            }

            _visibleListViewEntries = filtered;
        }

        private static bool DependencyEntryMatches(ListViewEntry entry, string searchText)
        {
            AssetFile file = entry.File;
            return ContainsIgnoreCase(entry.DisplayText, searchText)
                || ContainsIgnoreCase(file?.FileName, searchText)
                || ContainsIgnoreCase(file?.Path, searchText)
                || ContainsIgnoreCase(file?.Type, searchText);
        }

        private static bool ContainsIgnoreCase(string value, string searchText)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private VisualElement CreateDependencyRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(DependenciesRowClass);

            Label header = AssetInventoryUITK.CreateCopyLabel(string.Empty);
            header.name = "header";
            header.AddToClassList(DependenciesHeaderClass);
            row.Add(header);

            VisualElement file = new VisualElement();
            file.name = "file";
            file.AddToClassList(DependenciesFileRowClass);

            Image icon = new Image
            {
                name = "icon",
                scaleMode = ScaleMode.ScaleToFit
            };
            icon.AddToClassList(DependenciesStatusIconClass);
            file.Add(icon);

            Label path = AssetInventoryUITK.CreateCopyLabel(string.Empty);
            path.name = "path";
            path.AddToClassList(DependenciesPathClass);
            file.Add(path);

            Button showInInventory = AssetInventoryUITK.CreateIconButton(
                "Show in Asset Inventory",
                "d_Search Icon",
                () => ShowInInventory(file.userData as AssetFile));
            showInInventory.name = "show-in-inventory";
            showInInventory.AddToClassList(DependenciesInventoryButtonClass);
            file.Add(showInInventory);
            file.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || evt.clickCount < 2) return;

                VisualElement target = evt.target as VisualElement;
                while (target != null && target != file)
                {
                    if (target is Button) return;
                    target = target.parent;
                }

                ShowInInventory(file.userData as AssetFile);
                evt.StopImmediatePropagation();
            });

            row.Add(file);
            return row;
        }

        private void BindDependencyRow(VisualElement element, int index)
        {
            if (_visibleListViewEntries == null || index < 0 || index >= _visibleListViewEntries.Count) return;

            ListViewEntry entry = _visibleListViewEntries[index];
            Label header = element.Q<Label>("header");
            VisualElement file = element.Q<VisualElement>("file");

            if (entry.IsHeader)
            {
                file.userData = null;
                header.text = entry.HeaderText;
                header.style.display = DisplayStyle.Flex;
                file.style.display = DisplayStyle.None;
                return;
            }

            header.style.display = DisplayStyle.None;
            file.style.display = DisplayStyle.Flex;
            file.userData = entry.File;
            file.tooltip = "Double-click to show this file in Asset Inventory.";

            Image icon = element.Q<Image>("icon");
            icon.image = GetDependencyStatusIcon(entry.File.InProject);
            icon.tooltip = entry.File.InProject ? "Already in project" : "Needs to be imported";

            Label path = element.Q<Label>("path");
            path.text = entry.DisplayText;
            string identity = !string.IsNullOrEmpty(entry.File.Guid) ? entry.File.Guid : $"{entry.File.AssetId}_{entry.File.Path}";
            path.tooltip = $"{identity}\nDouble-click to show this file in Asset Inventory.";
            path.EnableInClassList(DependenciesScriptPathClass, entry.IsScriptDependency);
        }

        private void ShowInInventory(AssetFile file)
        {
            if (file == null) return;

            if (_showInInventory != null)
            {
                _showInInventory(file);
                return;
            }

            IndexUI window = EditorWindow.GetWindow<IndexUI>("Asset Inventory");
            window.OpenAssetFileInSearch(file);
        }

        private static Texture GetDependencyStatusIcon(bool inProject)
        {
            if (_iconInstalled == null) _iconInstalled = EditorGUIUtility.IconContent("Installed", "|Already in project");
            if (_iconImport == null) _iconImport = EditorGUIUtility.IconContent("Import", "|Needs to be imported");
            return inProject ? _iconInstalled.image : _iconImport.image;
        }

        private VisualElement CreateGraphPanel()
        {
            VisualElement panel = new VisualElement();
            panel.AddToClassList(DependenciesContentClass);

            InitializeGraph();
            _graphRenderer.SetGraph(_graphData, _graphLayoutMode == GraphLayoutMode.Organic ? _forceLayout : null);
            _graphRenderer.SetSearchText(_dependencySearchText);
            _graphRenderer.AddToClassList(DependenciesGraphContainerClass);

            VisualElement body = new VisualElement();
            body.AddToClassList(DependenciesGraphBodyClass);
            body.RegisterCallback<GeometryChangedEvent>(evt =>
                body.EnableInClassList("ai-dependencies-graph-body-narrow", evt.newRect.width < 780f));
            body.Add(_graphRenderer);

            _graphInspector = new ScrollView(ScrollViewMode.Vertical);
            _graphInspector.AddToClassList(DependenciesGraphInspectorClass);
            body.Add(_graphInspector);
            panel.Add(body);
            panel.Add(CreateGraphFooter());

            if (_selectedGraphNode != null && !_graphData.Nodes.Contains(_selectedGraphNode)) _selectedGraphNode = null;
            _graphRenderer.SelectNode(_selectedGraphNode, false);
            RebuildGraphInspector();
            if (_needsInitialFrame || _pendingFrameAll)
            {
                _graphRenderer.RequestFrameAll();
                _needsInitialFrame = false;
                _pendingFrameAll = false;
            }

            return panel;
        }

        private VisualElement CreateDependencySearchBar()
        {
            VisualElement searchBar = new VisualElement();
            searchBar.AddToClassList(DependenciesSearchBarClass);

            ToolbarSearchField search = new ToolbarSearchField();
            search.AddToClassList(DependenciesSearchClass);
            search.tooltip = "Find dependencies by name, path, type, or package. In Graph view, press Enter to focus the next match.";
            search.SetValueWithoutNotify(_dependencySearchText);
            search.RegisterValueChangedCallback(evt => SetDependencySearchText(evt.newValue));
            search.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (_viewMode != ViewMode.Graph) return;
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
                _graphRenderer?.FocusNextMatch();
                evt.StopPropagation();
            });
            searchBar.Add(search);
            return searchBar;
        }

        internal void SetDependencySearchText(string searchText)
        {
            _dependencySearchText = searchText ?? string.Empty;
            _graphRenderer?.SetSearchText(_dependencySearchText);
            RebuildVisibleListViewCache();
            if (_dependencyList == null) return;

            _dependencyList.itemsSource = _visibleListViewEntries;
            _dependencyList.Rebuild();
        }

        private VisualElement CreateGraphFooter()
        {
            VisualElement footer = AssetInventoryUITK.CreateFooter();
            footer.AddToClassList(DependenciesGraphFooterClass);

            PopupField<string> layoutControl = new PopupField<string>(
                new List<string> {"Left to Right", "Grouped", "Organic"},
                (int)_graphLayoutMode);
            layoutControl.tooltip = "Choose how dependencies are arranged.";
            layoutControl.AddToClassList("ai-dependencies-layout-popup");
            layoutControl.RegisterValueChangedCallback(evt => SetGraphLayoutMode(layoutControl.index));
            footer.Add(layoutControl);

            Button frameAll = AssetInventoryUITK.CreateSecondaryButton("Frame All", RequestGraphFrameAll);
            frameAll.tooltip = "Fit all dependencies in the graph (F).";
            footer.Add(frameAll);
            return footer;
        }

        private void RebuildGraphInspector()
        {
            if (_graphInspector == null) return;
            _graphInspector.Clear();

            DependencyGraphNode node = _selectedGraphNode;
            if (node == null)
            {
                _graphInspector.style.display = DisplayStyle.None;
                return;
            }

            _graphInspector.style.display = DisplayStyle.Flex;

            VisualElement titleRow = new VisualElement();
            titleRow.AddToClassList("ai-dependencies-graph-inspector-title");
            Label title = AssetInventoryUITK.CreateCopyLabel(node.GetDisplayName());
            title.AddToClassList("ai-section-title");
            title.AddToClassList("ai-inline-grow");
            titleRow.Add(title);
            titleRow.Add(AssetInventoryUITK.CreateIconButton("Close details", "CrossIcon", () =>
            {
                _selectedGraphNode = null;
                _graphRenderer?.SelectNode(null, false);
                RebuildGraphInspector();
            }));
            _graphInspector.Add(titleRow);

            AssetFile file = node.AssetFile;

            Image preview = new Image
            {
                image = node.Icon,
                scaleMode = ScaleMode.ScaleToFit
            };
            preview.AddToClassList(DependenciesGraphPreviewClass);
            _graphInspector.Add(preview);

            VisualElement details = AssetInventoryUITK.CreateSection("Dependency");
            details.Add(AssetInventoryUITK.CreateKeyValueRow("Package", node.PackageNode?.Name ?? "-Unknown-"));
            details.Add(AssetInventoryUITK.CreateKeyValueRow("Type", string.IsNullOrEmpty(file?.Type) ? "-Unknown-" : file.Type));
            details.Add(AssetInventoryUITK.CreateKeyValueRow("Status", file != null && file.InProject ? "In Project" : "Needs Import"));
            details.Add(AssetInventoryUITK.CreateKeyValueRow("Size", EditorUtility.FormatBytes(file?.Size ?? 0L)));
            details.Add(AssetInventoryUITK.CreateKeyValueRow("Path", file?.Path ?? string.Empty));
            details.Add(AssetInventoryUITK.CreateKeyValueRow("Connections", $"{node.IncomingNodes.Count:N0} incoming, {node.OutgoingNodes.Count:N0} outgoing"));
            _graphInspector.Add(details);

            VisualElement actions = new VisualElement();
            actions.AddToClassList("ai-dependencies-graph-inspector-actions");
            actions.Add(AssetInventoryUITK.CreatePrimaryButton("Show in Inventory", () => ShowInInventory(file)));
            actions.Add(AssetInventoryUITK.CreateSecondaryButton("Focus", () => _graphRenderer?.FocusOnNode(node)));
            if (node.HasHiddenDependencies)
            {
                actions.Add(AssetInventoryUITK.CreateSecondaryButton("Expand", () => ExpandGraphNode(node)));
            }
            if (node.IsExpanded && !node.IsRoot)
            {
                actions.Add(AssetInventoryUITK.CreateSecondaryButton("Collapse", () => CollapseGraphNode(node)));
            }
            if (file != null && !string.IsNullOrEmpty(file.ProjectPath))
            {
                actions.Add(AssetInventoryUITK.CreateSecondaryButton("Reveal in Project", () => EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(file.ProjectPath))));
            }
            actions.Add(AssetInventoryUITK.CreateSecondaryButton("Copy Path", () => EditorGUIUtility.systemCopyBuffer = file?.Path ?? string.Empty));
            _graphInspector.Add(actions);
        }

    }
}
