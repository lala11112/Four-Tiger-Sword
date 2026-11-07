using System;
using System.Collections.Generic;
using System.Reflection;
using StudioBaekho;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Authoritative travel eligibility and click-time checks in an isolated physics scene.</summary>
public static class DungeonFastTravelServiceRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;

    [MenuItem("Tools/Studio Baekho/Dungeon/Run Fast Travel Service Regression")]
    private static void RunMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run travel service fixtures in Edit Mode.");
        _checks = 0;
        float savedScale = Time.timeScale;
        int frameRate = Application.targetFrameRate;
        bool cursorVisible = Cursor.visible;
        CursorLockMode cursorLock = Cursor.lockState;
        var services = new HashSet<CombatHitStopService>(Resources.FindObjectsOfTypeAll<CombatHitStopService>());
        var instanceField = typeof(CombatHitStopService).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
        object savedInstance = instanceField.GetValue(null);
        var scene = EditorSceneManager.NewPreviewScene();
        RunManager run = null;
        ItemCatalog catalog = null;
        ItemDefinition item = null;
        GameObject Host(string name, Transform parent = null)
        {
            var host = new GameObject(name);
            SceneManager.MoveGameObjectToScene(host, scene);
            if (parent != null) host.transform.SetParent(parent, false);
            return host;
        }
        try
        {
            instanceField.SetValue(null, null);
            Time.timeScale = 1f;
            var host = Host("Stage fixture");
            var stage = host.AddComponent<StageManager>();
            var generator = Host("Generator fixture").AddComponent<BaekhoDungeonGenerator>();
            var director = generator.gameObject.AddComponent<DungeonEncounterDirector>();
            stage.Configure(generator, director);
            var root = Host("Current dungeon");
            root.transform.SetPositionAndRotation(new Vector3(30f, 5f, -45f), Quaternion.Euler(0f, 90f, 0f));
            var player = Host("Travel player").AddComponent<PlayerController>();
            var controller = player.GetComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = .3f;
            controller.center = Vector3.up;
            var input = player.GetComponent<PlayerInputHandler>();
            var stats = player.GetComponent<PlayerStatManager>();
            Property(player, "Input", input);
            Property(player, "Controller", controller);
            Property(player, "StatManager", stats);
            var machine = new StateMachine();
            // The already-entered idle state needs no Animator asset to exercise the actual warp path.
            Property(machine, "CurrentState", new PlayerIdleState(player));
            Property(player, "StateMachine", machine);
            Set(player, "_encounterDirector", director);
            var result = new BaekhoDungeonResult { success = true, root = root,
                plan = new BaekhoDungeonPlan { startMapId = 1, endMapId = 2 } };
            var encounters = Field<List<DungeonRoomEncounter>>(director, "_rooms");
            for (int i = 0; i < 4; i++)
            {
                // Room 3 lies between the source and visited destination, but has never been visited.
                float x = i == 1 ? 80f : i == 2 ? 40f : i * 40f;
                var room = Host("Room_" + (i + 1), root.transform).AddComponent<BaekhoMapModule>();
                room.transform.localPosition = new Vector3(x, 0f, 0f);
                room.transform.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
                room.localBounds = new Bounds(new Vector3(0f, 2f, 0f), new Vector3(20f, 8f, 20f));
                var identity = room.gameObject.AddComponent<BaekhoGeneratedRoom>();
                identity.instanceId = i + 1;
                var floor = Host("OwnFloor", room.transform).AddComponent<BoxCollider>();
                floor.transform.localPosition = new Vector3(0f, -.25f, 0f);
                floor.size = new Vector3(18f, .5f, 18f);
                room.groundObjects = new[] { floor.gameObject };
                var point = Host("TeleportPoint", room.transform).transform;
                point.SetPositionAndRotation(DungeonFastTravelLanding.PlayerRootAtFloor(room.transform.position,
                    room.transform.rotation, controller), room.transform.rotation);
                room.gameObject.AddComponent<StageMapAnchors>().teleportPoint = point;
                var encounter = room.gameObject.AddComponent<DungeonRoomEncounter>();
                Property(encounter, "State", DungeonEncounterState.Cleared);
                Property(encounter, "WasEntered", true);
                Set(encounter, "_director", director);
                Set(encounter, "_hasPrevious", true);
                encounters.Add(encounter);
                result.rooms.Add(room);
                result.plan.rooms.Add(new BaekhoPlacedModule { id = i + 1, prefab = room,
                    position = new Vector3(x, 0f, 0f), quarterTurns = i });
            }
            result.startRoom = result.rooms[0];
            result.endRoom = result.rooms[1];
            Property(generator, "CurrentRoot", root);
            Property(generator, "LastResult", result);
            Property(stage, "Result", result);
            Property(stage, "IsReady", true);
            Property(director, "IsReady", true);
            Set(director, "_generator", generator);
            Set(director, "_root", root);
            Set(director, "_boundPlayer", player);
            Set(director, "player", controller);
            run = Host("Run fixture").AddComponent<RunManager>();
            run.stageManager = stage;
            run.player = player;
            Property(run, "State", RunState.Playing);
            Property(run, "CurrentChapterIndex", 0);
            Property(run, "CurrentStageIndex", 0);
            var owner = Host("Map owner");
            var sourcePoint = result.startRoom.GetComponent<StageMapAnchors>().teleportPoint;
            player.transform.SetPositionAndRotation(sourcePoint.position, sourcePoint.rotation);
            Physics.SyncTransforms();
            var model = new DungeonMinimapModel();
            model.Reset(result.plan);
            model.UpdatePosition(new Vector2(80f, 0f), false);
            model.UpdatePosition(Vector2.zero, false);
            var service = new DungeonFastTravelService(run, model);
            service.Bind(result);
            Check(run.TryOpenMap(owner), "Healthy fixture could not acquire map ownership.");
            Check(service.CanTravel(2, out string reason), "Visited cleared destination was rejected: " + reason);

            void Reject(int id, string label, BaekhoDungeonResult expected = null)
            {
                Vector3 position = player.transform.position;
                Quaternion rotation = player.transform.rotation;
                float scale = Time.timeScale;
                Check(!service.TryTravel(id, expected ?? result, out string rejected) && !string.IsNullOrEmpty(rejected),
                    label + " was accepted or returned no reason.");
                Check(Vector3.Distance(position, player.transform.position) < .0001f
                    && Quaternion.Angle(rotation, player.transform.rotation) < .001f && controller.enabled
                    && run.IsMapOpen && player.IsMapPaused && Mathf.Approximately(Time.timeScale, scale),
                    label + " partially moved the player or changed modal ownership.");
            }
            Reject(1, "Current-room click");
            Reject(4, "Unvisited cleared room");
            Reject(99, "Unknown room ID");
            Property(encounters[1], "State", DungeonEncounterState.Waiting);
            Reject(2, "Uncleared destination after hover");
            Property(encounters[1], "State", DungeonEncounterState.Cleared);
            var member = Host("Combat fixture member", result.startRoom.transform).AddComponent<DungeonEncounterMember>();
            var alive = Field<HashSet<DungeonEncounterMember>>(encounters[0], "_alive");
            alive.Add(member);
            Property(encounters[0], "State", DungeonEncounterState.Fighting);
            Check(player.IsInRoomCombat, "Combat fixture did not reach the real player query.");
            Reject(2, "Combat beginning after hover");
            alive.Clear();
            Property(encounters[0], "State", DungeonEncounterState.Cleared);
            Reject(2, "Stale clicked result", new BaekhoDungeonResult { success = true, root = root });
            Property(generator, "LastResult", new BaekhoDungeonResult { success = true, root = root });
            Reject(2, "Stale generator LastResult");
            Property(generator, "LastResult", result);
            Property(generator, "IsGenerating", true);
            Reject(2, "Generation starting after hover");
            Property(generator, "IsGenerating", false);
            Property(run, "CurrentStageIndex", 1);
            Reject(2, "Stage index changing after hover");
            Property(run, "CurrentStageIndex", 0);
            owner.SetActive(false);
            Reject(2, "Same-frame map owner deactivation");
            owner.SetActive(true);
            var lease = Field<CombatHitStopService.WorldScaleLease>(run, "_mapWorldPause");
            lease.Dispose();
            Reject(2, "Same-frame expired map lease");
            run.CloseMap();
            Check(run.TryOpenMap(owner), "Map could not reopen after lease invalidation.");
            var anchor = result.endRoom.GetComponent<StageMapAnchors>().teleportPoint;
            Check(service.CanTravel(2, out reason), "Destination did not recover after restoring eligibility: " + reason);
            var wall = Host("New blocking obstacle", result.endRoom.transform).AddComponent<BoxCollider>();
            wall.transform.localPosition = Vector3.up;
            wall.size = Vector3.one;
            Physics.SyncTransforms();
            Reject(2, "Landing obstruction appearing after hover");
            wall.enabled = false;
            Physics.SyncTransforms();
            Check(service.CanTravel(2, out reason), "Destination did not recover after removing the obstacle: " + reason);

            stats.TakeDamage(7f, ElementType.ELEMENT_NONE, false);
            stats.TryConsumeSp(11f);
            stats.AddUltimateGauge(32f);
            var wallet = player.gameObject.AddComponent<PlayerWallet>();
            wallet.Initialize(57);
            item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.projectId = "fixture"; item.candidateKey = "fixture-item"; item.itemId = "fixture-item";
            catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            catalog.projectId = "fixture"; catalog.items = new[] { item };
            var items = player.gameObject.AddComponent<PlayerItemRuntime>();
            items.Catalog = catalog;
            items.Initialize(stats, () => ElementType.ELEMENT_FIRE);
            Property(player, "ItemRuntime", items);
            Check(items.TryAddItem(item.itemId), "Fixture could not own its sample item.");
            float hp = stats.CurrentHp, sp = stats.CurrentSp, gauge = stats.CurrentUltimateGauge;
            Set(player, "_dashCooldownTimer", .37f);
            Set(player, "_formChangeCooldownTimer", .82f);
            player.VerticalVelocity = -9f;
            Property(player, "PendingHitReaction", PlayerHitReaction.Knockback);
            Set(player, "_pendingHitVelocity", Vector3.one * 4f);
            input.AttackBuffer.Set(); input.DashBuffer.Set(); input.SkillBuffer.Set();
            var effect = Host("Retained world effect", root.transform).AddComponent<StageTransientObject>();
            Check(service.TryTravel(2, result, out reason), "Valid click failed its actual warp: " + reason);
            Check(Vector3.Distance(player.transform.position, anchor.position) < .0001f
                && Quaternion.Angle(player.transform.rotation, anchor.rotation) < .001f && controller.enabled,
                "Success changed the authored pose or left the collider disabled.");
            Check(stats.CurrentHp == hp && stats.CurrentSp == sp && stats.CurrentUltimateGauge == gauge && wallet.Gold == 57
                && player.ItemRuntime == items && items.Contains(item.itemId) && effect != null && effect.gameObject.activeInHierarchy,
                "Success reset player resources, ownership or world effects.");
            Check(Mathf.Approximately(Field<float>(player, "_dashCooldownTimer"), .37f)
                && Mathf.Approximately(Field<float>(player, "_formChangeCooldownTimer"), .82f), "Success reset cooldowns.");
            Check(player.VerticalVelocity == 0f && player.PendingHitReaction == PlayerHitReaction.None
                && !input.AttackBuffer.IsActive && !input.DashBuffer.IsActive && !input.SkillBuffer.IsActive,
                "Success retained stale movement/hit reactions or attack input.");
            Check(model.CurrentAreaIndex == model.GetRoomAreaIndex(2)
                && !model.Areas[model.GetRoomAreaIndex(3)].Visited && !model.Areas[model.GetRoomAreaIndex(4)].Visited,
                "Warp swept exploration through intervening or unrelated rooms.");
            foreach (var room in encounters) Check(!Field<bool>(room, "_hasPrevious"), "Warp retained encounter entrance movement history.");
            Check(run.IsMapSessionCurrent && player.IsMapPaused && Mathf.Approximately(Time.timeScale, 0f),
                "The service released map pause before the presenter refreshed the destination.");
            run.CloseMap();
            Check(!run.IsMapOpen && !player.IsMapPaused && Mathf.Approximately(Time.timeScale, 1f),
                "Normal close after a successful warp left modal ownership behind.");
            return $"Fast-travel service: {_checks} checks passed (visited/cleared/current/unvisited, combat, stale contexts, "
                + "owner/lease invalidation, changed obstacles, exact warp, resources/cooldowns/items/effects and history reset).";
        }
        finally
        {
            if (run != null) run.CloseMap();
            EditorSceneManager.ClosePreviewScene(scene);
            if (catalog != null) Object.DestroyImmediate(catalog);
            if (item != null) Object.DestroyImmediate(item);
            foreach (var service in Resources.FindObjectsOfTypeAll<CombatHitStopService>())
                if (!services.Contains(service)) Object.DestroyImmediate(service.gameObject);
            instanceField.SetValue(null, savedInstance);
            Time.timeScale = savedScale;
            Application.targetFrameRate = frameRate;
            Cursor.visible = cursorVisible;
            Cursor.lockState = cursorLock;
        }
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Hidden).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
    private static void Property(object target, string name, object value)
        => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public).SetValue(target, value);
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException("Travel service: " + message); }
}
