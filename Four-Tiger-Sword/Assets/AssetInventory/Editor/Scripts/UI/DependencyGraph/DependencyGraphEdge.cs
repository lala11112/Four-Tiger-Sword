using UnityEngine;

namespace AssetInventory
{
    /// <summary>Directed relationship between two dependency graph nodes.</summary>
    public class DependencyGraphEdge
    {
        public DependencyGraphNode Source { get; private set; }
        public DependencyGraphNode Target { get; private set; }

        public bool IsVisible { get; set; }
        public bool IsCrossDependency { get; set; }
        public bool IsPartOfCycle { get; set; }

        // Visual properties
        public Color Color { get; set; }
        public float Width { get; set; }

        public DependencyGraphEdge(DependencyGraphNode source, DependencyGraphNode target)
        {
            Source = source;
            Target = target;
            IsVisible = true;
            Color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            Width = 2f;
        }

        public bool ShouldRender()
        {
            return IsVisible && Source.IsVisible && Target.IsVisible;
        }

        public override string ToString()
        {
            return $"Edge: {Source.GetDisplayName()} -> {Target.GetDisplayName()}";
        }
    }
}
