using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AssetInventory
{
    /// <summary>
    /// Deterministic package-orbit layout. The root package remains central while every other
    /// package receives a stable non-overlapping cluster around it.
    /// </summary>
    public sealed class PackageRadialLayout
    {
        private const int NodesPerRing = 8;
        private const float FirstNodeRingRadius = 120f;
        private const float NodeRingSpacing = 115f;
        private const float PackageGap = 120f;
        private const float LabelWidth = 140f;
        private const float LabelHeight = 22f;

        public void AutoAdjustParameters(DependencyGraphData graphData)
        {
            // Kept as an explicit phase to mirror the previous layout contract.
        }

        public void InitializeHierarchicalPositions(DependencyGraphData graphData)
        {
            if (graphData == null || graphData.RootNode == null) return;

            List<PackageNode> packages = graphData.Packages
                .Where(package => package.IsVisible && package.Files.Any(file => file.IsVisible))
                .OrderBy(package => package.Files.Any(file => file.IsRoot) ? 0 : 1)
                .ThenBy(package => package.Files.Where(file => file.IsVisible).Min(file => file.Depth))
                .ThenBy(package => package.Name)
                .ThenBy(package => package.AssetId)
                .ToList();
            if (packages.Count == 0) return;

            DependencyGraphNode root = graphData.RootNode;
            PackageNode rootPackage = root.PackageNode ?? packages[0];
            LayoutPackage(rootPackage, Vector2.zero, root);

            Rect rootBounds = rootPackage.CalculateBounds();
            float rootRadius = GetBoundingRadius(rootBounds);
            List<PackageNode> orbitPackages = packages.Where(package => package != rootPackage).ToList();
            if (orbitPackages.Count == 0) return;

            float largestClusterRadius = 0f;
            foreach (PackageNode package in orbitPackages)
            {
                LayoutPackage(package, Vector2.zero, null);
                float clusterRadius = GetBoundingRadius(package.CalculateBounds());
                largestClusterRadius = Mathf.Max(largestClusterRadius, clusterRadius);
            }

            float orbitRadius = rootRadius + largestClusterRadius + PackageGap;
            if (orbitPackages.Count > 1)
            {
                float halfAngle = Mathf.PI / orbitPackages.Count;
                float radiusForNeighbors = (largestClusterRadius * 2f + PackageGap) / (2f * Mathf.Sin(halfAngle));
                orbitRadius = Mathf.Max(orbitRadius, radiusForNeighbors);
            }

            float angleStep = Mathf.PI * 2f / orbitPackages.Count;
            // Start on the horizontal axis so the common two-package case uses the wider editor viewport.
            float angleOffset = 0f;
            for (int i = 0; i < orbitPackages.Count; i++)
            {
                PackageNode package = orbitPackages[i];
                float angle = angleOffset + angleStep * i;
                Vector2 center = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * orbitRadius;
                MovePackage(package, center);
            }
        }

        public void UpdatePackagePositions(DependencyGraphData graphData)
        {
            if (graphData == null) return;
            foreach (PackageNode package in graphData.Packages)
            {
                List<DependencyGraphNode> visible = package.Files.Where(file => file.IsVisible).ToList();
                package.IsVisible = visible.Count > 0;
                if (visible.Count == 0) continue;

                Vector2 centroid = Vector2.zero;
                foreach (DependencyGraphNode file in visible) centroid += file.Position;
                package.Position = centroid / visible.Count;
                package.Velocity = Vector2.zero;
                package.Force = Vector2.zero;
                package.Bounds = package.CalculateBounds();
            }
        }

        private static void LayoutPackage(PackageNode package, Vector2 center, DependencyGraphNode centerNode)
        {
            List<DependencyGraphNode> files = package.Files
                .Where(file => file.IsVisible && file != centerNode)
                .OrderBy(file => file.Depth)
                .ThenBy(file => file.GetDisplayName())
                .ThenBy(file => file.AssetFile.Id)
                .ToList();

            if (centerNode != null)
            {
                SetNodePosition(centerNode, center, 56f);
            }

            int offset = 0;
            int ring = 0;
            while (offset < files.Count)
            {
                int ringCount = Mathf.Min(NodesPerRing, files.Count - offset);
                float minimumLabelRadius = ringCount <= 1
                    ? 0f
                    : LabelWidth / (2f * Mathf.Sin(Mathf.PI / ringCount));
                float radius = Mathf.Max(
                    FirstNodeRingRadius + ring * NodeRingSpacing,
                    minimumLabelRadius + 20f);
                float angleStep = Mathf.PI * 2f / ringCount;
                float angleOffset = -Mathf.PI * 0.5f + (ring % 2 == 0 ? 0f : angleStep * 0.5f);

                for (int i = 0; i < ringCount; i++)
                {
                    float angle = angleOffset + i * angleStep;
                    Vector2 position = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    SetNodePosition(files[offset + i], position, 48f);
                }

                offset += ringCount;
                ring++;
            }

            package.Position = center;
            package.Velocity = Vector2.zero;
            package.Force = Vector2.zero;
            package.Bounds = package.CalculateBounds();
        }

        private static void MovePackage(PackageNode package, Vector2 targetCenter)
        {
            Rect bounds = package.CalculateBounds();
            Vector2 offset = targetCenter - bounds.center;
            foreach (DependencyGraphNode file in package.Files)
            {
                if (!file.IsVisible) continue;
                file.Position += offset;
                file.FullBounds = new Rect(file.FullBounds.position + offset, file.FullBounds.size);
            }

            package.Position = targetCenter;
            package.Bounds = package.CalculateBounds();
        }

        private static void SetNodePosition(DependencyGraphNode node, Vector2 position, float size)
        {
            node.Size = size;
            node.Position = position;
            node.Velocity = Vector2.zero;
            node.Force = Vector2.zero;
            node.FullBounds = CreateFullBounds(node);
        }

        private static float GetBoundingRadius(Rect bounds)
        {
            return bounds.size.magnitude * 0.5f;
        }

        private static Rect CreateFullBounds(DependencyGraphNode node)
        {
            return new Rect(
                node.Position.x - LabelWidth * 0.5f,
                node.Position.y - node.Size * 0.5f,
                LabelWidth,
                node.Size + LabelHeight);
        }
    }
}
