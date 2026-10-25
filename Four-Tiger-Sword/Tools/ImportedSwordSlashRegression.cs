// Edit Mode: Unity Pipeline run_script, ImportedSwordSlashRegression.Run.
// Exercise production scheduling and native particle simulation with an injected
// action clock. Temporary objects live in a preview scene and are removed immediately.
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class ImportedSwordSlashRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo TickPlayback = typeof(AttackSlashPlayback).GetMethod("LateUpdate", Hidden);
    private static readonly MethodInfo ClearSlashes = typeof(BaseForm).GetMethod("ClearSlashEffects", Hidden);
    private static readonly FieldInfo ActiveSlashes = typeof(BaseForm).GetField("_activeSlashes", Hidden);

    private sealed class Probe : BaseForm
    {
        public readonly List<int> Events = new List<int>();
        public bool RealSpawn;
        public Probe() : base(null) { }
        public void SetPlayer(PlayerController player) => _playerController = player;
        public void Tick(WeaponActionData step, float time) { _timer = time; UpdateSlashEffects(step); }
        public void Reset() { ClearSlashes.Invoke(this, null); Events.Clear(); }
        public List<GameObject> Effects => (List<GameObject>)ActiveSlashes.GetValue(this);
        protected override void SpawnSlashEffect(WeaponActionData step, int index)
        {
            Events.Add(index);
            if (RealSpawn) base.SpawnSlashEffect(step, index);
        }
    }

    public static string Run()
    {
        if (Application.isPlaying) throw new Exception("Run ImportedSwordSlashRegression in Edit Mode.");
        int checks = 0;
        Action<bool, string> check = (ok, message) =>
        {
            if (!ok) throw new Exception(message);
            checks++;
        };
        var scene = EditorSceneManager.NewPreviewScene();
        var randomState = UnityEngine.Random.state;
        try
        {
            var source = Temporary("Scheduling source", scene);
            VerifyScheduling(source, check);
            VerifyParticles(scene, check);
            VerifyPlacement(source, scene, check);
            return checks + " imported sword slash checks passed: event offsets/overrides, custom starts, " +
                "low-frame catch-up and expiry, native one-shot particle seeking, authored speed, inactive children, " +
                "shared-clock pause/duplicate/backward protection, prefab rotation/scale and follow placement.";
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            UnityEngine.Random.state = randomState;
        }
    }

    private static GameObject Temporary(string name, Scene scene)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        return go;
    }

    private static WeaponActionData Step(GameObject source)
        => new WeaponActionData
        {
            Duration = 1.2f, HitStartTime = .3f, HitDuration = .05f,
            SlashVFX = source, SlashVFXLifetime = .3f,
            HitEvents = new List<HitEvent>
            {
                new HitEvent { StartTime = .3f, Duration = .05f },
                new HitEvent { StartTime = .6f, Duration = .05f }
            }
        };

    private static void VerifyScheduling(GameObject source, Action<bool, string> check)
    {
        var step = Step(source);
        step.Feedback.Slash.TimeOffset = -.1f;
        var probe = new Probe();
        probe.Tick(step, .19f);
        check(probe.Events.Count == 0, "Automatic slash fired before its negative time offset.");
        probe.Tick(step, .21f);
        probe.Tick(step, .21f);
        check(probe.Events.Count == 1 && probe.Events[0] == 0,
            "First shifted slash was missing or duplicated during a paused attack clock.");
        probe.Tick(step, .63f);
        check(probe.Events.Count == 2 && probe.Events[1] == 1,
            "A frame crossing the full second hit window did not catch up its still-live slash.");
        probe.Reset();
        probe.Tick(step, 1.1f);
        check(probe.Events.Count == 0, "Expired skipped slashes flashed after their entire lifetime.");
        probe.Reset();
        probe.Tick(step, .21f);
        check(probe.Events.Count == 1, "Reusing an attack did not reset consumed slash events.");

        probe.Reset();
        step.SlashVFXStartTime = .15f;
        step.Feedback.Slash.TimeOffset = .4f;
        probe.Tick(step, .14f);
        check(probe.Events.Count == 0, "Explicit slash start fired early.");
        probe.Tick(step, .16f);
        probe.Tick(step, .3f);
        check(probe.Events.Count == 1 && probe.Events[0] == -1,
            "Explicit start must ignore automatic offset and play once for the whole attack.");

        probe.Reset();
        step = Step(source);
        step.SlashVFX = null;
        step.HitEvents[1].Feedback.OverrideSlashVFX = true;
        step.HitEvents[1].Feedback.Slash.SlashVFX = source;
        step.HitEvents[1].Feedback.Slash.TimeOffset = -.05f;
        probe.Tick(step, .54f);
        check(probe.Events.Count == 0, "A per-hit override fired before its own start.");
        probe.Tick(step, .56f);
        check(probe.Events.Count == 1 && probe.Events[0] == 1,
            "A per-hit slash override could not play while the attack default was disabled.");

        probe.Reset();
        step = Step(source);
        step.HitEvents[0].Feedback.OverrideSlashVFX = true;
        step.HitEvents[0].Feedback.Slash.SlashVFX = null;
        step.HitEvents[1].Feedback.OverrideSlashVFX = true;
        step.HitEvents[1].Feedback.Slash.SlashVFX = source;
        step.HitEvents[1].Feedback.Slash.SlashVFXStartTime = .4f;
        step.HitEvents[1].Feedback.Slash.TimeOffset = 5f;
        probe.Tick(step, .31f);
        check(probe.Events.Count == 0, "A null per-hit override did not mute its inherited slash.");
        probe.Tick(step, .41f);
        check(probe.Events.Count == 1 && probe.Events[0] == 1,
            "Per-hit custom start must use absolute attack time and ignore automatic offset.");

        probe.Reset();
        step = Step(source);
        step.HitEvents.Clear();
        step.HitStartTime = 0f;
        probe.Tick(step, 0f);
        check(probe.Events.Count == 1 && probe.Events[0] == -1, "A zero-start legacy single hit lost its slash.");
        probe.Tick(null, 0f);
        check(probe.Events.Count == 1, "A null action changed slash playback state.");
    }

    private static void ConfigureParticle(ParticleSystem particles, float speed)
    {
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.duration = 2f;
        main.startLifetime = 2f;
        main.startSpeed = 0f;
        main.startDelay = 0f;
        main.loop = true;
        main.playOnAwake = true;
        main.useUnscaledTime = true;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.simulationSpeed = speed;
        var emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
    }

    private static void VerifyParticles(Scene scene, Action<bool, string> check)
    {
        var root = Temporary("Manual slash particle fixture", scene);
        var particles = root.AddComponent<ParticleSystem>();
        ConfigureParticle(particles, 2f);
        var child = new GameObject("Inactive authored child");
        child.transform.SetParent(root.transform, false);
        var inactive = child.AddComponent<ParticleSystem>();
        ConfigureParticle(inactive, 3f);
        child.SetActive(false);
        var playback = root.AddComponent<AttackSlashPlayback>();
        float clock = .1f;
        playback.Play(.8f, clock, 2f, () => clock);
        check(Mathf.Abs(particles.time - .4f) < .0002f && particles.particleCount == 1,
            "Initial seek did not preserve the authored child simulation speed and playback multiplier.");
        check(!particles.main.loop && !particles.main.playOnAwake && !particles.main.useUnscaledTime
            && particles.main.scalingMode == ParticleSystemScalingMode.Hierarchy,
            "The spawned particle system retained demo looping or independent time/scale settings.");
        check(!inactive.main.loop && !inactive.main.playOnAwake && !inactive.main.useUnscaledTime
            && inactive.main.scalingMode == ParticleSystemScalingMode.Hierarchy && !child.activeSelf,
            "Inactive authored children were not configured, or were unintentionally activated.");
        TickPlayback.Invoke(playback, null);
        check(Mathf.Abs(particles.time - .4f) < .0002f, "The spawn frame advanced the same attack time twice.");
        clock = .15f;
        TickPlayback.Invoke(playback, null);
        check(Mathf.Abs(particles.time - .6f) < .0002f && particles.particleCount == 1,
            "Incremental native particle simulation lost or duplicated the one-shot burst.");
        TickPlayback.Invoke(playback, null);
        check(Mathf.Abs(particles.time - .6f) < .0002f, "Hitstop advanced a paused attack's particles.");
        clock = .05f;
        TickPlayback.Invoke(playback, null);
        check(Mathf.Abs(particles.time - .6f) < .0002f, "A backward clock rewound or replayed the slash.");
        clock = .25f;
        TickPlayback.Invoke(playback, null);
        check(Mathf.Abs(particles.time - 1f) < .0002f,
            "A changed attack-clock rate accumulated independent wall-clock drift.");
        Object.DestroyImmediate(root);
    }

    private static void VerifyPlacement(GameObject source, Scene scene, Action<bool, string> check)
    {
        source.transform.position = new Vector3(0f, 12f, 0f);
        source.transform.rotation = Quaternion.Euler(20f, 40f, 80f);
        source.transform.localScale = new Vector3(1.5f, .7f, 2f);
        var owner = Temporary("Slash placement owner", scene);
        owner.transform.SetPositionAndRotation(new Vector3(40000f, 40000f, 40000f), Quaternion.Euler(0f, 70f, 0f));
        var player = owner.AddComponent<PlayerController>();
        var probe = new Probe { RealSpawn = true };
        probe.SetPlayer(player);
        var step = Step(source);
        step.HitEvents.Clear();
        step.HitBoxOffset = new Vector3(.2f, .3f, .4f);
        step.SlashVFXOffset = new Vector3(-.1f, .8f, -.3f);
        step.SlashVFXRotation = new Vector3(15f, 25f, 35f);
        step.SlashVFXScale = 1.3f;
        step.Feedback.Slash.FollowPlayer = true;
        probe.Tick(step, .31f);
        check(probe.Effects.Count == 1, "The actual spawn path did not create an effect.");
        var effect = probe.Effects[0];
        try
        {
            Vector3 expectedPosition = owner.transform.position
                + owner.transform.rotation * (step.HitBoxOffset + step.SlashVFXOffset);
            Quaternion expectedRotation = owner.transform.rotation * Quaternion.Euler(step.SlashVFXRotation)
                * source.transform.localRotation;
            check(effect.transform.parent == owner.transform
                && Vector3.Distance(effect.transform.position, expectedPosition) < .01f,
                "Follow placement lost the hitbox offset or inherited the prefab's demo translation.");
            check(Quaternion.Angle(effect.transform.rotation, expectedRotation) < .01f,
                "The slash overwrote the prefab's authored orientation.");
            check(Vector3.Distance(effect.transform.lossyScale, source.transform.localScale * 1.3f) < .001f,
                "The slash lost its authored root scale during follow parenting.");
        }
        finally
        {
            if (effect != null) Object.DestroyImmediate(effect);
            probe.Reset();
            Object.DestroyImmediate(owner);
        }
    }
}
