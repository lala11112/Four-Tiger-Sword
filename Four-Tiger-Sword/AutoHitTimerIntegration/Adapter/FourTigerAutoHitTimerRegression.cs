using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StudioBaekho.AutoHitTimer.FourTigerSword
{
    // No NUnit/asmdef dependency: this integration lives beside Assembly-CSharp in the game's Editor folder.
    // Run in an isolated validation project: -executeMethod StudioBaekho.AutoHitTimer.FourTigerSword.FourTigerAutoHitTimerRegression.Run
    public static class FourTigerAutoHitTimerRegression
    {
        [MenuItem("Tools/Studio Baekho/Auto Hit Timer/Run Adapter Regression")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("편집 모드에서 실행하세요.");
            Mapping();
            AssetTransactions();
            ExistingStateClipChange(false);
            ExistingStateClipChange(true);
            Debug.Log("[Auto Hit Timer] Four Tiger adapter regression PASS: mapping, inheritance, time guards, support matrix, stale checks, duplicate apply, copy, registration, empty-state assignment, clip replacement, existing-action time editing, rollback, Undo.");
        }

        private static void Mapping()
        {
            var action = new WeaponActionData { Damage = 27, PoiseDamage = 31, Duration = 1, HitStartTime = 0.1f, HitDuration = 0.2f };
            action.Feedback.HitStop.OverrideHitStop = true;
            action.Feedback.HitStop.HitStopDuration = 0.17f;
            var single = FourTigerHitMapping.Map(action, Windows(0.2f, 0.4f, 0), 1);
            Assert(single.HitEvents.Count == 0 && Near(single.HitStartTime, 0.2f) && Near(single.HitDuration, 0.2f), "legacy single-hit mapping");
            Assert(Near(single.Damage, 27) && Near(single.Feedback.HitStop.HitStopDuration, 0.17f), "action balance and feedback preservation");
            Assert(Near(action.HitStartTime, 0.1f), "mapping must not mutate input");
            var multi = FourTigerHitMapping.Map(action, new JArray(Window(0.2f, 0.3f, 0), Window(0.5f, 0.6f, -1)), 1);
            Assert(multi.HitEvents.Count == 2 && multi.HitEvents.All(h => h.Damage == 0 && h.PoiseDamage == 0), "new events inherit damage and poise");
            Assert(multi.HitEvents.All(h => !h.OverrideHitStop && !h.OverrideHitVFX && !h.OverrideKnockbackForce), "new event overrides disabled");
            Assert(Near(FourTigerHitMapping.DamageSum(multi), 54), "added hit damage preview");
            multi.HitEvents[1].Damage = 99;
            multi.HitEvents[1].OverrideHitStop = true;
            multi.HitEvents[1].HitStopDuration = 0.25f;
            var survivor = FourTigerHitMapping.Map(multi, Windows(0.4f, 0.7f, 1), 1);
            Assert(survivor.HitEvents.Count == 1 && survivor.HitEvents[0].Damage == 99 && survivor.HitEvents[0].OverrideHitStop,
                "reducing multi-hit must preserve selected event overrides");
            Assert(Near(survivor.HitEvents[0].HitStopDuration, 0.25f), "mapped hit feedback preservation");
            var none = FourTigerHitMapping.Map(multi, new JArray(), 1);
            Assert(none.HitEvents.Count == 0 && none.HitDuration == 0 && FourTigerHitMapping.DamageSum(none) == 0, "zero hits disables legacy fallback");
            Throws(() => FourTigerHitMapping.Map(action, Windows(-0.1f, 0.2f, 0), 1), "negative start rejected");
            Throws(() => FourTigerHitMapping.Map(action, Windows(0.1f, 1.1f, 0), 1), "beyond clip rejected");
            Throws(() => FourTigerHitMapping.Map(action, new JArray(Window(0.2f, 0.3f, 0), Window(0.4f, 0.5f, 0)), 1), "duplicate old mapping rejected");
            Throws(() => FourTigerHitMapping.Map(action, new JArray(Window(0.5f, 0.6f, 0), Window(0.1f, 0.2f, -1)), 1), "unsorted windows rejected");
            Throws(() => FourTigerHitMapping.ApplyTimeOverrides(action, new JArray(new JObject { ["path"] = "Balance.Damage", ["value"] = 0 })), "time override whitelist");
            Assert(FourTigerHitMapping.InvalidTimes(action, 0.1f).Count > 0, "short clip template time guard");
            FourTigerHitMapping.ApplyTimeOverrides(action, new JArray(new JObject { ["path"] = "Execution.ComboTransitionTime", ["value"] = 0.8 }));
            Assert(Near(action.ComboTransitionTime, 0.8f), "time override applies");
            var fire = ScriptableObject.CreateInstance<FireFormActionDataSO>();
            var iron = ScriptableObject.CreateInstance<IronFormActionDataSO>();
            var water = ScriptableObject.CreateInstance<WaterFormActionDataSO>();
            try
            {
                Assert(FourTigerHitMapping.UnsupportedReason(fire, "skill") == null && FourTigerHitMapping.UnsupportedReason(fire, "ultimate") != null, "fire support matrix");
                Assert(FourTigerHitMapping.UnsupportedReason(iron, "ultimate") == null, "iron ultimate support");
                Assert(FourTigerHitMapping.UnsupportedReason(water, "combo") == null && FourTigerHitMapping.UnsupportedReason(water, "heavy") != null, "water special exclusion");
            }
            finally { UnityEngine.Object.DestroyImmediate(fire); UnityEngine.Object.DestroyImmediate(iron); UnityEngine.Object.DestroyImmediate(water); }
        }

        private static void AssetTransactions()
        {
            var previousScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(previousScene.path))
                throw new InvalidOperationException("검증 프로젝트의 시작 씬을 저장한 뒤 회귀 검사를 실행하세요. 기존 미저장 씬은 자동으로 저장하거나 폐기하지 않습니다.");
            string folder = "Assets/__AutoHitTimerRegression_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var clip = new AnimationClip { name = "RegressionAttack", frameRate = 60 };
                clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 1, 1));
                AssetDatabase.CreateAsset(clip, folder + "/Attack.anim");
                var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Actor.controller");
                var state = controller.layers[0].stateMachine.AddState("Attack");
                state.motion = clip;
                var go = new GameObject("Auto Hit Timer regression actor");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                var animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                var source = ScriptableObject.CreateInstance<FireFormActionDataSO>();
                source.name = "Regression Fire";
                source.ComboSteps = new List<WeaponActionData> { new WeaponActionData { AnimationName = "Attack", Duration = 1, Damage = 23, ComboTransitionTime = 0.6f } };
                source.ComboSteps[0].Feedback.HitStop.OverrideHitStop = true;
                source.ComboSteps[0].Feedback.HitStop.HitStopDuration = 0.17f;
                AssetDatabase.CreateAsset(source, folder + "/Form.asset");
                AssetDatabase.SaveAssets();
                EditorSceneManager.SaveScene(scene, folder + "/Actor.unity");
                string actorId = GlobalObjectId.GetGlobalObjectIdSlow(animator).ToString();
                var adapter = new FourTigerAutoHitTimerAdapter();
                var request = new JObject { ["actorId"] = actorId, ["sourceGuid"] = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source)),
                    ["actionPath"] = "Actions.ComboSteps.Array.data[0]", ["mode"] = "update", ["clip"] = AutoHitTimerAssets.ClipRef(clip),
                    ["windows"] = Windows(0.25f, 0.45f, 0), ["copy"] = false,
                    ["timeOverrides"] = new JArray(new JObject { ["path"] = "Execution.ComboTransitionTime", ["value"] = 0.8f }) };
                var preview = adapter.PreviewChange(request);
                Assert((bool)preview["canApply"], "existing action preview");
                Assert(!(bool)preview["controllerChanged"] && preview["clipAssignment"].Type == JTokenType.Null, "same clip edits do not change controller");
                Assert(((JArray)preview["editableTimeSettings"]).Any(t => (string)t["path"] == "Execution.ComboTransitionTime" && Near((float)t["value"], 0.8f)),
                    "existing action preview exposes edited time settings");
                Assert((string)adapter.SaveStatus(new JObject { ["changeId"] = preview["changeId"] })["status"] == "preview", "save status before mutation");
                Assert((string)adapter.SaveStatus(new JObject { ["changeId"] = Guid.NewGuid().ToString("N") })["status"] == "missing", "missing save status");
                source.ComboSteps[0].Damage = 24;
                Throws(() => adapter.Apply(new JObject { ["changeId"] = preview["changeId"] }), "stale SO rejected");
                source.ComboSteps[0].Damage = 23;
                preview = adapter.PreviewChange(request);
                string controllerBefore = File.ReadAllText(AssetDatabase.GetAssetPath(controller));
                var result = adapter.Apply(new JObject { ["changeId"] = preview["changeId"] });
                Assert(Near(source.ComboSteps[0].HitStartTime, 0.25f) && Near(source.ComboSteps[0].ComboTransitionTime, 0.8f) &&
                    source.ComboSteps[0].Damage == 23 && source.ComboSteps[0].Feedback.HitStop.OverrideHitStop &&
                    Near(source.ComboSteps[0].Feedback.HitStop.HitStopDuration, 0.17f), "existing action edits timing while preserving balance and feedback");
                Assert(((JArray)result["assets"]).Count == 1 && File.ReadAllText(AssetDatabase.GetAssetPath(controller)) == controllerBefore && state.motion == clip,
                    "same clip time edit persists only the SO");
                var repeated = adapter.Apply(new JObject { ["changeId"] = preview["changeId"] });
                Assert(JToken.DeepEquals(result, repeated) && source.ComboSteps.Count == 1, "duplicate apply returns previous result");
                var saveStatus = adapter.SaveStatus(new JObject { ["changeId"] = preview["changeId"] });
                Assert((string)saveStatus["status"] == "complete" && JToken.DeepEquals(result, saveStatus["result"]), "durable completed result can be recovered without mutation");
                Undo.PerformUndo();
                Assert(Near(source.ComboSteps[0].HitStartTime, 0.1f) && Near(source.ComboSteps[0].ComboTransitionTime, 0.6f), "Undo restores original hit and execution timing");
                AssetDatabase.SaveAssetIfDirty(source);

                request["mode"] = "register";
                request["group"] = "combo";
                request["templatePath"] = "Actions.ComboSteps.Array.data[0]";
                request["stateName"] = "Attack";
                request["copy"] = true;
                request["outputFolder"] = folder + "/New/Nested";
                preview = adapter.PreviewChange(request);
                Assert((bool)preview["canApply"] && (string)preview["proposedStateName"] == "Attack_2", "registration preview creates unique name");
                Assert((int)preview["hitCountBefore"] == 0, "append preview counts no previous destination hits");
                result = adapter.Apply(new JObject { ["changeId"] = preview["changeId"] });
                var assets = (JArray)result["assets"];
                var copiedSource = AssetDatabase.LoadAssetAtPath<FireFormActionDataSO>((string)assets[0]["path"]);
                var copiedController = AssetDatabase.LoadAssetAtPath<AnimatorController>((string)assets[1]["path"]);
                Assert(copiedSource.ComboSteps.Count == 2 && source.ComboSteps.Count == 1, "copy registration preserves original SO");
                Assert(controller.layers[0].stateMachine.states.Length == 1 && copiedController.layers[0].stateMachine.states.Length == 2, "copy registration preserves original controller");
                Assert(copiedSource.ComboSteps[1].AnimationName == "Attack_2" && Near(copiedSource.ComboSteps[1].Duration, clip.length), "new action state and duration");
                Assert(copiedController.layers[0].stateMachine.states.Any(s => s.state.name == "Attack_2" && s.state.motion == clip), "new controller state clip");
                Assert(JToken.DeepEquals(result, adapter.Apply(new JObject { ["changeId"] = preview["changeId"] })), "duplicate registration does not append again");

                request["outputFolder"] = folder + "/Rollback/Nested";
                preview = adapter.PreviewChange(request);
                FourTigerAutoHitTimerAdapter.BeforePersistForTests = () => { throw new IOException("Injected persistence failure"); };
                try { Throws(() => adapter.Apply(new JObject { ["changeId"] = preview["changeId"] }), "save failure rolls back"); }
                finally { FourTigerAutoHitTimerAdapter.BeforePersistForTests = null; }
                Assert((string)adapter.SaveStatus(new JObject { ["changeId"] = preview["changeId"] })["status"] == "rolled-back", "durable rollback status");
                Assert(!AssetDatabase.IsValidFolder(folder + "/Rollback"), "failed copy removes created assets and empty folders");
                Assert(source.ComboSteps.Count == 1 && controller.layers[0].stateMachine.states.Length == 1, "failed registration preserves original assets");

                request["mode"] = "update";
                request["copy"] = false;
                state.speed = 2;
                preview = adapter.PreviewChange(request);
                Assert(!(bool)preview["canApply"], "non-unit state speed blocked");
                state.speed = 1;
                state.cycleOffset = 0.3f;
                Assert(!(bool)adapter.PreviewChange(request)["canApply"], "state cycle offset blocked");
                state.cycleOffset = 0;
                source.ComboSteps.Add(FourTigerHitMapping.Clone(source.ComboSteps[0]));
                Assert(!(bool)adapter.PreviewChange(request)["canApply"], "consecutive same-state steps blocked");
            }
            finally
            {
                FourTigerAutoHitTimerAdapter.BeforePersistForTests = null;
                EditorSceneManager.CloseScene(scene, true);
                if (previousScene.IsValid() && previousScene.isLoaded) UnityEngine.SceneManagement.SceneManager.SetActiveScene(previousScene);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static void ExistingStateClipChange(bool replaceExisting)
        {
            var previousScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(previousScene.path))
                throw new InvalidOperationException("검증 프로젝트의 시작 씬을 저장한 뒤 회귀 검사를 실행하세요.");
            string folder = "Assets/__AutoHitTimerEmptyStateRegression_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                string sourcePath = folder + "/Form.asset";
                string controllerPath = folder + "/Actor.controller";
                string clipPath = folder + "/Attack.anim";
                var clip = new AnimationClip { name = replaceExisting ? "ReplacementAttack" : "RecoveredAttack", frameRate = 60 };
                clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, replaceExisting ? 2 : 1, 1));
                AssetDatabase.CreateAsset(clip, clipPath);
                var otherClip = new AnimationClip { name = "OtherAttack", frameRate = 60 };
                otherClip.SetCurve("", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0, 0, 1, 1));
                if (!replaceExisting) AssetDatabase.CreateAsset(otherClip, folder + "/Other.anim");
                var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                if (replaceExisting) AssetDatabase.AddObjectToAsset(otherClip, controller);
                var root = controller.layers[0].stateMachine;
                var idle = root.AddState("Idle");
                idle.motion = otherClip;
                root.defaultState = idle;
                var machine = root.AddStateMachine("Attacks");
                var state = machine.AddState("EmptyAttack", new Vector3(220, 130, 0));
                var originalMotion = replaceExisting ? otherClip : null;
                state.motion = originalMotion;
                state.tag = "PreservedAttackTag";
                state.writeDefaultValues = true;
                state.iKOnFeet = true;
                var transition = state.AddExitTransition();
                transition.hasExitTime = true;
                transition.exitTime = 0.85f;
                transition.duration = 0.14f;
                transition.hasFixedDuration = true;
                string stateName = controller.layers[0].name + ".Attacks.EmptyAttack";
                var source = ScriptableObject.CreateInstance<FireFormActionDataSO>();
                source.name = "Empty state regression Fire";
                source.ComboSteps = new List<WeaponActionData> { new WeaponActionData {
                    AnimationName = stateName, Duration = 1, Damage = 37, PoiseDamage = 19, HitStartTime = 0.1f, HitDuration = 0.2f, ComboTransitionTime = 0.8f } };
                source.ComboSteps[0].Feedback.HitStop.OverrideHitStop = true;
                source.ComboSteps[0].Feedback.HitStop.HitStopDuration = 0.19f;
                AssetDatabase.CreateAsset(source, sourcePath);
                var go = new GameObject("Empty state regression actor");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                var animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                AssetDatabase.SaveAssets();
                EditorSceneManager.SaveScene(scene, folder + "/Actor.unity");
                string actorId = GlobalObjectId.GetGlobalObjectIdSlow(animator).ToString();
                string sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);
                var adapter = new FourTigerAutoHitTimerAdapter();
                float hitStart = replaceExisting ? 1.25f : 0.25f;
                float transitionTime = replaceExisting ? 1.8f : 0.8f;
                var request = new JObject { ["actorId"] = actorId, ["sourceGuid"] = sourceGuid,
                    ["actionPath"] = "Actions.ComboSteps.Array.data[0]", ["mode"] = "update", ["clip"] = AutoHitTimerAssets.ClipRef(clip),
                    ["windows"] = Windows(hitStart, hitStart + 0.2f, 0), ["copy"] = false,
                    ["timeOverrides"] = new JArray(new JObject { ["path"] = "Execution.ComboTransitionTime", ["value"] = transitionTime }) };
                Func<JObject> target = () => ((JArray)adapter.Targets(actorId)["targets"]).OfType<JObject>().Single(t =>
                    (string)t["sourceGuid"] == sourceGuid && (string)t["actionPath"] == (string)request["actionPath"]);
                Assert((bool)target()["supported"] && (bool)target()["needsClipAssignment"] == !replaceExisting, "existing state is a selectable clip change target");
                if (replaceExisting)
                    Assert((string)target()["currentClip"]?["name"] == otherClip.name &&
                        ((JArray)target()["timeSettings"]).Any(t => (string)t["path"] == "Execution.ComboTransitionTime" && Near((float)t["value"], 0.8f)),
                        "occupied target exposes current clip and execution times");
                var preview = adapter.PreviewChange(request);
                Assert((bool)preview["canApply"] && (bool)preview["controllerChanged"], "empty state preview declares controller mutation");
                Assert((string)preview["clipAssignment"]?["stateName"] == stateName &&
                    (string)preview["clipAssignment"]?["clipName"] == clip.name && (string)preview["clipAssignment"]?["clipPath"] == clipPath,
                    "empty state preview identifies exact destination and selected clip");
                Assert((string)preview["clipAssignment"]?["previousClipName"] == (replaceExisting ? otherClip.name : null) &&
                    (string)preview["clipAssignment"]?["previousClipPath"] == (replaceExisting ? AssetDatabase.GetAssetPath(otherClip) : null),
                    "clip change preview identifies previous motion");
                Assert(Near((float)preview["after"]["duration"], clip.length), "clip change derives action duration from selected clip");
                Assert(state.motion == originalMotion && Near(source.ComboSteps[0].HitStartTime, 0.1f), "assignment preview does not mutate assets");

                // Occupying an empty state after review must never overwrite the newly assigned clip.
                var interveningClip = replaceExisting ? clip : otherClip;
                state.motion = interveningClip;
                Throws(() => adapter.Apply(new JObject { ["changeId"] = preview["changeId"] }), "occupied-after-preview state rejected");
                Assert(state.motion == interveningClip && Near(source.ComboSteps[0].HitStartTime, 0.1f), "stale assignment leaves both assets untouched");
                Assert((bool)adapter.PreviewChange(request)["canApply"], "a fresh review supports an occupied state");
                state.motion = originalMotion;
                preview = adapter.PreviewChange(request);
                state.tag = "ChangedAfterReview";
                Throws(() => adapter.Apply(new JObject { ["changeId"] = preview["changeId"] }), "state settings changed after preview rejected");
                state.tag = "PreservedAttackTag";

                // Allowing a missing motion does not bypass the timing and state support rules.
                state.speed = 2;
                Assert(!(bool)adapter.PreviewChange(request)["canApply"] && !(bool)target()["supported"], "empty state non-unit speed blocked");
                state.speed = 1;
                state.speedParameterActive = true;
                Assert(!(bool)adapter.PreviewChange(request)["canApply"], "empty state dynamic speed blocked");
                state.speedParameterActive = false;
                state.timeParameterActive = true;
                Assert(!(bool)adapter.PreviewChange(request)["canApply"], "empty state dynamic time blocked");
                state.timeParameterActive = false;
                state.cycleOffset = 0.2f;
                Assert(!(bool)adapter.PreviewChange(request)["canApply"], "empty state offset blocked");
                state.cycleOffset = 0;
                state.mirror = true;
                Assert(!(bool)adapter.PreviewChange(request)["canApply"], "empty state mirroring blocked");
                state.mirror = false;
                var blendTree = new BlendTree { name = "UnsupportedBlendTree" };
                AssetDatabase.AddObjectToAsset(blendTree, controller);
                state.motion = blendTree;
                Assert(!(bool)adapter.PreviewChange(request)["canApply"] && !(bool)target()["supported"], "BlendTree remains unsupported");
                state.motion = originalMotion;
                source.ComboSteps.Add(FourTigerHitMapping.Clone(source.ComboSteps[0]));
                Assert(!(bool)adapter.PreviewChange(request)["canApply"], "empty state consecutive same-state steps blocked");
                source.ComboSteps.RemoveAt(1);
                EditorUtility.SetDirty(state);
                EditorUtility.SetDirty(source);
                AssetDatabase.SaveAssets();

                string stateId = GlobalObjectId.GetGlobalObjectIdSlow(state).ToString();
                string transitionId = GlobalObjectId.GetGlobalObjectIdSlow(transition).ToString();
                string sourceBefore = File.ReadAllText(sourcePath);
                string controllerBefore = File.ReadAllText(controllerPath);
                preview = adapter.PreviewChange(request);
                var result = adapter.Apply(new JObject { ["changeId"] = preview["changeId"] });
                Assert(((JArray)result["assets"]).Count == 2, "original assignment reports SO and controller");
                Assert(state.motion == clip && GlobalObjectId.GetGlobalObjectIdSlow(state).ToString() == stateId,
                    "original assignment fills the same existing state");
                AssertEmptyStateSettings(state, transitionId);
                Assert(machine.states.Length == 1 && root.states.Length == 1 && root.defaultState == idle,
                    "assignment preserves state graph and default state");
                Assert(source.ComboSteps.Count == 1 && source.ComboSteps[0].AnimationName == stateName &&
                    Near(source.ComboSteps[0].HitStartTime, hitStart) && Near(source.ComboSteps[0].Duration, clip.length) &&
                    Near(source.ComboSteps[0].ComboTransitionTime, transitionTime) && Near(source.ComboSteps[0].Damage, 37) &&
                    Near(source.ComboSteps[0].PoiseDamage, 19) && source.ComboSteps[0].Feedback.HitStop.OverrideHitStop &&
                    Near(source.ComboSteps[0].Feedback.HitStop.HitStopDuration, 0.19f), "clip change updates duration and times while preserving action balance and feedback");
                string[] backups = Directory.GetFiles((string)result["snapshotFolder"]);
                Assert(backups.Length == 2 && backups.Any(p => File.ReadAllText(p) == sourceBefore) &&
                    backups.Any(p => File.ReadAllText(p) == controllerBefore), "assignment backs up both original assets");
                Assert(JToken.DeepEquals(result, adapter.Apply(new JObject { ["changeId"] = preview["changeId"] })), "duplicate assignment is idempotent");
                Assert(!(bool)target()["needsClipAssignment"], "assigned state no longer requests a clip");
                Undo.PerformUndo();
                Assert(state.motion == originalMotion && Near(source.ComboSteps[0].HitStartTime, 0.1f) && Near(source.ComboSteps[0].Duration, 1) &&
                    Near(source.ComboSteps[0].ComboTransitionTime, 0.8f), "Undo restores original motion, duration and timing together");
                AssertEmptyStateSettings(state, transitionId);
                AssetDatabase.SaveAssets();

                request["copy"] = true;
                request["outputFolder"] = folder + "/Copy/Nested";
                preview = adapter.PreviewChange(request);
                Assert((bool)preview["canApply"], "empty state copy preview");
                result = adapter.Apply(new JObject { ["changeId"] = preview["changeId"] });
                var copiedAssets = (JArray)result["assets"];
                Assert(copiedAssets.Count == 2, "empty state copy returns SO and controller");
                var copiedSource = AssetDatabase.LoadAssetAtPath<FireFormActionDataSO>((string)copiedAssets[0]["path"]);
                var copiedController = AssetDatabase.LoadAssetAtPath<AnimatorController>((string)copiedAssets[1]["path"]);
                var copiedState = copiedController.layers[0].stateMachine.stateMachines.Single(s => s.stateMachine.name == "Attacks").stateMachine.states.Single().state;
                Assert(copiedState.motion == clip && copiedState.name == state.name, "copy assigns the existing nested state");
                if (replaceExisting)
                {
                    var copiedIdleClip = copiedController.layers[0].stateMachine.states.Single(s => s.state.name == "Idle").state.motion;
                    Assert(copiedIdleClip != otherClip && copiedIdleClip.name == otherClip.name && AssetDatabase.GetAssetPath(copiedIdleClip) == AssetDatabase.GetAssetPath(copiedController),
                        "replacement copy supports a controller-owned previous clip without changing its other users");
                }
                AssertEmptyStateSettings(copiedState, null);
                Assert(copiedSource.ComboSteps.Count == 1 && copiedSource.ComboSteps[0].AnimationName == stateName &&
                    Near(copiedSource.ComboSteps[0].HitStartTime, hitStart) && Near(copiedSource.ComboSteps[0].Duration, clip.length) &&
                    Near(copiedSource.ComboSteps[0].ComboTransitionTime, transitionTime) && Near(copiedSource.ComboSteps[0].Damage, 37) &&
                    Near(copiedSource.ComboSteps[0].Feedback.HitStop.HitStopDuration, 0.19f), "copy updates existing action and times without appending");
                Assert(state.motion == originalMotion && Near(source.ComboSteps[0].HitStartTime, 0.1f) && animator.runtimeAnimatorController == controller,
                    "copy leaves source assets and actor controller unchanged");
                Assert(File.ReadAllText(sourcePath) == sourceBefore && File.ReadAllText(controllerPath) == controllerBefore,
                    "copy leaves original asset files unchanged");

                request["outputFolder"] = folder + "/FailedCopy/Nested";
                preview = adapter.PreviewChange(request);
                FourTigerAutoHitTimerAdapter.BeforePersistForTests = () => { throw new IOException("Injected empty-state copy failure"); };
                try { Throws(() => adapter.Apply(new JObject { ["changeId"] = preview["changeId"] }), "empty state copy failure rolls back"); }
                finally { FourTigerAutoHitTimerAdapter.BeforePersistForTests = null; }
                Assert((string)adapter.SaveStatus(new JObject { ["changeId"] = preview["changeId"] })["status"] == "rolled-back" &&
                    !AssetDatabase.IsValidFolder(folder + "/FailedCopy"), "failed assignment copy removes assets and folders");
                Assert(state.motion == originalMotion && Near(source.ComboSteps[0].HitStartTime, 0.1f), "failed copy leaves original state and action unchanged");

                request["copy"] = false;
                preview = adapter.PreviewChange(request);
                FourTigerAutoHitTimerAdapter.BeforePersistForTests = () => { throw new IOException("Injected empty-state original failure"); };
                try { Throws(() => adapter.Apply(new JObject { ["changeId"] = preview["changeId"] }), "empty state original failure rolls back"); }
                finally { FourTigerAutoHitTimerAdapter.BeforePersistForTests = null; }
                Assert((string)adapter.SaveStatus(new JObject { ["changeId"] = preview["changeId"] })["status"] == "rolled-back", "failed original assignment records rollback");
                source = AssetDatabase.LoadAssetAtPath<FireFormActionDataSO>(sourcePath);
                controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                state = controller.layers[0].stateMachine.stateMachines.Single(s => s.stateMachine.name == "Attacks").stateMachine.states.Single().state;
                Assert(state.motion == originalMotion && Near(source.ComboSteps[0].HitStartTime, 0.1f) && Near(source.ComboSteps[0].Duration, 1) &&
                    Near(source.ComboSteps[0].ComboTransitionTime, 0.8f), "rollback restores original motion and action timing in memory");
                Assert(File.ReadAllText(sourcePath) == sourceBefore && File.ReadAllText(controllerPath) == controllerBefore,
                    "rollback restores both original asset files");
                AssertEmptyStateSettings(state, transitionId);

                if (replaceExisting)
                {
                    var shortClip = new AnimationClip { name = "ShortReplacement", frameRate = 60 };
                    shortClip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 0.5f, 1));
                    AssetDatabase.CreateAsset(shortClip, folder + "/Short.anim");
                    request["clip"] = AutoHitTimerAssets.ClipRef(shortClip);
                    request["windows"] = Windows(0.2f, 0.4f, 0);
                    request.Remove("timeOverrides");
                    preview = adapter.PreviewChange(request);
                    Assert(!(bool)preview["canApply"] && ((JArray)preview["requiredTimeOverrides"]).Any(t =>
                        (string)t["path"] == "Execution.ComboTransitionTime" && Near((float)t["max"], shortClip.length)),
                        "shorter replacement requires out-of-range execution times to be corrected");
                    Throws(() => adapter.Apply(new JObject { ["changeId"] = preview["changeId"] }), "invalid shorter replacement cannot save");
                    Assert(state.motion == otherClip && Near(source.ComboSteps[0].Duration, 1), "blocked shorter replacement leaves assets untouched");
                    var forbiddenRequest = (JObject)request.DeepClone();
                    forbiddenRequest["timeOverrides"] = new JArray(new JObject { ["path"] = "Balance.Damage", ["value"] = 99 });
                    Throws(() => adapter.PreviewChange(forbiddenRequest), "existing-action time overrides cannot edit balance");

                    request["timeOverrides"] = new JArray(((JArray)preview["editableTimeSettings"]).Select(t => new JObject {
                        ["path"] = (string)t["path"], ["value"] = (string)t["path"] == "Execution.ComboTransitionTime" ? 0.4f :
                            (string)t["path"] == "Feedback.Slash.SlashVFXStartTime" ? -1f : 0.25f }));
                    preview = adapter.PreviewChange(request);
                    Assert((bool)preview["canApply"] && ((JArray)preview["requiredTimeOverrides"]).Count == 0,
                        "supported time overrides resolve shorter replacement timing errors");
                    adapter.Apply(new JObject { ["changeId"] = preview["changeId"] });
                    Assert(state.motion == shortClip && Near(source.ComboSteps[0].Duration, 0.5f) && Near(source.ComboSteps[0].HitStartTime, 0.2f) &&
                        Near(source.ComboSteps[0].ComboTransitionTime, 0.4f) && FourTigerHitMapping.InvalidTimes(source.ComboSteps[0], shortClip.length).Count == 0,
                        "shorter replacement persists corrected clip, duration and execution times");
                    Assert(Near(source.ComboSteps[0].Damage, 37) && Near(source.ComboSteps[0].Feedback.HitStop.HitStopDuration, 0.19f),
                        "shorter replacement preserves balance and feedback");
                    AssertEmptyStateSettings(state, transitionId);
                    Undo.PerformUndo();
                    Assert(state.motion == otherClip && Near(source.ComboSteps[0].Duration, 1) && Near(source.ComboSteps[0].ComboTransitionTime, 0.8f),
                        "shorter replacement Undo restores original clip and execution times");
                }
            }
            finally
            {
                FourTigerAutoHitTimerAdapter.BeforePersistForTests = null;
                EditorSceneManager.CloseScene(scene, true);
                if (previousScene.IsValid() && previousScene.isLoaded) UnityEngine.SceneManagement.SceneManager.SetActiveScene(previousScene);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static void AssertEmptyStateSettings(AnimatorState state, string transitionId)
        {
            Assert(state.tag == "PreservedAttackTag" && state.writeDefaultValues && state.iKOnFeet && Near(state.speed, 1),
                "assignment preserves existing state settings");
            Assert(state.transitions.Length == 1 && state.transitions[0].isExit && state.transitions[0].hasExitTime &&
                Near(state.transitions[0].exitTime, 0.85f) && Near(state.transitions[0].duration, 0.14f) && state.transitions[0].hasFixedDuration,
                "assignment preserves existing transition settings");
            if (transitionId != null)
                Assert(GlobalObjectId.GetGlobalObjectIdSlow(state.transitions[0]).ToString() == transitionId, "assignment preserves transition identity");
        }

        private static JObject Window(float start, float end, int index) => new JObject { ["start"] = start, ["end"] = end, ["sourceIndex"] = index };
        private static JArray Windows(float start, float end, int index) => new JArray(Window(start, end, index));
        private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.0001f;
        private static void Assert(bool condition, string label) { if (!condition) throw new Exception("Auto Hit Timer regression failed: " + label); }
        private static void Throws(Action action, string label)
        {
            try { action(); } catch (ArgumentException) { return; } catch (InvalidOperationException) { return; }
            throw new Exception("Auto Hit Timer regression expected rejection: " + label);
        }
    }
}
