using System;
using System.Collections.Generic;
using Penta.Upgrade;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Presentation for run-owned item receipts. The RunManager owns pause, input and cursor.</summary>
public sealed class RunItemChoiceView : MonoBehaviour
{
    private sealed class Card
    {
        public RectTransform Root;
        public UnityEngine.UI.Image Icon;
        public TMP_Text Title, Grade, Meta, Description, HoldLabel;
        public UnityEngine.UI.Image[] RarityBorder;
        public UnityEngine.UI.Button Choose, Hold;
        public UnityEngine.UI.ScrollRect Scroll;
        public string Id, SessionId;
    }

    private sealed class SynergyOption
    {
        public RectTransform Root;
        public UnityEngine.UI.Image Icon;
        public TMP_Text Title, Summary;
        public UnityEngine.UI.Button Button;
        public string Id, SessionId;
    }

    private sealed class OwnedSynergy
    {
        public RectTransform Root;
        public UnityEngine.UI.Image Icon, Accent;
        public TMP_Text Name, Progress;
        public OwnedSynergyRowInteraction Interaction;
        public ItemSynergyState State;
    }

    private RunItemChoiceService _service;
    private TMP_FontAsset _font;
    private RectTransform _window, _cardsRoot, _synergiesRoot;
    private RectTransform _ownedSynergiesRoot;
    private UnityEngine.UI.ScrollRect _ownedSynergiesScroll;
    private TMP_Text _noOwnedSynergies;
    private readonly List<OwnedSynergy> _ownedSynergies = new();
    private RectTransform _synergyTooltip;
    private TMP_Text _synergyTooltipTitle, _synergyTooltipBody;
    private OwnedSynergy _hoveredSynergy, _focusedSynergy;
    private TMP_Text _title, _guide, _status, _empty, _emptyLabel, _rerollLabel;
    private UnityEngine.UI.Button _emptyClaim, _reroll, _skip;
    private readonly List<Card> _cards = new();
    private readonly List<SynergyOption> _synergies = new();
    private bool _shown, _busy;
    private UIModalMotion _motion;
    private CanvasGroup _windowGroup;
    public bool IsClosing => _motion != null && _motion.IsClosing;
    private string _sessionId, _localError;
    public bool IsOpen => _shown && gameObject.activeInHierarchy;
    public int VisibleCardCount { get; private set; }
    public UnityEngine.UI.Button RerollButton => _reroll;
    public UnityEngine.UI.Button SkipButton => _skip;
    public UnityEngine.UI.Button EmptyClaimButton => _emptyClaim;

    public static RunItemChoiceView Ensure(RunManager run, RunItemChoiceService service)
    {
        if (run == null) throw new ArgumentNullException(nameof(run));
        var views = run.GetComponentsInChildren<RunItemChoiceView>(true);
        RunItemChoiceView view;
        if (views.Length > 0) view = views[0];
        else
        {
            var root = new GameObject("RunItemChoice", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            root.layer = LayerMask.NameToLayer("UI");
            root.transform.SetParent(run.transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 180;
            var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            view = root.AddComponent<RunItemChoiceView>();
            view.Build();
            view.Hide();
        }
        view.Bind(service);
        return view;
    }

    public void Bind(RunItemChoiceService service)
    {
        if (ReferenceEquals(_service, service)) return;
        if (_service != null) _service.Changed -= Refresh;
        _service = service;
        if (_service != null) _service.Changed += Refresh;
        _sessionId = null; _localError = null;
        if (_shown) Refresh();
    }

    public void Show()
    {
        if (_service?.Current == null) return;
        _shown = true; gameObject.SetActive(true);
        transform.SetAsLastSibling();
        Refresh();
        _windowGroup.interactable = false;
        _motion.PlayShow(() =>
        {
            if (!_shown) return;
            _windowGroup.interactable = true;
            var events = EventSystem.current;
            if (events == null || events.currentSelectedGameObject != null) return;
            var initial = VisibleCardCount > 0 ? _cards[0].Choose : _skip;
            events.SetSelectedGameObject(initial.gameObject);
        });
    }

    public void Hide()
    {
        _shown = false;
        ClearSynergyTooltip();
        ClearRewardSelection();
        _motion?.HideImmediate();
        gameObject.SetActive(false);
    }

    public void Close(Action completed)
    {
        if (IsClosing) return;
        if (!_shown || _motion == null) { Hide(); completed?.Invoke(); return; }
        ClearSynergyTooltip();
        _windowGroup.interactable = false;
        _motion.PlayClose(() => { Hide(); completed?.Invoke(); });
    }

    public void Refresh()
    {
        if (!_shown || _window == null) return;
        var session = _service?.Current;
        if (session == null)
        {
            ClearSynergyTooltip();
            if (_windowGroup != null) _windowGroup.interactable = false;
            return;
        }
        bool newSession = _sessionId != session.Id;
        if (newSession) { _sessionId = session.Id; _localError = null; }
        bool starter = session.Kind == RunItemChoiceKind.Starter;
        _title.text = starter ? "출격 유품" : "전투 보상";
        _title.rectTransform.sizeDelta = new Vector2(session.NeedsSynergy ? 1632 : 1288, 52);
        _guide.text = string.Empty;
        _guide.gameObject.SetActive(false);
        _cardsRoot.gameObject.SetActive(!session.NeedsSynergy);
        _synergiesRoot.gameObject.SetActive(session.NeedsSynergy);
        RefreshOwnedSynergies(!session.NeedsSynergy, newSession);
        VisibleCardCount = 0;
        int visibleSynergies = 0;
        if (session.NeedsSynergy)
        {
            var options = _service.StarterSynergies();
            visibleSynergies = options.Count;
            int columns = Mathf.Max(3, (visibleSynergies + 1) / 2);
            for (int i = 0; i < visibleSynergies; i++)
            {
                if (i == _synergies.Count) _synergies.Add(CreateSynergy(i));
                var option = _synergies[i]; var definition = options[i];
                option.Root.gameObject.SetActive(true); option.Id = definition.id; option.SessionId = session.Id;
                LayoutStarterSynergy(option, i, visibleSynergies, columns);
                option.Title.text = definition.displayName;
                option.Summary.text = definition.summary ?? definition.description ?? "이 계통의 유품 후보 보기";
                option.Icon.sprite = definition.icon; option.Icon.enabled = definition.icon != null;
                option.Button.interactable = !_busy;
            }
        }
        else
        {
            int count = Mathf.Min(4, session.Candidates.Count);
            VisibleCardCount = count;
            const float cardAreaWidth = 1288;
            float width = count > 0 ? Mathf.Min(528, (cardAreaWidth - (count - 1) * 24) / count) : 528;
            float total = count * width + Mathf.Max(0, count - 1) * 24;
            for (int i = 0; i < count; i++)
            {
                if (i == _cards.Count) _cards.Add(CreateCard(i));
                var card = _cards[i]; var item = session.Candidates[i];
                bool changed = newSession || card.Id != item.itemId;
                card.Root.gameObject.SetActive(true); card.Id = item.itemId; card.SessionId = session.Id;
                card.Root.anchoredPosition = new Vector2(64 + (cardAreaWidth - total) * .5f + i * (width + 24), -126);
                LayoutCard(card, width, _service.CanHold);
                card.Title.text = item.displayName;
                Color rarity = BoundaryZero.Inventory.ItemRarityFrame.ColorFor(item.grade);
                if (rarity.a == 0f) rarity = Muted;
                card.Grade.text = ItemDisplayText.Meta(item.grade, null);
                card.Grade.color = rarity;
                card.Meta.text = item.primarySynergyName;
                foreach (var edge in card.RarityBorder) edge.color = rarity;
                card.Icon.sprite = item.icon; card.Icon.enabled = item.icon != null;
                card.Description.text = ItemDisplayText.RewardDescription(item, _service.Catalog);
                card.Choose.interactable = !_busy;
                card.Hold.gameObject.SetActive(_service.CanHold);
                card.Hold.interactable = !_busy;
                bool held = session.HeldId == item.itemId;
                card.HoldLabel.text = held ? "보관 중 · 해제" : "후보 보관";
                card.HoldLabel.color = held ? new Color32(152, 220, 189, 255) : Ink;
                if (changed)
                {
                    card.Scroll.StopMovement();
                    card.Scroll.content.anchoredPosition = Vector2.zero;
                }
            }
        }
        for (int i = VisibleCardCount; i < _cards.Count; i++) _cards[i].Root.gameObject.SetActive(false);
        for (int i = visibleSynergies; i < _synergies.Count; i++) _synergies[i].Root.gameObject.SetActive(false);
        bool empty = !session.NeedsSynergy && session.Candidates.Count == 0;
        _empty.gameObject.SetActive(empty); _emptyClaim.gameObject.SetActive(empty);
        _empty.text = "받을 수 있는 유품이 없습니다.";
        _emptyLabel.text = "출격 계속";
        _emptyClaim.interactable = !_busy;
        bool showReroll = !starter && !session.NeedsSynergy && !empty && _service.CanReroll;
        _reroll.gameObject.SetActive(showReroll);
        _reroll.interactable = !_busy && _service.CanReroll;
        _rerollLabel.text = $"다시 뽑기 · {_service.RerollsRemaining}회";
        float actionCenter = session.NeedsSynergy ? 880 : 708;
        Place((RectTransform)_reroll.transform, actionCenter - 316, 842, 304, 56);
        Place((RectTransform)_skip.transform, showReroll ? actionCenter + 12 : actionCenter - 152, 842, 304, 56);
        _skip.interactable = !_busy;
        _status.text = _localError ?? _service.LastError ?? string.Empty;
        _status.gameObject.SetActive(!string.IsNullOrWhiteSpace(_status.text));
        Canvas.ForceUpdateCanvases();
        RefreshSynergyTooltip();
    }

    private void Execute(Func<bool> action)
    {
        if (_busy || !_shown || _motion.IsAnimating || _service?.Current == null) return;
        _busy = true; _localError = null;
        try { Refresh(); if (!action()) _localError = _service.LastError ?? "선택을 적용할 수 없습니다. 후보를 다시 확인하세요."; }
        finally { _busy = false; Refresh(); }
    }

    private static readonly Color Ink = new Color32(231, 237, 245, 255);
    private static readonly Color Muted = new Color32(160, 178, 198, 255);
    private static readonly Color ActiveSynergy = new Color32(235, 116, 121, 255);

    private void Build()
    {
        _font = ResolveFont();
        var root = (RectTransform)transform;
        var dim = Rect("Dimmer", root, 0, 0, 1920, 1080); PentaUpgradeView.Fill(dim);
        Image(dim, new Color(.015f, .025f, .04f, .8f), true);
        _window = Rect("Window", root, 0, 0, 1760, 930);
        _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(.5f, .5f);
        _window.anchoredPosition = Vector2.zero;
        _window.gameObject.AddComponent<BoundaryZeroWindowFit>().Configure(new Vector2(1760, 930), 1, 24);
        Chrome(_window, PentaUpgradeChrome.Style.Window);
        _title = Label("Title", _window, "전투 보상", 64, 43, 1288, 52, 34);
        _title.alignment = TextAlignmentOptions.Midline;
        _guide = Label("Guide", _window, "아이템 하나를 선택하세요.", 64, 110, 1632, 42, 21);
        _guide.color = Muted;
        _cardsRoot = Rect("Candidates", _window, 0, 0, 1760, 930);
        _synergiesRoot = Rect("Synergies", _window, 0, 0, 1760, 930);
        BuildOwnedSynergies();
        _empty = Label("Empty", _window, "", 64, 330, 1288, 150, 28);
        _empty.alignment = TextAlignmentOptions.Center;
        _emptyClaim = Button("ClaimEmpty", _window, "계속", 468, 520, 480, 60,
            () => Execute(() => _service.TryClaimEmptyReward(_sessionId)), true);
        _emptyLabel = _emptyClaim.GetComponentInChildren<TMP_Text>();
        _status = Label("Status", _window, "", 64, 794, 1288, 42, 22); _status.color = new Color32(255, 179, 122, 255);
        _status.alignment = TextAlignmentOptions.Center;
        _reroll = Button("Reroll", _window, "다시 뽑기", 392, 842, 304, 56,
            () => Execute(() => _service.TryReroll(_sessionId)));
        _rerollLabel = _reroll.GetComponentInChildren<TMP_Text>();
        _skip = Button("Skip", _window, "보상 건너뛰기", 720, 842, 304, 56,
            () => Execute(() => _service.TrySkip(_sessionId)));
        BuildSynergyTooltip();
        _motion = UIModalMotion.Ensure(gameObject, _window, .24f, .16f, 18f);
        _windowGroup = _window.GetComponent<CanvasGroup>();
    }

    private Card CreateCard(int index)
    {
        var card = new Card { Root = Rect("Candidate" + index, _cardsRoot, 64 + index * 414, 126, 390, 660) };
        Chrome(card.Root, PentaUpgradeChrome.Style.Detail);
        card.RarityBorder = new UnityEngine.UI.Image[6];
        string[] edges = { "Top", "TopCorner", "Right", "BottomCorner", "Bottom", "Left" };
        for (int i = 0; i < edges.Length; i++)
            card.RarityBorder[i] = Image(Rect("Rarity" + edges[i], card.Root, 0, 0, 0, 0), Color.white, false);
        card.Icon = Image(Rect("OriginalItemIcon", card.Root, 147, 18, 96, 96), Color.white, false);
        card.Icon.preserveAspect = true;
        card.Title = Label("Name", card.Root, "", 18, 128, 354, 62, 24); card.Title.alignment = TextAlignmentOptions.Center;
        card.Grade = Label("Grade", card.Root, "", 18, 192, 92, 30, 19);
        card.Grade.fontStyle = FontStyles.Bold;
        card.Meta = Label("Meta", card.Root, "", 18, 192, 354, 30, 16); card.Meta.color = Muted;
        card.Meta.alignment = TextAlignmentOptions.Center;
        var scroll = Rect("DescriptionScroll", card.Root, 18, 232, 354, 230);
        card.Scroll = scroll.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
        var viewport = Rect("Viewport", scroll, 0, 0, 354, 230);
        Image(viewport, new Color(0, 0, 0, .001f), true);
        viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        card.Description = Label("Content", viewport, "", 0, 0, 354, 230, 22);
        card.Description.richText = true;
        card.Description.lineSpacing = 4;
        var content = card.Description.rectTransform;
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.sizeDelta = new Vector2(0, 230);
        card.Description.overflowMode = TextOverflowModes.Overflow;
        var fit = content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fit.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        card.Scroll.viewport = viewport; card.Scroll.content = content;
        card.Scroll.horizontal = false; card.Scroll.vertical = true; card.Scroll.inertia = false;
        card.Scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped; card.Scroll.scrollSensitivity = 32;
        card.Choose = Button("Choose", card.Root, "아이템 선택", 26, 480, 338, 52,
            () => Execute(() => _service.TryClaim(card.SessionId, card.Id)), true);
        card.Hold = Button("Hold", card.Root, "후보 보관", 26, 530, 338, 48,
            () => Execute(() => _service.ToggleHeldCandidate(card.SessionId, card.Id)));
        card.HoldLabel = card.Hold.GetComponentInChildren<TMP_Text>();
        return card;
    }

    private static void LayoutCard(Card card, float width, bool hold)
    {
        const float height = 660;
        bool narrow = width < 360;
        card.Root.sizeDelta = new Vector2(width, height);
        var corners = new[]
        {
            new Vector2(2, 2), new Vector2(width - 28, 2), new Vector2(width - 2, 28),
            new Vector2(width - 2, height - 29), new Vector2(width - 29, height - 2),
            new Vector2(2, height - 2)
        };
        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 start = corners[i], end = corners[(i + 1) % corners.Length];
            Vector2 center = (start + end) * .5f, direction = end - start;
            var edge = card.RarityBorder[i].rectTransform;
            edge.pivot = new Vector2(.5f, .5f);
            Place(edge, center.x, center.y, direction.magnitude + 1f, 2f);
            edge.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        }
        float iconSize = narrow ? 64 : 72;
        float titleLeft = narrow ? 100 : 112;
        Place(card.Icon.rectTransform, 24, 22, iconSize, iconSize);
        Place(card.Title.rectTransform, titleLeft, 22, width - titleLeft - 24, 76);
        card.Title.fontSize = 26; card.Title.alignment = TextAlignmentOptions.MidlineLeft;
        Place(card.Grade.rectTransform, 24, 108, 74, 32);
        Place(card.Meta.rectTransform, 102, 108, width - 126, narrow ? 48 : 32);
        card.Meta.fontSize = 19; card.Meta.alignment = TextAlignmentOptions.TopLeft;
        float descriptionTop = narrow ? 174 : 158;
        float descriptionHeight = height - (hold ? 136 : 80) - descriptionTop;
        Place((RectTransform)card.Scroll.transform, 24, descriptionTop, width - 48, descriptionHeight);
        Place(card.Scroll.viewport, 0, 0, width - 48, descriptionHeight);
        Place((RectTransform)card.Choose.transform, 24, hold ? height - 116 : height - 60, width - 48, 48);
        Place((RectTransform)card.Hold.transform, 24, height - 60, width - 48, 48);
        foreach (var button in new[] { card.Choose, card.Hold })
        {
            var label = button.GetComponentInChildren<TMP_Text>(true).rectTransform;
            Place(label, 8, 0, width - 64, 48);
        }
    }

    private void BuildOwnedSynergies()
    {
        _ownedSynergiesRoot = Rect("OwnedSynergies", _window, 1376, 126, 320, 660);
        Image(_ownedSynergiesRoot, new Color32(12, 21, 34, 255), false);
        Image(Rect("HeaderBand", _ownedSynergiesRoot, 0, 0, 320, 64), new Color32(27, 42, 60, 255), false);
        Image(Rect("LeftRule", _ownedSynergiesRoot, 0, 0, 1, 660), new Color32(91, 115, 141, 180), false);
        Label("Heading", _ownedSynergiesRoot, "보유 시너지", 20, 20, 156, 32, 24);
        Label("TooltipHint", _ownedSynergiesRoot, "효과 보기", 180, 24, 120, 26, 17).color = Muted;
        Image(Rect("Divider", _ownedSynergiesRoot, 20, 64, 280, 1), new Color32(89, 109, 132, 150), false);
        _noOwnedSynergies = Label("Empty", _ownedSynergiesRoot, "보유 시너지 없음", 20, 84, 280, 64, 22);
        _noOwnedSynergies.color = Muted;
        var scroll = Rect("Scroll", _ownedSynergiesRoot, 20, 80, 280, 560);
        _ownedSynergiesScroll = scroll.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
        var viewport = Rect("Viewport", scroll, 0, 0, 260, 560);
        Image(viewport, new Color(0, 0, 0, .001f), true);
        viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        var content = Rect("Content", viewport, 0, 0, 260, 560);
        var layout = content.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
        layout.cellSize = new Vector2(260, 80); layout.spacing = new Vector2(0, 12);
        layout.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = 1;
        content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        var bar = Rect("Scrollbar", scroll, 268, 0, 12, 560);
        Image(bar, new Color32(38, 52, 70, 255), true);
        var handle = Rect("Handle", bar, 0, 0, 12, 560);
        PentaUpgradeView.Fill(handle);
        var scrollbar = bar.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();
        scrollbar.handleRect = handle; scrollbar.targetGraphic = Image(handle, Muted, true);
        scrollbar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
        _ownedSynergiesScroll.viewport = viewport; _ownedSynergiesScroll.content = content;
        _ownedSynergiesScroll.horizontal = false; _ownedSynergiesScroll.vertical = true;
        _ownedSynergiesScroll.inertia = false;
        _ownedSynergiesScroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        _ownedSynergiesScroll.scrollSensitivity = 46;
        _ownedSynergiesScroll.verticalScrollbar = scrollbar;
        _ownedSynergiesScroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;
    }

    private void RefreshOwnedSynergies(bool visible, bool resetScroll)
    {
        _ownedSynergiesRoot.gameObject.SetActive(visible);
        if (!visible) return;
        var states = new List<ItemSynergyState>();
        foreach (var state in _service.OwnedSynergies)
            if (state.Definition != null && state.OwnedCount > 0) states.Add(state);
        states.Sort((a, b) =>
        {
            int activeOrder = (b.ActiveTier != null).CompareTo(a.ActiveTier != null);
            return activeOrder != 0 ? activeOrder : StringComparer.Ordinal.Compare(a.Definition.id, b.Definition.id);
        });
        _noOwnedSynergies.gameObject.SetActive(states.Count == 0);
        _ownedSynergiesScroll.gameObject.SetActive(states.Count > 0);
        for (int i = 0; i < states.Count; i++)
        {
            if (i == _ownedSynergies.Count)
            {
                var root = Rect("OwnedSynergy" + i, _ownedSynergiesScroll.content, 0, 0, 260, 80);
                var row = new OwnedSynergy { Root = root };
                var hitArea = Image(root, new Color32(99, 127, 157, 24), true);
                row.Interaction = root.gameObject.AddComponent<OwnedSynergyRowInteraction>();
                row.Interaction.targetGraphic = hitArea;
                var colors = row.Interaction.colors;
                colors.normalColor = new Color(1f, 1f, 1f, .25f);
                colors.highlightedColor = colors.selectedColor = Color.white;
                row.Interaction.colors = colors;
                row.Interaction.PointerChanged = inside =>
                {
                    if (inside) _hoveredSynergy = row;
                    else if (_hoveredSynergy == row) _hoveredSynergy = null;
                    RefreshSynergyTooltip();
                };
                row.Interaction.FocusChanged = selected =>
                {
                    if (selected)
                    {
                        _focusedSynergy = row;
                        RevealOwnedSynergy(row);
                    }
                    else if (_focusedSynergy == row) _focusedSynergy = null;
                    RefreshSynergyTooltip();
                };
                row.Accent = Image(Rect("Active", root, 0, 4, 3, 68), ActiveSynergy, false);
                row.Icon = Image(Rect("Icon", root, 12, 4, 32, 32), Color.white, false);
                row.Icon.preserveAspect = true;
                row.Name = Label("Name", root, "", 56, 4, 204, 32, 22);
                row.Progress = Label("Progress", root, "", 12, 42, 248, 32, 22);
                Image(Rect("RowDivider", root, 12, 79, 248, 1), new Color32(56, 76, 99, 155), false);
                _ownedSynergies.Add(row);
            }
            var current = _ownedSynergies[i]; var state = states[i];
            current.State = state;
            current.Root.gameObject.SetActive(true);
            current.Name.text = state.Definition.displayName;
            current.Icon.sprite = state.Definition.icon; current.Icon.enabled = current.Icon.sprite != null;
            bool active = state.ActiveTier != null;
            current.Accent.enabled = active;
            current.Name.color = active ? ActiveSynergy : Ink;
            current.Progress.color = active ? Ink : Muted;
            var next = NextSynergyTier(state);
            string progress = next == null ? $"{state.OwnedCount}개 보유" : $"{state.OwnedCount}/{next.count}";
            current.Progress.text = active ? $"{state.ActiveTier.count}세트 활성 · {progress}" : $"미활성 · {progress}";
        }
        for (int i = states.Count; i < _ownedSynergies.Count; i++) _ownedSynergies[i].Root.gameObject.SetActive(false);
        if (resetScroll)
        {
            _ownedSynergiesScroll.StopMovement();
            _ownedSynergiesScroll.content.anchoredPosition = Vector2.zero;
        }
    }

    private static ItemSynergyTier NextSynergyTier(ItemSynergyState state)
    {
        ItemSynergyTier next = null;
        foreach (var tier in state.Definition.tiers ?? Array.Empty<ItemSynergyTier>())
            if (tier != null && tier.count > state.OwnedCount && (next == null || tier.count < next.count))
                next = tier;
        return next;
    }

    private void BuildSynergyTooltip()
    {
        _synergyTooltip = Rect("OwnedSynergyTooltip", _window, 0, 0, 460, 120);
        var chrome = Chrome(_synergyTooltip, PentaUpgradeChrome.Style.Detail);
        chrome.raycastTarget = false;
        _synergyTooltipTitle = Label("Title", _synergyTooltip, "", 20, 18, 420, 36, 24);
        _synergyTooltipTitle.fontStyle = FontStyles.Bold;
        _synergyTooltipBody = Label("Effects", _synergyTooltip, "", 20, 62, 420, 100, 20);
        _synergyTooltipBody.overflowMode = TextOverflowModes.Overflow;
        _synergyTooltipBody.lineSpacing = 4;
        _synergyTooltip.gameObject.SetActive(false);
    }

    private void RefreshSynergyTooltip()
    {
        if (_synergyTooltip == null) return;
        var row = _hoveredSynergy ?? _focusedSynergy;
        if (!_shown || IsClosing || row == null || !row.Root.gameObject.activeInHierarchy)
        {
            _synergyTooltip.gameObject.SetActive(false);
            return;
        }

        var state = row.State;
        var next = NextSynergyTier(state);
        _synergyTooltipTitle.text = ItemDisplayText.LiteralEffect(state.Definition.displayName);
        _synergyTooltipTitle.color = state.ActiveTier != null ? ActiveSynergy : Ink;
        string body = state.ActiveTier != null
            ? $"<color=#EB7479><b>현재 활성 · {state.ActiveTier.count}세트</b></color>\n{ItemDisplayText.LiteralEffect(state.ActiveTier.text)}"
            : $"<color=#BCCEE0><b>미활성 · {state.OwnedCount}개 보유</b></color>";
        if (next != null)
            body += $"\n\n<color=#F9C48A><b>다음 단계 · {state.OwnedCount}/{next.count}</b></color>\n{ItemDisplayText.LiteralEffect(next.text)}";
        _synergyTooltipBody.text = body;
        const float width = 460f, inset = 32f, gap = 12f;
        float bodyHeight = _synergyTooltipBody.GetPreferredValues(body, width - 40, 0).y;
        float height = 82f + bodyHeight;
        _synergyTooltipBody.rectTransform.sizeDelta = new Vector2(width - 40, bodyHeight);
        Vector3 rowTop = _window.InverseTransformPoint(row.Root.TransformPoint(new Vector3(row.Root.rect.xMin, row.Root.rect.yMax)));
        float x = Mathf.Clamp(rowTop.x - _window.rect.xMin - width - gap, inset, _window.rect.width - width - inset);
        float y = Mathf.Clamp(_window.rect.yMax - rowTop.y, inset, _window.rect.height - height - inset);
        Place(_synergyTooltip, x, y, width, height);
        _synergyTooltip.gameObject.SetActive(true);
    }

    private void ClearSynergyTooltip()
    {
        if (_focusedSynergy != null && EventSystem.current != null &&
            EventSystem.current.currentSelectedGameObject == _focusedSynergy.Root.gameObject)
            EventSystem.current.SetSelectedGameObject(null);
        _hoveredSynergy = _focusedSynergy = null;
        if (_synergyTooltip != null) _synergyTooltip.gameObject.SetActive(false);
    }

    private void ClearRewardSelection()
    {
        var events = EventSystem.current;
        var selected = events != null ? events.currentSelectedGameObject : null;
        if (selected != null && selected.transform.IsChildOf(_window))
            events.SetSelectedGameObject(null);
    }

    private void RevealOwnedSynergy(OwnedSynergy row)
    {
        var scroll = _ownedSynergiesScroll;
        float contentHeight = scroll.content.rect.height, viewportHeight = scroll.viewport.rect.height;
        if (contentHeight <= viewportHeight) return;
        var top = scroll.content.InverseTransformPoint(row.Root.position);
        float rowTop = scroll.content.rect.yMax - top.y, rowBottom = rowTop + row.Root.rect.height;
        float offset = (1f - scroll.verticalNormalizedPosition) * (contentHeight - viewportHeight);
        float target = Mathf.Clamp(offset, rowBottom - viewportHeight, rowTop);
        scroll.StopMovement();
        scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(target / (contentHeight - viewportHeight));
    }

    private static void Place(RectTransform rect, float x, float y, float w, float h)
    { rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); }

    private static void LayoutStarterSynergy(SynergyOption option, int index, int count, int columns)
    {
        const float areaWidth = 1632f, gap = 24f, left = 64f;
        int row = index / columns;
        bool compact = columns > 3;
        float width = (areaWidth - (columns - 1) * gap) / columns;
        int itemsInRow = Mathf.Min(columns, count - row * columns);
        float rowInset = compact ? (columns - itemsInRow) * (width + gap) * .5f : 0f;
        Place(option.Root, left + rowInset + index % columns * (width + gap), 208 + row * 236, width, 212);
        float titleLeft = compact ? 86f : 108f;
        Place(option.Icon.rectTransform, compact ? 18f : 24f, 22, compact ? 52f : 64f, compact ? 52f : 64f);
        Place(option.Title.rectTransform, titleLeft, 28, width - titleLeft - (compact ? 18f : 28f), 48);
        Place(option.Summary.rectTransform, compact ? 18f : 24f, compact ? 94f : 108f,
            width - (compact ? 36f : 48f), compact ? 96f : 78f);
    }

    private SynergyOption CreateSynergy(int index)
    {
        var option = new SynergyOption { Root = Rect("Synergy" + index, _synergiesRoot, 0, 0, 528, 212) };
        Chrome(option.Root, PentaUpgradeChrome.Style.Detail);
        option.Button = option.Root.gameObject.AddComponent<UnityEngine.UI.Button>();
        option.Button.targetGraphic = option.Root.GetComponent<PentaUpgradeChrome>();
        option.Button.onClick.AddListener(() => Execute(() => _service.TryChooseStarterSynergy(option.SessionId, option.Id)));
        option.Icon = Image(Rect("SynergyIcon", option.Root, 24, 22, 64, 64), Color.white, false); option.Icon.preserveAspect = true;
        option.Title = Label("Name", option.Root, "", 108, 28, 392, 48, 26);
        option.Summary = Label("Summary", option.Root, "", 24, 108, 480, 78, 22); option.Summary.color = Muted;
        UIButtonMotion.Install(option.Root.gameObject);
        return option;
    }

    private TMP_Text Label(string name, Transform parent, string value, float x, float y, float w, float h, float size)
    {
        var label = Rect(name, parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = _font; label.fontSize = size; label.color = Ink; label.text = value;
        label.alignment = TextAlignmentOptions.TopLeft; label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.Normal; label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }

    private UnityEngine.UI.Button Button(string name, Transform parent, string text, float x, float y,
        float w, float h, Action click, bool primary = false)
    {
        var rect = Rect(name, parent, x, y, w, h);
        var graphic = Chrome(rect, primary ? PentaUpgradeChrome.Style.PrimaryButton : PentaUpgradeChrome.Style.Button);
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = graphic;
        button.onClick.AddListener(() => click());
        Label("Label", rect, text, 8, 0, w - 16, h, 22).alignment = TextAlignmentOptions.Center;
        PentaUpgradeView.ButtonFeedback(button, graphic);
        UIButtonMotion.Install(rect.gameObject);
        return button;
    }

    private static PentaUpgradeChrome Chrome(RectTransform rect, PentaUpgradeChrome.Style style)
    {
        var graphic = rect.gameObject.AddComponent<PentaUpgradeChrome>(); graphic.Configure(style); graphic.raycastTarget = true; return graphic;
    }
    private static UnityEngine.UI.Image Image(RectTransform rect, Color color, bool raycast)
    {
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = raycast; return image;
    }
    private static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); return rect;
    }
    private static TMP_FontAsset ResolveFont()
    {
        TMP_FontAsset fallback = null;
        foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (font == null) continue;
            if (font.name == "BoundaryFont") return font;
            if (font.name == "BoundaryUpgradeKorean") fallback = font;
        }
        if (fallback != null) return fallback;
        foreach (var label in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (label.font != null && label.font.HasCharacters("전투 보상")) return label.font;
        return TMP_Settings.defaultFontAsset;
    }

    private void OnDestroy() { if (_service != null) _service.Changed -= Refresh; }
}
