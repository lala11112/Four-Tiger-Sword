using UnityEngine;
using UnityEngine.EventSystems;

namespace BoundaryZero.SupplyDepot
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EventSystem))]
    public sealed class SupplyDepotDemoInput : MonoBehaviour
    {
        void Awake()
        {
            if (GetComponent<BaseInputModule>() != null) return;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var module = gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            if (module.actionsAsset == null) module.AssignDefaultActions();
#else
            gameObject.AddComponent<StandaloneInputModule>();
#endif
        }
    }
}
