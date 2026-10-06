using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    /// <summary>
    /// Preserves requested UI Toolkit multi-column widths and horizontal overflow on Unity versions
    /// that compress oversized columns into the viewport despite horizontal scrolling being enabled.
    /// </summary>
    public sealed class CommonMultiColumnOverflowController
    {
        private const string HeaderContainerName = "unity-multi-column-view__header-container";
        private const string HeaderClass = "unity-multi-column-header";
        private const string HeaderColumnsClass = "unity-multi-column-header__column-container";
        private const string ResizeHandlesClass = "unity-multi-column-header__resize-handle-container";
        private const string RowClass = "unity-multi-column-view__row-container";

        private readonly MultiColumnTreeView _view;
        private readonly Dictionary<string, float> _effectiveWidths = new Dictionary<string, float>();
        private readonly List<string> _visibleColumnNames = new List<string>();
        private bool _refreshScheduled;
        private float _lastViewWidth = -1f;

        public float ContentWidth { get; private set; }
        public int VisibleColumnCount => _visibleColumnNames.Count;

        public CommonMultiColumnOverflowController(MultiColumnTreeView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _view.horizontalScrollingEnabled = true;
            _view.columns.stretchMode = Columns.StretchMode.Grow;
            _view.RegisterCallback<AttachToPanelEvent>(_ => ScheduleRefresh());
            _view.RegisterCallback<GeometryChangedEvent>(OnViewGeometryChanged);
            _view.RegisterCallback<PointerUpEvent>(_ => ScheduleRefresh());
#if UNITY_6000_0_OR_NEWER
            _view.columns.propertyChanged += (_, __) => ScheduleRefresh();

            foreach (Column column in _view.columns)
            {
                column.propertyChanged += (_, __) => ScheduleRefresh();
            }
#endif

            Refresh();
        }

        public void ScheduleRefresh()
        {
            if (_refreshScheduled) return;
            if (_view.panel == null)
            {
                Refresh();
                return;
            }

            _refreshScheduled = true;
            _view.schedule.Execute(Refresh).ExecuteLater(0);
        }

        public void Refresh()
        {
            ScrollView scrollView = _view.Q<ScrollView>();
            float viewportWidth = scrollView?.contentViewport.layout.width ?? 0f;
            if (float.IsNaN(viewportWidth) || float.IsInfinity(viewportWidth) || viewportWidth <= 0f)
            {
                viewportWidth = _view.layout.width;
            }
            Refresh(viewportWidth);
        }

        public void Refresh(float viewportWidth)
        {
            _refreshScheduled = false;
            RebuildEffectiveWidths(Mathf.Max(0f, viewportWidth));

            ScrollView scrollView = _view.Q<ScrollView>();
            if (scrollView == null || ContentWidth <= 0f) return;

            scrollView.horizontalScrollerVisibility = ScrollerVisibility.Auto;

            SetFixedWidth(_view.Q<VisualElement>(HeaderContainerName), ContentWidth);
            SetFixedWidth(_view.Q(className: HeaderClass), ContentWidth);
            SetFixedWidth(_view.Q(className: HeaderColumnsClass), ContentWidth);
            SetFixedWidth(_view.Q(className: ResizeHandlesClass), ContentWidth);
            SetFixedWidth(scrollView.contentContainer, ContentWidth);

            foreach (string columnName in _visibleColumnNames)
            {
                SetFixedWidth(_view.Q<VisualElement>(columnName), _effectiveWidths[columnName]);
            }

            _view.Query<VisualElement>(className: RowClass).ForEach(ApplyRow);
        }

        public void ApplyCell(VisualElement cell, string columnName)
        {
            if (cell == null || string.IsNullOrEmpty(columnName)) return;
            if (!_effectiveWidths.TryGetValue(columnName, out float width)) return;

            SetFixedWidth(cell, width);
            SetFixedWidth(cell.parent, ContentWidth);
        }

        public float GetEffectiveWidth(string columnName)
        {
            return columnName != null && _effectiveWidths.TryGetValue(columnName, out float width) ? width : 0f;
        }

        public string GetVisibleColumnName(int displayIndex)
        {
            return displayIndex >= 0 && displayIndex < _visibleColumnNames.Count
                ? _visibleColumnNames[displayIndex]
                : null;
        }

        private void RebuildEffectiveWidths(float viewportWidth)
        {
            _effectiveWidths.Clear();
            _visibleColumnNames.Clear();

            float requestedWidth = 0f;
            float stretchableWidth = 0f;
            foreach (Column column in _view.columns)
            {
                if (!column.visible) continue;

                float width = Mathf.Max(0f, column.width.value);
                _effectiveWidths[column.name] = width;
                requestedWidth += width;
                if (column.stretchable) stretchableWidth += width;
            }

            VisualElement headerColumns = _view.Q<VisualElement>(className: HeaderColumnsClass);
            if (headerColumns != null)
            {
                for (int i = 0; i < headerColumns.hierarchy.childCount; i++)
                {
                    string columnName = headerColumns.hierarchy[i].name;
                    if (_effectiveWidths.ContainsKey(columnName)) _visibleColumnNames.Add(columnName);
                }
            }
            foreach (Column column in _view.columns)
            {
                if (column.visible && !_visibleColumnNames.Contains(column.name))
                {
                    _visibleColumnNames.Add(column.name);
                }
            }

            float extraWidth = Mathf.Max(0f, viewportWidth - requestedWidth);
            if (extraWidth > 0f && stretchableWidth > 0f)
            {
                foreach (Column column in _view.columns)
                {
                    if (!column.visible || !column.stretchable) continue;

                    float width = _effectiveWidths[column.name];
                    _effectiveWidths[column.name] = width + extraWidth * (width / stretchableWidth);
                }
            }

            ContentWidth = Mathf.Max(requestedWidth, viewportWidth);
        }

        private void ApplyRow(VisualElement row)
        {
            SetFixedWidth(row, ContentWidth);
            int cellCount = Mathf.Min(row.hierarchy.childCount, _visibleColumnNames.Count);
            for (int i = 0; i < cellCount; i++)
            {
                string columnName = _visibleColumnNames[i];
                SetFixedWidth(row.hierarchy[i], _effectiveWidths[columnName]);
            }
        }

        private void OnViewGeometryChanged(GeometryChangedEvent evt)
        {
            if (Mathf.Approximately(_lastViewWidth, evt.newRect.width)) return;

            _lastViewWidth = evt.newRect.width;
            ScheduleRefresh();
        }

        private static void SetFixedWidth(VisualElement element, float width)
        {
            if (element == null) return;

            element.style.width = width;
            element.style.minWidth = width;
            element.style.maxWidth = width;
            element.style.flexGrow = 0f;
            element.style.flexShrink = 0f;
        }
    }
}
