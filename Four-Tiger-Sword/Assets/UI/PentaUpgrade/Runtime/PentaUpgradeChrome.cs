using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Penta.Upgrade
{
    /// <summary>BoundaryZERO blade rails, drawn at the containing rect's native size.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PentaUpgradeChrome : MaskableGraphic
    {
        public enum Style { Window, Detail, Node, Diamond, Button, PrimaryButton, TreeNode, UnlockButton }
        [SerializeField] private Style mode;
        [SerializeField] private bool selected;
        [SerializeField] private bool linked;
        private float treeNodeFocus;
        private VertexHelper mesh;
        private float w, h;
        private static readonly Color Navy = new Color32(13, 21, 33, 255);
        private static readonly Color GlassTop = new Color32(18, 33, 51, 255);
        private static readonly Color GlassBottom = new Color32(8, 17, 30, 255);
        private static readonly Color Steel = new Color32(232, 238, 245, 255);
        private static readonly Color Red = new Color32(231, 43, 70, 255);
        private static readonly Color FocusRim = new Color32(255, 225, 232, 255);
        private const float TreeNodeCenterFromTop = 75f;

        public bool IsTreeNode => mode == Style.TreeNode;
        public Vector2 TreeNodeCenter
        {
            get
            {
                Rect rect = rectTransform.rect;
                return new Vector2(rect.center.x, rect.yMax - TreeNodeCenterFromTop);
            }
        }

        public void SetTreeNodeFocus(float amount)
        {
            if (treeNodeFocus == amount) return;
            treeNodeFocus = amount;
            SetVerticesDirty();
        }

        public void Configure(Style mode, bool selected = false, bool linked = false)
        {
            this.mode = mode;
            this.selected = selected;
            this.linked = linked;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); mesh = vh;
            w = rectTransform.rect.width; h = rectTransform.rect.height;
            if (w <= 0 || h <= 0) return;
            switch (mode)
            {
                case Style.Window: Window(); break;
                case Style.Detail: Detail(); break;
                case Style.Node: Node(); break;
                case Style.TreeNode: TreeNode(); break;
                case Style.Diamond: Diamond(w * .5f, h * .5f, Mathf.Min(w, h) * .38f, selected); break;
                default: Button(); break;
            }
        }

        private static Color A(Color c, float a) { c.a = a; return c; }
        private Vector2 V(float x, float y) { return new Vector2(x, y); }
        private void Poly(Color c, params Vector2[] p)
        {
            // Ear clipping handles the blade's re-entrant shoulder without overlapping fans.
            int first = mesh.currentVertCount;
            Color tint = c * color;
            foreach (Vector2 q in p)
                mesh.AddVert(new Vector3(rectTransform.rect.xMin + q.x, rectTransform.rect.yMax - q.y), tint, Vector2.zero);
            var remaining = new List<int>();
            float area = 0;
            for (int i = 0; i < p.Length; i++) { remaining.Add(i); area += Cross(p[i], p[(i + 1) % p.Length]); }
            float sign = area >= 0 ? 1 : -1;
            int guard = p.Length * p.Length;
            while (remaining.Count > 2 && guard-- > 0)
            {
                bool found = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int a = remaining[(i + remaining.Count - 1) % remaining.Count], b = remaining[i], d = remaining[(i + 1) % remaining.Count];
                    if (Cross(p[b] - p[a], p[d] - p[b]) * sign <= .0001f) continue;
                    bool inside = false;
                    foreach (int j in remaining)
                    {
                        if (j == a || j == b || j == d) continue;
                        if (Cross(p[b] - p[a], p[j] - p[a]) * sign >= 0 &&
                            Cross(p[d] - p[b], p[j] - p[b]) * sign >= 0 &&
                            Cross(p[a] - p[d], p[j] - p[d]) * sign >= 0) { inside = true; break; }
                    }
                    if (inside) continue;
                    mesh.AddTriangle(first + a, first + b, first + d);
                    remaining.RemoveAt(i); found = true; break;
                }
                if (!found) break;
            }
        }
        private static float Cross(Vector2 a, Vector2 b) { return a.x * b.y - a.y * b.x; }
        private void Line(float x, float y, float xx, float yy, float width, Color c)
        {
            Vector2 a = V(x, y), b = V(xx, yy);
            Vector2 n = new Vector2(-(b-a).y, (b-a).x).normalized * width * .5f;
            Poly(c, a+n, b+n, b-n, a-n);
        }
        private void Stroke(float width, Color c, params Vector2[] p)
        { for (int i = 1; i < p.Length; i++) Line(p[i-1].x, p[i-1].y, p[i].x, p[i].y, width, c); }
        private void Surface(float left, float top, float right, float bottom, float cut = 0)
        {
            int first = mesh.currentVertCount;
            Vector2[] p = { V(left, top), V(right-cut, top), V(right, top+cut), V(right, bottom), V(left, bottom) };
            foreach (Vector2 q in p)
            {
                Color c = Color.Lerp(GlassTop, GlassBottom, Mathf.InverseLerp(top, bottom, q.y));
                mesh.AddVert(new Vector3(rectTransform.rect.xMin+q.x, rectTransform.rect.yMax-q.y), c*color, Vector2.zero);
            }
            for (int i=1; i<p.Length-1; i++) mesh.AddTriangle(first, first+i, first+i+1);
        }
        private void Window()
        {
            Surface(29, 29, w-29, h-29, 22);
            Stroke(1.3f,A(Steel,.42f),V(24,114),V(24,66),V(66,24),V(w-114,24),V(w-70,44),V(w-24,44));
            Stroke(1,A(Steel,.25f),V(w-24,64),V(w-24,h-68),V(w-68,h-24),V(104,h-24),V(24,h-64),V(24,174));
            Stroke(3,Steel,V(26,106),V(26,66),V(66,26),V(207,26));
            Stroke(3,Steel,V(w-26,h-146),V(w-26,h-68),V(w-68,h-26),V(w-226,h-26));
            Poly(Steel,V(36,63),V(70,30),V(144,30),V(126,43),V(77,43),V(36,83));
            Poly(Red,V(211,24),V(326,24),V(310,31),V(204,31));
            Poly(Red,V(w-78,h-35),V(w-36,h-77),V(w-36,h-58),V(w-59,h-35));
            for(int i=0;i<3;i++) Poly(A(Steel,.7f-i*.17f),V(59+i*19,h-40),V(67+i*19,h-40),V(80+i*19,h-26),V(72+i*19,h-26));
            Line(40,190,40,h-112,1,A(Steel,.12f)); Line(w-40,190,w-40,h-174,1,A(Steel,.12f));
            Line(w-160,25,w-120,25,2,Steel);
        }
        private void Detail()
        {
            Surface(1,1,w-1,h-1,27);
            Stroke(1.3f,A(Steel,.60f),V(1,66),V(1,1),V(w-28,1),V(w-1,28),V(w-1,h-29),V(w-29,h-1),V(1,h-1),V(1,h-155));
            Stroke(1,A(Steel,.20f),V(15,14),V(w-35,14),V(w-14,35));
            Stroke(1,A(Steel,.18f),V(15,h-14),V(w-36,h-14),V(w-14,h-36));
            Line(2,2,61,2,3,Red);
        }
        private void Node()
        {
            Poly(A(Navy,.68f),V(36,7),V(w-2,7),V(w-12,h-5),V(36,h-5));
            Line(42,7,w-3,7,.8f,A(Steel,selected?.72f:.28f));
            Line(42,h-5,w-13,h-5,.7f,A(Steel,.15f));
            Line(w-3,7,w-13,h-5,1,A(Steel,selected?.85f:.35f));
            if(selected) Poly(Red,V(w-9,6),V(w-1,6),V(w-5,12),V(w-13,12));
            Diamond(18,17,15,selected);
        }
        private void TreeNode()
        {
            float x = w * .5f, y = TreeNodeCenterFromTop, radius = 62;
            if (selected)
                Poly(A(Red,.10f),V(x,y-71),V(x+71,y),V(x,y+71),V(x-71,y));
            Poly(A(Navy,.98f),V(x,y-radius),V(x+radius,y),V(x,y+radius),V(x-radius,y));
            Poly(A(GlassTop,.88f),V(x,y-54),V(x+54,y),V(x,y+54),V(x-54,y));
            Poly(A(GlassBottom,.90f),V(x-54,y),V(x,y+54),V(x+54,y),V(x,y+25));
            Color rim = A(selected ? Red : Steel, selected ? 1f : linked ? .78f : .56f);
            Color focusedRim = selected ? Color.Lerp(Red, FocusRim, .35f) : FocusRim;
            Stroke(Mathf.Lerp(2f, 3f, treeNodeFocus), Color.Lerp(rim, focusedRim, treeNodeFocus),
                V(x,y-radius),V(x+radius,y),V(x,y+radius),V(x-radius,y),V(x,y-radius));
            Stroke(1f,A(Steel,.20f),V(x,y-53),V(x+53,y),V(x,y+53),V(x-53,y),V(x,y-53));
            if (!selected) return;
            const float b = 49, arm = 15;
            Stroke(2f,Steel,V(x-b,y-b+arm),V(x-b,y-b),V(x-b+arm,y-b));
            Stroke(2f,Steel,V(x+b-arm,y-b),V(x+b,y-b),V(x+b,y-b+arm));
            Stroke(2f,Steel,V(x-b,y+b-arm),V(x-b,y+b),V(x-b+arm,y+b));
            Stroke(2f,Steel,V(x+b-arm,y+b),V(x+b,y+b),V(x+b,y+b-arm));
            Line(x-22,y+radius+7,x+22,y+radius+7,3,Red);
        }
        private void Diamond(float x,float y,float radius,bool active)
        {
            Poly(A(Navy,.94f),V(x,y-radius),V(x+radius,y),V(x,y+radius),V(x-radius,y));
            Stroke(1.1f,A(active?Red:Steel,active?.95f:linked?.68f:.42f),V(x,y-radius),V(x+radius,y),V(x,y+radius),V(x-radius,y),V(x,y-radius));
            float inner=radius*.72f;
            Stroke(.7f,A(Steel,.19f),V(x,y-inner),V(x+inner,y),V(x,y+inner),V(x-inner,y),V(x,y-inner));
            if(!active) return;
            float b=radius*.91f, arm=Mathf.Max(5,radius*.35f);
            Stroke(1.6f,Steel,V(x-b,y-b+arm),V(x-b,y-b),V(x-b+arm,y-b));
            Stroke(1.6f,Steel,V(x+b-arm,y-b),V(x+b,y-b),V(x+b,y-b+arm));
            Stroke(1.6f,Steel,V(x-b,y+b-arm),V(x-b,y+b),V(x-b+arm,y+b));
            Stroke(1.6f,Steel,V(x+b-arm,y+b),V(x+b,y+b),V(x+b,y+b-arm));
            Line(x-radius*.28f,y+radius+3,x+radius*.28f,y+radius+3,2,Red);
        }
        private void Button()
        {
            bool unlock = mode == Style.UnlockButton;
            Color fill = unlock ? new Color32(82, 19, 35, 255) : Navy;
            Poly(A(fill,.96f),V(0,2),V(w-9,2),V(w,h*.45f),V(w-10,h-2),V(0,h-2));
            Stroke(1,A(unlock ? Red : Steel,unlock ? 1f : .48f),V(9,2),V(w-9,2),V(w,h*.45f),V(w-10,h-2),V(9,h-2));
            if (mode == Style.PrimaryButton || unlock)
            {
                Color accent = Red;
                Line(2,7,2,h-7,4,accent);
                Poly(accent,V(w-11,1),V(w-3,1),V(w,7),V(w-7,7));
            }
        }
    }
}
