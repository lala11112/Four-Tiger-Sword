using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    /// <summary>
    /// Reusable two-dimensional comparison surface with one shared scroll area.
    /// Product packages provide the row headers, column headers, cells, and styling.
    /// </summary>
    public sealed class CommonComparisonMatrix<TRow, TColumn> : VisualElement
    {
        public sealed class MatrixClasses
        {
            public string RootClass;
            public string ScrollClass;
            public string ContentClass;
            public string HeaderClass;
            public string CornerClass;
            public string ColumnHeaderClass;
            public string RowClass;
            public string RowHeaderClass;
            public string CellClass;
        }

        private readonly struct CellKey : IEquatable<CellKey>
        {
            public readonly TRow Row;
            public readonly TColumn Column;

            public CellKey(TRow row, TColumn column)
            {
                Row = row;
                Column = column;
            }

            public bool Equals(CellKey other)
            {
                return EqualityComparer<TRow>.Default.Equals(Row, other.Row) &&
                    EqualityComparer<TColumn>.Default.Equals(Column, other.Column);
            }

            public override bool Equals(object obj)
            {
                return obj is CellKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int rowHash = ReferenceEquals(Row, null) ? 0 : EqualityComparer<TRow>.Default.GetHashCode(Row);
                    int columnHash = ReferenceEquals(Column, null) ? 0 : EqualityComparer<TColumn>.Default.GetHashCode(Column);
                    return (rowHash * 397) ^ columnHash;
                }
            }
        }

        private readonly Func<VisualElement> _makeCorner;
        private readonly Func<TColumn, VisualElement> _makeColumnHeader;
        private readonly Func<TRow, VisualElement> _makeRowHeader;
        private readonly Func<TRow, TColumn, VisualElement> _makeCell;
        private readonly MatrixClasses _classes;
        private readonly float _rowHeaderWidth;
        private readonly float _columnWidth;
        private readonly float _headerHeight;
        private readonly float _rowHeight;
        private readonly ScrollView _scroll;
        private readonly VisualElement _content;
        private readonly Dictionary<TRow, VisualElement> _rowHeaders = new Dictionary<TRow, VisualElement>();
        private readonly Dictionary<TColumn, VisualElement> _columnHeaders = new Dictionary<TColumn, VisualElement>();
        private readonly Dictionary<CellKey, VisualElement> _cells = new Dictionary<CellKey, VisualElement>();

        private IReadOnlyList<TRow> _rows = Array.Empty<TRow>();
        private IReadOnlyList<TColumn> _columns = Array.Empty<TColumn>();

        public CommonComparisonMatrix(
            Func<VisualElement> makeCorner,
            Func<TColumn, VisualElement> makeColumnHeader,
            Func<TRow, VisualElement> makeRowHeader,
            Func<TRow, TColumn, VisualElement> makeCell,
            float rowHeaderWidth,
            float columnWidth,
            float headerHeight,
            float rowHeight,
            MatrixClasses classes = null)
        {
            _makeCorner = makeCorner;
            _makeColumnHeader = makeColumnHeader ?? throw new ArgumentNullException(nameof(makeColumnHeader));
            _makeRowHeader = makeRowHeader ?? throw new ArgumentNullException(nameof(makeRowHeader));
            _makeCell = makeCell ?? throw new ArgumentNullException(nameof(makeCell));
            _rowHeaderWidth = Mathf.Max(1f, rowHeaderWidth);
            _columnWidth = Mathf.Max(1f, columnWidth);
            _headerHeight = Mathf.Max(1f, headerHeight);
            _rowHeight = Mathf.Max(1f, rowHeight);
            _classes = classes ?? new MatrixClasses();

            CommonUITK.AddClasses(this, _classes.RootClass);
            style.flexGrow = 1f;

            _scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            CommonUITK.AddClasses(_scroll, _classes.ScrollClass);
            _scroll.style.flexGrow = 1f;
            _scroll.style.minHeight = 0f;
            Add(_scroll);

            _content = CommonUITK.CreateContainer(_classes.ContentClass);
            _scroll.Add(_content);
        }

        public ScrollView ScrollView => _scroll;
        public IReadOnlyList<TRow> Rows => _rows;
        public IReadOnlyList<TColumn> Columns => _columns;

        public void SetItems(IReadOnlyList<TRow> rows, IReadOnlyList<TColumn> columns)
        {
            _rows = rows ?? Array.Empty<TRow>();
            _columns = columns ?? Array.Empty<TColumn>();
            Rebuild();
        }

        public void Rebuild()
        {
            _content.Clear();
            _rowHeaders.Clear();
            _columnHeaders.Clear();
            _cells.Clear();

            float matrixWidth = _rowHeaderWidth + _columns.Count * _columnWidth;
            _content.style.width = matrixWidth;
            _content.Add(BuildHeader(matrixWidth));

            for (int rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
            {
                TRow rowItem = _rows[rowIndex];
                VisualElement row = CommonUITK.CreateContainer(_classes.RowClass);
                row.style.width = matrixWidth;
                row.style.height = _rowHeight;
                row.style.flexShrink = 0f;

                VisualElement rowHeader = CreateSlot(_makeRowHeader(rowItem), _classes.RowHeaderClass, _rowHeaderWidth, _rowHeight);
                _rowHeaders[rowItem] = rowHeader;
                row.Add(rowHeader);

                for (int columnIndex = 0; columnIndex < _columns.Count; columnIndex++)
                {
                    TColumn columnItem = _columns[columnIndex];
                    VisualElement cell = CreateSlot(_makeCell(rowItem, columnItem), _classes.CellClass, _columnWidth, _rowHeight);
                    _cells[new CellKey(rowItem, columnItem)] = cell;
                    row.Add(cell);
                }

                _content.Add(row);
            }
        }

        public void RefreshRow(TRow row)
        {
            if (_rowHeaders.TryGetValue(row, out VisualElement rowHeader))
            {
                ReplaceSlotContent(rowHeader, _makeRowHeader(row));
            }

            for (int i = 0; i < _columns.Count; i++)
            {
                RefreshCell(row, _columns[i]);
            }
        }

        public void RefreshColumn(TColumn column)
        {
            if (_columnHeaders.TryGetValue(column, out VisualElement columnHeader))
            {
                ReplaceSlotContent(columnHeader, _makeColumnHeader(column));
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                RefreshCell(_rows[i], column);
            }
        }

        public void RefreshCell(TRow row, TColumn column)
        {
            CellKey key = new CellKey(row, column);
            if (_cells.TryGetValue(key, out VisualElement cell))
            {
                ReplaceSlotContent(cell, _makeCell(row, column));
            }
        }

        public VisualElement GetCell(TRow row, TColumn column)
        {
            _cells.TryGetValue(new CellKey(row, column), out VisualElement cell);
            return cell;
        }

        private VisualElement BuildHeader(float matrixWidth)
        {
            VisualElement header = CommonUITK.CreateContainer(_classes.HeaderClass);
            header.style.width = matrixWidth;
            header.style.height = _headerHeight;
            header.style.flexShrink = 0f;
            header.Add(CreateSlot(_makeCorner?.Invoke(), _classes.CornerClass, _rowHeaderWidth, _headerHeight));

            for (int i = 0; i < _columns.Count; i++)
            {
                TColumn columnItem = _columns[i];
                VisualElement columnHeader = CreateSlot(_makeColumnHeader(columnItem), _classes.ColumnHeaderClass, _columnWidth, _headerHeight);
                _columnHeaders[columnItem] = columnHeader;
                header.Add(columnHeader);
            }

            return header;
        }

        private static VisualElement CreateSlot(VisualElement content, string className, float width, float height)
        {
            VisualElement slot = CommonUITK.CreateContainer(className);
            slot.style.width = width;
            slot.style.height = height;
            slot.style.flexShrink = 0f;
            if (content != null) slot.Add(content);
            return slot;
        }

        private static void ReplaceSlotContent(VisualElement slot, VisualElement content)
        {
            slot.Clear();
            if (content != null) slot.Add(content);
        }
    }
}
