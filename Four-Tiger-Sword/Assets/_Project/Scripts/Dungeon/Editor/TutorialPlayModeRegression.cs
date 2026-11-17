using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using EasternLab.GateAddon;
using StudioBaekho;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Runs the authored tutorial and its checkpoint with an in-memory progression account.</summary>
[InitializeOnLoad]
public static class TutorialPlayModeRegression
{
    private const string TitlePath = "Assets/_Project/Scenes/TitleScene.unity";
    private const string ArmedKey = "Tutorial.PlayModeRegression.Armed";
    private const string StatusKey = "Tutorial.PlayModeRegression.Status";
    private const string StartSceneKey = "Tutorial.PlayModeRegression.StartScene";
    private const string SlotRootKey = "Tutorial.PlayModeRegression.SlotRoot";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static IDisposable _accountScope;
    private static IDisposable _slotScope;

    static TutorialPlayModeRegression()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Studio Baekho/Tutorial/Run Play Mode Regression")]
    private static void StartFromMenu() => Debug.Log(Start());

    public static string Start()
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Start the tutorial regression in Edit Mode.");
        var title = AssetDatabase.LoadAssetAtPath<SceneAsset>(TitlePath);
        Check(title != null && AssetDatabase.LoadAssetAtPath<SceneAsset>(TutorialSetup.ScenePath) != null,
            "Set up the authored tutorial scene before running its regression.");
        SessionState.SetString(StartSceneKey, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) ?? string.Empty);
        SessionState.SetBool(ArmedKey, true);
        Phase("entering the title scene before binding the test account");
        EditorSceneManager.playModeStartScene = title;
        EditorApplication.EnterPlaymode();
        return Status();
    }

    public static string Status() => SessionState.GetString(StatusKey, "NOT STARTED");

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(ArmedKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode) _ = Verify();
        if (change != PlayModeStateChange.EnteredEditMode) return;

        _accountScope?.Dispose();
        _accountScope = null;
        _slotScope?.Dispose();
        _slotScope = null;
        SaveSlotRegression.DeleteTemporaryRoot(SessionState.GetString(SlotRootKey, string.Empty));
        SessionState.EraseString(SlotRootKey);
        string path = SessionState.GetString(StartSceneKey, string.Empty);
        EditorSceneManager.playModeStartScene = path.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
        SessionState.EraseBool(ArmedKey);
        SessionState.EraseString(StartSceneKey);
        if (Status().StartsWith("RUNNING", StringComparison.Ordinal))
            SessionState.SetString(StatusKey, "FAIL: tutorial regression was interrupted.");
    }

    private static async Task Verify()
    {
        try
        {
            // The title screen does not access progression until the player chooses Start or Tutorial.
            Check(SceneManager.GetActiveScene().path == TitlePath, "Title scene did not boot for account isolation.");
            var profile = PlayerUpgradeProfile.New();
            profile.currency = 17;
            profile.supplyDepotItemId = "I01";
            profile.supplyDepotTransactionId = "qa-next-campaign";
            var settings = Resources.Load<PlayerUpgradeSettingsSO>("PlayerUpgradeSettings");
            string slotRoot = SaveSlotRegression.CreateTemporaryRoot();
            SessionState.SetString(SlotRootKey, slotRoot);
            var slots = new SaveSlotService(slotRoot, settings);
            _slotScope = SaveSlotService.OverrideForTesting(slots);
            Check(slots.TryCreate(1, "튜토리얼 검증", out var slot, out string error),
                "Could not prepare the isolated tutorial slot: " + error);
            var file = new PlayerUpgradeFileStore(SaveSlotRegression.ProfilePath(slotRoot, slot));
            Check(file.TrySave(profile, out error), "Could not seed the isolated title slot: " + error);
            var store = new MemoryStore(profile);
            var account = new PlayerUpgradeService(settings, store);
            _accountScope = PlayerUpgradeService.OverrideForTesting(account);
            Check(account.IsReady, "The isolated progression account failed to load.");

            var title = Object.FindFirstObjectByType<TitleManager>();
            Check(title != null, "The authored title has no TitleManager.");
            var startButton = (Button)typeof(TitleManager).GetField("startButton", Hidden).GetValue(title);
            await Wait(() => startButton != null && startButton.interactable, "Title controls never became ready.");
            title.OnStartButton();
            await Wait(() => title.SlotView != null && title.SlotView.IsOpen, "Start did not open the slot selector.");
            title.OnSlotChosen(slot.Id);
            var run = await WaitForRun(null);
            Check(slots.SelectedSlotId == slot.Id && ReferenceEquals(PlayerUpgradeService.Instance, account)
                && account.TutorialStatus == TutorialProgressStatus.InMap && account.Balance == 17,
                "First Start did not begin the tutorial in the isolated account.");

            Phase("checking five fixed rooms and training protections");
            var lesson = run.GetComponent<TutorialDirector>();
            var result = run.stageManager.Result;
            var encounters = result.rooms.Select(room => room.GetComponent<DungeonRoomEncounter>()).ToArray();
            Check(lesson.Step == TutorialStep.Move && result.plan.links.Select(link => (link.fromMapId, link.toMapId))
                .SequenceEqual(new[] { (0, 1), (1, 2), (2, 3), (3, 4) }),
                "The authored scene did not start at the beginning of the five-room path.");
            Check(result.shopRoom == result.rooms[2] && result.bossRoom == null
                && encounters[1].Members.Count == 1 && encounters[1].WaveCount == 1
                && encounters[3].Members.Count == 3 && encounters[3].WaveCount == 1
                && encounters[1].Members.Concat(encounters[3].Members).All(member => member.Enemy != null && member.Enemy.name.StartsWith("Chang", StringComparison.Ordinal)),
                "Encounter roster differs from the authored tutorial.");
            Check(result.plan.links.All(link => encounters[link.fromMapId].GetGate(link.fromSocketIndex).IsLocked),
                "A tutorial gate was open before its lesson.");
            var player = run.player;
            Capture("01-movement");
            await Awaitable.NextFrameAsync();
            var items = player.GetComponent<PlayerItemRuntime>();
            Check(items.OwnedItems.Count == 0, "The tutorial consumed the next campaign's paid supply item.");
            player.StatManager.TakeDamage(1_000_000f, ElementType.ELEMENT_NONE, false);
            Check(player.StatManager.CurrentHp > 0f, "The protected training player could die.");
            player.StatManager.Heal(player.StatManager.MaxHp);

            int writesBeforeSkipFailure = store.Writes;
            store.FailWrites = true;
            run.SkipTutorial();
            store.FailWrites = false;
            Check(run.State == RunState.Playing && lesson.Step == TutorialStep.Move
                && account.TutorialStatus == TutorialProgressStatus.InMap && store.Writes == writesBeforeSkipFailure,
                "A failed skip write changed the tutorial or stopped its run.");
            await MoveToLessonTarget(player, result.startRoom.transform.TransformPoint(lesson.definition.movementTargetLocal), lesson);
            Check(!encounters[0].GetGate(result.plan.links[0].fromSocketIndex).IsLocked, "Movement did not unlock the first gate.");
            WarpToRoom(run, result.rooms[1]);
            Check(encounters[1].BeginEncounter(), "The first live training encounter could not start.");
            await Wait(() => lesson.Step == TutorialStep.Attack, "First room arrival did not start the attack lesson.");
            var reporter = player.GetComponent<TutorialCombatReporter>();
            var trainee = encounters[1].Members[0];
            var target = trainee.Enemy;
            await Wait(() => !target.IsSpawning, "The first training enemy never finished spawning.");
            var protectedHit = target.TakeDamage(1_000_000f, poiseDamage: 0f, isParryable: false);
            Check(protectedHit.Applied && !protectedHit.Killed && target.EnemyStat.CurrentHp > 0f,
                "A required attack could kill the practice target prematurely.");
            // Submit the confirmed hit at the reporter seam. Input buffers below still drive real player actions.
            Invoke(reporter, "ReportHit", TutorialCombatAction.GroundHit, target);
            Expect(lesson, TutorialStep.Dodge);
            player.Input.DashBuffer.Set(2f);
            await Wait(() => lesson.Step == TutorialStep.Skill, "An actual dash did not complete the dodge lesson.");
            player.Input.SkillBuffer.Set(2f);
            await Wait(() => lesson.Step == TutorialStep.AirAttack, "An actual skill did not complete the skill lesson.");
            await Wait(() => player.StateMachine.CurrentState is PlayerIdleState && player.IsGround(), "Skill recovery did not finish.");
            player.Input.JumpBuffer.Set(2f);
            await Wait(() => !player.IsGround() && player.StateMachine.CurrentState is PlayerJumpState, "The real jump did not leave the floor.");
            player.Input.AttackBuffer.Set(2f);
            await Wait(() => player.StateMachine.CurrentState is PlayerAirAttackState, "The real air attack did not start.");
            Invoke(reporter, "ReportHit", TutorialCombatAction.AirHit, target);
            Expect(lesson, TutorialStep.AirAttack);
            await Wait(() => lesson.Step == TutorialStep.Ultimate, "Air attack landing did not complete the exercise.");
            player.Input.UltimateBuffer.Set(2f);
            await Wait(() => lesson.Step is TutorialStep.DefeatTraining or TutorialStep.ClaimChest,
                "Activating the fire ultimate did not advance the exercise.");
            Check(player.FormManager.CurrentForm is FireForm { IsUltimateActive: true }, "The real fire stance was not activated.");
            if (!trainee.HasDied)
                Check(target != null && target.TakeDamage(1_000_000f, poiseDamage: 0f, isParryable: false).Killed,
                    "The final drill did not restore the enemy's ordinary defeat path.");
            Check(trainee.HasDied && encounters[1].WasCombatClearedByDefeat,
                "The first room did not record a confirmed defeat.");

            Phase("claiming the authored chest and travelling to the visited start room");
            await Wait(() => lesson.Step == TutorialStep.ClaimChest, "First-room clear did not offer the chest.");
            var chest = result.rooms[1].GetComponentInChildren<DungeonRewardChest>();
            Check(chest != null, "Confirmed defeat did not create the first-room chest.");
            var available = typeof(DungeonRewardChest).GetProperty("IsAvailable", Hidden);
            await Wait(() => chest != null && (bool)available.GetValue(chest), "The chest could not find a valid spawn and finish appearing.");
            bool reached = false;
            foreach (var direction in new[] { chest.transform.forward, chest.transform.right, -chest.transform.forward, -chest.transform.right })
            {
                if (!TryWarpToFloor(run, result.rooms[1], chest.transform.position + direction * 1.3f,
                        Quaternion.LookRotation(-direction))
                    || !(bool)Invoke(run, "CanUseRewardChest", chest)) continue;
                reached = true;
                break;
            }
            Check(reached, "The placed reward chest has no reachable interaction side.");
            await Wait(() => (bool)Invoke(run, "TryOpenRewardChest", chest), "The real chest never became interactable.");
            await Wait(() => run.IsRewardOpen && run.ItemChoices.Current != null, "The real item reward modal did not open.");
            var rewardView = run.GetComponentInChildren<RunItemChoiceView>();
            var rewardWindow = rewardView != null
                ? (CanvasGroup)typeof(RunItemChoiceView).GetField("_windowGroup", Hidden).GetValue(rewardView)
                : null;
            await Wait(() => rewardView != null && rewardView.IsOpen && rewardWindow != null && rewardWindow.interactable,
                "The item reward modal did not finish opening.");
            Capture("04-chest-reward");
            await Awaitable.NextFrameAsync();
            var choice = run.ItemChoices.Current;
            Check(choice.Candidates.Select(item => item.itemId).SequenceEqual(new[] { "I01" })
                && !run.ItemChoices.TrySkip(choice.Id) && run.ItemChoices.TryClaim(choice.Id, "I01") && items.Contains("I01"),
                "The chest did not require and grant the configured tutorial item.");
            Expect(lesson, TutorialStep.OpenMap);
            await Wait(() => !run.IsRewardOpen, "Reward modal did not release gameplay.");
            var map = Object.FindFirstObjectByType<DungeonMinimapPresenter>();
            Check(map != null, "The authored full map is missing.");
            map.ToggleFullMap();
            Check(map.IsFullMapOpen, "The actual map did not open.");
            Expect(lesson, TutorialStep.TravelToStart);
            await Wait(() => !map.FullMapView.IsAnimating, "Full map opening did not finish.");
            Capture("02-full-map");
            await Awaitable.NextFrameAsync();
            Invoke(map, "OnRoomClicked", 0);
            Expect(lesson, TutorialStep.ReachShop);
            await Wait(() => !run.IsMapOpen, "Fast travel did not dismiss the map.");
            Check(map.Model.CurrentAreaIndex == map.Model.GetRoomAreaIndex(0), "The player did not land in the visited start room.");

            Phase("committing actual purchase, sale and fusion transactions");
            var shop = run.GetComponent<StageShopService>();
            WarpToFloor(run, result.shopRoom, shop.ShopPoint.position + shop.ShopPoint.forward * 1.5f,
                Quaternion.LookRotation(-shop.ShopPoint.forward));
            await Wait(() => lesson.Step == TutorialStep.Buy, "Entering the shop did not prepare the practice kit.");
            Check(items.Contains("I05") && shop.Wallet.Gold >= 40 && shop.TryOpen(), "The live shop could not begin the purchase lesson.");
            var shopView = run.GetComponentInChildren<ShopView>();
            await Wait(() => shopView != null && shopView.IsInteractionReady, "The shop did not finish opening.");
            Capture("03-shop");
            await Awaitable.NextFrameAsync();
            var offer = shop.CurrentStock.Offers.Single(item => item.Item?.itemId == "I04");
            Check(shop.TrySell("I01") == ShopSaleResult.Unavailable, "Selling was allowed before the purchase lesson.");
            await Wait(() => shop.TryBuy(offer.Id) == ShopPurchaseResult.Success, "The configured practice purchase did not commit.");
            Expect(lesson, TutorialStep.Sell);
            Check(shop.TrySell("I01") == ShopSaleResult.Success, "The actual practice sale did not commit.");
            Expect(lesson, TutorialStep.Fuse);
            var fusion = shop.TryFuse("I04", "I05");
            if (fusion == ShopFusionResult.ChoiceRequired)
            {
                var pending = (ShopFusionChoice)typeof(StageShopService).GetField("_fusionChoice", Hidden).GetValue(shop);
                Check(pending?.Candidates.Count > 0, "Fusion asked for a choice without offering any candidates.");
                fusion = shop.TryCommitFusionChoice(pending.Candidates[0].itemId);
            }
            Check(fusion == ShopFusionResult.Success, "The actual practice fusion did not commit.");
            Expect(lesson, TutorialStep.EnterFreeCombat);
            run.CloseShop();
            await Wait(() => TutorialSession.PendingCheckpoint != null, "Closing the shop did not capture a checkpoint.");
            var checkpoint = TutorialSession.PendingCheckpoint;
            Check(checkpoint.itemIds.SequenceEqual(items.OwnedItems.Select(item => item.itemId)) && checkpoint.gold == shop.Wallet.Gold,
                "Checkpoint missed the actual post-transaction inventory or balance.");

            Phase("retrying the second combat encounter after a real defeat");
            WarpToRoom(run, result.rooms[3]);
            Check(encounters[3].BeginEncounter(), "The second live encounter could not start.");
            await Wait(() => lesson.Step == TutorialStep.FreeCombat, "Room 2 did not start free combat.");
            player.StatManager.TakeDamage(1_000_000f, ElementType.ELEMENT_NONE, false);
            await Wait(() => run.State == RunState.Lost && !run.IsDeathPresentationPlaying, "A real defeat did not offer the checkpoint retry.");
            Check(run.CanRetryTutorialCheckpoint && account.Balance == 17
                && account.CurrentSupplyDepotReceipt?.transactionId == "qa-next-campaign",
                "Tutorial defeat changed the permanent account or lost its retry.");
            run.RetryTutorialCheckpoint();
            var retry = await WaitForRun(run);
            var restored = retry.stageManager.Result;
            var first = restored.rooms[1].GetComponent<DungeonRoomEncounter>();
            var second = restored.rooms[3].GetComponent<DungeonRoomEncounter>();
            Check(retry.GetComponent<TutorialDirector>().Step == TutorialStep.FreeCombat
                && first.State == DungeonEncounterState.Cleared && first.Members.Count == 0
                && first.GetComponentInChildren<DungeonRewardChest>() == null
                && second.State == DungeonEncounterState.Fighting && second.AliveCount == 3
                && retry.player.StatManager.CurrentHp == retry.player.StatManager.MaxHp,
                "Checkpoint did not reset only the second encounter and the player.");
            Check(checkpoint.itemIds.SequenceEqual(retry.player.GetComponent<PlayerItemRuntime>().OwnedItems.Select(item => item.itemId))
                && checkpoint.gold == retry.player.GetComponent<PlayerWallet>().Gold && account.Balance == 17,
                "Retry lost the committed shop results or granted permanent rewards.");
            Phase("clearing the second room and walking through the actual exit portal");
            var retryLesson = retry.GetComponent<TutorialDirector>();
            var finalLink = restored.plan.links.Single(link => link.toMapId == restored.plan.endMapId);
            var finalGate = second.GetGate(finalLink.fromSocketIndex);
            Check(finalGate.IsLocked && !retry.stageManager.Exit.IsAvailable, "The exit was accessible during the second fight.");
            await Wait(() => second.Members.All(member => member.Enemy != null && !member.Enemy.IsSpawning),
                "Second-room enemies did not finish spawning after the retry.");
            foreach (var member in second.Members)
                Check(member.Enemy.TakeDamage(1_000_000f, poiseDamage: 0f, isParryable: false).Killed,
                    "An ordinary second-room enemy did not die on lethal damage.");
            await Wait(() => retryLesson.Step == TutorialStep.ReachExit && !finalGate.IsLocked,
                "Confirmed defeat did not unlock the clear room.");
            Phase("walking the player capsule across all four generated corridors");
            await WalkAllCorridors(retry);
            int writesBeforePortal = store.Writes;
            await WalkThroughExit(retry);
            Check(retry.State == RunState.Won && !retry.IsUpgradeSaveBlocked
                && account.Balance == 137 && account.TutorialGrantClaimed
                && account.TutorialStatus == TutorialProgressStatus.HubLessons,
                "The verified exit did not grant 120 stones and start the hub lessons.");
            Check(store.Writes == writesBeforePortal + 1 && store.Latest.currency == 137
                && store.Latest.tutorialGrantClaimed && store.Latest.tutorialStatus == TutorialProgressStatus.HubLessons
                && store.Latest.supplyDepotTransactionId == "qa-next-campaign",
                "The reward and hub tutorial progress were not committed together in one write.");
            Check(account.TryCompleteTutorialMap(120, out _) && store.Writes == writesBeforePortal + 1 && account.Balance == 137,
                "A repeated completion credited the once-only support again.");

            Phase("returning to the authored hub and loading its tutorial director");
            retry.ReturnToHub();
            await Wait(() => SceneManager.GetActiveScene().name == "Hub Scene"
                && Object.FindFirstObjectByType<HubWorldController>() != null
                && Object.FindFirstObjectByType<TutorialHubDirector>() != null && !HubReturnTransition.IsActive,
                "The real result transition did not enter the Hub world with its tutorial director.", 45f);
            Check(account.TutorialStatus == TutorialProgressStatus.HubLessons && account.Balance == 137,
                "Returning to the hub changed the first-clear grant or its stored lesson.");
            const string passed = "PASS: first start, failed skip recovery, four physical corridors, training actions, real chest, fast travel, shop transactions, Room 2 death/retry, exit, one-time grant, and Hub transition.";
            SessionState.SetString(StatusKey, passed);
            Debug.Log(passed);
        }
        catch (Exception failure)
        {
            SessionState.SetString(StatusKey, "FAIL: " + failure.Message);
            Debug.LogException(failure);
        }
        finally
        {
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }
    }

    private static async Task<RunManager> WaitForRun(RunManager previous)
    {
        RunManager found = null;
        await Wait(() =>
        {
            found = Object.FindFirstObjectByType<RunManager>();
            if (found == null || ReferenceEquals(found, previous)) return false;
            Check(found.State != RunState.Error, "Tutorial stage failed to prepare: " + found.LastError);
            return found.IsTutorial && found.State == RunState.Playing && found.stageManager.IsReady;
        }, "Did not enter a prepared tutorial run.", 120f);
        return found;
    }

    private static async Task MoveToLessonTarget(PlayerController player, Vector3 target, TutorialDirector lesson)
    {
        var direction = new GameObject("QA tutorial movement direction");
        var originalCamera = player.CameraTransform;
        var keyboard = InputSystem.AddDevice<Keyboard>();
        try
        {
            Vector3 delta = target - player.transform.position;
            delta.y = 0f;
            direction.transform.rotation = Quaternion.LookRotation(delta);
            player.CameraTransform = direction.transform;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            await Wait(() => lesson.Step == TutorialStep.EnterTraining, "Moving with W did not complete the start-room exercise.");
        }
        finally
        {
            if (keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (player != null) player.CameraTransform = originalCamera;
            Object.Destroy(direction);
        }
    }

    private static void WarpToRoom(RunManager run, BaekhoMapModule room)
    {
        var anchor = room.GetComponent<StageMapAnchors>()?.teleportPoint;
        Check(anchor != null && run.player.TryFastTravel(anchor.position, anchor.rotation),
            "Could not stage the QA player at the authored point in " + room.name);
        run.stageManager.Encounters.ResetPlayerMotionHistory();
    }

    private static void WarpToFloor(RunManager run, BaekhoMapModule room, Vector3 probe, Quaternion rotation)
        => Check(TryWarpToFloor(run, room, probe, rotation), "Failed to place the QA player on the floor in " + room.name);

    private static bool TryWarpToFloor(RunManager run, BaekhoMapModule room, Vector3 probe, Quaternion rotation)
    {
        var roots = DungeonFastTravelLanding.GroundRoots(room);
        var hit = Physics.RaycastAll(probe + Vector3.up * .8f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore)
            .OrderBy(value => value.distance)
            .FirstOrDefault(value => DungeonFastTravelLanding.OwnsGround(value.collider.transform, room.transform, roots));
        if (hit.collider == null) return false;
        var player = run.player;
        if (!player.TryFastTravel(DungeonFastTravelLanding.PlayerRootAtFloor(hit.point, rotation, player.Controller), rotation))
            return false;
        run.stageManager.Encounters.ResetPlayerMotionHistory();
        return true;
    }

    private static async Task WalkThroughExit(RunManager run)
    {
        var stage = run.stageManager;
        var exit = stage.Exit;
        var player = run.player;
        var end = stage.Result.endRoom.GetComponent<DungeonRoomEncounter>();
        Check(end.WasEntered, "Walking through the end doorway did not register room arrival.");
        await Wait(() => exit.IsAvailable && exit.Activation.CanTraverse, "The end portal did not open on arrival.");

        var transit = exit.Transit;
        var floor = transit.transform.TransformPoint(new Vector3(0f, 0f, transit.crossingPlaneOffset + 1f));
        floor.y = exit.transform.position.y;
        var portalFacing = Quaternion.LookRotation(-transit.transform.forward);
        GateTransit.Teleport(player.transform, player.Controller, null,
            DungeonFastTravelLanding.PlayerRootAtFloor(floor, portalFacing, player.Controller), portalFacing);
        player.VerticalVelocity = 0f;
        player.Controller.Move(Vector3.down * .01f);
        await Awaitable.FixedUpdateAsync();
        await Awaitable.NextFrameAsync();
        for (int i = 0; i < 18 && run.State == RunState.Playing; i++)
        {
            player.Controller.Move(-transit.transform.forward * .1f);
            await Awaitable.NextFrameAsync();
        }
        Check(run.State == RunState.Won && transit.TraversalCount == 1,
            "The actual portal did not verify a walking traversal and commit the stage exit.");
    }

    private static async Task WalkAllCorridors(RunManager run)
    {
        var result = run.stageManager.Result;
        var modules = result.rooms.ToDictionary(room => room.GetComponent<BaekhoGeneratedRoom>().instanceId);
        var encounters = modules.ToDictionary(pair => pair.Key, pair => pair.Value.GetComponent<DungeonRoomEncounter>());
        var player = run.player;
        var controller = player.Controller;
        int groundMask = LayerMask.GetMask("Ground");
        const float inside = 2.55f;
        const float stride = .15f;
        foreach (var link in result.plan.links)
        {
            var source = encounters[link.fromMapId].GetGate(link.fromSocketIndex);
            var destination = encounters[link.toMapId].GetGate(link.toSocketIndex);
            var sourceDoor = source.DoorRoot.GetComponentInChildren<LabDoor.SlidingDoor>(true);
            var destinationDoor = destination.DoorRoot.GetComponentInChildren<LabDoor.SlidingDoor>(true);
            string segment = $"room {link.fromMapId} to {link.toMapId}";
            await Wait(() => !source.IsLocked && !destination.IsLocked && sourceDoor.OpenAmount >= .999f
                && destinationDoor.OpenAmount >= .999f, "Doors did not open for " + segment);

            Vector3 forward = Vector3.ProjectOnPlane(destination.transform.position - source.transform.position, Vector3.up).normalized;
            Vector3 approach = source.transform.position - forward * inside;
            Vector3 arrival = destination.transform.position + forward * inside;
            WarpToFloor(run, modules[link.fromMapId], approach, Quaternion.LookRotation(forward));
            await Awaitable.NextFrameAsync();

            int maxSteps = Mathf.CeilToInt(Vector3.Dot(arrival - approach, forward) / stride) + 8;
            float remaining = Vector3.Dot(arrival - controller.bounds.center, forward);
            RaycastHit floor = default;
            for (int step = 0; step < maxSteps && remaining > .04f; step++)
            {
                controller.Move(forward * Mathf.Min(stride, remaining) + Vector3.down * .04f);
                var capsule = controller.bounds;
                var probe = new Vector3(capsule.center.x, capsule.min.y + .25f, capsule.center.z);
                Check(Physics.Raycast(probe, Vector3.down, out floor, .6f, groundMask, QueryTriggerInteraction.Ignore)
                    && floor.collider.transform.IsChildOf(result.root.transform),
                    $"Missing generated floor while walking {segment} near {probe}.");
                remaining = Vector3.Dot(arrival - capsule.center, forward);
            }
            Check(remaining <= .04f, $"The character controller could not cross {segment}.");
            var targetModule = modules[link.toMapId];
            Check(floor.collider != null && DungeonFastTravelLanding.OwnsGround(
                floor.collider.transform, targetModule.transform, DungeonFastTravelLanding.GroundRoots(targetModule)),
                $"The character controller did not reach room {link.toMapId}'s own floor.");
            // Let the room entrance sample the genuine controller displacement.
            await Awaitable.NextFrameAsync();
        }
    }

    private static object Invoke(object target, string method, params object[] arguments)
        => target.GetType().GetMethod(method, Hidden).Invoke(target, arguments);

    private static void Expect(TutorialDirector director, TutorialStep expected)
        => Check(director.Step == expected, $"Expected tutorial step {expected}, got {director.Step}.");

    private static void Phase(string text) => SessionState.SetString(StatusKey, "RUNNING: " + text);

    private static void Capture(string name)
    {
        string folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Temp/TutorialValidation"));
        System.IO.Directory.CreateDirectory(folder);
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, name + ".png"));
    }

    private static async Task Wait(Func<bool> predicate, string message, float seconds = 12f)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + seconds;
        while (!predicate())
        {
            Check(Time.realtimeSinceStartupAsDouble < deadline && EditorApplication.isPlaying, message);
            await Awaitable.NextFrameAsync();
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class MemoryStore : IPlayerUpgradeStore
    {
        private PlayerUpgradeProfile _profile;
        public int Writes { get; private set; }
        public bool FailWrites { get; set; }
        public PlayerUpgradeProfile Latest => _profile;
        public MemoryStore(PlayerUpgradeProfile profile) => _profile = profile.Copy();
        public bool TryLoad(out PlayerUpgradeProfile profile, out string error)
        { profile = _profile.Copy(); error = null; return true; }
        public bool TrySave(PlayerUpgradeProfile profile, out string error)
        {
            if (FailWrites) { error = "Injected tutorial save failure."; return false; }
            _profile = profile.Copy(); Writes++; error = null; return true;
        }
    }
}
