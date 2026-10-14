using UnityEngine;

namespace StudioBaekho
{
    /// <summary>
    /// An exterior doorway socket authored by Baekho Map Forge. Its transform is
    /// at the opening's floor-level center; transform.forward points out of the map.
    /// This component describes a connection and does not assemble maps or corridors.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BaekhoMapConnection : MonoBehaviour
    {
        public string id;
        public string label;
        public string kind;
        public float width;
        public float height;
        [Tooltip("Only doorway placeholder objects; never include the surrounding frame or walls.")]
        public GameObject[] placeholders = System.Array.Empty<GameObject>();
        [Tooltip("Prepared full wall bay used when this doorway is unused. Matches the surrounding map wall.")]
        public BaekhoClosedWall closedWall = new BaekhoClosedWall();
    }

    [System.Serializable]
    public sealed class BaekhoClosedWall
    {
        public bool manualOverride;
        public GameObject template;
        public Bounds templateLocalBounds;
        public Vector3 localCenter;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 size;
        public GameObject[] replaceWhenClosed = System.Array.Empty<GameObject>();
        [Tooltip("Matching foundation/cliff bands below the main wall, if the doorway also interrupted them.")]
        public BaekhoWallSection[] additionalSections = System.Array.Empty<BaekhoWallSection>();
    }

    [System.Serializable]
    public sealed class BaekhoWallSection
    {
        public GameObject template;
        public Bounds templateLocalBounds;
        public Vector3 localCenter;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 size;
    }
}
