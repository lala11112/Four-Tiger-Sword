using System;
using System.Collections.Generic;
using StudioBaekho;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Run-owned composition/presenter. Stock survives layout retries; world objects do not.</summary>
[DisallowMultipleComponent]
public sealed class StageShopService : MonoBehaviour
{
    public EconomySettingsSO settings;
    [Tooltip("Prefab component implementing IShopView.")] public MonoBehaviour shopViewPrefab;
    [Tooltip("Prefab component implementing IGoldView.")] public MonoBehaviour goldViewPrefab;
    public GameObject counterPrefab;
    public ShopStock CurrentStock { get; private set; }
    public Transform ShopPoint { get; private set; }
    public PlayerWallet Wallet { get; private set; }
    public bool IsInRange => _run != null && _run.State == RunState.Playing && _run.stageManager.IsReady
        && ShopPoint != null && _player != null && _player.StatManager.CurrentHp > 0f
        && !_player.IsStageTransitioning && InRange();

    private readonly Dictionary<int, ShopStock> _stageStocks = new();
    private RunManager _run;
    private PlayerController _player;
    private PlayerItemRuntime _items;
    private IShopView _view;
    private IGoldView _goldView;
    private MonoBehaviour _viewInstance, _goldInstance;
    private InputAction _interact, _cancel;
    private bool _initialized, _lastInRange;
    private bool _viewErrorReported;
    private string _message;

    private void OnEnable()
    {
        _interact ??= new InputAction("ShopInteract", InputActionType.Button, "<Keyboard>/f");
        _cancel ??= new InputAction("ShopClose", InputActionType.Button, "<Keyboard>/escape");
        _interact.Enable(); _cancel.Enable();
        if (_viewInstance != null) _viewInstance.gameObject.SetActive(true);
        if (_goldInstance != null) _goldInstance.gameObject.SetActive(true);
        if (_initialized) { _lastInRange = IsInRange; Refresh(); }
    }

    private void Start()
    {
        try { EnsureInitialized(); Refresh(); }
        catch (Exception error) { Debug.LogError("[Shop] " + error.Message, this); }
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        if (settings == null) throw new InvalidOperationException("Shop Economy Settings is missing.");
        if (!settings.Validate(out string error)) throw new InvalidOperationException(error);
        _run = GetComponent<RunManager>();
        _player = _run != null ? _run.player : null;
        _items = _player != null ? _player.GetComponent<PlayerItemRuntime>() : null;
        if (_player == null || _player.StatManager == null || _items == null || _items.Catalog == null)
            throw new InvalidOperationException("Shop requires the run player, stats and item catalog.");
        if (!(shopViewPrefab is IShopView) || !(goldViewPrefab is IGoldView) || counterPrefab == null)
            throw new InvalidOperationException("Assign shop, gold HUD and counter prefabs.");
        Wallet = _player.GetComponent<PlayerWallet>() ?? _player.gameObject.AddComponent<PlayerWallet>();
        Wallet.Initialize(settings.startingGold);
        _viewInstance = Instantiate(shopViewPrefab, transform);
        _goldInstance = Instantiate(goldViewPrefab, transform);
        _view = (IShopView)_viewInstance;
        _goldView = (IGoldView)_goldInstance;
        _view.PurchaseRequested += Purchase;
        _view.CloseRequested += Close;
        Wallet.GoldChanged += GoldChanged;
        _items.OnOwnedItemsChanged += Refresh;
        _player.StatManager.OnHpChanged += HealthChanged;
        _run.StateChanged += RunChanged;
        _run.ShopOpenChanged += ShopChanged;
        _initialized = true;
    }

    public void PrepareStage(int stageSeed, BaekhoDungeonResult result)
    {
        EnsureInitialized();
        SuspendStage();
        if (result == null || !result.success) throw new InvalidOperationException("Shop needs a generated stage.");
        // Bind rewards only after encounter preparation succeeded. Cleanup never emits Enemy.OnDied.
        foreach (var room in result.rooms)
        {
            var encounter = room.GetComponent<DungeonRoomEncounter>();
            if (encounter == null) continue;
            foreach (var member in encounter.Members)
            {
                var reward = member.GetComponent<EnemyGoldReward>() ?? member.gameObject.AddComponent<EnemyGoldReward>();
                reward.Initialize(Wallet, room == result.bossRoom ? settings.bossKillGold : settings.ordinaryKillGold);
            }
        }
        if (result.shopRoom == null) return;
        var anchors = result.shopRoom.GetComponent<StageMapAnchors>();
        if (anchors == null || anchors.shopPoint == null) throw new InvalidOperationException("Generated Shop Point is missing.");
        ShopPoint = anchors.shopPoint;
        Instantiate(counterPrefab, ShopPoint.position, ShopPoint.rotation, ShopPoint);
        if (!_stageStocks.TryGetValue(stageSeed, out var stock))
        {
            stock = ShopStock.Create(_items.Catalog, _items, settings, stageSeed);
            _stageStocks.Add(stageSeed, stock);
        }
        CurrentStock = stock;
        CurrentStock.Changed += Refresh;
        _message = "상품과 회복은 이 상점에서 한 번씩 구매할 수 있습니다.";
        Refresh();
    }

    public void SuspendStage()
    {
        if (CurrentStock != null) CurrentStock.Changed -= Refresh;
        CurrentStock = null;
        ShopPoint = null;
        _lastInRange = false;
        Refresh();
    }

    private bool InRange()
    {
        Vector3 playerFeet = _player.transform.position;
        if (_player.Controller != null)
        {
            var bounds = _player.Controller.bounds;
            playerFeet = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }
        return (playerFeet - ShopPoint.position).sqrMagnitude <= settings.interactionDistance * settings.interactionDistance;
    }

    private void Update()
    {
        if (!_initialized) return;
        bool near = IsInRange;
        if (near != _lastInRange) { _lastInRange = near; Refresh(); }
        if (_run.IsShopOpen)
        {
            if (!near || _cancel.WasPressedThisFrame()) Close();
        }
        else if (near && _interact.WasPressedThisFrame()) TryOpen();
    }

    public bool TryOpen()
    {
        if (!isActiveAndEnabled || !_initialized || CurrentStock == null || !IsInRange) return false;
        _message = "상품과 회복은 이 상점에서 한 번씩 구매할 수 있습니다.";
        return _run.TryOpenShop(this);
    }

    public ShopPurchaseResult TryBuy(string id)
    {
        if (!_initialized || !isActiveAndEnabled || !_run.IsShopOpen || !IsInRange || CurrentStock == null)
            return ShopPurchaseResult.Unavailable;
        var result = CurrentStock.TryPurchase(id, Wallet, _items, _player.StatManager);
        _message = result switch
        {
            ShopPurchaseResult.Success => "구매했습니다.",
            ShopPurchaseResult.InsufficientGold => "골드가 부족합니다.",
            ShopPurchaseResult.AlreadyOwned => "이미 보유한 아이템입니다.",
            ShopPurchaseResult.SoldOut => "이미 구매한 상품입니다.",
            ShopPurchaseResult.FullHealth => "체력이 가득 차 있습니다.",
            _ => "지금은 구매할 수 없습니다."
        };
        Refresh();
        return result;
    }

    private void Purchase(string id) => TryBuy(id);
    private void Close() { if (_run != null) _run.CloseShop(); }
    private void GoldChanged(int gold) => Refresh();
    private void HealthChanged(float hp, float max) => Refresh();
    private void RunChanged(RunState state) => Refresh();
    private void ShopChanged(bool open) => Refresh();

    private void Refresh()
    {
        if (!_initialized) return;
        bool playing = _run.State == RunState.Playing;
        RenderSafely(() => _goldView.Render(Wallet.Gold, playing));
        var state = new ShopViewState
        {
            Visible = _run.IsShopOpen && CurrentStock != null,
            ShowInteractionPrompt = isActiveAndEnabled && IsInRange,
            Gold = Wallet.Gold,
            Hp = _player.StatManager.CurrentHp,
            MaxHp = _player.StatManager.MaxHp,
            Message = _message
        };
        if (CurrentStock != null)
        {
            state.Offers = new ShopOfferViewState[CurrentStock.Offers.Count];
            for (int i = 0; i < state.Offers.Length; i++)
            {
                var offer = CurrentStock.Offers[i];
                bool owned = offer.Item != null && _items.Contains(offer.Item.itemId);
                bool full = offer.IsHealing && state.Hp >= state.MaxHp;
                string label = offer.SoldOut ? "품절" : owned ? "보유 중" : full ? "체력 최대"
                    : Wallet.Gold < offer.Price ? "골드 부족" : "구매";
                state.Offers[i] = new ShopOfferViewState
                {
                    Id = offer.Id, Icon = offer.Item != null ? offer.Item.icon : null,
                    Name = offer.IsHealing ? "체력 회복" : offer.Item.displayName,
                    Description = offer.IsHealing ? $"최대 체력의 {settings.healingFraction * 100:0}%를 즉시 회복합니다."
                        : offer.Item.description,
                    Price = offer.Price, ButtonLabel = label,
                    CanBuy = state.Visible && !offer.SoldOut && !owned && !full && Wallet.Gold >= offer.Price
                };
            }
        }
        RenderSafely(() => _view.Render(state));
    }

    private void RenderSafely(Action render)
    {
        try { render(); }
        catch (Exception error)
        {
            // A replacement view must never interrupt inventory, HP or wallet notifications.
            if (_viewErrorReported) return;
            _viewErrorReported = true;
            Debug.LogWarning("[Shop] View failed to render; gameplay transaction is unaffected. " + error, this);
        }
    }

    private void OnDisable()
    {
        Close();
        _interact?.Disable(); _cancel?.Disable();
        if (_viewInstance != null) _viewInstance.gameObject.SetActive(false);
        if (_goldInstance != null) _goldInstance.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        _interact?.Dispose(); _cancel?.Dispose();
        if (!_initialized) return;
        if (CurrentStock != null) CurrentStock.Changed -= Refresh;
        _view.PurchaseRequested -= Purchase; _view.CloseRequested -= Close;
        if (Wallet != null) Wallet.GoldChanged -= GoldChanged;
        if (_items != null) _items.OnOwnedItemsChanged -= Refresh;
        if (_player != null && _player.StatManager != null) _player.StatManager.OnHpChanged -= HealthChanged;
        if (_run != null) { _run.StateChanged -= RunChanged; _run.ShopOpenChanged -= ShopChanged; }
    }
}
