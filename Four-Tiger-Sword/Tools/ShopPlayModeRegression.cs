// Invoke Start in a running Game Scene via Unity Pipeline run_script.
// Poll Shop.PlayModeRegression. This advances one real stage, then restores test resources.
// No scenes, prefabs, settings or content assets are saved or modified.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using StudioBaekho;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class ShopPlayModeRegression
{
    public const string ResultKey = "Shop.PlayModeRegression";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool _running;
    private static int _checks;

    public static string Start()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode in Game Scene first.");
        if (_running) throw new InvalidOperationException("A shop Play Mode regression is already running.");
        var service = Object.FindFirstObjectByType<StageShopService>();
        if (service == null) throw new InvalidOperationException("StageShopService is not installed.");
        _checks = 0;
        _running = true;
        SessionState.SetString(ResultKey, "RUNNING");
        service.StartCoroutine(Drive(service));
        return "Shop Play Mode regression started; poll " + ResultKey + ".";
    }

    // Flatten nested iterators so generation/wait failures are reported instead of being lost
    // in Unity's coroutine exception log. Disposing Execute always runs its restoration block.
    private static IEnumerator Drive(StageShopService service)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(Execute(service));
        Exception failure = null;
        try
        {
            while (stack.Count > 0)
            {
                bool moved = false;
                object yielded = null;
                try
                {
                    moved = stack.Peek().MoveNext();
                    if (moved) yielded = stack.Peek().Current;
                }
                catch (Exception error) { failure = error; }
                if (failure != null) break;
                if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (yielded is IEnumerator nested) { stack.Push(nested); continue; }
                yield return yielded;
            }
        }
        finally
        {
            while (stack.Count > 0)
            {
                try { (stack.Pop() as IDisposable)?.Dispose(); }
                catch (Exception error) { failure = failure ?? error; }
            }
            _running = false;
            string result = failure == null
                ? $"PASS: shop Play Mode ({_checks} checks): generated safe leaf shop, real kill gold, uGUI purchase/heal, modal input, retry stock and next-stage retention."
                : $"FAIL after {_checks} checks: {failure}";
            SessionState.SetString(ResultKey, result);
            if (failure == null) Debug.Log(result); else Debug.LogError(result);
        }
    }

    private static IEnumerator Execute(StageShopService shop)
    {
        Restore snapshot = null;
        Mouse mouse = null;
        Keyboard keyboard = null;
        RunManager run = null;
        try
        {
            run = shop.GetComponent<RunManager>();
            Check(run != null, "Shop has no RunManager.");
            yield return Ready(run);
            Check(run.CurrentStageIndex < run.definition.chapters[run.CurrentChapterIndex].stages.Length - 1,
                "Start this regression in a normal stage, before its chapter boss.");
            Check(run.definition.TryGetNext(run.CurrentChapterIndex, run.CurrentStageIndex, out int nextChapter, out int nextStage),
                "The fixture requires a next stage.");
            var player = run.player;
            Check(player != null && player.Controller != null && player.Input != null && player.ItemRuntime != null,
                "Player systems are not initialized.");
            Check(shop.Wallet != null && shop.settings != null && shop.CurrentStock != null && shop.ShopPoint != null,
                "Committed stage has no initialized wallet/shop/stock.");
            Check(Mathf.Approximately(Time.timeScale, 1f) && !run.IsShopOpen, "Start with ordinary gameplay running.");
            snapshot = Restore.Capture(run, shop);
            CheckGeneratedShop(run, shop);
            var stage = run.stageManager;
            var firstStock = shop.CurrentStock;
            var item = firstStock.Offers.FirstOrDefault(offer => !offer.IsHealing && !offer.SoldOut
                && offer.Item != null && !player.ItemRuntime.Contains(offer.Item.itemId));
            var heal = firstStock.Offers.Single(offer => offer.IsHealing);
            Check(item != null && item.Price > 0, "The fixture needs an available paid catalog item.");
            int originalGold = shop.Wallet.Gold;
            Check(shop.TryBuy(item.Id) == ShopPurchaseResult.Unavailable && shop.Wallet.Gold == originalGold,
                "A closed shop accepted a purchase.");
            Check(!shop.TryOpen(), "A shop opened from the stage spawn outside interaction range.");

            var combatRoom = stage.Encounters.Rooms.FirstOrDefault(room => room.State == DungeonEncounterState.Waiting
                && room.Members.Count > 0);
            Check(combatRoom != null && combatRoom.BeginEncounter(), "No real ordinary encounter could activate for the reward check.");
            var victim = combatRoom.Members.First(member => member.Enemy != null && !member.IsDead);
            Check(victim.GetComponent<EnemyGoldReward>() != null && victim.IsAwake && victim.Enemy.enabled,
                "Generated encounter members were not wired to gold rewards.");
            var lethal = victim.Enemy.TakeDamage(victim.GetComponent<EnemyStat>().CurrentHp + 100000f, poiseDamage: 0f);
            Check(lethal.Applied && lethal.Killed && victim.HasDied, "Real lethal enemy damage did not confirm a death.");
            Check(shop.Wallet.Gold == originalGold + shop.settings.ordinaryKillGold, "Real ordinary kill paid the wrong gold amount.");
            int afterKill = shop.Wallet.Gold;
            var repeat = victim.Enemy.TakeDamage(100000f, poiseDamage: 0f);
            Check(!repeat.Applied && shop.Wallet.Gold == afterKill, "Repeated lethal damage paid twice.");

            yield return TeleportFeet(player, shop.ShopPoint.position + shop.ShopPoint.forward
                * Mathf.Min(1.75f, shop.settings.interactionDistance * 0.65f), shop.ShopPoint.rotation);
            yield return Frames(2);
            Check(shop.IsInRange && !player.IsStageTransitioning, "Approaching the authored shop point did not enable interaction.");
            var view = shop.GetComponentInChildren<ShopView>(true);
            var hud = shop.GetComponentInChildren<GoldHudView>(true);
            Check(view != null && hud != null, "Temporary shop and gold UI adapters were not instantiated.");
            Check(view.HasBoundaryLayout, "The BoundaryZERO shop layout is not installed.");
            var boundary = Field<ShopView.BoundaryControls>(view, "boundary");
            Check(Field<GameObject>(view, "interactionPrompt").activeSelf, "The nearby interaction prompt is hidden.");
            var cameraInputs = TrackedCameraInputs(player).ToDictionary(input => input, input => input.enabled);
            Check(cameraInputs.Count > 0 && cameraInputs.Values.Any(enabled => enabled), "No active tracked camera input exists in this Game Scene.");
            player.StatManager.Heal(player.StatManager.MaxHp);
            player.Input.AttackBuffer.Set(); player.Input.DashBuffer.Set(); player.Input.SkillBuffer.Set();
            Check(shop.TryOpen(), "A nearby live shop could not open.");
            Check(run.IsShopOpen && run.State == RunState.Playing && player.IsShopPaused, "Browsing changed run state or failed to gate the player.");
            Check(Mathf.Approximately(Time.timeScale, 0f) && Cursor.visible && Cursor.lockState == CursorLockMode.None,
                "Opening did not pause world time and unlock the pointer.");
            Check(player.Input.enabled && player.Controller.enabled && player.Movement.enabled && player.Animator.enabled,
                "Opening disabled core player components.");
            Check(cameraInputs.Keys.All(input => !input.enabled), "Browsing left tracked camera input enabled.");
            Check(!player.Input.AttackBuffer.IsActive && !player.Input.DashBuffer.IsActive && !player.Input.SkillBuffer.IsActive,
                "Opening retained gameplay input buffers.");
            Check(Field<GameObject>(view, "panel").activeSelf && !Field<GameObject>(view, "interactionPrompt").activeSelf,
                "The shop panel/prompt visibility is incorrect.");
            Check(boundary.StockScroll.content.GetComponentsInChildren<ShopOfferView>(false).Length
                == firstStock.Offers.Count(offer => !offer.IsHealing),
                "Stock cards do not match item stock independently of the healing service.");
            Check(shop.TryBuy(heal.Id) == ShopPurchaseResult.FullHealth && !heal.SoldOut,
                "Full-health healing was charged or consumed.");
            Check(!boundary.HealingButton.interactable, "Full-health healing is still an enabled UI purchase.");

            Check(shop.Wallet.TrySpend(shop.Wallet.Gold), "Could not prepare the zero-gold purchase fixture.");
            Check(shop.TryBuy(item.Id) == ShopPurchaseResult.InsufficientGold && !item.SoldOut && shop.Wallet.Gold == 0,
                "Insufficient-gold purchase changed stock or balance.");
            Check(ButtonFor(view, item.Id).interactable, "Unaffordable stock is no longer inspectable.");
            ButtonFor(view, item.Id).onClick.Invoke();
            Check(!item.SoldOut && !player.ItemRuntime.Contains(item.Item.itemId) && shop.Wallet.Gold == 0,
                "Card inspection purchased an unaffordable item.");
            Check(!boundary.DetailAction.interactable && boundary.DetailIcon.sprite == item.Item.icon,
                "Unaffordable inspected stock has an enabled explicit purchase or substituted icon.");
            Check(shop.Wallet.AddGold(item.Price + heal.Price), "Could not fund the UI purchase fixture.");
            Check(boundary.DetailAction.interactable, "Funding the wallet did not refresh the explicit purchase button.");
            boundary.DetailAction.onClick.Invoke();
            Check(item.SoldOut && player.ItemRuntime.Contains(item.Item.itemId), "Clicking the explicit detail action did not grant the item.");
            Check(shop.Wallet.Gold == heal.Price && !boundary.DetailAction.interactable && ButtonFor(view, item.Id).interactable,
                "The purchase charged incorrectly, remained purchasable, or disabled sold-card inspection.");
            Check(shop.TryBuy(item.Id) == ShopPurchaseResult.SoldOut && shop.Wallet.Gold == heal.Price,
                "A duplicate item purchase charged again.");
            float maxHp = player.StatManager.MaxHp;
            player.StatManager.Heal(maxHp);
            player.StatManager.ConsumeHealth(maxHp * 0.6f);
            float hpBeforeHeal = player.StatManager.CurrentHp;
            Check(boundary.HealingButton.interactable, "Damaging the player did not enable the healing UI.");
            boundary.HealingButton.onClick.Invoke();
            Near(player.StatManager.CurrentHp, Mathf.Min(maxHp, hpBeforeHeal + maxHp * shop.settings.healingFraction),
                "The real healing UI restored the wrong amount.");
            Check(heal.SoldOut && shop.Wallet.Gold == 0 && !boundary.HealingButton.interactable,
                "Healing was not sold once for its configured price.");
            Check(shop.TryBuy(heal.Id) == ShopPurchaseResult.SoldOut && shop.Wallet.Gold == 0,
                "A duplicate healing purchase charged again.");
            Check(Field<TMP_Text>(hud, "goldText").text == shop.Wallet.Gold.ToString("N0") + " G",
                "The gold HUD does not reflect the completed purchase.");

            var pausedState = player.StateMachine.CurrentState;
            Vector3 pausedPosition = player.transform.position;
            float pausedHp = player.StatManager.CurrentHp, pausedSp = player.StatManager.CurrentSp;
            uint attackPress = player.Input.AttackPressId;
            mouse = InputSystem.AddDevice<Mouse>("ShopRegressionMouse");
            keyboard = InputSystem.AddDevice<Keyboard>("ShopRegressionKeyboard");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(-1000f, -1000f), buttons = 1 });
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space, Key.E, Key.R, Key.Digit2, Key.LeftShift));
            yield return Frames(4);
            Check(player.StateMachine.CurrentState == pausedState && (player.transform.position - pausedPosition).sqrMagnitude < 0.0001f,
                "Paused browsing advanced a player action or moved the player.");
            Near(player.StatManager.CurrentHp, pausedHp, "HP changed during the world pause.");
            Near(player.StatManager.CurrentSp, pausedSp, "SP regenerated during the world pause.");
            Check(NoBufferedInput(player.Input) && player.Input.AttackPressId == attackPress && !player.Input.IsAttackHeld,
                "Gameplay keys pressed while browsing leaked into gameplay input.");
            Field<Button>(view, "closeButton").onClick.Invoke();
            Check(!run.IsShopOpen && !player.IsShopPaused && Mathf.Approximately(Time.timeScale, 1f),
                "The real close button did not release the shop pause.");
            Check(!Cursor.visible && Cursor.lockState == CursorLockMode.Locked,
                "Closing did not restore the gameplay cursor.");
            Check(cameraInputs.All(pair => pair.Key.enabled == pair.Value), "Closing did not restore tracked camera input ownership.");
            yield return Frames(3);
            Check(NoBufferedInput(player.Input) && !player.Input.IsAttackHeld && player.Input.AttackPressId == attackPress,
                "Held gameplay buttons replayed after closing.");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(-1000f, -1000f) });
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return Frames(3);
            Check(NoBufferedInput(player.Input) && player.Input.AttackPressId == attackPress,
                "The neutral release frame replayed a stale gameplay press.");
            InputSystem.RemoveDevice(mouse); mouse = null;
            InputSystem.RemoveDevice(keyboard); keyboard = null;
            Check(shop.TryOpen() && ReferenceEquals(shop.CurrentStock, firstStock) && item.SoldOut && heal.SoldOut,
                "Closing/reopening rerolled stock or restored sold offers.");
            run.CloseShop();

            int stageSeed = run.CurrentStageSeed;
            int chapterIndex = run.CurrentChapterIndex, stageIndex = run.CurrentStageIndex;
            var oldPoint = shop.ShopPoint;
            int balanceBeforeRetry = shop.Wallet.Gold;
            // Fault-inject the existing runtime failure path, then execute the actual public retry.
            // No asset, definition or generator configuration is changed for this injection.
            typeof(RunManager).GetMethod("OnStageFailure", Hidden).Invoke(run, new object[] { "Shop regression: retry retention probe" });
            Check(run.State == RunState.Error && player.IsStageTransitioning && !run.IsShopOpen,
                "The runtime failure path did not suspend the stage.");
            run.RetryPendingStage();
            yield return Ready(run);
            Check(run.CurrentChapterIndex == chapterIndex && run.CurrentStageIndex == stageIndex && run.CurrentStageSeed == stageSeed,
                "Retry advanced the committed stage or changed its stock seed.");
            Check(ReferenceEquals(shop.CurrentStock, firstStock) && item.SoldOut && heal.SoldOut,
                "A real same-stage layout retry rerolled purchased stock.");
            Check(shop.Wallet.Gold == balanceBeforeRetry && player.ItemRuntime.Contains(item.Item.itemId),
                "A layout retry reset gold or ownership.");
            Check(shop.ShopPoint != null && shop.ShopPoint != oldPoint, "Retry did not replace its generated physical shop.");
            CheckGeneratedShop(run, shop);

            stage = run.stageManager;
            var oldRoot = stage.Result.root;
            stage.Exit.enabled = false; // Retain real arrival detection, control only the exact exit invocation.
            var end = stage.Result.endRoom.GetComponent<DungeonRoomEncounter>();
            Check(end != null && end.State == DungeonEncounterState.Cleared && end.Members.Count == 0,
                "The next-stage fixture expects the installed empty end room.");
            yield return Enter(end, player);
            yield return Frames(2);
            Check(end.WasEntered && stage.IsCleared && stage.Exit.IsAvailable, "Actual end-room arrival did not complete its exit objective.");
            MoveFeet(player, stage.Exit.transform.position);
            Check(stage.Exit.ContainsPlayer(player), "The player did not overlap the actual stage exit.");
            int generation = Field<int>(stage.Exit, "_generation");
            int goldBeforeNext = shop.Wallet.Gold;
            var ownedBeforeNext = player.ItemRuntime.OwnedItems.Select(definition => definition.itemId).ToArray();
            Check(stage.TryUseExit(player, generation), "The completed actual exit rejected progression.");
            yield return Ready(run);
            Check(run.CurrentChapterIndex == nextChapter && run.CurrentStageIndex == nextStage,
                "The actual exit advanced to an unexpected stage.");
            Check(run.player == player && shop.Wallet == player.GetComponent<PlayerWallet>() && shop.Wallet.Gold == goldBeforeNext,
                "Next-stage generation replaced the wallet/player or reset gold.");
            Check(ownedBeforeNext.SequenceEqual(player.ItemRuntime.OwnedItems.Select(definition => definition.itemId)),
                "Next-stage generation lost item ownership.");
            Check(shop.CurrentStock != null && !ReferenceEquals(shop.CurrentStock, firstStock), "The next stage reused the previous shop's inventory instance.");
            Check(shop.CurrentStock.Offers.All(offer => offer.IsHealing || !player.ItemRuntime.Contains(offer.Item.itemId)),
                "New-stage stock offered an already owned item.");
            Check(shop.CurrentStock.Offers.Single(offer => offer.IsHealing).SoldOut == false,
                "The next stage did not receive a fresh healing offer.");
            yield return Frames(3);
            Check(oldRoot == null && !run.IsShopOpen && !player.IsShopPaused, "An old stage root or shop modal survived progression.");
            CheckGeneratedShop(run, shop);
        }
        finally
        {
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            snapshot?.Apply();
        }
    }

    private static void CheckGeneratedShop(RunManager run, StageShopService shop)
    {
        var stage = run.stageManager;
        var result = stage.Result;
        var plan = result?.plan;
        Check(stage.IsReady && stage.Encounters.IsReady && result != null && result.success && result.root == stage.Generator.CurrentRoot,
            "The committed stage does not match its ready generation.");
        Check(plan != null && result.shopRoom != null && plan.shopMapId >= 0, "The generated stage has no dedicated shop role.");
        Check(plan.rooms.Count(room => (room.role & BaekhoRoomRole.Shop) != 0) == 1,
            "The generated plan does not contain exactly one shop room.");
        Check(result.rooms.Count(room => room.GetComponent<BaekhoGeneratedRoom>()?.role == BaekhoRoomRole.Shop) == 1,
            "The generated scene does not contain exactly one shop room.");
        var definition = run.definition.chapters[run.CurrentChapterIndex].stages[run.CurrentStageIndex];
        bool boss = run.CurrentStageIndex == run.definition.chapters[run.CurrentChapterIndex].stages.Length - 1;
        Check(stage.MapCount == definition.ResolveMapCount(run.CurrentStageSeed, boss), "The shop changed the configured generated room count.");
        Check(plan.shopMapId != plan.startMapId && plan.shopMapId != plan.endMapId && plan.shopMapId != plan.bossMapId,
            "Shop reused an endpoint or boss role.");
        Check(plan.links.Count(link => link.fromMapId == plan.shopMapId || link.toMapId == plan.shopMapId) == 1,
            "The shop is not an optional leaf branch.");
        var reachable = new HashSet<int> { plan.startMapId };
        bool changed;
        do
        {
            changed = false;
            foreach (var link in plan.links)
            {
                if (link.fromMapId == plan.shopMapId || link.toMapId == plan.shopMapId) continue;
                if (reachable.Contains(link.fromMapId)) changed |= reachable.Add(link.toMapId);
                if (reachable.Contains(link.toMapId)) changed |= reachable.Add(link.fromMapId);
            }
        } while (changed);
        Check(reachable.Contains(plan.endMapId) && (plan.bossMapId < 0 || reachable.Contains(plan.bossMapId)),
            "Reaching a required endpoint requires entering the shop branch.");
        var encounter = result.shopRoom.GetComponent<DungeonRoomEncounter>();
        Check(result.shopRoom.encounterBudget == 0 && encounter != null && encounter.Members.Count == 0
            && encounter.State == DungeonEncounterState.Cleared, "The safe shop spawned a combat encounter.");
        var anchors = result.shopRoom.GetComponent<StageMapAnchors>();
        Check(anchors != null && anchors.shopPoint == shop.ShopPoint && shop.ShopPoint.IsChildOf(result.shopRoom.transform),
            "The service is not using its generated room's authored shop anchor.");
        Check(shop.ShopPoint.childCount == 1, "The physical shop counter is missing or instantiated more than once.");
    }

    private static Button ButtonFor(ShopView view, string offerId)
    {
        var controls = Field<ShopView.BoundaryControls>(view, "boundary");
        var row = controls.StockScroll.content.GetComponentsInChildren<ShopOfferView>(false)
            .FirstOrDefault(candidate => Field<string>(candidate, "_id") == offerId);
        if (row == null) throw new InvalidOperationException("No visible UI row for " + offerId);
        return Field<Button>(row, "purchaseButton");
    }

    private static IEnumerable<CinemachineInputAxisController> TrackedCameraInputs(PlayerController player)
    {
        foreach (var camera in Object.FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool Tracks(Transform target) => target != null && (target == player.transform || target.IsChildOf(player.transform));
            if (!Tracks(camera.Follow) && !Tracks(camera.LookAt)) continue;
            foreach (var input in camera.GetComponents<CinemachineInputAxisController>()) yield return input;
        }
    }

    private static bool NoBufferedInput(PlayerInputHandler input)
        => !input.AttackBuffer.IsActive && !input.DashBuffer.IsActive && !input.JumpBuffer.IsActive
            && !input.SkillBuffer.IsActive && !input.UltimateBuffer.IsActive && !input.IsForm1Pressed
            && !input.IsForm2Pressed && !input.IsForm3Pressed && !input.IsForm4Pressed
            && !input.IsForm5Pressed && !input.IsFastFormPressed;

    private static IEnumerator TeleportFeet(PlayerController player, Vector3 floor, Quaternion rotation)
    {
        player.BeginStageTransition();
        yield return Frames(3); // Every encounter drops its old swept-entry history while the player is frozen.
        var controller = player.Controller;
        float halfHeight = controller.height * Mathf.Abs(player.transform.lossyScale.y) * 0.5f;
        Vector3 center = floor + Vector3.up * (halfHeight + controller.skinWidth + 0.05f);
        player.CompleteStageTransition(center - rotation * Vector3.Scale(controller.center, player.transform.lossyScale), rotation);
    }

    private static IEnumerator Enter(DungeonRoomEncounter room, PlayerController player)
    {
        var gate = room.Gates.FirstOrDefault(candidate => !candidate.IsLocked);
        Check(gate != null, "The end room has no unlocked doorway.");
        player.BeginStageTransition();
        yield return Frames(3);
        Vector3 outside = gate.transform.TransformPoint(new Vector3(0f, 1.2f, 0.5f));
        Quaternion rotation = gate.transform.rotation;
        player.CompleteStageTransition(outside - rotation * Vector3.Scale(player.Controller.center, player.transform.lossyScale), rotation);
        yield return Frames(2);
        MoveCenter(player, gate.transform.TransformPoint(new Vector3(0f, 1.2f, -3f)));
        yield return Frames(2);
    }

    private static void MoveFeet(PlayerController player, Vector3 floor)
    {
        float halfHeight = player.Controller.height * Mathf.Abs(player.transform.lossyScale.y) * 0.5f;
        MoveCenter(player, floor + Vector3.up * (halfHeight + player.Controller.skinWidth + 0.05f));
    }

    private static void MoveCenter(PlayerController player, Vector3 center)
    {
        bool enabled = player.Controller.enabled;
        player.Controller.enabled = false;
        player.transform.position = center - player.transform.TransformVector(player.Controller.center);
        player.VerticalVelocity = 0f;
        player.Controller.enabled = enabled;
        Physics.SyncTransforms();
    }

    private static IEnumerator Frames(int count)
    {
        int target = Time.frameCount + count;
        yield return Wait(() => Time.frameCount >= target, "The actual player loop stopped advancing.");
    }

    private static IEnumerator Ready(RunManager run)
    {
        yield return Wait(() => run == null || run.State == RunState.Playing || run.State == RunState.Error,
            "Stage generation/encounter preparation timed out.", 180f);
        Check(run != null && run.State == RunState.Playing && run.stageManager.IsReady,
            "Stage preparation failed: " + (run != null ? run.LastError : "Run was destroyed."));
    }

    private static IEnumerator Wait(Func<bool> predicate, string message, float seconds = 20f)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!predicate())
        {
            if (!Application.isPlaying || Time.realtimeSinceStartup > deadline) throw new InvalidOperationException(message);
            yield return null;
        }
    }

    private static T Field<T>(object owner, string name) => (T)FindField(owner, name).GetValue(owner);
    private static void SetField(object owner, string name, object value) => FindField(owner, name).SetValue(owner, value);
    private static FieldInfo FindField(object owner, string name)
    {
        for (Type type = owner.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Hidden);
            if (field != null) return field;
        }
        throw new MissingFieldException(owner.GetType().Name, name);
    }

    private static void Near(float actual, float expected, string message)
        => Check(!float.IsNaN(actual) && Mathf.Abs(actual - expected) <= 0.01f, $"{message} {actual} != {expected}");
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    /// <summary>Restores only resources/input deliberately changed by this fixture. Real progression stays committed.</summary>
    private sealed class Restore
    {
        private RunManager _run;
        private StageShopService _shop;
        private PlayerController _player;
        private string[] _owned;
        private int _gold;
        private float _hp, _sp, _gauge, _regen, _timeScale;
        private Vector3 _position;
        private Quaternion _rotation;
        private int _chapter, _stage;
        private GameObject _generationRoot;
        private ShopStock _stock;
        private bool[] _sold;

        public static Restore Capture(RunManager run, StageShopService shop) => new Restore
        {
            _run = run, _shop = shop, _player = run.player,
            _owned = run.player.ItemRuntime.OwnedItems.Select(item => item.itemId).ToArray(),
            _gold = shop.Wallet.Gold, _hp = run.player.StatManager.CurrentHp, _sp = run.player.StatManager.CurrentSp,
            _gauge = run.player.StatManager.CurrentUltimateGauge, _regen = Field<float>(run.player.StatManager, "_spRegenTimer"),
            _timeScale = Time.timeScale, _position = run.player.transform.position, _rotation = run.player.transform.rotation,
            _chapter = run.CurrentChapterIndex, _stage = run.CurrentStageIndex,
            _generationRoot = run.stageManager.Result.root,
            _stock = shop.CurrentStock, _sold = shop.CurrentStock.Offers.Select(offer => offer.SoldOut).ToArray()
        };

        public void Apply()
        {
            if (_run == null || _player == null || _shop == null) return;
            _run.CloseShop();
            var items = _player.ItemRuntime;
            foreach (var item in items.OwnedItems.ToArray())
                if (!_owned.Contains(item.itemId)) items.TryRemoveItem(item.itemId);
            foreach (string id in _owned) if (!items.Contains(id)) items.TryAddItem(id);
            int gold = _shop.Wallet.Gold;
            if (gold > _gold) _shop.Wallet.TrySpend(gold - _gold);
            else if (gold < _gold) _shop.Wallet.AddGold(_gold - gold);
            var stats = _player.StatManager;
            if (stats.CurrentHp < _hp) stats.Heal(_hp - stats.CurrentHp);
            else if (stats.CurrentHp > _hp) stats.ConsumeHealth(stats.CurrentHp - _hp);
            if (stats.CurrentSp < _sp) stats.AddSp(_sp - stats.CurrentSp);
            else if (stats.CurrentSp > _sp) stats.TryConsumeSp(stats.CurrentSp - _sp);
            stats.ConsumeAllUltimateGauge(); stats.AddUltimateGauge(_gauge);
            SetField(stats, "_spRegenTimer", _regen);
            for (int i = 0; i < _stock.Offers.Count; i++)
                SetField(_stock.Offers[i], "<SoldOut>k__BackingField", _sold[i]);
            _player.Input.AttackBuffer.Consume(); _player.Input.DashBuffer.Consume(); _player.Input.SkillBuffer.Consume();
            _player.Input.JumpBuffer.Consume(); _player.Input.UltimateBuffer.Consume(); _player.Input.ConsumeFormInput();
            CombatHitStopService.SetExternalWorldTimeScale(_timeScale);
            if (_run.State == RunState.Playing && _run.stageManager.IsReady)
            {
                bool sameStage = _run.CurrentChapterIndex == _chapter && _run.CurrentStageIndex == _stage
                    && _generationRoot != null && _run.stageManager.Result.root == _generationRoot;
                _player.BeginStageTransition();
                _player.CompleteStageTransition(sameStage ? _position : _run.stageManager.PlayerSpawnPosition,
                    sameStage ? _rotation : _run.stageManager.PlayerSpawnRotation);
            }
        }
    }
}
