using System;
using System.Collections.Generic;
using System.Linq;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    internal enum AssetInventoryTableLayoutKind
    {
        Packages,
        Search,
        Reporting
    }

    [InitializeOnLoad]
#if UNITY_6000_7_OR_NEWER
    // Static-constructor state is code-load scoped; cleanup is handled explicitly where the type crosses Play Mode.
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    internal static partial class AssetInventoryColumnLayoutCoordinator
    {
        private const double SaveDelaySeconds = 0.25d;

        private sealed class Registration
        {
            public AssetInventoryTableLayoutKind Kind;
            public WeakReference<NativeAssetTreeViewAdapter> Adapter;
            public CommonMultiColumnState State;
            public Func<int, CommonMultiColumnColumn, string> KeyProvider;
        }

        private sealed class IsolatedTestScope : IDisposable
        {
            private readonly List<Registration> _previousRegistrations;
            private bool _disposed;

            public IsolatedTestScope(List<Registration> previousRegistrations)
            {
                _previousRegistrations = previousRegistrations;
            }

            public void Dispose()
            {
                if (_disposed) return;

                _disposed = true;
                ResetSaveState();
                Registrations.Clear();
                Registrations.AddRange(_previousRegistrations);
                PruneRegistrations();
            }
        }

        private static readonly List<Registration> Registrations = new List<Registration>();
        private static bool _dirty;
        private static bool _saveScheduled;
        private static double _saveAt;

        static AssetInventoryColumnLayoutCoordinator()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Flush;
            EditorApplication.quitting += Flush;
        }

        internal static CommonMultiColumnState Restore(
            AssetInventoryTableLayoutKind kind,
            CommonMultiColumnState defaultState,
            Func<int, CommonMultiColumnColumn, string> keyProvider,
            int fallbackSortIndex,
            bool fallbackSortDescending,
            out int[] displayOrder,
            out int sortIndex,
            out bool sortDescending,
            bool persistNormalizedLayout = true)
        {
            if (defaultState == null) throw new ArgumentNullException(nameof(defaultState));
            if (keyProvider == null) throw new ArgumentNullException(nameof(keyProvider));

            AssetInventoryTableLayout storedLayout = GetLayout(kind);
            ApplyLayout(
                storedLayout,
                defaultState,
                keyProvider,
                fallbackSortIndex,
                fallbackSortDescending,
                out displayOrder,
                out sortIndex,
                out sortDescending);

            AssetInventoryTableLayout normalizedLayout = BuildLayout(
                defaultState,
                displayOrder,
                sortIndex,
                sortDescending,
                keyProvider);
            if (!LayoutsEqual(storedLayout, normalizedLayout))
            {
                SetLayout(kind, normalizedLayout);
                if (persistNormalizedLayout) ScheduleSave();
            }

            return defaultState;
        }

        internal static void Register(
            AssetInventoryTableLayoutKind kind,
            NativeAssetTreeViewAdapter adapter,
            CommonMultiColumnState state,
            Func<int, CommonMultiColumnColumn, string> keyProvider)
        {
            if (adapter == null || state == null || keyProvider == null) return;

            PruneRegistrations();
            Unregister(adapter);
            Registrations.Add(new Registration
            {
                Kind = kind,
                Adapter = new WeakReference<NativeAssetTreeViewAdapter>(adapter),
                State = state,
                KeyProvider = keyProvider
            });
        }

        internal static void Unregister(NativeAssetTreeViewAdapter adapter)
        {
            if (adapter == null) return;

            for (int i = Registrations.Count - 1; i >= 0; i--)
            {
                if (!Registrations[i].Adapter.TryGetTarget(out NativeAssetTreeViewAdapter registeredAdapter) ||
                    ReferenceEquals(registeredAdapter, adapter))
                {
                    Registrations.RemoveAt(i);
                }
            }
        }

        internal static void UpdateColumns(
            AssetInventoryTableLayoutKind kind,
            NativeAssetTreeViewAdapter sourceAdapter,
            CommonMultiColumnState state,
            Func<int, CommonMultiColumnColumn, string> keyProvider)
        {
            if (sourceAdapter == null || state == null || keyProvider == null) return;
            if (!sourceAdapter.TryConsumeColumnStateChanges(out List<int> changedIndices, out bool orderChanged)) return;

            List<KeyValuePair<int, Column>> sourceColumns = sourceAdapter.GetSourceColumns().ToList();
            AssetInventoryTableLayout layout = GetLayout(kind) ?? BuildLayout(
                state,
                sourceColumns.Select(pair => pair.Key).ToArray(),
                state.SortedColumnIndex,
                state.SortedColumnIndex >= 0 && !state.Columns[state.SortedColumnIndex].SortedAscending,
                keyProvider);
            EnsureCollections(layout);

            Dictionary<string, AssetInventoryColumnLayout> preferences = CreatePreferenceMap(layout);
            HashSet<int> changed = new HashSet<int>(changedIndices);
            List<int> visibleColumns = new List<int>();

            for (int order = 0; order < sourceColumns.Count; order++)
            {
                int sourceIndex = sourceColumns[order].Key;
                Column column = sourceColumns[order].Value;
                if (sourceIndex < 0 || sourceIndex >= state.ColumnCount) continue;

                string key = keyProvider(sourceIndex, state.Columns[sourceIndex]);
                if (string.IsNullOrEmpty(key)) continue;
                if (!preferences.TryGetValue(key, out AssetInventoryColumnLayout preference))
                {
                    preference = new AssetInventoryColumnLayout {key = key};
                    layout.columns.Add(preference);
                    preferences[key] = preference;
                }

                if (changed.Contains(sourceIndex))
                {
                    float width = ClampWidth(state.Columns[sourceIndex], column.width.value);
                    preference.width = width;
                    preference.visible = column.visible;
                    state.Columns[sourceIndex].Width = width;
                }
                if (orderChanged) preference.order = order;
                if (column.visible) visibleColumns.Add(sourceIndex);
            }

            state.VisibleColumns = visibleColumns.ToArray();
            sourceAdapter.RememberAuthoritativeColumnState(
                state,
                sourceColumns.Select(pair => pair.Key).ToArray());
            SetLayout(kind, layout);
            ScheduleSave();
            Broadcast(kind, sourceAdapter);
        }

        internal static void UpdateSort(
            AssetInventoryTableLayoutKind kind,
            NativeAssetTreeViewAdapter sourceAdapter,
            CommonMultiColumnState state,
            Func<int, CommonMultiColumnColumn, string> keyProvider,
            int sourceColumnIndex,
            bool descending)
        {
            if (state == null || keyProvider == null) return;

            AssetInventoryTableLayout layout = GetLayout(kind) ?? new AssetInventoryTableLayout();
            EnsureCollections(layout);
            layout.sorting.Clear();

            if (sourceColumnIndex >= 0 && sourceColumnIndex < state.ColumnCount &&
                state.Columns[sourceColumnIndex].Sortable)
            {
                string key = keyProvider(sourceColumnIndex, state.Columns[sourceColumnIndex]);
                if (!string.IsNullOrEmpty(key))
                {
                    layout.sorting.Add(new AssetInventorySortLayout
                    {
                        key = key,
                        descending = descending
                    });
                    state.SortedColumnIndex = sourceColumnIndex;
                    state.Columns[sourceColumnIndex].SortedAscending = !descending;
                }
            }
            else
            {
                state.SortedColumnIndex = -1;
            }

            sourceAdapter?.SyncSort(sourceColumnIndex, descending);
            SetLayout(kind, layout);
            ScheduleSave();
            Broadcast(kind, sourceAdapter);
        }

        internal static string GetPackageColumnKey(int sourceColumnIndex, CommonMultiColumnColumn column)
        {
            if (column == null || sourceColumnIndex < 0) return null;
            if (sourceColumnIndex <= (int)AssetTreeViewControl.Columns.NoIndex)
            {
                return "BuiltIn:" + ((AssetTreeViewControl.Columns)sourceColumnIndex);
            }
            return "Metadata:" + column.UserData;
        }

        internal static string GetSearchColumnKey(int sourceColumnIndex, CommonMultiColumnColumn column)
        {
            if (column == null || sourceColumnIndex < 0 ||
                sourceColumnIndex > (int)SearchTreeViewControl.Columns.Package) return null;
            return "BuiltIn:" + ((SearchTreeViewControl.Columns)sourceColumnIndex);
        }

        internal static void Flush()
        {
            EditorApplication.update -= SaveWhenDue;
            _saveScheduled = false;
            if (!_dirty) return;

            if (AI.TrySaveConfig()) _dirty = false;
        }

        internal static IDisposable BeginIsolatedTestScope()
        {
            ResetSaveState();
            List<Registration> previousRegistrations = new List<Registration>(Registrations);
            Registrations.Clear();
            return new IsolatedTestScope(previousRegistrations);
        }

        private static void Broadcast(AssetInventoryTableLayoutKind kind, NativeAssetTreeViewAdapter sourceAdapter)
        {
            PruneRegistrations();
            AssetInventoryTableLayout layout = GetLayout(kind);

            foreach (Registration registration in Registrations)
            {
                if (registration.Kind != kind ||
                    !registration.Adapter.TryGetTarget(out NativeAssetTreeViewAdapter adapter) ||
                    ReferenceEquals(adapter, sourceAdapter)) continue;

                ApplyLayout(
                    layout,
                    registration.State,
                    registration.KeyProvider,
                    registration.State.SortedColumnIndex,
                    registration.State.SortedColumnIndex >= 0 &&
                    !registration.State.Columns[registration.State.SortedColumnIndex].SortedAscending,
                    out int[] displayOrder,
                    out int sortIndex,
                    out bool sortDescending);
                adapter.ApplyAuthoritativeColumnState(
                    registration.State,
                    displayOrder,
                    sortIndex,
                    sortDescending);
            }
        }

        private static void ApplyLayout(
            AssetInventoryTableLayout layout,
            CommonMultiColumnState state,
            Func<int, CommonMultiColumnColumn, string> keyProvider,
            int fallbackSortIndex,
            bool fallbackSortDescending,
            out int[] displayOrder,
            out int sortIndex,
            out bool sortDescending)
        {
            int[] defaultDisplayOrder = GetDefaultDisplayOrder(state);
            HashSet<int> defaultVisibleColumns = new HashSet<int>(state.VisibleColumns);
            Dictionary<string, int> sourceIndices = CreateSourceIndexMap(state, keyProvider);
            Dictionary<string, AssetInventoryColumnLayout> savedPreferences =
                CreateRecognizedPreferenceMap(layout, sourceIndices);

            foreach (KeyValuePair<string, AssetInventoryColumnLayout> pair in savedPreferences)
            {
                int sourceIndex = sourceIndices[pair.Key];
                AssetInventoryColumnLayout preference = pair.Value;
                if (IsFinite(preference.width))
                {
                    state.Columns[sourceIndex].Width = ClampWidth(state.Columns[sourceIndex], preference.width);
                }
            }

            if (savedPreferences.Count == 0)
            {
                displayOrder = defaultDisplayOrder;
            }
            else
            {
                List<int> orderedColumns = savedPreferences
                    .OrderBy(pair => pair.Value.order)
                    .ThenBy(pair => sourceIndices[pair.Key])
                    .Select(pair => sourceIndices[pair.Key])
                    .ToList();
                HashSet<int> orderedSet = new HashSet<int>(orderedColumns);
                foreach (int sourceIndex in defaultDisplayOrder)
                {
                    if (orderedSet.Add(sourceIndex)) orderedColumns.Add(sourceIndex);
                }
                displayOrder = orderedColumns.ToArray();
            }

            List<int> visibleColumns = new List<int>();
            foreach (int sourceIndex in displayOrder)
            {
                string key = keyProvider(sourceIndex, state.Columns[sourceIndex]);
                bool visible = savedPreferences.TryGetValue(key, out AssetInventoryColumnLayout preference)
                    ? preference.visible
                    : defaultVisibleColumns.Contains(sourceIndex);
                if (visible) visibleColumns.Add(sourceIndex);
            }
            state.VisibleColumns = visibleColumns.ToArray();

            sortIndex = IsSortableIndex(state, fallbackSortIndex) ? fallbackSortIndex : -1;
            sortDescending = fallbackSortDescending;
            if (layout?.sorting != null)
            {
                HashSet<string> seenSortKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (AssetInventorySortLayout sorting in layout.sorting)
                {
                    if (sorting == null || string.IsNullOrEmpty(sorting.key) ||
                        !seenSortKeys.Add(sorting.key) ||
                        !sourceIndices.TryGetValue(sorting.key, out int candidate) ||
                        !IsSortableIndex(state, candidate)) continue;

                    sortIndex = candidate;
                    sortDescending = sorting.descending;
                    break;
                }
            }

            state.SortedColumnIndex = sortIndex;
            if (sortIndex >= 0) state.Columns[sortIndex].SortedAscending = !sortDescending;
        }

        private static AssetInventoryTableLayout BuildLayout(
            CommonMultiColumnState state,
            IReadOnlyList<int> displayOrder,
            int sortIndex,
            bool sortDescending,
            Func<int, CommonMultiColumnColumn, string> keyProvider)
        {
            AssetInventoryTableLayout layout = new AssetInventoryTableLayout();
            HashSet<int> visibleColumns = new HashSet<int>(state.VisibleColumns);
            HashSet<int> addedColumns = new HashSet<int>();

            for (int order = 0; order < displayOrder.Count; order++)
            {
                int sourceIndex = displayOrder[order];
                if (sourceIndex < 0 || sourceIndex >= state.ColumnCount || !addedColumns.Add(sourceIndex)) continue;

                string key = keyProvider(sourceIndex, state.Columns[sourceIndex]);
                if (string.IsNullOrEmpty(key)) continue;
                layout.columns.Add(new AssetInventoryColumnLayout
                {
                    key = key,
                    width = ClampWidth(state.Columns[sourceIndex], state.Columns[sourceIndex].Width),
                    visible = visibleColumns.Contains(sourceIndex),
                    order = layout.columns.Count
                });
            }

            for (int sourceIndex = 0; sourceIndex < state.ColumnCount; sourceIndex++)
            {
                if (!addedColumns.Add(sourceIndex)) continue;

                string key = keyProvider(sourceIndex, state.Columns[sourceIndex]);
                if (string.IsNullOrEmpty(key)) continue;
                layout.columns.Add(new AssetInventoryColumnLayout
                {
                    key = key,
                    width = ClampWidth(state.Columns[sourceIndex], state.Columns[sourceIndex].Width),
                    visible = visibleColumns.Contains(sourceIndex),
                    order = layout.columns.Count
                });
            }

            if (IsSortableIndex(state, sortIndex))
            {
                string sortKey = keyProvider(sortIndex, state.Columns[sortIndex]);
                if (!string.IsNullOrEmpty(sortKey))
                {
                    layout.sorting.Add(new AssetInventorySortLayout
                    {
                        key = sortKey,
                        descending = sortDescending
                    });
                }
            }
            return layout;
        }

        private static Dictionary<string, int> CreateSourceIndexMap(
            CommonMultiColumnState state,
            Func<int, CommonMultiColumnColumn, string> keyProvider)
        {
            Dictionary<string, int> sourceIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int sourceIndex = 0; sourceIndex < state.ColumnCount; sourceIndex++)
            {
                string key = keyProvider(sourceIndex, state.Columns[sourceIndex]);
                if (!string.IsNullOrEmpty(key) && !sourceIndices.ContainsKey(key))
                {
                    sourceIndices[key] = sourceIndex;
                }
            }
            return sourceIndices;
        }

        private static Dictionary<string, AssetInventoryColumnLayout> CreateRecognizedPreferenceMap(
            AssetInventoryTableLayout layout,
            IReadOnlyDictionary<string, int> sourceIndices)
        {
            Dictionary<string, AssetInventoryColumnLayout> preferences =
                new Dictionary<string, AssetInventoryColumnLayout>(StringComparer.Ordinal);
            if (layout?.columns == null) return preferences;

            foreach (AssetInventoryColumnLayout preference in layout.columns)
            {
                if (preference == null || string.IsNullOrEmpty(preference.key) ||
                    !sourceIndices.ContainsKey(preference.key) ||
                    preferences.ContainsKey(preference.key)) continue;
                preferences[preference.key] = preference;
            }
            return preferences;
        }

        private static Dictionary<string, AssetInventoryColumnLayout> CreatePreferenceMap(
            AssetInventoryTableLayout layout)
        {
            Dictionary<string, AssetInventoryColumnLayout> preferences =
                new Dictionary<string, AssetInventoryColumnLayout>(StringComparer.Ordinal);
            if (layout?.columns == null) return preferences;

            foreach (AssetInventoryColumnLayout preference in layout.columns)
            {
                if (preference == null || string.IsNullOrEmpty(preference.key) ||
                    preferences.ContainsKey(preference.key)) continue;
                preferences[preference.key] = preference;
            }
            return preferences;
        }

        private static int[] GetDefaultDisplayOrder(CommonMultiColumnState state)
        {
            List<int> order = new List<int>();
            HashSet<int> added = new HashSet<int>();
            foreach (int sourceIndex in state.VisibleColumns)
            {
                if (sourceIndex >= 0 && sourceIndex < state.ColumnCount && added.Add(sourceIndex))
                {
                    order.Add(sourceIndex);
                }
            }
            for (int sourceIndex = 0; sourceIndex < state.ColumnCount; sourceIndex++)
            {
                if (added.Add(sourceIndex)) order.Add(sourceIndex);
            }
            return order.ToArray();
        }

        private static bool IsSortableIndex(CommonMultiColumnState state, int sourceIndex)
        {
            return sourceIndex >= 0 && sourceIndex < state.ColumnCount && state.Columns[sourceIndex].Sortable;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static float ClampWidth(CommonMultiColumnColumn column, float width)
        {
            float maximum = column.MaxWidth > 0f ? column.MaxWidth : float.MaxValue;
            return Mathf.Clamp(width, column.MinWidth, maximum);
        }

        private static bool LayoutsEqual(AssetInventoryTableLayout left, AssetInventoryTableLayout right)
        {
            if (left == null || right == null || left.columns == null || right.columns == null ||
                left.sorting == null || right.sorting == null ||
                left.columns.Count != right.columns.Count ||
                left.sorting.Count != right.sorting.Count) return false;

            for (int i = 0; i < left.columns.Count; i++)
            {
                AssetInventoryColumnLayout leftColumn = left.columns[i];
                AssetInventoryColumnLayout rightColumn = right.columns[i];
                if (leftColumn == null || rightColumn == null ||
                    leftColumn.key != rightColumn.key ||
                    Mathf.Abs(leftColumn.width - rightColumn.width) > 0.01f ||
                    leftColumn.visible != rightColumn.visible ||
                    leftColumn.order != rightColumn.order) return false;
            }

            for (int i = 0; i < left.sorting.Count; i++)
            {
                AssetInventorySortLayout leftSort = left.sorting[i];
                AssetInventorySortLayout rightSort = right.sorting[i];
                if (leftSort == null || rightSort == null ||
                    leftSort.key != rightSort.key ||
                    leftSort.descending != rightSort.descending) return false;
            }
            return true;
        }

        private static void EnsureCollections(AssetInventoryTableLayout layout)
        {
            if (layout.columns == null) layout.columns = new List<AssetInventoryColumnLayout>();
            if (layout.sorting == null) layout.sorting = new List<AssetInventorySortLayout>();
        }

        private static AssetInventoryTableLayout GetLayout(AssetInventoryTableLayoutKind kind)
        {
            switch (kind)
            {
                case AssetInventoryTableLayoutKind.Packages:
                    return AI.Config.packageTableLayout;
                case AssetInventoryTableLayoutKind.Search:
                    return AI.Config.searchTableLayout;
                case AssetInventoryTableLayoutKind.Reporting:
                    return AI.Config.reportTableLayout;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        private static void SetLayout(AssetInventoryTableLayoutKind kind, AssetInventoryTableLayout layout)
        {
            switch (kind)
            {
                case AssetInventoryTableLayoutKind.Packages:
                    AI.Config.packageTableLayout = layout;
                    break;
                case AssetInventoryTableLayoutKind.Search:
                    AI.Config.searchTableLayout = layout;
                    break;
                case AssetInventoryTableLayoutKind.Reporting:
                    AI.Config.reportTableLayout = layout;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        private static void ScheduleSave()
        {
            _dirty = true;
            _saveAt = EditorApplication.timeSinceStartup + SaveDelaySeconds;
            if (_saveScheduled) return;

            _saveScheduled = true;
            EditorApplication.update += SaveWhenDue;
        }

        private static void SaveWhenDue()
        {
            if (!_saveScheduled || EditorApplication.timeSinceStartup < _saveAt) return;

            EditorApplication.update -= SaveWhenDue;
            _saveScheduled = false;
            if (AI.TrySaveConfig()) _dirty = false;
        }

        private static void ResetSaveState()
        {
            EditorApplication.update -= SaveWhenDue;
            _dirty = false;
            _saveScheduled = false;
        }

        private static void PruneRegistrations()
        {
            for (int i = Registrations.Count - 1; i >= 0; i--)
            {
                if (!Registrations[i].Adapter.TryGetTarget(out _)) Registrations.RemoveAt(i);
            }
        }
    }
}
