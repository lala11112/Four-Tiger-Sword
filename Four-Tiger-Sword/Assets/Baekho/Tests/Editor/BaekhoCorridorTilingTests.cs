using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StudioBaekho.Tests
{
    public sealed class BaekhoCorridorTilingTests
    {
        private const float Tolerance = 0.001f;
        private readonly List<Object> owned = new List<Object>();
        private GameObject parent;
        private GameObject source;
        private Mesh sourceMesh;
        private MeshFilter sourceFilter;
        private BaekhoCorridorStyle style;

        [SetUp]
        public void SetUp()
        {
            parent = Own(new GameObject("Tiling test parent"));
            source = Own(new GameObject("Authored 2m corridor art"));
            source.transform.localScale = new Vector3(2f, 1f, 2f);
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sourceMesh = Own(Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh));
            Object.DestroyImmediate(primitive);
            sourceMesh.name = "Readable two-material unit cube";
            sourceMesh.uv = sourceMesh.vertices.Select(vertex => new Vector2(vertex.x + 0.5f, vertex.z + 0.5f)).ToArray();
            var triangles = sourceMesh.triangles;
            sourceMesh.subMeshCount = 2;
            sourceMesh.SetTriangles(triangles.Take(triangles.Length / 2).ToArray(), 0);
            sourceMesh.SetTriangles(triangles.Skip(triangles.Length / 2).ToArray(), 1);
            var art = new GameObject("Offset-capable mesh child");
            art.transform.SetParent(source.transform, false);
            sourceFilter = art.AddComponent<MeshFilter>();
            sourceFilter.sharedMesh = sourceMesh;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            art.AddComponent<MeshRenderer>().sharedMaterials = new[]
            {
                Own(new Material(shader)), Own(new Material(shader))
            };
            art.AddComponent<BoxCollider>();
            style = Own(ScriptableObject.CreateInstance<BaekhoCorridorStyle>());
            style.floor = NewPiece();
            style.wall = NewPiece();
            style.cap = NewPiece();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i]) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [Test]
        public void PlacementEnumRetainsSerializedLegacyValues()
        {
            Assert.That((int)BaekhoPiecePlacement.Stretch, Is.EqualTo(0));
            Assert.That((int)BaekhoPiecePlacement.Repeat, Is.EqualTo(1));
            Assert.That((int)BaekhoPiecePlacement.TilePreserveSize, Is.EqualTo(2));
        }

        [Test]
        public void FourByFiveFloorUsesSixAuthoredTilesAndCropsOnlyTheLastRow()
        {
            BaekhoDungeonGeometry.BuildSegment(new BaekhoCorridorSegment(Vector3.zero, new Vector3(0f, 0f, 5f)),
                4f, style, parent.transform);

            var floor = parent.transform.Find("Floor");
            Assert.That(floor, Is.Not.Null);
            Assert.That(floor.childCount, Is.EqualTo(6));
            var visuals = Children(floor);
            foreach (var visual in visuals)
            {
                Assert.That(visual.localScale.x, Is.EqualTo(2f).Within(Tolerance));
                Assert.That(visual.localScale.z, Is.EqualTo(2f).Within(Tolerance));
                Assert.That(visual.localScale.y, Is.EqualTo(style.floorThickness).Within(Tolerance));
            }
            var bounds = visuals.Select(visual => MeshBoundsIn(visual.gameObject, floor)).ToArray();
            Assert.That(bounds.Count(box => Mathf.Abs(box.size.z - 2f) < Tolerance), Is.EqualTo(4));
            Assert.That(bounds.Count(box => Mathf.Abs(box.size.z - 1f) < Tolerance), Is.EqualTo(2));
            Assert.That(bounds.All(box => Mathf.Abs(box.size.x - 2f) < Tolerance), Is.True);
            AssertBounds(new Bounds(Vector3.zero, new Vector3(4f, style.floorThickness, 5f)),
                MeshBoundsIn(floor.gameObject, floor));
            AssertTilesFillWithoutOverlap(bounds, 4f * 5f);
            Assert.That(floor.GetComponentsInChildren<MeshFilter>().Count(filter => filter.sharedMesh == sourceMesh),
                Is.EqualTo(4), "Uncut tiles should reuse the original mesh.");
            AssertOneCollider(floor.gameObject, new Vector3(4f, style.floorThickness, 5f));
            Assert.That(floor.GetComponentsInChildren<Transform>(true).All(item => item.gameObject.layer == LayerMask.NameToLayer("Ground")), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ThinAuthoredTilesCropCeilingsAtDistantRotatedPositionsWithoutLosingGeometry(bool transformParent)
        {
            var thinCube = Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
            thinCube.name = "Authored 2m by 1mm floor tile";
            thinCube.transform.localScale = new Vector3(2f, 0.001f, 2f);
            var tile = new BaekhoPrefabPiece
            {
                prefab = thinCube,
                localBounds = new Bounds(Vector3.zero, Vector3.one),
                placement = BaekhoPiecePlacement.TilePreserveSize
            };
            style.floor = tile;
            style.wall = tile;
            style.cap = tile;
            style.wallHeight = 4f;
            style.ceilingThickness = 0.2f;
            if (transformParent)
                parent.transform.SetPositionAndRotation(new Vector3(1234f, 7f, -2345f), Quaternion.Euler(0f, 90f, 0f));
            var segment = new BaekhoCorridorSegment(new Vector3(90.5f, 2f, -109.5f), new Vector3(89f, 2f, -109.5f));

            Assert.DoesNotThrow(() => BaekhoDungeonGeometry.BuildSegment(segment, 4f, style, parent.transform));

            var ceiling = parent.transform.Find("Ceiling");
            Assert.That(ceiling, Is.Not.Null);
            var filters = ceiling.GetComponentsInChildren<MeshFilter>();
            Assert.That(filters.Length, Is.EqualTo(3));
            Assert.That(filters.All(filter => filter.sharedMesh != null && filter.sharedMesh.vertexCount > 0), Is.True,
                "Cropping the remainder on both tile axes must leave every visible roof tile intact.");
            Assert.That(filters.All(filter => filter.GetComponent<MeshRenderer>().enabled), Is.True);
            AssertBounds(new Bounds(Vector3.zero, new Vector3(4.5f, 0.2f, 1.5f)),
                MeshBoundsIn(ceiling.gameObject, ceiling));
            AssertBounds(new Bounds(new Vector3(89.75f, 6.1f, -109.5f), new Vector3(1.5f, 0.2f, 4.5f)),
                MeshBoundsIn(ceiling.gameObject, parent.transform));
            AssertOneCollider(ceiling.gameObject, new Vector3(4.5f, 0.2f, 1.5f));
            Assert.That(thinCube.transform.localScale, Is.EqualTo(new Vector3(2f, 0.001f, 2f)));
        }

        [Test]
        public void TwoAndHalfMetreWallsKeepAuthoredLengthWhileFittingHeightAndThickness()
        {
            style.wallHeight = 5f;
            BaekhoDungeonGeometry.BuildSegment(new BaekhoCorridorSegment(Vector3.zero, new Vector3(0f, 0f, 2.5f)),
                4f, style, parent.transform);
            foreach (var wall in new[] { parent.transform.Find("LeftWall"), parent.transform.Find("RightWall") })
            {
                Assert.That(wall.childCount, Is.EqualTo(2));
                var visuals = Children(wall);
                foreach (var visual in visuals)
                {
                    Assert.That(visual.localScale.z, Is.EqualTo(2f).Within(Tolerance));
                    Assert.That(visual.localScale.y, Is.EqualTo(5f).Within(Tolerance));
                    Assert.That(visual.localScale.x, Is.EqualTo(style.wallThickness).Within(Tolerance));
                }
                var lengths = visuals.Select(visual => MeshBoundsIn(visual.gameObject, wall).size.z).OrderBy(value => value).ToArray();
                Assert.That(lengths[0], Is.EqualTo(0.5f).Within(Tolerance));
                Assert.That(lengths[1], Is.EqualTo(2f).Within(Tolerance));
                AssertBounds(new Bounds(Vector3.zero, new Vector3(style.wallThickness, 5f, 2.5f)), MeshBoundsIn(wall.gameObject, wall));
                AssertOneCollider(wall.gameObject, new Vector3(style.wallThickness, 5f, 2.5f));
            }
            var left = MeshBoundsIn(parent.transform.Find("LeftWall").gameObject, parent.transform);
            var right = MeshBoundsIn(parent.transform.Find("RightWall").gameObject, parent.transform);
            Assert.That(right.min.x - left.max.x, Is.EqualTo(4f).Within(Tolerance));
            Assert.That(left.min.y, Is.EqualTo(0f).Within(Tolerance));
        }

        [TestCase(1f, 0.5f)]
        [TestCase(4f, 1f)]
        public void DoorShouldersCropAcrossWidthAndKeepTheAuthoredHorizontalScale(float openWidth, float shoulderWidth)
        {
            var module = CreateDoor();
            BaekhoDungeonGeometry.FinishDoor(module, 0, openWidth, style, parent.transform);

            Assert.That(parent.transform.childCount, Is.EqualTo(2));
            foreach (var shoulder in Children(parent.transform))
            {
                Assert.That(shoulder.childCount, Is.EqualTo(1));
                Assert.That(shoulder.GetChild(0).localScale.x, Is.EqualTo(2f).Within(Tolerance));
                float height = openWidth > 2f ? style.wallHeight : module.connections[0].height;
                var expectedSize = new Vector3(shoulderWidth, height, style.wallThickness);
                AssertBounds(new Bounds(Vector3.zero, expectedSize), MeshBoundsIn(shoulder.gameObject, shoulder));
                AssertOneCollider(shoulder.gameObject, expectedSize);
                AssertCutSurfaceArea(shoulder, shoulderWidth * 0.5f, height * style.wallThickness);
            }
            Assert.That(module.connections[0].width, Is.EqualTo(2f));
            Assert.That(module.connections[0].placeholders[0].activeSelf, Is.False);
        }

        [Test]
        public void StyleCapForUnusedDoorRepeatsAcrossWidthAndCropsTheRemainder()
        {
            var module = CreateDoor();
            module.connections[0].width = 2.5f;
            BaekhoDungeonGeometry.FinishDoor(module, 0, 0f, style, parent.transform);

            var cap = parent.transform.Find("Closed_N");
            Assert.That(cap, Is.Not.Null);
            Assert.That(cap.childCount, Is.EqualTo(2));
            Assert.That(Children(cap).All(visual => Mathf.Abs(visual.localScale.x - 2f) < Tolerance), Is.True);
            var widths = Children(cap).Select(visual => MeshBoundsIn(visual.gameObject, cap).size.x).OrderBy(value => value).ToArray();
            Assert.That(widths[0], Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(widths[1], Is.EqualTo(2f).Within(Tolerance));
            AssertBounds(new Bounds(Vector3.zero, new Vector3(2.5f, 3f, style.wallThickness)), MeshBoundsIn(cap.gameObject, cap));
        }

        [TestCase(0f)]
        [TestCase(90f)]
        public void RotatedNestedMeshesAndOffsetPivotsFitWithoutChangingAuthoredTileScales(float yaw)
        {
            var carrier = new GameObject("Rotated nested carrier");
            carrier.transform.SetParent(source.transform, false);
            carrier.transform.localPosition = new Vector3(2f, 3f, -4f);
            carrier.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            sourceFilter.transform.SetParent(carrier.transform, false);
            sourceFilter.transform.localPosition = new Vector3(0.5f, 0f, 0.25f);
            sourceFilter.transform.localScale = new Vector3(2f, 1f, 0.5f);
            source.transform.localScale = new Vector3(3f, 2f, 4f);
            style.floor.localBounds = MeshBoundsIn(source, source.transform);
            parent.transform.SetPositionAndRotation(new Vector3(11f, 2f, -6f), Quaternion.Euler(0f, yaw, 0f));
            var wanted = new Vector3(4.2f, 0.35f, 9.25f);

            var piece = BaekhoDungeonGeometry.PlacePiece(style.floor, parent.transform,
                new Vector3(4f, 5f, 6f), wanted, Quaternion.Euler(0f, 90f, 0f), 0,
                tiledAxes: BaekhoPieceTilingAxes.XZ);

            AssertBounds(new Bounds(Vector3.zero, wanted), MeshBoundsIn(piece, piece.transform));
            AssertTilesFillWithoutOverlap(Children(piece.transform).Select(visual => MeshBoundsIn(visual.gameObject, piece.transform)).ToArray(), wanted.x * wanted.z);
            foreach (var visual in Children(piece.transform))
            {
                Assert.That(visual.localScale.x, Is.EqualTo(3f).Within(Tolerance));
                Assert.That(visual.localScale.z, Is.EqualTo(4f).Within(Tolerance));
                var copiedCarrier = visual.Find("Rotated nested carrier");
                Assert.That(copiedCarrier.localPosition, Is.EqualTo(carrier.transform.localPosition));
                Assert.That(Quaternion.Angle(copiedCarrier.localRotation, carrier.transform.localRotation), Is.LessThan(Tolerance));
            }
            AssertOneCollider(piece, wanted);
        }

        [Test]
        public void CroppingPreservesOriginalFaceUvsNormalsMaterialSlotsAndSourceAssets()
        {
            var originalVertices = sourceMesh.vertices;
            var originalUvs = sourceMesh.uv;
            var originalNormals = sourceMesh.normals;
            var originalTriangles = Enumerable.Range(0, sourceMesh.subMeshCount).Select(index => sourceMesh.GetTriangles(index)).ToArray();
            var materials = sourceFilter.GetComponent<MeshRenderer>().sharedMaterials;

            var piece = BaekhoDungeonGeometry.PlacePiece(style.cap, parent.transform, Vector3.zero,
                new Vector3(0.5f, 3f, 0.25f), Quaternion.identity, 0, tiledAxes: BaekhoPieceTilingAxes.X);

            var filter = piece.GetComponentInChildren<MeshFilter>();
            Assert.That(filter.sharedMesh, Is.Not.SameAs(sourceMesh));
            var clipped = filter.sharedMesh;
            var vertices = clipped.vertices;
            var uvs = clipped.uv;
            var normals = clipped.normals;
            int inspected = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (Mathf.Abs(normals[i].z) < 0.99f) continue;
                Assert.That(uvs[i].x, Is.EqualTo(vertices[i].x + 0.5f).Within(Tolerance));
                Assert.That(uvs[i].y, Is.EqualTo(vertices[i].z + 0.5f).Within(Tolerance));
                Assert.That(Mathf.Abs(normals[i].z), Is.EqualTo(1f).Within(Tolerance));
                inspected++;
            }
            Assert.That(inspected, Is.GreaterThan(0));
            var copiedMaterials = filter.GetComponent<MeshRenderer>().sharedMaterials;
            Assert.That(copiedMaterials.Length, Is.GreaterThanOrEqualTo(materials.Length));
            for (int i = 0; i < materials.Length; i++) Assert.That(copiedMaterials[i], Is.SameAs(materials[i]));
            CollectionAssert.AreEqual(originalVertices, sourceMesh.vertices);
            CollectionAssert.AreEqual(originalUvs, sourceMesh.uv);
            CollectionAssert.AreEqual(originalNormals, sourceMesh.normals);
            Assert.That(sourceMesh.subMeshCount, Is.EqualTo(originalTriangles.Length));
            for (int i = 0; i < originalTriangles.Length; i++) CollectionAssert.AreEqual(originalTriangles[i], sourceMesh.GetTriangles(i));
            CollectionAssert.AreEqual(materials, sourceFilter.GetComponent<MeshRenderer>().sharedMaterials);
            Assert.That(source.transform.localScale, Is.EqualTo(new Vector3(2f, 1f, 2f)));
            Assert.That(sourceFilter.GetComponent<BoxCollider>().enabled, Is.True);
        }

        [Test]
        public void OptionalColliderDisablesAllSourceCollidersIncludingInactiveChildren()
        {
            var hiddenCollider = new GameObject("Inactive source collider");
            hiddenCollider.transform.SetParent(source.transform, false);
            hiddenCollider.AddComponent<BoxCollider>();
            hiddenCollider.SetActive(false);

            var piece = BaekhoDungeonGeometry.PlacePiece(style.floor, parent.transform, Vector3.zero,
                new Vector3(4f, 0.2f, 5f), Quaternion.identity, 0, false, BaekhoPieceTilingAxes.XZ);

            Assert.That(piece.GetComponent<BoxCollider>(), Is.Null);
            Assert.That(piece.GetComponentsInChildren<Collider>(true).All(collider => !collider.enabled), Is.True);
            Assert.That(source.GetComponentsInChildren<Collider>(true).All(collider => collider.enabled), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DestroyingAnInitiallyInactiveOrActiveHierarchyReleasesOnlyGeneratedMeshes(bool initiallyInactive)
        {
            if (initiallyInactive) parent.SetActive(false);
            var piece = BaekhoDungeonGeometry.PlacePiece(style.floor, parent.transform, Vector3.zero,
                new Vector3(4f, 0.2f, 5f), Quaternion.identity, 0, tiledAxes: BaekhoPieceTilingAxes.XZ);
            var generated = piece.GetComponentsInChildren<MeshFilter>(true).Select(filter => filter.sharedMesh)
                .Where(mesh => mesh != sourceMesh).Distinct().ToArray();
            Assert.That(generated.Length, Is.GreaterThan(0));

            Object.DestroyImmediate(parent);

            Assert.That(generated.All(mesh => mesh == null), Is.True, "Cropped meshes must not leak from discarded staging roots.");
            Assert.That(sourceMesh != null, Is.True, "The authored mesh is shared and must survive generated geometry cleanup.");
        }

        [Test]
        public void TwoDimensionalTileLimitRejectsBeforeAllocatingAContainer()
        {
            var error = Assert.Throws<ArgumentException>(() => BaekhoDungeonGeometry.PlacePiece(style.floor,
                parent.transform, Vector3.zero, new Vector3(202f, 0.2f, 202f), Quaternion.identity, 0,
                tiledAxes: BaekhoPieceTilingAxes.XZ));

            StringAssert.Contains("10000", error.Message);
            Assert.That(parent.transform.childCount, Is.Zero);
        }

        [Test]
        public void ReadableStaticTilingStyleIsAccepted()
        {
            Assert.That(BaekhoDungeonGenerator.ValidateStyle(style, out var error), Is.True, error);
        }

        [Test]
        public void NonReadableTileMeshProducesAnActionableStyleValidationError()
        {
            sourceMesh.UploadMeshData(true);

            Assert.That(BaekhoDungeonGenerator.ValidateStyle(style, out var error), Is.False);
            StringAssert.Contains("read", error.ToLowerInvariant());
            AssertNamesSourceOrMesh(error);
        }

        [Test]
        public void SkinnedTileRendererProducesAnActionableStyleValidationError()
        {
            var skin = source.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = sourceMesh;

            Assert.That(BaekhoDungeonGenerator.ValidateStyle(style, out var error), Is.False);
            StringAssert.Contains("skin", error.ToLowerInvariant());
            AssertNamesSourceOrMesh(error);
        }

        [Test]
        public void NonTriangleTileMeshProducesAnActionableStyleValidationError()
        {
            sourceMesh.SetIndices(new[] { 0, 1 }, MeshTopology.Lines, 0);

            Assert.That(BaekhoDungeonGenerator.ValidateStyle(style, out var error), Is.False);
            Assert.That(error.ToLowerInvariant(), Does.Contain("triang").Or.Contain("topology"));
            AssertNamesSourceOrMesh(error);
        }

        private BaekhoPrefabPiece NewPiece() => new BaekhoPrefabPiece
        {
            prefab = source,
            localBounds = new Bounds(Vector3.zero, Vector3.one),
            placement = BaekhoPiecePlacement.TilePreserveSize,
            repeatLength = 7f // This setting belongs to legacy Repeat; authored mesh size controls tiling.
        };

        private BaekhoMapModule CreateDoor()
        {
            var root = Own(new GameObject("Tiling door test map"));
            var module = root.AddComponent<BaekhoMapModule>();
            var socketObject = new GameObject("N");
            socketObject.transform.SetParent(root.transform, false);
            socketObject.transform.localPosition = new Vector3(0f, 0f, 3f);
            var socket = socketObject.AddComponent<BaekhoMapConnection>();
            socket.id = "N";
            socket.width = 2f;
            socket.height = 3f;
            var placeholder = new GameObject("Door placeholder");
            placeholder.transform.SetParent(socketObject.transform, false);
            socket.placeholders = new[] { placeholder };
            module.connections = new[] { socket };
            return module;
        }

        private void AssertNamesSourceOrMesh(string error)
        {
            Assert.That(error, Does.Contain(source.name).Or.Contain(sourceMesh.name),
                "Unsupported art diagnostics must identify the source prefab or mesh.");
        }

        private T Own<T>(T value) where T : Object
        {
            owned.Add(value);
            value.hideFlags = HideFlags.HideAndDontSave;
            return value;
        }

        private static Transform[] Children(Transform root) => Enumerable.Range(0, root.childCount).Select(root.GetChild).ToArray();

        private static Bounds MeshBoundsIn(GameObject root, Transform frame)
        {
            bool initialized = false;
            var result = new Bounds();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                foreach (var vertex in filter.sharedMesh.vertices)
                {
                    var point = frame.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                    if (!initialized) { result = new Bounds(point, Vector3.zero); initialized = true; }
                    else result.Encapsulate(point);
                }
            Assert.That(initialized, Is.True, "Geometry should contain readable visible meshes.");
            return result;
        }

        private static void AssertBounds(Bounds expected, Bounds actual)
        {
            Assert.That(Vector3.Distance(expected.center, actual.center), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(expected.size, actual.size), Is.LessThan(Tolerance));
        }

        private static void AssertOneCollider(GameObject piece, Vector3 size)
        {
            var collider = piece.GetComponent<BoxCollider>();
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.isTrigger, Is.False);
            Assert.That(collider.size, Is.EqualTo(size));
            Assert.That(piece.GetComponentsInChildren<Collider>(true).Count(item => item.enabled), Is.EqualTo(1));
        }

        private static void AssertTilesFillWithoutOverlap(Bounds[] tiles, float area)
        {
            Assert.That(tiles.Sum(tile => tile.size.x * tile.size.z), Is.EqualTo(area).Within(Tolerance));
            for (int i = 0; i < tiles.Length; i++)
                for (int j = i + 1; j < tiles.Length; j++)
                {
                    float overlapX = Mathf.Min(tiles[i].max.x, tiles[j].max.x) - Mathf.Max(tiles[i].min.x, tiles[j].min.x);
                    float overlapZ = Mathf.Min(tiles[i].max.z, tiles[j].max.z) - Mathf.Max(tiles[i].min.z, tiles[j].min.z);
                    Assert.That(overlapX <= Tolerance || overlapZ <= Tolerance, Is.True, "Tiles must meet without overlapping.");
                }
        }

        private static void AssertCutSurfaceArea(Transform root, float cutX, float expectedArea)
        {
            float area = 0f;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var points = filter.sharedMesh.vertices.Select(vertex => root.InverseTransformPoint(filter.transform.TransformPoint(vertex))).ToArray();
                var triangles = filter.sharedMesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var a = points[triangles[i]];
                    var b = points[triangles[i + 1]];
                    var c = points[triangles[i + 2]];
                    if (Mathf.Abs(a.x - cutX) < Tolerance && Mathf.Abs(b.x - cutX) < Tolerance && Mathf.Abs(c.x - cutX) < Tolerance)
                        area += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                }
            }
            Assert.That(area, Is.EqualTo(expectedArea).Within(Tolerance), "The newly cut end must be closed by a complete surface.");
        }
    }
}
