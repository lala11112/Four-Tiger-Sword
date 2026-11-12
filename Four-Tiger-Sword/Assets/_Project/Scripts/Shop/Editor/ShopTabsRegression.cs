using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

/// <summary>Checks the authored tab prefab and isolated presenter state without saving the open scene.</summary>
public static class ShopTabsRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;

    [MenuItem("Tools/Studio Baekho/Shop/Run Tabs Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run shop tab regression in Edit Mode.");
        _checks = 0;
        AuthoredViewWiring();
        SharedItemDetails();
        PresenterSelectionAndTransactions();
        return $"Shop tabs: {_checks} checks passed (authored prefab, events, disabled actions, selection, stale ownership and reset).";
    }

    private static void AuthoredViewWiring()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoundaryZeroShopSetup.PrefabPath);
        Check(prefab != null, "The authored shop prefab is missing.");
        var events = EventSystem.current;
        var previousSelection = events != null ? events.currentSelectedGameObject : null;
        var host = Object.Instantiate(prefab);
        host.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var view = host.GetComponent<ShopView>();
            var tabs = Field<ShopView.TabControls>(view, "tabs");
            var controls = Field<ShopView.BoundaryControls>(view, "boundary");
            Check(view.HasBoundaryLayout && controls.StockScroll.content != controls.OwnedScroll.content
                && controls.StockScroll.viewport != null && controls.OwnedScroll.viewport != null,
                "BoundaryZERO must have separate stock and owned scroll bindings.");
            Check(controls.Backdrop != null && new SerializedObject(controls.Backdrop)
                .FindProperty("modalContent").objectReferenceValue != null,
                "The authored backdrop must retain a valid sharp-content CanvasGroup.");
            // Snapshot capture is verified in Play Mode; this isolated test owns no renderer.
            controls.Backdrop = null;
            Invoke(view, "OnDestroy"); Invoke(view, "Awake");
            string bought = null, inspectedOffer = null, selected = null, material = null, sold = null;
            int sales = 0, fusions = 0, cleared = -1;
            var tabRequests = new List<ShopTab>();
            view.PurchaseRequested += id => bought = id;
            view.OfferSelected += id => inspectedOffer = id;
            view.ItemSelected += id => selected = id;
            view.FusionMaterialToggled += id => material = id;
            view.SellRequested += id => { sold = id; sales++; };
            view.FusionRequested += () => fusions++;
            view.FusionSlotCleared += slot => cleared = slot;
            view.TabRequested += tab => tabRequests.Add(tab);
            var owned = new ShopOwnedItemViewState { Id = "owned", Name = "검증 아이템", Grade = 1,
                Element = ElementType.ELEMENT_WATER, Description = "아이템 설명", CanSelect = true,
                CanSell = true, CanAddMaterial = true, SalePrice = 10 };
            var offer = new ShopOfferViewState { Id = "offer", Name = "상품", Grade = 1,
                Element = ElementType.ELEMENT_FIRE, CanBuy = true };
            var heal = new ShopOfferViewState { Id = "healing", Name = "체력 회복", IsHealing = true,
                CanBuy = true, Price = 20, ButtonLabel = "구매" };
            var state = new ShopViewState { Visible = true, Hp = 100, MaxHp = 100, Tab = ShopTab.Buy,
                Offers = new[] { offer, heal }, OwnedItems = new[] { owned }, SelectedOffer = offer,
                DetailSource = ShopDetailSource.Offer };
            view.Render(state);
            var shopHeader = controls.Content.parent;
            var coin = shopHeader.Find("GoldIcon")?.GetComponent<UnityEngine.UI.Image>();
            Check(coin != null && coin.sprite != null && !shopHeader.Find("Health").gameObject.activeSelf,
                "The shop header should display the coin asset and hide the HP readout.");
            Check(controls.DetailBody.fontSize >= 28 && controls.DetailBody.richText
                && ((RectTransform)controls.DetailBody.transform.parent.parent).sizeDelta.y >= 294
                && controls.DetailIcon.rectTransform.sizeDelta.x < 150,
                "The detail view should reserve readable space for effects and use compact artwork.");
            var stockRows = Field<List<ShopOfferView>>(view, "_stockRows");
            var ownedRows = Field<List<ShopOfferView>>(view, "_ownedRows");
            Canvas.ForceUpdateCanvases();
            Check(HasVisibleGlyph(Field<TMP_Text>(stockRows[0], "title"))
                && HasVisibleGlyph(Field<TMP_Text>(stockRows[0], "gradeLabel"))
                && HasVisibleGlyph(controls.DetailMeta),
                "Card names, grades and detail metadata must fit the Korean font's line metrics.");
            Invoke(stockRows[0], "Awake"); Invoke(ownedRows[0], "Awake");
            Field<UnityEngine.UI.Button>(stockRows[0], "purchaseButton").onClick.Invoke();
            Check(inspectedOffer == "offer" && bought == null && !tabs.BuyTab.interactable,
                "A stock card must inspect without purchasing.");
            controls.DetailAction.onClick.Invoke();
            Check(bought == "offer", "The explicit detail purchase action was not wired.");
            bought = null; controls.HealingButton.onClick.Invoke();
            Check(bought == "healing" && stockRows.Count == 1, "Healing must be separate from item cards.");
            Check(view.TryHandleBack() && !view.BuyInventoryExpanded && !view.TryHandleBack(),
                "Escape must collapse Buy inventory once before closing.");
            view.ToggleBuyInventory();
            controls.StockFilters[1].onClick.Invoke(); controls.OwnedFilters[4].onClick.Invoke();
            Check(view.BuyInventoryExpanded && Field<ElementType>(view, "_stockFilter") == ElementType.ELEMENT_FIRE
                && Field<ElementType>(view, "_ownedFilter") == ElementType.ELEMENT_WATER,
                "Stock and owned filters must remain independent.");
            state.DetailSource = ShopDetailSource.Owned; state.SelectedOwned = owned; view.Render(state);
            inspectedOffer = null; controls.DetailAction.onClick.Invoke();
            Check(inspectedOffer == "offer", "Owned detail must return to the remembered stock inspection.");
            tabs.SellTab.onClick.Invoke(); state.Tab = ShopTab.Sell; state.SelectedSale = owned; state.CanSell = true;
            view.Render(state); Field<UnityEngine.UI.Button>(ownedRows[0], "purchaseButton").onClick.Invoke();
            controls.DetailAction.onClick.Invoke();
            Check(selected == owned.Id && sold == owned.Id && sales == 1
                && controls.OwnedGrid.constraintCount == 4 && controls.DetailPrice.text.Contains("10"),
                "Sale inspection, four-column layout or explicit sale action is incorrect.");
            owned.CanSelect = false; state.CanSell = false; selected = null; view.Render(state);
            Field<UnityEngine.UI.Button>(ownedRows[0], "purchaseButton").onClick.Invoke(); controls.DetailAction.onClick.Invoke();
            Check(selected == null && sales == 1, "A disabled row or sale action emitted a request.");
            tabs.FuseTab.onClick.Invoke(); state.Tab = ShopTab.Fuse; owned.CanSelect = true;
            state.FusionFirst = owned; state.FusionOddsText = "1등급 100%"; view.Render(state);
            selected = material = null; Field<UnityEngine.UI.Button>(ownedRows[0], "purchaseButton").onClick.Invoke();
            Check(selected == owned.Id && material == null, "Inspecting a fusion card must not mutate materials.");
            controls.DetailAction.onClick.Invoke();
            Check(material == owned.Id, "The explicit material toggle was not wired.");
            tabs.FuseButton.onClick.Invoke(); tabs.FirstSlot.onClick.Invoke();
            Check(fusions == 0 && cleared == 0 && tabs.FirstSlot.interactable && !tabs.SecondSlot.interactable
                && controls.FusionPanel.gameObject.activeSelf && tabs.Odds.text == state.FusionOddsText,
                "Fusion slots, preview or disabled action is incorrect.");
            state.CanFuse = true; state.FusionResult = owned; view.Render(state); tabs.FuseButton.onClick.Invoke();
            Check(fusions == 1 && tabs.ResultPanel.activeSelf && tabs.ResultLabel.text.Contains(owned.Name),
                "Fusion action or result rendering is not wired.");
            tabs.BuyTab.onClick.Invoke();
            Check(tabRequests.Count == 3 && tabRequests[0] == ShopTab.Sell && tabRequests[1] == ShopTab.Fuse
                && tabRequests[2] == ShopTab.Buy, "Tab controls emitted incorrect requests.");
            state.Visible = false; view.Render(state); state.Visible = true; state.Tab = ShopTab.Buy; view.Render(state);
            Check(stockRows.Count == 1 && ownedRows.Count == 1, "Repeated render or reopen duplicated item cards.");
            Check(Field<UnityEngine.UI.Image>(stockRows[0], "icon").sprite == offer.Icon
                && Field<UnityEngine.UI.Image>(ownedRows[0], "icon").sprite == owned.Icon,
                "The card view replaced an authoritative item icon.");
            var manyOffers = new ShopOfferViewState[12];
            var manyOwned = new ShopOwnedItemViewState[12];
            for (int i = 0; i < 12; i++)
            {
                manyOffers[i] = new ShopOfferViewState { Id = "offer" + i, Name = "상품", Grade = 1,
                    Element = ElementType.ELEMENT_FIRE, CanBuy = true };
                manyOwned[i] = new ShopOwnedItemViewState { Id = "owned" + i, Name = "소지품", Grade = 1,
                    Element = ElementType.ELEMENT_WATER, CanSelect = true };
            }
            var largeBuy = new ShopViewState { Visible = true, Tab = ShopTab.Buy, Offers = manyOffers,
                OwnedItems = manyOwned, SelectedOffer = manyOffers[0], DetailSource = ShopDetailSource.Offer };
            view.Render(largeBuy); Canvas.ForceUpdateCanvases();
            controls.StockScroll.verticalNormalizedPosition = .37f;
            controls.OwnedScroll.verticalNormalizedPosition = .43f;
            view.ToggleBuyInventory();
            view.Render(new ShopViewState { Visible = true, Tab = ShopTab.Sell, Offers = manyOffers, OwnedItems = manyOwned });
            controls.OwnedScroll.verticalNormalizedPosition = .9f;
            view.Render(largeBuy); view.ToggleBuyInventory(); Canvas.ForceUpdateCanvases();
            Check(Mathf.Abs(controls.StockScroll.verticalNormalizedPosition - .37f) < .02f
                && Mathf.Abs(controls.OwnedScroll.verticalNormalizedPosition - .43f) < .02f,
                "Folding inventory across another tab lost independent stock/owned scroll positions.");
            var smallBuy = new ShopViewState { Visible = true, Tab = ShopTab.Buy,
                Offers = new[] { manyOffers[0], manyOffers[1], manyOffers[2] }, OwnedItems = manyOwned,
                SelectedOffer = manyOffers[0], DetailSource = ShopDetailSource.Offer };
            view.Render(smallBuy); Canvas.ForceUpdateCanvases();
            controls.StockScroll.verticalNormalizedPosition = .23f;
            view.ToggleBuyInventory(); view.ToggleBuyInventory(); Canvas.ForceUpdateCanvases();
            Check(Mathf.Abs(controls.StockScroll.verticalNormalizedPosition - .23f) < .02f,
                "A non-scrollable collapsed stock list erased the expanded list's scroll position.");
            int closed = 0;
            bought = null;
            view.PlayClose(() => closed++);
            view.PlayClose(() => closed++);
            controls.DetailAction.onClick.Invoke();
            view.Render(smallBuy);
            Check(closed == 1 && !view.IsInteractionReady && bought == null,
                "Repeated close/render must not reopen a closing shop or accept a stale purchase.");
            view.HideImmediate();
            view.Render(smallBuy);
            Check(view.IsInteractionReady, "A forced hide prevented a fresh shop entrance.");
            int disabled = 0;
            view.Disabled += () => disabled++;
            host.SetActive(false);
            // Non-ExecuteAlways views do not receive lifecycle callbacks in this Edit Mode fixture.
            Invoke(view, "OnDisable");
            Check(disabled == 1 && !view.IsAvailable && !view.IsInteractionReady,
                "Disabling the view must notify its pause owner and cancel presentation input.");
            host.SetActive(true);
            view.Render(smallBuy);
            Check(view.IsAvailable && view.IsInteractionReady,
                "Re-enabling the view prevented a fresh shop entrance.");
        }
        finally
        {
            if (events != null) events.SetSelectedGameObject(previousSelection);
            Object.DestroyImmediate(host);
        }
    }

    private static void SharedItemDetails()
    {
        var item = ScriptableObject.CreateInstance<ItemDefinition>();
        var catalog = ScriptableObject.CreateInstance<ItemCatalog>();
        try
        {
            item.itemId = "details";
            item.description = "외형 문구";
            item.effectText = "평타 공격속도 +10%";
            catalog.items = new[] { item };
            catalog.synergies = new[] { new ItemSynergyDefinition { id = "set", displayName = "신속",
                members = new[] { item.itemId }, tiers = new[] { new ItemSynergyTier { count = 2, text = "평타 피해 +4%" } } } };
            string description = ItemDisplayText.Description(item, catalog);
            Check(description.Contains("개별 효과</b></color>\n기본 공격 속도 +10%")
                && description.Contains("\n\n<link=\"item-synergy-divider\">") && description.Contains("기본 공격 피해 +4%")
                && !description.Contains(item.description), "Effect text must omit appearance, separate synergies and use 기본 공격.");
            var inventory = ItemDisplayText.ToInventoryData(catalog);
            Check(inventory.items[0].effect == "기본 공격 속도 +10%"
                && inventory.synergies[0].tiers[0].text == "기본 공격 피해 +4%", "Inventory kept a different combat term.");
        }
        finally { Object.DestroyImmediate(item); Object.DestroyImmediate(catalog); }
    }

    private static void PresenterSelectionAndTransactions()
    {
        using var f = new Fixture();
        f.Service.SelectTab(ShopTab.Sell); f.Service.SelectItem("a");
        Check(f.View.Last.SelectedSale?.Id == "a" && f.View.Last.CanSell && f.View.Last.SelectedSale.SalePrice == 10
            && string.IsNullOrEmpty(f.View.Last.Message),
            "Presenter did not quote an eligible selected item.");
        f.Service.SelectTab(ShopTab.Fuse); f.Service.SelectItem("a");
        Check(f.View.Last.SelectedOwned?.Id == "a" && f.View.Last.FusionFirst == null
            && Owned(f.View.Last, "upper").CanSelect,
            "Inspection must be independent from material eligibility.");
        f.Service.ToggleFusionMaterial("a");
        Check(f.View.Last.FusionFirst?.Id == "a" && !Owned(f.View.Last, "upper").CanAddMaterial
            && Owned(f.View.Last, "upper").CanSelect, "First material must restrict add without restricting inspection.");
        f.Service.SelectItem("upper"); f.Service.ToggleFusionMaterial("upper");
        Check(f.View.Last.SelectedOwned?.Id == "upper" && f.View.Last.FusionSecond == null,
            "An incompatible item must be inspectable but cannot be added.");
        f.Service.SelectItem("b"); f.Service.ToggleFusionMaterial("b");
        Check(f.View.Last.CanFuse && f.View.Last.FusionSecond?.Id == "b" && f.View.Last.FusionOddsText.Contains("1등급"),
            "Two eligible materials did not enable fusion and its preview.");
        f.Service.ClearFusionSlot(0);
        Check(f.View.Last.FusionFirst?.Id == "b" && f.View.Last.FusionSecond == null && !f.View.Last.CanFuse,
            "Clearing the first slot did not compact the remaining selection.");
        f.Service.ToggleFusionMaterial("a");
        Check(f.Service.TryFuse("a", "upper") != ShopFusionResult.Success && f.Items.OwnedItems.Count == 3
            && f.View.Last.CanFuse, "Failed fusion consumed items or cleared valid materials.");
        Check(f.Items.TryRemoveItem("b"), "Stale material fixture failed."); f.Refresh();
        Check(f.View.Last.FusionFirst?.Id == "a" && f.View.Last.FusionSecond == null && !f.View.Last.CanFuse,
            "Ownership changes retained a stale material or enabled fusion.");
        Check(f.Items.TryAddItem("b"), "Fusion material restoration failed.");
        f.Service.ToggleFusionMaterial("b");
        Check(f.Service.TryFuse("a", "b") == ShopFusionResult.Success && f.View.Last.FusionResult != null
            && f.View.Last.FusionFirst == null && f.View.Last.FusionSecond == null && !f.View.Last.CanFuse,
            "Successful fusion did not publish a result and clear materials.");
        f.Service.SelectTab(ShopTab.Sell);
        Check(f.View.Last.FusionResult == null, "Changing tabs retained the previous fusion result.");
        f.Service.SelectItem("upper");
        Check(f.Items.TryRemoveItem("upper"), "Stale sale fixture failed."); f.Refresh();
        Check(f.View.Last.SelectedSale == null && !f.View.Last.CanSell, "A stale sale selection stayed actionable.");
        var remaining = f.Items.OwnedItems[0]; f.Service.SelectItem(remaining.itemId);
        Check(f.Service.TrySell(remaining.itemId) == ShopSaleResult.Success && f.View.Last.SelectedSale == null
            && !f.View.Last.CanSell && !string.IsNullOrEmpty(f.View.Last.EmptyMessage),
            "Sale did not clear selection or show the empty inventory state.");
        SetProperty(f.Run, "IsShopOpen", false); Invoke(f.Service, "ShopChanged", false);
        Check(!f.View.Last.Visible && f.View.Last.Tab == ShopTab.Buy, "Closing did not reset the active tab.");
        Check(f.Service.TrySell("a") == ShopSaleResult.Unavailable, "A closed shop accepted a transaction.");
        SetProperty(f.Run, "IsShopOpen", true); Invoke(f.Service, "ShopChanged", true);
        Check(f.View.Last.Visible && f.View.Last.Tab == ShopTab.Buy && f.View.Last.SelectedSale == null
            && f.View.Last.FusionResult == null, "Reopening did not start with a clean purchase tab.");
    }
    private static ShopOwnedItemViewState Owned(ShopViewState state, string id)
    { foreach (var item in state.OwnedItems) if (item.Id == id) return item; throw new InvalidOperationException("Missing row: " + id); }
    private static bool HasVisibleGlyph(TMP_Text text)
    {
        text.ForceMeshUpdate(true, true);
        for (int i = 0; i < text.textInfo.characterCount; i++)
            if (text.textInfo.characterInfo[i].isVisible) return true;
        return false;
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Hidden).GetValue(target);
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
    private static void SetProperty(object target, string name, object value) => SetField(target, "<" + name + ">k__BackingField", value);
    private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Hidden).Invoke(target, args);
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException("Shop tab regression: " + message); }

    private sealed class Fixture : IDisposable
    {
        private readonly GameObject _root = new("__ShopTabsRegression") { hideFlags = HideFlags.HideAndDontSave };
        private readonly List<Object> _assets = new();
        public readonly StageShopService Service;
        public readonly RunManager Run;
        public readonly PlayerItemRuntime Items;
        public readonly CaptureView View = new();
        public Fixture()
        {
            var playerHost = new GameObject("Player"); playerHost.transform.SetParent(_root.transform, false);
            var player = playerHost.AddComponent<PlayerController>();
            var stats = playerHost.GetComponent<PlayerStatManager>();
            var data = Own(ScriptableObject.CreateInstance<PlayerStatData>()); data.baseHP = 100;
            stats.Initialize(data); SetProperty(player, "StatManager", stats);
            var wallet = playerHost.AddComponent<PlayerWallet>(); wallet.Initialize(0);
            var catalog = Own(ScriptableObject.CreateInstance<ItemCatalog>()); catalog.projectId = "tabs-regression";
            Items = playerHost.AddComponent<PlayerItemRuntime>(); Items.Catalog = catalog;
            Items.Initialize(stats, () => ElementType.ELEMENT_WATER);
            var a = Item("a", 1, catalog.projectId); var b = Item("b", 1, catalog.projectId);
            var upper = Item("upper", 2, catalog.projectId); catalog.items = new[] { a, b, upper };
            Check(Items.TryAddItem("a") && Items.TryAddItem("b") && Items.TryAddItem("upper"), "Presenter inventory setup failed.");
            var stage = _root.AddComponent<StageManager>(); SetProperty(stage, "IsReady", true);
            Run = _root.AddComponent<RunManager>(); Run.player = player; Run.stageManager = stage;
            SetProperty(Run, "State", RunState.Playing); SetProperty(Run, "IsShopOpen", true);
            var settings = Own(ScriptableObject.CreateInstance<EconomySettingsSO>());
            Service = _root.AddComponent<StageShopService>(); Service.settings = settings;
            SetProperty(Service, "Wallet", wallet); SetProperty(Service, "ShopPoint", _root.transform);
            SetProperty(Service, "CurrentStock", ShopStock.Create(catalog, Items, settings, 1));
            SetField(Service, "_run", Run); SetField(Service, "_player", player); SetField(Service, "_items", Items);
            SetField(Service, "_transactions", new ShopTransactions(Items, wallet, stats, settings, 1));
            SetField(Service, "_view", View); SetField(Service, "_goldView", new GoldView()); SetField(Service, "_initialized", true);
            Refresh();
        }
        private ItemDefinition Item(string id, int grade, string project)
        { var item = Own(ScriptableObject.CreateInstance<ItemDefinition>()); item.itemId = item.candidateKey = item.displayName = id;
            item.projectId = project; item.grade = grade; item.purchasePrice = 20; return item; }
        private T Own<T>(T value) where T : Object { value.hideFlags = HideFlags.HideAndDontSave; _assets.Add(value); return value; }
        public void Refresh() => Invoke(Service, "Refresh");
        public void Dispose()
        {
            SetProperty(Run, "IsShopOpen", false);
            Object.DestroyImmediate(_root);
            foreach (var asset in _assets) Object.DestroyImmediate(asset);
        }
    }
    private sealed class CaptureView : IShopView
    {
        public ShopViewState Last;
        public void Render(ShopViewState state) => Last = state;
        public event Action<string> PurchaseRequested { add { } remove { } }
        public event Action CloseRequested { add { } remove { } }
        public event Action<ShopTab> TabRequested { add { } remove { } }
        public event Action<string> ItemSelected { add { } remove { } }
        public event Action<string> OfferSelected { add { } remove { } }
        public event Action<string> FusionMaterialToggled { add { } remove { } }
        public event Action<string> SellRequested { add { } remove { } }
        public event Action FusionRequested { add { } remove { } }
        public event Action<int> FusionSlotCleared { add { } remove { } }
    }
    private sealed class GoldView : IGoldView { public void Render(int gold, bool visible) { } }
}

