using System;
using System.Collections.Generic;
using System.Reflection;
using StudioBaekho;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Isolated inventory pause/input ownership fixtures; no scene asset or input device is changed.</summary>
public static class InventoryModalRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Studio Baekho/Dungeon/Run Inventory Modal Regression")]
    private static void RunMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run inventory modal ownership fixtures in Edit Mode.");
        float savedScale = Time.timeScale;
        bool savedCursorVisible = Cursor.visible;
        CursorLockMode savedCursorLock = Cursor.lockState;
        int savedFrameRate = Application.targetFrameRate;
        var services = new HashSet<CombatHitStopService>(Resources.FindObjectsOfTypeAll<CombatHitStopService>());
        var instanceField = typeof(CombatHitStopService).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
        object savedInstance = instanceField.GetValue(null);
        GameObject fixture = null;
        RunManager run = null;
        int checks = 0;
        void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException("Inventory modal: " + message); checks++; }
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
            // Keep any pre-existing service/leases detached from this synchronous test's time ownership.
            instanceField.SetValue(null, null);
            Time.timeScale = .8f;
            fixture = Host("__InventoryModalRegression", null);
            var stage = Host("Stage", fixture.transform).AddComponent<StageManager>();
            var generation = Host("Generation", stage.transform);
            var player = Host("Player", fixture.transform).AddComponent<PlayerController>();
            var input = player.GetComponent<PlayerInputHandler>();
            var controller = player.GetComponent<CharacterController>();
            Property(player, "Input", input);
            Property(player, "Controller", controller);
            Property(player, "StatManager", player.GetComponent<PlayerStatManager>());
            var probe = new ProbeState();
            var machine = new StateMachine();
            machine.ChangeState(probe);
            Property(player, "StateMachine", machine);
            Property(stage, "Result", new BaekhoDungeonResult { root = generation, success = true });
            Property(stage, "IsReady", true);
            run = Host("Run", fixture.transform).AddComponent<RunManager>();
            run.player = player;
            run.stageManager = stage;
            Property(run, "State", RunState.Playing);
            var owner = Host("InventoryOwner", generation.transform);
            var other = Host("OtherOwner", generation.transform);
            var camera = Host("Tracked camera", fixture.transform).AddComponent<CinemachineCamera>();
            camera.Follow = player.transform;
            var axis = camera.gameObject.AddComponent<CinemachineInputAxisController>();
            axis.enabled = true;
            var disabledCamera = Host("Disabled camera", fixture.transform).AddComponent<CinemachineCamera>();
            disabledCamera.Follow = player.transform;
            var disabledAxis = disabledCamera.gameObject.AddComponent<CinemachineInputAxisController>();
            disabledAxis.enabled = false;
            var unrelatedCamera = Host("Unrelated camera", fixture.transform).AddComponent<CinemachineCamera>();
            unrelatedCamera.Follow = other.transform;
            var unrelatedAxis = unrelatedCamera.gameObject.AddComponent<CinemachineInputAxisController>();
            unrelatedAxis.enabled = true;
            int opens = 0, closes = 0;
            run.InventoryOpenChanged += open => { if (open) opens++; else closes++; };

            var slow = CombatHitStopService.AcquireWorldScale(other, .5f);
            var stopOwner = Host("CombatStopOwner", fixture.transform);
            CombatHitStopService.Apply(stopOwner, player, null,
                new HitStopSettings { Scope = HitStopScope.Global, HitStopDuration = 30f, HitStopTimeScale = 0f });
            Near(Time.timeScale, 0f, "Combat stop fixture did not start paused");
            input.AttackBuffer.Set();
            input.DashBuffer.Set();
            float hp = player.StatManager.CurrentHp;
            Check(run.TryOpenInventory(owner), "An active combat stop prevented opening the inventory");
            Check(run.State == RunState.Playing && run.IsInventoryOpen && player.IsInventoryPaused
                && player.IsGameplayModalPaused && !player.IsShopPaused, "Inventory and shop ownership were conflated");
            Check(controller.enabled && !input.AttackBuffer.IsActive && !input.DashBuffer.IsActive,
                "Opening disabled the combat-query collider or retained action buffers");
            Check(!axis.enabled && !disabledAxis.enabled && unrelatedAxis.enabled, "Inventory camera input ownership is incorrect");
            Check(Cursor.visible && Cursor.lockState == CursorLockMode.None, "Inventory did not expose the pointer");
            Check(run.TryOpenInventory(owner) && opens == 1 && !run.TryOpenInventory(other), "Inventory opening is not owner-idempotent");
            Check(!run.TryOpenShop(other) && !run.TryOpenMap(other), "Another modal opened while the inventory owned input");
            Invoke(player, "Update");
            Check(machine.CurrentState == probe && probe.Updates == 0 && probe.Exits == 0
                && player.StatManager.CurrentHp == hp, "Inventory pause advanced or restarted the retained action/resources");
            Check(player.ActionDeltaTime == 0f && player.ActionTimeScale == 0f
                && !player.CanDash && !player.CanChangeForm && !player.CanSkill && !player.CanUltimate && !player.CanJump(),
                "A gameplay action remained available while browsing the inventory");
            run.CloseInventory();
            Near(Time.timeScale, 0f, "Inventory close discarded an existing global combat stop");
            Check(slow.IsValid && !run.IsInventoryOpen && !player.IsInventoryPaused && !player.IsGameplayModalPaused
                && opens == 1 && closes == 1, "Inventory close discarded another owner's lease or left modal flags set");
            Check(axis.enabled && !disabledAxis.enabled && unrelatedAxis.enabled && controller.enabled,
                "Inventory close changed unrelated/initially disabled camera input or collider state");
            input.AttackBuffer.Set();
            Invoke(input, "Update");
            Check(!input.AttackBuffer.IsActive, "Closing replayed a gameplay press before a neutral release frame");
            run.CloseInventory();
            Check(closes == 1, "Repeated close published a second event");
            CombatHitStopService.ReleaseOwner(stopOwner);
            Near(Time.timeScale, .4f, "Inventory ownership lost the other owner's slowdown");
            slow.Dispose();
            Near(Time.timeScale, .8f, "Owned slowdown did not restore its own prior scale");
            Check(run.TryOpenMap(owner) && !run.TryOpenInventory(other), "Inventory opened over the full map");
            run.CloseMap();

            Invoke(player, "SetShopPaused", true);
            Check(!run.TryOpenInventory(owner), "Inventory opened while another player modal owned input");
            Invoke(player, "SetInventoryPaused", true);
            Invoke(player, "SetInventoryPaused", false);
            input.AttackBuffer.Set();
            Invoke(input, "Update");
            Check(player.IsShopPaused && player.IsGameplayModalPaused && !input.AttackBuffer.IsActive,
                "Releasing inventory input also released the shop's input gate");
            Invoke(player, "SetShopPaused", false);
            Field(player, "_finisherAttack", new PlayerAttackState(player));
            Check(!run.TryOpenInventory(owner), "Inventory opened over finisher selection");
            Field(player, "_finisherAttack", null);

            Check(run.TryOpenInventory(owner), "Inventory did not reopen after releasing other modals");
            var otherPause = CombatHitStopService.AcquireWorldScale(other, 0f);
            run.CloseInventory();
            Near(Time.timeScale, 0f, "Closing inventory resumed another zero-scale lease");
            Check(otherPause.IsValid, "Closing inventory invalidated another zero-scale lease");
            otherPause.Dispose();
            Near(Time.timeScale, .8f, "Releasing the remaining pause did not restore time");
            Check(run.TryOpenInventory(owner), "Inventory did not reopen before owner invalidation");
            owner.SetActive(false);
            Invoke(run, "Update");
            Check(!run.IsInventoryOpen && !player.IsInventoryPaused, "A disabled inventory owner stranded its pause/input gate");
            Near(Time.timeScale, .8f, "Owner invalidation stranded inventory time ownership");
            owner.SetActive(true);
            Check(run.TryOpenInventory(owner), "Inventory did not reopen before transition ownership check");
            Property(player, "IsStageTransitioning", true);
            input.enabled = false;
            axis.enabled = false;
            run.CloseInventory();
            Check(!input.enabled && !axis.enabled && !player.IsInventoryPaused,
                "Closing the inventory resumed input/camera already owned by a transition");
            Property(player, "IsStageTransitioning", false);
            input.enabled = true;
            axis.enabled = true;
            Check(run.TryOpenInventory(owner), "Inventory did not reopen before focus loss");
            Invoke(run, "OnApplicationFocus", false);
            Check(!run.IsInventoryOpen && !player.IsInventoryPaused, "Focus loss left inventory ownership active");
            Near(Time.timeScale, .8f, "Focus loss stranded inventory time ownership");
            Check(run.TryOpenInventory(owner), "Inventory did not reopen before state transition");
            Invoke(run, "SetState", RunState.Preparing);
            Check(!run.IsInventoryOpen && !player.IsInventoryPaused && !run.TryOpenInventory(owner), "Preparing state retained or reopened an inventory modal");
            Property(run, "State", RunState.Playing);
            Check(run.TryOpenInventory(owner), "Inventory did not reopen before shutdown");
            Invoke(run, "OnDisable");
            Check(!run.IsInventoryOpen && !player.IsInventoryPaused, "Run shutdown stranded inventory ownership");
            Near(Time.timeScale, .8f, "Run shutdown stranded inventory time ownership");
            return $"PASS: inventory modal ownership ({checks} checks).";
        }
        finally
        {
            if (run != null) Invoke(run, "CleanupInventoryOnShutdown");
            if (fixture != null) Object.DestroyImmediate(fixture);
            foreach (var service in Resources.FindObjectsOfTypeAll<CombatHitStopService>())
                if (!services.Contains(service)) Object.DestroyImmediate(service.gameObject);
            instanceField.SetValue(null, savedInstance);
            Time.timeScale = savedScale;
            Application.targetFrameRate = savedFrameRate;
            Cursor.visible = savedCursorVisible;
            Cursor.lockState = savedCursorLock;
        }
    }

    private static void Property(object target, string name, object value)
        => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public).SetValue(target, value);
    private static void Field(object target, string name, object value)
        => target.GetType().GetField(name, Hidden).SetValue(target, value);
    private static void Invoke(object target, string name, params object[] values)
        => target.GetType().GetMethod(name, Hidden).Invoke(target, values);
    private sealed class ProbeState : IPlayerState
    {
        public int Updates, Exits;
        public void Enter() { }
        public void Update() => Updates++;
        public void Exit() => Exits++;
    }
}
