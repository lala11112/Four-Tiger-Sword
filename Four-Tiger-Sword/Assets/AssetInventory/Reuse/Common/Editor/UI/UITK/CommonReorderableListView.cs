using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public sealed class CommonReorderableListView<T> : VisualElement
    {
        public const string RootClass = "common-reorderable-list-view";

        private readonly IList<T> _items;
        private Action<CommonReorderableListView<T>, Button> _addHandler;
        private Action<CommonReorderableListView<T>> _removeHandler;
        private Func<CommonReorderableListView<T>, bool> _canRemoveHandler;
#if !UNITY_2023_2_OR_NEWER
        private readonly VisualElement _fallbackFooter;
        private readonly Button _fallbackAddButton;
        private readonly Button _fallbackRemoveButton;
#endif

        public ListView ListView { get; }

        public int SelectedIndex => ListView.selectedIndex;

        public T SelectedItem
        {
            get
            {
                int index = SelectedIndex;
                return index >= 0 && index < _items.Count ? _items[index] : default;
            }
        }

        public event Action<int, int> ItemIndexChanged;
        public event Action ItemsChanged;

        public CommonReorderableListView(
            IList<T> items,
            Func<VisualElement> makeItem,
            Action<VisualElement, T, int> bindItem,
            float fixedItemHeight,
            params string[] classNames)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (makeItem == null) throw new ArgumentNullException(nameof(makeItem));
            if (bindItem == null) throw new ArgumentNullException(nameof(bindItem));
            if (!(items is IList untypedItems))
            {
                throw new ArgumentException("The item source must also implement System.Collections.IList so UI Toolkit can reorder it.", nameof(items));
            }

            _items = items;

            CommonUITK.AddClasses(this, RootClass);
            CommonUITK.AddClasses(this, classNames);

            ListView = new ListView(
                untypedItems,
                fixedItemHeight,
                makeItem,
                (element, index) =>
                {
                    if (index < 0 || index >= _items.Count) return;
                    bindItem(element, _items[index], index);
                })
            {
                fixedItemHeight = fixedItemHeight,
                horizontalScrollingEnabled = false,
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                selectionType = SelectionType.Single,
                showAddRemoveFooter = true,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                showBorder = true,
                showBoundCollectionSize = false,
                showFoldoutHeader = false,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight
            };
            ListView.AddToClassList("common-reorderable-list-view__list");
            ListView.itemIndexChanged += OnItemIndexChanged;
            ListView.selectionChanged += _ => RefreshRemoveState();
#if UNITY_2023_2_OR_NEWER
            ListView.allowAdd = false;
            ListView.onRemove = _ => RemoveSelectedOrCustom();
            ListView.overridingAddButtonBehavior = (_, button) => _addHandler?.Invoke(this, button);
#else
            ListView.showAddRemoveFooter = false;
#endif

            Add(ListView);
#if !UNITY_2023_2_OR_NEWER
            _fallbackFooter = CommonUITK.CreateContainer("common-reorderable-list-view__footer");
            _fallbackFooter.style.flexDirection = FlexDirection.Row;
            _fallbackFooter.style.justifyContent = Justify.FlexEnd;
            _fallbackAddButton = CommonUITK.CreateButton(
                "+",
                () => _addHandler?.Invoke(this, _fallbackAddButton),
                "common-reorderable-list-view__add");
            _fallbackAddButton.tooltip = "Add an item.";
            _fallbackRemoveButton = CommonUITK.CreateButton(
                "−",
                RemoveSelectedOrCustom,
                "common-reorderable-list-view__remove");
            _fallbackRemoveButton.tooltip = "Remove the selected item.";
            _fallbackFooter.Add(_fallbackAddButton);
            _fallbackFooter.Add(_fallbackRemoveButton);
            Add(_fallbackFooter);
#endif
            Refresh();
        }

        public void SetReorderable(bool reorderable)
        {
            ListView.reorderable = reorderable;
            if (reorderable)
            {
                ListView.reorderMode = ListViewReorderMode.Animated;
            }
        }

        public void SetAddHandler(Action<CommonReorderableListView<T>, Button> addHandler)
        {
            _addHandler = addHandler;
#if UNITY_2023_2_OR_NEWER
            ListView.allowAdd = addHandler != null;
#else
            _fallbackAddButton.style.display = addHandler != null ? DisplayStyle.Flex : DisplayStyle.None;
#endif
        }

        public void SetRemoveHandler(
            Action<CommonReorderableListView<T>> removeHandler,
            Func<CommonReorderableListView<T>, bool> canRemoveHandler = null)
        {
            _removeHandler = removeHandler;
            _canRemoveHandler = canRemoveHandler;
            RefreshRemoveState();
        }

        public void AddItem(T item, int insertIndex = -1)
        {
            int clampedIndex = insertIndex < 0 || insertIndex > _items.Count ? _items.Count : insertIndex;
            _items.Insert(clampedIndex, item);
            Refresh(clampedIndex);
            ListView.ScrollToItem(clampedIndex);
            ItemsChanged?.Invoke();
        }

        public void RemoveSelected()
        {
            List<int> indices = new List<int>();
            foreach (int selectedIndex in ListView.selectedIndices)
            {
                if (selectedIndex >= 0 && selectedIndex < _items.Count)
                {
                    indices.Add(selectedIndex);
                }
            }

            if (indices.Count == 0 && _items.Count > 0)
            {
                indices.Add(_items.Count - 1);
            }
            if (indices.Count == 0) return;

            indices.Sort((left, right) => right.CompareTo(left));
            int nextSelection = indices[indices.Count - 1];
            for (int i = 0; i < indices.Count; i++)
            {
                _items.RemoveAt(indices[i]);
            }

            if (_items.Count == 0)
            {
                Refresh(-1);
            }
            else
            {
                Refresh(Math.Min(nextSelection, _items.Count - 1));
            }

            ItemsChanged?.Invoke();
        }

        public void Refresh(int selectedIndex = -2)
        {
            RefreshRemoveState();
            ListView.RefreshItems();

            if (_items.Count == 0)
            {
                ListView.ClearSelection();
                return;
            }

            if (selectedIndex == -2) return;

            int clampedIndex = selectedIndex < 0 ? 0 : Math.Min(selectedIndex, _items.Count - 1);
            ListView.SetSelection(clampedIndex);
            RefreshRemoveState();
        }

        public void RefreshRemoveState()
        {
            bool canRemove = _items.Count > 0 && (_canRemoveHandler?.Invoke(this) ?? true);
#if UNITY_2023_2_OR_NEWER
            ListView.allowRemove = canRemove;
#else
            _fallbackRemoveButton.SetEnabled(canRemove);
#endif
        }

        private void RemoveSelectedOrCustom()
        {
            if (_removeHandler != null)
            {
                _removeHandler(this);
                RefreshRemoveState();
                return;
            }

            RemoveSelected();
        }

        private void OnItemIndexChanged(int oldIndex, int newIndex)
        {
            ListView.SetSelection(newIndex);
            ItemIndexChanged?.Invoke(oldIndex, newIndex);
            ItemsChanged?.Invoke();
        }
    }
}
