using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using StudioBaekho;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Isolated lifecycle regressions; native boss Awake/agent binding also needs Play Mode coverage.</summary>
public static class DungeonStageEncounterRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string DoorPath = "Assets/LabDoor4x3/Prefabs/35_industrial_sliding_door.prefab";
    private static int _checks;

    [MenuItem("Tools/Studio Baekho/Dungeon/Run Stage Encounter Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run stage encounter regression in Edit Mode.");
        _checks = 0;
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        bool logging = Debug.unityLogger.logEnabled;
        try
        {
            SceneManager.SetActiveScene(scene);
            // Failed rooms are expected outcomes, not errors in the user's open scene.
            Debug.unityLogger.logEnabled = false;
            PreparationReportsMissingBoss();
            MissingBossAnchorFails();
            ConfirmedDeathAndDownstreamGate();
            UnexpectedBossRemoval(false);
            UnexpectedBossRemoval(true);
            UnconfirmedZeroHpDoesNotDefeat();
            SuspensionDoesNotClear();
            FirstWaveActivatesWithoutPlayerPosition(false);
            FirstWaveActivatesWithoutPlayerPosition(true);
            EmptyEndRoomTracksArrival();
            return $"Stage encounters: {_checks} checks passed (preparation errors, missing anchors, downstream lock, " +
                "actual lethal death, destroyed/deactivated boss rejection, unconfirmed HP, suspension, first-wave activation and combat group membership, and empty-room arrival). " +
                "Native fixed spawn and NavMeshAgent binding require Play Mode verification.";
        }
        finally
        {
            try
            {
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
            finally { Debug.unityLogger.logEnabled = logging; }
        }
    }

    private static void PreparationReportsMissingBoss()
    {
        var director = Director("Missing boss");
        var module = new GameObject("Boss without content").AddComponent<BaekhoMapModule>();
        var identity = module.gameObject.AddComponent<BaekhoGeneratedRoom>();
        identity.instanceId = 7;
        identity.role = BaekhoRoomRole.Boss;
        var result = new BaekhoDungeonResult { success = true, root = module.gameObject, bossRoom = module,
            plan = new BaekhoDungeonPlan { bossMapId = 7, endMapId = 8 } };
        result.rooms.Add(module);
        Set(director, "_root", module.gameObject);
        int ready = 0;
        director.Ready += _ => ready++;
        var preparation = (IEnumerator)typeof(DungeonEncounterDirector).GetMethod("Prepare", Hidden)
            .Invoke(director, new object[] { result });
        while (preparation.MoveNext()) { }
        Check(director.IsReady && ready == 1, "A room preparation failure prevented the final Ready notification.");
        Check(director.Rooms.Count == 1 && director.Rooms[0].State == DungeonEncounterState.Failed,
            "A missing boss was treated as an empty cleared room.");
        Check(!string.IsNullOrEmpty(director.Error) && director.Error.Contains("boss prefab"),
            "Director omitted the failed room's preparation error.");
    }

    private static void MissingBossAnchorFails()
    {
        var director = Director("Missing anchor");
        var module = new GameObject("Boss without anchor").AddComponent<BaekhoMapModule>();
        module.gameObject.AddComponent<BaekhoGeneratedRoom>().role = BaekhoRoomRole.Boss;
        var room = module.gameObject.AddComponent<DungeonRoomEncounter>();
        var placeholder = new GameObject("Unresolved boss prefab");
        room.Initialize(director, module, new Dictionary<int, float>(), 1, placeholder);
        Check(room.State == DungeonEncounterState.Failed && !room.BossDefeated && room.Error.Contains("bossSpawn"),
            "A boss room without its anchor did not fail safely.");
    }

    private static void ConfirmedDeathAndDownstreamGate()
    {
        var f = FightingBoss("Confirmed boss death");
        var module = f.room.gameObject.AddComponent<BaekhoMapModule>();
        module.connections = new[] { Socket(module.transform, "entry", 20f), Socket(module.transform, "end", -20f) };
        var door = AssetDatabase.LoadAssetAtPath<GameObject>(DoorPath);
        Check(door != null, "The authored dungeon door fixture is missing.");
        typeof(DungeonRoomEncounter).GetMethod("CreateGates", Hidden).Invoke(f.room, new object[]
        { module, new Dictionary<int, float> { { 0, 4f }, { 1, 4f } }, door, 1 });
        Check(!f.room.Gates[0].IsLocked && f.room.Gates[1].IsLocked,
            "The boss route to the end room was open before entry.");
        int cleared = 0;
        f.room.Cleared += _ => cleared++;
        var hit = f.member.Enemy.TakeDamage(100f, poiseDamage: 0f);
        Check(hit.Killed && f.member.HasDied && f.room.BossDefeated, "An actual lethal Enemy.OnDied did not confirm boss defeat.");
        Check(f.room.State == DungeonEncounterState.Cleared && cleared == 1,
            "Confirmed boss death did not clear exactly once.");
        Check(!f.room.Gates[0].IsLocked && !f.room.Gates[1].IsLocked, "Boss death left the downstream gate locked.");
        f.room.NotifyEnemyRemoved(f.member);
        Object.DestroyImmediate(f.member.gameObject);
        Tick(f.room);
        Check(f.room.BossDefeated && cleared == 1, "Duplicate/removal notification changed a confirmed clear.");
    }

    private static void UnexpectedBossRemoval(bool deactivate)
    {
        var f = FightingBoss(deactivate ? "Deactivated boss" : "Destroyed boss");
        int cleared = 0;
        f.room.Cleared += _ => cleared++;
        if (deactivate) f.member.gameObject.SetActive(false);
        else Object.DestroyImmediate(f.member.gameObject);
        Tick(f.room);
        Check(!f.room.BossDefeated && f.room.State == DungeonEncounterState.Failed && cleared == 0,
            "Unexpected boss " + (deactivate ? "deactivation" : "destruction") + " granted a clear.");
        Check(!string.IsNullOrEmpty(f.director.Error), "Director did not expose a runtime boss failure.");
    }

    private static void UnconfirmedZeroHpDoesNotDefeat()
    {
        var f = FightingBoss("HP changed without death callback");
        f.member.GetComponent<EnemyStat>().CurrentHp = 0f;
        Tick(f.room);
        Check(!f.member.HasDied && !f.member.IsDead && !f.room.BossDefeated
            && f.room.State == DungeonEncounterState.Fighting && f.room.AliveCount == 1,
            "Provisional zero HP removed the actor before Enemy confirmed final death.");
    }

    private static void SuspensionDoesNotClear()
    {
        var f = FightingBoss("Suspended boss");
        int cleared = 0;
        f.room.Cleared += _ => cleared++;
        f.director.SuspendEncounters();
        Check(!f.member.Enemy.enabled && !f.member.IsAwake, "Suspension did not freeze enemy AI.");
        Check(!f.member.Enemy.TakeDamage(100f, poiseDamage: 0f).Applied, "A suspended enemy still received damage.");
        f.member.gameObject.SetActive(false);
        Tick(f.room);
        f.room.NotifyEnemyRemoved(f.member);
        Check(f.room.State == DungeonEncounterState.Fighting && f.room.AliveCount == 1 &&
            !f.room.BossDefeated && cleared == 0 && f.director.Error == null,
            "Transition suspension disposed members, failed the room or granted a clear.");
    }

    private static void FirstWaveActivatesWithoutPlayerPosition(bool boss)
    {
        var director = Director(boss ? "Boss activation" : "Group activation");
        director.normalEnemyAI.alertRadius = 7.5f;
        var player = new GameObject("Hidden player").AddComponent<CharacterController>();
        player.transform.position = new Vector3(7100f, 1000f, 7100f);
        Set(director, "player", player);
        var room = new GameObject("Activation room").AddComponent<DungeonRoomEncounter>();
        Set(room, "_director", director);
        Set(room, "_isBoss", boss);
        var members = (List<DungeonEncounterMember>)Get(room, "_members");
        var alive = (HashSet<DungeonEncounterMember>)Get(room, "_alive");
        for (int i = 0; i < (boss ? 1 : 3); i++)
        {
            var actor = new GameObject("First-wave actor " + i);
            actor.SetActive(false);
            actor.transform.SetParent(room.transform, false);
            // Model an already awake patrol member; native agent activation is covered in Play Mode.
            actor.AddComponent<NavMeshAgent>().enabled = false;
            var enemy = actor.AddComponent<Enemy>();
            enemy.EnemyStat = actor.GetComponent<EnemyStat>();
            enemy.EnemyStat.CurrentHp = 10f;
            var member = actor.AddComponent<DungeonEncounterMember>();
            member.Initialize(room);
            actor.SetActive(true);
            typeof(DungeonEncounterMember).GetProperty("IsAwake").SetValue(member, true);
            enemy.enabled = true;
            enemy.SetEncounterPatrolOnly(true);
            members.Add(member);
            alive.Add(member);
            Check(!enemy.HasTargetMemory && enemy.CombatGroup == null,
                "A waiting first-wave member already knew the player position or joined the combat group.");
        }
        typeof(DungeonRoomEncounter).GetProperty("State").SetValue(room, DungeonEncounterState.Waiting);
        Check(room.BeginEncounter(), "The first-wave encounter did not begin.");
        Check(boss ? room.CombatGroup == null : room.CombatGroup != null,
            "Combat groups must belong to ordinary encounters only.");
        if (!boss)
            Check(Mathf.Approximately(room.CombatGroup.Settings.alertRadius, 7.5f),
                "The room did not use the director's AI settings.");
        foreach (var member in members)
        {
            var enemy = member.Enemy;
            Check(!enemy.EncounterPatrolOnly && !enemy.HasTargetMemory,
                "Entry must allow the first wave to detect the player without revealing the player's position.");
            Check(enemy.CombatGroup == room.CombatGroup,
                "An active member's group did not match its room's group.");
            Check(enemy.DetectedTarget == null && enemy.CurrentAction == null && !enemy.IsInExecuteRange,
                "Entry granted vision or an executable attack without ordinary detection.");
        }
        player.transform.position += Vector3.right * 10f;
        Check(!room.BeginEncounter(), "An active encounter accepted a repeated entry.");
        foreach (var member in members)
            Check(!member.Enemy.HasTargetMemory, "Repeated entry revealed the hidden player's position.");

        room.SuspendEncounter();
        foreach (var member in members)
            Check(!member.IsAwake && member.Enemy.CombatGroup == null,
                "Suspension did not remove the frozen member from group coordination.");
    }

    private static void EmptyEndRoomTracksArrival()
    {
        var director = Director("Empty end room");
        var module = new GameObject("Empty end module").AddComponent<BaekhoMapModule>();
        module.transform.position = new Vector3(7000f, 1000f, 7000f);
        module.encounterBudget = 0;
        module.connections = new[] { Socket(module.transform, "entry", 20f) };
        var room = module.gameObject.AddComponent<DungeonRoomEncounter>();
        room.Initialize(director, module, new Dictionary<int, float> { { 0, 4f } }, 1);
        Check(room.State == DungeonEncounterState.Cleared && !room.WasEntered, "Empty room was already marked entered.");
        var player = new GameObject("Arrival capsule").AddComponent<CharacterController>();
        player.center = Vector3.up;
        player.height = 2f;
        player.radius = 0.5f;
        Set(director, "player", player);
        player.transform.position = module.transform.position + new Vector3(0f, 0f, 23f);
        Tick(room);
        Check(!room.WasEntered, "Observing a player outside the doorway counted as arrival.");
        player.transform.position = module.transform.position + new Vector3(0f, 0f, 17f);
        Physics.SyncTransforms();
        Tick(room);
        Check(room.WasEntered && room.State == DungeonEncounterState.Cleared,
            "A swept doorway entry was not tracked for an initially cleared end room.");
    }

    private static (DungeonRoomEncounter room, DungeonEncounterMember member, DungeonEncounterDirector director)
        FightingBoss(string name)
    {
        var director = Director(name);
        var room = new GameObject(name).AddComponent<DungeonRoomEncounter>();
        var actor = new GameObject("Boss actor");
        actor.SetActive(false);
        actor.transform.SetParent(room.transform, false);
        var agent = actor.AddComponent<NavMeshAgent>();
        agent.enabled = false; // Edit Mode fixture does not claim native agent activation coverage.
        var enemy = actor.AddComponent<Enemy>();
        var stats = actor.GetComponent<EnemyStat>();
        stats.CurrentHp = 10f;
        enemy.EnemyStat = stats;
        actor.GetComponent<PoiseHandler>().ResetPoise();
        actor.AddComponent<CapsuleCollider>();
        var member = actor.AddComponent<DungeonEncounterMember>();
        member.Initialize(room);
        actor.SetActive(true);
        enemy.enabled = true;
        ((List<DungeonEncounterMember>)Get(room, "_members")).Add(member);
        ((HashSet<DungeonEncounterMember>)Get(room, "_alive")).Add(member);
        Set(room, "_director", director);
        Set(room, "_isBoss", true);
        Set(room, "_bossMember", member);
        typeof(DungeonRoomEncounter).GetProperty("State").SetValue(room, DungeonEncounterState.Fighting);
        typeof(DungeonRoomEncounter).GetProperty("WasEntered").SetValue(room, true);
        ((List<DungeonRoomEncounter>)Get(director, "_rooms")).Add(room);
        return (room, member, director);
    }

    private static DungeonEncounterDirector Director(string name)
    {
        var director = new GameObject(name + " director").AddComponent<DungeonEncounterDirector>();
        director.GetComponent<BaekhoDungeonGenerator>().generateOnStart = false;
        director.doorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DoorPath);
        return director;
    }

    private static BaekhoMapConnection Socket(Transform parent, string id, float z)
    {
        var socket = new GameObject(id).AddComponent<BaekhoMapConnection>();
        socket.transform.SetParent(parent, false);
        socket.transform.localPosition = new Vector3(0f, 0f, z);
        socket.transform.localRotation = z < 0f ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
        socket.id = id;
        socket.width = 4f;
        socket.height = 3f;
        return socket;
    }

    private static object Get(object owner, string name) => owner.GetType().GetField(name, Hidden).GetValue(owner);
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Hidden).SetValue(owner, value);
    private static void Tick(DungeonRoomEncounter room) => typeof(DungeonRoomEncounter).GetMethod("Update", Hidden).Invoke(room, null);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
}
