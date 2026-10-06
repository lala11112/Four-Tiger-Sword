using System;
using System.Collections.Generic;
using System.Linq;
using ImpossibleRobert.Common;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    /// <summary>
    /// Retained UI Toolkit dependency graph canvas. The graph model and layout engines remain
    /// independent; this element owns only viewport state, interaction, and presentation.
    /// </summary>
    public class DependencyGraphRenderer : VisualElement
    {
        private const float MinZoom = 0.1f;
        private const float MaxZoom = 2f;
        private const float LabelLodThreshold = 0.42f;
        private const float IconLodThreshold = 0.12f;
        private const float EdgeWidth = 1.15f;
        private const float ArrowSize = 6f;

        private static readonly Color BackgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Color GridColor = new Color(0.25f, 0.25f, 0.25f, 0.3f);
        private static readonly Color SelectionColor = new Color(1f, 0.82f, 0.2f, 0.95f);
        private static readonly Color HoverColor = new Color(1f, 1f, 1f, 0.65f);

        private readonly CommonUITKMeshBuilder _meshBuilder = new CommonUITKMeshBuilder(8192);
        private readonly List<NodeOverlay> _nodeOverlayPool = new List<NodeOverlay>();
        private readonly List<PackageOverlay> _packageOverlayPool = new List<PackageOverlay>();
        private readonly VisualElement _packageOverlayLayer;
        private readonly VisualElement _nodeOverlayLayer;
        private readonly Label _nodeCountLabel;
        private readonly Label _zoomLabel;
        private readonly Label _emptyLabel;

        private DependencyGraphData _graphData;
        private ForceDirectedLayout _layout;
        private Vector2 _panOffset;
        private float _zoom = 1f;
        private DependencyGraphNode _hoveredNode;
        private DependencyGraphNode _selectedNode;
        private PackageNode _selectedPackage;
        private PackageNode _hoveredPackage;
        private bool _isDragging;
        private int _dragPointerId = -1;
        private Vector2 _lastPointerPosition;
        private bool _showLabels = true;
        private bool _showIcons = true;
        private bool _showArrows = true;
        private bool _frameWhenReady;
        private string _searchText = string.Empty;
        private int _activeNodeOverlayCount;
        private int _activePackageOverlayCount;

        public DependencyGraphNode HoveredNode => _hoveredNode;
        public DependencyGraphNode SelectedNode => _selectedNode;
        public int RetainedNodeOverlayCount => _nodeOverlayPool.Count;
        public int ActiveNodeOverlayCount => _activeNodeOverlayCount;
        public int RetainedPackageOverlayCount => _packageOverlayPool.Count;
        public int ActivePackageOverlayCount => _activePackageOverlayCount;

        public event Action<DependencyGraphNode> OnNodeSelected;
        public event Action<DependencyGraphNode> OnNodeDoubleClicked;
        public event Action<DependencyGraphNode> OnNodeRightClicked;
        public event Action<PackageNode> OnPackageClicked;
        public event Action<PackageNode> OnPackageDoubleClicked;
        public event Action<PackageNode> OnPackageRightClicked;

        public DependencyGraphRenderer()
        {
            name = "dependency-graph-canvas";
            pickingMode = PickingMode.Position;
            focusable = true;
            tabIndex = 0;
            style.position = Position.Relative;
            style.overflow = Overflow.Hidden;
            style.flexGrow = 1f;
            style.minHeight = 160f;

            _packageOverlayLayer = CreateOverlayLayer("package-overlays");
            _nodeOverlayLayer = CreateOverlayLayer("node-overlays");
            Add(_packageOverlayLayer);
            Add(_nodeOverlayLayer);

            _emptyLabel = CreateOverlayLabel("No dependencies to display", "ai-dependency-graph-empty");
            _nodeCountLabel = CreateOverlayLabel(string.Empty, "ai-dependency-graph-counter");
            _zoomLabel = CreateOverlayLabel(string.Empty, "ai-dependency-graph-zoom");
            Add(_emptyLabel);
            Add(_nodeCountLabel);
            Add(_zoomLabel);

            generateVisualContent += GenerateGraphVisualContent;
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
            RegisterCallback<KeyDownEvent>(OnKeyDown);

            schedule.Execute(UpdateLayout).Every(16);
        }

        public void SetGraph(DependencyGraphData graphData, ForceDirectedLayout layout)
        {
            _graphData = graphData;
            _layout = layout;
            RefreshGraph();
        }

        public void RefreshGraph()
        {
            UpdateOverlayGeometry();
            MarkDirtyRepaint();
        }

        public void RequestFrameAll()
        {
            _frameWhenReady = true;
            TryFrameWhenReady();
        }

        // Kept for source compatibility with the previous immediate renderer surface.
        public void Render(Rect viewRect, DependencyGraphData graphData, ForceDirectedLayout layout)
        {
            SetGraph(graphData, layout);
            if (viewRect.width > 1f && viewRect.height > 1f)
            {
                FrameAll(viewRect, graphData);
            }
        }

        public void FrameAll(Rect viewRect, DependencyGraphData graphData)
        {
            if (graphData == null || graphData.Nodes.Count == 0) return;
            SetGraph(graphData, _layout);
            ApplyFrameAll(viewRect.size);
        }

        public void FocusOnNode(DependencyGraphNode node)
        {
            if (node == null) return;
            _panOffset = -node.Position;
            RefreshGraph();
        }

        public void ResetView(DependencyGraphData graphData = null)
        {
            if (graphData != null) _graphData = graphData;
            _zoom = 1f;
            if (_graphData != null && _graphData.Nodes.Count > 0)
            {
                Rect bounds = CalculateVisibleBounds(_graphData);
                _panOffset = -bounds.center;
            }
            else
            {
                _panOffset = Vector2.zero;
            }
            RefreshGraph();
        }

        public void SetShowLabels(bool show)
        {
            _showLabels = show;
            RefreshGraph();
        }

        public void SetShowIcons(bool show)
        {
            _showIcons = show;
            RefreshGraph();
        }

        public void SetSearchText(string searchText)
        {
            string normalized = searchText?.Trim() ?? string.Empty;
            if (string.Equals(_searchText, normalized, StringComparison.OrdinalIgnoreCase)) return;
            _searchText = normalized;
            RefreshGraph();
        }

        public bool FocusNextMatch()
        {
            if (_graphData == null || string.IsNullOrEmpty(_searchText)) return false;
            List<DependencyGraphNode> matches = _graphData.Nodes.Where(node => node.IsVisible && IsSearchMatch(node)).ToList();
            if (matches.Count == 0) return false;

            int current = matches.IndexOf(_selectedNode);
            DependencyGraphNode next = matches[(current + 1) % matches.Count];
            SelectNode(next, true);
            return true;
        }

        public void SelectNode(DependencyGraphNode node, bool focusNode)
        {
            if (_selectedNode == node && !focusNode) return;
            _selectedNode = node;
            _selectedPackage = node?.PackageNode;
            if (focusNode && node != null) _panOffset = -node.Position;
            OnNodeSelected?.Invoke(node);
            RefreshGraph();
        }

        private static Label CreateOverlayLabel(string text, string className)
        {
            Label label = new Label(text);
            label.pickingMode = PickingMode.Ignore;
            label.style.position = Position.Absolute;
            label.AddToClassList(className);
            return label;
        }

        private static VisualElement CreateOverlayLayer(string name)
        {
            VisualElement layer = new VisualElement
            {
                name = name,
                pickingMode = PickingMode.Ignore
            };
            layer.style.position = Position.Absolute;
            layer.style.left = 0f;
            layer.style.right = 0f;
            layer.style.top = 0f;
            layer.style.bottom = 0f;
            return layer;
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            TryFrameWhenReady();
            RefreshGraph();
        }

        private void TryFrameWhenReady()
        {
            Rect rect = contentRect;
            if (!_frameWhenReady || rect.width <= 1f || rect.height <= 1f || _graphData == null) return;
            _frameWhenReady = false;
            ApplyFrameAll(rect.size);
        }

        private void ApplyFrameAll(Vector2 viewportSize)
        {
            if (_graphData == null || _graphData.Nodes.Count == 0 || viewportSize.x <= 1f || viewportSize.y <= 1f) return;

            Rect bounds = CalculateVisibleBounds(_graphData);
            float xZoom = viewportSize.x / Mathf.Max(1f, bounds.width + 100f);
            float yZoom = viewportSize.y / Mathf.Max(1f, bounds.height + 100f);
            _zoom = Mathf.Clamp(Mathf.Min(xZoom, yZoom), MinZoom, MaxZoom);
            if (!IsFinite(_zoom)) _zoom = 1f;
            _panOffset = -bounds.center;
            RefreshGraph();
        }

        private void UpdateLayout()
        {
            if (_graphData == null || _layout == null || _layout.IsStable) return;
            _layout.Update(_graphData, 0.016f);
            RefreshGraph();
        }

        private void GenerateGraphVisualContent(MeshGenerationContext context)
        {
            Rect viewport = contentRect;
            if (viewport.width <= 1f || viewport.height <= 1f) return;

            _meshBuilder.Clear();
            _meshBuilder.AddRect(viewport, BackgroundColor);
            DrawGrid(viewport);

            if (_graphData != null && _graphData.Nodes.Count > 0)
            {
                DrawPackages(viewport);
                _meshBuilder.Flush(context);
                _meshBuilder.Clear();
                DrawEdges(viewport);
                _meshBuilder.Flush(context);
                _meshBuilder.Clear();
                DrawNodes(viewport);
            }

            _meshBuilder.Flush(context);
        }

        private void DrawGrid(Rect viewport)
        {
            float spacing = 100f * _zoom;
            if (spacing < 20f) return;

            Vector2 origin = GetGraphOrigin(viewport);
            float startX = Mathf.Repeat(origin.x, spacing);
            float startY = Mathf.Repeat(origin.y, spacing);
            for (float x = startX; x < viewport.width; x += spacing)
            {
                _meshBuilder.AddRect(new Rect(x, 0f, 1f, viewport.height), GridColor);
            }
            for (float y = startY; y < viewport.height; y += spacing)
            {
                _meshBuilder.AddRect(new Rect(0f, y, viewport.width, 1f), GridColor);
            }
        }

        private void DrawPackages(Rect viewport)
        {
            foreach (PackageNode package in _graphData.Packages)
            {
                if (!package.IsVisible) continue;
                Rect bounds = GraphToLocal(package.CalculateBounds(), viewport);
                if (!IsFinite(bounds)) continue;
                if (!IntersectsViewport(bounds, viewport, 100f)) continue;

                Color background = package.Color;
                _meshBuilder.AddRect(bounds, background);
                Color border = package == _selectedPackage
                    ? SelectionColor
                    : package == _hoveredPackage
                        ? HoverColor
                        : new Color(Mathf.Min(1f, background.r * 1.5f), Mathf.Min(1f, background.g * 1.5f), Mathf.Min(1f, background.b * 1.5f), 1f);
                _meshBuilder.AddRectOutline(bounds, (package == _selectedPackage ? 3f : 2f) * _zoom, border);

                Rect header = new Rect(bounds.x, bounds.y, bounds.width, package.HeaderHeight * _zoom);
                _meshBuilder.AddRect(header, new Color(background.r * 0.7f, background.g * 0.7f, background.b * 0.7f, 0.92f));

                if (!package.IsExpanded)
                {
                    DependencyGraphNode root = package.Files.FirstOrDefault(file => file.IsRoot);
                    if (root != null)
                    {
                        float bodyHeight = bounds.height - header.height;
                        float radius = Mathf.Min(16f * _zoom, bodyHeight * 0.3f);
                        Vector2 center = new Vector2(bounds.center.x, header.yMax + bodyHeight * 0.5f);
                        _meshBuilder.AddCircle(center, radius, new Color(0.3f, 0.6f, 1f, 0.9f));
                    }
                }
            }
        }

        private void DrawEdges(Rect viewport)
        {
            Vector2 origin = GetGraphOrigin(viewport);
            foreach (DependencyGraphEdge edge in _graphData.Edges)
            {
                if (!edge.ShouldRender()) continue;
                if (!IsFinite(edge.Source.Position) || !IsFinite(edge.Target.Position)) continue;

                Vector2 start = origin + edge.Source.Position * _zoom;
                Vector2 end = origin + edge.Target.Position * _zoom;
                Vector2 edgeDirection = end - start;
                if (edgeDirection.sqrMagnitude > 0.001f)
                {
                    Vector2 normalized = edgeDirection.normalized;
                    start += normalized * edge.Source.Size * _zoom * 0.5f;
                    end -= normalized * edge.Target.Size * _zoom * 0.5f;
                }
                if (!LineIntersectsRect(start, end, Expand(viewport, 100f))) continue;

                bool internalEdge = edge.Source.PackageNode == edge.Target.PackageNode && edge.Source.PackageNode != null;
                bool crossPackage = edge.Source.PackageNode != edge.Target.PackageNode && edge.Source.PackageNode != null && edge.Target.PackageNode != null;
                Color color;
                float width;
                if (edge.IsPartOfCycle)
                {
                    color = new Color(1f, 0.36f, 0.36f, 0.9f);
                    width = 2.1f;
                }
                else if (crossPackage)
                {
                    color = new Color(0.58f, 0.68f, 0.82f, 0.7f);
                    width = 1.4f;
                }
                else if (internalEdge)
                {
                    color = new Color(0.6f, 0.7f, 0.75f, 0.72f);
                    width = 1.1f;
                }
                else
                {
                    color = new Color(0.7f, 0.7f, 0.8f, 0.8f);
                    width = EdgeWidth;
                }

                if (edge.Source == _selectedNode || edge.Target == _selectedNode)
                {
                    color = new Color(0.3f, 0.66f, 0.94f, 0.92f);
                    width = 2f;
                }
                else if (!string.IsNullOrEmpty(_searchText) && !IsSearchMatch(edge.Source) && !IsSearchMatch(edge.Target))
                {
                    color.a *= 0.28f;
                }

                if (crossPackage || edge.IsCrossDependency)
                {
                    Vector2 midpoint = (start + end) * 0.5f;
                    Vector2 delta = end - start;
                    Vector2 perpendicular = delta.sqrMagnitude > 0.001f ? new Vector2(-delta.y, delta.x).normalized : Vector2.up;
                    _meshBuilder.AddBezier(start, midpoint + perpendicular * 42f, end, width, color, 18);
                }
                else
                {
                    _meshBuilder.AddLine(start, end, width, color);
                }

                if (_showArrows && _zoom > 0.3f)
                {
                    AddArrow(end, end - start, crossPackage ? ArrowSize * 1.25f : ArrowSize, color);
                }
            }
        }

        private void AddArrow(Vector2 position, Vector2 direction, float size, Color color)
        {
            if (direction.sqrMagnitude <= 0.001f) return;
            Vector2 normalized = direction.normalized;
            Vector2 right = new Vector2(-normalized.y, normalized.x);
            Vector2 first = position - normalized * size + right * size * 0.5f;
            Vector2 second = position - normalized * size - right * size * 0.5f;
            _meshBuilder.AddTriangle(position, first, second, color);
        }

        private void DrawNodes(Rect viewport)
        {
            foreach (DependencyGraphNode node in _graphData.Nodes)
            {
                if (!node.IsVisible) continue;
                if (!IsFinite(node.Position)) continue;
                Rect bounds = GetNodeLocalBounds(node, viewport);
                if (!IntersectsViewport(bounds, viewport, 100f)) continue;

                float outline = Mathf.Max(1f, 2f * _zoom);
                if (node.IsPartOfCycle)
                {
                    _meshBuilder.AddRectOutline(Expand(bounds, 3f * _zoom), 3f * _zoom, new Color(1f, 0.2f, 0.2f, 1f));
                }
                if (node == _hoveredNode)
                {
                    _meshBuilder.AddRectOutline(Expand(bounds, 2f * _zoom), 2f * _zoom, HoverColor);
                }
                if (node == _selectedNode)
                {
                    _meshBuilder.AddRectOutline(Expand(bounds, 4f), 3f, SelectionColor);
                }
                else if (IsSearchMatch(node))
                {
                    _meshBuilder.AddRectOutline(Expand(bounds, 3f), 2f, new Color(0.25f, 0.65f, 1f, 0.95f));
                }

                _meshBuilder.AddRect(bounds, node.Color);
                Color border = UnityEditor.EditorGUIUtility.isProSkin ? Color.black : Color.gray;
                _meshBuilder.AddRectOutline(bounds, outline, border);

                if (node.IsRoot)
                {
                    float indicatorSize = 6f;
                    _meshBuilder.AddRect(new Rect(bounds.center.x - indicatorSize * 0.5f, bounds.yMin - indicatorSize - 3f, indicatorSize, indicatorSize), Color.yellow);
                }
            }
        }

        private void HideAllOverlays()
        {
            foreach (NodeOverlay overlay in _nodeOverlayPool) overlay.style.display = DisplayStyle.None;
            foreach (PackageOverlay overlay in _packageOverlayPool) overlay.style.display = DisplayStyle.None;
            _activeNodeOverlayCount = 0;
            _activePackageOverlayCount = 0;
            _emptyLabel.style.display = DisplayStyle.Flex;
            _nodeCountLabel.style.display = DisplayStyle.None;
            _zoomLabel.style.display = DisplayStyle.None;
        }

        private void UpdateOverlayGeometry()
        {
            Rect viewport = contentRect;
            bool hasGraph = _graphData != null && _graphData.Nodes.Count > 0 && viewport.width > 1f && viewport.height > 1f;
            _emptyLabel.style.display = hasGraph ? DisplayStyle.None : DisplayStyle.Flex;
            _nodeCountLabel.style.display = hasGraph ? DisplayStyle.Flex : DisplayStyle.None;
            _zoomLabel.style.display = hasGraph ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasGraph)
            {
                HideAllOverlays();
                return;
            }

            int packageOverlayIndex = 0;
            foreach (PackageNode package in _graphData.Packages)
            {
                Rect bounds = GraphToLocal(package.CalculateBounds(), viewport);
                bool visible = package.IsVisible && IsFinite(bounds) && IntersectsViewport(bounds, viewport, 100f);
                if (!visible) continue;
                GetPackageOverlay(packageOverlayIndex++).Bind(package, bounds, _zoom, true);
            }
            HideUnusedPackageOverlays(packageOverlayIndex);
            _activePackageOverlayCount = packageOverlayIndex;

            int nodeOverlayIndex = 0;
            foreach (DependencyGraphNode node in _graphData.Nodes)
            {
                Rect bounds = GetNodeLocalBounds(node, viewport);
                bool visible = node.IsVisible && IsFinite(node.Position) && IsFinite(bounds) && IntersectsViewport(bounds, viewport, 100f);
                if (!visible) continue;
                bool selected = node == _selectedNode;
                bool searchMatch = IsSearchMatch(node);
                GetNodeOverlay(nodeOverlayIndex++).Bind(
                    node,
                    bounds,
                    _zoom,
                    visible,
                    _showLabels && (_zoom > LabelLodThreshold || selected || searchMatch),
                    _showIcons && _zoom > IconLodThreshold,
                    selected,
                    searchMatch);
            }
            HideUnusedNodeOverlays(nodeOverlayIndex);
            _activeNodeOverlayCount = nodeOverlayIndex;

            int visibleCount = _graphData.Nodes.Count(node => node.IsVisible);
            int matchCount = string.IsNullOrEmpty(_searchText) ? 0 : _graphData.Nodes.Count(node => node.IsVisible && IsSearchMatch(node));
            _nodeCountLabel.text = matchCount > 0
                ? $"Nodes: {visibleCount:N0}/{_graphData.Nodes.Count:N0}  |  Matches: {matchCount:N0}"
                : $"Nodes: {visibleCount:N0}/{_graphData.Nodes.Count:N0}";
            _zoomLabel.text = $"Zoom: {_zoom * 100f:F0}%";
        }

        private NodeOverlay GetNodeOverlay(int index)
        {
            while (_nodeOverlayPool.Count <= index)
            {
                NodeOverlay overlay = new NodeOverlay();
                _nodeOverlayPool.Add(overlay);
                _nodeOverlayLayer.Add(overlay);
            }
            return _nodeOverlayPool[index];
        }

        private PackageOverlay GetPackageOverlay(int index)
        {
            while (_packageOverlayPool.Count <= index)
            {
                PackageOverlay overlay = new PackageOverlay();
                _packageOverlayPool.Add(overlay);
                _packageOverlayLayer.Add(overlay);
            }
            return _packageOverlayPool[index];
        }

        private void HideUnusedNodeOverlays(int usedCount)
        {
            for (int i = usedCount; i < _nodeOverlayPool.Count; i++) _nodeOverlayPool[i].style.display = DisplayStyle.None;
        }

        private void HideUnusedPackageOverlays(int usedCount)
        {
            for (int i = usedCount; i < _packageOverlayPool.Count; i++) _packageOverlayPool[i].style.display = DisplayStyle.None;
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            Vector2 localPosition = evt.localPosition;
            if (_graphData == null || !contentRect.Contains(localPosition)) return;
            Focus();
            UpdateHoveredElements(localPosition);

            if (evt.button == 1)
            {
                if (_hoveredPackage != null) OnPackageRightClicked?.Invoke(_hoveredPackage);
                else if (_hoveredNode != null) OnNodeRightClicked?.Invoke(_hoveredNode);
                evt.StopPropagation();
                return;
            }
            if (evt.button != 0) return;

            PackageNode package = GetPackageAt(localPosition);
            if (package != null && IsPackageHeaderAt(package, localPosition))
            {
                _selectedNode = null;
                OnNodeSelected?.Invoke(null);
                _selectedPackage = package;
                if (evt.clickCount >= 2) OnPackageDoubleClicked?.Invoke(package);
                else OnPackageClicked?.Invoke(package);
                RefreshGraph();
                evt.StopPropagation();
                return;
            }

            DependencyGraphNode node = GetNodeAt(localPosition);
            if (node != null)
            {
                SelectNode(node, false);
                if (evt.clickCount >= 2) OnNodeDoubleClicked?.Invoke(node);
                evt.StopPropagation();
                return;
            }

            _isDragging = true;
            _dragPointerId = evt.pointerId;
            _lastPointerPosition = localPosition;
            PointerCaptureHelper.CapturePointer(this, evt.pointerId);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            Vector2 localPosition = evt.localPosition;
            if (_isDragging && evt.pointerId == _dragPointerId)
            {
                Vector2 delta = localPosition - _lastPointerPosition;
                _panOffset += delta / Mathf.Max(_zoom, 0.001f);
                _lastPointerPosition = localPosition;
                RefreshGraph();
                evt.StopPropagation();
                return;
            }

            UpdateHoveredElements(localPosition);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!_isDragging || evt.pointerId != _dragPointerId) return;
            _isDragging = false;
            if (PointerCaptureHelper.HasPointerCapture(this, evt.pointerId)) PointerCaptureHelper.ReleasePointer(this, evt.pointerId);
            _dragPointerId = -1;
            evt.StopPropagation();
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            _isDragging = false;
            _dragPointerId = -1;
        }

        private void OnWheel(WheelEvent evt)
        {
            Vector2 localPosition = evt.mousePosition;
            if (_graphData == null || !contentRect.Contains(localPosition)) return;
            float oldZoom = _zoom;
            float newZoom = Mathf.Clamp(oldZoom - evt.delta.y * 0.05f, MinZoom, MaxZoom);
            if (Mathf.Approximately(oldZoom, newZoom)) return;

            Vector2 center = contentRect.center;
            Vector2 graphPoint = (localPosition - center) / oldZoom - _panOffset;
            _zoom = newZoom;
            _panOffset = (localPosition - center) / newZoom - graphPoint;
            _hoveredNode = null;
            _hoveredPackage = null;
            RefreshGraph();
            evt.StopPropagation();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.F) return;
            RequestFrameAll();
            evt.StopPropagation();
        }

        private void UpdateHoveredElements(Vector2 localPosition)
        {
            PackageNode package = GetPackageAt(localPosition);
            DependencyGraphNode node = package != null && IsPackageHeaderAt(package, localPosition) ? null : GetNodeAt(localPosition);
            PackageNode packageHover = node == null ? package : null;
            if (_hoveredNode == node && _hoveredPackage == packageHover) return;
            _hoveredNode = node;
            _hoveredPackage = packageHover;
            RefreshGraph();
        }

        private DependencyGraphNode GetNodeAt(Vector2 localPosition)
        {
            if (_graphData == null) return null;
            Vector2 graphPosition = LocalToGraph(localPosition, contentRect);
            for (int i = _graphData.Nodes.Count - 1; i >= 0; i--)
            {
                DependencyGraphNode node = _graphData.Nodes[i];
                if (!node.IsVisible) continue;
                float halfSize = node.Size * 0.5f;
                Rect bounds = new Rect(node.Position.x - halfSize, node.Position.y - halfSize, node.Size, node.Size);
                if (bounds.Contains(graphPosition)) return node;
            }
            return null;
        }

        private PackageNode GetPackageAt(Vector2 localPosition)
        {
            if (_graphData == null) return null;
            Vector2 graphPosition = LocalToGraph(localPosition, contentRect);
            for (int i = _graphData.Packages.Count - 1; i >= 0; i--)
            {
                PackageNode package = _graphData.Packages[i];
                if (package.IsVisible && package.CalculateBounds().Contains(graphPosition)) return package;
            }
            return null;
        }

        private bool IsPackageHeaderAt(PackageNode package, Vector2 localPosition)
        {
            Vector2 graphPosition = LocalToGraph(localPosition, contentRect);
            Rect bounds = package.CalculateBounds();
            Rect header = new Rect(bounds.x, bounds.y, bounds.width, package.HeaderHeight);
            return header.Contains(graphPosition);
        }

        private bool IsSearchMatch(DependencyGraphNode node)
        {
            if (node == null || string.IsNullOrEmpty(_searchText)) return false;
            AssetFile file = node.AssetFile;
            return ContainsIgnoreCase(node.GetDisplayName(), _searchText)
                || ContainsIgnoreCase(file?.Path, _searchText)
                || ContainsIgnoreCase(file?.Type, _searchText)
                || ContainsIgnoreCase(node.PackageNode?.Name, _searchText);
        }

        private static bool ContainsIgnoreCase(string value, string searchText)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private Vector2 GetGraphOrigin(Rect viewport)
        {
            return viewport.center + _panOffset * _zoom;
        }

        private Vector2 LocalToGraph(Vector2 localPosition, Rect viewport)
        {
            return (localPosition - GetGraphOrigin(viewport)) / Mathf.Max(_zoom, 0.001f);
        }

        private Rect GraphToLocal(Rect graphRect, Rect viewport)
        {
            Vector2 origin = GetGraphOrigin(viewport);
            return new Rect(origin + graphRect.position * _zoom, graphRect.size * _zoom);
        }

        private Rect GetNodeLocalBounds(DependencyGraphNode node, Rect viewport)
        {
            Vector2 center = GetGraphOrigin(viewport) + node.Position * _zoom;
            float size = node.Size * _zoom;
            return new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);
        }

        private static bool IntersectsViewport(Rect bounds, Rect viewport, float margin)
        {
            return bounds.Overlaps(Expand(viewport, margin));
        }

        private static Rect Expand(Rect rect, float amount)
        {
            return new Rect(rect.x - amount, rect.y - amount, rect.width + amount * 2f, rect.height + amount * 2f);
        }

        private static bool LineIntersectsRect(Vector2 first, Vector2 second, Rect rect)
        {
            if (rect.Contains(first) || rect.Contains(second)) return true;
            float minX = Mathf.Min(first.x, second.x);
            float maxX = Mathf.Max(first.x, second.x);
            float minY = Mathf.Min(first.y, second.y);
            float maxY = Mathf.Max(first.y, second.y);
            return !(maxX < rect.xMin || minX > rect.xMax || maxY < rect.yMin || minY > rect.yMax);
        }

        private static Rect CalculateVisibleBounds(DependencyGraphData graphData)
        {
            bool found = false;
            float minX = 0f;
            float minY = 0f;
            float maxX = 0f;
            float maxY = 0f;
            foreach (DependencyGraphNode node in graphData.Nodes)
            {
                if (!node.IsVisible || !IsFinite(node.Position)) continue;
                float size = IsFinite(node.Size) ? Mathf.Max(1f, node.Size) : 40f;
                if (!found)
                {
                    minX = node.Position.x - size;
                    minY = node.Position.y - size;
                    maxX = node.Position.x + size;
                    maxY = node.Position.y + size;
                    found = true;
                    continue;
                }

                minX = Mathf.Min(minX, node.Position.x - size);
                minY = Mathf.Min(minY, node.Position.y - size);
                maxX = Mathf.Max(maxX, node.Position.x + size);
                maxY = Mathf.Max(maxY, node.Position.y + size);
            }

            foreach (PackageNode package in graphData.Packages)
            {
                if (!package.IsVisible) continue;
                Rect packageBounds = package.CalculateBounds();
                if (!IsFinite(packageBounds)) continue;
                if (!found)
                {
                    minX = packageBounds.xMin;
                    minY = packageBounds.yMin;
                    maxX = packageBounds.xMax;
                    maxY = packageBounds.yMax;
                    found = true;
                    continue;
                }
                minX = Mathf.Min(minX, packageBounds.xMin);
                minY = Mathf.Min(minY, packageBounds.yMin);
                maxX = Mathf.Max(maxX, packageBounds.xMax);
                maxY = Mathf.Max(maxY, packageBounds.yMax);
            }

            return found ? new Rect(minX, minY, Mathf.Max(1f, maxX - minX), Mathf.Max(1f, maxY - minY)) : new Rect(-50f, -50f, 100f, 100f);
        }

        private static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }

        private static bool IsFinite(Rect value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.width) && IsFinite(value.height);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private sealed class NodeOverlay : VisualElement
        {
            private readonly Image _icon;
            private readonly Label _label;
            private readonly Label _badge;

            public NodeOverlay()
            {
                pickingMode = PickingMode.Ignore;
                style.position = Position.Absolute;
                AddToClassList("ai-dependency-graph-node-overlay");

                _icon = new Image {pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleToFit};
                _icon.AddToClassList("ai-dependency-graph-node-icon");
                Add(_icon);

                _label = new Label {pickingMode = PickingMode.Ignore};
                _label.AddToClassList("ai-dependency-graph-node-label");
                Add(_label);

                _badge = new Label {pickingMode = PickingMode.Ignore};
                _badge.AddToClassList("ai-dependency-graph-badge");
                Add(_badge);
            }

            public void Bind(DependencyGraphNode node, Rect bounds, float zoom, bool visible, bool showLabel, bool showIcon, bool selected, bool searchMatch)
            {
                style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible) return;

                style.left = bounds.x;
                style.top = bounds.y;
                style.width = bounds.width;
                style.height = bounds.height;

                _icon.image = node.Icon;
                _icon.style.display = showIcon && node.Icon != null ? DisplayStyle.Flex : DisplayStyle.None;
                float iconSize = bounds.width * 0.5f;
                _icon.style.left = (bounds.width - iconSize) * 0.5f;
                _icon.style.top = (bounds.height - iconSize) * 0.5f;
                _icon.style.width = iconSize;
                _icon.style.height = iconSize;

                _label.text = node.GetDisplayName();
                _label.tooltip = _label.text;
                _label.style.display = showLabel ? DisplayStyle.Flex : DisplayStyle.None;
                _label.style.fontSize = Mathf.Max(8f, 8f * zoom);
                _label.style.top = bounds.height + 4f * zoom;
                float labelWidth = Mathf.Clamp(_label.text.Length * 5.2f, 56f, 126f);
                _label.style.left = (bounds.width - labelWidth) * 0.5f;
                _label.style.width = labelWidth;
                _label.EnableInClassList("ai-dependency-graph-node-label-selected", selected);
                _label.EnableInClassList("ai-dependency-graph-node-label-match", searchMatch && !selected);

                _badge.text = node.HiddenDependencyCount.ToString();
                _badge.style.display = node.HasHiddenDependencies ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private sealed class PackageOverlay : VisualElement
        {
            private readonly Label _indicator;
            private readonly Label _name;
            private readonly Label _count;
            private readonly Image _rootIcon;

            public PackageOverlay()
            {
                pickingMode = PickingMode.Ignore;
                style.position = Position.Absolute;
                AddToClassList("ai-dependency-graph-package-overlay");

                _name = new Label {pickingMode = PickingMode.Ignore};
                _name.AddToClassList("ai-dependency-graph-package-name");
                Add(_name);

                _indicator = new Label {pickingMode = PickingMode.Ignore};
                _indicator.AddToClassList("ai-dependency-graph-package-indicator");
                Add(_indicator);

                _count = new Label {pickingMode = PickingMode.Ignore};
                _count.AddToClassList("ai-dependency-graph-package-count");
                Add(_count);

                _rootIcon = new Image {pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleToFit};
                _rootIcon.AddToClassList("ai-dependency-graph-package-root-icon");
                Add(_rootIcon);
            }

            public void Bind(PackageNode package, Rect bounds, float zoom, bool visible)
            {
                style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible) return;

                float headerHeight = package.HeaderHeight * zoom;
                style.left = bounds.x;
                style.top = bounds.y;
                style.width = bounds.width;
                style.height = bounds.height;

                _name.text = package.Name;
                _name.style.left = 6f * zoom;
                _name.style.right = 48f * zoom;
                _name.style.height = headerHeight;
                _name.style.fontSize = Mathf.Max(8f, 10f * zoom);

                _indicator.text = package.IsExpanded ? "▼" : "▶";
                _indicator.style.right = 27f * zoom;
                _indicator.style.width = 16f * zoom;
                _indicator.style.height = headerHeight;

                _count.text = package.Files.Count.ToString();
                _count.style.right = 5f * zoom;
                _count.style.top = 5f * zoom;
                _count.style.width = 20f * zoom;
                _count.style.height = 15f * zoom;
                _count.style.fontSize = Mathf.Max(7f, 9f * zoom);

                DependencyGraphNode root = package.Files.FirstOrDefault(file => file.IsRoot);
                bool showRoot = !package.IsExpanded && root != null && root.Icon != null;
                _rootIcon.style.display = showRoot ? DisplayStyle.Flex : DisplayStyle.None;
                if (showRoot)
                {
                    _rootIcon.image = root.Icon;
                    float bodyHeight = bounds.height - headerHeight;
                    float size = Mathf.Min(32f * zoom, bodyHeight * 0.6f);
                    _rootIcon.style.left = (bounds.width - size) * 0.5f;
                    _rootIcon.style.top = headerHeight + (bodyHeight - size) * 0.5f;
                    _rootIcon.style.width = size;
                    _rootIcon.style.height = size;
                }
            }
        }
    }
}
