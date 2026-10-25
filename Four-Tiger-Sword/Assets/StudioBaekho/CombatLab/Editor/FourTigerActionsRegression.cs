using System;
using System.Collections.Generic;
using System.Linq;
using StudioBaekho.CombatLab;
using StudioBaekho.CombatLab.FourTigerSword;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Checks real project action catalogs, target-only cloning and exported action identity.</summary>
public static class FourTigerActionsRegression
{
    [Serializable] private sealed class Result { public int checks; public int sources; public int availableActions; public string[] passed; }
    // A constructor argument keeps this probe out of normal TypeCache adapter discovery.
    private sealed class LegacyProbe : ICombatLabAdapter
    {
        public LegacyProbe(int ignored) { }
        public string Id => "legacy-probe";
        public string DisplayName => "Legacy probe";
        public List<CombatSource> DiscoverSources() => new List<CombatSource>();
        public List<ParameterSpec> Describe(string sourceGuid) => new List<ParameterSpec> { new ParameterSpec { id = "legacy", baseline = 1, minimum = 0, maximum = 2 } };
        public void Validate(ExperimentRequest request) { }
        public ICombatLabSession Begin(ExperimentRequest request, CombatCandidate candidate) => throw new NotSupportedException();
        public Object Export(ExperimentRequest request, CombatCandidate candidate, string assetPath) => throw new NotSupportedException();
    }
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run in Edit Mode.");
        var adapter = new FourTigerCombatAdapter();
        var passed = new List<string>();
        var owned = new List<Object>();
        var sources = adapter.DiscoverSources();
        int available = 0;
        string folder = null;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); passed.Add(label); }
        try
        {
            var legacy = new LegacyProbe(0);
            Check(CombatLab.Describe(legacy, "unused", null)[0].id == "legacy", "Legacy adapter omitted action compatible");
            Check(CombatLab.Describe(legacy, "unused", "normal-combo")[0].id == "legacy", "Legacy adapter explicit default compatible");
            bool legacyRejected = false;
            try { CombatLab.Describe(legacy, "unused", "skill"); } catch (ArgumentException) { legacyRejected = true; }
            Check(legacyRejected, "Legacy adapter unsupported action rejected");
            Check(sources.Select(item => FourTigerCombatAdapter.ResolveSource(item.guid).GetType()).Distinct().Count() == 5, "Five form types discovered");
            foreach (var item in sources)
            {
                var source = FourTigerCombatAdapter.ResolveSource(item.guid);
                string before = EditorJsonUtility.ToJson(source);
                var actions = adapter.DiscoverActions(item.guid);
                Check(actions.Any(action => action.id == "normal-combo" && action.available), source.name + " combo available");
                Check(actions.Any(action => action.id == "skill" && action.available), source.name + " skill available");
                Check(actions.Any(action => action.id == "ultimate" && action.available), source.name + " ultimate available");
                var heavy = actions.Single(action => action.id == "heavy");
                Check(heavy.available == (source is WaterFormActionDataSO), source.name + " heavy implementation accurately classified");
                Check(heavy.available || !string.IsNullOrWhiteSpace(heavy.unavailableReason), source.name + " unavailable reason");
                foreach (var action in actions.Where(action => action.available))
                {
                    available++;
                    var request = CombatLab.CreateComparison(adapter.Id, item.guid, action.id);
                    Check(request.actionId == action.id, source.name + " " + action.id + " request identity");
                    CombatLab.ValidateData(request);
                    var candidate = request.candidates[0];
                    candidate.rationale = "Action regression rationale";
                    foreach (var value in candidate.values)
                    {
                        var spec = adapter.Describe(item.guid, action.id).Single(parameter => parameter.id == value.id);
                        value.value = Mathf.Clamp(spec.baseline == 0f ? spec.maximum * 0.25f : spec.baseline * 1.25f, spec.minimum, spec.maximum);
                    }
                    var clone = FourTigerCombatAdapter.CloneAndApply(source, candidate, owned, action.id);
                    Check(clone.GetType() == source.GetType() && clone != source && !EditorUtility.IsPersistent(clone), source.name + " " + action.id + " isolated correct form clone");
                    Check(FourTigerCombatAdapter.CreateForm(clone) != null, source.name + " " + action.id + " runtime factory");
                    Check(clone.ComboSteps.Count == source.ComboSteps.Count, source.name + " " + action.id + " full combo retained for export");
                    float Value(string id) => candidate.values.Single(value => value.id == id).value;
                    if (candidate.values.Any(value => value.id == FourTigerCombatAdapter.KnockbackId))
                    {
                        var originalSteps = FourTigerCombatAdapter.SelectedSteps(source, action.id);
                        var clonedSteps = FourTigerCombatAdapter.SelectedSteps(clone, action.id);
                        for (int i = 0; i < originalSteps.Count; i++)
                        {
                            Check(!ReferenceEquals(originalSteps[i], clonedSteps[i]), source.name + " " + action.id + " nested isolation " + i);
                            Check(Mathf.Approximately(clonedSteps[i].KnockbackForce, originalSteps[i].KnockbackForce * Value(FourTigerCombatAdapter.KnockbackId)), source.name + " " + action.id + " target knockback " + i);
                            if (originalSteps[i].CameraProfile != null)
                                Check(clonedSteps[i].CameraProfile != originalSteps[i].CameraProfile && Mathf.Approximately(clonedSteps[i].CameraProfile.HitImpulse, originalSteps[i].CameraProfile.HitImpulse * Value(FourTigerCombatAdapter.CameraId)), source.name + " " + action.id + " target camera profile " + i);
                            else Check(Mathf.Approximately(clonedSteps[i].CameraShakeStrength, originalSteps[i].CameraShakeStrength * Value(FourTigerCombatAdapter.CameraId)), source.name + " " + action.id + " target camera fallback " + i);
                        }
                        if (action.id.StartsWith("normal-", StringComparison.Ordinal) && action.id != "normal-combo")
                        {
                            int selected = int.Parse(action.id.Substring(7)) - 1;
                            Check(clone.ComboSteps.Where((step, index) => index != selected).All(step => step.KnockbackForce == source.ComboSteps[clone.ComboSteps.IndexOf(step)].KnockbackForce), source.name + " " + action.id + " other combo steps unchanged");
                        }
                        if (!action.id.StartsWith("normal-", StringComparison.Ordinal))
                            Check(clone.ComboSteps.Select((step, index) => step.KnockbackForce == source.ComboSteps[index].KnockbackForce && step.CameraProfile == source.ComboSteps[index].CameraProfile).All(value => value), source.name + " " + action.id + " normal combo unchanged");
                    }
                    else if (action.id == "skill" && clone is WoodFormActionDataSO woodSkill)
                    {
                        var original = (WoodFormActionDataSO)source;
                        Check(Mathf.Approximately(woodSkill.PullSpeed, original.PullSpeed * Value("pullSpeedMultiplier")) && Mathf.Approximately(woodSkill.SkillRadius, original.SkillRadius * Value("skillRadiusMultiplier")) && Mathf.Approximately(woodSkill.SkillDamageMultiplier, original.SkillDamageMultiplier * Value("skillDamageMultiplier")), "Wood gather actual parameters applied");
                        Check(woodSkill.SkillSteps[0].KnockbackForce == original.SkillSteps[0].KnockbackForce, "Wood gather unused knockback unchanged");
                    }
                    else if (clone is WaterFormActionDataSO water)
                    {
                        var original = (WaterFormActionDataSO)source;
                        Check(Mathf.Approximately(water.AreaRadius, original.AreaRadius * Value("areaRadiusMultiplier")) && Mathf.Approximately(water.UltimateSteps[0].CameraShakeStrength, original.UltimateSteps[0].CameraShakeStrength * Value(FourTigerCombatAdapter.CameraId)), "Water ultimate direct feedback parameters");
                        Check(water.UltimateSteps[0].CameraProfile == original.UltimateSteps[0].CameraProfile, "Water ultimate unused camera profile unchanged");
                    }
                    else if (clone is FireFormActionDataSO fire)
                        Check(fire.FireUltimateDamageDealtBonusPercent == Value("ultimateDamageBonusPercent") && fire.FireUltimateAttackSpeedBonusPercent == Value("ultimateAttackSpeedBonusPercent") && fire.FireUltimateDamageTakenBonusPercent == Value("ultimateDamageTakenBonusPercent"), "Fire ultimate actual buff parameters");
                    else if (clone is EarthFormActionDataSO earth)
                        Check(Mathf.Approximately(earth.UltimateShieldAmount, ((EarthFormActionDataSO)source).UltimateShieldAmount * Value("ultimateShieldMultiplier")) && earth.UltimateBuffDuration == Value("ultimateBuffDuration") && earth.UltimateShieldDamageBonusPercent == Value("ultimateDamageBonusPercent"), "Earth ultimate actual shield parameters");
                    else if (clone is WoodFormActionDataSO wood)
                        Check(Mathf.Approximately(wood.UltimateRadius, ((WoodFormActionDataSO)source).UltimateRadius * Value("ultimateRadiusMultiplier")) && wood.UltimateFieldDuration == Value("ultimateFieldDuration") && wood.ElementDamageBonusPercent == Value("ultimateElementBonusPercent"), "Wood ultimate actual field parameters");
                    Check(before == EditorJsonUtility.ToJson(source), source.name + " " + action.id + " source unchanged");
                    var invalid = JsonUtility.FromJson<ExperimentRequest>(JsonUtility.ToJson(request));
                    invalid.actionId = "arbitrary-action";
                    bool rejected = false;
                    try { CombatLab.ValidateData(invalid); } catch (ArgumentException) { rejected = true; }
                    Check(rejected, source.name + " " + action.id + " invalid action rejected");
                }
            }
            var waterItem = sources.First(item => FourTigerCombatAdapter.ResolveSource(item.guid) is WaterFormActionDataSO);
            var exportRequest = CombatLab.CreateComparison(adapter.Id, waterItem.guid, "normal-2");
            exportRequest.candidates[0].rationale = "Preserved AI explanation";
            folder = "Assets/CombatLabActionRegression_" + Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets", folder.Substring(7)))) throw new InvalidOperationException("Cannot create test export folder.");
            var preset = adapter.Export(exportRequest, exportRequest.candidates[0], folder + "/Normal2.asset") as CombatLabPreset;
            Check(preset != null && preset.actionId == "normal-2" && preset.candidate.rationale == "Preserved AI explanation", "Export preserves action identity and AI rationale");
            Check(((WeaponActionDataSO)preset.actionAsset).ComboSteps.Count == FourTigerCombatAdapter.ResolveSource(waterItem.guid).ComboSteps.Count, "Single-step export preserves complete playable form");
            Check(available >= 30, "Thirty configured actions available");
            return JsonUtility.ToJson(new Result { checks = passed.Count, sources = sources.Count, availableActions = available, passed = passed.ToArray() });
        }
        finally
        {
            if (folder != null && AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            foreach (var item in owned) if (item != null && !EditorUtility.IsPersistent(item)) Object.DestroyImmediate(item);
        }
    }
}
