using UnityEngine;
using UnityEngine.UI;

namespace ArpgHud
{
    // An independently filled parallelogram fitted to the openings in the original frame.
    public sealed class HudSlantedImage : Image
    {
        [Range(0, .25f)] public float slant = .055f;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            float amount = type == Type.Filled ? fillAmount : 1;
            if (amount <= 0) return;
            Rect r = GetPixelAdjustedRect();
            float cut = r.xMin + r.width * amount;
            float skew = r.width * slant;
            var polygon = new System.Collections.Generic.List<Vector2> {
                new Vector2(r.xMin, r.yMin), new Vector2(r.xMax-skew,r.yMin),
                new Vector2(r.xMax,r.yMax), new Vector2(r.xMin+skew,r.yMax)
            };
            var clipped = new System.Collections.Generic.List<Vector2>();
            for(int i=0;i<polygon.Count;i++) {
                Vector2 a=polygon[i], b=polygon[(i+1)%polygon.Count];
                if(a.x<=cut) clipped.Add(a);
                if((a.x<=cut)!=(b.x<=cut)) clipped.Add(Vector2.Lerp(a,b,(cut-a.x)/(b.x-a.x)));
            }
            Vector4 uv = sprite ? UnityEngine.Sprites.DataUtility.GetOuterUV(sprite) : new Vector4(0,0,1,1);
            for(int i=0;i<clipped.Count;i++) {
                Vector2 p=clipped[i];
                vh.AddVert(p,color,new Vector2(Mathf.Lerp(uv.x,uv.z,(p.x-r.xMin)/r.width),Mathf.Lerp(uv.y,uv.w,(p.y-r.yMin)/r.height)));
            }
            for(int i=1;i<clipped.Count-1;i++) vh.AddTriangle(0,i,i+1);
        }
    }
}
