using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Isolated world-time ownership checks; no assets or live gameplay objects are changed.</summary>
public static class CombatWorldScaleRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run world-scale fixtures in Edit Mode.");
        float savedScale = Time.timeScale;
        var existingServices = new HashSet<CombatHitStopService>(Resources.FindObjectsOfTypeAll<CombatHitStopService>());
        var hosts = new List<GameObject>();
        CombatActorClock actor = null;
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }
        void Near(float actual, float expected, string message)
            => Check(Mathf.Abs(actual - expected) < .0001f, message + $" (actual {actual}, expected {expected})");
        GameObject Host(string name)
        {
            var host = new GameObject(name);
            hosts.Add(host);
            return host;
        }
        try
        {
            var a = Host("World scale owner A");
            var b = Host("World scale owner B");
            var hitOwner = Host("Hit stop owner");
            var shopOwner = Host("Shop pause owner");
            CombatHitStopService.SetExternalWorldTimeScale(.75f);
            var slow = CombatHitStopService.AcquireWorldScale(a, .05f);
            Check(slow.IsValid, "A live owner acquires world slow motion");
            Near(Time.timeScale, .0375f, "Owned multiplier composes with logical world speed");
            Near(CombatHitStopService.ModalTimeMultiplier, .05f, "Realtime combat sees modal slowdown");
            var second = CombatHitStopService.AcquireWorldScale(b, .2f);
            Near(Time.timeScale, .0375f, "Concurrent owners retain the strongest slowdown");
            slow.Dispose();
            Near(Time.timeScale, .15f, "Releasing one owner retains the other multiplier");
            slow.Dispose();
            Near(Time.timeScale, .15f, "Repeated release is idempotent");
            second.Dispose();
            Near(Time.timeScale, .75f, "Final release restores logical external speed");

            var stop = new HitStopSettings { Scope = HitStopScope.Global, HitStopDuration = 30f, HitStopTimeScale = 0f };
            CombatHitStopService.Apply(hitOwner, null, null, stop);
            var service = (CombatHitStopService)typeof(CombatHitStopService)
                .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var stops = (IList)typeof(CombatHitStopService).GetField("_stops", Hidden).GetValue(service);
            Check(stops.Count == 1, "Fixture owns one active global stop");
            slow = CombatHitStopService.AcquireWorldScale(a, .05f);
            Near(Time.timeScale, 0f, "Acquiring slowdown retains a stronger active hit stop");
            Check(stops.Count == 1, "Acquiring slowdown does not cancel global hit stop");
            Near(CombatHitStopService.ModalTimeMultiplier, .05f, "Modal multiplier excludes global hit stop");
            slow.Dispose();
            Near(Time.timeScale, 0f, "Disposing slowdown does not release another owner's hit stop");
            Near(CombatHitStopService.ModalTimeMultiplier, 1f, "Realtime combat ignores ordinary global hit stop");
            CombatHitStopService.ReleaseOwner(hitOwner);
            Near(Time.timeScale, .75f, "Hit-stop release restores the logical speed after slowdown closes");

            stop.HitStopTimeScale = .01f;
            CombatHitStopService.Apply(hitOwner, null, null, stop);
            slow = CombatHitStopService.AcquireWorldScale(a, .05f);
            Near(Time.timeScale, .01f, "A stronger fractional hit stop composes with modal slowdown");
            var pendingStop = stops[0];
            pendingStop.GetType().GetField("Expires").SetValue(pendingStop, -1d);
            Near(CombatHitStopService.ModalTimeMultiplier, .05f, "Realtime expiry retains modal ownership");
            Check(stops.Count == 0, "Expired hit stops are pruned while modal slowdown is active");
            Near(Time.timeScale, .0375f, "Hit-stop expiry resumes the remaining modal slowdown");
            slow.Dispose();
            Near(Time.timeScale, .75f, "Final modal release restores world speed after hit-stop expiry");

            actor = Host("Actor clock fixture").AddComponent<CombatActorClock>();
            Invoke(actor, "Awake");
            Invoke(actor, "OnEnable");
            actor.Request(hitOwner, 30f, 0f, HitStopStacking.Replace);
            slow = CombatHitStopService.AcquireWorldScale(a, .05f);
            Near(actor.TimeScale, 0f, "Acquiring world slowdown retains local hit stop");
            slow.Dispose();
            Near(actor.TimeScale, 0f, "Releasing world slowdown retains local hit stop");
            CombatHitStopService.ReleaseOwner(hitOwner);

            slow = CombatHitStopService.AcquireWorldScale(a, .05f);
            var pause = CombatHitStopService.AcquireWorldScale(shopOwner, 0f);
            Near(Time.timeScale, 0f, "A shop owner composes a full pause with selection slowdown");
            Near(CombatHitStopService.ModalTimeMultiplier, 0f, "Realtime combat pauses for a modal world pause");
            pause.Dispose();
            Near(Time.timeScale, .0375f, "Releasing shop pause retains selection slowdown");
            CombatHitStopService.ClearAll("world-scale-regression");
            Check(slow.IsValid, "Clearing hit stops preserves owned modal slowdown");
            Near(Time.timeScale, .0375f, "Hit-stop cleanup does not resume modal world time");
            CombatHitStopService.SetExternalWorldTimeScale(0f);
            Check(!slow.IsValid, "An external pause invalidates owned slowdown");
            slow.Dispose();
            Near(Time.timeScale, 0f, "Disposing invalid slowdown preserves external pause");
            Near(CombatHitStopService.ModalTimeMultiplier, 0f, "Realtime combat respects external pause");
            Check(!CombatHitStopService.AcquireWorldScale(a, .05f).IsValid, "A genuine external pause rejects acquisition");

            CombatHitStopService.SetExternalWorldTimeScale(.75f);
            pause = CombatHitStopService.AcquireWorldScale(shopOwner, 0f);
            ulong revision = CombatHitStopService.ExternalWorldTimeRevision;
            CombatHitStopService.SetExternalWorldTimeScale(0f);
            Check(CombatHitStopService.ExternalWorldTimeRevision != revision && !pause.IsValid,
                "An external same-zero write invalidates the old pause owner");
            pause.Dispose();
            Near(Time.timeScale, 0f, "Old pause release cannot resume an external same-zero pause");

            CombatHitStopService.SetExternalWorldTimeScale(.75f);
            slow = CombatHitStopService.AcquireWorldScale(a, .05f);
            a.SetActive(false);
            Check(!slow.IsValid, "Disabled owners lose their request");
            Near(Time.timeScale, .75f, "Disabled-owner pruning restores world speed");
            a.SetActive(true);
            slow = CombatHitStopService.AcquireWorldScale(a, .05f);
            Object.DestroyImmediate(a);
            Near(CombatHitStopService.ModalTimeMultiplier, 1f, "Destroyed owners cannot strand modal slowdown");
            Check(!slow.IsValid, "Destroyed-owner leases become invalid");
            Near(Time.timeScale, .75f, "Destroyed-owner pruning restores world speed");
            slow = CombatHitStopService.AcquireWorldScale(b, .05f);
            Time.timeScale = 0f;
            Check(!slow.IsValid, "Direct external scale changes relinquish owned time");
            slow.Dispose();
            Near(Time.timeScale, 0f, "Release preserves directly observed external pause");
            return $"PASS: combat world scale ownership ({checks} checks).";
        }
        finally
        {
            CombatHitStopService.SetExternalWorldTimeScale(savedScale);
            if (actor != null) Invoke(actor, "OnDisable");
            foreach (var host in hosts) if (host != null) Object.DestroyImmediate(host);
            foreach (var service in Resources.FindObjectsOfTypeAll<CombatHitStopService>())
                if (!existingServices.Contains(service)) Object.DestroyImmediate(service.gameObject);
        }
    }

    private static void Invoke(object target, string method)
        => target.GetType().GetMethod(method, Hidden).Invoke(target, null);
}
