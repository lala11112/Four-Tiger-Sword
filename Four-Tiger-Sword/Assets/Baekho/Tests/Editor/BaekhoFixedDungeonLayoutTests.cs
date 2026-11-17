using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace StudioBaekho.Tests
{
    public sealed class BaekhoFixedDungeonLayoutTests
    {
        private readonly List<Object> _owned = new();
        private BaekhoDungeonSettings _settings;
        private BaekhoFixedDungeonLayout _layout;

        [SetUp]
        public void SetUp()
        {
            var start = Room("Start");
            var first = Room("Practice");
            var shop = Room("Shop");
            var second = Room("Combat");
            var end = Room("Clear");
            _settings = Own(ScriptableObject.CreateInstance<BaekhoDungeonSettings>());
            _settings.startRoom = start;
            _settings.shopRoom = shop;
            _settings.endRoom = end;
            _settings.mapCount = 5;
            _layout = Own(ScriptableObject.CreateInstance<BaekhoFixedDungeonLayout>());
            _layout.rooms = new[]
            {
                At(start, BaekhoRoomRole.Start, 0, null, "North"),
                At(first, BaekhoRoomRole.Normal, 14, "South", "North"),
                At(shop, BaekhoRoomRole.Shop, 28, "South", "North"),
                At(second, BaekhoRoomRole.Normal, 42, "South", "North"),
                At(end, BaekhoRoomRole.End, 56, "South", null)
            };
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _owned.Count - 1; i >= 0; i--) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
        }

        [TestCase(0)]
        [TestCase(431)]
        public void FixedPathUsesShopAsPassageWithStableRoomIds(int seed)
        {
            Assert.That(_layout.TryCreatePlan(_settings, seed, out var plan, out var error), Is.True, error);
            Assert.That(plan.seed, Is.EqualTo(seed));
            Assert.That(plan.startMapId, Is.Zero);
            Assert.That(plan.shopMapId, Is.EqualTo(2));
            Assert.That(plan.bossMapId, Is.EqualTo(-1));
            Assert.That(plan.endMapId, Is.EqualTo(4));
            Assert.That(plan.links.Select(link => (link.fromMapId, link.toMapId)),
                Is.EqualTo(new[] { (0, 1), (1, 2), (2, 3), (3, 4) }));
            foreach (var link in plan.links)
            {
                Assert.That(link.segments, Has.Count.EqualTo(1));
                Assert.That(link.corners, Is.Empty);
                Assert.That(link.segments[0].end - link.segments[0].start, Is.EqualTo(Vector3.forward * 4f));
            }
        }

        [Test]
        public void ShopCannotReuseTheEntranceAsItsExit()
        {
            _layout.rooms[2].exitSocket = _layout.rooms[2].entranceSocket;
            Assert.That(_layout.TryCreatePlan(_settings, 0, out var plan, out var error), Is.False);
            Assert.That(plan, Is.Null);
            Assert.That(error, Does.Contain("distinct"));
        }

        [Test]
        public void MisalignedSocketsDoNotProduceInvalidCorridorGeometry()
        {
            _layout.rooms[3].position += Vector3.right * 2f;
            Assert.That(_layout.TryCreatePlan(_settings, 0, out var plan, out var error), Is.False);
            Assert.That(plan, Is.Null);
            Assert.That(error, Does.Contain("aligned"));
        }

        private T Own<T>(T value) where T : Object { _owned.Add(value); return value; }

        private BaekhoMapModule Room(string name)
        {
            var module = Own(new GameObject(name)).AddComponent<BaekhoMapModule>();
            module.localBounds = new Bounds(new Vector3(0, 2, 0), new Vector3(10, 4, 10));
            module.connections = new[] { Socket("South", -5f, 180), Socket("North", 5f, 0) };
            return module;

            BaekhoMapConnection Socket(string id, float z, int angle)
            {
                var socket = new GameObject(id).AddComponent<BaekhoMapConnection>();
                socket.transform.SetParent(module.transform, false);
                socket.transform.SetLocalPositionAndRotation(new Vector3(0, 0, z), Quaternion.Euler(0, angle, 0));
                socket.id = id;
                socket.width = 2f;
                socket.height = 3f;
                return socket;
            }
        }

        private static BaekhoFixedDungeonLayout.Room At(BaekhoMapModule module, BaekhoRoomRole role,
            float z, string entrance, string exit) => new()
        {
            module = module, role = role, position = new Vector3(0, 0, z), entranceSocket = entrance, exitSocket = exit
        };
    }
}
