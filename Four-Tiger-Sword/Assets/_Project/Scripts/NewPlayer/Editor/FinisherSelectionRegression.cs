using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

/// <summary>Exercises the real input/attack/modal path in a disposable Edit Mode fixture.</summary>
public static class FinisherSelectionRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private class ProbeForm : BaseForm
    {
        private readonly ElementType _element;
        public int Updates;
        public float LastDelta;
        public float Elapsed => _timer;
        public override ElementType Element => _element;
        public ProbeForm(WeaponActionDataSO data, ElementType element) : base(data) => _element = element;
        public void Seek(float elapsed) => _timer = elapsed;
        public override void UpdateAttack(out bool complete)
        {
            Updates++;
            LastDelta = _playerController.ActionDeltaTime * AttackSpeed;
            base.UpdateAttack(out complete);
        }
    }
    // Distinct types also exercise the existing Q transition lookup keyed by form type.
    private sealed class FireProbe : ProbeForm
    {
        public FireProbe(WeaponActionDataSO data) : base(data, ElementType.ELEMENT_FIRE) { }
    }
    private sealed class EarthProbe : ProbeForm
    {
        public EarthProbe(WeaponActionDataSO data) : base(data, ElementType.ELEMENT_EARTH) { }
    }
    private sealed class NeutralState : IPlayerState
    {
        public void Enter() { }
        public void Update() { }
        public void Exit() { }
    }

    [MenuItem("Tools/Studio Baekho/Combat/Run Finisher Selection Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Finisher selection regression requires Edit Mode.");
        float savedScale = Time.timeScale;
        bool savedCursorVisible = Cursor.visible;
        CursorLockMode savedCursorLock = Cursor.lockState;
        Mouse savedMouse = Mouse.current;
        Keyboard savedKeyboard = Keyboard.current;
        UnityEngine.InputSystem.Pointer savedPointer = UnityEngine.InputSystem.Pointer.current;
        object inputManager = typeof(InputSystem).GetField("s_Manager", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        PropertyInfo runPlayerUpdates = inputManager.GetType().GetProperty("runPlayerUpdatesInEditMode");
        bool savedRunPlayerUpdates = (bool)runPlayerUpdates.GetValue(inputManager);
        var existingServices = new HashSet<CombatHitStopService>(Resources.FindObjectsOfTypeAll<CombatHitStopService>());
        var savedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        UnityEngine.SceneManagement.Scene scene = default;
        var objects = new List<Object>();
        var references = new List<InputActionReference>();
        GameObject fixture = null;
        PlayerController player = null;
        PlayerInputHandler input = null;
        InputActionMap actions = null;
        Mouse mouse = null;
        Keyboard keyboard = null;
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Finisher selection: " + message);
            checks++;
        }
        void Near(float actual, float expected, string message)
            => Check(Mathf.Abs(actual - expected) < .0001f, message + $" (actual {actual}, expected {expected})");
        void RawFrame(bool right, bool left = false, params Key[] keys)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = (ushort)((right ? 2 : 0) | (left ? 1 : 0)) });
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
            mouse.MakeCurrent();
            keyboard.MakeCurrent();
        }
        void Frame(bool right, bool left = false, params Key[] keys)
        {
            RawFrame(right, left, keys);
            Invoke(input, "Update");
        }
        void Bind(string field, string actionName)
        {
            var action = actions.FindAction(actionName, true);
            var reference = InputActionReference.Create(action);
            references.Add(reference);
            Field(input, field, reference);
        }
        try
        {
            Check(savedScale > 0f, "Fixture requires a running external world clock.");
            foreach (var service in existingServices)
            {
                var leases = (System.Collections.ICollection)GetField(service, "_worldScales");
                var stops = (System.Collections.ICollection)GetField(service, "_stops");
                Check(leases.Count == 0 && stops.Count == 0, "Existing world time owners must finish before this isolated regression runs.");
            }
            // The production ground query uses the global Physics scene, which excludes preview scenes.
            // All temporary objects are created while this disposable additive scene is active.
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            // InputActionState intentionally ignores Editor update events. This package's temporary
            // test switch makes manual updates use the real player action pipeline; it is not a
            // serialized InputSettings change and is restored even when an assertion fails.
            runPlayerUpdates.SetValue(inputManager, true);
            mouse = InputSystem.AddDevice<Mouse>("FinisherRegressionMouse");
            keyboard = InputSystem.AddDevice<Keyboard>("FinisherRegressionKeyboard");
            fixture = new GameObject("__FinisherSelectionRegression");
            fixture.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(fixture, scene);
            var host = new GameObject("Player");
            host.transform.SetParent(fixture.transform, false);
            host.transform.position = new Vector3(12345f, 1000f, 12345f);
            player = host.AddComponent<PlayerController>();
            input = host.GetComponent<PlayerInputHandler>();
            var controller = host.GetComponent<CharacterController>();
            var stats = host.GetComponent<PlayerStatManager>();
            var statData = ScriptableObject.CreateInstance<PlayerStatData>();
            objects.Add(statData);
            stats.Initialize(statData);
            Property(player, "Controller", controller);
            Property(player, "StatManager", stats);
            Property(player, "Input", input);
            Property(player, "Animator", host.AddComponent<Animator>());
            Property(player, "Movement", host.GetComponent<PlayerMovement>());
            player.Movement.Initialize(player);
            Field(player, "_attackTrails", Array.Empty<TrailVfxController>());
            Field(player, "_attackTrailsInitialized", true);
            player.Gravity = player.VerticalVelocity = 0f;
            var ground = new GameObject("Ground", typeof(BoxCollider));
            ground.transform.SetParent(fixture.transform, false);
            var box = ground.GetComponent<BoxCollider>();
            box.size = new Vector3(20f, 1f, 20f);
            Vector3 sphere = host.transform.position + controller.center
                + Vector3.down * (controller.height / 2f - controller.radius);
            ground.transform.position = sphere + Vector3.down * (controller.radius + .55f);
            Field(player, "_groundLayer", (LayerMask)(1 << ground.layer));
            var sourceData = ScriptableObject.CreateInstance<FireFormActionDataSO>();
            var targetData = ScriptableObject.CreateInstance<EarthFormActionDataSO>();
            objects.Add(sourceData); objects.Add(targetData);
            sourceData.ComboSteps = Steps();
            targetData.ComboSteps = Steps();
            // Match Water's authored second-hit timing: final hit ends at 0.60 seconds,
            // ordinary LMB chaining opens at 1.00, and the action expires at 1.40.
            sourceData.ComboSteps[1].Duration = 1.4f;
            sourceData.ComboSteps[1].ComboTransitionTime = 1f;
            sourceData.ComboSteps[1].HitEvents = new List<HitEvent>
            {
                new HitEvent { StartTime = .33f, Duration = .04f },
                new HitEvent { StartTime = .56f, Duration = .04f }
            };
            sourceData.FinisherStep = new WeaponActionData { AnimationName = "", Duration = 1f, Damage = 3f };
            var source = new FireProbe(sourceData);
            var target = new EarthProbe(targetData);
            bool fireOnly = false;
            var forms = new FormManager(player, element => !fireOnly || element == ElementType.ELEMENT_FIRE);
            var machine = new StateMachine();
            var neutral = new NeutralState();
            Property(player, "FormManager", forms);
            Property(player, "StateMachine", machine);
            machine.ChangeState(neutral);
            forms.AddTransition(source, () => input.IsForm1Pressed);
            forms.AddTransition(target, () => input.IsForm5Pressed);
            forms.AddFastTransition(source, target, () => input.IsFastFormPressed);
            forms.AddFastTransition(target, source, () => input.IsFastFormPressed);
            forms.ChangeForm(source);
            int changes = 0, successes = 0;
            forms.OnFormChanged += _ => changes++;
            forms.OnFormSwitchSucceeded += (_, __) => successes++;
            var authoredInput = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/PlayerInputActions.inputactions");
            Check(authoredInput != null, "The actual Player input asset must exist.");
            var actionAsset = InputActionAsset.FromJson(authoredInput.ToJson());
            objects.Add(actionAsset);
            actions = actionAsset.FindActionMap("Player", true);
            actions.devices = new InputDevice[] { mouse, keyboard };
            InputAction mappedFinisher = actions.FindAction("FinisherSelect", true);
            bool hasRightMouseBinding = false;
            foreach (var binding in mappedFinisher.bindings)
                hasRightMouseBinding |= binding.path == "<Mouse>/rightButton";
            Check(hasRightMouseBinding, "The actual Player/FinisherSelect action must bind RMB.");
            Bind("_attackAction", "Attack");
            Bind("_form1Action", "Form1");
            Bind("_form5Action", "Form5");
            Bind("_fastFormAction", "FastFormChange");
            Bind("_dashAction", "Sprint");
            Bind("_skillAction", "Skill");
            Bind("_ultimateAction", "Ultimate");
            fixture.SetActive(true);
            Invoke(input, "Awake");
            Physics.SyncTransforms();
            Check(player.IsGround(), "Additive fixture must satisfy the real ground query.");

            PlayerAttackState Ready()
            {
                player.CancelFinisherSelection();
                Property(player, "PendingHitReaction", PlayerHitReaction.None);
                Property(player, "IsStageTransitioning", false);
                Property(player, "IsShopPaused", false);
                stats.Initialize(statData);
                machine.ChangeState(neutral);
                forms.ChangeForm(source);
                player.transform.forward = Vector3.right;
                Field(player, "_formChangeCooldownTimer", .75f);
                Frame(false); Frame(false);
                var attack = new PlayerAttackState(player);
                machine.ChangeState(attack);
                source.Seek(.6f);
                Check(source.TryContinueAttack(), "Actual normal combo advances from stage one to stage two.");
                source.Seek(.65f);
                changes = successes = 0;
                Check(attack.CanSelectFinisher && !source.CanChainAttack,
                    "Finisher is ready after the last hit ends, while the ordinary combo transition is still closed.");
                return attack;
            }
            void Open()
            {
                Frame(true);
                Check(input.FinisherPressedThisFrame, "A fresh right click creates one finisher input edge.");
                Invoke(player, "TryOpenFinisherFromInput");
                Check(player.IsFinisherSelecting && !source.CanChainAttack,
                    "Actual RMB opens selection after the final hit and before ordinary LMB chaining is permitted.");
                Near(Time.timeScale, savedScale * .05f, "Session applies the owned five percent world speed.");
            }
            void Release()
            {
                Frame(false);
                Invoke(player, "LateUpdate");
                Check(!player.IsFinisherSelecting, "Release closes the session.");
                Near(Time.timeScale, savedScale, "Release restores only the owned world multiplier.");
            }
            FinisherWheelView Wheel() => (FinisherWheelView)GetField(player, "_finisherWheel");

            // Enabling while RMB is already held must not synthesize an opening press.
            RawFrame(true);
            Invoke(input, "OnEnable");
            Check(ReferenceEquals(typeof(PlayerInputHandler).GetProperty("FinisherAction", Hidden).GetValue(input), mappedFinisher)
                && GetField(input, "_ownedFinisherAction") == null && mappedFinisher.enabled,
                "The handler enables and resolves Player/FinisherSelect from its real attack action map, without a fallback action.");
            var state = Ready();
            // Re-arm with a preheld button exactly as focus/action-map re-enabling does.
            Invoke(input, "OnDisable");
            RawFrame(true);
            Invoke(input, "OnEnable");
            Frame(true);
            Check(!input.FinisherPressedThisFrame, "Preheld RMB does not become a fresh press after enable.");
            Invoke(player, "TryOpenFinisherFromInput");
            Check(!player.IsFinisherSelecting, "Preheld RMB cannot open during an eligible recovery.");
            Frame(false);
            Frame(true, true, Key.Digit5, Key.Q, Key.LeftShift, Key.E, Key.R);
            Check(input.FinisherPressedThisFrame && input.AttackBuffer.IsActive,
                "Simultaneous left/right presses exist before controller priority handling. " + InputSnapshot(input, mouse));
            float remaining = source.FinisherSelectionWindowRemaining;
            Invoke(player, "TryOpenFinisherFromInput");
            Check(player.IsFinisherSelecting && forms.CurrentForm == source && changes == 0,
                "Opening keeps the equipped form and does not publish preview changes.");
            Check(source.Elapsed < sourceData.ComboSteps[1].ComboTransitionTime && !source.CanChainAttack,
                "A real simultaneous RMB input opens in the final-hit-end to combo-transition gap.");
            Check(!input.AttackBuffer.IsActive && !input.DashBuffer.IsActive && !input.SkillBuffer.IsActive
                && !input.UltimateBuffer.IsActive && !input.IsForm5Pressed && !input.IsFastFormPressed,
                "RMB priority clears simultaneous attacks, form requests, dash and ability reservations.");
            Near(source.FinisherSelectionWindowRemaining, remaining, "Opening does not refill or extend the connection window.");
            Check(Wheel().SelectedElement == source.Element, "The virtual cursor starts at the current-form center.");
            int updates = source.Updates;
            float elapsed = source.Elapsed;
            state.Update();
            Check(source.Updates == updates + 1, "Selection continues dispatching the actual form attack update.");
            Near(source.Elapsed, elapsed + source.LastDelta, "Selection retains the live actor timer without freezing or resetting it.");
            Frame(true, true, Key.Digit5, Key.Q);
            Check(!input.AttackBuffer.IsActive && !input.IsForm5Pressed && !input.IsFastFormPressed,
                "Holding combat controls during selection cannot reserve a follow-up.");
            Release();
            Check(source.CurrentComboStage == 2 && forms.CurrentForm == source && changes == 0 && successes == 0,
                "Center release executes normal stage three without a switch event.");
            Check(ReferenceEquals(source.CurrentNormalAttackStep, sourceData.ComboSteps[2]),
                "Center release uses the normal third hit even when transformed data is assigned.");
            Check(!(bool)Invoke(player, "ResolveFinisherSelection"), "A completed session cannot resolve twice.");

            state = Ready(); Open();
            Field(player, "_finisherCursor", Vector2.up * 180f);
            Invoke(player, "LateUpdate");
            Release();
            Check(forms.CurrentForm == source && source.CurrentComboStage == 2 && changes == 0 && successes == 0,
                "Selecting the current outer sector also executes normal stage three without rewards.");

            state = Ready();
            Open();
            float theta = 72f * Mathf.Deg2Rad;
            Field(player, "_finisherCursor", new Vector2(Mathf.Sin(theta), Mathf.Cos(theta)) * 180f);
            Invoke(player, "LateUpdate");
            Check(Wheel().SelectedElement == target.Element && forms.CurrentForm == source && changes == 0,
                "Other-form preview highlights the destination while the source stays equipped.");
            Release();
            Check(forms.CurrentForm == target && target.CurrentComboStage == 2 && source.CurrentComboStage == -1
                && changes == 1 && successes == 1, "Other-form release switches exactly once and starts its third hit directly.");
            Near((float)GetField(player, "_formChangeCooldownTimer"), .75f, "Finisher preserves the existing switch cooldown.");
            Check(!player.HasPendingSwitchAttack, "Finisher does not queue the separate E-style switch attack.");
            int targetUpdates = target.Updates;
            state.Update();
            Check(target.Updates == targetUpdates + 1, "The attack state continues updating the destination after switching.");
            Check(!target.TryContinueAttack(), "Committed third hit cannot chain into a fourth hit.");
            Check(!(bool)Invoke(player, "ResolveFinisherSelection") && changes == 1 && successes == 1,
                "Repeated release cannot duplicate switching or rewards.");

            Frame(false, true, Key.Digit1, Key.Q);
            Check(!input.AttackBuffer.IsActive && !input.IsForm1Pressed && !input.IsFastFormPressed,
                "Controls held across release stay suppressed.");
            Frame(false); Frame(false, true, Key.Digit1);
            Check(input.AttackBuffer.IsActive && input.IsForm1Pressed && forms.GetRequestedForm() == source,
                "A new left click and numeric form key work after the neutral release barrier.");
            // Numeric requests intentionally survive zero actor time until a consumer handles them.
            input.ConsumeFormInput();
            input.AttackBuffer.Consume();
            Frame(false); Frame(false, false, Key.Q);
            Check(input.IsFastFormPressed && forms.GetRequestedForm() == null
                && ((FormTransition)Invoke(forms, "GetTransition")).TargetForm == source,
                "Q retains the existing fast transition and remains separate from numeric parry requests.");

            fireOnly = true;
            Ready(); Open();
            Field(player, "_finisherCursor", new Vector2(Mathf.Sin(theta), Mathf.Cos(theta)) * 180f);
            Invoke(player, "LateUpdate");
            Check(Wheel().SelectedElement == ElementType.ELEMENT_NONE,
                "Hovering a locked, authored destination clears the finisher selection.");
            Release();
            Check(!Wheel().IsVisible && !(bool)GetField(player, "_finisherOwnsCursor")
                && forms.CurrentForm == source && source.CurrentComboStage == 1 && changes == 0 && successes == 0,
                "Releasing over a locked sector closes the modal without committing a hit or a switch.");
            Near((float)GetField(player, "_formChangeCooldownTimer"), .75f,
                "Releasing over a locked sector preserves the existing switch cooldown.");

            foreach (Vector2 cursor in new[] { Vector2.zero, Vector2.up * 180f })
            {
                Ready(); Open();
                Field(player, "_finisherCursor", cursor);
                Release();
                Check(forms.CurrentForm == source && source.CurrentComboStage == 2
                    && ReferenceEquals(source.CurrentNormalAttackStep, sourceData.ComboSteps[2])
                    && changes == 0 && successes == 0,
                    "Both center and outer Fire select its normal third hit when all other forms are locked.");
            }
            fireOnly = false;

            state = Ready(); Open();
            elapsed = source.Elapsed;
            player.CancelFinisherSelection();
            Near(source.Elapsed, elapsed, "Cancel does not rewind the source action.");
            Frame(false); Frame(true);
            Invoke(player, "TryOpenFinisherFromInput");
            Check(!player.IsFinisherSelecting && !state.CanSelectFinisher, "One combo cannot reopen a canceled selection.");
            Near(Time.timeScale, savedScale, "Cancel releases world slowdown.");

            state = Ready(); Open();
            source.Seek(sourceData.ComboSteps[1].Duration);
            state.Update();
            Check(state.IsAttackComplete, "The source attack completes while selection is held.");
            Release();
            Check(source.CurrentComboStage == 1 && changes == 0, "Timeout wins over a simultaneous release and never executes stage three.");

            void Cancellation(string reason, Action invalidate)
            {
                Ready(); Open(); invalidate(); Release();
                Check(changes == 0 && successes == 0 && source.CurrentComboStage != 2,
                    reason + " wins over release and produces no finisher reward.");
            }
            Cancellation("Hit reaction", () => Property(player, "PendingHitReaction", PlayerHitReaction.Hit));
            Cancellation("Death", () => Field(stats, "_currentHp", 0f));
            Cancellation("Stage transition", () => Property(player, "IsStageTransitioning", true));
            Cancellation("Shop modal", () => Property(player, "IsShopPaused", true));

            Ready(); Open();
            Frame(false, false, Key.Escape);
            Invoke(player, "LateUpdate");
            Check(!player.IsFinisherSelecting && changes == 0, "Escape has priority over release.");
            Ready(); Open();
            Invoke(player, "OnApplicationFocus", false);
            Check(!player.IsFinisherSelecting && !Wheel().IsVisible, "Focus loss closes the wheel and releases ownership.");
            Ready(); Open();
            machine.ChangeState(neutral);
            Check(!player.IsFinisherSelecting && source.CurrentComboStage == -1, "Actual attack-state exit cancels and cleans its combo.");
            Ready(); Open();
            input.enabled = false;
            Invoke(input, "OnDisable");
            Check(!player.IsFinisherSelecting && !input.IsFinisherInputAvailable, "Input shutdown cancels the modal.");
            input.enabled = true;
            Invoke(input, "OnEnable");
            Ready(); Open();
            player.enabled = false;
            Invoke(player, "OnDisable");
            Check(!player.IsFinisherSelecting, "Player shutdown releases the session.");
            Near(Time.timeScale, savedScale, "All shutdown paths restore world speed.");
            return $"PASS: finisher selection integration ({checks} checks: authored Player/FinisherSelect input, RMB opening immediately after the last hit before normal combo transition, input priority and preheld release, live attack timer, normal/transformed third hit, locked release and Fire-only selection, cooldown independence, one-shot rewards, suppression, numeric/Q coexistence, timeout and cancellation precedence).";
        }
        finally
        {
            if (player != null) player.CancelFinisherSelection();
            if (input != null)
            {
                Invoke(input, "OnDisable");
                // Edit Mode does not promise the non-ExecuteAlways component's lifecycle callbacks.
                Invoke(input, "OnDestroy");
                Field(input, "_ownedFinisherAction", null);
            }
            if (fixture != null) Object.DestroyImmediate(fixture);
            foreach (var reference in references) Object.DestroyImmediate(reference);
            actions?.Dispose();
            foreach (var item in objects) Object.DestroyImmediate(item);
            foreach (var service in Resources.FindObjectsOfTypeAll<CombatHitStopService>())
                if (!existingServices.Contains(service)) Object.DestroyImmediate(service.gameObject);
            if (savedScene.IsValid() && savedScene.isLoaded)
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(savedScene);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (savedMouse != null && savedMouse.added) savedMouse.MakeCurrent();
            if (savedKeyboard != null && savedKeyboard.added) savedKeyboard.MakeCurrent();
            if (savedPointer != null && savedPointer.added) savedPointer.MakeCurrent();
            runPlayerUpdates.SetValue(inputManager, savedRunPlayerUpdates);
            Time.timeScale = savedScale;
            Cursor.visible = savedCursorVisible;
            Cursor.lockState = savedCursorLock;
        }
    }

    private static List<WeaponActionData> Steps() => new List<WeaponActionData>
    {
        Step(), Step(), Step()
    };
    private static WeaponActionData Step() => new WeaponActionData
    {
        AnimationName = "", Duration = 10f, ComboTransitionTime = .55f,
        HitStartTime = .2f, HitDuration = .1f, UseTargetApproach = false,
        ForwardDistance = 0f, ThrustMultiplier = 0f, DisableTrail = true
    };
    private static object GetField(object target, string name)
        => target.GetType().GetField(name, Hidden).GetValue(target);
    private static void Field(object target, string name, object value)
        => target.GetType().GetField(name, Hidden).SetValue(target, value);
    private static void Property(object target, string name, object value)
        => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public).SetValue(target, value);
    private static object Invoke(object target, string name, params object[] arguments)
        => target.GetType().GetMethod(name, Hidden).Invoke(target, arguments);

    private static string InputSnapshot(PlayerInputHandler input, Mouse mouse)
    {
        var attack = ((InputActionReference)GetField(input, "_attackAction"))?.action;
        var finisher = (InputAction)typeof(PlayerInputHandler).GetProperty("FinisherAction", Hidden).GetValue(input);
        return $"type={InputState.currentUpdateType}; rawLeft={mouse.leftButton.isPressed}; rawRight={mouse.rightButton.isPressed}; "
            + $"attackEnabled={attack?.enabled}; attackPressed={attack?.WasPressedThisFrame()}; attackHeld={attack?.IsPressed()}; "
            + $"finisherEnabled={finisher?.enabled}; finisherActionPressed={finisher?.WasPressedThisFrame()}; "
            + $"finisherEdge={input.FinisherPressedThisFrame}; attackBuffer={input.AttackBuffer.IsActive}; "
            + $"waitGameplay={GetField(input, "_waitForGameplayRelease")}; needsRightRelease={GetField(input, "_finisherNeedsRelease")}; "
            + $"shopBlock={GetField(input, "_shopInputBlocked")}; finisherBlock={GetField(input, "_finisherInputBlocked")}.";
    }
}
