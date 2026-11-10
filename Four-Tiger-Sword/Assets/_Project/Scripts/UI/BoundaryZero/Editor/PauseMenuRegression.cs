using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using EasternLab.GateAddon;
using StudioBaekho;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Checks pause ownership in isolation and ESC handling in both authored scenes.</summary>
public static class PauseMenuRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string ResultKey = "BoundaryZero.PauseMenuRegression.Live";
    private static bool _liveRunning;

    [MenuItem("Tools/Studio Baekho/BoundaryZERO/Run Pause Menu Regression")]
    private static void RunMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run pause ownership fixtures in Edit Mode.");

        float savedScale = Time.timeScale;
        bool savedCursorVisible = Cursor.visible;
        CursorLockMode savedCursorLock = Cursor.lockState;
        var events = EventSystem.current;
        var priorSelection = events != null ? events.currentSelectedGameObject : null;
        var services = new HashSet<CombatHitStopService>(Resources.FindObjectsOfTypeAll<CombatHitStopService>());
        var instance = typeof(CombatHitStopService).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
        object savedInstance = instance.GetValue(null);
        var fixture = new GameObject("__PauseMenuRegression");
        RunManager run = null;
        HubWorldController hub = null;
        try
        {
            instance.SetValue(null, null);
            Time.timeScale = .8f;
            run = Child(fixture.transform, "Run").AddComponent<RunManager>();
            var player = AddPlayer(run.transform);
            run.player = player;
            var stage = Child(run.transform, "Stage").AddComponent<StageManager>();
            var generation = Child(stage.transform, "Generation");
            run.stageManager = stage;
            Property(stage, "Result", new BaekhoDungeonResult { root = generation, success = true });
            Property(stage, "IsReady", true);
            Property(run, "State", RunState.Playing);
            var menu = PauseMenuPresenter.Ensure(run);
            Field(run, "_pauseMenu", menu);
            Check(PauseMenuPresenter.Ensure(run) == menu, "Run setup duplicated the presenter.");

            var owner = Child(fixture.transform, "Other owner");
            var axis = CameraAxis(fixture.transform, "Tracked camera", player.transform, true);
            var disabledAxis = CameraAxis(fixture.transform, "Disabled camera", player.transform, false);
            var unrelatedAxis = CameraAxis(fixture.transform, "Unrelated camera", owner.transform, true);
            var action = new ProbeState();
            player.StateMachine.ChangeState(action);
            player.Input.AttackBuffer.Set();
            using (var slow = CombatHitStopService.AcquireWorldScale(owner, .5f))
            {
                Check(menu.Open() && run.IsPauseMenuOpen && player.IsPauseMenuPaused && Time.timeScale == 0f,
                    "Run pause did not acquire world time and player input.");
                Check(menu.PauseCanvas.isActiveAndEnabled && Cursor.visible && Cursor.lockState == CursorLockMode.None,
                    "Pause did not display its UI and expose the cursor.");
                var window = menu.PauseCanvas.transform.Find("Window") as RectTransform;
                Check(window != null && window.rect.size == new Vector2(920f, 690f),
                    "The fitted window did not retain its authored size.");
                foreach (var graphic in menu.PauseCanvas.GetComponentsInChildren<UnityEngine.UI.Graphic>())
                    Check(graphic.GetComponent<CanvasRenderer>() != null,
                        "The visible pause graphic cannot render: " + graphic.name);
                Check(!axis.enabled && !disabledAxis.enabled && unrelatedAxis.enabled,
                    "Pause modified unrelated camera axes or missed the tracked one.");
                Check(!run.TryOpenInventory(owner) && !run.TryOpenMap(owner) && !run.TryOpenShop(owner),
                    "An interaction opened another modal over pause.");
                Invoke(player, "Update");
                Check(player.Controller.enabled && player.StateMachine.CurrentState == action && action.Updates == 0
                    && action.Exits == 0 && !player.Input.AttackBuffer.IsActive,
                    "Pause consumed the active action, advanced it, or retained buffered attack input.");
                Invoke(menu, "OnApplicationFocus", false);
                Check(menu.IsOpen && Time.timeScale == 0f, "Losing focus resumed gameplay.");

                menu.RequestTitle();
                Check(menu.IsConfirming && MenuText(menu).Contains("저장되지 않습니다"),
                    "Run exit confirmation omitted the warning about the unfinished run.");
                menu.HandleEscape();
                Check(menu.IsOpen && !menu.IsConfirming && Time.timeScale == 0f,
                    "Escape at the confirmation did not return to the paused menu.");
                menu.HandleEscape();
                Check(!menu.IsOpen && !player.IsGameplayModalPaused && slow.IsValid && Near(.4f),
                    "Resume dropped another owner's slowdown or left input paused.");
                Check(axis.enabled && !disabledAxis.enabled && unrelatedAxis.enabled,
                    "Resume restored axes it did not own.");
                player.Input.AttackBuffer.Set();
                Invoke(player.Input, "Update");
                Check(!player.Input.AttackBuffer.IsActive, "Resume replayed an attack before neutral input.");
            }
            Check(Near(.8f), "Releasing the independent slowdown did not restore the starting scale.");

            Check(run.TryOpenInventory(owner) && !menu.Open(), "Pause opened over the inventory.");
            run.CloseInventory();
            Check(run.TryOpenMap(owner) && !menu.Open(), "Pause opened over the map.");
            run.CloseMap();
            Check(run.TryOpenShop(owner) && !menu.Open(), "Pause opened over the shop.");
            run.CloseShop();
            Property(run, "IsRewardOpen", true);
            Check(!menu.Open(), "Pause opened over the mandatory reward selection.");
            Property(run, "IsRewardOpen", false);
            Field(player, "_finisherAttack", new PlayerAttackState(player));
            Check(!menu.Open(), "Pause opened during finisher selection.");
            Field(player, "_finisherAttack", null);

            var store = new MemoryStore();
            var account = new PlayerUpgradeService(Resources.Load<PlayerUpgradeSettingsSO>("PlayerUpgradeSettings"), store);
            Check(account.IsReady, "The isolated upgrade account could not load the installed settings.");
            Field(run, "_upgradeService", account);
            Check(menu.Open(), "Pause could not reopen after closing the other modals.");
            QueueReward(run, "pause-regression-awarded");
            int loads = 0, quits = 0;
            Field(menu, "SceneLoaderOverride", (Func<string, AsyncOperation>)(scene =>
            {
                Check(scene == "TitleScene" && Time.timeScale == 0f && player.IsPauseMenuPaused,
                    "Title navigation resumed the world before loading the correct scene.");
                loads++;
                if (loads == 1) return null;
                throw new InvalidOperationException("Fixture title loader failure.");
            }));
            Field(menu, "QuitOverride", (Action)(() => quits++));
            menu.RequestTitle();
            menu.ConfirmExit();
            Check(loads == 1 && menu.IsOpen && menu.IsConfirming && !menu.IsExiting && !string.IsNullOrEmpty(menu.LastError)
                && Time.timeScale == 0f && account.Balance == 5 && store.Writes == 1,
                "A null title load lost the paused confirmation or failed to persist the earned reward.");
            menu.ConfirmExit();
            Check(loads == 2 && menu.IsConfirming && !menu.IsExiting && !string.IsNullOrEmpty(menu.LastError) && store.Writes == 1,
                "A thrown title load could not be retried safely.");
            menu.CancelConfirmation();
            menu.RequestQuit();
            menu.ConfirmExit();
            menu.ConfirmExit();
            Check(quits == 1 && menu.IsExiting && Time.timeScale == 0f && run.State == RunState.Playing
                && run.DeathUpgradeRewardTotal == 0 && run.GoldConversionRewardTotal == 0 && account.Balance == 5,
                "Confirmed exit resumed gameplay, repeated the request, or granted terminal rewards.");
            Invoke(run, "OnDisable");
            Check(!menu.IsOpen && !player.IsPauseMenuPaused && axis.enabled && Near(.8f),
                "Run shutdown stranded pause ownership.");

            Check(menu.Open(), "Pause could not reopen for save failure recovery.");
            QueueReward(run, "pause-regression-save-failure");
            store.RejectWrites = true;
            menu.RequestQuit();
            menu.ConfirmExit();
            Check(quits == 1 && run.IsUpgradeSaveBlocked && run.HasPendingUpgradeRewards && run.State == RunState.Error
                && !menu.IsOpen && !player.IsPauseMenuPaused && player.IsStageTransitioning && Time.timeScale == 0f,
                "A failed reward write did not hand pause ownership to the save recovery flow.");
            Check(!menu.Open(), "The pause menu reopened over the save recovery screen.");
            Invoke(run, "OnDisable");
            Check(Near(.8f), "Save recovery shutdown retained a time lease.");

            hub = Child(fixture.transform, "Hub").AddComponent<HubWorldController>();
            var hubPlayer = AddPlayer(hub.transform);
            // This isolated fixture only needs modal ownership, not the live HUD/profile lifecycle.
            var hubMenu = PauseMenuPresenter.Ensure(hub);
            Field(hub, "_pauseMenu", hubMenu);
            Invoke(hub, "SetModalOpen", true);
            Check(!hubMenu.Open(), "Hub pause opened over the workshop or gate loading.");
            Invoke(hub, "SetModalOpen", false);
            Check(hubMenu.Open() && hub.IsPauseMenuOpen && hub.IsModalOpen && hubPlayer.IsPauseMenuPaused && Time.timeScale == 0f,
                "The Hub pause did not share the run's pause and input behavior.");
            hubMenu.RequestQuit();
            Check(hubMenu.IsConfirming && !MenuText(hubMenu).Contains("탐사 진행"), "The Hub confirmation showed run-only text.");
            hubMenu.CancelConfirmation();
            hubMenu.Resume();
            Check(!hub.IsModalOpen && !hubPlayer.IsGameplayModalPaused && Near(.8f), "Resuming did not restore Hub gameplay.");
            Check(hubMenu.Open(), "Hub pause could not reopen before player invalidation.");
            hubPlayer.enabled = false;
            Invoke(hubMenu, "Update");
            Check(!hubMenu.IsOpen && !hub.IsModalOpen && !hubPlayer.IsPauseMenuPaused && Near(.8f),
                "Losing the Hub player left the workshop and gate blocked by a stale modal flag.");
            hubPlayer.enabled = true;
            Check(hubMenu.Open(), "Hub pause could not reopen before shutdown.");
            Invoke(hub, "OnDisable");
            Check(!hubMenu.IsOpen && !hubPlayer.IsPauseMenuPaused && Near(.8f), "Hub shutdown stranded pause ownership.");
            return "PASS: pause ownership, camera/input restoration, modal precedence, confirmations, exit failure and reward persistence, Hub and run teardown.";
        }
        finally
        {
            if (run != null) Invoke(run, "OnDisable");
            if (hub != null) Invoke(hub, "OnDisable");
            Object.DestroyImmediate(fixture);
            if (events != null) events.SetSelectedGameObject(priorSelection);
            foreach (var service in Resources.FindObjectsOfTypeAll<CombatHitStopService>())
                if (!services.Contains(service)) Object.DestroyImmediate(service.gameObject);
            instance.SetValue(null, savedInstance);
            Time.timeScale = savedScale;
            Cursor.lockState = savedCursorLock;
            Cursor.visible = savedCursorVisible;
        }
    }

    public static string StartLive()
    {
        if (!Application.isPlaying || _liveRunning)
            throw new InvalidOperationException("Run the live pause check once at a time in Play Mode.");
        var scene = SceneManager.GetActiveScene().name;
        var menu = Object.FindFirstObjectByType<PauseMenuPresenter>();
        var player = Object.FindFirstObjectByType<PlayerController>();
        if ((scene != "Game Scene" && scene != "Hub Scene") || menu == null || player == null
            || menu.IsOpen || player.IsGameplayModalPaused || player.IsStageTransitioning)
            throw new InvalidOperationException("Start on the exploration screen of the authored Game Scene or Hub Scene.");
        _liveRunning = true;
        SessionState.SetString(ResultKey, "RUNNING: " + scene);
        _ = RunLive(menu, player, scene);
        return "Live pause check started. Poll PauseMenuRegression.LiveStatus().";
    }

    public static string LiveStatus() => SessionState.GetString(ResultKey, "NOT STARTED");

    private static async Task RunLive(PauseMenuPresenter menu, PlayerController player, string scene)
    {
        var previousKeyboard = Keyboard.current;
        Keyboard keyboard = null;
        var inventory = Object.FindFirstObjectByType<PlayerInventoryPresenter>();
        var minimap = Object.FindFirstObjectByType<DungeonMinimapPresenter>();
        var workshop = Object.FindFirstObjectByType<HubWorkshopInteraction>();
        var gate = Object.FindFirstObjectByType<HubGateController>()?.Settings?.transit?.activation;
        Transform workshopStation = null;
        Vector3 workshopAnchor = default;
        bool movedWorkshop = false, openedGate = false;
        var deadline = DateTime.UtcNow.AddSeconds(20);
        try
        {
            keyboard = InputSystem.AddDevice<Keyboard>();
            keyboard.MakeCurrent();
            if (scene == "Game Scene")
            {
                Check(inventory != null && inventory.Open(), "Authored inventory could not open for the ESC precedence check.");
                await PressEscape();
                Check(!inventory.IsOpen && !menu.IsOpen, "One ESC press opened pause while dismissing the inventory.");

                Check(minimap != null, "The authored dungeon has no map presenter.");
                minimap.ToggleFullMap();
                Check(minimap.IsFullMapOpen, "The authored full map could not open for the ESC precedence check.");
                await PressEscape();
                while (minimap.IsFullMapOpen) await Frames(1);
                Check(!menu.IsOpen, "One ESC press opened pause while dismissing the map.");
            }
            else
            {
                Check(workshop != null, "The authored Hub has no workshop interaction.");
                workshopStation = ReadField<Transform>(workshop, "station");
                workshopAnchor = ReadField<Vector3>(workshop, "localAnchor");
                movedWorkshop = true;
                Field(workshop, "station", player.transform);
                Field(workshop, "localAnchor", Vector3.zero);
                Check(workshop.TryOpen(), "The authored workshop could not open for the ESC precedence check.");
                await PressEscape();
                while (workshop.IsOpen) await Frames(1);
                Check(!menu.IsOpen, "One ESC press opened pause while dismissing the workshop.");
                RestoreWorkshop();

                Check(gate != null && gate.Open(), "Begin the Hub pause check with the authored gate closed.");
                openedGate = true;
                await Frames(3);
            }

            float scale = Time.timeScale;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
            await Frames(3);
            Check(menu.IsOpen && player.IsPauseMenuPaused && Time.timeScale == 0f,
                "Actual ESC input did not pause the authored scene.");
            float worldTime = Time.time;
            var position = player.transform.position;
            float gateProgress = openedGate ? gate.NormalizedOpenAmount : 0f;
            await Frames(12);
            Check(menu.IsOpen && Time.time == worldTime && player.transform.position == position,
                "Holding ESC toggled pause again or the world advanced while paused.");
            if (openedGate)
                Check(gate.State == GateState.Opening && gate.NormalizedOpenAmount == gateProgress,
                    "The Hub gate animation advanced while paused.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            await Frames(2);

            menu.RequestQuit();
            Check(EventSystem.current != null && SelectedText(EventSystem.current) == "취소",
                "The authored confirmation did not select Cancel by default.");
            await PressEscape();
            Check(menu.IsOpen && !menu.IsConfirming && Time.timeScale == 0f,
                "ESC at an exit confirmation did not keep the authored scene paused.");
            await PressEscape();
            while (menu.IsOpen) await Frames(1);
            Check(!player.IsGameplayModalPaused && Near(scale), "Actual ESC did not restore authored gameplay.");
            if (openedGate)
            {
                await Frames(3);
                Check(gate.NormalizedOpenAmount > gateProgress, "The Hub gate did not resume opening.");
            }
            SessionState.SetString(ResultKey, "PASS: " + scene + " real ESC, modal precedence, held key, frozen world, confirmation cancel and resume.");
        }
        catch (Exception failure) { SessionState.SetString(ResultKey, "FAIL: " + scene + " " + failure); }
        finally
        {
            if (inventory != null && inventory.IsOpen) inventory.Close();
            if (minimap != null && minimap.IsFullMapOpen) minimap.CloseFullMap();
            RestoreWorkshop();
            if (menu != null && menu.IsOpen) menu.CloseForHostShutdown();
            if (openedGate && gate != null)
            {
                gate.enabled = false;
                gate.enabled = true;
            }
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            _liveRunning = false;
        }

        void RestoreWorkshop()
        {
            if (!movedWorkshop || workshop == null) return;
            if (workshop.IsOpen) workshop.Close();
            Field(workshop, "station", workshopStation);
            Field(workshop, "localAnchor", workshopAnchor);
            movedWorkshop = false;
        }

        async Task PressEscape()
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
            await Frames(3);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            await Frames(2);
        }

        async Task Frames(int count)
        {
            int target = Time.frameCount + count;
            while (Time.frameCount < target)
            {
                if (!Application.isPlaying || DateTime.UtcNow > deadline)
                    throw new InvalidOperationException("The authored scene did not advance frames before the deadline.");
                var tick = new TaskCompletionSource<bool>();
                void Next() { EditorApplication.update -= Next; tick.TrySetResult(true); }
                EditorApplication.update += Next;
                await tick.Task;
            }
        }
    }

    private static GameObject Child(Transform parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child;
    }

    private static PlayerController AddPlayer(Transform parent)
    {
        var player = Child(parent, "Player").AddComponent<PlayerController>();
        Property(player, "Input", player.GetComponent<PlayerInputHandler>());
        Property(player, "Controller", player.GetComponent<CharacterController>());
        Property(player, "StatManager", player.GetComponent<PlayerStatManager>());
        Property(player, "StateMachine", new StateMachine());
        return player;
    }

    private static CinemachineInputAxisController CameraAxis(Transform parent, string name, Transform target, bool enabled)
    {
        var camera = Child(parent, name).AddComponent<CinemachineCamera>();
        camera.Follow = target;
        var axis = camera.gameObject.AddComponent<CinemachineInputAxisController>();
        axis.enabled = enabled;
        return axis;
    }

    private static void QueueReward(RunManager run, string id)
    {
        var rewardType = typeof(RunManager).GetNestedType("PendingUpgradeReward", BindingFlags.NonPublic);
        var reward = Activator.CreateInstance(rewardType, true);
        rewardType.GetField("Id").SetValue(reward, id);
        rewardType.GetField("Amount").SetValue(reward, 5);
        var pending = (IList)typeof(RunManager).GetField("_pendingUpgradeRewards", Hidden).GetValue(run);
        pending.Add(reward);
    }

    private static string SelectedText(EventSystem events)
        => events.currentSelectedGameObject?.GetComponentInChildren<TMP_Text>()?.text;

    private static string MenuText(PauseMenuPresenter menu)
    {
        var parts = Array.ConvertAll(menu.PauseCanvas.GetComponentsInChildren<TMP_Text>(), text => text.text);
        return string.Join("\n", parts);
    }

    private static bool Near(float expected) => Mathf.Abs(Time.timeScale - expected) < .0001f;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Pause menu: " + message);
    }

    private static void Property(object owner, string name, object value)
        => owner.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public).SetValue(owner, value);

    private static void Field(object owner, string name, object value) => owner.GetType().GetField(name, Hidden).SetValue(owner, value);

    private static T ReadField<T>(object owner, string name) => (T)owner.GetType().GetField(name, Hidden).GetValue(owner);

    private static void Invoke(object owner, string name, params object[] arguments)
        => owner.GetType().GetMethod(name, Hidden).Invoke(owner, arguments);

    private sealed class ProbeState : IPlayerState
    {
        public int Updates, Exits;
        public void Enter() { }
        public void Update() => Updates++;
        public void Exit() => Exits++;
    }

    private sealed class MemoryStore : IPlayerUpgradeStore
    {
        private PlayerUpgradeProfile _profile = PlayerUpgradeProfile.New();
        public bool RejectWrites;
        public int Writes;

        public bool TryLoad(out PlayerUpgradeProfile profile, out string error)
        { profile = _profile.Copy(); error = null; return true; }

        public bool TrySave(PlayerUpgradeProfile profile, out string error)
        {
            if (RejectWrites) { error = "Fixture reward save failure."; return false; }
            _profile = profile.Copy();
            Writes++;
            error = null;
            return true;
        }
    }
}
