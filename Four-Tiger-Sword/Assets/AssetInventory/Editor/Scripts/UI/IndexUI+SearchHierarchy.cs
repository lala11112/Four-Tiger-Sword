using System;
using System.Collections.Generic;
using System.Linq;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using NativeTreeView = UnityEngine.UIElements.TreeView;

namespace AssetInventory
{
    public partial class IndexUI
    {
        private const string SearchHierarchyPanelClass = "ai-search-hierarchy";
        private const string SearchHierarchyToolbarClass = "ai-search-hierarchy-toolbar";
        private const string SearchHierarchyPopupClass = "ai-search-hierarchy-popup";
        private const string SearchHierarchyClearClass = "ai-search-hierarchy-clear";
        private const string SearchHierarchyTreeClass = "ai-search-hierarchy-tree";
        private const string SearchHierarchyRowClass = "ai-search-hierarchy-row";
        private const string SearchHierarchyNameClass = "ai-search-hierarchy-name";
        private const string SearchHierarchyCountClass = "ai-search-hierarchy-count";
        private const string SearchHierarchyActiveClass = "ai-search-hierarchy-row-active";
        private const string SearchHierarchyEmptyClass = "ai-search-hierarchy-empty";
        private const string SearchSidebarFiltersScrollClass = "ai-search-sidebar-filters-scroll";
        private const string SearchSidebarFiltersContentClass = "ai-search-sidebar-filters-content";
        internal const int SearchSidebarFiltersMode = 5;

        private Vector2 _leftSidebarScrollPos;

        private static readonly string[] _hierarchyTypes = {"File Path", "Category", "Publisher", "Package", "File Type", "Filters"};
        private TreeModel<HierarchyTreeElement> _hierarchyTreeModel;
        private bool _requireHierarchyRebuild;
        [SerializeField] private string _activeHierarchyFilter;
        [SerializeField] private string _activeHierarchyFilterValue;
        [SerializeField] private string _searchPhraseBeforeHierarchyFilter;
        [SerializeField] private string _previousSearchPhraseBeforeHierarchyFilter;
        private bool _nativeSearchHierarchyActive;
        private bool _suppressNativeSearchHierarchySelection;
        private NativeTreeView _nativeSearchHierarchyTreeView;
        private Button _nativeSearchHierarchyClearButton;
        private VisualElement _nativeSearchHierarchyEmpty;
        private ScrollView _nativeSearchSidebarFiltersScroll;
        private VisualElement _nativeSearchSidebarFiltersContent;
        private int _nativeSearchSidebarFiltersStateHash = int.MinValue;

        private VisualElement CreateNativeSearchHierarchyPane()
        {
            VisualElement panel = new VisualElement();
            panel.AddToClassList(SearchHierarchyPanelClass);
            panel.AddToClassList(PackagesInspectorClass);

            VisualElement toolbar = new VisualElement();
            toolbar.AddToClassList(SearchHierarchyToolbarClass);

            int hierarchyIndex = Mathf.Clamp(AI.Config.searchLeftSideBarHierarchy, 0, _hierarchyTypes.Length - 1);
            PopupField<string> hierarchyPopup = new PopupField<string>(_hierarchyTypes.ToList(), hierarchyIndex);
            hierarchyPopup.tooltip = "Choose a search hierarchy, or keep the filters visible in this pane.";
            hierarchyPopup.AddToClassList(SearchHierarchyPopupClass);
            hierarchyPopup.RegisterValueChangedCallback(evt =>
            {
                int nextIndex = Array.IndexOf(_hierarchyTypes, evt.newValue);
                if (nextIndex < 0 || AI.Config.searchLeftSideBarHierarchy == nextIndex) return;

                AI.Config.searchLeftSideBarHierarchy = nextIndex;
                AI.SaveConfig();
                _requireHierarchyRebuild = true;
                RefreshNativeSearchSidebarMode(true);
                RefreshNativeSearchInspector();
            });
            toolbar.Add(hierarchyPopup);

            _nativeSearchHierarchyClearButton = AssetInventoryUITK.CreateIconButton(
                "Clear hierarchy filter",
                "SearchCancelButton",
                ClearHierarchyFilter);
            _nativeSearchHierarchyClearButton.AddToClassList(SearchHierarchyClearClass);
            toolbar.Add(_nativeSearchHierarchyClearButton);
            panel.Add(toolbar);

            _nativeSearchHierarchyTreeView = new NativeTreeView
            {
                fixedItemHeight = 22f,
                selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.All,
                showBorder = true,
                horizontalScrollingEnabled = false,
                makeItem = CreateNativeHierarchyRow,
                bindItem = BindNativeHierarchyRow,
                viewDataKey = "AI4.Search.HierarchyTree"
            };
            _nativeSearchHierarchyTreeView.AddToClassList(SearchHierarchyTreeClass);
            _nativeSearchHierarchyTreeView.selectionChanged += OnNativeHierarchySelectionChanged;
            panel.Add(_nativeSearchHierarchyTreeView);

            _nativeSearchHierarchyEmpty = AssetInventoryUITK.CreateHelpBox(
                "No hierarchy data available. Perform a search first.",
                MessageType.Info);
            _nativeSearchHierarchyEmpty.AddToClassList(SearchHierarchyEmptyClass);
            panel.Add(_nativeSearchHierarchyEmpty);

            _nativeSearchSidebarFiltersScroll = new ScrollView(ScrollViewMode.Vertical)
            {
                viewDataKey = "AI4.Search.SidebarFilters"
            };
            _nativeSearchSidebarFiltersScroll.AddToClassList(PackagesInspectorScrollClass);
            _nativeSearchSidebarFiltersScroll.AddToClassList(SearchSidebarFiltersScrollClass);
            panel.Add(_nativeSearchSidebarFiltersScroll);

            RefreshNativeSearchSidebarMode(true);
            panel.schedule.Execute(RefreshNativeSearchSidebarFilters).Every(350);
            return panel;
        }

        internal static bool IsNativeSearchFilterSidebarMode(int mode)
        {
            return mode == SearchSidebarFiltersMode;
        }

        private static bool IsNativeSearchFilterSidebarMode()
        {
            return IsNativeSearchFilterSidebarMode(AI.Config.searchLeftSideBarHierarchy);
        }

        private void RefreshNativeSearchSidebarMode(bool forceFilterRebuild = false)
        {
            bool showFilters = IsNativeSearchFilterSidebarMode();
            if (_nativeSearchHierarchyTreeView != null)
            {
                _nativeSearchHierarchyTreeView.style.display = showFilters ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (_nativeSearchHierarchyEmpty != null)
            {
                _nativeSearchHierarchyEmpty.style.display = DisplayStyle.None;
            }
            if (_nativeSearchSidebarFiltersScroll != null)
            {
                _nativeSearchSidebarFiltersScroll.style.display = showFilters ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (showFilters)
            {
                _requireHierarchyRebuild = false;
                RefreshNativeSearchSidebarFilters(forceFilterRebuild);
            }
            else
            {
                CaptureNativeSearchSidebarFiltersScroll();
                RefreshNativeSearchHierarchy();
            }
            RefreshNativeSearchHierarchyState();
        }

        private VisualElement CreateNativeHierarchyRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(SearchHierarchyRowClass);

            Label name = new Label {name = "hierarchy-name"};
            name.AddToClassList(SearchHierarchyNameClass);
            row.Add(name);

            Label count = new Label {name = "hierarchy-count"};
            count.AddToClassList(SearchHierarchyCountClass);
            row.Add(count);
            return row;
        }

        private void BindNativeHierarchyRow(VisualElement row, int index)
        {
            if (_nativeSearchHierarchyTreeView == null) return;

            HierarchyTreeElement element = _nativeSearchHierarchyTreeView.GetItemDataForIndex<HierarchyTreeElement>(index);
            Label name = row.Q<Label>("hierarchy-name");
            name.text = element?.TreeName ?? string.Empty;
            name.tooltip = name.text;
            row.Q<Label>("hierarchy-count").text = element != null && element.MatchCount > 0
                ? $"{element.MatchCount:N0}"
                : string.Empty;
            row.EnableInClassList(SearchHierarchyActiveClass, IsActiveHierarchyElement(element));
        }

        private void OnNativeHierarchySelectionChanged(IEnumerable<object> selectedItems)
        {
            if (_suppressNativeSearchHierarchySelection) return;

            HierarchyTreeElement element = selectedItems?.OfType<HierarchyTreeElement>().FirstOrDefault();
            if (element == null || string.IsNullOrEmpty(element.FilterKey)) return;

            ApplyHierarchyFilter(element);
        }

        private void RefreshNativeSearchHierarchy()
        {
            if (_nativeSearchHierarchyTreeView == null) return;
            if (IsNativeSearchFilterSidebarMode())
            {
                _requireHierarchyRebuild = false;
                RefreshNativeSearchSidebarMode();
                return;
            }

            List<HierarchyTreeElement> elements = BuildHierarchyElements();
            _hierarchyTreeModel = new TreeModel<HierarchyTreeElement>(elements);

            _suppressNativeSearchHierarchySelection = true;
            try
            {
                _nativeSearchHierarchyTreeView.ClearSelection();
                _nativeSearchHierarchyTreeView.SetRootItems(CreateNativeHierarchyItems(_hierarchyTreeModel.Root));
                _nativeSearchHierarchyTreeView.Rebuild();
            }
            finally
            {
                _suppressNativeSearchHierarchySelection = false;
            }

            bool hasItems = _hierarchyTreeModel.Root?.Children != null && _hierarchyTreeModel.Root.Children.Count > 0;
            _nativeSearchHierarchyTreeView.style.display = hasItems ? DisplayStyle.Flex : DisplayStyle.None;
            if (_nativeSearchHierarchyEmpty != null)
            {
                _nativeSearchHierarchyEmpty.style.display = hasItems ? DisplayStyle.None : DisplayStyle.Flex;
            }

            _requireHierarchyRebuild = false;
            RefreshNativeSearchHierarchyState();
        }

        private List<TreeViewItemData<HierarchyTreeElement>> CreateNativeHierarchyItems(TreeElement parent)
        {
            List<TreeViewItemData<HierarchyTreeElement>> items = new List<TreeViewItemData<HierarchyTreeElement>>();
            if (parent?.Children == null) return items;

            foreach (TreeElement child in parent.Children)
            {
                if (child is HierarchyTreeElement element)
                {
                    items.Add(new TreeViewItemData<HierarchyTreeElement>(
                        element.TreeId,
                        element,
                        CreateNativeHierarchyItems(element)));
                }
            }
            return items;
        }

        private void RefreshNativeSearchHierarchyState()
        {
            if (_nativeSearchHierarchyClearButton != null)
            {
                _nativeSearchHierarchyClearButton.style.display = string.IsNullOrEmpty(_activeHierarchyFilter)
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }
            _nativeSearchHierarchyTreeView?.RefreshItems();
        }

        private void RefreshNativeSearchSidebarFilters()
        {
            RefreshNativeSearchSidebarFilters(false);
        }

        private void RefreshNativeSearchSidebarFilters(bool forceRebuild)
        {
            if (_nativeSearchSidebarFiltersScroll == null || !IsNativeSearchFilterSidebarMode()) return;

            int stateHash = GetNativeSearchFilterStateHash();
            if (!forceRebuild && _nativeSearchSidebarFiltersStateHash == stateHash) return;

            CaptureNativeSearchSidebarFiltersScroll();
            _nativeSearchSidebarFiltersScroll.Clear();
            _nativeSearchSidebarFiltersContent = new VisualElement();
            _nativeSearchSidebarFiltersContent.AddToClassList(PackagesInspectorContentClass);
            _nativeSearchSidebarFiltersContent.AddToClassList(PackagesDetailRootClass);
            _nativeSearchSidebarFiltersContent.AddToClassList(SearchSidebarFiltersContentClass);
            _nativeSearchSidebarFiltersContent.Add(CreateNativeSearchFilters(SearchFilterViewHost.Sidebar));
            AssetInventoryUITK.HideEmptySections(_nativeSearchSidebarFiltersContent);
            _nativeSearchSidebarFiltersScroll.Add(_nativeSearchSidebarFiltersContent);
            _nativeSearchSidebarFiltersStateHash = stateHash;
            _nativeSearchSidebarFiltersScroll.schedule.Execute(() =>
            {
                if (_nativeSearchSidebarFiltersScroll != null)
                {
                    _nativeSearchSidebarFiltersScroll.scrollOffset = _leftSidebarScrollPos;
                }
            }).ExecuteLater(0);
        }

        private void ScheduleNativeSearchSidebarFiltersRebuild()
        {
            _nativeSearchSidebarFiltersStateHash = int.MinValue;
            _nativeSearchSidebarFiltersScroll?.schedule.Execute(RefreshNativeSearchSidebarFilters).ExecuteLater(0);
        }

        private void CaptureNativeSearchSidebarFiltersScroll()
        {
            if (_nativeSearchSidebarFiltersScroll != null)
            {
                _leftSidebarScrollPos = _nativeSearchSidebarFiltersScroll.scrollOffset;
            }
        }

        private bool IsActiveHierarchyElement(HierarchyTreeElement element)
        {
            return element != null &&
                string.Equals(element.FilterKey, _activeHierarchyFilter, StringComparison.Ordinal) &&
                string.Equals(element.FilterValue, _activeHierarchyFilterValue, StringComparison.Ordinal);
        }

        private List<HierarchyTreeElement> BuildHierarchyElements()
        {
            return HierarchyBuilder.Build(_files, AI.Config.searchLeftSideBarHierarchy);
        }

        private void ApplyHierarchyFilter(HierarchyTreeElement element)
        {
            if (string.IsNullOrEmpty(_activeHierarchyFilter))
            {
                _searchPhraseBeforeHierarchyFilter = _searchPhrase;
                _previousSearchPhraseBeforeHierarchyFilter = _previousSearchPhrase;
            }

            _activeHierarchyFilter = element.FilterKey;
            _activeHierarchyFilterValue = element.FilterValue;

            switch (element.FilterKey)
            {
                case "Path":
                    _searchPhrase = $"=Path like '{element.FilterValue}%'";
                    _previousSearchPhrase = _searchPhrase;
                    break;

                case "Category":
                    _selectedCategory = FindIndexByValue(_categoryNames, element.FilterValue, splitPath: false);
                    break;

                case "Publisher":
                    _selectedPublisher = FindIndexByValue(_publisherNames, element.FilterValue, splitPath: true);
                    break;

                case "Package":
                    _selectedAsset = FindIndexByValue(_assetNames, element.FilterValue, splitPath: true);
                    break;

                case "Type":
                    int typeIdx = Array.FindIndex(_types, t => t.Equals(element.FilterValue, StringComparison.OrdinalIgnoreCase) ||
                        t.EndsWith("/" + element.FilterValue, StringComparison.OrdinalIgnoreCase));
                    if (typeIdx >= 0) AI.Config.searchType = typeIdx;
                    break;
            }

            _requireSearchUpdate = true;
            _curPage = 1;
            RefreshNativeSearchHierarchyState();
        }

        private void ClearHierarchyFilter()
        {
            bool restoreSearchPhrase = string.Equals(_activeHierarchyFilter, "Path", StringComparison.Ordinal);
            _activeHierarchyFilter = null;
            _activeHierarchyFilterValue = null;
            _nativeSearchHierarchyTreeView?.ClearSelection();

            ResetSearch(true, false);
            if (restoreSearchPhrase)
            {
                _searchPhrase = _searchPhraseBeforeHierarchyFilter ?? string.Empty;
                _previousSearchPhrase = _previousSearchPhraseBeforeHierarchyFilter ?? _searchPhrase;
            }
            _searchPhraseBeforeHierarchyFilter = null;
            _previousSearchPhraseBeforeHierarchyFilter = null;
            _requireSearchUpdate = true;
            RefreshNativeSearchHierarchyState();
        }
    }
}
