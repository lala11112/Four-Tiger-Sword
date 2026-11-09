using System.Collections.Generic;
using Penta.Upgrade;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed partial class HubWorkshopView
{
    public enum WorkshopPage { Basic, Engravings }

    [SerializeField] private PentaUpgradeView engravingView;
    [SerializeField] private GameObject basicWindow;
    [SerializeField] private UnityEngine.UI.Button[] basicPageButtons, engravingPageButtons;
    [SerializeField] private Sprite normalTab, selectedTab;
    private WorkshopPage _selectedPage;
    private bool _engravingListeners;
    private string _engravingMessage, _engravingMessageNode;

    public WorkshopPage SelectedPage => _selectedPage;
    public PentaUpgradeView EngravingView => engravingView;
    public GameObject BasicWindow => basicWindow;

    public void ConfigureEngravings(PentaUpgradeView tree, GameObject basic,
        UnityEngine.UI.Button[] basicButtons, UnityEngine.UI.Button[] treeButtons, Sprite normal, Sprite selected)
    {
        RemoveEngravingListeners();
        engravingView = tree;
        basicWindow = basic;
        basicPageButtons = basicButtons;
        engravingPageButtons = treeButtons;
        normalTab = normal;
        selectedTab = selected;
        if (engravingView != null) engravingView.SetHosted(true);
        InitializeEngravingListeners();
        ApplySelectedPage();
        RefreshEngraving();
    }

    private void InitializeEngravingListeners()
    {
        if (_engravingListeners || engravingView == null) return;
        _engravingListeners = true;
        engravingView.CloseRequested += RequestClose;
        engravingView.PurchaseRequested += PurchaseEngraving;
        if (basicPageButtons != null) foreach (var button in basicPageButtons)
            if (button != null) button.onClick.AddListener(ShowBasicPage);
        if (engravingPageButtons != null) foreach (var button in engravingPageButtons)
            if (button != null) button.onClick.AddListener(ShowEngravingPage);
    }

    private void RemoveEngravingListeners()
    {
        if (!_engravingListeners) return;
        if (engravingView != null)
        {
            engravingView.CloseRequested -= RequestClose;
            engravingView.PurchaseRequested -= PurchaseEngraving;
        }
        if (basicPageButtons != null) foreach (var button in basicPageButtons)
            if (button != null) button.onClick.RemoveListener(ShowBasicPage);
        if (engravingPageButtons != null) foreach (var button in engravingPageButtons)
            if (button != null) button.onClick.RemoveListener(ShowEngravingPage);
        _engravingListeners = false;
    }

    public void ShowBasicPage() => SelectPage(WorkshopPage.Basic);
    public void ShowEngravingPage() => SelectPage(WorkshopPage.Engravings);

    public void SelectPage(WorkshopPage page)
    {
        if (_purchasing || (IsOpen && !IsInteractionReady)) return;
        if (page == WorkshopPage.Engravings && engravingView == null) return;
        _selectedPage = page;
        ApplySelectedPage();
        Refresh();
        if (IsOpen && EventSystem.current != null)
        {
            var selected = page == WorkshopPage.Engravings ? engravingView.GetNodeButton(engravingView.SelectedId) : null;
            if (selected != null) EventSystem.current.SetSelectedGameObject(selected.gameObject);
        }
    }

    private void ApplySelectedPage()
    {
        if (engravingView == null) return;
        bool tree = _selectedPage == WorkshopPage.Engravings;
        if (basicWindow != null) basicWindow.SetActive(!tree);
        if (engravingView.gameObject.activeSelf != tree) engravingView.gameObject.SetActive(tree);
        if (tree && IsOpen) engravingView.Open();
        UpdatePageButtons(basicPageButtons, !tree);
        UpdatePageButtons(engravingPageButtons, tree);
    }

    private void UpdatePageButtons(UnityEngine.UI.Button[] buttons, bool selected)
    {
        if (buttons == null) return;
        foreach (var button in buttons)
        {
            if (button == null) continue;
            if (button.image != null && normalTab != null && selectedTab != null)
                button.image.sprite = selected ? selectedTab : normalTab;
            var text = button.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (text != null) text.color = selected ? new Color32(255, 226, 230, 255) : new Color32(200, 213, 226, 255);
        }
    }

    private void RefreshEngraving()
    {
        if (engravingView == null) return;
        var states = new List<PentaUpgradeNodeState>(PentaUpgradeCatalog.Nodes.Count);
        string currency = _service?.Settings?.currencyName ?? "강화석";
        foreach (var concept in PentaUpgradeCatalog.Nodes)
        {
            var state = _service != null ? _service.GetNodePurchaseState(concept.Id) : default;
            bool owned = state.IsUnlocked;
            bool parent = concept.ParentIds.Count == 0 || (_service != null && _service.IsNodeUnlocked(concept.ParentIds[0]));
            bool ready = _service != null && _service.IsReady && state.Definition != null;
            var availability = owned ? PentaUpgradeAvailability.Owned : !parent || !ready ? PentaUpgradeAvailability.Locked
                : state.CanPurchase ? PentaUpgradeAvailability.Available : PentaUpgradeAvailability.Unaffordable;
            states.Add(new PentaUpgradeNodeState
            {
                Id = concept.Id,
                DisplayName = state.Definition?.displayName ?? concept.Name,
                Description = concept.Id == "origin" ? "무료 시작 각인" : state.Definition?.description ?? concept.Description,
                Availability = availability,
                CanPurchase = ready && state.CanPurchase && !_purchasing && IsInteractionReady,
                CostLabel = owned || state.Definition == null ? "—" : $"{currency}  {state.Cost:N0}",
                StatusText = !ready ? _service?.LastError ?? "각인 설정을 불러올 수 없습니다." : ""
            });
        }
        engravingView.SetPurchaseBusy(_purchasing);
        engravingView.ApplyPresentation(states,
            _service != null && _service.IsReady ? $"{currency}  {_service.Balance:N0}" : currency + "  —",
            _engravingMessageNode == engravingView.SelectedId ? _engravingMessage : null);
    }

    private void PurchaseEngraving(string id)
    {
        if (_purchasing || !IsInteractionReady || _selectedPage != WorkshopPage.Engravings || _service == null) return;
        _purchasing = true;
        _engravingMessage = null;
        Refresh();
        try
        {
            bool success = _service.TryUnlockNode(id, out string error);
            _engravingMessageNode = id;
            _engravingMessage = success ? null : error ?? "각인을 저장하지 못했습니다.";
        }
        finally { _purchasing = false; Refresh(); }
    }
}
