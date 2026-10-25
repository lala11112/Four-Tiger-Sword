// Edit Mode: Unity Pipeline run_script, DungeonEncounterCombatRegression.Run.
// Lifecycle bridge: real catalog prefabs, room/member/gate APIs and Enemy.TakeDamage.
// Edit Mode does not invoke ordinary Awake/Start callbacks. The fixture therefore
// initializes a zero-budget room normally, then supplies explicitly initialized
// prefab members to the same room before testing the actual combat lifecycle.
// Native NavMeshAgent activation is deliberately excluded by restoring its disabled
// fixture state; the separate Play Mode integration check must verify native binding.
using System;
using System.Collections.Generic;
using System.Reflection;
using StudioBaekho;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class DungeonEncounterCombatRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string CatalogPath = "Assets/_Game/ProceduralDungeon/DungeonEnemyCatalog.asset";
    private const string DoorPath = "Assets/LabDoor4x3/Prefabs/35_industrial_sliding_door.prefab";

    private sealed class Fixture
    {
        public DungeonRoomEncounter room;
        public CharacterController player;
        public List<DungeonEncounterMember> members;
        public Collider originallyDisabledCollider;
        public Vector3 origin;
    }

    public static string Run()
    {
        if (Application.isPlaying) throw new Exception("Run this regression in Edit Mode.");
        var originalScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var navigation = new List<NavMeshData>();
        var surfaces = new List<NavMeshSurface>();
        MonsterSelfDestructSO selfDestructData = null;
        var damageText = DamageTextManager.Instance;
        var randomState = UnityEngine.Random.state;
        int checks = 0;
        Action<bool, string> check = (condition, message) =>
        {
            if (!condition) throw new Exception(message);
            checks++;
        };
        try
        {
            SceneManager.SetActiveScene(scene);
            // Enemy.Start must not attach a test clone to a manager from the user's open scene.
            DamageTextManager.Instance = null;
            var catalog = AssetDatabase.LoadAssetAtPath<DungeonEnemyCatalog>(CatalogPath);
            var door = AssetDatabase.LoadAssetAtPath<GameObject>(DoorPath);
            check(catalog != null && door != null, "The dungeon catalog and encounter door assets are required.");
            check(catalog.GetValidatedCosts().Length >= 2, "The default catalog must provide multiple monster species.");

            var combat = BuildFixture(scene, catalog, door, new Vector3(6000, 1000, 6000), 71,
                navigation, surfaces, check);
            int cleared = 0;
            combat.room.Cleared += _ => cleared++;
            var dormant = combat.members[0];
            float hp = dormant.Enemy.EnemyStat.CurrentHp;
            var ignored = dormant.Enemy.TakeDamage(100f, poiseDamage: 0f);
            check(!ignored.Applied && dormant.Enemy.EnemyStat.CurrentHp == hp,
                "A visible dormant enemy received damage before the room was entered.");
            check(!dormant.Enemy.enabled && !dormant.GetComponent<NavMeshAgent>().enabled,
                "Dormant enemy AI/navigation was active.");
            foreach (var collider in dormant.GetComponentsInChildren<Collider>(true))
                check(!collider.enabled, "A dormant enemy remained targetable through a collider.");
            bool visible = false;
            foreach (var renderer in dormant.GetComponentsInChildren<Renderer>(true))
                visible |= renderer.enabled && renderer.gameObject.activeInHierarchy;
            check(visible, "Dormancy hid all of the spawned enemy's visuals.");

            Tick(combat.room); // Observe the capsule outside the northern doorway first.
            check(combat.room.State == DungeonEncounterState.Waiting, "Observing a player outside started the encounter.");
            combat.player.transform.position = combat.origin + new Vector3(0, 0, 17);
            Physics.SyncTransforms();
            Tick(combat.room); // A single swept entry crosses the inward gate plane.
            check(combat.room.State == DungeonEncounterState.Fighting,
                "Crossing the doorway did not start the encounter: " + combat.room.State + " / " + combat.room.Error);
            CheckGates(combat.room, true, check);
            check(!combat.originallyDisabledCollider.enabled, "Activation enabled a collider that the prefab/fixture had disabled.");
            foreach (var member in combat.members)
            {
                check(member.IsAwake && member.Enemy.enabled && !member.GetComponent<NavMeshAgent>().enabled,
                    "A room member's lifecycle bridge did not activate while preserving the disabled Editor fixture agent.");
                StartEnemy(member);
            }
            int initialAlive = combat.room.AliveCount;
            Kill(combat.members[0]);
            check(combat.room.AliveCount == initialAlive - 1 && combat.room.State == DungeonEncounterState.Fighting,
                "One actual enemy death cleared the room or failed to decrement the count.");
            CheckGates(combat.room, true, check);
            for (int i = 1; i < combat.members.Count - 1; i++) Kill(combat.members[i]);
            check(combat.room.AliveCount == 1 && combat.room.State == DungeonEncounterState.Fighting,
                "The self-destruct fixture did not retain exactly its last enemy.");
            var selfDestructEnemy = combat.members[combat.members.Count - 1].Enemy;
            selfDestructData = ScriptableObject.CreateInstance<MonsterSelfDestructSO>();
            var selfDestruct = new EnemySelfDestruct(selfDestructData, selfDestructEnemy);
            int selfDestructNotifications = 0;
            selfDestructEnemy.OnDied += () => selfDestructNotifications++;
            // Establish an active self-destruct completion, bypassing its separate warning/
            // explosion timing so this focused case tests the real Enemy death notification.
            selfDestructEnemy.CurrentAction = selfDestruct;
            typeof(Enemy).GetProperty("IsSelfDestructing").SetValue(selfDestructEnemy, true);
            selfDestructEnemy.CompleteSelfDestruct(selfDestruct);
            check(selfDestructEnemy.EnemyStat.IsDead && selfDestructNotifications == 1,
                "Actual self-destruct completion did not emit exactly one death notification.");
            check(combat.room.AliveCount == 0 && combat.room.State == DungeonEncounterState.Cleared && cleared == 1,
                "The final self-destruct did not clear the encounter exactly once.");
            CheckGates(combat.room, false, check);
            selfDestructEnemy.CompleteSelfDestruct(selfDestruct);
            check(selfDestructNotifications == 1 && cleared == 1 && combat.room.AliveCount == 0,
                "Duplicate self-destruct completion decremented or cleared the room twice.");
            combat.members[0].Enemy.TakeDamage(100f, poiseDamage: 0f);
            check(!combat.room.BeginEncounter() && cleared == 1 && combat.room.AliveCount == 0,
                "A repeated death/re-entry restarted a completed encounter.");
            combat.player.transform.position = combat.origin + new Vector3(0, 0, 23);
            Tick(combat.room);
            combat.player.transform.position = combat.origin + new Vector3(0, 0, 17);
            Tick(combat.room);
            CheckGates(combat.room, false, check);

            var removed = BuildFixture(scene, catalog, door, new Vector3(6080, 1000, 6000), 72,
                navigation, surfaces, check);
            int removedCleared = 0;
            removed.room.Cleared += _ => removedCleared++;
            check(removed.room.BeginEncounter(), "The second encounter could not start.");
            CheckGates(removed.room, true, check);
            foreach (var member in removed.members) StartEnemy(member);
            for (int i = 0; i < removed.members.Count - 1; i++) Kill(removed.members[i]);
            check(removed.room.AliveCount == 1, "Unexpected-removal fixture did not retain its last enemy.");
            Object.DestroyImmediate(removed.members[removed.members.Count - 1].gameObject);
            Tick(removed.room); // Also tests the stale-reference pruning fallback if Edit Mode omitted OnDestroy.
            check(removed.room.AliveCount == 0 && removed.room.State == DungeonEncounterState.Cleared && removedCleared == 1,
                "Destroying the last enemy stranded the room behind locked doors.");
            CheckGates(removed.room, false, check);

            return checks + " encounter lifecycle bridge checks passed: catalog allocation/placement, visible dormancy, swept entry, " +
                "two locked gates, real lethal damage, final self-destruct, duplicate/re-entry protection and unexpected destruction. " +
                "Native NavMeshAgent activation, runtime Awake/Start scheduling and generator completion are excluded here " +
                "and require the separate Play Mode integration check.";
        }
        finally
        {
            // Remove data before closing the temporary scene, regardless of ordinary MonoBehaviour callbacks.
            foreach (var surface in surfaces)
                if (surface != null) { surface.RemoveData(); surface.navMeshData = null; }
            SceneManager.SetActiveScene(originalScene);
            EditorSceneManager.CloseScene(scene, true);
            foreach (var data in navigation) if (data != null) Object.DestroyImmediate(data);
            if (selfDestructData != null) Object.DestroyImmediate(selfDestructData);
            DamageTextManager.Instance = damageText;
            UnityEngine.Random.state = randomState;
        }
    }

    private static Fixture BuildFixture(Scene scene, DungeonEnemyCatalog catalog, GameObject door,
        Vector3 origin, int seed, List<NavMeshData> navigation, List<NavMeshSurface> surfaces,
        Action<bool, string> check)
    {
        var root = new GameObject("Encounter regression room " + seed);
        SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.position = origin;
        var module = root.AddComponent<BaekhoMapModule>();
        module.localBounds = new Bounds(new Vector3(0, 2, 0), new Vector3(40, 4, 40));
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Authored walkable floor";
        floor.transform.SetParent(root.transform, false);
        floor.transform.localPosition = Vector3.down * 0.5f;
        floor.transform.localScale = new Vector3(40, 1, 40);
        module.groundObjects = new[] { floor };
        var marker = new GameObject("Authored enemy marker").AddComponent<BaekhoGameplayMarker>();
        marker.transform.SetParent(root.transform, false);
        marker.id = "regression_enemy";
        marker.kind = "enemy";
        module.connections = new[]
        {
            Socket(root.transform, "north", new Vector3(0, 0, 20), Quaternion.identity),
            Socket(root.transform, "south", new Vector3(0, 0, -20), Quaternion.Euler(0, 180, 0))
        };
        Physics.SyncTransforms();
        check(DungeonEncounterPlacement.TryPrepareNavigation(module, out var data, out var navigationError),
            "Temporary navigation build failed: " + navigationError);
        if (data != null) navigation.Add(data);
        surfaces.Add(root.GetComponent<NavMeshSurface>());

        var directorObject = new GameObject("Encounter regression director " + seed);
        SceneManager.MoveGameObjectToScene(directorObject, scene);
        var director = directorObject.AddComponent<DungeonEncounterDirector>();
        director.GetComponent<BaekhoDungeonGenerator>().generateOnStart = false;
        director.catalog = catalog;
        director.doorPrefab = door;
        director.spawnRadius = 5f;
        var playerObject = new GameObject("Encounter regression player " + seed);
        SceneManager.MoveGameObjectToScene(playerObject, scene);
        playerObject.transform.position = origin + new Vector3(0, 0, 23);
        var player = playerObject.AddComponent<CharacterController>();
        player.center = Vector3.up;
        player.height = 2f;
        player.radius = 0.5f;
        typeof(DungeonEncounterDirector).GetField("player", Hidden).SetValue(director, player);

        var room = root.AddComponent<DungeonRoomEncounter>();
        module.encounterBudget = 0;
        room.Initialize(director, module, new Dictionary<int, float> { { 0, 4f }, { 1, 4f } }, seed);
        check(room.State == DungeonEncounterState.Cleared && room.Gates.Count == 2,
            "A zero-budget room did not initialize its two open gates.");
        CheckGates(room, false, check);
        typeof(DungeonRoomEncounter).GetField("_ownedNavigation", Hidden).SetValue(room, data);

        // Supply production prefab clones after their real EnemyStat.Awake, since Edit Mode
        // cannot execute Awake between Instantiate and Initialize's positive-HP assertion.
        module.encounterBudget = 20;
        var plan = BaekhoEncounterPlanner.Plan(module.encounterBudget, catalog.GetValidatedCosts(), seed);
        var prefabs = new List<GameObject>();
        for (int i = 0; i < plan.Counts.Length; i++)
            for (int n = 0; n < plan.Counts[i]; n++) prefabs.Add(catalog.enemies[i].prefab);
        check(prefabs.Count > 1 && plan.Spent == 20, "The default budget did not produce a full mixed encounter.");
        check(DungeonEncounterPlacement.TryPlan(module, prefabs, seed, director.spawnRadius, out var placements, out var error),
            "Real prefab placement failed: " + error);
        var enemyRoot = new GameObject("EncounterEnemies");
        enemyRoot.transform.SetParent(root.transform, false);
        enemyRoot.SetActive(false);
        typeof(DungeonRoomEncounter).GetField("_enemyRoot", Hidden).SetValue(room, enemyRoot);
        var members = (List<DungeonEncounterMember>)typeof(DungeonRoomEncounter).GetField("_members", Hidden).GetValue(room);
        var alive = (HashSet<DungeonEncounterMember>)typeof(DungeonRoomEncounter).GetField("_alive", Hidden).GetValue(room);
        Collider originallyDisabled = null;
        foreach (var placement in placements)
        {
            var clone = Object.Instantiate(placement.prefab, placement.position, placement.rotation, enemyRoot.transform);
            // UI has its own regressions. Avoid starting a coroutine on an inactive Edit Mode HP bar.
            foreach (var bar in clone.GetComponentsInChildren<EnemyHpBar>(true)) Object.DestroyImmediate(bar.gameObject);
            var enemy = clone.GetComponent<Enemy>();
            enemy.EnemyStat = clone.GetComponent<EnemyStat>();
            typeof(EnemyStat).GetMethod("Awake", Hidden).Invoke(enemy.EnemyStat, null);
            clone.GetComponent<PoiseHandler>().ResetPoise();
            if (originallyDisabled == null)
            {
                var child = new GameObject("Originally disabled collider");
                child.transform.SetParent(clone.transform, false);
                originallyDisabled = child.AddComponent<BoxCollider>();
                originallyDisabled.enabled = false;
            }
            var member = clone.AddComponent<DungeonEncounterMember>();
            member.Initialize(room);
            // NavMeshAgent does not natively bind in this Edit Mode run_script context.
            // Exercise all remaining activation and combat behavior without pretending
            // the Editor fixture verifies the engine's Play Mode agent lifecycle.
            typeof(DungeonEncounterMember).GetField("_agentEnabled", Hidden).SetValue(member, false);
            members.Add(member);
            alive.Add(member);
        }
        enemyRoot.SetActive(true);
        typeof(DungeonRoomEncounter).GetProperty("State").SetValue(room, DungeonEncounterState.Waiting);
        check(room.AliveCount == prefabs.Count && room.AliveCount > 1,
            "The prepared room did not track all monsters around its single marker.");
        return new Fixture { room = room, player = player, members = members, origin = origin,
            originallyDisabledCollider = originallyDisabled };
    }

    private static BaekhoMapConnection Socket(Transform parent, string id, Vector3 position, Quaternion rotation)
    {
        var socket = new GameObject("Socket " + id).AddComponent<BaekhoMapConnection>();
        socket.transform.SetParent(parent, false);
        socket.transform.localPosition = position;
        socket.transform.localRotation = rotation;
        socket.id = id;
        socket.width = 4f;
        socket.height = 3f;
        return socket;
    }

    private static void StartEnemy(DungeonEncounterMember member)
        => typeof(Enemy).GetMethod("Start", Hidden).Invoke(member.Enemy, null);

    private static void Tick(DungeonRoomEncounter room)
        => typeof(DungeonRoomEncounter).GetMethod("Update", Hidden).Invoke(room, null);

    private static void Kill(DungeonEncounterMember member)
    {
        var result = member.Enemy.TakeDamage(member.Enemy.EnemyStat.CurrentHp + 1f, poiseDamage: 0f);
        if (!result.Killed) throw new Exception("Actual lethal Enemy.TakeDamage did not report a kill.");
    }

    private static void CheckGates(DungeonRoomEncounter room, bool locked, Action<bool, string> check)
    {
        check(room.Gates.Count == 2, "The fixture must retain both room exits.");
        foreach (var gate in room.Gates)
        {
            check(gate.IsLocked == locked, locked ? "A room exit remained open during combat." : "A cleared/waiting room exit stayed locked.");
            var blocker = (BoxCollider)typeof(DungeonEncounterGate).GetField("_blocker", Hidden).GetValue(gate);
            var obstacle = (NavMeshObstacle)typeof(DungeonEncounterGate).GetField("_obstacle", Hidden).GetValue(gate);
            check(blocker.enabled == locked && obstacle.enabled == locked,
                "Gate presentation, player collision and navigation did not agree.");
        }
    }
}
