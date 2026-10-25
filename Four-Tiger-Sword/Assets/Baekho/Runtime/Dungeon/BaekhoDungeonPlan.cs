using System.Collections.Generic;
using UnityEngine;

namespace StudioBaekho
{
    [System.Flags]
    public enum BaekhoRoomRole { Normal = 0, Start = 1, End = 2, Boss = 4, Shop = 8 }

    public sealed class BaekhoPlacedModule
    {
        public int id;
        public BaekhoMapModule prefab;
        public Vector3 position;
        public int quarterTurns;
        public BaekhoRoomRole role;
        public Quaternion Rotation => Quaternion.Euler(0f, quarterTurns * 90f, 0f);
        public Vector3 SocketPosition(int index) => position + Rotation * prefab.GetSocket(index).position;
        public Vector3 SocketForward(int index) => Rotation * prefab.GetSocket(index).forward;
        public Bounds WorldBounds
        {
            get
            {
                var local = prefab.localBounds;
                var size = local.size;
                if ((quarterTurns & 1) != 0) size = new Vector3(size.z, size.y, size.x);
                return new Bounds(position + Rotation * local.center, size);
            }
        }
    }

    public struct BaekhoCorridorSegment
    {
        public Vector3 start;
        public Vector3 end;
        public BaekhoCorridorSegment(Vector3 start, Vector3 end) { this.start = start; this.end = end; }
    }

    public sealed class BaekhoDungeonLink
    {
        public int fromMapId;
        public int fromSocketIndex;
        public int toMapId;
        public int toSocketIndex;
        public float width;
        // Every turn is a room. Straight segments end exactly at room sockets.
        public readonly List<BaekhoCorridorSegment> segments = new List<BaekhoCorridorSegment>();
        public readonly List<BaekhoPlacedModule> corners = new List<BaekhoPlacedModule>();
    }

    public sealed class BaekhoDungeonPlan
    {
        public int seed;
        public int startMapId = -1;
        public int endMapId = -1;
        public int bossMapId = -1;
        public int shopMapId = -1;
        public readonly List<BaekhoPlacedModule> rooms = new List<BaekhoPlacedModule>();
        public readonly List<BaekhoDungeonLink> links = new List<BaekhoDungeonLink>();
    }

    public sealed class BaekhoDungeonResult
    {
        public bool success;
        public int seed;
        public string error;
        public BaekhoDungeonPlan plan;
        public GameObject root;
        public BaekhoMapModule startRoom;
        public BaekhoMapModule endRoom;
        public BaekhoMapModule bossRoom;
        public BaekhoMapModule shopRoom;
        public readonly List<BaekhoMapModule> rooms = new List<BaekhoMapModule>();
    }
}
