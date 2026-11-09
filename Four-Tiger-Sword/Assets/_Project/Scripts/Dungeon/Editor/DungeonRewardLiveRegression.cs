using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using LabDoor;
using StudioBaekho;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Drives the authored Game Scene in a separate QA run. Arm before loading with an isolated account.</summary>
public static class DungeonRewardLiveRegression
{
    private const string GameScene = "Assets/_Project/Scenes/Game Scene.unity";
    private const string NormalSettings = "Assets/_Game/RunProgression/Settings/NormalStage.asset";
    private static readonly int IdleHash = Animator.StringToHash("Idle");
    private static readonly int RunHash = Animator.StringToHash("Run");
    private static RunDefinitionSO _definition;
    private static BaekhoDungeonSettings _normalSettings;
    private static RunManager _run;
    private static DungeonRoomEncounter _room;
    private static DungeonRewardChest _chest, _abandonedChest;
    private static GameObject _oldRoot;
    private static DungeonEncounterGate _gate;
    private static BoxCollider _threshold;
    private static bool _doorClosed, _expectGold, _chestArmed;
    private static int _goldAtOpen, _expectedWave, _seed;
    private static Keyboard _keyboard;
    private static string _action = "idle";

    public static string ConfigureNextGameScene(int seed = 12345)
    {
        Check(Application.isPlaying, "Enter Play Mode first. Load the next Game Scene under an isolated upgrade account.");
        _seed = seed;
        SceneManager.sceneLoaded -= ConfigureScene;
        SceneManager.sceneLoaded += ConfigureScene;
        EditorApplication.playModeStateChanged -= OnPlayModeState;
        EditorApplication.playModeStateChanged += OnPlayModeState;
        return $"Armed the next authored Game Scene for a transient two-stage QA run, seed {seed}.";
    }

    private static void ConfigureScene(Scene scene, LoadSceneMode mode)
    {
        if (scene.path != GameScene) return;
        SceneManager.sceneLoaded -= ConfigureScene;
        _run = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<RunManager>()).Single();
        var bossStage = _run.definition.chapters.Last().stages.Last();
        _normalSettings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<BaekhoDungeonSettings>(NormalSettings));
        _normalSettings.hideFlags = HideFlags.DontSave;
        _normalSettings.mapCount = 6; // Three ordinary rooms for item, exhausted and deliberately abandoned chests.
        _definition = ScriptableObject.CreateInstance<RunDefinitionSO>();
        _definition.hideFlags = HideFlags.DontSave;
        _definition.chapters = new[] { new ChapterDefinition { displayName = "QA", stages = new[]
        {
            new StageDefinition { displayName = "QA Normal", dungeonSettings = _normalSettings,
                enemyCatalog = bossStage.enemyCatalog, mapCountVariation = 0 }, bossStage
        } } };
        Check(_definition.Validate(out string error), error);
        _run.definition = _definition;
        _run.randomizeSeed = false;
        _run.seed = _seed;
        _room = null;
        _chest = null;
        _abandonedChest = null;
        _oldRoot = null;
        _gate = null;
        _threshold = null;
        _chestArmed = false;
        _action = "scene loaded";
    }

    public static string Attach()
    {
        var run = PlayingRun();
        Check(run.CurrentStageIndex == 0 && run.stageManager.Result.bossRoom == null,
            "The authored ordinary QA stage must be active first.");
        Check(OrdinaryRooms().Length >= 3, "The QA layout needs three ordinary encounters.");
        return "Attached to the live, generated Game Scene. " + Status();
    }

    public static string Status()
    {
        if (_run == null) return "No armed live Game Scene has loaded.";
        var p = _run.player != null ? _run.player : null;
        var room = _room != null ? _room : null;
        var a = p != null && p.Animator != null && p.Animator.isInitialized ? p.Animator.GetCurrentAnimatorStateInfo(0).shortNameHash : 0;
        var stage = _run.stageManager != null ? _run.stageManager : null;
        return $"Run={_run.State}, stage={_run.CurrentStageIndex + 1}/{_run.definition.TotalStages}, seed={_run.RunSeed}, layout={stage?.Result?.seed}, "
            + $"stageReady={stage?.IsReady}, room={room?.name ?? "none"} {room?.State}, wave={room?.CurrentWaveNumber}/{room?.WaveCount}, alive={room?.AliveCount}, "
            + $"chests={RoomChests().Length}, rewardOpen={_run.IsRewardOpen}, choice={_run.ItemChoices?.Current?.Id ?? "none"}, "
            + $"gold={p?.GetComponent<PlayerWallet>()?.Gold}, position={p?.transform.position}, grounded={p != null && p.Controller != null && p.IsGround()}, "
            + $"playerState={p?.StateMachine?.CurrentState?.GetType().Name}, animator={(a == IdleHash ? "Idle" : a == RunHash ? "Run" : a.ToString())}, "
            + $"action={_action}, error={_run.LastError ?? "none"}";
    }

    public static string SelectRoom(bool multipleWaves = true)
    {
        var run = PlayingRun();
        Check(!run.HasPendingItemChoice && !run.IsRewardOpen && !run.player.IsGameplayModalPaused, "Close the current reward or gameplay UI first.");
        var candidates = OrdinaryRooms().Where(room => room.State == DungeonEncounterState.Waiting);
        _room = multipleWaves ? candidates.FirstOrDefault(room => room.WaveCount > 1 && room.AliveCount > 1)
            : candidates.OrderBy(room => room.WaveCount).FirstOrDefault();
        Check(_room != null, "No fresh ordinary room matches the requested wave count.");
        _chest = null;
        _chestArmed = false;
        WarpToRoom(_room);
        Check(_room.State == DungeonEncounterState.Fighting || _room.BeginEncounter(), "Actual encounter did not begin: " + _room.Error);
        // Keep the manual inspection window open without changing AI, HP, or death handling.
        _room.MemberSpawned += ReduceDamage;
        foreach (var member in _room.Members) ReduceDamage(member);
        Check(RoomChests().Length == 0 && !run.HasPendingItemChoice, "Reward appeared before the first enemy died.");
        return Status();
    }

    private static void ReduceDamage(DungeonEncounterMember member)
        => member?.Enemy?.EnemyStat?.AddModifier(EnemyStatType.ATK, 0f, -.99f);

    public static string DefeatWave(bool leaveOne = false)
    {
        var run = PlayingRun();
        Check(_room != null && _room.State == DungeonEncounterState.Fighting, "Select a live room first.");
        int wave = _room.CurrentWaveNumber;
        var alive = _room.Members.Where(member => member != null && member.IsAwake && !member.HasDied && !member.IsDead).ToArray();
        Check(alive.Length > (leaveOne ? 1 : 0), "Wait for the active wave to spawn, or use leaveOne=false for its final enemy.");
        int count = alive.Length - (leaveOne ? 1 : 0);
        for (int i = 0; i < count; i++)
        {
            var hit = alive[i].Enemy.TakeDamage(1000000f, poiseDamage: 0f, isParryable: false);
            Check(hit.Killed && alive[i].HasDied, "A live enemy did not run its real lethal damage path: " + alive[i].name);
            bool finalKill = !leaveOne && wave == _room.WaveCount && i == count - 1;
            if (!finalKill)
                Check(RoomChests().Length == 0 && !run.HasPendingItemChoice && !run.IsRewardOpen,
                    "A reward appeared before all enemies and waves were defeated.");
        }
        if (!leaveOne && wave == _room.WaveCount)
        {
            Check(_room.WasCombatClearedByDefeat && _room.State == DungeonEncounterState.Cleared,
                "The actual encounter did not confirm its final wave.");
            _room.MemberSpawned -= ReduceDamage;
            var chests = RoomChests();
            Check(chests.Length == 1 && !run.HasPendingItemChoice && !run.IsRewardOpen, "Final defeat must reserve one unopened chest.");
            _chest = chests[0];
            _action = "one chest created after final defeat";
        }
        else
        {
            _expectedWave = leaveOne ? wave : wave + 1;
            _action = leaveOne ? "no reward with one enemy alive" : $"no reward before wave {_expectedWave}";
        }
        return Status();
    }

    public static string LeaveRoom()
    {
        var run = PlayingRun();
        Check(_chest != null && !run.HasPendingItemChoice, "Clear the room and leave its chest unopened first.");
        Check(run.player.TryFastTravel(run.stageManager.PlayerSpawnPosition, run.stageManager.PlayerSpawnRotation), "Failed to leave the room.");
        Check(Vector3.Distance(run.player.transform.position, _chest.transform.position) > 2.5f && RoomChests().Single() == _chest,
            "The player did not leave range, or the same-stage chest was lost.");
        _action = "left unopened chest in room";
        return Status();
    }

    public static string ApproachChest(bool exhaustCatalog = false)
    {
        var run = PlayingRun();
        Check(_chest != null && RoomChests().Single() == _chest && !run.HasPendingItemChoice, "No unopened room chest is available.");
        var p = run.player;
        if (exhaustCatalog)
        {
            var items = p.GetComponent<PlayerItemRuntime>();
            foreach (var item in items.Catalog.items)
                if (!items.Contains(item.itemId)) Check(items.TryAddItem(item.itemId), "Cannot exhaust the real catalog: " + item.itemId);
        }
        Vector3 target = _chest.transform.position + _chest.transform.forward * 1.3f;
        var module = _room.GetComponent<BaekhoMapModule>();
        var roots = DungeonFastTravelLanding.GroundRoots(module);
        var hits = Physics.RaycastAll(target + Vector3.up * .8f, Vector3.down, 2f, ~0, QueryTriggerInteraction.Ignore);
        var ground = hits.OrderBy(hit => hit.distance).FirstOrDefault(hit => DungeonFastTravelLanding.OwnsGround(hit.collider.transform, module.transform, roots));
        if (ground.collider != null)
        {
            var rotation = Quaternion.LookRotation(-_chest.transform.forward);
            Check(p.TryFastTravel(DungeonFastTravelLanding.PlayerRootAtFloor(ground.point, rotation, p.Controller), rotation),
                "The live player rejected the chest approach pose.");
        }
        else WarpToRoom(_room);
        Check(Vector3.Distance(p.transform.position, _chest.transform.position) <= 2.5f, "Player is outside the chest interaction range.");
        _expectGold = exhaustCatalog;
        _goldAtOpen = p.GetComponent<PlayerWallet>().Gold;
        _chestArmed = true;
        _action = "waiting for F";
        return Status();
    }

    public static string VerifyOpened()
    {
        var run = PlayingRun();
        Check(_chestArmed, "Approach and open a chest first.");
        var choice = run.ItemChoices.Current;
        int gold = run.player.GetComponent<PlayerWallet>().Gold;
        Check(_chest == null && RoomChests().Length == 0, "The used chest is still present. Wait for its animation or check whether F reached the game.");
        if (_expectGold)
            Check(gold == _goldAtOpen + 10 && choice == null && !run.IsRewardOpen, "Exhausted chest must pay exactly ten gold with no modal.");
        else
            Check(gold == _goldAtOpen && choice?.Kind == RunItemChoiceKind.Room && choice.Candidates.Count > 0 && run.IsRewardOpen,
                "The actual item reward modal did not open with current candidates.");
        _action = _expectGold ? "PASS: 10 gold, no modal" : "PASS: actual item modal opened";
        return Status();
    }

    public static string TapF()
    {
        var run = PlayingRun();
        Check(_chestArmed && _keyboard == null, "Approach a chest and wait for any previous keyboard action first.");
        run.StartCoroutine(TapKey(Key.F));
        return "Queued F through the live Unity Input System.";
    }

    public static string PrepareDoor(bool closed)
    {
        var run = PlayingRun();
        Check(_room != null && _room.State == DungeonEncounterState.Cleared && !run.player.IsGameplayModalPaused,
            "Use a cleared ordinary room without any open modal.");
        _gate = _room.Gates.FirstOrDefault(gate => gate.DoorRoot != null && gate.DoorRoot.transform.Find("Frame_Threshold") != null);
        Check(_gate != null, "No real generated threshold was found.");
        _threshold = _gate.DoorRoot.transform.Find("Frame_Threshold").GetComponent<BoxCollider>();
        _doorClosed = closed;
        if (closed)
        {
            _gate.Lock();
            _gate.DoorRoot.GetComponent<SlidingDoor>().SetOpenImmediate(false);
        }
        else _gate.Open(true);
        var local = _threshold.center + new Vector3(0f, _threshold.size.y * .5f, closed ? _threshold.size.z * .37f : 0f);
        var floor = _threshold.transform.TransformPoint(local);
        var facing = Quaternion.LookRotation(_threshold.transform.right);
        Check(run.player.TryFastTravel(DungeonFastTravelLanding.PlayerRootAtFloor(floor, facing, run.player.Controller), facing),
            "The live player rejected the threshold test pose.");
        _action = $"placed on {(closed ? "closed" : "open")} generated threshold";
        return Status();
    }

    public static string ProbeDoorMovement()
    {
        var run = PlayingRun();
        Check(_threshold != null && _keyboard == null, "Prepare an open or closed threshold first.");
        run.StartCoroutine(MoveAndRelease(run.player));
        return "Driving real W movement along the threshold, then releasing it.";
    }

    public static string VerifyDoor()
    {
        var p = PlayingRun().player;
        var state = p.Animator.GetCurrentAnimatorStateInfo(0);
        var feet = p.Controller.bounds.center;
        feet.y = p.Controller.bounds.min.y;
        Check(_threshold != null && _gate.IsLocked == _doorClosed && p.IsGround()
            && _threshold.Raycast(new Ray(feet + Vector3.up * .25f, Vector3.down), out _, .45f),
            "The player is not supported by the selected real door threshold.");
        Check(p.Input.MoveInput.sqrMagnitude < .01f && p.StateMachine.CurrentState is PlayerIdleState && state.shortNameHash == IdleHash,
            "The released player did not settle into both Idle state and Idle animation.");
        return "PASS: released movement on real door threshold. " + Status();
    }

    public static string AdvanceWithUnopenedChest()
    {
        var run = PlayingRun();
        Check(_chest != null && !run.HasPendingItemChoice && !run.IsRewardOpen, "Leave a fresh cleared-room chest unopened first.");
        Check(run.CurrentStageIndex == 0 && run.stageManager.Exit != null && run.stageManager.Exit.IsAvailable, "The real first-stage exit is unavailable.");
        _oldRoot = run.stageManager.Result.root;
        _abandonedChest = _chest;
        var exit = run.stageManager.Exit.transform;
        var p = run.player;
        Check(p.TryFastTravel(DungeonFastTravelLanding.PlayerRootAtFloor(exit.position, exit.rotation, p.Controller), exit.rotation),
            "The real player rejected the exit pose.");
        _action = "standing on the actual StageExit";
        return Status();
    }

    public static string VerifyStageCleanup()
    {
        var run = PlayingRun();
        Check(run.CurrentStageIndex == 1 && run.stageManager.Result.bossRoom != null
            && _oldRoot == null && _abandonedChest == null && !run.HasPendingItemChoice && !run.IsRewardOpen,
            "The next real stage did not discard the previous unopened chest and generation.");
        _action = "PASS: real stage exit and old chest destroyed";
        return Status();
    }

    public static string CheckActivePlacements()
    {
        PlayingRun();
        var type = typeof(DungeonRewardChest).Assembly.GetType("DungeonRewardChestPlacement");
        var find = type.GetMethod("TryFind", BindingFlags.Static | BindingFlags.NonPublic);
        var placements = OrdinaryRooms().Select(room =>
        {
            var module = room.GetComponent<BaekhoMapModule>();
            object[] args = { module, Vector3.zero, Quaternion.identity };
            Check((bool)find.Invoke(null, args), "No reward chest placement in live generated room " + room.name);
            return $"{room.name} at {args[1]}";
        }).ToArray();
        return $"PASS: {placements.Length} live generated room placements\n" + string.Join("\n", placements);
    }

    private static IEnumerator TapKey(Key key)
    {
        _keyboard = InputSystem.AddDevice<Keyboard>();
        try
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            yield return null;
        }
        finally { ReleaseKeyboard(); }
    }

    private static IEnumerator MoveAndRelease(PlayerController player)
    {
        var originalDirection = player.CameraTransform;
        var direction = new GameObject("QA keyboard movement direction");
        direction.transform.rotation = Quaternion.LookRotation(_threshold.transform.right);
        player.CameraTransform = direction.transform;
        var start = player.transform.position;
        _keyboard = InputSystem.AddDevice<Keyboard>();
        bool ran = false;
        float distance = 0f;
        try
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.W));
            for (int frame = 0; frame < 16; frame++)
            {
                yield return null;
                ran |= player.StateMachine.CurrentState is PlayerRunState && player.Input.MoveInput.sqrMagnitude > .01f;
                distance = Vector3.Distance(start, player.transform.position);
                if (distance >= .65f) break;
            }
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return new WaitForSecondsRealtime(.35f);
            if (ran && distance > .1f)
            {
                VerifyDoor();
                _action = $"PASS: live W drove Run {distance:F2} m, then grounded Idle on release.";
            }
            else _action = $"FAIL: queued W drove {distance:F2} m, Run observed={ran}.";
        }
        finally
        {
            if (player != null) player.CameraTransform = originalDirection;
            Object.Destroy(direction);
            ReleaseKeyboard();
        }
    }

    private static void WarpToRoom(DungeonRoomEncounter room)
    {
        var module = room.GetComponent<BaekhoMapModule>();
        var anchor = module.GetComponent<StageMapAnchors>()?.teleportPoint;
        var p = _run.player;
        Check(anchor != null && p.TryFastTravel(anchor.position, anchor.rotation), "Cannot warp to the actual authored safe point in " + room.name);
        _run.stageManager.Encounters.ResetPlayerMotionHistory();
    }

    private static RunManager PlayingRun()
    {
        Check(Application.isPlaying && _run != null && _run.definition == _definition && _run.State == RunState.Playing && _run.stageManager.IsReady,
            "Use the armed Game Scene in a ready, isolated Play Mode run. " + Status());
        return _run;
    }

    private static DungeonRoomEncounter[] OrdinaryRooms() => _run.stageManager.Encounters.Rooms
        .Where(room => room != null && room.GetComponent<BaekhoGeneratedRoom>()?.role == BaekhoRoomRole.Normal).ToArray();
    private static DungeonRewardChest[] RoomChests() => _room != null ? _room.GetComponentsInChildren<DungeonRewardChest>() : Array.Empty<DungeonRewardChest>();
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static void ReleaseKeyboard()
    {
        if (_keyboard == null) return;
        InputSystem.RemoveDevice(_keyboard);
        _keyboard = null;
    }

    private static void OnPlayModeState(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingPlayMode) return;
        SceneManager.sceneLoaded -= ConfigureScene;
        EditorApplication.playModeStateChanged -= OnPlayModeState;
        ReleaseKeyboard();
        if (_definition != null) Object.Destroy(_definition);
        if (_normalSettings != null) Object.Destroy(_normalSettings);
    }
}
