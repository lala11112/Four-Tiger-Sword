using System;
using System.Collections.Generic;
using System.Reflection;
using StudioBaekho;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Real run modal gates with isolated items, time ownership and player state.</summary>
public static class RunItemChoiceModalRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
        float scale = Time.timeScale;
        bool visible = Cursor.visible;
        var cursor = Cursor.lockState;
        int frameRate = Application.targetFrameRate;
        var existing = new HashSet<CombatHitStopService>(Resources.FindObjectsOfTypeAll<CombatHitStopService>());
        var singleton = typeof(CombatHitStopService).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
        object previous = singleton.GetValue(null);
        GameObject root = null;
        PlayerStatData data = null;
        RunManager run = null;
        int checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
        GameObject Host(string name, Transform parent) { var go = new GameObject(name); go.transform.SetParent(parent); return go; }
        try
        {
            singleton.SetValue(null, null);
            Time.timeScale = .8f;
            root = new GameObject("__RunItemChoiceModalRegression");
            var stage = Host("Stage", root.transform).AddComponent<StageManager>();
            var generation = Host("Generation", stage.transform);
            Property(stage, "Result", new BaekhoDungeonResult { root = generation, success = true });
            Property(stage, "IsReady", true);
            var player = Host("Player", root.transform).AddComponent<PlayerController>();
            var stats = player.GetComponent<PlayerStatManager>();
            data = ScriptableObject.CreateInstance<PlayerStatData>(); data.baseHP = 100;
            stats.Initialize(data);
            Property(player, "StatManager", stats);
            Property(player, "Input", player.GetComponent<PlayerInputHandler>());
            Property(player, "Controller", player.GetComponent<CharacterController>());
            var items = player.GetComponent<PlayerItemRuntime>() ?? player.gameObject.AddComponent<PlayerItemRuntime>();
            items.Catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/_Project/Generated/BoundaryInventory/ItemCatalog.asset");
            items.Initialize(stats, () => ElementType.ELEMENT_NONE);
            var wallet = player.GetComponent<PlayerWallet>() ?? player.gameObject.AddComponent<PlayerWallet>(); wallet.Initialize(0);
            var choices = new RunItemChoiceService(items, wallet, EngravingSnapshot.Empty, 781);
            run = Host("Run", root.transform).AddComponent<RunManager>(); run.player = player; run.stageManager = stage;
            typeof(RunManager).GetField("_engravingsInitialized", Hidden).SetValue(run, true);
            var engravingSettings = EngravingSettingsSO.Default;
            typeof(RunManager).GetField("_runEngravings", Hidden).SetValue(run,
                new EngravingSnapshot(engravingSettings, Array.ConvertAll(engravingSettings.nodes, node => node.id)));
            Invoke(run, "EnsureRunItemChoices");
            var initialView = (RunItemChoiceView)typeof(RunManager).GetField("_itemChoiceView", Hidden).GetValue(run);
            Check(run.ItemChoices != null && !run.HasPendingItemChoice && !run.IsRewardOpen && !initialView.IsOpen,
                "Starting a run must not enqueue or open an unsolicited item choice, even with every engraving.");
            Check(items.OwnedItems.Count == 0 && wallet.Gold == 0, "Run initialization must not grant a starter item or replacement gold.");
            Object.DestroyImmediate(initialView.gameObject);
            typeof(RunManager).GetField("_itemChoiceView", Hidden).SetValue(run, null);
            Property(run, "State", RunState.Playing); Property(run, "ItemChoices", choices);
            var owner = Host("Owner", generation.transform);
            var camera = Host("Follow camera", root.transform).AddComponent<CinemachineCamera>(); camera.Follow = player.transform;
            var axis = camera.gameObject.AddComponent<CinemachineInputAxisController>(); axis.enabled = true;
            var inactiveCamera = Host("Disabled camera", root.transform).AddComponent<CinemachineCamera>(); inactiveCamera.Follow = player.transform;
            var inactiveAxis = inactiveCamera.gameObject.AddComponent<CinemachineInputAxisController>(); inactiveAxis.enabled = false;
            Check(run.TryOpenInventory(owner), "Inventory setup failed.");
            Check(choices.BeginRoomReward("first"), "Reward enqueue failed.");
            Invoke(run, "UpdateItemChoiceSession");
            Check(!run.IsRewardOpen && run.IsInventoryOpen, "Reward interrupted an existing modal.");
            run.CloseInventory();
            Check(!run.TryOpenInventory(owner) && !run.TryOpenMap(owner) && !run.TryOpenShop(owner), "Pending reward did not reserve modal ownership.");
            player.Input.AttackBuffer.Set(); player.Input.DashBuffer.Set();
            var slow = CombatHitStopService.AcquireWorldScale(owner, .5f);
            Invoke(run, "UpdateItemChoiceSession");
            Check(run.IsRewardOpen && player.IsRewardPaused && player.IsGameplayModalPaused && Time.timeScale == 0f,
                "Reward failed to pause the world and player.");
            Check(!player.Input.AttackBuffer.IsActive && !player.Input.DashBuffer.IsActive, "Reward retained combat inputs.");
            Check(!axis.enabled && !inactiveAxis.enabled && player.Controller.enabled, "Reward camera/collider ownership is incorrect.");
            Check(Cursor.visible && Cursor.lockState == CursorLockMode.None && player.ActionDeltaTime == 0f,
                "Reward did not expose cursor or stop action time.");
            string id = choices.Current.Id;
            Check(choices.TryClaim(id, choices.Current.Candidates[0].itemId), "Reward selection failed.");
            Invoke(run, "UpdateItemChoiceSession");
            Check(!run.IsRewardOpen && !player.IsRewardPaused && axis.enabled && !inactiveAxis.enabled,
                "Selection stranded pause/input/camera state.");
            Check(Mathf.Abs(Time.timeScale - .4f) < .0001f && slow.IsValid, "Reward close released another time owner.");
            slow.Dispose();
            Check(Mathf.Abs(Time.timeScale - .8f) < .0001f, "Closing all leases did not restore initial time.");
            choices.BeginRoomReward("transition"); Invoke(run, "UpdateItemChoiceSession");
            Property(player, "IsStageTransitioning", true); player.Input.enabled = false;
            Invoke(run, "UpdateItemChoiceSession");
            Check(!run.IsRewardOpen && !player.IsRewardPaused && !axis.enabled && !player.Input.enabled && choices.Current != null,
                "Transition either lost the candidate receipt or resumed transition-owned input.");
            Property(player, "IsStageTransitioning", false); player.Input.enabled = true; axis.enabled = true;
            Invoke(run, "UpdateItemChoiceSession");
            Check(run.IsRewardOpen, "Retained reward did not reopen after transition.");
            choices.TrySkip(choices.Current.Id); Invoke(run, "UpdateItemChoiceSession");
            Check(wallet.Gold == 0 && !run.IsRewardOpen, "Explicit skip paid fallback gold or retained the modal.");
            choices.BeginRoomReward("finisher");
            typeof(PlayerController).GetField("_finisherAttack", Hidden).SetValue(player, new PlayerAttackState(player));
            Invoke(run, "UpdateItemChoiceSession");
            Check(!run.IsRewardOpen, "Reward interrupted finisher selection.");
            typeof(PlayerController).GetField("_finisherAttack", Hidden).SetValue(player, null);
            Invoke(run, "UpdateItemChoiceSession");
            Invoke(run, "OnDisable");
            Check(!run.IsRewardOpen && !player.IsRewardPaused && Mathf.Abs(Time.timeScale - .8f) < .0001f,
                "Shutdown stranded reward ownership.");
            return $"PASS: reward modal ownership ({checks} checks: pending exclusion, input, time leases, cameras, transition, finisher and skip).";
        }
        finally
        {
            if (run != null) Invoke(run, "CloseItemChoiceModal");
            if (root != null) Object.DestroyImmediate(root);
            if (data != null) Object.DestroyImmediate(data);
            foreach (var service in Resources.FindObjectsOfTypeAll<CombatHitStopService>())
                if (!existing.Contains(service)) Object.DestroyImmediate(service.gameObject);
            singleton.SetValue(null, previous);
            Time.timeScale = scale; Cursor.visible = visible; Cursor.lockState = cursor; Application.targetFrameRate = frameRate;
        }
    }
    private static void Property(object target, string name, object value)
        => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public).SetValue(target, value);
    private static void Invoke(object target, string name, params object[] values)
        => target.GetType().GetMethod(name, Hidden).Invoke(target, values);
}
