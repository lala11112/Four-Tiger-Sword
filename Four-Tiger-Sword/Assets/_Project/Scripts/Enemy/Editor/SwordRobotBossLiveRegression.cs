using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using StudioBaekho;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

/// <summary>Exercises the current run's real sword robot encounter without entering the stage exit.</summary>
public static class SwordRobotBossLiveRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool _running;
    private static Report _report = new Report { status = "idle" };

    [Serializable] private sealed class Report
    {
        public string status, error, boss, directory, reportPath;
        public int checks, phaseDepletions, deathEvents, initialGold, finalGold;
        public float expectedRevivalSeconds, actualRevivalSeconds, phase1Hp, phase2Hp;
        public List<Capture> captures = new List<Capture>();
    }

    [Serializable] private sealed class Capture
    {
        public string label, path, animation;
        public int frame, phase;
        public float hp, hudFill, reviveProgress;
        public bool hudVisible, controlsEnabled;
        public Vector3 position;
        public Vector3[] bones;
    }

    [Serializable] private sealed class Status
    {
        public string status, error, reportPath;
        public int checks, phaseDepletions, deathEvents, initialGold, finalGold;
        public float expectedRevivalSeconds, actualRevivalSeconds;
        public string[] screenshots;
    }

    public static string StartPlayMode()
    {
        if (!Application.isPlaying || EditorApplication.isCompiling || _running)
            throw new InvalidOperationException("Start sword robot live verification in idle Game Scene Play Mode.");
        _running = true;
        _report = new Report { status = "running" };
        _report.directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "SwordRobotBossLive",
            DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")));
        _report.reportPath = Path.Combine(_report.directory, "report.json");
        Directory.CreateDirectory(_report.directory);
        _ = Run();
        return PlayModeStatus();
    }

    public static string PlayModeStatus() => JsonUtility.ToJson(new Status {
        status = _report.status, error = _report.error, reportPath = _report.reportPath,
        checks = _report.checks, phaseDepletions = _report.phaseDepletions, deathEvents = _report.deathEvents,
        initialGold = _report.initialGold, finalGold = _report.finalGold,
        expectedRevivalSeconds = _report.expectedRevivalSeconds, actualRevivalSeconds = _report.actualRevivalSeconds,
        screenshots = _report.captures.Select(capture => capture.path).ToArray()
    }, true);

    private static async Task Run()
    {
        Boss boss = null;
        PlayerController player = null;
        RunManager run = null;
        Vector3 startPosition = default;
        Quaternion startRotation = Quaternion.identity;
        float atkAdjustment = 0f;
        bool suppressed = false, relocated = false;
        Action onDied = () => _report.deathEvents++;
        Action<int> onDepleted = _ => _report.phaseDepletions++;
        try
        {
            await Until(() => (run = Object.FindObjectsByType<RunManager>(FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.isActiveAndEnabled && candidate.State == RunState.Playing)) != null
                && run.stageManager != null && run.stageManager.IsReady, 45f, "The current run did not finish generating its stage.");
            Check(run.gameObject.scene.path.Replace('\\', '/').EndsWith("/_Project/Scenes/Game Scene.unity", StringComparison.Ordinal),
                "Live verification requires the actual Game Scene run.");
            Check(CombatSettings.Active != null && CombatSettings.Active.ElementTable != null,
                "The actual Game Scene is missing its combat damage table.");
            int musicOwners = BossMusicService.ActiveOwnerCount;
            var musicOwner = BossMusicService.CurrentOwner;
            var musicClip = BossMusicService.CurrentClip;
            var stage = run.stageManager;
            var result = stage.Result;
            Check(result != null && result.success && result.bossRoom != null, "The generated stage has no boss room.");
            var room = result.bossRoom.GetComponent<DungeonRoomEncounter>();
            Check(room != null && room.State == DungeonEncounterState.Waiting && room.Members.Count == 1,
                "Live verification requires a fresh waiting boss encounter.");
            boss = room.Members[0].Enemy as Boss;
            Check(boss != null && boss.name.StartsWith("SwordRobotBoss", StringComparison.Ordinal)
                && boss.PhaseConfiguration.Count == 2, "The generated encounter did not use the new two-phase SwordRobotBoss.");
            _report.boss = boss.name;
            _report.phase1Hp = boss.PhaseConfiguration[0].phaseMaxHp * boss.PhaseHealthMultiplier;
            _report.phase2Hp = boss.PhaseConfiguration[1].phaseMaxHp * boss.PhaseHealthMultiplier;
            player = run.player;
            Check(ControlsEnabled(player) && !player.IsInRoomCombat && !player.IsGameplayModalPaused,
                "The real player must be alive, outside combat and have usable controls.");
            var end = result.endRoom != null ? result.endRoom.GetComponent<DungeonRoomEncounter>() : null;
            Check(!stage.IsCleared && (end == null || !end.WasEntered),
                "The stage exit was already visited; no persistent progression should be tested here.");
            var wallet = player.GetComponent<PlayerWallet>();
            Check(wallet != null && boss.Data?.rewardGroup != null && boss.Data.rewardGroup.items.Count == 0,
                "Live verification requires a run wallet and the sword robot's gold-only reward group.");
            int reward = boss.Data.rewardGroup.gold;
            _report.initialGold = wallet.Gold;
            startPosition = player.transform.position; startRotation = player.transform.rotation;
            Check(TryLanding(result.bossRoom, boss, player, out var position, out var rotation),
                "No collision-free grounded landing could be found near the real boss.");
            Check(player.TryFastTravel(position, rotation), "The actual player rejected same-stage relocation near the boss.");
            relocated = true;
            Check(room.BeginEncounter(), "The actual boss encounter did not activate: " + room.Error);
            await Until(() => boss.Brain != null && boss.Animator != null, 10f, "The actual boss failed to initialize.");
            var modifiers = (Dictionary<EnemyStatType, float>)typeof(EnemyStat).GetField("multValues", Hidden).GetValue(boss.EnemyStat);
            atkAdjustment = -1f - modifiers[EnemyStatType.ATK];
            boss.EnemyStat.AddModifier(EnemyStatType.ATK, 0f, atkAdjustment); suppressed = true;
            Check(Mathf.Approximately(boss.EnemyStat.GetStat(EnemyStatType.ATK), 0f),
                "Temporary actor-only damage suppression failed.");
            Check(boss.GetComponent<IEnemySensor>() == null && boss.ActiveCombatProfile != null,
                "The real encounter must use direct-target boss tactics without an exploration sensor.");
            var nav = boss.GetComponent<NavMeshAgent>();
            Check(nav != null && nav.enabled && nav.isOnNavMesh, "The real boss did not land on generated navigation.");
            boss.OnDied += onDied; boss.OnPhaseDepleted += onDepleted;
            await Until(() => boss.CombatTarget == player.transform && boss.CurrentAction?.IsActive == true
                && ParryEventBus.IsTelegraphActive(boss.CurrentAction), 15f,
                "The actual boss did not autonomously engage the real player with an attack telegraph.");
            BossHealthHud hud = null;
            // HUD discovery/binding happens in LateUpdate after the actor begins combat.
            await Until(() => (hud = Object.FindObjectsByType<BossHealthHud>(FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.isActiveAndEnabled && candidate.IsVisible)) != null,
                2f, "The real boss health HUD did not appear.");
            CaptureFrame("phase1_telegraph", boss, player, hud);
            var opening = boss.CurrentAction;
            await Until(() => opening.BossStage == BossAttackStage.Active || opening.IsFinished, 12f,
                "The real telegraphed attack did not reach its strike window.");
            CaptureFrame("phase1_strike", boss, player, hud);
            await Frames(2); // ScreenCapture writes at end of frame, before the phase-depleting hit.
            await VerifyRevival(boss, player, room, hud, wallet);
            await VerifyEntryFlight(boss, player, room, hud);
            await Frames(2); // Keep the phase-2 capture alive before the final death mutation.
            Check(!stage.IsCleared && (end == null || !end.WasEntered),
                "The stage exit was entered during verification; refusing to confirm final death.");
            var final = boss.TakeDamage(10000000f, poiseDamage: 0f);
            Check(final.Applied && final.Killed && boss.IsDead && _report.deathEvents == 1,
                "The last HP pool did not confirm exactly one final boss death.");
            await Frames(3);
            Check(room.State == DungeonEncounterState.Cleared && room.BossDefeated && room.AliveCount == 0,
                "The real encounter did not clear after the final boss defeat.");
            Check(room.Gates.Count > 0 && room.Gates.All(gate => gate != null && !gate.IsLocked),
                "The real boss-room doors did not open after the final defeat.");
            await Until(() => !hud.IsVisible && Mathf.Approximately(hud.FillAmount, 0f),
                2f, "The real HUD did not hide after final death.");
            if (boss.PhaseConfiguration.All(phase => phase.musicMode == BossMusicMode.Keep))
                Check(BossMusicService.ActiveOwnerCount == musicOwners && BossMusicService.CurrentOwner == musicOwner
                    && BossMusicService.CurrentClip == musicClip, "A Keep-music boss interrupted existing music ownership.");
            _report.finalGold = wallet.Gold;
            Check(_report.finalGold == _report.initialGold + reward, "The final boss reward was not granted exactly once.");
            Check(!boss.TakeDamage(1f, poiseDamage: 0f).Applied && _report.deathEvents == 1
                && wallet.Gold == _report.finalGold, "A hit after final death duplicated damage, death or reward.");
            Check(ControlsEnabled(player) && run.State == RunState.Playing && !stage.IsCleared
                && stage.Result == result && (end == null || !end.WasEntered),
                "The live fight disabled controls or advanced persistent stage progression.");
            CaptureFrame("final_death", boss, player, hud);
            await Frames(2);
            await Frames(8);
            Check(_report.captures.All(capture => File.Exists(capture.path)), "One or more live GameView captures were not written.");
            _report.status = "passed";
        }
        catch (Exception exception)
        {
            _report.status = "failed"; _report.error = exception.GetBaseException().Message;
            Debug.LogException(exception);
        }
        finally
        {
            if (boss != null)
            {
                boss.OnDied -= onDied; boss.OnPhaseDepleted -= onDepleted;
                if (suppressed && boss.EnemyStat != null) boss.EnemyStat.RemoveModifier(EnemyStatType.ATK, 0f, atkAdjustment);
            }
            if (relocated && ControlsEnabled(player) && run != null && run.State == RunState.Playing
                && !player.IsInRoomCombat && !player.IsGameplayModalPaused)
                player.TryFastTravel(startPosition, startRotation);
            try { File.WriteAllText(_report.reportPath, JsonUtility.ToJson(_report, true)); }
            catch (Exception exception) { Debug.LogWarning("Could not write sword robot live report: " + exception.Message); }
            _running = false;
        }
    }

    private static async Task VerifyRevival(Boss boss, PlayerController player, DungeonRoomEncounter room,
        BossHealthHud hud, PlayerWallet wallet)
    {
        Check(boss.PhaseIndex == 0 && Mathf.Approximately(boss.EnemyStat.CurrentHp, _report.phase1Hp),
            "The actual first phase did not begin at full authored HP.");
        var oldAction = boss.CurrentAction;
        float start = boss.ActionTime;
        var hit = boss.TakeDamage(10000000f, poiseDamage: 0f);
        Check(hit.Applied && !hit.Killed && boss.IsTransitioning && !boss.IsDead
            && _report.phaseDepletions == 1 && _report.deathEvents == 0,
            "The first HP pool failed to enter revival without a reward-eligible death.");
        Check(!boss.TakeDamage(10000000f, poiseDamage: 0f).Applied, "Revival accepted a same-frame follow-up hit.");
        Capture down = null, hold = null, rise = null;
        double deadline = Time.realtimeSinceStartupAsDouble + 25d;
        while (boss.IsTransitioning)
        {
            await Frames(1);
            Check(ControlsEnabled(player) && room.State == DungeonEncounterState.Fighting && !room.BossDefeated
                && room.AliveCount == 1 && wallet.Gold == _report.initialGold && _report.deathEvents == 0,
                "Revival interrupted controls, cleared the encounter or paid an early reward.");
            Check(room.Gates.All(gate => gate != null && gate.IsLocked) && hud.IsVisible,
                "A boss-room door opened or the boss HUD disappeared during revival.");
            if (!boss.IsTransitioning) break;
            float elapsed = boss.ActionTime - start;
            Check(boss.PhaseIndex == 0 && boss.EnemyStat.CurrentHp == 0f
                && Mathf.Abs(hud.FillAmount - boss.ReviveProgress) < .1f,
                "The actual revival health/HUD timing diverged from the down-and-rise timeline.");
            if (down == null && elapsed >= boss.Data.phaseDownSeconds * .4f)
                down = CaptureFrame("phase1_down", boss, player, hud);
            if (hold == null && elapsed >= boss.Data.phaseDownSeconds + boss.Data.phaseHoldSeconds * .25f)
                hold = CaptureFrame("phase1_hold", boss, player, hud);
            if (rise == null && boss.ReviveProgress >= .45f)
                rise = CaptureFrame("revival_rise", boss, player, hud);
            if (Time.realtimeSinceStartupAsDouble > deadline) throw new InvalidOperationException("The real revival timeline stalled.");
        }
        _report.expectedRevivalSeconds = boss.Data.phaseDownSeconds + boss.Data.phaseHoldSeconds + boss.Data.phaseReviveSeconds;
        _report.actualRevivalSeconds = boss.ActionTime - start;
        Check(down != null && hold != null && rise != null && down.animation == "PhaseDown"
            && hold.animation == "PhaseDown" && rise.animation == "PhaseRevive", "The real revival did not show its down/hold/rise animation windows.");
        Check(PoseChanged(down, hold) && PoseChanged(hold, rise), "The actual robot skeleton did not change through its revival poses.");
        Check(boss.PhaseIndex == 1 && !boss.IsDead && Mathf.Approximately(boss.EnemyStat.CurrentHp, _report.phase2Hp)
            && Mathf.Abs(_report.actualRevivalSeconds - _report.expectedRevivalSeconds) < 1f,
            "Revival did not restore phase 2 at full HP near the authored actor-time duration.");
        Check(oldAction == null || !oldAction.IsActive && !oldAction.HasBossArmor && !ParryEventBus.IsTelegraphActive(oldAction),
            "The first phase's attack, temporary armor or telegraph survived revival.");
        await Until(() => boss.CurrentAction?.IsActive == true && boss.CombatTarget == player.transform, 15f,
            "The actual second phase did not resume attacks against the real player.");
        CaptureFrame("phase2_active", boss, player, hud);
    }

    private static async Task VerifyEntryFlight(Boss boss, PlayerController player, DungeonRoomEncounter room, BossHealthHud hud)
    {
        var flight = boss.GetComponent<BossPhaseEntryFlight>();
        var visuals = boss.GetComponent<BossPhaseVisuals>();
        var presentation = boss.GetComponent<SwordRobotBossPresentation>();
        var burst = boss.CurrentAction as BossPhaseBurstAction;
        Check(flight != null && burst != null && burst.UsesAirborneEntry,
            "The current run did not start the configured airborne phase entry.");
        Check(visuals.ActiveVisualPhase == 0 && !burst.WarningStarted,
            "The actual boss must take off in phase one before showing a floor warning.");
        await Until(() => flight.VisualHidden, 5f, "The actual boss never disappeared above the player camera.");
        Check(!burst.WarningStarted && !presentation.BurstWarningVisible && boss.IsInvulnerable,
            "The hidden wait displayed an early warning or allowed damage.");
        CaptureFrame("phase_entry_hidden", boss, player, hud);
        await Until(() => burst.WarningStarted, 5f, "The actual boss never published its landing warning.");
        await Frames(1);
        Vector3 center = burst.Center;
        Check(visuals.ActiveVisualPhase == 1 && presentation.BurstWarningVisible,
            "The warning did not bind the second rig and its ground presentation.");
        CaptureFrame("phase_entry_warning", boss, player, hud);
        await Until(() => !flight.VisualHidden, 5f, "The second rig never returned from above the screen.");
        CaptureFrame("phase_entry_descent", boss, player, hud);
        await Until(() => burst.BurstPlayed, 5f, "The actual phase-entry landing was cancelled on valid arena ground.");
        await Frames(1);
        Check(burst.Center == center && presentation.BurstEffectCount == 1
            && Vector3.Distance(boss.transform.position, center) < .15f,
            "The marked landing, actor and impact effect diverged in the actual boss room.");
        CaptureFrame("phase_entry_impact", boss, player, hud);
        await Until(() => boss.CurrentAction != burst && boss.CurrentAction?.IsActive == true, 8f,
            "Normal phase-two combat did not resume after landing recovery.");
        Check(!boss.IsInvulnerable && ControlsEnabled(player) && room.State == DungeonEncounterState.Fighting,
            "The flight left invulnerability, disabled controls, or ended the encounter early.");
        CaptureFrame("phase2_regular_combat", boss, player, hud);
    }

    private static bool TryLanding(BaekhoMapModule module, Boss boss, PlayerController player, out Vector3 position, out Quaternion rotation)
    {
        position = default; rotation = Quaternion.identity;
        var probe = new GameObject("SwordRobotLiveLandingProbe") { hideFlags = HideFlags.HideAndDontSave };
        probe.transform.SetParent(module.transform, false);
        try
        {
            var nav = boss.GetComponent<NavMeshAgent>();
            var filter = new NavMeshQueryFilter { agentTypeID = nav != null ? nav.agentTypeID : 0,
                areaMask = nav != null ? nav.areaMask : NavMesh.AllAreas };
            foreach (float distance in new[] { 3f, 3.5f, 5f })
                for (int i = 0; i < 12; i++)
                {
                    Vector3 direction = Quaternion.Euler(0f, i * 30f, 0f) * boss.transform.forward;
                    if (!NavMesh.SamplePosition(boss.transform.position + direction * distance, out var hit, 2f, filter)) continue;
                    rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(boss.transform.position - hit.position, Vector3.up));
                    probe.transform.SetPositionAndRotation(DungeonFastTravelLanding.PlayerRootAtFloor(hit.position, rotation, player.Controller), rotation);
                    if (DungeonFastTravelLanding.TryResolve(module, probe.transform, player.Controller,
                        out position, out rotation, out _)) return true;
                }
            return false;
        }
        finally { Object.Destroy(probe); }
    }

    private static Capture CaptureFrame(string label, Boss boss, PlayerController player, BossHealthHud hud)
    {
        var state = boss.Animator.GetCurrentAnimatorStateInfo(0);
        var skin = boss.GetComponentInChildren<SkinnedMeshRenderer>();
        var capture = new Capture { label = label, path = Path.Combine(_report.directory, label + ".png"),
            frame = Time.frameCount, phase = boss.PhaseIndex, hp = boss.EnemyStat.CurrentHp,
            hudVisible = hud.IsVisible, hudFill = hud.FillAmount, reviveProgress = boss.ReviveProgress,
            controlsEnabled = ControlsEnabled(player), position = boss.transform.position,
            animation = state.IsName("PhaseDown") ? "PhaseDown" : state.IsName("PhaseRevive") ? "PhaseRevive" : "Combat",
            bones = skin != null ? skin.bones.Where(bone => bone != null).Take(128)
                .Select(bone => boss.transform.InverseTransformPoint(bone.position)).ToArray() : Array.Empty<Vector3>() };
        _report.captures.Add(capture); ScreenCapture.CaptureScreenshot(capture.path);
        return capture;
    }

    private static bool PoseChanged(Capture first, Capture second)
        => first.bones.Length > 0 && first.bones.Length == second.bones.Length
            && first.bones.Where((position, index) => (position - second.bones[index]).sqrMagnitude > .0001f).Any();
    private static bool ControlsEnabled(PlayerController player)
        => player != null && player.isActiveAndEnabled && player.Input != null && player.Input.isActiveAndEnabled
            && player.Movement != null && player.Movement.isActiveAndEnabled && player.Controller != null
            && player.Controller.enabled && player.StatManager != null && player.StatManager.CurrentHp > 0f;
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); _report.checks++; }
    private static async Task Until(Func<bool> condition, float timeout, string message)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + timeout;
        while (!condition())
        {
            if (!Application.isPlaying || Time.realtimeSinceStartupAsDouble > deadline) throw new InvalidOperationException(message);
            await Task.Yield();
        }
        _report.checks++;
    }
    private static async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            int frame = Time.frameCount;
            do { await Task.Yield(); } while (Application.isPlaying && Time.frameCount == frame);
            if (!Application.isPlaying) throw new InvalidOperationException("Play Mode ended during sword robot live verification.");
        }
    }
}
