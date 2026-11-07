// Run Start in Play Mode through Unity Pipeline run_script. Poll SessionState key
// DungeonEncounter.PlayModeRegression for RUNNING / PASS / FAIL. Changes are Play Mode only.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using StudioBaekho;

public static class DungeonEncounterPlayModeRegression
{
    public const string ResultKey = "DungeonEncounter.PlayModeRegression";
    private static int checks;

    public static string Start()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        checks = 0;
        SessionState.SetString(ResultKey, "RUNNING");
        _ = Run();
        return "Play Mode encounter regression started.";
    }

    private static async Task Run()
    {
        CharacterController player = null;
        PlayerController controller = null;
        Vector3 savedPosition = default;
        Quaternion savedRotation = default;
        bool savedControllerEnabled = false;
        try
        {
            var director = UnityEngine.Object.FindFirstObjectByType<DungeonEncounterDirector>();
            Check(director != null, "Director not installed in the scene.");
            await Wait(() => director.IsReady, "Initial encounters did not finish preparing.");
            int firstFrame = Time.frameCount;
            await Frame();
            Check(Time.frameCount > firstFrame, "The game is not advancing frames.");
            var generator = director.GetComponent<BaekhoDungeonGenerator>();
            int seed = generator.LastResult.seed;
            var fightingRooms = new List<DungeonRoomEncounter>();
            foreach (var room in director.Rooms)
            {
                Check(room.State != DungeonEncounterState.Failed, room.name + ": " + room.Error);
                if (room.State == DungeonEncounterState.Waiting && !(room.Members[0].Enemy is Boss)) fightingRooms.Add(room);
            }
            Check(fightingRooms.Count > 0, "Validation requires an uncleared ordinary combat room.");
            fightingRooms.Sort((left, right) => right.WaveCount.CompareTo(left.WaveCount));
            player = director.Player;
            Check(player != null, "No player was found.");
            controller = player.GetComponent<PlayerController>();
            savedPosition = player.transform.position;
            savedRotation = player.transform.rotation;
            savedControllerEnabled = controller.enabled;
            controller.enabled = false;

            var first = fightingRooms[0];
            Check(first.WaveCount > 1, "Validation requires a room with reinforcements.");
            int cleared = 0;
            first.Cleared += _ => cleared++;
            var patrol = first.Members[0];
            float hp = patrol.GetComponent<EnemyStat>().CurrentHp;
            Check(!patrol.Enemy.TakeDamage(100, poiseDamage: 0).Applied && patrol.GetComponent<EnemyStat>().CurrentHp == hp,
                "Patrolling enemy received damage before entry.");
            var patrolStarts = new Dictionary<DungeonEncounterMember, Vector3>();
            foreach (var member in first.Members)
            {
                Check(member.Enemy.DetectedTarget == null && member.GetComponent<NavMeshAgent>().isOnNavMesh,
                    "Waiting enemy acquired a target or had no patrol navigation.");
                patrolStarts.Add(member, member.transform.position);
            }
            await Wait(() =>
            {
                foreach (var pair in patrolStarts)
                    if ((pair.Key.transform.position - pair.Value).sqrMagnitude > 0.04f) return true;
                return false;
            }, "The first wave never visibly patrolled.");
            await Enter(first, player, false);
            foreach (var member in first.Members)
            {
                var agent = member.GetComponent<NavMeshAgent>();
                Check(member.Enemy.enabled && member.IsAwake && agent.isOnNavMesh, "Native agent failed to activate.");
                var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
                float offset = agent.baseOffset * Mathf.Abs(member.transform.lossyScale.y);
                Check(NavMesh.SamplePosition(member.transform.position - Vector3.up * offset, out var hit, 0.5f, filter)
                    && Mathf.Abs(member.transform.position.y - hit.position.y - offset) < 0.25f,
                    member.name + ": activation changed the agent base offset.");
            }
            var shop = UnityEngine.Object.FindFirstObjectByType<StageShopService>();
            var wallet = shop != null ? shop.Wallet : null;
            int goldBefore = wallet != null ? wallet.Gold : 0;
            var run = UnityEngine.Object.FindFirstObjectByType<RunManager>();
            int creditedBefore = run != null ? run.ClearedCombatRoomCount : 0;
            await DefeatWaves(first, false);
            Check(first.WasCombatClearedByDefeat && cleared == 1, "Final defeat did not credit the room exactly once.");
            if (run != null)
                Check(run.ClearedCombatRoomCount == creditedBefore + 1, "The run did not record exactly one cleared room.");
            if (wallet != null)
                Check(wallet.Gold - goldBefore == first.Members.Count * shop.settings.ordinaryKillGold,
                    "Gold was missing or duplicated on a wave member.");
            CheckGates(first, false);
            Check(!first.BeginEncounter(), "Cleared room restarted.");

            var oldRoot = generator.CurrentRoot;
            var settings = generator.settings;
            try
            {
                generator.settings = null;
                generator.StartCoroutine(generator.Generate(seed));
                await Frame();
                Check(generator.CurrentRoot == oldRoot && director.IsReady && first.State == DungeonEncounterState.Cleared,
                    "Failed generation discarded existing encounter state.");
            }
            finally { generator.settings = settings; }
            MoveCenter(player, new Vector3(5000f, 10f, 5000f));
            await Frame();
            generator.StartCoroutine(generator.Generate(seed));
            await Wait(() => generator.CurrentRoot != oldRoot && director.IsReady, "Regeneration did not finish.");
            await Frame();
            Check(oldRoot == null && first == null, "Regeneration left old encounters alive.");
            DungeonRoomEncounter second = null;
            foreach (var room in director.Rooms)
            {
                Check(room.State != DungeonEncounterState.Failed, "Regenerated encounter failed: " + room.Error);
                if (room.State == DungeonEncounterState.Waiting && !(room.Members[0].Enemy is Boss))
                    second = room;
            }
            Check(second != null, "Regeneration did not restore a fresh ordinary encounter.");
            await Enter(second, player, true);
            await DefeatWaves(second, true);
            Check(!second.WasCombatClearedByDefeat, "Despawned enemy earned room credit.");
            CheckGates(second, false);
            generator.ClearGenerated();
            await Frame();
            Check(generator.CurrentRoot == null && director.Rooms.Count == 0 && !director.IsReady,
                "ClearGenerated did not clean encounter tracking.");
            SessionState.SetString(ResultKey, "PASS: " + checks + " Play Mode checks. Patrol, admission, wave delay/pause, combat lock, rewards, final clear, re-entry, regeneration and cleanup.");
        }
        catch (Exception exception) { SessionState.SetString(ResultKey, "FAIL: " + exception); }
        finally
        {
            if (Application.isPlaying && player != null)
            {
                player.enabled = false;
                player.transform.SetPositionAndRotation(savedPosition, savedRotation);
                player.enabled = true;
                if (controller != null) controller.enabled = savedControllerEnabled;
            }
        }
    }

    private static async Task DefeatWaves(DungeonRoomEncounter room, bool despawnLast)
    {
        int seen = 0;
        for (int wave = 1; wave <= room.WaveCount; wave++)
        {
            int end = room.Members.Count;
            Check(room.CurrentWaveNumber == wave && end > seen, "Wrong active wave or empty roster.");
            bool finalWave = wave == room.WaveCount;
            for (int i = seen; i < end; i++)
            {
                if (finalWave && despawnLast && i == end - 1)
                    UnityEngine.Object.Destroy(room.Members[i].gameObject);
                else Kill(room.Members[i]);
            }
            seen = end;
            if (finalWave) break;
            Check(room.IsCombatActive && room.AliveCount == 0 && !room.WasCombatClearedByDefeat,
                "Intermission unlocked combat or granted a room clear.");
            CheckGates(room, true);
            if (wave == 1)
            {
                float scale = Time.timeScale;
                try
                {
                    Time.timeScale = 0f;
                    await Task.Delay(1650);
                    Check(room.Members.Count == seen && room.IsCombatActive, "Reinforcements arrived during pause.");
                }
                finally { Time.timeScale = scale; }
            }
            await Wait(() => room.Members.Count > seen || room.State == DungeonEncounterState.Failed,
                "Reinforcements never arrived.");
            Check(room.State == DungeonEncounterState.Fighting, "Reinforcement failed: " + room.Error);
            await Frame(); // Let newly enabled Enemy components run Start before applying damage.
        }
        await Frame();
        Check(room.AliveCount == 0 && room.State == DungeonEncounterState.Cleared, "Last wave did not clear.");
    }

    private static async Task Enter(DungeonRoomEncounter room, CharacterController player, bool jamb)
    {
        Check(room.Gates.Count > 0, "Room has no gates.");
        var gate = room.Gates[0];
        float x = 0;
        if (jamb)
        {
            // Use authored socket width, not a door-frame child's collider width.
            x = (float)typeof(DungeonEncounterGate).GetField("_halfWidth", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(gate)
                - player.radius + player.skinWidth * 0.25f;
        }
        MoveCenter(player, gate.transform.TransformPoint(new Vector3(x, 1.2f, 3f)));
        await Frame();
        Check(room.State == DungeonEncounterState.Waiting, "Room started before safely entering.");
        MoveCenter(player, gate.transform.TransformPoint(new Vector3(x, 1.2f, -3f)));
        await Frame();
        Check(room.State == DungeonEncounterState.Fighting, "Safe entrance did not start combat: " + room.State + " / " + room.Error);
        CheckGates(room, true);
    }

    private static void MoveCenter(CharacterController player, Vector3 center)
    {
        player.enabled = false;
        player.transform.position = center - player.transform.TransformVector(player.center);
        player.enabled = true;
        Physics.SyncTransforms();
    }

    private static void Kill(DungeonEncounterMember member)
    {
        if (member == null || member.IsDead) return;
        member.Enemy.TakeDamage(member.GetComponent<EnemyStat>().CurrentHp + 1f, poiseDamage: 0f);
    }

    private static void CheckGates(DungeonRoomEncounter room, bool locked)
    {
        foreach (var gate in room.Gates) Check(gate.IsLocked == locked, "A room gate has the wrong lock state.");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    private static async Task Frame()
    {
        int start = Time.frameCount;
        await Wait(() => Time.frameCount >= start + 2, "Player loop stopped advancing.");
    }
    private static async Task Wait(Func<bool> predicate, string error)
    {
        var timeout = DateTime.UtcNow.AddSeconds(20);
        while (!predicate())
        {
            if (!Application.isPlaying || DateTime.UtcNow >= timeout) throw new Exception(error);
            await Task.Delay(20);
        }
    }
}
