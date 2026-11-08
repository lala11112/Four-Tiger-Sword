using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BoundaryZero.Inventory
{
    [AddComponentMenu("Boundary Zero/Inventory Demo")]
    public sealed class InventoryDemo : MonoBehaviour
    {
        public InventoryView view;
        public EventSystem eventSystem;

        private void Awake()
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            if (eventSystem == null)
                return;
            var legacy = eventSystem.GetComponent<StandaloneInputModule>();
            if (legacy != null)
                legacy.enabled = false;
            var input = eventSystem.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            if (input == null)
            {
                input = eventSystem.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                if (input.actionsAsset == null)
                    input.AssignDefaultActions();
            }
#endif
        }

        private void Start()
        {
            if (view == null || view.Catalog == null)
                return;

            var ids = new List<string>();
            foreach (var item in view.Catalog.ReadData().items)
                ids.Add(item.id);

            view.SetOwnedItems(ids);
            view.Open();
        }

        private void Update()
        {
            if (view == null)
                return;

            bool toggle = false, close = false;
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null)
            {
                toggle = keyboard.iKey.wasPressedThisFrame;
                close = keyboard.escapeKey.wasPressedThisFrame;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            toggle |= Input.GetKeyDown(KeyCode.I);
            close |= Input.GetKeyDown(KeyCode.Escape);
#endif
            if (close)
                view.Close();
            else if (toggle)
                view.Toggle();
        }
    }
}
