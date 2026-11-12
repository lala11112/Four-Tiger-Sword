using System;
using UnityEngine;

namespace ArpgHud
{
    [Serializable]
    public struct MeterState
    {
        public float Current, Maximum;
        public MeterState(float current, float maximum) { Current = current; Maximum = maximum; }
    }

    [Serializable]
    public struct SkillState
    {
        public Sprite ColorIcon, GrayIcon;
        public float Remaining, Duration;
        public bool Unavailable, Active;
        public SkillState(Sprite color, Sprite gray, float remaining, float duration)
        {
            ColorIcon = color; GrayIcon = gray; Remaining = remaining; Duration = duration;
            Unavailable = Active = false;
        }
    }

    [Serializable]
    public struct ChainSkillState
    {
        public float Remaining;
        public bool Selected;
        public ChainSkillState(float remaining, bool selected)
        { Remaining = remaining; Selected = selected; }
    }

    [Serializable]
    public struct HudState
    {
        public MeterState Hp, UltimateCharge;
        public int ActiveFormIndex;
        // Bits 0..4 follow HUD order: Fire, Water, Wood, Metal, Earth. Zero preserves unrestricted previews.
        public int LockedFormsMask;
        public SkillState E, R;
        public ChainSkillState FireChain, WaterChain;

        public bool IsFormLocked(int oneBasedIndex) => oneBasedIndex >= 1 && oneBasedIndex <= 5
            && (LockedFormsMask & (1 << (oneBasedIndex - 1))) != 0;
    }

    /// <summary>The game owns all state and cooldown timing. Publish after accepted changes.</summary>
    public interface IHudStateSource
    {
        HudState CurrentState { get; }
        event Action StateChanged;
    }

    public static class HudMath
    {
        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static float Ratio(MeterState value) =>
            IsFinite(value.Current) && IsFinite(value.Maximum) && value.Maximum > 0
                ? Mathf.Clamp01(value.Current / value.Maximum) : 0;

        public static float Remaining(SkillState value) =>
            IsFinite(value.Duration) && IsFinite(value.Remaining) && value.Duration > 0
                ? Mathf.Clamp(value.Remaining, 0, value.Duration) : 0;

        public static float Progress(SkillState value) =>
            IsFinite(value.Duration) && value.Duration > 0 ? 1 - Remaining(value) / value.Duration : 1;

        public static float ChainRemaining(ChainSkillState value) =>
            IsFinite(value.Remaining) ? Mathf.Max(0f, value.Remaining) : 0f;
    }
}
