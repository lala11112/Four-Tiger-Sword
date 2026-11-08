using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Result selection inside the shop's existing modal and pause ownership.</summary>
public sealed class ShopFusionChoicePanel : MonoBehaviour
{
    private readonly List<Card> _cards = new();
    private TMP_Text _template;
    private Action<string> _select;
    private Action _cancel;
    private RectTransform _window;
    private sealed class Card
    {
        public RectTransform Root;
        public UnityEngine.UI.Image Icon;
        public TMP_Text Title, Description;
        public UnityEngine.UI.ScrollRect Scroll;
        public string Id;
    }

    public void Configure(TMP_Text textTemplate, Action<string> select, Action cancel)
    {
        _template = textTemplate; _select = select; _cancel = cancel;
        var root = (RectTransform)transform;
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        Background(root, new Color(.01f, .02f, .035f, .92f));
        _window = Rect("Window", root, 0, 0, 1140, 670);
        _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(.5f, .5f);
        _window.anchoredPosition = Vector2.zero;
        Background(_window, new Color32(18, 31, 47, 255));
        Label("Title", _window, "합성 선별 · 결과 하나 선택", 32, 20, 1076, 50, 30);
        Label("Guide", _window, "선택을 확정하면 재료 두 개를 소모합니다. 취소해도 후보는 바뀌지 않습니다.", 32, 76, 1076, 42, 20);
        Button("Cancel", _window, "취소 · 재료 유지", 370, 604, 400, 48, () => _cancel?.Invoke());
        gameObject.SetActive(false);
    }

    public void Render(bool visible, ShopOwnedItemViewState[] candidates)
    {
        bool opening = visible && !gameObject.activeSelf;
        gameObject.SetActive(visible);
        if (!visible) return;
        transform.SetAsLastSibling();
        int count = candidates?.Length ?? 0;
        for (int i = 0; i < count; i++)
        {
            if (i == _cards.Count) _cards.Add(CreateCard(i));
            var card = _cards[i]; var item = candidates[i];
            card.Root.gameObject.SetActive(true); card.Id = item.Id;
            card.Title.text = item.Name + "\n" + ItemDisplayText.Meta(item.Grade, item.PrimarySynergyName);
            card.Icon.sprite = item.Icon; card.Icon.enabled = item.Icon != null;
            card.Description.text = item.Description ?? string.Empty;
            if (opening) { card.Scroll.StopMovement(); card.Scroll.verticalNormalizedPosition = 1f; }
        }
        for (int i = count; i < _cards.Count; i++) _cards[i].Root.gameObject.SetActive(false);
    }

    private Card CreateCard(int index)
    {
        var card = new Card { Root = Rect("Candidate" + index, _window, 32 + index * 364, 132, 348, 448) };
        Background(card.Root, new Color32(30, 46, 65, 255));
        var icon = Rect("OriginalItemIcon", card.Root, 114, 12, 120, 120);
        card.Icon = icon.gameObject.AddComponent<UnityEngine.UI.Image>();
        card.Icon.preserveAspect = true; card.Icon.raycastTarget = false;
        card.Title = Label("Title", card.Root, string.Empty, 12, 140, 324, 62, 22);
        card.Title.alignment = TextAlignmentOptions.Center;
        var scroll = Rect("DescriptionScroll", card.Root, 16, 210, 316, 170);
        card.Scroll = scroll.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
        var viewport = Rect("Viewport", scroll, 0, 0, 316, 170);
        Background(viewport, new Color(1f, 1f, 1f, .01f));
        viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
        var content = Rect("Content", viewport, 0, 0, 316, 170);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
        content.sizeDelta = new Vector2(0, 170);
        card.Description = content.gameObject.AddComponent<TextMeshProUGUI>();
        Style(card.Description, 18); card.Description.textWrappingMode = TextWrappingModes.Normal;
        var fit = content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fit.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        card.Scroll.viewport = viewport; card.Scroll.content = content;
        card.Scroll.horizontal = false; card.Scroll.vertical = true;
        card.Scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        card.Scroll.scrollSensitivity = 32f;
        Button("Select", card.Root, "이 결과 선택", 34, 396, 280, 40, () => _select?.Invoke(card.Id));
        return card;
    }

    private TMP_Text Label(string name, Transform parent, string value, float x, float y, float w, float h, int size)
    {
        var text = Rect(name, parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        Style(text, size); text.text = value;
        return text;
    }
    private void Style(TMP_Text text, int size)
    {
        text.font = _template != null ? _template.font : TMP_Settings.defaultFontAsset;
        if (_template != null) text.fontSharedMaterial = _template.fontSharedMaterial;
        text.fontSize = size; text.color = new Color32(230, 237, 245, 255);
        text.raycastTarget = false; text.alignment = TextAlignmentOptions.TopLeft;
    }
    private void Button(string name, Transform parent, string value, float x, float y,
        float w, float h, Action action)
    {
        var rect = Rect(name, parent, x, y, w, h);
        var image = Background(rect, new Color32(65, 88, 111, 255));
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
        button.onClick.AddListener(() => action());
        Label("Label", rect, value, 0, 0, w, h, 21).alignment = TextAlignmentOptions.Center;
        UIButtonMotion.Install(rect.gameObject);
    }
    private static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h);
        return rect;
    }
    private static UnityEngine.UI.Image Background(RectTransform rect, Color color)
    { var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = true; return image; }
}
