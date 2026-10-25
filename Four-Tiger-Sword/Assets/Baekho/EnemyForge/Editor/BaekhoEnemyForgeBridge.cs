using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace StudioBaekho.EnemyForge
{
    [InitializeOnLoad]
    [McpForUnityTool("baekho_enemy_forge", Description = "Analyze enemy prefabs, validate and create independent enemies, render isolated previews and validate generated attack types.")]
    public static class BaekhoEnemyForgeBridge
    {
        private const string CompilerErrorsKey = "BaekhoEnemyForge.CompilerErrors";
        private static readonly string Session = Guid.NewGuid().ToString("D");
        private static string activeRequest;
        private static StringComparison PathComparison => Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        public sealed class Parameters
        {
            [ToolParameter("catalog, assets_search, validate, build, status, preview_open, preview_frame, preview_close, compiler_info, refresh, compilation_status or validate_attack")]
            public string action { get; set; }
            [ToolParameter("Absolute expected Unity project root. Access is rejected if it does not match the connected project.")]
            public string expectedProjectRoot { get; set; }
            [ToolParameter("Action-specific JSON object.", Required = false)]
            public JObject payload { get; set; }
        }

        static BaekhoEnemyForgeBridge()
        {
            CompilationPipeline.compilationStarted += _ => SessionState.SetString(CompilerErrorsKey, "[]");
            CompilationPipeline.assemblyCompilationFinished += (path, messages) =>
            {
                var errors = JArray.Parse(SessionState.GetString(CompilerErrorsKey, "[]"));
                foreach (var message in messages.Where(m => m.type == CompilerMessageType.Error))
                    errors.Add(message.file + "(" + message.line + "," + message.column + "): " + message.message);
                SessionState.SetString(CompilerErrorsKey, errors.ToString(Formatting.None));
            };
        }

        public static object HandleCommand(JObject parameters)
        {
            try
            {
                string project = RequireProject((string)parameters?["expectedProjectRoot"]);
                string action = (string)parameters?["action"];
                var payload = parameters?["payload"] as JObject ?? new JObject();
                object result;
                switch (action)
                {
                    case "catalog": RequireReady(false); result = BaekhoEnemyForgeCatalog.Get(); break;
                    case "assets_search": RequireReady(false); result = BaekhoEnemyForgeCatalog.SearchAssets(payload); break;
                    case "validate": RequireReady(false); result = BaekhoEnemyForgeBuilder.Validate(payload["blueprint"] as JObject ?? payload); break;
                    case "build": result = Build(project, payload); break;
                    case "status": result = Status(project, payload); break;
                    case "preview_open": case "preview_frame": case "preview_close": result = BaekhoEnemyForgePreview.Handle(action, payload); break;
                    case "compiler_info": result = CompilerInfo(); break;
                    case "compilation_status": case "ready": result = CompilationStatus(); break;
                    case "refresh":
                        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Unity Play Mode before importing generated attack code.");
                        SessionState.SetString(CompilerErrorsKey, "[]");
                        EditorApplication.delayCall += () => { AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate); CompilationPipeline.RequestScriptCompilation(); };
                        result = new { requested = true }; break;
                    case "validate_attack": RequireReady(true); result = ValidateGeneratedAttack(payload); break;
                    case "regression": RequireReady(true); result = payload.Value<bool?>("build") == true ? RunBuildRegression() : (object)RunRegression(); break;
                    default: throw new InvalidDataException("Unsupported Enemy Forge action: " + action);
                }
                return new SuccessResponse("Enemy Forge " + action + " completed.", result);
            }
            catch (Exception exception) { return new ErrorResponse(exception.GetBaseException().Message); }
        }

        public static string RequireProject(string expected)
        {
            if (string.IsNullOrWhiteSpace(expected) || !Path.IsPathRooted(expected)) throw new InvalidDataException("expectedProjectRoot must be an absolute path.");
            string actual = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string wanted = Path.GetFullPath(expected).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(actual, wanted, PathComparison)) throw new InvalidOperationException("Unity project mismatch: " + actual);
            CheckPath(actual); return actual;
        }

        public static void CheckPath(string path)
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string full = Path.GetFullPath(path);
            if (!string.Equals(full, project, PathComparison) && !full.StartsWith(project + Path.DirectorySeparatorChar, PathComparison)) throw new InvalidDataException("Path escapes the Unity project.");
            for (string current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Junction or symbolic-link paths are not supported: " + current);
                if (Path.GetDirectoryName(current) == current) break;
            }
        }

        public static void RequireReady(bool editMode)
        {
            if (editMode && EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Unity Play Mode를 종료하세요.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Unity의 컴파일 및 에셋 가져오기가 끝날 때까지 기다려 주세요.");
            if (EditorUtility.scriptCompilationFailed) throw new InvalidOperationException("Unity 컴파일 오류를 해결한 뒤 다시 시도하세요.");
        }

        private static JObject CompilationStatus()
        {
            var errors = JArray.Parse(SessionState.GetString(CompilerErrorsKey, "[]"));
            return new JObject { ["ready"] = !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorUtility.scriptCompilationFailed,
                ["isCompiling"] = EditorApplication.isCompiling, ["isUpdating"] = EditorApplication.isUpdating,
                ["hasErrors"] = EditorUtility.scriptCompilationFailed || errors.Count > 0, ["errors"] = errors };
        }

        private static JObject CompilerInfo()
        {
            var assembly = CompilationPipeline.GetAssemblies().FirstOrDefault(a => a.name == "Assembly-CSharp")
                ?? throw new InvalidOperationException("Assembly-CSharp compiler configuration is unavailable.");
            var references = new HashSet<string>(assembly.allReferences.Select(Path.GetFullPath).Where(File.Exists), StringComparer.OrdinalIgnoreCase);
            string game = Path.GetFullPath(assembly.outputPath); if (File.Exists(game)) references.Add(game);
            references.Add(typeof(Object).Assembly.Location);
            string version = "9.0";
            if (assembly.compilerOptions != null)
            {
                var property = assembly.compilerOptions.GetType().GetProperty("LanguageVersion", BindingFlags.Instance | BindingFlags.Public);
                string found = property?.GetValue(assembly.compilerOptions)?.ToString();
                if (!string.IsNullOrEmpty(found) && Regex.IsMatch(found, @"\A\d+(\.\d+)?\z")) version = found;
            }
            return new JObject { ["editorPath"] = EditorApplication.applicationPath, ["references"] = new JArray(references.OrderBy(r => r)),
                ["defines"] = new JArray(assembly.defines), ["languageVersion"] = version, ["hasErrors"] = EditorUtility.scriptCompilationFailed };
        }

        private static string RequestId(JObject payload)
        {
            if (!Guid.TryParseExact((string)payload["requestId"], "D", out Guid id)) throw new InvalidDataException("requestId must be a UUID.");
            return id.ToString("D");
        }

        private static string ReceiptPath(string project, string request)
        {
            string path = Path.Combine(project, "Library", "BaekhoEnemyForge", "requests", request + ".json"); CheckPath(path); return path;
        }

        private static JObject ReadReceipt(string path)
        {
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Oversized request receipt.");
            var receipt = JObject.Parse(File.ReadAllText(path));
            if ((int?)receipt["version"] != 1 || !new[] { "running", "completed", "failed" }.Contains((string)receipt["state"])) throw new InvalidDataException("Invalid request receipt.");
            return receipt;
        }

        private static void WriteReceipt(string path, JObject receipt)
        {
            CheckPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path));
            receipt["updatedAt"] = DateTime.UtcNow.ToString("O");
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, receipt.ToString(Formatting.None), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static JObject Status(string project, JObject payload)
        {
            string request = RequestId(payload); string path = ReceiptPath(project, request);
            var receipt = ReadReceipt(path) ?? throw new InvalidOperationException("No receipt exists for this request. Do not automatically repeat a lost build request.");
            VerifyReceipt(receipt, project, request, payload);
            if ((string)receipt["state"] == "running" && ((string)receipt["session"] != Session || activeRequest != request))
            {
                receipt["state"] = "failed"; receipt["error"] = "Unity reloaded during generation. Inspect the output folder before starting a new request. This request will not run again.";
                WriteReceipt(path, receipt);
            }
            return receipt;
        }

        private static void VerifyReceipt(JObject receipt, string project, string request, JObject payload)
        {
            if ((string)receipt["requestId"] != request || !string.Equals((string)receipt["projectRoot"], project, PathComparison)) throw new InvalidDataException("Request receipt identity mismatch.");
            if (payload["inputHash"] != null && !string.Equals((string)receipt["inputHash"], (string)payload["inputHash"], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("This request ID belongs to a different blueprint.");
        }

        private static JObject Build(string project, JObject payload)
        {
            string request = RequestId(payload); string path = ReceiptPath(project, request);
            string hash = (string)payload["inputHash"];
            if (hash == null || !Regex.IsMatch(hash, @"\A[a-fA-F0-9]{64}\z")) throw new InvalidDataException("inputHash must be a SHA-256 digest.");
            var blueprint = payload["blueprint"] as JObject ?? throw new InvalidDataException("blueprint is required.");
            // Hash the exact app JSON serialization supplied alongside the parsed blueprint.
            // JavaScript and Json.NET differ on a few numeric formats; canonical comparison uses the optional exact JSON text.
            string inputJson = (string)payload["inputJson"] ?? blueprint.ToString(Formatting.None);
            if (!JToken.DeepEquals(JObject.Parse(inputJson), blueprint)) throw new InvalidDataException("inputJson does not match blueprint.");
            using (var sha = SHA256.Create())
            {
                string digest = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(inputJson))).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(digest, hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Blueprint SHA-256 mismatch. Send inputJson as the exact JSON.stringify(blueprint) text.");
            }
            var previous = ReadReceipt(path);
            if (previous != null) { VerifyReceipt(previous, project, request, payload); return Status(project, payload); }
            RequireReady(true);
            if (activeRequest != null) throw new InvalidOperationException("Another enemy prefab is being generated.");
            var receipt = new JObject { ["version"] = 1, ["requestId"] = request, ["projectRoot"] = project, ["inputHash"] = hash, ["session"] = Session, ["state"] = "running", ["outputFolder"] = BaekhoEnemyForgeBuilder.OutputFolder(blueprint) };
            WriteReceipt(path, receipt); activeRequest = request;
            try
            {
                var result = BaekhoEnemyForgeBuilder.Build(blueprint);
                foreach (var entry in result) receipt[entry.Key] = entry.Value.DeepClone();
                receipt["state"] = "completed"; WriteReceipt(path, receipt);
            }
            catch (Exception exception)
            {
                receipt["state"] = "failed"; receipt["error"] = exception.GetBaseException().Message;
                WriteReceipt(path, receipt);
            }
            finally { activeRequest = null; }
            return receipt;
        }

        public static JObject ValidateGeneratedAttack(JObject payload)
        {
            RequireReady(true);
            string typeId = (string)payload["typeId"], attackId = (string)payload["attackId"];
            if (string.IsNullOrEmpty(attackId) || !Regex.IsMatch(attackId, @"\A[a-zA-Z0-9_-]{1,80}\z")) throw new InvalidDataException("Invalid generated attack ID.");
            Type type = BaekhoEnemyForgeCatalog.AttackType(typeId, false) ?? throw new InvalidDataException("The generated MonsterSkillData type did not compile: " + typeId);
            if (typeof(MonsterSelfDestructSO).IsAssignableFrom(type)) throw new InvalidDataException("AI generation currently supports normal attacks only.");
            string folder = BaekhoEnemyForgeCatalog.GeneratedAttacks + "/" + attackId;
            CheckPath(Path.GetFullPath(folder));
            string metadataPath = folder + "/Attack.enemyattack.json";
            if (!File.Exists(metadataPath) || new FileInfo(metadataPath).Length > 256 * 1024) throw new InvalidDataException("Generated attack metadata is missing or oversized.");
            var descriptor = JObject.Parse(File.ReadAllText(metadataPath));
            if ((int?)descriptor["version"] != 1 || (string)descriptor["attackId"] != attackId || (string)descriptor["typeId"] != typeId) throw new InvalidDataException("Generated attack descriptor identity mismatch.");
            if (payload["descriptor"] != null && !JToken.DeepEquals(descriptor, payload["descriptor"])) throw new InvalidDataException("Descriptor changed since generation.");
            MonsterSkillData skill = null; GameObject fixture = null; Scene fixtureScene = default;
            try
            {
                skill = (MonsterSkillData)ScriptableObject.CreateInstance(type);
                string sourcePath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(skill));
                if (!sourcePath.StartsWith(folder + "/", StringComparison.Ordinal)) throw new InvalidDataException("Attack type is outside this generated request folder.");
                BaekhoEnemyForgeCatalog.ApplyValues(skill, descriptor["defaults"] as JObject ?? new JObject());
                if (string.IsNullOrWhiteSpace(skill.animName) || skill.attackSpeed <= 0 || skill.minimumRange < 0 || skill.minimumRange > skill.executeRange || skill.executeRange > skill.engageRange) throw new InvalidDataException("Generated attack defaults violate animation/range/speed constraints.");
                fixtureScene = EditorSceneManager.NewPreviewScene();
                fixture = new GameObject("EnemyForge_TypeProbe") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(fixture, fixtureScene);
                fixture.SetActive(false);
                fixture.AddComponent<UnityEngine.AI.NavMeshAgent>(); fixture.AddComponent<FanVisionSensor>(); fixture.AddComponent<SphereCollider>();
                var enemy = fixture.AddComponent<Enemy>(); enemy.EnemyStat = fixture.GetComponent<EnemyStat>();
                var action = skill.CreateAction(enemy) ?? throw new InvalidDataException("CreateAction returned null.");
                if (action.Owner != enemy || action.SkillData != skill) throw new InvalidDataException("CreateAction must bind the requested enemy and skill data.");
                foreach (string method in new[] { "Enter", "Update", "Exit" })
                    if (action.GetType().GetMethod(method, Type.EmptyTypes)?.DeclaringType == typeof(EnemyAction)) throw new InvalidDataException("Generated action must implement " + method + " lifecycle behavior.");
                if (action.IsActive) { action.Exit(); throw new InvalidDataException("CreateAction must not start an attack or mutate the scene."); }
                var description = BaekhoEnemyForgeCatalog.Describe(skill);
                string markerPath = Path.GetFullPath("Library/BaekhoEnemyForge/attacks/" + attackId + ".json");
                CheckPath(markerPath); Directory.CreateDirectory(Path.GetDirectoryName(markerPath));
                File.WriteAllText(markerPath, new JObject { ["typeId"] = typeId, ["fingerprint"] = BaekhoEnemyForgeCatalog.AttackFingerprint(attackId) }.ToString(Formatting.None), new UTF8Encoding(false));
                return new JObject { ["valid"] = true, ["typeId"] = typeId, ["attackId"] = attackId, ["asset"] = null,
                    ["fields"] = description["fields"], ["defaults"] = description["defaults"],
                    ["warnings"] = new JArray("컴파일·타입·설정·액션 생성 계약을 확인했습니다. 실제 전투의 Enter/Update/Exit 동작은 Play Mode에서 검증하세요.") };
            }
            finally
            {
                if (skill != null) Object.DestroyImmediate(skill);
                try { if (fixture != null) Object.DestroyImmediate(fixture); }
                finally { if (fixtureScene.IsValid()) EditorSceneManager.ClosePreviewScene(fixtureScene); }
            }
        }

        public static string RunRegression()
        {
            int checks = 0;
            Action<bool, string> check = (condition, message) => { if (!condition) throw new InvalidOperationException("Enemy Forge regression: " + message); checks++; };
            var catalog = BaekhoEnemyForgeCatalog.Get(); var blueprint = (JObject)catalog["template"].DeepClone();
            check(((JArray)catalog["attacks"]).Count >= 5, "five built-in attacks discovered");
            check((float)blueprint["stats"]["criticalChance"] >= 0 && (float)blueprint["stats"]["criticalChance"] <= 1, "normalized critical chance");
            check((bool)BaekhoEnemyForgeBuilder.Validate(blueprint, false)["valid"], "Drone template validates");
            blueprint["stats"]["maxHp"] = 0; check(!(bool)BaekhoEnemyForgeBuilder.Validate(blueprint, false)["valid"], "zero HP rejected");
            blueprint = (JObject)catalog["template"].DeepClone(); blueprint["animation"]["states"]["Move"] = null;
            check(!(bool)BaekhoEnemyForgeBuilder.Validate(blueprint, false)["valid"], "missing Move rejected");
            blueprint = (JObject)catalog["template"].DeepClone(); blueprint["name"] = "../escape";
            check(!(bool)BaekhoEnemyForgeBuilder.Validate(blueprint, false)["valid"], "path traversal rejected");
            check(BaekhoEnemyForgeCatalog.Resolve(catalog["template"]["model"], typeof(GameObject)) != null, "model localId round trip");
            return "PASS: " + checks + " enemy forge catalog/validation checks; " + BaekhoEnemyForgePreview.RunGeometryRegression();
        }

        /// <summary>Disposable integration fixtures; source prefab/SOs/controllers must remain byte-identical.</summary>
        public static JObject RunBuildRegression()
        {
            RequireReady(true);
            int checks = 0;
            Action<bool, string> check = (condition, message) => { if (!condition) throw new InvalidOperationException("Enemy Forge build regression: " + message); checks++; };
            var catalog = BaekhoEnemyForgeCatalog.Get();
            var template = (JObject)catalog["template"];
            var sourcePrefab = BaekhoEnemyForgeBuilder.ResolveTemplate(template);
            var sourceStat = new SerializedObject(sourcePrefab.GetComponent<EnemyStat>()).FindProperty("baseData").objectReferenceValue as EnemyData;
            var sources = new HashSet<string> { BaekhoEnemyForgeCatalog.TemplatePath, AssetDatabase.GetAssetPath(sourceStat), AssetDatabase.GetAssetPath(sourceStat.monsterSkillData) };
            foreach (var skill in sourceStat.monsterSkillData.skillData.Where(s => s != null)) sources.Add(AssetDatabase.GetAssetPath(skill));
            if (sourceStat.monsterSkillData.selfDestruct != null) sources.Add(AssetDatabase.GetAssetPath(sourceStat.monsterSkillData.selfDestruct));
            var sourceAnimator = sourcePrefab.GetComponentInChildren<Animator>(true);
            sources.Add(AssetDatabase.GetAssetPath(sourceAnimator.runtimeAnimatorController));
            var snapshots = sources.Where(File.Exists).ToDictionary(p => p, File.ReadAllBytes);
            var folders = new List<string>(); var reports = new JArray();
            try
            {
                var blueprint = (JObject)template.DeepClone();
                blueprint["name"] = "EnemyForgeRegression_" + Guid.NewGuid().ToString("N");
                var attacks = new JArray(); int number = 0;
                foreach (Type type in new[] { typeof(MonsterDefaultAttackSO), typeof(MonsterDashAttackSO), typeof(MonsterComboAttackSO), typeof(MonsterJumpSlamSO) })
                {
                    var definition = BaekhoEnemyForgeCatalog.AttackDefinition(type);
                    string state = "RegressionAttack" + number++;
                    var values = (JObject)definition["defaults"].DeepClone(); values["animName"] = state;
                    attacks.Add(new JObject { ["id"] = Guid.NewGuid().ToString("D"), ["typeId"] = type.FullName, ["values"] = values });
                    blueprint["animation"]["states"][state] = template["animation"]["states"]["Attack"].DeepClone();
                }
                blueprint["attacks"] = attacks;
                string folder = BaekhoEnemyForgeBuilder.OutputFolder(blueprint); folders.Add(folder);
                var result = BaekhoEnemyForgeBuilder.Build(blueprint); reports.Add(result);
                var output = AssetDatabase.LoadAssetAtPath<GameObject>((string)result["prefabPath"]);
                check(output != null && output.transform.position == Vector3.zero, "saved independent prefab with origin root");
                var enemyStat = output.GetComponent<EnemyStat>();
                var data = (EnemyData)new SerializedObject(enemyStat).FindProperty("baseData").objectReferenceValue;
                check(data != sourceStat && AssetDatabase.GetAssetPath(data).StartsWith(folder + "/", StringComparison.Ordinal), "owned EnemyData");
                check(data.monsterSkillData != sourceStat.monsterSkillData && data.monsterSkillData.skillData.Count == 4, "independent skill list with four regular attacks");
                foreach (var skill in data.monsterSkillData.skillData) check(AssetDatabase.GetAssetPath(skill).StartsWith(folder + "/", StringComparison.Ordinal), "owned attack SO " + skill.GetType().Name);
                check(data.monsterSkillData.selfDestruct != sourceStat.monsterSkillData.selfDestruct, "owned self-destruct SO");
                check(output.GetComponent<Enemy>().StaggerResistLevel == data.staggerResistLevel, "effective stagger resistance synchronized");
                var outputController = output.GetComponentInChildren<Animator>(true).runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
                check(outputController != null && AssetDatabase.GetAssetPath(outputController).StartsWith(folder + "/", StringComparison.Ordinal), "owned AnimatorController");
                foreach (string state in new[] { "Idle", "Move", "Groggy", "RegressionAttack0", "RegressionAttack1", "RegressionAttack2", "RegressionAttack3", "SelfDestruct" })
                    check(outputController.layers[0].stateMachine.states.Any(s => s.state.name == state && s.state.motion != null), "mapped required state " + state);
                check(output.GetComponent<HitVfxAnchor>().SpawnPoint.IsChildOf(output.transform), "VFX point rebound inside output");

                // Exercise a real model replacement using a clip imported with that model, so Generic rig paths are compatible.
                bool alternateBuilt = false;
                foreach (var reference in ((JArray)catalog["assets"]).OfType<JObject>().Where(a => (string)a["kind"] == "Model" && (string)a["guid"] != (string)template["model"]["guid"]))
                {
                    var model = BaekhoEnemyForgeCatalog.Resolve(reference, typeof(GameObject)) as GameObject;
                    if (model == null || model.GetComponentsInChildren<Animator>(true).Length != 1) continue;
                    var clip = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(model)).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
                    if (clip == null) continue;
                    var alternate = (JObject)blueprint.DeepClone(); alternate["name"] = "EnemyForgeModelRegression_" + Guid.NewGuid().ToString("N"); alternate["model"] = reference.DeepClone();
                    foreach (var state in ((JObject)alternate["animation"]["states"]).Properties().ToArray()) state.Value = BaekhoEnemyForgeCatalog.AssetRef(clip);
                    if (!(bool)BaekhoEnemyForgeBuilder.Validate(alternate)["valid"]) continue;
                    string alternateFolder = BaekhoEnemyForgeBuilder.OutputFolder(alternate); folders.Add(alternateFolder);
                    var alternateResult = BaekhoEnemyForgeBuilder.Build(alternate); reports.Add(alternateResult);
                    var alternatePrefab = AssetDatabase.LoadAssetAtPath<GameObject>((string)alternateResult["prefabPath"]);
                    check(alternatePrefab.GetComponentsInChildren<Animator>(true).Length == 1, "model replacement keeps one Animator");
                    var fade = alternatePrefab.GetComponent<EnemyDeathFade>();
                    check(fade == null || new SerializedObject(fade).FindProperty("_targets").arraySize == 0, "old renderer fade references cleared");
                    alternateBuilt = true; break;
                }
                foreach (var snapshot in snapshots) check(File.ReadAllBytes(snapshot.Key).SequenceEqual(snapshot.Value), "source bytes unchanged: " + snapshot.Key);
                return new JObject { ["passed"] = true, ["checks"] = checks, ["alternateModelBuilt"] = alternateBuilt,
                    ["message"] = "Disposable prefab builds verified; all generated test output folders are removed.", ["builds"] = reports };
            }
            finally
            {
                foreach (string folder in folders)
                {
                    CheckPath(Path.GetFullPath(folder));
                    if (!folder.StartsWith(BaekhoEnemyForgeBuilder.OutputRoot + "/EnemyForge", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected regression cleanup target.");
                    if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
                }
            }
        }
    }
}
