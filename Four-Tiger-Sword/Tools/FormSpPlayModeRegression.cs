// Unity Pipeline run_script: Start in the real Game Scene Play Mode, then poll ResultKey.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public static class FormSpPlayModeRegression
{
    public const string ResultKey = "FormSp.PlayModeRegression";
    private static DateTime deadline;
    private static int checks;
    public static string Start()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Game Scene Play Mode first.");
        checks = 0; deadline = DateTime.UtcNow.AddSeconds(120);
        SessionState.SetString(ResultKey, "RUNNING");
        _ = Run();
        return "Form SP Play Mode regression started.";
    }

    private static async Task Run()
    {
        PlayerController player = null;
        Vector3 position = default; Quaternion rotation = default;
        bool inputEnabled = false, capsuleEnabled = false, saved = false;
        var frozen = new List<(Enemy enemy, bool enabled, NavMeshAgent agent, bool navigation)>();
        object shield = new object();
        try
        {
            var run = UnityEngine.Object.FindFirstObjectByType<RunManager>();
            Check(run != null, "The real Game Scene has no RunManager.");
            await Wait(() => run.State == RunState.Playing && run.stageManager.Encounters.IsReady,
                "Run/encounters did not become ready: " + run.LastError);
            player = run.player;
            Check(player != null && player.enabled && !player.IsStageTransitioning, "The live player is not ready.");
            position = player.transform.position; rotation = player.transform.rotation;
            inputEnabled = player.Input.enabled; capsuleEnabled = player.Controller.enabled;
            saved = true;
            player.Input.enabled = false;
            player.StatManager.GrantShield(shield, 1000000f, 120f);
            var director = run.stageManager.Encounters;
            DungeonRoomEncounter room = null;
            foreach (var candidate in director.Rooms)
                if (candidate.State == DungeonEncounterState.Waiting && candidate.Gates.Count > 0) { room = candidate; break; }
            Check(room != null, "No waiting combat room is available.");
            var forms = player.FormManager;
            var active = forms.CurrentForm as BaseForm;
            BaseForm target = null;
            foreach (ElementType element in Enum.GetValues(typeof(ElementType)))
            { var candidate = forms.GetForm(element); if (candidate != null && candidate != active && candidate.CanSwitchAttack) { target = candidate; break; } }
            Check(active != null && target != null, "Two live forms are required.");
            float shared = player.StatManager.CurrentSp;
            active.UpdateFormSp(15f, true, true); target.UpdateFormSp(15f, true, true);
            Near(player.StatManager.CurrentSp, shared, .001f, "Preparing form SP changed common SP.");
            var gate = room.Gates[0];
            Move(player, gate.transform.TransformPoint(new Vector3(0f, 1.2f, 3f)));
            await Frame();
            Check(room.State == DungeonEncounterState.Waiting && !player.IsInRoomCombat, "Outside entry already counted as combat.");
            float outside = active.CurrentFormSp, resting = target.CurrentFormSp;
            await Seconds(.35f);
            Near(active.CurrentFormSp, outside, .001f, "The active form drained outside combat.");
            Check(target.CurrentFormSp > resting, "An inactive form did not recover outside combat.");
            Move(player, gate.transform.TransformPoint(new Vector3(0f, 1.2f, -3f)));
            await Wait(() => room.State == DungeonEncounterState.Fighting, "Natural gate entry did not start the encounter.");
            Check(player.IsInRoomCombat, "The active encounter was not bound to the live player.");
            foreach (var member in room.Members)
            {
                Check(member.IsAwake && member.Enemy.enabled, "A real encounter member did not activate.");
                var agent = member.GetComponent<NavMeshAgent>();
                frozen.Add((member.Enemy, member.Enemy.enabled, agent, agent != null && agent.enabled));
                member.Enemy.enabled = false; if (agent != null) agent.enabled = false;
            }
            float before = active.CurrentFormSp, started = Time.time;
            await Seconds(1.2f);
            Near(before - active.CurrentFormSp, (Time.time - started) * 100f / 30f, .3f, "Live combat drain rate is incorrect.");
            shared = player.StatManager.CurrentSp;
            active.UpdateFormSp(30f, true, true);
            Check(active.CurrentFormSp == 0f && active.IsFormSpExhausted, "The active form did not exhaust.");
            Near(player.StatManager.CurrentSp, shared, .001f, "Form exhaustion consumed common SP.");
            await Wait(() => player.CanChangeForm && (player.StateMachine.CurrentState is PlayerIdleState
                || player.StateMachine.CurrentState is PlayerRunState), "Player never became eligible to switch.");
            player.StatManager.AddSp(player.StatManager.MaxSp);
            shared = player.StatManager.CurrentSp;
            float cost = player.SwitchSpCost;
            Check(forms.TrySwitchForm(target) && forms.CurrentForm == target, "An exhausted form could not switch normally.");
            Near(player.StatManager.CurrentSp, Mathf.Min(shared - cost, player.StatManager.MaxSp), .001f,
                "The actual switch did not pay its existing common SP cost exactly once.");
            await Seconds(4.1f);
            Check(active.CurrentFormSp >= 20f && !active.IsFormSpExhausted, "Real inactive ticks did not recover and clear exhaustion.");
            foreach (var member in room.Members)
            {
                if (member == null || member.IsDead) continue;
                member.Enemy.enabled = true;
                var hit = member.Enemy.TakeDamage(member.GetComponent<EnemyStat>().CurrentHp + 100000f, poiseDamage: 0f);
                Check(hit.Killed && member.HasDied, "Real lethal damage did not confirm encounter death.");
            }
            Check(room.State == DungeonEncounterState.Cleared && !player.IsInRoomCombat, "Final death did not stop combat immediately.");
            before = target.CurrentFormSp; resting = active.CurrentFormSp;
            await Seconds(.5f);
            Near(target.CurrentFormSp, before, .001f, "The active form drained in the cleared room.");
            Check(active.CurrentFormSp > resting, "The inactive form stopped recovering after clear.");
            SessionState.SetString(ResultKey, "PASS: " + checks + " live Game Scene checks; natural gate combat, timed drain/recovery, "
                + "exhaustion, real switch/common SP cost, confirmed lethal clear and post-combat hold.");
        }
        catch (Exception exception) { SessionState.SetString(ResultKey, "FAIL: " + exception); }
        finally
        {
            if (Application.isPlaying)
            {
                foreach (var state in frozen)
                    if (state.enemy != null && state.enemy.EnemyStat.CurrentHp > 0f)
                    { state.enemy.enabled = state.enabled; if (state.agent != null) state.agent.enabled = state.navigation; }
                if (player != null && saved)
                {
                    player.StatManager.RemoveShield(shield);
                    player.Controller.enabled = false; player.transform.SetPositionAndRotation(position, rotation);
                    player.Controller.enabled = capsuleEnabled; player.VerticalVelocity = 0f;
                    player.Input.enabled = inputEnabled; Physics.SyncTransforms();
                }
            }
        }
    }

    private static void Move(PlayerController player, Vector3 center)
    { player.Controller.enabled = false; player.transform.position = center - player.transform.TransformVector(player.Controller.center);
      player.VerticalVelocity = 0f; player.Controller.enabled = true; Physics.SyncTransforms(); }
    private static Task Frame() { int frame = Time.frameCount; return Wait(() => Time.frameCount >= frame + 2, "Frames stopped."); }
    private static Task Seconds(float seconds) { float until = Time.time + seconds; return Wait(() => Time.time >= until, "World time stopped."); }
    private static async Task Wait(Func<bool> predicate, string error)
    { while (!predicate()) { if (!Application.isPlaying || DateTime.UtcNow >= deadline) throw new Exception(error); await Task.Delay(20); } }
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); checks++; }
    private static void Near(float actual, float expected, float tolerance, string message)
        => Check(Mathf.Abs(actual - expected) <= tolerance, message + " actual=" + actual + " expected=" + expected);
}
