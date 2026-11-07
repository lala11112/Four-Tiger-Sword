using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace StudioBaekho.EnemyForge
{
    /// <summary>Builds real disposable boss prefabs, then verifies their production lifecycle.</summary>
    [InitializeOnLoad]
    public static class BaekhoEnemyForgeBossRegression
    {
        private const string Prefix = "EnemyForgeBossRegression_";
        private const string JobKey = "BaekhoEnemyForge.BossRegression";
        private static PlayFixture play;
        private static readonly List<string> runtimeErrors = new List<string>();

        static BaekhoEnemyForgeBossRegression() { EditorApplication.update += Tick; }

        public static JObject Run()
        {
            BaekhoEnemyForgeBridge.RequireReady(true);
            int checks = 0;
            var scenes = SceneSnapshot();
            var derivedUi = DerivedUiLayout();
            var sources = SourceHashes();
            var blueprint = Blueprint();
            var legacy = (JObject)blueprint.DeepClone(); legacy["version"] = 2;
            legacy["stats"]["maxHp"] = 4800;
            var legacyPhases = (JArray)legacy["boss"]["phases"];
            legacyPhases.Add(legacyPhases[1].DeepClone()); legacyPhases[2]["phaseId"] = "phase_3";
            for (int i = 0; i < 3; i++) { ((JObject)legacyPhases[i]).Remove("phaseMaxHp"); legacyPhases[i]["triggerHpPercent"] = new[] { 100, 65, 30 }[i]; }
            var migrated = BaekhoEnemyForgeBoss.Normalize(legacy);
            Check((int)migrated["version"] == 3 && (float)migrated["stats"]["maxHp"] == 4800f
                && ((JArray)migrated["boss"]["phases"]).Select(p => (float)p["phaseMaxHp"]).SequenceEqual(new[] { 1680f, 1680f, 1440f })
                && ((JArray)migrated["boss"]["phases"]).All(p => p["triggerHpPercent"] == null)
                && (int)legacy["version"] == 2, "Legacy migration must preserve total HP, list order and its source document.", ref checks);
            string folder = BaekhoEnemyForgeBuilder.OutputFolder(blueprint);
            var validation = BaekhoEnemyForgeBuilder.Validate(blueprint);
            Check((bool)validation["valid"], "Boss fixture must validate: " + validation, ref checks);
            var catalog = BaekhoEnemyForgeCatalog.Get("boss");
            Check((string)catalog["kind"] == "boss" && (int)catalog["template"]["version"] == 3,
                "Boss catalog must expose a version-3 boss template.", ref checks);
            Check(((JArray)catalog["boss"]["immunities"]).Count == 8, "Boss catalog must expose eight fixed immunities.", ref checks);
            Check(!((JArray)catalog["attacks"]).Any(a => (string)a["category"] == "selfDestruct"),
                "Boss catalog must not expose normal automatic self-destruction.", ref checks);
            Invalid(blueprint, b => b["boss"]["phases"][1]["phaseMaxHp"] = 0, "zero phase HP", ref checks);
            Invalid(blueprint, b => b["boss"]["phases"][0]["phaseMaxHp"] = -1, "negative phase HP", ref checks);
            Invalid(blueprint, b => b["boss"]["phases"][1]["skillIds"] = new JArray("deleted-attack"), "deleted phase attack", ref checks);
            Invalid(blueprint, b => b["boss"]["phases"][0]["pattern"]["steps"][0]["attackId"] = "deleted-attack",
                "deleted sequence attack", ref checks);
            Invalid(blueprint, b => b["boss"]["reward"]["items"][0]["guid"] = new string('0', 32), "deleted reward item", ref checks);
            Invalid(blueprint, b => b["selfDestruct"] = Attack(typeof(MonsterSelfDestructSO), "self", "BossRegressionA"),
                "automatic self-destruction slot", ref checks);
            Invalid(blueprint, b => b["attacks"][0] = Attack(typeof(MonsterSelfDestructSO), "a", "BossRegressionA"),
                "self-destruction as sequence attack", ref checks);
            JObject build;
            try
            {
                build = BaekhoEnemyForgeBuilder.Build(blueprint);
                VerifySaved(blueprint, build, ref checks);
                Check(!(bool)BaekhoEnemyForgeBuilder.Validate(blueprint)["valid"], "Existing boss output must never be overwritten.", ref checks);
            }
            finally { Cleanup(folder); }
            VerifySources(sources, ref checks);
            Check(JToken.DeepEquals(scenes, SceneSnapshot()), "Edit Mode build must preserve open scenes, hierarchy and dirty flags.", ref checks);
            Check(!AssetDatabase.IsValidFolder(folder), "Disposable boss output must be removed.", ref checks);
            return new JObject { ["passed"] = true, ["checks"] = checks, ["build"] = build,
                ["sourceHashes"] = sources, ["scenesPreserved"] = true, ["cleanedFolder"] = folder,
                ["derivedUiLayoutBefore"] = derivedUi, ["derivedUiLayoutAfter"] = DerivedUiLayout() };
        }

        private static JObject Blueprint()
        {
            var catalog = BaekhoEnemyForgeCatalog.Get("boss");
            var b = (JObject)catalog["template"].DeepClone();
            b["name"] = Prefix + Guid.NewGuid().ToString("N");
            b["stats"]["maxHp"] = 1000; b["stats"]["baseAtk"] = 10;
            b["stats"]["baseDef"] = 20; b["stats"]["criticalChance"] = 0;
            b["stats"]["attackCooldown"] = .8; b["stats"]["staggerResistLevel"] = (int)StaggerResistLevel.SUPER_ARMOR;
            b["components"]["NavMeshAgent"]["m_Speed"] = 0;
            var states = (JObject)b["animation"]["states"];
            // The source controller intentionally has unmapped motions. Supply real
            // compatible fixture clips explicitly, without changing that source asset.
            const string modelPath = "Assets/_Project/Models/Warrok W Kurniawan.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath)
                ?? throw new InvalidDataException("Boss regression requires the existing Warrok model.");
            var motion = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal))
                ?? throw new InvalidDataException("Boss regression requires a Warrok animation clip.");
            b["model"] = BaekhoEnemyForgeCatalog.AssetRef(model);
            var clip = BaekhoEnemyForgeCatalog.AssetRef(motion);
            b["animation"]["mode"] = "clips"; b["animation"]["move"] = null;
            foreach (var state in states.Properties().ToArray()) state.Value = clip.DeepClone();
            foreach (string state in new[] { "Idle", "Move", "Groggy", "PhaseDown", "PhaseRevive", "BossRegressionA", "BossRegressionB" })
                states[state] = clip.DeepClone();
            var a = Attack(typeof(MonsterDefaultAttackSO), "a", "BossRegressionA");
            var c = Attack(typeof(MonsterDefaultAttackSO), "b", "BossRegressionB");
            foreach (var attack in new[] { a, c })
            {
                var values = (JObject)attack["values"];
                values["attackDuration"] = .25; values["hitStartTime"] = .05; values["hitDuration"] = .05;
                values["minimumRange"] = 0; values["executeRange"] = 3; values["engageRange"] = 12;
                values["cooldown"] = 0; values["weight"] = 0; values["attackSpeed"] = 1;
                values["hitBoxOffset"] = new JArray(0, 0, 1); values["hitBoxRadius"] = 1.5;
                values["isParryable"] = true;
            }
            a["values"]["damage"] = 1; c["values"]["damage"] = 2;
            b["attacks"] = new JArray(a, c); b["selfDestruct"] = null;
            var config = (JObject)b["boss"];
            config["initialShield"] = 30; config["enableWeakness"] = true;
            config["weaknessElement"] = (int)ElementType.ELEMENT_FIRE; config["weakDamageMult"] = 1.5;
            config["enableResistance"] = true; config["resistElement"] = (int)ElementType.ELEMENT_WATER;
            config["statusImmunityList"] = new JArray("Silence", "DefenseReduction");
            var first = (JObject)config["phases"][0];
            first["phaseMaxHp"] = 500; first["skillIds"] = new JArray("a", "b"); first["pattern"] = Pattern("a", "b");
            var second = (JObject)first.DeepClone(); second["phaseId"] = "phase_2";
            second["phaseMaxHp"] = 500; second["atkMultiplier"] = 2; second["defMultiplier"] = 3;
            second["shield"] = 15; second["skillIds"] = new JArray("b", "a"); second["pattern"] = Pattern("b", "a");
            config["phases"] = new JArray(first, second);
            var items = (JArray)catalog["boss"]["items"];
            if (items.Count == 0) throw new InvalidDataException("Boss regression requires one existing catalog reward item.");
            config["reward"] = new JObject { ["gold"] = 23, ["items"] = new JArray(items[0].DeepClone()) };
            return b;
        }

        private static JObject Pattern(string first, string last) => new JObject { ["mode"] = "sequence", ["loop"] = true,
            ["steps"] = new JArray(Step("attack", first, 0), Step("wait", null, .15), Step("attack", last, 0)) };
        private static JObject Step(string kind, string attackId, double seconds)
            => new JObject { ["kind"] = kind, ["attackId"] = attackId, ["waitSeconds"] = seconds };
        private static JObject Attack(Type type, string id, string state)
        {
            var values = (JObject)BaekhoEnemyForgeCatalog.AttackDefinition(type)["defaults"].DeepClone();
            values["animName"] = state;
            return new JObject { ["id"] = id, ["typeId"] = type.FullName, ["values"] = values };
        }
        private static void Invalid(JObject source, Action<JObject> mutate, string label, ref int checks)
        {
            var b = (JObject)source.DeepClone(); mutate(b);
            Check(!(bool)BaekhoEnemyForgeBuilder.Validate(b)["valid"], "Must reject " + label + ".", ref checks);
        }
        private static void VerifySaved(JObject blueprint, JObject build, ref int checks)
        {
            string folder = BaekhoEnemyForgeBuilder.OutputFolder(blueprint);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>((string)build["prefabPath"]);
            Check(prefab != null && prefab.GetComponents<Enemy>().Length == 1
                && prefab.GetComponent<ConfiguredBoss>() != null, "Saved root must contain exactly one ConfiguredBoss actor.", ref checks);
            Check(prefab.GetComponent<BossShield>() != null, "Saved boss must contain a shield component.", ref checks);
            var boss = prefab.GetComponent<ConfiguredBoss>();
            var stats = (EnemyData)new SerializedObject(prefab.GetComponent<EnemyStat>()).FindProperty("baseData").objectReferenceValue;
            Check(stats is BossData && ReferenceEquals(stats, boss.Data), "Saved boss must reference one shared BossData stats asset.", ref checks);
            Check(Mathf.Approximately(stats.maxHp, 1000f) && boss.PhaseConfiguration.All(p => Mathf.Approximately(p.phaseMaxHp, 500f)),
                "Saved total HP must be derived from the ordered phase pools.", ref checks);
            Check(Mathf.Approximately(boss.Data.phaseDownSeconds + boss.Data.phaseHoldSeconds + boss.Data.phaseReviveSeconds, 3f),
                "Generated transition must preserve the default three-second duration.", ref checks);
            Owned(stats, folder, "boss stats", ref checks);
            Owned(stats.monsterSkillData, folder, "base skill set", ref checks);
            Check(stats.monsterSkillData.skillData.Count == 2 && stats.monsterSkillData.selfDestruct == null,
                "Saved base skill set must contain two attacks and no automatic self-destruct.", ref checks);
            foreach (var skill in stats.monsterSkillData.skillData) Owned(skill, folder, "attack", ref checks);
            Owned(prefab.GetComponentInChildren<Animator>(true).runtimeAnimatorController, folder, "animator", ref checks);
            Check(boss.PhaseConfiguration.Count == 2, "Saved boss must contain both phases.", ref checks);
            for (int i = 0; i < 2; i++)
            {
                var phase = boss.PhaseConfiguration[i]; Owned(phase, folder, "phase", ref checks);
                Owned(phase.skillSet, folder, "phase skill set", ref checks);
                Owned(phase.pattern, folder, "sequence pattern", ref checks);
                var pattern = phase.pattern as BossSequencePatternSO;
                Check(pattern != null && pattern.loop && pattern.steps.Count == 3
                    && pattern.steps[1].kind == BossPatternStepKind.Wait && Mathf.Approximately(pattern.steps[1].waitSeconds, .15f),
                    "Saved sequence must preserve attack/wait/attack ordering.", ref checks);
                Check(pattern.steps[0].skill == stats.monsterSkillData.skillData[i == 0 ? 0 : 1]
                    && pattern.steps[2].skill == stats.monsterSkillData.skillData[i == 0 ? 1 : 0],
                    "Sequence steps must reference the correct owned attacks.", ref checks);
            }
            Check(!boss.PhaseConfiguration[0].overrideShield && boss.PhaseConfiguration[1].overrideShield
                && Mathf.Approximately(boss.PhaseConfiguration[1].shieldAmount, 15f), "Nullable phase shields must preserve keep/replace semantics.", ref checks);
            Owned(boss.Data.rewardGroup, folder, "reward group", ref checks);
            var selected = BaekhoEnemyForgeCatalog.Resolve(blueprint["boss"]["reward"]["items"][0], typeof(ItemDefinition));
            Check(boss.Data.rewardGroup.gold == 23 && boss.Data.rewardGroup.items.Count == 1
                && boss.Data.rewardGroup.items[0] == selected, "Saved reward must preserve gold and the selected catalog item reference.", ref checks);
            foreach (string path in ((JArray)build["assets"]).Values<string>())
                Check(path.StartsWith(folder + "/", StringComparison.Ordinal) && File.Exists(path), "Every reported output must exist in the owned folder.", ref checks);
        }
        private static void Owned(Object asset, string folder, string label, ref int checks)
            => Check(asset != null && AssetDatabase.GetAssetPath(asset).StartsWith(folder + "/", StringComparison.Ordinal), "Expected owned " + label + ".", ref checks);
        private static void Check(bool value, string message, ref int checks)
        { if (!value) throw new InvalidOperationException("Boss prefab regression: " + message); checks++; }

        private static JObject SourceHashes()
        {
            var paths = new HashSet<string>(AssetDatabase.GetDependencies(BaekhoEnemyForgeBoss.TemplatePath, true)
                .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && File.Exists(p)));
            const string fixtureModel = "Assets/_Project/Models/Warrok W Kurniawan.fbx";
            if (File.Exists(fixtureModel)) paths.Add(fixtureModel);
            var rewardItem = BaekhoEnemyForgeBoss.Items().FirstOrDefault();
            string rewardPath = rewardItem != null ? AssetDatabase.GetAssetPath(rewardItem) : null;
            if (!string.IsNullOrEmpty(rewardPath) && File.Exists(rewardPath)) paths.Add(rewardPath);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            { string path = SceneManager.GetSceneAt(i).path; if (!string.IsNullOrEmpty(path) && File.Exists(path)) paths.Add(path); }
            foreach (var path in paths.ToArray()) if (File.Exists(path + ".meta")) paths.Add(path + ".meta");
            return JObject.FromObject(paths.OrderBy(p => p, StringComparer.Ordinal).ToDictionary(p => p, HashFile));
        }
        private static string HashFile(string path)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
        private static void VerifySources(JObject sources, ref int checks)
        { foreach (var entry in sources) Check(File.Exists(entry.Key) && HashFile(entry.Key) == (string)entry.Value, "Source file changed: " + entry.Key, ref checks); }
        private static JArray SceneSnapshot()
        {
            var scenes = new JArray();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                var hierarchy = scene.isLoaded ? scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                    .OrderBy(HierarchyPath, StringComparer.Ordinal)
                    .Select(t => new { path = HierarchyPath(t), name = t.name, parent = t.parent != null ? t.parent.name : "", active = t.gameObject.activeSelf,
                        layer = t.gameObject.layer, position = t.localPosition.ToString("R"), rotation = t.localRotation.ToString("R"),
                        scale = t.GetComponent("BoundaryZeroWindowFit") != null ? "viewport-derived BoundaryZeroWindowFit scale" : t.localScale.ToString("R"),
                        components = t.GetComponents<Component>().Select(c => c != null ? c.GetType().FullName : "missing").ToArray() }).ToArray() : null;
                scenes.Add(new JObject { ["path"] = scene.path, ["dirty"] = scene.isDirty, ["loaded"] = scene.isLoaded,
                    ["active"] = scene == SceneManager.GetActiveScene(), ["hierarchy"] = hierarchy == null ? null : JArray.FromObject(hierarchy) });
            }
            return scenes;
        }
        private static string HierarchyPath(Transform transform)
        {
            var parts = new List<string>();
            for (var current = transform; current != null; current = current.parent)
                parts.Add(current.name + "#" + current.GetSiblingIndex());
            parts.Reverse(); return string.Join("/", parts);
        }
        private static JArray DerivedUiLayout()
        {
            var result = new JArray();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i); if (!scene.isLoaded) continue;
                foreach (var transform in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                    .Where(t => t.GetComponent("BoundaryZeroWindowFit") != null).OrderBy(HierarchyPath, StringComparer.Ordinal))
                    result.Add(new JObject { ["scene"] = scene.path, ["path"] = HierarchyPath(transform), ["scale"] = transform.localScale.ToString("R") });
            }
            return result;
        }
        private static void Cleanup(string folder)
        {
            string prefix = BaekhoEnemyForgeBoss.OutputRoot + "/" + Prefix;
            if (folder == null || !folder.StartsWith(prefix, StringComparison.Ordinal)
                || !Guid.TryParseExact(folder.Substring(prefix.Length), "N", out _))
                throw new InvalidOperationException("Refusing an unexpected boss regression cleanup target.");
            BaekhoEnemyForgeBridge.CheckPath(Path.GetFullPath(folder));
            if (AssetDatabase.IsValidFolder(folder) && !AssetDatabase.DeleteAsset(folder))
                throw new IOException("Could not remove disposable boss regression output: " + folder);
        }

        public static JObject StartPlayMode()
        {
            BaekhoEnemyForgeBridge.RequireReady(true);
            string state = (string)Status()["state"];
            if (state == "entering" || state == "running" || state == "exiting") throw new InvalidOperationException("Boss regression is already active.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && (scene.isDirty || string.IsNullOrEmpty(scene.path)))
                    throw new InvalidOperationException("Save open scenes before entering the generated-boss Play Mode regression.");
            }
            var blueprint = Blueprint(); string folder = BaekhoEnemyForgeBuilder.OutputFolder(blueprint);
            var job = new JObject { ["jobId"] = Guid.NewGuid().ToString("D"), ["state"] = "entering", ["checks"] = 0,
                ["folder"] = folder, ["sources"] = SourceHashes(), ["scenes"] = SceneSnapshot(),
                ["derivedUiLayoutBefore"] = DerivedUiLayout(),
                ["sceneSetup"] = JToken.FromObject(EditorSceneManager.GetSceneManagerSetup()), ["startedUtc"] = DateTime.UtcNow.ToString("O") };
            try
            {
                var build = BaekhoEnemyForgeBuilder.Build(blueprint); job["prefabPath"] = build["prefabPath"].DeepClone(); Save(job);
                BaekhoEnemyForgePreview.CloseAll();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorApplication.EnterPlaymode(); return job;
            }
            catch
            {
                Cleanup(folder);
                EditorSceneManager.RestoreSceneManagerSetup(job["sceneSetup"].ToObject<SceneSetup[]>());
                job["state"] = "failed"; Save(job); throw;
            }
        }
        public static JObject Status()
        { string json = SessionState.GetString(JobKey, ""); return string.IsNullOrEmpty(json) ? new JObject { ["state"] = "idle" } : JObject.Parse(json); }
        public static JObject Cancel()
        {
            var job = Status(); string state = (string)job["state"];
            if (state == "entering" || state == "running") Finish(job, "Generated boss regression cancelled by its caller.");
            return Status();
        }
        private static void Save(JObject job) => SessionState.SetString(JobKey, job.ToString(Formatting.None));
        private static void Tick()
        {
            var job = Status(); string state = (string)job["state"];
            if (state != "entering" && state != "running" && state != "exiting") return;
            try
            {
                if (state == "exiting")
                {
                    if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                    try { EditorSceneManager.RestoreSceneManagerSetup(job["sceneSetup"].ToObject<SceneSetup[]>()); }
                    finally { Cleanup((string)job["folder"]); job["cleaned"] = !AssetDatabase.IsValidFolder((string)job["folder"]); }
                    int checks = (int)job["checks"]; VerifySources((JObject)job["sources"], ref checks);
                    job["sourceHashesPreserved"] = true;
                    job["derivedUiLayoutAfter"] = DerivedUiLayout();
                    job["scenesPreserved"] = JToken.DeepEquals(job["scenes"], SceneSnapshot());
                    Check((bool)job["scenesPreserved"], "Play Mode must restore the original scene setup and hierarchy.", ref checks);
                    job["checks"] = checks; job["state"] = job["error"] == null ? "completed" : "failed";
                    job["scenesPreserved"] = true; job["cleaned"] = true; Save(job); return;
                }
                if ((DateTime.UtcNow - DateTime.Parse((string)job["startedUtc"], null, DateTimeStyles.RoundtripKind)).TotalSeconds > 120)
                    throw new TimeoutException("Generated boss Play Mode regression timed out.");
                if (EditorApplication.isCompiling || !EditorApplication.isPlaying) return;
                Application.runInBackground = true; EditorApplication.QueuePlayerLoopUpdate();
                if (state == "entering")
                {
                    runtimeErrors.Clear(); Application.logMessageReceived += RecordError;
                    play = new PlayFixture((string)job["prefabPath"]); job["state"] = "running"; Save(job); return;
                }
                if (runtimeErrors.Count > 0) throw new InvalidOperationException(string.Join("\n", runtimeErrors));
                if (play == null) throw new InvalidOperationException("Generated boss runtime fixture was lost.");
                if (play.Tick(job)) Finish(job, null); else Save(job);
            }
            catch (Exception exception)
            {
                if (state == "exiting")
                { job["state"] = "failed"; job[job["error"] == null ? "error" : "restoreError"] = exception.GetBaseException().Message; Save(job); }
                else Finish(job, exception.GetBaseException().Message);
            }
        }
        private static void RecordError(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) runtimeErrors.Add(message); }
        private static void Finish(JObject job, string error)
        {
            Application.logMessageReceived -= RecordError;
            try { play?.Dispose(); }
            catch (Exception cleanup) { error = error ?? cleanup.GetBaseException().Message; }
            play = null; job["state"] = "exiting"; if (error != null) job["error"] = error; Save(job);
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
        }

        private sealed class PlayFixture : IDisposable
        {
            private readonly ConfiguredBoss boss, second;
            private readonly EnemyForgeBossTargetProbe target;
            private readonly EnemyForgeBossTimelineProbe timeline;
            private readonly List<Object> objects = new List<Object>();
            private readonly NavMeshDataInstance navigation;
            private int stage, lastFrame = -1, disabledAt;
            private EnemyAction cancelled;
            private bool waitVerified, cooldownVerified;
            private float phaseDepletedAt;
            public PlayFixture(string prefabPath)
            {
                try
                {
                var origin = new Vector3(24000f, 0f, 24000f);
                var nav = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), new List<NavMeshBuildSource> {
                    new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                        transform = Matrix4x4.TRS(origin + Vector3.down * .25f, Quaternion.identity, Vector3.one), size = new Vector3(40f, .5f, 40f), area = 0 }
                }, new Bounds(origin, new Vector3(40f, 10f, 40f)), Vector3.zero, Quaternion.identity);
                if (nav == null) throw new InvalidOperationException("Could not build isolated boss NavMesh.");
                objects.Add(nav); navigation = NavMesh.AddNavMeshData(nav);
                var table = ScriptableObject.CreateInstance<ElementEffectiveManager>(); objects.Add(table);
                foreach (ElementType attack in Enum.GetValues(typeof(ElementType))) foreach (ElementType defense in Enum.GetValues(typeof(ElementType)))
                    table.elementEffectiveList.Add(new ElementEffective { AttackType = attack, DefenseType = defense, multiplier = 1f });
                table.Initialize();
                var settingsObject = Make("Generated boss neutral combat settings"); settingsObject.SetActive(false);
                var settings = settingsObject.AddComponent<CombatSettings>();
                BaekhoEnemyForgeRuntimeRegression.Set(settings, "_elementTable", table); settingsObject.SetActive(true);
                var player = Make("Generated boss two-collider target"); player.layer = LayerMask.NameToLayer("Player"); player.tag = "Player";
                player.transform.position = origin + Vector3.forward; target = player.AddComponent<EnemyForgeBossTargetProbe>();
                player.AddComponent<SphereCollider>().radius = .2f;
                var child = Make("Second body collider"); child.transform.SetParent(player.transform, false); child.layer = player.layer;
                child.AddComponent<SphereCollider>().radius = .2f;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) ?? throw new InvalidDataException("Generated boss prefab is missing.");
                var actor = Object.Instantiate(prefab, origin, Quaternion.identity); objects.Add(actor); boss = actor.GetComponent<ConfiguredBoss>();
                timeline = actor.AddComponent<EnemyForgeBossTimelineProbe>(); timeline.Boss = boss;
                var other = Object.Instantiate(prefab, origin + Vector3.right * 10f, Quaternion.identity); objects.Add(other); second = other.GetComponent<ConfiguredBoss>();
                var camera = Make("Generated boss regression camera").AddComponent<Camera>(); camera.transform.position = origin + new Vector3(0f, 5f, -8f); camera.transform.LookAt(origin);
                Physics.SyncTransforms();
                }
                catch { Dispose(); throw; }
            }
            private GameObject Make(string name) { var value = new GameObject(name); objects.Add(value); return value; }
            public bool Tick(JObject job)
            {
                if (lastFrame == Time.frameCount) return false; lastFrame = Time.frameCount;
                int checks = (int)job["checks"];
                if (stage == 0 && boss.Brain != null) ObserveDelays(job, ref checks);
                if (stage == 0 && target.States.Count >= 3 && boss.CurrentAction?.IsActive == true)
                {
                    Check(boss.Brain is BossBrain && boss.PhaseIndex == 0 && boss.GetComponent<NavMeshAgent>().isOnNavMesh,
                        "Actual generated prefab must initialize BossBrain on the fixture NavMesh.", ref checks);
                    Check(target.States[0] == "BossRegressionA" && target.States[1] == "BossRegressionB" && target.States[2] == "BossRegressionA",
                        "Actual generated sequence must execute A, wait, B, then repeat A.", ref checks);
                    var timing = (JObject)job["timing"];
                    timing["hitTimes"] = new JArray(target.Times.Take(3)); timing["hitOwners"] = new JArray(target.Owners.Take(3));
                    timing["actorTimes"] = new JArray(target.ActorTimes.Take(3)); timing["attackCooldown"] = boss.AttackCooldown;
                    Check(waitVerified, "Actual generated sequence must honor its .15-second action-clock wait. Timing: " + timing.ToString(Formatting.None), ref checks);
                    Check(cooldownVerified, "Actual generated sequence must honor its .8-second world-clock cooldown. Timing: " + timing.ToString(Formatting.None), ref checks);
                    Check(Mathf.Abs(target.Damages[0] - 10f) < .001f && Mathf.Abs(target.Damages[1] - 20f) < .001f,
                        "Generated attack assets must apply their configured ATK multipliers once per two-collider target.", ref checks);
                    Check(boss.CurrentAction?.IsActive == true, "Phase fixture must hit during an active generated action.", ref checks);
                    var depleted = boss.TakeDamage(10000f, poiseDamage: 0f); phaseDepletedAt = Time.time;
                    Check(!depleted.Killed && boss.IsTransitioning && boss.PhaseIndex == 0 && boss.EnemyStat.CurrentHp == 0f, "Generated boss must survive nonfinal HP depletion and begin its transition.", ref checks);
                    Check(boss.TakeDamage(10000f, poiseDamage: 0f).HealthDamage == 0f && !boss.IsDead, "Generated boss must be invulnerable during revival.", ref checks);
                    stage = 1;
                }
                else if (stage == 1 && boss.PhaseIndex == 1 && target.Phases.Contains(1) && boss.CurrentAction?.IsActive == true)
                {
                    Check(Time.time - phaseDepletedAt >= 2.9f && !boss.IsTransitioning && Mathf.Approximately(boss.EnemyStat.CurrentHp, 500f), "Generated boss must complete the three-second revival and refill only the next phase HP.", ref checks);
                    int phaseHitIndex = target.Phases.FindIndex(p => p == 1);
                    Check(target.States[phaseHitIndex] == "BossRegressionB", "Second generated phase must start its own reversed pattern.", ref checks);
                    Check(Mathf.Abs(target.Damages[phaseHitIndex] - 40f) < .001f,
                        "Second generated phase must apply its ATK multiplier to actual attack damage.", ref checks);
                    Check(Mathf.Approximately(boss.EnemyStat.GetStat(EnemyStatType.ATK), 20f)
                        && Mathf.Approximately(boss.EnemyStat.GetStat(EnemyStatType.DEF), 60f), "Generated phase must apply ATK and DEF scaling.", ref checks);
                    Check(Mathf.Approximately(boss.GetComponent<BossShield>().CurrentAbsorption, 15f), "Generated phase must replace its shield pool.", ref checks);
                    Check(second.PhaseIndex == 0 && Mathf.Approximately(second.EnemyStat.CurrentHp, 500f)
                        && Mathf.Approximately(second.GetComponent<BossShield>().CurrentAbsorption, 30f), "Two instances sharing generated assets must retain independent runtime state.", ref checks);
                    var effects = boss.GetComponent<StatusEffectHandler>() ?? boss.gameObject.AddComponent<StatusEffectHandler>();
                    Check(!effects.TryApply(new SilenceEffect(5f)), "Generated boss must apply its saved silence immunity.", ref checks);
                    var forms = boss.gameObject.AddComponent<FormTargetEffects>(); forms.ApplyArmorShred(true);
                    Check(forms.ArmorReductionRatio == 0f, "Generated boss must block actual Iron-form defense reduction.", ref checks);
                    boss.GetComponent<BossShield>().Absorb(4f); cancelled = boss.CurrentAction;
                    boss.enabled = false; disabledAt = Time.frameCount; stage = 2;
                }
                else if (stage == 2 && Time.frameCount >= disabledAt + 2)
                {
                    Check(boss.CurrentAction == null && cancelled != null && !cancelled.IsActive && !ParryEventBus.IsTelegraphActive(cancelled),
                        "Disabling generated boss must cancel its live action and telegraph.", ref checks);
                    boss.enabled = true; stage = 3;
                }
                else if (stage == 3 && boss.CurrentAction?.IsActive == true)
                {
                    Check(boss.PhaseIndex == 1 && Mathf.Approximately(boss.GetComponent<BossShield>().CurrentAbsorption, 11f),
                        "Reactivating generated boss must retain its phase and remaining shield.", ref checks);
                    var active = boss.CurrentAction;
                    Check(boss.TakeDamage(10000f, poiseDamage: 0f).Killed && !active.IsActive && !ParryEventBus.IsTelegraphActive(active),
                        "Generated boss death must cancel its live action and confirm the kill.", ref checks);
                    stage = 4;
                }
                else if (stage == 4 && boss.stateName == nameof(EnemyDieState))
                {
                    Check(boss.GetComponentsInChildren<Collider>(true).All(c => !c.enabled)
                        && Mathf.Approximately(boss.GetComponent<BossShield>().CurrentAbsorption, 0f), "Generated boss death must clean body colliders and shield.", ref checks);
                    job["checks"] = checks; return true;
                }
                job["checks"] = checks; return false;
            }
            private void ObserveDelays(JObject job, ref int checks)
            {
                var timing = job["timing"] as JObject;
                if (timing == null) { timing = new JObject(); job["timing"] = timing; }
                timing["waitBudget"] = timeline.WaitBudget; timing["cooldownBudget"] = timeline.CooldownBudget;
                if (!waitVerified && timeline.WaitCompleted)
                {
                    timing["waitElapsed"] = timeline.WaitElapsed;
                    Check(timeline.WaitBudget >= .1495f && timeline.WaitElapsed + .0005f >= timeline.WaitBudget,
                        "The saved .15-second wait must elapse on the actor clock before entering B.", ref checks);
                    waitVerified = true;
                }
                if (!cooldownVerified && timeline.CooldownCompleted)
                {
                    timing["cooldownElapsed"] = timeline.CooldownElapsed;
                    Check(timeline.CooldownBudget >= .7995f && timeline.CooldownElapsed + .0005f >= timeline.CooldownBudget,
                        "The saved .8-second cooldown must elapse on the world clock before repeating A.", ref checks);
                    cooldownVerified = true;
                }
            }
            public void Dispose()
            {
                foreach (var actor in new[] { boss, second }) if (actor != null)
                { actor.enabled = false; var agent = actor.GetComponent<NavMeshAgent>(); if (agent != null) agent.enabled = false; }
                if (navigation.valid) navigation.Remove();
                for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.Destroy(objects[i]);
            }
        }
    }

    /// <summary>Observe production counters after each real gameplay Update, including hitch frames.</summary>
    public sealed class EnemyForgeBossTimelineProbe : MonoBehaviour
    {
        public ConfiguredBoss Boss;
        public float WaitBudget, CooldownBudget, WaitElapsed, CooldownElapsed;
        public bool WaitCompleted, CooldownCompleted;
        private float? waitStartedAt, cooldownStartedAt;
        private void LateUpdate()
        {
            if (Boss == null || Boss.Brain == null || Boss.ActorClock == null) return;
            var behavior = BaekhoEnemyForgeRuntimeRegression.Get(Boss.Brain, "_behavior");
            float wait = (float)BaekhoEnemyForgeRuntimeRegression.Get(behavior, "_waitRemaining");
            float cooldown = (float)BaekhoEnemyForgeRuntimeRegression.Get(Boss, "_attackCooldownTimer");
            if (!waitStartedAt.HasValue && wait > 0f) { waitStartedAt = Boss.ActorClock.Time; WaitBudget = wait; }
            if (!WaitCompleted && waitStartedAt.HasValue && wait <= 0f && Boss.CurrentAction?.IsActive == true
                && Boss.CurrentAction.SkillData.animName == "BossRegressionB")
            { WaitElapsed = Boss.ActorClock.Time - waitStartedAt.Value; WaitCompleted = true; }
            if (!cooldownStartedAt.HasValue && cooldown > 0f) { cooldownStartedAt = Time.time; CooldownBudget = cooldown; }
            if (!CooldownCompleted && cooldownStartedAt.HasValue && cooldown <= 0f && Boss.CurrentAction?.IsActive == true
                && Boss.CurrentAction.SkillData.animName == "BossRegressionA")
            { CooldownElapsed = Time.time - cooldownStartedAt.Value; CooldownCompleted = true; }
        }
    }

    public sealed class EnemyForgeBossTargetProbe : MonoBehaviour, IDamageable
    {
        public readonly List<string> States = new List<string>();
        public readonly List<float> Times = new List<float>();
        public readonly List<float> Damages = new List<float>();
        public readonly List<int> Phases = new List<int>();
        public readonly List<int> Owners = new List<int>();
        public readonly List<float> ActorTimes = new List<float>();
        public DamageResult TakeDamage(float damage, ElementType damageType = ElementType.ELEMENT_NONE, bool isCritical = false,
            Vector3 power = default, float poiseDamage = 0f, StaggerResistLevel staggerResistLevel = StaggerResistLevel.NONE,
            bool isParryable = true, object source = null)
        {
            var action = source as EnemyAction;
            States.Add(action?.SkillData?.animName ?? "unknown"); Times.Add(Time.time); Damages.Add(damage);
            Phases.Add(action?.Owner is Boss boss ? boss.PhaseIndex : -1);
            Owners.Add(action?.Owner != null ? action.Owner.GetInstanceID() : 0);
            ActorTimes.Add(action?.Owner?.ActorClock != null ? action.Owner.ActorClock.Time : 0f);
            return new DamageResult(DamageOutcome.Applied, damage);
        }
    }
}
