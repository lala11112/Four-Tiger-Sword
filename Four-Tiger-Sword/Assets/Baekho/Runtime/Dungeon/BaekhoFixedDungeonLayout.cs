using System;
using System.Collections.Generic;
using UnityEngine;

namespace StudioBaekho
{
    /// <summary>An authored sequence of rooms with straight connections between named doorways.</summary>
    [CreateAssetMenu(menuName = "Baekho/Fixed Dungeon Layout")]
    public sealed class BaekhoFixedDungeonLayout : ScriptableObject
    {
        [Serializable]
        public sealed class Room
        {
            public BaekhoMapModule module;
            public BaekhoRoomRole role;
            public Vector3 position;
            [Range(0, 3)] public int quarterTurns;
            public string entranceSocket;
            public string exitSocket;
        }

        public Room[] rooms = Array.Empty<Room>();
        private const float Tolerance = .001f;

        public bool TryCreatePlan(BaekhoDungeonSettings settings, int seed, out BaekhoDungeonPlan plan, out string error)
        {
            plan = null;
            error = null;
            if (settings == null || !Positive(settings.gridSize) || !Positive(settings.corridorWidth)
                || !Positive(settings.minimumDoorWidth) || settings.corridorWidth < settings.minimumDoorWidth
                || !Positive(settings.maximumConnectionLength))
                return Fail("Fixed layout requires valid dungeon settings.", out error);
            if (rooms == null || rooms.Length < 2 || rooms.Length != settings.mapCount)
                return Fail("Fixed layout room count must match Dungeon Settings.", out error);

            var candidate = new BaekhoDungeonPlan { seed = seed, startMapId = 0, endMapId = rooms.Length - 1 };
            var entrances = new int[rooms.Length];
            var exits = new int[rooms.Length];
            var checkedModules = new HashSet<BaekhoMapModule>();
            for (int i = 0; i < rooms.Length; i++)
            {
                var room = rooms[i];
                if (room == null || room.module == null || room.quarterTurns < 0 || room.quarterTurns > 3
                    || !Finite(room.position.x) || !Finite(room.position.y) || !Finite(room.position.z))
                    return Fail($"Invalid fixed room at index {i}.", out error);
                if (checkedModules.Add(room.module) && !BaekhoDungeonPlanner.ValidateFixedModule(room.module, settings, out error)) return false;
                bool first = i == 0, last = i == rooms.Length - 1;
                if (first ? room.role != BaekhoRoomRole.Start || room.module != settings.startRoom
                    : last ? room.role != BaekhoRoomRole.End || room.module != settings.endRoom
                    : room.role != BaekhoRoomRole.Normal && room.role != BaekhoRoomRole.Shop)
                    return Fail($"Unexpected role or module for fixed room {i}.", out error);
                if (room.role == BaekhoRoomRole.Shop)
                {
                    if (candidate.shopMapId >= 0 || room.module != settings.shopRoom)
                        return Fail($"Unexpected shop at fixed room {i}.", out error);
                    candidate.shopMapId = i;
                }

                entrances[i] = FindSocket(room.module, room.entranceSocket);
                exits[i] = FindSocket(room.module, room.exitSocket);
                if ((!first && !Usable(room.module, entrances[i], settings.minimumDoorWidth))
                    || (!last && !Usable(room.module, exits[i], settings.minimumDoorWidth))
                    || (!first && !last && entrances[i] == exits[i]))
                    return Fail($"Fixed room {i} needs distinct, usable doorways for its connections.", out error);

                var placed = new BaekhoPlacedModule
                {
                    id = i, prefab = room.module, position = room.position, quarterTurns = room.quarterTurns, role = room.role
                };
                foreach (var other in candidate.rooms)
                    if (OverlapsOnFloor(placed.WorldBounds, other.WorldBounds))
                        return Fail($"Fixed rooms {other.id} and {i} overlap.", out error);
                candidate.rooms.Add(placed);
            }
            if ((settings.shopRoom != null) != (candidate.shopMapId >= 0))
                return Fail("The configured shop is missing from the fixed layout.", out error);

            for (int i = 0; i < candidate.rooms.Count - 1; i++)
            {
                var from = candidate.rooms[i];
                var to = candidate.rooms[i + 1];
                var start = from.SocketPosition(exits[i]);
                var end = to.SocketPosition(entrances[i + 1]);
                var offset = end - start;
                if (offset.sqrMagnitude < Tolerance * Tolerance || offset.magnitude > settings.maximumConnectionLength
                    || Mathf.Abs(offset.y) > Tolerance || (Mathf.Abs(offset.x) > Tolerance && Mathf.Abs(offset.z) > Tolerance)
                    || Vector3.Dot(offset.normalized, from.SocketForward(exits[i])) < .9999f
                    || Vector3.Dot(offset.normalized, to.SocketForward(entrances[i + 1])) > -.9999f)
                    return Fail($"Fixed rooms {i} and {i + 1} need aligned, level doorways facing each other.", out error);

                // The corridor may cross its endpoint rooms' doorway recesses, but no other room.
                Vector3 corridorSize = new(Mathf.Abs(offset.x) + (Mathf.Abs(offset.x) < Tolerance ? settings.corridorWidth : 0f),
                    0f, Mathf.Abs(offset.z) + (Mathf.Abs(offset.z) < Tolerance ? settings.corridorWidth : 0f));
                var corridor = new Bounds((start + end) * .5f, corridorSize);
                foreach (var room in candidate.rooms)
                    if (room.id != i && room.id != i + 1 && OverlapsOnFloor(corridor, room.WorldBounds))
                        return Fail($"The corridor between fixed rooms {i} and {i + 1} crosses room {room.id}.", out error);

                var link = new BaekhoDungeonLink
                {
                    fromMapId = i, fromSocketIndex = exits[i], toMapId = i + 1,
                    toSocketIndex = entrances[i + 1], width = settings.corridorWidth
                };
                link.segments.Add(new BaekhoCorridorSegment(start, end));
                candidate.links.Add(link);
            }
            plan = candidate;
            return true;
        }

        private static int FindSocket(BaekhoMapModule module, string id)
        {
            if (!string.IsNullOrWhiteSpace(id))
                for (int i = 0; i < module.connections.Length; i++)
                    if (string.Equals(module.connections[i].id, id, StringComparison.Ordinal)) return i;
            return -1;
        }

        private static bool OverlapsOnFloor(Bounds left, Bounds right)
            => left.max.x > right.min.x + Tolerance && right.max.x > left.min.x + Tolerance
                && left.max.z > right.min.z + Tolerance && right.max.z > left.min.z + Tolerance;
        private static bool Usable(BaekhoMapModule module, int socket, float width)
            => socket >= 0 && module.connections[socket].width + Tolerance >= width;
        private static bool Positive(float value) => Finite(value) && value > 0f;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Fail(string message, out string error) { error = message; return false; }
    }
}
