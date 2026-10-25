using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StudioBaekho.Tests
{
    public sealed class BaekhoCorridorMeshClipperTests
    {
        private readonly List<Object> owned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i]) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [Test]
        public void ClippedSolidIsClosedAndRetainsOriginalFaceAttributesAndSubmeshes()
        {
            Mesh source = Cube();
            source.name = "Attributed source";
            Vector3[] sourcePositions = source.vertices;
            source.colors = sourcePositions.Select(p => new Color(p.x + 0.5f, p.y + 0.5f, p.z + 0.5f, 0.6f)).ToArray();
            source.tangents = sourcePositions.Select(p => new Vector4(0f, 0f, 1f, -1f)).ToArray();
            source.SetUVs(0, sourcePositions.Select(p => new Vector2(p.y + 0.5f, p.z + 0.5f)).ToList());
            source.SetUVs(3, sourcePositions.Select(p => new Vector4(p.x, p.y, p.z, 7f)).ToList());
            source.SetUVs(7, sourcePositions.Select(p => new Vector3(p.z, p.y, 4f)).ToList());
            int[] originalTriangles = source.triangles;
            source.subMeshCount = 2;
            source.SetTriangles(originalTriangles.Take(18).ToArray(), 0);
            source.SetTriangles(originalTriangles.Skip(18).ToArray(), 1);
            Vector3[] originalNormals = source.normals;
            Vector2[] originalUvs = source.uv;

            Mesh clipped = Own(BaekhoCorridorMeshClipper.Clip(source, Matrix4x4.identity,
                new Bounds(new Vector3(-0.25f, 0f, 0f), new Vector3(0.5f, 2f, 2f))));
            Assert.That(clipped.subMeshCount, Is.EqualTo(2));
            Assert.That(clipped.GetTriangles(1).Length, Is.GreaterThan(0));
            AssertClosed(clipped);
            Assert.That(CapArea(clipped, 0, 0f), Is.EqualTo(1f).Within(0.0001f));
            var uv3 = new List<Vector4>();
            var uv7 = new List<Vector3>();
            clipped.GetUVs(3, uv3);
            clipped.GetUVs(7, uv7);
            Vector3[] positions = clipped.vertices;
            Vector3[] normals = clipped.normals;
            Vector2[] uv = clipped.uv;
            Color[] colors = clipped.colors;
            Vector4[] tangents = clipped.tangents;
            for (int i = 0; i < positions.Length; i++)
            {
                Assert.That(positions[i].x, Is.InRange(-0.50001f, 0.00001f));
                if (normals[i].x > 0.99f) continue; // Newly created cap has planar UVs.
                Assert.That(uv[i].x, Is.EqualTo(positions[i].y + 0.5f).Within(0.00001f));
                Assert.That(uv[i].y, Is.EqualTo(positions[i].z + 0.5f).Within(0.00001f));
                Assert.That((uv3[i] - new Vector4(positions[i].x, positions[i].y, positions[i].z, 7f)).magnitude,
                    Is.LessThan(0.00001f));
                Assert.That((uv7[i] - new Vector3(positions[i].z, positions[i].y, 4f)).magnitude, Is.LessThan(0.00001f));
                Assert.That(colors[i].r, Is.EqualTo(positions[i].x + 0.5f).Within(0.00001f));
                Assert.That(colors[i].a, Is.EqualTo(0.6f).Within(0.00001f));
                Assert.That(tangents[i], Is.EqualTo(new Vector4(0f, 0f, 1f, -1f)));
            }
            CollectionAssert.AreEqual(sourcePositions, source.vertices);
            CollectionAssert.AreEqual(originalNormals, source.normals);
            CollectionAssert.AreEqual(originalUvs, source.uv);
            CollectionAssert.AreEqual(originalTriangles.Take(18), source.GetTriangles(0));
            CollectionAssert.AreEqual(originalTriangles.Skip(18), source.GetTriangles(1));
        }

        [TestCase(1f)]
        [TestCase(-1f)]
        public void ArbitraryChildTransformAndMirroredScaleClipAgainstAllSixPlanes(float mirror)
        {
            Mesh source = Cube();
            Matrix4x4 transform = Matrix4x4.TRS(new Vector3(3f, -2f, 7f), Quaternion.Euler(25f, 44f, 12f),
                new Vector3(2f * mirror, 1.2f, 0.7f));
            var bounds = new Bounds(new Vector3(3f, -2f, 7f), new Vector3(0.8f, 0.8f, 0.8f));
            Mesh clipped = Own(BaekhoCorridorMeshClipper.Clip(source, transform, bounds));
            Assert.That(clipped.vertexCount, Is.GreaterThan(0));
            foreach (Vector3 position in clipped.vertices)
            {
                Vector3 actual = transform.MultiplyPoint3x4(position);
                for (int axis = 0; axis < 3; axis++)
                    Assert.That(actual[axis], Is.InRange(bounds.min[axis] - 0.00001f, bounds.max[axis] + 0.00001f));
            }
            Vector3[] points = clipped.vertices;
            Vector3[] normals = clipped.normals;
            int[] triangles = clipped.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Assert.That(Vector3.Dot(Vector3.Cross(points[b] - points[a], points[c] - points[a]), normals[a]),
                    Is.GreaterThanOrEqualTo(-0.000001f), "Cap winding must agree with local-space outward normals.");
            }
            AssertClosed(clipped);
        }

        [Test]
        public void ConcaveCutUsesConcaveTriangulationAndKeepsItsMissingCornerOpen()
        {
            Mesh source = Prism(new[] { new Vector2(0, 0), new Vector2(2, 0), new Vector2(2, 1),
                new Vector2(1, 1), new Vector2(1, 2), new Vector2(0, 2) },
                new[] { 0, 1, 3, 1, 2, 3, 0, 3, 5, 3, 4, 5 });
            Mesh clipped = Own(BaekhoCorridorMeshClipper.Clip(source, Matrix4x4.identity,
                new Bounds(new Vector3(-0.5f, 1, 1), new Vector3(1, 3, 3))));
            Assert.That(CapArea(clipped, 0, 0f), Is.EqualTo(3f).Within(0.0001f));
            Vector3[] positions = clipped.vertices;
            int[] indices = clipped.triangles;
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 a = positions[indices[i]], b = positions[indices[i + 1]], c = positions[indices[i + 2]];
                if (Mathf.Abs(a.x) + Mathf.Abs(b.x) + Mathf.Abs(c.x) > 0.0001f) continue;
                Vector3 center = (a + b + c) / 3f;
                Assert.That(center.y <= 1.00001f || center.z <= 1.00001f, Is.True);
            }
            AssertClosed(clipped);
        }

        [Test]
        public void IndependentSolidIslandsGetSeparateClosedCaps()
        {
            Mesh cube = Cube();
            var source = Own(new Mesh());
            source.CombineMeshes(new[]
            {
                new CombineInstance { mesh = cube, transform = Matrix4x4.Translate(new Vector3(0, -2, 0)) },
                new CombineInstance { mesh = cube, transform = Matrix4x4.Translate(new Vector3(0, 2, 0)) }
            });
            Mesh clipped = Own(BaekhoCorridorMeshClipper.Clip(source, Matrix4x4.identity,
                new Bounds(new Vector3(-0.25f, 0, 0), new Vector3(0.5f, 6, 2))));
            Assert.That(CapArea(clipped, 0, 0f), Is.EqualTo(2f).Within(0.0001f));
            AssertClosed(clipped);
        }

        [Test]
        public void CutWithNestedHoleReportsUnsupportedTopologyInsteadOfFillingTheHole()
        {
            Vector2[] ring = { new Vector2(-2, -2), new Vector2(2, -2), new Vector2(2, 2), new Vector2(-2, 2),
                new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            var vertices = ring.Select(p => new Vector3(-1f, p.x, p.y))
                .Concat(ring.Select(p => new Vector3(1f, p.x, p.y))).ToArray();
            var triangles = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Quad(triangles, i, j, j + 8, i + 8);
                Quad(triangles, i + 4, i + 12, j + 12, j + 4);
                Quad(triangles, i, i + 4, j + 4, j);
                Quad(triangles, i + 8, j + 8, j + 12, i + 12);
            }
            var source = Own(new Mesh { name = "Hollow test mesh", vertices = vertices, triangles = triangles.ToArray() });
            var error = Assert.Throws<ArgumentException>(() => BaekhoCorridorMeshClipper.Clip(source, Matrix4x4.identity,
                new Bounds(new Vector3(-0.5f, 0, 0), new Vector3(1, 6, 6))));
            StringAssert.Contains("Hollow test mesh", error.Message);
            StringAssert.Contains("nested holes", error.Message);
        }

        [Test]
        public void FullyOutsideSourceReturnsAnOwnedEmptyMesh()
        {
            Mesh source = Cube();
            Mesh clipped = Own(BaekhoCorridorMeshClipper.Clip(source, Matrix4x4.identity,
                new Bounds(new Vector3(5, 0, 0), Vector3.one)));
            Assert.That(clipped, Is.Not.SameAs(source));
            Assert.That(clipped.vertexCount, Is.Zero);
            Assert.That(source.vertexCount, Is.GreaterThan(0));
        }

        [Test]
        public void UnreadableAndNonTriangleMeshesReportTheSourceAndReason()
        {
            var lines = Own(new Mesh { name = "Line art", vertices = new[] { Vector3.zero, Vector3.one } });
            lines.SetIndices(new[] { 0, 1 }, MeshTopology.Lines, 0);
            StringAssert.Contains("triangles", Assert.Throws<ArgumentException>(() =>
                BaekhoCorridorMeshClipper.Clip(lines, Matrix4x4.identity, new Bounds(Vector3.zero, Vector3.one))).Message);
            Mesh unreadable = Cube();
            unreadable.name = "Unreadable test art";
            unreadable.UploadMeshData(true);
            var error = Assert.Throws<ArgumentException>(() => BaekhoCorridorMeshClipper.Clip(unreadable,
                Matrix4x4.identity, new Bounds(Vector3.zero, Vector3.one)));
            StringAssert.Contains("Unreadable test art", error.Message);
            StringAssert.Contains("readable", error.Message);
        }

        private Mesh Cube()
        {
            GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh mesh = Own(Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh));
            Object.DestroyImmediate(primitive);
            return mesh;
        }

        private Mesh Prism(Vector2[] polygon, int[] cap)
        {
            int count = polygon.Length;
            var vertices = polygon.Select(p => new Vector3(-1, p.x, p.y))
                .Concat(polygon.Select(p => new Vector3(1, p.x, p.y))).ToArray();
            var triangles = new List<int>();
            for (int i = 0; i < cap.Length; i += 3)
            {
                triangles.AddRange(new[] { cap[i], cap[i + 2], cap[i + 1] });
                triangles.AddRange(new[] { cap[i] + count, cap[i + 1] + count, cap[i + 2] + count });
            }
            for (int i = 0; i < count; i++) Quad(triangles, i, (i + 1) % count, (i + 1) % count + count, i + count);
            var mesh = Own(new Mesh { vertices = vertices, triangles = triangles.ToArray() });
            mesh.RecalculateNormals();
            return mesh;
        }

        private static void Quad(List<int> output, int a, int b, int c, int d) =>
            output.AddRange(new[] { a, b, c, a, c, d });

        private static float CapArea(Mesh mesh, int axis, float coordinate)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            float result = 0f;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                if (Mathf.Abs(a[axis] - coordinate) + Mathf.Abs(b[axis] - coordinate) + Mathf.Abs(c[axis] - coordinate) < 0.00001f)
                    result += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            }
            return result;
        }

        private static void AssertClosed(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            // Sequential cuts can legitimately create edges a few micrometres long.
            // Weld matching positions by distance instead of rounding to 0.1 mm cells,
            // which would collapse those edges and can also split near cell boundaries.
            var welded = new List<Vector3>();
            var ids = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                int found = -1;
                for (int j = 0; j < welded.Count; j++)
                    if ((vertices[i] - welded[j]).sqrMagnitude <= 0.000001f * 0.000001f)
                    { found = j; break; }
                if (found < 0) { found = welded.Count; welded.Add(vertices[i]); }
                ids[i] = found;
            }
            var edgeCounts = new Dictionary<string, int>();
            for (int i = 0; i < triangles.Length; i += 3)
                for (int edge = 0; edge < 3; edge++)
                {
                    int a = ids[triangles[i + edge]], b = ids[triangles[i + (edge + 1) % 3]];
                    Assert.That(a, Is.Not.EqualTo(b), "Clipping must not leave collapsed triangle edges.");
                    string key = a < b ? a + ":" + b : b + ":" + a;
                    edgeCounts.TryGetValue(key, out int count);
                    edgeCounts[key] = count + 1;
                }
            foreach (KeyValuePair<string, int> edge in edgeCounts)
                Assert.That(edge.Value, Is.EqualTo(2), "An exposed edge remains: " + edge.Key);
        }

        private T Own<T>(T value) where T : Object { owned.Add(value); return value; }
    }
}
