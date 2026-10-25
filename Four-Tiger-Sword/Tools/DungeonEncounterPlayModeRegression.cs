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
                if (room.State == DungeonEncounterState.Waiting) fightingRooms.Add(room);
            }
            Check(fightingRooms.Count >= 2, "Validation requires at least two uncleared combat rooms.");
            player = director.Player;
            Check(player != null, "No player was found.");
            controller = player.GetComponent<PlayerController>();
            savedPosition = player.transform.position;
            savedRotation = player.transform.rotation;
            savedControllerEnabled = controller.enabled;
            controller.enabled = false;

            var first = fightingRooms[0];
            var dormant = first.Members[0];
            float hp = dormant.GetComponent<EnemyStat>().CurrentHp;
            Check(!dormant.Enemy.TakeDamage(100, poiseDamage: 0).Applied && dormant.GetComponent<EnemyStat>().CurrentHp == hp,
                "Dormant enemy received damage.");
            foreach (var member in first.Members)
                Check(!member.Enemy.enabled && !member.GetComponent<NavMeshAgent>().enabled, "Dormant AI was running.");
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
            int alive = first.AliveCount;
            Kill(first.Members[0]);
            Check(first.AliveCount == alive - 1 && first.State == DungeonEncounterState.Fighting,
                "One death prematurely cleared the room.");
            CheckGates(first, true);
            for (int i = 1; i < first.Members.Count; i++) Kill(first.Members[i]);
            Check(first.AliveCount == 0 && first.State == DungeonEncounterState.Cleared, "Last death did not clear.");
            CheckGates(first, false);
            Check(!first.BeginEncounter(), "Cleared room restarted.");

            var second = fightingRooms[1];
            await Enter(second, player, true);
            for (int i = 0; i < second.Members.Count - 1; i++) Kill(second.Members[i]);
            UnityEngine.Object.Destroy(second.Members[second.Members.Count - 1].gameObject);
            await Frame();
            Check(second.AliveCount == 0 && second.State == DungeonEncounterState.Cleared,
                "Unexpected destruction left the room locked.");
            CheckGates(second, false);

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
            Check(oldRoot == null && first == null && second == null, "Regeneration left old encounters alive.");
            int fresh = 0;
            foreach (var room in director.Rooms)
            {
                Check(room.State != DungeonEncounterState.Failed, "Regenerated encounter failed: " + room.Error);
                if (room.State == DungeonEncounterState.Waiting) fresh++;
            }
            Check(fresh >= 2, "Regeneration did not restore fresh waiting encounters.");
            generator.ClearGenerated();
            await Frame();
            Check(generator.CurrentRoot == null && director.Rooms.Count == 0 && !director.IsReady,
                "ClearGenerated did not clean encounter tracking.");
            SessionState.SetString(ResultKey, "PASS: " + checks + " Play Mode checks; actual generation, dormant damage, swept and jamb entry, native agent offsets, all-gate lock, lethal deaths, unexpected destruction, re-entry, failed/successful regeneration and clear cleanup.");
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
