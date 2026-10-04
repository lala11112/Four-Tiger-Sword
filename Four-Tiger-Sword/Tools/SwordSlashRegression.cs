using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;

public static class SwordSlashRegression
{
    sealed class Probe : BaseForm
    {
        public readonly List<int> Events = new();
        public Probe() : base(null) { }
        public void Tick(WeaponActionData step, float time) { _timer = time; UpdateSlashEffects(step); }
        protected override void SpawnSlashEffect(WeaponActionData step, int index) => Events.Add(index);
        public void Reset() => typeof(BaseForm).GetMethod("ClearHitTargets", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(this, null);
    }

    public static string Run()
    {
        int checks = 0;
        Action<bool, string> check = (ok, message) => { if (!ok) throw new Exception(message); checks++; };
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/SlashEffects/WaterSlash.prefab");
        check(prefab != null, "Missing prefab");
        var step = new WeaponActionData { SlashVFX = prefab, Duration = 1f,
            HitEvents = new List<HitEvent> {
                new HitEvent { StartTime = .3f, Duration = .05f },
                new HitEvent { StartTime = .6f, Duration = .05f } } };
        var probe = new Probe();
        probe.Tick(step, .29f);
        check(probe.Events.Count == 0, "Effect fired during wind-up");
        probe.Tick(step, .3f);
        probe.Tick(step, .3f);
        probe.Tick(step, .4f);
        check(probe.Events.Count == 1, "Duplicate during hit stop / repeated frames");
        probe.Tick(step, .9f);
        check(probe.Events.Count == 2, "Skipped narrow hit window at low frame rate");
        probe.Tick(step, 1.5f);
        check(probe.Events.Count == 2, "Repeated effect after completion");
        probe.Reset();
        probe.Tick(step, .3f);
        check(probe.Events.Count == 3, "Next use of same attack did not reset");
        probe.Reset();
        step.SlashVFXStartTime = .2f;
        probe.Tick(step, .19f);
        check(probe.Events.Count == 3, "Custom timing fired early");
        probe.Tick(step, .7f);
        check(probe.Events.Count == 4 && probe.Events[3] == -1, "Custom timing should fire once");
        probe.Reset();
        step.SlashVFXStartTime = -1f;
        step.HitEvents.Clear();
        step.HitStartTime = 0f;
        probe.Tick(step, 0f);
        check(probe.Events.Count == 5, "Zero-start single hit missing");
        probe.Reset();
        step.SlashVFX = null;
        probe.Tick(step, 1f);
        check(probe.Events.Count == 5, "Null prefab should be disabled");
        probe.Tick(null, 1f);
        check(probe.Events.Count == 5, "Null step should be safe");
        string[] assets = { "WaterFromAction", "FireFormAction", "WoodFormAction", "IronFormAction", "EarthFormAction" };
        int steps = 0;
        foreach (string asset in assets)
        {
            var data = AssetDatabase.LoadAssetAtPath<WeaponActionDataSO>("Assets/_Project/Scripts/FormActions/" + asset + ".asset");
            foreach (var action in data.ComboSteps)
            {
                check(action.SlashVFX != null && action.SlashVFXLifetime > 0f, "Unconfigured combo " + asset);
                steps++;
            }
            if (data is WaterFormActionDataSO water)
                check(water.HeavyAttackStep.SlashVFX != null, "Missing heavy slash");
        }
        return checks + " checks passed, " + steps + " combo steps and water heavy attack configured.";
    }
}
