using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AssetInventory
{
    /// <summary>
    /// Deterministic left-to-right dependency layout with one stable horizontal band per package.
    /// It favors edge readability and predictable node positions over physics simulation.
    /// </summary>
    public sealed class PackageFlowLayout
    {
        private const float DepthSpacing = 220f;
        private const float NodeSpacing = 88f;
        private const float PackageSpacing = 100f;
        private const float PackagePadding = 52f;
        private const float LabelWidth = 150f;
        private const float LabelHeight = 22f;

        public void Apply(DependencyGraphData graphData)
        {
            if (graphData == null || graphData.Nodes.Count == 0) return;

            List<PackageNode> packages = graphData.Packages
                .Where(package => package.IsVisible && package.Files.Any(file => file.IsVisible))
                .OrderBy(package => package.Files.Any(file => file.IsRoot) ? 0 : 1)
                .ThenBy(package => package.Files.Where(file => file.IsVisible).Min(file => file.Depth))
                .ThenBy(package => package.Name)
                .ThenBy(package => package.AssetId)
                .ToList();

            float bandTop = 0f;
            foreach (PackageNode package in packages)
            {
                List<DependencyGraphNode> files = package.Files.Where(file => file.IsVisible).ToList();
                if (files.Count == 0) continue;

                Dictionary<int, List<DependencyGraphNode>> byDepth = files
                    .GroupBy(file => file.Depth)
                    .ToDictionary(
                        group => group.Key,
                        group => group.OrderBy(file => file.GetDisplayName()).ThenBy(file => file.AssetFile.Id).ToList());
                int maxRows = byDepth.Values.Max(nodes => nodes.Count);
                float contentHeight = Mathf.Max(120f, (maxRows - 1) * NodeSpacing + 64f);
                float bandCenter = bandTop + PackagePadding + package.HeaderHeight + contentHeight * 0.5f;

                foreach (KeyValuePair<int, List<DependencyGraphNode>> pair in byDepth)
                {
                    List<DependencyGraphNode> nodes = pair.Value;
                    float firstY = bandCenter - (nodes.Count - 1) * NodeSpacing * 0.5f;
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        DependencyGraphNode node = nodes[i];
                        node.Size = 54f;
                        node.Position = new Vector2(pair.Key * DepthSpacing, firstY + i * NodeSpacing);
                        node.Velocity = Vector2.zero;
                        node.Force = Vector2.zero;
                        node.FullBounds = new Rect(
                            node.Position.x - LabelWidth * 0.5f,
                            node.Position.y - node.Size * 0.5f,
                            LabelWidth,
                            node.Size + LabelHeight);
                    }
                }

                Vector2 centroid = Vector2.zero;
                foreach (DependencyGraphNode file in files) centroid += file.Position;
                package.Position = centroid / files.Count;
                package.Velocity = Vector2.zero;
                package.Force = Vector2.zero;
                package.Bounds = package.CalculateBounds();
                bandTop = package.Bounds.yMax + PackageSpacing;
            }

            CenterGraph(packages);
        }

        private static void CenterGraph(List<PackageNode> packages)
        {
            if (packages.Count == 0) return;

            Rect bounds = packages[0].CalculateBounds();
            for (int i = 1; i < packages.Count; i++) bounds = Encapsulate(bounds, packages[i].CalculateBounds());
            Vector2 offset = -bounds.center;
            foreach (PackageNode package in packages)
            {
                package.Position += offset;
                foreach (DependencyGraphNode file in package.Files)
                {
                    if (!file.IsVisible) continue;
                    file.Position += offset;
                    file.FullBounds = new Rect(file.FullBounds.position + offset, file.FullBounds.size);
                }
                package.Bounds = package.CalculateBounds();
            }
        }

        private static Rect Encapsulate(Rect first, Rect second)
        {
            float minX = Mathf.Min(first.xMin, second.xMin);
            float minY = Mathf.Min(first.yMin, second.yMin);
            float maxX = Mathf.Max(first.xMax, second.xMax);
            float maxY = Mathf.Max(first.yMax, second.yMax);
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }
    }
}
