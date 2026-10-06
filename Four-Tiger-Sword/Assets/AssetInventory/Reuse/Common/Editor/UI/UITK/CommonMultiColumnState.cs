using System;
using System.Collections.Generic;
using UnityEngine;

namespace ImpossibleRobert.Common
{
    /// <summary>
    /// Serializable retained-mode column definitions and user state for UI Toolkit multi-column views.
    /// </summary>
    [Serializable]
    public sealed class CommonMultiColumnState
    {
        [SerializeField] private CommonMultiColumnColumn[] _columns;
        [SerializeField] private int[] _visibleColumns;
        [SerializeField] private int _sortedColumnIndex = -1;

        public CommonMultiColumnColumn[] Columns => _columns ?? Array.Empty<CommonMultiColumnColumn>();
        public int ColumnCount => Columns.Length;

        public int[] VisibleColumns
        {
            get => _visibleColumns ?? Array.Empty<int>();
            set => _visibleColumns = SanitizeVisibleColumns(value);
        }

        public int SortedColumnIndex
        {
            get => _sortedColumnIndex;
            set => _sortedColumnIndex = value >= 0 && value < ColumnCount ? value : -1;
        }

        public CommonMultiColumnState(CommonMultiColumnColumn[] columns, int[] visibleColumns = null)
        {
            _columns = columns ?? Array.Empty<CommonMultiColumnColumn>();
            _visibleColumns = SanitizeVisibleColumns(visibleColumns ?? CreateAllColumnIndices());
        }

        public static CommonMultiColumnState RestoreCompatibleState(
            CommonMultiColumnState serializedState,
            CommonMultiColumnState defaultState)
        {
            if (defaultState == null) throw new ArgumentNullException(nameof(defaultState));
            if (!IsCompatible(serializedState, defaultState)) return defaultState;

            for (int i = 0; i < defaultState.ColumnCount; i++)
            {
                CommonMultiColumnColumn source = serializedState.Columns[i];
                CommonMultiColumnColumn target = defaultState.Columns[i];
                target.Width = target.ClampWidth(source.Width);
                target.SortedAscending = source.SortedAscending;
            }

            defaultState.VisibleColumns = serializedState.VisibleColumns;
            defaultState.SortedColumnIndex = serializedState.SortedColumnIndex;
            return defaultState;
        }

        public float[] ExtractWidths()
        {
            if (ColumnCount == 0) return null;

            float[] widths = new float[ColumnCount];
            for (int i = 0; i < ColumnCount; i++) widths[i] = Columns[i].Width;
            return widths;
        }

        public void RestoreWidths(float[] savedWidths)
        {
            if (savedWidths == null || savedWidths.Length != ColumnCount) return;

            for (int i = 0; i < savedWidths.Length; i++)
            {
                Columns[i].Width = Columns[i].ClampWidth(savedWidths[i]);
            }
        }

        private static bool IsCompatible(CommonMultiColumnState serializedState, CommonMultiColumnState defaultState)
        {
            if (serializedState == null || serializedState.ColumnCount != defaultState.ColumnCount) return false;

            for (int i = 0; i < defaultState.ColumnCount; i++)
            {
                if (!serializedState.Columns[i].HasSameIdentity(defaultState.Columns[i])) return false;
            }
            return true;
        }

        private int[] CreateAllColumnIndices()
        {
            int[] indices = new int[ColumnCount];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            return indices;
        }

        private int[] SanitizeVisibleColumns(int[] columns)
        {
            List<int> sanitized = new List<int>();
            HashSet<int> seen = new HashSet<int>();
            if (columns != null)
            {
                foreach (int index in columns)
                {
                    if (index >= 0 && index < ColumnCount && seen.Add(index)) sanitized.Add(index);
                }
            }

            for (int i = ColumnCount - 1; i >= 0; i--)
            {
                if (Columns[i].Optional || !seen.Add(i)) continue;
                sanitized.Insert(0, i);
            }
            return sanitized.ToArray();
        }
    }

    [Serializable]
    public sealed class CommonMultiColumnColumn
    {
        [SerializeField] private string _title;
        [SerializeField] private float _width;
        [SerializeField] private float _minWidth;
        [SerializeField] private float _maxWidth;
        [SerializeField] private bool _sortable;
        [SerializeField] private bool _optional;
        [SerializeField] private bool _stretchable;
        [SerializeField] private bool _sortedAscending = true;
        [SerializeField] private int _userData;

        public string Title => _title ?? string.Empty;
        public float Width { get => _width; set => _width = value; }
        public float MinWidth => _minWidth;
        public float MaxWidth => _maxWidth;
        public bool Sortable => _sortable;
        public bool Optional => _optional;
        public bool Stretchable => _stretchable;
        public bool SortedAscending { get => _sortedAscending; set => _sortedAscending = value; }
        public int UserData => _userData;

        public CommonMultiColumnColumn(
            string title,
            float width,
            float minWidth,
            float maxWidth = 0f,
            bool sortable = true,
            bool optional = true,
            bool stretchable = false,
            int userData = 0)
        {
            _title = title ?? string.Empty;
            _width = width;
            _minWidth = minWidth;
            _maxWidth = maxWidth;
            _sortable = sortable;
            _optional = optional;
            _stretchable = stretchable;
            _userData = userData;
        }

        internal float ClampWidth(float width)
        {
            float maximum = _maxWidth > 0f ? _maxWidth : float.MaxValue;
            return Mathf.Clamp(width, _minWidth, maximum);
        }

        internal bool HasSameIdentity(CommonMultiColumnColumn other)
        {
            return other != null && _title == other._title && _userData == other._userData;
        }
    }
}
