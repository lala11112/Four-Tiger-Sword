using UnityEngine;

namespace StudioBaekho.CombatLab
{
    // Authoring artifact. Only actionAsset is consumed by the game's runtime.
    public sealed class CombatLabPreset : ScriptableObject
    {
        public string adapterId;
        public string sourceGuid;
        public string actionId;
        public Object actionAsset;
        public CombatCandidate candidate;
    }
}
