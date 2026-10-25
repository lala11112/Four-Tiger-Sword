using System;
using UnityEngine;

namespace StudioBaekho
{
    [CreateAssetMenu(menuName = "Baekho/Dungeon Settings")]
    public sealed class BaekhoDungeonSettings : ScriptableObject
    {
        public BaekhoMapModule[] maps = Array.Empty<BaekhoMapModule>();
        [Tooltip("Optional dedicated start prefab. It is excluded from the ordinary Maps pool.")]
        public BaekhoMapModule startRoom;
        [Tooltip("Optional dedicated end prefab. The end is a leaf; with a boss it connects only to the boss.")]
        public BaekhoMapModule endRoom;
        [Tooltip("Optional boss prefab before the separate end room. Requires two usable doors; no combat behavior is added.")]
        public BaekhoMapModule bossRoom;
        [Tooltip("Optional dedicated shop prefab. Exactly one safe leaf is added within Map Count and excluded from the ordinary Maps pool.")]
        public BaekhoMapModule shopRoom;
        public BaekhoMapModule cornerRoom;
        public BaekhoCorridorStyle style;
        [Min(1)] public int mapCount = 6;
        public bool randomizeSeed = true;
        public int seed = 12345;
        [Min(0.1f)] public float gridSize = 0.5f;
        [Min(0f)] public float minimumMapGap = 4f;
        [Min(1f)] public float maximumConnectionLength = 60f;
        [Min(0.1f), Tooltip("Clear width of every corridor. Narrower map or corner doorways receive local end shoulders without shrinking the corridor.")]
        public float corridorWidth = 2f;
        [Min(0.1f)] public float minimumDoorWidth = 1f;
        [Min(1)] public int placementAttempts = 64;
        [Min(1)] public int layoutAttempts = 8;
        [Min(100), Tooltip("Total candidate checks and route expansions across all retries; a deterministic generation work budget.")]
        public int maxSearchNodes = 60000;
    }
}
