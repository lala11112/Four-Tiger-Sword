using System;
using System.Collections.Generic;
using System.Linq;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed class WorkspaceUI : BasicEditorUI
    {
        private const float SearchRowHeight = 46f;

        private Workspace _workspace;
        private List<WorkspaceSearch> _searches;
        private List<SavedSearch> _savedSearches;
        private Action<Workspace> _onSave;
        private CommonReorderableListView<WorkspaceSearch> _searchesList;
        private Button _updateButton;

        public static WorkspaceUI ShowWindow()
        {
            WorkspaceUI window = GetWindow<WorkspaceUI>("Workspace Editor");
            window.minSize = new Vector2(360, 260);
            return window;
        }

        public void Init(Workspace workspace, Action<Workspace> onSave = null)
        {
            _workspace = workspace;
            _searches = _workspace?.LoadSearches() ?? new List<WorkspaceSearch>();
            _onSave = onSave;
            LoadSavedSearches();
            BuildContent();
        }

        private void CreateGUI()
        {
            BuildContent();
        }

        private void BuildContent()
        {
            VisualElement root = rootVisualElement;
            if (root == null) return;

            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);

            if (_workspace == null)
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("Select a workspace before editing.", MessageType.Info));
                return;
            }

            if (_searches == null)
            {
                _searches = _workspace.LoadSearches() ?? new List<WorkspaceSearch>();
            }
            if (_savedSearches == null)
            {
                LoadSavedSearches();
            }

            root.Add(BuildDetailsSection());
            root.Add(BuildSearchesSection());
            root.Add(AssetInventoryUITK.CreateFlexibleSpacer());

            _updateButton = AssetInventoryUITK.CreatePrimaryButton("Update", Save);
            _updateButton.SetEnabled(CanSave());

            VisualElement footer = AssetInventoryUITK.CreateWindowFooter();
            footer.Add(_updateButton);
            root.Add(footer);
        }

        private void LoadSavedSearches()
        {
            _savedSearches = DBAdapter.DB.Table<SavedSearch>().ToList();
            _savedSearches.Sort((left, right) => string.Compare(left?.Name, right?.Name, StringComparison.OrdinalIgnoreCase));
        }

        private VisualElement BuildDetailsSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Workspace");

            TextField nameField = new TextField
            {
                value = _workspace.Name ?? string.Empty
            };
            nameField.RegisterValueChangedCallback(evt =>
            {
                _workspace.Name = evt.newValue;
                _updateButton?.SetEnabled(CanSave());
            });
            section.Add(AssetInventoryUITK.CreateFieldRow("Name", nameField));

            return section;
        }

        private VisualElement BuildSearchesSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Saved Searches to Show");

            Label hint = AssetInventoryUITK.CreateCopyLabel("Drag rows by the handle to change their order.");
            hint.AddToClassList("ai-list-hint");
            section.Add(hint);

            if (_savedSearches.Count == 0)
            {
                Button openSearch = AssetInventoryUITK.CreatePrimaryButton("Open Search", () =>
                {
                    AI.Config.tab = (int)AssetInventoryTab.Search;
                    AI.SaveConfig();
                    MenuIntegration.ShowWindow();
                    GetWindow<IndexUI>("Asset Inventory").Focus();
                });
                section.Add(AssetInventoryUITK.CreateEmptyState(
                    "No saved searches yet",
                    "Save a search in Asset Inventory, then return here to add it to this workspace.",
                    openSearch));
            }

            _searchesList = new CommonReorderableListView<WorkspaceSearch>(
                _searches,
                CreateSearchRow,
                BindSearchRow,
                SearchRowHeight,
                "ai-reorderable-list",
                "ai-workspace-search-list");
            _searchesList.SetAddHandler(ShowAddSearchMenu);
            _searchesList.style.display = _savedSearches.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            section.Add(_searchesList);

            return section;
        }

        private static VisualElement CreateSearchRow()
        {
            VisualElement row = new VisualElement();
            return AssetInventoryUITK.PopulateListRow(row, string.Empty, string.Empty, collectionItem: true, extraClasses: new[] {"ai-workspace-search-row"});
        }

        private void BindSearchRow(VisualElement element, WorkspaceSearch search, int index)
        {
            SavedSearch savedSearch = _savedSearches.FirstOrDefault(s => s.Id == search.SavedSearchId);
            string title = savedSearch != null ? savedSearch.Name : $"-Unknown Search- ({search.SavedSearchId})";
            string subtitle = savedSearch != null && !string.IsNullOrWhiteSpace(savedSearch.SearchPhrase)
                ? savedSearch.SearchPhrase
                : "No search phrase";
            CommonUITK.SetTitleSubtitleRowText(element, title, subtitle);
            element.tooltip = subtitle;
        }

        private void ShowAddSearchMenu(CommonReorderableListView<WorkspaceSearch> list, Button button)
        {
            GenericMenu menu = new GenericMenu();
            foreach (SavedSearch savedSearch in _savedSearches)
            {
                SavedSearch search = savedSearch;
                string name = string.IsNullOrWhiteSpace(search.Name) ? $"-Unnamed Search- ({search.Id})" : search.Name;
                menu.AddItem(new GUIContent(name, search.SearchPhrase), false, () => AddSearch(search));
            }
            menu.ShowAsContext();
        }

        private bool CanSave()
        {
            return _workspace != null && !string.IsNullOrWhiteSpace(_workspace.Name);
        }

        private void Save()
        {
            if (!CanSave()) return;

            DBAdapter.DB.Update(_workspace);

            for (int i = 0; i < _searches.Count; i++)
            {
                WorkspaceSearch search = _searches[i];
                search.OrderIdx = i;

                if (search.Id > 0)
                {
                    DBAdapter.DB.Update(search);
                }
                else
                {
                    DBAdapter.DB.Insert(search);
                }
            }

            if (_searches.Count == 0)
            {
                DBAdapter.DB.Execute("delete from WorkspaceSearch where WorkspaceId=?", _workspace.Id);
            }
            else
            {
                DBAdapter.DB.Execute("delete from WorkspaceSearch where WorkspaceId=? and Id not in (" +
                    string.Join(",", _searches.Select(s => s.Id)) + ")", _workspace.Id);
            }

            _onSave?.Invoke(_workspace);
            Close();
        }

        private void AddSearch(SavedSearch savedSearch)
        {
            WorkspaceSearch wsSearch = new WorkspaceSearch
            {
                WorkspaceId = _workspace.Id,
                SavedSearchId = savedSearch.Id,
                OrderIdx = _searches.Count
            };

            int selectedIndex = _searchesList?.SelectedIndex ?? -1;
            int insertIndex = selectedIndex >= 0 ? selectedIndex + 1 : -1;
            _searchesList?.AddItem(wsSearch, insertIndex);
        }
    }
}
