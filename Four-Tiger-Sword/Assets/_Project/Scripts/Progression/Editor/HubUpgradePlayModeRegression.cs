using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Real Hub/game/result navigation against an isolated temporary save, never the player's account.</summary>
public static class HubUpgradePlayModeRegression
{
    private const string ResultKey = "PlayerUpgrades.HubPlayModeRegression";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool _running;
    private static int _checks;
    private static string _phase;
    private static TaskCompletionSource<bool> _nextTick;

    public static string Start()
    {
        if (!Application.isPlaying || SceneManager.GetActiveScene().name != "Hub Scene")
            throw new InvalidOperationException("Start in the authored Hub Scene while Play Mode is running.");
        if (_running) throw new InvalidOperationException("The Hub acceptance regression is already running.");
        _running = true;
        _checks = 0;
        EditorApplication.update += Tick;
        _ = Run();
        return "Hub acceptance started with a temporary save. Poll HubUpgradePlayModeRegression.Status().";
    }

    public static string Status() => SessionState.GetString(ResultKey, "NOT STARTED");

    private static void Tick()
    {
        var waiting = _nextTick;
        _nextTick = null;
        waiting?.TrySetResult(true);
    }

    private static Task NextTick()
    {
        _nextTick = new TaskCompletionSource<bool>();
        return _nextTick.Task;
    }

    private static async Task Run()
    {
        IDisposable accountOverride = null;
        PlayerUpgradeSettingsSO settings = null;
        string directory = null;
        string outcome = null;
        try
        {
            Phase("isolating the save and checking the Hub");
            await Frames(2);
            var installed = Resources.Load<PlayerUpgradeSettingsSO>("PlayerUpgradeSettings");
            Check(installed != null && installed.Validate(out _), "The installed upgrade settings are missing or invalid.");
            settings = Object.Instantiate(installed);
            settings.hideFlags = HideFlags.DontSave;
            directory = Path.Combine(Path.GetTempPath(), "FourTigerSword-HubRegression-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "upgrades.json");
            var store = new FaultStore(new PlayerUpgradeFileStore(path));
            var service = new PlayerUpgradeService(settings, store);
            accountOverride = PlayerUpgradeService.OverrideForTesting(service);
            Check(service.IsReady && service.Balance == 0, "The isolated account was not initialized empty.");

            var view = Find<HubUpgradeView>();
            var hub = Find<HubSceneController>();
            // The running Hub was created before the override. Rebind its view and controller gate.
            hub.Bind(service);
            view.Open();
            Check(view.IsOpen && view.Cards.Length == 3, "Hub does not expose all three upgrade cards.");
            Check(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length == 1,
                "The Hub must contain exactly one EventSystem.");
            Check(view.GetComponent<Canvas>() != null && view.GetComponent<UnityEngine.UI.GraphicRaycaster>() != null,
                "The Hub Canvas cannot receive UI pointer events.");
            Check(view.Cards.All(card => !card.purchase.IsInteractable()), "An empty account can purchase an upgrade.");

            Phase("buying through the authored card buttons");
            Check(service.TryGrantReward("acceptance:starting-grant", settings.initialCost * 3, out string grantError), grantError);
            foreach (var stat in new[] { StatType.ST_HP, StatType.ST_ATK, StatType.ST_DEF })
            {
                var card = Card(view, stat);
                Check(card.purchase.IsInteractable(), stat + " first upgrade button is disabled.");
                card.purchase.onClick.Invoke();
                Check(service.GetLevel(stat) == 1, stat + " button did not purchase exactly one level.");
            }
            Check(service.Balance == 0 && view.Cards.All(card => !card.purchase.IsInteractable()),
                "Three first upgrades did not consume exactly the starting grant.");
            Card(view, StatType.ST_HP).purchase.onClick.Invoke();
            Check(service.GetLevel(StatType.ST_HP) == 1 && service.Balance == 0,
                "Programmatic repeat of a disabled button bypassed the currency guard.");

            int defenseCost = 0;
            for (int level = 1; level < settings.maxLevel; level++)
                defenseCost += settings.initialCost + level * settings.costPerLevel;
            Check(service.TryGrantReward("acceptance:max-defense", defenseCost, out grantError), grantError);
            for (int level = 1; level < settings.maxLevel; level++) Card(view, StatType.ST_DEF).purchase.onClick.Invoke();
            Check(service.GetLevel(StatType.ST_DEF) == settings.maxLevel && service.Balance == 0,
                "The final defense purchase did not reach the configured cap at the exact cost.");
            Check(!Card(view, StatType.ST_DEF).purchase.IsInteractable()
                && Card(view, StatType.ST_DEF).actionLabel.text.Contains("최대"), "The capped card remains purchasable.");
            Card(view, StatType.ST_DEF).purchase.onClick.Invoke();
            Check(service.GetLevel(StatType.ST_DEF) == settings.maxLevel, "A repeated capped click exceeded the cap.");

            var disk = new PlayerUpgradeService(settings, new PlayerUpgradeFileStore(path));
            Check(disk.IsReady && disk.Balance == 0 && disk.GetLevel(StatType.ST_HP) == 1
                && disk.GetLevel(StatType.ST_ATK) == 1 && disk.GetLevel(StatType.ST_DEF) == settings.maxLevel,
                "A new service did not restore the purchases from the temporary JSON file.");
            view.Close();
            Check(!view.IsOpen, "Close did not hide the reusable upgrade view.");
            view.Open();
            Check(view.IsOpen, "Open did not restore the reusable upgrade view.");

            Phase("starting a real generated run with the saved upgrades");
            Button(hub, "startButton").onClick.Invoke();
            await Wait(() => SceneManager.GetActiveScene().name == "Game Scene", "Hub did not enter Game Scene.", 40);
            await Wait(() => Object.FindFirstObjectByType<RunManager>() != null, "Game Scene has no RunManager.");
            var run = Find<RunManager>();
            await Wait(() => run.State == RunState.Playing || run.State == RunState.Error,
                "The first generated stage did not finish preparing.", 180);
            Check(run.State == RunState.Playing, "Initial stage preparation failed: " + run.LastError);
            var player = run.player;
            Check(player != null && player.GetComponent<PlayerUpgradeBinding>() != null, "The gameplay player has no upgrade binding.");
            Near(player.StatManager.MaxHp, settings.basePlayerStats.baseHP * (1f + settings.healthPerLevel), "Spawn maximum HP");
            Near(player.StatManager.CurrentHp, player.StatManager.MaxHp, "Spawn full HP");
            Near(player.StatManager.GetStat(StatType.ST_ATK), settings.basePlayerStats.baseATK * (1f + settings.attackPerLevel), "Spawn attack");
            Near(player.StatManager.GetStat(StatType.ST_DEF), settings.basePlayerStats.baseDEF * (1f + settings.maxLevel * settings.defensePerLevel), "Spawn defense");
            Check(run.CurrentChapterIndex == 0 && run.CurrentStageIndex == 0, "A new Hub run did not start from the first stage.");

            Phase("confirming a real combat clear and blocked-save recovery");
            var combat = run.stageManager.Encounters.Rooms.FirstOrDefault(room => room.State == DungeonEncounterState.Waiting && room.Members.Count > 0);
            Check(combat != null && combat.BeginEncounter(), "The generated first stage has no activatable combat room.");
            foreach (var member in combat.Members.ToArray())
            {
                Check(member != null && member.Enemy != null && member.IsAwake, "A combat member did not activate.");
                var hit = member.Enemy.TakeDamage(member.GetComponent<EnemyStat>().CurrentHp + 1000000f, poiseDamage: 0f);
                Check(hit.Applied && member.HasDied, "Actual lethal Enemy.TakeDamage did not confirm a death.");
            }
            Check(combat.WasCombatClearedByDefeat && run.ClearedCombatRoomCount == 1,
                "One defeated combat room did not produce exactly one credited room.");
            var wallet = player.GetComponent<PlayerWallet>();
            int goldBeforeClear = wallet != null ? wallet.Gold : 0;
            run.stageManager.Exit.enabled = false;
            store.RejectWrites = true;
            var end = run.stageManager.Result.endRoom.GetComponent<DungeonRoomEncounter>();
            Check(end != null && end.Members.Count == 0, "The first-stage exit room is not the expected empty objective.");
            await Enter(end, player);
            await Wait(() => run.IsUpgradeSaveBlocked, "A failed stage-reward save did not block progression.");
            Check(service.Balance == 0 && run.HasPendingUpgradeRewards && run.StageUpgradeRewardTotal == 0,
                "A failed write changed currency, discarded its receipt, or published a successful reward.");
            Check(player.IsStageTransitioning, "The player remained active while the reward save was blocked.");
            run.ReturnToHub();
            await Frames(2);
            Check(SceneManager.GetActiveScene().name == "Game Scene", "Hub navigation escaped a pending reward save.");
            store.RejectWrites = false;
            var overlay = Find<BoundaryZeroRunOverlay>();
            Check(overlay.ActionButton.IsInteractable(), "The save retry UI is disabled.");
            overlay.ActionButton.onClick.Invoke();
            await Wait(() => !run.IsUpgradeSaveBlocked && run.State == RunState.Playing, "The actual retry button did not resume the saved stage.");
            Check(service.Balance == settings.stageClearReward && run.StageUpgradeRewardTotal == settings.stageClearReward,
                "Retry did not commit the normal-stage reward exactly once.");
            Check(!run.HasPendingUpgradeRewards && (wallet == null || wallet.Gold == goldBeforeClear),
                "Reward recovery retained a pending receipt or changed the separate gold wallet.");
            typeof(StageManager).GetMethod("MarkClear", Hidden).Invoke(run.stageManager, null);
            Check(service.Balance == settings.stageClearReward, "Repeated stage clear paid twice.");

            Phase("playing the death animation and fading into the result screen");
            player.StatManager.TakeDamage(float.MaxValue, ElementType.ELEMENT_NONE, false);
            await Wait(() => run.State == RunState.Lost, "Actual lethal player damage did not terminate the run.");
            int death = Math.Min(settings.deathRewardCap, settings.deathRewardPerRoom);
            int expectedBalance = settings.stageClearReward + death;
            Check(run.DeathUpgradeRewardTotal == death && service.Balance == expectedBalance,
                "Death did not settle the single combat-room reward exactly once.");
            Check(run.IsDeathPresentationPlaying && !overlay.ModalRoot.activeInHierarchy,
                "The result appeared as soon as the player died.");
            await Wait(() => run.resultUI.IsDeathFadePlaying || overlay.ModalRoot.activeInHierarchy,
                "The death animation did not lead to a screen fade.");
            var deathState = player.Animator.GetCurrentAnimatorStateInfo(0);
            Check(run.resultUI.IsDeathFadePlaying && !overlay.ModalRoot.activeInHierarchy
                && deathState.shortNameHash == Animator.StringToHash("Die") && deathState.normalizedTime >= 1f,
                "The screen did not begin fading after the death animation finished and before the result.");
            await Wait(() => overlay.ModalRoot.activeInHierarchy, "The result did not appear after the death fade.");
            var fade = run.resultUI.transform.Find("DeathFadeCanvas")?.GetComponent<CanvasGroup>();
            Check(run.resultUI.DeathFadeCompleted && fade != null && fade.isActiveAndEnabled
                && Mathf.Approximately(fade.alpha, 1f), "The result appeared before the world had faded to black.");
            await Wait(() => overlay.ActionButton.IsInteractable(), "The result button did not finish revealing.");
            Check(run.UpgradeRewardSummary.Contains(expectedBalance.ToString("N0"))
                && overlay.ResultSummaryRoot.GetComponentsInChildren<TMPro.TMP_Text>()
                    .Any(label => label.text == settings.currencyName), "The result does not show the saved currency summary.");
            Check(overlay.ActionButton.GetComponentInChildren<TMPro.TMP_Text>(true).text == "거점으로 돌아가기",
                "The terminal result action is not labeled for Hub return.");
            overlay.ActionButton.onClick.Invoke();
            await Wait(() => SceneManager.GetActiveScene().name == "Hub Scene", "The result button did not return to Hub.", 40);
            await Frames(3);
            Check(Find<HubUpgradeView>().IsOpen && service.Balance == expectedBalance,
                "Hub return lost the open panel or saved rewards.");

            Phase("checking Hub, title, and JSON restoration");
            hub = Find<HubSceneController>();
            Button(hub, "titleButton").onClick.Invoke();
            await Wait(() => SceneManager.GetActiveScene().name == "TitleScene", "Hub did not return to TitleScene.", 40);
            await Frames(2);
            var title = Find<TitleManager>();
            Button(title, "startButton").onClick.Invoke();
            await Wait(() => SceneManager.GetActiveScene().name == "Hub Scene", "The title start button bypassed Hub.", 40);
            await Frames(3);
            disk = new PlayerUpgradeService(settings, new PlayerUpgradeFileStore(path));
            Check(disk.IsReady && disk.Balance == expectedBalance && disk.GetLevel(StatType.ST_HP) == 1
                && disk.GetLevel(StatType.ST_ATK) == 1 && disk.GetLevel(StatType.ST_DEF) == settings.maxLevel,
                "A fresh service after complete navigation did not restore rewards and upgrades.");
            Check(Find<HubUpgradeView>().Cards.All(card => card.level.text.Contains(disk.GetLevel(card.stat).ToString("00"))),
                "The returned Hub shows stale saved levels.");
            outcome = "PASS: " + _checks + " checks; authored purchases/cap, JSON persistence, actual generated spawn/combat/stage rewards, save failure/retry, death animation/fade/result, Hub button, and title roundtrip.";
        }
        catch (Exception failure) { outcome = "FAIL during " + _phase + ": " + failure; }
        finally
        {
            accountOverride?.Dispose();
            // Recreate the live Hub after restoring the original account, removing all temporary subscriptions.
            if (Application.isPlaying)
            {
                try
                {
                    var operation = SceneManager.LoadSceneAsync("Hub Scene");
                    while (operation != null && !operation.isDone && Application.isPlaying) await NextTick();
                    if (Application.isPlaying) await Frames(2);
                }
                catch (Exception cleanup) { outcome += " Cleanup navigation: " + cleanup.Message; }
            }
            if (settings != null) Object.DestroyImmediate(settings);
            if (directory != null)
            {
                try
                {
                    string resolved = Path.GetFullPath(directory);
                    string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (resolved.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
                        && Path.GetFileName(resolved).StartsWith("FourTigerSword-HubRegression-", StringComparison.Ordinal)
                        && Directory.Exists(resolved)) Directory.Delete(resolved, true);
                }
                catch (Exception cleanup) { outcome += " Temporary save cleanup: " + cleanup.Message; }
            }
            SessionState.SetString(ResultKey, outcome ?? "FAIL: acceptance stopped without an outcome.");
            EditorApplication.update -= Tick;
            _nextTick = null;
            _running = false;
        }
    }

    private static async Task Enter(DungeonRoomEncounter room, PlayerController player)
    {
        var gate = room.Gates.FirstOrDefault(candidate => !candidate.IsLocked);
        Check(gate != null, "The exit room has no unlocked entrance for real arrival detection.");
        var outside = gate.transform.TransformPoint(new Vector3(0f, 1.2f, .5f));
        player.BeginStageTransition();
        await Frames(3);
        player.CompleteStageTransition(outside - player.transform.TransformVector(player.Controller.center), gate.transform.rotation);
        await Frames(2);
        bool enabled = player.Controller.enabled;
        player.Controller.enabled = false;
        player.transform.position = gate.transform.TransformPoint(new Vector3(0f, 1.2f, -3f))
            - player.transform.TransformVector(player.Controller.center);
        player.VerticalVelocity = 0f;
        player.Controller.enabled = enabled;
        Physics.SyncTransforms();
        await Frames(2);
    }

    private static HubUpgradeView.Card Card(HubUpgradeView view, StatType stat) => view.Cards.Single(card => card.stat == stat);
    private static T Find<T>() where T : Object
    {
        var found = Object.FindFirstObjectByType<T>();
        Check(found != null, "No active " + typeof(T).Name + " was found.");
        return found;
    }
    private static FieldInfo Field(Type owner, string name) => owner.GetField(name, Hidden)
        ?? throw new InvalidOperationException(owner.Name + "." + name + " field is missing.");
    private static UnityEngine.UI.Button Button(object owner, string name) => (UnityEngine.UI.Button)Field(owner.GetType(), name).GetValue(owner);
    private static void Phase(string phase) { _phase = phase; SessionState.SetString(ResultKey, "RUNNING: " + phase); }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
    private static void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) < .01f,
        message + ": expected " + expected + ", actual " + actual);
    private static async Task Frames(int count)
    {
        int initial = Time.frameCount;
        await Wait(() => Time.frameCount >= initial + count, "The game loop stopped advancing.");
    }
    private static async Task Wait(Func<bool> predicate, string message, int seconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!predicate())
        {
            if (!Application.isPlaying || DateTime.UtcNow >= deadline) throw new InvalidOperationException(message);
            await NextTick();
        }
    }

    private sealed class FaultStore : IPlayerUpgradeStore
    {
        private readonly IPlayerUpgradeStore _inner;
        public bool RejectWrites;
        public FaultStore(IPlayerUpgradeStore inner) => _inner = inner;
        public bool TryLoad(out PlayerUpgradeProfile profile, out string error) => _inner.TryLoad(out profile, out error);
        public bool TrySave(PlayerUpgradeProfile profile, out string error)
        {
            if (RejectWrites) { error = "Acceptance fixture: simulated write failure."; return false; }
            return _inner.TrySave(profile, out error);
        }
    }
}
