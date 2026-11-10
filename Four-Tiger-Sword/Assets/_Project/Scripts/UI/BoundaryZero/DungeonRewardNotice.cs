using TMPro;
using UnityEngine;

/// <summary>A brief screen-space notice shown once when a room's chest can be opened.</summary>
[DisallowMultipleComponent]
public sealed class DungeonRewardNotice : MonoBehaviour
{
    private const float HoldSeconds = 2.5f;
    private const float FadeSeconds = .28f;
    private Canvas _canvas;
    private CanvasGroup _group;
    private float _shownAt;

    internal static DungeonRewardNotice Create()
    {
        var root = new GameObject("DungeonRewardNotice", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(CanvasGroup), typeof(DungeonRewardNotice));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 110;
        var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;

        var panel = new GameObject("NoticePlate", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        var rect = (RectTransform)panel.transform;
        rect.SetParent(root.transform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
        rect.pivot = new Vector2(.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -106f);
        rect.sizeDelta = new Vector2(392f, 46f);
        var plate = panel.GetComponent<UnityEngine.UI.Image>();
        plate.color = new Color32(10, 18, 30, 242);
        plate.raycastTarget = false;

        var accent = new GameObject("RewardChest", typeof(RectTransform), typeof(DungeonMinimapIconGraphic))
            .GetComponent<DungeonMinimapIconGraphic>();
        var icon = accent.rectTransform;
        icon.SetParent(rect, false);
        icon.anchorMin = icon.anchorMax = new Vector2(0f, .5f);
        icon.anchoredPosition = new Vector2(64f, 0f);
        icon.sizeDelta = new Vector2(24f, 24f);
        accent.SetSymbol(DungeonMinimapIconGraphic.Symbol.RewardChest);
        accent.color = new Color32(255, 207, 100, 255);

        var label = new GameObject("Message", typeof(RectTransform), typeof(TextMeshProUGUI))
            .GetComponent<TextMeshProUGUI>();
        label.rectTransform.SetParent(rect, false);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(90f, 0f);
        label.rectTransform.offsetMax = new Vector2(-22f, 0f);
        label.font = Resources.Load<TMP_FontAsset>("BoundaryUI/Fonts/BoundaryFont") ?? TMP_Settings.defaultFontAsset;
        label.text = "보상 상자 잠금 해제";
        label.fontSize = 24f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.color = new Color32(255, 235, 193, 255);
        label.raycastTarget = false;

        var notice = root.GetComponent<DungeonRewardNotice>();
        notice._canvas = canvas;
        notice._group = root.GetComponent<CanvasGroup>();
        notice._group.interactable = false;
        notice._group.blocksRaycasts = false;
        notice.Hide();
        return notice;
    }

    internal void Show()
    {
        _shownAt = Time.unscaledTime;
        _group.alpha = 1f;
        gameObject.SetActive(true);
    }

    internal void Hide() => gameObject.SetActive(false);

    private void OnEnable()
    {
        BoundaryZeroBackdrop.CaptureVisibilityChanged += SetCaptureVisibility;
        SetCaptureVisibility(BoundaryZeroBackdrop.IsCapturing);
    }

    private void OnDisable() => BoundaryZeroBackdrop.CaptureVisibilityChanged -= SetCaptureVisibility;

    private void SetCaptureVisibility(bool capturing)
    {
        if (_canvas != null) _canvas.enabled = !capturing;
    }

    private void Update()
    {
        float elapsed = Time.unscaledTime - _shownAt;
        _group.alpha = 1f - Mathf.Clamp01((elapsed - HoldSeconds) / FadeSeconds);
        if (_group.alpha <= 0f) Hide();
    }
}
