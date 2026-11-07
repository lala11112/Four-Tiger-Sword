using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

/// <summary>Exercises authored controls through the real input pipeline without touching live scenes or input assets.</summary>
public static class PlayerInputModeRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Studio Baekho/Progression/Run Hub Input Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Player input mode regression requires Edit Mode.");
        Keyboard savedKeyboard = Keyboard.current;
        Mouse savedMouse = Mouse.current;
        Gamepad savedGamepad = Gamepad.current;
        UnityEngine.InputSystem.Pointer savedPointer = UnityEngine.InputSystem.Pointer.current;
        object inputManager = typeof(InputSystem).GetField("s_Manager", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        PropertyInfo playerUpdates = inputManager.GetType().GetProperty("runPlayerUpdatesInEditMode");
        bool savedPlayerUpdates = (bool)playerUpdates.GetValue(inputManager);
        var scene = EditorSceneManager.NewPreviewScene();
        var references = new List<InputActionReference>();
        GameObject host = null;
        PlayerInputHandler input = null;
        InputActionAsset asset = null;
        InputActionMap actions = null;
        Keyboard keyboard = null;
        Mouse mouse = null;
        Gamepad gamepad = null;
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Player input mode: " + message);
            checks++;
        }
        void Invoke(string name, params object[] arguments)
            => typeof(PlayerInputHandler).GetMethod(name, Hidden).Invoke(input, arguments);
        void Frame(bool attack = false, bool finisher = false, Vector2 stick = default, params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.QueueStateEvent(mouse, new MouseState
            {
                buttons = (ushort)((attack ? 1 : 0) | (finisher ? 2 : 0)),
                delta = stick == Vector2.zero ? new Vector2(12f, -4f) : Vector2.zero
            });
            InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = stick });
            InputSystem.Update();
            keyboard.MakeCurrent(); mouse.MakeCurrent(); gamepad.MakeCurrent();
            Invoke("Update");
        }
        void Bind(string field, string action)
        {
            var reference = InputActionReference.Create(actions.FindAction(action, true));
            references.Add(reference);
            typeof(PlayerInputHandler).GetField(field, Hidden).SetValue(input, reference);
        }
        void NoCombat(string context)
        {
            Check(!input.IsAttackHeld && input.AttackHeldTime == 0f && !input.AttackBuffer.IsActive,
                context + ": attack input escaped the Hub restriction.");
            Check(!input.IsForm1Pressed && !input.IsForm2Pressed && !input.IsForm3Pressed
                && !input.IsForm4Pressed && !input.IsForm5Pressed && !input.IsFastFormPressed,
                context + ": form input escaped the Hub restriction.");
            Check(!input.SkillBuffer.IsActive && !input.UltimateBuffer.IsActive && !input.ChainSkillBuffer.IsActive,
                context + ": skill input escaped the Hub restriction.");
            Check(!input.FinisherPressedThisFrame && !input.IsFinisherHeld && !input.IsFinisherInputAvailable
                && input.FinisherPointerDelta == Vector2.zero, context + ": direct finisher polling remains available.");
        }
        void FullCombat(string context)
        {
            Check(input.IsAttackHeld && input.AttackBuffer.IsActive, context + ": normal attack is unavailable.");
            Check(input.IsForm1Pressed && input.IsForm2Pressed && input.IsForm3Pressed
                && input.IsForm4Pressed && input.IsForm5Pressed && input.IsFastFormPressed,
                context + ": numeric/Q forms are unavailable.");
            Check(input.SkillBuffer.IsActive && input.UltimateBuffer.IsActive && input.ChainSkillBuffer.IsActive,
                context + ": normal skills are unavailable.");
            Check(input.FinisherPressedThisFrame && input.IsFinisherHeld && input.IsFinisherInputAvailable,
                context + ": normal finisher selection is unavailable.");
        }
        var combatKeys = new[] { Key.W, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Q, Key.E, Key.R, Key.F };
        var allKeys = new[] { Key.W, Key.Space, Key.LeftShift, Key.Digit1, Key.Digit2, Key.Digit3,
            Key.Digit4, Key.Digit5, Key.Q, Key.E, Key.R, Key.F };
        try
        {
            playerUpdates.SetValue(inputManager, true);
            keyboard = InputSystem.AddDevice<Keyboard>("HubInputRegressionKeyboard");
            mouse = InputSystem.AddDevice<Mouse>("HubInputRegressionMouse");
            gamepad = InputSystem.AddDevice<Gamepad>("HubInputRegressionGamepad");
            var authored = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/PlayerInputActions.inputactions");
            Check(authored != null, "The authored Player input asset is missing.");
            asset = InputActionAsset.FromJson(authored.ToJson());
            actions = asset.FindActionMap("Player", true);
            actions.devices = new InputDevice[] { keyboard, mouse, gamepad };
            host = new GameObject("__HubInputRegression");
            host.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene);
            input = host.AddComponent<PlayerInputHandler>();
            Bind("_moveAction", "Move"); Bind("_jumpAction", "Jump"); Bind("_dashAction", "Sprint");
            Bind("_attackAction", "Attack"); Bind("_skillAction", "Skill"); Bind("_ultimateAction", "Ultimate");
            Bind("_form1Action", "Form1"); Bind("_form2Action", "Form2"); Bind("_form3Action", "Form3");
            Bind("_form4Action", "Form4"); Bind("_form5Action", "Form5"); Bind("_fastFormAction", "FastFormChange");
            Bind("_finisherAction", "FinisherSelect"); Bind("_chainSkillAction", "ChainSkill");
            host.SetActive(true);
            Invoke("Awake"); Invoke("OnEnable");
            var look = actions.FindAction("Look", true);
            look.Enable();
            Frame(); Frame();
            Check(input.Mode == PlayerInputHandler.InputMode.FullGameplay && input.AllowsCombatInput,
                "Existing players must default to full gameplay controls.");
            Frame(true, true, default, allKeys);
            Check(input.MoveInput == Vector2.up && input.JumpBuffer.IsActive && input.DashBuffer.IsActive,
                "Default gameplay lost movement, jump or dash.");
            FullCombat("Default gameplay");
            uint attackPresses = input.AttackPressId;

            Invoke("SetInputMode", PlayerInputHandler.InputMode.LocomotionOnly);
            NoCombat("Mode change");
            Frame(true, true, default, combatKeys);
            Check(input.MoveInput == Vector2.zero, "Mode changes must discard the first neutral gameplay frame.");
            Frame(true, true, default, combatKeys);
            Check(input.MoveInput == Vector2.up, "Held combat controls blocked walking after changing mode.");
            NoCombat("Held combat controls");
            Frame(true, true, default, allKeys);
            Check(input.MoveInput == Vector2.up && input.JumpBuffer.IsActive && input.DashBuffer.IsActive,
                "Hub movement, jump and dash must remain available alongside combat key presses.");
            NoCombat("Simultaneous movement and combat");
            Check(input.AttackPressId == attackPresses, "Suppressed attacks still advanced the press sequence.");
            Check(look.enabled && look.ReadValue<Vector2>() != Vector2.zero,
                "Hub restrictions disabled shared mouse-look actions.");

            input.AttackBuffer.Set(); input.SkillBuffer.Set(); input.UltimateBuffer.Set(); input.ChainSkillBuffer.Set();
            typeof(PlayerInputHandler).GetProperty("IsForm1Pressed").SetValue(input, true);
            typeof(PlayerInputHandler).GetField("_wasActionStopped", Hidden).SetValue(input, true);
            Frame(true, true, default, combatKeys);
            NoCombat("Stale buffered actions");

            foreach (string modal in new[] { "SetShopInputBlocked", "SetMapInputBlocked" })
            {
                Invoke(modal, true);
                Frame(true, true, default, combatKeys);
                Check(input.MoveInput == Vector2.zero, modal + " did not block movement.");
                Invoke(modal, false);
                Frame(true, true, default, combatKeys);
                Frame(true, true, default, combatKeys);
                Check(input.MoveInput == Vector2.up, modal + " waits for disabled combat controls to be released.");
                NoCombat(modal);
            }
            Invoke("SetShopInputBlocked", true);
            Invoke("SetShopInputBlocked", false);
            Frame(false, false, default, Key.W, Key.Space);
            Frame(false, false, default, Key.W, Key.Space);
            Check(input.MoveInput == Vector2.zero && !input.JumpBuffer.IsActive,
                "Held jump bypassed the post-modal release guard.");
            Frame(false, false, default, Key.W);
            Frame(false, false, default, Key.W);
            Check(input.MoveInput == Vector2.up, "Releasing jump did not restore Hub movement.");
            Frame(false, false, Vector2.up);
            Check(input.MoveInput == Vector2.zero && look.enabled && look.ReadValue<Vector2>() == Vector2.up,
                "Hub mode must preserve the authored gamepad stick binding to Look.");

            Invoke("SetInputMode", PlayerInputHandler.InputMode.FullGameplay);
            Frame(true, true, default, combatKeys);
            Check(input.MoveInput == Vector2.zero && !input.AttackBuffer.IsActive,
                "Returning to gameplay replayed held combat inputs.");
            Frame(); Frame();
            Frame(true, true, default, allKeys);
            FullCombat("Restored gameplay");
            Check(input.MoveInput == Vector2.up && input.JumpBuffer.IsActive && input.DashBuffer.IsActive,
                "Returning to gameplay lost locomotion.");
            return $"PASS: Hub input modes ({checks} checks: authored keyboard movement and gamepad look, jump/dash, combat suppression, direct finisher guards, modal release, shared mouse look, default and restored gameplay).";
        }
        finally
        {
            if (input != null)
            {
                Invoke("OnDisable"); Invoke("OnDestroy");
                typeof(PlayerInputHandler).GetField("_ownedFinisherAction", Hidden).SetValue(input, null);
                typeof(PlayerInputHandler).GetField("_ownedChainSkillAction", Hidden).SetValue(input, null);
            }
            if (host != null) Object.DestroyImmediate(host);
            foreach (var reference in references) Object.DestroyImmediate(reference);
            actions?.Dispose();
            if (asset != null) Object.DestroyImmediate(asset);
            EditorSceneManager.ClosePreviewScene(scene);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (gamepad != null && gamepad.added) InputSystem.RemoveDevice(gamepad);
            if (savedKeyboard != null && savedKeyboard.added) savedKeyboard.MakeCurrent();
            if (savedMouse != null && savedMouse.added) savedMouse.MakeCurrent();
            if (savedGamepad != null && savedGamepad.added) savedGamepad.MakeCurrent();
            if (savedPointer != null && savedPointer.added) savedPointer.MakeCurrent();
            playerUpdates.SetValue(inputManager, savedPlayerUpdates);
        }
    }
}
