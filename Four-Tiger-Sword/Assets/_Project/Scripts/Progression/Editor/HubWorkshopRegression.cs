using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Verifies the authored workshop with virtual input and an isolated in-memory account.</summary>
public static class HubWorkshopRegression
{
    private const string ResultKey = "HubWorkshop.PlayModeRegression";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool _running;
    private static int _checks;
    private static string _phase;
    private static DateTime _deadline;
    private static TaskCompletionSource<bool> _nextTick;

    public static string Start()
    {
        if (!Application.isPlaying || SceneManager.GetActiveScene().name != "Hub Scene")
            throw new InvalidOperationException("Start in the authored Hub Scene during Play Mode.");
        if (_running) throw new InvalidOperationException("Workshop regression is already running.");
        _running = true;
        _checks = 0;
        _deadline = DateTime.UtcNow.AddSeconds(20);
        EditorApplication.update += Tick;
        _ = Run();
        return "Workshop acceptance started with an in-memory account. Poll HubWorkshopRegression.Status().";
    }

    public static string Status() => SessionState.GetString(ResultKey, "NOT STARTED");

    private static void Tick()
    {
        var pending = _nextTick;
        _nextTick = null;
        pending?.TrySetResult(true);
    }

    private static Task NextTick()
    {
        _nextTick = new TaskCompletionSource<bool>();
        return _nextTick.Task;
    }

    private static async Task Run()
    {
        Keyboard keyboard = null;
        Mouse mouse = null;
        var previousKeyboard = Keyboard.current;
        var previousMouse = Mouse.current;
        InputActionAsset actions = null;
        ReadOnlyArray<InputDevice>? previousDevices = null;
        IDisposable accountOverride = null;
        PlayerUpgradeService liveService = null;
        PlayerController player = null;
        HubWorkshopInteraction workshop = null;
        HubWorkshopView view = null;
        CinemachineCamera camera = null;
        CinemachineOrbitalFollow orbit = null;
        Vector3 originalPosition = default;
        Quaternion originalRotation = default;
        float yaw = 0f, pitch = 0f;
        bool captured = false;
        string outcome = null;
        try
        {
            Phase("checking authored workshop and isolating the account");
            workshop = Object.FindFirstObjectByType<HubWorkshopInteraction>();
            view = Object.FindFirstObjectByType<HubWorkshopView>();
            player = Object.FindFirstObjectByType<PlayerController>();
            camera = Object.FindFirstObjectByType<CinemachineCamera>();
            Check(workshop != null && view != null && player != null && camera != null,
                "The authored workshop, player or gameplay camera is missing.");
            Check(!workshop.IsOpen, "Close the workshop before starting acceptance.");
            Check(view.Entries != null && view.Entries.Length == 3
                && new[] { StatType.ST_HP, StatType.ST_ATK, StatType.ST_DEF }.All(stat => view.Entries.Any(entry => entry.stat == stat)),
                "The workshop does not expose the three existing upgrade categories.");
            Check(view.UI.purchaseButton != null && view.UI.closeButton != null && view.UI.panel != null,
                "Workshop purchase/close controls are not authored.");
            liveService = PlayerUpgradeService.Instance;
            var settings = liveService.Settings;
            Check(settings != null && settings.Validate(out _), "Installed upgrade settings are invalid.");
            var profile = PlayerUpgradeProfile.New();
            profile.currency = settings.initialCost * 3;
            var store = new MemoryStore(profile);
            var service = new PlayerUpgradeService(settings, store);
            accountOverride = PlayerUpgradeService.OverrideForTesting(service);
            view.Bind(service);

            originalPosition = player.transform.position;
            originalRotation = player.transform.rotation;
            orbit = camera.GetComponent<CinemachineOrbitalFollow>();
            yaw = orbit != null ? orbit.HorizontalAxis.Value : 0f;
            pitch = orbit != null ? orbit.VerticalAxis.Value : 0f;
            captured = true;
            actions = Read<InputActionReference>(player.Input, "_moveAction").action.actionMap.asset;
            previousDevices = actions.devices;
            keyboard = InputSystem.AddDevice<Keyboard>("HubWorkshopAcceptanceKeyboard");
            mouse = InputSystem.AddDevice<Mouse>("HubWorkshopAcceptanceMouse");
            keyboard.MakeCurrent();
            mouse.MakeCurrent();
            actions.devices = new InputDevice[] { keyboard, mouse };
            Keys(keyboard);
            await Frames(3);

            Phase("rejecting F outside the interaction range");
            Teleport(player, camera, originalPosition, originalRotation);
            await Frames(2);
            Check(!workshop.IsInRange, "The central Hub spawn unexpectedly falls inside workshop range.");
            Keys(keyboard, Key.F);
            await Frames(3);
            Check(!workshop.IsOpen && !workshop.TryOpen(), "The workshop opened outside its interaction range.");
            Keys(keyboard);
            await Frames(2);

            Phase("opening through F near the workshop");
            Vector3 near = workshop.InteractionPosition;
            near.y = originalPosition.y;
            Teleport(player, camera, near, Quaternion.identity);
            await Frames(3);
            Check(workshop.IsInRange, "The authored interaction anchor is not in workshop range.");
            Keys(keyboard, Key.F);
            await Wait(() => workshop.IsOpen, "F near the station did not open the workshop.");
            Check(player.IsShopPaused && Time.timeScale == 0f,
                "The workshop entrance began before gameplay was paused.");
            await Wait(() => view.IsInteractionReady, "Workshop entrance did not complete while the Hub was paused.");
            await Frames(3);
            Check(workshop.IsOpen && view.IsOpen && service.Balance == profile.currency,
                "Holding F closed/reopened the panel or changed the upgrade account.");
            Check(player.IsShopPaused && Time.timeScale == 0f && Cursor.visible
                && Cursor.lockState == CursorLockMode.None, "Opening did not pause gameplay and release the cursor.");
            Keys(keyboard);
            await Frames(2);

            Phase("checking visible upgrade labels and pointer targets");
            Canvas.ForceUpdateCanvases();
            foreach (var label in new[] { view.UI.currentBonus, view.UI.nextBonus, view.UI.currentLevel,
                         view.UI.nextLevel, view.UI.selectedTitle, view.UI.purchaseLabel })
            {
                Check(label != null, "An authored upgrade detail label is missing.");
                label.ForceMeshUpdate();
                Check(!label.isTextTruncated && label.textInfo.characterInfo.Take(label.textInfo.characterCount)
                    .Any(character => character.isVisible),
                    "Upgrade label is clipped or has no visible characters: " + label.name);
            }
            CheckPointerTarget(view.UI.closeButton);
            CheckPointerTarget(view.UI.purchaseButton);
            foreach (var entry in view.Entries) CheckPointerTarget(entry.selectButton);

            Phase("blocking movement and camera controls while the panel owns input");
            var before = player.transform.position;
            float modalYaw = orbit != null ? orbit.HorizontalAxis.Value : 0f;
            Keys(keyboard, Key.W, Key.Space, Key.LeftShift);
            InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(100f, 50f) });
            await Frames(4);
            Check(Vector3.Distance(before, player.transform.position) < .01f,
                "The player moved while the workshop modal was open.");
            Check(orbit == null || Mathf.Abs(Mathf.DeltaAngle(modalYaw, orbit.HorizontalAxis.Value)) < .01f,
                "Mouse look rotated the camera while the workshop modal was open.");
            Check(camera.GetComponents<CinemachineInputAxisController>().All(input => !input.enabled),
                "The workshop did not disable player camera input controllers.");
            Keys(keyboard);
            InputSystem.QueueStateEvent(mouse, new MouseState());
            await Frames(2);

            Phase("selecting a category and purchasing through its actual button");
            var health = view.Entries.Single(entry => entry.stat == StatType.ST_HP);
            health.selectButton.onClick.Invoke();
            Check(view.SelectedStat == StatType.ST_HP && view.UI.purchaseButton.IsInteractable(),
                "Selecting Health did not update the active upgrade category.");
            int cost = service.GetNextCost(StatType.ST_HP);
            string previewLevel = view.UI.currentLevel.text;
            view.UI.purchaseButton.onClick.Invoke();
            Check(service.GetLevel(StatType.ST_HP) == 1 && service.Balance == profile.currency - cost && store.Writes == 1,
                "The authored purchase button did not commit exactly one paid level.");
            Check(view.UI.currentLevel.text != previewLevel && health.level.text.Contains("1"),
                "The current level/category row did not refresh after purchase.");
            var reloaded = new PlayerUpgradeService(settings, store);
            Check(reloaded.GetLevel(StatType.ST_HP) == 1 && reloaded.Balance == service.Balance,
                "A new service did not restore the purchased level and balance.");

            Phase("closing with Escape and reopening with restored world controls");
            Keys(keyboard, Key.Escape);
            await Wait(() => !workshop.IsOpen, "Escape did not close the workshop.");
            Check(!player.IsShopPaused && Time.timeScale > .99f && !Cursor.visible
                && Cursor.lockState == CursorLockMode.Locked, "Closing did not restore gameplay time and cursor lock.");
            Check(camera.GetComponents<CinemachineInputAxisController>().All(input => input.enabled),
                "Closing did not restore camera input controllers.");
            Keys(keyboard);
            await Frames(3);
            Keys(keyboard, Key.F);
            await Wait(() => workshop.IsOpen, "F did not reopen the workshop after Escape.");
            await Wait(() => view.IsInteractionReady, "The reopened workshop entrance did not complete.");
            Keys(keyboard);
            await Frames(2);
            var attack = view.Entries.Single(entry => entry.stat == StatType.ST_ATK);
            attack.selectButton.onClick.Invoke();
            Check(view.SelectedStat == StatType.ST_ATK && view.UI.purchaseButton.IsInteractable(),
                "Category selection failed after reopening the panel.");
            view.UI.purchaseButton.onClick.Invoke();
            Check(service.GetLevel(StatType.ST_ATK) == 1 && store.Writes == 2,
                "The reopened panel did not purchase the newly selected category.");

            Phase("showing insufficient balance, maximum level and save failures");
            health.selectButton.onClick.Invoke();
            Check(service.Balance < service.GetNextCost(StatType.ST_HP) && !view.UI.purchaseButton.IsInteractable(),
                "The workshop permits a purchase with insufficient balance.");
            var cappedProfile = PlayerUpgradeProfile.New();
            cappedProfile.currency = settings.initialCost * 10;
            cappedProfile.healthLevel = settings.maxLevel;
            var capped = new PlayerUpgradeService(settings, new MemoryStore(cappedProfile));
            view.Bind(capped);
            view.Select(StatType.ST_HP);
            Check(!view.UI.purchaseButton.IsInteractable(), "The maximum-level category still permits purchases.");
            var failingProfile = PlayerUpgradeProfile.New();
            failingProfile.currency = settings.initialCost * 10;
            var failingStore = new MemoryStore(failingProfile) { RejectWrites = true };
            var failing = new PlayerUpgradeService(settings, failingStore);
            view.Bind(failing);
            view.Select(StatType.ST_HP);
            view.UI.purchaseButton.onClick.Invoke();
            Check(failing.GetLevel(StatType.ST_HP) == 0 && failing.Balance == failingProfile.currency
                && view.UI.status.text.Contains("fixture"), "Save failure did not retain the account and surface feedback.");

            Phase("closing through the authored X button");
            view.UI.closeButton.onClick.Invoke();
            Check(workshop.IsOpen && player.IsShopPaused && Time.timeScale == 0f,
                "The workshop released gameplay before the exit animation completed.");
            view.UI.closeButton.onClick.Invoke();
            Check(!view.IsInteractionReady, "The exiting workshop still accepts interaction.");
            await Wait(() => !workshop.IsOpen, "The X button exit animation did not complete.");
            Check(!workshop.IsOpen && !player.IsShopPaused && Time.timeScale > .99f,
                "The X button did not close the panel and resume the Hub.");
            Check(!player.Input.AttackBuffer.IsActive && !player.Input.ChainSkillBuffer.IsActive
                && !player.IsFinisherSelecting, "Workshop input leaked a combat action into the Hub.");
            outcome = "PASS: " + _checks + " checks; F range/repeat, pause/cursor/camera ownership, Escape/X and reopening, selection/purchase persistence, insufficient/max/save-error UI; in-memory account only.";
        }
        catch (Exception error) { outcome = "FAIL during " + _phase + ": " + error; }
        finally
        {
            if (workshop != null && workshop.IsOpen) workshop.Close();
            accountOverride?.Dispose();
            if (view != null && liveService != null) view.Bind(liveService);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
            if (actions != null) actions.devices = previousDevices;
            if (captured && player != null && Application.isPlaying)
            {
                try
                {
                    Teleport(player, camera, originalPosition, originalRotation);
                    if (orbit != null)
                    {
                        orbit.HorizontalAxis.Value = yaw;
                        orbit.VerticalAxis.Value = pitch;
                        camera.PreviousStateIsValid = false;
                    }
                }
                catch (Exception cleanup) { outcome += " Cleanup: " + cleanup.Message; }
            }
            SessionState.SetString(ResultKey, outcome ?? "FAIL: workshop acceptance ended without a result.");
            EditorApplication.update -= Tick;
            _nextTick = null;
            _running = false;
        }
    }

    private static void Teleport(PlayerController player, CinemachineCamera camera, Vector3 position, Quaternion rotation)
    {
        Vector3 delta = position - player.transform.position;
        bool enabled = player.Controller.enabled;
        player.Controller.enabled = false;
        player.transform.SetPositionAndRotation(position, rotation);
        player.VerticalVelocity = 0f;
        player.StateMachine.ChangeState(new PlayerIdleState(player));
        player.Controller.enabled = enabled;
        Physics.SyncTransforms();
        if (camera != null)
        {
            camera.OnTargetObjectWarped(camera.Follow, delta);
            camera.PreviousStateIsValid = false;
        }
    }

    private static T Read<T>(object owner, string field) => (T)owner.GetType().GetField(field, Hidden).GetValue(owner);
    private static void CheckPointerTarget(UnityEngine.UI.Button button)
    {
        Check(EventSystem.current != null && button != null && button.IsInteractable(),
            "The authored pointer target is missing or cannot be interacted with.");
        var rect = (RectTransform)button.transform;
        var pointer = new PointerEventData(EventSystem.current)
        {
            position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center))
        };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        Check(hits.Count > 0 && (hits[0].gameObject == button.gameObject
                || hits[0].gameObject.transform.IsChildOf(button.transform)),
            "Another graphic blocks the actual button center: " + button.name
                + "; first hit: " + (hits.Count > 0 ? hits[0].gameObject.name : "none"));
    }
    private static void Keys(Keyboard keyboard, params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    private static void Phase(string phase) { _phase = phase; SessionState.SetString(ResultKey, "RUNNING: " + phase); }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
    private static async Task Frames(int count)
    {
        int until = Time.frameCount + count;
        await Wait(() => Time.frameCount >= until, "The game loop stopped advancing.");
    }
    private static async Task Wait(Func<bool> condition, string message)
    {
        while (!condition())
        {
            if (!Application.isPlaying || DateTime.UtcNow >= _deadline) throw new InvalidOperationException(message);
            await NextTick();
        }
    }

    private sealed class MemoryStore : IPlayerUpgradeStore
    {
        private PlayerUpgradeProfile _profile;
        public int Writes;
        public bool RejectWrites;
        public MemoryStore(PlayerUpgradeProfile profile) => _profile = profile.Copy();
        public bool TryLoad(out PlayerUpgradeProfile profile, out string error)
        { profile = _profile.Copy(); error = null; return true; }
        public bool TrySave(PlayerUpgradeProfile profile, out string error)
        {
            if (RejectWrites) { error = "Acceptance fixture save failure."; return false; }
            _profile = profile.Copy();
            Writes++;
            error = null;
            return true;
        }
    }
}
