// Run in Edit Mode through Unity Pipeline run_script, entry FourTigerCombinedExportRegression.Run.
// Uses temporary synthetic assets only; the exported main object must be directly usable as a form.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StudioBaekho.CombatLab;
using StudioBaekho.CombatLab.FourTigerSword;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class FourTigerCombinedExportRegression
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
        try
        {
            folder = "Assets/CombatLabCombinedRegression_" + Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets", folder.Substring(7))))
                throw new InvalidOperationException("Cannot create test export folder.");
            var source = ScriptableObject.CreateInstance<WaterFormActionDataSO>();
            owned.Add(source);
            source.name = "Combined source";
            var profile = ScriptableObject.CreateInstance<AttackCameraProfile>();
            owned.Add(profile);
            profile.name = "Shared camera";
            profile.HitImpulse = 2f;
            source.Actions.ComboSteps = new List<WeaponActionData> { Step(profile), Step(profile), Step(profile) };
            source.Actions.SkillSteps = new List<WeaponActionData> { Step(profile), Step(profile) };
            source.Actions.UltimateSteps = new List<WeaponActionData> { Step(profile), Step(profile) };
            source.HeavyAttackStep = Step(profile);
            source.HeavyAttackStep.Execution.AnimationName = "RegressionHeavy";
            var firstHit = new HitEvent();
            firstHit.Balance.OverrideKnockbackForce = true;
            firstHit.Balance.KnockbackForce = 10f;
            firstHit.Feedback.HitStop.OverrideHitStop = true;
            firstHit.Feedback.HitStop.HitStopDuration = 0.08f;
            firstHit.Feedback.HitStop.HitStopTimeScale = 0.2f;
            source.Actions.ComboSteps[0].HitEvents = new List<HitEvent> { firstHit };
            AssetDatabase.CreateAsset(profile, folder + "/Camera.asset");
            AssetDatabase.CreateAsset(source, folder + "/Source.asset");
            string sourceGuid = AssetDatabase.AssetPathToGUID(folder + "/Source.asset");
            string sourceJson = EditorJsonUtility.ToJson(source);
            string cameraJson = EditorJsonUtility.ToJson(profile);
            string sourceFile = File.ReadAllText(folder + "/Source.asset");
            string cameraFile = File.ReadAllText(folder + "/Camera.asset");

            var first = Selection(source, sourceGuid, "normal-1");
            Set(first, FourTigerCombatAdapter.KnockbackId, 0.5f);
            Set(first, FourTigerCombatAdapter.CameraId, 1.5f);
            Set(first, FourTigerCombatAdapter.HitStopId, 0.12f);
            var unselectedCandidate = JsonUtility.FromJson<CombatCandidate>(JsonUtility.ToJson(first.request.candidates[0]));
            unselectedCandidate.id = "not-chosen";
            unselectedCandidate.values.Single(item => item.id == FourTigerCombatAdapter.KnockbackId).value = 2f;
            first.request.candidates.Insert(0, unselectedCandidate);
            var second = Selection(source, sourceGuid, "normal-2");
            Set(second, FourTigerCombatAdapter.KnockbackId, 1.5f);
            Set(second, FourTigerCombatAdapter.CameraId, 0.5f);
            Set(second, FourTigerCombatAdapter.HitStopId, 0.04f);
            var skill = Selection(source, sourceGuid, "skill");
            Set(skill, FourTigerCombatAdapter.KnockbackId, 2f);
            Set(skill, FourTigerCombatAdapter.CameraId, 3f);
            Set(skill, FourTigerCombatAdapter.HitStopId, 0.2f);
            var request = Request(sourceGuid, first, second, skill);
            string path = folder + "/Combined.asset";
            var exported = CombatLab.ExportCombined(request, path);
            Check(exported is WaterFormActionDataSO && AssetDatabase.IsMainAsset(exported), "Combined export is a directly assignable main form asset");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var saved = AssetDatabase.LoadAssetAtPath<WaterFormActionDataSO>(path);
            var objects = AssetDatabase.LoadAllAssetsAtPath(path);
            Check(objects.OfType<WeaponActionDataSO>().Count() == 1 && !objects.Any(item => item is CombatLabPreset), "One form and no preset wrapper are stored in the asset");
            Check(objects.Length == 4 && objects.OfType<AttackCameraProfile>().Count() == 3, "Only one camera copy per selected action is saved without orphan subassets");
            Equal(saved.Actions.ComboSteps[0].Balance.KnockbackForce, 4f, "First action applies its chosen candidate ID instead of the first candidate");
            Equal(saved.Actions.ComboSteps[1].Balance.KnockbackForce, 12f, "Second action applies its own knockback candidate");
            Equal(saved.Actions.SkillSteps[0].Balance.KnockbackForce, 16f, "Skill applies its own knockback candidate");
            Equal(saved.Actions.SkillSteps[1].Balance.KnockbackForce, 16f, "All selected skill steps are applied once");
            Equal(saved.Actions.ComboSteps[0].HitEvents[0].Balance.KnockbackForce, 5f, "Selected per-hit knockback survives combined export");
            Equal(saved.Actions.ComboSteps[0].Feedback.HitStop.HitStopDuration, 0.12f, "First action retains selected hit stop after reload");
            Equal(saved.Actions.ComboSteps[0].HitEvents[0].Feedback.HitStop.HitStopDuration, 0.12f, "Selected hit override survives combined export");
            Equal(saved.Actions.ComboSteps[0].HitEvents[0].Feedback.HitStop.HitStopTimeScale, 0.2f, "Hit override time scale is preserved");
            Equal(saved.Actions.ComboSteps[1].Feedback.HitStop.HitStopDuration, 0.04f, "Second action retains independent hit stop");
            var firstCamera = saved.Actions.ComboSteps[0].Feedback.Camera.CameraProfile;
            var secondCamera = saved.Actions.ComboSteps[1].Feedback.Camera.CameraProfile;
            var skillCamera = saved.Actions.SkillSteps[0].Feedback.Camera.CameraProfile;
            Check(firstCamera != secondCamera && firstCamera != skillCamera && secondCamera != skillCamera,
                "Actions sharing an original camera own distinct tuned profiles");
            Equal(firstCamera.HitImpulse, 3f, "First camera scales original profile exactly once");
            Equal(secondCamera.HitImpulse, 1f, "Second camera scales original profile exactly once");
            Equal(skillCamera.HitImpulse, 6f, "Skill camera scales original profile exactly once");
            Equal(saved.Actions.ComboSteps[0].Feedback.Camera.CameraShakeStrength, 3f, "First camera fallback retains independent scaling");
            Equal(saved.Actions.ComboSteps[1].Feedback.Camera.CameraShakeStrength, 1f, "Second camera fallback retains independent scaling");
            Check(saved.Actions.SkillSteps[1].Feedback.Camera.CameraProfile == skillCamera, "Steps within one action reuse their owned camera clone");
            Check(new[] { firstCamera, secondCamera, skillCamera }.All(item => AssetDatabase.GetAssetPath(item) == path), "Every tuned camera is a subasset of the single exported file");
            Check(JsonUtility.ToJson(saved.Actions.ComboSteps[2]) == JsonUtility.ToJson(source.Actions.ComboSteps[2]), "Unselected normal attack is unchanged");
            Check(saved.Actions.UltimateSteps.Select(step => JsonUtility.ToJson(step)).SequenceEqual(source.Actions.UltimateSteps.Select(step => JsonUtility.ToJson(step)))
                && JsonUtility.ToJson(saved.HeavyAttackStep) == JsonUtility.ToJson(source.HeavyAttackStep), "Unselected ultimate and heavy attack are unchanged");
            Check(saved.Actions.ComboSteps[2].Feedback.Camera.CameraProfile == profile, "Unselected shared camera reference is preserved");
            Check(EditorJsonUtility.ToJson(source) == sourceJson && EditorJsonUtility.ToJson(profile) == cameraJson,
                "Source form and shared camera remain unchanged in memory");
            Check(File.ReadAllText(folder + "/Source.asset") == sourceFile && File.ReadAllText(folder + "/Camera.asset") == cameraFile,
                "Source form and shared camera files remain unchanged");

            var reversed = Copy(request);
            reversed.selections.Reverse();
            var reverseSaved = (WaterFormActionDataSO)CombatLab.ExportCombined(reversed, folder + "/Reverse.asset");
            Equal(reverseSaved.Actions.ComboSteps[0].Balance.KnockbackForce, saved.Actions.ComboSteps[0].Balance.KnockbackForce, "Selection order does not change first action values");
            Equal(reverseSaved.Actions.ComboSteps[1].Feedback.Camera.CameraProfile.HitImpulse, secondCamera.HitImpulse, "Selection order does not change shared-camera scaling");

            var one = (WaterFormActionDataSO)CombatLab.ExportCombined(Request(sourceGuid, first), folder + "/One.asset");
            Check(AssetDatabase.IsMainAsset(one) && AssetDatabase.LoadAllAssetsAtPath(folder + "/One.asset").OfType<WeaponActionDataSO>().Count() == 1,
                "A single selected action also exports a direct form asset");
            var combo = Selection(source, sourceGuid, "normal-combo");
            var comboSaved = (WaterFormActionDataSO)CombatLab.ExportCombined(Request(sourceGuid, combo, skill), folder + "/Combo.asset");
            Check(AssetDatabase.LoadAllAssetsAtPath(folder + "/Combo.asset").OfType<AttackCameraProfile>().Count() == 2
                && comboSaved.Actions.ComboSteps.All(step => step.Feedback.Camera.CameraProfile == comboSaved.Actions.ComboSteps[0].Feedback.Camera.CameraProfile),
                "Entire combo and skill share only within each selected action");

            void Reject(string label, Action<CombinedExportRequest> change, bool direct = false, string expectedMessage = null)
            {
                var invalid = Copy(request);
                change(invalid);
                string rejectedPath = folder + "/Rejected.asset";
                bool rejected = false;
                try
                {
                    if (direct) new FourTigerCombatAdapter().ExportCombined(invalid, rejectedPath);
                    else CombatLab.ExportCombined(invalid, rejectedPath);
                }
                catch (ArgumentException error) { rejected = expectedMessage == null || error.Message.Contains(expectedMessage); }
                Check(rejected, label);
                Check(!File.Exists(rejectedPath) && !File.Exists(rejectedPath + ".meta")
                    && AssetDatabase.LoadMainAssetAtPath(rejectedPath) == null, label + " leaves no partial asset");
            }
            Reject("Unsupported combined schema is rejected", invalid => invalid.schemaVersion = 2);
            Reject("Empty selections are rejected", invalid => invalid.selections.Clear());
            Reject("More than 32 selections are rejected before inspecting entries", invalid =>
                invalid.selections = Enumerable.Repeat<CombatActionSelection>(null, 33).ToList(), expectedMessage: "1~32");
            Reject("Null selection is rejected", invalid => invalid.selections[0] = null);
            Reject("Mixed source forms are rejected", invalid => invalid.selections[1].request.sourceGuid = "different-source");
            Reject("Mixed adapters are rejected", invalid => invalid.selections[1].request.adapterId = "different-adapter");
            Reject("Duplicate action IDs are rejected", invalid => invalid.selections[1] = invalid.selections[0]);
            Reject("Legacy default and normal-combo are treated as duplicate actions", invalid => {
                invalid.selections[0].request.actionId = null;
                invalid.selections[1].request.actionId = "normal-combo";
            });
            Reject("Combo and individual normal attack overlap is rejected", invalid => invalid.selections[0].request.actionId = "normal-combo");
            Reject("An unknown selected candidate is rejected", invalid => invalid.selections[1].candidateId = "missing");
            Reject("Malformed values in another action are rejected before exporting", invalid => invalid.selections[1].request.candidates[0].values.Clear());
            Reject("Direct adapter export also rejects overlapping actions", invalid => invalid.selections[0].request.actionId = "normal-combo", true);

            // Fail while applying the second action, after allocating the form and first camera clone.
            source.Actions.ComboSteps[1].Balance.KnockbackForce = float.MaxValue;
            int formsBefore = Resources.FindObjectsOfTypeAll<WeaponActionDataSO>().Length;
            int camerasBefore = Resources.FindObjectsOfTypeAll<AttackCameraProfile>().Length;
            Reject("Application overflow rolls back the whole combined export", invalid => Set(invalid.selections[1], FourTigerCombatAdapter.KnockbackId, 2f));
            Check(Resources.FindObjectsOfTypeAll<WeaponActionDataSO>().Length == formsBefore
                && Resources.FindObjectsOfTypeAll<AttackCameraProfile>().Length == camerasBefore, "Application rollback destroys every temporary form and camera clone");
            source.Actions.ComboSteps[1].Balance.KnockbackForce = 8f;

            foreach (var buff in new WeaponActionDataSO[] { ScriptableObject.CreateInstance<FireFormActionDataSO>(),
                ScriptableObject.CreateInstance<EarthFormActionDataSO>(), ScriptableObject.CreateInstance<WoodFormActionDataSO>() })
            {
                owned.Add(buff);
                buff.name = buff.GetType().Name;
                buff.Actions.ComboSteps = new List<WeaponActionData> { Step(profile) };
                buff.Actions.SkillSteps = new List<WeaponActionData> { Step(profile) };
                buff.Actions.UltimateSteps = null;
                string buffSourcePath = folder + "/" + buff.name + ".asset";
                AssetDatabase.CreateAsset(buff, buffSourcePath);
                string buffGuid = AssetDatabase.AssetPathToGUID(buffSourcePath);
                string buffBefore = EditorJsonUtility.ToJson(buff);
                var normal = Selection(buff, buffGuid, "normal-1");
                Set(normal, FourTigerCombatAdapter.KnockbackId, 0.5f);
                var ultimate = Selection(buff, buffGuid, "ultimate");
                Set(ultimate, buff is WoodFormActionDataSO ? "ultimateElementBonusPercent" : "ultimateDamageBonusPercent", 80f);
                var buffRequest = Request(buffGuid, normal, ultimate);
                if (buff is WoodFormActionDataSO)
                {
                    var gather = Selection(buff, buffGuid, "skill");
                    Set(gather, "skillRadiusMultiplier", 1.5f);
                    Set(gather, "skillDamageMultiplier", 2f);
                    buffRequest.selections.Add(gather);
                }
                var buffSaved = (WeaponActionDataSO)CombatLab.ExportCombined(buffRequest, folder + "/" + buff.name + "Combined.asset");
                Equal(buffSaved.Actions.ComboSteps[0].Balance.KnockbackForce, 4f, buff.name + " combines normal attack tuning with a buff ultimate");
                float bonus = buffSaved is FireFormActionDataSO fire ? fire.FormBalance.FireUltimateDamageDealtBonusPercent
                    : buffSaved is EarthFormActionDataSO earth ? earth.FormBalance.UltimateShieldDamageBonusPercent
                    : ((WoodFormActionDataSO)buffSaved).FormBalance.ElementDamageBonusPercent;
                Equal(bonus, 80f, buff.name + " saves the independently selected buff candidate");
                if (buffSaved is WoodFormActionDataSO woodSaved && buff is WoodFormActionDataSO woodSource)
                {
                    Equal(woodSaved.FormExecution.SkillRadius, woodSource.FormExecution.SkillRadius * 1.5f, "Wood skill and ultimate keep separate form execution fields");
                    Equal(woodSaved.FormBalance.SkillDamageMultiplier, woodSource.FormBalance.SkillDamageMultiplier * 2f, "Wood skill and ultimate keep separate form balance fields");
                }
                Check(EditorJsonUtility.ToJson(buff) == buffBefore, buff.name + " source remains unchanged");
            }
            return JsonUtility.ToJson(new Result { checks = passed.Count, passed = passed.ToArray() });
        }
        finally
        {
            if (folder != null && AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null && !EditorUtility.IsPersistent(owned[i])) Object.DestroyImmediate(owned[i]);
        }
    }

    private static WeaponActionData Step(AttackCameraProfile profile)
    {
        var step = new WeaponActionData();
        step.Balance.KnockbackForce = 8f;
        step.Feedback.Camera.CameraProfile = profile;
        step.Feedback.Camera.CameraShakeStrength = 2f;
        step.Feedback.HitStop.OverrideHitStop = true;
        step.Feedback.HitStop.HitStopDuration = 0.07f;
        step.Feedback.HitStop.HitStopTimeScale = 0.1f;
        return step;
    }

    private static CombatActionSelection Selection(WeaponActionDataSO source, string guid, string actionId)
    {
        var candidate = new CombatCandidate { id = "chosen", label = actionId,
            values = FourTigerCombatAdapter.ParameterSpecs(source, actionId).Select(spec => new ParameterValue(spec.id, spec.baseline)).ToList() };
        return new CombatActionSelection { candidateId = candidate.id,
            request = new ExperimentRequest { adapterId = FourTigerCombatAdapter.AdapterId, sourceGuid = guid,
                actionId = actionId, durationSeconds = 3f, candidates = new List<CombatCandidate> { candidate } } };
    }

    private static CombinedExportRequest Request(string guid, params CombatActionSelection[] selections)
        => new CombinedExportRequest { adapterId = FourTigerCombatAdapter.AdapterId, sourceGuid = guid, selections = selections.ToList() };
    private static CombinedExportRequest Copy(CombinedExportRequest request)
        => JsonUtility.FromJson<CombinedExportRequest>(JsonUtility.ToJson(request));
    private static void Set(CombatActionSelection selection, string id, float value)
        => selection.request.candidates.Single(candidate => candidate.id == selection.candidateId).values.Single(item => item.id == id).value = value;
}
