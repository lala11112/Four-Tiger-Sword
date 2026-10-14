using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StudioBaekho.Tests
{
    public sealed class BaekhoDungeonPlannerTests
    {
        private const float Tolerance = 0.001f;
        private readonly List<Object> owned = new List<Object>();
        private BaekhoDungeonSettings settings;

        [SetUp]
        public void SetUp()
        {
            settings = Own(ScriptableObject.CreateInstance<BaekhoDungeonSettings>());
            settings.style = Own(ScriptableObject.CreateInstance<BaekhoCorridorStyle>());
            settings.randomizeSeed = false;
            settings.mapCount = 6;
            settings.maps = new[] { CreateRoom("Test room", 2f) };
            settings.cornerRoom = CreateCorner();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i]) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [Test]
        public void SocketMetadataIsRelativeToModuleRoot()
        {
            var room = settings.maps[0];
            room.transform.SetPositionAndRotation(new Vector3(17, 4, -9), Quaternion.Euler(0, 90, 0));
            var socket = room.GetSocket(0);
            AssertVector(new Vector3(0, 0, 6), socket.position);
            AssertVector(Vector3.forward, socket.forward);
            Assert.That(socket.width, Is.EqualTo(2));
            Assert.That(socket.height, Is.EqualTo(3));
        }

        [Test]
        public void PlacedBoundsRespectOffsetPivotAndQuarterTurn()
        {
            var room = settings.maps[0];
            room.localBounds = new Bounds(new Vector3(2, 3, -1), new Vector3(8, 6, 12));
            var placed = new BaekhoPlacedModule
            {
                prefab = room, position = new Vector3(10, 4, 20), quarterTurns = 1
            };
            AssertVector(new Vector3(9, 7, 18), placed.WorldBounds.center);
            AssertVector(new Vector3(12, 6, 8), placed.WorldBounds.size);
        }

        [TestCase(1)]
        [TestCase(12345)]
        [TestCase(9277)]
        public void RequestedRoomsFormConnectedBranchingTree(int seed)
        {
            var plan = Plan(seed);
            Assert.That(plan.seed, Is.EqualTo(seed));
            Assert.That(plan.rooms.Count, Is.EqualTo(settings.mapCount));
            Assert.That(plan.links.Count, Is.EqualTo(settings.mapCount - 1));
            Assert.That(plan.rooms.Select(room => room.id).Distinct().Count(), Is.EqualTo(settings.mapCount));
            Assert.That(plan.rooms.All(room => !room.prefab.isCornerRoom), Is.True);
            var allIds = plan.rooms.Select(room => room.id)
                .Concat(plan.links.SelectMany(link => link.corners).Select(corner => corner.id)).ToArray();
            Assert.That(allIds.Distinct().Count(), Is.EqualTo(allIds.Length),
                "Every placed map and corner room must have a unique instance ID.");

            var degree = plan.rooms.ToDictionary(room => room.id, room => 0);
            var adjacency = plan.rooms.ToDictionary(room => room.id, room => new List<int>());
            var usedSockets = new HashSet<string>();
            foreach (var link in plan.links)
            {
                Assert.That(degree.ContainsKey(link.fromMapId), Is.True);
                Assert.That(degree.ContainsKey(link.toMapId), Is.True);
                Assert.That(link.fromMapId, Is.Not.EqualTo(link.toMapId));
                Assert.That(usedSockets.Add(link.fromMapId + ":" + link.fromSocketIndex), Is.True,
                    "A doorway cannot connect to two corridors.");
                Assert.That(usedSockets.Add(link.toMapId + ":" + link.toSocketIndex), Is.True,
                    "Repeated prefab instances must have independent doorway identities.");
                degree[link.fromMapId]++;
                degree[link.toMapId]++;
                adjacency[link.fromMapId].Add(link.toMapId);
                adjacency[link.toMapId].Add(link.fromMapId);
            }
            var visited = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(plan.rooms[0].id);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (!visited.Add(current)) continue;
                foreach (int neighbor in adjacency[current]) queue.Enqueue(neighbor);
            }
            Assert.That(visited.Count, Is.EqualTo(settings.mapCount), "Every map must be reachable.");
            Assert.That(degree.Values.Max(), Is.GreaterThanOrEqualTo(3), "Four or more maps need a branch.");
        }

        [Test]
        public void SameSeedReproducesLayoutWithoutChangingUnityRandomState()
        {
            var saved = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(2468);
                var initial = UnityEngine.Random.state;
                var expected = new[] { UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value };
                UnityEngine.Random.state = initial;
                string first = Fingerprint(Plan(93201));
                var actual = new[] { UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value };
                string second = Fingerprint(Plan(93201));
                Assert.That(second, Is.EqualTo(first));
                Assert.That(actual, Is.EqualTo(expected),
                    "Generation must not perturb unrelated gameplay random numbers.");
            }
            finally { UnityEngine.Random.state = saved; }
        }

        [Test]
        public void OneTemplateCanSupplyEveryRequestedMap()
        {
            var plan = Plan(42);
            Assert.That(plan.rooms.Count, Is.EqualTo(6));
            Assert.That(plan.rooms.All(room => room.prefab == settings.maps[0]), Is.True);
            Assert.That(plan.rooms.Select(room => room.position).Distinct().Count(), Is.EqualTo(6));
        }

        [Test]
        public void DedicatedEndpointsAppearOnceAndAreExcludedFromOrdinarySelections()
        {
            var ordinary = settings.maps[0];
            settings.startRoom = CreateRoom("Dedicated start", 2f);
            settings.endRoom = CreateRoom("Dedicated end", 2f);
            settings.maps = new[] { settings.startRoom, ordinary, settings.endRoom };
            var plan = Plan(12345);
            Assert.That(plan.rooms.Count, Is.EqualTo(6));
            Assert.That(plan.rooms.Count(room => room.prefab == settings.startRoom), Is.EqualTo(1));
            Assert.That(plan.rooms.Count(room => room.prefab == settings.endRoom), Is.EqualTo(1));
            Assert.That(plan.rooms.Where(room => room.role == BaekhoRoomRole.Normal)
                .All(room => room.prefab == ordinary), Is.True);
            Assert.That(plan.rooms.Count(room => (room.role & BaekhoRoomRole.Start) != 0), Is.EqualTo(1));
            Assert.That(plan.rooms.Count(room => (room.role & BaekhoRoomRole.End) != 0), Is.EqualTo(1));
            Assert.That(plan.rooms.Single(room => room.id == plan.startMapId).prefab, Is.SameAs(settings.startRoom));
            Assert.That(plan.rooms.Single(room => room.id == plan.endMapId).prefab, Is.SameAs(settings.endRoom));
            Assert.That(plan.startMapId, Is.Not.EqualTo(plan.endMapId));
            Assert.That(plan.bossMapId, Is.EqualTo(-1));
            Assert.That(Degree(plan, plan.endMapId), Is.EqualTo(1));
        }

        [TestCase(4)]
        [TestCase(6)]
        public void OneDoorStartConnectsToAnOrdinaryBranchHub(int mapCount)
        {
            settings.mapCount = mapCount;
            settings.startRoom = CreateRoom("One door start", 2f);
            settings.startRoom.connections = settings.startRoom.connections.Take(1).ToArray();
            var plan = Plan(9277);
            Assert.That(Degree(plan, plan.startMapId), Is.EqualTo(1));
            var entranceLink = plan.links.Single(link => link.fromMapId == plan.startMapId || link.toMapId == plan.startMapId);
            int neighbor = entranceLink.fromMapId == plan.startMapId ? entranceLink.toMapId : entranceLink.fromMapId;
            Assert.That(plan.rooms.Single(room => room.id == neighbor).role, Is.EqualTo(BaekhoRoomRole.Normal));
            Assert.That(Degree(plan, neighbor), Is.GreaterThanOrEqualTo(3));
            Assert.That(Degree(plan, plan.endMapId), Is.EqualTo(1));
        }

        [TestCase(5)]
        [TestCase(6)]
        public void BossIsUniqueAndMandatoryImmediatelyBeforeTheSeparateEnd(int mapCount)
        {
            settings.mapCount = mapCount;
            var ordinary = settings.maps[0];
            settings.startRoom = CreateRoom("Start", 2f);
            settings.startRoom.connections = settings.startRoom.connections.Take(1).ToArray();
            settings.endRoom = CreateRoom("End", 2f);
            settings.bossRoom = CreateRoom("Boss", 2f);
            settings.bossRoom.connections = settings.bossRoom.connections.Take(2).ToArray();
            settings.maps = new[] { ordinary, settings.startRoom, settings.endRoom, settings.bossRoom };
            var plan = Plan(42);
            Assert.That(plan.rooms.Count, Is.EqualTo(mapCount));
            Assert.That(plan.links.Count, Is.EqualTo(mapCount - 1));
            Assert.That(Degree(plan, plan.startMapId), Is.EqualTo(1));
            Assert.That(plan.rooms.Count(room => room.prefab == settings.bossRoom), Is.EqualTo(1));
            Assert.That(plan.rooms.Single(room => room.id == plan.bossMapId).role, Is.EqualTo(BaekhoRoomRole.Boss));
            Assert.That(plan.rooms.Where(room => room.role == BaekhoRoomRole.Normal)
                .All(room => room.prefab == ordinary), Is.True);
            Assert.That(Degree(plan, plan.bossMapId), Is.EqualTo(2));
            Assert.That(Degree(plan, plan.endMapId), Is.EqualTo(1));
            var exitLink = plan.links.Single(link => link.fromMapId == plan.endMapId || link.toMapId == plan.endMapId);
            int beforeEnd = exitLink.fromMapId == plan.endMapId ? exitLink.toMapId : exitLink.fromMapId;
            Assert.That(beforeEnd, Is.EqualTo(plan.bossMapId));
            Assert.That(Reachable(plan, plan.startMapId, plan.endMapId, -1), Is.True);
            Assert.That(Reachable(plan, plan.startMapId, plan.endMapId, plan.bossMapId), Is.False,
                "There must be no route to the end that bypasses the boss.");
            Assert.That(plan.rooms.Where(room => room.role == BaekhoRoomRole.Normal)
                .Any(room => Degree(plan, room.id) >= 3), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BossRequiresTwoUsableDoorways(bool narrowSecondDoor)
        {
            settings.bossRoom = CreateRoom("Invalid boss", 2f);
            settings.bossRoom.connections = settings.bossRoom.connections.Take(narrowSecondDoor ? 2 : 1).ToArray();
            if (narrowSecondDoor) settings.bossRoom.connections[1].width = 0.5f;
            Assert.That(BaekhoDungeonPlanner.TryPlan(settings, 62, out var plan, out var error), Is.False);
            Assert.That(plan, Is.Null);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void TooFewRoomsCannotSilentlyMergeDedicatedRoles()
        {
            settings.mapCount = 2;
            settings.bossRoom = CreateRoom("Boss requiring separate end", 2f);
            Assert.That(BaekhoDungeonPlanner.TryPlan(settings, 22, out var plan, out var error), Is.False);
            Assert.That(plan, Is.Null);
            Assert.That(error, Is.Not.Null.And.Not.Empty);

            settings.bossRoom = null;
            settings.mapCount = 1;
            settings.startRoom = CreateRoom("Separate start", 2f);
            settings.endRoom = CreateRoom("Separate end", 2f);
            Assert.That(BaekhoDungeonPlanner.TryPlan(settings, 22, out plan, out error), Is.False);
            Assert.That(plan, Is.Null);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ThreeExplicitRoleRoomsNeedNoOrdinaryPool()
        {
            settings.mapCount = 3;
            settings.maps = Array.Empty<BaekhoMapModule>();
            settings.startRoom = CreateRoom("Only start", 2f);
            settings.startRoom.connections = settings.startRoom.connections.Take(1).ToArray();
            settings.endRoom = CreateRoom("Only end", 2f);
            settings.endRoom.connections = settings.endRoom.connections.Take(1).ToArray();
            settings.bossRoom = CreateRoom("Only boss", 2f);
            settings.bossRoom.connections = settings.bossRoom.connections.Take(2).ToArray();
            var plan = Plan(17);
            Assert.That(plan.rooms.Count, Is.EqualTo(3));
            Assert.That(plan.rooms.Count(room => room.role == BaekhoRoomRole.Normal), Is.Zero);
            Assert.That(Degree(plan, plan.startMapId), Is.EqualTo(1));
            Assert.That(Degree(plan, plan.bossMapId), Is.EqualTo(2));
            Assert.That(Degree(plan, plan.endMapId), Is.EqualTo(1));
        }

        [Test]
        public void ElevatedDoorwaysMoveWholeRoomsAndKeepCorridorsFlat()
        {
            var room = settings.maps[0];
            room.connections[0].transform.localPosition += Vector3.up * 4f;
            room.connections[2].transform.localPosition += Vector3.up * 4f;
            room.localBounds = new Bounds(new Vector3(0, 3.5f, 0), new Vector3(12, 7, 12));
            bool connectedDifferentLocalHeights = false;
            for (int seed = 0; seed < 8 && !connectedDifferentLocalHeights; seed++)
            {
                var plan = Plan(seed);
                foreach (var link in plan.links)
                {
                    var from = plan.rooms.Single(map => map.id == link.fromMapId);
                    var to = plan.rooms.Single(map => map.id == link.toMapId);
                    float floorY = from.SocketPosition(link.fromSocketIndex).y;
                    Assert.That(to.SocketPosition(link.toSocketIndex).y, Is.EqualTo(floorY).Within(Tolerance));
                    foreach (var segment in link.segments)
                    {
                        Assert.That(segment.start.y, Is.EqualTo(floorY).Within(Tolerance));
                        Assert.That(segment.end.y, Is.EqualTo(floorY).Within(Tolerance));
                    }
                    foreach (var corner in link.corners)
                        for (int i = 0; i < corner.prefab.connections.Length; i++)
                            Assert.That(corner.SocketPosition(i).y, Is.EqualTo(floorY).Within(Tolerance));
                    float localDifference = from.prefab.GetSocket(link.fromSocketIndex).position.y -
                                            to.prefab.GetSocket(link.toSocketIndex).position.y;
                    if (Mathf.Abs(localDifference) > Tolerance)
                    {
                        connectedDifferentLocalHeights = true;
                        Assert.That(Mathf.Abs(to.position.y - from.position.y), Is.EqualTo(4f).Within(Tolerance));
                    }
                }
            }
            Assert.That(connectedDifferentLocalHeights, Is.True, "Fixture must exercise a 4m floor offset.");
        }

        [Test]
        public void OneMetreDoorwaysAreAcceptedAndLimitCorridorWidth()
        {
            foreach (var socket in settings.maps[0].connections) socket.width = 1f;
            var plan = Plan(62);
            Assert.That(plan.links.Count, Is.GreaterThan(0));
            Assert.That(plan.links.All(link => Mathf.Abs(link.width - 1f) < Tolerance), Is.True);
        }

        [Test]
        public void HalfMetreDoorwaysAreRejectedWithoutWideningThem()
        {
            settings.mapCount = 2;
            foreach (var socket in settings.maps[0].connections) socket.width = 0.5f;
            Assert.That(BaekhoDungeonPlanner.TryPlan(settings, 62, out var plan, out var error), Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
            Assert.That(settings.maps[0].connections.All(socket => socket.width == 0.5f), Is.True);
        }

        [TestCase(1)]
        [TestCase(12345)]
        [TestCase(9277)]
        public void RoomsCornersAndCorridorWallsDoNotOverlapInXZ(int seed)
        {
            var plan = Plan(seed);
            var modules = new List<BaekhoPlacedModule>(plan.rooms);
            modules.AddRange(plan.links.SelectMany(link => link.corners));
            for (int a = 0; a < modules.Count; a++)
                for (int b = a + 1; b < modules.Count; b++)
                    AssertNoInteriorOverlap(modules[a].WorldBounds, modules[b].WorldBounds,
                        "Room footprints overlap, including corners.");

            var corridors = new List<Bounds>();
            foreach (var link in plan.links)
                foreach (var segment in link.segments)
                {
                    var bounds = CorridorBounds(segment, link.width + settings.style.wallThickness * 2f);
                    foreach (var module in modules)
                        AssertNoInteriorOverlap(bounds, module.WorldBounds,
                            "A corridor wall enters a room away from its doorway boundary.");
                    foreach (var previous in corridors)
                        AssertNoInteriorOverlap(bounds, previous, "Corridors cross or overlap.");
                    corridors.Add(bounds);
                }

            for (int a = 0; a < plan.rooms.Count; a++)
                for (int b = a + 1; b < plan.rooms.Count; b++)
                {
                    var first = plan.rooms[a].WorldBounds;
                    var second = plan.rooms[b].WorldBounds;
                    first.Expand(new Vector3(settings.minimumMapGap, 0, settings.minimumMapGap));
                    second.Expand(new Vector3(settings.minimumMapGap, 0, settings.minimumMapGap));
                    AssertNoInteriorOverlap(first, second, "Map spacing must survive every rotation.");
                }
        }

        [Test]
        public void EveryBendIsOccupiedByAReplaceableCornerRoom()
        {
            int totalCorners = 0;
            for (int seed = 0; seed < 8 && totalCorners == 0; seed++)
            {
                var plan = Plan(seed);
                Assert.That(plan.rooms.Count, Is.EqualTo(settings.mapCount), "Corner rooms are extra.");
                totalCorners += AssertCornerChains(plan);
            }
            Assert.That(totalCorners, Is.GreaterThan(0), "Seeded fixtures must exercise actual corner placement.");
        }

        [Test]
        public void LargerCornerWithOffsetPivotUsesItsAuthoredSocketsAndFootprint()
        {
            var replacement = settings.cornerRoom;
            replacement.localBounds = new Bounds(new Vector3(1, 1.4f, -1), new Vector3(10, 3.2f, 8));
            replacement.connections[0].transform.localPosition = new Vector3(1, 0, 3);
            replacement.connections[1].transform.localPosition = new Vector3(6, 0, -1);
            int totalCorners = 0;
            for (int seed = 0; seed < 8 && totalCorners == 0; seed++)
            {
                var plan = Plan(seed);
                Assert.That(plan.rooms.Count, Is.EqualTo(settings.mapCount));
                totalCorners += AssertCornerChains(plan);
                var modules = plan.rooms.Concat(plan.links.SelectMany(link => link.corners)).ToList();
                for (int a = 0; a < modules.Count; a++)
                    for (int b = a + 1; b < modules.Count; b++)
                        AssertNoInteriorOverlap(modules[a].WorldBounds, modules[b].WorldBounds,
                            "The replacement corner's full size and offset must be reserved.");
                foreach (var link in plan.links)
                    foreach (var segment in link.segments)
                    {
                        var corridor = CorridorBounds(segment, link.width + 2f * settings.style.wallThickness);
                        foreach (var module in modules)
                            AssertNoInteriorOverlap(corridor, module.WorldBounds,
                                "A replacement corner may not intrude into a corridor.");
                    }
            }
            Assert.That(totalCorners, Is.GreaterThan(0), "The swapped corner must actually appear in the plan.");
        }

        [Test]
        public void SharedPlanningBudgetStopsAllRetriesWithoutReturningAPartialLayout()
        {
            settings.maxSearchNodes = 1;
            settings.mapCount = 6;
            Assert.That(BaekhoDungeonPlanner.TryPlan(settings, 12345, out var plan, out var error), Is.False);
            Assert.That(plan, Is.Null);
            Assert.That(error, Does.Contain("budget").IgnoreCase);
            Assert.That(error, Does.Contain("maxSearchNodes=1"));
        }

        [Test]
        public void OffGridSocketProducesActionableFailure()
        {
            settings.maps[0].connections[0].transform.localPosition += Vector3.right * 0.13f;
            Assert.That(BaekhoDungeonPlanner.TryPlan(settings, 22, out var plan, out var error), Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void InsufficientConnectionRangeFailsWithinConfiguredBudget()
        {
            settings.mapCount = 2;
            settings.maximumConnectionLength = 1f;
            settings.placementAttempts = 2;
            settings.layoutAttempts = 2;
            settings.maxSearchNodes = 100;
            Assert.That(BaekhoDungeonPlanner.TryPlan(settings, 90, out var plan, out var error), Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void SingleMapNeedsNoLinksOrCornerInstances()
        {
            settings.mapCount = 1;
            var plan = Plan(17);
            Assert.That(plan.rooms.Count, Is.EqualTo(1));
            Assert.That(plan.links, Is.Empty);
            Assert.That(plan.startMapId, Is.EqualTo(plan.endMapId));
            Assert.That(plan.rooms[0].role, Is.EqualTo(BaekhoRoomRole.Start | BaekhoRoomRole.End));
            Assert.That(plan.bossMapId, Is.EqualTo(-1));
        }

        private static int Degree(BaekhoDungeonPlan plan, int roomId)
            => plan.links.Count(link => link.fromMapId == roomId || link.toMapId == roomId);

        private static bool Reachable(BaekhoDungeonPlan plan, int start, int target, int excluded)
        {
            var seen = new HashSet<int> { excluded };
            var pending = new Queue<int>();
            pending.Enqueue(start);
            while (pending.Count > 0)
            {
                int current = pending.Dequeue();
                if (!seen.Add(current)) continue;
                if (current == target) return true;
                foreach (var link in plan.links)
                {
                    if (link.fromMapId == current) pending.Enqueue(link.toMapId);
                    else if (link.toMapId == current) pending.Enqueue(link.fromMapId);
                }
            }
            return false;
        }

        private BaekhoDungeonPlan Plan(int seed)
        {
            bool success = BaekhoDungeonPlanner.TryPlan(settings, seed, out var plan, out var error);
            Assert.That(success, Is.True, "Seed " + seed + ": " + error);
            Assert.That(plan, Is.Not.Null);
            return plan;
        }

        private int AssertCornerChains(BaekhoDungeonPlan plan)
        {
            int count = 0;
            foreach (var link in plan.links)
            {
                var from = plan.rooms.Single(map => map.id == link.fromMapId);
                var to = plan.rooms.Single(map => map.id == link.toMapId);
                Assert.That(link.segments.Count, Is.EqualTo(link.corners.Count + 1));
                AssertVector(from.SocketPosition(link.fromSocketIndex), link.segments[0].start);
                AssertVector(to.SocketPosition(link.toSocketIndex), link.segments.Last().end);
                foreach (var segment in link.segments)
                {
                    var delta = segment.end - segment.start;
                    Assert.That(delta.sqrMagnitude, Is.GreaterThan(Tolerance));
                    Assert.That(Mathf.Min(Mathf.Abs(delta.x), Mathf.Abs(delta.z)), Is.LessThan(Tolerance),
                        "Corridors between corner rooms must remain axis aligned.");
                }
                for (int i = 0; i < link.corners.Count; i++)
                {
                    var corner = link.corners[i];
                    Assert.That(corner.prefab, Is.SameAs(settings.cornerRoom));
                    var incoming = link.segments[i];
                    var outgoing = link.segments[i + 1];
                    bool forwardSockets = Close(incoming.end, corner.SocketPosition(0)) &&
                                          Close(outgoing.start, corner.SocketPosition(1));
                    bool reverseSockets = Close(incoming.end, corner.SocketPosition(1)) &&
                                          Close(outgoing.start, corner.SocketPosition(0));
                    Assert.That(forwardSockets || reverseSockets, Is.True,
                        "The corner must join both straight segments at its actual authored sockets.");
                    Assert.That(Mathf.Abs(Vector3.Dot((incoming.end - incoming.start).normalized,
                        (outgoing.end - outgoing.start).normalized)), Is.LessThan(Tolerance));
                    count++;
                }
            }
            return count;
        }

        private BaekhoMapModule CreateRoom(string name, float width)
        {
            var root = Own(new GameObject(name));
            var module = root.AddComponent<BaekhoMapModule>();
            module.localBounds = new Bounds(new Vector3(0, 1.5f, 0), new Vector3(12, 3, 12));
            module.connections = new[]
            {
                Socket(root, "N", new Vector3(0, 0, 6), Vector3.forward, width),
                Socket(root, "E", new Vector3(6, 0, 0), Vector3.right, width),
                Socket(root, "S", new Vector3(0, 0, -6), Vector3.back, width),
                Socket(root, "W", new Vector3(-6, 0, 0), Vector3.left, width)
            };
            return module;
        }

        private BaekhoMapModule CreateCorner()
        {
            var root = Own(new GameObject("Test replaceable corner"));
            var module = root.AddComponent<BaekhoMapModule>();
            module.isCornerRoom = true;
            module.localBounds = new Bounds(new Vector3(0, 1.5f, 0), new Vector3(6, 3, 6));
            module.connections = new[]
            {
                Socket(root, "N", new Vector3(0, 0, 3), Vector3.forward, 2f),
                Socket(root, "E", new Vector3(3, 0, 0), Vector3.right, 2f)
            };
            return module;
        }

        private static BaekhoMapConnection Socket(GameObject parent, string id, Vector3 position,
            Vector3 forward, float width)
        {
            var root = new GameObject(id);
            root.transform.SetParent(parent.transform, false);
            root.transform.localPosition = position;
            root.transform.localRotation = Quaternion.LookRotation(forward, Vector3.up);
            var socket = root.AddComponent<BaekhoMapConnection>();
            socket.id = id;
            socket.width = width;
            socket.height = 3f;
            return socket;
        }

        private T Own<T>(T value) where T : Object
        {
            owned.Add(value);
            value.hideFlags = HideFlags.HideAndDontSave;
            return value;
        }

        private static Bounds CorridorBounds(BaekhoCorridorSegment segment, float outerWidth)
        {
            var delta = segment.end - segment.start;
            return new Bounds((segment.start + segment.end) / 2f,
                Mathf.Abs(delta.x) > Mathf.Abs(delta.z)
                    ? new Vector3(Mathf.Abs(delta.x), 1, outerWidth)
                    : new Vector3(outerWidth, 1, Mathf.Abs(delta.z)));
        }

        private static void AssertNoInteriorOverlap(Bounds first, Bounds second, string message)
        {
            float x = Mathf.Min(first.max.x, second.max.x) - Mathf.Max(first.min.x, second.min.x);
            float z = Mathf.Min(first.max.z, second.max.z) - Mathf.Max(first.min.z, second.min.z);
            Assert.That(x <= Tolerance || z <= Tolerance, Is.True,
                message + " First: " + first + "; second: " + second);
        }

        private static bool Close(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < Tolerance * Tolerance;

        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(Tolerance),
                "Expected " + expected + ", actual " + actual);
        }

        private static string Fingerprint(BaekhoDungeonPlan plan)
        {
            var result = new StringBuilder();
            Action<Vector3> appendVector = v => result.AppendFormat(CultureInfo.InvariantCulture,
                "({0:R},{1:R},{2:R})", v.x, v.y, v.z);
            Action<BaekhoPlacedModule> appendModule = module =>
            {
                result.Append(module.id).Append('/').Append(module.prefab.name).Append('/').Append(module.quarterTurns)
                    .Append('/').Append(module.role);
                appendVector(module.position);
            };
            result.Append(plan.seed).Append('/').Append(plan.startMapId).Append('/').Append(plan.endMapId).Append('/').Append(plan.bossMapId);
            foreach (var room in plan.rooms) appendModule(room);
            foreach (var link in plan.links)
            {
                result.Append('|').Append(link.fromMapId).Append(':').Append(link.fromSocketIndex)
                    .Append('>').Append(link.toMapId).Append(':').Append(link.toSocketIndex)
                    .Append('/').Append(link.width.ToString("R", CultureInfo.InvariantCulture));
                foreach (var segment in link.segments) { appendVector(segment.start); appendVector(segment.end); }
                foreach (var corner in link.corners) appendModule(corner);
            }
            return result.ToString();
        }
    }
}
