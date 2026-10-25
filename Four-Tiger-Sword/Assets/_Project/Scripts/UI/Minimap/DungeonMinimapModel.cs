using System;
using System.Collections.Generic;
using StudioBaekho;
using UnityEngine;

public enum DungeonMinimapAreaKind { Room, Corridor, Corner }

/// <summary>Stage-local XZ geometry and exploration, independent of encounters and the gameplay camera.</summary>
public sealed class DungeonMinimapModel
{
    public sealed class Area
    {
        public DungeonMinimapAreaKind Kind { get; }
        public int RoomId { get; }
        public Vector2 Center { get; }
        public Vector2 Size { get; }
        public float RotationDegrees { get; }
        public bool Visited { get; private set; }

        private readonly Vector2 _right;
        private readonly Vector2 _up;

        internal Area(DungeonMinimapAreaKind kind, int roomId, Vector2 center, Vector2 size, float rotation)
        {
            Kind = kind;
            RoomId = roomId;
            Center = center;
            Size = size;
            RotationDegrees = rotation;
            float radians = rotation * Mathf.Deg2Rad;
            _right = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            _up = new Vector2(-_right.y, _right.x);
        }

        internal Vector2 ToLocal(Vector2 point)
        {
            var offset = point - Center;
            return new Vector2(Vector2.Dot(offset, _right), Vector2.Dot(offset, _up));
        }

        internal bool Visit()
        {
            if (Visited) return false;
            Visited = true;
            return true;
        }
    }

    public sealed class Stub
    {
        public int OwnerAreaIndex { get; }
        public Vector2 Center { get; }
        public Vector2 Size { get; }
        public float RotationDegrees { get; }

        internal Stub(int ownerAreaIndex, Vector2 center, Vector2 size, float rotation)
        {
            OwnerAreaIndex = ownerAreaIndex;
            Center = center;
            Size = size;
            RotationDegrees = rotation;
        }
    }

    private const float CurrentAreaMargin = 0.35f;
    private const float StubLength = 3f;
    private const float GeometryEpsilon = 0.0001f;
    private readonly List<Area> _areas = new();
    private readonly List<Stub> _stubs = new();
    private readonly Dictionary<int, int> _roomAreas = new();
    private bool _hasPreviousPosition;
    private Vector2 _previousPosition;

    public IReadOnlyList<Area> Areas => _areas;
    public IReadOnlyList<Stub> Stubs => _stubs;
    public int CurrentAreaIndex { get; private set; } = -1;
    public int Revision { get; private set; }

    public void Reset(BaekhoDungeonPlan plan)
    {
        _areas.Clear();
        _stubs.Clear();
        _roomAreas.Clear();
        CurrentAreaIndex = -1;
        ResetMotionHistory();

        if (plan != null)
        {
            var mainRooms = new Dictionary<int, BaekhoPlacedModule>();
            foreach (var room in plan.rooms)
            {
                if (!AddModule(room, DungeonMinimapAreaKind.Room)) continue;
                mainRooms.Add(room.id, room);
            }

            var stubSockets = new HashSet<long>();
            foreach (var link in plan.links)
            {
                if (link == null) continue;
                foreach (var segment in link.segments)
                    AddCorridor(segment, link.width);
                foreach (var corner in link.corners)
                    AddModule(corner, DungeonMinimapAreaKind.Corner);
                AddStub(link.fromMapId, link.fromSocketIndex, link.width, mainRooms, stubSockets);
                AddStub(link.toMapId, link.toSocketIndex, link.width, mainRooms, stubSockets);
            }

            int startIndex = GetRoomAreaIndex(plan.startMapId);
            if (startIndex >= 0) _areas[startIndex].Visit();
        }

        ++Revision;
    }

    public int GetRoomAreaIndex(int roomId) => _roomAreas.TryGetValue(roomId, out int index) ? index : -1;

    /// <summary>
    /// Updates current/visited areas. Disable movement tracking for a teleport;
    /// this visits its destination without revealing the gap and starts the next sweep there.
    /// </summary>
    public bool UpdatePosition(Vector2 localXZ, bool trackMovement = true)
    {
        if (!Finite(localXZ))
        {
            ResetMotionHistory();
            return false;
        }

        bool changed = false;
        if (trackMovement && _hasPreviousPosition)
            foreach (var area in _areas)
                if (!area.Visited && Crosses(area, _previousPosition, localXZ))
                    changed |= area.Visit();

        int current = -1;
        // The previous area wins both shared doorway overlaps and its small exit margin.
        if (CurrentAreaIndex >= 0 && Contains(_areas[CurrentAreaIndex], localXZ, CurrentAreaMargin))
            current = CurrentAreaIndex;
        else
            for (int i = 0; i < _areas.Count; ++i)
                if (Contains(_areas[i], localXZ, 0f)) { current = i; break; }

        if (current >= 0) changed |= _areas[current].Visit();
        if (current != CurrentAreaIndex)
        {
            CurrentAreaIndex = current;
            changed = true;
        }

        _previousPosition = localXZ;
        _hasPreviousPosition = true;
        if (changed) ++Revision;
        return changed;
    }

    public void ResetMotionHistory() => _hasPreviousPosition = false;

    private bool AddModule(BaekhoPlacedModule room, DungeonMinimapAreaKind kind)
    {
        if (room == null || room.prefab == null || _roomAreas.ContainsKey(room.id)) return false;
        Bounds bounds = room.WorldBounds;
        var center = XZ(bounds.center);
        var size = XZ(bounds.size);
        if (!Finite(center) || !Positive(size)) return false;
        _roomAreas.Add(room.id, _areas.Count);
        // WorldBounds already applies the module's quarter-turn and offset local pivot.
        _areas.Add(new Area(kind, room.id, center, size, 0f));
        return true;
    }

    private void AddCorridor(BaekhoCorridorSegment segment, float width)
    {
        var start = XZ(segment.start);
        var end = XZ(segment.end);
        var delta = end - start;
        float length = delta.magnitude;
        if (!Finite(start) || !Finite(end) || !Positive(width) || !Positive(length)) return;
        _areas.Add(new Area(DungeonMinimapAreaKind.Corridor, -1, (start + end) * 0.5f,
            new Vector2(width, length), Angle(delta)));
    }

    private void AddStub(int roomId, int socketIndex, float width,
        Dictionary<int, BaekhoPlacedModule> mainRooms, HashSet<long> sockets)
    {
        if (!Positive(width) || !mainRooms.TryGetValue(roomId, out var room)) return;
        var connections = room.prefab.connections;
        if (connections == null || socketIndex < 0 || socketIndex >= connections.Length || connections[socketIndex] == null)
            return;
        long key = ((long)roomId << 32) | (uint)socketIndex;
        if (!sockets.Add(key)) return;

        var position = XZ(room.SocketPosition(socketIndex));
        var forward = XZ(room.SocketForward(socketIndex));
        if (!Finite(position) || !Finite(forward) || forward.sqrMagnitude < GeometryEpsilon) return;
        forward.Normalize();
        float opening = connections[socketIndex].width;
        if (Positive(opening)) width = Mathf.Min(width, opening);
        _stubs.Add(new Stub(GetRoomAreaIndex(roomId), position + forward * (StubLength * 0.5f),
            new Vector2(width, StubLength), Angle(forward)));
    }

    private static bool Contains(Area area, Vector2 point, float margin)
    {
        var local = area.ToLocal(point);
        var half = area.Size * 0.5f;
        return Mathf.Abs(local.x) <= half.x + margin && Mathf.Abs(local.y) <= half.y + margin;
    }

    private static bool Crosses(Area area, Vector2 from, Vector2 to)
    {
        var start = area.ToLocal(from);
        var delta = area.ToLocal(to) - start;
        var half = area.Size * 0.5f;
        float enter = 0f, exit = 1f;
        return ClipAxis(start.x, delta.x, half.x, ref enter, ref exit)
            && ClipAxis(start.y, delta.y, half.y, ref enter, ref exit);
    }

    // Slab clipping handles short areas crossed entirely between frames, without sampling gaps.
    private static bool ClipAxis(float start, float delta, float extent, ref float enter, ref float exit)
    {
        if (Mathf.Abs(delta) < GeometryEpsilon) return Mathf.Abs(start) <= extent;
        float first = (-extent - start) / delta;
        float last = (extent - start) / delta;
        if (first > last) { float swap = first; first = last; last = swap; }
        enter = Mathf.Max(enter, first);
        exit = Mathf.Min(exit, last);
        return enter <= exit;
    }

    private static Vector2 XZ(Vector3 point) => new(point.x, point.z);
    private static float Angle(Vector2 forward) => -Mathf.Atan2(forward.x, forward.y) * Mathf.Rad2Deg;
    private static bool Finite(Vector2 point) => Finite(point.x) && Finite(point.y);
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Positive(Vector2 size) => Positive(size.x) && Positive(size.y);
    private static bool Positive(float value) => Finite(value) && value > GeometryEpsilon;
}
