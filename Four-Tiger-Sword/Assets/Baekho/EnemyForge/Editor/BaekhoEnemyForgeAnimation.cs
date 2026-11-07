using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace StudioBaekho.EnemyForge
{
    /// <summary>Shared movement tree import, validation, asset construction and isolated preview construction.</summary>
    public static class BaekhoEnemyForgeAnimation
    {
        public static bool IsActive(JObject animation)
            => animation?["move"] is JObject move && ((string)animation["mode"] == "clips" || (bool?)move["overrideController"] == true);

        static void Issue(JArray issues, string path, string message)
            => issues.Add(new JObject { ["severity"] = "error", ["path"] = path, ["message"] = message });

        public static JObject Inspect(JObject reference)
        {
            if (reference == null) return new JObject
            {
                ["parameters"] = new JArray(new JObject { ["name"] = "DirX", ["type"] = "Float" }, new JObject { ["name"] = "DirY", ["type"] = "Float" }),
                ["move"] = null, ["warnings"] = new JArray()
            };
            var controller = BaekhoEnemyForgeCatalog.Resolve(reference, typeof(AnimatorController)) as AnimatorController;
            if (controller == null) throw new InvalidDataException("Animator Controller를 선택하세요.");
            var warnings = new JArray();
            var move = ImportMove(controller, warnings);
            return new JObject
            {
                ["parameters"] = new JArray(controller.parameters.Select(p => new JObject { ["name"] = p.name, ["type"] = p.type.ToString() })),
                ["move"] = move, ["warnings"] = warnings
            };
        }

        public static JObject ImportMove(AnimatorController controller, JArray warnings)
        {
            if (controller == null || controller.layers.Length == 0) return null;
            var state = controller.layers[0].stateMachine.states.FirstOrDefault(s => s.state.name == "Move").state;
            if (!(state?.motion is BlendTree tree)) return null;
            if (tree.blendType != BlendTreeType.SimpleDirectional2D || tree.children.Any(c => !(c.motion is AnimationClip)))
            {
                warnings?.Add("Move에 지원하지 않는 종류 또는 중첩 Blend Tree가 있습니다. 기존 컨트롤러 모드에서는 원본 트리를 유지할 수 있습니다.");
                return null;
            }
            return new JObject
            {
                ["type"] = "blend2d", ["parameterX"] = tree.blendParameter, ["parameterY"] = tree.blendParameterY,
                ["overrideController"] = false,
                ["children"] = new JArray(tree.children.Select(c => new JObject
                {
                    ["clip"] = BaekhoEnemyForgeCatalog.AssetRef(c.motion), ["position"] = new JArray(c.position.x, c.position.y), ["timeScale"] = c.timeScale
                }))
            };
        }

        static bool ParameterName(JToken token)
            => token?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)token) && ((string)token).Length <= 64
                && (string)token == ((string)token).Trim() && !((string)token).Any(char.IsControl);

        public static void ValidateMove(JObject move, JArray issues)
        {
            const string path = "animation.move";
            if (move == null || (string)move["type"] != "blend2d") { Issue(issues, path, "Move는 2D Simple Directional 블렌드 트리를 사용해야 합니다."); return; }
            if (move["overrideController"]?.Type != JTokenType.Boolean) Issue(issues, path + ".overrideController", "기존 Move 재정의 여부가 필요합니다.");
            foreach (string key in new[] { "parameterX", "parameterY" })
                if (!ParameterName(move[key])) Issue(issues, path + "." + key, "파라미터 이름은 앞뒤 공백과 제어 문자 없이 1~64자로 입력하세요.");
            string x = (string)move["parameterX"], y = (string)move["parameterY"];
            if (x == y) Issue(issues, path, "X와 Y에는 서로 다른 Float 파라미터를 사용하세요.");
            if (x == "DirY" || y == "DirX") Issue(issues, path, "DirX는 X축, DirY는 Y축에만 연결할 수 있습니다. 다른 이름의 Float 파라미터를 선택할 수도 있습니다.");
            if (!(move["children"] is JArray children) || children.Count < 2 || children.Count > 32)
            { Issue(issues, path + ".children", "이동 블렌드 트리에는 2~32개의 클립이 필요합니다."); return; }
            var positions = new List<Vector2>();
            for (int i = 0; i < children.Count; i++)
            {
                string childPath = path + ".children." + i;
                if (!(children[i] is JObject child)) { Issue(issues, childPath, "이동 클립 설정이 올바르지 않습니다."); continue; }
                try
                {
                    var values = BaekhoEnemyForgeCatalog.Numbers(child["position"], 2);
                    var position = new Vector2(values[0], values[1]);
                    if (positions.Any(p => (p - position).sqrMagnitude < .00000001f))
                        Issue(issues, childPath + ".position", "같은 위치에 클립을 여러 개 배치할 수 없습니다.");
                    else if (position.sqrMagnitude > .00000001f && positions.Any(p => p.sqrMagnitude > .00000001f
                        && Mathf.Abs(p.x * position.y - p.y * position.x) < .0001f * p.magnitude * position.magnitude && Vector2.Dot(p, position) > 0))
                        Issue(issues, childPath + ".position", "Simple Directional에는 한 방향당 하나의 이동 클립만 배치하세요.");
                    positions.Add(position);
                }
                catch (Exception exception) { Issue(issues, childPath + ".position", exception.Message); }
                var scale = child["timeScale"];
                if (scale == null || (scale.Type != JTokenType.Float && scale.Type != JTokenType.Integer)
                    || float.IsNaN(scale.Value<float>()) || float.IsInfinity(scale.Value<float>()) || scale.Value<float>() <= 0)
                    Issue(issues, childPath + ".timeScale", "클립 재생 배율은 0보다 큰 유한한 숫자여야 합니다.");
                try
                {
                    if (BaekhoEnemyForgeCatalog.Resolve(child["clip"], typeof(AnimationClip)) == null)
                        Issue(issues, childPath + ".clip", "이동 애니메이션 클립을 지정하세요.");
                }
                catch (Exception exception) { Issue(issues, childPath + ".clip", exception.Message); }
            }
        }

        public static void Validate(JObject animation, Animator animator, HashSet<string> required, JArray issues)
        {
            if (animation == null) throw new InvalidDataException("animation is required.");
            string mode = (string)animation["mode"];
            if (mode != "clips" && mode != "controller") throw new InvalidDataException("Animation mode must be clips or controller.");
            var mappings = animation["states"] as JObject ?? throw new InvalidDataException("Animation states are required.");
            if (animation["move"] != null && animation["move"].Type != JTokenType.Null && !(animation["move"] is JObject))
                Issue(issues, "animation.move", "이동 블렌드 트리 설정이 올바르지 않습니다.");
            bool active = IsActive(animation);
            var existing = new HashSet<string>(); var clips = new HashSet<AnimationClip>();
            AnimatorController controller = null;
            if (mode == "controller")
            {
                controller = BaekhoEnemyForgeCatalog.Resolve(animation["controller"], typeof(AnimatorController)) as AnimatorController;
                if (controller == null || controller.layers.Length == 0) Issue(issues, "animation.controller", "상태 레이어가 있는 Animator Controller를 선택하세요.");
                else
                {
                    foreach (var state in controller.layers[0].stateMachine.states)
                    {
                        existing.Add(state.state.name);
                        if (required.Contains(state.state.name) && !(active && state.state.name == "Move") && BaekhoEnemyForgeCatalog.FirstClip(state.state.motion) == null)
                            Issue(issues, "animation.states." + state.state.name, "컨트롤러의 " + state.state.name + " 상태에 재생할 클립이 없습니다. 클립 모드로 전환해 연결하거나 컨트롤러를 수정하세요.");
                    }
                    for (int layer = 0; layer < controller.layers.Length; layer++)
                        CollectClips(controller.layers[layer].stateMachine, clips, active && layer == 0);
                }
            }
            foreach (var mapping in mappings)
            {
                if ((active && mapping.Key == "Move") || mapping.Value.Type == JTokenType.Null || (mode == "controller" && existing.Contains(mapping.Key))) continue;
                try
                {
                    var clip = BaekhoEnemyForgeCatalog.Resolve(mapping.Value, typeof(AnimationClip)) as AnimationClip;
                    if (clip != null) { existing.Add(mapping.Key); clips.Add(clip); }
                }
                catch (Exception exception) { Issue(issues, "animation.states." + mapping.Key, exception.Message); }
            }
            var parameters = new HashSet<string>(new[] { "DirX", "DirY" });
            if (active)
            {
                var move = (JObject)animation["move"];
                ValidateMove(move, issues);
                existing.Add("Move");
                if (ParameterName(move["parameterX"])) parameters.Add((string)move["parameterX"]);
                if (ParameterName(move["parameterY"])) parameters.Add((string)move["parameterY"]);
                if (move["children"] is JArray children)
                    foreach (JObject child in children.OfType<JObject>())
                        try { var clip = BaekhoEnemyForgeCatalog.Resolve(child["clip"], typeof(AnimationClip)) as AnimationClip; if (clip != null) clips.Add(clip); }
                        catch { /* ValidateMove reports the precise child path. */ }
            }
            if (controller != null)
                foreach (var parameter in controller.parameters)
                    if (parameters.Contains(parameter.name) && parameter.type != AnimatorControllerParameterType.Float)
                        Issue(issues, "animation.move", parameter.name + " 파라미터는 Float 형식이어야 합니다.");
            foreach (string state in required)
                if (!existing.Contains(state)) Issue(issues, "animation.states." + state, "필수 상태 " + state + "에 애니메이션을 지정하세요.");
            if (animator == null) return;
            foreach (var clip in clips) ValidateClip(clip, animator, issues);
        }

        static void ValidateClip(AnimationClip clip, Animator animator, JArray issues)
        {
            if (clip.legacy) { Issue(issues, "animation", clip.name + ": Legacy 클립은 Animator에서 사용할 수 없습니다."); return; }
            if (clip.humanMotion)
            {
                if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                    Issue(issues, "animation", clip.name + ": Humanoid 클립을 사용하려면 유효한 Humanoid Avatar가 필요합니다.");
                return;
            }
            var paths = AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip))
                .Select(b => b.path).Where(p => !string.IsNullOrEmpty(p)).Distinct();
            string missing = paths.FirstOrDefault(p => animator.transform.Find(p) == null);
            if (missing != null) Issue(issues, "animation", clip.name + ": 모델에 애니메이션 경로가 없습니다: " + missing);
        }

        static void CollectClips(AnimatorStateMachine machine, HashSet<AnimationClip> clips, bool skipRootMove)
        {
            foreach (var state in machine.states)
                if (!skipRootMove || state.state.name != "Move") CollectClips(state.state.motion, clips);
            foreach (var child in machine.stateMachines) CollectClips(child.stateMachine, clips, false);
        }

        static void CollectClips(Motion motion, HashSet<AnimationClip> clips)
        {
            if (motion is AnimationClip clip) clips.Add(clip);
            else if (motion is BlendTree tree) foreach (var child in tree.children) CollectClips(child.motion, clips);
        }

        public static BlendTree CreateTree(JObject move)
        {
            var issues = new JArray(); ValidateMove(move, issues);
            if (issues.Count > 0) throw new InvalidDataException(string.Join("\n", issues.Select(i => (string)i["message"])));
            return new BlendTree
            {
                name = "Move Blend Tree", blendType = BlendTreeType.SimpleDirectional2D,
                blendParameter = (string)move["parameterX"], blendParameterY = (string)move["parameterY"], useAutomaticThresholds = false,
                children = ((JArray)move["children"]).OfType<JObject>().Select(child =>
                {
                    var position = BaekhoEnemyForgeCatalog.Numbers(child["position"], 2);
                    return new ChildMotion { motion = (AnimationClip)BaekhoEnemyForgeCatalog.Resolve(child["clip"], typeof(AnimationClip)),
                        position = new Vector2(position[0], position[1]), timeScale = (float)child["timeScale"] };
                }).ToArray()
            };
        }

        static void EnsureFloat(AnimatorController controller, string name)
        {
            var existing = controller.parameters.FirstOrDefault(p => p.name == name);
            if (existing == null) controller.AddParameter(name, AnimatorControllerParameterType.Float);
            else if (existing.type != AnimatorControllerParameterType.Float) throw new InvalidDataException(name + " Animator parameter must be float.");
        }

        public static AnimatorController CreateController(JObject animation, string path)
        {
            bool clone = (string)animation["mode"] == "controller";
            AnimatorController controller;
            if (clone)
            {
                var original = BaekhoEnemyForgeCatalog.Resolve(animation["controller"], typeof(AnimatorController));
                if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(original), path)) throw new IOException("Animator Controller copy failed.");
                controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            }
            else controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            EnsureFloat(controller, "DirX"); EnsureFloat(controller, "DirY");
            var machine = controller.layers[0].stateMachine;
            bool active = IsActive(animation);
            foreach (var mapping in (JObject)animation["states"])
            {
                if ((active && mapping.Key == "Move") || mapping.Value.Type == JTokenType.Null) continue;
                var existing = machine.states.FirstOrDefault(s => s.state.name == mapping.Key).state;
                if (clone && existing != null) continue;
                var state = existing ?? machine.AddState(mapping.Key);
                state.motion = (AnimationClip)BaekhoEnemyForgeCatalog.Resolve(mapping.Value, typeof(AnimationClip));
                if (mapping.Key == "Idle") machine.defaultState = state;
            }
            if (active)
            {
                var move = (JObject)animation["move"];
                EnsureFloat(controller, (string)move["parameterX"]); EnsureFloat(controller, (string)move["parameterY"]);
                var tree = CreateTree(move); AssetDatabase.AddObjectToAsset(tree, controller);
                var state = machine.states.FirstOrDefault(s => s.state.name == "Move").state ?? machine.AddState("Move");
                state.motion = tree; EditorUtility.SetDirty(state); EditorUtility.SetDirty(tree);
            }
            EditorUtility.SetDirty(machine); EditorUtility.SetDirty(controller);
            return controller;
        }

        public static AnimatorController CreatePreviewController(JObject move)
        {
            var tree = CreateTree(move);
            var controller = new AnimatorController { name = "Enemy Forge isolated Move preview", hideFlags = HideFlags.HideAndDontSave };
            try
            {
                tree.hideFlags = HideFlags.HideAndDontSave;
                var machine = new AnimatorStateMachine { name = "Base Layer", hideFlags = HideFlags.HideAndDontSave };
                controller.layers = new[] { new AnimatorControllerLayer { name = "Base Layer", defaultWeight = 1, stateMachine = machine } };
                EnsureFloat(controller, (string)move["parameterX"]); EnsureFloat(controller, (string)move["parameterY"]);
                var state = machine.AddState("Move"); state.hideFlags = HideFlags.HideAndDontSave; state.motion = tree;
                machine.defaultState = state;
                return controller;
            }
            catch { Object.DestroyImmediate(tree); DestroyPreviewController(controller); throw; }
        }

        public static void DestroyPreviewController(AnimatorController controller)
        {
            if (controller == null || EditorUtility.IsPersistent(controller)) return;
            foreach (var layer in controller.layers)
            {
                var machine = layer.stateMachine;
                foreach (var child in machine.states)
                {
                    if (child.state.motion is BlendTree tree && !EditorUtility.IsPersistent(tree)) Object.DestroyImmediate(tree);
                    Object.DestroyImmediate(child.state);
                }
                Object.DestroyImmediate(machine);
            }
            Object.DestroyImmediate(controller);
        }

        public static void ConfigureDriver(GameObject root, Animator animator, JObject animation)
        {
            if (!IsActive(animation)) return;
            foreach (var old in root.GetComponents<BaekhoEnemyForgeDirectionParameters>()) Object.DestroyImmediate(old);
            var move = (JObject)animation["move"];
            if ((string)move["parameterX"] == "DirX" && (string)move["parameterY"] == "DirY") return;
            var driver = root.AddComponent<BaekhoEnemyForgeDirectionParameters>();
            driver.Configure(animator, (string)move["parameterX"], (string)move["parameterY"]);
        }

        /// <summary>Pure/transient checks; leaves project assets and open scenes unchanged.</summary>
        public static JObject RunRegression()
        {
            var checks = new JArray();
            Action<bool, string> check = (condition, name) => { checks.Add(new JObject { ["name"] = name, ["passed"] = condition }); if (!condition) throw new InvalidOperationException(name); };
            var catalog = BaekhoEnemyForgeCatalog.Get(); var animation = (JObject)catalog["template"]["animation"];
            check(animation["move"] is JObject, "Drone imports a complete Move tree");
            var move = (JObject)animation["move"].DeepClone();
            var controller = CreatePreviewController(move);
            try
            {
                var tree = controller.layers[0].stateMachine.defaultState.motion as BlendTree;
                check(tree != null && tree.blendType == BlendTreeType.SimpleDirectional2D && tree.children.Length == ((JArray)move["children"]).Count, "Shared construction preserves all Move children");
                check(controller.parameters.Length == 2 && controller.parameters.All(p => p.type == AnimatorControllerParameterType.Float), "Preview has selected Float parameters");
            }
            finally { DestroyPreviewController(controller); }
            var invalid = (JObject)move.DeepClone(); invalid["children"][1]["position"] = invalid["children"][0]["position"].DeepClone();
            var issues = new JArray(); ValidateMove(invalid, issues); check(issues.Any(i => ((string)i["path"]).EndsWith("position")), "Duplicate blend positions rejected");
            invalid = (JObject)move.DeepClone(); invalid["parameterX"] = "DirY";
            issues = new JArray(); ValidateMove(invalid, issues); check(issues.Count > 0, "Reserved axis remapping rejected");
            invalid = (JObject)move.DeepClone(); invalid["children"][0]["clip"] = null;
            issues = new JArray(); ValidateMove(invalid, issues); check(issues.Any(i => ((string)i["path"]).EndsWith("clip")), "Missing child clip rejected");
            invalid = (JObject)move.DeepClone(); invalid["children"][2]["position"] = new JArray(0, 2);
            issues = new JArray(); ValidateMove(invalid, issues); check(issues.Any(i => ((string)i["path"]).EndsWith("position")), "Two clips in the same direction rejected");
            invalid = (JObject)move.DeepClone(); invalid["children"][0]["timeScale"] = 0;
            issues = new JArray(); ValidateMove(invalid, issues); check(issues.Any(i => ((string)i["path"]).EndsWith("timeScale")), "Invalid child playback multiplier rejected");
            var preserved = (JObject)animation.DeepClone(); preserved["mode"] = "controller"; preserved["move"]["overrideController"] = false;
            check(!IsActive(preserved), "Existing controller Move preserved by default"); preserved["move"]["overrideController"] = true;
            check(IsActive(preserved), "Existing controller Move override is explicit");
            return new JObject { ["passed"] = true, ["checks"] = checks };
        }

        public static JObject RunDriverRegression()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Direction regression requires idle Edit Mode.");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            AnimatorController controller = null, replacement = null;
            try
            {
                SceneManager.SetActiveScene(scene);
                var template = (JObject)BaekhoEnemyForgeCatalog.Get()["template"];
                var move = (JObject)template["animation"]["move"].DeepClone();
                move["parameterX"] = "Horizontal"; move["parameterY"] = "Vertical";
                controller = CreatePreviewController(move); EnsureFloat(controller, "DirX"); EnsureFloat(controller, "DirY");
                var model = Object.Instantiate((GameObject)BaekhoEnemyForgeCatalog.Resolve(template["model"], typeof(GameObject)));
                var animator = model.GetComponentInChildren<Animator>(true);
                animator.runtimeAnimatorController = controller; animator.applyRootMotion = false; animator.Rebind(); animator.Update(0);
                var driver = model.AddComponent<BaekhoEnemyForgeDirectionParameters>(); driver.Configure(animator, "Horizontal", "Vertical");
                animator.SetFloat("DirX", 1.25f); animator.SetFloat("DirY", -.75f); driver.Synchronize();
                if (!Mathf.Approximately(animator.GetFloat("Horizontal"), 1.25f) || !Mathf.Approximately(animator.GetFloat("Vertical"), -.75f))
                    throw new InvalidOperationException("Custom movement parameters did not receive raw direction channels.");
                animator.SetFloat("DirX", 0); animator.SetFloat("DirY", 0); driver.Synchronize();
                if (animator.GetFloat("Horizontal") != 0 || animator.GetFloat("Vertical") != 0)
                    throw new InvalidOperationException("Stopped movement did not clear custom channels.");
                replacement = CreatePreviewController(move); EnsureFloat(replacement, "DirX"); EnsureFloat(replacement, "DirY");
                animator.runtimeAnimatorController = replacement; animator.Rebind(); animator.Update(0);
                animator.SetFloat("DirX", -.5f); animator.SetFloat("DirY", 2); driver.Synchronize();
                if (!Mathf.Approximately(animator.GetFloat("Horizontal"), -.5f) || !Mathf.Approximately(animator.GetFloat("Vertical"), 2))
                    throw new InvalidOperationException("Movement parameter cache did not recover after a controller change.");
                return new JObject { ["passed"] = true, ["customParameters"] = true, ["stoppedMovement"] = true, ["controllerChange"] = true };
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                DestroyPreviewController(controller); DestroyPreviewController(replacement);
            }
        }

        public static JObject RunAssetRegression()
        {
            BaekhoEnemyForgeBridge.RequireReady(true);
            var template = (JObject)BaekhoEnemyForgeCatalog.Get()["template"];
            var sourceController = (AnimatorController)BaekhoEnemyForgeCatalog.Resolve(template["animation"]["controller"], typeof(AnimatorController));
            string sourcePath = AssetDatabase.GetAssetPath(sourceController);
            var sourceBytes = File.ReadAllBytes(sourcePath);
            var sourceMove = sourceController.layers[0].stateMachine.states.First(s => s.state.name == "Move").state;
            var folders = new List<string>(); var checks = new JArray();
            Action<bool, string> check = (valid, name) => { checks.Add(new JObject { ["name"] = name, ["passed"] = valid }); if (!valid) throw new InvalidOperationException(name); };
            try
            {
                foreach (string test in new[] { "authored", "legacy", "preserved", "overridden" })
                {
                    var blueprint = (JObject)template.DeepClone();
                    blueprint["name"] = "EnemyForgeFeaturesRegression_" + Guid.NewGuid().ToString("N");
                    var animation = (JObject)blueprint["animation"];
                    var move = (JObject)animation["move"];
                    if (test == "legacy") animation["move"] = null;
                    else
                    {
                        move["parameterX"] = "Horizontal"; move["parameterY"] = "Vertical";
                        move["children"][1]["timeScale"] = 1.25;
                        if (test == "preserved" || test == "overridden") animation["mode"] = "controller";
                        move["overrideController"] = test == "overridden";
                    }
                    string folder = BaekhoEnemyForgeBuilder.OutputFolder(blueprint); folders.Add(folder);
                    var build = BaekhoEnemyForgeBuilder.Build(blueprint);
                    string prefabPath = (string)build["prefabPath"];
                    AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    var animator = prefab.GetComponentInChildren<Animator>(true);
                    string controllerPath = AssetDatabase.GetAssetPath(animator.runtimeAnimatorController);
                    AssetDatabase.ImportAsset(controllerPath, ImportAssetOptions.ForceUpdate);
                    var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                    var outputMove = controller.layers[0].stateMachine.states.First(s => s.state.name == "Move").state;
                    check(controllerPath.StartsWith(folder + "/", StringComparison.Ordinal), test + ": controller belongs to generated output");
                    if (test == "legacy") check(outputMove.motion is AnimationClip, "legacy: Move remains a single clip");
                    else
                    {
                        var tree = outputMove.motion as BlendTree;
                        check(tree != null && tree.children.Length == ((JArray)move["children"]).Count, test + ": complete tree survives asset reload");
                        check(AssetDatabase.GetAssetPath(tree) == controllerPath, test + ": tree is embedded in output controller");
                        if (test == "preserved")
                        {
                            var original = (BlendTree)sourceMove.motion;
                            check(tree.blendParameter == original.blendParameter && tree.blendParameterY == original.blendParameterY
                                && tree.children.Select(c => c.timeScale).SequenceEqual(original.children.Select(c => c.timeScale)), "preserved: original Move parameter and child settings retained");
                            check(outputMove.transitions.Length == sourceMove.transitions.Length, "preserved: Move transitions retained");
                        }
                        else
                        {
                            check(tree.blendParameter == "Horizontal" && tree.blendParameterY == "Vertical" && Mathf.Approximately(tree.children[1].timeScale, 1.25f), test + ": selected parameters and playback multiplier persisted");
                            var driver = prefab.GetComponent<BaekhoEnemyForgeDirectionParameters>();
                            check(driver != null && new SerializedObject(driver).FindProperty("animator").objectReferenceValue == animator, test + ": runtime driver references generated model Animator");
                            if (test == "overridden") check(outputMove.transitions.Length == sourceMove.transitions.Length, "overridden: Move transitions retained");
                        }
                    }
                    check(File.ReadAllBytes(sourcePath).SequenceEqual(sourceBytes), test + ": source controller byte-identical");
                }
                return new JObject { ["passed"] = true, ["cases"] = 4, ["checks"] = checks };
            }
            finally
            {
                foreach (string folder in folders)
                {
                    BaekhoEnemyForgeBridge.CheckPath(Path.GetFullPath(folder));
                    if (!folder.StartsWith(BaekhoEnemyForgeBuilder.OutputRoot + "/EnemyForgeFeaturesRegression_", StringComparison.Ordinal))
                        throw new InvalidOperationException("Unexpected feature regression cleanup target.");
                    if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
                }
            }
        }
    }
}
