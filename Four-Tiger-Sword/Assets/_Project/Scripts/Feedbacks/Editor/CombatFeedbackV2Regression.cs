using System;
using UnityEngine;

/// <summary>Explicit editor regression entry point; does not edit or save project assets.</summary>
public static class CombatFeedbackV2Regression
{
    public static string Run()
    {
        var defaults = new CombatCameraV2Settings();
        Require(!defaults.HasOverrides, "New camera data must inherit the authored route.");
        var hitDefaults = new CombatCameraHitSettings();
        Require(!hitDefaults.HasOverrides, "A compact hit camera must inherit until a hit channel is authored.");
        hitDefaults.PreserveFeelFlash = false;
        Require(hitDefaults.HasOverrides, "A hit-only flash policy must activate without attack motion settings.");
        hitDefaults.PreserveFeelFlash = true;
        hitDefaults.Shake.Mode = CombatFeedbackChannelMode.Off;
        Require(hitDefaults.HasOverrides, "A compact hit camera can explicitly suppress an inherited shake.");
        CombatCameraHitSettings hitView = defaults;
        defaults.Mode = CombatFeedbackChannelMode.Override;
        Require(hitView.HasOverrides, "Full attack camera settings retain their override gate through the hit-camera interface.");
        defaults.Mode = CombatFeedbackChannelMode.Inherit;
        var profile = ScriptableObject.CreateInstance<AttackCameraProfile>();
        try
        {
            Require(!profile.EnableV2, "An existing profile must not opt into absolute playback by default.");
            profile.EnableV2 = true;
            profile.V2.Mode = CombatFeedbackChannelMode.Override;
            profile.V2.TriggerPolicy = CombatFeedbackTriggerPolicy.FirstPerStep;
            profile.V2.Motion.FovOffset = AnimationCurve.Linear(0, -3, 1, 0);
            Require(ReferenceEquals(CombatCameraV2Settings.Resolve(profile, null), profile.V2),
                "An absent inline camera directly inherits the existing shared profile.");
            var inline = new CombatCameraV2Settings { Authored = true,
                TriggerPolicy = CombatFeedbackTriggerPolicy.FirstPerHitEvent, PreserveFeelFlash = false };
            var effective = CombatCameraV2Settings.Resolve(profile, inline);
            Require(effective.Mode == CombatFeedbackChannelMode.Override
                && effective.Motion.FovOffset == profile.V2.Motion.FovOffset,
                "An inherited inline motion channel must use the opted-in profile's v2 motion.");
            Require(effective.TriggerPolicy == CombatFeedbackTriggerPolicy.FirstPerHitEvent && !effective.PreserveFeelFlash,
                "Explicit default-valued policies and standalone flash flags must be applied.");
            Require(inline.Mode == CombatFeedbackChannelMode.Inherit && profile.V2.TriggerPolicy == CombatFeedbackTriggerPolicy.FirstPerStep,
                "Resolving camera channels must never modify inline or source profile authoring.");
        }
        finally { UnityEngine.Object.DestroyImmediate(profile); }

        var gate = new CombatFeedbackHitGate();
        Require(gate.Accept(CombatFeedbackTriggerPolicy.FirstPerHitEvent, 0, 10), "First target of an event must play.");
        Require(!gate.Accept(CombatFeedbackTriggerPolicy.FirstPerHitEvent, 0, 11), "A second target in the same hit must not multiply screen feedback.");
        Require(gate.Accept(CombatFeedbackTriggerPolicy.FirstPerHitEvent, 1, 10), "The same enemy in a different hit must play again.");
        gate.Reset();
        Require(gate.Accept(CombatFeedbackTriggerPolicy.EveryTarget, 0, 10)
            && gate.Accept(CombatFeedbackTriggerPolicy.EveryTarget, 0, 11)
            && !gate.Accept(CombatFeedbackTriggerPolicy.EveryTarget, 0, 10), "EveryTarget must deduplicate repeated collider hits.");
        gate.Reset();
        Require(gate.Accept(CombatFeedbackTriggerPolicy.FirstPerStep, 0, 10)
            && !gate.Accept(CombatFeedbackTriggerPolicy.FirstPerStep, 1, 10), "Legacy first-step policy must suppress later hit events.");

        var mixer = new CombatCameraV2Mixer();
        var impact = ConstantImpact(0.2f);
        impact.Delay = 0.1f;
        impact.Clock = CombatFeedbackClock.Actor;
        mixer.AddImpact(impact, Quaternion.identity, Vector3.forward);
        mixer.Tick(0.2f, 0, 0.2f);
        Require(mixer.Position == Vector3.zero, "Actor-timed delay must freeze during local hit stop.");
        mixer.Tick(0.1f, 0.1f, 0.1f);
        Near(mixer.Position.x, 0.2f, "Actor-timed kick must start after its delay.");
        mixer.Clear();
        impact.Clock = CombatFeedbackClock.Unscaled;
        mixer.AddImpact(impact, Quaternion.identity, Vector3.forward);
        mixer.Tick(0, 0, 0.1f);
        Near(mixer.Position.x, 0.2f, "Unscaled kick must advance while game and actor clocks are stopped.");
        mixer.Tick(0, 0, 0.4f);
        Require(!mixer.Active && mixer.Position == Vector3.zero, "Completed tracks must restore a neutral output.");

        mixer.Clear();
        var small = ConstantImpact(0.2f);
        small.Stacking = CombatFeedbackStackMode.Add;
        var large = ConstantImpact(0.4f);
        large.Stacking = CombatFeedbackStackMode.Add;
        mixer.AddImpact(small, Quaternion.identity, Vector3.forward);
        mixer.AddImpact(large, Quaternion.identity, Vector3.forward);
        mixer.Tick(0.01f, 0.01f, 0.01f);
        Near(mixer.Position.x, 0.6f, "Add must sum concurrent impacts.");
        large.Stacking = CombatFeedbackStackMode.Replace;
        mixer.AddImpact(large, Quaternion.identity, Vector3.forward);
        Require(mixer.ImpactCount == 1, "Replace must replace only our impact tracks.");
        mixer.Clear();
        large.Stacking = CombatFeedbackStackMode.Strongest;
        mixer.AddImpact(small, Quaternion.identity, Vector3.forward);
        mixer.AddImpact(large, Quaternion.identity, Vector3.forward);
        mixer.Tick(0.01f, 0.01f, 0.01f);
        Near(mixer.Position.x, 0.4f, "Strongest must select one signal rather than sum it.");

        mixer.Clear();
        var shake = new CombatCameraShakeV2 { Mode = CombatFeedbackChannelMode.Override,
            Translation = new Vector3(0.3f, 0.2f, 0.1f), Rotation = new Vector3(0, 0, 2),
            Frequency = 20, Duration = 0.3f, Envelope = AnimationCurve.Linear(0, 1, 1, 1) };
        mixer.AddShake(shake, Quaternion.identity, Vector3.forward);
        mixer.Tick(0, 0, 0.05f);
        Require(mixer.Position.sqrMagnitude > 0.000001f, "Absolute shake must work without an ImpulseSource or a nonzero original amplitude.");
        var secondMixer = new CombatCameraV2Mixer();
        secondMixer.AddShake(shake, Quaternion.identity, Vector3.forward);
        secondMixer.Tick(0, 0, 0.05f);
        Require((mixer.Position - secondMixer.Position).sqrMagnitude < 0.000001f,
            "Same authored shake seed and elapsed time must reproduce the same signal.");
        shake.Mode = CombatFeedbackChannelMode.Off;
        var offMixer = new CombatCameraV2Mixer();
        offMixer.AddShake(shake, Quaternion.identity, Vector3.forward);
        Require(!offMixer.Active, "Off must not create an inherited or absolute shake.");
        Require(CombatCameraV2Mixer.Frame(CombatFeedbackSpace.World, Quaternion.Euler(0, 90, 0), Vector3.right) == Quaternion.identity,
            "World-space feedback must not inherit view rotation.");
        Require((CombatCameraV2Mixer.Frame(CombatFeedbackSpace.Attack, Quaternion.identity, Vector3.right) * Vector3.forward - Vector3.right).sqrMagnitude < 0.000001f,
            "Attack-space forward must follow the attack direction.");
        return "CombatFeedbackV2: inheritance, per-hit/target gates, clocks/delay, stacking, absolute zero-source shake and spaces passed.";
    }

    private static CombatCameraImpactV2 ConstantImpact(float value)
        => new CombatCameraImpactV2 { Mode = CombatFeedbackChannelMode.Override, Duration = 0.2f,
            PositionX = AnimationCurve.Linear(0, value, 1, value) };
    private static void Near(float actual, float expected, string message)
        => Require(Mathf.Abs(actual - expected) < 0.0001f, message + " actual=" + actual + " expected=" + expected);
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
