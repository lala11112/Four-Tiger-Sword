using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Initializes only the five action assets used by Game Scene, using loaded Inspector values.</summary>
public static class ActionTimingSetup
{
    private const string Folder = "Assets/_Project/Scripts/FormActions/";
    private const float RecoverySeconds = 0.1f;
    private static readonly (string File, Type Type)[] LiveAssets =
    {
        ("FireFormAction.asset", typeof(FireFormActionDataSO)),
        ("WaterFormV2.asset", typeof(WaterFormActionDataSO)),
        ("WoodFormAction.asset", typeof(WoodFormActionDataSO)),
        ("IronFormAction.asset", typeof(IronFormActionDataSO)),
        ("EarthFormAction.asset", typeof(EarthFormActionDataSO))
    };

    private sealed class Change
    {
        public WeaponActionData Step;
        public float? Combo;
        public float Move, Action;
    }

    private sealed class Plan
    {
        public string Path;
        public WeaponActionDataSO Data;
        public readonly List<Change> Changes = new();
    }

    [MenuItem("Tools/Studio Baekho/Combat/Initialize Player Action Timing")]
    private static void RunFromMenu() => Debug.Log(Run());

    /// <summary>Explicit, repeatable migration. A nonnegative ActionCancelTime preserves later tuning.</summary>
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Player action timing initialization requires Edit Mode.");

        // Build every change first so missing or malformed data cannot partially update the live set.
        var plans = new List<Plan>();
        foreach (var spec in LiveAssets)
        {
            string path = Folder + spec.File;
            var data = AssetDatabase.LoadAssetAtPath<WeaponActionDataSO>(path);
            if (data == null || !spec.Type.IsInstanceOfType(data))
                throw new InvalidOperationException("Missing live action asset or unexpected form type: " + path);
            ValidateSteps(data.ComboSteps, path + " Combo Steps");
            ValidateSteps(data.SkillSteps, path + " Skill Steps");
            ValidateSteps(data.UltimateSteps, path + " Ultimate Steps");
            var plan = new Plan { Path = path, Data = data };
            PlanNormals(plan);
            PlanSkills(plan);
            PlanUltimates(plan);
            plans.Add(plan);
        }

        int changedAssets = 0, changedSteps = 0;
        var report = new StringBuilder();
        foreach (var plan in plans)
        {
            if (plan.Changes.Count > 0)
            {
                Undo.RecordObject(plan.Data, "Initialize player action timing");
                foreach (var change in plan.Changes)
                {
                    if (change.Combo.HasValue) change.Step.ComboTransitionTime = change.Combo.Value;
                    change.Step.RecoveryCancelTime = change.Move;
                    change.Step.ActionCancelTime = change.Action;
                }
                EditorUtility.SetDirty(plan.Data);
                // Saves this asset only, including any Inspector edits already present on the loaded object.
                AssetDatabase.SaveAssetIfDirty(plan.Data);
                changedAssets++;
                changedSteps += plan.Changes.Count;
            }
            report.AppendLine(plan.Path + $" ({plan.Changes.Count} initialized steps)");
            AppendSteps(report, "Combo", plan.Data.ComboSteps);
            AppendSteps(report, "Skill", plan.Data.SkillSteps);
            AppendSteps(report, "Ultimate", plan.Data.UltimateSteps);
        }
        return $"Initialized {changedSteps} steps in {changedAssets} live assets. "
            + "Existing Fire combo/movement and nonnegative action timing were preserved.\n" + report;
    }

    private static void PlanNormals(Plan plan)
    {
        bool preserveFire = plan.Data is FireFormActionDataSO;
        var steps = plan.Data.ComboSteps;
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (IsInitialized(step)) continue;
            float gate = DefaultGate(step);
            Add(plan, step,
                preserveFire || i == steps.Count - 1 ? null : gate,
                preserveFire ? step.RecoveryCancelTime : gate,
                preserveFire ? step.RecoveryCancelTime : gate);
        }
    }

    private static void PlanSkills(Plan plan)
    {
        var steps = plan.Data.SkillSteps;
        for (int i = 0; i < steps.Count - 1; i++) Add(plan, steps[i], null, -1f, -1f);
        var final = steps[steps.Count - 1];
        if (IsInitialized(final)) return;
        float gate;
        if (plan.Data is WaterFormActionDataSO)
            gate = final.Duration; // The swept path hit remains active until this action's original end.
        else if (plan.Data is EarthFormActionDataSO earth)
        {
            RequireFinite(earth.SkillDashDuration, "Earth Skill Dash Duration");
            float dashEnd = Mathf.Max(0.01f, earth.SkillDashDuration);
            gate = Mathf.Min(Mathf.Max(final.Duration, dashEnd), dashEnd + RecoverySeconds);
        }
        else if (plan.Data is WoodFormActionDataSO)
            gate = Mathf.Min(Mathf.Max(final.Duration, FirstEffectTime(final)), FirstEffectTime(final) + RecoverySeconds);
        else gate = DefaultGate(final);
        Add(plan, final, null, gate, gate);
    }

    private static void PlanUltimates(Plan plan)
    {
        var steps = plan.Data.UltimateSteps;
        bool instant = plan.Data is FireFormActionDataSO || plan.Data is WoodFormActionDataSO
            || plan.Data is EarthFormActionDataSO;
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (instant || i < steps.Count - 1) Add(plan, step, null, -1f, -1f);
            else if (!IsInitialized(step))
            {
                // Water's ten fixed hits complete at 2.0 seconds; no extra recovery hold is added.
                float gate = plan.Data is WaterFormActionDataSO ? 2f : DefaultGate(step);
                Add(plan, step, null, gate, gate);
            }
        }
    }

    private static bool IsInitialized(WeaponActionData step)
        => step.ActionCancelTime >= 0f && !float.IsInfinity(step.ActionCancelTime);

    private static float DefaultGate(WeaponActionData step)
        => Mathf.Min(step.Duration, LastExecutableHitEnd(step) + RecoverySeconds);

    private static float LastExecutableHitEnd(WeaponActionData step)
    {
        float last = 0f;
        if (step.HitEvents != null && step.HitEvents.Count > 0)
        {
            foreach (var hit in step.HitEvents)
            {
                if (hit == null || hit.Execution == null)
                    throw new InvalidOperationException("A timing initialization hit event is missing its execution data.");
                last = Mathf.Max(last, ExecutableHitEnd(step.Duration, hit.StartTime, hit.Duration));
            }
        }
        else last = ExecutableHitEnd(step.Duration, step.HitStartTime, step.HitDuration);
        return last;
    }

    private static float ExecutableHitEnd(float actionDuration, float start, float duration)
    {
        RequireFinite(start, "Hit Start Time");
        RequireFinite(duration, "Hit Duration");
        if (duration <= 0f || start > actionDuration) return 0f;
        RequireFinite(start + duration, "Hit end");
        return Mathf.Clamp(start + duration, 0f, actionDuration);
    }

    private static float FirstEffectTime(WeaponActionData step)
    {
        float first = step.HitStartTime;
        if (step.HitEvents != null && step.HitEvents.Count > 0)
        {
            first = float.PositiveInfinity;
            foreach (var hit in step.HitEvents)
                if (hit?.Execution != null) first = Mathf.Min(first, hit.StartTime);
        }
        RequireFinite(first, "First effect time");
        return Mathf.Max(0f, first);
    }

    private static void ValidateSteps(List<WeaponActionData> steps, string label)
    {
        if (steps == null || steps.Count == 0) throw new InvalidOperationException(label + " is empty.");
        foreach (var step in steps)
        {
            if (step?.Execution == null) throw new InvalidOperationException(label + " contains an empty step.");
            RequireFinite(step.Duration, label + " Duration");
            if (step.Duration <= 0f) throw new InvalidOperationException(label + " Duration must be positive.");
        }
    }

    private static void RequireFinite(float value, string label)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new InvalidOperationException(label + " must be finite.");
    }

    private static void Add(Plan plan, WeaponActionData step, float? combo, float move, float action)
    {
        RequireFinite(move, "Movement cancel time");
        RequireFinite(action, "Action cancel time");
        if ((!combo.HasValue || step.ComboTransitionTime == combo.Value)
            && step.RecoveryCancelTime == move && step.ActionCancelTime == action) return;
        plan.Changes.Add(new Change { Step = step, Combo = combo, Move = move, Action = action });
    }

    private static void AppendSteps(StringBuilder report, string label, List<WeaponActionData> steps)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            report.AppendLine($"  {label}[{i + 1}]: combo={Number(step.ComboTransitionTime)}, "
                + $"move={Number(step.RecoveryCancelTime)}, action={Number(step.ActionCancelTime)}");
        }
    }

    private static string Number(float value) => value.ToString("0.#####", CultureInfo.InvariantCulture);
}
