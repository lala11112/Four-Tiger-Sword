using System;
using System.Collections.Generic;
using UnityEngine;

// The zero enum values preserve the existing authored route on deserialization.
public enum CombatFeedbackChannelMode { Inherit = 0, Off = 1, Override = 2 }
public enum CombatFeedbackClock { World = 0, Actor = 1, Unscaled = 2 }
public enum CombatFeedbackSpace { Camera = 0, World = 1, Attack = 2 }
public enum CombatFeedbackStackMode { Add = 0, Replace = 1, Strongest = 2 }
public enum CombatFeedbackTriggerPolicy { FirstPerStep = 0, FirstPerHitEvent = 1, EveryTarget = 2 }

public sealed class CombatFeedbackHitGate
{
    private readonly HashSet<string> _seen = new HashSet<string>();
    public bool Accept(CombatFeedbackTriggerPolicy policy, int hitIndex, int targetId)
    {
        string key = policy == CombatFeedbackTriggerPolicy.FirstPerStep ? "step"
            : hitIndex.ToString() + (policy == CombatFeedbackTriggerPolicy.EveryTarget ? ":" + targetId : string.Empty);
        return _seen.Add(key);
    }
    public void Reset() => _seen.Clear();
}

[Serializable]
public class CombatCameraHitSettings
{
    // Distinguishes an explicit default-valued policy/flag from an untouched block.
    public bool Authored;
    public CombatCameraImpactV2 Impact = new CombatCameraImpactV2();
    public CombatCameraShakeV2 Shake = new CombatCameraShakeV2();
    public CombatFeedbackTriggerPolicy TriggerPolicy = CombatFeedbackTriggerPolicy.FirstPerHitEvent;
    public bool PreserveFeelFlash = true;

    public virtual bool HasOverrides => Authored || (Impact != null && Impact.Mode != CombatFeedbackChannelMode.Inherit)
        || (Shake != null && Shake.Mode != CombatFeedbackChannelMode.Inherit)
        || !PreserveFeelFlash || TriggerPolicy != CombatFeedbackTriggerPolicy.FirstPerHitEvent;
}

[Serializable]
public sealed class CombatCameraV2Settings : CombatCameraHitSettings
{
    public CombatFeedbackChannelMode Mode;
    public CombatCameraMotionV2 Motion = new CombatCameraMotionV2();

    public override bool HasOverrides => base.HasOverrides || Mode != CombatFeedbackChannelMode.Inherit;

    public static CombatCameraV2Settings Resolve(AttackCameraProfile profile, CombatCameraV2Settings inline)
    {
        var inherited = profile != null && profile.EnableV2 ? profile.V2 : null;
        if (inline?.HasOverrides != true) return inherited;
        if (inherited == null || ReferenceEquals(inherited, inline)) return inline;
        bool motion = inline.Mode != CombatFeedbackChannelMode.Inherit;
        bool impact = inline.Impact != null && inline.Impact.Mode != CombatFeedbackChannelMode.Inherit;
        bool shake = inline.Shake != null && inline.Shake.Mode != CombatFeedbackChannelMode.Inherit;
        // Playback is read-only. Preserve every authored key/tangent and never
        // mutate the source profile when an inline channel inherits from it.
        return new CombatCameraV2Settings { Authored = true, Mode = motion ? inline.Mode : inherited.Mode,
            Motion = motion ? inline.Motion : inherited.Motion,
            Impact = impact ? inline.Impact : inherited.Impact,
            Shake = shake ? inline.Shake : inherited.Shake,
            TriggerPolicy = inline.TriggerPolicy, PreserveFeelFlash = inline.PreserveFeelFlash };
    }
}

[Serializable]
public sealed class CombatCameraMotionV2
{
    public AnimationCurve FovOffset = AnimationCurve.Linear(0, 0, 1, 0);
    public AnimationCurve PositionX = AnimationCurve.Linear(0, 0, 1, 0);
    public AnimationCurve PositionY = AnimationCurve.Linear(0, 0, 1, 0);
    public AnimationCurve PositionZ = AnimationCurve.Linear(0, 0, 1, 0);
    public AnimationCurve RotationX = AnimationCurve.Linear(0, 0, 1, 0);
    public AnimationCurve RotationY = AnimationCurve.Linear(0, 0, 1, 0);
    public AnimationCurve RotationZ = AnimationCurve.Linear(0, 0, 1, 0);
    public AnimationCurve FramingWeight = AnimationCurve.Linear(0, 1, 1, 1);
    public float TargetWeight = 0.35f;
    public float MaxTargetDistance = 12f;
    public float MaxFramingOffset = 2.5f;
    public float DirectionLookAhead;
    public float ResponseTime = 0.08f;
    public float ReturnDuration = 0.3f;
    public float CancelDuration = 0.12f;
    public CombatFeedbackSpace Space = CombatFeedbackSpace.Camera;
    public CombatFeedbackClock Clock = CombatFeedbackClock.Actor;
    public CombatFeedbackClock ReturnClock = CombatFeedbackClock.Unscaled;
    public bool PreserveChainViewDirection = true;
    public bool UseFeelZoom;
}

[Serializable]
public sealed class CombatCameraImpactV2
{
    public CombatFeedbackChannelMode Mode;
    public float Delay;
    public float Duration = 0.15f;
    public CombatFeedbackClock Clock = CombatFeedbackClock.Unscaled;
    public CombatFeedbackSpace Space = CombatFeedbackSpace.Camera;
    public CombatFeedbackStackMode Stacking = CombatFeedbackStackMode.Replace;
    // Time is normalized after Delay, independently of the attack's progress.
    public AnimationCurve FovOffset = AnimationCurve.Linear(0, 0, 1, 0);
    public AnimationCurve PositionX = AnimationCurve.Linear(0, 0, 1, 0);
    public AnimationCurve PositionY = AnimationCurve.Linear(0, 0, 1, 0);
    public AnimationCurve PositionZ = AnimationCurve.Linear(0, 0, 1, 0);
    public AnimationCurve RotationX = AnimationCurve.Linear(0, 0, 1, 0);
    public AnimationCurve RotationY = AnimationCurve.Linear(0, 0, 1, 0);
    public AnimationCurve RotationZ = AnimationCurve.Linear(0, 0, 1, 0);
}

[Serializable]
public sealed class CombatCameraShakeV2
{
    public CombatFeedbackChannelMode Mode;
    // Absolute camera displacement (metres) and angular displacement (degrees).
    // Neither depends on a scene ImpulseSource, its DefaultVelocity or listener gain.
    public Vector3 Translation;
    public Vector3 Rotation;
    public float Frequency = 25f;
    public float Delay;
    public float Duration = 0.15f;
    public AnimationCurve Envelope = AnimationCurve.Linear(0, 1, 1, 0);
    public CombatFeedbackClock Clock = CombatFeedbackClock.Unscaled;
    public CombatFeedbackSpace Space = CombatFeedbackSpace.Camera;
    public CombatFeedbackStackMode Stacking = CombatFeedbackStackMode.Replace;
    public int Seed = 17;
}

/// <summary>Camera-owned mixer. No global time or Cinemachine impulse state is changed.</summary>
public sealed class CombatCameraV2Mixer
{
    private sealed class Impact
    {
        public CombatCameraImpactV2 Settings;
        public Quaternion Frame;
        public float Elapsed;
    }

    private sealed class Shake
    {
        public CombatCameraShakeV2 Settings;
        public Quaternion Frame;
        public float Elapsed;
    }

    private readonly List<Impact> _impacts = new List<Impact>();
    private readonly List<Shake> _shakes = new List<Shake>();
    public Vector3 Position { get; private set; }
    public Quaternion Rotation { get; private set; } = Quaternion.identity;
    public float Fov { get; private set; }
    public bool Active => _impacts.Count != 0 || _shakes.Count != 0;
    public int ImpactCount => _impacts.Count;
    public int ShakeCount => _shakes.Count;

    public void AddImpact(CombatCameraImpactV2 settings, Quaternion cameraFrame, Vector3 attackDirection)
    {
        if (settings == null || settings.Mode != CombatFeedbackChannelMode.Override || settings.Duration <= 0) return;
        if (settings.Stacking == CombatFeedbackStackMode.Replace) _impacts.Clear();
        _impacts.Add(new Impact { Settings = settings, Frame = Frame(settings.Space, cameraFrame, attackDirection) });
    }

    public void AddShake(CombatCameraShakeV2 settings, Quaternion cameraFrame, Vector3 attackDirection)
    {
        if (settings == null || settings.Mode != CombatFeedbackChannelMode.Override || settings.Duration <= 0) return;
        if (settings.Stacking == CombatFeedbackStackMode.Replace) _shakes.Clear();
        _shakes.Add(new Shake { Settings = settings, Frame = Frame(settings.Space, cameraFrame, attackDirection) });
    }

    public void Tick(float worldDelta, float actorDelta, float unscaledDelta)
    {
        Position = Vector3.zero;
        Rotation = Quaternion.identity;
        Fov = 0;
        EvaluateImpacts(worldDelta, actorDelta, unscaledDelta);
        EvaluateShakes(worldDelta, actorDelta, unscaledDelta);
    }

    private void EvaluateImpacts(float worldDelta, float actorDelta, float unscaledDelta)
    {
        bool strongest = _impacts.Exists(item => item.Settings.Stacking == CombatFeedbackStackMode.Strongest);
        Vector3 sumPosition = Vector3.zero;
        Quaternion sumRotation = Quaternion.identity;
        float sumFov = 0, best = -1;
        for (int i = _impacts.Count - 1; i >= 0; i--)
        {
            var item = _impacts[i];
            var settings = item.Settings;
            item.Elapsed += Delta(settings.Clock, worldDelta, actorDelta, unscaledDelta);
            float elapsed = item.Elapsed - Mathf.Max(0, settings.Delay);
            if (elapsed < 0) continue;
            if (elapsed > settings.Duration) { _impacts.RemoveAt(i); continue; }
            float t = Mathf.Clamp01(elapsed / settings.Duration);
            Vector3 position = item.Frame * EvaluateVector(settings.PositionX, settings.PositionY, settings.PositionZ, t);
            Quaternion rotation = WorldRotation(item.Frame, EvaluateVector(settings.RotationX, settings.RotationY, settings.RotationZ, t));
            float fov = Evaluate(settings.FovOffset, t);
            float strength = position.sqrMagnitude + Quaternion.Angle(Quaternion.identity, rotation) * 0.0001f + Mathf.Abs(fov) * 0.001f;
            if (strongest)
            {
                if (strength <= best) continue;
                best = strength;
                sumPosition = position; sumRotation = rotation; sumFov = fov;
            }
            else { sumPosition += position; sumRotation *= rotation; sumFov += fov; }
        }
        Position += sumPosition;
        Rotation *= sumRotation;
        Fov += sumFov;
    }

    private void EvaluateShakes(float worldDelta, float actorDelta, float unscaledDelta)
    {
        bool strongest = _shakes.Exists(item => item.Settings.Stacking == CombatFeedbackStackMode.Strongest);
        Vector3 sumPosition = Vector3.zero;
        Quaternion sumRotation = Quaternion.identity;
        float best = -1;
        for (int i = _shakes.Count - 1; i >= 0; i--)
        {
            var item = _shakes[i];
            var settings = item.Settings;
            item.Elapsed += Delta(settings.Clock, worldDelta, actorDelta, unscaledDelta);
            float elapsed = item.Elapsed - Mathf.Max(0, settings.Delay);
            if (elapsed < 0) continue;
            if (elapsed > settings.Duration) { _shakes.RemoveAt(i); continue; }
            float t = Mathf.Clamp01(elapsed / settings.Duration);
            float envelope = Evaluate(settings.Envelope, t);
            float phase = elapsed * Mathf.Max(0, settings.Frequency);
            Vector3 linear = Vector3.Scale(settings.Translation, Noise(settings.Seed, phase, 0)) * envelope;
            Vector3 angular = Vector3.Scale(settings.Rotation, Noise(settings.Seed, phase, 3)) * envelope;
            Vector3 position = item.Frame * linear;
            Quaternion rotation = WorldRotation(item.Frame, angular);
            float strength = position.sqrMagnitude + angular.sqrMagnitude * 0.0001f;
            if (strongest)
            {
                if (strength <= best) continue;
                best = strength; sumPosition = position; sumRotation = rotation;
            }
            else { sumPosition += position; sumRotation *= rotation; }
        }
        Position += sumPosition;
        Rotation *= sumRotation;
    }

    public void Clear()
    {
        _impacts.Clear(); _shakes.Clear();
        Position = Vector3.zero; Rotation = Quaternion.identity; Fov = 0;
    }

    public static float Delta(CombatFeedbackClock clock, float world, float actor, float unscaled)
        => Mathf.Max(0, clock == CombatFeedbackClock.Unscaled ? unscaled : clock == CombatFeedbackClock.Actor ? actor : world);

    public static float Evaluate(AnimationCurve curve, float time) => curve == null || curve.length == 0 ? 0 : curve.Evaluate(time);
    public static Vector3 EvaluateVector(AnimationCurve x, AnimationCurve y, AnimationCurve z, float time)
        => new Vector3(Evaluate(x, time), Evaluate(y, time), Evaluate(z, time));
    public static Quaternion Frame(CombatFeedbackSpace space, Quaternion camera, Vector3 attackDirection)
    {
        Vector3 horizontal = Vector3.ProjectOnPlane(attackDirection, Vector3.up);
        return space == CombatFeedbackSpace.World ? Quaternion.identity : space == CombatFeedbackSpace.Attack
            && horizontal.sqrMagnitude > 0.001f ? Quaternion.LookRotation(horizontal.normalized, Vector3.up) : camera;
    }
    public static Quaternion WorldRotation(Quaternion frame, Vector3 euler)
        => frame * Quaternion.Euler(euler) * Quaternion.Inverse(frame);
    private static Vector3 Noise(int seed, float phase, int channel)
    {
        float origin = (seed & 65535) * 0.0137f;
        return new Vector3(Mathf.PerlinNoise(origin + channel * 17.13f, phase) * 2 - 1,
            Mathf.PerlinNoise(origin + (channel + 1) * 17.13f, phase) * 2 - 1,
            Mathf.PerlinNoise(origin + (channel + 2) * 17.13f, phase) * 2 - 1);
    }
}
