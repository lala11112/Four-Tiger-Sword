using System;
using System.Linq;
using System.Reflection;
using Penta.Upgrade;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Renders production views with disposable Play Mode data for visual checks.</summary>
public static class UIReadabilityPreview
{
    private const int Seed = 812763;
    private const float Epsilon = .05f;
    private static GameObject _root;
    private static RunItemChoiceView _reward;
    private static ShopView _shop;
    private static ItemCatalog _catalog;
    private static PlayerStatData _data;
    private static PlayerStatManager _stats;
    private static PlayerItemRuntime _items;
    private static PlayerWallet _wallet;

    public static string ShowReward(int count, string[] ownedIds = null)
    {
        if (count != 3 && count != 4) throw new ArgumentOutOfRangeException(nameof(count));
        Begin();
        try
        {
            foreach (string id in ownedIds ?? Array.Empty<string>())
                if (!_items.TryAddItem(id)) throw new InvalidOperationException("Unable to add preview item " + id);
            var engravings = count == 3 ? EngravingSnapshot.Empty
                : new EngravingSnapshot(EngravingSettingsSO.Default, new[] { "choices", "reroll", "hold" });
            var choices = new RunItemChoiceService(_items, _wallet, engravings, Seed);
            if (!choices.BeginRoomReward("ui-readability") || choices.Current?.Candidates.Count != count)
                throw new InvalidOperationException("The preview could not generate the expected number of item candidates.");

            var canvasRoot = new GameObject("RunItemChoice", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            canvasRoot.layer = LayerMask.NameToLayer("UI");
            canvasRoot.transform.SetParent(_root.transform, false);
            var canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 180;
            var scaler = canvasRoot.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            _reward = canvasRoot.AddComponent<RunItemChoiceView>();
            typeof(RunItemChoiceView).GetMethod("Build", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_reward, null);
            _reward.Bind(choices);
            _reward.Show();
            return $"Rendering {count} reward candidates. Call Verify after the entrance animation.";
        }
        catch { Close(); throw; }
    }

    public static string ShowShop(ShopTab tab)
    {
        Begin();
        try
        {
            var settings = AssetDatabase.LoadAssetAtPath<EconomySettingsSO>(ShopSetup.Folder + "/EconomySettings.asset");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoundaryZeroShopSetup.PrefabPath);
            if (settings == null || prefab == null) throw new InvalidOperationException("The shop assets are missing.");
            var available = _catalog.items.Where(item => item != null && item.icon != null).ToArray();
            var pair = available.GroupBy(item => item.grade)
                .Select(group => group.GroupBy(item => item.candidateKey).Select(items => items.First()).Take(2).ToArray())
                .First(group => group.Length == 2);
            var sample = pair.Concat(available.Where(item => item.grade != pair[0].grade).Take(6)).ToArray();
            foreach (var item in sample)
                if (!_items.TryAddItem(item.itemId)) throw new InvalidOperationException("Unable to add preview item " + item.itemId);
            var stock = ShopStock.Create(_catalog, _items, settings, Seed);
            var trade = new ShopTransactions(_items, _wallet, _stats, settings, Seed);
            var state = new ShopViewState
            {
                Visible = true, Tab = tab, Gold = _wallet.Gold, Hp = 60, MaxHp = 100, UsesSynergyCatalog = true,
                RefreshCost = stock.RefreshCost, CanRefresh = stock.CanRefresh(_items)
            };
            state.Offers = stock.Offers.Select(offer => new ShopOfferViewState
            {
                Id = offer.Id, Name = offer.Item?.displayName ?? "체력 회복", Icon = offer.Item?.icon,
                Description = offer.IsHealing ? $"최대 체력의 {settings.healingFraction * 100:0}% 회복"
                    : ItemDisplayText.Description(offer.Item, _catalog),
                PrimarySynergyIcon = offer.Item?.primarySynergyIcon,
                PrimarySynergyName = offer.Item?.primarySynergyName,
                Grade = offer.Item?.grade ?? 0, Element = offer.Item?.element ?? ElementType.ELEMENT_NONE,
                Price = offer.Price, IsHealing = offer.IsHealing, CanBuy = true, ButtonLabel = "구매"
            }).ToArray();
            state.SelectedOffer = state.Offers.Where(offer => !offer.IsHealing)
                .OrderByDescending(offer => offer.Description.Length).First();
            state.SelectedOffer.Selected = true;
            state.OwnedItems = sample.Select((item, i) =>
            {
                bool sellable = trade.TryGetSalePrice(item.itemId, out int price);
                return new ShopOwnedItemViewState
                {
                    Id = item.itemId, Name = item.displayName, Icon = item.icon, Grade = item.grade,
                    Element = item.element, Description = ItemDisplayText.Description(item, _catalog),
                    PrimarySynergyIcon = item.primarySynergyIcon, PrimarySynergyName = item.primarySynergyName,
                    SalePrice = price, CanSell = sellable, CanSelect = true, Selected = tab != ShopTab.Buy && i == 0,
                    IsMaterial = tab == ShopTab.Fuse && i < 2, CanAddMaterial = tab == ShopTab.Fuse && i < 2
                };
            }).ToArray();
            state.DetailSource = tab == ShopTab.Buy ? ShopDetailSource.Offer : ShopDetailSource.Owned;
            if (tab != ShopTab.Buy) state.SelectedOwned = state.OwnedItems[0];
            if (tab == ShopTab.Sell) { state.SelectedSale = state.SelectedOwned; state.CanSell = state.SelectedSale.CanSell; }
            if (tab == ShopTab.Fuse)
            {
                state.FusionFirst = state.OwnedItems[0];
                state.FusionSecond = state.OwnedItems[1];
                state.CanFuse = trade.GetFusionPreview(pair[0].itemId, pair[1].itemId, out int grade, out float chance)
                    == ShopFusionResult.Success;
                state.FusionOddsText = chance > 0f ? $"{grade}등급 {(1f - chance):P0} / {grade + 1}등급 {chance:P0}" : $"{grade}등급 100%";
            }
            _shop = Object.Instantiate(prefab, _root.transform).GetComponent<ShopView>();
            if (_shop == null || !_shop.HasBoundaryLayout) throw new InvalidOperationException("The production shop view is missing.");
            _shop.Render(state);
            return $"Rendering the {tab} shop tab. Call Verify after the entrance animation.";
        }
        catch { Close(); throw; }
    }

    public static string Verify()
    {
        Canvas.ForceUpdateCanvases();
        if (_reward != null && _reward.IsOpen)
        {
            var window = (RectTransform)_reward.transform.Find("Window");
            var cards = window.Find("Candidates").Cast<RectTransform>().Where(card => card.gameObject.activeInHierarchy).ToArray();
            if (cards.Length == 0 || cards.Length != _reward.VisibleCardCount) throw new InvalidOperationException("Reward card count mismatch.");
            float lastRight = window.rect.xMin;
            foreach (var card in cards)
            {
                Rect cardBounds = BoundsIn(card, window);
                var scroll = card.GetComponentInChildren<UnityEngine.UI.ScrollRect>();
                var choose = (RectTransform)card.Find("Choose");
                if (cardBounds.xMin < lastRight - Epsilon || cardBounds.xMax > window.rect.xMax + Epsilon
                    || cardBounds.yMin < window.rect.yMin - Epsilon || cardBounds.yMax > window.rect.yMax + Epsilon
                    || card.GetComponent<PentaUpgradeChrome>() == null
                    || scroll.viewport.GetComponent<UnityEngine.UI.RectMask2D>() == null
                    || BoundsIn(scroll.viewport, card).Overlaps(BoundsIn(choose, card)) || choose.rect.height < 48f)
                    throw new InvalidOperationException("Overlapping or clipped reward controls at " + card.name);
                lastRight = cardBounds.xMax;
                CheckText(card.Find("Name").GetComponent<TMP_Text>());
                CheckText(scroll.content.GetComponent<TMP_Text>());
            }
            CheckRewardFraming(window, cards);
            int ownedRows = CheckOwnedSynergySidebar(window, lastRight);
            return $"PASS: centered reward header and actions, {cards.Length} cards, and {ownedRows} rows in the distinct synergy sidebar.";
        }
        if (_shop != null)
        {
            if (!_shop.IsInteractionReady) return "WAIT: shop animation is still running.";
            var controls = new SerializedObject(_shop).FindProperty("boundary");
            var body = (TMP_Text)controls.FindPropertyRelative("DetailBody").objectReferenceValue;
            var action = (UnityEngine.UI.Button)controls.FindPropertyRelative("DetailAction").objectReferenceValue;
            var scroll = body.GetComponentInParent<UnityEngine.UI.ScrollRect>();
            var panel = (RectTransform)controls.FindPropertyRelative("DetailPanel").objectReferenceValue;
            if (scroll == null || scroll.viewport.GetComponent<UnityEngine.UI.Mask>() == null
                || BoundsIn(scroll.viewport, panel).Overlaps(BoundsIn((RectTransform)action.transform, panel))
                || body.fontSize < 22f)
                throw new InvalidOperationException("Shop effects or action do not fit their layout.");
            CheckText(body);
            return "PASS: the shop has readable glyphs and masked effects above its action.";
        }
        throw new InvalidOperationException("Open a reward or shop preview first.");
    }

    private static void CheckRewardFraming(RectTransform window, RectTransform[] cards)
    {
        var bounds = cards.Select(card => BoundsIn(card, window)).ToArray();
        var cardArea = Rect.MinMaxRect(bounds.Min(rect => rect.xMin), bounds.Min(rect => rect.yMin),
            bounds.Max(rect => rect.xMax), bounds.Max(rect => rect.yMax));
        var title = window.Find("Title").GetComponent<TMP_Text>();
        var titleBounds = BoundsIn(title.rectTransform, window);
        CheckContained(title.rectTransform, window);
        CheckFittedText(title);
        if (title.horizontalAlignment != HorizontalAlignmentOptions.Center || titleBounds.yMin < cardArea.yMax - Epsilon
            || Mathf.Abs(titleBounds.center.x - cardArea.center.x) > Epsilon)
            throw new InvalidOperationException("The reward title is not centered above the candidate cards.");

        var skip = (RectTransform)_reward.SkipButton.transform;
        if (!skip.gameObject.activeInHierarchy) throw new InvalidOperationException("The skip button is hidden.");
        CheckContained(skip, window);
        CheckFittedText(skip.GetComponentInChildren<TMP_Text>());
        var footer = BoundsIn(skip, window);
        var reroll = (RectTransform)_reward.RerollButton.transform;
        if (reroll.gameObject.activeInHierarchy)
        {
            CheckContained(reroll, window);
            CheckFittedText(reroll.GetComponentInChildren<TMP_Text>());
            var rerollBounds = BoundsIn(reroll, window);
            if (rerollBounds.xMax >= footer.xMin - Epsilon || Mathf.Abs(rerollBounds.yMin - footer.yMin) > Epsilon
                || Mathf.Abs(rerollBounds.yMax - footer.yMax) > Epsilon)
                throw new InvalidOperationException("Reroll and skip must be separate buttons on the same footer row.");
            footer.xMin = rerollBounds.xMin;
        }
        if (footer.yMax > cardArea.yMin + Epsilon || Mathf.Abs(footer.center.x - cardArea.center.x) > Epsilon)
            throw new InvalidOperationException("The reward actions are not centered below the candidate cards.");

        var status = window.Find("Status").GetComponent<TMP_Text>();
        CheckContained(status.rectTransform, window);
        var statusBounds = BoundsIn(status.rectTransform, window);
        if (statusBounds.yMax > cardArea.yMin + Epsilon || statusBounds.yMin < footer.yMax - Epsilon
            || statusBounds.xMin < cardArea.xMin - Epsilon || statusBounds.xMax > cardArea.xMax + Epsilon)
            throw new InvalidOperationException("The reward status overlaps the candidates or actions or extends outside their column.");
        if (status.gameObject.activeInHierarchy) CheckFittedText(status);
    }

    private static int CheckOwnedSynergySidebar(RectTransform window, float cardsRight)
    {
        var panel = (RectTransform)window.Find("OwnedSynergies");
        if (panel == null || !panel.gameObject.activeInHierarchy || BoundsIn(panel, window).xMin < cardsRight - Epsilon)
            throw new InvalidOperationException("The owned synergy panel must be visible to the right of the reward cards.");
        CheckContained(panel, window);
        var background = panel.GetComponent<UnityEngine.UI.Image>();
        var headerBand = panel.Find("HeaderBand")?.GetComponent<UnityEngine.UI.Image>();
        if (panel.GetComponent<PentaUpgradeChrome>() != null || background == null || background.sprite != null
            || headerBand == null || headerBand.color == background.color)
            throw new InvalidOperationException("The owned synergy panel needs its own plain background and distinct header band.");
        CheckContained(headerBand.rectTransform, panel);
        var heading = panel.Find("Heading").GetComponent<TMP_Text>();
        var empty = panel.Find("Empty").GetComponent<TMP_Text>();
        CheckContained(heading.rectTransform, panel);
        CheckContained(empty.rectTransform, panel);
        CheckFittedText(heading);

        var scroll = panel.Find("Scroll").GetComponent<UnityEngine.UI.ScrollRect>();
        CheckContained((RectTransform)scroll.transform, panel);
        CheckContained(scroll.viewport, panel);
        if (scroll.viewport.GetComponent<UnityEngine.UI.RectMask2D>() == null || scroll.horizontal || !scroll.vertical)
            throw new InvalidOperationException("Owned synergy rows need a masked vertical scroll area.");
        var scrollbar = scroll.verticalScrollbar;
        if (scrollbar == null || scrollbar.handleRect == null)
            throw new InvalidOperationException("The owned synergy scrollbar or handle is missing.");
        var bar = (RectTransform)scrollbar.transform;
        CheckContained(bar, panel);
        CheckContained(scrollbar.handleRect, bar);

        var rows = scroll.content.Cast<RectTransform>().Where(row => row.gameObject.activeInHierarchy).ToArray();
        int expectedRows = _items.SynergyStates.Count(state => state.Definition != null && state.OwnedCount > 0);
        if (rows.Length != expectedRows || empty.gameObject.activeInHierarchy != (expectedRows == 0))
            throw new InvalidOperationException("The owned synergy rows and empty state do not match the preview inventory.");
        if (rows.Length == 0) CheckFittedText(empty);
        if (rows.Length > 0 && scroll.content.rect.height > scroll.viewport.rect.height + Epsilon
            && !scrollbar.gameObject.activeInHierarchy)
            throw new InvalidOperationException("Overflowing owned synergies do not show a scrollbar.");

        foreach (var row in rows)
        {
            CheckContained(row, scroll.viewport, horizontalOnly: true);
            foreach (RectTransform child in row) CheckContained(child, row);
            CheckFittedText(row.Find("Name").GetComponent<TMP_Text>());
            CheckFittedText(row.Find("Progress").GetComponent<TMP_Text>());
        }
        return rows.Length;
    }

    public static string Close()
    {
        if (_root != null) Object.DestroyImmediate(_root);
        if (_data != null) Object.DestroyImmediate(_data);
        _root = null; _reward = null; _shop = null;
        _data = null; _stats = null; _items = null; _wallet = null; _catalog = null;
        return "Closed the temporary UI preview.";
    }

    private static void Begin()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode to preview game UI.");
        Close();
        _catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/_Project/Generated/BoundaryInventory/ItemCatalog.asset");
        if (_catalog == null) throw new InvalidOperationException("The production item catalog is missing.");
        _root = new GameObject("UI Readability Preview") { hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild };
        var actor = new GameObject("In-memory actor");
        actor.transform.SetParent(_root.transform, false);
        actor.SetActive(false);
        _data = ScriptableObject.CreateInstance<PlayerStatData>();
        _data.baseHP = 100;
        _stats = actor.AddComponent<PlayerStatManager>();
        _stats.Initialize(_data);
        _items = actor.AddComponent<PlayerItemRuntime>();
        _items.Catalog = _catalog;
        _items.Initialize(_stats, () => ElementType.ELEMENT_NONE);
        _wallet = actor.AddComponent<PlayerWallet>();
        _wallet.Initialize(750);
    }

    private static Rect BoundsIn(RectTransform rect, RectTransform relativeTo)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector3 min = relativeTo.InverseTransformPoint(corners[0]);
        Vector3 max = relativeTo.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static void CheckContained(RectTransform child, RectTransform parent, bool horizontalOnly = false)
    {
        var bounds = BoundsIn(child, parent);
        var limits = parent.rect;
        if (bounds.xMin < limits.xMin - Epsilon || bounds.xMax > limits.xMax + Epsilon
            || (!horizontalOnly && (bounds.yMin < limits.yMin - Epsilon || bounds.yMax > limits.yMax + Epsilon)))
            throw new InvalidOperationException($"{child.name} extends outside {parent.name}.");
    }

    private static void CheckFittedText(TMP_Text label)
    {
        CheckText(label);
        var rendered = label.GetRenderedValues(true);
        var available = label.rectTransform.rect;
        if (label.isTextTruncated || label.isTextOverflowing
            || rendered.x > available.width + Epsilon || rendered.y > available.height + Epsilon)
            throw new InvalidOperationException($"{label.name} does not fit: {label.text}");
    }

    private static void CheckText(TMP_Text label)
    {
        label.ForceMeshUpdate();
        if (label.font == null || !label.font.HasCharacters(label.text, out uint[] _, true, true)
            || !label.textInfo.characterInfo.Take(label.textInfo.characterCount).Any(character => character.isVisible))
            throw new InvalidOperationException("Missing visible glyphs in " + label.name);
    }
}
