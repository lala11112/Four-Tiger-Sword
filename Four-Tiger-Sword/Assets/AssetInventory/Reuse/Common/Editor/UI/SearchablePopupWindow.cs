using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public sealed class SearchablePopupWindow : EditorWindow
    {
        private const float ItemHeight = 22f;
        private const float MinimumWidth = 200f;
        private const float DefaultHeight = 400f;

        private readonly List<int> _visibleIndices = new List<int>();
        private SearchablePopup.PopupItem[] _popupItems = Array.Empty<SearchablePopup.PopupItem>();
        private string[] _items = Array.Empty<string>();
        private int _selectedIndex;
        private Action<int> _callback;
        private string _searchText = string.Empty;
        private string _currentHierarchyPath = string.Empty;
        private bool _showBracketedValues;
        private bool _treatSlashLiterally;
        private ToolbarSearchField _searchField;
        private Button _breadcrumb;
        private HelpBox _emptyState;
        private ListView _listView;

        public static SearchablePopupWindow ShowAsDropDown(
            Rect anchor,
            SearchablePopup.PopupItem[] items,
            int selectedIndex,
            Action<int> callback,
            float width = 300f,
            float maxHeight = DefaultHeight,
            bool showBracketedValues = false,
            bool treatSlashLiterally = false)
        {
            SearchablePopupWindow window = CreateInstance<SearchablePopupWindow>();
            window.Init(items, selectedIndex, callback, showBracketedValues, treatSlashLiterally);
            CommonUITK.ApplyDropDownWindowStyle(window);
            window.ShowAsDropDown(anchor, new Vector2(Mathf.Max(width, MinimumWidth), Mathf.Max(120f, maxHeight)));
            return window;
        }

        public void Init(
            SearchablePopup.PopupItem[] items,
            int selectedIndex,
            Action<int> callback,
            bool showBracketedValues = false,
            bool treatSlashLiterally = false)
        {
            _popupItems = items ?? Array.Empty<SearchablePopup.PopupItem>();
            _items = new string[_popupItems.Length];
            for (int i = 0; i < _popupItems.Length; i++)
            {
                _items[i] = _popupItems[i].Text ?? string.Empty;
            }

            _selectedIndex = selectedIndex;
            _callback = callback;
            _showBracketedValues = showBracketedValues;
            _treatSlashLiterally = treatSlashLiterally;
            _searchText = string.Empty;
            _currentHierarchyPath = string.Empty;
            titleContent = new GUIContent("Search");
            UpdateVisibleIndices();
            BuildIfReady();
        }

        private void CreateGUI()
        {
            Build();
        }

        private void BuildIfReady()
        {
            if (rootVisualElement != null && rootVisualElement.panel != null)
            {
                Build();
            }
        }

        private void Build()
        {
            VisualElement root = rootVisualElement;
            if (root == null) return;

            root.Clear();
            root.style.flexGrow = 1f;
            CommonUITK.ApplyWindowLayout(root);
            root.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            _searchField = new ToolbarSearchField
            {
                value = _searchText ?? string.Empty
            };
            _searchField.style.alignSelf = Align.Stretch;
            _searchField.style.width = Length.Percent(100f);
            _searchField.style.marginBottom = 4f;
            _searchField.RegisterValueChangedCallback(evt =>
            {
                _searchText = evt.newValue ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(_searchText))
                {
                    _currentHierarchyPath = string.Empty;
                }
                RefreshList(true);
            });
            root.Add(_searchField);

            _breadcrumb = new Button(NavigateBack);
            _breadcrumb.style.alignSelf = Align.Stretch;
            _breadcrumb.style.unityTextAlign = TextAnchor.MiddleLeft;
            _breadcrumb.style.marginLeft = 0f;
            _breadcrumb.style.marginRight = 0f;
            _breadcrumb.style.marginBottom = 4f;
            root.Add(_breadcrumb);

            _emptyState = new HelpBox("No items match the search", HelpBoxMessageType.Info);
            _emptyState.style.display = DisplayStyle.None;
            root.Add(_emptyState);

            _listView = new ListView(_visibleIndices, ItemHeight, CreateRow, BindRow)
            {
                fixedItemHeight = ItemHeight,
                horizontalScrollingEnabled = true,
                selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                showBorder = true,
                showBoundCollectionSize = false,
                showFoldoutHeader = false,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight
            };
            _listView.style.flexGrow = 1f;
            _listView.selectionChanged += OnSelectionChanged;
            root.Add(_listView);

            RefreshList(false);
            root.schedule.Execute(() => _searchField?.Focus()).ExecuteLater(0);
        }

        private VisualElement CreateRow()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = ItemHeight;
            row.style.paddingLeft = 5f;
            row.style.paddingRight = 4f;

            VisualElement divider = new VisualElement();
            divider.name = "divider";
            divider.style.height = 1f;
            divider.style.flexGrow = 1f;
            divider.style.backgroundColor = EditorGUIUtility.isProSkin
                ? new Color(0.55f, 0.55f, 0.55f, 0.45f)
                : new Color(0.25f, 0.25f, 0.25f, 0.45f);
            divider.style.display = DisplayStyle.None;
            row.Add(divider);

            Label label = new Label {name = "label"};
            label.style.flexGrow = 1f;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.textOverflow = TextOverflow.Ellipsis;
            row.Add(label);

            Label arrow = new Label {name = "arrow", text = ">"};
            arrow.style.width = 16f;
            arrow.style.unityTextAlign = TextAnchor.MiddleRight;
            arrow.style.display = DisplayStyle.None;
            row.Add(arrow);

            row.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0 || !(row.userData is int listIndex)) return;

                SelectListIndex(listIndex);
                evt.StopPropagation();
            });

            return row;
        }

        private void BindRow(VisualElement element, int listIndex)
        {
            int itemIndex = listIndex >= 0 && listIndex < _visibleIndices.Count ? _visibleIndices[listIndex] : -1;
            SearchablePopup.PopupItem popupItem = GetPopupItem(itemIndex);
            string itemText = itemIndex >= 0 && itemIndex < _items.Length ? _items[itemIndex] ?? string.Empty : string.Empty;
            bool isSeparator = string.IsNullOrEmpty(itemText);
            bool isSelected = itemIndex == _selectedIndex;
            bool isParent = !isSeparator && IsParentItem(itemText);

            element.userData = listIndex;
            element.SetEnabled(!isSeparator);
            ResetRowStyles(element);

            VisualElement divider = element.Q<VisualElement>("divider");
            Label label = element.Q<Label>("label");
            Label arrow = element.Q<Label>("arrow");

            divider.style.display = isSeparator ? DisplayStyle.Flex : DisplayStyle.None;
            label.style.display = isSeparator ? DisplayStyle.None : DisplayStyle.Flex;
            arrow.style.display = isParent ? DisplayStyle.Flex : DisplayStyle.None;

            if (isSeparator)
            {
                return;
            }

            string displayText = GetDisplayText(itemText);
            if (!_showBracketedValues)
            {
                displayText = RemoveBracketedValues(displayText);
            }
            label.text = displayText;
            label.tooltip = GetDisplayText(itemText);

            Color textColor = EditorGUIUtility.isProSkin ? new Color(0.82f, 0.82f, 0.82f, 1f) : Color.black;
            if (popupItem.TintBackground)
            {
                Color background = popupItem.BackgroundColor;
                element.style.backgroundColor = background;
                textColor = CommonUIStyles.GetHSPColor(background);
                if (isSelected)
                {
                    element.style.borderBottomWidth = 1f;
                    element.style.borderTopWidth = 1f;
                    element.style.borderLeftWidth = 1f;
                    element.style.borderRightWidth = 1f;
                    element.style.borderBottomColor = textColor;
                    element.style.borderTopColor = textColor;
                    element.style.borderLeftColor = textColor;
                    element.style.borderRightColor = textColor;
                }
            }
            else if (isSelected)
            {
                element.style.backgroundColor = EditorGUIUtility.isProSkin
                    ? new Color(0.22f, 0.38f, 0.58f, 1f)
                    : new Color(0.55f, 0.72f, 0.95f, 1f);
                textColor = Color.white;
            }

            label.style.color = textColor;
            arrow.style.color = textColor;
        }

        private void ResetRowStyles(VisualElement element)
        {
            element.style.backgroundColor = StyleKeyword.Null;
            element.style.borderBottomWidth = 0f;
            element.style.borderTopWidth = 0f;
            element.style.borderLeftWidth = 0f;
            element.style.borderRightWidth = 0f;
            element.style.borderBottomColor = StyleKeyword.Null;
            element.style.borderTopColor = StyleKeyword.Null;
            element.style.borderLeftColor = StyleKeyword.Null;
            element.style.borderRightColor = StyleKeyword.Null;
        }

        private void RefreshList(bool resetSelection)
        {
            UpdateVisibleIndices();
            RefreshBreadcrumb();

            bool hasItems = _visibleIndices.Count > 0;
            if (_emptyState != null)
            {
                _emptyState.style.display = hasItems ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (_listView == null) return;

            _listView.style.display = hasItems ? DisplayStyle.Flex : DisplayStyle.None;
            _listView.RefreshItems();
            if (resetSelection && hasItems)
            {
                int firstSelectable = GetFirstSelectableListIndex();
                if (firstSelectable >= 0)
                {
                    _listView.SetSelection(firstSelectable);
                    _listView.ScrollToItem(firstSelectable);
                }
                else
                {
                    _listView.ClearSelection();
                }
            }
        }

        private void RefreshBreadcrumb()
        {
            if (_breadcrumb == null) return;

            bool show = !_treatSlashLiterally &&
                string.IsNullOrWhiteSpace(_searchText) &&
                !string.IsNullOrEmpty(_currentHierarchyPath);
            _breadcrumb.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            _breadcrumb.text = show ? "< " + _currentHierarchyPath : string.Empty;
        }

        private void UpdateVisibleIndices()
        {
            _visibleIndices.Clear();
            if (_items == null || _items.Length == 0) return;

            if (_treatSlashLiterally)
            {
                AddSearchMatches();
            }
            else if (!string.IsNullOrWhiteSpace(_searchText))
            {
                AddSearchMatches();
            }
            else if (string.IsNullOrEmpty(_currentHierarchyPath))
            {
                HashSet<string> seenPrefixes = new HashSet<string>();
                for (int i = 0; i < _items.Length; i++)
                {
                    string item = _items[i];
                    if (string.IsNullOrEmpty(item))
                    {
                        _visibleIndices.Add(i);
                        continue;
                    }

                    int slashIndex = item.IndexOf('/');
                    if (slashIndex >= 0)
                    {
                        string prefix = item.Substring(0, slashIndex);
                        if (seenPrefixes.Add(prefix))
                        {
                            _visibleIndices.Add(i);
                        }
                    }
                    else
                    {
                        _visibleIndices.Add(i);
                    }
                }
            }
            else
            {
                string pathPrefix = _currentHierarchyPath + "/";
                HashSet<string> childPrefixes = new HashSet<string>();
                for (int i = 0; i < _items.Length; i++)
                {
                    string item = _items[i];
                    if (string.IsNullOrEmpty(item))
                    {
                        _visibleIndices.Add(i);
                        continue;
                    }

                    if (!item.StartsWith(pathPrefix, StringComparison.Ordinal)) continue;

                    string remaining = item.Substring(pathPrefix.Length);
                    int nextSlash = remaining.IndexOf('/');
                    if (nextSlash >= 0)
                    {
                        string nextLevelPrefix = remaining.Substring(0, nextSlash);
                        if (childPrefixes.Add(nextLevelPrefix))
                        {
                            _visibleIndices.Add(i);
                        }
                    }
                    else
                    {
                        _visibleIndices.Add(i);
                    }
                }
            }

            PruneVisibleSeparators();
        }

        private void PruneVisibleSeparators()
        {
            for (int i = _visibleIndices.Count - 1; i >= 0; i--)
            {
                if (!IsVisibleSeparator(i)) continue;

                if (i == 0 || i == _visibleIndices.Count - 1 || IsVisibleSeparator(i - 1))
                {
                    _visibleIndices.RemoveAt(i);
                }
            }
        }

        private bool IsVisibleSeparator(int visibleIndex)
        {
            if (visibleIndex < 0 || visibleIndex >= _visibleIndices.Count) return true;

            int itemIndex = _visibleIndices[visibleIndex];
            return itemIndex < 0 || itemIndex >= _items.Length || string.IsNullOrEmpty(_items[itemIndex]);
        }

        private void AddSearchMatches()
        {
            string searchLower = _searchText?.ToLowerInvariant() ?? string.Empty;
            for (int i = 0; i < _items.Length; i++)
            {
                string item = _items[i] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(searchLower) || item.ToLowerInvariant().Contains(searchLower))
                {
                    _visibleIndices.Add(i);
                }
            }
        }

        private string GetDisplayText(string item)
        {
            if (_treatSlashLiterally || string.IsNullOrEmpty(item)) return item;

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                int lastSlashIndex = item.LastIndexOf('/');
                return lastSlashIndex >= 0 && lastSlashIndex < item.Length - 1 ? item.Substring(lastSlashIndex + 1) : item;
            }

            if (string.IsNullOrEmpty(_currentHierarchyPath))
            {
                int slashIndex = item.IndexOf('/');
                return slashIndex >= 0 ? item.Substring(0, slashIndex) : item;
            }

            string pathPrefix = _currentHierarchyPath + "/";
            if (!item.StartsWith(pathPrefix, StringComparison.Ordinal)) return item;

            string remaining = item.Substring(pathPrefix.Length);
            int nextSlash = remaining.IndexOf('/');
            return nextSlash >= 0 ? remaining.Substring(0, nextSlash) : remaining;
        }

        private bool IsParentItem(string itemText)
        {
            if (_treatSlashLiterally || string.IsNullOrEmpty(itemText) || !string.IsNullOrWhiteSpace(_searchText)) return false;

            if (string.IsNullOrEmpty(_currentHierarchyPath))
            {
                return itemText.IndexOf('/') >= 0;
            }

            string pathPrefix = _currentHierarchyPath + "/";
            if (!itemText.StartsWith(pathPrefix, StringComparison.Ordinal)) return false;

            string remaining = itemText.Substring(pathPrefix.Length);
            return remaining.IndexOf('/') >= 0;
        }

        private void SelectListIndex(int listIndex)
        {
            if (listIndex < 0 || listIndex >= _visibleIndices.Count) return;

            int itemIndex = _visibleIndices[listIndex];
            string itemText = _items[itemIndex];
            if (string.IsNullOrEmpty(itemText)) return;

            if (_treatSlashLiterally || !string.IsNullOrWhiteSpace(_searchText))
            {
                SelectItem(itemIndex);
                return;
            }

            if (IsParentItem(itemText))
            {
                NavigateInto(itemText);
                return;
            }

            SelectItem(itemIndex);
        }

        private void NavigateInto(string itemText)
        {
            if (string.IsNullOrEmpty(_currentHierarchyPath))
            {
                int slashIndex = itemText.IndexOf('/');
                if (slashIndex < 0) return;

                _currentHierarchyPath = itemText.Substring(0, slashIndex);
            }
            else
            {
                string pathPrefix = _currentHierarchyPath + "/";
                if (!itemText.StartsWith(pathPrefix, StringComparison.Ordinal)) return;

                string remaining = itemText.Substring(pathPrefix.Length);
                int nextSlash = remaining.IndexOf('/');
                if (nextSlash < 0) return;

                _currentHierarchyPath += "/" + remaining.Substring(0, nextSlash);
            }

            RefreshList(true);
        }

        private void NavigateBack()
        {
            if (string.IsNullOrEmpty(_currentHierarchyPath)) return;

            int lastSlash = _currentHierarchyPath.LastIndexOf('/');
            _currentHierarchyPath = lastSlash >= 0 ? _currentHierarchyPath.Substring(0, lastSlash) : string.Empty;
            RefreshList(true);
        }

        private void SelectItem(int itemIndex)
        {
            _callback?.Invoke(itemIndex);
            Close();
        }

        private int GetFirstSelectableListIndex()
        {
            for (int i = 0; i < _visibleIndices.Count; i++)
            {
                int itemIndex = _visibleIndices[i];
                if (itemIndex >= 0 && itemIndex < _items.Length && !string.IsNullOrEmpty(_items[itemIndex]))
                {
                    return i;
                }
            }

            return -1;
        }

        private void OnSelectionChanged(IEnumerable<object> selectedItems)
        {
            if (_listView == null || _listView.selectedIndex < 0) return;

            int itemIndex = _listView.selectedIndex < _visibleIndices.Count ? _visibleIndices[_listView.selectedIndex] : -1;
            if (itemIndex >= 0 && itemIndex < _items.Length && string.IsNullOrEmpty(_items[itemIndex]))
            {
                _listView.ClearSelection();
            }
        }

        private void MoveSelection(int delta)
        {
            if (_listView == null || _visibleIndices.Count == 0) return;

            int index = _listView.selectedIndex;
            if (index < 0)
            {
                index = delta > 0 ? -1 : _visibleIndices.Count;
            }

            for (int i = 0; i < _visibleIndices.Count; i++)
            {
                index = Mathf.Clamp(index + delta, 0, _visibleIndices.Count - 1);
                int itemIndex = _visibleIndices[index];
                if (itemIndex >= 0 && itemIndex < _items.Length && !string.IsNullOrEmpty(_items[itemIndex]))
                {
                    _listView.SetSelection(index);
                    _listView.ScrollToItem(index);
                    return;
                }
            }
        }

        private SearchablePopup.PopupItem GetPopupItem(int index)
        {
            if (_popupItems != null && index >= 0 && index < _popupItems.Length)
            {
                return _popupItems[index];
            }

            return new SearchablePopup.PopupItem(index >= 0 && index < _items.Length ? _items[index] : string.Empty);
        }

        private static string RemoveBracketedValues(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            StringBuilder result = new StringBuilder();
            int index = 0;
            while (index < text.Length)
            {
                if (text[index] == '[')
                {
                    int endIndex = text.IndexOf(']', index);
                    if (endIndex >= 0)
                    {
                        index = endIndex + 1;
                        continue;
                    }
                }

                result.Append(text[index]);
                index++;
            }

            return result.ToString();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Escape:
                    if (!_treatSlashLiterally &&
                        string.IsNullOrWhiteSpace(_searchText) &&
                        !string.IsNullOrEmpty(_currentHierarchyPath))
                    {
                        NavigateBack();
                    }
                    else
                    {
                        Close();
                    }
                    evt.StopPropagation();
                    break;

                case KeyCode.DownArrow:
                    MoveSelection(1);
                    evt.StopPropagation();
                    break;

                case KeyCode.UpArrow:
                    MoveSelection(-1);
                    evt.StopPropagation();
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                case KeyCode.RightArrow:
                    if (_listView != null && _listView.selectedIndex >= 0)
                    {
                        SelectListIndex(_listView.selectedIndex);
                        evt.StopPropagation();
                    }
                    break;

                case KeyCode.LeftArrow:
                    if (!string.IsNullOrEmpty(_currentHierarchyPath))
                    {
                        NavigateBack();
                        evt.StopPropagation();
                    }
                    break;
            }
        }
    }
}
