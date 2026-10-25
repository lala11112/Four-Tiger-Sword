using UnityEngine;

namespace StudioBaekho
{
    /// <summary>Prefab-root registration metadata. Read by Map Forge; never changes transforms or runtime behaviour.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Baekho/Map Asset")]
    public sealed class BaekhoMapAsset : MonoBehaviour
    {
        // Append new roles to preserve existing prefab enum values.
        public enum AssetRole
        {
            Unspecified, Floor, Wall, Door, Prop, Stair, Slope, Bridge, Ceiling, Cliff, Deadzone,
            Light, Railing, Column, Arch, Water, Hazard, Trap, Gate, Switch, SecretDoor, Rest,
            Merchant, Altar, Decoration, Vegetation
        }
        public enum LightAnchor { Either, Ceiling, Wall }

        [Tooltip("Include this prefab when Map Forge imports marked prefabs. The component must be on the prefab root.")]
        public bool importEnabled = true;
        public AssetRole role = AssetRole.Unspecified;
        [Tooltip("Optional initial name in Map Forge. Leave empty to use the prefab name (maximum 80 characters).")]
        public string label = "";

        public bool useInitialScale;
        [Tooltip("Optional initial root scale in Map Forge; does not change this prefab. Positive XYZ values only.")]
        public Vector3 initialScale = Vector3.one;
        public bool useInitialRotation;
        public float initialRotationY;
        public bool useInitialOffset;
        public Vector3 initialOffset;

        [Tooltip("Stair/slope rise in metres at root scale Y=1. Default is 1 metre.")]
        public float stairNativeRise = 1;
        [Tooltip("Light installation surface. Either leaves the placement unrestricted.")]
        public LightAnchor lightAnchor = LightAnchor.Either;
    }
}
