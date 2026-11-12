using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Owns the loading cover and reveal across a return from the run to the world Hub.</summary>
[DisallowMultipleComponent]
public sealed class HubReturnTransition : MonoBehaviour
{
    private const string HubSceneName = "Hub Scene";
    private const float CoverSeconds = .2f;
    private const float RevealSeconds = .7f;
    private static readonly Vector2 LoadingReferenceSize = new Vector2(2560f, 1440f);
    private static HubReturnTransition current;
    internal static Func<string, AsyncOperation> SceneLoaderOverride;

    private Canvas _canvas;
    private GameObject _loadingRoot;
    private UnityEngine.UI.Image _cover, _pulse;
    private RectTransform _rotor, _activity;
    private AsyncOperation _operation;
    private Action<string> _onFailure;
    private HubWorldController _hubWorld;
    private bool _ownsHubModal, _ownsPlayerLock, _finished;

    public static bool IsActive => current != null;
    public bool IsLoadingVisible => _loadingRoot != null && _loadingRoot.activeInHierarchy;
    public TMP_Text LoadingHeading { get; private set; }
    public float CoverAlpha => _cover != null ? _cover.color.a : 0f;
    public PlayerController ArrivingPlayer { get; private set; }
    public bool IsRevealingHub { get; private set; }

    public static bool TryStart(BoundaryZeroRunOverlay source, Action<string> onFailure)
    {
        if (IsActive) return false;
        if (!Application.isPlaying || !Application.CanStreamedLevelBeLoaded(HubSceneName))
        {
            onFailure?.Invoke("거점 씬을 불러올 수 없습니다. 빌드 씬 설정을 확인해 주세요.");
            return false;
        }

        var host = new GameObject(nameof(HubReturnTransition), typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        var transition = host.AddComponent<HubReturnTransition>();
        try { transition.BuildOverlay(source); }
        catch (Exception failure)
        {
            host.SetActive(false);
            Destroy(host);
            onFailure?.Invoke("거점 이동 화면을 준비하지 못했습니다. " + failure.Message);
            return false;
        }

        DontDestroyOnLoad(host);
        current = transition;
        transition._onFailure = onFailure;
        Canvas.ForceUpdateCanvases();
        transition.StartCoroutine(transition.LoadAndReveal());
        return true;
    }

    private IEnumerator LoadAndReveal()
    {
        // Let the loading screen render before deserializing the destination.
        yield return null;
        SceneManager.sceneLoaded += OnSceneLoaded;
        string error = null;
        try
        {
            _operation = SceneLoaderOverride != null ? SceneLoaderOverride(HubSceneName)
                : SceneManager.LoadSceneAsync(HubSceneName, LoadSceneMode.Single);
        }
        catch (Exception failure) { error = failure.Message; }
        if (_operation == null)
        {
            Fail("거점을 불러오지 못했습니다. 다시 시도해 주세요." + (error != null ? " " + error : string.Empty));
            yield break;
        }

        // The accepted load cannot be cancelled. Release the outgoing scene and its callback.
        _onFailure = null;
        if (!_operation.isDone)
        {
            _operation.allowSceneActivation = false;
            while (!_operation.isDone && _operation.progress < .9f) yield return null;
            yield return FadeCover(0f, 1f, CoverSeconds);
            _loadingRoot.SetActive(false);
            // A fully black frame precedes scene activation.
            yield return null;
            AllowActivation();
            while (!_operation.isDone) yield return null;
        }
        else
        {
            SetCover(1f);
            _loadingRoot.SetActive(false);
        }

        // The Hub's Awake/Start setup and first rendered frame occur behind the cover.
        yield return null;
        if (_ownsPlayerLock && ArrivingPlayer != null)
        {
            try
            {
                if (!ArrivingPlayer.PrepareStageIdleForReveal())
                    Debug.LogWarning("The arriving player's Idle animation could not be prepared.", ArrivingPlayer);
            }
            catch (Exception failure)
            {
                ArrivingPlayer.CancelStageIdlePresentation();
                Debug.LogWarning("The arriving player's Idle animation could not be prepared: " + failure.Message,
                    ArrivingPlayer);
            }
            // Render an evaluated Idle frame under black before exposing the character.
            yield return null;
        }
        IsRevealingHub = true;
        yield return FadeCover(1f, 0f, RevealSeconds);
        IsRevealingHub = false;
        ReleaseHubControls();
        _finished = true;
        Destroy(gameObject);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != HubSceneName || mode != LoadSceneMode.Single) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        foreach (var root in scene.GetRootGameObjects())
        {
            _hubWorld = root.GetComponentInChildren<HubWorldController>();
            if (_hubWorld != null) break;
        }
        if (_hubWorld == null) return;

        _ownsHubModal = !_hubWorld.IsModalOpen;
        if (_ownsHubModal) _hubWorld.SetModalOpen(true);
        ArrivingPlayer = _hubWorld.GetComponentInChildren<PlayerController>();
        _ownsPlayerLock = ArrivingPlayer != null && !ArrivingPlayer.IsStageTransitioning;
        if (_ownsPlayerLock) ArrivingPlayer.BeginStageTransition();
    }

    private void ReleaseHubControls()
    {
        if (_ownsPlayerLock && ArrivingPlayer != null) ArrivingPlayer.ResumeStageTransition();
        if (_ownsHubModal && _hubWorld != null) _hubWorld.SetModalOpen(false);
        _ownsPlayerLock = _ownsHubModal = false;
    }

    private void AllowActivation()
    {
        if (_operation != null && !_operation.isDone) _operation.allowSceneActivation = true;
    }

    private void Fail(string message)
    {
        var callback = _onFailure;
        _onFailure = null;
        _finished = true;
        gameObject.SetActive(false);
        Destroy(gameObject);
        callback?.Invoke(message);
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        StopAllCoroutines();
        // Never strand the asynchronous scene queue if this temporary host is interrupted.
        AllowActivation();
        ReleaseHubControls();
        IsRevealingHub = false;
        if (current != this) return;
        current = null;
        if (_finished) return;
        if (_canvas != null) _canvas.enabled = false;
        var callback = _onFailure;
        _onFailure = null;
        Destroy(gameObject);
        callback?.Invoke("거점 이동이 취소되었습니다. 다시 시도해 주세요.");
    }

    private IEnumerator FadeCover(float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            SetCover(Mathf.SmoothStep(from, to, elapsed / duration));
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }
        SetCover(to);
    }

    private void SetCover(float alpha) => _cover.color = new Color(0f, 0f, 0f, alpha);

    private void BuildOverlay(BoundaryZeroRunOverlay source)
    {
        _canvas = GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 300;
        var scaler = GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        // Fit the whole authored frame on the loading canvas's first render.
        scaler.referenceResolution = LoadingReferenceSize;
        scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;

        var loading = StretchChild("Loading", transform);
        _loadingRoot = loading.gameObject;
        var shade = _loadingRoot.AddComponent<UnityEngine.UI.Image>();
        shade.color = new Color(.025f, .04f, .065f, 1f);
        shade.raycastTarget = true;
        var cover = StretchChild("BlackCover", transform);
        _cover = cover.gameObject.AddComponent<UnityEngine.UI.Image>();
        _cover.raycastTarget = true;
        SetCover(0f);

        var frameRect = new GameObject("ReferenceFrame", typeof(RectTransform)).GetComponent<RectTransform>();
        frameRect.SetParent(loading, false);
        frameRect.anchorMin = frameRect.anchorMax = frameRect.pivot = new Vector2(.5f, .5f);
        frameRect.anchoredPosition = Vector2.zero;
        frameRect.sizeDelta = LoadingReferenceSize;

        var loadingArt = source != null && source.ModalRoot != null
            ? source.ModalRoot.transform.Find("ReferenceFrame/Loading") : null;
        var details = source != null ? source.Details : null;
        var sourceFont = details != null ? details.font : null;
        var font = sourceFont != null ? sourceFont
            : Resources.Load<TMP_FontAsset>("BoundaryUI/Fonts/BoundaryFont") ?? TMP_Settings.defaultFontAsset;
        var material = sourceFont != null ? details.fontSharedMaterial : null;
        if (loadingArt != null)
        {
            // Copy only the authored loading artwork. It has no references to the outgoing run.
            var art = Instantiate(loadingArt.gameObject, frameRect, false);
            art.name = "LoadingArtwork";
            art.SetActive(true);
            _rotor = art.transform.Find("Seal/Rotor") as RectTransform;
            _pulse = art.transform.Find("Seal/Pulse")?.GetComponent<UnityEngine.UI.Image>();
            _activity = art.transform.Find("ProgressTrack/Activity") as RectTransform;
            LoadingHeading = AddLabel(frameRect, "Title", "이동 중...", font, material, 36f, new Rect(160, 1168, 1100, 60));
        }
        else
        {
            string text = "이동 중...";
            LoadingHeading = AddLabel(frameRect, "Title", text, font, material, 52f, new Rect(680, 656, 1200, 128));
            LoadingHeading.alignment = TextAlignmentOptions.Center;
        }
        LoadingHeading.fontStyle = FontStyles.Bold;
    }

    private void Update()
    {
        if (!IsLoadingVisible) return;
        if (_rotor != null) _rotor.Rotate(0f, 0f, -5f * Time.unscaledDeltaTime);
        if (_pulse != null)
        {
            var tint = _pulse.color;
            tint.a = .32f + .28f * (.5f + .5f * Mathf.Sin(Time.unscaledTime * 2.5f));
            _pulse.color = tint;
        }
        if (_activity != null)
        {
            var track = (RectTransform)_activity.parent;
            float distance = Mathf.Max(0f, track.rect.width - _activity.rect.width);
            _activity.anchoredPosition = new Vector2(Mathf.PingPong(Time.unscaledTime * 180f, distance), 0f);
        }
    }

    private static TMP_Text AddLabel(RectTransform parent, string name, string text, TMP_FontAsset font,
        Material material, float size, Rect layout)
    {
        var label = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        var rect = label.rectTransform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(layout.x, -layout.y);
        rect.sizeDelta = layout.size;
        if (font != null)
        {
            label.font = font;
            label.fontSharedMaterial = material != null && material.mainTexture == font.atlasTexture
                ? material : font.material;
        }
        label.text = text;
        label.fontSize = size;
        label.color = new Color(.9f, .94f, .98f, 1f);
        label.alignment = TextAlignmentOptions.TopLeft;
        label.raycastTarget = false;
        return label;
    }

    private static RectTransform StretchChild(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        return rect;
    }
}
