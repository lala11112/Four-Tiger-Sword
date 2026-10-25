using UnityEngine;

namespace StudioBaekho
{
    /// <summary>
    /// Identifies an authored room fixture and its optional linked placement.
    /// The game implements interactions, hazards and switch/gate behavior.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BaekhoMapFixture : MonoBehaviour
    {
        public string id;
        public string role;
        public string linkedId;
    }
}
