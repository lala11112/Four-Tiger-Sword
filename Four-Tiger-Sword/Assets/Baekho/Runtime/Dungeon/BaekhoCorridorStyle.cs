using System;
using UnityEngine;

namespace StudioBaekho
{
    public enum BaekhoPiecePlacement { Stretch = 0, Repeat = 1, TilePreserveSize = 2 }

    [Flags]
    public enum BaekhoPieceTilingAxes { X = 1, Z = 2, XZ = X | Z }

    [Serializable]
    public sealed class BaekhoPrefabPiece
    {
        public GameObject prefab;
        [Tooltip("Mesh bounds in prefab root LOCAL space. Prepared by the editor tool; includes pivot offset.")]
        public Bounds localBounds = new Bounds(Vector3.zero, Vector3.one);
        [Tooltip("TilePreserveSize repeats the authored prefab size and crops the final piece without horizontal stretching.")]
        public BaekhoPiecePlacement placement;
        [Min(0.1f), Tooltip("Desired length along local Z when repeating. The last piece is fitted to the remainder.")]
        public float repeatLength = 2f;
    }

    [CreateAssetMenu(menuName = "Baekho/Corridor Style")]
    public sealed class BaekhoCorridorStyle : ScriptableObject
    {
        [Tooltip("Pieces use local X for width, Y for height, Z for length. Geometry colliders are managed by the generator.")]
        public BaekhoPrefabPiece floor = new BaekhoPrefabPiece();
        public BaekhoPrefabPiece wall = new BaekhoPrefabPiece();
        public BaekhoPrefabPiece cap = new BaekhoPrefabPiece();
        [Tooltip("Optional ceiling visual. An unassigned ceiling reuses the floor piece, including its measured bounds and placement mode.")]
        public BaekhoPrefabPiece ceiling = new BaekhoPrefabPiece();
        [Min(0.01f)] public float wallThickness = 0.25f;
        [Min(0.01f)] public float wallHeight = 3f;
        [Min(0.01f)] public float floorThickness = 0.2f;
        [Min(0.01f)] public float ceilingThickness = 0.2f;
        [Tooltip("Optional fixture with local +Z pointing away from its mounting wall into the corridor.")]
        public GameObject wallLightPrefab;
        [Tooltip("Optional fixture authored to hang below its root and illuminate downward.")]
        public GameObject ceilingLightPrefab;
        [Min(0.1f)] public float lightSpacing = 6f;
        [Min(0.01f)] public float wallLightHeight = 2.2f;
        public string groundLayer = "Ground";

        public BaekhoPrefabPiece CeilingPiece => ceiling != null && ceiling.prefab != null ? ceiling : floor;
    }
}
