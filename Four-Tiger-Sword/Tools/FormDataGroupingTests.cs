// Ephemeral Unity Pipeline run_script verification. Entry: FormDataGroupingTests.Run.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEditor;
using UnityEngine;
using StudioBaekho.CombatLab;
using StudioBaekho.CombatLab.FourTigerSword;
using Object = UnityEngine.Object;

public static class FormDataGroupingTests
{
    static int checks, actions, curves, hits, properties;
    const BindingFlags Public = BindingFlags.Instance | BindingFlags.Public;
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    static void Equal(float actual, float expected, string label) => Check(Mathf.Approximately(actual, expected), label + ": " + actual + " != " + expected);
    static string Json(object value) => value is Object obj ? EditorJsonUtility.ToJson(obj) : JsonUtility.ToJson(value);
    static IEnumerable<WeaponActionData> Steps(WeaponActionDataSO data) => (data.ComboSteps ?? new List<WeaponActionData>())
        .Concat(data.SkillSteps ?? new List<WeaponActionData>()).Concat(data.UltimateSteps ?? new List<WeaponActionData>())
        .Concat(new[] { data.AirAttackStep, data.CounterStep, (data as WaterFormActionDataSO)?.HeavyAttackStep }).Where(s => s != null);

    public static string Run()
    {
        Check(!Application.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode, "Edit Mode required");
        checks = actions = curves = hits = properties = 0;
        var names = new[] { "WaterFromAction", "FireFormAction", "WoodFormAction", "EarthFormAction", "IronFormAction" };
        var sources = names.Select(n => AssetDatabase.LoadAssetAtPath<WeaponActionDataSO>("Assets/_Project/Scripts/FormActions/" + n + ".asset")).ToArray();
        Check(sources.All(s => s != null), "All five live form assets must load");
        var originals = sources.Cast<Object>().Concat(sources.SelectMany(Steps).Select(s => s.CameraProfile).Where(p => p != null)).Distinct().ToDictionary(o => o, Json);
        var adapter = new FourTigerCombatAdapter();
        var skipped = new List<string>();
        try
        {
            foreach (var source in sources)
            {
                var clone = Object.Instantiate(source);
                try { CopyFields(source, clone, source.name); }
                finally { Object.DestroyImmediate(clone); }
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
                foreach (var action in adapter.DiscoverActions(guid))
                {
                    if (!action.available) { skipped.Add(source.name + ":" + action.id); continue; }
                    foreach (bool changed in new[] { false, true })
                    {
                        var owned = new List<Object>();
                        try { var copy = FourTigerCombatAdapter.CloneAndApply(source, Candidate(source, action.id, changed), owned, action.id); Applied(source, copy, action.id, changed); }
                        finally { for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]); }
                        Check(originals.All(p => Json(p.Key) == p.Value), "Adapter changed original asset or camera profile: " + source.name + "/" + action.id);
                    }
                    actions++;
                }
                SpecialMetrics(source, false); SpecialMetrics(source, true);
            }
            Export(adapter, (WaterFormActionDataSO)sources[0]);
            Check(curves > 0 && hits > 0 && properties > 0, "Deep-copy/property coverage was empty");
        }
        finally { Check(originals.All(p => Json(p.Key) == p.Value), "A source asset changed during grouping verification"); }
        return "PASS: 5 forms; " + actions + " available actions x baseline/changed; " + curves + " curves; " + hits + " hits; " + properties + " forwarding properties; " + checks + " assertions. Export removed. Unavailable: " + string.Join(", ", skipped);
    }

    static void CopyFields(object source, object copy, string path)
    {
        Check(copy != null && !ReferenceEquals(source, copy), "Shared grouped object: " + path);
        if (copy is HitEvent) hits++;
        if (copy is WeaponActionDataSO || copy is WeaponActionData || copy is HitEvent)
            foreach (var p in copy.GetType().GetProperties(Public).Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && (p.DeclaringType == copy.GetType() || p.DeclaringType == typeof(WeaponActionDataSO))))
            {
                object old = p.GetValue(copy), value;
                if (p.PropertyType == typeof(float)) value = (float)old + 0.123f;
                else if (p.PropertyType == typeof(int)) value = (int)old + 1;
                else if (p.PropertyType == typeof(bool)) value = !(bool)old;
                else if (p.PropertyType == typeof(string)) value = (string)old + "__group_test";
                else if (p.PropertyType == typeof(Vector3)) value = (Vector3)old + Vector3.one;
                else if (p.PropertyType.IsEnum) value = Enum.GetValues(p.PropertyType).Cast<object>().FirstOrDefault(v => !v.Equals(old));
                else continue;
                if (value == null) continue;
                string before = Json(copy);
                try { p.SetValue(copy, value); Check(p.GetValue(copy).Equals(value) && Json(copy) != before, "Forwarding property not serialized: " + path + "." + p.Name); properties++; }
                finally { p.SetValue(copy, old); }
            }
        foreach (var field in source.GetType().GetFields(Public).Where(f => !f.IsNotSerialized))
        {
            object a = field.GetValue(source), b = field.GetValue(copy); string child = path + "." + field.Name;
            if (a == null) { Check(b == null, "Null changed: " + child); continue; }
            if (a is Object || a is string || a.GetType().IsValueType) { Check(a.Equals(b), "Serialized value/reference changed: " + child); continue; }
            if (a is AnimationCurve curve)
            {
                var copied = b as AnimationCurve; Check(copied != null && !ReferenceEquals(curve, copied), "Shared curve: " + child);
                var keys = curve.keys; Check(keys.SequenceEqual(copied.keys), "Curve keys changed: " + child);
                copied.keys = new[] { new Keyframe(0f, 987.25f), new Keyframe(1f, 654.5f) };
                Check(curve.keys.SequenceEqual(keys), "Curve mutation reached source: " + child); curves++; copied.keys = keys; continue;
            }
            if (a is IList list)
            {
                var other = b as IList; Check(other != null && !ReferenceEquals(list, other) && list.Count == other.Count, "Shared/different list: " + child);
                for (int i = 0; i < list.Count; i++) if (list[i] != null) CopyFields(list[i], other[i], child + "[" + i + "]");
                continue;
            }
            CopyFields(a, b, child);
        }
    }

    static CombatCandidate Candidate(WeaponActionDataSO source, string action, bool changed)
    {
        return new CombatCandidate { id = changed ? "changed" : "baseline", label = "Grouping verification", values = FourTigerCombatAdapter.ParameterSpecs(source, action).Select(s =>
            new ParameterValue(s.id, !changed || s.id == FourTigerCombatAdapter.HitStopId ? s.baseline : Mathf.Clamp(s.id.EndsWith("Multiplier", StringComparison.Ordinal) ? 0.5f : s.baseline * 0.5f, s.minimum, s.maximum))).ToList() };
    }
    static float Value(CombatCandidate candidate, string id) => candidate.values.Single(v => v.id == id).value;
    static void Applied(WeaponActionDataSO source, WeaponActionDataSO copy, string action, bool changed)
    {
        var candidate = Candidate(source, action, changed);
        Action<string, string, bool> field = (property, parameter, multiply) => {
            var p = source.GetType().GetProperty(property, Public);
            Equal((float)p.GetValue(copy), Value(candidate, parameter) * (multiply ? (float)p.GetValue(source) : 1f), action + "." + property);
        };
        if (action == "skill" && source is WoodFormActionDataSO)
        { field("SkillRadius", "skillRadiusMultiplier", true); field("PullSpeed", "pullSpeedMultiplier", true); field("SkillDamageMultiplier", "skillDamageMultiplier", true); return; }
        if (action == "ultimate")
        {
            if (source is WaterFormActionDataSO) { field("AreaRadius", "areaRadiusMultiplier", true); Equal(copy.UltimateSteps[0].CameraShakeStrength, source.UltimateSteps[0].CameraShakeStrength * Value(candidate, FourTigerCombatAdapter.CameraId), "Water ultimate shake"); return; }
            if (source is FireFormActionDataSO) { field("FireUltimateDamageDealtBonusPercent", "ultimateDamageBonusPercent", false); field("FireUltimateAttackSpeedBonusPercent", "ultimateAttackSpeedBonusPercent", false); field("FireUltimateDamageTakenBonusPercent", "ultimateDamageTakenBonusPercent", false); return; }
            if (source is EarthFormActionDataSO) { field("UltimateShieldAmount", "ultimateShieldMultiplier", true); field("UltimateBuffDuration", "ultimateBuffDuration", false); field("UltimateShieldDamageBonusPercent", "ultimateDamageBonusPercent", false); return; }
            if (source is WoodFormActionDataSO) { field("UltimateRadius", "ultimateRadiusMultiplier", true); field("UltimateFieldDuration", "ultimateFieldDuration", false); field("ElementDamageBonusPercent", "ultimateElementBonusPercent", false); return; }
        }
        var selected = new HashSet<WeaponActionData>(FourTigerCombatAdapter.SelectedSteps(source, action));
        var before = Steps(source).ToArray(); var after = Steps(copy).ToArray(); Check(before.Length == after.Length, "Action count changed");
        for (int i = 0; i < before.Length; i++)
        {
            var a = before[i]; var b = after[i]; float k = selected.Contains(a) ? Value(candidate, FourTigerCombatAdapter.KnockbackId) : 1f;
            float c = selected.Contains(a) ? Value(candidate, FourTigerCombatAdapter.CameraId) : 1f;
            Equal(b.KnockbackForce, a.KnockbackForce * k, "Knockback"); Equal(b.Damage, a.Damage, "Damage preserved"); Equal(b.HitStopDuration, a.HitStopDuration, "Hit stop preserved");
            if (a.CameraProfile == null) Equal(b.CameraShakeStrength, a.CameraShakeStrength * c, "Fallback shake");
            else { Equal(b.CameraProfile.HitImpulse, a.CameraProfile.HitImpulse * c, "Profile impulse"); if (selected.Contains(a)) Check(b.CameraProfile != a.CameraProfile, "Selected camera profile was not cloned"); }
            for (int h = 0; h < (a.HitEvents?.Count ?? 0); h++) Equal(b.HitEvents[h].KnockbackForce, a.HitEvents[h].KnockbackForce * (a.HitEvents[h].OverrideKnockbackForce ? k : 1f), "Hit knockback");
        }
    }

    static void SpecialMetrics(WeaponActionDataSO source, bool ultimate)
    {
        string[] fields = !ultimate && source is WoodFormActionDataSO ? new[] { "SkillRadius", "PullSpeed", "SkillDamageMultiplier" }
            : ultimate && source is WaterFormActionDataSO ? new[] { "AreaRadius" }
            : ultimate && source is FireFormActionDataSO ? new[] { "FireUltimateDamageDealtBonusPercent", "FireUltimateAttackSpeedBonusPercent", "FireUltimateDamageTakenBonusPercent" }
            : ultimate && source is EarthFormActionDataSO ? new[] { "UltimateShieldAmount", "UltimateBuffDuration", "UltimateShieldDamageBonusPercent" }
            : ultimate && source is WoodFormActionDataSO ? new[] { "UltimateRadius", "UltimateFieldDuration", "ElementDamageBonusPercent" } : null;
        if (fields == null) return;
        // Isolate only the metrics method: no constructor, scene access, input installation or Dispose.
        var type = typeof(FourTigerCombatSession); var session = FormatterServices.GetUninitializedObject(type); var metrics = new CombatMetrics();
        type.GetField("_metrics", Hidden).SetValue(session, metrics); type.GetField("_isSkill", Hidden).SetValue(session, !ultimate); type.GetField("_isUltimate", Hidden).SetValue(session, ultimate);
        type.GetMethod("RecordSpecialResolvedValues", Hidden).Invoke(session, new object[] { source });
        Check(metrics.appliedValues.Count == fields.Length, "Special metrics omitted forwarded properties: " + source.name);
        foreach (string field in fields) Equal(metrics.appliedValues.Single(v => v.id == "runtime.action." + field).value, (float)source.GetType().GetProperty(field, Public).GetValue(source), "Special metric " + field);
    }

    static void Export(FourTigerCombatAdapter adapter, WaterFormActionDataSO water)
    {
        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(water));
        string action = adapter.DiscoverActions(guid).Any(a => a.id == "heavy" && a.available) ? "heavy" : "normal-combo";
        var candidate = Candidate(water, action, true);
        var request = new ExperimentRequest { adapterId = adapter.Id, sourceGuid = guid, actionId = action, candidates = new List<CombatCandidate> { candidate } };
        string path = AssetDatabase.GenerateUniqueAssetPath("Assets/__FormDataGroupingTests_" + Guid.NewGuid().ToString("N") + ".asset");
        Check(AssetDatabase.LoadMainAssetAtPath(path) == null, "Temporary export path already exists");
        try
        {
            adapter.Export(request, candidate, path); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var preset = AssetDatabase.LoadAssetAtPath<CombatLabPreset>(path);
            var reloaded = AssetDatabase.LoadAllAssetsAtPath(path).OfType<WaterFormActionDataSO>().Single();
            Check(preset != null && preset.actionAsset == reloaded && preset.actionId == action, "Exported preset/subasset relationship lost");
            Applied(water, reloaded, action, true);
            var serialized = new SerializedObject(reloaded);
            foreach (string property in new[] { "Actions.ComboSteps.Array.data[0].Balance.Damage", "Actions.ComboSteps.Array.data[0].Execution.Movement.MovementProgress", "Actions.ComboSteps.Array.data[0].Feedback.HitStop.HitStopDuration", "FormBalance.WaterGaugePerSegment", "FormExecution.AreaRadius", "FormFeedback.DelayedHitVFX" })
                Check(serialized.FindProperty(property) != null, "Grouped path not serialized after export: " + property);
            foreach (var step in Steps(reloaded)) foreach (var hit in step.HitEvents ?? new List<HitEvent>()) Check(hit.Balance != null && hit.Execution != null && hit.Feedback.HitStop != null, "Export lost hit groups");
        }
        finally
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) Check(AssetDatabase.DeleteAsset(path), "Could not delete test-created export: " + path);
            Check(AssetDatabase.LoadMainAssetAtPath(path) == null, "Test export remains: " + path);
        }
    }
}
