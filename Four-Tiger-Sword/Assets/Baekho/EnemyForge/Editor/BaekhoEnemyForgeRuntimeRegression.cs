using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace StudioBaekho.EnemyForge
{
    /// <summary>Actual action lifecycle and damage-query fixtures in a disposable additive scene; never enters Play Mode.</summary>
    public static class BaekhoEnemyForgeRuntimeRegression
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private sealed class Fixture : IDisposable
        {
            public readonly Scene Scene;
            public readonly Enemy Enemy;
            public readonly EnemyForgeDamageProbe Target;
            public readonly List<Object> Assets = new List<Object>();
            private readonly Scene previous;
            private readonly CombatSettings previousSettings;
            private readonly UnityEngine.Random.State random;
            private readonly object attackSource;
            private readonly HashSet<object> telegraphs;

            public Fixture()
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Runtime regression fixtures require idle Edit Mode.");
                previous = SceneManager.GetActiveScene(); previousSettings = CombatSettings.Active; random = UnityEngine.Random.state;
                attackSource = ParryEventBus.CurrentAttackSource;
                telegraphs = new HashSet<object>((HashSet<object>)typeof(ParryEventBus).GetField("_activeSources", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null));
                Scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                try
                {
                SceneManager.SetActiveScene(Scene);
                var actor = new GameObject("Enemy Forge action fixture");
                actor.transform.position = new Vector3(180000, 500, 180000);
                actor.AddComponent<NavMeshAgent>(); actor.AddComponent<SphereCollider>();
                Enemy = actor.AddComponent<Enemy>(); Enemy.EnemyStat = actor.GetComponent<EnemyStat>();
                var data = New<EnemyData>(); data.maxHp = 100; data.baseAtk = 100; data.criticalChance = 0; data.criticalDamage = 1; data.combatRange = 10; data.combatExitRange = 15; data.attackCooldown = 1;
                Set(Enemy.EnemyStat, "baseData", data); Invoke(Enemy.EnemyStat, "InitializeFromData");
                var model = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Models/Drone.fbx"));
                model.transform.SetParent(actor.transform, false);
                var animator = model.GetComponentInChildren<Animator>(true);
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/_Project/Animations/Drone/Drone.controller");
                animator.Rebind(); animator.Update(0f); animator.applyRootMotion = true;
                Set(Enemy, "<Animator>k__BackingField", animator);
                var clock = actor.AddComponent<CombatActorClock>(); Invoke(clock, "Awake"); Invoke(clock, "OnEnable"); Set(Enemy, "<ActorClock>k__BackingField", clock);
                Set(Enemy, "_navMeshAgent", actor.GetComponent<NavMeshAgent>());
                var target = new GameObject("Enemy Forge damage probe"); target.layer = LayerMask.NameToLayer("Player"); target.transform.position = actor.transform.position + Vector3.forward;
                Target = target.AddComponent<EnemyForgeDamageProbe>(); target.AddComponent<SphereCollider>().radius = .2f;
                var child = new GameObject("Second body collider"); child.transform.SetParent(target.transform, false); child.layer = target.layer; child.AddComponent<SphereCollider>().radius = .2f;
                var sensor = actor.AddComponent<EnemyForgeSensorProbe>(); sensor.Target = target.transform; Set(Enemy, "_sensor", sensor);
                var table = New<ElementEffectiveManager>();
                foreach (ElementType attack in Enum.GetValues(typeof(ElementType))) foreach (ElementType defense in Enum.GetValues(typeof(ElementType)))
                    table.elementEffectiveList.Add(new ElementEffective { AttackType = attack, DefenseType = defense, multiplier = 1 });
                table.Initialize();
                var settingsObject = new GameObject("Enemy Forge neutral combat settings"); settingsObject.SetActive(false);
                var settings = settingsObject.AddComponent<CombatSettings>(); Set(settings, "_elementTable", table);
                typeof(CombatSettings).GetProperty(nameof(CombatSettings.Active)).SetValue(null, settings);
                Physics.SyncTransforms();
                }
                catch { Dispose(); throw; }
            }

            public T New<T>() where T : ScriptableObject
            { var instance = ScriptableObject.CreateInstance<T>(); Assets.Add(instance); return instance; }

            public void Dispose()
            {
                try
                {
                    try { if (Enemy != null && Enemy.CurrentAction?.IsActive == true) Enemy.CurrentAction.Exit(); }
                    finally
                    {
                        try { if (Enemy != null) { var clock = Enemy.GetComponent<CombatActorClock>(); if (clock != null) Invoke(clock, "OnDisable"); } }
                        finally
                        {
                            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                            try { if (Scene.IsValid() && Scene.isLoaded) EditorSceneManager.CloseScene(Scene, true); }
                            finally { foreach (var asset in Assets) if (asset != null) Object.DestroyImmediate(asset); }
                        }
                    }
                }
                finally
                {
                    typeof(CombatSettings).GetProperty(nameof(CombatSettings.Active)).SetValue(null, previousSettings);
                    var active = (HashSet<object>)typeof(ParryEventBus).GetField("_activeSources", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                    active.Clear(); active.UnionWith(telegraphs);
                    typeof(ParryEventBus).GetField("<CurrentAttackSource>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, attackSource);
                    UnityEngine.Random.state = random;
                }
            }
        }

        public static JObject RunBuiltins()
        {
            int checks = 0;
            Action<bool, string> check = (condition, message) => { if (!condition) throw new InvalidOperationException("Enemy action regression: " + message); checks++; };
            using (var fixture = new Fixture())
            {
                foreach (Type type in new[] { typeof(MonsterDefaultAttackSO), typeof(MonsterDashAttackSO), typeof(MonsterComboAttackSO), typeof(MonsterJumpSlamSO), typeof(MonsterSelfDestructSO) })
                {
                    var skill = (MonsterSkillData)ScriptableObject.CreateInstance(type); fixture.Assets.Add(skill); skill.animName = "Attack"; skill.attackSpeed = 1.7f; skill.isParryable = true;
                    var action = skill.CreateAction(fixture.Enemy); fixture.Enemy.CurrentAction = action;
                    action.Enter();
                    check(action.IsActive && !action.IsFinished, type.Name + " entered");
                    check(ParryEventBus.IsTelegraphActive(action), type.Name + " registered telegraph");
                    check(Mathf.Abs(fixture.Enemy.ActorClock.AnimationSpeed - 1.7f) < .0001f, type.Name + " applied speed");
                    action.Exit(); action.Exit();
                    check(!action.IsActive && action.IsFinished, type.Name + " cancelled and repeated Exit handled");
                    check(!ParryEventBus.IsTelegraphActive(action), type.Name + " released telegraph");
                    check((int)Get(fixture.Enemy, "_moveLockCount") == 0, type.Name + " released movement locks");
                    check(fixture.Enemy.GetComponent<Collider>().enabled && fixture.Enemy.GetComponent<NavMeshAgent>().enabled, type.Name + " restored collision/navigation");
                    check(Mathf.Abs(fixture.Enemy.ActorClock.AnimationSpeed - 1f) < .0001f, type.Name + " restored speed");
                    check(fixture.Enemy.Animator.applyRootMotion, type.Name + " restored root motion");
                }
                var basic = fixture.New<MonsterDefaultAttackSO>(); basic.animName = "Attack"; basic.damage = .5f; basic.hitBoxRadius = 1.5f;
                var attack = basic.CreateAction(fixture.Enemy); fixture.Enemy.CurrentAction = attack; attack.Enter();
                Invoke(attack, "ExecuteHit"); Invoke(attack, "ExecuteHit");
                check(fixture.Target.Hits == 1, "two colliders plus repeated hit-query damage the target once");
                check(Mathf.Abs(fixture.Target.Damage - 50f) < .0001f, "actual DamageManager applies ATK multiplier with neutral table");
                check(ReferenceEquals(fixture.Target.Source, attack), "damage retains action source for parry");
                attack.Exit();
            }
            return new JObject { ["passed"] = true, ["checks"] = checks,
                ["message"] = "Actual Enter/Exit cancellation for all five attacks and actual overlap/damage deduplication passed in an isolated scene. No Play Mode or timeline progression was used." };
        }

        /// <summary>Behavior checks for the live generated triple-sphere implementation; does not pretend Edit Mode simulates runtime Destroy or frame time.</summary>
        public static JObject RunGeneratedProjectile(string typeId)
        {
            int checks = 0;
            Action<bool, string> check = (condition, message) => { if (!condition) throw new InvalidOperationException("Generated projectile regression: " + message); checks++; };
            Type type = BaekhoEnemyForgeCatalog.AttackType(typeId) ?? throw new InvalidOperationException("Generated attack is not registered.");
            using (var fixture = new Fixture())
            {
                var skill = (MonsterSkillData)ScriptableObject.CreateInstance(type); fixture.Assets.Add(skill);
                var descriptor = BaekhoEnemyForgeCatalog.Descriptor(typeId) ?? throw new InvalidOperationException("Descriptor missing.");
                BaekhoEnemyForgeCatalog.ApplyValues(skill, (JObject)descriptor["defaults"]);
                EnemyAction Action()
                {
                    var created = skill.CreateAction(fixture.Enemy); fixture.Enemy.CurrentAction = created; created.Enter(); return created;
                }
                object Projectile(EnemyAction action)
                {
                    var projectileType = action.GetType().GetNestedType("Projectile", BindingFlags.NonPublic) ?? throw new InvalidOperationException("This regression targets the generated triple-sphere implementation.");
                    var projectile = Activator.CreateInstance(projectileType, true);
                    Set(projectile, "Direction", Vector3.forward);
                    return projectile;
                }
                IList Projectiles(EnemyAction action) => (IList)Get(action, "_projectiles");

                var initial = Action();
                check(initial.IsActive && ParryEventBus.IsTelegraphActive(initial), "actual Enter starts attack and telegraph");
                fixture.Enemy.ActorClock.Request(fixture.Enemy, 10f, 0f, HitStopStacking.Replace);
                initial.Update();
                check((int)Get(initial, "_fired") == 0 && (float)Get(initial, "_timer") == 0f, "actual Update does not fire during a complete actor stop");
                fixture.Enemy.ActorClock.ClearStops(); initial.Exit(); initial.Exit();
                check(!initial.IsActive && initial.IsFinished && (int)Get(fixture.Enemy, "_moveLockCount") == 0, "startup cancellation and repeated Exit release ownership");
                check(fixture.Enemy.Animator.applyRootMotion, "generated attack restores previous root motion on cancellation");

                var contactAction = Action();
                var projectile = Projectile(contactAction); Projectiles(contactAction).Add(projectile);
                var colliders = fixture.Target.GetComponentsInChildren<Collider>();
                Invoke(contactAction, "ResolveContact", projectile, colliders[0], fixture.Target);
                Invoke(contactAction, "ResolveContact", projectile, colliders[1], fixture.Target);
                check(fixture.Target.Hits == 1 && Mathf.Abs(fixture.Target.Damage - 30f) < .0001f, "actual ResolveContact plus DamageManager applies ATK×0.3 exactly once across colliders");
                check(ReferenceEquals(fixture.Target.Source, contactAction) && !(bool)Get(projectile, "Alive"), "actual hit carries action source and consumes non-piercing projectile");
                contactAction.Exit();
                check(Projectiles(contactAction).Count == 0 && !ParryEventBus.IsTelegraphActive(contactAction), "Exit clears owned projectile data and telegraph");

                var parried = Action(); var parriedProjectile = Projectile(parried); Projectiles(parried).Add(parriedProjectile);
                fixture.Target.ParryNext = true;
                Invoke(parried, "ResolveContact", parriedProjectile, colliders[0], fixture.Target);
                check(!parried.IsActive && parried.IsFinished && Projectiles(parried).Count == 0, "DamageOutcome.Parried executes actual OnParried and cancels all projectiles");
                check((int)Get(fixture.Enemy, "_moveLockCount") == 0 && !ParryEventBus.IsTelegraphActive(parried), "parry releases movement/telegraph");

                var hurt = Action(); Projectiles(hurt).Add(Projectile(hurt)); fixture.Enemy.IsHurt = true; hurt.Update(); fixture.Enemy.IsHurt = false;
                check(!hurt.IsActive && Projectiles(hurt).Count == 0, "actual Update cancels when owner is hurt");
                var dead = Action(); Projectiles(dead).Add(Projectile(dead)); fixture.Enemy.EnemyStat.CurrentHp = 0; dead.Update(); fixture.Enemy.EnemyStat.CurrentHp = 100;
                check(!dead.IsActive && Projectiles(dead).Count == 0, "actual Update cancels when owner dies");

                var geometry = Action();
                Vector3 origin = fixture.Enemy.transform.position + Vector3.up * 1.5f;
                fixture.Target.transform.position = origin + Vector3.forward * 8f;
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Projectile regression wall"; wall.transform.position = origin + Vector3.forward * 3f;
                Physics.SyncTransforms();
                object[] hit = { origin, Vector3.forward, 20f, null, null, 0f };
                bool found = (bool)Invoke(geometry, "FindContact", hit);
                check(found && hit[3] is Collider wallHit && wallHit.gameObject == wall && hit[4] == null, "actual swept SphereCast picks the nearer wall before a player");
                wall.GetComponent<Collider>().isTrigger = true; Physics.SyncTransforms();
                hit = new object[] { origin, Vector3.forward, 20f, null, null, 0f }; found = (bool)Invoke(geometry, "FindContact", hit);
                check(found && ReferenceEquals(hit[4], fixture.Target), "environment sensor triggers are ignored while the player remains hittable");
                colliders[0].isTrigger = true; colliders[1].isTrigger = true; colliders[1].gameObject.layer = 0; Physics.SyncTransforms();
                hit = new object[] { origin, Vector3.forward, 20f, null, null, 0f }; found = (bool)Invoke(geometry, "FindContact", hit);
                check(found && ReferenceEquals(hit[4], fixture.Target), "player damage triggers and parent Player layer are recognized");
                object[] self = { fixture.Enemy.GetComponent<Collider>(), null };
                check(!(bool)Invoke(geometry, "Classify", self), "owner body is ignored by actual collision classifier");
                geometry.Exit();
            }
            return new JObject { ["passed"] = true, ["checks"] = checks, ["typeId"] = typeId,
                ["covered"] = new JArray("Enter/Exit lifecycle", "frozen-clock Update", "hurt/death cancellation", "real swept collision classification", "actual DamageManager/deduplication", "parry cleanup"),
                ["notCovered"] = new JArray("runtime visual spawning/destruction", "elapsed-frame scheduling", "visual motion/lifetime"),
                ["message"] = "Behavior checks used actual generated methods in an isolated additive scene. Projectile data probes intentionally have no Visual so runtime-only Object.Destroy is never invoked in Edit Mode." };
        }

        internal static object Get(object target, string name)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType) { var field = type.GetField(name, Hidden | BindingFlags.Public | BindingFlags.DeclaredOnly); if (field != null) return field.GetValue(target); }
            throw new MissingFieldException(target.GetType().FullName, name);
        }

        internal static void Set(object target, string name, object value)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType) { var field = type.GetField(name, Hidden | BindingFlags.Public | BindingFlags.DeclaredOnly); if (field != null) { field.SetValue(target, value); return; } }
            throw new MissingFieldException(target.GetType().FullName, name);
        }

        internal static object Invoke(object target, string method, params object[] args)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                var callback = type.GetMethods(Hidden | BindingFlags.Public | BindingFlags.DeclaredOnly).FirstOrDefault(m => m.Name == method && m.GetParameters().Length == args.Length);
                if (callback != null) return callback.Invoke(target, args);
            }
            throw new MissingMethodException(target.GetType().FullName, method);
        }
    }

    public sealed class EnemyForgeSensorProbe : MonoBehaviour, IEnemySensor
    {
        public Transform Target;
        public Transform DetectedTarget => Target;
        public bool DetectTarget() => Target != null;
    }

    public sealed class EnemyForgeDamageProbe : MonoBehaviour, IDamageable
    {
        public int Hits; public float Damage; public object Source; public bool ParryNext;
        public DamageResult TakeDamage(float damage, ElementType damageType = ElementType.ELEMENT_NONE, bool isCritical = false,
            Vector3 power = default, float poiseDamage = 20f, StaggerResistLevel staggerResistLevel = StaggerResistLevel.NONE, bool isParryable = true, object source = null)
        { Source = source; if (ParryNext) { ParryNext = false; return new DamageResult(DamageOutcome.Parried); } Hits++; Damage += damage; return new DamageResult(DamageOutcome.Applied, damage); }
    }

    public sealed class EnemyForgeRuntimeEnemyProbe : Enemy
    {
        // A fixture owner retaining all production Enemy APIs while autonomous gameplay stays out of this test scene.
        protected override void Start() { }
        protected override void Update() { }
    }

    [InitializeOnLoad]
    public static class BaekhoEnemyForgePlayModeRegression
    {
        private const string JobKey = "BaekhoEnemyForge.PlayModeRegression";
        private static PlayFixture fixture;
        private static EnemyAction action;
        private static int caseIndex, lastFrame = -1, waitUntilFrame, maximumProjectiles;
        private static bool moved, spacingChecked;
        private static Vector3? firstPosition;
        private static GameObject firstVisual, wall;
        private static float caseStart;
        private static JArray cases;

        static BaekhoEnemyForgePlayModeRegression() { EditorApplication.update += Tick; }

        public static JObject Start(string typeId)
        {
            BaekhoEnemyForgeBridge.RequireReady(true);
            var existing = Status();
            if ((string)existing["state"] == "entering" || (string)existing["state"] == "running" || (string)existing["state"] == "exiting") throw new InvalidOperationException("A PlayMode regression is already active.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && (scene.isDirty || string.IsNullOrEmpty(scene.path))) throw new InvalidOperationException("PlayMode regression requires every open scene to be saved and clean.");
            }
            if (BaekhoEnemyForgeCatalog.AttackType(typeId) == null) throw new InvalidOperationException("Registered attack type was not found.");
            var job = new JObject { ["jobId"] = Guid.NewGuid().ToString("D"), ["state"] = "entering", ["typeId"] = typeId,
                ["sceneSetup"] = JToken.Parse(JsonConvert.SerializeObject(EditorSceneManager.GetSceneManagerSetup())), ["startedUtc"] = DateTime.UtcNow.ToString("O") };
            Save(job);
            try
            {
                BaekhoEnemyForgePreview.CloseAll();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorApplication.EnterPlaymode();
                return job;
            }
            catch (Exception exception) { Finish(exception.GetBaseException().Message); return Status(); }
        }

        public static JObject Status()
        {
            string json = SessionState.GetString(JobKey, "");
            return string.IsNullOrEmpty(json) ? new JObject { ["state"] = "idle" } : JObject.Parse(json);
        }
        private static void Save(JObject job) => SessionState.SetString(JobKey, job.ToString(Formatting.None));
        private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        private static object Get(object target, string name) => BaekhoEnemyForgeRuntimeRegression.Get(target, name);
        private static void Set(object target, string name, object value) => BaekhoEnemyForgeRuntimeRegression.Set(target, name, value);

        private static void Tick()
        {
            var job = Status(); string state = (string)job["state"];
            if (state != "entering" && state != "running" && state != "exiting") return;
            try
            {
                if (state == "exiting")
                {
                    if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                    EditorSceneManager.RestoreSceneManagerSetup(JsonConvert.DeserializeObject<SceneSetup[]>(job["sceneSetup"].ToString()));
                    job["state"] = job["error"] == null ? "completed" : "failed";
                    job["restoredScene"] = SceneManager.GetActiveScene().path; Save(job); return;
                }
                if ((DateTime.UtcNow - DateTime.Parse((string)job["startedUtc"], null, System.Globalization.DateTimeStyles.RoundtripKind)).TotalSeconds > 150) throw new TimeoutException("PlayMode regression timed out.");
                if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying) return;
                if (!EditorApplication.isPlaying)
                {
                    if (state == "running") throw new InvalidOperationException("Play Mode ended before the test completed.");
                    return;
                }
                Application.runInBackground = true;
                EditorApplication.QueuePlayerLoopUpdate();
                if (state == "entering")
                {
                    fixture = new PlayFixture((string)job["typeId"]); caseIndex = 0; cases = new JArray(); lastFrame = -1;
                    job["state"] = "running"; Save(job); BeginCase(); return;
                }
                if (fixture == null || action == null) throw new InvalidOperationException("Unexpected domain reload during runtime test.");
                if (Time.frameCount == lastFrame) return; lastFrame = Time.frameCount;
                if (waitUntilFrame > 0)
                {
                    if (Time.frameCount < waitUntilFrame) return;
                    Check(LiveVisuals() == 0, "Runtime destruction left a projectile after two frames.");
                    Check((int)Get(fixture.Enemy, "_moveLockCount") == 0 && !ParryEventBus.IsTelegraphActive(action), "Movement lock or telegraph remained after completion.");
                    Check(fixture.Enemy.Animator.applyRootMotion, "Previous root-motion setting was not restored.");
                    cases.Add(new JObject { ["case"] = new[] { "three-shot timing/motion/lifetime", "wall occlusion", "three hits with two colliders", "cancellation cleanup", "parry cleanup" }[caseIndex], ["passed"] = true });
                    if (wall != null) Object.Destroy(wall);
                    caseIndex++;
                    if (caseIndex >= 5) { Finish(null); return; }
                    BeginCase(); return;
                }
                Check(Time.time - caseStart < 15f, "Runtime case timed out: " + caseIndex);
                action.Update();
                int fired = (int)Get(action, "_fired"); var projectiles = (IList)Get(action, "_projectiles");
                maximumProjectiles = Math.Max(maximumProjectiles, projectiles.Count);
                if (firstVisual == null && projectiles.Count > 0)
                {
                    firstVisual = Get(projectiles[0], "Visual") as GameObject;
                    if (firstVisual != null) firstPosition = firstVisual.transform.position;
                }
                if (firstVisual != null && firstPosition.HasValue && Vector3.Distance(firstVisual.transform.position, firstPosition.Value) > .2f) moved = true;
                if (caseIndex == 0)
                {
                    float elapsed = (float)Get(action, "_timer");
                    if (elapsed < 1f - .0001f) Check(fired == 0, "A projectile fired before the authored one-second preparation.");
                    if (projectiles.Count == 3 && !spacingChecked)
                    {
                        float first = (float)Get(projectiles[0], "Age"), second = (float)Get(projectiles[1], "Age"), third = (float)Get(projectiles[2], "Age");
                        Check(Mathf.Abs(first - second - .15f) < .005f && Mathf.Abs(second - third - .15f) < .005f, "Authored shot spacing was not retained across frame boundaries.");
                        spacingChecked = true;
                    }
                    if (action.IsFinished)
                    {
                        Check(fired == 3 && maximumProjectiles == 3 && moved && spacingChecked, "Three projectiles, actual motion or spacing were not observed.");
                        Check(elapsed >= 4.29f && fixture.Target.Hits == 0, "Projectiles completed before lifetime or hit an out-of-range target."); CompleteCase();
                    }
                }
                else if (caseIndex == 1 && action.IsFinished)
                { Check(fired == 3 && fixture.Target.Hits == 0, "Wall failed to block all three projectiles."); CompleteCase(); }
                else if (caseIndex == 2 && action.IsFinished)
                { Check(fixture.Target.Hits == 3 && Mathf.Abs(fixture.Target.Damage - 90f) < .001f, "Each projectile must damage a two-collider target once (three hits / 90 damage)."); CompleteCase(); }
                else if (caseIndex == 3 && fired == 3)
                {
                    Check(projectiles.Count == 3, "Cancellation fixture requires three live projectiles."); action.Exit();
                    Check(LiveActiveVisuals() == 0, "Exit did not immediately deactivate owned projectiles."); CompleteCase();
                }
                else if (caseIndex == 4 && !action.IsActive)
                { Check(fired == 3 && fixture.Target.Hits == 0 && !fixture.Target.ParryNext, "Parry did not consume the first hit and cancel the burst."); CompleteCase(); }
            }
            catch (Exception exception) { Finish(exception.GetBaseException().Message); }
        }

        private static void BeginCase()
        {
            fixture.Target.Hits = 0; fixture.Target.Damage = 0; fixture.Target.ParryNext = caseIndex == 4;
            fixture.Target.transform.position = Vector3.forward * (caseIndex == 1 ? 8f : caseIndex == 2 || caseIndex == 4 ? 4f : 40f);
            if (caseIndex == 1)
            {
                wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Enemy Forge test wall"; wall.transform.position = new Vector3(0, 1.5f, 3); wall.transform.localScale = new Vector3(2, 3, .25f);
            }
            Physics.SyncTransforms(); action = fixture.Skill.CreateAction(fixture.Enemy); fixture.Enemy.CurrentAction = action;
            action.Enter(); caseStart = Time.time; waitUntilFrame = 0; maximumProjectiles = 0; moved = false; spacingChecked = false; firstVisual = null; firstPosition = null;
        }
        private static void CompleteCase() { action.Exit(); waitUntilFrame = Time.frameCount + 2; }
        private static int LiveVisuals() => SceneManager.GetActiveScene().GetRootGameObjects().Count(go => go.name == "Triple Sphere Burst Projectile");
        private static int LiveActiveVisuals() => SceneManager.GetActiveScene().GetRootGameObjects().Count(go => go.name == "Triple Sphere Burst Projectile" && go.activeInHierarchy);

        private static void Finish(string error)
        {
            var job = Status();
            try { if (action?.IsActive == true) action.Exit(); }
            catch (Exception cleanup) { error = error ?? cleanup.GetBaseException().Message; }
            job["state"] = "exiting"; job["cases"] = cases ?? new JArray();
            if (error != null) job["error"] = error;
            Save(job);
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
        }

        private sealed class PlayFixture
        {
            public readonly Enemy Enemy;
            public readonly EnemyForgeDamageProbe Target;
            public readonly MonsterSkillData Skill;
            public PlayFixture(string typeId)
            {
                var camera = new GameObject("Regression Camera").AddComponent<Camera>(); camera.transform.position = new Vector3(0, 4, -8);
                new GameObject("Regression Light").AddComponent<Light>().type = LightType.Directional;
                var actor = new GameObject("Isolated generated attack owner"); actor.SetActive(false);
                var nav = actor.AddComponent<NavMeshAgent>(); nav.enabled = false; actor.AddComponent<SphereCollider>();
                Enemy = actor.AddComponent<EnemyForgeRuntimeEnemyProbe>(); Enemy.EnemyStat = actor.GetComponent<EnemyStat>();
                var data = ScriptableObject.CreateInstance<EnemyData>(); data.maxHp = 100; data.baseAtk = 100; data.criticalChance = 0; data.criticalDamage = 1; data.attackCooldown = 1; data.combatRange = 10; data.combatExitRange = 15;
                Set(Enemy.EnemyStat, "baseData", data);
                var model = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Models/Drone.fbx")); model.transform.SetParent(actor.transform, false);
                var animator = model.GetComponentInChildren<Animator>(true); animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/_Project/Animations/Drone/Drone.controller"); animator.applyRootMotion = true;
                var clock = actor.AddComponent<CombatActorClock>(); var sensor = actor.AddComponent<EnemyForgeSensorProbe>();
                Set(Enemy, "<Animator>k__BackingField", animator); Set(Enemy, "<ActorClock>k__BackingField", clock); Set(Enemy, "_navMeshAgent", nav); Set(Enemy, "_sensor", sensor);
                var target = new GameObject("Two-collider player probe"); target.layer = LayerMask.NameToLayer("Player");
                Target = target.AddComponent<EnemyForgeDamageProbe>(); var body = target.AddComponent<SphereCollider>(); body.radius = .2f; body.center = Vector3.up * 1.5f;
                var extra = new GameObject("Second collider"); extra.transform.SetParent(target.transform, false); extra.layer = target.layer;
                var second = extra.AddComponent<SphereCollider>(); second.radius = .2f; second.center = Vector3.up * 1.5f; sensor.Target = target.transform;
                var table = ScriptableObject.CreateInstance<ElementEffectiveManager>();
                foreach (ElementType attack in Enum.GetValues(typeof(ElementType))) foreach (ElementType defense in Enum.GetValues(typeof(ElementType))) table.elementEffectiveList.Add(new ElementEffective { AttackType = attack, DefenseType = defense, multiplier = 1 });
                table.Initialize(); var settingsObject = new GameObject("Isolated combat settings"); settingsObject.SetActive(false); var settings = settingsObject.AddComponent<CombatSettings>(); Set(settings, "_elementTable", table); settingsObject.SetActive(true);
                Skill = (MonsterSkillData)ScriptableObject.CreateInstance(BaekhoEnemyForgeCatalog.AttackType(typeId));
                BaekhoEnemyForgeCatalog.ApplyValues(Skill, (JObject)BaekhoEnemyForgeCatalog.Descriptor(typeId)["defaults"]);
                actor.SetActive(true); animator.Rebind(); animator.Update(0f);
            }
        }
    }
}
