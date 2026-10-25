using System;
using System.Collections.Generic;
using System.Linq;
using StudioBaekho.CombatLab;
using StudioBaekho.CombatLab.FourTigerSword;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Run with the Unity Pipeline run_script command after the package and integration compile.
/// Uses the real registered adapter and existing source assets, without changing scene or assets.
/// </summary>
public static class CombatLabCoreRegression
{
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run core validation checks in Edit Mode.");
        var adapter = CombatLab.Adapter(FourTigerCombatAdapter.AdapterId);
        var sources = adapter.DiscoverSources();
        if (sources.Count == 0) throw new InvalidOperationException("No real Water action source is available for integration checks.");
        string sourceGuid = sources.First(item => FourTigerCombatAdapter.ResolveSource(item.guid) is WaterFormActionDataSO).guid;
        var source = FourTigerCombatAdapter.ResolveSource(sourceGuid);
        string sourceBefore = EditorJsonUtility.ToJson(source);
        var template = CombatLab.CreateComparison(adapter.Id, sourceGuid);
        var passed = new List<string>();

        // Every case is malformed before scene preflight; no test scene or fake adapter is needed.
        void Reject(string label, Action<ExperimentRequest> mutate, Type exceptionType = null)
        {
            var request = JsonUtility.FromJson<ExperimentRequest>(JsonUtility.ToJson(template));
            mutate(request);
            bool rejected = false;
            try { CombatLab.Validate(request); }
            catch (Exception error)
            {
                Type expected = exceptionType ?? typeof(ArgumentException);
                if (!expected.IsInstanceOfType(error))
                    throw new InvalidOperationException(label + " failed with an unexpected exception type: " + error.GetType().Name, error);
                rejected = true;
            }
            if (!rejected) throw new InvalidOperationException("Invalid request was accepted: " + label);
            passed.Add(label);
        }

        Reject("unsupported schema", r => r.schemaVersion = 2);
        Reject("unknown adapter", r => r.adapterId = "missing-combat-lab-adapter", typeof(InvalidOperationException));
        Reject("missing source", r => r.sourceGuid = "00000000000000000000000000000000");
        Reject("duration below minimum", r => r.durationSeconds = 2.99f);
        Reject("duration above maximum", r => r.durationSeconds = 60.01f);
        Reject("NaN duration", r => r.durationSeconds = float.NaN);
        Reject("infinite duration", r => r.durationSeconds = float.PositiveInfinity);
        Reject("no candidates", r => r.candidates.Clear());
        Reject("too many candidates", r => { while (r.candidates.Count < 13) r.candidates.Add(r.candidates[0]); });
        Reject("null candidate", r => r.candidates[0] = null);
        Reject("duplicate candidate ID", r => r.candidates[1].id = r.candidates[0].id);
        Reject("candidate path traversal", r => r.candidates[0].id = "../outside");
        Reject("candidate trailing newline", r => r.candidates[0].id = "baseline\n");
        Reject("candidate ID too long", r => r.candidates[0].id = new string('a', 65));
        Reject("missing values", r => r.candidates[0].values = null);
        Reject("missing parameter", r => r.candidates[0].values.RemoveAt(0));
        Reject("null parameter", r => r.candidates[0].values[0] = null);
        Reject("unknown parameter", r => r.candidates[0].values[0].id = "arbitraryField");
        Reject("duplicate parameter", r => r.candidates[0].values[0].id = r.candidates[0].values[1].id);
        Reject("out of range parameter", r => r.candidates[0].values[0].value = -0.01f);
        Reject("NaN parameter", r => r.candidates[0].values[0].value = float.NaN);
        Reject("infinite parameter", r => r.candidates[0].values[0].value = float.NegativeInfinity);

        var specs = adapter.Describe(sourceGuid);
        if (template.candidates.Count != 3 || template.candidates.Select(c => c.id).Distinct().Count() != 3)
            throw new InvalidOperationException("Default comparison must create three uniquely identified candidates.");
        foreach (var spec in specs)
        {
            var baseline = template.candidates[0].values.Single(v => v.id == spec.id);
            if (!Mathf.Approximately(baseline.value, spec.baseline))
                throw new InvalidOperationException("The baseline candidate silently changed the current value for " + spec.id + ".");
        }
        passed.Add("comparison preserves actual baseline");
        if (sourceBefore != EditorJsonUtility.ToJson(source))
            throw new InvalidOperationException("Validation or candidate creation changed the source asset.");
        passed.Add("source asset remains unchanged");
        return passed.Count + " core regression checks passed: " + string.Join(", ", passed);
    }
}
