using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public sealed class CommonSearchableLineList : VisualElement
    {
        public const string RootClass = "common-searchable-line-list";
        public const string ToolbarClass = "common-searchable-line-list__toolbar";
        public const string SearchClass = "common-searchable-line-list__search";
        public const string StatusClass = "common-searchable-line-list__status";
        public const string EmptyClass = "common-searchable-line-list__empty";
        public const string ListClass = "common-searchable-line-list__list";
        public const string RowClass = "common-searchable-line-list__row";
        public const string RowLabelClass = "common-searchable-line-list__row-label";

        private const float DefaultItemHeight = 24f;

        private readonly List<string> _lines = new List<string>();
        private readonly List<int> _filteredIndices = new List<int>();
        private readonly Action<int, string> _onLineClick;
        private readonly string _emptyText;
        private readonly string _noMatchesText;
        private readonly float _itemHeight;
        private string _searchText = string.Empty;
        private ToolbarSearchField _searchField;
        private Label _statusLabel;
        private HelpBox _emptyState;
        private ListView _listView;

        public CommonSearchableLineList(
            IEnumerable<string> lines,
            Action<int, string> onLineClick = null,
            string emptyText = "No items to display",
            string noMatchesText = "No items match the search",
            float itemHeight = DefaultItemHeight)
        {
            _onLineClick = onLineClick;
            _emptyText = emptyText;
            _noMatchesText = noMatchesText;
            _itemHeight = Mathf.Max(18f, itemHeight);

            CommonUITK.AddClasses(this, RootClass);
            style.flexGrow = 1f;
            style.minHeight = 0f;

            Build(_itemHeight);
            SetItems(lines);
        }

        public void SetItems(IEnumerable<string> lines)
        {
            _lines.Clear();
            if (lines != null)
            {
                _lines.AddRange(lines);
            }

            RefreshFilteredItems(false);
        }

        public void FocusSearchField()
        {
            _searchField?.Focus();
        }

        private void Build(float itemHeight)
        {
            VisualElement toolbar = CommonUITK.CreateContainer(ToolbarClass);
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.minWidth = 0f;
            toolbar.style.marginBottom = 6f;
            Add(toolbar);

            _searchField = new ToolbarSearchField
            {
                value = _searchText
            };
            CommonUITK.AddClasses(_searchField, SearchClass);
            _searchField.style.flexGrow = 1f;
            _searchField.style.flexShrink = 1f;
            _searchField.style.minWidth = 0f;
            _searchField.style.width = 0f;
            _searchField.RegisterValueChangedCallback(evt =>
            {
                _searchText = evt.newValue ?? string.Empty;
                RefreshFilteredItems(true);
            });
            toolbar.Add(_searchField);

            _statusLabel = CommonUITK.CreateLabel(string.Empty, StatusClass);
            _statusLabel.style.marginLeft = 8f;
            _statusLabel.style.flexShrink = 0f;
            _statusLabel.style.width = 62f;
            _statusLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            toolbar.Add(_statusLabel);

            _emptyState = CommonUITK.CreateHelpBox(string.Empty, HelpBoxMessageType.Info, EmptyClass);
            _emptyState.style.display = DisplayStyle.None;
            _emptyState.style.marginTop = 0f;
            _emptyState.style.marginBottom = 0f;
            Add(_emptyState);

            _listView = new ListView(_filteredIndices, itemHeight, CreateRow, BindRow)
            {
                fixedItemHeight = itemHeight,
                horizontalScrollingEnabled = false,
                selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                showBorder = true,
                showBoundCollectionSize = false,
                showFoldoutHeader = false,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight
            };
            CommonUITK.AddClasses(_listView, ListClass);
            _listView.style.flexGrow = 1f;
            _listView.style.minHeight = 0f;
            Add(_listView);
        }

        private VisualElement CreateRow()
        {
            LineRow row = new LineRow(_itemHeight);
            row.RegisterCallback<MouseDownEvent>(OnRowMouseDown);
            return row;
        }

        private void BindRow(VisualElement element, int index)
        {
            if (!(element is LineRow row)) return;

            int originalIndex = index >= 0 && index < _filteredIndices.Count ? _filteredIndices[index] : -1;
            string line = originalIndex >= 0 && originalIndex < _lines.Count ? _lines[originalIndex] ?? string.Empty : string.Empty;
            row.Label.text = line;
            row.tooltip = line;
            row.userData = originalIndex;
        }

        private void OnRowMouseDown(MouseDownEvent evt)
        {
            if (evt.button != 0 || _onLineClick == null || !(evt.currentTarget is VisualElement row)) return;
            if (!(row.userData is int originalIndex) || originalIndex < 0 || originalIndex >= _lines.Count) return;

            _onLineClick.Invoke(originalIndex, _lines[originalIndex]);
            evt.StopPropagation();
        }

        private void RefreshFilteredItems(bool scrollToTop)
        {
            UpdateFilteredIndices();

            if (_statusLabel != null)
            {
                _statusLabel.text = string.IsNullOrWhiteSpace(_searchText)
                    ? FormatItemCount(_lines.Count)
                    : $"Showing {_filteredIndices.Count:N0} of {_lines.Count:N0}";
            }

            bool hasItems = _filteredIndices.Count > 0;
            if (_emptyState != null)
            {
                _emptyState.text = string.IsNullOrWhiteSpace(_searchText) ? _emptyText : _noMatchesText;
                _emptyState.style.display = hasItems ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_listView != null)
            {
                _listView.style.display = hasItems ? DisplayStyle.Flex : DisplayStyle.None;
                _listView.RefreshItems();
                if (scrollToTop && hasItems)
                {
                    _listView.ScrollToItem(0);
                }
            }
        }

        private void UpdateFilteredIndices()
        {
            _filteredIndices.Clear();
            if (_lines.Count == 0) return;

            string search = _searchText?.Trim();
            for (int i = 0; i < _lines.Count; i++)
            {
                string line = _lines[i];
                if (string.IsNullOrWhiteSpace(search) ||
                    (!string.IsNullOrEmpty(line) && line.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    _filteredIndices.Add(i);
                }
            }
        }

        private static string FormatItemCount(int count)
        {
            return count == 1 ? "1 item" : $"{count:N0} items";
        }

        private sealed class LineRow : VisualElement
        {
            public readonly Label Label;

            public LineRow(float itemHeight)
            {
                CommonUITK.AddClasses(this, RowClass);
                style.flexDirection = FlexDirection.Row;
                style.alignItems = Align.Center;
                style.minWidth = 0f;
                style.height = itemHeight;
                style.paddingLeft = 6f;
                style.paddingRight = 6f;

                Label = CommonUITK.CreateLabel(string.Empty, RowLabelClass);
                Label.style.flexGrow = 1f;
                Label.style.minWidth = 0f;
                Label.style.unityTextAlign = TextAnchor.MiddleLeft;
                Label.style.whiteSpace = WhiteSpace.NoWrap;
                Label.style.overflow = Overflow.Hidden;
                Label.style.textOverflow = TextOverflow.Ellipsis;
                Add(Label);
            }
        }
    }
}
