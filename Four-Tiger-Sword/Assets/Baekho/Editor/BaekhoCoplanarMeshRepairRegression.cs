using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace StudioBaekho
{
    public static partial class BaekhoCoplanarMeshRepair
    {
        public static string RunRegression()
        {
            var root = new GameObject("Coplanar repair fixture");
            var meshes = new HashSet<Mesh>();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var higher = new Material(shader) { name = "HUB_edge", renderQueue = 2000 };
            var lower = new Material(shader) { name = "HUB_jade", renderQueue = 2000 };
            try
            {
                root.transform.SetPositionAndRotation(new Vector3(500f, 40f, -350f), Quaternion.Euler(11f, 37f, 9f));
                Vector3 a = Vector3.zero, b = new Vector3(2, 0, 0), c = new Vector3(0, 2, 0);
                Vector3[] baseFace = { a, b, c };
                MeshFilter keep = FixtureMesh(root.transform, higher, meshes, baseFace, false);
                // Imported Hub geometry is non-readable in the player.
                keep.sharedMesh.UploadMeshData(true);
                MeshFilter change = FixtureMesh(root.transform, lower, meshes, new[]
                {
                    a, b, c,                                                     // Fully covered.
                    new Vector3(1, 0, 0), new Vector3(3, 0, 0), new Vector3(1, 2, 0), // Partially covered.
                    a, new Vector3(0, -2, 0), b,                                  // Shares an edge only.
                    a, c, b,                                                     // Opposite facing.
                    new Vector3(8, 0, 0), new Vector3(10, 0, 0), new Vector3(8, 2, 0) // Separate material slot.
                }, true);
                Mesh original = change.sharedMesh;
                var collider = change.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = original;

                Result result = Solve(root);
                meshes.UnionWith(result.replacements.Values);
                Require(result.replacements.Count == 1 && result.replacements.ContainsKey(change),
                    "Only the lower-priority filter needs a replacement.");
                Require(result.inputTriangles == 6 && result.overlappingTriangles == 2 && result.removedTriangles == 1,
                    "Expected one full and one partial overlap.");
                Require(Math.Abs(result.overlapArea - 2.5) < .0001, "Unexpected area removed.");
                Mesh repaired = result.replacements[change];
                Require(change.sharedMesh == original && collider.sharedMesh == original,
                    "Solver changed the visual or collider source.");
                Require(repaired.subMeshCount == 2 && repaired.GetTriangles(1).SequenceEqual(new[] { 12, 13, 14 }),
                    "Unchanged submesh indices moved.");
                int[] remaining = repaired.GetTriangles(0);
                Require(remaining.Skip(remaining.Length - 6).SequenceEqual(new[] { 6, 7, 8, 9, 10, 11 }),
                    "Adjacent or opposite faces changed.");
                Require(!remaining.Any(i => i < 3) && repaired.vertexCount > original.vertexCount,
                    "Complete removal or fragment creation failed.");
                Vector3[] positions = repaired.vertices;
                Vector2[] uv0 = repaired.uv;
                Color[] colors = repaired.colors;
                Vector3[] normals = repaired.normals;
                Vector4[] tangents = repaired.tangents;
                var uv1 = new List<Vector3>();
                var uv7 = new List<Vector4>();
                repaired.GetUVs(1, uv1);
                repaired.GetUVs(7, uv7);
                Require(repaired.GetVertexAttributeDimension(VertexAttribute.TexCoord1) == 3
                    && repaired.GetVertexAttributeDimension(VertexAttribute.TexCoord5) == 1
                    && repaired.GetVertexAttributeDimension(VertexAttribute.TexCoord7) == 4,
                    "UV channel dimensions changed.");
                for (int i = original.vertexCount; i < positions.Length; i++)
                {
                    Vector3 p = positions[i];
                    Require((uv0[i] - (Vector2)p).sqrMagnitude < 1e-9f && (uv1[i] - p * 2f).sqrMagnitude < 1e-9f
                        && (uv7[i] - new Vector4(p.x, p.y, p.z, 1f)).sqrMagnitude < 1e-9f
                        && Mathf.Abs(colors[i].r - p.x * .02f - .5f) < .0001f
                        && normals[i] == Vector3.forward && tangents[i] == new Vector4(1, 0, 0, 1),
                        "New vertex attributes do not match their interpolated position.");
                }
                change.sharedMesh = repaired;
                Result repeat = Solve(root);
                meshes.UnionWith(repeat.replacements.Values);
                Require(repeat.replacements.Count == 0 && repeat.overlappingTriangles == 0,
                    "Repaired geometry still has overlapping same-facing triangles.");
                CheckLargeNearPlane(root.transform, higher, lower, meshes);
                return "PASS: complete and partial clipping, adjacent and opposite faces, attributes, collider and repeat solve at small and large coordinates.";
            }
            finally
            {
                Object.DestroyImmediate(root);
                foreach (Mesh mesh in meshes) Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(higher);
                Object.DestroyImmediate(lower);
            }
        }

        private static void CheckLargeNearPlane(Transform parent, Material higher, Material lower, HashSet<Mesh> meshes)
        {
            var root = new GameObject("Large near-plane faces");
            root.transform.SetParent(parent, false);
            Vector3[] blocker =
            {
                new Vector3(49.593f, -34.013f, .00005f),
                new Vector3(71.391f, -34.013f, .00005f),
                new Vector3(49.593f, -14.26f, .00005f)
            };
            FixtureMesh(root.transform, higher, meshes, blocker, false);
            FixtureMesh(root.transform, higher, meshes,
                Array.ConvertAll(blocker, point => point + Vector3.forward * .0002f), false);
            MeshFilter change = FixtureMesh(root.transform, lower, meshes, new[]
            {
                new Vector3(58.391f, -36.725f, 0f),
                new Vector3(80.734f, -35.111f, 0f),
                new Vector3(61.44f, -18.723f, 0f)
            }, false);
            Result initial = Solve(root);
            meshes.UnionWith(initial.replacements.Values);
            Require(initial.replacements.Count == 1 && initial.replacements.ContainsKey(change)
                && initial.overlappingTriangles == 1 && initial.removedTriangles == 0 && initial.overlapArea > 1,
                "Expected one partial overlap between large triangles and no cut on the more distant plane.");
            change.sharedMesh = initial.replacements[change];
            Result repeat = Solve(root);
            meshes.UnionWith(repeat.replacements.Values);
            Require(repeat.replacements.Count == 0, "Rounding left an overlap after cutting a long triangle.");
        }

        private static MeshFilter FixtureMesh(Transform parent, Material material, HashSet<Mesh> owned,
            Vector3[] roomVertices, bool offset)
        {
            var owner = new GameObject(material.name);
            owner.transform.SetParent(parent, false);
            if (offset)
            {
                owner.transform.localPosition = new Vector3(.5f, .25f, 0f);
                owner.transform.localScale = new Vector3(2f, .5f, 1f);
            }
            Matrix4x4 toLocal = Matrix4x4.TRS(owner.transform.localPosition, Quaternion.identity,
                owner.transform.localScale).inverse;
            Vector3[] local = Array.ConvertAll(roomVertices, toLocal.MultiplyPoint3x4);
            var mesh = new Mesh { name = "Coplanar test source", vertices = local };
            owned.Add(mesh);
            mesh.normals = Enumerable.Repeat(Vector3.forward, local.Length).ToArray();
            mesh.tangents = Enumerable.Repeat(new Vector4(1, 0, 0, 1), local.Length).ToArray();
            mesh.colors = Array.ConvertAll(local, p => new Color(.5f + p.x * .02f, .5f, .5f, 1f));
            mesh.SetUVs(0, Array.ConvertAll(local, p => (Vector2)p));
            mesh.SetUVs(1, Array.ConvertAll(local, p => p * 2f));
            using (var scalar = new NativeArray<float>(Array.ConvertAll(local, p => p.y), Allocator.Temp))
                mesh.SetUVs(5, scalar);
            mesh.SetUVs(7, Array.ConvertAll(local, p => new Vector4(p.x, p.y, p.z, 1f)));
            mesh.subMeshCount = offset ? 2 : 1;
            mesh.SetTriangles(Enumerable.Range(0, offset ? 12 : 3).ToArray(), 0);
            if (offset) mesh.SetTriangles(new[] { 12, 13, 14 }, 1);
            var filter = owner.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            owner.AddComponent<MeshRenderer>().sharedMaterials = offset ? new[] { material, material } : new[] { material };
            return filter;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
