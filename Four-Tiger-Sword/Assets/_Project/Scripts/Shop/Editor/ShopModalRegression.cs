using System;
using System.Collections.Generic;
using System.Reflection;
using StudioBaekho;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Isolated modal ownership checks; no live player, scene assets or input devices are changed.</summary>
public static class ShopModalRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run modal ownership fixtures in Edit Mode.");
        float savedScale = Time.timeScale;
        bool savedCursorVisible = Cursor.visible;
        CursorLockMode savedCursorLock = Cursor.lockState;
        var existingServices = new HashSet<CombatHitStopService>(Resources.FindObjectsOfTypeAll<CombatHitStopService>());
        GameObject fixture = null;
        RunManager run = null;
        FireFormActionDataSO fireActions = null;
        WaterFormActionDataSO waterActions = null;
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }
        void Near(float actual, float expected, string message)
            => Check(Mathf.Abs(actual - expected) < .0001f, message + $" (actual {actual}, expected {expected})");
        GameObject Host(string name, Transform parent)
        {
            var host = new GameObject(name);
            if (parent != null) host.transform.SetParent(parent, false);
            return host;
        }
        try
        {
            fixture = Host("__ShopModalRegression", null);
            var stage = Host("Stage", fixture.transform).AddComponent<StageManager>();
            var root = Host("Generation", stage.transform);
            var player = Host("Player", fixture.transform).AddComponent<PlayerController>();
            var input = player.GetComponent<PlayerInputHandler>();
            Property(player, "Input", input);
            Property(player, "Controller", player.GetComponent<CharacterController>());
            Property(player, "StatManager", player.GetComponent<PlayerStatManager>());
            var state = new ProbeState();
            var machine = new StateMachine();
            machine.ChangeState(state);
            Property(player, "StateMachine", machine);
            fireActions = ScriptableObject.CreateInstance<FireFormActionDataSO>();
            waterActions = ScriptableObject.CreateInstance<WaterFormActionDataSO>();
            fireActions.SwitchAttackSteps.Add(new WeaponActionData { Duration = 1f });
            var fire = new ProbeForm(fireActions, ElementType.ELEMENT_FIRE);
            var water = new ProbeForm(waterActions, ElementType.ELEMENT_WATER);
            var forms = new FormManager(player);
            forms.AddTransition(fire, () => false);
            forms.AddTransition(water, () => false);
            Property(player, "FormManager", forms);
            forms.ChangeForm(water);
            forms.NotifyFinalComboHit(water.Element);
            Check(forms.ReadyChainSkill == fire, "Fixture has an eligible fire chain before shop interaction");
            Property(stage, "Result", new BaekhoDungeonResult { root = root, success = true });
            Property(stage, "IsReady", true);
            run = Host("Run", fixture.transform).AddComponent<RunManager>();
            run.player = player;
            run.stageManager = stage;
            Property(run, "State", RunState.Playing);
            var owner = Host("Owner", root.transform);
            var camera = Host("Camera", fixture.transform).AddComponent<CinemachineCamera>();
            camera.Follow = player.transform;
            var cameraInput = camera.gameObject.AddComponent<CinemachineInputAxisController>();
            cameraInput.enabled = true;
            var disabledCamera = Host("Disabled camera", fixture.transform).AddComponent<CinemachineCamera>();
            disabledCamera.Follow = player.transform;
            var disabledInput = disabledCamera.gameObject.AddComponent<CinemachineInputAxisController>();
            disabledInput.enabled = false;
            int opens = 0, closes = 0;
            run.ShopOpenChanged += open => { if (open) opens++; else closes++; };

            CombatHitStopService.SetExternalWorldTimeScale(.75f);
            CombatHitStopService.Apply(owner, player, null,
                new HitStopSettings { Scope = HitStopScope.Global, HitStopDuration = 30f, HitStopTimeScale = 0f });
            Near(Time.timeScale, 0f, "Fixture starts inside a global combat stop");
            input.AttackBuffer.Set();
            input.DashBuffer.Set();
            input.ChainSkillBuffer.Set();
            Check(run.TryOpenShop(owner), "A live stage accepts an available shop owner");
            Check(run.State == RunState.Playing && run.IsShopOpen && player.IsShopPaused, "Browsing retains Playing and gates the player");
            Check(!input.AttackBuffer.IsActive && !input.DashBuffer.IsActive && !input.ChainSkillBuffer.IsActive,
                "Opening consumes the shared F combat reservation with the other gameplay buffers");
            forms.UpdateChainReadiness(1f);
            Near(forms.GetChainReadyRemaining(fire.Element), 5f, "Browsing preserves and pauses unconsumed chain readiness");
            Check(!cameraInput.enabled && !disabledInput.enabled, "Opening blocks tracked camera input");
            Check(!run.TryOpenShop(root), "Another owner cannot replace the open session");
            Invoke(player, "Update");
            Check(machine.CurrentState == state && state.Exits == 0 && state.Updates == 0, "The modal preserves the active state without updating or exiting it");
            Check(forms.CurrentForm == water && forms.GetChainReadyRemaining(fire.Element) == 5f,
                "A shop opened before the combat update cannot also consume a ready chain");
            run.CloseShop();
            Near(Time.timeScale, .75f, "Closing restores pre-hitstop world speed");
            Check(cameraInput.enabled && !disabledInput.enabled, "Closing restores only camera inputs that were enabled");
            Check(!run.IsShopOpen && !player.IsShopPaused && opens == 1 && closes == 1, "Open and close publish one event each");
            Check(forms.ReadyChainSkill == fire && !input.ChainSkillBuffer.IsActive,
                "Closing restores chain eligibility while requiring a fresh F press");
            input.AttackBuffer.Set();
            Invoke(input, "Update");
            Check(!input.AttackBuffer.IsActive, "The first neutral frame after close discards remaining gameplay input");
            run.CloseShop();
            Check(closes == 1, "Closing is idempotent");

            Check(run.TryOpenShop(owner), "The shop reopens after normal close");
            ulong shopRevision = CombatHitStopService.ExternalWorldTimeRevision;
            CombatHitStopService.SetExternalWorldTimeScale(0f);
            Check(CombatHitStopService.ExternalWorldTimeRevision != shopRevision, "Another zero write changes external ownership");
            run.CloseShop();
            Near(Time.timeScale, 0f, "Close preserves another owner's same-zero pause");
            Check(!run.TryOpenShop(owner), "A genuine external pause rejects shop acquisition");

            CombatHitStopService.SetExternalWorldTimeScale(.75f);
            Check(run.TryOpenShop(owner), "Opening works after the external pause is released");
            owner.SetActive(false);
            Invoke(run, "Update");
            Check(!run.IsShopOpen && !player.IsShopPaused, "A disabled owner releases its session");
            Near(Time.timeScale, .75f, "Owner invalidation releases owned world time");
            owner.SetActive(true);

            Check(run.TryOpenShop(owner), "The owner can reopen after becoming active");
            Property(player, "IsStageTransitioning", true);
            input.enabled = false;
            cameraInput.enabled = false;
            run.CloseShop();
            Check(!input.enabled && !cameraInput.enabled, "Closing never reenables input already owned by a transition");
            Near(Time.timeScale, .75f, "Forced close still releases its own world pause");
            Property(player, "IsStageTransitioning", false);
            input.enabled = true;

            Check(run.TryOpenShop(owner), "A completed transition permits a fresh session");
            Invoke(run, "OnDisable");
            Check(!run.IsShopOpen && !player.IsShopPaused, "Manager shutdown removes its modal gate");
            Near(Time.timeScale, .75f, "Manager shutdown cannot strand its world pause");
            return $"PASS: shop modal ownership ({checks} checks).";
        }
        finally
        {
            if (run != null) Invoke(run, "CleanupShopOnShutdown");
            if (fixture != null) Object.DestroyImmediate(fixture);
            if (fireActions != null) Object.DestroyImmediate(fireActions);
            if (waterActions != null) Object.DestroyImmediate(waterActions);
            foreach (var service in Resources.FindObjectsOfTypeAll<CombatHitStopService>())
                if (!existingServices.Contains(service)) Object.DestroyImmediate(service.gameObject);
            CombatHitStopService.SetExternalWorldTimeScale(savedScale);
            Cursor.visible = savedCursorVisible;
            Cursor.lockState = savedCursorLock;
        }
    }

    private static void Property(object target, string name, object value)
        => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public).SetValue(target, value);

    private static void Invoke(object target, string name)
        => target.GetType().GetMethod(name, Hidden).Invoke(target, null);

    private sealed class ProbeState : IPlayerState
    {
        public int Updates, Exits;
        public void Enter() { }
        public void Update() => Updates++;
        public void Exit() => Exits++;
    }

    private sealed class ProbeForm : BaseForm
    {
        private readonly ElementType _element;
        public override ElementType Element => _element;
        public ProbeForm(WeaponActionDataSO data, ElementType element) : base(data) => _element = element;
    }
}
