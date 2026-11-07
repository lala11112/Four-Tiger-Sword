using System.Collections;
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

    [Header("Stage Introduction (unscaled seconds)")]
    [SerializeField, Min(0f)] private float introNumberFadeIn = .25f;
    [SerializeField, Min(0f)] private float introNumberHold = .85f;
    [SerializeField, Min(0f)] private float introNumberFadeOut = .2f;
    [SerializeField, Min(0f)] private float introWorldReveal = .5f;

    [Header("Death Presentation (unscaled seconds)")]
    [SerializeField, Min(0f)] private float deathFadeDuration = .7f;

    private enum DisplayMode { Hidden, Loading, Intro, Death, Error, Result, RewardSaveError }

    private RunManager _runManager;
    private DisplayMode _mode;
    private bool _initialized;
    private Canvas _fallbackCanvas;
    private GameObject _progressRoot;
    private string _progressLabel = string.Empty;
    private CanvasGroup _introCover;
    private TMP_Text _introNumber;
    private CanvasGroup _deathFade;
    private uint _deathFadeVersion;
    private uint _introVersion, _resultVersion;
    private Coroutine _resultReveal;
    private bool _resultReady;
    private bool _hubReturnPending;
    private TMP_Text _hubReturnError;
    private CanvasGroup _fallbackTitleGroup, _fallbackDetailsGroup, _fallbackActionGroup, _fallbackMenuGroup;

    public bool IsStageIntroPlaying { get; private set; }
    public bool StageIntroCompleted { get; private set; }
    public bool IsDeathFadePlaying { get; private set; }
    public bool DeathFadeCompleted { get; private set; }
    public bool IsResultAnimating => _mode == DisplayMode.Result && !_resultReady;
    public BoundaryZeroRunOverlay PresentationOverlay => boundaryOverlay;

    /// <summary>Keeps the death visible and blocks UI input until its animation has finished.</summary>
    public void BeginDeathPresentation()
    {
        HideResult();
        EnsureDeathFade();
        _mode = DisplayMode.Death;
        _deathFade.alpha = 0f;
        _deathFade.blocksRaycasts = true;
        _deathFade.gameObject.SetActive(true);
        UpdateButtons();
    }

    public IEnumerator FadeOutForDeath()
    {
        uint version = _deathFadeVersion;
        if (!DeathFadeIsCurrent(version)) yield break;
        IsDeathFadePlaying = true;
        float elapsed = 0f;
        while (elapsed < deathFadeDuration)
        {
            if (!DeathFadeIsCurrent(version)) yield break;
            _deathFade.alpha = Mathf.SmoothStep(0f, 1f, Phase(elapsed, 0f, deathFadeDuration));
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }
        if (!DeathFadeIsCurrent(version)) yield break;
        _deathFade.alpha = 1f;
        // Render a completely black frame before the result is allowed to appear.
        yield return null;
        if (!DeathFadeIsCurrent(version)) yield break;
        IsDeathFadePlaying = false;
        DeathFadeCompleted = true;
    }

    public void CancelDeathFade()
    {
        ++_deathFadeVersion;
        IsDeathFadePlaying = false;
        DeathFadeCompleted = false;
        if (_deathFade == null) return;
        _deathFade.alpha = 0f;
        _deathFade.blocksRaycasts = false;
        _deathFade.gameObject.SetActive(false);
    }

    private bool DeathFadeIsCurrent(uint version) => version == _deathFadeVersion && isActiveAndEnabled
        && _mode == DisplayMode.Death && _deathFade != null && _deathFade.gameObject.activeInHierarchy;

    private void EnsureDeathFade()
    {
        if (_deathFade != null) return;
        var root = new GameObject("DeathFadeCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.GraphicRaycaster), typeof(UnityEngine.UI.Image), typeof(CanvasGroup));
        root.transform.SetParent(transform, false);
        Stretch(root.GetComponent<RectTransform>());
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Keep the world black underneath the existing result canvas (sorting order 200).
        canvas.sortingOrder = 199;
        var black = root.GetComponent<UnityEngine.UI.Image>();
        black.color = Color.black;
        black.raycastTarget = true;
        _deathFade = root.GetComponent<CanvasGroup>();
        _deathFade.interactable = false;
        _deathFade.alpha = 0f;
        root.SetActive(false);
    }

    /// <summary>Shows a stage banner over the world while RunManager keeps gameplay suspended.</summary>
    public void BeginStageIntro(int chapterNumber, int stageNumber)
    {
        EnsureInitialized();
        HideResult();
        EnsureIntroCover();
        _mode = DisplayMode.Intro;
        StageIntroCompleted = false;
        IsStageIntroPlaying = true;
        _introCover.alpha = 1f;
        _introCover.blocksRaycasts = true;
        _introNumber.text = $"Stage {chapterNumber} - {stageNumber}";
        _introNumber.alpha = 0f;
        _introCover.gameObject.SetActive(true);
        Canvas.ForceUpdateCanvases();
        UpdateButtons();
    }

    /// <summary>The run owns this coroutine and retains gameplay suspension until it completes.</summary>
    public IEnumerator PlayStageIntro()
    {
        uint version = _introVersion;
        if (!IntroIsCurrent(version)) yield break;
        // Let the banner and the newly placed camera settle for a rendered frame.
        yield return null;
        float elapsed = 0f;
        float fadeInEnd = introNumberFadeIn;
        float holdEnd = fadeInEnd + introNumberHold;
        float numberEnd = holdEnd + introNumberFadeOut;
        float end = numberEnd + introWorldReveal;
        while (elapsed < end)
        {
            if (!IntroIsCurrent(version)) yield break;
            _introNumber.alpha = elapsed < fadeInEnd ? Phase(elapsed, 0f, introNumberFadeIn)
                : elapsed < holdEnd ? 1f : 1f - Phase(elapsed, holdEnd, introNumberFadeOut);
            _introCover.alpha = 1f - Phase(elapsed, numberEnd, introWorldReveal);
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }
        if (!IntroIsCurrent(version)) yield break;
        _introNumber.alpha = 0f;
        _introCover.alpha = 0f;
        _introCover.blocksRaycasts = false;
        _introCover.gameObject.SetActive(false);
        IsStageIntroPlaying = false;
        StageIntroCompleted = true;
    }

    public void CancelStageIntro()
    {
        ++_introVersion;
        IsStageIntroPlaying = false;
        StageIntroCompleted = false;
        if (_introCover == null) return;
        _introCover.alpha = 0f;
        _introCover.blocksRaycasts = false;
        _introCover.gameObject.SetActive(false);
    }

    private bool IntroIsCurrent(uint version) => version == _introVersion && IsStageIntroPlaying
        && isActiveAndEnabled && _mode == DisplayMode.Intro && _introCover != null && _introCover.gameObject.activeInHierarchy;

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
        CancelDeathFade();
        CancelStageIntro();
        CancelResultReveal();
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
        CancelDeathFade();
        CancelStageIntro();
        CancelResultReveal();
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
        CancelStageIntro();
        CancelResultReveal();
        _mode = DisplayMode.Result;
        _resultReady = false;
        if (boundaryOverlay != null) boundaryOverlay.ShowResult(won);
        if (_runManager != null)
            SetProgress(_runManager.ProgressLabel);
        string rewards = _runManager != null ? _runManager.UpgradeRewardSummary : string.Empty;
        string message = boundaryOverlay != null ? string.Empty
            : _progressLabel + (!string.IsNullOrEmpty(rewards) ? "\n" + rewards : string.Empty);
        if (boundaryOverlay != null)
            boundaryOverlay.SetResultSummary(_runManager != null ? _runManager.ResultSummary
                : new RunResultSummary(_progressLabel, 0L, 0, 0, "강화석", null));
        if (boundaryOverlay != null) boundaryOverlay.PrepareResultReveal();
        else PrepareFallbackResultReveal();
        ShowPanel(boundaryOverlay != null ? (won ? "탐사 완료" : "탐사 종료")
            : (won ? "Run complete!" : "Game over"), message);
        SetButtonLabel(retryButton, "거점으로 돌아가기");
        UpdateButtons();
        ClearPanelSelection();
        if (!Application.isPlaying)
        {
            if (boundaryOverlay != null) boundaryOverlay.CancelResultReveal();
            ResetFallbackResultReveal();
            _resultReady = true;
            UpdateButtons();
            SelectRetryButton();
            return;
        }
        if (isActiveAndEnabled) _resultReveal = StartCoroutine(RevealRunResult(_resultVersion));
    }

    public void ShowRewardSaveError(string message)
    {
        EnsureInitialized();
        CancelStageIntro();
        CancelResultReveal();
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
        CancelDeathFade();
        CancelStageIntro();
        CancelResultReveal();
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
        _hubReturnPending = false;
        if (_hubReturnError != null) _hubReturnError.gameObject.SetActive(false);
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
        bool ready = (_mode != DisplayMode.Result || _resultReady)
            && !_hubReturnPending && !HubReturnTransition.IsActive;
        retryButton.interactable = visible && ready && _runManager != null;
        if (menuButton != null) menuButton.interactable = visible && ready && menuAvailable && _runManager != null && _mode != DisplayMode.RewardSaveError;
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

    public void SetHubReturnInProgress(bool loading)
    {
        EnsureInitialized();
        _hubReturnPending = loading;
        if (loading)
        {
            if (_hubReturnError != null) _hubReturnError.gameObject.SetActive(false);
            ClearPanelSelection();
        }
        UpdateButtons();
    }

    public void ShowHubReturnError(string message)
    {
        EnsureInitialized();
        if (_hubReturnError == null)
        {
            _hubReturnError = CreateText("HubReturnError", retryButton.transform,
                boundaryOverlay != null ? 34f : 20f, TextAlignmentOptions.TopLeft);
            if (detailText != null && detailText.font != null) _hubReturnError.font = detailText.font;
            _hubReturnError.color = new Color(1f, .6f, .56f, 1f);
            var rect = _hubReturnError.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -24f);
            rect.sizeDelta = new Vector2(0f, boundaryOverlay != null ? 110f : 64f);
        }
        _hubReturnError.text = message;
        _hubReturnError.gameObject.SetActive(true);
        SetHubReturnInProgress(false);
        SelectRetryButton();
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

    private IEnumerator RevealRunResult(uint version)
    {
        // Preparation happened before activation, so no full-opacity frame can flash through.
        yield return null;
        if (!ResultIsCurrent(version)) yield break;
        if (boundaryOverlay != null)
        {
            yield return boundaryOverlay.PlayResultReveal();
            if (!boundaryOverlay.ResultRevealCompleted) yield break;
        }
        else
        {
            float elapsed = 0f;
            while (elapsed < .6f)
            {
                if (!ResultIsCurrent(version)) yield break;
                SetGroupAlpha(_fallbackTitleGroup, Phase(elapsed, 0f, .22f));
                SetGroupAlpha(_fallbackDetailsGroup, Phase(elapsed, .18f, .25f));
                float actions = Phase(elapsed, .4f, .2f);
                SetGroupAlpha(_fallbackActionGroup, actions);
                SetGroupAlpha(_fallbackMenuGroup, actions);
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
            ResetFallbackResultReveal();
        }
        if (!ResultIsCurrent(version)) yield break;
        _resultReveal = null;
        _resultReady = true;
        UpdateButtons();
        SelectRetryButton();
    }

    private bool ResultIsCurrent(uint version) => version == _resultVersion && isActiveAndEnabled
        && _mode == DisplayMode.Result && resultPanel != null && resultPanel.activeInHierarchy;

    private void CancelResultReveal()
    {
        ++_resultVersion;
        if (_resultReveal != null) StopCoroutine(_resultReveal);
        _resultReveal = null;
        _resultReady = false;
        if (boundaryOverlay != null) boundaryOverlay.CancelResultReveal();
        ResetFallbackResultReveal();
    }

    private void PrepareFallbackResultReveal()
    {
        _fallbackTitleGroup = GroupFor(resultLabel != null ? resultLabel.gameObject : resultText != null ? resultText.gameObject : null);
        _fallbackDetailsGroup = GroupFor(detailText != null ? detailText.gameObject : null);
        _fallbackActionGroup = GroupFor(retryButton != null ? retryButton.gameObject : null);
        _fallbackMenuGroup = GroupFor(menuButton != null ? menuButton.gameObject : null);
        SetGroupAlpha(_fallbackTitleGroup, 0f);
        SetGroupAlpha(_fallbackDetailsGroup, 0f);
        SetGroupAlpha(_fallbackActionGroup, 0f);
        SetGroupAlpha(_fallbackMenuGroup, 0f);
    }

    private void ResetFallbackResultReveal()
    {
        SetGroupAlpha(_fallbackTitleGroup, 1f);
        SetGroupAlpha(_fallbackDetailsGroup, 1f);
        SetGroupAlpha(_fallbackActionGroup, 1f);
        SetGroupAlpha(_fallbackMenuGroup, 1f);
    }

    private static CanvasGroup GroupFor(GameObject target)
    {
        if (target == null) return null;
        var group = target.GetComponent<CanvasGroup>();
        return group != null ? group : target.AddComponent<CanvasGroup>();
    }

    private static void SetGroupAlpha(CanvasGroup group, float alpha)
    { if (group != null) group.alpha = alpha; }

    private static float Phase(float elapsed, float start, float duration)
        => elapsed < start ? 0f : duration <= 0f ? 1f : Mathf.Clamp01((elapsed - start) / duration);

    private void EnsureIntroCover()
    {
        if (_introCover != null) return;
        var root = new GameObject("StageIntroCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster),
            typeof(UnityEngine.UI.Image), typeof(CanvasGroup));
        root.transform.SetParent(transform, false);
        Stretch(root.GetComponent<RectTransform>());
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250;
        var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        var sourceScaler = boundaryOverlay != null ? boundaryOverlay.GetComponent<UnityEngine.UI.CanvasScaler>() : null;
        scaler.referenceResolution = sourceScaler != null ? sourceScaler.referenceResolution : new Vector2(2560f, 1440f);
        scaler.matchWidthOrHeight = sourceScaler != null ? sourceScaler.matchWidthOrHeight : .5f;
        // Block pointer input during the introduction without obscuring the stage.
        var inputBlocker = root.GetComponent<UnityEngine.UI.Image>();
        inputBlocker.color = Color.clear;
        inputBlocker.raycastTarget = true;
        _introCover = root.GetComponent<CanvasGroup>();
        _introCover.interactable = false;
        _introCover.blocksRaycasts = false;

        var banner = CreateRect("StageBanner", root.transform);
        banner.anchorMin = new Vector2(0f, .5f);
        banner.anchorMax = new Vector2(1f, .5f);
        banner.pivot = new Vector2(.5f, .5f);
        banner.anchoredPosition = Vector2.zero;
        banner.sizeDelta = new Vector2(0f, 435f);
        var backdrop = banner.gameObject.AddComponent<UnityEngine.UI.Image>();
        backdrop.color = new Color(.015f, .02f, .035f, .86f);
        backdrop.raycastTarget = false;

        _introNumber = CreateText("StageNumber", banner, 80f, TextAlignmentOptions.Center);
        if (boundaryOverlay != null && boundaryOverlay.IntroFont != null)
        {
            _introNumber.font = boundaryOverlay.IntroFont;
            _introNumber.fontSharedMaterial = boundaryOverlay.IntroFont.material;
        }
        _introNumber.color = Color.white;
        _introNumber.fontStyle = FontStyles.Normal;
        _introNumber.enableAutoSizing = true;
        _introNumber.fontSizeMin = 32f;
        _introNumber.fontSizeMax = 80f;
        var numberRect = _introNumber.rectTransform;
        Stretch(numberRect);
        numberRect.offsetMin = new Vector2(48f, 20f);
        numberRect.offsetMax = new Vector2(-48f, -20f);
        root.SetActive(false);
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
        UIButtonMotion.Install(resultPanel);
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

    private void OnEnable()
    {
        if (!_initialized || _mode != DisplayMode.Result || resultPanel == null || !resultPanel.activeInHierarchy) return;
        _resultReady = false;
        if (boundaryOverlay != null) boundaryOverlay.PrepareResultReveal();
        else PrepareFallbackResultReveal();
        UpdateButtons();
        ClearPanelSelection();
        if (!Application.isPlaying)
        {
            if (boundaryOverlay != null) boundaryOverlay.CancelResultReveal();
            ResetFallbackResultReveal();
            _resultReady = true;
            UpdateButtons();
            return;
        }
        _resultReveal = StartCoroutine(RevealRunResult(_resultVersion));
    }

    private void OnDisable()
    {
        CancelDeathFade();
        CancelStageIntro();
        CancelResultReveal();
        ClearPanelSelection();
        if (retryButton != null) retryButton.interactable = false;
        if (menuButton != null) menuButton.interactable = false;
    }

    private void OnDestroy()
    {
        CancelDeathFade();
        CancelStageIntro();
        CancelResultReveal();
        if (retryButton != null)
            retryButton.onClick.RemoveListener(Retry);
        if (menuButton != null)
            menuButton.onClick.RemoveListener(ReturnToMenu);
    }
}
