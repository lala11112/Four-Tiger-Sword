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

/// <summary>Opt-in verification of the actual generated Game Scene boss. Writes only project Temp captures/reports.</summary>
public static class BossRevivalLiveRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool running;
    private static Report report = new Report { status = "idle" };

    [Serializable] private sealed class Report
    {
        public string status, error, scene, boss, outputDirectory, reportPath, startedUtc;
        public int checks, initialGold, finalGold, deathEvents, phaseDepletions;
        public float playerInitialHp, playerFinalHp;
        public List<Transition> transitions = new List<Transition>();
        public List<Capture> captures = new List<Capture>();
        public List<string> warnings = new List<string>();
    }

    [Serializable] private sealed class Transition
    {
        public int fromPhase, toPhase;
        public float expectedSeconds, actionSeconds, worldSeconds, realSeconds, restoredHp;
    }

    [Serializable] private sealed class Status
    {
        public string status, error, reportPath, outputDirectory;
        public int checks, deathEvents, phaseDepletions, initialGold, finalGold;
        public List<Transition> transitions;
        public string[] screenshots, warnings;
    }

    [Serializable] private sealed class Capture
    {
        public string label, path, animationState;
        public int phase, frame;
        public bool fileExists, hudVisible, playerControlsEnabled;
        public float actionElapsed, reviveProgress, hp, hudFill, animationNormalizedTime;
        public Vector3 actorPosition;
        public List<Vector3> bonePositions = new List<Vector3>();
    }

    public static string StartPlayMode()
    {
        if (!Application.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("Start live boss verification in idle Game Scene Play Mode.");
        if (running) throw new InvalidOperationException("Live boss verification is already running.");
        running = true;
        report = new Report { status = "running", startedUtc = DateTime.UtcNow.ToString("O") };
        report.outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "BossRevival",
            DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")));
        report.reportPath = Path.Combine(report.outputDirectory, "report.json");
        Directory.CreateDirectory(report.outputDirectory);
        _ = Run();
        return PlayModeStatus();
    }

    public static string PlayModeStatus() => JsonUtility.ToJson(new Status
    {
        status = report.status, error = report.error, checks = report.checks,
        reportPath = report.reportPath, outputDirectory = report.outputDirectory,
        deathEvents = report.deathEvents, phaseDepletions = report.phaseDepletions,
        initialGold = report.initialGold, finalGold = report.finalGold, transitions = report.transitions,
        screenshots = report.captures.Select(capture => capture.path).ToArray(), warnings = report.warnings.ToArray()
    }, true);

    private static async Task Run()
    {
        Boss boss = null;
        PlayerController player = null;
        RunManager run = null;
        float atkAdjustment = 0f;
        bool attackSuppressed = false, teleported = false;
        Vector3 originalPosition = default;
        Quaternion originalRotation = Quaternion.identity;
        Action onDied = null;
        Action<int> onDepleted = null;
        try
        {
            run = Object.FindObjectsByType<RunManager>(FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.isActiveAndEnabled && candidate.State == RunState.Playing);
            Check(run != null, "No active Playing RunManager exists; start the actual Game Scene and prepare the Test_Boss stage first.");
            report.scene = run.gameObject.scene.path;
            Check(report.scene.Replace('\\', '/').EndsWith("/_Project/Scenes/Game Scene.unity", StringComparison.Ordinal),
                "The active run is not in Assets/_Project/Scenes/Game Scene.unity: " + report.scene);
            var stage = run.stageManager;
            Check(stage != null && stage.IsReady && stage.Result != null && stage.Result.success
                && stage.Result.bossRoom != null, "The current ready stage has no generated boss room.");
            var result = stage.Result;
            var roomModule = result.bossRoom;
            var room = roomModule.GetComponent<DungeonRoomEncounter>();
            Check(room != null && room.State == DungeonEncounterState.Waiting && room.Members.Count == 1,
                "A fresh waiting boss encounter with one member is required; current state: " + room?.State);
            boss = room.Members[0].Enemy as Boss;
            Check(boss != null && boss.gameObject.name.StartsWith("Test_Boss", StringComparison.Ordinal),
                "The generated boss-room member is not Test_Boss.");
            report.boss = boss.gameObject.name;
            Check(boss.PhaseConfiguration.Count == 3
                && NearValue(boss.PhaseConfiguration[0].phaseMaxHp, 1680f)
                && NearValue(boss.PhaseConfiguration[1].phaseMaxHp, 1680f)
                && NearValue(boss.PhaseConfiguration[2].phaseMaxHp, 1440f),
                "Test_Boss must be configured with the approved 1680 / 1680 / 1440 HP pools.");
            Check(boss.Data != null && NearValue(boss.Data.phaseDownSeconds, 1f)
                && NearValue(boss.Data.phaseHoldSeconds, .5f) && NearValue(boss.Data.phaseReviveSeconds, 1.5f),
                "Test_Boss down/hold/revive durations must be 1 / .5 / 1.5 seconds.");
            var endEncounter = result.endRoom != null ? result.endRoom.GetComponent<DungeonRoomEncounter>() : null;
            Check(!stage.IsCleared && (endEncounter == null || !endEncounter.WasEntered),
                "The end room was already visited; aborting before combat to avoid a persistent stage-clear reward.");
            player = run.player;
            Check(PlayerControlsEnabled(player), "Player input, movement or CharacterController is disabled before verification.");
            Check(!player.IsInRoomCombat && !player.IsGameplayModalPaused,
                "Leave other room combat and close gameplay modals before starting live verification.");
            report.playerInitialHp = player.StatManager.CurrentHp;
            originalPosition = player.transform.position;
            originalRotation = player.transform.rotation;
            var wallet = player.GetComponent<PlayerWallet>();
            Check(wallet != null, "The real run's player wallet has not initialized.");
            report.initialGold = wallet.Gold;
            Check(boss.Data.rewardGroup != null && boss.Data.rewardGroup.gold == 150,
                "Test_Boss must reference its 150-gold boss reward group.");

            Check(TryNearBossPose(roomModule, boss, player, out var position, out var rotation, out var landingError),
                "Could not resolve a safe near-boss player pose: " + landingError);
            Check(player.TryFastTravel(position, rotation), "The real player rejected the safe same-stage teleport.");
            teleported = true;
            Check(PlayerControlsEnabled(player), "The teleport failed to restore player controls within its own frame.");
            Check(room.BeginEncounter(), "The generated boss encounter could not activate: " + room.Error);
            await Until(() => boss.Brain != null && boss.Animator != null, 10f,
                "The actual Test_Boss did not run Enemy.Start after encounter activation.");
            atkAdjustment = -1f - StatMultiplier(boss.EnemyStat, EnemyStatType.ATK);
            boss.EnemyStat.AddModifier(EnemyStatType.ATK, 0f, atkAdjustment);
            attackSuppressed = true;
            Check(NearValue(boss.EnemyStat.GetStat(EnemyStatType.ATK), 0f),
                "The temporary actor-only ATK modifier did not suppress damage.");
            Check(boss.Animator.HasState(0, Animator.StringToHash("PhaseDown"))
                && boss.Animator.HasState(0, Animator.StringToHash("PhaseRevive")),
                "The real boss Animator is missing PhaseDown or PhaseRevive.");
            var nav = boss.GetComponent<NavMeshAgent>();
            Check(nav != null && nav.enabled && nav.isOnNavMesh, "The actual activated boss has no live NavMeshAgent.");
            await Until(() => boss.DetectedTarget == player.transform && boss.CurrentAction?.IsActive == true, 12f,
                "The actual boss failed to detect the relocated player and begin an attack.");
            var hud = Object.FindObjectsByType<BossHealthHud>(FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.isActiveAndEnabled && candidate.IsVisible);
            Check(hud != null, "The real boss HUD did not become visible after boss-room entry.");
            onDied = () => report.deathEvents++;
            onDepleted = _ => report.phaseDepletions++;
            boss.OnDied += onDied;
            boss.OnPhaseDepleted += onDepleted;
            CaptureFrame("phase1_active", boss, player, hud, 0f);
            await VerifyRevival(boss, player, run, room, hud, wallet, 0, 1680f, 1680f);
            await VerifyRevival(boss, player, run, room, hud, wallet, 1, 1680f, 1440f);

            Check(!stage.IsCleared && (endEncounter == null || !endEncounter.WasEntered),
                "The player entered the end room during verification; aborting before final death to avoid persistent progression.");
            var final = boss.TakeDamage(1000000f, poiseDamage: 0f);
            Check(final.Applied && final.Killed && boss.IsDead && report.deathEvents == 1,
                "Final-phase depletion did not confirm exactly one boss death.");
            await Frames(2);
            Check(room.State == DungeonEncounterState.Cleared && room.BossDefeated && room.AliveCount == 0,
                "Final death did not clear the real boss encounter.");
            Check(room.Gates.Count > 0 && room.Gates.All(gate => gate != null && !gate.IsLocked),
                "The real encounter gates did not open after final death.");
            Check(!hud.IsVisible && NearValue(hud.FillAmount, 0f), "The real boss HUD did not hide after final death.");
            Check(wallet.Gold == report.initialGold + 150, "The actual boss reward was not exactly 150 gold.");
            Check(!boss.TakeDamage(1000000f, poiseDamage: 0f).Applied && report.deathEvents == 1,
                "A hit after final death caused a duplicate death or reward.");
            Check(PlayerControlsEnabled(player), "Player controls were disabled by the boss's final death.");
            Check(run.State == RunState.Playing && !stage.IsCleared && stage.Result == result
                && (endEncounter == null || !endEncounter.WasEntered),
                "Live verification unexpectedly advanced or completed the stage.");
            CaptureFrame("final_death", boss, player, hud, 0f);
            report.finalGold = wallet.Gold;
            report.playerFinalHp = player.StatManager.CurrentHp;
            Check(report.playerFinalHp > 0f, "The real player died during live verification.");
            await Frames(4);
            foreach (var capture in report.captures)
            {
                capture.fileExists = File.Exists(capture.path);
                if (!capture.fileExists) report.warnings.Add("GameView screenshot was not produced: " + capture.path);
            }
            report.status = "passed";
        }
        catch (Exception error)
        {
            report.status = "failed";
            report.error = error.GetBaseException().Message;
            Debug.LogError("[Boss Revival Live Regression] " + error);
        }
        finally
        {
            if (boss != null)
            {
                if (onDied != null) boss.OnDied -= onDied;
                if (onDepleted != null) boss.OnPhaseDepleted -= onDepleted;
                if (attackSuppressed && !boss.IsDead)
                    boss.EnemyStat.RemoveModifier(EnemyStatType.ATK, 0f, atkAdjustment);
            }
            if (teleported && PlayerControlsEnabled(player) && run != null && run.State == RunState.Playing
                && !player.IsInRoomCombat && !player.IsGameplayModalPaused)
            {
                if (!player.TryFastTravel(originalPosition, originalRotation))
                    report.warnings.Add("The player could not return to the original pose after verification.");
            }
            try { File.WriteAllText(report.reportPath, JsonUtility.ToJson(report, true)); }
            catch (Exception error) { report.warnings.Add("Could not write Temp report: " + error.Message); }
            running = false;
        }
    }

    private static async Task VerifyRevival(Boss boss, PlayerController player, RunManager run,
        DungeonRoomEncounter room, BossHealthHud hud, PlayerWallet wallet, int phase, float hp, float nextHp)
    {
        Check(boss.PhaseIndex == phase && NearValue(boss.EnemyStat.CurrentHp, hp)
            && NearValue(boss.EnemyStat.MaxHp, hp), "The actual boss did not start phase " + (phase + 1) + " at full authored HP.");
        int depletions = report.phaseDepletions;
        var action = boss.CurrentAction;
        float actorStart = boss.ActionTime, worldStart = Time.time;
        double realStart = Time.realtimeSinceStartupAsDouble;
        var hit = boss.TakeDamage(1000000f, poiseDamage: 1000f);
        Check(hit.Applied && !hit.Killed && NearValue(hit.HealthDamage, hp)
            && boss.IsTransitioning && !boss.IsDead && boss.EnemyStat.CurrentHp == 0f
            && report.phaseDepletions == depletions + 1 && report.deathEvents == 0,
            "Phase " + (phase + 1) + " depletion did not enter one nonfinal HP0 transition.");
        Check(!boss.TakeDamage(1000000f, poiseDamage: 0f).Applied,
            "A same-frame follow-up hit was accepted during real boss revival.");
        Check(NearValue(hud.FillAmount, 0f) && NearValue(hud.TrailAmount, 0f),
            "The real boss HUD did not immediately empty both fill and trail on phase depletion.");
        Capture down = null, hold = null, rise = null;
        float expected = boss.Data.phaseDownSeconds + boss.Data.phaseHoldSeconds + boss.Data.phaseReviveSeconds;
        double deadline = Time.realtimeSinceStartupAsDouble + 20d;
        while (boss.IsTransitioning)
        {
            await Frames(1);
            Check(run.State == RunState.Playing && room.State == DungeonEncounterState.Fighting && !room.BossDefeated
                && room.IsCombatActive && room.AliveCount == 1 && !room.Members[0].HasDied && !room.Members[0].IsDead,
                "The actual boss encounter ended or lost its member during intermediate revival.");
            Check(PlayerControlsEnabled(player), "Player input/movement/controller was suspended during boss revival.");
            Check(wallet.Gold == report.initialGold && report.deathEvents == 0,
                "The actual boss paid a reward or emitted death during intermediate revival.");
            Check(room.Gates.Count > 0 && room.Gates.All(gate => gate != null && gate.IsLocked),
                "A real encounter gate opened during intermediate revival.");
            Check(hud.IsVisible, "The real boss HUD disappeared during intermediate revival.");
            if (!boss.IsTransitioning) break;
            Check(boss.EnemyStat.CurrentHp == 0f && boss.PhaseIndex == phase,
                "The real boss restored HP or entered the next phase before revival completed.");
            float elapsed = boss.ActionTime - actorStart;
            float tolerance = Mathf.Max(.04f, Time.deltaTime / boss.Data.phaseReviveSeconds + .02f);
            Check(Mathf.Abs(hud.FillAmount - boss.ReviveProgress) <= tolerance,
                "The real HUD refill was not synchronized with the rising animation.");
            if (down == null && elapsed >= boss.Data.phaseDownSeconds * .4f)
                down = CaptureFrame($"phase{phase + 1}_down", boss, player, hud, elapsed);
            if (hold == null && elapsed >= boss.Data.phaseDownSeconds + boss.Data.phaseHoldSeconds * .25f)
                hold = CaptureFrame($"phase{phase + 1}_hold", boss, player, hud, elapsed);
            if (rise == null && boss.ReviveProgress >= .45f)
                rise = CaptureFrame($"phase{phase + 1}_rise", boss, player, hud, elapsed);
            if (Time.realtimeSinceStartupAsDouble > deadline)
                throw new InvalidOperationException("The actual boss revival exceeded 20 seconds; timer or actor clock is stalled.");
        }
        Check(down != null && hold != null && rise != null,
            "The actual revival did not expose each down/hold/rise sample window.");
        Check(down.animationState == "PhaseDown" && hold.animationState == "PhaseDown" && rise.animationState == "PhaseRevive",
            "The real Animator did not use PhaseDown for falling/hold and PhaseRevive for rising.");
        Check(PoseChanged(down, hold) && PoseChanged(hold, rise),
            "The real boss skeleton did not change between falling, held and rising poses.");
        float actionElapsed = boss.ActionTime - actorStart;
        Check(actionElapsed >= expected - .05f && actionElapsed <= expected + 1f,
            "The real boss revival did not complete near the authored 3-second actor timeline: " + actionElapsed);
        Check(boss.PhaseIndex == phase + 1 && !boss.IsDead && NearValue(boss.EnemyStat.CurrentHp, nextHp)
            && NearValue(boss.EnemyStat.MaxHp, nextHp), "The real next phase did not restore its full independent HP pool.");
        Check(action == null || (!action.IsActive && !ParryEventBus.IsTelegraphActive(action)),
            "The real previous-phase attack or telegraph survived revival.");
        await Until(() => boss.CurrentAction?.IsActive == true && boss.DetectedTarget == player.transform,
            12f, "The actual boss did not resume attacks and target acquisition after revival.");
        var nav = boss.GetComponent<NavMeshAgent>();
        Check(nav != null && nav.enabled && nav.isOnNavMesh && PlayerControlsEnabled(player),
            "The actual revived boss or player did not retain usable navigation/controls.");
        report.transitions.Add(new Transition { fromPhase = phase, toPhase = boss.PhaseIndex,
            expectedSeconds = expected, actionSeconds = actionElapsed, worldSeconds = Time.time - worldStart,
            realSeconds = (float)(Time.realtimeSinceStartupAsDouble - realStart), restoredHp = boss.EnemyStat.CurrentHp });
        CaptureFrame($"phase{phase + 2}_active", boss, player, hud, actionElapsed);
    }

    private static bool TryNearBossPose(BaekhoMapModule module, Boss boss, PlayerController player,
        out Vector3 position, out Quaternion rotation, out string reason)
    {
        position = default; rotation = Quaternion.identity; reason = "No collision-free grounded sample was found.";
        var probe = new GameObject("BossRevivalLandingProbe") { hideFlags = HideFlags.HideAndDontSave };
        probe.transform.SetParent(module.transform, false);
        try
        {
            var nav = boss.GetComponent<NavMeshAgent>();
            var filter = new NavMeshQueryFilter { agentTypeID = nav != null ? nav.agentTypeID : 0,
                areaMask = nav != null ? nav.areaMask : NavMesh.AllAreas };
            foreach (float distance in new[] { 2.5f, 3.5f, 5f })
                for (int index = 0; index < 12; index++)
                {
                    Vector3 direction = Quaternion.Euler(0f, index * 30f, 0f) * boss.transform.forward;
                    if (!NavMesh.SamplePosition(boss.transform.position + direction * distance, out var hit, 2f, filter)) continue;
                    rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(boss.transform.position - hit.position, Vector3.up));
                    probe.transform.SetPositionAndRotation(DungeonFastTravelLanding.PlayerRootAtFloor(hit.position, rotation, player.Controller), rotation);
                    if (DungeonFastTravelLanding.TryResolve(module, probe.transform, player.Controller,
                        out position, out rotation, out reason)) return true;
                }
            return false;
        }
        finally { Object.Destroy(probe); }
    }

    private static Capture CaptureFrame(string label, Boss boss, PlayerController player, BossHealthHud hud, float elapsed)
    {
        var capture = new Capture { label = label, path = Path.Combine(report.outputDirectory, label + ".png"),
            phase = boss.PhaseIndex, frame = Time.frameCount, actionElapsed = elapsed, reviveProgress = boss.ReviveProgress,
            hp = boss.EnemyStat.CurrentHp, hudVisible = hud.IsVisible, hudFill = hud.FillAmount,
            playerControlsEnabled = PlayerControlsEnabled(player), actorPosition = boss.transform.position };
        var state = boss.Animator.GetCurrentAnimatorStateInfo(0);
        capture.animationState = state.IsName("PhaseDown") ? "PhaseDown" : state.IsName("PhaseRevive") ? "PhaseRevive" : "Combat";
        capture.animationNormalizedTime = state.normalizedTime;
        var skin = boss.GetComponentInChildren<SkinnedMeshRenderer>();
        if (skin != null) foreach (var bone in skin.bones.Take(128))
            if (bone != null) capture.bonePositions.Add(boss.transform.InverseTransformPoint(bone.position));
        report.captures.Add(capture);
        try { ScreenCapture.CaptureScreenshot(capture.path); }
        catch (Exception error) { report.warnings.Add("GameView capture failed: " + error.Message); }
        return capture;
    }

    private static bool PoseChanged(Capture first, Capture second)
    {
        if (first.bonePositions.Count == 0 || first.bonePositions.Count != second.bonePositions.Count) return false;
        for (int i = 0; i < first.bonePositions.Count; i++)
            if ((first.bonePositions[i] - second.bonePositions[i]).sqrMagnitude > .0001f) return true;
        return false;
    }

    private static float StatMultiplier(EnemyStat stats, EnemyStatType stat)
    {
        var multipliers = (Dictionary<EnemyStatType, float>)typeof(EnemyStat).GetField("multValues", Hidden).GetValue(stats);
        return multipliers.TryGetValue(stat, out float value) ? value : 0f;
    }
    private static bool NearValue(float first, float second) => Mathf.Abs(first - second) <= .01f;
    private static bool PlayerControlsEnabled(PlayerController player)
        => player != null && player.isActiveAndEnabled && player.Input != null && player.Input.isActiveAndEnabled
            && player.Movement != null && player.Movement.isActiveAndEnabled && player.Controller != null
            && player.Controller.enabled && player.StatManager != null && player.StatManager.CurrentHp > 0f;
    private static void Check(bool condition, string message)
    {
        report.checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static async Task Until(Func<bool> condition, float timeoutSeconds, string message)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + timeoutSeconds;
        while (!condition())
        {
            if (!Application.isPlaying || Time.realtimeSinceStartupAsDouble > deadline)
                throw new InvalidOperationException(message);
            await Task.Yield();
        }
        report.checks++;
    }
    private static async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            int frame = Time.frameCount;
            do { await Task.Yield(); } while (Application.isPlaying && Time.frameCount == frame);
            if (!Application.isPlaying) throw new InvalidOperationException("Play Mode ended during live boss verification.");
        }
    }
}
