using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StudioBaekho.Tests
{
    public sealed class BaekhoCorridorLightingTests
    {
        private const float Tolerance = 0.001f;
        private readonly List<Object> owned = new List<Object>();
        private GameObject parent;
        private BaekhoCorridorStyle style;

        [SetUp]
        public void SetUp()
        {
            parent = Own(new GameObject("Ceiling and lighting test parent"));
            var source = Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
            source.name = "Unit corridor art";
            style = Own(ScriptableObject.CreateInstance<BaekhoCorridorStyle>());
            style.floor = Piece(source);
            style.wall = Piece(source);
            style.cap = Piece(source);
            style.wallHeight = 4f;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i]) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(180f)]
        [TestCase(270f)]
        public void CeilingMeetsWallTopsAtElevationAndKeepsOneSolidCollider(float yaw)
        {
            parent.transform.SetPositionAndRotation(new Vector3(11f, 2f, -6f), Quaternion.Euler(0f, 90f, 0f));
            var start = new Vector3(2f, 4f, -5f);
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var end = start + rotation * Vector3.forward * 10f;

            BaekhoDungeonGeometry.BuildSegment(new BaekhoCorridorSegment(start, end), 4f, style, parent.transform);

            var ceiling = parent.transform.Find("Ceiling");
            Assert.That(ceiling, Is.Not.Null);
            AssertVector((start + end) * 0.5f + Vector3.up * (style.wallHeight + style.ceilingThickness * 0.5f),
                ceiling.localPosition);
            Assert.That(Quaternion.Angle(ceiling.localRotation, rotation), Is.LessThan(Tolerance));
            var collider = ceiling.GetComponent<BoxCollider>();
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.enabled && !collider.isTrigger, Is.True);
            AssertVector(new Vector3(4f + style.wallThickness * 2f, style.ceilingThickness, 10f), collider.size);
            Assert.That(ceiling.GetComponentsInChildren<Collider>(true).Count(item => item.enabled), Is.EqualTo(1));
            Assert.That(ceiling.gameObject.layer, Is.EqualTo(0));
            var underside = parent.transform.InverseTransformPoint(collider.transform.TransformPoint(
                collider.center - Vector3.up * collider.size.y * 0.5f));
            Assert.That(underside.y, Is.EqualTo(start.y + style.wallHeight).Within(Tolerance));
            Assert.That(VisualBounds(ceiling.gameObject).min.y,
                Is.EqualTo(parent.transform.position.y + start.y + style.wallHeight).Within(Tolerance));
            Assert.That(parent.GetComponentsInChildren<Collider>().Count(item => item.enabled), Is.EqualTo(4));
        }

        [Test]
        public void EmptyCeilingPieceFallsBackToFloorAndExplicitCeilingUsesItsOwnArt()
        {
            Assert.That(style.CeilingPiece, Is.SameAs(style.floor));
            style.ceiling = new BaekhoPrefabPiece();
            Assert.That(style.CeilingPiece, Is.SameAs(style.floor));
            var custom = Own(GameObject.CreatePrimitive(PrimitiveType.Sphere));
            custom.name = "Dedicated ceiling art";
            style.ceiling = Piece(custom);

            BaekhoDungeonGeometry.BuildSegment(new BaekhoCorridorSegment(Vector3.zero, Vector3.forward * 6f),
                4f, style, parent.transform);

            Assert.That(style.CeilingPiece, Is.SameAs(style.ceiling));
            Assert.That(parent.transform.Find("Ceiling").GetComponentInChildren<MeshFilter>().sharedMesh,
                Is.SameAs(custom.GetComponent<MeshFilter>().sharedMesh));
            Assert.That(BaekhoDungeonGenerator.ValidateStyle(style, out var error), Is.True, error);
        }

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(180f)]
        [TestCase(270f)]
        public void LightStationsKeepScaleAndFaceDownOrInwardAfterRotation(float yaw)
        {
            style.ceilingLightPrefab = Fixture("Ceiling fixture", true, new Vector3(1f, 0.2f, 0.6f));
            style.wallLightPrefab = Fixture("Wall fixture", false, new Vector3(0.5f, 0.8f, 0.2f));
            parent.transform.SetPositionAndRotation(new Vector3(17f, 3f, -11f), Quaternion.Euler(0f, 90f, 0f));
            var start = new Vector3(2f, 4f, -5f);
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var forward = rotation * Vector3.forward;
            var right = rotation * Vector3.right;

            BaekhoDungeonGeometry.BuildSegment(new BaekhoCorridorSegment(start, start + forward * 12f),
                4f, style, parent.transform);

            var ceilings = Fixtures("CeilingLight");
            var walls = Fixtures("WallLight");
            Assert.That(ceilings.Length, Is.EqualTo(2));
            Assert.That(walls.Length, Is.EqualTo(2));
            for (int i = 0; i < 2; i++)
            {
                var station = start + forward * (3f + i * 6f);
                AssertVector(station + Vector3.up * style.wallHeight, ceilings[i].localPosition);
                AssertVector(Vector3.down, ceilings[i].GetComponentInChildren<Light>().transform.forward);
                AssertVector(style.ceilingLightPrefab.transform.localScale, ceilings[i].localScale);
                float side = i == 0 ? -1f : 1f;
                AssertVector(station + right * side * 2f + Vector3.up * style.wallLightHeight, walls[i].localPosition);
                AssertVector(parent.transform.TransformDirection(-right * side), walls[i].GetComponentInChildren<Light>().transform.forward);
                AssertVector(style.wallLightPrefab.transform.localScale, walls[i].localScale);
            }
            Assert.That(parent.GetComponentsInChildren<Light>().All(light => light.shadows == LightShadows.None), Is.True);
            Assert.That(style.ceilingLightPrefab.GetComponentInChildren<Light>().shadows, Is.EqualTo(LightShadows.Soft));
            Assert.That(style.wallLightPrefab.GetComponentInChildren<Light>().shadows, Is.EqualTo(LightShadows.Soft));
            Assert.That(style.wallLightPrefab.GetComponentInChildren<Collider>().enabled, Is.True);
        }

        [Test]
        public void PlaceLightKeepsAuthoredScaleAndSourcePropertiesWhileDisablingCloneShadows()
        {
            var source = Fixture("Authored light", true, new Vector3(1f, 0.2f, 0.6f));
            source.transform.localPosition = new Vector3(31f, 8f, -17f);
            source.transform.localRotation = Quaternion.Euler(0f, 35f, 0f);
            var sourcePosition = source.transform.localPosition;
            var sourceRotation = source.transform.localRotation;
            var sourceScale = source.transform.localScale;
            var sourceLight = source.GetComponentInChildren<Light>();
            var rotation = Quaternion.Euler(0f, 90f, 0f);

            var clone = BaekhoDungeonGeometry.PlaceLight(source, parent.transform, new Vector3(2f, 4f, 6f), rotation, "Placed fixture");

            Assert.That(clone.name, Is.EqualTo("Placed fixture"));
            AssertVector(new Vector3(2f, 4f, 6f), clone.transform.localPosition);
            Assert.That(Quaternion.Angle(clone.transform.localRotation, rotation), Is.LessThan(Tolerance));
            AssertVector(sourceScale, clone.transform.localScale);
            var cloneLight = clone.GetComponentInChildren<Light>();
            Assert.That(cloneLight.shadows, Is.EqualTo(LightShadows.None));
            Assert.That(cloneLight.GetComponent<BaekhoGeneratedLightShadow>().authoredMode, Is.EqualTo(LightShadows.Soft));
            Assert.That(cloneLight.intensity, Is.EqualTo(sourceLight.intensity));
            Assert.That(cloneLight.range, Is.EqualTo(sourceLight.range));
            AssertVector(sourcePosition, source.transform.localPosition);
            AssertVector(sourceScale, source.transform.localScale);
            Assert.That(Quaternion.Angle(sourceRotation, source.transform.localRotation), Is.LessThan(Tolerance));
            Assert.That(sourceLight.shadows, Is.EqualTo(LightShadows.Soft));
            Assert.That(sourceLight.GetComponent<BaekhoGeneratedLightShadow>(), Is.Null);
            Assert.That(source.GetComponentInChildren<Collider>().enabled, Is.True);
            Assert.That(clone.GetComponentsInChildren<Collider>(true).All(item => !item.enabled), Is.True);
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void UnassignedLightKindsRemainOptional(bool ceilingAssigned, bool wallAssigned)
        {
            style.ceilingLightPrefab = ceilingAssigned ? Fixture("Ceiling fixture", true, new Vector3(1f, 0.2f, 0.6f)) : null;
            style.wallLightPrefab = wallAssigned ? Fixture("Wall fixture", false, new Vector3(0.5f, 0.8f, 0.2f)) : null;
            Assert.That(BaekhoDungeonGenerator.ValidateStyle(style, out var error), Is.True, error);

            Assert.DoesNotThrow(() => BaekhoDungeonGeometry.BuildSegment(
                new BaekhoCorridorSegment(Vector3.zero, Vector3.forward * 12f), 4f, style, parent.transform));

            Assert.That(Fixtures("CeilingLight").Length, Is.EqualTo(ceilingAssigned ? 2 : 0));
            Assert.That(Fixtures("WallLight").Length, Is.EqualTo(wallAssigned ? 2 : 0));
            Assert.That(parent.transform.Find("Ceiling"), Is.Not.Null);
        }

        [Test]
        public void FixturesThatWouldCrossShortSegmentEndsAreSkipped()
        {
            style.ceilingLightPrefab = Fixture("Long ceiling fixture", true, new Vector3(0.5f, 0.2f, 3f));
            style.wallLightPrefab = Fixture("Wide wall fixture", false, new Vector3(3f, 0.8f, 0.2f));

            BaekhoDungeonGeometry.BuildSegment(new BaekhoCorridorSegment(Vector3.zero, Vector3.forward),
                4f, style, parent.transform);

            Assert.That(parent.GetComponentsInChildren<Light>(), Is.Empty);
            Assert.That(parent.transform.Find("Ceiling"), Is.Not.Null);
            Assert.That(parent.GetComponentsInChildren<Collider>().Count(item => item.enabled), Is.EqualTo(4));
        }

        [Test]
        public void RegenerationAndClearRemoveThePreviousLightsAndCeiling()
        {
            style.ceilingLightPrefab = Fixture("Ceiling fixture", true, new Vector3(1f, 0.2f, 0.6f));
            style.wallLightPrefab = Fixture("Wall fixture", false, new Vector3(0.5f, 0.8f, 0.2f));
            var generator = parent.AddComponent<BaekhoDungeonGenerator>();
            generator.generateOnStart = false;
            var settings = Own(ScriptableObject.CreateInstance<BaekhoDungeonSettings>());
            settings.style = style;
            settings.mapCount = 2;
            settings.corridorWidth = 4f;
            settings.minimumMapGap = 12f;
            settings.maps = new[] { Module("Test map", false) };
            settings.cornerRoom = Module("Test corner", true);
            generator.settings = settings;

            Complete(generator.Generate(10));
            Assert.That(generator.LastResult.success, Is.True, generator.LastResult.error);
            var previousRoot = generator.CurrentRoot;
            var previousLight = previousRoot.GetComponentInChildren<Light>();
            Assert.That(previousLight, Is.Not.Null);
            Assert.That(previousRoot.GetComponentsInChildren<Transform>().Any(item => item.name == "Ceiling"), Is.True);

            Complete(generator.Generate(11));
            Assert.That(generator.LastResult.success, Is.True, generator.LastResult.error);
            Assert.That(previousRoot == null, Is.True);
            Assert.That(previousLight == null, Is.True);
            var currentLight = generator.CurrentRoot.GetComponentInChildren<Light>();
            Assert.That(currentLight, Is.Not.Null);
            generator.ClearGenerated();
            Assert.That(generator.CurrentRoot, Is.Null);
            Assert.That(currentLight == null, Is.True);
            Assert.That(parent.transform.childCount, Is.Zero);
        }

        [Test]
        public void ExistingCornerPrefabMatchesFourMetreCorridorsAndContainsBothFixtureKinds()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/ProceduralDungeon/Modules/TEMP_CornerRoom_6x6.prefab");
            Assert.That(prefab, Is.Not.Null);
            var instance = Own(Object.Instantiate(prefab));
            var module = instance.GetComponent<BaekhoMapModule>();
            Assert.That(module.isCornerRoom, Is.True);
            Assert.That(module.localBounds.size.x, Is.EqualTo(6f).Within(Tolerance));
            Assert.That(module.localBounds.size.z, Is.EqualTo(6f).Within(Tolerance));
            Assert.That(module.connections.Length, Is.EqualTo(2));
            Assert.That(module.connections.All(socket => socket.width == 4f && socket.height == 4f), Is.True);
            Assert.That(Mathf.Abs(Vector3.Dot(module.GetSocket(0).forward, module.GetSocket(1).forward)), Is.LessThan(Tolerance));
            var walls = instance.GetComponentsInChildren<BoxCollider>(true)
                .Where(box => box.enabled && box.name.StartsWith("wall_", StringComparison.Ordinal))
                .ToDictionary(box => box.name, box => box.bounds);
            Assert.That(walls["wall_NorthRight"].min.x - walls["wall_NorthLeft"].max.x,
                Is.EqualTo(4f).Within(Tolerance), "The north entrance must have a real 4m opening.");
            Assert.That(walls["wall_EastTop"].min.z - walls["wall_EastBottom"].max.z,
                Is.EqualTo(4f).Within(Tolerance), "The east entrance must have a real 4m opening.");
            Assert.That(walls.Values.All(bounds => Mathf.Abs(bounds.min.y) < Tolerance && Mathf.Abs(bounds.max.y - 4f) < Tolerance),
                Is.True, "All connecting room walls must reach the ceiling from floor height.");
            var roof = instance.GetComponentsInChildren<BoxCollider>(true).Single(box => box.enabled &&
                Mathf.Abs(box.bounds.size.x - 6f) < Tolerance && Mathf.Abs(box.bounds.size.z - 6f) < Tolerance &&
                Mathf.Abs(box.bounds.min.y - 4f) < Tolerance);
            Assert.That(roof.isTrigger, Is.False);
            var fixtures = instance.GetComponentsInChildren<Transform>(true);
            Assert.That(fixtures.Count(item => item.name.StartsWith("CeilingLight", StringComparison.Ordinal)), Is.EqualTo(1));
            Assert.That(fixtures.Count(item => item.name.StartsWith("WallLight", StringComparison.Ordinal)), Is.EqualTo(2));
            Assert.That(instance.GetComponentsInChildren<Light>(true).All(light => light.shadows == LightShadows.None), Is.True);
        }

        private GameObject Fixture(string name, bool ceiling, Vector3 size)
        {
            var root = Own(new GameObject(name));
            root.transform.localScale = new Vector3(1.5f, 1.25f, 0.75f);
            var art = GameObject.CreatePrimitive(PrimitiveType.Cube);
            art.name = "Fixture art";
            art.transform.SetParent(root.transform, false);
            art.transform.localScale = size;
            art.transform.localPosition = ceiling ? Vector3.down * size.y * 0.5f : Vector3.forward * size.z * 0.5f;
            var lightRoot = new GameObject("Authored spot");
            lightRoot.transform.SetParent(root.transform, false);
            lightRoot.transform.localRotation = ceiling ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
            var light = lightRoot.AddComponent<Light>();
            light.type = LightType.Spot;
            light.intensity = 3f;
            light.range = 6f;
            light.shadows = LightShadows.Soft;
            return root;
        }

        private BaekhoMapModule Module(string name, bool corner)
        {
            var root = Own(new GameObject(name));
            var module = root.AddComponent<BaekhoMapModule>();
            module.localBounds = new Bounds(new Vector3(0f, 2f, 0f), new Vector3(6f, 4f, 6f));
            module.isCornerRoom = corner;
            var sockets = new List<BaekhoMapConnection>();
            for (int i = 0; i < (corner ? 2 : 4); i++)
            {
                var child = new GameObject("Socket " + i);
                child.transform.SetParent(root.transform, false);
                child.transform.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
                child.transform.localPosition = child.transform.forward * 3f;
                var socket = child.AddComponent<BaekhoMapConnection>();
                socket.id = "Door_" + i;
                socket.width = 4f;
                socket.height = 4f;
                sockets.Add(socket);
            }
            module.connections = sockets.ToArray();
            return module;
        }

        private Transform[] Fixtures(string prefix) => Enumerable.Range(0, parent.transform.childCount)
            .Select(parent.transform.GetChild).Where(item => item.name.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(item => item.name, StringComparer.Ordinal).ToArray();

        private static BaekhoPrefabPiece Piece(GameObject source) => new BaekhoPrefabPiece
        {
            prefab = source, localBounds = new Bounds(Vector3.zero, Vector3.one), placement = BaekhoPiecePlacement.Stretch
        };

        private T Own<T>(T value) where T : Object
        {
            owned.Add(value);
            value.hideFlags = HideFlags.HideAndDontSave;
            return value;
        }

        private static void Complete(IEnumerator routine)
        {
            int iterations = 0;
            while (routine.MoveNext())
            {
                Assert.That(++iterations, Is.LessThan(1000));
                Assert.That(routine.Current, Is.Null);
            }
        }

        private static Bounds VisualBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static void AssertVector(Vector3 expected, Vector3 actual) =>
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(Tolerance), $"Expected {expected}, got {actual}.");
    }
}
