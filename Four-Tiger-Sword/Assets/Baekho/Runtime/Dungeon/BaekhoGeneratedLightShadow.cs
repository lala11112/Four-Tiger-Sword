using UnityEngine;

namespace StudioBaekho
{
    /// <summary>Stores the shadow mode a generated fixture can use when a budget selects it.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Light))]
    public sealed class BaekhoGeneratedLightShadow : MonoBehaviour
    {
        public LightShadows authoredMode;
    }
}
