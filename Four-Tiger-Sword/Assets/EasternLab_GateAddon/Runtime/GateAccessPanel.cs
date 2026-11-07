using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EasternLab.GateAddon
{
    /// <summary>Independent gate-console interaction; requires no player or UI scripts.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class GateAccessPanel : MonoBehaviour
    {
        public GateActivation gateActivation;
        [Tooltip("Optional. Uses the tagged main camera when empty.")]
        public Camera playerCamera;
        [Min(0.1f)] public float interactionDistance = 3f;
        public LayerMask hitMask = ~0;
        [Tooltip("Disable when your own interaction system calls Operate().")]
        public bool useBuiltInInput = true;
        public bool showPrompt = true;
        [Tooltip("Optional Unity Font with Korean glyphs (not a TMP font asset).")]
        public Font koreanFont;

        public bool IsFocused { get; private set; }
        public string LastResult => gateActivation ? gateActivation.LastResult : "경계 제어 연결을 확인하세요.";

        GUIStyle promptStyle;
        Font generatedFont;

        void Reset()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            gateActivation = GetComponentInParent<GateActivation>();
        }

        void Update()
        {
            IsFocused = HasFocus();
            if (useBuiltInInput && IsFocused && InteractPressed()) Operate();
        }

        bool HasFocus()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || !gateActivation || !gateActivation.isActiveAndEnabled)
                return false;
            var camera = playerCamera ? playerCamera : Camera.main;
            if (!camera || !camera.isActiveAndEnabled) return false;
            if (!Physics.Raycast(camera.transform.position, camera.transform.forward, out var hit,
                    interactionDistance, hitMask, QueryTriggerInteraction.Collide)) return false;
            return hit.collider.GetComponentInParent<GateAccessPanel>() == this;
        }

        /// <summary>Call from an existing interaction system. Still requires focus and range.</summary>
        public void Operate()
        {
            if (!HasFocus()) return;
            if (gateActivation.State == GateState.Closed) gateActivation.Open();
            else if (gateActivation.State == GateState.Open) gateActivation.Close();
        }

        static bool InteractPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER || !ENABLE_INPUT_SYSTEM
            if (Input.GetKeyDown(KeyCode.E)) return true;
#endif
            return false;
        }

        void OnGUI()
        {
            if (!showPrompt || !IsFocused || !gateActivation) return;
            if (promptStyle == null)
            {
                if (!koreanFont)
                    generatedFont = Font.CreateDynamicFontFromOSFont(
                        new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Noto Sans KR", "Arial Unicode MS" }, 20);
                promptStyle = new GUIStyle(GUI.skin.box)
                {
                    font = koreanFont ? koreanFont : generatedFont,
                    fontSize = 20,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true,
                    padding = new RectOffset(16, 16, 10, 10)
                };
                promptStyle.normal.textColor = new Color(.85f, .97f, .95f);
            }
            string action = gateActivation.State == GateState.Closed ? "[E] 경계 개방" :
                gateActivation.State == GateState.Open ? "[E] 경계 닫기" :
                gateActivation.State == GateState.Opening ? "경계 개방 중…" : "경계 폐쇄 중…";
            if (!useBuiltInInput) action = action.Replace("[E] ", "");
            float width = Mathf.Min(650f, Screen.width - 24f);
            GUI.Box(new Rect((Screen.width - width) * .5f, Screen.height - 124f, width, 94f),
                action + "\n" + LastResult, promptStyle);
        }

        void OnDisable() { IsFocused = false; }
        void OnDestroy() { if (generatedFont) Destroy(generatedFont); }
    }
}
