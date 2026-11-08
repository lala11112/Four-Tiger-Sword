using System;
using System.Collections.Generic;
using Penta.Upgrade;
using TMPro;
using UnityEngine;

/// <summary>Presentation for run-owned item receipts. The RunManager owns pause, input and cursor.</summary>
public sealed class RunItemChoiceView : MonoBehaviour
{
    private sealed class Card
    {
        public RectTransform Root;
        public UnityEngine.UI.Image Icon;
        public TMP_Text Title, Meta, Description, HoldLabel;
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

    private RunItemChoiceService _service;
    private TMP_FontAsset _font;
    private RectTransform _window, _cardsRoot, _synergiesRoot;
    private TMP_Text _title, _guide, _status, _empty, _emptyLabel, _rerollLabel;
    private UnityEngine.UI.Button _emptyClaim, _reroll, _skip;
    private readonly List<Card> _cards = new();
    private readonly List<SynergyOption> _synergies = new();
    private bool _shown, _busy;
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
    }

    public void Hide()
    {
        _shown = false;
        gameObject.SetActive(false);
    }

    public void Refresh()
    {
        if (!_shown || _window == null) return;
        var session = _service?.Current;
        if (session == null) { Hide(); return; }
        bool newSession = _sessionId != session.Id;
        if (newSession) { _sessionId = session.Id; _localError = null; }
        bool starter = session.Kind == RunItemChoiceKind.Starter;
        _title.text = starter ? "출격 유품" : "전투 보상";
        _guide.text = session.NeedsSynergy ? "첫 유품을 찾을 계통을 선택하세요. 선택한 계통의 1등급 후보를 살펴봅니다."
            : starter ? "다음 도전의 첫 아이템을 하나 선택하세요. 선택한 아이템만 받습니다."
            : _service.CanHold ? "아이템 하나를 선택하세요. 보관한 후보는 다시 뽑아도 유지됩니다."
            : "원하는 아이템 하나를 선택하세요. 선택한 아이템만 받습니다.";
        _cardsRoot.gameObject.SetActive(!session.NeedsSynergy);
        _synergiesRoot.gameObject.SetActive(session.NeedsSynergy);
        for (int i = 0; i < _cards.Count; i++) _cards[i].Root.gameObject.SetActive(false);
        for (int i = 0; i < _synergies.Count; i++) _synergies[i].Root.gameObject.SetActive(false);
        VisibleCardCount = 0;
        if (session.NeedsSynergy)
        {
            var options = _service.StarterSynergies();
            for (int i = 0; i < options.Count; i++)
            {
                if (i == _synergies.Count) _synergies.Add(CreateSynergy(i));
                var option = _synergies[i]; var definition = options[i];
                option.Root.gameObject.SetActive(true); option.Id = definition.id; option.SessionId = session.Id;
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
            float total = count * 390 + Mathf.Max(0, count - 1) * 24;
            for (int i = 0; i < count; i++)
            {
                if (i == _cards.Count) _cards.Add(CreateCard(i));
                var card = _cards[i]; var item = session.Candidates[i];
                bool changed = newSession || card.Id != item.itemId;
                card.Root.gameObject.SetActive(true); card.Id = item.itemId; card.SessionId = session.Id;
                card.Root.anchoredPosition = new Vector2((1760 - total) * .5f + i * 414, -170);
                card.Title.text = item.displayName; card.Meta.text = ItemDisplayText.Meta(item);
                card.Icon.sprite = item.icon; card.Icon.enabled = item.icon != null;
                card.Description.text = ItemDisplayText.Description(item, _service.Catalog);
                card.Choose.interactable = !_busy;
                card.Hold.gameObject.SetActive(_service.CanHold);
                card.Hold.interactable = !_busy;
                bool held = session.HeldId == item.itemId;
                card.HoldLabel.text = held ? "보관 중 · 해제" : "후보 보관";
                card.HoldLabel.color = held ? new Color32(152, 220, 189, 255) : Ink;
                if (changed) { card.Scroll.StopMovement(); card.Scroll.verticalNormalizedPosition = 1f; }
            }
        }
        bool empty = !session.NeedsSynergy && session.Candidates.Count == 0;
        _empty.gameObject.SetActive(empty); _emptyClaim.gameObject.SetActive(empty);
        _empty.text = starter ? "현재 받을 수 있는 유품이 없습니다.\n유품 없이 출격을 계속할 수 있습니다."
            : $"현재 받을 수 있는 미보유 아이템이 없습니다.\n대신 {_service.EmptyRewardGold:N0} 골드를 받을 수 있습니다.";
        _emptyLabel.text = starter ? "출격 계속" : $"{_service.EmptyRewardGold:N0} 골드 받기";
        _emptyClaim.interactable = !_busy;
        _reroll.gameObject.SetActive(!starter && !session.NeedsSynergy && !empty);
        _reroll.interactable = !_busy && _service.CanReroll;
        _rerollLabel.text = $"다시 뽑기 · 남은 {_service.RerollsRemaining}회";
        _skip.interactable = !_busy;
        _status.text = _localError ?? _service.LastError ?? (session.NeedsSynergy ? "계통 선택은 이번 출격 유품 후보에만 적용됩니다."
            : _service.CanHold && !empty ? "보관은 후보 한 개에 적용됩니다. 아이템은 선택을 확정할 때 받습니다." : "선택을 확정하면 창이 닫힙니다.");
        Canvas.ForceUpdateCanvases();
    }

    private void Execute(Func<bool> action)
    {
        if (_busy || !_shown || _service?.Current == null) return;
        _busy = true; _localError = null; Refresh();
        try { if (!action()) _localError = _service.LastError ?? "선택을 적용할 수 없습니다. 후보를 다시 확인하세요."; }
        finally { _busy = false; Refresh(); }
    }

    private static readonly Color Ink = new Color32(231, 237, 245, 255);
    private static readonly Color Muted = new Color32(160, 178, 198, 255);

    private void Build()
    {
        _font = ResolveFont();
        var root = (RectTransform)transform;
        var dim = Rect("Dimmer", root, 0, 0, 1920, 1080); PentaUpgradeView.Fill(dim);
        Image(dim, new Color(.015f, .025f, .04f, .8f), true);
        _window = Rect("Window", root, 0, 0, 1760, 900);
        _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(.5f, .5f);
        _window.anchoredPosition = Vector2.zero;
        _window.gameObject.AddComponent<BoundaryZeroWindowFit>().Configure(new Vector2(1760, 900), 1, 24);
        Chrome(_window, PentaUpgradeChrome.Style.Window);
        _title = Label("Title", _window, "전투 보상", 64, 43, 1500, 52, 34);
        _guide = Label("Guide", _window, "아이템 하나를 선택하세요.", 64, 110, 1632, 42, 21);
        _guide.color = Muted;
        _cardsRoot = Rect("Candidates", _window, 0, 0, 1760, 900);
        _synergiesRoot = Rect("Synergies", _window, 0, 0, 1760, 900);
        _empty = Label("Empty", _window, "", 180, 330, 1400, 150, 28);
        _empty.alignment = TextAlignmentOptions.Center;
        _emptyClaim = Button("ClaimEmpty", _window, "계속", 640, 520, 480, 60,
            () => Execute(() => _service.TryClaimEmptyReward(_sessionId)), true);
        _emptyLabel = _emptyClaim.GetComponentInChildren<TMP_Text>();
        _status = Label("Status", _window, "", 64, 760, 1632, 42, 18); _status.color = Muted;
        _reroll = Button("Reroll", _window, "다시 뽑기", 64, 818, 480, 48,
            () => Execute(() => _service.TryReroll(_sessionId)));
        _rerollLabel = _reroll.GetComponentInChildren<TMP_Text>();
        _skip = Button("Skip", _window, "보상 건너뛰기", 1336, 818, 360, 48,
            () => Execute(() => _service.TrySkip(_sessionId)));
    }

    private Card CreateCard(int index)
    {
        var card = new Card { Root = Rect("Candidate" + index, _cardsRoot, 64 + index * 414, 170, 390, 572) };
        Chrome(card.Root, PentaUpgradeChrome.Style.Detail);
        card.Icon = Image(Rect("OriginalItemIcon", card.Root, 147, 18, 96, 96), Color.white, false);
        card.Icon.preserveAspect = true;
        card.Title = Label("Name", card.Root, "", 18, 128, 354, 62, 24); card.Title.alignment = TextAlignmentOptions.Center;
        card.Meta = Label("Meta", card.Root, "", 18, 192, 354, 30, 16); card.Meta.color = Muted;
        card.Meta.alignment = TextAlignmentOptions.Center;
        var scroll = Rect("DescriptionScroll", card.Root, 18, 232, 354, 230);
        card.Scroll = scroll.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
        var viewport = Rect("Viewport", scroll, 0, 0, 354, 230);
        Image(viewport, new Color(0, 0, 0, .001f), true);
        viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        card.Description = Label("Content", viewport, "", 0, 0, 354, 230, 18);
        var content = card.Description.rectTransform;
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.sizeDelta = new Vector2(0, 230);
        card.Description.overflowMode = TextOverflowModes.Overflow;
        var fit = content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fit.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        card.Scroll.viewport = viewport; card.Scroll.content = content;
        card.Scroll.horizontal = false; card.Scroll.vertical = true; card.Scroll.inertia = false;
        card.Scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped; card.Scroll.scrollSensitivity = 32;
        card.Choose = Button("Choose", card.Root, "이 아이템 선택", 26, 480, 338, 42,
            () => Execute(() => _service.TryClaim(card.SessionId, card.Id)), true);
        card.Hold = Button("Hold", card.Root, "후보 보관", 26, 530, 338, 32,
            () => Execute(() => _service.ToggleHeldCandidate(card.SessionId, card.Id)));
        card.HoldLabel = card.Hold.GetComponentInChildren<TMP_Text>();
        return card;
    }

    private SynergyOption CreateSynergy(int index)
    {
        var option = new SynergyOption { Root = Rect("Synergy" + index, _synergiesRoot,
            64 + (index % 3) * 552, 208 + (index / 3) * 236, 528, 212) };
        Chrome(option.Root, PentaUpgradeChrome.Style.Detail);
        option.Button = option.Root.gameObject.AddComponent<UnityEngine.UI.Button>();
        option.Button.targetGraphic = option.Root.GetComponent<PentaUpgradeChrome>();
        option.Button.onClick.AddListener(() => Execute(() => _service.TryChooseStarterSynergy(option.SessionId, option.Id)));
        option.Icon = Image(Rect("SynergyIcon", option.Root, 24, 22, 64, 64), Color.white, false); option.Icon.preserveAspect = true;
        option.Title = Label("Name", option.Root, "", 108, 28, 392, 48, 26);
        option.Summary = Label("Summary", option.Root, "", 24, 108, 480, 78, 19); option.Summary.color = Muted;
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
        Label("Label", rect, text, 8, 0, w - 16, h, 20).alignment = TextAlignmentOptions.Center;
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
