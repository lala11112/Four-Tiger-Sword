using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Exercises the real two-rig revival and opener, and measures escape routes in the authored arena.</summary>
public static class BossPhaseTwoVisualRegression
{
    private const string VariantFolder = "Assets/_Project/Generated/Bosses/HitReactionVariants";
    private const string RoomPath = "Assets/_Game/RunProgression/Rooms/ChapterBossRoom.prefab";
    private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static bool _running;
    private static Report _report = new Report { status = "idle" };

    [Serializable] private sealed class Report
    {
        public string status, currentCase, error, directory;
        public int checks;
        public List<VariantResult> variants = new List<VariantResult>();
        public List<string> screenshots = new List<string>();
    }
    [Serializable] private sealed class VariantResult
    {
        public string name;
        public int burstActions, burstEffects, phaseChanges, deaths;
        public float initialHp, revivedHp, gripError;
        public bool animatorRebound, overlayRebound, clockFrozen;
    }
    [Serializable] private sealed class ArenaReport
    {
        public string status, error, path;
        public int colliderSources;
        public float radius = 12f, playerRadius = .23f, runSpeed = 7f, warning = 2.5f, reaction = .4f, budget = 14.7f;
        public List<EscapeResult> cases = new List<EscapeResult>();
    }
    [Serializable] private sealed class EscapeResult
    {
        public string name;
        public Vector3 center, start, destination;
        public float pathLength = -1f;
        public bool startOnNavigation, routeFound, withinBudget;
    }

    [MenuItem("Tools/Studio Baekho/Enemy/Verify Phase Two Arena Escape")]
    private static void ArenaMenu() => Debug.Log(RunArenaEscape());

    /// <summary>Builds navigation from the actual room's colliders, including props, walls and raised floor.</summary>
    public static string RunArenaEscape()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Measure the authored arena in Edit Mode.");
        var report = new ArenaReport { status = "running", path = Path.Combine(OutputRoot(), "arena-escape.json") };
        Scene scene = default;
        GameObject room = null;
        NavMeshData navigation = null;
        NavMeshDataInstance instance = default;
        try
        {
            scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoomPath);
            if (prefab == null) throw new InvalidOperationException("The current ChapterBossRoom is missing.");
            room = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var origin = new Vector3(40000f, 0f, 40000f);
            room.transform.SetPositionAndRotation(origin, Quaternion.identity);
            // CollectSources does not discover colliders in an Editor preview physics scene.
            // Convert the authored collision shapes directly instead of substituting render geometry.
            var sources = ColliderSources(room);
            report.colliderSources = sources.Count;
            if (sources.Count == 0) throw new InvalidOperationException("The real arena supplied no collider geometry.");
            if (NavMesh.GetSettingsCount() == 0) throw new InvalidOperationException("No navigation build settings exist.");
            var settings = NavMesh.GetSettingsByIndex(0);
            settings.agentRadius = report.playerRadius;
            settings.agentHeight = 1.56f;
            settings.agentClimb = .35f;
            settings.agentSlope = 45f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = .1f;
            navigation = NavMeshBuilder.BuildNavMeshData(settings, sources,
                new Bounds(origin + Vector3.up * 6f, new Vector3(40f, 24f, 48f)), Vector3.zero, Quaternion.identity);
            if (navigation == null) throw new InvalidOperationException("Could not build the actual arena navigation.");
            instance = NavMesh.AddNavMeshData(navigation);
            var filter = new NavMeshQueryFilter { agentTypeID = settings.agentTypeID, areaMask = NavMesh.AllAreas };
            report.cases.Add(MeasureEscape("flat-near-spawn", origin, new Vector3(-1f, 0f, 1f), new Vector3(-1f, 0f, -1f), filter, report));
            report.cases.Add(MeasureEscape("west-wall", origin, new Vector3(-10f, 0f, -4f), new Vector3(-10f, 0f, -2f), filter, report));
            // The south stairs occupy x=-8..-4, z=6..8 (roots -7/-5,0,7 at scale 2).
            // Sample the flat approach half a metre before their foot, rather than the adjacent wall at x=-4,z=7.
            report.cases.Add(MeasureEscape("platform-lower-edge", origin, new Vector3(-6f, 0f, 4.5f), new Vector3(-6f, 0f, 5.5f), filter, report));
            report.cases.Add(MeasureEscape("raised-platform", origin, new Vector3(-4f, 2f, 12f), new Vector3(-4f, 2f, 10f), filter, report));
            report.status = report.cases.All(result => result.routeFound && result.withinBudget) ? "passed" : "failed";
            if (report.status != "passed") report.error = "An actual arena route is missing or exceeds the 14.7m walking budget.";
        }
        catch (Exception exception) { report.status = "failed"; report.error = exception.GetBaseException().Message; }
        finally
        {
            if (instance.valid) instance.Remove();
            if (navigation != null) Object.DestroyImmediate(navigation);
            if (room != null) Object.DestroyImmediate(room);
            if (scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            Directory.CreateDirectory(Path.GetDirectoryName(report.path));
            File.WriteAllText(report.path, JsonUtility.ToJson(report, true));
        }
        return JsonUtility.ToJson(report, true);
    }

    private static List<NavMeshBuildSource> ColliderSources(GameObject room)
    {
        var sources = new List<NavMeshBuildSource>();
        foreach (var collider in room.GetComponentsInChildren<Collider>(true))
        {
            if (!collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy) continue;
            var source = new NavMeshBuildSource { area = 0, component = collider };
            Vector3 scale = collider.transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            if (collider is BoxCollider box)
            {
                source.shape = NavMeshBuildSourceShape.Box;
                source.size = Vector3.Scale(box.size, scale);
                source.transform = Matrix4x4.TRS(box.transform.TransformPoint(box.center), box.transform.rotation, Vector3.one);
            }
            else if (collider is MeshCollider mesh)
            {
                if (mesh.sharedMesh == null) throw new InvalidOperationException("An enabled arena MeshCollider has no mesh: " + mesh.name);
                source.shape = NavMeshBuildSourceShape.Mesh;
                source.sourceObject = mesh.sharedMesh;
                source.transform = mesh.transform.localToWorldMatrix;
            }
            else if (collider is CapsuleCollider capsule)
            {
                Vector3 direction = capsule.direction == 0 ? Vector3.right : capsule.direction == 2 ? Vector3.forward : Vector3.up;
                float along = capsule.direction == 0 ? scale.x : capsule.direction == 2 ? scale.z : scale.y;
                float across = capsule.direction == 0 ? Mathf.Max(scale.y, scale.z)
                    : capsule.direction == 2 ? Mathf.Max(scale.x, scale.y) : Mathf.Max(scale.x, scale.z);
                float diameter = capsule.radius * across * 2f;
                source.shape = NavMeshBuildSourceShape.Capsule;
                source.size = new Vector3(diameter, Mathf.Max(diameter, capsule.height * along), diameter);
                source.transform = Matrix4x4.TRS(capsule.transform.TransformPoint(capsule.center),
                    capsule.transform.rotation * Quaternion.FromToRotation(Vector3.up, direction), Vector3.one);
            }
            else
                throw new InvalidOperationException("Unsupported authored arena collider: " + collider.GetType().Name + " on " + collider.name);
            sources.Add(source);
        }
        return sources;
    }

    private static EscapeResult MeasureEscape(string name, Vector3 origin, Vector3 localCenter,
        Vector3 localStart, NavMeshQueryFilter filter, ArenaReport report)
    {
        var result = new EscapeResult { name = name, center = localCenter, start = localStart };
        Vector3 center = origin + localCenter;
        if (!NavMesh.SamplePosition(origin + localStart, out var start, .8f, filter)) return result;
        result.startOnNavigation = true;
        result.start = start.position - origin;
        float best = float.PositiveInfinity;
        var path = new NavMeshPath();
        for (int ring = 0; ring < 3; ring++)
            for (int step = 0; step < 72; step++)
                foreach (float height in new[] { 0f, 2f })
                {
                    float angle = step * Mathf.PI * 2f / 72f;
                    float radius = report.radius + report.playerRadius + .25f + ring;
                    var destination = new Vector3(center.x + Mathf.Cos(angle) * radius,
                        origin.y + height, center.z + Mathf.Sin(angle) * radius);
                    if (!NavMesh.SamplePosition(destination, out var exit, .8f, filter)) continue;
                    Vector3 offset = exit.position - center; offset.y = 0f;
                    if (offset.magnitude < report.radius + report.playerRadius) continue;
                    if (!NavMesh.CalculatePath(start.position, exit.position, filter, path)
                        || path.status != NavMeshPathStatus.PathComplete) continue;
                    float length = 0f;
                    for (int corner = 1; corner < path.corners.Length; corner++)
                        length += Vector3.Distance(path.corners[corner - 1], path.corners[corner]);
                    if (length >= best) continue;
                    best = length;
                    result.destination = exit.position - origin;
                }
        result.routeFound = !float.IsPositiveInfinity(best);
        if (result.routeFound) result.pathLength = best;
        result.withinBudget = result.routeFound && best <= report.budget;
        return result;
    }

    public static string StartPlayMode()
    {
        if (!Application.isPlaying || EditorApplication.isCompiling || _running)
            throw new InvalidOperationException("Start phase-two verification once in idle Play Mode.");
        _report = new Report { status = "running", directory = Path.Combine(OutputRoot(), DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")) };
        Directory.CreateDirectory(_report.directory);
        _running = true;
        _ = RunRuntime();
        return Status();
    }

    public static string Status() => JsonUtility.ToJson(_report, true);
    private static string OutputRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "BossPhaseTwoVerification"));

    private static async Task RunRuntime()
    {
        Fixture fixture = null;
        try
        {
            fixture = new Fixture();
            foreach (string variant in new[] { "Additive", "FinalIK" })
            {
                _report.currentCase = variant;
                await VerifyVariant(fixture, variant);
            }
            _report.status = "passed";
        }
        catch (Exception exception)
        { _report.status = "failed"; _report.error = exception.GetBaseException().Message; Debug.LogException(exception); }
        finally
        {
            try { if (fixture != null) await fixture.Dispose(); }
            catch (Exception exception) { _report.status = "cleanup failed"; _report.error = exception.GetBaseException().Message; }
            _running = false;
            File.WriteAllText(Path.Combine(_report.directory, "report.json"), Status());
        }
    }

    private static async Task VerifyVariant(Fixture fixture, string variant)
    {
        var result = new VariantResult { name = variant };
        _report.variants.Add(result);
        var boss = fixture.Actor(VariantFolder + "/SwordRobotBoss_" + variant + ".prefab");
        var observedBursts = new HashSet<EnemyAction>();
        void Observe()
        {
            if (boss != null && boss.CurrentAction is BossPhaseBurstAction action) observedBursts.Add(action);
            result.burstActions = observedBursts.Count;
        }
        await Until(() => boss.Brain != null, "The real boss did not initialize.", Observe);
        await Frames(2);
        var visuals = boss.GetComponent<BossPhaseVisuals>();
        var presentation = boss.GetComponent<SwordRobotBossPresentation>();
        Check(visuals != null && presentation != null, "The generated boss lacks phase visuals or attack presentation.");
        Check(visuals.ActiveVisualPhase == 0 && visuals.phaseOne.root.activeInHierarchy
            && !visuals.phaseTwo.root.activeInHierarchy, "The encounter did not start with only the phase-one model.");
        Check(boss.Animator == visuals.phaseOne.animator && visuals.phaseTwo.animator.avatar.isHuman
            && visuals.phaseTwo.animator.avatar.isValid, "The phase-two rig or initial animator binding is invalid.");
        result.initialHp = boss.EnemyStat.CurrentHp;
        Check(Mathf.Approximately(result.initialHp, 5460f), "The actual variant did not initialize at 5460 HP.");
        Capture(fixture, boss, variant + "_01_phase-one", false);
        boss.OnPhaseChanged += (index, phase) => result.phaseChanges++;
        boss.OnDied += () => result.deaths++;
        var lethal = boss.TakeDamage(10000000f, poiseDamage: 0f);
        Check(lethal.Applied && !lethal.Killed && boss.IsTransitioning, "Phase one depleted without starting revival.");
        boss.CombatLabPassive = false;
        await Until(() => visuals.ActiveVisualPhase == 1 && boss.IsTransitioning,
            "The new model did not appear at the start of the revival animation.", Observe);
        await Frames(1);
        result.animatorRebound = boss.Animator == visuals.phaseTwo.animator
            && GetField<Animator>(boss.ActorClock, "_animator") == visuals.phaseTwo.animator;
        Check(result.animatorRebound && !visuals.phaseOne.root.activeInHierarchy && visuals.phaseTwo.root.activeInHierarchy,
            "The visual swap failed to rebind the combat actor and its clock to the new rig.");
        Check(visuals.phaseTwo.weapon != null && visuals.phaseTwo.weapon.IsChildOf(
            boss.Animator.GetBoneTransform(HumanBodyBones.RightHand)), "The new weapon is not attached to the new right hand.");
        Capture(fixture, boss, variant + "_02_new-model-revival", false);
        await Until(() => boss.CurrentAction is BossPhaseBurstAction,
            "The revived boss failed to execute its opening burst before regular patterns.", Observe);
        var burst = (BossPhaseBurstAction)boss.CurrentAction;
        result.revivedHp = boss.EnemyStat.CurrentHp;
        Check(boss.PhaseIndex == 1 && !boss.IsTransitioning && Mathf.Approximately(result.revivedHp, 6552f),
            "Phase two did not restore its actual 6552 HP pool before the burst.");
        await Until(() => burst.ChargeProgress >= .25f, "The burst warning did not progress.", Observe);
        await Frames(1);
        Check(presentation.BurstWarningVisible && presentation.BurstFillProgress > 0f,
            "The real phase-entry action did not display its floor warning and fill.");
        Capture(fixture, boss, variant + "_03_opening-warning", true);
        float beforeStop = burst.AttackTimeline;
        boss.ActorClock.Request(boss, 2f, 0f, HitStopStacking.Replace);
        await Frames(3);
        result.clockFrozen = Mathf.Abs(burst.AttackTimeline - beforeStop) < .005f;
        Check(result.clockFrozen, "The new-rig phase opener ignored actor-local hit stop.");
        boss.ActorClock.ClearStops();
        await Until(() => presentation.BurstEffectCount == 1, "The burst never produced its impact effect.", Observe);
        await Awaitable.EndOfFrameAsync();
        Capture(fixture, boss, variant + "_04_opening-impact", true);
        await Until(() => burst.BossStage == BossAttackStage.Recovery && boss.CurrentAction == burst,
            "The burst failed to enter its punishable recovery.", Observe);
        var hit = boss.TakeDamage(1f, power: Vector3.right * 5f, poiseDamage: 0f,
            staggerResistLevel: StaggerResistLevel.HIGH);
        Check(hit.Applied && boss.CurrentAction == burst && !boss.IsHurt && !boss.IsKnockbacking,
            "An ordinary hit in burst recovery replaced the action instead of playing cosmetic recoil.");
        await Frames(2);
        var additive = boss.GetComponent<BossAdditiveHitReaction>();
        var finalIK = boss.GetComponent<BossFinalIKHitReaction>();
        result.overlayRebound = additive != null
            ? additive.animator == visuals.phaseTwo.animator && additive.IsReacting
            : finalIK != null && finalIK.ik == visuals.phaseTwo.ik && finalIK.reaction == visuals.phaseTwo.reaction && finalIK.IsReacting;
        Check(result.overlayRebound, "The real damage recoil was not rebound to the phase-two rig.");
        result.gripError = GripError(boss);
        Check(result.gripError < .35f, "The phase-two sword grip separated by more than 35cm: " + result.gripError);
        Capture(fixture, boss, variant + "_05_recovery-hit", false);
        await Until(() => boss.CurrentAction != null && !(boss.CurrentAction is BossPhaseBurstAction),
            "Regular phase-two combat did not resume after the opening recovery.", Observe);
        await Frames(2);
        Observe();
        result.burstEffects = presentation.BurstEffectCount;
        Check(result.burstActions == 1 && result.burstEffects == 1 && result.phaseChanges == 1,
            "Revival replayed its entry attack or phase change instead of returning to regular combat.");
        Capture(fixture, boss, variant + "_06_regular-combat", false);
        var death = boss.TakeDamage(10000000f, poiseDamage: 0f);
        await Frames(2);
        Check(death.Killed && boss.IsDead && result.deaths == 1 && visuals.ActiveVisualPhase == 1,
            "The final kill did not retain the new visual and dispatch exactly one death.");
        Check(!presentation.BurstWarningVisible && (additive == null || !additive.IsReacting)
            && (finalIK == null || !finalIK.IsReacting), "Death retained the burst warning or cosmetic recoil.");
        Capture(fixture, boss, variant + "_07_final-death", false);
        fixture.Stop(boss);
        await Frames(2);
    }

    private static float GripError(Boss boss)
    {
        var grip = boss.Animator.GetComponent<SwordRobotBossWeaponGrip>();
        if (grip == null || grip.leftHandGrip == null) throw new InvalidOperationException("The phase-two rig has no two-hand grip.");
        return Vector3.Distance(boss.Animator.GetBoneTransform(HumanBodyBones.LeftHand).position, grip.leftHandGrip.position);
    }

    private static void Capture(Fixture fixture, Boss boss, string name, bool wide)
    {
        var camera = fixture.Camera;
        camera.transform.position = boss.transform.position + (wide ? new Vector3(18f, 25f, 24f) : new Vector3(4.2f, 3.3f, 5.8f));
        camera.transform.LookAt(boss.transform.position + Vector3.up * (wide ? 0f : 1.55f));
        camera.farClipPlane = wide ? 80f : 35f;
        var previous = RenderTexture.active;
        var target = RenderTexture.GetTemporary(720, 720, 24, RenderTextureFormat.ARGB32);
        var texture = new Texture2D(720, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 720, 720), 0, 0);
            texture.Apply();
            string path = Path.Combine(_report.directory, name + ".png");
            File.WriteAllBytes(path, texture.EncodeToPNG());
            _report.screenshots.Add(path);
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.Destroy(texture);
        }
    }

    private static async Task Until(Func<bool> condition, string message, Action observe = null)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + 18d;
        while (true)
        {
            observe?.Invoke();
            if (condition()) return;
            if (!Application.isPlaying || Time.realtimeSinceStartupAsDouble > deadline) throw new InvalidOperationException(message);
            await Task.Yield();
        }
    }
    private static async Task Frames(int count)
    {
        int end = Time.frameCount + count;
        await Until(() => Time.frameCount >= end, "The editor stopped advancing Play Mode frames.");
        await Awaitable.EndOfFrameAsync();
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); _report.checks++; }
    private static T GetField<T>(object target, string name)
        => (T)target.GetType().GetField(name, Hidden).GetValue(target);
    private static void SetField(object target, string name, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Hidden | BindingFlags.DeclaredOnly);
            if (field != null) { field.SetValue(target, value); return; }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    private sealed class Fixture
    {
        private readonly Scene _scene;
        private readonly List<Object> _owned = new List<Object>();
        private readonly float _timeScale = Time.timeScale;
        private readonly bool _background = Application.runInBackground;
        private readonly UnityEngine.Random.State _random = UnityEngine.Random.state;
        private readonly Vector3 _origin = new Vector3(24000f, 0f, 24000f);
        private readonly Transform _target;
        private NavMeshDataInstance _navigation;
        public readonly Camera Camera;

        public Fixture()
        {
            Time.timeScale = 1f;
            Application.runInBackground = true;
            _scene = SceneManager.CreateScene("BossPhaseTwoVerification-" + Guid.NewGuid().ToString("N"));
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(floor, _scene);
            floor.name = "Isolated phase verification floor";
            floor.transform.position = _origin + Vector3.down * .25f;
            floor.transform.localScale = new Vector3(60f, .5f, 60f);
            var settings = NavMesh.GetSettingsByID(0);
            var data = NavMeshBuilder.BuildNavMeshData(settings, new List<NavMeshBuildSource> {
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(60f, .5f, 60f),
                    transform = Matrix4x4.TRS(_origin + Vector3.down * .25f, Quaternion.identity, Vector3.one) }
            }, new Bounds(_origin, new Vector3(62f, 15f, 62f)), Vector3.zero, Quaternion.identity);
            if (data == null) throw new InvalidOperationException("Could not build isolated boss verification navigation.");
            _owned.Add(data);
            _navigation = NavMesh.AddNavMeshData(data);
            var target = new GameObject("Assigned phase-two target");
            SceneManager.MoveGameObjectToScene(target, _scene);
            _target = target.transform;
            _target.position = _origin + Vector3.forward * 3f;
            var cameraHost = new GameObject("Phase-two verification camera");
            SceneManager.MoveGameObjectToScene(cameraHost, _scene);
            Camera = cameraHost.AddComponent<Camera>();
            Camera.enabled = false;
            Camera.fieldOfView = 45f;
            Camera.nearClipPlane = .05f;
            Camera.farClipPlane = 80f;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(.1f, .12f, .15f, 1f);
            var lightHost = new GameObject("Phase-two verification light");
            SceneManager.MoveGameObjectToScene(lightHost, _scene);
            var light = lightHost.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.transform.rotation = Quaternion.Euler(35f, -35f, 0f);
        }

        public Boss Actor(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException("The actual boss variant is missing: " + path);
            var staging = new GameObject("Inactive phase verification staging");
            staging.SetActive(false);
            SceneManager.MoveGameObjectToScene(staging, _scene);
            var root = Object.Instantiate(prefab, staging.transform);
            root.SetActive(false);
            root.transform.SetParent(null);
            SceneManager.MoveGameObjectToScene(root, _scene);
            root.transform.SetPositionAndRotation(_origin, Quaternion.identity);
            Object.Destroy(staging);
            var boss = root.GetComponent<ConfiguredBoss>();
            var data = Object.Instantiate(boss.Data);
            data.rewardGroup = null;
            data.initialShield = 0f;
            _owned.Add(data);
            SetField(boss, "BossData", data);
            SetField(root.GetComponent<EnemyStat>(), "baseData", data);
            boss.CombatLabPassive = true;
            boss.SetCombatTarget(_target);
            foreach (var animator in root.GetComponentsInChildren<Animator>(true)) animator.fireEvents = false;
            root.SetActive(true);
            return boss;
        }
        public void Stop(Boss boss)
        {
            if (boss == null) return;
            boss.CancelCurrentAction();
            boss.ActorClock.ClearStops();
            boss.gameObject.SetActive(false);
            Object.Destroy(boss.gameObject);
        }
        public async Task Dispose()
        {
            try
            {
                if (_scene.IsValid() && _scene.isLoaded)
                {
                    foreach (var root in _scene.GetRootGameObjects())
                        foreach (var boss in root.GetComponentsInChildren<Boss>(true)) Stop(boss);
                    var operation = SceneManager.UnloadSceneAsync(_scene);
                    await Until(() => operation == null || operation.isDone, "Phase verification scene cleanup timed out.");
                }
                if (_navigation.valid) _navigation.Remove();
                foreach (var owned in _owned) if (owned != null) Object.Destroy(owned);
            }
            finally { Time.timeScale = _timeScale; Application.runInBackground = _background; UnityEngine.Random.state = _random; }
        }
    }
}
