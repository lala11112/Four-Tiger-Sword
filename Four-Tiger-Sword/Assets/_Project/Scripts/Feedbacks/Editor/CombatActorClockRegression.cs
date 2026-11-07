using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

/// <summary>Isolated edit-mode fixtures; no scene assets, player settings or navigation meshes are changed.</summary>
public static class CombatActorClockRegression
{
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run clock unit fixtures in Edit Mode.");
        float worldScale = Time.timeScale;
        var owned = new List<GameObject>();
        var telemetry = new List<CombatFeedbackTelemetryEvent>();
        CombatFeedbackTelemetry.Recorded += telemetry.Add;
        int checks = 0;
        void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); checks++; }
        void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) < .0001f,
            message + " actual=" + actual + " expected=" + expected);
        CombatActorClock Actor(string name)
        {
            var host = new GameObject(name); owned.Add(host);
            host.AddComponent<Animator>();
            var nav = host.AddComponent<NavMeshAgent>();
            nav.speed = 8; nav.acceleration = 24; nav.angularSpeed = 120;
            var clock = host.AddComponent<CombatActorClock>();
            // Runtime-only components intentionally do not execute in the editor. Initialize this isolated fixture.
            InvokeLifecycle(clock, "Awake"); InvokeLifecycle(clock, "OnEnable");
            return clock;
        }
        try
        {
            var a = Actor("Clock fixture A"); var b = Actor("Clock fixture B");
            var ownerA = new GameObject("Clock owner A"); owned.Add(ownerA);
            var ownerB = new GameObject("Clock owner B"); owned.Add(ownerB);
            var nav = a.GetComponent<NavMeshAgent>();
            var animator = a.GetComponent<Animator>();
            var defaults = new HitStopSettings();
            Check(defaults.Scope == HitStopScope.Global && defaults.Stacking == HitStopStacking.Replace,
                "Older serialized settings must retain global/replace defaults.");
            a.SetAnimationSpeed(2);
            object slow = new object();
            a.SetMovementModifier(slow, .5f);
            Near(nav.speed, 4, "Slow applies to base navigation speed.");
            a.Request(ownerA, 10, .25f, HitStopStacking.Replace);
            Near(a.TimeScale, .25f, "Fractional local hit stop remains a time scale.");
            Near(animator.speed, .5f, "Local time multiplies the authored animation speed.");
            Near(nav.speed, 1, "Slow and local time compose without replacing each other.");
            Near(nav.acceleration, 3, "Acceleration composes with local time and slow.");
            a.RemoveMovementModifier(slow);
            Near(nav.speed, 2, "Expiring slow preserves a currently active hit stop.");
            a.SetAnimationSpeed(3);
            Near(animator.speed, .75f, "Changing attack speed during a stop keeps the local multiplier.");
            a.Request(ownerB, 10, .5f, HitStopStacking.Replace);
            Near(a.TimeScale, .25f, "Concurrent owners retain the strongest active local stop.");
            CombatHitStopService.ReleaseOwner(ownerA);
            Near(a.TimeScale, .5f, "Releasing one owner preserves another owner's stop.");
            CombatHitStopService.ReleaseOwner(ownerB);
            Near(nav.speed, 8, "Navigation restores the unscaled speed after all local stops.");
            Near(animator.speed, 3, "Animation restores its most recent authored speed.");

            CombatHitStopService.SetExternalWorldTimeScale(.75f);
            var settings = new HitStopSettings { HitStopDuration = 10, HitStopTimeScale = 0, Scope = HitStopScope.Attacker };
            CombatHitStopService.Apply(ownerA, a, b, settings);
            Near(a.TimeScale, 0, "Attacker scope stops the attacker.");
            Near(b.TimeScale, 1, "Attacker scope preserves victim time.");
            Near(Time.timeScale, .75f, "Local scopes never modify world time.");
            var input = new InputBuffer(); input.Set(.15f); input.Update(a.DeltaTime);
            Check(input.IsActive, "Buffered input retains its lifetime during a complete actor stop.");
            CombatHitStopService.ClearAll();

            var enemy = a.gameObject.AddComponent<Enemy>();
            enemy.EnemyStat = a.GetComponent<EnemyStat>(); enemy.EnemyStat.CurrentHp = 100;
            enemy.CombatLabPassive = true;
            enemy.CombatLabPassive = false;
            Check(!enemy.CombatLabPassive, "Passive toggles before Start do not require an initialized state machine.");
            var body = a.gameObject.AddComponent<CapsuleCollider>();
            var brain = new FixtureBrain(enemy);
            brain.Initialize();
            var machine = brain.StateMachine;
            var activeAction = new FixtureState(() => body.enabled = false);
            machine.ChangeState(activeAction);
            typeof(Enemy).GetProperty(nameof(Enemy.Brain)).SetValue(enemy, brain);
            enemy.CombatLabPassive = true;
            Check(body.enabled, "Passive transition preserves a live body collider when an interrupted state restores a stale disabled snapshot.");
            var reaction = new FixtureReactionState();
            machine.ChangeState(reaction); enemy.IsHurt = true;
            enemy.CombatLabPassive = false;
            enemy.CombatLabPassive = true;
            Check(ReferenceEquals(machine.CurrentState, reaction) && enemy.IsHurt,
                "Passive mode preserves an active victim reaction instead of forcing idle.");
            enemy.IsHurt = false;

            settings.Scope = HitStopScope.Victim;
            CombatHitStopService.Apply(ownerA, a, b, settings);
            Near(a.TimeScale, 1, "Victim scope preserves attacker time.");
            Near(b.TimeScale, 0, "Victim scope stops the victim.");
            CombatHitStopService.ClearAll();
            settings.Scope = HitStopScope.Both; settings.HitStopTimeScale = .2f;
            CombatHitStopService.Apply(ownerA, a, b, settings);
            Near(a.TimeScale, .2f, "Both scope slows attacker.");
            Near(b.TimeScale, .2f, "Both scope slows victim.");
            a.Request(ownerA, 10, .8f, HitStopStacking.Replace);
            Near(a.TimeScale, .8f, "Replace can replace an earlier stronger stop from the same owner.");
            a.Request(ownerA, 1, .9f, HitStopStacking.Maximum);
            Near(a.TimeScale, .8f, "Maximum preserves the strongest scale.");
            CombatHitStopService.ClearAll();
            Near(a.TimeScale, 1, "Scene/disposal cleanup releases attacker tokens.");
            Near(b.TimeScale, 1, "Scene/disposal cleanup releases victim tokens.");

            settings.Scope = HitStopScope.Global; settings.HitStopTimeScale = .25f;
            CombatHitStopService.Apply(ownerA, a, b, settings);
            Near(Time.timeScale, .25f, "Global stop uses the authored global time scale.");
            settings.HitStopTimeScale = .1f;
            CombatHitStopService.Apply(ownerB, a, b, settings);
            Near(Time.timeScale, .1f, "Global stops compose across owners.");
            CombatHitStopService.ReleaseOwner(ownerB);
            Near(Time.timeScale, .25f, "Releasing a global owner preserves remaining stops.");
            CombatHitStopService.ReleaseOwner(ownerA);
            Near(Time.timeScale, .75f, "Final global cleanup restores the previous external scale.");
            settings.HitStopTimeScale = 0;
            CombatHitStopService.Apply(ownerA, a, b, settings);
            CombatHitStopService.SetExternalWorldTimeScale(0);
            CombatHitStopService.ReleaseOwner(ownerA);
            Near(Time.timeScale, 0, "An external pause with the same zero scale cannot be undone by an expired owner.");
            a.Request(ownerA, 10, 0, HitStopStacking.Replace);
            Object.DestroyImmediate(ownerA);
            Near(a.TimeScale, 1, "Destroyed owners cannot leave local tokens stuck.");
            Check(telemetry.Exists(e => e.kind == "hitstop-begin" && e.targetId == a.gameObject.GetInstanceID()
                && e.detail.Contains("scope=Attacker") && e.detail.Contains("applied=0;")),
                "Local stops record their actual target, scope and applied scale.");
            Check(telemetry.Exists(e => e.kind == "hitstop-change" && e.detail.Contains("reason=merged-lease")),
                "Stacked requests record a changed lease.");
            Check(telemetry.Exists(e => e.kind == "hitstop-end" && e.detail.Contains("reason=owner-destroyed")),
                "Destroyed-owner pruning records its release reason.");
            Check(telemetry.Exists(e => e.kind == "hitstop-end" && e.detail.Contains("scope=Global")
                && e.detail.Contains("reason=external-world-time")),
                "An external pause records why the global stop ended.");
            return "CombatActorClock: " + checks + " scope, composition, owner cleanup, buffer and pause checks passed.";
        }
        finally
        {
            CombatFeedbackTelemetry.Recorded -= telemetry.Add;
            CombatHitStopService.SetExternalWorldTimeScale(worldScale);
            foreach (var host in owned)
            {
                if (host == null) continue;
                InvokeLifecycle(host.GetComponent<CombatActorClock>(), "OnDisable");
                Object.DestroyImmediate(host);
            }
            foreach (var service in Resources.FindObjectsOfTypeAll<CombatHitStopService>())
                Object.DestroyImmediate(service.gameObject);
            Time.timeScale = worldScale;
        }
    }

    // SendMessage routes lifecycle names through Unity's runtime behaviour gate,
    // which asserts for a runtime-only MonoBehaviour in Edit Mode. Invoke only
    // these explicitly owned fixtures directly; never mark production code ExecuteAlways.
    private static void InvokeLifecycle(CombatActorClock clock, string method)
    {
        if (clock == null) return;
        var callback = typeof(CombatActorClock).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
        if (callback == null) throw new MissingMethodException(typeof(CombatActorClock).FullName, method);
        callback.Invoke(clock, null);
    }

    private class FixtureState : IPlayerState
    {
        private readonly Action _onExit;
        public FixtureState(Action onExit) => _onExit = onExit;
        public void Enter() { }
        public void Update() { }
        public void Exit() => _onExit?.Invoke();
    }
    private sealed class FixtureBrain : EnemyBrainBase
    {
        public FixtureBrain(Enemy enemy) : base(enemy) { }
        public StateMachine StateMachine => Machine;
        protected override void BuildBehavior()
        {
            IdleState = new FixtureIdleState();
            ReturnState = IdleState;
        }
    }
    private sealed class FixtureIdleState : FixtureState { public FixtureIdleState() : base(null) { } }
    private sealed class FixtureReactionState : FixtureState { public FixtureReactionState() : base(null) { } }
}
