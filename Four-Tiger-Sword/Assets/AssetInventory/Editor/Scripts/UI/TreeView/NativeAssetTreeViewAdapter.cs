using System;
using System.Collections.Generic;
using System.Linq;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using NativeColumn = UnityEngine.UIElements.Column;
using NativeColumns = UnityEngine.UIElements.Columns;

namespace AssetInventory
{
    internal sealed class NativeAssetTreeViewAdapter
    {
        private const string NativeCellClass = "ai-native-tree-cell";
        private const string HeaderColumnsClass = "unity-multi-column-header__column-container";

        private sealed class CellBinding
        {
            public AssetInfo Info;
            public int SourceColumnIndex;
            public int MetadataDefinitionId;
        }

        private readonly Func<int, int, VisualElement> _createCell;
        private readonly Action<VisualElement, AssetInfo, int, int, bool> _bindCell;
        private readonly Action<VisualElement, AssetInfo, int> _unbindCell;
        private readonly Func<bool, float> _rowHeightProvider;
        private readonly Func<int, CommonMultiColumnColumn, int> _metadataDefinitionResolver;
        private readonly int _primaryColumnIndex;
        private readonly int _heightAffectingColumnIndex;
        private readonly Dictionary<string, int> _sourceColumnIndices = new Dictionary<string, int>();
        private readonly List<string> _displayColumnNames = new List<string>();
        private readonly Dictionary<int, List<int>> _nativeIdsByModelId = new Dictionary<int, List<int>>();
        private readonly Dictionary<int, int?> _parentNativeIds = new Dictionary<int, int?>();
        private readonly HashSet<int> _usedNativeIds = new HashSet<int>();
        private readonly HashSet<VisualElement> _boundCells = new HashSet<VisualElement>();
        private readonly Dictionary<int, float> _baselineColumnWidths = new Dictionary<int, float>();
        private readonly Dictionary<int, bool> _baselineColumnVisibility = new Dictionary<int, bool>();
        private readonly List<int> _baselineDisplayOrder = new List<int>();
        private readonly Action _columnStateChanged;
        private readonly Action<GenericMenu, IReadOnlyList<AssetInfo>, int> _populateContextMenu;
        private readonly Action<int, bool> _sortChanged;
        private CommonMultiColumnState _authoritativeColumnState;
        private int[] _authoritativeDisplayOrder;
        private bool _columnStateChangeScheduled;
        private bool _acceptColumnStateChanges;
        private bool _suppressColumnStateChanged;
        private bool _suppressSelectionChanged;
        private bool _syncingSort;
        private int _authoritativeSortColumnIndex = -1;
        private bool _authoritativeSortDescending;
        private int _viewDataRestorePass;
        private int _nextSyntheticId;

        public MultiColumnTreeView View { get; }

        public event Action<IList<int>> SelectionChanged;
        public event Action<AssetInfo> ItemChosen;

        public NativeAssetTreeViewAdapter(
            CommonMultiColumnState columnState,
            AssetTreeViewControl renderer,
            string viewDataKey,
            bool allowSorting,
            Action columnStateChanged,
            Action<int, bool> sortChanged = null,
            Action<GenericMenu, IReadOnlyList<AssetInfo>, int> populateContextMenu = null,
            IReadOnlyList<int> displayOrder = null)
            : this(
                columnState,
                viewDataKey,
                allowSorting,
                (int)AssetTreeViewControl.Columns.Name,
                (int)AssetTreeViewControl.Columns.Media,
                mediaVisible => mediaVisible ? AI.Config.rowHeightMedia : AI.Config.rowHeight,
                renderer.CreateNativeCell,
                renderer.BindNativeCell,
                columnStateChanged,
                sortChanged,
                populateContextMenu,
                ResolvePackageMetadataDefinition,
                renderer.UnbindNativeCell,
                displayOrder)
        {
        }

        public NativeAssetTreeViewAdapter(
            CommonMultiColumnState columnState,
            string viewDataKey,
            bool allowSorting,
            int primaryColumnIndex,
            Func<float> rowHeightProvider,
            Func<int, VisualElement> createCell,
            Action<VisualElement, AssetInfo, int> bindCell,
            Action columnStateChanged,
            Action<int, bool> sortChanged = null,
            Action<GenericMenu, IReadOnlyList<AssetInfo>, int> populateContextMenu = null,
            Action<VisualElement, AssetInfo, int> unbindCell = null,
            IReadOnlyList<int> displayOrder = null)
            : this(
                columnState,
                viewDataKey,
                allowSorting,
                primaryColumnIndex,
                -1,
                _ => rowHeightProvider?.Invoke() ?? 18f,
                createCell == null ? null : (sourceColumnIndex, _) => createCell(sourceColumnIndex),
                bindCell == null ? null : (element, info, sourceColumnIndex, _, __) => bindCell(element, info, sourceColumnIndex),
                columnStateChanged,
                sortChanged,
                populateContextMenu,
                null,
                unbindCell,
                displayOrder)
        {
        }

        private NativeAssetTreeViewAdapter(
            CommonMultiColumnState columnState,
            string viewDataKey,
            bool allowSorting,
            int primaryColumnIndex,
            int heightAffectingColumnIndex,
            Func<bool, float> rowHeightProvider,
            Func<int, int, VisualElement> createCell,
            Action<VisualElement, AssetInfo, int, int, bool> bindCell,
            Action columnStateChanged,
            Action<int, bool> sortChanged,
            Action<GenericMenu, IReadOnlyList<AssetInfo>, int> populateContextMenu,
            Func<int, CommonMultiColumnColumn, int> metadataDefinitionResolver,
            Action<VisualElement, AssetInfo, int> unbindCell,
            IReadOnlyList<int> displayOrder)
        {
            if (columnState == null) throw new ArgumentNullException(nameof(columnState));

            _createCell = createCell ?? throw new ArgumentNullException(nameof(createCell));
            _bindCell = bindCell ?? throw new ArgumentNullException(nameof(bindCell));
            _rowHeightProvider = rowHeightProvider ?? throw new ArgumentNullException(nameof(rowHeightProvider));
            _primaryColumnIndex = primaryColumnIndex;
            _heightAffectingColumnIndex = heightAffectingColumnIndex;
            _metadataDefinitionResolver = metadataDefinitionResolver;
            _unbindCell = unbindCell;
            _columnStateChanged = columnStateChanged;
            _sortChanged = sortChanged;
            _populateContextMenu = populateContextMenu;
            _authoritativeColumnState = columnState;
            _authoritativeDisplayOrder = SanitizeDisplayOrder(columnState, displayOrder);

            NativeColumns columns = CreateColumns(columnState, allowSorting, _authoritativeDisplayOrder);
            View = new MultiColumnTreeView(columns)
            {
                fixedItemHeight = _rowHeightProvider(false),
                selectionType = SelectionType.Multiple,
                showAlternatingRowBackgrounds = AlternatingRowBackground.All,
                showBorder = true,
                horizontalScrollingEnabled = true,
                reorderable = false,
                viewDataKey = viewDataKey
            };
            View.selectionChanged += HandleSelectionChanged;
            View.itemsChosen += HandleItemsChosen;
            View.RegisterCallback<AttachToPanelEvent>(HandleAttachToPanel);
            View.RegisterCallback<DetachFromPanelEvent>(_ => _acceptColumnStateChanges = false);
            View.RegisterCallback<GeometryChangedEvent>(_ => ScheduleColumnStateChanged());
            View.RegisterCallback<PointerUpEvent>(_ =>
            {
                RefreshRowHeight();
                RefreshDisplayColumnOrder();
                FlushPendingColumnState();
            });

            if (allowSorting)
            {
#pragma warning disable CS0618 // Required by Unity 2022; replaced by sortingMode in newer editors.
                View.sortingEnabled = true;
#pragma warning restore CS0618
                View.columnSortingChanged += HandleColumnSortingChanged;
            }
            if (_populateContextMenu != null)
            {
                View.RegisterCallback<ContextClickEvent>(HandleContextClick);
            }
            CaptureColumnStateBaseline();
        }

        public IEnumerable<KeyValuePair<int, NativeColumn>> GetSourceColumns()
        {
            RefreshDisplayColumnOrder();
            HashSet<string> yielded = new HashSet<string>();
            foreach (string columnName in _displayColumnNames)
            {
                if (!yielded.Add(columnName) || !View.columns.Contains(columnName)) continue;
                if (_sourceColumnIndices.TryGetValue(columnName, out int sourceIndex))
                {
                    yield return new KeyValuePair<int, NativeColumn>(sourceIndex, View.columns[columnName]);
                }
            }

            foreach (NativeColumn column in View.columns)
            {
                if (yielded.Add(column.name) && _sourceColumnIndices.TryGetValue(column.name, out int sourceIndex))
                {
                    yield return new KeyValuePair<int, NativeColumn>(sourceIndex, column);
                }
            }
        }

        public void SetRoot(TreeElement root, IEnumerable<int> selectedModelIds = null, bool revealSelection = false)
        {
            RunWithoutSelectionChanged(() =>
            {
                _nativeIdsByModelId.Clear();
                _parentNativeIds.Clear();
                _usedNativeIds.Clear();
                _nextSyntheticId = int.MinValue;

                View.SetRootItems(CreateItems(root, null));
                View.Rebuild();
                RefreshRowHeight();
                SetSelectionByModelIds(selectedModelIds, revealSelection);
            });
        }

        public void SetSelectionByModelIds(IEnumerable<int> modelIds, bool reveal)
        {
            RunWithoutSelectionChanged(() =>
            {
                List<int> nativeIds = new List<int>();
                if (modelIds != null)
                {
                    foreach (int modelId in modelIds.Distinct())
                    {
                        if (_nativeIdsByModelId.TryGetValue(modelId, out List<int> matches) && matches.Count > 0)
                        {
                            nativeIds.Add(matches[0]);
                        }
                    }
                }

                // Unity reveals hidden selected items even in its "without notify" API.
                // Restore only ancestors it opened, keeping refreshes independent of navigation.
                HashSet<int> collapsedAncestors = new HashSet<int>();
                if (!reveal)
                {
                    foreach (int nativeId in nativeIds)
                    {
                        int current = nativeId;
                        while (_parentNativeIds.TryGetValue(current, out int? parent) && parent.HasValue)
                        {
                            if (!View.IsExpanded(parent.Value)) collapsedAncestors.Add(parent.Value);
                            current = parent.Value;
                        }
                    }
                }
                // Expand every selected branch before Unity resolves row indices. Otherwise
                // opening a later branch shifts an earlier selection to a different item.
                ExpandAncestors(nativeIds);
                View.SetSelectionByIdWithoutNotify(nativeIds);
                foreach (int ancestor in collapsedAncestors) View.CollapseItem(ancestor);
                if (reveal && nativeIds.Count > 0)
                {
                    View.ScrollToItemById(nativeIds[0]);
                }
            });
        }

        public IList<int> GetSelectedModelIds()
        {
            return View.selectedIds
                .Select(id => View.GetItemDataForId<AssetInfo>(id))
                .Where(info => info != null)
                .Select(info => info.TreeId)
                .Distinct()
                .ToList();
        }

        public void ClearSelection()
        {
            RunWithoutSelectionChanged(View.ClearSelection);
        }

        public void ExpandAll()
        {
            View.ExpandAll();
        }

        public void CollapseAll()
        {
            View.CollapseAll();
        }

        public bool IsColumnVisible(int sourceColumnIndex)
        {
            if (View == null) return false;

            string columnName = GetColumnName(sourceColumnIndex);
            return View.columns.Contains(columnName) && View.columns[columnName].visible;
        }

        public void RefreshRowHeight()
        {
            if (View == null) return;

            float height = _rowHeightProvider(IsColumnVisible(_heightAffectingColumnIndex));
            if (Mathf.Approximately(View.fixedItemHeight, height)) return;

            View.fixedItemHeight = height;
            View.RefreshItems();
        }

        public void RepaintCells()
        {
            if (View == null) return;

            foreach (VisualElement cell in _boundCells)
            {
                if (!(cell.userData is CellBinding binding) || binding.Info == null) continue;
                _bindCell(
                    cell,
                    binding.Info,
                    binding.SourceColumnIndex,
                    binding.MetadataDefinitionId,
                    IsColumnVisible(_heightAffectingColumnIndex));
            }
        }

        public void SyncSort(int sourceColumnIndex, bool descending)
        {
            _authoritativeSortColumnIndex = sourceColumnIndex;
            _authoritativeSortDescending = descending;
            ApplyAuthoritativeSort();
        }

        internal void ApplyAuthoritativeColumnState(
            CommonMultiColumnState columnState,
            IReadOnlyList<int> displayOrder,
            int sortColumnIndex,
            bool sortDescending)
        {
            if (columnState == null) return;

            _authoritativeColumnState = columnState;
            _authoritativeDisplayOrder = SanitizeDisplayOrder(columnState, displayOrder);
            _authoritativeSortColumnIndex = sortColumnIndex;
            _authoritativeSortDescending = sortDescending;

            bool wasAcceptingChanges = _acceptColumnStateChanges;
            _suppressColumnStateChanged = true;
            try
            {
                ApplyAuthoritativeColumns();
                ApplyAuthoritativeSort();
                CaptureColumnStateBaseline();
            }
            finally
            {
                _suppressColumnStateChanged = false;
                _acceptColumnStateChanges = wasAcceptingChanges;
            }
        }

        internal void RememberAuthoritativeColumnState(
            CommonMultiColumnState columnState,
            IReadOnlyList<int> displayOrder)
        {
            if (columnState == null) return;

            _authoritativeColumnState = columnState;
            _authoritativeDisplayOrder = SanitizeDisplayOrder(columnState, displayOrder);
        }

        internal bool TryConsumeColumnStateChanges(out List<int> changedIndices, out bool orderChanged)
        {
            changedIndices = new List<int>();
            orderChanged = false;
            if (!_acceptColumnStateChanges) return false;

            List<KeyValuePair<int, NativeColumn>> columns = GetSourceColumns().ToList();
            foreach (KeyValuePair<int, NativeColumn> pair in columns)
            {
                if (!_baselineColumnWidths.TryGetValue(pair.Key, out float baselineWidth) ||
                    Mathf.Abs(baselineWidth - pair.Value.width.value) > 0.01f ||
                    !_baselineColumnVisibility.TryGetValue(pair.Key, out bool baselineVisibility) ||
                    baselineVisibility != pair.Value.visible)
                {
                    changedIndices.Add(pair.Key);
                }
            }

            List<int> displayOrder = columns.Select(pair => pair.Key).ToList();
            orderChanged = !_baselineDisplayOrder.SequenceEqual(displayOrder);
            if (changedIndices.Count == 0 && !orderChanged) return false;

            CaptureColumnStateBaseline(columns);
            return true;
        }

        internal void FlushPendingColumnState()
        {
            if (!_acceptColumnStateChanges || _suppressColumnStateChanged) return;
            if (!HasColumnStateChanges()) return;

            _columnStateChangeScheduled = false;
            _columnStateChanged?.Invoke();
        }

        internal void CompleteViewDataRestoreForTests()
        {
            CompleteViewDataRestore();
        }

        private void ApplyAuthoritativeSort()
        {
            string columnName = GetColumnName(_authoritativeSortColumnIndex);
            _syncingSort = true;
            try
            {
                View.sortColumnDescriptions.Clear();
                if (!View.columns.Contains(columnName) || !View.columns[columnName].sortable) return;

                View.sortColumnDescriptions.Add(new SortColumnDescription(
                    columnName,
                    _authoritativeSortDescending ? SortDirection.Descending : SortDirection.Ascending));
            }
            finally
            {
                _syncingSort = false;
            }
        }

        private NativeColumns CreateColumns(
            CommonMultiColumnState columnState,
            bool allowSorting,
            IReadOnlyList<int> displayOrder)
        {
            NativeColumns columns = new NativeColumns
            {
                primaryColumnName = GetColumnName(_primaryColumnIndex),
                reorderable = true,
                resizable = true,
                resizePreview = false,
                stretchMode = NativeColumns.StretchMode.Grow
            };

            HashSet<int> visibleColumns = new HashSet<int>(columnState.VisibleColumns);
            foreach (int sourceColumnIndex in GetColumnOrder(columnState, displayOrder))
            {
                CommonMultiColumnColumn sourceColumn = columnState.Columns[sourceColumnIndex];
                if (sourceColumn == null) continue;

                int localColumnIndex = sourceColumnIndex;
                int metadataDefinitionId = _metadataDefinitionResolver?.Invoke(sourceColumnIndex, sourceColumn) ?? -1;
                string columnName = GetColumnName(sourceColumnIndex);
                NativeColumn nativeColumn = new NativeColumn
                {
                    name = columnName,
                    title = sourceColumn.Title,
                    width = sourceColumn.Width,
                    minWidth = sourceColumn.MinWidth,
                    visible = visibleColumns.Contains(sourceColumnIndex),
                    optional = sourceColumn.Optional,
                    resizable = true,
                    sortable = allowSorting && sourceColumn.Sortable,
                    stretchable = sourceColumn.Stretchable || sourceColumnIndex == _primaryColumnIndex,
                    makeCell = () => CreateCell(localColumnIndex, metadataDefinitionId),
                    bindCell = BindCell,
                    unbindCell = UnbindCell
                };
                if (sourceColumn.MaxWidth > 0f) nativeColumn.maxWidth = sourceColumn.MaxWidth;
#if UNITY_6000_0_OR_NEWER
                nativeColumn.propertyChanged += (_, __) =>
                {
                    RefreshRowHeight();
                    ScheduleColumnStateChanged();
                };
#endif

                columns.Add(nativeColumn);
                _sourceColumnIndices[columnName] = sourceColumnIndex;
                _displayColumnNames.Add(columnName);
            }
            return columns;
        }

        private void RefreshDisplayColumnOrder()
        {
            VisualElement headerColumns = View?.Q<VisualElement>(className: HeaderColumnsClass);
            if (headerColumns == null) return;

            List<string> orderedNames = new List<string>();
            for (int i = 0; i < headerColumns.hierarchy.childCount; i++)
            {
                string columnName = headerColumns.hierarchy[i].name;
                if (_sourceColumnIndices.ContainsKey(columnName) && !orderedNames.Contains(columnName))
                {
                    orderedNames.Add(columnName);
                }
            }
            foreach (string columnName in _displayColumnNames)
            {
                if (!orderedNames.Contains(columnName)) orderedNames.Add(columnName);
            }

            _displayColumnNames.Clear();
            _displayColumnNames.AddRange(orderedNames);
        }

        private void ScheduleColumnStateChanged()
        {
            if (!_acceptColumnStateChanges || _suppressColumnStateChanged ||
                _columnStateChangeScheduled || View == null) return;

            _columnStateChangeScheduled = true;
            View.schedule.Execute(PersistScheduledColumnState).ExecuteLater(120);
        }

        private void PersistScheduledColumnState()
        {
            if (!_columnStateChangeScheduled) return;

            _columnStateChangeScheduled = false;
            FlushPendingColumnState();
        }

        private static IEnumerable<int> GetColumnOrder(
            CommonMultiColumnState columnState,
            IReadOnlyList<int> displayOrder)
        {
            HashSet<int> yielded = new HashSet<int>();
            if (displayOrder != null)
            {
                foreach (int index in displayOrder)
                {
                    if (index >= 0 && index < columnState.ColumnCount && yielded.Add(index)) yield return index;
                }
            }
            foreach (int index in columnState.VisibleColumns)
            {
                if (index >= 0 && index < columnState.ColumnCount && yielded.Add(index)) yield return index;
            }

            for (int i = 0; i < columnState.ColumnCount; i++)
            {
                if (yielded.Add(i)) yield return i;
            }
        }

        private void HandleAttachToPanel(AttachToPanelEvent evt)
        {
            _acceptColumnStateChanges = false;
            _viewDataRestorePass = 0;
            View.schedule.Execute(CompleteViewDataRestore).ExecuteLater(0);
        }

        private void CompleteViewDataRestore()
        {
            _suppressColumnStateChanged = true;
            try
            {
                ApplyAuthoritativeColumns();
                ApplyAuthoritativeSort();
                CaptureColumnStateBaseline();
                if (View.panel != null && _viewDataRestorePass++ == 0)
                {
                    View.schedule.Execute(CompleteViewDataRestore).ExecuteLater(0);
                    return;
                }
            }
            finally
            {
                _suppressColumnStateChanged = false;
                _acceptColumnStateChanges = View.panel == null || _viewDataRestorePass > 1;
            }
        }

        private void ApplyAuthoritativeColumns()
        {
            if (_authoritativeColumnState == null) return;

            ApplyDisplayOrder(_authoritativeDisplayOrder);
            HashSet<int> visibleColumns = new HashSet<int>(_authoritativeColumnState.VisibleColumns);
            foreach (KeyValuePair<int, NativeColumn> pair in GetSourceColumns())
            {
                int sourceIndex = pair.Key;
                if (sourceIndex < 0 || sourceIndex >= _authoritativeColumnState.ColumnCount) continue;

                CommonMultiColumnColumn preference = _authoritativeColumnState.Columns[sourceIndex];
                NativeColumn column = pair.Value;
                column.visible = visibleColumns.Contains(sourceIndex);
                ResetPreferredWidth(column, preference);
            }
            RefreshRowHeight();
        }

        private void ApplyDisplayOrder(IReadOnlyList<int> desiredOrder)
        {
            if (desiredOrder == null || desiredOrder.Count == 0) return;

            RefreshDisplayColumnOrder();
            List<int> currentOrder = GetSourceColumns().Select(pair => pair.Key).ToList();
            for (int targetIndex = 0; targetIndex < desiredOrder.Count; targetIndex++)
            {
                int sourceIndex = desiredOrder[targetIndex];
                int currentIndex = currentOrder.IndexOf(sourceIndex);
                if (currentIndex < 0 || currentIndex == targetIndex) continue;

                View.columns.ReorderDisplay(currentIndex, targetIndex);
                currentOrder.RemoveAt(currentIndex);
                currentOrder.Insert(targetIndex, sourceIndex);
            }

            _displayColumnNames.Clear();
            foreach (int sourceIndex in currentOrder) _displayColumnNames.Add(GetColumnName(sourceIndex));
        }

        private static void ResetPreferredWidth(NativeColumn column, CommonMultiColumnColumn preference)
        {
            float preferredWidth = Mathf.Clamp(
                preference.Width,
                preference.MinWidth,
                preference.MaxWidth > 0f ? preference.MaxWidth : float.MaxValue);
            float temporaryWidth = preferredWidth;
            if (preference.MaxWidth <= 0f || preferredWidth + 0.5f <= preference.MaxWidth)
            {
                temporaryWidth = preferredWidth + 0.5f;
            }
            else if (preferredWidth - 0.5f >= preference.MinWidth)
            {
                temporaryWidth = preferredWidth - 0.5f;
            }

            if (Mathf.Abs(temporaryWidth - preferredWidth) > 0.01f) column.width = temporaryWidth;
            column.width = preferredWidth;
        }

        private bool HasColumnStateChanges()
        {
            List<KeyValuePair<int, NativeColumn>> columns = GetSourceColumns().ToList();
            if (!_baselineDisplayOrder.SequenceEqual(columns.Select(pair => pair.Key))) return true;

            foreach (KeyValuePair<int, NativeColumn> pair in columns)
            {
                if (!_baselineColumnWidths.TryGetValue(pair.Key, out float baselineWidth) ||
                    Mathf.Abs(baselineWidth - pair.Value.width.value) > 0.01f ||
                    !_baselineColumnVisibility.TryGetValue(pair.Key, out bool baselineVisibility) ||
                    baselineVisibility != pair.Value.visible) return true;
            }
            return false;
        }

        private void CaptureColumnStateBaseline()
        {
            CaptureColumnStateBaseline(GetSourceColumns().ToList());
        }

        private void CaptureColumnStateBaseline(IReadOnlyList<KeyValuePair<int, NativeColumn>> columns)
        {
            _baselineColumnWidths.Clear();
            _baselineColumnVisibility.Clear();
            _baselineDisplayOrder.Clear();
            foreach (KeyValuePair<int, NativeColumn> pair in columns)
            {
                _baselineColumnWidths[pair.Key] = pair.Value.width.value;
                _baselineColumnVisibility[pair.Key] = pair.Value.visible;
                _baselineDisplayOrder.Add(pair.Key);
            }
        }

        private static int[] SanitizeDisplayOrder(
            CommonMultiColumnState columnState,
            IReadOnlyList<int> displayOrder)
        {
            List<int> sanitized = new List<int>();
            HashSet<int> added = new HashSet<int>();
            if (displayOrder != null)
            {
                foreach (int sourceIndex in displayOrder)
                {
                    if (sourceIndex >= 0 && sourceIndex < columnState.ColumnCount && added.Add(sourceIndex))
                    {
                        sanitized.Add(sourceIndex);
                    }
                }
            }
            foreach (int sourceIndex in columnState.VisibleColumns)
            {
                if (sourceIndex >= 0 && sourceIndex < columnState.ColumnCount && added.Add(sourceIndex))
                {
                    sanitized.Add(sourceIndex);
                }
            }
            for (int sourceIndex = 0; sourceIndex < columnState.ColumnCount; sourceIndex++)
            {
                if (added.Add(sourceIndex)) sanitized.Add(sourceIndex);
            }
            return sanitized.ToArray();
        }

        private static string GetColumnName(int sourceColumnIndex)
        {
            return $"asset-column-{sourceColumnIndex}";
        }

        private List<TreeViewItemData<AssetInfo>> CreateItems(TreeElement parent, int? parentNativeId)
        {
            List<TreeViewItemData<AssetInfo>> result = new List<TreeViewItemData<AssetInfo>>();
            if (parent?.Children == null) return result;

            foreach (TreeElement child in parent.Children)
            {
                if (!(child is AssetInfo info)) continue;

                int nativeId = AllocateNativeId(info.TreeId);
                _parentNativeIds[nativeId] = parentNativeId;
                if (!_nativeIdsByModelId.TryGetValue(info.TreeId, out List<int> modelIds))
                {
                    modelIds = new List<int>();
                    _nativeIdsByModelId[info.TreeId] = modelIds;
                }
                modelIds.Add(nativeId);

                result.Add(new TreeViewItemData<AssetInfo>(nativeId, info, CreateItems(info, nativeId)));
            }
            return result;
        }

        private int AllocateNativeId(int preferredId)
        {
            if (_usedNativeIds.Add(preferredId)) return preferredId;

            while (!_usedNativeIds.Add(_nextSyntheticId))
            {
                _nextSyntheticId++;
            }
            return _nextSyntheticId++;
        }

        private void ExpandAncestors(IEnumerable<int> nativeIds)
        {
            List<int> ancestors = new List<int>();
            HashSet<int> visited = new HashSet<int>();
            foreach (int nativeId in nativeIds)
            {
                int current = nativeId;
                while (_parentNativeIds.TryGetValue(current, out int? parent) && parent.HasValue)
                {
                    if (visited.Add(parent.Value)) ancestors.Add(parent.Value);
                    current = parent.Value;
                }
            }

            for (int i = ancestors.Count - 1; i >= 0; i--)
            {
#if UNITY_6000_0_OR_NEWER
                View.ExpandItem(ancestors[i], false, false);
#else
                View.ExpandItem(ancestors[i], false);
#endif
            }
            View.RefreshItems();
        }

        private VisualElement CreateCell(int sourceColumnIndex, int metadataDefinitionId)
        {
            VisualElement cell = _createCell(sourceColumnIndex, metadataDefinitionId) ?? new VisualElement();
            cell.AddToClassList(NativeCellClass);
            cell.userData = new CellBinding
            {
                SourceColumnIndex = sourceColumnIndex,
                MetadataDefinitionId = metadataDefinitionId
            };
            cell.style.flexGrow = 1f;
            cell.style.flexShrink = 1f;
            cell.style.minWidth = 0f;
            cell.style.minHeight = 0f;
            cell.style.overflow = Overflow.Hidden;
            return cell;
        }

        private void BindCell(VisualElement element, int index)
        {
            if (!(element?.userData is CellBinding binding)) return;

            binding.Info = View.GetItemDataForIndex<AssetInfo>(index);
            _boundCells.Add(element);
            _bindCell(
                element,
                binding.Info,
                binding.SourceColumnIndex,
                binding.MetadataDefinitionId,
                IsColumnVisible(_heightAffectingColumnIndex));
        }

        private void UnbindCell(VisualElement element, int index)
        {
            if (!(element?.userData is CellBinding binding)) return;

            _boundCells.Remove(element);
            _unbindCell?.Invoke(element, binding.Info, binding.SourceColumnIndex);
            binding.Info = null;
        }

        private static int ResolvePackageMetadataDefinition(int sourceColumnIndex, CommonMultiColumnColumn sourceColumn)
        {
            return sourceColumnIndex > (int)AssetTreeViewControl.Columns.NoIndex ? sourceColumn.UserData : -1;
        }


        private void HandleSelectionChanged(IEnumerable<object> selectedItems)
        {
            if (_suppressSelectionChanged) return;

            List<int> ids = selectedItems
                .OfType<AssetInfo>()
                .Select(info => info.TreeId)
                .Distinct()
                .ToList();
            SelectionChanged?.Invoke(ids);
        }

        private void RunWithoutSelectionChanged(Action action)
        {
            bool wasSuppressed = _suppressSelectionChanged;
            _suppressSelectionChanged = true;
            try
            {
                action?.Invoke();
            }
            finally
            {
                _suppressSelectionChanged = wasSuppressed;
            }
        }

        private void HandleItemsChosen(IEnumerable<object> chosenItems)
        {
            AssetInfo info = chosenItems.OfType<AssetInfo>().FirstOrDefault();
            if (info != null) ItemChosen?.Invoke(info);
        }

        private void HandleColumnSortingChanged()
        {
            if (_syncingSort || _sortChanged == null || View.sortColumnDescriptions.Count == 0) return;

            SortColumnDescription description = View.sortColumnDescriptions[0];
            if (string.IsNullOrEmpty(description.columnName) ||
                !_sourceColumnIndices.TryGetValue(description.columnName, out int sourceColumnIndex)) return;

            _sortChanged(sourceColumnIndex, description.direction == SortDirection.Descending);
        }

        private void HandleContextClick(ContextClickEvent evt)
        {
            if (IsColumnHeaderClick(evt)) return;

            List<AssetInfo> selection = View.selectedItems
                .OfType<AssetInfo>()
                .GroupBy(info => info.TreeId)
                .Select(group => group.First())
                .ToList();

            GenericMenu menu = new GenericMenu();
            _populateContextMenu(menu, selection, selection.Count > 0 ? 0 : -1);
            menu.ShowAsContext();
            evt.StopPropagation();
        }

        private bool IsColumnHeaderClick(ContextClickEvent evt)
        {
            VisualElement header = View.Q(className: "unity-multi-column-header");
            if (header != null && header.worldBound.Contains(evt.mousePosition)) return true;

            VisualElement current = evt.target as VisualElement;
            while (current != null && current != View)
            {
                if (current.GetClasses().Any(className =>
                        className.StartsWith("unity-multi-column-header", StringComparison.Ordinal))) return true;
                current = current.parent;
            }
            return false;
        }

    }
}
