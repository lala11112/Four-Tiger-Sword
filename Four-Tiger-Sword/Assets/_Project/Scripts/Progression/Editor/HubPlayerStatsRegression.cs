using System;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Checks saved and newly purchased progression against the Hub player's ability panel.</summary>
public static class HubPlayerStatsRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Studio Baekho/Progression/Run Hub Player Stats Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run the Hub player stats regression in Edit Mode.");

        var preview = EditorSceneManager.NewPreviewScene();
        var data = ScriptableObject.CreateInstance<PlayerStatData>();
        var settings = ScriptableObject.CreateInstance<PlayerUpgradeSettingsSO>();
        var engravings = ScriptableObject.CreateInstance<EngravingSettingsSO>();
        var cursorLock = Cursor.lockState;
        var cursorVisible = Cursor.visible;
        HubWorldController hub = null;
        PlayerUpgradeBinding binding = null;
        bool hubEnabled = false;

        try
        {
            data.baseHP = 200f;
            data.baseATK = 100f;
            data.baseDEF = 50f;
            data.baseCRT = 10f;
            settings.basePlayerStats = data;
            var profile = PlayerUpgradeProfile.New();
            profile.currency = 500;
            profile.attackLevel = 1;
            profile.unlockedNodeIds = new[] { "atk", "crit", "severe" };
            var store = new MemoryStore(profile);
            var service = new PlayerUpgradeService(settings, store, engravings);
            Check(service.IsReady, service.LastError);
            using var account = PlayerUpgradeService.OverrideForTesting(service);

            var world = EditorUtility.CreateGameObjectWithHideFlags("Hub player stats regression", HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(world, preview);
            world.SetActive(false);
            var playerObject = new GameObject("Hub player");
            SceneManager.MoveGameObjectToScene(playerObject, preview);
            playerObject.transform.SetParent(world.transform, false);
            var player = playerObject.AddComponent<PlayerController>();
            var stats = player.GetComponent<PlayerStatManager>();
            stats.Initialize(data);
            binding = playerObject.AddComponent<PlayerUpgradeBinding>();
            var view = world.AddComponent<PlayerStatsPanelView>();
            hub = world.AddComponent<HubWorldController>();
            typeof(HubWorldController).GetField("player", Hidden).SetValue(hub, player);
            // Build the HUD normally created by Awake without changing the global world time scale.
            var hudField = typeof(HubWorldController).GetField("_hud", Hidden);
            hudField.SetValue(hub, Activator.CreateInstance(hudField.FieldType, world.transform, null));
            world.SetActive(true);
            binding.Bind(service, refillHealth: true);

            hubEnabled = true;
            Invoke(hub, "OnEnable");
            Near(stats.GetStat(StatType.ST_ATK), 113f, "Saved basic and tree attack bonuses combine at Hub entry");
            Near(stats.GetStat(StatType.ST_CRT), 15f, "Saved crit chance is converted to percentage points");
            Near(stats.GetStat(StatType.ST_CRTD), 170f, "Saved crit damage is converted to percentage points");
            var frozenRun = service.CreateEngravingSnapshot();
            stats.TakeDamage(25f, ElementType.ELEMENT_NONE, false);

            Check(service.TryUpgrade(StatType.ST_HP, out string error), error);
            Check(service.TryUpgrade(StatType.ST_ATK, out error), error);
            Check(service.TryUpgrade(StatType.ST_DEF, out error), error);
            Near(stats.MaxHp, 210f, "Basic health purchase updates the Hub player");
            Near(stats.GetStat(StatType.ST_ATK), 118f, "Basic attack purchase preserves the tree bonus");
            Near(stats.GetStat(StatType.ST_DEF), 52.5f, "Basic defense purchase updates the Hub player");
            Check(service.TryUnlockNode("hp", out error), error);
            Near(stats.MaxHp, 230f, "Tree health purchase updates the Hub player");
            Near(stats.CurrentHp, 175f, "Buying progression preserves remaining HP");

            view.Render(stats);
            Check(view.GetValueText(StatType.ST_HP) == "230" && view.GetValueText(StatType.ST_ATK) == "118"
                && view.GetValueText(StatType.ST_DEF) == "52.5", "The ability panel shows the combined live values");
            CheckSources(stats, StatType.ST_HP, 10f, 20f);
            CheckSources(stats, StatType.ST_ATK, 10f, 8f);
            Check(!frozenRun.Has("hp"), "The earlier run snapshot is unchanged by a Hub purchase");

            store.RejectWrites = true;
            Check(!service.TryUpgrade(StatType.ST_DEF, out _) && !service.TryUnlockNode("speed", out _),
                "Failed saves must reject both kinds of purchase");
            Near(stats.GetStat(StatType.ST_DEF), 52.5f, "Rejected basic purchase preserves defense");
            Near(stats.GetStat(StatType.ST_ATK_SPD), 1f, "Rejected tree purchase preserves attack speed");
            store.RejectWrites = false;

            Check(service.TryUnlockNode("armor", out error) && service.TryGrantReward("balance-refresh", 1, out error), error);
            Near(stats.GetStat(StatType.ST_DEF), 52.5f, "Damage-reduction engraving does not add to defense");
            Near(stats.MaxHp, 230f, "Repeated account notifications do not stack bonuses");
            Near(stats.CurrentHp, 175f, "Unrelated notifications do not refill HP");
            var balance = world.transform.Find("HubFieldHud/PermanentCurrency/Balance").GetComponent<TMP_Text>();
            Check(balance.text == service.Balance.ToString("N0"), "The Hub balance still refreshes");

            Invoke(hub, "OnDisable");
            hubEnabled = false;
            Check(service.TryUnlockNode("speed", out error), error);
            Near(stats.GetStat(StatType.ST_ATK_SPD), 1f, "A disabled Hub stops listening for changes");
            hubEnabled = true;
            Invoke(hub, "OnEnable");
            Near(stats.GetStat(StatType.ST_ATK_SPD), 1.06f, "Reentering the Hub applies saved attack speed");
            Near(stats.MaxHp, 230f, "Reentering the Hub does not stack health bonuses");
            return "PASS: Hub ability panel reflects basic upgrades and tree bonuses at entry and after purchases.";
        }
        finally
        {
            if (hubEnabled && hub != null) Invoke(hub, "OnDisable");
            if (binding != null) Invoke(binding, "OnDisable");
            Cursor.lockState = cursorLock;
            Cursor.visible = cursorVisible;
            EditorSceneManager.ClosePreviewScene(preview);
            Object.DestroyImmediate(engravings);
            Object.DestroyImmediate(settings);
            Object.DestroyImmediate(data);
        }
    }

    private static void CheckSources(PlayerStatManager stats, StatType stat, float basic, float tree)
    {
        var entry = PlayerStatsReadModel.Read(stats, stat);
        Near(entry.Contributions.Single(source => source.Label == "강화").Value, basic, stat + " basic contribution");
        Near(entry.Contributions.Single(source => source.Label == "각인").Value, tree, stat + " tree contribution");
        Near(entry.Delta, basic + tree, stat + " total increase");
    }

    private static void Invoke(MonoBehaviour target, string name)
        => target.GetType().GetMethod(name, Hidden).Invoke(target, null);

    private static void Near(float actual, float expected, string message)
        => Check(Mathf.Abs(actual - expected) < .001f, $"{message}: {actual} != {expected}");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Hub player stats regression: " + message);
    }

    private sealed class MemoryStore : IPlayerUpgradeStore
    {
        private PlayerUpgradeProfile _profile;
        public bool RejectWrites;

        public MemoryStore(PlayerUpgradeProfile profile) => _profile = profile.Copy();

        public bool TryLoad(out PlayerUpgradeProfile profile, out string error)
        {
            profile = _profile.Copy();
            error = null;
            return true;
        }

        public bool TrySave(PlayerUpgradeProfile profile, out string error)
        {
            error = RejectWrites ? "Simulated save failure." : null;
            if (RejectWrites) return false;
            _profile = profile.Copy();
            return true;
        }
    }
}
