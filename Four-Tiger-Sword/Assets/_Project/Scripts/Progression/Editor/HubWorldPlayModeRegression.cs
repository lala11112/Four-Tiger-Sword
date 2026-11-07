using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Exercises the authored world Hub using real input and advancing game frames.</summary>
public static class HubWorldPlayModeRegression
{
    private const string ResultKey = "HubWorld.PlayModeRegression";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static TaskCompletionSource<bool> _nextTick;
    private static bool _running;
    private static int _checks;
    private static string _phase;
    private static DateTime _deadline;

    public static string Start()
    {
        if (!Application.isPlaying || SceneManager.GetActiveScene().name != "Hub Scene")
            throw new InvalidOperationException("Start in the authored Hub Scene during Play Mode.");
        if (_running) throw new InvalidOperationException("Hub world regression is already running.");
        _running = true;
        _checks = 0;
        _deadline = DateTime.UtcNow.AddSeconds(20);
        EditorApplication.update += Tick;
        _ = Run();
        return "Hub world acceptance started. Poll HubWorldPlayModeRegression.Status().";
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
        var priorKeyboard = Keyboard.current;
        var priorMouse = Mouse.current;
        InputActionAsset actions = null;
        ReadOnlyArray<InputDevice>? priorDevices = null;
        PlayerController player = null;
        CinemachineCamera camera = null;
        CinemachineOrbitalFollow orbit = null;
        Vector3 position = default;
        Quaternion rotation = default;
        float orbitYaw = 0f, orbitPitch = 0f;
        float sp = 0f, regen = 0f, dashCooldown = 0f;
        bool capturedPlayer = false;
        string result = null;
        try
        {
            Phase("checking authored player, cameras, collision and hidden HUD");
            var players = Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            Check(players.Length == 1, "Expected exactly one active Hub player.");
            player = players[0];
            Check(player.Input.Mode == PlayerInputHandler.InputMode.LocomotionOnly && !player.Input.AllowsCombatInput,
                "Hub player does not use locomotion-only input.");
            Check(Object.FindObjectsByType<CinemachineBrain>(FindObjectsSortMode.None).Length == 1,
                "Expected exactly one active CinemachineBrain.");
            Check(Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length == 1,
                "Expected exactly one active AudioListener.");
            var cameras = Object.FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
            Check(cameras.Length == 1 && cameras[0].Follow != null && cameras[0].Follow.IsChildOf(player.transform),
                "The gameplay camera does not follow the Hub player.");
            camera = cameras[0];
            orbit = camera.GetComponent<CinemachineOrbitalFollow>();
            Check(orbit != null, "Hub camera is missing its gameplay orbital follow.");
            Check(Object.FindObjectsByType<HubWorkshopView>(FindObjectsSortMode.None).All(view => !view.IsOpen),
                "The workshop modal must be closed before world exploration verification.");
            Check(Object.FindObjectsByType<PlayerUIManager>(FindObjectsSortMode.None).Length == 0
                && Object.FindObjectsByType<PlayerHudPresenter>(FindObjectsSortMode.None).Length == 0,
                "Player HUD components were copied into the Hub.");
            var scene = SceneManager.GetActiveScene();
            var map = scene.GetRootGameObjects().SingleOrDefault(root => root.name == "EasternLab_Model");
            var floor = map != null ? map.GetComponentsInChildren<MeshCollider>(true)
                .SingleOrDefault(collider => collider.name == "00_Floor") : null;
            Check(floor != null && floor.gameObject.layer == LayerMask.NameToLayer("Ground"),
                "The Hub floor has no Ground collider.");
            Check(scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .All(transform => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0),
                "The Hub contains a missing MonoBehaviour script.");

            position = player.transform.position;
            rotation = player.transform.rotation;
            orbitYaw = orbit.HorizontalAxis.Value;
            orbitPitch = orbit.VerticalAxis.Value;
            sp = player.StatManager.CurrentSp;
            regen = Read<float>(player.StatManager, "_spRegenTimer");
            dashCooldown = Read<float>(player, "_dashCooldownTimer");
            capturedPlayer = true;
            actions = Read<InputActionReference>(player.Input, "_moveAction").action.actionMap.asset;
            priorDevices = actions.devices;
            keyboard = InputSystem.AddDevice<Keyboard>("HubWorldAcceptanceKeyboard");
            mouse = InputSystem.AddDevice<Mouse>("HubWorldAcceptanceMouse");
            keyboard.MakeCurrent();
            mouse.MakeCurrent();
            actions.devices = new InputDevice[] { keyboard, mouse };
            Keys(keyboard);
            await Frames(3);
            await Wait(() => player.IsGround(), "Player did not settle on the Hub floor.");
            position = player.transform.position;

            Phase("walking, stopping and camera tracking");
            var before = player.transform.position;
            var cameraBefore = Camera.main.transform.position;
            var expected = Horizontal(Camera.main.transform.forward);
            Keys(keyboard, Key.W);
            await Seconds(.18f);
            Keys(keyboard);
            await Frames(2);
            var walked = HorizontalDisplacement(player.transform.position - before);
            Check(walked.magnitude > .6f && Vector3.Dot(walked.normalized, expected) > .85f,
                "W did not move the player forward relative to the camera.");
            Check(Vector3.Distance(cameraBefore, Camera.main.transform.position) > .05f,
                "The camera did not track the moving player.");
            before = player.transform.position;
            await Seconds(.12f);
            Check(HorizontalDisplacement(player.transform.position - before).magnitude < .08f,
                "Player kept walking after movement was released.");

            Phase("jumping and landing");
            Teleport(player, camera, position, rotation);
            await Frames(2);
            float groundedY = player.transform.position.y;
            Keys(keyboard, Key.Space);
            await Wait(() => player.transform.position.y > groundedY + .2f, "Jump did not raise the player.");
            Keys(keyboard);
            await Wait(() => player.IsGround() && player.VerticalVelocity <= 0f,
                "Jump did not land back on the Hub floor.");
            Check(Mathf.Abs(player.transform.position.y - groundedY) < .15f, "Player landed at the wrong floor height.");

            Phase("directional dash and backstep");
            Teleport(player, camera, position, rotation);
            await Frames(2);
            await Wait(() => player.CanDash, "Dash did not become available.");
            before = player.transform.position;
            expected = Horizontal(Camera.main.transform.forward);
            Keys(keyboard, Key.W, Key.LeftShift);
            await Wait(() => player.IsDashing, "Directional dash did not start.");
            Keys(keyboard);
            await Wait(() => !player.IsDashing, "Directional dash did not finish.");
            Check(Vector3.Dot(player.transform.position - before, expected) > 1.5f,
                "Directional dash did not cover the expected distance.");
            Teleport(player, camera, position, rotation);
            await Frames(2);
            await Wait(() => player.CanDash, "Backstep cooldown did not expire.");
            before = player.transform.position;
            expected = -Horizontal(player.transform.forward);
            Keys(keyboard, Key.LeftShift);
            await Wait(() => player.IsDashing, "Backstep did not start.");
            Keys(keyboard);
            await Wait(() => !player.IsDashing, "Backstep did not finish.");
            Check(Vector3.Dot(player.transform.position - before, expected) > 1.5f,
                "Standing dash did not move backwards.");

            Phase("blocking combat while preserving walking");
            Teleport(player, camera, position, rotation);
            await Frames(2);
            var element = player.Element;
            before = player.transform.position;
            Keys(keyboard, Key.W, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Q, Key.E, Key.R, Key.F);
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left).WithButton(MouseButton.Right));
            await Seconds(.18f);
            Check(HorizontalDisplacement(player.transform.position - before).magnitude > .6f,
                "Holding combat controls blocked movement.");
            Check(player.Element == element && !player.IsFinisherSelecting && !player.Input.IsFinisherInputAvailable
                && !player.Input.IsAttackHeld && !player.Input.FinisherPressedThisFrame
                && !player.Input.AttackBuffer.IsActive && !player.Input.SkillBuffer.IsActive
                && !player.Input.UltimateBuffer.IsActive && !player.Input.ChainSkillBuffer.IsActive
                && !player.Input.IsForm1Pressed && !player.Input.IsForm2Pressed && !player.Input.IsForm3Pressed
                && !player.Input.IsForm4Pressed && !player.Input.IsForm5Pressed && !player.Input.IsFastFormPressed,
                "A combat/form/finisher request escaped the Hub input restriction.");
            Check(player.StateMachine.CurrentState is PlayerRunState && Time.timeScale > .99f,
                "Combat input changed the locomotion state or world time.");
            Keys(keyboard);
            InputSystem.QueueStateEvent(mouse, new MouseState());
            await Frames(2);

            Phase("mouse camera control and movement after turning");
            Teleport(player, camera, position, rotation);
            await Frames(2);
            var cameraRotation = Camera.main.transform.rotation;
            for (int i = 0; i < 3; i++)
            {
                InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(25f, 0f) });
                await Frames(1);
            }
            await Frames(3);
            Check(Quaternion.Angle(cameraRotation, Camera.main.transform.rotation) > 1f,
                "Mouse look did not rotate the Hub camera.");
            expected = Horizontal(Camera.main.transform.forward);
            before = player.transform.position;
            Keys(keyboard, Key.W);
            await Seconds(.16f);
            Keys(keyboard);
            await Frames(2);
            walked = HorizontalDisplacement(player.transform.position - before);
            Check(walked.magnitude > .5f && Vector3.Dot(walked.normalized, expected) > .8f,
                "Movement did not follow the turned camera.");

            Phase("wall and facility collision");
            Teleport(player, camera, position, rotation);
            bool hitSide = false;
            for (int i = 0; i < 50; i++)
                hitSide |= (player.Controller.Move(Vector3.right * .3f) & CollisionFlags.Sides) != 0;
            Check(hitSide && player.transform.position.x < 9.3f,
                "The player passed through the east wall or facilities.");
            Check(Object.FindObjectsByType<PlayerUIManager>(FindObjectsSortMode.None).Length == 0
                && Object.FindObjectsByType<PlayerHudPresenter>(FindObjectsSortMode.None).Length == 0
                && Object.FindObjectsByType<HubWorkshopView>(FindObjectsSortMode.None).All(view => !view.IsOpen)
                && Object.FindObjectsByType<FinisherWheelView>(FindObjectsSortMode.None).Length == 0,
                "A player HUD or finisher wheel appeared while exploring.");
            result = "PASS: " + _checks + " checks; real movement/stop/jump/landing/dash/backstep, camera tracking/look, combat-held walking, hidden HUD and collisions.";
        }
        catch (Exception failure) { result = "FAIL during " + _phase + ": " + failure; }
        finally
        {
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (priorKeyboard != null && priorKeyboard.added) priorKeyboard.MakeCurrent();
            if (priorMouse != null && priorMouse.added) priorMouse.MakeCurrent();
            if (actions != null) actions.devices = priorDevices;
            if (capturedPlayer && player != null && Application.isPlaying)
            {
                try
                {
                    Teleport(player, camera, position, rotation);
                    Write(player.StatManager, "_currentSp", sp);
                    Write(player.StatManager, "_spRegenTimer", regen);
                    Write(player, "_dashCooldownTimer", dashCooldown);
                    if (orbit != null)
                    {
                        orbit.HorizontalAxis.Value = orbitYaw;
                        orbit.VerticalAxis.Value = orbitPitch;
                        camera.PreviousStateIsValid = false;
                    }
                }
                catch (Exception cleanup) { result += " Cleanup: " + cleanup.Message; }
            }
            SessionState.SetString(ResultKey, result ?? "FAIL: acceptance ended without a result.");
            EditorApplication.update -= Tick;
            _nextTick = null;
            _running = false;
        }
    }

    private static void Keys(Keyboard keyboard, params Key[] keys)
        => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));

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
        if (camera != null)
        {
            camera.OnTargetObjectWarped(camera.Follow, delta);
            camera.PreviousStateIsValid = false;
        }
    }

    private static Vector3 HorizontalDisplacement(Vector3 vector) => new(vector.x, 0f, vector.z);
    private static Vector3 Horizontal(Vector3 vector) => HorizontalDisplacement(vector).normalized;
    private static T Read<T>(object owner, string field) => (T)owner.GetType().GetField(field, Hidden).GetValue(owner);
    private static void Write<T>(object owner, string field, T value) => owner.GetType().GetField(field, Hidden).SetValue(owner, value);
    private static void Phase(string phase) { _phase = phase; SessionState.SetString(ResultKey, "RUNNING: " + phase); }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
    private static async Task Seconds(float seconds)
    {
        float until = Time.time + seconds;
        await Wait(() => Time.time >= until, "The game clock stopped advancing.");
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
}
