using UnityEngine;

namespace StudioBaekho
{
    /// <summary>
    /// An authored enemy, reward or event location. The game resolves contentKey
    /// and implements spawning/interactions; this component only stores metadata.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BaekhoGameplayMarker : MonoBehaviour
    {
        public string id;
        public string label;
        public string kind;
        public string contentKey;
    }
}
