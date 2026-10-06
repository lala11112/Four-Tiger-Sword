using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    internal sealed class GridControl
    {
        public List<AssetInfo> packages;
        private Texture[] _previews;

        public List<AssetInfo> selectionItems;
        public int selectionCount;
        public int selectionTile;
        public int selectionPackageCount;
        public long selectionSize;
        public bool LastClickAlt;

        private bool[] _selection;
        private int _cellsPerRow = 1;
        private float _actualTileHeight;
        private List<AssetInfo> _allPackages;
        private Dictionary<int, AssetInfo> _allPackagesById;
        private Action _bulkHandler;
        private readonly HashSet<int> _selectionPackageIds = new HashSet<int>();

        public int CellsPerRow => _cellsPerRow;
        public float ActualTileHeight => _actualTileHeight;
        public bool HasPreviewSlots => _previews != null;
        public int PreviewCount => _previews?.Length ?? 0;

        public void ResetPreviews(int count)
        {
            DisposePreviewTextures();
            _previews = count > 0 ? new Texture[count] : Array.Empty<Texture>();
            EnsureSelectionLength();
        }

        public Texture GetPreview(int index)
        {
            return index >= 0 && index < PreviewCount ? _previews[index] : null;
        }

        public bool SetPreview(int index, Texture preview)
        {
            if (index < 0 || index >= PreviewCount) return false;

            _previews[index] = preview;
            return true;
        }

        public void ClearPreview(int index)
        {
            if (index >= 0 && index < PreviewCount) _previews[index] = null;
        }

        public void Init(List<AssetInfo> allPackages, IEnumerable<AssetInfo> visiblePackages, Action bulkHandler)
        {
            packages = NormalizeVisiblePackages(visiblePackages);
            if (!ReferenceEquals(_allPackages, allPackages))
            {
                _allPackages = allPackages;
                _allPackagesById = BuildAssetLookup(allPackages);
            }
            _bulkHandler = bulkHandler;
            EnsureSelectionLength();
            SetSingleVisualSelection(selectionTile);
            CalculateBulkSelection();
        }

        public void LimitSelection(int count)
        {
            if (selectionTile >= count) selectionTile = 0;
        }

        public void SetBulkSelection(List<AssetInfo> items)
        {
            selectionItems = items ?? new List<AssetInfo>();
            UpdateBulkSelectionStats();
        }

        public void SetVisualBulkSelection(List<AssetInfo> items)
        {
            List<AssetInfo> selectedItems = items ?? new List<AssetInfo>();
            if (packages == null)
            {
                SetBulkSelection(selectedItems);
                return;
            }

            HashSet<int> selectedIds = new HashSet<int>();
            for (int i = 0; i < selectedItems.Count; i++)
            {
                if (selectedItems[i] != null) selectedIds.Add(selectedItems[i].AssetId);
            }

            EnsureSelectionLength();
            Array.Clear(_selection, 0, _selection.Length);
            int firstIndex = -1;
            for (int i = 0; i < packages.Count && i < _selection.Length; i++)
            {
                if (!selectedIds.Contains(packages[i].AssetId)) continue;

                _selection[i] = true;
                if (firstIndex < 0) firstIndex = i;
            }
            selectionTile = Mathf.Max(0, firstIndex);
            CalculateBulkSelection();
        }

        public void SetVisualSelectionIndices(IReadOnlyList<int> indices, int activeIndex)
        {
            EnsureSelectionLength();
            Array.Clear(_selection, 0, _selection.Length);

            int firstIndex = -1;
            if (indices != null)
            {
                for (int i = 0; i < indices.Count; i++)
                {
                    int index = indices[i];
                    if (index < 0 || index >= _selection.Length) continue;

                    _selection[index] = true;
                    if (firstIndex < 0) firstIndex = index;
                }
            }

            selectionTile = firstIndex < 0
                ? 0
                : activeIndex >= 0 && activeIndex < _selection.Length ? activeIndex : firstIndex;
            CalculateBulkSelection();
        }

        public void SetLayoutMetrics(int cellsPerRow, float actualTileHeight)
        {
            _cellsPerRow = Mathf.Max(1, cellsPerRow);
            _actualTileHeight = Mathf.Max(0f, actualTileHeight);
        }

        public void DeselectAll()
        {
            selectionTile = 0;
            SetSingleVisualSelection(selectionTile);
            CalculateBulkSelection();
        }

        public void Select(AssetInfo info)
        {
            selectionTile = 0;
            if (packages != null && info != null)
            {
                for (int i = 0; i < packages.Count; i++)
                {
                    if (packages[i].AssetId != info.AssetId) continue;

                    selectionTile = i;
                    break;
                }
            }

            SetSingleVisualSelection(selectionTile);
            CalculateBulkSelection();
        }

        private void DisposePreviewTextures()
        {
            if (_previews == null) return;

            foreach (Texture preview in _previews)
            {
                if (preview == null) continue;
                if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(preview))) continue;

                UnityEngine.Object.DestroyImmediate(preview);
            }
        }

        private List<AssetInfo> NormalizeVisiblePackages(IEnumerable<AssetInfo> visiblePackages)
        {
            if (visiblePackages == null) return null;

            List<AssetInfo> visibleList = visiblePackages as List<AssetInfo>;
            if (visibleList != null && !ContainsNull(visibleList) && (!HasPreviewSlots || visibleList.Count == PreviewCount))
            {
                return visibleList;
            }

            List<AssetInfo> result = new List<AssetInfo>();
            foreach (AssetInfo item in visiblePackages)
            {
                if (item != null) result.Add(item);
            }
            return result;
        }

        private static bool ContainsNull(List<AssetInfo> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == null) return true;
            }
            return false;
        }

        private static Dictionary<int, AssetInfo> BuildAssetLookup(List<AssetInfo> allPackages)
        {
            if (allPackages == null) return null;

            Dictionary<int, AssetInfo> result = new Dictionary<int, AssetInfo>();
            for (int i = 0; i < allPackages.Count; i++)
            {
                AssetInfo asset = allPackages[i];
                if (asset == null || result.ContainsKey(asset.AssetId)) continue;
                result.Add(asset.AssetId, asset);
            }
            return result;
        }

        private void EnsureSelectionLength()
        {
            int count = PreviewCount;
            if (_selection != null && _selection.Length == count) return;

            _selection = new bool[count];
            if (count > 0)
            {
                selectionTile = Mathf.Clamp(selectionTile, 0, count - 1);
                _selection[selectionTile] = true;
            }
            else
            {
                selectionTile = 0;
            }
        }

        private void SetSingleVisualSelection(int index)
        {
            EnsureSelectionLength();
            Array.Clear(_selection, 0, _selection.Length);
            if (index >= 0 && index < _selection.Length) _selection[index] = true;
        }

        private void CalculateBulkSelection()
        {
            if (selectionItems == null) selectionItems = new List<AssetInfo>();
            else selectionItems.Clear();

            EnsureSelectionLength();
            if (packages != null)
            {
                int count = Mathf.Min(_selection.Length, packages.Count);
                for (int i = 0; i < count; i++)
                {
                    if (_selection[i] && packages[i] != null) selectionItems.Add(packages[i]);
                }
            }

            UpdateBulkSelectionStats();
        }

        private void UpdateBulkSelectionStats()
        {
            selectionCount = selectionItems.Count;
            selectionSize = 0;
            _selectionPackageIds.Clear();
            for (int i = 0; i < selectionItems.Count; i++)
            {
                AssetInfo info = selectionItems[i];
                if (info == null) continue;

                selectionSize += info.Size;
                _selectionPackageIds.Add(info.AssetId);
                info.CheckIfInProject();
            }
            selectionPackageCount = _selectionPackageIds.Count;
            Assets.ResolveParents(selectionItems, _allPackagesById);
            _bulkHandler?.Invoke();
        }
    }
}
