using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BoundaryZero.SupplyDepot;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Real Hub inputs with an isolated account; never purchases against the player's save.</summary>
public static class HubSupplyDepotPlayModeRegression
{
    private const string ResultKey = "HubSupplyDepot.PlayModeRegression";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool running;
    private static int checks;
    private static string phase;
    private static DateTime deadline;
    private static TaskCompletionSource<bool> tick;
    private static IDisposable previewScope;
    private static Vector3 previewPosition;
    private static Quaternion previewRotation;

    public static string Start()
    {
        RequireHub();
        if (running || previewScope != null) throw new InvalidOperationException("Supply verification is already active.");
        running = true; checks = 0; deadline = DateTime.UtcNow.AddSeconds(40);
        EditorApplication.update += Tick;
        _ = Run();
        return "Supply Depot acceptance started with a memory-only account. Poll Status().";
    }
    public static string Status() => SessionState.GetString(ResultKey, "NOT STARTED");
    private static void Tick() { var next = tick; tick = null; next?.TrySetResult(true); }
    private static Task NextTick() { tick = new TaskCompletionSource<bool>(); return tick.Task; }

    private static async Task Run()
    {
        Keyboard keyboard = null;
        var previousKeyboard = Keyboard.current;
        InputActionAsset actions = null;
        ReadOnlyArray<InputDevice>? previousDevices = null;
        IDisposable scope = null;
        HubSupplyDepotInteraction interaction = null;
        HubSupplyDepotAccount account = null;
        SupplyDepotController controller = null;
        PlayerController player = null;
        Vector3 position = default;
        Quaternion rotation = default;
        string result = null;
        try
        {
            Phase("binding the authored depot to an isolated account");
            interaction = Object.FindFirstObjectByType<HubSupplyDepotInteraction>();
            controller = Object.FindFirstObjectByType<SupplyDepotController>();
            account = Object.FindFirstObjectByType<HubSupplyDepotAccount>();
            player = Object.FindFirstObjectByType<PlayerController>();
            var world = Object.FindFirstObjectByType<HubWorldController>();
            Check(interaction != null && controller != null && account != null && player != null && world != null,
                "Authored depot references are missing.");
            Check(!interaction.IsOpen && !world.IsModalOpen, "Close Hub modals before running acceptance.");
            position = player.transform.position; rotation = player.transform.rotation;
            var profile = PlayerUpgradeProfile.New(); profile.currency = 1000;
            var store = new MemoryStore(profile);
            var service = new PlayerUpgradeService(Resources.Load<PlayerUpgradeSettingsSO>("PlayerUpgradeSettings"), store);
            scope = PlayerUpgradeService.OverrideForTesting(service);
            account.RefreshAccount();
            Check(account.IsReady && controller.catalog.items.Count == 77, "The current 77-item account is unavailable.");
            var ui = Read<SupplyDepotView.Elements>(controller.view, "elements");
            actions = Read<InputActionReference>(player.Input, "_moveAction").action.actionMap.asset;
            previousDevices = actions.devices;
            keyboard = InputSystem.AddDevice<Keyboard>("SupplyDepotAcceptanceKeyboard");
            keyboard.MakeCurrent(); actions.devices = new InputDevice[] { keyboard };
            Keys(keyboard); await Frames(3);
            Check(!interaction.IsInRange && !interaction.TryOpen(), "Depot opened outside its station radius.");

            Phase("opening with F without spending on the same press");
            var near = interaction.InteractionPosition; near.y = position.y;
            Teleport(player, near, rotation); await Frames(3);
            Check(interaction.IsInRange, "The authored supply anchor is unreachable.");
            Keys(keyboard, Key.F); await Wait(() => interaction.IsOpen, "F did not open the depot.");
            await Frames(3);
            Check(!controller.IsDrawing && service.Balance == 1000 && store.Writes == 0,
                "Opening/holding F purchased an item.");
            Check(world.IsModalOpen && player.IsShopPaused && Time.timeScale == 0f && Cursor.visible
                && Cursor.lockState == CursorLockMode.None, "The depot did not own gameplay pause and cursor.");
            Check(!Object.FindFirstObjectByType<HubWorkshopInteraction>().TryOpen()
                && !Object.FindFirstObjectByType<PauseMenuPresenter>().Open(), "Another modal opened over the depot.");
            var inputs = Object.FindObjectsByType<CinemachineInputAxisController>(FindObjectsSortMode.None);
            Check(inputs.All(input => !input.enabled), "Player camera inputs remain enabled.");
            Canvas.ForceUpdateCanvases(); Pointer(ui.draw); Pointer(ui.close);
            var before = player.transform.position;
            Keys(keyboard, Key.W, Key.Space); await Frames(4);
            Check(Vector3.Distance(before, player.transform.position) < .01f, "Player moved inside the modal.");
            Keys(keyboard); await Frames(2);

            Phase("rejecting a failed save before animation or debit");
            store.RejectWrites = true;
            ui.draw.onClick.Invoke();
            Check(service.Balance == 1000 && service.CurrentSupplyDepotReceipt == null && !controller.IsDrawing
                && !string.IsNullOrWhiteSpace(account.LastError), "Failed persistence mutated the purchase.");
            store.RejectWrites = false;
            Phase("purchasing once, skipping and restoring the same result");
            Keys(keyboard, Key.F); await Wait(() => controller.IsDrawing, "Second F did not start the purchase.");
            string transaction = service.CurrentSupplyDepotReceipt?.transactionId;
            Check(service.Balance == 900 && store.Writes == 1 && !string.IsNullOrEmpty(transaction),
                "Purchase did not atomically save one paid reward.");
            Check(!controller.TryDraw() && !controller.Close(), "Draw allowed a duplicate purchase or premature close.");
            Keys(keyboard, Key.Escape); await Frames(2);
            Check(interaction.IsOpen && controller.IsDrawing && !world.IsPauseMenuOpen, "Escape interrupted the draw.");
            ui.skip.onClick.Invoke();
            Check(!controller.IsDrawing && service.CurrentSupplyDepotReceipt.transactionId == transaction
                && store.Writes == 1, "Skip changed the committed reward.");
            Keys(keyboard); await Frames(2);
            Keys(keyboard, Key.Escape); await Wait(() => !interaction.IsOpen, "Escape did not close the completed draw.");
            Check(!world.IsModalOpen && !world.IsPauseMenuOpen && !player.IsShopPaused && Time.timeScale > .99f
                && !Cursor.visible && Cursor.lockState == CursorLockMode.Locked, "Close did not restore world controls.");
            Check(inputs.All(input => input.enabled), "Camera inputs were not restored.");
            Keys(keyboard); await Frames(2);
            Check(interaction.TryOpen() && !controller.TryDraw() && ui.resultName.text ==
                controller.catalog.items.Single(item => item.id == service.CurrentSupplyDepotReceipt.itemId).name,
                "Reopening did not retain the purchased reward.");
            ui.close.onClick.Invoke(); await Frames(2);

            Phase("forcing controller disable during a committed draw");
            Check(service.TryClearSupplyDepotReceipt(transaction, out _), "Test receipt could not reset.");
            Check(interaction.TryOpen() && controller.TryDraw(), "Second isolated draw failed.");
            transaction = service.CurrentSupplyDepotReceipt.transactionId;
            controller.enabled = false; await Frames(2);
            Check(!interaction.IsOpen && !world.IsModalOpen && !player.IsShopPaused && Time.timeScale > .99f
                && service.CurrentSupplyDepotReceipt.transactionId == transaction && service.Balance == 800,
                "Forced disable leaked a modal lease or changed its paid reward.");
            controller.enabled = true; await Frames(2);
            Check(interaction.TryOpen() && !controller.TryDraw(), "Controller re-enable forgot its purchase.");
            interaction.Close();
            result = "PASS: " + checks + " checks; F/range/repeat, paid draw and skip, failed save, receipt reopening, modal conflicts, cursor/camera/time recovery and forced disable. Memory-only save.";
        }
        catch (Exception failure) { result = "FAIL during " + phase + ": " + failure; }
        finally
        {
            if (controller != null) controller.enabled = true;
            if (interaction != null) interaction.Close();
            scope?.Dispose();
            if (account != null) account.RefreshAccount();
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            if (actions != null) actions.devices = previousDevices;
            if (player != null) Teleport(player, position, rotation);
            SessionState.SetString(ResultKey, result ?? "FAIL: no result.");
            EditorApplication.update -= Tick; tick = null; running = false;
        }
    }

    public static string BeginPreview(string itemId = null)
    {
        RequireHub(); EndPreview();
        var player = Object.FindFirstObjectByType<PlayerController>();
        var interaction = Object.FindFirstObjectByType<HubSupplyDepotInteraction>();
        previewPosition = player.transform.position; previewRotation = player.transform.rotation;
        var profile = PlayerUpgradeProfile.New(); profile.currency = 900;
        if (itemId != null) { profile.supplyDepotItemId = itemId; profile.supplyDepotTransactionId = "visual-preview"; }
        previewScope = PlayerUpgradeService.OverrideForTesting(new PlayerUpgradeService(
            Resources.Load<PlayerUpgradeSettingsSO>("PlayerUpgradeSettings"), new MemoryStore(profile)));
        Object.FindFirstObjectByType<HubSupplyDepotAccount>().RefreshAccount();
        var near = interaction.InteractionPosition; near.y = previewPosition.y;
        Teleport(player, near, previewRotation);
        if (!interaction.TryOpen()) { EndPreview(); throw new InvalidOperationException("Preview failed to open."); }
        Canvas.ForceUpdateCanvases();
        return "Preview open using an isolated account.";
    }

    public static string EndPreview()
    {
        if (previewScope == null) return "No preview active.";
        Object.FindFirstObjectByType<HubSupplyDepotInteraction>()?.Close();
        previewScope.Dispose(); previewScope = null;
        Object.FindFirstObjectByType<HubSupplyDepotAccount>()?.RefreshAccount();
        var player = Object.FindFirstObjectByType<PlayerController>();
        if (player != null) Teleport(player, previewPosition, previewRotation);
        return "Preview closed; original account and player position restored.";
    }

    private static void RequireHub()
    {
        if (!Application.isPlaying || SceneManager.GetActiveScene().name != "Hub Scene")
            throw new InvalidOperationException("Run in the authored Hub Scene during Play Mode.");
    }
    private static T Read<T>(object owner, string field) => (T)owner.GetType().GetField(field, Hidden).GetValue(owner);
    private static void Teleport(PlayerController player, Vector3 position, Quaternion rotation)
    {
        Vector3 delta = position - player.transform.position;
        bool enabled = player.Controller.enabled; player.Controller.enabled = false;
        player.transform.SetPositionAndRotation(position, rotation); player.VerticalVelocity = 0f;
        player.StateMachine.ChangeState(new PlayerIdleState(player)); player.Controller.enabled = enabled;
        Physics.SyncTransforms();
        foreach (var camera in Object.FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
            if (camera.Follow != null && camera.Follow.IsChildOf(player.transform))
            { camera.OnTargetObjectWarped(camera.Follow, delta); camera.PreviousStateIsValid = false; }
    }
    private static void Pointer(UnityEngine.UI.Button button)
    {
        var rect = (RectTransform)button.transform;
        var pointer = new PointerEventData(EventSystem.current)
            { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
        Check(button.IsInteractable() && hits.Count > 0 && (hits[0].gameObject == button.gameObject
            || hits[0].gameObject.transform.IsChildOf(button.transform)), "Blocked pointer target: " + button.name);
    }
    private static void Keys(Keyboard keyboard, params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    private static void Phase(string value) { phase = value; SessionState.SetString(ResultKey, "RUNNING: " + value); }
    private static void Check(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); checks++; }
    private static async Task Frames(int count)
    { int target = Time.frameCount + count; await Wait(() => Time.frameCount >= target, "Game loop stopped."); }
    private static async Task Wait(Func<bool> condition, string error)
    {
        while (!condition())
        {
            if (!Application.isPlaying || DateTime.UtcNow >= deadline) throw new InvalidOperationException(error);
            await NextTick();
        }
    }
    private sealed class MemoryStore : IPlayerUpgradeStore
    {
        private PlayerUpgradeProfile profile;
        public bool RejectWrites;
        public int Writes;
        public MemoryStore(PlayerUpgradeProfile value) => profile = value.Copy();
        public bool TryLoad(out PlayerUpgradeProfile value, out string error)
        { value = profile.Copy(); error = null; return true; }
        public bool TrySave(PlayerUpgradeProfile value, out string error)
        {
            if (RejectWrites) { error = "보급 저장 실패 테스트"; return false; }
            profile = value.Copy(); Writes++; error = null; return true;
        }
    }
}
