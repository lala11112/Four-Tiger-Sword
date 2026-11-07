#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LabDoor;
using StudioBaekho;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Exercises the real Game Scene additively, then unloads that disposable runtime scene.</summary>
public static class MapRefreshPlayModeValidation
{
    public const string ResultKey = "MapRefresh.PlayModeValidation";
    const string GameScenePath = "Assets/_Project/Scenes/Game Scene.unity";
    static bool running;
    [Serializable] public sealed class Report
    {
        public string status, progress, originalScene;
        public bool passed, originalScenePreserved;
        public int checks, cases, generatedRooms, doors, unusedClosures, corridorSamples, teleports;
        public int enemyAgents, bossActivations, shopCounters, shopInteractions, refreshedSelections, v3Selections;
        public List<string> errors = new List<string>();
    }

    /// <summary>Default four actual builds supplement the forty asset/planner seed checks. Pass ten for twenty builds.</summary>
    public static string Start(int seedsPerKind = 2)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode in the current non-Game Scene before starting this additive validation.");
        if (running) throw new InvalidOperationException("Map refresh Play Mode validation is already running.");
        var existing = SceneManager.GetSceneByPath(GameScenePath);
        if (existing.IsValid() && existing.isLoaded)
            throw new InvalidOperationException("Game Scene is already loaded. This helper owns a disposable additive Game Scene and will not replace an existing run.");
        if (seedsPerKind < 1 || seedsPerKind > 10) throw new ArgumentOutOfRangeException(nameof(seedsPerKind));
        running = true;
        var report = new Report { status = "running", progress = "Loading Game Scene additively", originalScene = SceneManager.GetActiveScene().path };
        Save(report);
        _ = Run(report, seedsPerKind);
        return "Started additive Game Scene validation; poll " + ResultKey + ".";
    }

    public static string Status() => SessionState.GetString(ResultKey, "Not started");

    static async Task Run(Report report, int seedsPerKind)
    {
        var original = SceneManager.GetActiveScene();
        Scene game = default;
        bool added = false;
        var temporary = new List<BaekhoDungeonSettings>();
        RunManager run = null;
        try
        {
            var load = SceneManager.LoadSceneAsync(GameScenePath, LoadSceneMode.Additive);
            Check(report, load != null, "Could not start loading the Game Scene.");
            await Wait(() => load.isDone, "Game Scene did not finish loading.", 180);
            game = SceneManager.GetSceneByPath(GameScenePath);
            Check(report, game.IsValid() && game.isLoaded, "Game Scene is not loaded.");
            added = true;
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            run = game.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<RunManager>(true)).SingleOrDefault();
            Check(report, run && run.player && run.stageManager, "Additive Game Scene has no configured run/player/stage.");
            await Wait(() => run.State == RunState.Playing || run.State == RunState.Error, "Initial real run did not become ready.", 240);
            Check(report, run.State == RunState.Playing, "Initial real run failed: " + run.LastError);
            var player = run.player;
            var controller = player.Controller;
            Check(report, controller && run.stageManager.Encounters && run.stageManager.Generator, "The actual run services are incomplete.");
            // Keep the player out of all room entry volumes while checking generated geometry.
            player.enabled = false;
            controller.enabled = false;
            player.transform.position = new Vector3(200000, 1000, 200000);
            run.enabled = false;
            var manifest = MapRefreshValidation.ReadManifest();
            var refreshed = new HashSet<string>(manifest.entries.Where(e => e.role == "normal").Select(e => e.prefabPath), StringComparer.Ordinal);
            var v3 = new HashSet<string>(JsonUtility.FromJson<RoomSetV3.SetReport>(System.IO.File.ReadAllText(RoomSetV3.Root + "/Manifests/RoomSet_manifest.json"))
                .rooms.Select(r => r.prefab), StringComparer.Ordinal);
            var stages = run.definition.chapters.SelectMany(c => c.stages).ToArray();
            var boss = stages.FirstOrDefault(s => s.bossPrefab && s.dungeonSettings && s.dungeonSettings.bossRoom);
            Check(report, boss != null, "The actual run has no configured boss stage.");
            // A designer may temporarily run only a boss chapter. Test the persisted normal-stage
            // settings without changing that run definition or its current stage selection.
            var normal = stages.FirstOrDefault(s => s.dungeonSettings && !s.dungeonSettings.bossRoom && !s.bossPrefab)
                ?? new StageDefinition { displayName = "Normal-stage validation",
                    dungeonSettings = AssetDatabase.LoadAssetAtPath<BaekhoDungeonSettings>("Assets/_Game/RunProgression/Settings/NormalStage.asset"),
                    enemyCatalog = boss.enemyCatalog, mapCountVariation = 0 };
            foreach (bool bossStage in new[] { false, true })
            {
                var template = bossStage ? boss : normal;
                for (int seed = 0; seed < seedsPerKind; seed++)
                {
                    report.progress = (bossStage ? "Boss" : "Normal") + " seed " + seed;
                    Save(report);
                    var clone = Object.Instantiate(template.dungeonSettings);
                    clone.hideFlags = HideFlags.DontSave;
                    clone.randomizeSeed = false;
                    clone.seed = seed;
                    temporary.Add(clone);
                    var definition = new StageDefinition { displayName = "Map refresh runtime validation", dungeonSettings = clone,
                        enemyCatalog = template.enemyCatalog, bossPrefab = bossStage ? template.bossPrefab : null, mapCountVariation = 0 };
                    Check(report, definition.Validate(bossStage, out var error), "Invalid disposable stage definition: " + error);
                    var stage = run.stageManager;
                    stage.StartCoroutine(stage.Prepare(definition, bossStage, seed, seed, player));
                    await Wait(() => stage.IsReady || !string.IsNullOrEmpty(stage.Error), report.progress + " did not complete.", 180);
                    Check(report, stage.IsReady && string.IsNullOrEmpty(stage.Error), report.progress + ": " + stage.Error);
                    await Frames(3);
                    CheckCase(report, stage, bossStage, controller, refreshed, v3);
                    CheckShopInteraction(report, run, stage);
                    if (bossStage) await CheckBossActivation(report, stage);
                    stage.Suspend();
                    report.cases++;
                    Save(report);
                }
            }
            Check(report, report.refreshedSelections > 0 && report.v3Selections > 0,
                "Actual generated stages did not exercise both refreshed and original V3 room families; rerun with more seeds.");
            report.passed = true;
            report.status = "passed";
        }
        catch (Exception exception)
        {
            report.passed = false;
            report.status = "failed";
            report.errors.Add(report.progress + ": " + exception.Message);
        }
        finally
        {
            try
            {
                if (Application.isPlaying)
                {
                    if (run && run.stageManager) run.stageManager.Cancel();
                    if (added && game.IsValid() && game.isLoaded)
                    {
                        var unload = SceneManager.UnloadSceneAsync(game);
                        if (unload != null) await Wait(() => unload.isDone, "Disposable Game Scene did not unload.", 90);
                    }
                    foreach (var settings in temporary) if (settings) Object.Destroy(settings);
                    if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                    report.originalScenePreserved = original.IsValid() && original.isLoaded && SceneManager.GetActiveScene() == original;
                    if (!report.originalScenePreserved)
                    {
                        report.passed = false; report.status = "failed";
                        report.errors.Add("The original UI/demo scene was not preserved as active.");
                    }
                }
            }
            catch (Exception exception)
            {
                report.passed = false; report.status = "failed";
                report.errors.Add("Cleanup: " + exception.Message);
            }
            report.progress = "Finished";
            running = false;
            Save(report);
        }
    }

    static void CheckCase(Report report, StageManager stage, bool bossStage, CharacterController controller,
        HashSet<string> refreshed, HashSet<string> v3)
    {
        var result = stage.Result;
        var director = stage.Encounters;
        Check(report, result != null && result.success && result.root && result.root.activeInHierarchy && director.IsReady && string.IsNullOrEmpty(director.Error),
            "Actual generator/encounter services did not finish successfully: " + director.Error);
        Check(report, result.rooms.Count == result.plan.rooms.Count && result.rooms.Count == stage.Generator.settings.mapCount,
            "Runtime room count differs from the configured complete layout.");
        report.generatedRooms += result.rooms.Count;
        report.refreshedSelections += result.plan.rooms.Count(r => refreshed.Contains(AssetDatabase.GetAssetPath(r.prefab)));
        report.v3Selections += result.plan.rooms.Count(r => v3.Contains(AssetDatabase.GetAssetPath(r.prefab)));
        foreach (var room in director.Rooms)
        {
            Check(report, room && room.State != DungeonEncounterState.Failed, "An actual encounter failed: " + (room ? room.Error : "missing room"));
            foreach (var member in room.Members)
            {
                Check(report, member && member.CanActivate(out _), "An actual enemy spawn cannot join its room NavMesh.");
                var agent = member.GetComponent<NavMeshAgent>();
                if (member.IsAwake) Check(report, agent && agent.enabled && agent.isOnNavMesh, "A patrolling enemy is not on its native NavMesh.");
                report.enemyAgents++;
            }
        }
        foreach (var module in result.rooms)
        {
            var anchors = module.GetComponent<StageMapAnchors>();
            Check(report, anchors && anchors.teleportPoint && DungeonFastTravelLanding.TryResolve(module, anchors.teleportPoint, controller, out _, out _, out _),
                "A generated room has no safe full-player teleport landing: " + module.name);
            report.teleports++;
        }
        Physics.SyncTransforms();
        CheckDoors(report, result, controller, bossStage);
        CheckCorridors(report, result, controller);
        var shop = stage.GetComponent<StageShopService>();
        Check(report, shop && result.shopRoom && shop.ShopPoint && shop.CurrentStock != null
            && shop.ShopPoint.IsChildOf(result.shopRoom.transform), "Actual shop service did not prepare its stock and authored counter point.");
        var counter = shop.ShopPoint.Cast<Transform>().FirstOrDefault(t => t.Find(MapRefreshBuilder.AppearanceName));
        Check(report, counter && counter.GetComponentsInChildren<Renderer>(true).Any(r => r.enabled && r.gameObject.activeInHierarchy)
            && counter.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && !c.isTrigger), "Generated shop has no visible refreshed counter with its original collision.");
        report.shopCounters++;
        Check(report, (result.bossRoom != null) == bossStage, "Actual boss-room assignment does not match the tested stage kind.");
    }

    static void CheckDoors(Report report, BaekhoDungeonResult result, CharacterController player, bool bossStage)
    {
        var rooms = result.rooms.ToDictionary(r => r.GetComponent<BaekhoGeneratedRoom>().instanceId);
        var used = rooms.Keys.ToDictionary(id => id, id => new HashSet<int>());
        foreach (var link in result.plan.links)
        {
            used[link.fromMapId].Add(link.fromSocketIndex);
            used[link.toMapId].Add(link.toSocketIndex);
        }
        foreach (var pair in rooms)
        {
            var module = pair.Value;
            var encounter = module.GetComponent<DungeonRoomEncounter>();
            Check(report, encounter && encounter.Gates.Count == used[pair.Key].Count, "Connected runtime door/gate count is incomplete: " + module.name);
            for (int index = 0; index < module.connections.Length; index++)
            {
                var socket = module.connections[index];
                if (!used[pair.Key].Contains(index))
                {
                    Check(report, !ClearCapsule(socket.transform.position, player, out _), "An unused doorway has no physical closing wall: " + module.name + "/" + socket.id);
                    report.unusedClosures++;
                    continue;
                }
                var gate = encounter.Gates.FirstOrDefault(g => Vector3.Distance(g.transform.position, socket.transform.position) < .02f);
                Check(report, gate && gate.DoorRoot, "Connected exit has no native encounter door: " + module.name + "/" + socket.id);
                bool bossExit = bossStage && module == result.bossRoom && result.plan.links.Any(link =>
                    (link.fromMapId == pair.Key && link.fromSocketIndex == index && link.toMapId == result.plan.endMapId)
                    || (link.toMapId == pair.Key && link.toSocketIndex == index && link.fromMapId == result.plan.endMapId));
                Check(report, gate.IsLocked == bossExit, "Unexpected initial encounter-door lock: " + module.name + "/" + socket.id);
                if (bossExit)
                {
                    Check(report, gate.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && !c.isTrigger), "Boss exit lock has no active physical blocker.");
                }
                else
                {
                    var door = gate.DoorRoot.GetComponentInChildren<SlidingDoor>(true);
                    Check(report, door && door.OpenAmount >= .99f, "Open native doorway leaves did not retract.");
                    foreach (float offset in new[] { -1f, -.5f, 0f, .5f, 1f })
                    {
                        var floor = socket.transform.position + socket.transform.forward * offset;
                        Check(report, ClearCapsule(floor, player, out var blocker), "Connected doorway blocks the real player capsule: " + module.name + "/" + socket.id + " by " + blocker);
                    }
                }
                report.doors++;
            }
        }
    }

    static void CheckCorridors(Report report, BaekhoDungeonResult result, CharacterController player)
    {
        var rooms = result.rooms.ToDictionary(room => room.GetComponent<BaekhoGeneratedRoom>().instanceId);
        var scale = player.transform.lossyScale;
        float radius = player.radius * Mathf.Max(scale.x, scale.z);
        float height = Mathf.Max(player.height * scale.y, radius * 2f);
        foreach (var link in result.plan.links)
        {
            var visual = result.root.transform.Find("Connection_" + link.fromMapId + "_" + link.toMapId);
            Check(report, visual && visual.GetComponentsInChildren<BaekhoWallAppearanceInstance>(true).Length > 0, "An actual corridor has no V3 wall appearance.");
            var from = rooms[link.fromMapId];
            var to = rooms[link.toMapId];
            var fromSocket = from.connections[link.fromSocketIndex];
            var toSocket = to.connections[link.toSocketIndex];
            var fromGate = GateAt(from, fromSocket);
            var toGate = GateAt(to, toSocket);
            for (int index = 0; index < link.segments.Count; index++)
            {
                var segment = link.segments[index];
                var direction = (segment.end - segment.start).normalized;
                var right = Vector3.Cross(Vector3.up, direction);
                foreach (float fraction in new[] { .25f, .5f, .75f })
                {
                    var center = Vector3.Lerp(segment.start, segment.end, fraction);
                    var worldCenter = result.root.transform.TransformPoint(center);
                    float samplingWidth = link.width;
                    if (index == 0)
                        samplingWidth = ApertureSamplingWidth(samplingWidth, worldCenter, fromSocket, fromGate, radius, height);
                    if (index == link.segments.Count - 1)
                        samplingWidth = ApertureSamplingWidth(samplingWidth, worldCenter, toSocket, toGate, radius, height);
                    foreach (float side in new[] { -.25f, 0f, .25f })
                    {
                        float offset = samplingWidth * side;
                        if (samplingWidth < link.width)
                        {
                            float available = Mathf.Max(0f, samplingWidth * .5f - radius - .01f);
                            offset = Mathf.Clamp(offset, -available, available);
                        }
                        var floor = result.root.transform.TransformPoint(center + right * offset);
                        Check(report, ClearCapsule(floor, player, out var blocker),
                            $"Corridor {link.fromMapId}->{link.toMapId}, segment {segment.start}->{segment.end}, fraction {fraction}, side {side}, sampling width {samplingWidth:F2}, floor {floor}: player capsule blocked by {blocker}");
                        Check(report, Physics.Raycast(floor + Vector3.up * .4f, Vector3.down, out var hit, .8f, ~0, QueryTriggerInteraction.Ignore)
                            && hit.collider.transform.IsChildOf(result.root.transform), "An actual corridor sample has no supporting floor.");
                        report.corridorSamples++;
                    }
                }
            }
        }
    }

    static DungeonEncounterGate GateAt(BaekhoMapModule room, BaekhoMapConnection socket)
    {
        var encounter = room.GetComponent<DungeonRoomEncounter>();
        return encounter ? encounter.Gates.FirstOrDefault(gate => gate && Vector3.Distance(gate.transform.position, socket.transform.position) < .02f) : null;
    }

    static float ApertureSamplingWidth(float width, Vector3 floor, BaekhoMapConnection socket,
        DungeonEncounterGate gate, float capsuleRadius, float capsuleHeight)
    {
        if (!gate || !gate.DoorRoot) return width;
        var forward = socket.transform.forward;
        var lateral = socket.transform.right;
        var depthAxis = new Vector3(Mathf.Abs(forward.x), Mathf.Abs(forward.y), Mathf.Abs(forward.z));
        var lateralAxis = new Vector3(Mathf.Abs(lateral.x), Mathf.Abs(lateral.y), Mathf.Abs(lateral.z));
        // Near a room socket, a native jamb can extend into the first or last
        // straight segment. Restrict the side probes only where the actual door
        // solids overlap the capsule's depth and height. The center stays tested.
        foreach (var collider in gate.DoorRoot.GetComponentsInChildren<Collider>(true))
        {
            if (!collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy) continue;
            var bounds = collider.bounds;
            if (bounds.max.y <= floor.y + .2f || bounds.min.y >= floor.y + capsuleHeight + .08f) continue;
            var delta = bounds.center - floor;
            float depth = Mathf.Abs(Vector3.Dot(delta, forward));
            if (depth > Vector3.Dot(bounds.extents, depthAxis) + capsuleRadius) continue;
            width = Mathf.Min(width, socket.width);
            float innerEdge = Mathf.Abs(Vector3.Dot(delta, lateral)) - Vector3.Dot(bounds.extents, lateralAxis);
            if (innerEdge > 0f) width = Mathf.Min(width, innerEdge * 2f);
        }
        return width;
    }

    static async Task CheckBossActivation(Report report, StageManager stage)
    {
        var encounter = stage.Result.bossRoom.GetComponent<DungeonRoomEncounter>();
        Check(report, encounter && encounter.State == DungeonEncounterState.Waiting && encounter.Members.Count == 1, "Actual boss service did not prepare one dormant boss.");
        Check(report, encounter.BeginEncounter(), "Actual boss encounter could not begin.");
        Check(report, encounter.Gates.Count > 0, "The active boss encounter has no gates.");
        foreach (var gate in encounter.Gates)
        {
            var lockCollider = gate.GetComponentsInChildren<BoxCollider>(true).SingleOrDefault(collider =>
                collider.transform.parent == gate.transform && collider.name.StartsWith("EncounterLock_", StringComparison.Ordinal));
            Check(report, gate.IsLocked && lockCollider && lockCollider.enabled && !lockCollider.isTrigger && lockCollider.gameObject.activeInHierarchy,
                "Starting the boss encounter did not engage its physical door lock: " + gate.name);
        }
        var member = encounter.Members[0];
        var agent = member.GetComponent<NavMeshAgent>();
        Check(report, member.IsAwake && member.Enemy.enabled && agent && agent.enabled && agent.isOnNavMesh,
            "Boss activation failed to join the native room NavMesh.");
        await Wait(() => encounter.Gates.All(g => g.DoorRoot.GetComponentInChildren<SlidingDoor>(true).OpenAmount <= .01f),
            "Boss encounter door leaves did not finish closing.", 10f);
        report.bossActivations++;
        stage.Suspend();
    }

    static void CheckShopInteraction(Report report, RunManager run, StageManager stage)
    {
        var shop = stage.GetComponent<StageShopService>();
        var player = run.player;
        var controller = player.Controller;
        Check(report, shop && shop.ShopPoint && shop.settings, "The generated shop is unavailable for an interaction check.");
        var parkedPosition = player.transform.position;
        var parkedRotation = player.transform.rotation;
        bool runEnabled = run.enabled, playerEnabled = player.enabled, controllerEnabled = controller.enabled;
        float verticalVelocity = player.VerticalVelocity;
        float worldScale = Time.timeScale;
        try
        {
            // Exercise the same public service entry point as the F interaction, with the actual player capsule.
            controller.enabled = false;
            var rotation = shop.ShopPoint.rotation;
            var floor = shop.ShopPoint.position + shop.ShopPoint.forward * Mathf.Min(1.75f, shop.settings.interactionDistance * .65f);
            float halfHeight = controller.height * Mathf.Abs(player.transform.lossyScale.y) * .5f;
            var center = floor + Vector3.up * (halfHeight + controller.skinWidth + .05f);
            player.transform.SetPositionAndRotation(center - rotation * Vector3.Scale(controller.center, player.transform.lossyScale), rotation);
            player.VerticalVelocity = 0f;
            stage.Encounters.ResetPlayerMotionHistory();
            run.enabled = true;
            player.enabled = true;
            controller.enabled = true;
            Physics.SyncTransforms();
            Check(report, shop.IsInRange && !player.IsStageTransitioning, "Moving the real player near the generated counter did not enable shop interaction.");
            Check(report, shop.TryOpen(), "The generated shop service rejected its actual nearby-player interaction.");
            Check(report, run.IsShopOpen && run.State == RunState.Playing && player.IsShopPaused && Mathf.Approximately(Time.timeScale, 0f),
                "Opening the generated shop did not establish the real modal and world pause.");
            run.CloseShop();
            Check(report, !run.IsShopOpen && !player.IsShopPaused && Mathf.Approximately(Time.timeScale, worldScale),
                "Closing the generated shop did not release its modal/world pause.");
            report.shopInteractions++;
        }
        finally
        {
            if (run && run.IsShopOpen) run.CloseShop();
            controller.enabled = false;
            player.transform.SetPositionAndRotation(parkedPosition, parkedRotation);
            player.VerticalVelocity = verticalVelocity;
            stage.Encounters.ResetPlayerMotionHistory();
            controller.enabled = controllerEnabled;
            player.enabled = playerEnabled;
            run.enabled = runEnabled;
            Physics.SyncTransforms();
        }
    }

    static bool ClearCapsule(Vector3 floor, CharacterController controller, out string blocker)
    {
        blocker = null;
        var scale = controller.transform.lossyScale;
        float radius = controller.radius * Mathf.Max(scale.x, scale.z);
        float height = Mathf.Max(controller.height * scale.y, radius * 2);
        foreach (var collider in Physics.OverlapCapsule(floor + Vector3.up * (radius + .08f), floor + Vector3.up * (height - radius + .08f), radius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (collider.transform == controller.transform || collider.transform.IsChildOf(controller.transform)
                || collider.GetComponentInParent<Enemy>() || collider.bounds.max.y <= floor.y + .2f) continue;
            blocker = collider.name + " at " + collider.bounds.center + " size " + collider.bounds.size
                + " in " + (collider.GetComponentInParent<BaekhoMapModule>() ? collider.GetComponentInParent<BaekhoMapModule>().name : collider.transform.root.name);
            return false;
        }
        return true;
    }

    static async Task Wait(Func<bool> condition, string error, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition())
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Play Mode ended before validation finished.");
            if (Time.realtimeSinceStartup >= deadline) throw new TimeoutException(error);
            await Task.Delay(10);
        }
    }
    static async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) { int frame = Time.frameCount; await Wait(() => Time.frameCount > frame, "Player loop stopped advancing.", 20); }
    }
    static void Save(Report report) => SessionState.SetString(ResultKey, JsonUtility.ToJson(report, true));
    static void Check(Report report, bool passed, string error)
    {
        report.checks++;
        if (!passed) throw new InvalidOperationException(error);
    }
}
#endif
