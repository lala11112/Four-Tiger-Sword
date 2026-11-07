using System;
using System.Collections.Generic;
using StudioBaekho;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Run-owned composition/presenter. Stock survives layout retries; world objects do not.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)] // Resolve shared F interaction after input (-100), before player combat (0).
public sealed class StageShopService : MonoBehaviour
{
    public EconomySettingsSO settings;
    [Tooltip("Prefab component implementing IShopView.")] public MonoBehaviour shopViewPrefab;
    [Tooltip("Prefab component implementing IGoldView.")] public MonoBehaviour goldViewPrefab;
    public GameObject counterPrefab;
    public ShopStock CurrentStock { get; private set; }
    public Transform ShopPoint { get; private set; }
    public PlayerWallet Wallet { get; private set; }
    public bool IsInRange => _run != null && _run.State == RunState.Playing && _run.stageManager != null && _run.stageManager.IsReady
        && ShopPoint != null && _player != null && _player.StatManager.CurrentHp > 0f
        && !_player.IsStageTransitioning && InRange();

    private readonly Dictionary<int, ShopStock> _stageStocks = new();
    private readonly Dictionary<int, ShopTransactions> _stageTransactions = new();
    private readonly List<DungeonRoomEncounter> _rewardRooms = new();
    private ShopTransactions _transactions;
    private RunManager _run;
    private PlayerController _player;
    private PlayerItemRuntime _items;
    private IShopView _view;
    private IGoldView _goldView;
    private MonoBehaviour _viewInstance, _goldInstance;
    private InputAction _interact, _cancel, _toggleInventory;
    private bool _initialized, _lastInRange;
    private bool _viewErrorReported;
    private bool _transactionInProgress;
    private bool _closing;
    private int _closeVersion;
    private ShopTab _tab;
    private string _saleSelection, _fusionFirst, _fusionSecond;
    private string _offerSelection;
    private ShopDetailSource _detailSource;
    private ItemDefinition _fusionResult;
    private string _message;

    private void OnEnable()
    {
        _interact ??= new InputAction("ShopInteract", InputActionType.Button, "<Keyboard>/f");
        _cancel ??= new InputAction("ShopClose", InputActionType.Button, "<Keyboard>/escape");
        _toggleInventory ??= new InputAction("ShopInventory", InputActionType.Button, "<Keyboard>/tab");
        _interact.Enable(); _cancel.Enable(); _toggleInventory.Enable();
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
        _view.CloseRequested += RequestClose;
        if (_view is IAnimatedModalView animated) animated.Disabled += Close;
        _view.TabRequested += SelectTab;
        _view.ItemSelected += SelectItem;
        _view.OfferSelected += SelectOffer;
        _view.FusionMaterialToggled += ToggleFusionMaterial;
        _view.SellRequested += Sell;
        _view.FusionRequested += FuseSelected;
        _view.FusionSlotCleared += ClearFusionSlot;
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
            if (room != result.bossRoom)
            {
                encounter.MemberSpawned += BindReinforcementReward;
                _rewardRooms.Add(encounter);
            }
            foreach (var member in encounter.Members)
                BindKillReward(member, room == result.bossRoom ? settings.bossKillGold : settings.ordinaryKillGold);
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
        if (!_stageTransactions.TryGetValue(stageSeed, out _transactions))
        {
            _transactions = new ShopTransactions(_items, Wallet, _player.StatManager, settings, stageSeed);
            _stageTransactions.Add(stageSeed, _transactions);
        }
        CurrentStock.Changed += Refresh;
        ResetSelection();
        _tab = ShopTab.Buy;
        _message = TabHint();
        Refresh();
    }

    private void BindReinforcementReward(DungeonEncounterMember member) => BindKillReward(member, settings.ordinaryKillGold);

    private void BindKillReward(DungeonEncounterMember member, int gold)
    {
        if (member == null || member.IsDead) return;
        var reward = member.GetComponent<EnemyGoldReward>() ?? member.gameObject.AddComponent<EnemyGoldReward>();
        if (member.Enemy is Boss boss && boss.Data != null && boss.Data.rewardGroup != null)
            reward.Initialize(Wallet, _items, boss.Data.rewardGroup);
        else reward.Initialize(Wallet, gold);
    }

    private void UnbindRewardRooms()
    {
        foreach (var room in _rewardRooms)
            if (room != null) room.MemberSpawned -= BindReinforcementReward;
        _rewardRooms.Clear();
    }

    public void SuspendStage()
    {
        Close();
        UnbindRewardRooms();
        if (CurrentStock != null) CurrentStock.Changed -= Refresh;
        CurrentStock = null;
        _transactions = null;
        ResetSelection();
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
        // The service owns the pause, so losing only the view does not invalidate RunManager's owner.
        if (_run.IsShopOpen && !ViewIsAvailable) { Close(); return; }
        bool near = IsInRange;
        if (near != _lastInRange) { _lastInRange = near; Refresh(); }
        if (_run.IsShopOpen)
        {
            if (!near) Close();
            else if (_closing) return;
            else if (_cancel.WasPressedThisFrame())
            {
                if (!(_view is IShopLayoutView layout) || !layout.TryHandleBack()) RequestClose();
            }
            else if (_tab == ShopTab.Buy && _toggleInventory.WasPressedThisFrame()
                && !(_view is IAnimatedModalView animated && !animated.IsInteractionReady)
                && _view is IShopLayoutView buyLayout) buyLayout.ToggleBuyInventory();
        }
        else if (near && _interact.WasPressedThisFrame()) TryOpen();
    }

    public bool TryOpen()
    {
        if (!isActiveAndEnabled || !_initialized || _closing || CurrentStock == null || !IsInRange) return false;
        return _run.TryOpenShop(this);
    }

    private bool CanTransact => _initialized && isActiveAndEnabled && !_transactionInProgress && !_closing
        && !(_view is IAnimatedModalView animated && !animated.IsInteractionReady)
        && _run.IsShopOpen && IsInRange && CurrentStock != null && _transactions != null;

    private bool ViewIsAvailable => (!(_view is Behaviour behaviour)
        || (behaviour != null && behaviour.isActiveAndEnabled))
        && (!(_view is IAnimatedModalView animated) || animated.IsAvailable);

    public ShopPurchaseResult TryBuy(string id)
    {
        if (!CanTransact) return ShopPurchaseResult.Unavailable;
        _transactionInProgress = true;
        try
        {
            var result = _transactions.TryBuy(CurrentStock, id);
            _message = result switch
            {
                ShopPurchaseResult.Success => "구매했습니다.",
                ShopPurchaseResult.InsufficientGold => "골드가 부족합니다.",
                ShopPurchaseResult.AlreadyOwned => "이미 보유한 아이템입니다.",
                ShopPurchaseResult.SoldOut => "이미 구매한 상품입니다.",
                ShopPurchaseResult.FullHealth => "체력이 가득 차 있습니다.",
                _ => "지금은 구매할 수 없습니다."
            };
            return result;
        }
        finally { _transactionInProgress = false; Refresh(); }
    }

    public ShopSaleResult TrySell(string itemId)
    {
        if (!CanTransact) return ShopSaleResult.Unavailable;
        _transactionInProgress = true;
        try
        {
            var result = _transactions.TrySell(itemId);
            _message = result switch
            {
                ShopSaleResult.Success => "판매했습니다.",
                ShopSaleResult.NotOwned => "보유하지 않은 아이템입니다.",
                ShopSaleResult.GoldOverflow => "보유 골드 한도를 초과해 판매할 수 없습니다.",
                ShopSaleResult.InvalidItem => "판매할 수 없는 아이템입니다.",
                _ => "지금은 판매할 수 없습니다."
            };
            if (result == ShopSaleResult.Success) _saleSelection = null;
            return result;
        }
        finally { _transactionInProgress = false; Refresh(); }
    }

    public ShopFusionResult TryFuse(string firstId, string secondId)
    {
        if (!CanTransact) return ShopFusionResult.Unavailable;
        _transactionInProgress = true;
        try
        {
            var result = _transactions.TryFuse(firstId, secondId, out var item);
            _message = result switch
            {
                ShopFusionResult.Success => $"{item.displayName} ({item.grade}등급)을 획득했습니다.",
                ShopFusionResult.NotOwned => "선택한 재료를 더 이상 보유하고 있지 않습니다.",
                ShopFusionResult.InvalidMaterials => "같은 등급의 서로 다른 아이템 두 개를 선택하세요.",
                ShopFusionResult.NoCandidates => "획득할 수 있는 합성 결과가 없습니다.",
                _ => "지금은 합성할 수 없습니다."
            };
            if (result == ShopFusionResult.Success)
            {
                _fusionFirst = _fusionSecond = null;
                _fusionResult = item;
            }
            return result;
        }
        finally { _transactionInProgress = false; Refresh(); }
    }

    public void SelectTab(ShopTab tab)
    {
        if (!CanTransact || !Enum.IsDefined(typeof(ShopTab), tab) || _tab == tab) return;
        _tab = tab;
        _fusionFirst = _fusionSecond = null;
        _fusionResult = null;
        _message = TabHint();
        Refresh();
    }

    public void SelectItem(string itemId)
    {
        if (!CanTransact || !_items.Contains(itemId)) return;
        var item = _items.Catalog.Find(itemId);
        if (item == null) return;
        _saleSelection = itemId;
        _detailSource = ShopDetailSource.Owned;
        if (_tab == ShopTab.Sell)
            _message = !_transactions.TryGetSalePrice(itemId, out int price) ? "판매할 수 없는 아이템입니다."
                : price > int.MaxValue - Wallet.Gold ? "보유 골드 한도를 초과해 판매할 수 없습니다."
                : "판매할 아이템과 금액을 확인하세요.";
        Refresh();
    }

    public void SelectOffer(string offerId)
    {
        if (!CanTransact || CurrentStock == null) return;
        foreach (var offer in CurrentStock.Offers)
            if (offer.Id == offerId && !offer.IsHealing)
            {
                _offerSelection = offerId;
                _detailSource = ShopDetailSource.Offer;
                Refresh();
                return;
            }
    }

    /// <summary>Only the explicit detail action mutates materials; inspecting cards is side-effect free.</summary>
    public void ToggleFusionMaterial(string itemId)
    {
        if (!CanTransact || _tab != ShopTab.Fuse || !_items.Contains(itemId)) return;
        if (_fusionFirst == itemId) { ClearFusionSlot(0); return; }
        if (_fusionSecond == itemId) { ClearFusionSlot(1); return; }
        var item = _items.Catalog.Find(itemId);
        var first = _fusionFirst == null ? null : _items.Catalog.Find(_fusionFirst);
        if (item == null || _fusionSecond != null || (first != null
            && (first.grade != item.grade || first.candidateKey == item.candidateKey))) return;
        if (_fusionFirst == null) _fusionFirst = itemId;
        else _fusionSecond = itemId;
        _fusionResult = null;
        _message = TabHint();
        Refresh();
    }

    public void ClearFusionSlot(int slot)
    {
        if (!CanTransact || _tab != ShopTab.Fuse || slot < 0 || slot > 1) return;
        if (slot == 0) { _fusionFirst = _fusionSecond; _fusionSecond = null; }
        else _fusionSecond = null;
        _fusionResult = null;
        _message = TabHint();
        Refresh();
    }

    private void ResetSelection()
    {
        _saleSelection = _fusionFirst = _fusionSecond = null;
        _offerSelection = null;
        _detailSource = ShopDetailSource.None;
        _fusionResult = null;
    }

    private string TabHint() => _tab switch
    {
        ShopTab.Sell => "보유 아이템을 선택해 판매할 수 있습니다.",
        ShopTab.Fuse => "같은 등급 두 개를 소모합니다. 재료 아이템이 다시 나올 수 있습니다.",
        _ => "상품과 회복은 이 상점에서 한 번씩 구매할 수 있습니다."
    };

    private void ShopChanged(bool open)
    {
        _closeVersion++;
        _closing = false;
        if (!open && _view is IAnimatedModalView animated) animated.HideImmediate();
        ResetSelection();
        _tab = ShopTab.Buy;
        _message = TabHint();
        Refresh();
    }

    private void Purchase(string id) => TryBuy(id);
    private void Sell(string id) => TrySell(id);
    private void FuseSelected() => TryFuse(_fusionFirst, _fusionSecond);
    private void RequestClose()
    {
        if (_closing || _run == null || !_run.IsShopOpen) return;
        if (!(_view is IAnimatedModalView animated)) { Close(); return; }
        _closing = true;
        int version = ++_closeVersion;
        try
        {
            animated.PlayClose(() =>
            {
                if (!_closing || version != _closeVersion) return;
                Close();
            });
        }
        catch (Exception error)
        {
            Debug.LogWarning("[Shop] Close motion failed; closing immediately. " + error, this);
            Close();
        }
    }

    // Invalidated sessions must release their resources synchronously, even during a visual exit.
    private void Close()
    {
        _closeVersion++;
        _closing = false;
        if (_view is IAnimatedModalView animated) animated.HideImmediate();
        if (_run != null) _run.CloseShop();
    }
    private void GoldChanged(int gold) => Refresh();
    private void HealthChanged(float hp, float max) => Refresh();
    private void RunChanged(RunState state) => Refresh();

    private void Refresh()
    {
        // Inventory/stat/wallet callbacks arrive during fulfillment; render only the final transaction.
        if (!_initialized || _transactionInProgress) return;
        bool playing = _run.State == RunState.Playing;
        RenderSafely(() => _goldView.Render(Wallet.Gold, playing));
        var state = new ShopViewState
        {
            Visible = _run.IsShopOpen && CurrentStock != null,
            ShowInteractionPrompt = isActiveAndEnabled && IsInRange,
            Gold = Wallet.Gold,
            Hp = _player.StatManager.CurrentHp,
            MaxHp = _player.StatManager.MaxHp,
            Message = _message,
            Tab = _tab
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
                    Grade = offer.Item != null ? offer.Item.grade : 0,
                    Element = offer.Item != null ? offer.Item.element : ElementType.ELEMENT_NONE,
                    IsHealing = offer.IsHealing,
                    Selected = offer.Id == _offerSelection,
                    CanBuy = state.Visible && !offer.SoldOut && !owned && !full && Wallet.Gold >= offer.Price
                };
            }
        }
        foreach (var offer in state.Offers)
            if (offer.Id == _offerSelection) state.SelectedOffer = offer;
        if (state.SelectedOffer == null)
            foreach (var offer in state.Offers)
                if (!offer.IsHealing)
                {
                    _offerSelection = offer.Id; offer.Selected = true; state.SelectedOffer = offer;
                    break;
                }
        PopulateOwnedState(state);
        state.DetailSource = _detailSource == ShopDetailSource.Owned && state.SelectedOwned != null
            ? ShopDetailSource.Owned : state.SelectedOffer != null ? ShopDetailSource.Offer : ShopDetailSource.None;
        RenderSafely(() => _view.Render(state));
    }

    private void PopulateOwnedState(ShopViewState state)
    {
        if (!_items.Contains(_saleSelection)) _saleSelection = null;
        if (!_items.Contains(_fusionFirst)) _fusionFirst = null;
        if (!_items.Contains(_fusionSecond)) _fusionSecond = null;
        if (_fusionFirst == null) { _fusionFirst = _fusionSecond; _fusionSecond = null; }
        var first = _fusionFirst != null ? _items.Catalog.Find(_fusionFirst) : null;
        var owned = new List<ItemDefinition>(_items.OwnedItems);
        owned.RemoveAll(item => item == null);
        owned.Sort((a, b) =>
        {
            int grade = a.grade.CompareTo(b.grade);
            return grade != 0 ? grade : StringComparer.Ordinal.Compare(a.itemId, b.itemId);
        });
        state.OwnedItems = new ShopOwnedItemViewState[owned.Count];
        for (int i = 0; i < owned.Count; i++)
        {
            var item = owned[i];
            var row = OwnedState(item);
            row.Selected = item.itemId == _saleSelection;
            row.IsMaterial = item.itemId == _fusionFirst || item.itemId == _fusionSecond;
            row.CanSelect = state.Visible;
            row.CanAddMaterial = state.Visible && (row.IsMaterial || (_fusionSecond == null
                && (first == null || (first.grade == item.grade && first.candidateKey != item.candidateKey))));
            state.OwnedItems[i] = row;
            if (item.itemId == _saleSelection)
            { state.SelectedOwned = row; if (_tab == ShopTab.Sell) state.SelectedSale = row; }
            if (item.itemId == _fusionFirst) state.FusionFirst = row;
            if (item.itemId == _fusionSecond) state.FusionSecond = row;
        }
        state.CanSell = state.Visible && state.SelectedSale != null && state.SelectedSale.CanSell;
        state.FusionResult = _fusionResult != null ? OwnedState(_fusionResult) : null;
        state.FusionOddsText = "같은 등급 아이템을 두 개 선택하세요.";
        if (_transactions != null && _fusionFirst != null && _fusionSecond != null)
        {
            var preview = _transactions.GetFusionPreview(_fusionFirst, _fusionSecond, out int grade, out float chance);
            state.CanFuse = state.Visible && preview == ShopFusionResult.Success;
            state.FusionOddsText = preview == ShopFusionResult.Success
                ? chance <= 0f ? $"{grade}등급 100% · 골드 소모 없음"
                    : $"{grade}등급 {(1f - chance) * 100f:0.#}% / {grade + 1}등급 {chance * 100f:0.#}%\n골드 소모 없음"
                : "선택한 재료로 합성할 수 없습니다.";
        }
        state.EmptyMessage = owned.Count == 0 ? "보유한 아이템이 없습니다."
            : _tab == ShopTab.Fuse && !HasFusionPair(owned) ? "합성하려면 같은 등급의 아이템 두 개가 필요합니다." : string.Empty;
    }

    private ShopOwnedItemViewState OwnedState(ItemDefinition item)
    {
        int price = 0;
        bool sellable = _transactions != null && _transactions.TryGetSalePrice(item.itemId, out price);
        return new ShopOwnedItemViewState
        {
            Id = item.itemId, Name = item.displayName, Description = item.description,
            Grade = item.grade, Icon = item.icon, SalePrice = price,
            Element = item.element,
            CanSell = sellable && price <= int.MaxValue - Wallet.Gold
        };
    }

    private static bool HasFusionPair(List<ItemDefinition> items)
    {
        for (int i = 1; i < items.Count; i++)
            if (items[i - 1].grade == items[i].grade) return true;
        return false;
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
        _interact?.Disable(); _cancel?.Disable(); _toggleInventory?.Disable();
        if (_viewInstance != null) _viewInstance.gameObject.SetActive(false);
        if (_goldInstance != null) _goldInstance.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        UnbindRewardRooms();
        _interact?.Dispose(); _cancel?.Dispose(); _toggleInventory?.Dispose();
        if (!_initialized) return;
        if (CurrentStock != null) CurrentStock.Changed -= Refresh;
        _view.PurchaseRequested -= Purchase; _view.CloseRequested -= RequestClose;
        if (_view is IAnimatedModalView animated) animated.Disabled -= Close;
        _view.TabRequested -= SelectTab;
        _view.ItemSelected -= SelectItem;
        _view.OfferSelected -= SelectOffer;
        _view.FusionMaterialToggled -= ToggleFusionMaterial;
        _view.SellRequested -= Sell;
        _view.FusionRequested -= FuseSelected;
        _view.FusionSlotCleared -= ClearFusionSlot;
        if (Wallet != null) Wallet.GoldChanged -= GoldChanged;
        if (_items != null) _items.OnOwnedItemsChanged -= Refresh;
        if (_player != null && _player.StatManager != null) _player.StatManager.OnHpChanged -= HealthChanged;
        if (_run != null) { _run.StateChanged -= RunChanged; _run.ShopOpenChanged -= ShopChanged; }
    }
}
