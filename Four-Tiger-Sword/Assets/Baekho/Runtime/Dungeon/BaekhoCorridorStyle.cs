using System;
using UnityEngine;

namespace StudioBaekho
{
    public enum BaekhoPiecePlacement { Stretch, Repeat }

    [Serializable]
    public sealed class BaekhoPrefabPiece
    {
        public GameObject prefab;
        [Tooltip("Mesh bounds in prefab root LOCAL space. Prepared by the editor tool; includes pivot offset.")]
        public Bounds localBounds = new Bounds(Vector3.zero, Vector3.one);
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
        [Min(0.01f)] public float wallThickness = 0.25f;
        [Min(0.01f)] public float wallHeight = 3f;
        [Min(0.01f)] public float floorThickness = 0.2f;
        public string groundLayer = "Ground";
    }
}
