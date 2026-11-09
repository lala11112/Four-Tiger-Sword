using ArpgHud.PC;
using BoundaryZeroUI;
using UnityEngine;

/// <summary>Observes committed acquisitions; never infers rewards from balance or inventory snapshots.</summary>
[DisallowMultipleComponent]
public sealed class RunRewardFeedPresenter : MonoBehaviour
{
    [SerializeField] private RunManager run;
    [SerializeField] private PlayerController player;
    private PlayerWallet _wallet;
    private PlayerItemRuntime _items;
    private Canvas _canvas;
    private PentaGoldRewardFeed _feed;
    public PentaGoldRewardFeed Feed => _feed;

    public static RunRewardFeedPresenter Ensure(RunManager run, PlayerController player)
    {
        if (run == null) return null;
        var presenter = run.GetComponent<RunRewardFeedPresenter>();
        if (presenter == null) presenter = run.gameObject.AddComponent<RunRewardFeedPresenter>();
        presenter.Configure(run, player);
        return presenter;
    }

    public void Configure(RunManager owner, PlayerController target)
    {
        Unbind();
        run = owner;
        player = target;
        if (isActiveAndEnabled) Bind();
    }

    private void CreateFeed()
    {
        if (_feed != null) return;
        var prefab = Resources.Load<GameObject>("BoundaryUI/GoldHUD/GoldRewardFeed");
        if (prefab == null) { Debug.LogWarning("[Rewards] BoundaryUI reward feed prefab is missing.", this); return; }
        var root = new GameObject("RunAcquisitionCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(CanvasGroup));
        root.transform.SetParent(transform, false);
        _canvas = root.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 210;
        var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(2560, 1440);
        scaler.matchWidthOrHeight = .5f;
        var group = root.GetComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = false;
        var design = new GameObject("ReferenceFrame", typeof(RectTransform));
        design.transform.SetParent(root.transform, false);
        design.AddComponent<BoundaryDesignViewport>().Configure();
        _feed = Instantiate(prefab, design.transform, false).GetComponent<PentaGoldRewardFeed>();
        foreach (var graphic in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) graphic.raycastTarget = false;
        RefreshVisibility();
    }

    private void OnEnable()
    {
        BoundaryZeroBackdrop.CaptureVisibilityChanged += CaptureChanged;
        Bind();
    }

    private void Bind()
    {
        if (run == null || player == null) return;
        run.StateChanged -= StateChanged;
        run.StateChanged += StateChanged;
        CreateFeed();
        BindInventory();
    }

    private void BindInventory()
    {
        if (player == null) return;
        var wallet = player.GetComponent<PlayerWallet>();
        var items = player.GetComponent<PlayerItemRuntime>();
        if (wallet != _wallet)
        {
            if (_wallet != null) _wallet.GoldGained -= GoldGained;
            _wallet = wallet;
            if (_wallet != null) _wallet.GoldGained += GoldGained;
        }
        if (items != _items)
        {
            UnbindItems();
            _items = items;
            if (_items != null)
            {
                _items.ItemAcquired += ItemAcquired;
                _items.SynergyTierActivated += SynergyTierActivated;
            }
        }
    }

    private void Update()
    {
        // The shop creates the wallet during Start, after this presenter's Awake binding.
        if (_wallet == null || _items == null) BindInventory();
        RefreshVisibility();
    }

    private bool CanDisplay => run != null && run.isActiveAndEnabled
        && (run.State == RunState.Playing || run.State == RunState.Won || run.State == RunState.Lost);
    private void GoldGained(int amount) { if (CanDisplay && _feed != null) _feed.ShowGoldGain(amount); }
    private void ItemAcquired(ItemDefinition item)
    {
        if (CanDisplay && _feed != null && item != null) _feed.ShowItemGain(item.displayName, item.icon);
    }
    private void SynergyTierActivated(ItemSynergyState state)
    {
        if (CanDisplay && _feed != null && state.Definition != null && state.ActiveTier != null)
            _feed.ShowSynergyActivation(state.Definition.displayName, state.ActiveTier.count, state.Definition.icon);
    }
    private void StateChanged(RunState state)
    {
        if (state == RunState.Preparing || state == RunState.Error) _feed?.Clear();
        RefreshVisibility();
    }
    private void CaptureChanged(bool capturing) => RefreshVisibility();
    private void RefreshVisibility()
    {
        if (_canvas != null) _canvas.enabled = CanDisplay && !BoundaryZeroBackdrop.IsCapturing;
    }
    private void Unbind()
    {
        if (run != null) run.StateChanged -= StateChanged;
        if (_wallet != null) _wallet.GoldGained -= GoldGained;
        UnbindItems();
        _wallet = null; _items = null;
    }
    private void UnbindItems()
    {
        if (_items == null) return;
        _items.ItemAcquired -= ItemAcquired;
        _items.SynergyTierActivated -= SynergyTierActivated;
    }
    private void OnDisable()
    {
        Unbind();
        BoundaryZeroBackdrop.CaptureVisibilityChanged -= CaptureChanged;
        _feed?.Clear();
        if (_canvas != null) _canvas.enabled = false;
    }
}
