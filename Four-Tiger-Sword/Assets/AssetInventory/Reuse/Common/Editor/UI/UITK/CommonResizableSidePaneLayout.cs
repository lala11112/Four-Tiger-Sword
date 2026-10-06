using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public enum CommonSidePane
    {
        Leading,
        Trailing
    }

    public sealed class CommonResizableSidePaneLayout : VisualElement
    {
        public sealed class PaneDefinition
        {
            public VisualElement Content;
            public float PreferredWidth = 300f;
            public float MinimumWidth = 180f;
            public float MaximumWidth = 720f;
            public bool IsOpen = true;
            public Action<float, bool> StateChanged;
        }

        public sealed class LayoutOptions
        {
            public float MainMinimumWidth = 320f;
            public float CloseThreshold = 72f;
            public float CollapsedWidth = 8f;
            public float CompactThreshold = 280f;
            public float WideThreshold = 480f;
            public float KeyboardStep = 16f;
        }

        public sealed class LayoutClasses
        {
            public string RootClass;
            public string MainClass;
            public string HostClass;
            public string LeadingHostClass;
            public string TrailingHostClass;
            public string ContentClass;
            public string DividerClass;
            public string DividerLineClass;
            public string CollapsedClass;
            public string CompactClass;
            public string WideClass;
            public string ResizingClass;
        }

        private sealed class PaneState
        {
            public CommonSidePane Side;
            public PaneDefinition Definition;
            public VisualElement Host;
            public VisualElement Divider;
            public float PreferredWidth;
            public bool IsOpen;
            public bool IsDragging;
            public int PointerId;
            public float DragStartPointer;
            public float DragStartWidth;
            public float DragStartPreferredWidth;
            public bool DragStartOpen;
        }

        private readonly LayoutOptions _options;
        private readonly LayoutClasses _classes;
        private readonly PaneState _leading;
        private readonly PaneState _trailing;

        public CommonResizableSidePaneLayout(
            VisualElement main,
            PaneDefinition leading = null,
            PaneDefinition trailing = null,
            LayoutOptions options = null,
            LayoutClasses classes = null)
        {
            _options = options ?? new LayoutOptions();
            _classes = classes ?? new LayoutClasses();

            CommonUITK.AddClasses(this, _classes.RootClass);
            style.flexDirection = FlexDirection.Row;
            style.flexGrow = 1f;
            style.minWidth = 0f;
            style.minHeight = 0f;

            if (leading?.Content != null)
            {
                _leading = CreatePane(CommonSidePane.Leading, leading);
                Add(_leading.Host);
            }

            VisualElement safeMain = main ?? new VisualElement();
            CommonUITK.AddClasses(safeMain, _classes.MainClass);
            safeMain.style.flexGrow = 1f;
            safeMain.style.flexShrink = 1f;
            safeMain.style.minWidth = 0f;
            safeMain.style.minHeight = 0f;
            Add(safeMain);

            if (trailing?.Content != null)
            {
                _trailing = CreatePane(CommonSidePane.Trailing, trailing);
                Add(_trailing.Host);
            }

            RegisterCallback<GeometryChangedEvent>(_ => RefreshLayout());
            RefreshLayout();
        }

        public bool IsPaneOpen(CommonSidePane side)
        {
            PaneState state = GetPane(side);
            return state != null && state.IsOpen;
        }

        public float GetPreferredWidth(CommonSidePane side)
        {
            PaneState state = GetPane(side);
            return state?.PreferredWidth ?? 0f;
        }

        public void SetPaneOpen(CommonSidePane side, bool isOpen, bool notify = false)
        {
            PaneState state = GetPane(side);
            if (state == null || state.IsOpen == isOpen) return;

            state.IsOpen = isOpen;
            RefreshLayout();
            if (notify) NotifyStateChanged(state);
        }

        public void SetPreferredWidth(CommonSidePane side, float width, bool notify = false)
        {
            PaneState state = GetPane(side);
            if (state == null) return;

            state.PreferredWidth = ClampPreferredWidth(state, width);
            if (state.PreferredWidth >= _options.CloseThreshold) state.IsOpen = true;
            RefreshLayout();
            if (notify) NotifyStateChanged(state);
        }

        private PaneState CreatePane(CommonSidePane side, PaneDefinition definition)
        {
            PaneState state = new PaneState
            {
                Side = side,
                Definition = definition,
                Host = new VisualElement(),
                PreferredWidth = ClampDefinitionWidth(definition, definition.PreferredWidth),
                IsOpen = definition.IsOpen
            };

            CommonUITK.AddClasses(
                state.Host,
                _classes.HostClass,
                side == CommonSidePane.Leading ? _classes.LeadingHostClass : _classes.TrailingHostClass);
            state.Host.style.position = Position.Relative;
            state.Host.style.flexShrink = 0f;
            state.Host.style.minWidth = 0f;
            state.Host.style.minHeight = 0f;
            state.Host.style.overflow = Overflow.Hidden;

            CommonUITK.AddClasses(definition.Content, _classes.ContentClass);
            definition.Content.style.flexGrow = 1f;
            definition.Content.style.minWidth = 0f;
            definition.Content.style.minHeight = 0f;
            state.Host.Add(definition.Content);

            state.Divider = new VisualElement
            {
                focusable = true,
                tabIndex = 0,
                tooltip = "Drag to resize. Drag to the outer edge to close; drag inward to reopen. Double-click to toggle."
            };
            CommonUITK.AddClasses(state.Divider, _classes.DividerClass);
            state.Divider.style.position = Position.Absolute;
            state.Divider.style.top = 0f;
            state.Divider.style.bottom = 0f;
            state.Divider.style.width = _options.CollapsedWidth;
            if (side == CommonSidePane.Leading)
            {
                state.Divider.style.right = 0f;
            }
            else
            {
                state.Divider.style.left = 0f;
            }

            VisualElement line = new VisualElement {pickingMode = PickingMode.Ignore};
            CommonUITK.AddClasses(line, _classes.DividerLineClass);
            state.Divider.Add(line);
            state.Host.Add(state.Divider);

            state.Divider.RegisterCallback<PointerDownEvent>(evt => BeginResize(state, evt));
            state.Divider.RegisterCallback<PointerMoveEvent>(evt => ContinueResize(state, evt));
            state.Divider.RegisterCallback<PointerUpEvent>(evt => EndResize(state, evt.pointerId));
            state.Divider.RegisterCallback<PointerCaptureOutEvent>(_ => EndResize(state, state.PointerId));
            state.Divider.RegisterCallback<KeyDownEvent>(evt => HandleDividerKey(state, evt));
            return state;
        }

        private void BeginResize(PaneState state, PointerDownEvent evt)
        {
            if (evt.button != 0) return;

            if (evt.clickCount >= 2)
            {
                TogglePane(state);
                evt.StopImmediatePropagation();
                return;
            }

            state.IsDragging = true;
            state.PointerId = evt.pointerId;
            state.DragStartPointer = evt.position.x;
            state.DragStartWidth = state.IsOpen ? GetActualWidth(state) : 0f;
            state.DragStartPreferredWidth = state.PreferredWidth;
            state.DragStartOpen = state.IsOpen;
            EnableClass(this, _classes.ResizingClass, true);
            state.Divider.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void ContinueResize(PaneState state, PointerMoveEvent evt)
        {
            if (!state.IsDragging || evt.pointerId != state.PointerId) return;

            float delta = evt.position.x - state.DragStartPointer;
            float requested = state.DragStartWidth + (state.Side == CommonSidePane.Leading ? delta : -delta);
            ApplyRequestedWidth(state, requested);
            evt.StopPropagation();
        }

        private void EndResize(PaneState state, int pointerId)
        {
            if (!state.IsDragging || pointerId != state.PointerId) return;

            state.IsDragging = false;
            if (state.Divider.HasPointerCapture(pointerId)) state.Divider.ReleasePointer(pointerId);
            EnableClass(this, _classes.ResizingClass, false);
            if (state.DragStartOpen != state.IsOpen || !Mathf.Approximately(state.DragStartPreferredWidth, state.PreferredWidth))
            {
                NotifyStateChanged(state);
            }
        }

        private void HandleDividerKey(PaneState state, KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter || evt.keyCode == KeyCode.Space)
            {
                TogglePane(state);
                evt.StopPropagation();
                return;
            }

            if (evt.keyCode == KeyCode.Home)
            {
                if (state.IsOpen)
                {
                    state.IsOpen = false;
                    RefreshLayout();
                    NotifyStateChanged(state);
                }
                evt.StopPropagation();
                return;
            }

            bool inward = state.Side == CommonSidePane.Leading
                ? evt.keyCode == KeyCode.RightArrow
                : evt.keyCode == KeyCode.LeftArrow;
            bool outward = state.Side == CommonSidePane.Leading
                ? evt.keyCode == KeyCode.LeftArrow
                : evt.keyCode == KeyCode.RightArrow;
            if (!inward && !outward) return;

            if (!state.IsOpen)
            {
                if (inward)
                {
                    state.IsOpen = true;
                    RefreshLayout();
                    NotifyStateChanged(state);
                }
            }
            else
            {
                float actualWidth = GetActualWidth(state);
                if (outward && actualWidth <= state.Definition.MinimumWidth + 0.5f)
                {
                    state.IsOpen = false;
                    RefreshLayout();
                }
                else
                {
                    ApplyRequestedWidth(state, actualWidth + (inward ? _options.KeyboardStep : -_options.KeyboardStep));
                }
                NotifyStateChanged(state);
            }
            evt.StopPropagation();
        }

        private void TogglePane(PaneState state)
        {
            state.IsOpen = !state.IsOpen;
            RefreshLayout();
            NotifyStateChanged(state);
        }

        private void ApplyRequestedWidth(PaneState state, float requested)
        {
            if (requested < _options.CloseThreshold)
            {
                state.IsOpen = false;
            }
            else
            {
                state.IsOpen = true;
                state.PreferredWidth = ClampPreferredWidth(state, requested);
            }
            RefreshLayout();
        }

        private void RefreshLayout()
        {
            float leadingWidth = GetDesiredWidth(_leading);
            float trailingWidth = GetDesiredWidth(_trailing);
            float totalWidth = resolvedStyle.width;
            if (!float.IsNaN(totalWidth) && totalWidth > 0f)
            {
                int paneCount = (_leading == null ? 0 : 1) + (_trailing == null ? 0 : 1);
                float minimumPaneWidth = _options.CollapsedWidth * paneCount;
                float availableForPanes = Mathf.Max(minimumPaneWidth, totalWidth - _options.MainMinimumWidth);
                float requested = leadingWidth + trailingWidth;
                if (requested > availableForPanes && requested > 0f)
                {
                    float scale = availableForPanes / requested;
                    leadingWidth = Mathf.Max(_leading == null ? 0f : _options.CollapsedWidth, leadingWidth * scale);
                    trailingWidth = Mathf.Max(_trailing == null ? 0f : _options.CollapsedWidth, trailingWidth * scale);
                }
            }

            ApplyPaneLayout(_leading, leadingWidth);
            ApplyPaneLayout(_trailing, trailingWidth);
        }

        private void ApplyPaneLayout(PaneState state, float width)
        {
            if (state == null) return;

            state.Host.style.width = Mathf.Max(_options.CollapsedWidth, width);
            state.Definition.Content.style.display = state.IsOpen ? DisplayStyle.Flex : DisplayStyle.None;
            EnableClass(state.Host, _classes.CollapsedClass, !state.IsOpen);
            EnableClass(state.Host, _classes.CompactClass, state.IsOpen && width < _options.CompactThreshold);
            EnableClass(state.Host, _classes.WideClass, state.IsOpen && width >= _options.WideThreshold);
        }

        private float GetDesiredWidth(PaneState state)
        {
            if (state == null) return 0f;
            return state.IsOpen
                ? Mathf.Clamp(state.PreferredWidth, state.Definition.MinimumWidth, state.Definition.MaximumWidth)
                : _options.CollapsedWidth;
        }

        private float GetAvailableMaximumWidth(PaneState state)
        {
            float configuredMaximum = Mathf.Max(state.Definition.MinimumWidth, state.Definition.MaximumWidth);
            float totalWidth = resolvedStyle.width;
            if (float.IsNaN(totalWidth) || totalWidth <= 0f) return configuredMaximum;

            PaneState other = state.Side == CommonSidePane.Leading ? _trailing : _leading;
            float otherWidth = other == null
                ? 0f
                : other.IsOpen ? GetActualWidth(other) : _options.CollapsedWidth;
            float available = totalWidth - _options.MainMinimumWidth - otherWidth;
            return Mathf.Max(_options.CollapsedWidth, Mathf.Min(configuredMaximum, available));
        }

        private float GetActualWidth(PaneState state)
        {
            float width = state.Host.resolvedStyle.width;
            return float.IsNaN(width) || width <= 0f ? state.PreferredWidth : width;
        }

        private float ClampPreferredWidth(PaneState state, float width)
        {
            return ClampDefinitionWidth(state.Definition, width);
        }

        private static float ClampDefinitionWidth(PaneDefinition definition, float width)
        {
            float maximum = Mathf.Max(definition.MinimumWidth, definition.MaximumWidth);
            return Mathf.Clamp(width, definition.MinimumWidth, maximum);
        }

        private PaneState GetPane(CommonSidePane side)
        {
            return side == CommonSidePane.Leading ? _leading : _trailing;
        }

        private static void NotifyStateChanged(PaneState state)
        {
            state.Definition.StateChanged?.Invoke(state.PreferredWidth, state.IsOpen);
        }

        private static void EnableClass(VisualElement element, string className, bool enabled)
        {
            if (element == null || string.IsNullOrWhiteSpace(className)) return;
            element.EnableInClassList(className, enabled);
        }
    }
}
