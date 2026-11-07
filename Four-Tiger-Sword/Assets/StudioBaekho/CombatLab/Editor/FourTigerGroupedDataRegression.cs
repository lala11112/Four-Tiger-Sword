// Run in Edit Mode through Unity Pipeline run_script, entry FourTigerGroupedDataRegression.Run.
// Synthetic grouped assets exercise override precedence, isolation and persistent export without editing game assets.
using System;
using System.Collections.Generic;
using System.Linq;
using StudioBaekho.CombatLab;
using StudioBaekho.CombatLab.FourTigerSword;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class FourTigerGroupedDataRegression
{
    [Serializable] private sealed class Result { public int checks; public string[] passed; }

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run in Edit Mode.");
        var owned = new List<Object>();
        var passed = new List<string>();
        string folder = null;
        void Check(bool valid, string label) { if (!valid) throw new InvalidOperationException(label); passed.Add(label); }
        void Equal(float actual, float expected, string label) => Check(Mathf.Approximately(actual, expected), label);
        void Reject(Action operation, string label)
        {
            bool rejected = false;
            try { operation(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, label);
        }
        try
        {
            var source = ScriptableObject.CreateInstance<WaterFormActionDataSO>();
            source.name = "Grouped regression source";
            owned.Add(source);
            var profile = ScriptableObject.CreateInstance<AttackCameraProfile>();
            profile.name = "Shared camera";
            profile.HitImpulse = 2f;
            owned.Add(profile);
            var first = Step();
            first.Feedback.Camera.CameraProfile = profile;
            first.Feedback.Camera.CameraShakeStrength = 2f;
            first.Feedback.HitStop = Stop(true, 0.07f, 0.13f);
            first.HitEvents = new List<HitEvent> { Hit(false, 0.27f, 0.91f), Hit(true, 0.11f, 0.29f) };
            first.HitEvents[1].Execution.StartTime = 0.2f;
            first.HitEvents[1].Balance.OverrideKnockbackForce = true;
            first.HitEvents[1].Balance.KnockbackForce = 10f;
            var second = Step();
            second.Feedback.HitStop = Stop(false, 0.28f, 0.99f);
            second.Feedback.Camera.CameraProfile = profile;
            source.Actions.ComboSteps = new List<WeaponActionData> { first, second };
            source.Actions.SkillSteps = new List<WeaponActionData> { Step() };
            source.Actions.UltimateSteps = new List<WeaponActionData> { Step(), Step() };
            source.HeavyAttackStep = Step();
            source.HeavyAttackStep.Execution.AnimationName = "RegressionHeavy";
            source.HeavyAttackStep.Feedback.HitStop = Stop(true, 0.06f, 0.2f);
            source.HeavyAttackStep.HitEvents = new List<HitEvent> { Hit(true, 0.18f, 0.8f) };
            string original = EditorJsonUtility.ToJson(source);
            string originalSkill = JsonUtility.ToJson(source.Actions.SkillSteps[0]);
            string originalSecond = JsonUtility.ToJson(second);
            float playerDuration = FourTigerCombatAdapter.ReadSceneHitStop();
            float playerTimeScale = FourTigerCombatAdapter.ReadSceneHitStopTimeScale();

            Equal(FourTigerCombatAdapter.ReadSelectedHitStop(source, "normal-1"), 0.07f, "Baseline inherits attack duration before player default");
            Equal(FourTigerCombatAdapter.EffectiveHitStop(first, first.HitEvents[1], playerDuration, playerTimeScale).HitStopDuration,
                0.11f, "Hit override takes precedence over attack override");
            Equal(FourTigerCombatAdapter.ReadSelectedHitStop(source, "normal-2"), playerDuration, "Baseline inherits player default when attack override is off");
            Equal(FourTigerCombatAdapter.ReadSelectedHitStop(source, "heavy"), 0.06f, "Heavy baseline ignores unused HitEvents");
            var candidate = Candidate(source, "normal-1");
            Set(candidate, FourTigerCombatAdapter.HitStopId, 0.12f);
            Set(candidate, FourTigerCombatAdapter.KnockbackId, 0.5f);
            Set(candidate, FourTigerCombatAdapter.CameraId, 1.5f);
            var clone = (WaterFormActionDataSO)FourTigerCombatAdapter.CloneAndApply(source, candidate, owned, "normal-1");
            var attack = clone.Actions.ComboSteps[0];
            Check(!ReferenceEquals(clone.Actions, source.Actions) && !ReferenceEquals(clone.CommonBalance, source.CommonBalance)
                && !ReferenceEquals(clone.CommonExecution, source.CommonExecution) && !ReferenceEquals(clone.CommonFeedback, source.CommonFeedback)
                && !ReferenceEquals(clone.FormBalance, source.FormBalance) && !ReferenceEquals(clone.FormExecution, source.FormExecution),
                "All serialized form groups are owned by the clone");
            Check(!ReferenceEquals(attack, first) && !ReferenceEquals(attack.Balance, first.Balance)
                && !ReferenceEquals(attack.Execution, first.Execution) && !ReferenceEquals(attack.Feedback.HitStop, first.Feedback.HitStop)
                && !ReferenceEquals(attack.HitEvents[1].Feedback.HitStop, first.HitEvents[1].Feedback.HitStop), "Attack and hit groups are deep cloned");
            Equal(attack.Balance.KnockbackForce, 4f, "Grouped attack knockback applied");
            Equal(attack.HitEvents[1].Balance.KnockbackForce, 5f, "Active grouped hit knockback applied");
            Equal(attack.Feedback.Camera.CameraShakeStrength, 3f, "Camera fallback scaled even with assigned profile");
            Check(attack.Feedback.Camera.CameraProfile != profile, "Selected camera profile is cloned");
            Equal(attack.Feedback.Camera.CameraProfile.HitImpulse, 3f, "Camera profile impulse scaled");
            Check(clone.Actions.ComboSteps[1].Feedback.Camera.CameraProfile == profile, "Unselected shared camera profile remains original");
            Equal(profile.HitImpulse, 2f, "Source camera profile unchanged");
            Equal(attack.Feedback.HitStop.HitStopDuration, 0.12f, "Attack duration applied in grouped override");
            Equal(attack.Feedback.HitStop.HitStopTimeScale, 0.13f, "Attack time scale preserved");
            Equal(attack.HitEvents[1].Feedback.HitStop.HitStopDuration, 0.12f, "Active hit override receives duration");
            Equal(attack.HitEvents[1].Feedback.HitStop.HitStopTimeScale, 0.29f, "Active hit time scale preserved");
            Check(!attack.HitEvents[0].Feedback.HitStop.OverrideHitStop, "Inherited hit retains fallback relationship");
            Equal(attack.HitEvents[0].Feedback.HitStop.HitStopDuration, 0.27f, "Inactive authored hit duration remains untouched");
            Equal(FourTigerCombatAdapter.EffectiveHitStop(attack, attack.HitEvents[0], playerDuration, playerTimeScale).HitStopDuration,
                0.12f, "Inherited hit resolves to tuned attack duration");
            Check(JsonUtility.ToJson(clone.Actions.ComboSteps[1]) == originalSecond && JsonUtility.ToJson(clone.Actions.SkillSteps[0]) == originalSkill,
                "Unselected attacks retain complete serialized values");
            Check(EditorJsonUtility.ToJson(source) == original, "Source grouped asset unchanged");

            var inherited = Candidate(source, "normal-2");
            Set(inherited, FourTigerCombatAdapter.HitStopId, 0f);
            var inheritedClone = FourTigerCombatAdapter.CloneAndApply(source, inherited, owned, "normal-2");
            var inheritedStop = inheritedClone.Actions.ComboSteps[1].Feedback.HitStop;
            Check(inheritedStop.OverrideHitStop && inheritedStop.HitStopDuration == 0f, "Zero duration explicitly disables selected hit stop");
            Equal(inheritedStop.HitStopTimeScale, playerTimeScale, "Inherited player time scale materialized in exportable attack override");
            Equal(FourTigerCombatAdapter.ReadSceneHitStop(), playerDuration, "Player default duration unchanged after clone");
            Equal(FourTigerCombatAdapter.ReadSceneHitStopTimeScale(), playerTimeScale, "Player default time scale unchanged after clone");

            var heavyCandidate = Candidate(source, "heavy");
            Set(heavyCandidate, FourTigerCombatAdapter.HitStopId, 0.1f);
            var heavyClone = (WaterFormActionDataSO)FourTigerCombatAdapter.CloneAndApply(source, heavyCandidate, owned, "heavy");
            Equal(heavyClone.HeavyAttackStep.Feedback.HitStop.HitStopDuration, 0.1f, "Heavy uses tuned step hit stop");
            Equal(heavyClone.HeavyAttackStep.HitEvents[0].Feedback.HitStop.HitStopDuration, 0.18f, "Heavy unused hit override preserved");

            var ultimate = source.Actions.UltimateSteps[0];
            ultimate.Feedback.HitStop = Stop(true, 0.09f, 0.15f);
            ultimate.Feedback.Camera.CameraProfile = profile;
            ultimate.HitEvents = Enumerable.Range(0, 11).Select(index => Hit(true, 0.1f, 0.03f * index)).ToList();
            var ultimateCandidate = Candidate(source, "ultimate");
            Set(ultimateCandidate, FourTigerCombatAdapter.HitStopId, 0.16f);
            var ultimateClone = (WaterFormActionDataSO)FourTigerCombatAdapter.CloneAndApply(source, ultimateCandidate, owned, "ultimate");
            Check(ultimateClone.Actions.UltimateSteps[0].HitEvents.Take(10).All(hit => hit.Feedback.HitStop.HitStopDuration == 0.16f),
                "Water fixed ten hits receive grouped overrides");
            Equal(ultimateClone.Actions.UltimateSteps[0].HitEvents[10].Feedback.HitStop.HitStopDuration, 0.1f, "Water eleventh unused hit retained");
            Equal(ultimateClone.Actions.UltimateSteps[0].HitEvents[9].Feedback.HitStop.HitStopTimeScale, 0.27f, "Water indexed hit time scale retained");
            Check(ultimateClone.Actions.UltimateSteps[0].Feedback.Camera.CameraProfile == profile, "Water unused ultimate camera profile retained");
            Check(JsonUtility.ToJson(ultimateClone.Actions.UltimateSteps[1]) == JsonUtility.ToJson(source.Actions.UltimateSteps[1]),
                "Water unused later ultimate step retained");

            var wood = ScriptableObject.CreateInstance<WoodFormActionDataSO>();
            owned.Add(wood);
            wood.Actions.ComboSteps = new List<WeaponActionData> { Step() };
            wood.Actions.SkillSteps = new List<WeaponActionData> { Step() };
            var woodCandidate = Candidate(wood, "skill");
            Set(woodCandidate, "skillRadiusMultiplier", 1.5f);
            Set(woodCandidate, "pullSpeedMultiplier", 0.5f);
            Set(woodCandidate, "skillDamageMultiplier", 2f);
            var woodClone = (WoodFormActionDataSO)FourTigerCombatAdapter.CloneAndApply(wood, woodCandidate, owned, "skill");
            Equal(woodClone.FormExecution.SkillRadius, wood.FormExecution.SkillRadius * 1.5f, "Wood gather grouped execution radius");
            Equal(woodClone.FormExecution.PullSpeed, wood.FormExecution.PullSpeed * 0.5f, "Wood gather grouped execution pull speed");
            Equal(woodClone.FormBalance.SkillDamageMultiplier, wood.FormBalance.SkillDamageMultiplier * 2f, "Wood gather grouped balance damage");
            Check(JsonUtility.ToJson(woodClone.Actions.SkillSteps[0]) == JsonUtility.ToJson(wood.Actions.SkillSteps[0]), "Wood gather unused step feedback unchanged");
            foreach (var buff in new WeaponActionDataSO[] { ScriptableObject.CreateInstance<FireFormActionDataSO>(), ScriptableObject.CreateInstance<EarthFormActionDataSO>(), wood })
            {
                if (buff != wood) owned.Add(buff);
                buff.Actions.ComboSteps = new List<WeaponActionData> { Step() };
                buff.Actions.UltimateSteps = null;
                var buffCandidate = Candidate(buff, "ultimate");
                Set(buffCandidate, buff is WoodFormActionDataSO ? "ultimateElementBonusPercent" : "ultimateDamageBonusPercent", 80f);
                var buffClone = FourTigerCombatAdapter.CloneAndApply(buff, buffCandidate, owned, "ultimate");
                Check(FourTigerCombatAdapter.SelectedSteps(buffClone, "ultimate").Count == 0, buff.GetType().Name + " buff requires no ultimate hit steps");
                Check(JsonUtility.ToJson(buffClone.Actions.ComboSteps[0]) == JsonUtility.ToJson(buff.Actions.ComboSteps[0]), buff.GetType().Name + " follow-up combo unchanged");
                float bonus = buffClone is FireFormActionDataSO fire ? fire.FormBalance.FireUltimateDamageDealtBonusPercent
                    : buffClone is EarthFormActionDataSO earth ? earth.FormBalance.UltimateShieldDamageBonusPercent
                    : ((WoodFormActionDataSO)buffClone).FormBalance.ElementDamageBonusPercent;
                Equal(bonus, 80f, buff.GetType().Name + " grouped buff balance applied");
            }

            var malformed = ScriptableObject.CreateInstance<IronFormActionDataSO>();
            owned.Add(malformed);
            malformed.Actions = null;
            Reject(() => FourTigerCombatAdapter.ValidateSource(malformed), "Missing Actions group rejected with an actionable error");
            malformed.Actions = new FormActionSet { ComboSteps = new List<WeaponActionData> { Step() } };
            malformed.Actions.ComboSteps[0].Feedback.HitStop = Stop(true, 0.1f, float.NaN);
            Reject(() => FourTigerCombatAdapter.ValidateSource(malformed), "Non-finite active hit-stop time scale rejected");
            malformed.Actions.ComboSteps[0].Feedback.HitStop = Stop(true, 0.1f, 0.1f);
            malformed.Actions.ComboSteps[0].HitEvents = new List<HitEvent> { Hit(true, float.PositiveInfinity, 0f) };
            Reject(() => FourTigerCombatAdapter.ValidateSource(malformed), "Non-finite active hit duration rejected");

            folder = "Assets/CombatLabGroupedRegression_" + Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets", folder.Substring(7)))) throw new InvalidOperationException("Cannot create test export folder.");
            AssetDatabase.CreateAsset(profile, folder + "/Camera.asset");
            AssetDatabase.CreateAsset(source, folder + "/Source.asset");
            var request = new ExperimentRequest { adapterId = FourTigerCombatAdapter.AdapterId,
                sourceGuid = AssetDatabase.AssetPathToGUID(folder + "/Source.asset"), actionId = "normal-1",
                durationSeconds = 2f, candidates = new List<CombatCandidate> { candidate } };
            string sourceBeforeExport = EditorJsonUtility.ToJson(source);
            string exportPath = folder + "/Export.asset";
            new FourTigerCombatAdapter().Export(request, candidate, exportPath);
            AssetDatabase.ImportAsset(exportPath, ImportAssetOptions.ForceUpdate);
            var preset = AssetDatabase.LoadAssetAtPath<CombatLabPreset>(exportPath);
            var saved = preset.actionAsset as WaterFormActionDataSO;
            Check(preset.adapterId == FourTigerCombatAdapter.AdapterId && preset.actionId == "normal-1", "Export preserves saved-request adapter and action IDs");
            Check(saved != null && EditorUtility.IsPersistent(saved) && saved.Actions.ComboSteps.Count == 2, "Export retains complete playable grouped form");
            Equal(saved.Actions.ComboSteps[0].Feedback.HitStop.HitStopDuration, 0.12f, "Export reload retains attack hit-stop override");
            Equal(saved.Actions.ComboSteps[0].HitEvents[1].Feedback.HitStop.HitStopDuration, 0.12f, "Export reload retains per-hit override");
            Equal(saved.Actions.ComboSteps[0].HitEvents[1].Feedback.HitStop.HitStopTimeScale, 0.29f, "Export reload retains hit time scale");
            Check(AssetDatabase.GetAssetPath(saved.Actions.ComboSteps[0].Feedback.Camera.CameraProfile) == exportPath, "Export owns tuned camera subasset");
            Check(saved.Actions.ComboSteps[1].Feedback.Camera.CameraProfile == profile, "Export preserves unselected shared camera reference");
            Check(EditorJsonUtility.ToJson(source) == sourceBeforeExport, "Persistent source unchanged by export");
            return JsonUtility.ToJson(new Result { checks = passed.Count, passed = passed.ToArray() });
        }
        finally
        {
            if (folder != null && AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null && !EditorUtility.IsPersistent(owned[i])) Object.DestroyImmediate(owned[i]);
        }
    }

    private static WeaponActionData Step() => new WeaponActionData();
    private static HitStopSettings Stop(bool enabled, float duration, float timeScale)
        => new HitStopSettings { OverrideHitStop = enabled, HitStopDuration = duration, HitStopTimeScale = timeScale };
    private static HitEvent Hit(bool enabled, float duration, float timeScale)
        => new HitEvent { Feedback = new HitFeedbackSettings { HitStop = Stop(enabled, duration, timeScale) } };
    private static CombatCandidate Candidate(WeaponActionDataSO source, string actionId)
        => new CombatCandidate { id = "grouped", label = "Grouped regression",
            values = FourTigerCombatAdapter.ParameterSpecs(source, actionId).Select(spec => new ParameterValue(spec.id, spec.baseline)).ToList() };
    private static void Set(CombatCandidate candidate, string id, float value) => candidate.values.Single(item => item.id == id).value = value;
}
