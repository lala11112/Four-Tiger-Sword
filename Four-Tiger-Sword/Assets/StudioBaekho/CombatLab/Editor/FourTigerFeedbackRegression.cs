// Explicit Edit Mode integration regression. Temporary assets are removed; source game assets are never edited.
using System;
using System.Collections.Generic;
using System.Linq;
using StudioBaekho.CombatLab;
using StudioBaekho.CombatLab.FourTigerSword;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class FourTigerFeedbackRegression
{
    [Serializable] private sealed class Result { public int checks; public string[] passed; }

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run feedback integration fixtures in Edit Mode.");
        var owned = new List<Object>();
        var passed = new List<string>();
        string folder = null;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); passed.Add(message); }
        void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) < .0001f, message);
        void Reject(Action operation, string message)
        {
            bool rejected = false;
            try { operation(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, message);
        }
        try
        {
            folder = "Assets/CombatLabFeedbackRegression_" + Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets", folder.Substring(7))))
                throw new InvalidOperationException("Cannot create isolated regression asset folder.");
            var profile = ScriptableObject.CreateInstance<AttackCameraProfile>();
            owned.Add(profile); profile.name = "Feedback regression shared source profile";
            profile.HitImpulse = 0;
            var sourceKeys = Enumerable.Range(0, 20).Select(i => new Keyframe(i / 19f, Mathf.Sin(i * .2f), .3f, -.2f)).ToArray();
            profile.FovOffset = new AnimationCurve(sourceKeys) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.Loop };
            AssetDatabase.CreateAsset(profile, folder + "/Camera.asset");
            var source = ScriptableObject.CreateInstance<WaterFormActionDataSO>();
            owned.Add(source); source.name = "Feedback regression source";
            var first = Step(profile, 0, .45f);
            first.Balance.KnockbackForce = 0;
            first.HitEvents = new List<HitEvent> {
                Hit(false, 1.8f, .8f, .1f), Hit(false, 2.3f, .9f, .3f)
            };
            var second = Step(profile, 1000, 2f);
            var third = Step(null, 0, .07f);
            source.Actions.ComboSteps = new List<WeaponActionData> { first, second, third };
            source.Actions.SkillSteps = new List<WeaponActionData> { Step(null, 0, .08f) };
            source.Actions.UltimateSteps = new List<WeaponActionData> { Step(null, 0, .09f) };
            source.HeavyAttackStep = Step(null, 0, .1f);
            AssetDatabase.CreateAsset(source, folder + "/Source.asset");
            string guid = AssetDatabase.AssetPathToGUID(folder + "/Source.asset");
            string original = EditorJsonUtility.ToJson(source);
            string originalProfile = EditorJsonUtility.ToJson(profile);
            string untouchedSecond = JsonUtility.ToJson(second);
            string untouchedThird = JsonUtility.ToJson(third);
            string untouchedHit = JsonUtility.ToJson(first.HitEvents[1]);
            string untouchedCamera = JsonUtility.ToJson(first.Feedback.Camera);
            var adapter = new FourTigerCombatAdapter();

            FourTigerCombatAdapter.ValidateSource(source, "normal-1");
            Check(true, "Valid authored duration above the former 0.3s recommendation remains usable");
            var specs = adapter.DescribeFeedback(guid, "normal-1");
            Check(specs.Count > 64, "A multi-hit action exposes more than 64 absolute parameters");
            Near(specs.Single(s => s.id == "step.0.camera.shake.translation.x").baseline, 0,
                "Zero source shake is described as an absolute zero");
            Near(specs.Single(s => s.id == "step.0.hitstop.duration").baseline, .45f,
                "Actual source duration is preserved outside the suggested exploration range");
            Check(adapter.DescribeCurves(guid, "normal-1").Any(c => c.id == "step.0.camera.motion.fovOffset" && c.keys.Count == 20),
                "Describing authored curves does not resample their original 20 keys");
            Check(EditorJsonUtility.ToJson(source) == original && EditorJsonUtility.ToJson(profile) == originalProfile,
                "Describe does not modify source form or shared camera assets");

            var sourceControl = new CombatCandidate { id = "source-control", label = "원본", isSourceControl = true };
            var control = FourTigerCombatAdapter.CloneAndApply(source, sourceControl, owned, "normal-1", 2);
            Check(JsonUtility.ToJson(control.Actions) == JsonUtility.ToJson(source.Actions),
                "Source control bypasses feedback materialization and keeps every hit's exact serialized settings");
            Check(!ReferenceEquals(control.Actions, source.Actions), "Source control still owns a safe runtime form clone");

            var perHit = Candidate("per-hit", new ParameterValue("step.0.hit.0.hitstop.duration", .035f),
                new ParameterValue("step.0.hit.0.hitstop.timeScale", .25f),
                new ParameterValue("step.0.hit.0.hitstop.scope", (float)HitStopScope.Both));
            var hitClone = FourTigerCombatAdapter.CloneAndApply(source, perHit, owned, "normal-1", 2);
            var hitAttack = hitClone.Actions.ComboSteps[0];
            Near(hitAttack.HitEvents[0].Feedback.HitStop.HitStopDuration, .035f, "Only the selected hit gets the absolute stop duration");
            Check(hitAttack.HitEvents[0].Feedback.HitStop.OverrideHitStop && hitAttack.Feedback.EnableV2,
                "Edited hit enables its local override and the v2 custom-action route");
            Check(hitAttack.HitEvents[0].Feedback.HitStop.Scope == HitStopScope.Both,
                "Edited local stop scope survives clone application");
            Check(JsonUtility.ToJson(hitAttack.HitEvents[1]) == untouchedHit,
                "Editing one hit preserves another hit's dormant values and inheritance flags exactly");
            Check(JsonUtility.ToJson(hitAttack.Feedback.Camera) == untouchedCamera,
                "Editing hit stop does not materialize or edit an unrelated camera channel");
            Near(hitAttack.Feedback.HitStop.HitStopDuration, .45f, "Per-hit patch preserves the attack-level duration");
            Check(JsonUtility.ToJson(hitClone.Actions.ComboSteps[1]) == untouchedSecond,
                "Selected-action patch preserves other attacks that share a camera profile");

            var zeroToStrong = Candidate("absolute", new ParameterValue("step.0.knockback", 40),
                new ParameterValue("step.0.camera.shake.translation.x", .8f),
                new ParameterValue("step.0.camera.shake.rotation.z", 20),
                new ParameterValue("step.0.camera.shake.duration", .25f),
                new ParameterValue("step.0.hitstop.duration", .9f));
            zeroToStrong.origin = "ai-edited"; zeroToStrong.revision = 3;
            zeroToStrong.curves.Add(Curve("step.0.camera.motion.positionZ", 0, .6f, 0));
            CombatLab.ValidateData(Request(guid, "normal-1", zeroToStrong));
            var strong = FourTigerCombatAdapter.CloneAndApply(source, zeroToStrong, owned, "normal-1", 2);
            var strongStep = strong.Actions.ComboSteps[0];
            Near(strongStep.Balance.KnockbackForce, 40, "Absolute knockback escapes an originally zero value");
            Near(strongStep.Feedback.Camera.V2.Shake.Translation.x, .8f, "Absolute shake escapes an originally zero amplitude");
            Near(strongStep.Feedback.Camera.V2.Shake.Rotation.z, 20, "Absolute angular shake is stored in degrees");
            Near(strongStep.Feedback.HitStop.HitStopDuration, .9f, "User-authored stop is not clamped to an exploration recommendation");
            Check(strongStep.Feedback.Camera.V2.Shake.Mode == CombatFeedbackChannelMode.Override,
                "Editing a shake scalar activates only that absolute channel");
            Check(strongStep.Feedback.Camera.CameraProfile == profile,
                "Inline v2 feedback does not replace a shared legacy profile reference");
            Check(CurveEqual(profile.FovOffset, strongStep.Feedback.Camera.CameraProfile.FovOffset),
                "Unedited original curve keys, tangents and wrap modes remain exact");
            Check(strongStep.Feedback.Camera.V2.Motion.PositionZ.length == 3,
                "Only an explicitly edited curve is replaced with the supplied linear key set");
            Check(CurveEqual(profile.FovOffset, strongStep.Feedback.Camera.V2.Motion.FovOffset),
                "Editing a motion axis preserves the inherited FOV curve's keys, tangents and wrap modes");

            var inlineSource = Object.Instantiate(source); owned.Add(inlineSource);
            inlineSource.Actions.ComboSteps[0].Feedback.Camera.V2.Shake.Mode = CombatFeedbackChannelMode.Override;
            inlineSource.Actions.ComboSteps[0].Feedback.Camera.V2.Shake.Translation = new Vector3(.2f, 0, 0);
            var response = FourTigerCombatAdapter.CloneAndApply(inlineSource,
                Candidate("response", new ParameterValue("step.0.camera.motion.responseTime", .055f)), owned, "normal-1", 2);
            Check(CurveEqual(profile.FovOffset, response.Actions.ComboSteps[0].Feedback.Camera.V2.Motion.FovOffset),
                "Editing response time under an inline shake override retains effective legacy FOV");
            Near(response.Actions.ComboSteps[0].Feedback.Camera.V2.Shake.Translation.x, .2f,
                "Editing motion keeps an independent inline shake override");
            var inheritedHit = FourTigerCombatAdapter.CloneAndApply(inlineSource,
                Candidate("hit-shake", new ParameterValue("step.0.hit.0.camera.shake.duration", .4f)), owned, "normal-1", 2);
            Near(inheritedHit.Actions.ComboSteps[0].HitEvents[0].Feedback.CameraV2.Shake.Translation.x, .2f,
                "Editing an inherited hit shake's duration preserves the effective step amplitude");

            var profileV2 = Object.Instantiate(profile); owned.Add(profileV2);
            profileV2.EnableV2 = true;
            profileV2.V2.Mode = CombatFeedbackChannelMode.Override;
            profileV2.V2.Motion.FovOffset = AnimationCurve.Linear(0, 7, 1, 7);
            inlineSource.Actions.ComboSteps[0].Feedback.Camera.CameraProfile = profileV2;
            inlineSource.Actions.ComboSteps[0].Feedback.Camera.V2 = new CombatCameraV2Settings();
            var inheritedProfile = FourTigerCombatAdapter.CloneAndApply(inlineSource,
                Candidate("profile-v2", new ParameterValue("step.0.camera.motion.responseTime", .04f)), owned, "normal-1", 2);
            Near(inheritedProfile.Actions.ComboSteps[0].Feedback.Camera.V2.Motion.FovOffset.Evaluate(.5f), 7,
                "An opted-in profile supplies the effective absolute motion context");
            profileV2.EnableV2 = false;
            profileV2.CameraLocalOffset = Vector3.zero;
            var stepped = new[] { new Keyframe(0, 0, float.PositiveInfinity, float.PositiveInfinity),
                new Keyframe(1, 1, float.PositiveInfinity, float.PositiveInfinity) };
            profileV2.FramingWeight = new AnimationCurve(stepped);
            var zeroAxis = FourTigerCombatAdapter.CloneAndApply(inlineSource,
                Candidate("zero-axis", new ParameterValue("step.0.camera.motion.responseTime", .06f)), owned, "normal-1", 2);
            Check(zeroAxis.Actions.ComboSteps[0].Feedback.Camera.V2.Motion.PositionX.keys.All(k =>
                    !float.IsNaN(k.value) && !float.IsNaN(k.inTangent) && !float.IsNaN(k.outTangent)),
                "A zero legacy offset with stepped infinite tangents does not materialize NaN curve data");

            var reduceStrong = Candidate("reduce-strong", new ParameterValue("step.0.camera.shake.translation.x", .01f));
            var reduced = FourTigerCombatAdapter.CloneAndApply(source, reduceStrong, owned, "normal-2", 2);
            Near(reduced.Actions.ComboSteps[1].Feedback.Camera.V2.Shake.Translation.x, .01f,
                "A very strong source can be replaced with a small absolute shake");
            Near(reduced.Actions.ComboSteps[1].Feedback.Camera.CameraShakeStrength, 1000,
                "Absolute tuning preserves the dormant fallback source amplitude");
            var noProfile = FourTigerCombatAdapter.CloneAndApply(source, zeroToStrong, owned, "normal-3", 2);
            Check(noProfile.Actions.ComboSteps[2].Feedback.Camera.CameraProfile == null
                && noProfile.Actions.ComboSteps[2].Feedback.Camera.V2.Shake.Mode == CombatFeedbackChannelMode.Override,
                "Absolute feedback is authored without requiring an existing camera profile");

            var off = Candidate("off", new ParameterValue("step.0.camera.shake.mode", (float)CombatFeedbackChannelMode.Off),
                new ParameterValue("step.0.camera.shake.translation.x", 2));
            var offClone = FourTigerCombatAdapter.CloneAndApply(source, off, owned, "normal-1", 2);
            Check(offClone.Actions.ComboSteps[0].Feedback.Camera.V2.Shake.Mode == CombatFeedbackChannelMode.Off,
                "Explicit Off wins over automatic activation independently of parameter order");

            var keepSource = Object.Instantiate(source); owned.Add(keepSource);
            var keepStep = keepSource.Actions.ComboSteps[0];
            keepStep.HitEvents[0].Feedback.HitStop.OverrideHitStop = true;
            keepStep.HitEvents[0].Feedback.HitStop.HitStopDuration = .123f;
            keepStep.HitEvents[0].Feedback.HitStop.HitStopTimeScale = .15f;
            keepStep.Feedback.Camera.V2.Mode = CombatFeedbackChannelMode.Override;
            keepStep.Feedback.Camera.V2.Motion.FovOffset = new AnimationCurve(new Keyframe(0, -4, .7f, -.3f), new Keyframe(1, 0));
            keepStep.Feedback.Camera.V2.Impact.Mode = CombatFeedbackChannelMode.Override;
            keepStep.Feedback.Camera.V2.Impact.Duration = .22f;
            keepStep.Feedback.Camera.V2.Shake.Mode = CombatFeedbackChannelMode.Override;
            keepStep.Feedback.Camera.V2.Shake.Translation = new Vector3(.2f, .1f, 0);
            string keptStop = JsonUtility.ToJson(keepStep.HitEvents[0].Feedback.HitStop);
            string keptMotion = JsonUtility.ToJson(keepStep.Feedback.Camera.V2.Motion);
            string keptImpact = JsonUtility.ToJson(keepStep.Feedback.Camera.V2.Impact);
            string keptShake = JsonUtility.ToJson(keepStep.Feedback.Camera.V2.Shake);
            var keepStopPatch = Candidate("keep-stop", new ParameterValue("step.0.hit.0.hitstop.mode", 0),
                new ParameterValue("step.0.hit.0.hitstop.duration", .9f), new ParameterValue("step.0.knockback", 14));
            var keptStopClone = FourTigerCombatAdapter.CloneAndApply(keepSource, keepStopPatch, owned, "normal-1", 2);
            Check(JsonUtility.ToJson(keptStopClone.Actions.ComboSteps[0].HitEvents[0].Feedback.HitStop) == keptStop,
                "Explicit source-keep preserves an existing per-hit stop override and discards its conflicting duration patch");
            Near(keptStopClone.Actions.ComboSteps[0].Balance.KnockbackForce, 14,
                "Source-keep for one stop channel does not suppress another channel's patch");
            var keepMotionPatch = Candidate("keep-motion", new ParameterValue("step.0.camera.mode", 0),
                new ParameterValue("step.0.camera.motion.responseTime", .9f),
                new ParameterValue("step.0.camera.shake.translation.x", .6f));
            keepMotionPatch.curves.Add(Curve("step.0.camera.motion.fovOffset", 0, 20, 0));
            var keptMotionClone = FourTigerCombatAdapter.CloneAndApply(keepSource, keepMotionPatch, owned, "normal-1", 2);
            var keptCamera = keptMotionClone.Actions.ComboSteps[0].Feedback.Camera.V2;
            Check(keptCamera.Mode == CombatFeedbackChannelMode.Override && JsonUtility.ToJson(keptCamera.Motion) == keptMotion,
                "Camera source-keep preserves the original inline motion mode and exact curves while discarding motion patches");
            Near(keptCamera.Shake.Translation.x, .6f, "Camera motion source-keep permits an independent shake edit");
            var keepImpactPatch = Candidate("keep-impact", new ParameterValue("step.0.camera.impact.mode", 0),
                new ParameterValue("step.0.camera.impact.duration", .9f), new ParameterValue("step.0.camera.shake.duration", .4f));
            var keptImpactClone = FourTigerCombatAdapter.CloneAndApply(keepSource, keepImpactPatch, owned, "normal-1", 2);
            Check(JsonUtility.ToJson(keptImpactClone.Actions.ComboSteps[0].Feedback.Camera.V2.Impact) == keptImpact,
                "Impact source-keep preserves an existing Override instead of resetting it to parent inheritance");
            var keepShakePatch = Candidate("keep-shake", new ParameterValue("step.0.camera.shake.mode", 0),
                new ParameterValue("step.0.camera.shake.translation.x", 9), new ParameterValue("step.0.camera.impact.duration", .5f));
            var keptShakeClone = FourTigerCombatAdapter.CloneAndApply(keepSource, keepShakePatch, owned, "normal-1", 2);
            Check(JsonUtility.ToJson(keptShakeClone.Actions.ComboSteps[0].Feedback.Camera.V2.Shake) == keptShake,
                "Shake source-keep restores the complete original channel when another camera channel is materialized");
            var onlyKeep = FourTigerCombatAdapter.CloneAndApply(keepSource,
                Candidate("only-keep", new ParameterValue("step.0.camera.mode", 0),
                    new ParameterValue("step.0.hit.0.hitstop.mode", 0)), owned, "normal-1", 2);
            Check(JsonUtility.ToJson(onlyKeep.Actions) == JsonUtility.ToJson(keepSource.Actions),
                "A candidate containing only source-keep modes leaves the cloned source actions byte-for-byte unchanged");
            Reject(() => FourTigerCombatAdapter.CloneAndApply(source, Candidate("unknown", new ParameterValue("step.0.camera.GetType", 1)), owned, "normal-1", 2),
                "Generated binding allowlist rejects arbitrary reflection paths");
            Reject(() => CombatLab.ValidateData(Request(guid, "normal-1", Candidate("bad-enum", new ParameterValue("step.0.hitstop.scope", .5f)))),
                "Imported enum values must be integral");
            var badCurve = Candidate("bad-curve");
            badCurve.curves.Add(new CurveValue { id = "step.0.camera.motion.positionZ",
                keys = new List<CurveKey> { new CurveKey(.5f, 0), new CurveKey(.5f, 1) } });
            Reject(() => CombatLab.ValidateData(Request(guid, "normal-1", badCurve)), "Imported curve rejects duplicate key times");

            string exportPath = folder + "/Preset.asset";
            adapter.Export(Request(guid, "normal-1", zeroToStrong), zeroToStrong, exportPath);
            AssetDatabase.ImportAsset(exportPath, ImportAssetOptions.ForceUpdate);
            var preset = AssetDatabase.LoadAssetAtPath<CombatLabPreset>(exportPath);
            var saved = preset.actionAsset as WaterFormActionDataSO;
            Check(saved != null && EditorUtility.IsPersistent(saved), "V2 preset exports a complete persistent playable form");
            Near(saved.Actions.ComboSteps[0].Feedback.Camera.V2.Shake.Translation.x, .8f,
                "Preset reload retains the absolute shake value");
            Check(preset.candidate.origin == "ai-edited" && preset.candidate.revision == 3
                && preset.candidate.curves.Count == 1 && preset.candidate.curves[0].keys.Count == 3,
                "Candidate copy and preset reload preserve editing provenance and curve patches");

            var secondPatch = Candidate("second", new ParameterValue("step.0.hitstop.duration", .025f),
                new ParameterValue("step.0.hitstop.scope", (float)HitStopScope.Victim));
            var combined = new CombinedExportRequest { adapterId = FourTigerCombatAdapter.AdapterId, sourceGuid = guid,
                selections = new List<CombatActionSelection> {
                    Selection(Request(guid, "normal-1", zeroToStrong)), Selection(Request(guid, "normal-2", secondPatch))
                } };
            string combinedPath = folder + "/Combined.asset";
            adapter.ExportCombined(combined, combinedPath);
            AssetDatabase.ImportAsset(combinedPath, ImportAssetOptions.ForceUpdate);
            var combinedSaved = AssetDatabase.LoadMainAssetAtPath(combinedPath) as WaterFormActionDataSO;
            Check(combinedSaved != null, "Combined v2 export keeps the playable form as the main asset");
            Near(combinedSaved.Actions.ComboSteps[0].Feedback.Camera.V2.Shake.Translation.x, .8f,
                "Combined export retains the first action's absolute shake");
            Near(combinedSaved.Actions.ComboSteps[1].Feedback.HitStop.HitStopDuration, .025f,
                "Combined export applies another action's independent stop patch");
            Check(combinedSaved.Actions.ComboSteps[1].Feedback.HitStop.Scope == HitStopScope.Victim,
                "Combined export retains local stop scope");
            Check(JsonUtility.ToJson(combinedSaved.Actions.ComboSteps[2]) == untouchedThird,
                "Combined export preserves an unselected attack and its absent camera profile");
            Check(EditorJsonUtility.ToJson(source) == original && EditorJsonUtility.ToJson(profile) == originalProfile,
                "Clone, rejection, single export and combined export leave source assets unchanged");
            return JsonUtility.ToJson(new Result { checks = passed.Count, passed = passed.ToArray() });
        }
        finally
        {
            if (folder != null && AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null && !EditorUtility.IsPersistent(owned[i])) Object.DestroyImmediate(owned[i]);
        }
    }

    private static WeaponActionData Step(AttackCameraProfile profile, float shake, float stop)
    {
        var step = new WeaponActionData();
        step.Execution.Duration = 1f; step.Execution.AnimationName = "FeedbackRegression";
        step.Feedback.Camera.CameraProfile = profile; step.Feedback.Camera.CameraShakeStrength = shake;
        step.Feedback.HitStop = new HitStopSettings { OverrideHitStop = true, HitStopDuration = stop, HitStopTimeScale = .2f };
        return step;
    }
    private static HitEvent Hit(bool active, float duration, float scale, float start)
        => new HitEvent { Execution = new HitExecutionSettings { StartTime = start, Duration = .05f },
            Feedback = new HitFeedbackSettings { HitStop = new HitStopSettings {
                OverrideHitStop = active, HitStopDuration = duration, HitStopTimeScale = scale } } };
    private static CombatCandidate Candidate(string id, params ParameterValue[] values)
        => new CombatCandidate { id = id, label = id, values = values.ToList() };
    private static CurveValue Curve(string id, float start, float middle, float end)
        => new CurveValue { id = id, keys = new List<CurveKey> { new CurveKey(0, start), new CurveKey(.3f, middle), new CurveKey(1, end) } };
    private static ExperimentRequest Request(string guid, string action, CombatCandidate candidate)
        => new ExperimentRequest { schemaVersion = 2, adapterId = FourTigerCombatAdapter.AdapterId, sourceGuid = guid,
            actionId = action, durationSeconds = 3, candidates = new List<CombatCandidate> { candidate } };
    private static CombatActionSelection Selection(ExperimentRequest request)
        => new CombatActionSelection { request = request, candidateId = request.candidates[0].id };
    private static bool CurveEqual(AnimationCurve a, AnimationCurve b)
    {
        if (a == null || b == null) return a == b;
        if (a.preWrapMode != b.preWrapMode || a.postWrapMode != b.postWrapMode || a.length != b.length) return false;
        var left = a.keys; var right = b.keys;
        for (int i = 0; i < left.Length; i++)
            if (!left[i].Equals(right[i])) return false;
        return true;
    }
}
