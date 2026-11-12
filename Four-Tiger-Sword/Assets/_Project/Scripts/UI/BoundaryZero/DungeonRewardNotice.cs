using TMPro;
using UnityEngine;

/// <summary>A screen-space notice for unopened room reward chests.</summary>
[DisallowMultipleComponent]
public sealed class DungeonRewardNotice : MonoBehaviour
{
    private Canvas _canvas;

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
        rect.sizeDelta = new Vector2(490f, 57.5f);
        var plate = panel.GetComponent<UnityEngine.UI.Image>();
        plate.color = new Color32(10, 18, 30, 242);
        plate.raycastTarget = false;

        var accent = new GameObject("RewardChest", typeof(RectTransform), typeof(DungeonMinimapIconGraphic))
            .GetComponent<DungeonMinimapIconGraphic>();
        var icon = accent.rectTransform;
        icon.SetParent(rect, false);
        icon.anchorMin = icon.anchorMax = new Vector2(0f, .5f);
        icon.anchoredPosition = new Vector2(80f, 0f);
        icon.sizeDelta = new Vector2(30f, 30f);
        accent.SetSymbol(DungeonMinimapIconGraphic.Symbol.RewardChest);
        accent.color = new Color32(255, 207, 100, 255);

        var label = new GameObject("Message", typeof(RectTransform), typeof(TextMeshProUGUI))
            .GetComponent<TextMeshProUGUI>();
        label.rectTransform.SetParent(rect, false);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(112.5f, 0f);
        label.rectTransform.offsetMax = new Vector2(-27.5f, 0f);
        label.font = Resources.Load<TMP_FontAsset>("BoundaryUI/Fonts/BoundaryFont") ?? TMP_Settings.defaultFontAsset;
        label.text = "보상 상자 잠금 해제";
        label.fontSize = 30f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.color = new Color32(255, 235, 193, 255);
        label.raycastTarget = false;

        var notice = root.GetComponent<DungeonRewardNotice>();
        notice._canvas = canvas;
        var group = root.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        notice.Hide();
        return notice;
    }

    internal void Show()
    {
        if (!gameObject.activeSelf) gameObject.SetActive(true);
    }

    internal void Hide()
    {
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

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
}
