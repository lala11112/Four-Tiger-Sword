using UnityEngine;

namespace StudioBaekho
{
    [CreateAssetMenu(menuName = "Baekho/Architecture Appearance")]
    public sealed class BaekhoArchitectureProfile : ScriptableObject
    {
        public Material wallMaterial;
        public Material floorMaterial;
        public Material jointMaterial;
        public Material trimMaterial;
        public Material accentMaterial;
        [Tooltip("Reusable visual-only wall kit references for authoring; runtime skins use the same fixed module dimensions.")]
        public GameObject straightModule;
        public GameObject cornerModule;
        public GameObject doorwayModule;
        [Min(.1f)] public float panelWidth = 2f;
        [Min(.1f)] public float panelHeight = 1.25f;
        [Min(.001f)] public float jointWidth = .035f;
        [Min(.001f)] public float panelDepth = .025f;
        [Min(.001f)] public float trimWidth = .035f;
        [Min(0f)] public float accentHeight = 1.65f;

        public bool Validate(out string error)
        {
            error = null;
            if (!wallMaterial) error = "Architecture appearance requires a wall material.";
            else if (!Positive(panelWidth) || !Positive(panelHeight) || panelWidth < .1f || panelHeight < .1f
                || !Positive(jointWidth) || !Positive(panelDepth) || !Positive(trimWidth)
                || !BaekhoDungeonGeometry.Finite(accentHeight) || accentHeight < 0f)
                error = "Architecture module dimensions must be finite and positive.";
            else if (jointWidth >= Mathf.Min(panelWidth, panelHeight) * .5f)
                error = "Architecture joints must be smaller than half a panel.";
            return error == null;
        }

        private static bool Positive(float value) => BaekhoDungeonGeometry.Finite(value) && value > 0f;
    }
}
