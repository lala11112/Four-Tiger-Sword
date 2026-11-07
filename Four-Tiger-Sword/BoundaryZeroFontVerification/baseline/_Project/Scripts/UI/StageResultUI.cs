using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Stage loading, generation errors, and the final run result. The minimap owns gameplay progress.</summary>
public class StageResultUI : MonoBehaviour
{
    [Header("Result Panel (optional; created automatically when unassigned)")]
    [SerializeField] private GameObject resultPanel;
    // Keep the legacy field so existing scenes and prefabs retain their text reference.
    [SerializeField] private UnityEngine.UI.Text resultText;
    [SerializeField] private TMP_Text resultLabel;
    [SerializeField] private TMP_Text detailText;
    [SerializeField] private UnityEngine.UI.Button retryButton;
    [SerializeField] private UnityEngine.UI.Button menuButton;
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private TMP_FontAsset fontAsset;
    [SerializeField] private BoundaryZeroRunOverlay boundaryOverlay;

    private enum DisplayMode { Hidden, Loading, Error, Result, RewardSaveError }

    private RunManager _runManager;
    private DisplayMode _mode;
    private bool _initialized;
    private Canvas _fallbackCanvas;
    private GameObject _progressRoot;
    private string _progressLabel = string.Empty;

    private void Awake()
    {
        EnsureInitialized();
        HideResult();
    }

    public void Bind(RunManager runManager)
    {
        EnsureInitialized();
        _runManager = runManager;
        if (boundaryOverlay != null)
            boundaryOverlay.GetComponent<BossHealthHud>()?.Bind(runManager);
        UpdateButtons();
    }

    public void ShowLoading(string label)
    {
        EnsureInitialized();
        _mode = DisplayMode.Loading;
        if (boundaryOverlay != null) boundaryOverlay.ShowLoading();
        SetProgress(label);
        ShowPanel(boundaryOverlay != null ? "구역 준비 중" : "Preparing stage...", label);
        UpdateButtons();
        ClearPanelSelection();
    }

    public void ShowStage(string label)
    {
        EnsureInitialized();
        SetProgress(label);
        HideResult();
    }

    public void ShowError(string message)
    {
        EnsureInitialized();
        _mode = DisplayMode.Error;
        if (boundaryOverlay != null) boundaryOverlay.ShowError();
        ShowPanel(boundaryOverlay != null ? "구역 준비 실패" : "Stage preparation failed", message);
        SetButtonLabel(retryButton, boundaryOverlay != null ? "다시 시도" : "Retry stage");
        UpdateButtons();
        SelectRetryButton();
    }

    public void ShowRunResult(bool won)
    {
        EnsureInitialized();
        _mode = DisplayMode.Result;
        if (boundaryOverlay != null) boundaryOverlay.ShowResult(won);
        if (_runManager != null)
            SetProgress(_runManager.ProgressLabel);
        ShowPanel(boundaryOverlay != null ? (won ? "탐사 완료" : "탐사 종료")
            : (won ? "Run complete!" : "Game over"), _progressLabel
                + (_runManager != null ? "\n" + _runManager.UpgradeRewardSummary : string.Empty));
        if (boundaryOverlay != null && detailText != null) detailText.fontSize = 27;
        SetButtonLabel(retryButton, "Hub로 돌아가기");
        UpdateButtons();
        SelectRetryButton();
    }

    public void ShowRewardSaveError(string message)
    {
        EnsureInitialized();
        _mode = DisplayMode.RewardSaveError;
        if (boundaryOverlay != null) boundaryOverlay.ShowError();
        bool pending = _runManager != null && _runManager.HasPendingUpgradeRewards;
        ShowPanel(pending ? "보상 저장 실패" : "강화 데이터 불러오기 실패",
            (pending ? "보상을 보관 중입니다. 저장 후 계속 진행합니다.\n" : string.Empty) + message);
        SetButtonLabel(retryButton, "저장 다시 시도");
        UpdateButtons();
        SelectRetryButton();
    }

    // Retained for existing UnityEvents and callers of the old result screen.
    public void ShowResult(bool isClear) => ShowRunResult(isClear);

    public void HideResult()
    {
        EnsureInitialized();
        ClearPanelSelection();
        _mode = DisplayMode.Hidden;
        resultPanel.SetActive(false);
        _progressRoot.SetActive(false);
        if (boundaryOverlay != null) boundaryOverlay.Hide();
    }

    private void SetProgress(string label)
    {
        _progressLabel = label ?? string.Empty;
        progressText.text = _progressLabel;
        // Loading/results retain their detail text; gameplay progress lives inside the minimap.
        _progressRoot.SetActive(false);
    }

    private void ShowPanel(string title, string message)
    {
        resultPanel.SetActive(true);
        if (resultLabel != null)
            resultLabel.text = detailText == null && resultText == null && !string.IsNullOrEmpty(message)
                ? title + "\n\n" + message
                : title;
        if (detailText != null)
            detailText.text = message ?? string.Empty;
        if (resultText != null)
            resultText.text = detailText == null && !string.IsNullOrEmpty(message)
                ? title + "\n\n" + message
                : title;
    }

    private void UpdateButtons()
    {
        bool visible = _mode == DisplayMode.Error || _mode == DisplayMode.Result || _mode == DisplayMode.RewardSaveError;
        retryButton.gameObject.SetActive(visible);
        bool menuAvailable = menuButton != null && Application.CanStreamedLevelBeLoaded("TitleScene");
        if (menuButton != null) menuButton.gameObject.SetActive(visible && menuAvailable && _mode != DisplayMode.RewardSaveError);
        retryButton.interactable = visible && _runManager != null;
        if (menuButton != null) menuButton.interactable = visible && menuAvailable && _runManager != null && _mode != DisplayMode.RewardSaveError;
    }

    private void Retry()
    {
        if (_runManager == null || !retryButton.interactable)
            return;
        retryButton.interactable = false;
        if (menuButton != null) menuButton.interactable = false;
        if (_mode == DisplayMode.RewardSaveError)
            _runManager.TryRetryPendingRewards();
        else if (_mode == DisplayMode.Error)
            _runManager.RetryPendingStage();
        else if (_mode == DisplayMode.Result)
            _runManager.ReturnToHub();
    }

    private void ReturnToMenu()
    {
        if (_runManager == null || menuButton == null || !menuButton.interactable)
            return;
        retryButton.interactable = false;
        menuButton.interactable = false;
        _runManager.ReturnToTitle();
    }

    private void SelectRetryButton()
    {
        if (retryButton.interactable && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(retryButton.gameObject);
    }

    private void ClearPanelSelection()
    {
        if (EventSystem.current == null || resultPanel == null)
            return;
        GameObject selected = EventSystem.current.currentSelectedGameObject;
        if (selected != null && selected.transform.IsChildOf(resultPanel.transform))
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;
        _initialized = true;

        if (fontAsset == null)
            fontAsset = TMP_Settings.defaultFontAsset;

        if (boundaryOverlay != null)
        {
            resultPanel = boundaryOverlay.ModalRoot;
            resultLabel = boundaryOverlay.Title;
            detailText = boundaryOverlay.Details;
            retryButton = boundaryOverlay.ActionButton;
            progressText = boundaryOverlay.StageLabel;
            resultText = null;
            menuButton = null;
        }

        bool needsPanel = resultPanel == null || (resultLabel == null && resultText == null)
            || retryButton == null;
        if (needsPanel)
        {
            if (resultPanel != null)
                resultPanel.SetActive(false);
            CreateDefaultPanel();
        }
        if (progressText == null)
            CreateProgressDisplay();
        else
            _progressRoot = progressText.gameObject;

        EnsureEventSystem();
        retryButton.onClick.AddListener(Retry);
        if (menuButton != null) menuButton.onClick.AddListener(ReturnToMenu);
        UpdateButtons();
    }

    private Canvas GetFallbackCanvas()
    {
        if (_fallbackCanvas != null)
            return _fallbackCanvas;
        var root = new GameObject("RunOverlayCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        root.transform.SetParent(transform, false);
        _fallbackCanvas = root.GetComponent<Canvas>();
        _fallbackCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _fallbackCanvas.sortingOrder = 200;
        var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        return _fallbackCanvas;
    }

    private void CreateDefaultPanel()
    {
        RectTransform overlay = CreateRect("StageResultPanel", GetFallbackCanvas().transform);
        Stretch(overlay);
        var shade = overlay.gameObject.AddComponent<UnityEngine.UI.Image>();
        shade.color = new Color(0.015f, 0.02f, 0.035f, 0.9f);
        shade.raycastTarget = true;
        resultPanel = overlay.gameObject;
        resultText = null;

        RectTransform card = CreateRect("Content", overlay);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(700f, 360f);
        var background = card.gameObject.AddComponent<UnityEngine.UI.Image>();
        background.color = new Color(0.055f, 0.075f, 0.10f, 1f);
        background.raycastTarget = false;
        var layout = card.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        layout.padding = new RectOffset(36, 36, 32, 32);
        layout.spacing = 20f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        resultLabel = CreateText("Title", card, 34f, TextAlignmentOptions.Center);
        SetLayout(resultLabel.gameObject, 52f);
        detailText = CreateText("Details", card, 21f, TextAlignmentOptions.Center);
        var detailsLayout = SetLayout(detailText.gameObject, 140f);
        detailsLayout.flexibleHeight = 1f;
        detailText.overflowMode = TextOverflowModes.Truncate;

        RectTransform buttons = CreateRect("Actions", card);
        SetLayout(buttons.gameObject, 54f);
        var row = buttons.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        row.spacing = 18f;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = true;
        row.childForceExpandHeight = true;
        retryButton = CreateButton("RetryButton", "New run", buttons, new Color(0.16f, 0.42f, 0.52f));
        menuButton = CreateButton("MenuButton", "Title menu", buttons, new Color(0.20f, 0.24f, 0.30f));
    }

    private void CreateProgressDisplay()
    {
        RectTransform bar = CreateRect("StageProgress", GetFallbackCanvas().transform);
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 1f);
        bar.anchoredPosition = new Vector2(0f, -16f);
        bar.sizeDelta = new Vector2(340f, 44f);
        var image = bar.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = new Color(0.02f, 0.03f, 0.05f, 0.75f);
        image.raycastTarget = false;
        progressText = CreateText("ProgressLabel", bar, 20f, TextAlignmentOptions.Center);
        _progressRoot = bar.gameObject;
        Stretch(progressText.rectTransform);
        progressText.rectTransform.offsetMin = new Vector2(12f, 0f);
        progressText.rectTransform.offsetMax = new Vector2(-12f, 0f);
        // The modal must cover the HUD and other game UI while a stage is preparing.
        if (resultPanel.transform.parent == GetFallbackCanvas().transform)
            resultPanel.transform.SetAsLastSibling();
        bar.gameObject.SetActive(false);
    }

    private TMP_Text CreateText(string objectName, Transform parent, float size, TextAlignmentOptions alignment)
    {
        RectTransform rect = CreateRect(objectName, parent);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null)
            label.font = fontAsset;
        label.fontSize = size;
        label.alignment = alignment;
        label.color = new Color(0.94f, 0.96f, 0.98f, 1f);
        label.raycastTarget = false;
        label.richText = false;
        return label;
    }

    private UnityEngine.UI.Button CreateButton(string objectName, string text, Transform parent, Color color)
    {
        RectTransform rect = CreateRect(objectName, parent);
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = true;
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        SetLayout(rect.gameObject, 54f).flexibleWidth = 1f;
        TMP_Text label = CreateText("Label", rect, 22f, TextAlignmentOptions.Center);
        label.text = text;
        Stretch(label.rectTransform);
        return button;
    }

    private static void SetButtonLabel(UnityEngine.UI.Button button, string text)
    {
        TMP_Text tmp = button.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null)
            tmp.text = text;
        else
        {
            var legacy = button.GetComponentInChildren<UnityEngine.UI.Text>(true);
            if (legacy != null)
                legacy.text = text;
        }
    }

    private static RectTransform CreateRect(string objectName, Transform parent)
    {
        var go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static UnityEngine.UI.LayoutElement SetLayout(GameObject go, float height)
    {
        var element = go.AddComponent<UnityEngine.UI.LayoutElement>();
        element.preferredHeight = height;
        return element;
    }

    private void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
            return;
        var go = new GameObject("RunEventSystem", typeof(EventSystem));
        go.transform.SetParent(transform, false);
#if ENABLE_INPUT_SYSTEM
        go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        go.AddComponent<StandaloneInputModule>();
#endif
    }

    private void OnDestroy()
    {
        if (retryButton != null)
            retryButton.onClick.RemoveListener(Retry);
        if (menuButton != null)
            menuButton.onClick.RemoveListener(ReturnToMenu);
    }
}
