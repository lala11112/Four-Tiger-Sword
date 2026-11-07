using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Reusable upgrade panel. A Hub menu or a future world facility can bind and open it.</summary>
public sealed class HubUpgradeView : MonoBehaviour
{
    [Serializable]
    public sealed class Card
    {
        public StatType stat;
        public TMP_Text level, value, gain, price, actionLabel;
        public UnityEngine.UI.Button purchase;
        public UnityEngine.UI.Image[] steps;
        public Color accent = Color.white;
    }

    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text balance, message, rewardGuide;
    [SerializeField] private Card[] cards;
    private PlayerUpgradeService _service;
    private bool _purchasing;
    private bool _wasReady;
    public bool IsOpen => panel != null && panel.activeSelf;
    public Card[] Cards => cards;

    public void Configure(GameObject content, TMP_Text currency, TMP_Text feedback, TMP_Text guide, Card[] rows)
    { panel = content; balance = currency; message = feedback; rewardGuide = guide; cards = rows; }

    private void Awake()
    {
        if (cards == null) return;
        foreach (var card in cards)
        {
            var selected = card;
            selected.purchase.onClick.AddListener(() => Purchase(selected.stat));
        }
    }

    public void Bind(PlayerUpgradeService service)
    {
        if (_service != null) _service.Changed -= Refresh;
        _service = service;
        if (_service != null) _service.Changed += Refresh;
        Refresh();
    }

    public void Open()
    {
        panel.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null
            && EventSystem.current.currentSelectedGameObject.transform.IsChildOf(panel.transform))
            EventSystem.current.SetSelectedGameObject(null);
        panel.SetActive(false);
    }

    public void Refresh()
    {
        if (cards == null || balance == null) return;
        bool ready = _service != null && _service.IsReady;
        if (ready && !_wasReady && message != null)
        {
            message.text = "모든 강화는 확정 적용되며 자동으로 저장됩니다.";
            message.color = new Color32(143, 163, 183, 255);
        }
        _wasReady = ready;
        var settings = _service?.Settings;
        string currency = settings != null ? settings.currencyName : "강화석";
        balance.text = ready ? $"{currency}   {_service.Balance:N0}" : currency + "   —";
        if (rewardGuide != null && settings != null)
            rewardGuide.text = $"일반 구역 {settings.stageClearReward}  /  보스 구역 {settings.bossClearReward}  /  사망 시 전투 방당 {settings.deathRewardPerRoom}, 최대 {settings.deathRewardCap}";
        foreach (var card in cards)
        {
            int level = ready ? _service.GetLevel(card.stat) : 0;
            int max = settings != null ? settings.maxLevel : 20;
            bool capped = ready && level >= max;
            int cost = ready && !capped ? _service.GetNextCost(card.stat) : 0;
            float perLevel = settings == null ? .05f : card.stat == StatType.ST_HP ? settings.healthPerLevel
                : card.stat == StatType.ST_ATK ? settings.attackPerLevel : settings.defensePerLevel;
            float original = ready ? _service.GetBaseValue(card.stat) : 0f;
            float current = original * (1f + level * perLevel);
            card.level.text = $"Lv. {level:00} <color=#6E8398>/ {max:00}</color>";
            card.value.text = !ready ? "—" : capped ? Format(current) : $"{Format(current)}  <color=#6E8398>→</color>  {Format(current + original * perLevel)}";
            card.gain.text = capped ? "최대 강화에 도달했습니다" : $"단계마다 기본 능력치 +{perLevel * 100f:0.#}%";
            card.price.text = capped ? "강화 완료" : ready ? $"{currency}  {cost:N0}" : "저장 데이터를 확인해 주세요";
            card.purchase.interactable = ready && !capped && _service.Balance >= cost && !_purchasing;
            card.actionLabel.text = capped ? "최대 단계" : !ready ? "이용 불가" : _service.Balance < cost ? currency + " 부족" : "강화하기";
            if (card.steps != null)
                for (int i = 0; i < card.steps.Length; i++)
                    card.steps[i].color = i < Mathf.CeilToInt(level * card.steps.Length / (float)Mathf.Max(1, max))
                        ? card.accent : new Color32(43, 59, 75, 255);
        }
        if (!ready && message != null) message.text = _service?.LastError ?? "강화 데이터를 불러오고 있습니다.";
    }

    private void Purchase(StatType stat)
    {
        if (_purchasing || !IsOpen || _service == null || !_service.IsReady) return;
        _purchasing = true;
        try
        {
            bool success = _service.TryUpgrade(stat, out string error);
            string label = stat == StatType.ST_HP ? "최대 체력" : stat == StatType.ST_ATK ? "공격력" : "방어력";
            message.text = success ? $"{label} 강화 완료 · Lv. {_service.GetLevel(stat)} · 저장되었습니다." : error;
            message.color = success ? new Color32(159, 215, 194, 255) : new Color32(247, 151, 151, 255);
        }
        finally { _purchasing = false; Refresh(); }
        if (EventSystem.current != null)
        {
            var selected = EventSystem.current.currentSelectedGameObject;
            var button = selected != null ? selected.GetComponent<UnityEngine.UI.Button>() : null;
            if (button != null && !button.IsInteractable())
                foreach (var selectable in GetComponentsInChildren<UnityEngine.UI.Button>())
                    if (selectable.IsInteractable()) { EventSystem.current.SetSelectedGameObject(selectable.gameObject); break; }
        }
    }

    private static string Format(float value) => value.ToString("#,0.#");
    private void OnDestroy() { if (_service != null) _service.Changed -= Refresh; }
}
