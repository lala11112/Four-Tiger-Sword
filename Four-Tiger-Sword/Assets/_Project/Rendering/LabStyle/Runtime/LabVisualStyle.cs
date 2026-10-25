using System;
using UnityEngine;

namespace FourTigerSword.Rendering
{
    /// <summary>
    /// Explicit material substitutions and local-light calibration for generated lab geometry.
    /// Replacement materials are authored assets; applying a style never edits either material.
    /// </summary>
    [CreateAssetMenu(fileName = "LabVisualStyle", menuName = "Four Tiger Sword/Rendering/Lab Visual Style")]
    public sealed class LabVisualStyle : ScriptableObject
    {
        [Serializable]
        public sealed class MaterialReplacement
        {
            public Material source;
            public Material replacement;
        }

        [Tooltip("Only these exact source assets are remapped. First entry wins for duplicate sources.")]
        public MaterialReplacement[] materialReplacements = Array.Empty<MaterialReplacement>();

        [Min(0f)]
        [Tooltip("Applied once to Point and Spot lights inside the selected generated or explicit root.")]
        public float localLightIntensityMultiplier = 0.55f;

        [Min(0f)]
        [Tooltip("Optional intensity cap after scaling. Zero disables the cap.")]
        public float localLightIntensityLimit;

        public float EvaluateLocalLightIntensity(float originalIntensity)
        {
            float intensity = originalIntensity * Mathf.Max(0f, localLightIntensityMultiplier);
            return localLightIntensityLimit > 0f ? Mathf.Min(intensity, localLightIntensityLimit) : intensity;
        }
    }
}
