using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Temporary uGUI adapter; replace this prefab or implement IShopView for the final UI.</summary>
public sealed class ShopView : MonoBehaviour, IShopView, IShopLayoutView, IAnimatedModalView, IShopProgressionView
{
    public const int CurrentTabsLayoutVersion = 2;
    [SerializeField] private GameObject panel;
    [SerializeField] private GameObject interactionPrompt;
    [SerializeField] private TMP_Text balance;
    [SerializeField] private TMP_Text health;
    [SerializeField] private TMP_Text message;
    [SerializeField] private Transform offersRoot;
    [SerializeField] private ShopOfferView offerPrefab;
    [SerializeField] private UnityEngine.UI.Button closeButton;
    [SerializeField] private TabControls tabs;
    [SerializeField] private BoundaryControls boundary;
    [SerializeField, HideInInspector] private int tabsLayoutVersion;
    private readonly List<ShopOfferView> _rows = new();
    private string _selectedSaleId;
    private ShopTab _lastTab;
    private UIModalMotion _motion, _tabMotion;
    private CanvasGroup _interaction;
    private bool _visible, _closing;
    private int _motionVersion;
    public event Action<string> PurchaseRequested;
    public event Action CloseRequested;
    public event Action Disabled;
    public event Action<ShopTab> TabRequested;
    public event Action<string> ItemSelected;
    public event Action<string> OfferSelected;
    public event Action<string> FusionMaterialToggled;
    public event Action<string> SellRequested;
    public event Action FusionRequested;
    public event Action<int> FusionSlotCleared;
    public event Action RefreshRequested;
    public event Action<string> FusionResultSelected;
    public event Action FusionChoiceCancelled;
    private UnityEngine.UI.Button _refreshButton;
    private TMP_Text _refreshLabel;
    private ShopFusionChoicePanel _fusionChoices;

    [Serializable]
    public sealed class TabControls
    {
        public UnityEngine.UI.Button BuyTab, SellTab, FuseTab, SellButton, FuseButton, FirstSlot, SecondSlot;
        public GameObject DetailsPanel, SalePanel, FusionPanel, ResultPanel;
        public TMP_Text EmptyMessage, SaleTitle, SaleDescription, SalePrice, FirstLabel, SecondLabel, Odds, ResultLabel;
        public UnityEngine.UI.Image SaleIcon, FirstIcon, SecondIcon, ResultIcon;
        public UnityEngine.UI.ScrollRect Scroll;
    }

    [Serializable]
    public sealed class BoundaryControls
    {
        public RectTransform Content, StockPanel, OwnedPanel, DetailPanel, FusionPanel, ServicePanel;
        public UnityEngine.UI.ScrollRect StockScroll, OwnedScroll;
        public UnityEngine.UI.GridLayoutGroup StockGrid, OwnedGrid;
        public UnityEngine.UI.Button InventoryToggle, DetailAction, HealingButton;
        public UnityEngine.UI.Button[] StockFilters, OwnedFilters;
        public TMP_Text DetailTitle, DetailMeta, DetailBody, DetailPrice, DetailActionLabel,
            HealingCost, HealingLabel, StockEmpty, OwnedEmpty, OwnedTitle, InventoryToggleLabel;
        public UnityEngine.UI.Image DetailIcon;
        public Sprite TabNormal, TabSelected;
        public BoundaryZeroBackdrop Backdrop;
    }

    private readonly List<ShopOfferView> _stockRows = new(), _ownedRows = new();
    private ShopViewState _state;
    private string _detailId;
    private ElementType _stockFilter, _ownedFilter;
    private bool _buyInventoryExpanded = true;
    private float _expandedStockScroll = 1f, _collapsedStockScroll = 1f;
    private float _buyOwnedScroll = 1f, _sellOwnedScroll = 1f, _fuseOwnedScroll = 1f;
    private static readonly ElementType[] FilterElements = { ElementType.ELEMENT_NONE, ElementType.ELEMENT_FIRE,
        ElementType.ELEMENT_EARTH, ElementType.ELEMENT_GOLD, ElementType.ELEMENT_WATER, ElementType.ELEMENT_WOOD };

    public bool HasBoundaryLayout => boundary != null && boundary.StockScroll != null && boundary.DetailAction != null;
    public bool IsAvailable => isActiveAndEnabled && panel != null && (!_visible || panel.activeInHierarchy);
    public bool IsInteractionReady => _visible && !_closing && panel != null && panel.activeInHierarchy
        && (_motion == null || (!_motion.IsAnimating && _motion.IsShown))
        && (_tabMotion == null || !_tabMotion.IsAnimating);
    public bool BuyInventoryExpanded => _buyInventoryExpanded;
    public void ConfigureBoundary(BoundaryControls controls) => boundary = controls;

    public void Configure(GameObject panelRoot, GameObject prompt, TMP_Text gold, TMP_Text hp,
        TMP_Text feedback, Transform content, ShopOfferView row, UnityEngine.UI.Button close)
    { panel = panelRoot; interactionPrompt = prompt; balance = gold; health = hp; message = feedback; offersRoot = content; offerPrefab = row; closeButton = close; }

    public bool HasCurrentTabsLayout => tabsLayoutVersion == CurrentTabsLayoutVersion && tabs != null
        && tabs.BuyTab != null && tabs.SellTab != null && tabs.FuseTab != null && tabs.SalePrice != null;

    public void ConfigureTabs(TabControls controls)
    {
        tabs = controls;
        tabsLayoutVersion = CurrentTabsLayoutVersion;
    }

    private void Awake()
    {
        closeButton.onClick.RemoveListener(Close);
        closeButton.onClick.AddListener(Close);
        Listen(true);
        ListenBoundary(true);
        HideImmediate();
        interactionPrompt.SetActive(false);
    }
    private void OnDestroy()
    {
        if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        Listen(false);
        ListenBoundary(false);
        boundary?.Backdrop?.Hide();
    }
    private void OnDisable()
    {
        HideImmediate();
        Disabled?.Invoke();
    }
    private void Close() { if (_visible && !_closing) CloseRequested?.Invoke(); }
    private void Buy(string id) { if (IsInteractionReady) PurchaseRequested?.Invoke(id); }
    private void Select(string id) { if (IsInteractionReady) ItemSelected?.Invoke(id); }
    private void BuyTab() { if (IsInteractionReady) TabRequested?.Invoke(ShopTab.Buy); }
    private void SellTab() { if (IsInteractionReady) TabRequested?.Invoke(ShopTab.Sell); }
    private void FuseTab() { if (IsInteractionReady) TabRequested?.Invoke(ShopTab.Fuse); }
    private void Sell()
    {
        if (IsInteractionReady && tabs.SellButton.interactable && !string.IsNullOrEmpty(_selectedSaleId)) SellRequested?.Invoke(_selectedSaleId);
    }
    private void Fuse() { if (IsInteractionReady && tabs.FuseButton.interactable) FusionRequested?.Invoke(); }
    private void ClearFirst() { if (IsInteractionReady) FusionSlotCleared?.Invoke(0); }
    private void ClearSecond() { if (IsInteractionReady) FusionSlotCleared?.Invoke(1); }

    private void Listen(bool add)
    {
        if (tabs == null) return;
        Listener(tabs.BuyTab, BuyTab, add); Listener(tabs.SellTab, SellTab, add); Listener(tabs.FuseTab, FuseTab, add);
        Listener(tabs.SellButton, Sell, add); Listener(tabs.FuseButton, Fuse, add);
        Listener(tabs.FirstSlot, ClearFirst, add); Listener(tabs.SecondSlot, ClearSecond, add);
    }

    private static void Listener(UnityEngine.UI.Button button, UnityEngine.Events.UnityAction action, bool add)
    {
        if (button == null) return;
        button.onClick.RemoveListener(action);
        if (add) button.onClick.AddListener(action);
    }

    public void Render(ShopViewState state)
    {
        if (HasBoundaryLayout) { RenderBoundary(state); return; }
        bool opening = state.Visible && !_visible;
        bool changedTab = _visible && state.Tab != _lastTab;
        SetVisibility(state.Visible);
        interactionPrompt.SetActive(state.ShowInteractionPrompt && !state.Visible);
        if (!state.Visible) return;
        balance.text = "보유 골드  " + state.Gold + " G";
        health.text = $"체력  {Mathf.CeilToInt(state.Hp):N0} / {Mathf.CeilToInt(state.MaxHp):N0}";
        message.text = state.Message ?? string.Empty;
        int count = state.Tab == ShopTab.Buy ? state.Offers.Length : state.OwnedItems.Length;
        for (int i = 0; i < count; i++)
        {
            if (i >= _rows.Count)
            {
                var row = Instantiate(offerPrefab, offersRoot);
                UIButtonMotion.Install(row.gameObject);
                _rows.Add(row);
            }
            _rows[i].gameObject.SetActive(true);
            if (state.Tab == ShopTab.Buy) _rows[i].Render(state.Offers[i], Buy);
            else _rows[i].RenderOwned(state.OwnedItems[i], Select, state.Tab == ShopTab.Sell);
        }
        for (int i = count; i < _rows.Count; i++) _rows[i].gameObject.SetActive(false);
        RenderTabs(state);
        RenderProgression(state);
        if (tabs != null && (opening || state.Tab != _lastTab) && tabs.Scroll != null)
        {
            tabs.Scroll.StopMovement();
            Canvas.ForceUpdateCanvases();
            tabs.Scroll.verticalNormalizedPosition = 1f;
        }
        _lastTab = state.Tab;
        if (opening) PlayShow();
        else if (changedTab) PlayTabChange();
    }

    private void RenderTabs(ShopViewState state)
    {
        if (tabs == null) return;
        Tab(tabs.BuyTab, state.Tab == ShopTab.Buy);
        Tab(tabs.SellTab, state.Tab == ShopTab.Sell);
        Tab(tabs.FuseTab, state.Tab == ShopTab.Fuse);
        tabs.DetailsPanel.SetActive(state.Tab != ShopTab.Buy);
        tabs.SalePanel.SetActive(state.Tab == ShopTab.Sell);
        tabs.FusionPanel.SetActive(state.Tab == ShopTab.Fuse);
        tabs.EmptyMessage.text = state.EmptyMessage ?? string.Empty;
        tabs.EmptyMessage.gameObject.SetActive(state.Tab != ShopTab.Buy && state.OwnedItems.Length == 0
            && !string.IsNullOrEmpty(state.EmptyMessage));
        _selectedSaleId = state.SelectedSale?.Id;
        tabs.SaleTitle.text = state.SelectedSale != null ? ItemLabel(state.SelectedSale) : "판매할 아이템을 선택하세요.";
        tabs.SaleDescription.text = state.SelectedSale?.Description ?? "보유 목록에서 아이템을 선택하면 판매 금액을 확인할 수 있습니다.";
        tabs.SalePrice.text = state.SelectedSale == null ? string.Empty : state.SelectedSale.CanSell
            ? "판매 금액  " + state.SelectedSale.SalePrice + " G" : "판매할 수 없는 아이템입니다.";
        Icon(tabs.SaleIcon, state.SelectedSale?.Icon);
        tabs.SellButton.interactable = state.CanSell;
        Slot(tabs.FirstSlot, tabs.FirstLabel, tabs.FirstIcon, state.FusionFirst, "첫 번째 재료 선택");
        Slot(tabs.SecondSlot, tabs.SecondLabel, tabs.SecondIcon, state.FusionSecond, "두 번째 재료 선택");
        tabs.Odds.text = !string.IsNullOrEmpty(state.EmptyMessage) ? state.EmptyMessage
            : state.FusionOddsText ?? "같은 등급의 아이템 두 개를 선택하세요.";
        tabs.FuseButton.interactable = state.CanFuse;
        tabs.ResultPanel.SetActive(state.FusionResult != null);
        tabs.ResultLabel.text = state.FusionResult != null ? "합성 결과\n" + ItemLabel(state.FusionResult) : string.Empty;
        Icon(tabs.ResultIcon, state.FusionResult?.Icon);
    }

    private static string ItemLabel(ShopOwnedItemViewState item) => $"{item.Name}  ·  {item.Grade}등급";

    private void ListenBoundary(bool add)
    {
        if (boundary == null) return;
        Listener(boundary.InventoryToggle, ToggleBuyInventory, add);
        Listener(boundary.DetailAction, DetailAction, add);
        Listener(boundary.HealingButton, Heal, add);
        BindFilters(boundary.StockFilters, true, add);
        BindFilters(boundary.OwnedFilters, false, add);
    }

    private void BindFilters(UnityEngine.UI.Button[] buttons, bool stock, bool add)
    {
        if (buttons == null) return;
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null) continue;
            buttons[i].onClick.RemoveAllListeners();
            if (!add) continue;
            int index = i;
            buttons[i].onClick.AddListener(() =>
            {
                if (!IsInteractionReady || _state == null || !_state.Visible || index >= FilterElements.Length) return;
                if (stock)
                { _stockFilter = FilterElements[index]; _expandedStockScroll = _collapsedStockScroll = 1f;
                    boundary.StockScroll.verticalNormalizedPosition = 1f; }
                else { _ownedFilter = FilterElements[index]; boundary.OwnedScroll.verticalNormalizedPosition = 1f; }
                RenderBoundary(_state);
            });
        }
    }

    public void ToggleBuyInventory()
    {
        if (!IsInteractionReady || _state == null || !_state.Visible || _state.Tab != ShopTab.Buy || !HasBoundaryLayout) return;
        if (_buyInventoryExpanded) _expandedStockScroll = boundary.StockScroll.verticalNormalizedPosition;
        else _collapsedStockScroll = boundary.StockScroll.verticalNormalizedPosition;
        if (_buyInventoryExpanded) _buyOwnedScroll = boundary.OwnedScroll.verticalNormalizedPosition;
        _buyInventoryExpanded = !_buyInventoryExpanded;
        RenderBoundary(_state);
        Canvas.ForceUpdateCanvases();
        boundary.StockScroll.verticalNormalizedPosition = _buyInventoryExpanded ? _expandedStockScroll : _collapsedStockScroll;
        if (_buyInventoryExpanded) boundary.OwnedScroll.verticalNormalizedPosition = _buyOwnedScroll;
    }

    public bool TryHandleBack()
    {
        if (!IsInteractionReady || _state == null || !_state.Visible || _state.Tab != ShopTab.Buy || !_buyInventoryExpanded) return false;
        ToggleBuyInventory();
        return true;
    }

    private void DetailAction()
    {
        if (!IsInteractionReady || _state == null || !_state.Visible || !boundary.DetailAction.interactable) return;
        if (_state.Tab == ShopTab.Buy)
        {
            if (_state.DetailSource == ShopDetailSource.Owned)
            { if (_state.SelectedOffer != null) OfferSelected?.Invoke(_state.SelectedOffer.Id); }
            else if (_state.SelectedOffer != null && _state.SelectedOffer.CanBuy)
                PurchaseRequested?.Invoke(_state.SelectedOffer.Id);
        }
        else if (_state.Tab == ShopTab.Sell && _state.CanSell && _state.SelectedOwned != null)
            SellRequested?.Invoke(_state.SelectedOwned.Id);
        else if (_state.Tab == ShopTab.Fuse && _state.SelectedOwned != null && _state.SelectedOwned.CanAddMaterial)
            FusionMaterialToggled?.Invoke(_state.SelectedOwned.Id);
    }

    private void Heal()
    {
        if (!IsInteractionReady || _state == null || !_state.Visible || !boundary.HealingButton.interactable) return;
        foreach (var offer in _state.Offers)
            if (offer.IsHealing && offer.CanBuy) { PurchaseRequested?.Invoke(offer.Id); return; }
    }

    private void RenderBoundary(ShopViewState state)
    {
        bool opening = state.Visible && !_visible;
        if (_state != null && _state.Tab != state.Tab)
        {
            SaveOwnedScroll(_state.Tab);
            boundary.OwnedScroll.StopMovement();
        }
        bool changedTab = _state == null || _state.Tab != state.Tab;
        _state = state;
        if (state.UsesSynergyCatalog) _stockFilter = _ownedFilter = ElementType.ELEMENT_NONE;
        ShowCatalogFilters(boundary.StockFilters, state.UsesSynergyCatalog);
        ShowCatalogFilters(boundary.OwnedFilters, state.UsesSynergyCatalog);
        SetVisibility(state.Visible);
        interactionPrompt.SetActive(state.ShowInteractionPrompt && !state.Visible);
        if (!state.Visible) return;
        balance.text = "금화  " + state.Gold.ToString("N0") + " G";
        health.text = $"체력  {Mathf.CeilToInt(state.Hp):N0} / {Mathf.CeilToInt(state.MaxHp):N0}";
        message.text = state.Message ?? string.Empty;
        LayoutBoundary(state.Tab);
        TabArtwork(tabs.BuyTab, state.Tab == ShopTab.Buy);
        TabArtwork(tabs.SellTab, state.Tab == ShopTab.Sell);
        TabArtwork(tabs.FuseTab, state.Tab == ShopTab.Fuse);
        FilterArtwork(boundary.StockFilters, _stockFilter);
        FilterArtwork(boundary.OwnedFilters, _ownedFilter);
        int stockCount = 0, ownedCount = 0;
        foreach (var offer in state.Offers)
        {
            if (offer.IsHealing) continue;
            if (_stockFilter != ElementType.ELEMENT_NONE && offer.Element != _stockFilter) continue;
            if (stockCount == _stockRows.Count)
            {
                var created = Instantiate(offerPrefab, boundary.StockScroll.content);
                UIButtonMotion.Install(created.gameObject);
                _stockRows.Add(created);
            }
            var row = _stockRows[stockCount++]; row.gameObject.SetActive(true);
            row.Render(offer, id => { if (IsInteractionReady) OfferSelected?.Invoke(id); });
        }
        foreach (var owned in state.OwnedItems)
        {
            if (_ownedFilter != ElementType.ELEMENT_NONE && owned.Element != _ownedFilter) continue;
            if (ownedCount == _ownedRows.Count)
            {
                var created = Instantiate(offerPrefab, boundary.OwnedScroll.content);
                UIButtonMotion.Install(created.gameObject);
                _ownedRows.Add(created);
            }
            var row = _ownedRows[ownedCount++]; row.gameObject.SetActive(true);
            row.RenderOwned(owned, Select, state.Tab == ShopTab.Sell);
        }
        for (int i = stockCount; i < _stockRows.Count; i++) _stockRows[i].gameObject.SetActive(false);
        for (int i = ownedCount; i < _ownedRows.Count; i++) _ownedRows[i].gameObject.SetActive(false);
        boundary.StockEmpty.gameObject.SetActive(stockCount == 0);
        boundary.OwnedEmpty.gameObject.SetActive(ownedCount == 0);
        boundary.StockEmpty.text = "표시할 상품이 없습니다.";
        boundary.OwnedEmpty.text = state.OwnedItems.Length == 0 ? "보유한 아이템이 없습니다." : "선택한 속성의 아이템이 없습니다.";
        boundary.OwnedTitle.text = "소지품  ·  " + state.OwnedItems.Length + "개";
        RenderBoundaryDetail(state);
        RenderHealing(state);
        Slot(tabs.FirstSlot, tabs.FirstLabel, tabs.FirstIcon, state.FusionFirst, "첫 번째 재료");
        Slot(tabs.SecondSlot, tabs.SecondLabel, tabs.SecondIcon, state.FusionSecond, "두 번째 재료");
        tabs.Odds.text = state.FusionOddsText ?? string.Empty;
        tabs.FuseButton.interactable = state.CanFuse;
        tabs.ResultPanel.SetActive(state.FusionResult != null);
        tabs.ResultLabel.text = state.FusionResult != null ? "합성 결과\n" + ItemLabel(state.FusionResult) : string.Empty;
        Icon(tabs.ResultIcon, state.FusionResult?.Icon);
        if (changedTab)
        {
            Canvas.ForceUpdateCanvases();
            boundary.OwnedScroll.verticalNormalizedPosition = OwnedScroll(state.Tab);
        }
        RenderProgression(state);
        if (opening)
        {
            boundary.Backdrop?.Show();
            PlayShow();
        }
        else if (changedTab) PlayTabChange();
        _lastTab = state.Tab;
    }

    private void EnsureMotion()
    {
        if (panel == null || _motion != null) return;
        _interaction = panel.GetComponent<CanvasGroup>();
        if (_interaction == null) _interaction = panel.AddComponent<CanvasGroup>();
        RectTransform visual = null;
        if (HasBoundaryLayout && boundary.Content != null)
            visual = boundary.Content.parent as RectTransform;
        if (visual == null) visual = panel.transform.Find("Window") as RectTransform;
        if (visual == null) visual = panel.transform as RectTransform;
        _motion = UIModalMotion.Ensure(panel, visual);
        UIButtonMotion.Install(panel);
        if (HasBoundaryLayout && boundary.Content != null)
            _tabMotion = UIModalMotion.Ensure(boundary.Content.gameObject, boundary.Content, .14f, 0f, 0f);
    }

    private void SetVisibility(bool visible)
    {
        if (!visible) { HideImmediate(); return; }
        EnsureMotion();
        _visible = true;
        panel.SetActive(true);
        if (_interaction != null)
        {
            _interaction.alpha = 1f;
            _interaction.blocksRaycasts = true;
        }
    }

    private void PlayShow()
    {
        EnsureMotion();
        _closing = false;
        int version = ++_motionVersion;
        if (_tabMotion != null) _tabMotion.ShowImmediate();
        SetInteraction(false);
        _motion.PlayShow(() =>
        {
            if (!_visible || _closing || version != _motionVersion) return;
            SetInteraction(true);
            if (EventSystem.current != null && closeButton != null)
                EventSystem.current.SetSelectedGameObject(closeButton.gameObject);
        });
    }

    private void PlayTabChange()
    {
        if (_closing || _tabMotion == null || (_motion != null && _motion.IsAnimating)) return;
        SetInteraction(false);
        int version = _motionVersion;
        _tabMotion.PlayShow(() =>
        {
            if (_visible && !_closing && version == _motionVersion) SetInteraction(true);
        });
    }

    public void PlayClose(Action completed)
    {
        if (!_visible) { completed?.Invoke(); return; }
        if (_closing) return;
        EnsureMotion();
        _closing = true;
        int version = ++_motionVersion;
        _tabMotion?.Cancel();
        SetInteraction(false);
        ClearSelection();
        _motion.PlayClose(() =>
        {
            if (!_closing || version != _motionVersion) return;
            completed?.Invoke();
        });
    }

    public void HideImmediate()
    {
        _motionVersion++;
        _visible = _closing = false;
        _motion?.HideImmediate();
        _tabMotion?.ShowImmediate();
        SetInteraction(false);
        ClearSelection();
        boundary?.Backdrop?.Hide();
        if (panel != null) panel.SetActive(false);
    }

    private void SetInteraction(bool enabled)
    {
        if (_interaction == null) return;
        _interaction.interactable = enabled;
        _interaction.blocksRaycasts = _visible;
    }

    private void ClearSelection()
    {
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected != null && panel != null && selected.transform.IsChildOf(panel.transform))
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void RenderHealing(ShopViewState state)
    {
        boundary.HealingButton.interactable = false;
        boundary.HealingCost.text = string.Empty;
        foreach (var offer in state.Offers)
            if (offer.IsHealing)
            {
                boundary.HealingCost.text = "회복 비용  " + offer.Price.ToString("N0") + " G";
                boundary.HealingLabel.text = offer.CanBuy ? "체력 회복" : offer.ButtonLabel;
                boundary.HealingButton.interactable = offer.CanBuy;
                break;
            }
    }

    private void RenderProgression(ShopViewState state)
    {
        if (HasBoundaryLayout && _refreshButton == null)
        {
            _refreshButton = Instantiate(boundary.HealingButton, boundary.ServicePanel);
            _refreshButton.name = "RefreshStockButton";
            _refreshButton.onClick.RemoveAllListeners();
            _refreshButton.onClick.AddListener(() => { if (IsInteractionReady) RefreshRequested?.Invoke(); });
            _refreshLabel = _refreshButton.GetComponentInChildren<TMP_Text>(true);
            UIButtonMotion.Install(_refreshButton.gameObject);
        }
        if (_refreshButton != null)
        {
            float width = boundary.ServicePanel.rect.width;
            Place(boundary.HealingButton.transform as RectTransform, 0, 90, (width - 16) / 2, 56);
            Place(_refreshButton.transform as RectTransform, (width + 16) / 2, 90, (width - 16) / 2, 56);
            _refreshButton.interactable = state.CanRefresh;
            _refreshLabel.text = $"상품 새로고침 · {state.RefreshCost:N0} G";
            _refreshLabel.enableAutoSizing = true; _refreshLabel.fontSizeMin = 15; _refreshLabel.fontSizeMax = 21;
        }
        if (_fusionChoices == null && state.FusionChoiceActive && panel != null)
        {
            var host = new GameObject("FusionChoices", typeof(RectTransform), typeof(ShopFusionChoicePanel));
            host.transform.SetParent(panel.transform, false);
            _fusionChoices = host.GetComponent<ShopFusionChoicePanel>();
            _fusionChoices.Configure(message,
                id => { if (IsInteractionReady) FusionResultSelected?.Invoke(id); },
                () => { if (IsInteractionReady) FusionChoiceCancelled?.Invoke(); });
        }
        if (_fusionChoices != null) _fusionChoices.Render(state.FusionChoiceActive, state.FusionCandidates);
    }

    private void RenderBoundaryDetail(ShopViewState state)
    {
        bool offer = state.Tab == ShopTab.Buy && state.DetailSource != ShopDetailSource.Owned;
        var owned = state.SelectedOwned;
        var stock = state.SelectedOffer;
        bool any = offer ? stock != null : owned != null;
        boundary.DetailTitle.text = any ? offer ? stock.Name : owned.Name : "아이템을 선택하세요";
        string synergyName = any ? offer ? stock.PrimarySynergyName : owned.PrimarySynergyName : null;
        boundary.DetailMeta.text = !any ? string.Empty : !string.IsNullOrEmpty(synergyName)
            ? ItemDisplayText.Meta(offer ? stock.Grade : owned.Grade, synergyName)
            : $"{(offer ? stock.Grade : owned.Grade)}등급 · {ShopOfferView.ElementName(offer ? stock.Element : owned.Element)}";
        boundary.DetailBody.text = any ? (offer ? stock.Description : owned.Description) ?? string.Empty : string.Empty;
        string detailId = any ? (offer ? "offer:" + stock.Id : "owned:" + owned.Id) : string.Empty;
        if (_detailId != detailId)
        {
            _detailId = detailId;
            var descriptionScroll = boundary.DetailBody.GetComponentInParent<UnityEngine.UI.ScrollRect>();
            if (descriptionScroll != null)
            { descriptionScroll.StopMovement(); Canvas.ForceUpdateCanvases(); descriptionScroll.verticalNormalizedPosition = 1f; }
        }
        Icon(boundary.DetailIcon, any ? offer ? stock.Icon : owned.Icon : null);
        boundary.DetailPrice.text = !any ? string.Empty : state.Tab == ShopTab.Sell
            ? owned.CanSell ? "판매 가격  " + owned.SalePrice.ToString("N0") + " G" : "판매할 수 없는 아이템입니다."
            : offer ? "구매 가격  " + stock.Price.ToString("N0") + " G" : string.Empty;
        boundary.DetailAction.interactable = false;
        if (state.Tab == ShopTab.Buy)
        {
            boundary.DetailActionLabel.text = offer ? stock?.CanBuy == true ? "구매하기" : stock?.ButtonLabel ?? "구매하기" : "상품 보기";
            boundary.DetailAction.interactable = offer ? stock?.CanBuy == true : state.SelectedOffer != null;
        }
        else if (state.Tab == ShopTab.Sell)
        { boundary.DetailActionLabel.text = "판매하기"; boundary.DetailAction.interactable = state.CanSell; }
        else
        { boundary.DetailActionLabel.text = owned?.IsMaterial == true ? "재료 제외" : "재료 추가";
            boundary.DetailAction.interactable = owned?.CanAddMaterial == true; }
    }

    private void LayoutBoundary(ShopTab tab)
    {
        bool buy = tab == ShopTab.Buy, sell = tab == ShopTab.Sell, expanded = _buyInventoryExpanded;
        boundary.StockPanel.gameObject.SetActive(buy);
        boundary.ServicePanel.gameObject.SetActive(buy);
        boundary.OwnedPanel.gameObject.SetActive(!buy || expanded);
        boundary.FusionPanel.gameObject.SetActive(tab == ShopTab.Fuse);
        boundary.InventoryToggle.gameObject.SetActive(buy);
        boundary.InventoryToggleLabel.text = expanded ? "소지품 접기 · Tab" : "소지품 펼치기 · Tab";
        float detailWidth;
        if (buy)
        {
            float width = expanded ? 550 : 1000;
            Place(boundary.StockPanel, 0, 95, width, 540);
            Place(boundary.StockScroll.transform as RectTransform, 0, 115, width, 425);
            Place(boundary.ServicePanel, 0, 658, width, 148);
            Place(boundary.HealingButton.transform as RectTransform, 0, 90, width, 56);
            Place(boundary.OwnedPanel, 1184, 95, 590, 714);
            Place(boundary.OwnedScroll.transform as RectTransform, 0, 118, 590, 596);
            detailWidth = expanded ? 550 : 734;
            Place(boundary.DetailPanel, expanded ? 590 : 1040, 95, detailWidth, 714);
            Grid(boundary.StockGrid, expanded ? 2 : 3, expanded ? 269 : (1000f - 24f) / 3f, 238);
            Grid(boundary.OwnedGrid, 2, 289, 238);
        }
        else if (sell)
        {
            Place(boundary.OwnedPanel, 0, 95, 1100, 714);
            Place(boundary.OwnedScroll.transform as RectTransform, 0, 118, 1100, 596);
            detailWidth = 634; Place(boundary.DetailPanel, 1140, 95, detailWidth, 714);
            Grid(boundary.OwnedGrid, 4, 266, 238);
        }
        else
        {
            Place(boundary.OwnedPanel, 0, 95, 650, 714);
            Place(boundary.OwnedScroll.transform as RectTransform, 0, 118, 650, 596);
            detailWidth = 500; Place(boundary.DetailPanel, 690, 95, detailWidth, 714);
            Place(boundary.FusionPanel, 1230, 95, 544, 714);
            Grid(boundary.OwnedGrid, 2, 319, 248);
        }
        float art = buy ? expanded ? 300 : 360 : sell ? 360 : 340;
        Place(boundary.DetailIcon.rectTransform, (detailWidth - art) * .5f, 10, art, art);
        Place(boundary.DetailTitle.rectTransform, 28, 376, detailWidth - 56, 44);
        Place(boundary.DetailMeta.rectTransform, 28, 420, detailWidth - 56, 32);
        Place(boundary.DetailBody.rectTransform.parent.parent as RectTransform, 28, 486, detailWidth - 56, 98);
        Place(boundary.DetailPrice.rectTransform, 28, 596, detailWidth - 56, 36);
        Place(boundary.DetailAction.transform as RectTransform, (detailWidth - 260) * .5f, 650, 260, 56);
    }

    private static void Grid(UnityEngine.UI.GridLayoutGroup grid, int columns, float width, float height)
    { grid.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = columns;
        grid.cellSize = new Vector2(width, height); grid.spacing = new Vector2(12, 12); }
    private void SaveOwnedScroll(ShopTab tab)
    { float value = boundary.OwnedScroll.verticalNormalizedPosition;
        if (tab == ShopTab.Buy) _buyOwnedScroll = value; else if (tab == ShopTab.Sell) _sellOwnedScroll = value; else _fuseOwnedScroll = value; }
    private float OwnedScroll(ShopTab tab) => tab == ShopTab.Buy ? _buyOwnedScroll : tab == ShopTab.Sell ? _sellOwnedScroll : _fuseOwnedScroll;
    private void TabArtwork(UnityEngine.UI.Button button, bool selected)
    {
        button.interactable = !selected;
        if (button.targetGraphic is UnityEngine.UI.Image image) image.sprite = selected ? boundary.TabSelected : boundary.TabNormal;
    }
    private void FilterArtwork(UnityEngine.UI.Button[] buttons, ElementType element)
    {
        if (buttons == null) return;
        for (int i = 0; i < buttons.Length && i < FilterElements.Length; i++)
            if (buttons[i]?.targetGraphic is UnityEngine.UI.Image image)
                image.sprite = element == FilterElements[i] ? boundary.TabSelected : boundary.TabNormal;
    }
    private static void ShowCatalogFilters(UnityEngine.UI.Button[] buttons, bool synergyCatalog)
    {
        if (buttons == null) return;
        for (int i = 0; i < buttons.Length; i++)
            if (buttons[i] != null) buttons[i].gameObject.SetActive(!synergyCatalog || i == 0);
    }
    private static void Place(RectTransform rect, float x, float y, float width, float height)
    { rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); }
    private static void Tab(UnityEngine.UI.Button button, bool selected)
    {
        button.interactable = !selected;
        if (button.targetGraphic != null)
            button.targetGraphic.color = selected ? new Color(.76f, .58f, .29f) : new Color(.26f, .36f, .39f);
    }
    private static void Slot(UnityEngine.UI.Button button, TMP_Text label, UnityEngine.UI.Image icon,
        ShopOwnedItemViewState item, string empty)
    {
        button.interactable = item != null;
        label.text = item != null ? ItemLabel(item) + "\n선택 해제" : empty;
        Icon(icon, item?.Icon);
    }
    private static void Icon(UnityEngine.UI.Image image, Sprite sprite)
    {
        image.sprite = sprite;
        image.color = sprite != null ? Color.white : Color.clear;
    }
}
