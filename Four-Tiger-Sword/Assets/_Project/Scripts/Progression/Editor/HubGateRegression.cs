using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using EasternLab.GateAddon;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Real Hub gate input/physics/navigation checks with memory-only upgrade accounts.</summary>
public static class HubGateRegression
{
    private const string ResultKey = "HubGate.PlayModeRegression";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool _running;
    private static int _checks;
    private static string _phase;
    private static DateTime _deadline;
    private static TaskCompletionSource<bool> _nextTick;

    public static string Start() => Begin(false);
    public static string StartFull() => Begin(true);
    public static string Status() => SessionState.GetString(ResultKey, "NOT STARTED");

    private static string Begin(bool includeSceneTransition)
    {
        if (!Application.isPlaying || SceneManager.GetActiveScene().name != "Hub Scene")
            throw new InvalidOperationException("Start in Hub Scene during Play Mode; focus the Game view for virtual input.");
        if (_running) throw new InvalidOperationException("Hub gate verification is already running.");
        _running = true;
        _checks = 0;
        _deadline = DateTime.UtcNow.AddSeconds(includeSceneTransition ? 120 : 90);
        EditorApplication.update += Tick;
        _ = Run(includeSceneTransition);
        return "Hub gate " + (includeSceneTransition ? "full navigation" : "local")
            + " acceptance started with a memory account. Poll HubGateRegression.Status().";
    }

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

    private static async Task Run(bool includeSceneTransition)
    {
        HubGateController gate = null;
        GateAddonSettings settings = null;
        GateActivation activation = null;
        GateTransit transit = null;
        PlayerController player = null;
        CinemachineCamera camera = null;
        HubWorkshopInteraction workshop = null;
        HubWorkshopView workshopView = null;
        Transform workshopStation = null;
        Vector3 workshopAnchor = default;
        PlayerUpgradeService liveAccount = null;
        IDisposable accountOverride = null;
        InputActionAsset actions = null;
        ReadOnlyArray<InputDevice>? previousDevices = null;
        Keyboard keyboard = null;
        Mouse mouse = null;
        var previousKeyboard = Keyboard.current;
        var previousMouse = Mouse.current;
        Vector3 originalPosition = default;
        Quaternion originalRotation = default;
        string originalDestination = null;
        Func<string, AsyncOperation> originalLoader = null;
        UnityAction traversalListener = null;
        bool captured = false, movedScene = false;
        string result = null;
        try
        {
            Phase("checking the authored gate and isolating input/account");
            gate = Find<HubGateController>();
            player = Find<PlayerController>();
            camera = Find<CinemachineCamera>();
            workshop = Find<HubWorkshopInteraction>();
            workshopView = Find<HubWorkshopView>();
            settings = gate.Settings;
            Check(settings != null && settings.panel != null && settings.transit != null,
                "The gate is missing settings, terminal or transit.");
            transit = settings.transit;
            activation = transit.activation;
            Check(activation != null && activation.State == GateState.Closed,
                "Begin with the authored gate closed (restart Play Mode after an interrupted acceptance run).");
            Check(!workshop.IsOpen && !gate.IsLoading && Time.timeScale > .99f,
                "Close the workshop and finish any current navigation before testing.");
            Check(!settings.panel.useBuiltInInput && !settings.panel.showPrompt && transit.externalSceneLoading,
                "The imported gate still owns input/prompt or direct scene loading.");
            Check(Mathf.Abs(activation.openingDuration - 2.4f) < .01f,
                "The authored 2.4-second gate conversion duration changed.");
            originalPosition = player.transform.position;
            originalRotation = player.transform.rotation;
            originalDestination = settings.targetScene;
            originalLoader = Read<Func<string, AsyncOperation>>(gate, "SceneLoaderOverride");
            workshopStation = Read<Transform>(workshop, "station");
            workshopAnchor = Read<Vector3>(workshop, "localAnchor");
            liveAccount = PlayerUpgradeService.Instance;
            captured = true;
            var upgradeSettings = Resources.Load<PlayerUpgradeSettingsSO>("PlayerUpgradeSettings");
            Check(upgradeSettings != null && upgradeSettings.Validate(out _), "Installed permanent-upgrade settings are invalid.");
            var profile = PlayerUpgradeProfile.New();
            profile.currency = 100;
            profile.healthLevel = 1;
            profile.attackLevel = Math.Min(2, upgradeSettings.maxLevel);
            profile.defenseLevel = Math.Min(3, upgradeSettings.maxLevel);
            var store = new MemoryStore(profile);
            var account = new PlayerUpgradeService(upgradeSettings, store);
            accountOverride = PlayerUpgradeService.OverrideForTesting(account);
            workshopView.Bind(account);
            actions = Read<InputActionReference>(player.Input, "_moveAction").action.actionMap.asset;
            previousDevices = actions.devices;
            keyboard = InputSystem.AddDevice<Keyboard>("HubGateAcceptanceKeyboard");
            mouse = InputSystem.AddDevice<Mouse>("HubGateAcceptanceMouse");
            keyboard.MakeCurrent();
            mouse.MakeCurrent();
            actions.devices = new InputDevice[] { keyboard, mouse };
            Keys(keyboard);
            await Frames(3);

            Vector3 frontDirection = Vector3.ProjectOnPlane(settings.panel.transform.forward, Vector3.up).normalized;
            Vector3 front = settings.panel.transform.position + frontDirection * 1.25f;
            front.y = originalPosition.y;
            Vector3 backside = settings.panel.transform.position - frontDirection * .75f;
            backside.y = originalPosition.y;

            Phase("rejecting F outside range and behind the terminal");
            Teleport(player, camera, originalPosition, originalRotation);
            await Frames(2);
            Check(!gate.IsInRange, "The central Hub spawn is unexpectedly within terminal range.");
            await Press(keyboard, Key.F);
            Check(activation.State == GateState.Closed, "F operated the terminal outside its two-meter range.");
            Teleport(player, camera, backside, originalRotation);
            await Frames(2);
            Check(!gate.IsInRange, "The terminal accepts its back side as an interaction surface.");
            await Press(keyboard, Key.F);
            Check(activation.State == GateState.Closed, "F operated the back of the terminal.");

            Phase("opening from the front without camera aim and rejecting repeated F");
            Teleport(player, camera, front, Quaternion.LookRotation(frontDirection));
            await Frames(3);
            Check(gate.IsInRange, "The front-side test pose is not within two meters of the terminal.");
            Check(Vector3.Dot(player.transform.forward, settings.panel.transform.position - player.transform.position) < 0f,
                "The non-aiming interaction fixture accidentally faces the terminal.");
            float startedOpening = Time.time;
            Keys(keyboard, Key.F);
            await Wait(() => activation.State == GateState.Opening, "F did not begin the gate opening conversion.");
            Check(!gate.TryInteract() && !activation.CanTraverse && activation.apertureBlocker.enabled,
                "An opening gate accepted another operation or allowed early traversal.");
            await Seconds(.4f);
            Check(activation.State == GateState.Opening && activation.apertureBlocker.enabled,
                "The aperture opened before its conversion completed.");
            await Wait(() => activation.State == GateState.Open, "The 2.4-second conversion did not finish.");
            await Frames(3);
            Check(Time.time - startedOpening >= activation.openingDuration - .15f
                && !activation.apertureBlocker.enabled && activation.CanTraverse,
                "The gate did not retain its conversion duration and blocker lifecycle.");
            Check(activation.State == GateState.Open, "Holding F toggled the gate again after opening.");
            Keys(keyboard);
            await Frames(2);

            Phase("refusing to close an occupied aperture, then closing and reopening");
            var clearance = activation.clearanceVolume != null ? activation.clearanceVolume : activation.apertureBlocker;
            Vector3 occupied = clearance.transform.TransformPoint(clearance.center) - player.transform.TransformVector(player.Controller.center);
            Teleport(player, camera, occupied, originalRotation);
            Physics.SyncTransforms();
            Check(activation.HasTravellerInClearance() && !activation.Close() && activation.State == GateState.Open,
                "The gate closed while a CharacterController occupied its clearance volume.");
            Teleport(player, camera, front, originalRotation);
            await Frames(3);
            await Press(keyboard, Key.F);
            Check(activation.State == GateState.Closing, "F did not close an unoccupied open gate.");
            await Wait(() => activation.State == GateState.Closed, "Gate closing did not finish.");
            await Press(keyboard, Key.F);
            await Wait(() => activation.State == GateState.Open, "The closed gate could not be reopened.");

            Phase("giving an open workshop priority over the gate F key");
            // Co-locate only the runtime workshop interaction anchor so both F consumers are genuinely eligible.
            Write(workshop, "station", settings.panel.transform);
            Write(workshop, "localAnchor", Vector3.zero);
            Teleport(player, camera, front, originalRotation);
            await Frames(2);
            Check(workshop.TryOpen(), "The modal arbitration fixture could not open the actual workshop.");
            await Press(keyboard, Key.F);
            Check(workshop.IsOpen && activation.State == GateState.Open && !gate.IsLoading && !gate.TryInteract(),
                "The gate consumed F while the workshop owned gameplay input.");
            workshop.Close();
            Write(workshop, "station", workshopStation);
            Write(workshop, "localAnchor", workshopAnchor);
            await Frames(3);

            Phase("recovering an invalid destination and unreadable upgrade profile");
            Teleport(player, camera, front, originalRotation);
            await Frames(3);
            Vector3 safe = Read<Vector3>(gate, "_safePosition");
            settings.targetScene = "__Missing_HubGate_Regression_Scene__";
            settings.ApplyDestination();
            Check(!gate.TryStartRun(player.transform), "The gate accepted an invalid destination.");
            await Frames(2);
            AssertRecovered(gate, player, safe);
            settings.targetScene = originalDestination;
            settings.ApplyDestination();
            var unreadable = new PlayerUpgradeService(upgradeSettings, new MemoryStore(profile) { RejectLoads = true });
            using (PlayerUpgradeService.OverrideForTesting(unreadable))
            {
                Teleport(player, camera, front, originalRotation);
                await Frames(3);
                safe = Read<Vector3>(gate, "_safePosition");
                Check(!gate.TryStartRun(player.transform), "The gate accepted an unreadable upgrade profile.");
                await Frames(2);
                AssertRecovered(gate, player, safe);
            }

            Phase("restoring a safe pose after an asynchronous loader returns null");
            int loaderCalls = 0;
            Write<Func<string, AsyncOperation>>(gate, "SceneLoaderOverride", _ => { loaderCalls++; return null; });
            Teleport(player, camera, front, originalRotation);
            await Frames(3);
            safe = Read<Vector3>(gate, "_safePosition");
            Check(gate.TryStartRun(player.transform) && gate.IsLoading && player.IsStageTransitioning,
                "A valid departure did not suspend the player before loading.");
            Check(!gate.TryStartRun(player.transform) && !gate.TryInteract(), "Loading accepted a duplicate departure or operation.");
            await Wait(() => !gate.IsLoading, "A null loader result did not recover the Hub.");
            Check(loaderCalls == 1, "Duplicate navigation reached the asynchronous loader.");
            AssertRecovered(gate, player, safe);

            Phase("crossing the real directional trigger once and recovering a thrown load");
            int traversals = 0;
            traversalListener = () => traversals++;
            transit.onTraverse.AddListener(traversalListener);
            Write<Func<string, AsyncOperation>>(gate, "SceneLoaderOverride", _ =>
            { loaderCalls++; throw new InvalidOperationException("Hub gate loader fixture failure."); });
            int beforeTraversal = transit.TraversalCount;
            await CrossGate(player, camera, transit, gate, originalPosition.y);
            await Wait(() => loaderCalls == 2 && !gate.IsLoading, "The real crossing did not invoke and recover the throwing loader.");
            Check(traversals == 1 && transit.TraversalCount == beforeTraversal + 1,
                "Walking through the real gate trigger did not dispatch exactly one traversal.");
            safe = Read<Vector3>(gate, "_safePosition");
            AssertRecovered(gate, player, safe);
            Check(gate.LastError.Contains("fixture"), "The thrown scene-loader error was not reported.");
            await Frames(3);
            Check(traversals == 1 && loaderCalls == 2, "Recovery retriggered the gate without a new crossing.");

            if (includeSceneTransition)
            {
                Phase("walking into the actual configured Game Scene");
                Write<Func<string, AsyncOperation>>(gate, "SceneLoaderOverride", null);
                await Seconds(transit.cooldown + .1f);
                beforeTraversal = transit.TraversalCount;
                await CrossGate(player, camera, transit, gate, originalPosition.y);
                await Wait(() => SceneManager.GetActiveScene().name == "Game Scene", "The actual gate crossing did not load Game Scene.");
                movedScene = true;
                Check(traversals == 2, "Actual navigation was not dispatched by exactly one new gate crossing.");
                var run = Find<RunManager>();
                await Wait(() => run.State == RunState.Playing || run.State == RunState.Error,
                    "The configured first stage never completed preparation.");
                Check(run.State == RunState.Playing, "Actual first-stage generation failed: " + run.LastError);
                Check(run.CurrentChapterIndex == 0 && run.CurrentStageIndex == 0 && run.stageManager.IsReady
                    && run.stageManager.Result != null && run.stageManager.Result.root != null,
                    "Gate entry did not start the first configured stage.");
                var gameplayPlayer = run.player;
                Check(gameplayPlayer != null && gameplayPlayer.Input.Mode == PlayerInputHandler.InputMode.FullGameplay
                    && gameplayPlayer.GetComponent<PlayerUpgradeBinding>() != null, "Game Scene did not restore full player controls and upgrade binding.");
                Near(gameplayPlayer.StatManager.MaxHp, upgradeSettings.basePlayerStats.baseHP * (1 + profile.healthLevel * upgradeSettings.healthPerLevel), "Spawn HP upgrade");
                Near(gameplayPlayer.StatManager.GetStat(StatType.ST_ATK), upgradeSettings.basePlayerStats.baseATK * (1 + profile.attackLevel * upgradeSettings.attackPerLevel), "Spawn attack upgrade");
                Near(gameplayPlayer.StatManager.GetStat(StatType.ST_DEF), upgradeSettings.basePlayerStats.baseDEF * (1 + profile.defenseLevel * upgradeSettings.defensePerLevel), "Spawn defense upgrade");
                bool bossStage = run.definition.chapters[0].stages.Length == 1;
                if (bossStage)
                    Check(run.stageManager.Result.bossRoom != null
                        && run.stageManager.Result.root.GetComponentsInChildren<Boss>(true).Any(),
                        "The configured first boss stage did not spawn its current boss.");
                else Check(run.stageManager.Result.bossRoom == null, "A non-boss first stage unexpectedly contains a boss room.");
                await Frames(3);
                var hud = Find<PlayerHudPresenter>();
                Check(Read<CanvasGroup>(hud, "visibility").alpha > .99f, "The gameplay HUD remained hidden after leaving the Hub.");
                Check(store.Writes == 0 && account.Balance == profile.currency, "Entering a run unexpectedly wrote or spent the isolated upgrade account.");
                Phase("returning to the Hub before restoring the live account");
                var operation = SceneManager.LoadSceneAsync("Hub Scene", LoadSceneMode.Single);
                await Wait(() => operation.isDone && SceneManager.GetActiveScene().name == "Hub Scene", "Acceptance could not return to the Hub.");
                await Frames(3);
                Check(Find<PlayerController>().Input.Mode == PlayerInputHandler.InputMode.LocomotionOnly
                    && Object.FindObjectsByType<PlayerHudPresenter>(FindObjectsSortMode.None).Length == 0,
                    "Returning from Game Scene did not restore Hub controls and hidden HUD.");
            }
            result = "PASS: " + _checks + " checks; F front/range/no-aim/repeat, real conversion/blocker, close/reopen/clearance, workshop priority, invalid profile/destination and null/throw recovery, actual directional CC trigger"
                + (includeSceneTransition ? ", real first-stage/boss generation + gameplay controls/HUD/upgrades + Hub return" : "")
                + "; memory account only.";
        }
        catch (Exception failure) { result = "FAIL during " + _phase + ": " + failure; }
        finally
        {
            try
            {
                if (Application.isPlaying && SceneManager.GetActiveScene().name != "Hub Scene")
                {
                    var operation = SceneManager.LoadSceneAsync("Hub Scene", LoadSceneMode.Single);
                    var cleanupDeadline = DateTime.UtcNow.AddSeconds(30);
                    while (Application.isPlaying && operation != null && !operation.isDone && DateTime.UtcNow < cleanupDeadline) await NextTick();
                }
                if (transit != null && traversalListener != null) transit.onTraverse.RemoveListener(traversalListener);
                if (captured && settings != null) { settings.targetScene = originalDestination; settings.ApplyDestination(); }
                if (captured && gate != null) Write(gate, "SceneLoaderOverride", originalLoader);
                if (captured && workshop != null)
                {
                    workshop.Close();
                    Write(workshop, "station", workshopStation);
                    Write(workshop, "localAnchor", workshopAnchor);
                }
                accountOverride?.Dispose();
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                if (actions != null) actions.devices = previousDevices;
                if (captured && Application.isPlaying && SceneManager.GetActiveScene().name == "Hub Scene")
                {
                    var restoredPlayer = Object.FindFirstObjectByType<PlayerController>();
                    var restoredCamera = Object.FindFirstObjectByType<CinemachineCamera>();
                    if (restoredPlayer != null)
                    {
                        Teleport(restoredPlayer, restoredCamera, originalPosition, originalRotation);
                        if (movedScene && liveAccount != null) restoredPlayer.GetComponent<PlayerUpgradeBinding>()?.Bind(liveAccount, true);
                    }
                    if (liveAccount != null) Object.FindFirstObjectByType<HubWorkshopView>()?.Bind(liveAccount);
                    if (activation != null)
                    {
                        // OnEnable restores the imported gate's authored closed state, including its blocker and visuals.
                        activation.enabled = false;
                        activation.enabled = true;
                    }
                }
            }
            catch (Exception cleanup) { result += " Cleanup: " + cleanup; }
            SessionState.SetString(ResultKey, result ?? "FAIL: gate acceptance ended without a result.");
            EditorApplication.update -= Tick;
            _nextTick = null;
            _running = false;
        }
    }

    private static async Task CrossGate(PlayerController player, CinemachineCamera camera, GateTransit transit, HubGateController gate, float rootHeight)
    {
        Vector3 start = transit.transform.TransformPoint(new Vector3(0f, 0f, transit.crossingPlaneOffset + 1.6f));
        start.y = rootHeight;
        Teleport(player, camera, start, Quaternion.LookRotation(-transit.transform.forward, Vector3.up));
        await Frames(3);
        Check(transit.FitsAperture(player.Controller), "The authored player capsule does not fit the gate aperture.");
        int count = transit.TraversalCount;
        for (int step = 0; step < 24 && transit != null && transit.TraversalCount == count && !gate.IsLoading; step++)
        {
            player.Controller.Move(-transit.transform.forward * .12f);
            await Frames(2);
        }
        Check(transit == null || transit.TraversalCount == count + 1 || gate.IsLoading,
            "Actual CharacterController movement through the aperture did not trigger a directional traversal.");
    }
    private static void AssertRecovered(HubGateController gate, PlayerController player, Vector3 safe)
    {
        Check(!gate.IsLoading && !player.IsStageTransitioning && player.Controller.enabled
            && player.Input.enabled && !player.IsGameplayModalPaused && Time.timeScale > .99f,
            "Failed navigation did not restore player input/controller and world time.");
        Check(Vector3.Distance(player.transform.position, safe) < .2f, "Failed navigation did not restore the cached safe pose.");
        Check(!string.IsNullOrWhiteSpace(gate.LastError), "Failed navigation did not expose an error for retry.");
        Check(Object.FindObjectsByType<CinemachineInputAxisController>(FindObjectsSortMode.None).All(input => input.enabled),
            "Failed navigation left the camera input disabled.");
    }
    private static void Teleport(PlayerController player, CinemachineCamera camera, Vector3 position, Quaternion rotation)
    {
        Vector3 delta = position - player.transform.position;
        bool enabled = player.Controller.enabled;
        player.Controller.enabled = false;
        player.transform.SetPositionAndRotation(position, rotation);
        player.VerticalVelocity = 0f;
        player.Input.JumpBuffer.Consume();
        player.Input.DashBuffer.Consume();
        player.StateMachine.ChangeState(new PlayerIdleState(player));
        player.Controller.enabled = enabled;
        Physics.SyncTransforms();
        if (camera != null) { camera.OnTargetObjectWarped(camera.Follow, delta); camera.PreviousStateIsValid = false; }
    }
    private static T Find<T>() where T : Object
    {
        var found = Object.FindFirstObjectByType<T>();
        Check(found != null, "No active " + typeof(T).Name + " found.");
        return found;
    }
    private static T Read<T>(object owner, string field) => (T)owner.GetType().GetField(field, Hidden).GetValue(owner);
    private static void Write<T>(object owner, string field, T value) => owner.GetType().GetField(field, Hidden).SetValue(owner, value);
    private static void Keys(Keyboard keyboard, params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    private static async Task Press(Keyboard keyboard, Key key)
    { Keys(keyboard, key); await Frames(3); Keys(keyboard); await Frames(2); }
    private static void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) < .02f,
        message + ": expected " + expected + ", actual " + actual);
    private static void Phase(string phase) { _phase = phase; SessionState.SetString(ResultKey, "RUNNING: " + phase); }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); _checks++; }
    private static async Task Seconds(float duration)
    { float until = Time.time + duration; await Wait(() => Time.time >= until, "The game clock stopped advancing."); }
    private static async Task Frames(int count)
    { int until = Time.frameCount + count; await Wait(() => Time.frameCount >= until, "The game loop stopped advancing."); }
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
        public bool RejectLoads;
        public int Writes;
        public MemoryStore(PlayerUpgradeProfile profile) => _profile = profile.Copy();
        public bool TryLoad(out PlayerUpgradeProfile profile, out string error)
        {
            profile = RejectLoads ? null : _profile.Copy();
            error = RejectLoads ? "Hub gate unreadable-profile fixture." : null;
            return !RejectLoads;
        }
        public bool TrySave(PlayerUpgradeProfile profile, out string error)
        { _profile = profile.Copy(); Writes++; error = null; return true; }
    }
}
