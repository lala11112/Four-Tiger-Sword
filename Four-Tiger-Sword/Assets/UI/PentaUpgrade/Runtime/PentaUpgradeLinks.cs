using System;
using System.Collections.Generic;
using UnityEngine;

namespace Penta.Upgrade
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PentaUpgradeLinks : UnityEngine.UI.MaskableGraphic
    {
        [Serializable]
        public sealed class Link
        {
            public string ParentId;
            public string ChildId;
            public Vector2 Start;
            public Vector2 End;
            public Vector2[] Points;
        }

        [SerializeField] List<Link> links = new List<Link>();
        [SerializeField] Rect[] labelMasks = Array.Empty<Rect>();
        readonly HashSet<string> highlighted = new HashSet<string>();
        public int ConnectionCount => links.Count;

        public void Configure(List<Link> value, Rect[] textBounds = null)
        {
            links = value;
            labelMasks = textBounds ?? Array.Empty<Rect>();
            raycastTarget = false;
            SetVerticesDirty();
        }

        public void Highlight(ISet<string> ancestors)
        {
            highlighted.Clear();
            foreach (string id in ancestors) highlighted.Add(id);
            SetVerticesDirty();
        }

        public void SetLabelMasks(Rect[] textBounds)
        {
            labelMasks = textBounds ?? Array.Empty<Rect>();
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear();
            var bounds = rectTransform.rect;
            Color grid = new Color(.64f, .71f, .79f, .018f);
            for (float x = 40; x < bounds.width; x += 180)
                Stroke(mesh, new Vector2(x, -20), new Vector2(x, -bounds.height + 20), .6f, grid);
            for (float y = 25; y < bounds.height; y += 180)
                Stroke(mesh, new Vector2(20, -y), new Vector2(bounds.width - 20, -y), .6f, grid);

            foreach (var link in links) DrawLink(mesh, link, false);
            foreach (var link in links)
                if (highlighted.Contains(link.ParentId) && highlighted.Contains(link.ChildId))
                    DrawLink(mesh, link, true);
        }

        void DrawLink(UnityEngine.UI.VertexHelper mesh, Link link, bool active)
        {
            Vector2 start = link.Start, end = link.End;
            Color tint = active ? new Color32(231, 43, 70, 255) : new Color(.64f, .71f, .79f, .35f);
            float width = active ? 4f : 3f;
            if (link.Points != null && link.Points.Length >= 2)
            {
                for (int i = 1; i < link.Points.Length; i++)
                    RoutedStroke(mesh, link.Points[i - 1], link.Points[i], width, tint);
                return;
            }
            float direction = Mathf.Sign(end.y - start.y);
            float portLength = Mathf.Min(28f, Mathf.Abs(end.y - start.y) * .2f);
            Vector2 bendA = start + new Vector2(0, direction * portLength);
            Vector2 bendB = end - new Vector2(0, direction * portLength);
            Stroke(mesh, start, bendA, width, tint);
            Stroke(mesh, bendA, bendB, width, tint);
            Stroke(mesh, bendB, end, width, tint);
            // Short ports emphasize the attachment without competing with the node.
            Stroke(mesh, start + Vector2.left * 4, start + Vector2.right * 4, width, tint);
            Stroke(mesh, end + Vector2.left * 4, end + Vector2.right * 4, width, tint);
        }

        void RoutedStroke(UnityEngine.UI.VertexHelper mesh, Vector2 a, Vector2 b, float width, Color tint)
        {
            // Leave clean gaps behind node captions, including captions crossed by a shared stem.
            var gaps = new List<Vector2>();
            Vector2 direction = b - a;
            foreach (Rect bounds in labelMasks)
            {
                float enter = 0, leave = 1;
                if (ClipAxis(a.x, direction.x, bounds.xMin, bounds.xMax, ref enter, ref leave) &&
                    ClipAxis(a.y, direction.y, bounds.yMin, bounds.yMax, ref enter, ref leave) && leave > enter)
                    gaps.Add(new Vector2(enter, leave));
            }
            gaps.Sort((left, right) => left.x.CompareTo(right.x));
            float cursor = 0;
            foreach (Vector2 gap in gaps)
            {
                if (gap.x > cursor) Stroke(mesh, a + direction * cursor, a + direction * gap.x, width, tint);
                cursor = Mathf.Max(cursor, gap.y);
            }
            if (cursor < 1) Stroke(mesh, a + direction * cursor, b, width, tint);
        }

        static bool ClipAxis(float origin, float direction, float minimum, float maximum, ref float enter, ref float leave)
        {
            if (Mathf.Abs(direction) < .00001f) return origin >= minimum && origin <= maximum;
            float first = (minimum - origin) / direction, last = (maximum - origin) / direction;
            if (first > last) { float temporary = first; first = last; last = temporary; }
            enter = Mathf.Max(enter, first);
            leave = Mathf.Min(leave, last);
            return leave >= enter;
        }

        static void Stroke(UnityEngine.UI.VertexHelper mesh, Vector2 a, Vector2 b, float width, Color tint)
        {
            Vector2 direction = b - a;
            if (direction.sqrMagnitude < .00001f) return;
            Vector2 normal = new Vector2(-direction.y, direction.x).normalized * width * .5f;
            int index = mesh.currentVertCount;
            mesh.AddVert(a + normal, tint, Vector2.zero);
            mesh.AddVert(b + normal, tint, Vector2.zero);
            mesh.AddVert(b - normal, tint, Vector2.zero);
            mesh.AddVert(a - normal, tint, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
