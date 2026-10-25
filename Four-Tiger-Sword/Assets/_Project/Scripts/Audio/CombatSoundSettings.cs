using System;
using UnityEngine;

[Serializable]
public sealed class CombatSoundSettings
{
    public AudioClip Clip;
    public AudioClip Accent;
    [Range(0f, 1f)] public float Volume = 0.65f;
    [Range(0f, 1f)] public float AccentVolume = 0.3f;
    public Vector2 PitchRange = new Vector2(0.97f, 1.03f);

    public bool HasSound => Clip != null || Accent != null;

    public CombatSoundCue Resolve(AudioClip fallback = null)
        => new CombatSoundCue(Clip != null ? Clip : fallback, Accent, Volume, AccentVolume, PitchRange);
}

/// <summary>Snapshots attack audio before damage callbacks can change the active form or destroy its owner.</summary>
public readonly struct CombatSoundCue
{
    public readonly AudioClip Clip;
    public readonly AudioClip Accent;
    public readonly float Volume;
    public readonly float AccentVolume;
    public readonly Vector2 PitchRange;

    public bool IsEmpty => Clip == null && Accent == null;

    public CombatSoundCue(AudioClip clip, AudioClip accent = null, float volume = 0.65f,
        float accentVolume = 0.3f, Vector2 pitchRange = default)
    {
        Clip = clip;
        Accent = accent;
        Volume = SafeVolume(volume);
        AccentVolume = SafeVolume(accentVolume);
        if (pitchRange == Vector2.zero) pitchRange = Vector2.one;
        float low = SafePitch(pitchRange.x);
        float high = SafePitch(pitchRange.y);
        PitchRange = new Vector2(Mathf.Min(low, high), Mathf.Max(low, high));
    }

    private static float SafeVolume(float value)
        => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);

    private static float SafePitch(float value)
        => float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Clamp(value, 0.1f, 3f);
}
