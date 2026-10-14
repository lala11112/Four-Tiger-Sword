using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StudioBaekho.Tests
{
    public sealed class BaekhoDungeonGeometryTests
    {
        private const float Tolerance = 0.001f;
        private readonly List<Object> owned = new List<Object>();
        private BaekhoCorridorStyle style;
        private GameObject parent;

        [SetUp]
        public void SetUp()
        {
            parent = Own(new GameObject("Test geometry parent"));
            var source = Own(new GameObject("Offset replacement art"));
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesh.transform.SetParent(source.transform, false);
            mesh.transform.localPosition = new Vector3(2, 3, -4);
            mesh.transform.localScale = new Vector3(3, 4, 5);
            var piece = new BaekhoPrefabPiece
            {
                prefab = source,
                localBounds = new Bounds(new Vector3(2, 3, -4), new Vector3(3, 4, 5)),
                placement = BaekhoPiecePlacement.Stretch
            };
            style = Own(ScriptableObject.CreateInstance<BaekhoCorridorStyle>());
            style.floor = piece;
            style.wall = piece;
            style.cap = piece;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i]) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [Test]
        public void ReplacementArtWithOffsetPivotFitsRequestedBoxAndUsesOneSolidCollider()
        {
            parent.transform.SetPositionAndRotation(new Vector3(11, 2, -6), Quaternion.Euler(0, 90, 0));
            var size = new Vector3(2, 3, 7);
            var piece = BaekhoDungeonGeometry.PlacePiece(style.floor, parent.transform,
                new Vector3(4, 5, 6), size, Quaternion.Euler(0, 90, 0), 0);

            AssertBounds(WorldBounds(piece.transform, size), VisualBounds(piece));
            var colliders = piece.GetComponentsInChildren<Collider>(true);
            Assert.That(colliders.Count(collider => collider.enabled), Is.EqualTo(1));
            Assert.That(piece.GetComponent<BoxCollider>().size, Is.EqualTo(size));
            Assert.That(piece.GetComponent<BoxCollider>().isTrigger, Is.False);
            Assert.That(style.floor.prefab.GetComponentInChildren<Collider>().enabled, Is.True,
                "Instantiating replacement art must not modify its source collider.");
        }

        [Test]
        public void RepeatedPiecesFillLengthAndFitOnlyTheFinalRemainder()
        {
            style.floor.placement = BaekhoPiecePlacement.Repeat;
            style.floor.repeatLength = 2f;
            var piece = BaekhoDungeonGeometry.PlacePiece(style.floor, parent.transform,
                Vector3.zero, new Vector3(2, 0.2f, 5), Quaternion.identity, 0);
            Assert.That(piece.transform.childCount, Is.EqualTo(3));
            var bounds = Enumerable.Range(0, 3).Select(i => VisualBounds(piece.transform.GetChild(i).gameObject)).ToArray();
            Assert.That(bounds[0].size.z, Is.EqualTo(2f).Within(Tolerance));
            Assert.That(bounds[1].size.z, Is.EqualTo(2f).Within(Tolerance));
            Assert.That(bounds[2].size.z, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(bounds[0].min.z, Is.EqualTo(-2.5f).Within(Tolerance));
            Assert.That(bounds[0].max.z, Is.EqualTo(bounds[1].min.z).Within(Tolerance));
            Assert.That(bounds[1].max.z, Is.EqualTo(bounds[2].min.z).Within(Tolerance));
            Assert.That(bounds[2].max.z, Is.EqualTo(2.5f).Within(Tolerance));
            AssertBounds(new Bounds(Vector3.zero, new Vector3(2, 0.2f, 5)), VisualBounds(piece));
        }

        [Test]
        public void SegmentFloorIsBelowDoorLevelAndWallsLeaveRequestedClearWidth()
        {
            var segment = new BaekhoCorridorSegment(new Vector3(2, 4, -5), new Vector3(2, 4, 5));
            BaekhoDungeonGeometry.BuildSegment(segment, 2f, style, parent.transform);
            var floor = parent.transform.Find("Floor");
            var left = parent.transform.Find("LeftWall");
            var right = parent.transform.Find("RightWall");
            Assert.That(VisualBounds(floor.gameObject).max.y, Is.EqualTo(4f).Within(Tolerance));
            Assert.That(VisualBounds(left.gameObject).min.y, Is.EqualTo(4f).Within(Tolerance));
            Assert.That(VisualBounds(right.gameObject).min.x - VisualBounds(left.gameObject).max.x,
                Is.EqualTo(2f).Within(Tolerance));
            Assert.That(floor.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("Ground")));
            Assert.That(floor.GetComponentsInChildren<Transform>(true).All(item => item.gameObject.layer == floor.gameObject.layer), Is.True);
            Assert.That(parent.GetComponentsInChildren<Collider>().Count(collider => collider.enabled), Is.EqualTo(3));
        }

        [Test]
        public void NarrowConnectionCapsOnlyTheDoorShouldersAndHidesPlaceholder()
        {
            var module = CreateModuleWithDoor();
            var placeholder = module.connections[0].placeholders[0];
            BaekhoDungeonGeometry.FinishDoor(module, 0, 1f, style, parent.transform);
            Assert.That(placeholder.activeSelf, Is.False);
            var shoulders = parent.GetComponentsInChildren<BoxCollider>()
                .Where(box => box.enabled).Select(box => box.transform).OrderBy(item => item.localPosition.x).ToArray();
            Assert.That(shoulders.Length, Is.EqualTo(2));
            var left = VisualBounds(shoulders[0].gameObject);
            var right = VisualBounds(shoulders[1].gameObject);
            Assert.That(left.min.x, Is.EqualTo(-1f).Within(Tolerance));
            Assert.That(left.max.x, Is.EqualTo(-0.5f).Within(Tolerance));
            Assert.That(right.min.x, Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(right.max.x, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void UnusedDoorIsFullyClosed()
        {
            var module = CreateModuleWithDoor();
            BaekhoDungeonGeometry.FinishDoor(module, 0, 0f, style, parent.transform);
            var cap = parent.transform.Find("Closed_N");
            Assert.That(cap, Is.Not.Null);
            AssertBounds(new Bounds(new Vector3(0, 1.5f, 2.875f), new Vector3(2, 3, 0.25f)), VisualBounds(cap.gameObject));
            Assert.That(cap.GetComponent<BoxCollider>().enabled, Is.True);
        }

        [TestCase(8f)]
        [TestCase(14f)]
        public void UnusedDoorRestoresFullMatchingWallBayInsteadOfDoorSizedCap(float wallHeight)
        {
            var module = CreateModuleWithDoor();
            module.transform.SetPositionAndRotation(new Vector3(11, 4, -7), Quaternion.Euler(0, 90, 0));
            var socket = module.connections[0];
            var template = GameObject.CreatePrimitive(PrimitiveType.Cube);
            template.name = "Matching surrounding wall";
            template.transform.SetParent(module.transform, false);
            template.transform.localPosition = new Vector3(5, wallHeight * 0.5f, 3);
            template.transform.localScale = new Vector3(4, wallHeight, 2);
            template.layer = 12;
            var material = Own(new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")));
            template.GetComponent<Renderer>().sharedMaterial = material;
            var filler = new GameObject("Associated doorway filler");
            filler.transform.SetParent(module.transform, false);
            var ceilingTile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceilingTile.name = "ceiling_ceiling-13-10";
            ceilingTile.transform.SetParent(module.transform, false);
            ceilingTile.transform.localPosition = new Vector3(0, wallHeight + 0.125f, 3);
            ceilingTile.transform.localScale = new Vector3(4, 0.25f, 2);
            var unrelatedWall = new GameObject("Unrelated wall");
            unrelatedWall.transform.SetParent(module.transform, false);
            socket.closedWall = new BaekhoClosedWall
            {
                manualOverride = true,
                template = template,
                templateLocalBounds = new Bounds(Vector3.zero, Vector3.one),
                localCenter = new Vector3(0, wallHeight * 0.5f, 3),
                localRotation = Quaternion.identity,
                size = new Vector3(4, wallHeight, 2),
                replaceWhenClosed = new[] { filler, ceilingTile }
            };
            var sourcePosition = template.transform.localPosition;
            var sourceScale = template.transform.localScale;

            BaekhoDungeonGeometry.FinishDoor(module, 0, 0f, style, parent.transform);
            var wall = parent.transform.Find("ClosedWall_N");
            Assert.That(wall, Is.Not.Null);
            Assert.That(parent.transform.Find("Closed_N"), Is.Null);
            AssertBounds(new Bounds(new Vector3(14, 4 + wallHeight * 0.5f, -7),
                new Vector3(2, wallHeight, 4)), VisualBounds(wall.gameObject));
            Assert.That(wall.GetComponent<BoxCollider>().size, Is.EqualTo(new Vector3(4, wallHeight, 2)));
            Assert.That(wall.GetComponentsInChildren<Collider>().Count(collider => collider.enabled), Is.EqualTo(1));
            Assert.That(wall.GetComponentInChildren<Renderer>().sharedMaterial, Is.SameAs(material));
            Assert.That(wall.gameObject.layer, Is.EqualTo(template.layer));
            Assert.That(filler.activeSelf, Is.False);
            Assert.That(ceilingTile.activeSelf, Is.False,
                "A doorway ceiling tile must not remain visible across the restored wall top.");
            Assert.That(socket.placeholders[0].activeSelf, Is.False);
            Assert.That(unrelatedWall.activeSelf, Is.True);
            Assert.That(template.activeSelf, Is.True);
            Assert.That(template.GetComponent<Collider>().enabled, Is.True);
            Assert.That(template.transform.localPosition, Is.EqualTo(sourcePosition));
            Assert.That(template.transform.localScale, Is.EqualTo(sourceScale));
        }

        [Test]
        public void ConnectedDoorKeepsSurroundingFillersAndDoesNotRestoreFullWall()
        {
            var module = CreateModuleWithDoor();
            var socket = module.connections[0];
            var filler = new GameObject("Doorway lintel and filler");
            filler.transform.SetParent(module.transform, false);
            var ceilingTile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceilingTile.name = "ceiling_ceiling-13-10";
            ceilingTile.transform.SetParent(module.transform, false);
            ceilingTile.transform.localPosition = new Vector3(0, 14.125f, 3);
            ceilingTile.transform.localScale = new Vector3(4, 0.25f, 2);
            socket.closedWall = new BaekhoClosedWall
            {
                template = style.cap.prefab,
                templateLocalBounds = style.cap.localBounds,
                localCenter = new Vector3(0, 7, 3),
                size = new Vector3(4, 14, 2),
                replaceWhenClosed = new[] { filler, ceilingTile },
                additionalSections = new[]
                {
                    new BaekhoWallSection
                    {
                        template = style.cap.prefab,
                        templateLocalBounds = style.cap.localBounds,
                        localCenter = new Vector3(0, -2, 3),
                        size = new Vector3(4, 4, 1)
                    }
                }
            };
            BaekhoDungeonGeometry.FinishDoor(module, 0, 2f, style, parent.transform);
            Assert.That(filler.activeSelf, Is.True);
            Assert.That(ceilingTile.activeSelf, Is.True,
                "A connected doorway must retain its ceiling tile.");
            Assert.That(socket.placeholders[0].activeSelf, Is.False);
            Assert.That(parent.transform.childCount, Is.Zero);
            Assert.That(socket.closedWall.template.activeSelf, Is.True);
        }

        [Test]
        public void ClosedDoorRestoresFoundationBelowFloorUsingItsOwnDepthAndMaterial()
        {
            var module = CreateModuleWithDoor();
            module.transform.SetPositionAndRotation(new Vector3(11, 4, -7), Quaternion.Euler(0, 90, 0));
            var socket = module.connections[0];
            var foundationTemplate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            foundationTemplate.name = "Matching cliff foundation";
            foundationTemplate.transform.SetParent(module.transform, false);
            foundationTemplate.transform.localPosition = new Vector3(5, -2, 3.5f);
            foundationTemplate.transform.localScale = new Vector3(4, 4, 1);
            foundationTemplate.layer = 12;
            var foundationMaterial = Own(new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")));
            foundationTemplate.GetComponent<Renderer>().sharedMaterial = foundationMaterial;
            var sourcePosition = foundationTemplate.transform.localPosition;
            var sourceScale = foundationTemplate.transform.localScale;
            socket.closedWall = new BaekhoClosedWall
            {
                template = style.cap.prefab,
                templateLocalBounds = style.cap.localBounds,
                localCenter = new Vector3(0, 4, 3),
                size = new Vector3(4, 8, 2),
                additionalSections = new[]
                {
                    new BaekhoWallSection
                    {
                        template = foundationTemplate,
                        templateLocalBounds = new Bounds(Vector3.zero, Vector3.one),
                        localCenter = new Vector3(0, -2, 3.5f),
                        localRotation = Quaternion.identity,
                        size = new Vector3(4, 4, 1)
                    }
                }
            };

            BaekhoDungeonGeometry.FinishDoor(module, 0, 0f, style, parent.transform);
            var wall = parent.transform.Find("ClosedWall_N");
            var foundation = parent.transform.Find("ClosedWallFoundation_N_0");
            Assert.That(wall, Is.Not.Null);
            Assert.That(foundation, Is.Not.Null);
            var wallBounds = VisualBounds(wall.gameObject);
            var foundationBounds = VisualBounds(foundation.gameObject);
            AssertBounds(new Bounds(new Vector3(14, 8, -7), new Vector3(2, 8, 4)), wallBounds);
            AssertBounds(new Bounds(new Vector3(14.5f, 2, -7), new Vector3(1, 4, 4)), foundationBounds);
            Assert.That(foundationBounds.max.y, Is.EqualTo(socket.transform.position.y).Within(Tolerance));
            Assert.That(foundationBounds.max.y, Is.EqualTo(wallBounds.min.y).Within(Tolerance),
                "The restored foundation must meet the main wall without a gap at floor height.");
            Assert.That(foundationBounds.max.x, Is.EqualTo(wallBounds.max.x).Within(Tolerance),
                "Independent thickness must preserve the shared outside face.");
            Assert.That(foundation.GetComponent<BoxCollider>().size, Is.EqualTo(new Vector3(4, 4, 1)));
            Assert.That(foundation.GetComponentsInChildren<Collider>().Count(collider => collider.enabled), Is.EqualTo(1));
            Assert.That(foundation.GetComponentInChildren<Renderer>().sharedMaterial, Is.SameAs(foundationMaterial));
            Assert.That(foundation.GetComponentInChildren<Renderer>().sharedMaterial,
                Is.Not.SameAs(wall.GetComponentInChildren<Renderer>().sharedMaterial));
            Assert.That(foundation.gameObject.layer, Is.EqualTo(foundationTemplate.layer));
            Assert.That(foundationTemplate.activeSelf, Is.True);
            Assert.That(foundationTemplate.GetComponent<Collider>().enabled, Is.True);
            Assert.That(foundationTemplate.transform.localPosition, Is.EqualTo(sourcePosition));
            Assert.That(foundationTemplate.transform.localScale, Is.EqualTo(sourceScale));
            Assert.That(foundationTemplate.GetComponent<Renderer>().sharedMaterial, Is.SameAs(foundationMaterial));
        }

        [Test]
        public void FailedRegenerationRetainsCurrentDungeonAndClearPreservesUnrelatedChildren()
        {
            var generatorRoot = Own(new GameObject("Test generator"));
            var generator = generatorRoot.AddComponent<BaekhoDungeonGenerator>();
            generator.generateOnStart = false;
            var configuration = Own(ScriptableObject.CreateInstance<BaekhoDungeonSettings>());
            configuration.mapCount = 1;
            configuration.style = style;
            configuration.maps = new[] { CreateModuleWithDoor() };
            configuration.cornerRoom = CreateCorner();
            generator.settings = configuration;
            var unrelated = new GameObject("Unrelated child");
            unrelated.transform.SetParent(generatorRoot.transform, false);
            int completed = 0;
            int failed = 0;
            generator.Completed += result => completed++;
            generator.Failed += result => failed++;

            Complete(generator.Generate(10));
            Assert.That(completed, Is.EqualTo(1));
            Assert.That(generator.IsGenerating, Is.False);
            var successfulRoot = generator.CurrentRoot;
            Assert.That(successfulRoot, Is.Not.Null);
            Assert.That(successfulRoot.activeSelf, Is.True);
            Assert.That(generator.LastResult.success, Is.True);
            Assert.That(generator.LastResult.startRoom, Is.SameAs(generator.LastResult.rooms.Single()));
            Assert.That(generator.LastResult.endRoom, Is.SameAs(generator.LastResult.startRoom));
            Assert.That(generator.LastResult.bossRoom, Is.Null);
            var identity = generator.LastResult.startRoom.GetComponent<BaekhoGeneratedRoom>();
            Assert.That(identity, Is.Not.Null);
            Assert.That(identity.instanceId, Is.EqualTo(generator.LastResult.plan.startMapId));
            Assert.That(identity.role, Is.EqualTo(BaekhoRoomRole.Start | BaekhoRoomRole.End));
            int countAfterSuccess = generatorRoot.transform.childCount;

            configuration.maps = Array.Empty<BaekhoMapModule>();
            Complete(generator.Generate(11));
            Assert.That(failed, Is.EqualTo(1));
            Assert.That(generator.CurrentRoot, Is.SameAs(successfulRoot));
            Assert.That(successfulRoot.activeSelf, Is.True);
            Assert.That(generatorRoot.transform.childCount, Is.EqualTo(countAfterSuccess), "No staging root may leak.");
            Assert.That(generator.LastResult.success, Is.False);
            Assert.That(generator.LastResult.error, Is.Not.Null.And.Not.Empty);
            Assert.That(generator.IsGenerating, Is.False);

            // Fail after staging has begun as well, so cleanup is exercised, not just validation.
            configuration.maps = new[] { CreateModuleWithDoor() };
            var interruptedBuild = generator.Generate(12);
            Assert.That(interruptedBuild.MoveNext(), Is.True);
            Assert.That(generatorRoot.transform.childCount, Is.EqualTo(countAfterSuccess + 1));
            var visualPrefab = style.cap.prefab;
            style.cap.prefab = null;
            Complete(interruptedBuild);
            style.cap.prefab = visualPrefab;
            Assert.That(failed, Is.EqualTo(2));
            Assert.That(generator.CurrentRoot, Is.SameAs(successfulRoot));
            Assert.That(successfulRoot.activeSelf, Is.True);
            Assert.That(generatorRoot.transform.childCount, Is.EqualTo(countAfterSuccess));
            Assert.That(generator.IsGenerating, Is.False);

            generator.ClearGenerated();
            Assert.That(generator.CurrentRoot, Is.Null);
            Assert.That(successfulRoot == null, Is.True);
            Assert.That(generator.LastResult, Is.Null);
            Assert.That(unrelated != null, Is.True);
            Assert.That(generatorRoot.transform.childCount, Is.EqualTo(1));
        }

        [Test]
        public void DuplicateGenerationRequestCannotReplaceAnInProgressBuild()
        {
            var generatorRoot = Own(new GameObject("Test duplicate request"));
            var generator = generatorRoot.AddComponent<BaekhoDungeonGenerator>();
            generator.generateOnStart = false;
            generator.settings = Own(ScriptableObject.CreateInstance<BaekhoDungeonSettings>());
            generator.settings.mapCount = 1;
            generator.settings.style = style;
            generator.settings.maps = new[] { CreateModuleWithDoor() };
            generator.settings.cornerRoom = CreateCorner();
            var first = generator.Generate(1);
            Assert.That(first.MoveNext(), Is.True);
            Assert.That(generator.IsGenerating, Is.True);
            Assert.That(generator.CurrentRoot, Is.Null);
            Assert.That(generatorRoot.transform.GetChild(0).gameObject.activeSelf, Is.False);
            Complete(generator.Generate(2));
            Assert.That(generator.IsGenerating, Is.True);
            Assert.That(generatorRoot.transform.childCount, Is.EqualTo(1));
            Complete(first);
            Assert.That(generator.CurrentRoot, Is.Not.Null);
            Assert.That(generator.LastResult.seed, Is.EqualTo(1));
            Assert.That(generator.LastResult.success, Is.True);
        }

        private BaekhoMapModule CreateModuleWithDoor()
        {
            var root = Own(new GameObject("Test map"));
            var module = root.AddComponent<BaekhoMapModule>();
            module.localBounds = new Bounds(new Vector3(0, 1.5f, 0), new Vector3(6, 3, 6));
            var socketRoot = new GameObject("N");
            socketRoot.transform.SetParent(root.transform, false);
            socketRoot.transform.localPosition = new Vector3(0, 0, 3);
            var socket = socketRoot.AddComponent<BaekhoMapConnection>();
            socket.id = "N";
            socket.width = 2;
            socket.height = 3;
            var placeholder = new GameObject("Temporary door");
            placeholder.transform.SetParent(socketRoot.transform, false);
            socket.placeholders = new[] { placeholder };
            module.connections = new[] { socket };
            return module;
        }

        private BaekhoMapModule CreateCorner()
        {
            var corner = CreateModuleWithDoor();
            corner.isCornerRoom = true;
            var socketRoot = new GameObject("E");
            socketRoot.transform.SetParent(corner.transform, false);
            socketRoot.transform.localPosition = new Vector3(3, 0, 0);
            socketRoot.transform.localRotation = Quaternion.Euler(0, 90, 0);
            var socket = socketRoot.AddComponent<BaekhoMapConnection>();
            socket.id = "E";
            socket.width = 2;
            socket.height = 3;
            corner.connections = new[] { corner.connections[0], socket };
            return corner;
        }

        private static void Complete(IEnumerator routine)
        {
            int iterations = 0;
            while (routine.MoveNext())
            {
                Assert.That(++iterations, Is.LessThan(1000), "Generation exceeded the test's bounded yield budget.");
                Assert.That(routine.Current, Is.Null, "EditMode fixture only drives frame-yielding generation.");
            }
        }

        private T Own<T>(T value) where T : Object
        {
            owned.Add(value);
            value.hideFlags = HideFlags.HideAndDontSave;
            return value;
        }

        private static Bounds VisualBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers.Length, Is.GreaterThan(0));
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static Bounds WorldBounds(Transform transform, Vector3 size)
        {
            var corners = new List<Vector3>();
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                        corners.Add(transform.TransformPoint(Vector3.Scale(size * 0.5f, new Vector3(x, y, z))));
            var bounds = new Bounds(corners[0], Vector3.zero);
            foreach (var corner in corners) bounds.Encapsulate(corner);
            return bounds;
        }

        private static void AssertBounds(Bounds expected, Bounds actual)
        {
            Assert.That(Vector3.Distance(expected.center, actual.center), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(expected.size, actual.size), Is.LessThan(Tolerance));
        }
    }
}
