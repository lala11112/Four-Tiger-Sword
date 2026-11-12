using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
    [SerializeField] private TMP_FontAsset bodyFont;
    [SerializeField] private TMP_FontAsset titleKorean;
    [SerializeField] private TMP_FontAsset titleLatin;
    private Canvas _canvas;
    private readonly List<RevealLayer> _revealLayers = new();
    private uint _revealVersion;
    private bool _revealPrepared;
    private GameObject _resultSummaryRoot;
    private TMP_Text _resultProgress, _clearedRooms, _conversionSource;
    private ResultValue _stageReward, _deathReward, _goldConversion, _balance;
    private RunResultSummary _resultSummary;
    private bool _hasResultSummary, _rewardTransferPrepared;
    private const float RewardReadSeconds = .25f;
    private const float RewardTransferSeconds = 1.25f;

    private readonly struct ResultValue
    {
        public readonly TMP_Text Amount;
        public readonly TMP_Text Currency;

        public ResultValue(TMP_Text amount, TMP_Text currency)
        {
            Amount = amount;
            Currency = currency;
        }
    }

    private sealed class RevealLayer
    {
        public CanvasGroup Group;
        public RectTransform Rect;
        public Vector2 Position;
        public float Start, Duration, Rise;
    }
    private static readonly Regex RewardValues = new Regex(
        @"(?<=  )[0-9][0-9,.\u00a0\u202f]*(?=\s|$)|(?<=전투 방 )[0-9]+(?=개)");

    public GameObject ModalRoot => modalRoot;
    public TMP_Text Title => title;
    public TMP_Text Details => details;
    public TMP_Text StageLabel => stageLabel;
    public UnityEngine.UI.Button ActionButton => actionButton;
    public TMP_FontAsset IntroFont => titleLatin != null ? titleLatin : bodyFont;
    public GameObject ResultSummaryRoot => _resultSummaryRoot;
    public bool ResultRevealCompleted { get; private set; }
    public bool IsRewardTransferPlaying { get; private set; }

    /// <summary>Sets every result layer before the modal becomes visible.</summary>
    public void PrepareResultReveal()
    {
        CancelResultReveal();
        _revealLayers.Clear();
        var art = clearArt != null ? clearArt.transform.parent : deathArt != null ? deathArt.transform.parent : null;
        AddRevealLayer(art != null ? art.gameObject : null, 0f, .25f, 0f);
        AddRevealLayer(title != null ? title.gameObject : null, .1f, .22f, 12f);
        AddRevealLayer(titleRule, .1f, .22f, 0f);
        AddRevealLayer(_resultSummaryRoot, .23f, .22f, 8f);
        AddRevealLayer(actionButton != null ? actionButton.gameObject : null, .4f, .2f, 0f);
        PrepareRewardTransfer();
        _revealPrepared = true;
        ApplyResultReveal(0f);
    }

    public IEnumerator PlayResultReveal()
    {
        if (!_revealPrepared) PrepareResultReveal();
        uint version = _revealVersion;
        float elapsed = 0f;
        while (elapsed < .6f)
        {
            if (!ResultRevealIsCurrent(version)) yield break;
            ApplyResultReveal(elapsed);
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }
        if (!ResultRevealIsCurrent(version)) yield break;
        ApplyResultReveal(.6f);

        if (_rewardTransferPrepared)
        {
            // Show every label and the full reward before the numbers start moving.
            elapsed = 0f;
            while (elapsed < RewardReadSeconds)
            {
                yield return null;
                if (!ResultRevealIsCurrent(version)) yield break;
                elapsed += Time.unscaledDeltaTime;
            }
            IsRewardTransferPlaying = true;
            elapsed = 0f;
            while (elapsed < RewardTransferSeconds)
            {
                if (!ResultRevealIsCurrent(version)) yield break;
                ApplyRewardTransfer(Mathf.SmoothStep(0f, 1f, elapsed / RewardTransferSeconds));
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
            if (!ResultRevealIsCurrent(version)) yield break;
            ApplyRewardTransfer(1f);
        }
        IsRewardTransferPlaying = false;
        _rewardTransferPrepared = false;
        _revealPrepared = false;
        ResultRevealCompleted = true;
    }

    private bool ResultRevealIsCurrent(uint version) => version == _revealVersion && isActiveAndEnabled
        && modalRoot != null && modalRoot.activeInHierarchy;

    private void PrepareRewardTransfer()
    {
        if (!_hasResultSummary || !_resultSummary.Balance.HasValue || _resultSummary.Balance.Value < 0
            || _resultSummary.StageReward < 0 || _resultSummary.DeathReward < 0 || _resultSummary.GoldConversionReward < 0) return;
        long finalBalance = _resultSummary.Balance.Value;
        // Invalid/unknown snapshots stay readable; never invent a balance or overflow a large reward.
        if (_resultSummary.StageReward > finalBalance
            || _resultSummary.DeathReward > finalBalance - _resultSummary.StageReward
            || _resultSummary.GoldConversionReward > finalBalance - _resultSummary.StageReward - _resultSummary.DeathReward
            || (_resultSummary.StageReward == 0 && _resultSummary.DeathReward == 0 && _resultSummary.GoldConversionReward == 0)) return;
        _rewardTransferPrepared = true;
        ApplyRewardTransfer(0f);
    }

    private void ApplyRewardTransfer(float progress)
    {
        // Only presentation changes: settlement has already persisted these rewards exactly once.
        // Use integer remainders so the displayed total stays constant even for large balances.
        long stage = progress >= 1f ? 0L : _resultSummary.StageReward
            - (long)System.Math.Floor(_resultSummary.StageReward * (double)progress);
        long death = progress >= 1f ? 0L : _resultSummary.DeathReward
            - (long)System.Math.Floor(_resultSummary.DeathReward * (double)progress);
        _stageReward.Amount.text = FormatAmount(stage);
        _deathReward.Amount.text = FormatAmount(death);
        long conversion = progress >= 1f ? 0L : _resultSummary.GoldConversionReward
            - (long)System.Math.Floor(_resultSummary.GoldConversionReward * (double)progress);
        _goldConversion.Amount.text = FormatAmount(conversion);
        _balance.Amount.text = FormatAmount(_resultSummary.Balance.Value - stage - death - conversion);
    }

    public void CancelResultReveal()
    {
        ++_revealVersion;
        ResultRevealCompleted = false;
        IsRewardTransferPlaying = false;
        _rewardTransferPrepared = false;
        _revealPrepared = false;
        if (_hasResultSummary) ApplyResultSummary();
        foreach (var layer in _revealLayers)
        {
            if (layer.Group != null) layer.Group.alpha = 1f;
            if (layer.Rect != null) layer.Rect.anchoredPosition = layer.Position;
        }
        _revealLayers.Clear();
    }

    private void AddRevealLayer(GameObject target, float start, float duration, float rise)
    {
        if (target == null) return;
        // Fade-only layers must not restore geometry: UIButtonMotion can recenter
        // the action's pivot when the hidden modal is first activated.
        var rect = rise != 0f ? target.transform as RectTransform : null;
        var group = target.GetComponent<CanvasGroup>();
        if (group == null) group = target.AddComponent<CanvasGroup>();
        _revealLayers.Add(new RevealLayer
        {
            Group = group,
            Rect = rect,
            Position = rect != null ? rect.anchoredPosition : Vector2.zero,
            Start = start,
            Duration = duration,
            Rise = rise
        });
    }

    private void ApplyResultReveal(float elapsed)
    {
        foreach (var layer in _revealLayers)
        {
            float progress = Mathf.Clamp01((elapsed - layer.Start) / layer.Duration);
            float ease = 1f - Mathf.Pow(1f - progress, 3f);
            if (layer.Group != null) layer.Group.alpha = ease;
            if (layer.Rect != null) layer.Rect.anchoredPosition = layer.Position + Vector2.down * (layer.Rise * (1f - ease));
        }
    }

    public void Configure(GameObject modal, GameObject loading, GameObject clear, GameObject death,
        GameObject rule, TMP_Text heading, TMP_Text body, TMP_Text stage, UnityEngine.UI.Button action,
        RectTransform rotatingLayer, UnityEngine.UI.Image pulseLayer, RectTransform activitySignal)
    {
        modalRoot = modal; loadingRoot = loading; clearArt = clear; deathArt = death; titleRule = rule;
        title = heading; details = body; stageLabel = stage; actionButton = action;
        rotor = rotatingLayer; pulse = pulseLayer; activity = activitySignal;
    }

    public void ConfigureTypography(TMP_FontAsset body, TMP_FontAsset koreanTitle, TMP_FontAsset numeric)
    {
        bodyFont = body; titleKorean = koreanTitle; titleLatin = numeric;
        SetFont(details, bodyFont);
        SetFont(stageLabel, bodyFont);
        if (actionButton != null)
            SetFont(actionButton.GetComponentInChildren<TMP_Text>(true), bodyFont);
    }

    public void ShowLoading()
    {
        SetMode(true, false, false);
        SetResultLayout(false);
        SetTitleFont(bodyFont, FontStyles.Bold);
        title.text = "이동 중...";
        details.text = string.Empty;
        details.gameObject.SetActive(false);
        stageLabel.gameObject.SetActive(false);
        details.richText = false;
        Place(title.rectTransform, 160, 1168, 1100, 60);
        title.fontSize = 36;
        Place(details.rectTransform, 160, 1238, 1450, 84);
        details.fontSize = 27;
        details.alignment = TextAlignmentOptions.TopLeft;
    }

    public void ShowResult(bool won)
    {
        SetMode(false, won, !won);
        EnsureResultSummary();
        SetResultLayout(true);
        SetTitleFont(titleKorean, FontStyles.Normal);
        Place(title.rectTransform, 1372, 365, 1000, 125);
        title.fontSize = 78;
    }

    public void ShowError()
    {
        SetMode(false, false, true);
        SetResultLayout(false);
        SetTitleFont(bodyFont, FontStyles.Bold);
        details.richText = false;
        Place(title.rectTransform, 1372, 365, 1000, 125);
        title.fontSize = 68;
        Place(details.rectTransform, 1380, 620, 950, 330);
        details.fontSize = 27;
        details.alignment = TextAlignmentOptions.TopLeft;
    }

    public void SetResultSummary(RunResultSummary summary)
    {
        CancelResultReveal();
        EnsureResultSummary();
        _resultSummary = summary;
        _hasResultSummary = true;
        ApplyResultSummary();
    }

    private void ApplyResultSummary()
    {
        var summary = _resultSummary;
        string currency = string.IsNullOrWhiteSpace(summary.CurrencyName) ? "강화석" : summary.CurrencyName;
        _resultProgress.text = summary.ProgressLabel ?? string.Empty;
        _clearedRooms.text = $"클리어한 전투 방 {FormatAmount(summary.ClearedRooms)}개";
        SetResultValue(_stageReward, summary.StageReward, currency);
        SetResultValue(_deathReward, summary.DeathReward, currency);
        SetResultValue(_goldConversion, summary.GoldConversionReward, currency);
        _conversionSource.text = $"{FormatAmount(summary.GoldBeforeConversion)} 금화";
        SetResultValue(_balance, summary.Balance, currency);
    }

    private void EnsureResultSummary()
    {
        if (_resultSummaryRoot != null) return;

        var root = CreateRect("ResultsSummary", details.rectTransform.parent, 1380, 550, 950, 470);
        _resultSummaryRoot = root.gameObject;
        _resultSummaryRoot.SetActive(false);
        var primary = new Color(.93f, .95f, .98f, 1f);
        var secondary = new Color(.68f, .75f, .81f, 1f);

        _resultProgress = CreateSummaryLabel("Progress", root, 0, 0, 950, 64, 48, 32, secondary, TextAlignmentOptions.Left);
        _stageReward = CreateResultRow(root, "StageReward", "스테이지 보상", 72, primary, secondary);
        _deathReward = CreateResultRow(root, "DeathReward", "사망 보상", 148, primary, secondary);
        _goldConversion = CreateResultRow(root, "GoldConversion", "금화 환전", 224, primary, secondary);
        _clearedRooms = CreateSummaryLabel("ClearedRooms", root, 0, 308, 570, 42, 34, 25, secondary, TextAlignmentOptions.Left);
        _conversionSource = CreateSummaryLabel("ConversionSource", root, 590, 308, 360, 42, 30, 22, secondary, TextAlignmentOptions.Right);

        var rule = CreateRect("BalanceRule", root, 0, 366, 950, 2).gameObject.AddComponent<UnityEngine.UI.Image>();
        rule.color = new Color(.68f, .75f, .81f, .3f);
        rule.raycastTarget = false;
        _balance = CreateResultRow(root, "Balance", "현재 보유량", 386, primary, secondary);
    }

    private ResultValue CreateResultRow(RectTransform root, string name, string heading, float y, Color primary, Color secondary)
    {
        var label = CreateSummaryLabel(name + "Label", root, 0, y, 356, 74, 48, 34, primary, TextAlignmentOptions.Left);
        label.text = heading;
        var amount = CreateSummaryLabel(name + "Value", root, 378, y, 408, 74, 56, 24, primary, TextAlignmentOptions.Right);
        SetFont(amount, IntroFont);
        var currency = CreateSummaryLabel(name + "Currency", root, 800, y, 150, 74, 34, 22, secondary, TextAlignmentOptions.Left);
        return new ResultValue(amount, currency);
    }

    private TMP_Text CreateSummaryLabel(string name, Transform parent, float x, float y, float width, float height,
        float fontSize, float minFontSize, Color color, TextAlignmentOptions alignment)
    {
        var label = CreateRect(name, parent, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
        SetFont(label, bodyFont != null ? bodyFont : details.font);
        label.color = color;
        label.alignment = alignment;
        label.fontSize = fontSize;
        label.fontSizeMin = minFontSize;
        label.fontSizeMax = fontSize;
        label.enableAutoSizing = true;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.richText = false;
        label.raycastTarget = false;
        return label;
    }

    private void SetResultValue(ResultValue row, long? amount, string currency)
    {
        var font = amount.HasValue ? IntroFont : bodyFont;
        SetFont(row.Amount, font != null ? font : details.font);
        row.Amount.fontSizeMax = amount.HasValue ? 56 : 42;
        row.Amount.text = amount.HasValue ? FormatAmount(amount.Value) : "확인 필요";
        row.Currency.text = amount.HasValue ? currency : string.Empty;
    }

    private static string FormatAmount(long amount) => amount.ToString("N0", CultureInfo.InvariantCulture);

    private void SetResultLayout(bool result)
    {
        if (_resultSummaryRoot != null) _resultSummaryRoot.SetActive(result);
        details.gameObject.SetActive(!result);
        var label = actionButton != null ? actionButton.GetComponentInChildren<TMP_Text>(true) : null;
        if (label != null) label.fontSize = result ? 42 : 32;
    }

    /// <summary>Styles the existing reward values while keeping status and Korean labels in the body font.</summary>
    public string FormatResultDetails(string progress, string rewardSummary)
    {
        var formatted = new StringBuilder(EscapeLiteral(progress));
        if (string.IsNullOrEmpty(rewardSummary)) return formatted.ToString();
        formatted.Append('\n');
        if (titleLatin == null)
            return formatted.Append(EscapeLiteral(rewardSummary)).ToString();

        // TMP resolves <font> tags by the asset name through this registry, also in player builds.
        MaterialReferenceManager.AddFontAsset(titleLatin);
        int offset = 0;
        foreach (Match value in RewardValues.Matches(rewardSummary))
        {
            formatted.Append(EscapeLiteral(rewardSummary.Substring(offset, value.Index - offset)));
            formatted.Append("<font=\"").Append(titleLatin.name).Append("\">");
            formatted.Append(value.Value).Append("</font>");
            offset = value.Index + value.Length;
        }
        return formatted.Append(EscapeLiteral(rewardSummary.Substring(offset))).ToString();
    }

    private void SetTitleFont(TMP_FontAsset font, FontStyles style)
    {
        SetFont(title, font);
        if (title == null) return;
        title.fontStyle = style;
        title.richText = false;
    }

    private static void SetFont(TMP_Text label, TMP_FontAsset font)
    {
        if (label == null || font == null) return;
        label.font = font;
        label.fontSharedMaterial = font.material;
    }

    // Encode each opening bracket separately so even a literal </noparse> cannot escape its protection.
    private static string EscapeLiteral(string text) => (text ?? string.Empty).Replace("<", "<noparse><</noparse>");

    public void Hide()
    {
        CancelResultReveal();
        if (modalRoot != null) modalRoot.SetActive(false);
        SetCanvasOrder(100);
    }

    private Color? _nonLoadingBackground;

    private void SetMode(bool loading, bool clear, bool death)
    {
        CancelResultReveal();
        SetCanvasOrder(200);
        var background = modalRoot != null ? modalRoot.GetComponent<UnityEngine.UI.Image>() : null;
        if (background != null)
        {
            if (!_nonLoadingBackground.HasValue) _nonLoadingBackground = background.color;
            Color tint = _nonLoadingBackground.Value;
            if (loading) tint.a = 1f;
            background.color = tint;
        }
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

    private void OnDisable() => CancelResultReveal();

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

    private static RectTransform CreateRect(string name, Transform parent, float x, float y, float width, float height)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Place(rect, x, y, width, height);
        return rect;
    }
}
