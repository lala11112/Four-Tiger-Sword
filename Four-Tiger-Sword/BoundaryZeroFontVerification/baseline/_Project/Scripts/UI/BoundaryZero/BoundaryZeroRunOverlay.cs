using TMPro;
using UnityEngine;

/// <summary>Authored run presentation. StageResultUI owns navigation and run state.</summary>
[DisallowMultipleComponent]
public sealed class BoundaryZeroRunOverlay : MonoBehaviour
{
    [SerializeField] private GameObject modalRoot;
    [SerializeField] private GameObject loadingRoot;
    [SerializeField] private GameObject clearArt;
    [SerializeField] private GameObject deathArt;
    [SerializeField] private GameObject titleRule;
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text details;
    [SerializeField] private TMP_Text stageLabel;
    [SerializeField] private UnityEngine.UI.Button actionButton;
    [SerializeField] private RectTransform rotor;
    [SerializeField] private UnityEngine.UI.Image pulse;
    [SerializeField] private RectTransform activity;
    private Canvas _canvas;

    public GameObject ModalRoot => modalRoot;
    public TMP_Text Title => title;
    public TMP_Text Details => details;
    public TMP_Text StageLabel => stageLabel;
    public UnityEngine.UI.Button ActionButton => actionButton;

    public void Configure(GameObject modal, GameObject loading, GameObject clear, GameObject death,
        GameObject rule, TMP_Text heading, TMP_Text body, TMP_Text stage, UnityEngine.UI.Button action,
        RectTransform rotatingLayer, UnityEngine.UI.Image pulseLayer, RectTransform activitySignal)
    {
        modalRoot = modal; loadingRoot = loading; clearArt = clear; deathArt = death; titleRule = rule;
        title = heading; details = body; stageLabel = stage; actionButton = action;
        rotor = rotatingLayer; pulse = pulseLayer; activity = activitySignal;
    }

    public void ShowLoading()
    {
        SetMode(true, false, false);
        Place(title.rectTransform, 160, 1168, 1100, 60);
        title.fontSize = 36;
        Place(details.rectTransform, 160, 1238, 1450, 84);
        details.fontSize = 27;
        details.alignment = TextAlignmentOptions.TopLeft;
    }

    public void ShowResult(bool won)
    {
        SetMode(false, won, !won);
        Place(title.rectTransform, 1372, 365, 1000, 125);
        title.fontSize = 78;
        Place(details.rectTransform, 1380, 620, 950, 265);
        details.fontSize = 33;
        details.alignment = TextAlignmentOptions.TopLeft;
    }

    public void ShowError()
    {
        SetMode(false, false, true);
        Place(title.rectTransform, 1372, 365, 1000, 125);
        title.fontSize = 68;
        Place(details.rectTransform, 1380, 620, 950, 330);
        details.fontSize = 27;
        details.alignment = TextAlignmentOptions.TopLeft;
    }

    public void Hide()
    {
        if (modalRoot != null) modalRoot.SetActive(false);
        SetCanvasOrder(100);
    }

    private void SetMode(bool loading, bool clear, bool death)
    {
        SetCanvasOrder(200);
        loadingRoot.SetActive(loading);
        clearArt.SetActive(clear);
        deathArt.SetActive(death);
        titleRule.SetActive(!loading);
    }

    private void SetCanvasOrder(int order)
    {
        if (_canvas == null) _canvas = GetComponent<Canvas>();
        if (_canvas != null) _canvas.sortingOrder = order;
    }

    private void Update()
    {
        if (loadingRoot == null || !loadingRoot.activeInHierarchy) return;
        if (rotor != null) rotor.Rotate(0, 0, -5f * Time.unscaledDeltaTime);
        if (pulse != null)
        {
            var tint = pulse.color;
            tint.a = .32f + .28f * (.5f + .5f * Mathf.Sin(Time.unscaledTime * 2.5f));
            pulse.color = tint;
        }
        // No synthetic percentage: activity means work is in progress, not completed work.
        if (activity != null)
            activity.anchoredPosition = new Vector2(Mathf.PingPong(Time.unscaledTime * 180f, 456f), 0);
    }

    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }
}
