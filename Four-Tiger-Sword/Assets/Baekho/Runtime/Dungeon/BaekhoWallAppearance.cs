using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StudioBaekho
{
    [Flags]
    public enum BaekhoWallFaces { NegativeX = 1, PositiveX = 2, NegativeZ = 4, PositiveZ = 8, All = 15 }

    /// <summary>Replaces a rectangular wall's visual only; every original collider and transform stays intact.</summary>
    public static class BaekhoWallAppearance
    {
        public const string DefaultChildName = "V3WallAppearance";

        /// <summary>Bounds use owner-local coordinates. Panel sizes use world metres, including scaled legacy wall chunks.
        /// The cosmetic mesh stays within the existing box. Unselected faces receive a plain wall finish.
        /// Editor callers must persist a COPY of the generated mesh and remove its BaekhoGeneratedMeshes owner before saving a prefab.</summary>
        public static GameObject Apply(Transform owner, Bounds localBounds, BaekhoArchitectureProfile profile,
            BaekhoWallFaces faces = BaekhoWallFaces.All, string childName = DefaultChildName)
        {
            if (!owner) throw new ArgumentNullException(nameof(owner));
            if (!profile) return null;
            if (!profile.Validate(out string error)) throw new ArgumentException(error);
            if (!BaekhoDungeonGeometry.Positive(localBounds.size) || !Finite(localBounds.center)
                || string.IsNullOrEmpty(childName) || childName.Contains("/"))
                throw new ArgumentException("A finite wall box and a single appearance child name are required.");
            var scale = owner.lossyScale;
            if (!Finite(scale) || Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)) < .00001f)
                throw new ArgumentException("Wall appearance requires nonzero finite owner scale.");
            var existing = owner.Find(childName);
            if (existing && !existing.GetComponent<BaekhoWallAppearanceInstance>())
                throw new InvalidOperationException("The appearance child name belongs to authored content: " + childName);
            var size = Vector3.Scale(localBounds.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            var builder = new Builder();
            float depth = Mathf.Min(profile.panelDepth, Mathf.Min(size.x, size.z) * .2f);
            var worldCenter = owner.TransformPoint(localBounds.center);
            int patternPhase = Mathf.RoundToInt((worldCenter.x + worldCenter.z) / profile.panelWidth);
            AddFace(builder, size, Vector3.left, size.z, scale, profile, depth, (faces & BaekhoWallFaces.NegativeX) != 0, patternPhase);
            AddFace(builder, size, Vector3.right, size.z, scale, profile, depth, (faces & BaekhoWallFaces.PositiveX) != 0, patternPhase);
            AddFace(builder, size, Vector3.back, size.x, scale, profile, depth, (faces & BaekhoWallFaces.NegativeZ) != 0, patternPhase);
            AddFace(builder, size, Vector3.forward, size.x, scale, profile, depth, (faces & BaekhoWallFaces.PositiveZ) != 0, patternPhase);
            // Close the physical box's upper/lower silhouette without any extra collision.
            builder.Quad(new Vector3(-size.x, size.y, size.z) * .5f, new Vector3(size.x, size.y, size.z) * .5f,
                new Vector3(size.x, size.y, -size.z) * .5f, new Vector3(-size.x, size.y, -size.z) * .5f, Vector3.up, 0, scale);
            builder.Quad(new Vector3(-size.x, -size.y, -size.z) * .5f, new Vector3(size.x, -size.y, -size.z) * .5f,
                new Vector3(size.x, -size.y, size.z) * .5f, new Vector3(-size.x, -size.y, size.z) * .5f, Vector3.down, 0, scale);
            var materials = new[] { profile.wallMaterial, profile.jointMaterial ? profile.jointMaterial : profile.wallMaterial,
                profile.trimMaterial ? profile.trimMaterial : profile.wallMaterial, profile.accentMaterial ? profile.accentMaterial : profile.wallMaterial };
            Mesh mesh = builder.Create();
            GameObject skin = existing ? existing.gameObject : new GameObject(childName);
            bool created = !existing;
            try
            {
                // Attach ownership while active so failed inactive staging builds still receive OnDestroy.
                var ownership = skin.GetComponent<BaekhoGeneratedMeshes>();
                if (!ownership) ownership = skin.AddComponent<BaekhoGeneratedMeshes>();
                var marker = skin.GetComponent<BaekhoWallAppearanceInstance>();
                if (!marker) marker = skin.AddComponent<BaekhoWallAppearanceInstance>();
                skin.transform.SetParent(owner, false);
                skin.transform.localPosition = localBounds.center;
                skin.transform.localRotation = Quaternion.identity;
                skin.transform.localScale = Vector3.one;
                skin.layer = owner.gameObject.layer;
                var filter = skin.GetComponent<MeshFilter>();
                if (!filter) filter = skin.AddComponent<MeshFilter>();
                var renderer = skin.GetComponent<MeshRenderer>();
                if (!renderer) renderer = skin.AddComponent<MeshRenderer>();
                ownership.Release();
                filter.sharedMesh = mesh;
                ownership.Own(mesh);
                renderer.sharedMaterials = materials;
                renderer.enabled = true;
                marker.HideOriginals(owner);
                return skin;
            }
            catch
            {
                if (created) BaekhoDungeonGeometry.DestroyObject(skin);
                else BaekhoDungeonGeometry.DestroyObject(mesh);
                throw;
            }
        }

        public static void Remove(Transform owner, string childName = DefaultChildName)
        {
            if (!owner) return;
            var child = owner.Find(childName);
            var marker = child ? child.GetComponent<BaekhoWallAppearanceInstance>() : null;
            if (!marker) return;
            marker.RestoreOriginals();
            child.gameObject.SetActive(false);
            child.name = childName + "_Retired";
            BaekhoDungeonGeometry.DestroyObject(child.gameObject);
        }

        private static void AddFace(Builder b, Vector3 size, Vector3 normal, float width, Vector3 scale,
            BaekhoArchitectureProfile profile, float depth, bool panelled, int patternPhase)
        {
            var right = Vector3.Cross(Vector3.up, normal);
            var origin = Vector3.Scale(normal, size) * .5f - Vector3.up * size.y * .5f;
            if (!panelled)
            {
                Rect(b, origin, right, normal, -width * .5f, 0, width, size.y, 0, 0, scale);
                return;
            }
            double columns = Math.Ceiling((double)width / profile.panelWidth);
            double rows = Math.Ceiling((double)size.y / profile.panelHeight);
            if (columns * rows > 10000d) throw new ArgumentException("A wall face would require more than 10000 appearance panels.");
            Rect(b, origin, right, normal, -width * .5f, 0, width, size.y, -depth, 1, scale);
            float seam = Mathf.Min(profile.jointWidth * .5f, width * .1f, size.y * .1f);
            // Close the vertical corner seams of the replacement box while keeping
            // all panel relief inside its original footprint.
            Panel(b, origin, right, normal, -width * .5f, 0, seam, size.y, 0, depth, 1, scale);
            Panel(b, origin, right, normal, width * .5f - seam, 0, seam, size.y, 0, depth, 1, scale);
            for (int column = 0; column < (int)columns; column++)
            {
                float x = -width * .5f + column * profile.panelWidth;
                float cellWidth = Mathf.Min(profile.panelWidth, width - column * profile.panelWidth);
                for (int row = 0; row < (int)rows; row++)
                {
                    float y = row * profile.panelHeight;
                    float cellHeight = Mathf.Min(profile.panelHeight, size.y - y);
                    float w = cellWidth - seam * 2, h = cellHeight - seam * 2;
                    if (w <= .001f || h <= .001f) continue;
                    float ventCenter = Mathf.Min(3.15f, size.y - .3f);
                    bool vent = size.y > 2.6f && w > .65f && h > .3f && (column + patternPhase) % 3 == 0
                        && ventCenter >= y && ventCenter < y + cellHeight;
                    if (vent) VentPanel(b, origin, right, normal, x + seam, y + seam, w, h, depth, scale);
                    else Panel(b, origin, right, normal, x + seam, y + seam, w, h, -depth * .1f, depth * .9f, 0, scale);
                }
                float trim = Mathf.Min(profile.trimWidth, size.y * .1f);
                if (cellWidth > seam * 2 && size.y > .3f)
                {
                    Panel(b, origin, right, normal, x + seam, .12f, cellWidth - seam * 2, trim, 0, depth, 2, scale);
                    Panel(b, origin, right, normal, x + seam, size.y - .12f - trim, cellWidth - seam * 2, trim, 0, depth, 2, scale);
                }
                if (profile.accentMaterial && cellWidth > .5f && profile.accentHeight + .08f < size.y)
                    Panel(b, origin, right, normal, x + .12f, profile.accentHeight,
                        Mathf.Min(.4f, cellWidth - .24f), .04f, -depth * .04f, depth * .5f, 3, scale);
            }
            if (size.y > 2.4f && width > .65f)
                UpperConduit(b, origin, right, normal, width, size.y, depth, profile, scale);
        }

        private static void VentPanel(Builder b, Vector3 origin, Vector3 right, Vector3 normal,
            float x, float y, float width, float height, float depth, Vector3 scale)
        {
            float ventWidth = Mathf.Min(1.15f, width * .72f), ventHeight = Mathf.Min(.44f, height * .5f);
            float vx = x + (width - ventWidth) * .5f, vy = y + (height - ventHeight) * .5f;
            // Split the ceramic panel around a real inset; a dark quad behind an intact panel would be hidden.
            Panel(b, origin, right, normal, x, y, vx - x, height, -depth * .1f, depth * .9f, 0, scale);
            Panel(b, origin, right, normal, vx + ventWidth, y, x + width - vx - ventWidth, height, -depth * .1f, depth * .9f, 0, scale);
            Panel(b, origin, right, normal, vx, y, ventWidth, vy - y, -depth * .1f, depth * .9f, 0, scale);
            Panel(b, origin, right, normal, vx, vy + ventHeight, ventWidth, y + height - vy - ventHeight, -depth * .1f, depth * .9f, 0, scale);
            Rect(b, origin, right, normal, vx, vy, ventWidth, ventHeight, -depth * .88f, 1, scale);
            float rim = Mathf.Min(.025f, ventHeight * .1f);
            Panel(b, origin, right, normal, vx, vy, ventWidth, rim, -depth * .035f, depth * .65f, 2, scale);
            Panel(b, origin, right, normal, vx, vy + ventHeight - rim, ventWidth, rim, -depth * .035f, depth * .65f, 2, scale);
            Panel(b, origin, right, normal, vx, vy + rim, rim, ventHeight - rim * 2, -depth * .035f, depth * .65f, 2, scale);
            Panel(b, origin, right, normal, vx + ventWidth - rim, vy + rim, rim, ventHeight - rim * 2, -depth * .035f, depth * .65f, 2, scale);
            for (int slot = 1; slot <= 4; slot++)
                Panel(b, origin, right, normal, vx + rim * 2, vy + slot * ventHeight / 5f - .009f,
                    ventWidth - rim * 4, .018f, -depth * .32f, depth * .3f, 2, scale);
        }

        private static void UpperConduit(Builder b, Vector3 origin, Vector3 right, Vector3 normal,
            float width, float height, float depth, BaekhoArchitectureProfile profile, Vector3 scale)
        {
            float edge = Mathf.Min(.12f, width * .1f), run = width - edge * 2;
            float centerY = Mathf.Floor((height - .12f) / profile.panelHeight) * profile.panelHeight;
            float radiusY = Mathf.Min(.045f, profile.jointWidth * .4f), radiusDepth = Mathf.Min(depth * .44f, radiusY), centerDepth = -depth * .5f;
            // Seat the pipe in an existing upper horizontal joint so ceramic panels never hide its curved profile.
            float channelHeight = radiusY * 2 + Mathf.Min(.003f, profile.jointWidth * .08f);
            Panel(b, origin, right, normal, -width * .5f + edge, centerY - channelHeight * .5f, run, channelHeight,
                -depth * .9f, depth * .1f, 1, scale);
            float startX = -width * .5f + edge, endX = startX + run;
            // Six facets form a shallow half-round pipe. Its foremost point remains inside the original wall face.
            for (int segment = 0; segment < 6; segment++)
            {
                float a = -Mathf.PI * .5f + segment * Mathf.PI / 6f, z = a + Mathf.PI / 6f;
                var p = origin + Vector3.up * (centerY + Mathf.Sin(a) * radiusY) + normal * (centerDepth + Mathf.Cos(a) * radiusDepth);
                var q = origin + Vector3.up * (centerY + Mathf.Sin(z) * radiusY) + normal * (centerDepth + Mathf.Cos(z) * radiusDepth);
                float mid = (a + z) * .5f;
                var faceNormal = (normal * (radiusY * Mathf.Cos(mid)) + Vector3.up * (radiusDepth * Mathf.Sin(mid))).normalized;
                b.Quad(p + right * startX, p + right * endX, q + right * endX, q + right * startX, faceNormal, 2, scale);
            }
            for (int side = 0; side < 2; side++)
                Panel(b, origin, right, normal, side == 0 ? startX : endX - .04f, centerY - .075f,
                    .04f, .15f, 0, depth * .9f, 2, scale);
        }

        private static void Panel(Builder b, Vector3 origin, Vector3 right, Vector3 normal, float x, float y,
            float width, float height, float front, float depth, int material, Vector3 scale)
        {
            Rect(b, origin, right, normal, x, y, width, height, front, material, scale);
            var a = origin + right * x + Vector3.up * y + normal * front;
            var c = a + right * width + Vector3.up * height;
            var back = normal * -depth;
            b.Quad(a, a + Vector3.up * height, a + Vector3.up * height + back, a + back, -right, material, scale);
            b.Quad(c, c - Vector3.up * height, c - Vector3.up * height + back, c + back, right, material, scale);
            b.Quad(c - right * width, c, c + back, c - right * width + back, Vector3.up, material, scale);
            b.Quad(a + right * width, a, a + back, a + right * width + back, Vector3.down, material, scale);
        }

        private static void Rect(Builder b, Vector3 origin, Vector3 right, Vector3 normal, float x, float y,
            float width, float height, float depth, int material, Vector3 scale)
        {
            var a = origin + right * x + Vector3.up * y + normal * depth;
            b.Quad(a, a + right * width, a + right * width + Vector3.up * height, a + Vector3.up * height, normal, material, scale);
        }

        private static bool Finite(Vector3 v) => BaekhoDungeonGeometry.Finite(v.x) && BaekhoDungeonGeometry.Finite(v.y) && BaekhoDungeonGeometry.Finite(v.z);

        private sealed class Builder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Vector2> uv = new List<Vector2>();
            private readonly List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, int material, Vector3 scale)
            {
                int start = vertices.Count;
                foreach (var point in new[] { a, b, c, d }) vertices.Add(new Vector3(point.x / scale.x, point.y / scale.y, point.z / scale.z));
                normal = Vector3.Scale(normal, scale).normalized;
                for (int i = 0; i < 4; i++) normals.Add(normal);
                float width = Vector3.Distance(a, b), height = Vector3.Distance(b, c);
                uv.Add(Vector2.zero); uv.Add(new Vector2(width, 0)); uv.Add(new Vector2(width, height)); uv.Add(new Vector2(0, height));
                triangles[material].AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }

            public Mesh Create()
            {
                var mesh = new Mesh { name = "V3 modular wall appearance", indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.subMeshCount = triangles.Length;
                for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i, false);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
