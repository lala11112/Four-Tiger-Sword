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
    }
}
