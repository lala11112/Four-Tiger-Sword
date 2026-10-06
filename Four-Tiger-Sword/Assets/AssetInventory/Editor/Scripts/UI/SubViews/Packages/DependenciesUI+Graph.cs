using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public partial class DependenciesUI
    {
        // Graph visualization fields
        private enum ViewMode { List, Graph }
        private enum GraphLayoutMode { Flow, Radial, Organic }
        private ViewMode _viewMode = ViewMode.Graph;
        private GraphLayoutMode _graphLayoutMode = GraphLayoutMode.Flow;
        private int _serializedAssetInfoId = -1;

        private DependencyGraphData _graphData;
        private DependencyGraphRenderer _graphRenderer;
        private ForceDirectedLayout _forceLayout;
        private PackageRadialLayout _hierarchicalLayout;
        private PackageFlowLayout _flowLayout;
        private bool _useHierarchicalLayout = true;
        private bool _graphNeedsRebuild = true;
        private bool _needsInitialFrame = true;
        private bool _pendingFrameAll;
        private int _graphPreviewGeneration;

        private void InitializeGraph()
        {
            // Always reinitialize the graph objects after domain reload
            if (_graphData == null)
            {
                _graphData = new DependencyGraphData();
                _graphNeedsRebuild = true;
            }

            if (_graphRenderer == null)
            {
                _graphRenderer = new DependencyGraphRenderer();
                _graphRenderer.OnNodeSelected += OnGraphNodeSelected;
                _graphRenderer.OnNodeDoubleClicked += OnGraphNodeDoubleClicked;
                _graphRenderer.OnNodeRightClicked += OnGraphNodeRightClicked;
                _graphRenderer.OnPackageClicked += OnGraphPackageClicked;
                _graphRenderer.OnPackageDoubleClicked += OnGraphPackageDoubleClicked;
                _graphRenderer.OnPackageRightClicked += OnGraphPackageRightClicked;
            }

            if (_forceLayout == null)
            {
                _forceLayout = new ForceDirectedLayout();
                _graphNeedsRebuild = true;
            }

            if (_hierarchicalLayout == null)
            {
                _hierarchicalLayout = new PackageRadialLayout();
                _graphNeedsRebuild = true;
            }

            if (_flowLayout == null)
            {
                _flowLayout = new PackageFlowLayout();
                _graphNeedsRebuild = true;
            }

            // Rebuild graph if needed and we have valid info
            if (_graphNeedsRebuild && _info != null && _info.Id != 0)
            {
                _graphData.BuildFromAssetInfo(_info);

                // Make all files visible initially
                foreach (DependencyGraphNode node in _graphData.Nodes)
                {
                    node.IsVisible = true;
                }

                // Expand all packages
                foreach (PackageNode package in _graphData.Packages)
                {
                    package.IsExpanded = true;
                }

                _graphData.SetSimplifiedMode(false);

                if (_useHierarchicalLayout)
                {
                    // Use hierarchical radial layout
                    _hierarchicalLayout.AutoAdjustParameters(_graphData);
                    _hierarchicalLayout.InitializeHierarchicalPositions(_graphData);
                    _hierarchicalLayout.UpdatePackagePositions(_graphData);
                }
                else
                {
                    // Use force-directed layout (legacy)
                    _forceLayout.AutoAdjustParameters(_graphData.Nodes.Count);
                    _forceLayout.InitializePackagePositions(_graphData);

                    foreach (PackageNode package in _graphData.Packages)
                    {
                        ReinitializePackageFilePositions(package);
                    }

                    _forceLayout.RunIterations(_graphData, 50);
                    UpdatePackageBoundsAfterLayout(_graphData);
                }

                if (_graphLayoutMode == GraphLayoutMode.Flow) _flowLayout.Apply(_graphData);

                int previewGeneration = ++_graphPreviewGeneration;
                _ = LoadGraphPreviewsAsync(_graphData, previewGeneration);

                _graphNeedsRebuild = false;
                _needsInitialFrame = true; // Frame the view after first render
            }
        }

        private void OnGraphNodeDoubleClicked(DependencyGraphNode node)
        {
            ShowInInventory(node?.AssetFile);
        }

        private void ExpandGraphNode(DependencyGraphNode node)
        {
            if (node == null || !node.HasHiddenDependencies) return;

            _graphData.ExpandNode(node);

            if (_useHierarchicalLayout)
            {
                _hierarchicalLayout.InitializeHierarchicalPositions(_graphData);
                _hierarchicalLayout.UpdatePackagePositions(_graphData);
            }
            else
            {
                _forceLayout.RunIterations(_graphData, 30);
            }

            RefreshGraphView();
        }

        private void CollapseGraphNode(DependencyGraphNode node)
        {
            if (node == null || !node.IsExpanded || node.IsRoot) return;

            _graphData.CollapseNode(node);
            RefreshGraphView();
        }

        private void OnGraphNodeSelected(DependencyGraphNode node)
        {
            _selectedGraphNode = node;
            RebuildGraphInspector();
        }

        private async Task LoadGraphPreviewsAsync(DependencyGraphData graphData, int generation)
        {
            if (graphData == null) return;
            string previewFolder = Paths.GetPreviewFolder(createOnDemand: false);
            if (string.IsNullOrEmpty(previewFolder)) return;

            System.Collections.Generic.List<DependencyGraphNode> candidates = graphData.Nodes
                .Where(node => node.AssetFile != null && node.AssetFile.HasPreview(true))
                .OrderByDescending(node => node.IsRoot)
                .ThenBy(node => node.Depth)
                .Take(240)
                .ToList();

            int updated = 0;
            foreach (DependencyGraphNode node in candidates)
            {
                if (generation != _graphPreviewGeneration || graphData != _graphData) return;
                string previewFile = AssetImporter.ValidatePreviewFile(node.AssetFile, previewFolder);
                if (string.IsNullOrEmpty(previewFile)) continue;

                Texture2D texture = await AssetUtils.LoadLocalTexture(previewFile, true);
                if (generation != _graphPreviewGeneration || graphData != _graphData) return;
                if (texture == null) continue;

                node.Icon = texture;
                updated++;
                if (node == _selectedGraphNode) RebuildGraphInspector();
                if (updated % 6 == 0) _graphRenderer?.RefreshGraph();
            }

            if (updated > 0) _graphRenderer?.RefreshGraph();
        }

        private void OnGraphNodeRightClicked(DependencyGraphNode node)
        {
            if (node == null) return;

            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Show in Asset Inventory"), false, () => ShowInInventory(node.AssetFile));
            menu.AddSeparator("");

            if (node.HasHiddenDependencies)
            {
                menu.AddItem(new GUIContent("Expand Dependencies"), false, () => ExpandGraphNode(node));
            }

            if (node.IsExpanded && !node.IsRoot)
            {
                menu.AddItem(new GUIContent("Collapse Dependencies"), false, () => CollapseGraphNode(node));
            }

            if (!string.IsNullOrEmpty(node.AssetFile.ProjectPath))
            {
                menu.AddItem(new GUIContent("Reveal in Project"), false, () =>
                {
                    EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(node.AssetFile.ProjectPath));
                });
            }

            menu.AddItem(new GUIContent("Copy Path"), false, () =>
            {
                EditorGUIUtility.systemCopyBuffer = node.AssetFile.Path;
            });

            menu.ShowAsContext();
        }

        private void OnGraphPackageClicked(PackageNode package)
        {
            // Toggle expand/collapse on single click
            if (package != null)
            {
                package.ToggleExpanded();

                if (_graphData != null)
                {
                    if (package.IsExpanded)
                    {
                        if (_useHierarchicalLayout)
                        {
                            // Re-run hierarchical layout to incorporate newly visible nodes
                            _hierarchicalLayout.InitializeHierarchicalPositions(_graphData);
                            _hierarchicalLayout.UpdatePackagePositions(_graphData);
                        }
                        else
                        {
                            // Expanding: Reinitialize file positions within the package
                            ReinitializePackageFilePositions(package);
                            _forceLayout.RunIterations(_graphData, 50);

                            // Recenter package on its files and recalculate bounds
                            RecenterPackageOnFiles(package);

                            // Resolve any package overlaps
                            ResolvePackageOverlaps(_graphData);
                        }
                    }
                    else
                    {
                        // Collapsing: Just update bounds
                        package.Velocity = Vector2.zero;
                        package.Force = Vector2.zero;
                        package.Bounds = package.CalculateBounds();

                        // Reset forces on all packages and nodes
                        foreach (PackageNode pkg in _graphData.Packages)
                        {
                            pkg.Velocity = Vector2.zero;
                            pkg.Force = Vector2.zero;
                        }

                        foreach (DependencyGraphNode node in _graphData.Nodes)
                        {
                            node.Velocity = Vector2.zero;
                            node.Force = Vector2.zero;
                        }
                    }
                }

                RefreshGraphView();
            }
        }

        private void RecenterPackageOnFiles(PackageNode package)
        {
            if (!package.IsExpanded || package.Files.Count == 0) return;

            Vector2 centroid = Vector2.zero;
            int visibleCount = 0;

            foreach (DependencyGraphNode file in package.Files)
            {
                if (file.IsVisible)
                {
                    centroid += file.Position;
                    visibleCount++;
                }
            }

            if (visibleCount > 0)
            {
                package.Position = centroid / visibleCount;
            }

            package.Bounds = package.CalculateBounds();
        }

        private void ReinitializePackageFilePositions(PackageNode package)
        {
            // Arrange ALL files (including root if present) in a circle around package center
            float radius = 80f + package.Files.Count * 10f;
            float angleStep = 360f / Mathf.Max(1, package.Files.Count);

            for (int i = 0; i < package.Files.Count; i++)
            {
                DependencyGraphNode file = package.Files[i];
                float angle = i * angleStep * Mathf.Deg2Rad;

                // Position file in circle (overrides initial position like Vector2.zero for root)
                file.Position = package.Position + new Vector2(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius
                );
                file.Velocity = Vector2.zero;
                file.Force = Vector2.zero;
            }
        }

        private void UpdatePackageBoundsAfterLayout(DependencyGraphData graphData)
        {
            // Recenter all packages on their files and recalculate bounds
            foreach (PackageNode package in graphData.Packages)
            {
                if (!package.IsVisible) continue;
                RecenterPackageOnFiles(package);
            }

            // Resolve any package boundary overlaps
            ResolvePackageOverlaps(graphData);
        }

        private void ResolvePackageOverlaps(DependencyGraphData graphData)
        {
            // Final hard constraint pass - ensures absolutely no overlaps
            // This runs AFTER physics simulation as a cleanup
            const int maxIterations = 10; // Should be quick since physics already separated them
            const float minSeparation = 50f;

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                bool hadOverlap = false;

                for (int i = 0; i < graphData.Packages.Count; i++)
                {
                    PackageNode pkg1 = graphData.Packages[i];
                    if (!pkg1.IsVisible || !pkg1.IsExpanded) continue;

                    // Recalculate bounds
                    pkg1.Bounds = pkg1.CalculateBounds();

                    for (int j = i + 1; j < graphData.Packages.Count; j++)
                    {
                        PackageNode pkg2 = graphData.Packages[j];
                        if (!pkg2.IsVisible || !pkg2.IsExpanded) continue;

                        // Recalculate bounds
                        pkg2.Bounds = pkg2.CalculateBounds();

                        // Check if package bounds overlap
                        if (pkg1.Bounds.Overlaps(pkg2.Bounds))
                        {
                            hadOverlap = true;

                            // Calculate separation vector
                            Vector2 delta = pkg1.Position - pkg2.Position;
                            float distance = delta.magnitude;

                            if (distance < 0.1f)
                            {
                                delta = new Vector2(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f)).normalized;
                                distance = 0.1f;
                            }

                            Vector2 direction = delta.normalized;

                            // Calculate minimum required distance between centers
                            float halfWidth1 = pkg1.Bounds.width / 2f;
                            float halfWidth2 = pkg2.Bounds.width / 2f;
                            float halfHeight1 = pkg1.Bounds.height / 2f;
                            float halfHeight2 = pkg2.Bounds.height / 2f;

                            float requiredDistance = Mathf.Max(halfWidth1 + halfWidth2, halfHeight1 + halfHeight2) + minSeparation;
                            float overlap = requiredDistance - distance;

                            if (overlap > 0)
                            {
                                // Hard constraint: directly move packages and files
                                Vector2 correction = direction * (overlap / 2f);

                                pkg1.Position += correction;
                                MovePackageFiles(pkg1, correction);

                                pkg2.Position -= correction;
                                MovePackageFiles(pkg2, -correction);
                            }
                        }
                    }
                }

                // If no overlaps found, we're done
                if (!hadOverlap) break;
            }
        }

        private void MovePackageFiles(PackageNode package, Vector2 offset)
        {
            // Move all files in the package by the offset
            foreach (DependencyGraphNode file in package.Files)
            {
                if (file.IsVisible)
                {
                    file.Position += offset;
                }
            }
        }

        private void OnGraphPackageDoubleClicked(PackageNode package)
        {
            // Expand and focus on double-click
            if (package != null && !package.IsExpanded)
            {
                package.IsExpanded = true;

                // Make files visible
                foreach (DependencyGraphNode file in package.Files)
                {
                    file.IsVisible = true;
                }

                if (_graphData != null)
                {
                    if (_useHierarchicalLayout)
                    {
                        _hierarchicalLayout.InitializeHierarchicalPositions(_graphData);
                        _hierarchicalLayout.UpdatePackagePositions(_graphData);
                    }
                    else
                    {
                        ReinitializePackageFilePositions(package);
                        _forceLayout.RunIterations(_graphData, 50);
                        RecenterPackageOnFiles(package);
                        ResolvePackageOverlaps(_graphData);
                    }
                }

                _graphRenderer.FocusOnNode(package.Files.FirstOrDefault());
                RefreshGraphView();
            }
        }

        private void OnGraphPackageRightClicked(PackageNode package)
        {
            if (package == null) return;

            GenericMenu menu = new GenericMenu();

            menu.AddItem(new GUIContent(package.IsExpanded ? "Collapse Package" : "Expand Package"), false, () =>
            {
                package.ToggleExpanded();

                if (_graphData != null)
                {
                    if (_useHierarchicalLayout)
                    {
                        _hierarchicalLayout.InitializeHierarchicalPositions(_graphData);
                        _hierarchicalLayout.UpdatePackagePositions(_graphData);
                    }
                    else
                    {
                        ReinitializePackageFilePositions(package);
                        _forceLayout.RunIterations(_graphData, 50);
                        RecenterPackageOnFiles(package);
                        ResolvePackageOverlaps(_graphData);
                    }
                }

                RefreshGraphView();
            });

            menu.AddItem(new GUIContent("Expand All Files"), false, () =>
            {
                package.IsExpanded = true;
                foreach (DependencyGraphNode file in package.Files)
                {
                    file.IsVisible = true;
                    file.IsExpanded = true;
                }

                if (_graphData != null)
                {
                    if (_useHierarchicalLayout)
                    {
                        _hierarchicalLayout.InitializeHierarchicalPositions(_graphData);
                        _hierarchicalLayout.UpdatePackagePositions(_graphData);
                    }
                    else
                    {
                        ReinitializePackageFilePositions(package);
                        _forceLayout.RunIterations(_graphData, 50);
                        RecenterPackageOnFiles(package);
                        ResolvePackageOverlaps(_graphData);
                    }
                }

                RefreshGraphView();
            });

            if (package.AssetInfo != null && !string.IsNullOrEmpty(package.AssetInfo.Location))
            {
                menu.AddItem(new GUIContent("Open Package Location"), false, () =>
                {
                    EditorUtility.RevealInFinder(package.AssetInfo.Location);
                });
            }

            menu.ShowAsContext();
        }

        private void SetGraphLayoutMode(int layoutIndex)
        {
            GraphLayoutMode mode = (GraphLayoutMode)Mathf.Clamp(layoutIndex, 0, 2);
            if (_graphLayoutMode == mode) return;

            _graphLayoutMode = mode;
            _useHierarchicalLayout = mode != GraphLayoutMode.Organic;
            _needsInitialFrame = true;
            InitializeGraph();
            if (_graphData == null) return;
            switch (mode)
            {
                case GraphLayoutMode.Flow:
                    _flowLayout.Apply(_graphData);
                    break;
                case GraphLayoutMode.Radial:
                    _hierarchicalLayout.AutoAdjustParameters(_graphData);
                    _hierarchicalLayout.InitializeHierarchicalPositions(_graphData);
                    _hierarchicalLayout.UpdatePackagePositions(_graphData);
                    break;
                case GraphLayoutMode.Organic:
                    _forceLayout.AutoAdjustParameters(_graphData.Nodes.Count);
                    _forceLayout.InitializePackagePositions(_graphData);
                    foreach (PackageNode package in _graphData.Packages) ReinitializePackageFilePositions(package);
                    _forceLayout.RunIterations(_graphData, 60);
                    UpdatePackageBoundsAfterLayout(_graphData);
                    break;
            }
            _graphRenderer?.SetGraph(_graphData, mode == GraphLayoutMode.Organic ? _forceLayout : null);
            _graphRenderer?.RequestFrameAll();
            RefreshGraphView();
        }

        private void RequestGraphFrameAll()
        {
            _pendingFrameAll = true;
            _graphRenderer?.RequestFrameAll();
            _pendingFrameAll = false;
        }

        private void RefreshGraphView()
        {
            if (_graphLayoutMode == GraphLayoutMode.Flow && _graphData != null) _flowLayout?.Apply(_graphData);
            _graphRenderer?.RefreshGraph();
            Repaint();
        }
    }
}
