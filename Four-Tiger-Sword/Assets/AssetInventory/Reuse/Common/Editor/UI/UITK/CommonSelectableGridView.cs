using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public enum CommonGridViewDisplayMode
    {
        Tiny,
        Compact,
        Standard,
        Detailed
    }

    public sealed class CommonSelectableGridView<T> : VisualElement
    {
        public const string RootClass = "common-selectable-grid-view";
        public const string ListClass = "common-selectable-grid-view__list";
        public const string RowClass = "common-selectable-grid-view__row";
        public const string ItemClass = "common-selectable-grid-view__item";
        public const string SelectedItemClass = "common-selectable-grid-view__item-selected";
        public const string TinyClass = "common-selectable-grid-view--tiny";
        public const string CompactClass = "common-selectable-grid-view--compact";
        public const string StandardClass = "common-selectable-grid-view--standard";
        public const string DetailedClass = "common-selectable-grid-view--detailed";

        private readonly Func<VisualElement> _makeItem;
        private readonly Action<VisualElement, T, int> _bindItem;
        private readonly RowIndexCollection _rows = new RowIndexCollection();
        private readonly HashSet<int> _selectedIndices = new HashSet<int>();

        private IList<T> _items = Array.Empty<T>();
        private int _activeIndex = -1;
        private int _selectionAnchor = -1;
        private int _columnCount = 1;
        private float _preferredTileWidth = 160f;
        private float _tileAspectRatio = 1f;
        private float _tileMargin = 4f;
        private float _actualTileWidth;
        private float _actualTileHeight;
        private float _actualRowHeight;
        private bool _fillAvailableWidth;
        private bool _displayModeInitialized;

        public ListView ListView { get; }
        public ScrollView ScrollView { get; }
        public bool AllowMultipleSelection { get; set; } = true;
        public CommonGridViewDisplayMode DisplayMode { get; private set; } = CommonGridViewDisplayMode.Standard;
        public int ActiveIndex => _activeIndex;
        public int ColumnCount => _columnCount;
        public float ActualTileWidth => _actualTileWidth;
        public float ActualTileHeight => _actualTileHeight;
        public float ViewportHeight => ScrollView?.contentViewport.resolvedStyle.height ?? 0f;
        public int ItemCount => _items.Count;
        public IList<T> ItemsSource => _items;

        public event Action<IReadOnlyList<int>, int> SelectionChanged;
        public event Action<T, int, bool> ItemActivated;
        public event Action<T, int> ContextRequested;
        public event Action<T, int, PointerDownEvent> ItemPointerDown;
        public event Action<T, int, PointerMoveEvent> ItemPointerMove;
        public event Action<T, int, PointerUpEvent> ItemPointerUp;
        public event Action<int, float, float> LayoutChanged;
        public event Action<Vector2> ScrollOffsetChanged;

        public CommonSelectableGridView(
            Func<VisualElement> makeItem,
            Action<VisualElement, T, int> bindItem,
            params string[] classNames)
        {
            _makeItem = makeItem ?? throw new ArgumentNullException(nameof(makeItem));
            _bindItem = bindItem ?? throw new ArgumentNullException(nameof(bindItem));

            CommonUITK.AddClasses(this, RootClass);
            CommonUITK.AddClasses(this, classNames);
            focusable = true;

            ListView = new ListView(_rows, 1f, CreateRow, BindRow)
            {
                fixedItemHeight = 1f,
                horizontalScrollingEnabled = false,
                selectionType = SelectionType.None,
                showAlternatingRowBackgrounds = AlternatingRowBackground.None,
                showBorder = false,
                showBoundCollectionSize = false,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight
            };
            ListView.AddToClassList(ListClass);
            ListView.style.flexGrow = 1f;
            ListView.style.minWidth = 0f;
            ListView.style.minHeight = 0f;
            Add(ListView);

            ScrollView = ListView.Q<ScrollView>();
            if (ScrollView != null)
            {
                ScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                ScrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;
                ScrollView.verticalScroller.valueChanged += _ => ScrollOffsetChanged?.Invoke(ScrollView.scrollOffset);
                ScrollView.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => ReflowItems());
            }

            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            SetDisplayMode(CommonGridViewDisplayMode.Standard);
        }

        public void SetItems(IList<T> items, bool preserveSelection = false)
        {
            _items = items ?? Array.Empty<T>();
            if (!preserveSelection)
            {
                _selectedIndices.Clear();
                _activeIndex = -1;
                _selectionAnchor = -1;
            }
            else
            {
                RemoveInvalidSelection();
            }

            RebuildRows();
        }

        public void SetLayout(float preferredTileWidth, float tileAspectRatio, float tileMargin, bool fillAvailableWidth)
        {
            _preferredTileWidth = Mathf.Max(1f, preferredTileWidth);
            _tileAspectRatio = Mathf.Max(0.01f, tileAspectRatio);
            _tileMargin = Mathf.Max(0f, tileMargin);
            _fillAvailableWidth = fillAvailableWidth;
            ReflowItems();
        }

        public void SetDisplayMode(CommonGridViewDisplayMode displayMode)
        {
            if (_displayModeInitialized && DisplayMode == displayMode) return;

            _displayModeInitialized = true;
            DisplayMode = displayMode;
            EnableInClassList(TinyClass, displayMode == CommonGridViewDisplayMode.Tiny);
            EnableInClassList(CompactClass, displayMode == CommonGridViewDisplayMode.Compact);
            EnableInClassList(StandardClass, displayMode == CommonGridViewDisplayMode.Standard);
            EnableInClassList(DetailedClass, displayMode == CommonGridViewDisplayMode.Detailed);
            RefreshItems();
        }

        public void RefreshItems()
        {
            RemoveInvalidSelection();
            int rowCount = CalculateRowCount();
            if (_rows.Count != rowCount)
            {
                RebuildRows();
                return;
            }

            ListView.RefreshItems();
            RefreshSelectionClasses();
        }

        public void RefreshItem(int index)
        {
            if (index < 0 || index >= _items.Count) return;
            ListView.Query<VisualElement>(className: ItemClass).ForEach(item =>
            {
                if (GetItemIndex(item) != index) return;
                _bindItem(item, _items[index], index);
                item.EnableInClassList(SelectedItemClass, _selectedIndices.Contains(index));
            });
        }

        public IReadOnlyList<int> GetSelectedIndices()
        {
            List<int> indices = new List<int>(_selectedIndices);
            indices.Sort();
            return indices;
        }

        public void GetSelectedIndices(List<int> target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            target.Clear();
            foreach (int index in _selectedIndices) target.Add(index);
            target.Sort();
        }

        public void SetSelection(IEnumerable<int> indices, int activeIndex = -1, bool notify = false)
        {
            _selectedIndices.Clear();
            if (indices != null)
            {
                foreach (int index in indices)
                {
                    if (index < 0 || index >= _items.Count) continue;
                    _selectedIndices.Add(index);
                    if (!AllowMultipleSelection) break;
                }
            }

            _activeIndex = activeIndex >= 0 && activeIndex < _items.Count
                ? activeIndex
                : GetFirstSelectedIndex();
            _selectionAnchor = _activeIndex;
            RefreshSelectionClasses();
            if (notify) NotifySelectionChanged();
        }

        public void ClearSelection(bool notify = false)
        {
            _selectedIndices.Clear();
            _activeIndex = -1;
            _selectionAnchor = -1;
            RefreshSelectionClasses();
            if (notify) NotifySelectionChanged();
        }

        public void ScrollToItem(int index)
        {
            if (index < 0 || index >= _items.Count) return;
            ListView.ScrollToItem(index / Mathf.Max(1, _columnCount));
        }

        private VisualElement CreateRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(RowClass);
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.flexShrink = 0f;
            return row;
        }

        private void BindRow(VisualElement row, int rowSourceIndex)
        {
            EnsureRowItemCount(row);
            int rowIndex = rowSourceIndex;

            for (int column = 0; column < row.childCount; column++)
            {
                VisualElement item = row[column];
                int itemIndex = rowIndex * _columnCount + column;
                bool valid = itemIndex >= 0 && itemIndex < _items.Count;
                item.style.display = valid ? DisplayStyle.Flex : DisplayStyle.None;
                item.userData = valid ? itemIndex : -1;
                if (!valid) continue;

                ApplyItemLayout(item);
                _bindItem(item, _items[itemIndex], itemIndex);
                item.EnableInClassList(SelectedItemClass, _selectedIndices.Contains(itemIndex));
            }
        }

        private void EnsureRowItemCount(VisualElement row)
        {
            while (row.childCount < _columnCount)
            {
                VisualElement item = _makeItem() ?? new VisualElement();
                item.AddToClassList(ItemClass);
                item.focusable = false;
                item.RegisterCallback<PointerDownEvent>(evt => OnItemPointerDown(item, evt));
                item.RegisterCallback<PointerMoveEvent>(evt => OnItemPointerMove(item, evt));
                item.RegisterCallback<PointerUpEvent>(evt => OnItemPointerUp(item, evt));
                item.RegisterCallback<ContextClickEvent>(evt => OnItemContextClick(item, evt));
                row.Add(item);
            }

            while (row.childCount > _columnCount)
            {
                row.RemoveAt(row.childCount - 1);
            }
        }

        private void OnItemPointerDown(VisualElement item, PointerDownEvent evt)
        {
            int index = GetItemIndex(item);
            if (index < 0 || index >= _items.Count) return;
            Focus();

            if (evt.button == 0)
            {
                ApplyPointerSelection(index, evt.actionKey, evt.shiftKey);
                ItemPointerDown?.Invoke(_items[index], index, evt);
                if (evt.clickCount > 1)
                {
                    ItemActivated?.Invoke(_items[index], index, evt.altKey);
                }
            }
            else if (evt.button == 1 && !_selectedIndices.Contains(index))
            {
                SetSingleSelection(index, true);
            }
        }

        private void OnItemPointerMove(VisualElement item, PointerMoveEvent evt)
        {
            int index = GetItemIndex(item);
            if (index < 0 || index >= _items.Count) return;
            ItemPointerMove?.Invoke(_items[index], index, evt);
        }

        private void OnItemPointerUp(VisualElement item, PointerUpEvent evt)
        {
            int index = GetItemIndex(item);
            if (index < 0 || index >= _items.Count) return;
            ItemPointerUp?.Invoke(_items[index], index, evt);
        }

        private void OnItemContextClick(VisualElement item, ContextClickEvent evt)
        {
            int index = GetItemIndex(item);
            if (index < 0 || index >= _items.Count) return;
            if (!_selectedIndices.Contains(index)) SetSingleSelection(index, true);
            ContextRequested?.Invoke(_items[index], index);
            evt.StopPropagation();
        }

        private static int GetItemIndex(VisualElement item)
        {
            return item.userData is int index ? index : -1;
        }

        private void ApplyPointerSelection(int index, bool actionKey, bool shiftKey)
        {
            if (!AllowMultipleSelection)
            {
                SetSingleSelection(index, true);
                return;
            }

            if (shiftKey && _selectionAnchor >= 0)
            {
                _selectedIndices.Clear();
                int min = Math.Min(_selectionAnchor, index);
                int max = Math.Max(_selectionAnchor, index);
                for (int i = min; i <= max; i++) _selectedIndices.Add(i);
                _activeIndex = index;
                RefreshSelectionClasses();
                NotifySelectionChanged();
                return;
            }

            if (actionKey)
            {
                if (!_selectedIndices.Add(index)) _selectedIndices.Remove(index);
                _activeIndex = _selectedIndices.Contains(index) ? index : GetFirstSelectedIndex();
                _selectionAnchor = index;
                RefreshSelectionClasses();
                NotifySelectionChanged();
                return;
            }

            SetSingleSelection(index, true);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (_items.Count == 0) return;

            // Alt+horizontal navigation is commonly owned by the host for paging/history.
            if (evt.altKey && (evt.keyCode == KeyCode.LeftArrow || evt.keyCode == KeyCode.RightArrow)) return;

            // Modified page navigation is commonly owned by a host with logical pages or tabs.
            if (evt.actionKey && (evt.keyCode == KeyCode.PageUp || evt.keyCode == KeyCode.PageDown)) return;

            if (evt.actionKey && evt.keyCode == KeyCode.A && AllowMultipleSelection)
            {
                _selectedIndices.Clear();
                for (int i = 0; i < _items.Count; i++) _selectedIndices.Add(i);
                _activeIndex = _activeIndex >= 0 ? _activeIndex : 0;
                _selectionAnchor = _activeIndex;
                RefreshSelectionClasses();
                NotifySelectionChanged();
                CommonUITK.ConsumeEvent(evt, true);
                return;
            }

            if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && _activeIndex >= 0)
            {
                ItemActivated?.Invoke(_items[_activeIndex], _activeIndex, evt.altKey);
                CommonUITK.ConsumeEvent(evt, true);
                return;
            }

            int current = _activeIndex >= 0 ? _activeIndex : 0;
            int next = current;
            switch (evt.keyCode)
            {
                case KeyCode.LeftArrow:
                    next = Mathf.Max(0, current - 1);
                    break;
                case KeyCode.RightArrow:
                    next = Mathf.Min(_items.Count - 1, current + 1);
                    break;
                case KeyCode.UpArrow:
                    next = Mathf.Max(0, current - _columnCount);
                    break;
                case KeyCode.DownArrow:
                    next = Mathf.Min(_items.Count - 1, current + _columnCount);
                    break;
                case KeyCode.Home:
                    next = 0;
                    break;
                case KeyCode.End:
                    next = _items.Count - 1;
                    break;
                case KeyCode.PageUp:
                    next = Mathf.Max(0, current - _columnCount * 3);
                    break;
                case KeyCode.PageDown:
                    next = Mathf.Min(_items.Count - 1, current + _columnCount * 3);
                    break;
                default:
                    return;
            }

            if (evt.shiftKey && AllowMultipleSelection)
            {
                if (_selectionAnchor < 0) _selectionAnchor = current;
                _selectedIndices.Clear();
                int min = Math.Min(_selectionAnchor, next);
                int max = Math.Max(_selectionAnchor, next);
                for (int i = min; i <= max; i++) _selectedIndices.Add(i);
                _activeIndex = next;
                RefreshSelectionClasses();
                NotifySelectionChanged();
            }
            else
            {
                SetSingleSelection(next, true);
            }

            ScrollToItem(next);
            CommonUITK.ConsumeEvent(evt, true);
        }

        private void SetSingleSelection(int index, bool notify)
        {
            _selectedIndices.Clear();
            if (index >= 0 && index < _items.Count)
            {
                _selectedIndices.Add(index);
                _activeIndex = index;
                _selectionAnchor = index;
            }
            else
            {
                _activeIndex = -1;
                _selectionAnchor = -1;
            }

            RefreshSelectionClasses();
            if (notify) NotifySelectionChanged();
        }

        private void NotifySelectionChanged()
        {
            SelectionChanged?.Invoke(GetSelectedIndices(), _activeIndex);
        }

        private void RefreshSelectionClasses()
        {
            ListView.Query<VisualElement>(className: ItemClass).ForEach(item =>
            {
                int index = GetItemIndex(item);
                item.EnableInClassList(SelectedItemClass, index >= 0 && _selectedIndices.Contains(index));
            });
        }

        private void RemoveInvalidSelection()
        {
            _selectedIndices.RemoveWhere(index => index < 0 || index >= _items.Count);
            if (_activeIndex < 0 || _activeIndex >= _items.Count) _activeIndex = GetFirstSelectedIndex();
            if (_selectionAnchor < 0 || _selectionAnchor >= _items.Count) _selectionAnchor = _activeIndex;
        }

        private int GetFirstSelectedIndex()
        {
            int first = int.MaxValue;
            foreach (int index in _selectedIndices)
            {
                if (index < first) first = index;
            }
            return first == int.MaxValue ? -1 : first;
        }

        private void RebuildRows()
        {
            _rows.Count = CalculateRowCount();
            ListView.Rebuild();
            RefreshSelectionClasses();
        }

        private int CalculateRowCount()
        {
            return _items.Count == 0 ? 0 : Mathf.CeilToInt((float)_items.Count / Mathf.Max(1, _columnCount));
        }

        private void ReflowItems()
        {
            if (ScrollView == null) return;
            float availableWidth = ScrollView.contentViewport.resolvedStyle.width;
            if (float.IsNaN(availableWidth) || availableWidth <= 0f) return;

            ReflowItems(availableWidth);
        }

        private void ReflowItems(float availableWidth)
        {
            float occupiedTileWidth = _preferredTileWidth + _tileMargin * 2f;
            int columns = Mathf.Max(1, Mathf.FloorToInt(availableWidth / Mathf.Max(1f, occupiedTileWidth)));
            float maximumTileWidth = Mathf.Max(1f, availableWidth / columns - _tileMargin * 2f);
            float tileWidth = _fillAvailableWidth
                ? maximumTileWidth
                : Mathf.Min(_preferredTileWidth, maximumTileWidth);
            float tileHeight = tileWidth / _tileAspectRatio;
            float rowHeight = tileHeight + _tileMargin * 2f;

            bool columnsChanged = columns != _columnCount;
            bool layoutChanged = columnsChanged
                || Math.Abs(tileWidth - _actualTileWidth) > 0.5f
                || Math.Abs(tileHeight - _actualTileHeight) > 0.5f
                || Math.Abs(rowHeight - _actualRowHeight) > 0.5f;
            _columnCount = columns;
            _actualTileWidth = tileWidth;
            _actualTileHeight = tileHeight;
            _actualRowHeight = rowHeight;
            ListView.fixedItemHeight = Mathf.Max(1f, rowHeight);

            // Fixed-height virtualization caches row geometry. Rebuild whenever the row height
            // changes so live width adjustments cannot leave stale gaps or overlapping tiles.
            if (layoutChanged)
            {
                RebuildRows();
            }
            else
            {
                ListView.RefreshItems();
            }

            if (layoutChanged) LayoutChanged?.Invoke(_columnCount, _actualTileWidth, rowHeight);
        }

        private void ApplyItemLayout(VisualElement item)
        {
            item.style.width = _actualTileWidth;
            item.style.height = _actualTileHeight;
            item.style.marginLeft = _tileMargin;
            item.style.marginRight = _tileMargin;
            item.style.marginTop = _tileMargin;
            item.style.marginBottom = _tileMargin;
        }

        private sealed class RowIndexCollection : IList
        {
            public int Count { get; set; }
            public bool IsReadOnly => true;
            public bool IsFixedSize => true;
            public bool IsSynchronized => false;
            public object SyncRoot => this;

            public object this[int index]
            {
                get
                {
                    if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
                    return index;
                }
                set => throw new NotSupportedException();
            }

            public int Add(object value) => throw new NotSupportedException();
            public void Clear() => throw new NotSupportedException();
            public void Insert(int index, object value) => throw new NotSupportedException();
            public void Remove(object value) => throw new NotSupportedException();
            public void RemoveAt(int index) => throw new NotSupportedException();
            public bool Contains(object value) => value is int index && index >= 0 && index < Count;
            public int IndexOf(object value) => Contains(value) ? (int)value : -1;

            public void CopyTo(Array array, int index)
            {
                if (array == null) throw new ArgumentNullException(nameof(array));
                for (int i = 0; i < Count; i++) array.SetValue(i, index + i);
            }

            public IEnumerator GetEnumerator()
            {
                for (int i = 0; i < Count; i++) yield return i;
            }
        }
    }
}
