using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace StudioBaekho
{
    /// <summary>Clips competing opaque visual triangles in an isolated room or scene hierarchy.</summary>
    public static partial class BaekhoCoplanarMeshRepair
    {
        private const float PlaneTolerance = .0001f;
        private const float ParallelDot = 1f - .000001f;
        private const double AreaTolerance = .00000001;
        // Account for rounding when a newly clipped vertex is stored in the source mesh's local space.
        // This margin is used only after confirming a positive-area overlap.
        private const double EdgeClearance = .00002;

        public sealed class Result
        {
            public Dictionary<MeshFilter, Mesh> replacements = new Dictionary<MeshFilter, Mesh>();
            public int inputTriangles, overlappingTriangles, removedTriangles;
            public double overlapArea;
        }

        /// <summary>The caller owns the transient replacement meshes. This method changes no inputs.</summary>
        public static Result Solve(GameObject root)
        {
            if (!root) throw new ArgumentNullException(nameof(root));
            var result = new Result();
            var sources = new Dictionary<Mesh, MeshSource>();
            var bindings = new List<Binding>();
            var triangles = new List<Triangle>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                Mesh mesh = filter.sharedMesh;
                if (!renderer || !renderer.enabled || renderer.forceRenderingOff || renderer.HasPropertyBlock()
                    || !mesh || mesh.blendShapeCount != 0 || IsProtected(filter.transform)) continue;
                Material[] materials = renderer.sharedMaterials;
                if (materials.Length != mesh.subMeshCount || !materials.Any(IsOpaque)) continue;
                bool supported = true;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                    if (mesh.GetTopology(submesh) != MeshTopology.Triangles) { supported = false; break; }
                if (!supported || mesh.HasVertexAttribute(VertexAttribute.BlendWeight)
                    || mesh.HasVertexAttribute(VertexAttribute.BlendIndices)) continue;

                if (!sources.TryGetValue(mesh, out MeshSource source))
                    sources.Add(mesh, source = new MeshSource(mesh));
                var binding = new Binding(filter, source, RelativeMatrix(filter.transform, root.transform));
                bindings.Add(binding);
                Vector3[] points = Array.ConvertAll(source.positions, point => TransformPoint(binding.toRoot, point));
                bool floor = IsFloor(filter.transform, root.transform)
                    || mesh.name.StartsWith("Structure_Floors_", StringComparison.OrdinalIgnoreCase);
                for (int submesh = 0; submesh < source.indices.Length; submesh++)
                {
                    if (!IsOpaque(materials[submesh])) continue;
                    int priority = Priority(materials[submesh].name);
                    int[] indices = source.indices[submesh];
                    for (int offset = 0; offset < indices.Length; offset += 3)
                    {
                        if (!Face.TryCreate(points[indices[offset]], points[indices[offset + 1]],
                            points[indices[offset + 2]], out Face face)) continue;
                        triangles.Add(new Triangle(binding, face, submesh, offset, triangles.Count,
                            floor && Mathf.Abs(face.normal.y) < .2f ? Math.Max(15, priority) : priority));
                    }
                }
            }

            result.inputTriangles = triangles.Count;
            triangles.Sort((left, right) => left.priority != right.priority
                ? right.priority.CompareTo(left.priority) : left.order.CompareTo(right.order));
            var index = new FaceIndex();
            var candidates = new List<int>();
            var cutter = new PolygonCutter();
            foreach (Triangle triangle in triangles)
            {
                Face face = triangle.face;
                int axis = DominantAxis(face.normal);
                List<List<Point>> fragments = null;
                index.Find(face, candidates);
                foreach (int candidate in candidates)
                {
                    Face blocker = index[candidate];
                    if (!face.SharesPlane(blocker, axis)) continue;
                    fragments = cutter.Subtract(fragments, face, blocker, axis, out double area);
                    result.overlapArea += area;
                    if (fragments != null && fragments.Count == 0) break;
                }
                if (fragments == null)
                {
                    index.Add(face);
                    continue;
                }

                result.overlappingTriangles++;
                if (fragments.Count == 0) result.removedTriangles++;
                triangle.binding.Record(triangle, fragments, axis, index);
            }

            try
            {
                foreach (Binding binding in bindings)
                    if (binding.Changed) result.replacements.Add(binding.filter, binding.CreateReplacement());
                return result;
            }
            catch
            {
                foreach (Mesh mesh in result.replacements.Values) Object.DestroyImmediate(mesh);
                throw;
            }
        }

        private static bool IsOpaque(Material material) => material && material.renderQueue < 2450
            && material.GetTag("RenderType", false, "") != "Transparent"
            && material.GetTag("RenderType", false, "") != "TransparentCutout"
            && (!material.HasProperty("_AlphaClip") || material.GetFloat("_AlphaClip") < .5f)
            && !material.IsKeywordEnabled("_ALPHATEST_ON");

        private static bool IsProtected(Transform transform)
        {
            for (Transform current = transform; current; current = current.parent)
            {
                if (!current.gameObject.activeSelf || current.GetComponent<Animator>() || current.GetComponent<Animation>()
                    || current.GetComponent<LODGroup>() || current.GetComponent<Rigidbody>()) return true;
                foreach (MonoBehaviour behaviour in current.GetComponents<MonoBehaviour>())
                    if (behaviour && behaviour.GetType().Name.Contains("SlidingDoor")) return true;
            }
            return false;
        }

        private static bool IsFloor(Transform filter, Transform root)
        {
            for (Transform current = filter; current != root; current = current.parent)
                if (current.name.StartsWith("floor", StringComparison.OrdinalIgnoreCase)
                    || current.name.StartsWith("bridge", StringComparison.OrdinalIgnoreCase)
                    || current.name.StartsWith("Compact_Floor_", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static int Priority(string name)
        {
            if (name.StartsWith("HUB_edge", StringComparison.OrdinalIgnoreCase)) return 50;
            if (name.StartsWith("HUB_jade", StringComparison.OrdinalIgnoreCase)) return 40;
            if (name.StartsWith("HUB_metal", StringComparison.OrdinalIgnoreCase)) return 30;
            return name.IndexOf("Brushed_Metal", StringComparison.OrdinalIgnoreCase) >= 0 ? 20 : 0;
        }

        private static Matrix4x4 RelativeMatrix(Transform child, Transform root)
        {
            var matrix = Matrix4x4.identity;
            for (Transform current = child; current != root; current = current.parent)
                matrix = Matrix4x4.TRS(current.localPosition, current.localRotation, current.localScale) * matrix;
            return matrix;
        }

        private static Vector3 TransformPoint(Matrix4x4 matrix, Vector3 point)
        {
            double x = point.x, y = point.y, z = point.z;
            return new Vector3(
                (float)(matrix.m00 * x + matrix.m01 * y + matrix.m02 * z + matrix.m03),
                (float)(matrix.m10 * x + matrix.m11 * y + matrix.m12 * z + matrix.m13),
                (float)(matrix.m20 * x + matrix.m21 * y + matrix.m22 * z + matrix.m23));
        }

        private static int DominantAxis(Vector3 normal)
        {
            float x = Mathf.Abs(normal.x), y = Mathf.Abs(normal.y), z = Mathf.Abs(normal.z);
            return x >= y && x >= z ? 0 : y >= z ? 1 : 2;
        }

        private readonly struct Triangle
        {
            public readonly Binding binding;
            public readonly Face face;
            public readonly int submesh, offset, order, priority;

            public Triangle(Binding binding, Face face, int submesh, int offset, int order, int priority)
            {
                this.binding = binding;
                this.face = face;
                this.submesh = submesh;
                this.offset = offset;
                this.order = order;
                this.priority = priority;
            }
        }

        private readonly struct Face
        {
            public readonly Vector3 a, b, c, normal, min, max;

            private Face(Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
            {
                this.a = a;
                this.b = b;
                this.c = c;
                this.normal = normal;
                min = Vector3.Min(a, Vector3.Min(b, c));
                max = Vector3.Max(a, Vector3.Max(b, c));
            }

            public static bool TryCreate(Vector3 a, Vector3 b, Vector3 c, out Face face)
            {
                Vector3 cross = Vector3.Cross(b - a, c - a);
                float length = cross.magnitude;
                if (!(length > AreaTolerance * 2) || float.IsInfinity(length))
                {
                    face = default;
                    return false;
                }
                face = new Face(a, b, c, cross / length);
                return true;
            }

            public bool SharesPlane(Face other, int axis)
            {
                if (Vector3.Dot(normal, other.normal) < ParallelDot) return false;
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                if (Mathf.Min(max[u], other.max[u]) <= Mathf.Max(min[u], other.min[u])
                    || Mathf.Min(max[v], other.max[v]) <= Mathf.Max(min[v], other.min[v])
                    || Mathf.Min(max[axis], other.max[axis]) + PlaneTolerance < Mathf.Max(min[axis], other.min[axis]))
                    return false;
                return OnPlane(other.a) && OnPlane(other.b) && OnPlane(other.c)
                    && other.OnPlane(a) && other.OnPlane(b) && other.OnPlane(c);
            }

            private bool OnPlane(Vector3 point) => Mathf.Abs(Vector3.Dot(normal, point - a)) <= PlaneTolerance;
        }

        private sealed class FaceIndex
        {
            private const float Cell = 1f, NormalCell = .003f, NormalReach = .0015f;
            private const int MaxCells = 256;
            private readonly List<Face> faces = new List<Face>();
            private readonly List<int> seen = new List<int>();
            private readonly Dictionary<(int, int, int), List<int>> all = new Dictionary<(int, int, int), List<int>>();
            private readonly Dictionary<(int, int, int), List<int>> large = new Dictionary<(int, int, int), List<int>>();
            private readonly Dictionary<(int, int, int, int, int, int), List<int>> cells =
                new Dictionary<(int, int, int, int, int, int), List<int>>();
            private int query;
            public Face this[int index] => faces[index];

            public void Add(Face face)
            {
                int id = faces.Count;
                faces.Add(face);
                seen.Add(0);
                var normal = Quantize(face.normal, NormalCell);
                Vector3Int low = Quantize(face.min, Cell), high = Quantize(face.max, Cell);
                var key = (normal.x, normal.y, normal.z);
                AddTo(all, key, id);
                if (TooLarge(low, high))
                {
                    AddTo(large, key, id);
                    return;
                }
                for (int x = low.x; x <= high.x; x++)
                    for (int y = low.y; y <= high.y; y++)
                        for (int z = low.z; z <= high.z; z++)
                            AddTo(cells, (normal.x, normal.y, normal.z, x, y, z), id);
            }

            public void Find(Face face, List<int> output)
            {
                query++;
                output.Clear();
                Vector3Int normalLow = Quantize(face.normal - Vector3.one * NormalReach, NormalCell);
                Vector3Int normalHigh = Quantize(face.normal + Vector3.one * NormalReach, NormalCell);
                Vector3Int low = Quantize(face.min - Vector3.one * PlaneTolerance, Cell);
                Vector3Int high = Quantize(face.max + Vector3.one * PlaneTolerance, Cell);
                bool big = TooLarge(low, high);
                for (int nx = normalLow.x; nx <= normalHigh.x; nx++)
                    for (int ny = normalLow.y; ny <= normalHigh.y; ny++)
                        for (int nz = normalLow.z; nz <= normalHigh.z; nz++)
                        {
                            if (!all.TryGetValue((nx, ny, nz), out List<int> matching)) continue;
                            if (big) { Append(matching, output); continue; }
                            if (large.TryGetValue((nx, ny, nz), out matching)) Append(matching, output);
                            for (int x = low.x; x <= high.x; x++)
                                for (int y = low.y; y <= high.y; y++)
                                    for (int z = low.z; z <= high.z; z++)
                                        if (cells.TryGetValue((nx, ny, nz, x, y, z), out matching)) Append(matching, output);
                        }
                output.Sort();
            }

            private void Append(List<int> entries, List<int> output)
            {
                foreach (int id in entries)
                    if (seen[id] != query) { seen[id] = query; output.Add(id); }
            }

            private static void AddTo<TKey>(Dictionary<TKey, List<int>> bins, TKey key, int id)
            {
                if (!bins.TryGetValue(key, out List<int> list)) bins.Add(key, list = new List<int>());
                list.Add(id);
            }

            private static bool TooLarge(Vector3Int low, Vector3Int high) =>
                (double)(high.x - low.x + 1) * (high.y - low.y + 1) * (high.z - low.z + 1) > MaxCells;

            private static Vector3Int Quantize(Vector3 value, float step) => new Vector3Int(
                Mathf.FloorToInt(value.x / step), Mathf.FloorToInt(value.y / step), Mathf.FloorToInt(value.z / step));
        }

        private readonly struct Point
        {
            public readonly double x, y;
            private Point(double x, double y) { this.x = x; this.y = y; }
            public static Point Project(Vector3 point, int axis) => new Point(point[(axis + 1) % 3], point[(axis + 2) % 3]);
            public static double Side(Point a, Point b, Point point) =>
                (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x);
            public static double Length(Point a, Point b) =>
                Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
            public static Point Lerp(Point a, Point b, double t) => new Point(a.x + t * (b.x - a.x), a.y + t * (b.y - a.y));
            public bool Is(Point other) => x == other.x && y == other.y;
        }

        private sealed class PolygonCutter
        {
            private readonly List<Point> original = new List<Point>(3);
            private readonly List<Point> scratchA = new List<Point>(8);
            private readonly List<Point> scratchB = new List<Point>(8);
            private readonly Point[] blocker = new Point[3];

            public List<List<Point>> Subtract(List<List<Point>> fragments, Face face, Face other, int axis, out double overlap)
            {
                blocker[0] = Point.Project(other.a, axis);
                blocker[1] = Point.Project(other.b, axis);
                blocker[2] = Point.Project(other.c, axis);
                double orientation = Math.Sign(Point.Side(blocker[0], blocker[1], blocker[2]));
                double projection = Math.Abs(face.normal[axis]);
                overlap = 0;
                if (fragments == null)
                {
                    original.Clear();
                    original.Add(Point.Project(face.a, axis));
                    original.Add(Point.Project(face.b, axis));
                    original.Add(Point.Project(face.c, axis));
                }
                int count = fragments?.Count ?? 1;
                List<List<Point>> output = null;
                for (int i = 0; i < count; i++)
                {
                    List<Point> polygon = fragments == null ? original : fragments[i];
                    double area = Intersect(polygon, orientation) / projection;
                    if (area <= AreaTolerance)
                    {
                        output?.Add(polygon);
                        continue;
                    }
                    if (output == null)
                    {
                        output = new List<List<Point>>();
                        if (fragments != null)
                            for (int j = 0; j < i; j++) output.Add(fragments[j]);
                    }
                    overlap += area;
                    // Keep the convex portion outside each successive blocker edge.
                    scratchA.Clear();
                    scratchA.AddRange(polygon);
                    List<Point> input = scratchA, inside = scratchB;
                    for (int edge = 0; edge < 3 && input.Count >= 3; edge++)
                    {
                        Point a = blocker[edge], b = blocker[(edge + 1) % 3];
                        var outside = new List<Point>(input.Count + 1);
                        Split(input, a, b, orientation, EdgeClearance * Point.Length(a, b), inside, outside);
                        if (Area(outside) / projection > AreaTolerance) output.Add(outside);
                        (input, inside) = (inside, input);
                    }
                }
                return output ?? fragments;
            }

            private double Intersect(List<Point> polygon, double orientation)
            {
                scratchA.Clear();
                scratchA.AddRange(polygon);
                List<Point> input = scratchA, inside = scratchB;
                for (int edge = 0; edge < 3 && input.Count >= 3; edge++)
                {
                    Split(input, blocker[edge], blocker[(edge + 1) % 3], orientation, 0, inside, null);
                    (input, inside) = (inside, input);
                }
                return Area(input);
            }

            private static void Split(List<Point> polygon, Point a, Point b, double orientation, double clearance,
                List<Point> inside, List<Point> outside)
            {
                inside.Clear();
                Point previous = polygon[polygon.Count - 1];
                double previousSide = orientation * Point.Side(a, b, previous) + clearance;
                foreach (Point next in polygon)
                {
                    double nextSide = orientation * Point.Side(a, b, next) + clearance;
                    if ((nextSide >= 0) != (previousSide >= 0))
                    {
                        Point cut = Point.Lerp(previous, next, previousSide / (previousSide - nextSide));
                        Add(inside, cut);
                        if (outside != null) Add(outside, cut);
                    }
                    if (nextSide >= 0) Add(inside, next);
                    else if (outside != null) Add(outside, next);
                    previous = next;
                    previousSide = nextSide;
                }
                Close(inside);
                if (outside != null) Close(outside);
            }

            private static void Add(List<Point> polygon, Point point)
            {
                if (polygon.Count == 0 || !polygon[polygon.Count - 1].Is(point)) polygon.Add(point);
            }

            private static void Close(List<Point> polygon)
            {
                if (polygon.Count > 1 && polygon[0].Is(polygon[polygon.Count - 1])) polygon.RemoveAt(polygon.Count - 1);
            }

            private static double Area(List<Point> polygon)
            {
                double area = 0;
                for (int i = 1; i + 1 < polygon.Count; i++) area += Point.Side(polygon[0], polygon[i], polygon[i + 1]);
                return Math.Abs(area) * .5;
            }
        }

        private sealed class MeshSource
        {
            public readonly Mesh mesh;
            public readonly int[][] indices;
            public readonly Vector3[] positions, normals;
            public readonly Vector4[] tangents;
            public readonly Color[] colors;
            public readonly Vector4[][] uv = new Vector4[8][];
            public readonly int[] uvDimensions = new int[8];

            public MeshSource(Mesh mesh)
            {
                this.mesh = mesh;
                using (Mesh.MeshDataArray snapshot = MeshUtility.AcquireReadOnlyMeshData(mesh))
                {
                    Mesh.MeshData data = snapshot[0];
                    positions = Read<Vector3>(data.vertexCount, data.GetVertices);
                    if (data.HasVertexAttribute(VertexAttribute.Normal))
                        normals = Read<Vector3>(data.vertexCount, data.GetNormals);
                    if (data.HasVertexAttribute(VertexAttribute.Tangent))
                        tangents = Read<Vector4>(data.vertexCount, data.GetTangents);
                    if (data.HasVertexAttribute(VertexAttribute.Color))
                        colors = Read<Color>(data.vertexCount, data.GetColors);
                    for (int channel = 0; channel < uv.Length; channel++)
                    {
                        var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                        if (!data.HasVertexAttribute(attribute)) continue;
                        uvDimensions[channel] = data.GetVertexAttributeDimension(attribute);
                        uv[channel] = Read<Vector4>(data.vertexCount, values => data.GetUVs(channel, values));
                    }
                    indices = new int[data.subMeshCount][];
                    for (int i = 0; i < indices.Length; i++)
                        indices[i] = Read<int>(data.GetSubMesh(i).indexCount, values => data.GetIndices(values, i, true));
                }
            }

            private static T[] Read<T>(int length, Action<NativeArray<T>> copy) where T : struct
            {
                using (var buffer = new NativeArray<T>(length, Allocator.Temp, NativeArrayOptions.UninitializedMemory))
                {
                    copy(buffer);
                    return buffer.ToArray();
                }
            }
        }

        private readonly struct Blend
        {
            private readonly int a, b, c;
            private readonly double u, v, w;
            public Blend(int a, int b, int c, double v, double w)
            {
                this.a = a; this.b = b; this.c = c;
                this.u = 1 - v - w; this.v = v; this.w = w;
            }
            public Vector3 Value(Vector3[] values)
            {
                Vector3 first = values[a], second = values[b], third = values[c];
                return new Vector3(Interpolate(first.x, second.x, third.x),
                    Interpolate(first.y, second.y, third.y), Interpolate(first.z, second.z, third.z));
            }
            public Vector4 Value(Vector4[] values)
            {
                Vector4 first = values[a], second = values[b], third = values[c];
                return new Vector4(Interpolate(first.x, second.x, third.x),
                    Interpolate(first.y, second.y, third.y), Interpolate(first.z, second.z, third.z),
                    Interpolate(first.w, second.w, third.w));
            }
            public Color Value(Color[] values)
            {
                Color first = values[a], second = values[b], third = values[c];
                return new Color(Interpolate(first.r, second.r, third.r),
                    Interpolate(first.g, second.g, third.g), Interpolate(first.b, second.b, third.b),
                    Interpolate(first.a, second.a, third.a));
            }
            private float Interpolate(float first, float second, float third) => (float)(first * u + second * v + third * w);
        }

        private sealed class Binding
        {
            public readonly MeshFilter filter;
            public readonly Matrix4x4 toRoot;
            private readonly MeshSource source;
            private readonly List<Blend> added = new List<Blend>();
            private readonly Dictionary<(int, int), int[]> edits = new Dictionary<(int, int), int[]>();
            public bool Changed => edits.Count != 0;

            public Binding(MeshFilter filter, MeshSource source, Matrix4x4 toRoot)
            {
                this.filter = filter;
                this.source = source;
                this.toRoot = toRoot;
            }

            public void Record(Triangle triangle, List<List<Point>> polygons, int axis, FaceIndex index)
            {
                int[] indices = source.indices[triangle.submesh];
                int a = indices[triangle.offset], b = indices[triangle.offset + 1], c = indices[triangle.offset + 2];
                Point pa = Point.Project(triangle.face.a, axis), pb = Point.Project(triangle.face.b, axis),
                    pc = Point.Project(triangle.face.c, axis);
                double area = Point.Side(pa, pb, pc);
                var replacement = new List<int>();
                foreach (List<Point> polygon in polygons)
                {
                    var polygonIndices = new int[polygon.Count];
                    var points = new Vector3[polygon.Count];
                    for (int i = 0; i < polygon.Count; i++)
                    {
                        Point point = polygon[i];
                        int original = point.Is(pa) ? a : point.Is(pb) ? b : point.Is(pc) ? c : -1;
                        if (original >= 0)
                        {
                            polygonIndices[i] = original;
                            points[i] = TransformPoint(toRoot, source.positions[original]);
                            continue;
                        }
                        var blend = new Blend(a, b, c, Point.Side(pa, point, pc) / area, Point.Side(pa, pb, point) / area);
                        polygonIndices[i] = source.positions.Length + added.Count;
                        points[i] = TransformPoint(toRoot, blend.Value(source.positions));
                        added.Add(blend);
                    }
                    for (int i = 1; i + 1 < polygon.Count; i++)
                    {
                        if (!Face.TryCreate(points[0], points[i], points[i + 1], out Face kept)) continue;
                        replacement.Add(polygonIndices[0]);
                        replacement.Add(polygonIndices[i]);
                        replacement.Add(polygonIndices[i + 1]);
                        index.Add(kept);
                    }
                }
                edits.Add((triangle.submesh, triangle.offset), replacement.ToArray());
            }

            public Mesh CreateReplacement()
            {
                var mesh = new Mesh { name = source.mesh.name + " Coplanar Visual",
                    indexFormat = source.mesh.indexFormat == IndexFormat.UInt32 || source.positions.Length + added.Count > 65535
                        ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                try
                {
                    mesh.vertices = Append(source.positions, blend => blend.Value(source.positions));
                    if (source.normals != null) mesh.normals = Append(source.normals, blend => blend.Value(source.normals).normalized);
                    if (source.tangents != null) mesh.tangents = Append(source.tangents, blend =>
                    {
                        Vector4 value = blend.Value(source.tangents);
                        Vector3 direction = new Vector3(value.x, value.y, value.z).normalized;
                        return new Vector4(direction.x, direction.y, direction.z, value.w < 0f ? -1f : 1f);
                    });
                    if (source.colors != null) mesh.colors = Append(source.colors, blend => blend.Value(source.colors));
                    for (int channel = 0; channel < source.uv.Length; channel++)
                    {
                        Vector4[] values = source.uv[channel];
                        if (values == null) continue;
                        Vector4[] uv = Append(values, blend => blend.Value(values));
                        switch (source.uvDimensions[channel])
                        {
                            case 1:
                                using (var scalar = new NativeArray<float>(Array.ConvertAll(uv, value => value.x), Allocator.Temp))
                                    mesh.SetUVs(channel, scalar);
                                break;
                            case 2: mesh.SetUVs(channel, Array.ConvertAll(uv, value => new Vector2(value.x, value.y))); break;
                            case 3: mesh.SetUVs(channel, Array.ConvertAll(uv, value => new Vector3(value.x, value.y, value.z))); break;
                            default: mesh.SetUVs(channel, uv); break;
                        }
                    }
                    mesh.subMeshCount = source.indices.Length;
                    for (int submesh = 0; submesh < source.indices.Length; submesh++)
                    {
                        int[] indices = source.indices[submesh];
                        var result = new List<int>(indices.Length);
                        for (int offset = 0; offset < indices.Length; offset += 3)
                        {
                            if (edits.TryGetValue((submesh, offset), out int[] replacement)) result.AddRange(replacement);
                            else { result.Add(indices[offset]); result.Add(indices[offset + 1]); result.Add(indices[offset + 2]); }
                        }
                        mesh.SetTriangles(result, submesh, false);
                    }
                    mesh.bounds = source.mesh.bounds;
                    return mesh;
                }
                catch
                {
                    Object.DestroyImmediate(mesh);
                    throw;
                }
            }

            private T[] Append<T>(T[] sourceValues, Func<Blend, T> interpolate)
            {
                var output = new T[sourceValues.Length + added.Count];
                Array.Copy(sourceValues, output, sourceValues.Length);
                for (int i = 0; i < added.Count; i++) output[sourceValues.Length + i] = interpolate(added[i]);
                return output;
            }
        }
    }
}
