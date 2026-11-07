using System;
using UnityEngine;
using UnityEngine.Events;

namespace EasternCyber.EventPropsV2
{
    public enum RouletteOutcome { Miss, Gold, Item }

    /// <summary>Mechanical presentation only. Subscribe to events to connect your own game rules.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Animator))]
    public sealed class EventPropController : MonoBehaviour
    {
        [SerializeField] string deviceId;
        [SerializeField] float useDuration = 2f;
        [SerializeField, Range(0, 1)] float rewardTimeNormalized = 0.65f;
        [SerializeField] bool roulette;
        [SerializeField] string[] movingPartNames = Array.Empty<string>();
        public UnityEvent onUseStarted = new UnityEvent();
        public UnityEvent onRewardReady = new UnityEvent();
        public UnityEvent onUseCompleted = new UnityEvent();
        public UnityEvent onDeviceReset = new UnityEvent();
        public string DeviceId => deviceId;
        public bool IsBusy { get; private set; }
        public RouletteOutcome LastOutcome { get; private set; }
        public string[] MovingPartNames => movingPartNames;
        Animator animator;
        float elapsed;
        bool rewardSent;
        public void Configure(string id, float duration, bool isRoulette, float readyAt = .65f, string[] movingParts = null)
        { deviceId = id; useDuration = duration; roulette = isRoulette; rewardTimeNormalized = readyAt; movingPartNames = movingParts ?? Array.Empty<string>(); }
        void Awake() { animator = GetComponent<Animator>(); animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
        [ContextMenu("Use Device")]
        public void Use() { BeginUse(roulette ? "UseGold" : "Use", RouletteOutcome.Gold); }
        public bool UseRoulette(RouletteOutcome outcome)
        {
            if (!roulette) return false;
            return BeginUse("Use" + outcome, outcome);
        }
        bool BeginUse(string state, RouletteOutcome outcome)
        {
            if (IsBusy) return false;
            if (!animator) animator = GetComponent<Animator>();
            if (!animator || !animator.runtimeAnimatorController || !animator.HasState(0, Animator.StringToHash(state))) return false;
            LastOutcome = outcome; elapsed = 0; rewardSent = false; IsBusy = true;
            animator.Play(state, 0, 0); onUseStarted.Invoke(); return true;
        }
        void Update()
        {
            if (!IsBusy) return;
            elapsed += Time.deltaTime;
            if (!rewardSent && elapsed >= useDuration * rewardTimeNormalized)
            { rewardSent = true; onRewardReady.Invoke(); }
            if (elapsed >= useDuration)
            { IsBusy = false; onUseCompleted.Invoke(); }
        }
        [ContextMenu("Reset Device")]
        public void ResetDevice()
        {
            IsBusy = false; elapsed = 0; rewardSent = false;
            if (!animator) animator = GetComponent<Animator>();
            if (animator && animator.runtimeAnimatorController) { animator.Play("Idle", 0, 0); animator.Update(0); }
            onDeviceReset.Invoke();
        }
        void OnDisable() { IsBusy = false; }
    }
}
