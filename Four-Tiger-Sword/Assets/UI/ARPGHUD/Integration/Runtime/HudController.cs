using UnityEngine;

namespace ArpgHud
{
    public enum HudElement { Fire = 1, Water = 2, Wood = 3, Metal = 4, Earth = 5 }

    /// <summary>Push game-owned values into the HUD. This component never advances cooldowns.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HudView))]
    public sealed class HudController : MonoBehaviour
    {
        [SerializeField] HudView view;
        HudState state = new HudState {
            Hp = new MeterState(100, 100),
            ActiveFormIndex = (int)HudElement.Fire
        };

        public HudView View { get { ResolveView(); return view; } }
        public HudState CurrentState => state;

        void Awake() { ResolveView(); }
        void OnEnable() { Refresh(); }
        void ResolveView() { if (view == null) view = GetComponent<HudView>(); }

        public void Configure(HudView target) { view = target; Refresh(); }
        /// <summary>Apply one authoritative game snapshot in a single render.</summary>
        public void SetState(HudState value) { state = value; Refresh(); }
        public void SetHealth(float current, float maximum)
        { state.Hp = new MeterState(current, maximum); Refresh(); }
        public void SetUltimateCharge(float current, float maximum)
        { state.UltimateCharge = new MeterState(current, maximum); Refresh(); }
        public void SetForm(HudElement element) { SetForm((int)element); }

        /// <summary>Accepts one-based indices 1 through 5; other values leave the snapshot unchanged.</summary>
        public void SetForm(int oneBasedIndex)
        {
            if (oneBasedIndex < 1 || oneBasedIndex > 5) return;
            state.ActiveFormIndex = oneBasedIndex;
            Refresh();
        }

        public void SetCooldownE(float remainingSeconds, float durationSeconds)
        { state.E.Remaining = remainingSeconds; state.E.Duration = durationSeconds; Refresh(); }
        public void SetCooldownR(float remainingSeconds, float durationSeconds)
        { state.R.Remaining = remainingSeconds; state.R.Duration = durationSeconds; Refresh(); }

        public void Refresh()
        {
            ResolveView();
            if (view != null) view.Render(state);
        }
    }
}
