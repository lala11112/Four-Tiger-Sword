using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BoundaryZero.SupplyDepot;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Exercises the authored Hub tutorial with isolated progression loaded before each Hub scene.</summary>
[InitializeOnLoad]
public static class TutorialHubPlayModeRegression
{
    private const string TitlePath = "Assets/_Project/Scenes/TitleScene.unity";
    private const string HubPath = "Assets/_Project/Scenes/Hub Scene.unity";
    private const string ArmedKey = "Tutorial.HubRegression.Armed";
    private const string StatusKey = "Tutorial.HubRegression.Status";
    private const string StartSceneKey = "Tutorial.HubRegression.StartScene";
    private const string SlotRootKey = "Tutorial.HubRegression.SlotRoot";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static IDisposable _accountScope;
    private static IDisposable _slotScope;

    static TutorialHubPlayModeRegression() => EditorApplication.playModeStateChanged += OnModeChanged;

    [MenuItem("Tools/Studio Baekho/Tutorial/Run Hub Play Mode Regression")]
    private static void FromMenu() => Debug.Log(Start());

    public static string Start()
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Start the Hub regression in Edit Mode.");
        var title = AssetDatabase.LoadAssetAtPath<SceneAsset>(TitlePath);
        Check(title != null && AssetDatabase.LoadAssetAtPath<SceneAsset>(HubPath) != null,
            "The title and Hub scenes must be installed.");
        SessionState.SetString(StartSceneKey, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) ?? string.Empty);
        SessionState.SetBool(ArmedKey, true);
        Phase("entering title before loading the isolated Hub");
        EditorSceneManager.playModeStartScene = title;
        EditorApplication.EnterPlaymode();
        return Status();
    }

    public static string Status() => SessionState.GetString(StatusKey, "NOT STARTED");

    private static void OnModeChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(ArmedKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode) _ = Verify();
        if (change != PlayModeStateChange.EnteredEditMode) return;

        _accountScope?.Dispose();
        _accountScope = null;
        _slotScope?.Dispose();
        _slotScope = null;
        SaveSlotRegression.DeleteTemporaryRoot(SessionState.GetString(SlotRootKey, string.Empty));
        SessionState.EraseString(SlotRootKey);
        string previous = SessionState.GetString(StartSceneKey, string.Empty);
        EditorSceneManager.playModeStartScene = previous.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
        SessionState.EraseString(StartSceneKey);
        SessionState.EraseBool(ArmedKey);
        if (Status().StartsWith("RUNNING:", StringComparison.Ordinal))
            SessionState.SetString(StatusKey, "FAIL: Hub tutorial regression was interrupted.");
    }

    private static async Task Verify()
    {
        try
        {
            string slotRoot = SaveSlotRegression.CreateTemporaryRoot();
            SessionState.SetString(SlotRootKey, slotRoot);
            var slots = new SaveSlotService(slotRoot, Resources.Load<PlayerUpgradeSettingsSO>("PlayerUpgradeSettings"));
            _slotScope = SaveSlotService.OverrideForTesting(slots);
            Check(SceneManager.GetActiveScene().path == TitlePath, "The title scene did not boot before the Hub.");
            await FreshAccountPurchases();
            await ExistingProgressAcknowledgements();
            await ReplayOverview();
            await SkipWithSaveRetry();
            const string result = "PASS: real Hub workshop and depot use 120 support stones; existing progress, replay, and skip preserve committed records. Memory-only accounts.";
            SessionState.SetString(StatusKey, result);
            Debug.Log(result);
        }
        catch (Exception failure)
        {
            SessionState.SetString(StatusKey, "FAIL during " + Status() + ": " + failure.Message);
            Debug.LogException(failure);
        }
        finally
        {
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }
    }

    private static async Task FreshAccountPurchases()
    {
        Phase("committing the actual 10, 10, and 100 stone purchases");
        var (account, store) = NewAccount();
        var hub = await LoadHub(account);
        Check(account.Balance == 120 && account.TutorialHubStep == 0 && !hub.GateAvailable,
            "Fresh Hub must begin at the upgrade lesson, with its gate held and 120 stones.");

        hub.OpenDepot();
        Check(!hub.Draw.IsInteractable() && !hub.Depot.TryDraw() && account.Balance == 120,
            "The depot accepted an out-of-order draw.");
        hub.DepotInteraction.Close();
        await hub.OpenWorkshop();
        int writes = store.Writes;
        Click(hub.Workshop.Entries.Single(entry => entry.stat == StatType.ST_HP).selectButton, "select maximum HP");
        await Capture("04-hub-workshop");
        Click(hub.Workshop.UI.purchaseButton, "purchase the real HP upgrade");
        Check(account.TutorialHubStep == 1 && account.Balance == 110 && account.GetLevel(StatType.ST_HP) == 1
            && store.Writes == writes + 1 && !hub.Workshop.UI.purchaseButton.IsInteractable(),
            "The first real purchase must save the upgrade and next lesson once.");

        hub.OpenTree();
        var tree = hub.Workshop.EngravingView;
        Click(tree.GetNodeButton("reroll"), "inspect a second-tier engraving");
        Check(!tree.PurchaseButton.IsInteractable(), "The introductory engraving step offered an expensive second-tier node.");
        Click(tree.GetNodeButton("atk"), "select a first-tier engraving");
        await Capture("05-hub-engraving");
        Click(tree.PurchaseButton, "purchase the real first-tier engraving");
        Check(account.TutorialHubStep == 2 && account.Balance == 100 && account.IsNodeUnlocked("atk")
            && store.Writes == writes + 2, "The real engraving purchase did not save the depot lesson together with ownership.");
        await hub.CloseWorkshop();

        hub.OpenDepot();
        await Wait(() => hub.Draw.IsInteractable(), "The final lesson did not enable the supply draw.");
        Check(hub.Depot.catalog.price == 100, "The installed depot price changed from the 100 stones budgeted for the tutorial.");
        await Capture("06-hub-depot");
        SupplyDepotReceipt revealed = null;
        hub.Depot.RewardGranted += receipt => revealed = receipt;
        Click(hub.Draw, "purchase the real random depot reward");
        string transaction = account.CurrentSupplyDepotReceipt?.transactionId;
        Check(hub.Depot.IsDrawing && !string.IsNullOrWhiteSpace(transaction) && account.Balance == 0
            && account.TutorialStatus == TutorialProgressStatus.Completed && account.TutorialHubStep == 3
            && store.Writes == writes + 3, "Draw payment, receipt, and tutorial completion did not save together.");
        hub.Depot.SkipAnimation();
        Check(revealed?.transactionId == transaction && !hub.Depot.TryDraw() && store.Writes == writes + 3,
            "Revealing the item changed its receipt or allowed a second charge.");
        await Capture("07-hub-depot-result");
        hub.DepotInteraction.Close();
        await Wait(() => hub.GateAvailable, "Closing the completed draw did not release the expedition gate.");
        var reloaded = new PlayerUpgradeService(account.Settings, store, account.EngravingSettings);
        Check(reloaded.TutorialStatus == TutorialProgressStatus.Completed && reloaded.Balance == 0
            && reloaded.CurrentSupplyDepotReceipt?.transactionId == transaction,
            "A new account session could not restore the committed receipt and tutorial completion.");
    }

    private static async Task ExistingProgressAcknowledgements()
    {
        Phase("acknowledging previously used Hub features without payment");
        var profile = PlayerUpgradeProfile.New();
        profile.healthLevel = 2;
        profile.unlockedNodeIds = new[] { "hp" };
        profile.supplyDepotItemId = "I01";
        profile.supplyDepotTransactionId = "tutorial-regression-existing-receipt";
        var (account, store) = NewAccount(profile);
        var hub = await LoadHub(account);
        int writes = store.Writes;
        await hub.OpenWorkshop();
        Check(!hub.Workshop.UI.purchaseButton.IsInteractable(), "An existing account was asked to buy another basic upgrade.");
        await hub.Acknowledge();
        hub.OpenTree();
        Click(hub.Workshop.EngravingView.GetNodeButton("heal"), "inspect another eligible engraving");
        Check(!hub.Workshop.EngravingView.PurchaseButton.IsInteractable(), "An existing account was asked to buy another engraving.");
        await hub.Acknowledge();
        await hub.CloseWorkshop();
        hub.OpenDepot();
        Check(!hub.Draw.IsInteractable() && !hub.Depot.TryDraw(), "An existing next-expedition receipt did not prevent a draw.");
        await hub.Acknowledge();
        hub.DepotInteraction.Close();
        await Wait(() => hub.GateAvailable, "Acknowledging existing features did not finish the Hub lessons.");
        Check(account.TutorialStatus == TutorialProgressStatus.Completed && account.Balance == 120
            && account.GetLevel(StatType.ST_HP) == 2 && !account.IsNodeUnlocked("heal") && store.Writes == writes + 3
            && account.CurrentSupplyDepotReceipt?.transactionId == profile.supplyDepotTransactionId,
            "Acknowledgements changed funds, existing upgrades, or the existing paid receipt.");
    }

    private static async Task ReplayOverview()
    {
        Phase("showing the replay overview without writes, then releasing the gate");
        var (account, store) = NewAccount();
        var hub = await LoadHub(account, true);
        int writes = store.Writes;
        await hub.OpenWorkshop();
        Check(!hub.Workshop.UI.purchaseButton.IsInteractable(), "Replay exposed an actual basic upgrade purchase.");
        await hub.Acknowledge();
        hub.OpenTree();
        Click(hub.Workshop.EngravingView.GetNodeButton("atk"), "inspect a possible replay engraving");
        Check(!hub.Workshop.EngravingView.PurchaseButton.IsInteractable(), "Replay exposed an actual engraving purchase.");
        await hub.Acknowledge();
        await hub.CloseWorkshop();
        hub.OpenDepot();
        Check(!hub.Draw.IsInteractable() && !hub.Depot.TryDraw(), "Replay exposed an actual depot draw with sufficient funds.");
        await hub.Acknowledge();
        await Wait(() => hub.GateAvailable && !hub.DepotInteraction.IsOpen, "Replay did not release the gate and close the depot.");
        Check(account.TutorialStatus == TutorialProgressStatus.HubLessons && account.TutorialHubStep == 0
            && account.Balance == 120 && account.CurrentSupplyDepotReceipt == null && store.Writes == writes,
            "The overview wrote or resumed the pending real lessons in the same Hub session.");
    }

    private static async Task SkipWithSaveRetry()
    {
        Phase("retrying a failed Hub skip without undoing an upgrade");
        var (account, store) = NewAccount();
        var hub = await LoadHub(account);
        await hub.OpenWorkshop();
        Click(hub.Workshop.UI.purchaseButton, "buy one upgrade before skipping the Hub");
        int writes = store.Writes;
        store.RejectWrites = true;
        var skip = (Button)typeof(TutorialView).GetField("_skipButton", Hidden).GetValue(hub.Guide);
        await Wait(() => skip != null && skip.isActiveAndEnabled && skip.IsInteractable(), "The Hub skip control is unavailable.");
        Click(skip, "skip the Hub with a rejected save");
        Check(account.TutorialStatus == TutorialProgressStatus.HubLessons && account.TutorialHubStep == 1
            && !hub.GateAvailable && store.Writes == writes, "A failed skip changed progress or released the gate.");
        store.RejectWrites = false;
        await hub.Acknowledge();
        await Wait(() => hub.GateAvailable && !hub.Workshop.IsOpen && !hub.Guide.IsVisible,
            "A successful skip retry did not release the Hub and hide its lesson.");
        Check(account.TutorialStatus == TutorialProgressStatus.Skipped && account.TutorialHubStep == 1
            && account.TutorialGrantClaimed && account.Balance == 110 && account.GetLevel(StatType.ST_HP) == 1
            && store.Writes == writes + 1, "Skipping did not preserve the committed purchase and claimed starter grant.");
    }

    private static (PlayerUpgradeService Account, MemoryStore Store) NewAccount(PlayerUpgradeProfile profile = null)
    {
        var store = new MemoryStore(profile ?? PlayerUpgradeProfile.New());
        var account = new PlayerUpgradeService(Resources.Load<PlayerUpgradeSettingsSO>("PlayerUpgradeSettings"), store);
        Check(account.IsReady && account.TryStartTutorial(out _) && account.TryCompleteTutorialMap(120, out _),
            "Could not seed the isolated account through real tutorial clearance.");
        return (account, store);
    }

    private static async Task<HubFixture> LoadHub(PlayerUpgradeService account, bool overview = false)
    {
        _accountScope?.Dispose();
        _accountScope = PlayerUpgradeService.OverrideForTesting(account);
        TutorialSession.Reset();
        TutorialSession.PendingHubOverview = overview;
        int hubIndex = SceneUtility.GetBuildIndexByScenePath(HubPath);
        Check(hubIndex >= 0, "The Hub must be enabled in the build scenes.");
        var operation = SceneManager.LoadSceneAsync(hubIndex);
        Check(operation != null, "The Hub scene loader did not start.");
        await Wait(() => operation.isDone && SceneManager.GetActiveScene().path == HubPath, "The Hub did not load.", 90f);
        var hub = new HubFixture();
        await Wait(() => hub.Guide != null && hub.Guide.IsVisible, "The authored Hub did not start its tutorial guide.");
        return hub;
    }

    private sealed class HubFixture
    {
        private readonly HubWorldController _world = Object.FindFirstObjectByType<HubWorldController>();
        private readonly HubWorkshopInteraction _workshop = Object.FindFirstObjectByType<HubWorkshopInteraction>();
        public readonly HubSupplyDepotInteraction DepotInteraction = Object.FindFirstObjectByType<HubSupplyDepotInteraction>();
        public readonly SupplyDepotController Depot = Object.FindFirstObjectByType<SupplyDepotController>();
        private readonly HubGateController _gate = Object.FindFirstObjectByType<HubGateController>();
        public TutorialView Guide => _world != null ? _world.GetComponentInChildren<TutorialView>(true) : null;
        public HubWorkshopView Workshop => (HubWorkshopView)typeof(HubWorkshopInteraction).GetField("view", Hidden).GetValue(_workshop);
        public Button Draw => Depot.view.DrawTarget.GetComponent<Button>();
        public bool GateAvailable => (bool)typeof(HubGateController).GetProperty("CanControl", Hidden).GetValue(_gate);

        public HubFixture()
        {
            Check(_world != null && _world.PauseMenuPlayer != null && _workshop != null && Workshop != null
                && DepotInteraction != null && Depot != null && _gate != null, "The Hub is missing an authored interaction host.");
        }

        public async Task OpenWorkshop()
        {
            MoveTo(_workshop.InteractionPosition);
            Check(_workshop.IsInRange && _workshop.TryOpen(), "The player could not open the real workshop station.");
            await Wait(() => Workshop.IsInteractionReady, "Workshop opening animation remained locked.");
        }

        public void OpenTree()
        {
            var tab = Workshop.UI.panel.transform.Find("Window/EngravingTab")?.GetComponent<Button>();
            Click(tab, "open the authored engraving tab");
            Check(Workshop.SelectedPage == HubWorkshopView.WorkshopPage.Engravings, "The engraving tab did not open.");
        }

        public async Task CloseWorkshop()
        {
            Click(Workshop.UI.closeButton, "close the real workshop");
            await Wait(() => !_workshop.IsOpen, "The workshop did not close.");
        }

        public void OpenDepot()
        {
            MoveTo(DepotInteraction.InteractionPosition);
            Check(DepotInteraction.IsInRange && DepotInteraction.TryOpen(), "The player could not open the real supply depot.");
        }

        public async Task Acknowledge()
        {
            var confirm = Guide.ContinueButton;
            await Wait(() => confirm.isActiveAndEnabled && confirm.IsInteractable(), "The tutorial acknowledgement was not interactive.");
            Click(confirm, "acknowledge the active Hub lesson");
            await Awaitable.NextFrameAsync();
        }

        private void MoveTo(Vector3 point)
        {
            var player = _world.PauseMenuPlayer;
            point.y = player.transform.position.y;
            Check(player.TryFastTravel(point, player.transform.rotation), "Could not place the test player at the station entrance.");
        }
    }

    private static void Click(Button button, string action)
    {
        Check(button != null && button.isActiveAndEnabled && button.IsInteractable(), "Could not " + action + ".");
        button.onClick.Invoke();
    }

    private static async Task Wait(Func<bool> ready, string failure, float seconds = 12f)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + seconds;
        while (!ready())
        {
            Check(EditorApplication.isPlaying && Time.realtimeSinceStartupAsDouble < deadline, failure);
            await Awaitable.NextFrameAsync();
        }
    }

    private static async Task Capture(string name)
    {
        await Awaitable.NextFrameAsync();
        Canvas.ForceUpdateCanvases();
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/TutorialValidation"));
        Directory.CreateDirectory(folder);
        ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + ".png"));
        await Awaitable.NextFrameAsync();
        await Awaitable.NextFrameAsync();
    }

    private static void Phase(string phase) => SessionState.SetString(StatusKey, "RUNNING: " + phase);

    private static void Check(bool success, string error)
    {
        if (!success) throw new InvalidOperationException(error);
    }

    private sealed class MemoryStore : IPlayerUpgradeStore
    {
        private PlayerUpgradeProfile _profile;
        public int Writes { get; private set; }
        public bool RejectWrites { get; set; }

        public MemoryStore(PlayerUpgradeProfile profile) => _profile = profile.Copy();

        public bool TryLoad(out PlayerUpgradeProfile profile, out string error)
        {
            profile = _profile.Copy();
            error = null;
            return true;
        }

        public bool TrySave(PlayerUpgradeProfile profile, out string error)
        {
            error = RejectWrites ? "검증용 저장 실패입니다." : null;
            if (RejectWrites) return false;
            _profile = profile.Copy();
            Writes++;
            return true;
        }
    }
}
