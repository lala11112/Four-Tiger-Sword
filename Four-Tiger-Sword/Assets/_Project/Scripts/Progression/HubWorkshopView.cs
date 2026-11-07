using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>A selectable workshop backed by the existing permanent-upgrade account.</summary>
public sealed class HubWorkshopView : MonoBehaviour
{
    [Serializable]
    public sealed class References
    {
        public GameObject panel;
        public TMP_Text balance, selectedTitle, currentLevel, nextLevel, currentBonus, nextBonus;
        public TMP_Text gain, balanceAfter, cost, status, purchaseLabel;
        public UnityEngine.UI.Image selectedIcon;
        public UnityEngine.UI.Button purchaseButton, closeButton;
        public UnityEngine.UI.ScrollRect statusScroll;
    }

    [Serializable]
    public sealed class Entry
    {
        public StatType stat;
        public UnityEngine.UI.Button selectButton;
        public TMP_Text title, level, bonus;
        public GameObject selectedIndicator;
        public Sprite icon;
        [NonSerialized] internal UnityEngine.Events.UnityAction click;
    }

    [SerializeField] private References references;
    [SerializeField] private Entry[] entries;
    private PlayerUpgradeService _service;
    private bool _listenersInitialized, _purchasing, _messageIsError;
    private bool _resetStatusScrollPending;
    private string _message;
    private UIModalMotion _motion;
    private CanvasGroup _interaction;
    private bool _closing;
    private int _motionVersion;
    public event Action CloseRequested;
    public bool IsOpen => isActiveAndEnabled && references?.panel != null && references.panel.activeInHierarchy;
    public bool IsInteractionReady => IsOpen && !_closing && (_motion == null || (!_motion.IsAnimating && _motion.IsShown));
    public StatType SelectedStat { get; private set; } = StatType.ST_HP;
    public References UI => references;
    public Entry[] Entries => entries;

    public void Configure(References ui, Entry[] rows)
    {
        RemoveListeners();
        references = ui;
        entries = rows;
        InitializeListeners();
        Refresh();
    }

    private void Awake() => InitializeListeners();

    private void InitializeListeners()
    {
        if (_listenersInitialized || references == null || entries == null) return;
        _listenersInitialized = true;
        references.purchaseButton?.onClick.AddListener(Purchase);
        references.closeButton?.onClick.AddListener(RequestClose);
        foreach (var entry in entries)
        {
            if (entry?.selectButton == null) continue;
            var selected = entry;
            entry.click = () => Select(selected.stat);
            entry.selectButton.onClick.AddListener(entry.click);
        }
    }

    private void RemoveListeners()
    {
        if (!_listenersInitialized) return;
        references.purchaseButton?.onClick.RemoveListener(Purchase);
        references.closeButton?.onClick.RemoveListener(RequestClose);
        foreach (var entry in entries)
        {
            if (entry?.selectButton == null || entry.click == null) continue;
            entry.selectButton.onClick.RemoveListener(entry.click);
            entry.click = null;
        }
        _listenersInitialized = false;
    }

    public void Bind(PlayerUpgradeService service)
    {
        if (_service != null) _service.Changed -= Refresh;
        _service = service;
        if (_service != null) _service.Changed += Refresh;
        _message = null;
        Refresh();
    }

    public void Open()
    {
        InitializeListeners();
        if (references?.panel == null) return;
        references.panel.SetActive(true);
        EnsureMotion();
        _closing = false;
        int version = ++_motionVersion;
        _resetStatusScrollPending = true;
        Refresh();
        SetInteraction(false);
        _motion.PlayShow(() =>
        {
            if (!IsOpen || _closing || version != _motionVersion) return;
            SetInteraction(true);
            if (EventSystem.current != null && entries != null)
                foreach (var entry in entries)
                    if (entry != null && entry.stat == SelectedStat && entry.selectButton != null)
                    { EventSystem.current.SetSelectedGameObject(entry.selectButton.gameObject); break; }
        });
    }

    public void Close()
    {
        if (references?.panel == null) return;
        _motionVersion++;
        _closing = false;
        _motion?.HideImmediate();
        SetInteraction(false);
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected != null && selected.transform.IsChildOf(references.panel.transform))
            EventSystem.current.SetSelectedGameObject(null);
        references.panel.SetActive(false);
    }

    public void PlayClose(Action completed)
    {
        if (!IsOpen) { completed?.Invoke(); return; }
        if (_closing) return;
        EnsureMotion();
        _closing = true;
        int version = ++_motionVersion;
        SetInteraction(false);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        _motion.PlayClose(() =>
        {
            if (_closing && version == _motionVersion) completed?.Invoke();
        });
    }

    private void EnsureMotion()
    {
        if (_motion != null || references?.panel == null) return;
        _interaction = references.panel.GetComponent<CanvasGroup>();
        if (_interaction == null) _interaction = references.panel.AddComponent<CanvasGroup>();
        var visual = references.panel.transform.Find("Window") as RectTransform;
        if (visual == null) visual = references.panel.transform as RectTransform;
        Canvas.ForceUpdateCanvases();
        _motion = UIModalMotion.Ensure(references.panel, visual);
        UIButtonMotion.Install(references.panel);
    }

    private void SetInteraction(bool enabled)
    {
        if (_interaction == null) return;
        _interaction.interactable = enabled;
        _interaction.blocksRaycasts = IsOpen;
    }

    public void Select(StatType stat)
    {
        if (!Supported(stat) || _purchasing || (IsOpen && !IsInteractionReady)) return;
        if (SelectedStat != stat) _resetStatusScrollPending = true;
        SelectedStat = stat;
        _message = null;
        Refresh();
    }

    public void Refresh()
    {
        if (references == null) return;
        var settings = _service?.Settings;
        string validationError = null;
        bool ready = _service != null && _service.IsReady && settings != null && settings.Validate(out validationError);
        string currency = settings != null && !string.IsNullOrWhiteSpace(settings.currencyName) ? settings.currencyName : "재화";
        Set(references.balance, ready ? $"{currency}  {_service.Balance:N0}" : currency + "  —");
        Entry selected = null;
        if (entries != null)
            foreach (var entry in entries)
            {
                if (entry == null) continue;
                bool supported = Supported(entry.stat);
                Set(entry.title, Title(entry.stat));
                Set(entry.level, ready && supported ? $"Lv. {_service.GetLevel(entry.stat):00} / {settings.maxLevel:00}" : "Lv. —");
                Set(entry.bonus, ready && supported ? Bonus(_service.GetBonusRatio(entry.stat)) : "—");
                if (entry.selectButton != null) entry.selectButton.interactable = supported && !_purchasing;
                if (entry.selectedIndicator != null) entry.selectedIndicator.SetActive(entry.stat == SelectedStat);
                if (entry.stat == SelectedStat) selected = entry;
            }
        Set(references.selectedTitle, Title(SelectedStat));
        if (references.selectedIcon != null)
        {
            references.selectedIcon.sprite = selected?.icon;
            references.selectedIcon.enabled = selected?.icon != null;
        }
        int level = ready ? _service.GetLevel(SelectedStat) : 0;
        bool capped = ready && level >= settings.maxLevel;
        int cost = ready && !capped ? _service.GetNextCost(SelectedStat) : 0;
        float gain = !ready ? 0f : SelectedStat == StatType.ST_HP ? settings.healthPerLevel
            : SelectedStat == StatType.ST_ATK ? settings.attackPerLevel : settings.defensePerLevel;
        bool affordable = ready && !capped && _service.Balance >= cost;
        Set(references.currentLevel, ready ? $"Lv. {level:00}" : "—");
        Set(references.nextLevel, !ready ? "—" : capped ? "최대 단계" : $"Lv. {level + 1:00}");
        Set(references.currentBonus, ready ? Bonus(_service.GetBonusRatio(SelectedStat)) : "—");
        Set(references.nextBonus, ready && !capped ? Bonus((level + 1) * gain) : "—");
        Set(references.gain, ready ? $"단계마다 기본 능력치 {Bonus(gain)}" : "강화 정보를 불러올 수 없습니다.");
        Set(references.balanceAfter, affordable ? $"{currency}  {_service.Balance - cost:N0}" : "—");
        Set(references.cost, !ready ? "—" : capped ? "강화 완료" : $"{currency}  {cost:N0}");
        if (references.purchaseButton != null) references.purchaseButton.interactable = affordable && !_purchasing;
        if (references.closeButton != null) references.closeButton.interactable = !_purchasing;
        Set(references.purchaseLabel, !ready ? "이용 불가" : capped ? "최대 단계" : !affordable ? currency + " 부족" : "강화하기");
        string status = !ready ? validationError ?? _service?.LastError ?? "강화 데이터를 불러올 수 없습니다."
            : _message ?? _service.LastError ?? (capped ? "최대 강화 단계에 도달했습니다."
                : !affordable ? "보유한 강화 재화가 부족합니다." : "강화는 확정 적용되며 자동으로 저장됩니다.");
        if (references.status != null && !string.Equals(references.status.text, status, StringComparison.Ordinal))
            _resetStatusScrollPending = true;
        Set(references.status, status);
        if (references.status != null)
            references.status.color = !ready || (_message != null ? _messageIsError : !string.IsNullOrEmpty(_service.LastError))
                ? new Color32(247, 151, 151, 255) : _message != null ? new Color32(159, 215, 194, 255)
                : new Color32(143, 163, 183, 255);
    }

    private void LateUpdate()
    {
        if (!_resetStatusScrollPending || !IsOpen) return;
        var scroll = references.statusScroll;
        if (scroll == null) { _resetStatusScrollPending = false; return; }
        if (!scroll.isActiveAndEnabled || scroll.content == null) return;
        // Text wrapping depends on the final viewport width. Flush the deferred layout before
        // resetting the offset so long status messages start at their first line, even while paused.
        Canvas.ForceUpdateCanvases();
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        scroll.StopMovement();
        scroll.verticalNormalizedPosition = 1f;
        _resetStatusScrollPending = false;
    }

    private void Purchase()
    {
        if (_purchasing || !IsInteractionReady || _service == null) return;
        _purchasing = true;
        Refresh();
        try
        {
            bool success = _service.TryUpgrade(SelectedStat, out string error);
            _messageIsError = !success;
            _message = success ? $"{Title(SelectedStat)} 강화 완료 · Lv. {_service.GetLevel(SelectedStat):00} · 저장되었습니다."
                : error ?? "강화를 저장하지 못했습니다.";
        }
        finally { _purchasing = false; Refresh(); }
    }

    private void RequestClose() { if (IsOpen && !_closing && !_purchasing) CloseRequested?.Invoke(); }
    private static void Set(TMP_Text text, string value) { if (text != null) text.text = value; }
    private static string Bonus(float ratio) => $"+{ratio * 100f:0.#}%";
    private static bool Supported(StatType stat) => stat == StatType.ST_HP || stat == StatType.ST_ATK || stat == StatType.ST_DEF;
    private static string Title(StatType stat) => stat == StatType.ST_HP ? "최대 체력"
        : stat == StatType.ST_ATK ? "공격력" : stat == StatType.ST_DEF ? "방어력" : "—";

    private void OnDestroy()
    {
        if (_service != null) _service.Changed -= Refresh;
        RemoveListeners();
    }

    private void OnDisable() => Close();
}
