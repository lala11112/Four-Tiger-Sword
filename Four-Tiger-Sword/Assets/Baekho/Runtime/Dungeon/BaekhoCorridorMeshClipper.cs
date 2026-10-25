using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StudioBaekho
{
    /// <summary>Clips static art without changing its visible scale or its shared mesh.</summary>
    public static class BaekhoCorridorMeshClipper
    {
        /// <summary>
        /// Returns a new, caller-owned mesh in the source mesh's coordinate system. Clip bounds
        /// are in meshToClipSpace coordinates. Newly exposed solid faces use submesh zero.
        /// </summary>
        public static Mesh Clip(Mesh source, Matrix4x4 meshToClipSpace, Bounds clipBounds)
        {
            Validate(source, meshToClipSpace, clipBounds);
            var data = new SourceData(source, meshToClipSpace);
            var triangles = new List<Triangle>();
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                int[] indices = source.GetTriangles(submesh);
                for (int i = 0; i < indices.Length; i += 3)
                    triangles.Add(new Triangle(data.vertices[indices[i]], data.vertices[indices[i + 1]],
                        data.vertices[indices[i + 2]], submesh));
            }

            float tolerance = Mathf.Max(0.000001f, clipBounds.size.magnitude * 0.000001f);
            Matrix4x4 clipToMesh = meshToClipSpace.inverse;
            for (int axis = 0; axis < 3 && triangles.Count > 0; axis++)
            {
                triangles = ClipPlane(triangles, axis, clipBounds.min[axis], 1f,
                    tolerance, meshToClipSpace, clipToMesh, source.name);
                triangles = ClipPlane(triangles, axis, clipBounds.max[axis], -1f,
                    tolerance, meshToClipSpace, clipToMesh, source.name);
            }
            return data.CreateMesh(source, triangles);
        }

        private static void Validate(Mesh source, Matrix4x4 matrix, Bounds bounds)
        {
            if (source == null) throw new ArgumentException("A source mesh is required.");
            if (!source.isReadable)
                throw new ArgumentException("Mesh '" + source.name + "' must be readable for corridor clipping.");
            if (source.blendShapeCount > 0 || source.HasVertexAttribute(VertexAttribute.BlendIndices) ||
                source.HasVertexAttribute(VertexAttribute.BlendWeight))
                throw new ArgumentException("Mesh '" + source.name + "' must be a static mesh without skinning or blend shapes.");
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
                if (source.GetTopology(submesh) != MeshTopology.Triangles)
                    throw new ArgumentException("Mesh '" + source.name + "' submesh " + submesh + " must use triangles.");
            for (int i = 0; i < 16; i++)
                if (!Finite(matrix[i])) throw new ArgumentException("The mesh clipping transform must be finite.");
            if (Mathf.Abs(matrix.determinant) < 0.000000000001f)
                throw new ArgumentException("The mesh clipping transform must be invertible.");
            for (int axis = 0; axis < 3; axis++)
                if (!Finite(bounds.center[axis]) || !Finite(bounds.size[axis]) || bounds.size[axis] <= 0f)
                    throw new ArgumentException("Mesh clip bounds must be finite and positive.");
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static List<Triangle> ClipPlane(List<Triangle> input, int axis, float boundary,
            float insideSign, float tolerance, Matrix4x4 meshToClipSpace, Matrix4x4 clipToMesh, string meshName)
        {
            var output = new List<Triangle>(input.Count);
            var edges = new BoundaryEdges(tolerance);
            var polygon = new List<Vertex>(4);
            foreach (Triangle triangle in input)
            {
                polygon.Clear();
                Vertex a = Snap(triangle.a, axis, boundary, tolerance, clipToMesh);
                Vertex b = Snap(triangle.b, axis, boundary, tolerance, clipToMesh);
                Vertex c = Snap(triangle.c, axis, boundary, tolerance, clipToMesh);
                ClipEdge(c, a, polygon, axis, boundary, insideSign);
                ClipEdge(a, b, polygon, axis, boundary, insideSign);
                ClipEdge(b, c, polygon, axis, boundary, insideSign);
                RemoveAdjacentDuplicates(polygon, tolerance);
                if (polygon.Count < 3) continue;
                for (int i = 1; i < polygon.Count - 1; i++)
                    if (!Degenerate(polygon[0], polygon[i], polygon[i + 1], tolerance))
                        output.Add(new Triangle(polygon[0], polygon[i], polygon[i + 1], triangle.submesh));
                for (int i = 0; i < polygon.Count; i++)
                {
                    Vertex first = polygon[i];
                    Vertex second = polygon[(i + 1) % polygon.Count];
                    if (Mathf.Abs(first.clipPosition[axis] - boundary) <= tolerance &&
                        Mathf.Abs(second.clipPosition[axis] - boundary) <= tolerance)
                        edges.Add(first, second);
                }
            }

            var loops = edges.GetLoops(meshName);
            // Independent islands are valid. Nested loops would require a polygon-with-holes
            // triangulator; filling them would silently seal intentional openings.
            for (int i = 0; i < loops.Count; i++)
                for (int j = 0; j < loops.Count; j++)
                    if (i != j && PointInPolygon(Project(loops[i][0].clipPosition, axis), loops[j], axis))
                        throw new ArgumentException("Mesh '" + meshName + "' has a cut face with nested holes, which corridor clipping does not support.");

            Vector3 outward = Vector3.zero;
            outward[axis] = -insideSign;
            Vector3 localNormal = meshToClipSpace.transpose.MultiplyVector(outward).normalized;
            Vector3 tangent = Vector3.zero;
            tangent[(axis + 1) % 3] = 1f;
            tangent = clipToMesh.MultiplyVector(tangent).normalized;
            Vector3 bitangent = Vector3.zero;
            bitangent[(axis + 2) % 3] = 1f;
            bitangent = clipToMesh.MultiplyVector(bitangent).normalized;
            float handedness = Vector3.Dot(Vector3.Cross(localNormal, tangent), bitangent) < 0f ? -1f : 1f;
            foreach (List<Vertex> loop in loops)
            {
                var cap = new List<Vertex>(loop.Count);
                foreach (Vertex vertex in loop)
                {
                    var copy = vertex.Copy();
                    copy.normal = localNormal;
                    copy.tangent = new Vector4(tangent.x, tangent.y, tangent.z, handedness);
                    Vector2 uv = Project(vertex.clipPosition, axis);
                    for (int channel = 0; channel < 8; channel++)
                        copy.uv[channel] = new Vector4(uv.x, uv.y, 0f, 0f);
                    cap.Add(copy);
                }
                TriangulateCap(cap, axis, localNormal, tolerance, output, meshName);
            }
            return output;
        }

        private static Vertex Snap(Vertex vertex, int axis, float boundary, float tolerance, Matrix4x4 clipToMesh)
        {
            if (vertex.clipPosition[axis] == boundary || Mathf.Abs(vertex.clipPosition[axis] - boundary) > tolerance)
                return vertex;
            Vertex copy = vertex.Copy();
            copy.clipPosition[axis] = boundary;
            copy.position = clipToMesh.MultiplyPoint3x4(copy.clipPosition);
            return copy;
        }

        private static void ClipEdge(Vertex previous, Vertex current, List<Vertex> output,
            int axis, float boundary, float sign)
        {
            float a = (previous.clipPosition[axis] - boundary) * sign;
            float b = (current.clipPosition[axis] - boundary) * sign;
            if ((a < 0f) != (b < 0f))
            {
                Vertex intersection = Vertex.Lerp(previous, current, a / (a - b));
                intersection.clipPosition[axis] = boundary;
                output.Add(intersection);
            }
            if (b >= 0f) output.Add(current);
        }

        private static void RemoveAdjacentDuplicates(List<Vertex> polygon, float tolerance)
        {
            for (int i = polygon.Count - 1; i >= 0 && polygon.Count > 1; i--)
                if ((polygon[i].clipPosition - polygon[(i + 1) % polygon.Count].clipPosition).sqrMagnitude <= tolerance * tolerance)
                    polygon.RemoveAt(i);
        }

        private static bool Degenerate(Vertex a, Vertex b, Vertex c, float tolerance) =>
            Vector3.Cross(b.clipPosition - a.clipPosition, c.clipPosition - a.clipPosition).sqrMagnitude <= tolerance * tolerance * tolerance * tolerance;

        private static Vector2 Project(Vector3 point, int axis) =>
            new Vector2(point[(axis + 1) % 3], point[(axis + 2) % 3]);

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static bool PointInPolygon(Vector2 point, List<Vertex> polygon, int axis)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vector2 a = Project(polygon[i].clipPosition, axis);
                Vector2 b = Project(polygon[j].clipPosition, axis);
                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        private static void TriangulateCap(List<Vertex> polygon, int axis, Vector3 normal,
            float tolerance, List<Triangle> output, string meshName)
        {
            float area = 0f;
            for (int i = 0; i < polygon.Count; i++)
                area += Cross(Project(polygon[i].clipPosition, axis), Project(polygon[(i + 1) % polygon.Count].clipPosition, axis));
            float epsilon = tolerance * tolerance;
            if (Mathf.Abs(area) <= epsilon) return;
            if (area < 0f) polygon.Reverse();
            var remaining = new List<int>(polygon.Count);
            for (int i = 0; i < polygon.Count; i++) remaining.Add(i);
            while (remaining.Count > 3)
            {
                bool found = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int a = remaining[(i + remaining.Count - 1) % remaining.Count];
                    int b = remaining[i];
                    int c = remaining[(i + 1) % remaining.Count];
                    Vector2 p = Project(polygon[a].clipPosition, axis);
                    Vector2 q = Project(polygon[b].clipPosition, axis);
                    Vector2 r = Project(polygon[c].clipPosition, axis);
                    if (Cross(q - p, r - q) <= epsilon) continue;
                    bool blocked = false;
                    foreach (int candidate in remaining)
                    {
                        if (candidate == a || candidate == b || candidate == c) continue;
                        Vector2 point = Project(polygon[candidate].clipPosition, axis);
                        if (Cross(q - p, point - p) >= -epsilon && Cross(r - q, point - q) >= -epsilon &&
                            Cross(p - r, point - r) >= -epsilon) { blocked = true; break; }
                    }
                    if (blocked) continue;
                    AddCapTriangle(polygon[a], polygon[b], polygon[c], normal, output);
                    remaining.RemoveAt(i);
                    found = true;
                    break;
                }
                if (!found)
                    throw new ArgumentException("Mesh '" + meshName + "' has an invalid or self-intersecting cut face that cannot be triangulated.");
            }
            if (remaining.Count == 3 && !Degenerate(polygon[remaining[0]], polygon[remaining[1]], polygon[remaining[2]], tolerance))
                AddCapTriangle(polygon[remaining[0]], polygon[remaining[1]], polygon[remaining[2]], normal, output);
        }

        private static void AddCapTriangle(Vertex a, Vertex b, Vertex c, Vector3 normal, List<Triangle> output)
        {
            if (Vector3.Dot(Vector3.Cross(b.position - a.position, c.position - a.position), normal) < 0f)
                output.Add(new Triangle(a, c, b, 0));
            else output.Add(new Triangle(a, b, c, 0));
        }

        private sealed class Vertex
        {
            public Vector3 position, clipPosition, normal;
            public Vector4 tangent;
            public Color color;
            public Vector4[] uv = new Vector4[8];

            public Vertex Copy()
            {
                var result = (Vertex)MemberwiseClone();
                result.uv = (Vector4[])uv.Clone();
                return result;
            }

            public static Vertex Lerp(Vertex a, Vertex b, float t)
            {
                var result = new Vertex
                {
                    position = Vector3.LerpUnclamped(a.position, b.position, t),
                    clipPosition = Vector3.LerpUnclamped(a.clipPosition, b.clipPosition, t),
                    normal = Vector3.LerpUnclamped(a.normal, b.normal, t).normalized,
                    tangent = Vector4.LerpUnclamped(a.tangent, b.tangent, t),
                    color = Color.LerpUnclamped(a.color, b.color, t)
                };
                Vector3 tangent = new Vector3(result.tangent.x, result.tangent.y, result.tangent.z).normalized;
                result.tangent = new Vector4(tangent.x, tangent.y, tangent.z, result.tangent.w < 0f ? -1f : 1f);
                for (int i = 0; i < 8; i++) result.uv[i] = Vector4.LerpUnclamped(a.uv[i], b.uv[i], t);
                return result;
            }
        }

        private readonly struct Triangle
        {
            public readonly Vertex a, b, c;
            public readonly int submesh;
            public Triangle(Vertex a, Vertex b, Vertex c, int submesh)
            { this.a = a; this.b = b; this.c = c; this.submesh = submesh; }
        }

        private sealed class SourceData
        {
            public readonly Vertex[] vertices;
            private readonly bool normals, tangents, colors;
            private readonly int[] uvDimensions = new int[8];

            public SourceData(Mesh source, Matrix4x4 meshToClipSpace)
            {
                Vector3[] positions = source.vertices;
                Vector3[] sourceNormals = source.normals;
                Vector4[] sourceTangents = source.tangents;
                Color[] sourceColors = source.colors;
                normals = sourceNormals.Length == positions.Length && positions.Length > 0;
                tangents = sourceTangents.Length == positions.Length && positions.Length > 0;
                colors = sourceColors.Length == positions.Length && positions.Length > 0;
                vertices = new Vertex[positions.Length];
                for (int i = 0; i < positions.Length; i++)
                    vertices[i] = new Vertex
                    {
                        position = positions[i], clipPosition = meshToClipSpace.MultiplyPoint3x4(positions[i]),
                        normal = normals ? sourceNormals[i] : Vector3.zero,
                        tangent = tangents ? sourceTangents[i] : Vector4.zero,
                        color = colors ? sourceColors[i] : Color.white
                    };
                for (int channel = 0; channel < 8; channel++)
                {
                    var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                    if (!source.HasVertexAttribute(attribute)) continue;
                    uvDimensions[channel] = source.GetVertexAttributeDimension(attribute);
                    var uvs = new List<Vector4>();
                    source.GetUVs(channel, uvs);
                    for (int i = 0; i < positions.Length; i++) vertices[i].uv[channel] = uvs[i];
                }
            }

            public Mesh CreateMesh(Mesh source, List<Triangle> triangles)
            {
                var points = new List<Vector3>(triangles.Count * 3);
                var outputNormals = new List<Vector3>(triangles.Count * 3);
                var outputTangents = new List<Vector4>(triangles.Count * 3);
                var outputColors = new List<Color>(triangles.Count * 3);
                var uvs = new List<Vector4>[8];
                for (int i = 0; i < 8; i++) if (uvDimensions[i] > 0) uvs[i] = new List<Vector4>(triangles.Count * 3);
                var indices = new List<int>[source.subMeshCount];
                for (int i = 0; i < indices.Length; i++) indices[i] = new List<int>();
                foreach (Triangle triangle in triangles)
                {
                    AddVertex(triangle.a, triangle.submesh);
                    AddVertex(triangle.b, triangle.submesh);
                    AddVertex(triangle.c, triangle.submesh);
                }
                var mesh = new Mesh { name = source.name + "_Clipped" };
                try
                {
                    if (points.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                    mesh.SetVertices(points);
                    if (normals) mesh.SetNormals(outputNormals);
                    if (tangents) mesh.SetTangents(outputTangents);
                    if (colors) mesh.SetColors(outputColors);
                    for (int channel = 0; channel < 8; channel++)
                    {
                        if (uvs[channel] == null) continue;
                        if (uvDimensions[channel] <= 2)
                            mesh.SetUVs(channel, uvs[channel].ConvertAll(value => new Vector2(value.x, value.y)));
                        else if (uvDimensions[channel] == 3)
                            mesh.SetUVs(channel, uvs[channel].ConvertAll(value => new Vector3(value.x, value.y, value.z)));
                        else mesh.SetUVs(channel, uvs[channel]);
                    }
                    mesh.subMeshCount = indices.Length;
                    for (int i = 0; i < indices.Length; i++) mesh.SetTriangles(indices[i], i, false);
                    if (!normals && points.Count > 0) mesh.RecalculateNormals();
                    mesh.RecalculateBounds();
                    return mesh;
                }
                catch
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
                    else UnityEngine.Object.DestroyImmediate(mesh);
                    throw;
                }

                void AddVertex(Vertex vertex, int submesh)
                {
                    indices[submesh].Add(points.Count);
                    points.Add(vertex.position);
                    if (normals) outputNormals.Add(vertex.normal);
                    if (tangents) outputTangents.Add(vertex.tangent);
                    if (colors) outputColors.Add(vertex.color);
                    for (int i = 0; i < 8; i++) if (uvs[i] != null) uvs[i].Add(vertex.uv[i]);
                }
            }
        }

        private sealed class BoundaryEdges
        {
            private readonly float tolerance;
            private readonly List<Vertex> vertices = new List<Vertex>();
            private readonly Dictionary<Cell, List<int>> cells = new Dictionary<Cell, List<int>>();
            private readonly Dictionary<Edge, int> edges = new Dictionary<Edge, int>();
            public BoundaryEdges(float tolerance) { this.tolerance = tolerance; }

            public void Add(Vertex a, Vertex b)
            {
                int first = Find(a), second = Find(b);
                if (first == second) return;
                var edge = new Edge(first, second);
                edges.TryGetValue(edge, out int count);
                edges[edge] = count + 1;
            }

            private int Find(Vertex vertex)
            {
                var cell = new Cell(vertex.clipPosition, tolerance);
                for (int x = -1; x <= 1; x++)
                    for (int y = -1; y <= 1; y++)
                        for (int z = -1; z <= 1; z++)
                            if (cells.TryGetValue(new Cell(cell.x + x, cell.y + y, cell.z + z), out List<int> candidates))
                                foreach (int index in candidates)
                                    if ((vertices[index].clipPosition - vertex.clipPosition).sqrMagnitude <= tolerance * tolerance)
                                        return index;
                int result = vertices.Count;
                vertices.Add(vertex);
                if (!cells.TryGetValue(cell, out List<int> list)) { list = new List<int>(); cells.Add(cell, list); }
                list.Add(result);
                return result;
            }

            public List<List<Vertex>> GetLoops(string meshName)
            {
                var neighbors = new Dictionary<int, List<int>>();
                foreach (KeyValuePair<Edge, int> item in edges)
                {
                    if (item.Value == 2) continue;
                    if (item.Value != 1)
                        throw Invalid(meshName);
                    Connect(item.Key.a, item.Key.b);
                    Connect(item.Key.b, item.Key.a);
                }
                foreach (List<int> list in neighbors.Values) if (list.Count != 2) throw Invalid(meshName);
                var visited = new HashSet<int>();
                var loops = new List<List<Vertex>>();
                foreach (int start in neighbors.Keys)
                {
                    if (visited.Contains(start)) continue;
                    var loop = new List<Vertex>();
                    int previous = -1, current = start;
                    do
                    {
                        if (!visited.Add(current)) throw Invalid(meshName);
                        loop.Add(vertices[current]);
                        List<int> next = neighbors[current];
                        int following = next[0] == previous ? next[1] : next[0];
                        previous = current;
                        current = following;
                    } while (current != start);
                    if (loop.Count < 3) throw Invalid(meshName);
                    loops.Add(loop);
                }
                return loops;

                void Connect(int a, int b)
                {
                    if (!neighbors.TryGetValue(a, out List<int> list)) { list = new List<int>(); neighbors.Add(a, list); }
                    list.Add(b);
                }
            }

            private static ArgumentException Invalid(string name) => new ArgumentException("Mesh '" + name +
                "' has an open or non-manifold cut boundary; corridor clipping requires closed solid meshes at each cut.");
        }

        private readonly struct Edge : IEquatable<Edge>
        {
            public readonly int a, b;
            public Edge(int first, int second) { a = Math.Min(first, second); b = Math.Max(first, second); }
            public bool Equals(Edge other) => a == other.a && b == other.b;
            public override bool Equals(object value) => value is Edge other && Equals(other);
            public override int GetHashCode() => unchecked(a * 397 ^ b);
        }

        private readonly struct Cell : IEquatable<Cell>
        {
            public readonly long x, y, z;
            public Cell(long x, long y, long z) { this.x = x; this.y = y; this.z = z; }
            public Cell(Vector3 value, float tolerance)
            { x = (long)Math.Floor(value.x / tolerance); y = (long)Math.Floor(value.y / tolerance); z = (long)Math.Floor(value.z / tolerance); }
            public bool Equals(Cell other) => x == other.x && y == other.y && z == other.z;
            public override bool Equals(object value) => value is Cell other && Equals(other);
            public override int GetHashCode() => unchecked((x.GetHashCode() * 397 ^ y.GetHashCode()) * 397 ^ z.GetHashCode());
        }
    }
}
