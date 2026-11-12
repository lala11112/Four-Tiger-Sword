using System;
using System.Reflection;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Temporary Play Mode visual fixtures. Every account and inventory is isolated from disk saves.</summary>
public static class EngravingVisualQA
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static HubWorkshopView _view;
    private static HubWorkshopInteraction _interaction;
    private static PlayerController _player;
    private static PlayerUpgradeService _previousService;
    private static HubWorkshopView.WorkshopPage _previousPage;
    private static StatType _previousStat;
    private static string _previousNode;
    private static float _previousZoom, _yaw, _pitch;
    private static Vector2 _previousScroll;
    private static Vector3 _position;
    private static Quaternion _rotation;
    private static CinemachineCamera _camera;
    private static CinemachineOrbitalFollow _orbit;
    private static GameObject _rewardRoot, _rewardInventory;
    private static PlayerStatData _rewardStats;
    private static RunItemChoiceView _rewardView;
    private static RunItemChoiceService _rewardService;
    private static IDisposable _gameAccountScope;

    /// <summary>Reloads the current Game Scene under a memory-only account before the new RunManager.Start.</summary>
    public static string BeginIsolatedGameRun(bool allUnlocked = true, int balance = 5000)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode, then start the isolated Game Scene run.");
        if (_view != null) End();
        _gameAccountScope?.Dispose(); _gameAccountScope = null;
        var settings = Resources.Load<PlayerUpgradeSettingsSO>("PlayerUpgradeSettings");
        var engravings = EngravingSettingsSO.Default;
        var profile = PlayerUpgradeProfile.New(); profile.currency = Math.Max(0, balance);
        profile.unlockedNodeIds = allUnlocked ? engravings.nodes.Where(node => node.id != "origin").Select(node => node.id).ToArray() : Array.Empty<string>();
        var account = new PlayerUpgradeService(settings, new MemoryStore(profile), engravings);
        if (!account.IsReady) throw new InvalidOperationException(account.LastError);
        _gameAccountScope = PlayerUpgradeService.OverrideForTesting(account);
        SceneManager.LoadScene("Game Scene", LoadSceneMode.Single);
        return "LOADING: new Game Scene run with " + profile.unlockedNodeIds.Length + " unlocked nodes in a memory-only account. Poll GameStatus().";
    }

    public static string GameStatus()
    {
        var run = Object.FindFirstObjectByType<RunManager>();
        if (run == null) return "WAIT: no RunManager.";
        var choice = run.ItemChoices?.Current;
        return "Run=" + run.State + ", StageReady=" + (run.stageManager != null && run.stageManager.IsReady)
            + ", RewardOpen=" + run.IsRewardOpen + ", Pending=" + (choice?.Kind.ToString() ?? "none")
            + ", Candidates=" + (choice?.Candidates.Count ?? 0)
            + ", Error=" + (run.LastError ?? "none");
    }

    public static string QueueGameReward(string receipt = "visual-qa-room")
    {
        var run = Object.FindFirstObjectByType<RunManager>();
        if (_gameAccountScope == null || run?.ItemChoices == null)
            throw new InvalidOperationException("Begin the isolated Game Scene fixture and wait for item choices to initialize first.");
        if (run.ItemChoices.Current != null) return "WAIT: resolve the current reward before queueing the next receipt.";
        return run.ItemChoices.BeginRoomReward(receipt) ? "QUEUED: the RunManager will open its real reward modal next frame." : "Receipt was already queued or could not be opened.";
    }

    public static string EndIsolatedGameRun()
    {
        _gameAccountScope?.Dispose(); _gameAccountScope = null;
        return "Shared account override restored. The existing test run retains its isolated cached account until it ends.";
    }

    public static string BeginWorkshop(int balance = 2000)
    {
        if (!Application.isPlaying || SceneManager.GetActiveScene().name != "Hub Scene")
            throw new InvalidOperationException("Start visual QA in the authored Hub Scene during Play Mode.");
        if (_view != null) End();
        var view = Object.FindFirstObjectByType<HubWorkshopView>();
        var interaction = Object.FindFirstObjectByType<HubWorkshopInteraction>();
        var player = Object.FindFirstObjectByType<PlayerController>();
        if (view == null || view.EngravingView == null || interaction == null || player == null || interaction.IsOpen || player.IsGameplayModalPaused)
            throw new InvalidOperationException("The configured Hub workshop, living player and closed modal are required.");
        _view = view; _interaction = interaction; _player = player;
        _previousService = (PlayerUpgradeService)typeof(HubWorkshopView).GetField("_service", Hidden).GetValue(view);
        _previousPage = view.SelectedPage; _previousStat = view.SelectedStat;
        _previousNode = view.EngravingView.SelectedId; _previousZoom = view.EngravingView.Zoom;
        _previousScroll = view.EngravingView.NodeContent.anchoredPosition;
        _position = player.transform.position; _rotation = player.transform.rotation;
        _camera = Object.FindFirstObjectByType<CinemachineCamera>();
        _orbit = _camera != null ? _camera.GetComponent<CinemachineOrbitalFollow>() : null;
        _yaw = _orbit != null ? _orbit.HorizontalAxis.Value : 0;
        _pitch = _orbit != null ? _orbit.VerticalAxis.Value : 0;
        try
        {
            BindAccount(balance);
            Teleport(interaction.InteractionPosition, player.transform.rotation);
            if (!interaction.TryOpen()) throw new InvalidOperationException("The actual workshop interaction refused to open.");
            return "OPENING: isolated workshop account. Poll EngravingVisualQA.Status(), then ShowTree(id).";
        }
        catch { End(); throw; }
    }

    public static string Status() => _view == null ? "CLOSED" : _view.IsInteractionReady ? "READY" : _view.IsOpen ? "OPENING" : "CLOSED";

    public static string BindAccount(int balance, params string[] unlocked)
    {
        if (_view == null) throw new InvalidOperationException("Begin the visual fixture first.");
        var profile = PlayerUpgradeProfile.New(); profile.currency = Math.Max(0, balance);
        profile.unlockedNodeIds = unlocked ?? Array.Empty<string>();
        var settings = _previousService?.Settings ?? Resources.Load<PlayerUpgradeSettingsSO>("PlayerUpgradeSettings");
        var account = new PlayerUpgradeService(settings, new MemoryStore(profile));
        if (!account.IsReady) throw new InvalidOperationException(account.LastError);
        _view.Bind(account);
        return "READY: isolated balance " + account.Balance + ", " + profile.unlockedNodeIds.Length + " supplied node IDs.";
    }

    public static string ShowTree(string id = "atk")
    {
        if (_view == null || !_view.IsInteractionReady) return "WAIT: the workshop entrance animation has not finished.";
        EndReward();
        _view.ShowEngravingPage(); _view.EngravingView.SelectAndFocus(id);
        Canvas.ForceUpdateCanvases();
        return "RENDERING: engraving " + id + " at " + Screen.width + "x" + Screen.height
            + ". Allow a rendered frame, then call HubEngravingRegression.VerifyCurrentPointers().";
    }

    public static string ShowBasic()
    {
        if (_view == null || !_view.IsInteractionReady) return "WAIT: workshop is not ready.";
        EndReward(); _view.ShowBasicPage(); Canvas.ForceUpdateCanvases();
        return "READY: basic workshop at " + Screen.width + "x" + Screen.height + ".";
    }

    /// <summary>Shows the production reward UI over the genuinely paused Hub using an independent 59-item inventory.</summary>
    public static string ShowReward(string scenario = "room")
    {
        if (_view == null || !_view.IsInteractionReady) return "WAIT: first open the workshop fixture to own pause and cursor.";
        EndReward();
        var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/_Project/Generated/BoundaryInventory/ItemCatalog.asset");
        if (catalog == null) throw new InvalidOperationException("The installed item catalog is missing.");
        _rewardInventory = new GameObject("Engraving reward visual inventory"); _rewardInventory.SetActive(false);
        _rewardStats = ScriptableObject.CreateInstance<PlayerStatData>(); _rewardStats.baseHP = 1000;
        var stats = _rewardInventory.AddComponent<PlayerStatManager>(); stats.Initialize(_rewardStats);
        var items = _rewardInventory.AddComponent<PlayerItemRuntime>(); items.Catalog = catalog;
        items.Initialize(stats, () => ElementType.ELEMENT_NONE);
        var wallet = _rewardInventory.AddComponent<PlayerWallet>(); wallet.Initialize(0);
        var nodes = new[] { "choices", "reroll", "hold" };
        _rewardInventory.SetActive(true);
        var snapshot = new EngravingSnapshot(EngravingSettingsSO.Default, nodes);
        _rewardService = new RunItemChoiceService(items, wallet, snapshot, 812763);
        if (scenario == "empty") foreach (var item in catalog.items) items.TryAddItem(item.itemId);
        bool begun = _rewardService.BeginRoomReward("visual-qa");
        if (!begun) throw new InvalidOperationException("Could not create the isolated visual reward receipt.");
        _rewardRoot = new GameObject("Engraving reward visual overlay", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        _rewardRoot.layer = LayerMask.NameToLayer("UI");
        var canvas = _rewardRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 180;
        var scaler = _rewardRoot.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        _rewardView = _rewardRoot.AddComponent<RunItemChoiceView>();
        typeof(RunItemChoiceView).GetMethod("Build", Hidden).Invoke(_rewardView, null);
        _rewardView.Bind(_rewardService); _rewardView.Show(); Canvas.ForceUpdateCanvases();
        return "RENDERING: isolated " + scenario
            + " reward. Allow a rendered frame, then call HubEngravingRegression.VerifyCurrentRewardView().";
    }

    public static string EndReward()
    {
        if (_rewardRoot != null) Object.DestroyImmediate(_rewardRoot);
        if (_rewardInventory != null) Object.DestroyImmediate(_rewardInventory);
        if (_rewardStats != null) Object.DestroyImmediate(_rewardStats);
        _rewardRoot = null; _rewardInventory = null; _rewardStats = null; _rewardView = null; _rewardService = null;
        return "Reward visual fixture removed.";
    }

    public static string End()
    {
        EndReward();
        if (_interaction != null) _interaction.Close();
        if (_view != null)
        {
            _view.Bind(_previousService); _view.Select(_previousStat); _view.SelectPage(_previousPage);
            _view.EngravingView.Select(_previousNode); _view.EngravingView.SetZoom(_previousZoom);
            _view.EngravingView.NodeContent.anchoredPosition = _previousScroll;
        }
        if (_player != null && Application.isPlaying)
        {
            Teleport(_position, _rotation);
            if (_orbit != null) { _orbit.HorizontalAxis.Value = _yaw; _orbit.VerticalAxis.Value = _pitch; }
        }
        _view = null; _interaction = null; _player = null; _previousService = null; _camera = null; _orbit = null;
        return "CLOSED: original account binding and player position restored; disk saves were never accessed.";
    }

    private static void Teleport(Vector3 position, Quaternion rotation)
    {
        Vector3 delta = position - _player.transform.position;
        bool enabled = _player.Controller.enabled; _player.Controller.enabled = false;
        _player.transform.SetPositionAndRotation(position, rotation); _player.VerticalVelocity = 0;
        _player.StateMachine.ChangeState(new PlayerIdleState(_player)); _player.Controller.enabled = enabled;
        Physics.SyncTransforms();
        if (_camera != null) { _camera.OnTargetObjectWarped(_camera.Follow, delta); _camera.PreviousStateIsValid = false; }
    }

    private sealed class MemoryStore : IPlayerUpgradeStore
    {
        private PlayerUpgradeProfile _profile;
        public MemoryStore(PlayerUpgradeProfile profile) => _profile = profile.Copy();
        public bool TryLoad(out PlayerUpgradeProfile profile, out string error) { profile = _profile.Copy(); error = null; return true; }
        public bool TrySave(PlayerUpgradeProfile profile, out string error) { _profile = profile.Copy(); error = null; return true; }
    }
}
