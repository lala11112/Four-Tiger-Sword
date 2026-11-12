using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

namespace BoundaryZero.SupplyDepot
{
    [DisallowMultipleComponent]
    public sealed class SupplyDepotView : MonoBehaviour
    {
        private const int CardCount = 31;
        private const int StartIndex = 3;
        private const int WinnerIndex = 27;
        private const float CardPitch = 172f;

        [Serializable]
        public sealed class ReelCard
        {
            public RectTransform root;
            public CanvasGroup opacity;
            public UnityEngine.UI.Image background;
            public UnityEngine.UI.Image art;
            public UnityEngine.UI.Image accent;
            public TMP_Text mystery;
            public TMP_Text itemName;
            public TMP_Text stars;
        }

        [Serializable]
        public sealed class SynergyRow
        {
            public RectTransform root;
            public UnityEngine.UI.Image icon;
            public TMP_Text synergyName;
            public TMP_Text thresholds;
            public RectTransform lines;
            public TMP_Text lineTemplate;
        }

        [Serializable]
        public sealed class Elements
        {
            public GameObject window;
            public UnityEngine.UI.Image frame;
            public UnityEngine.UI.Image[] currencyIcons;
            public GameObject reopenContainer;
            public UnityEngine.UI.Button draw;
            public UnityEngine.UI.Button skip;
            public UnityEngine.UI.Button close;
            public UnityEngine.UI.Button reopen;
            public TMP_Text balance;
            public TMP_Text price;
            public TMP_Text drawLabel;
            public TMP_Text stageLabel;
            public UnityEngine.UI.Image drawSlash;
            public UnityEngine.UI.Image machineLight;
            public UnityEngine.UI.Image[] selectorMarks;
            public UnityEngine.UI.Image resultTopBorder;
            public UnityEngine.UI.Image resultAccent;
            public RectTransform reel;
            public ReelCard cardTemplate;
            public GameObject empty;
            public GameObject filled;
            public CanvasGroup resultFade;
            public UnityEngine.UI.Image resultArt;
            public TMP_Text resultName;
            public TMP_Text resultStars;
            public TMP_Text effect;
            public UnityEngine.UI.ScrollRect details;
            public GameObject synergySection;
            public RectTransform synergyList;
            public SynergyRow synergyTemplate;
        }

        [SerializeField] private Elements elements = new Elements();
        [SerializeField] private bool showReopenButton;

        private static readonly CultureInfo NumberFormat = CultureInfo.GetCultureInfo("ko-KR");
        private static readonly Regex TierValues = new Regex(
            @"[+-]?\(\d+(?:\.\d+)?(?:\s*/\s*\d+(?:\.\d+)?)+\)(?:%p?|초|회|개|배)?");
        private static readonly Color Red = new Color32(237, 45, 77, 255);
        private static readonly Color Mint = new Color32(150, 218, 203, 255);
        private static readonly Color Selector = new Color32(197, 214, 229, 210);
        private static readonly Color Quiet = new Color32(113, 134, 154, 255);
        private static readonly Color ActiveCard = new Color32(32, 48, 65, 255);
        private static readonly Color InactiveCard = new Color32(20, 32, 47, 255);
        private static readonly Color WinnerCard = new Color32(37, 64, 73, 255);

        private readonly List<ReelCard> cards = new List<ReelCard>(CardCount);
        private readonly List<GameObject> synergyRows = new List<GameObject>();
        private readonly System.Random reelRandom = new System.Random();
        private SupplyDepotCatalog catalog;
        private SupplyDepotItem displayedResult;
        private int activeIndex = -1;
        private float resultRevealTime = 1f;
        private bool initialized;

        public event Action DrawRequested;
        public event Action SkipRequested;
        public event Action CloseRequested;
        public event Action ReopenRequested;

#if UNITY_EDITOR
        public void AssignEditorElements(Elements references) => elements = references;
        public void EnableEditorDemoReopen() => showReopenButton = true;
#endif

        private void Awake() => InitializeButtons();

        private void OnDestroy()
        {
            if (!initialized)
                return;

            elements.draw.onClick.RemoveListener(RequestDraw);
            elements.skip.onClick.RemoveListener(RequestSkip);
            elements.close.onClick.RemoveListener(RequestClose);
            elements.reopen.onClick.RemoveListener(RequestReopen);
        }

        private void Update()
        {
            if (resultRevealTime >= 1f)
                return;

            resultRevealTime = Mathf.MoveTowards(resultRevealTime, 1f, Time.unscaledDeltaTime / 0.32f);
            elements.resultFade.alpha = 1f - Mathf.Pow(1f - resultRevealTime, 3f);
        }

        public void Configure(SupplyDepotCatalog source)
        {
            InitializeButtons();
            catalog = source;
            if (catalog == null)
                return;

            elements.price.text = catalog.price.ToString("N0", NumberFormat);
            if (catalog.frameSprite != null)
                elements.frame.sprite = catalog.frameSprite;
            if (catalog.currencyIcon != null)
                foreach (var icon in elements.currencyIcons)
                    icon.sprite = catalog.currencyIcon;
            if (catalog.font != null)
                foreach (var label in GetComponentsInChildren<TMP_Text>(true))
                    label.font = catalog.font;
        }

        public void SetOpen(bool open)
        {
            elements.window.SetActive(open);
            elements.reopenContainer.SetActive(!open && showReopenButton);
        }

        public void Render(int balance, bool canDraw, bool drawing, SupplyDepotItem result)
        {
            bool hasResult = result != null && !drawing;
            elements.balance.text = balance.ToString("N0", NumberFormat);
            elements.draw.interactable = canDraw && !drawing && !hasResult;
            elements.drawLabel.text = drawing ? "추첨 중" :
                hasResult ? "보급 완료" : canDraw ? "보급 받기" : "재화 부족";
            elements.stageLabel.text = drawing ? "보급품 추첨 중" : hasResult ? "보급품 확정" : "무작위 보급";
            elements.drawLabel.color = elements.draw.interactable ? Color.white : Quiet;
            elements.drawSlash.color = elements.draw.interactable
                ? new Color32(241, 139, 158, 255) : new Color32(83, 106, 129, 255);
            elements.close.interactable = !drawing;
            elements.skip.gameObject.SetActive(drawing);
            elements.machineLight.color = drawing ? Red : Quiet;
            elements.resultTopBorder.color = hasResult ? Mint : Selector;
            elements.resultAccent.color = hasResult ? Mint : Red;
            for (int i = 0; i < elements.selectorMarks.Length; i++)
                elements.selectorMarks[i].color = i < 2 ? (hasResult ? Mint : Red) : (hasResult ? Mint : Selector);

            elements.empty.SetActive(!hasResult);
            elements.filled.SetActive(hasResult);
            SupplyDepotItem nextResult = hasResult ? result : null;
            if (displayedResult != nextResult)
            {
                displayedResult = nextResult;
                if (displayedResult != null)
                    SetDetails(displayedResult);
            }
            if (activeIndex >= 0)
                cards[activeIndex].background.color = hasResult ? WinnerCard : ActiveCard;
        }

        public void PrepareReel(SupplyDepotItem winner)
        {
            if (catalog == null || catalog.items == null || catalog.items.Count == 0)
                return;

            if (cards.Count == 0)
            {
                for (int i = 0; i < CardCount; i++)
                    cards.Add(CreateCard(i));
            }

            for (int i = 0; i < CardCount; i++)
            {
                var item = i == StartIndex ? null :
                    i == WinnerIndex && winner != null ? winner :
                    catalog.items[reelRandom.Next(catalog.items.Count)];
                BindCard(cards[i], item);
            }

            activeIndex = -1;
            SetReelPosition(StartIndex);
        }

        public void SetReelPosition(float index)
        {
            if (cards.Count == 0)
                return;

            index = Mathf.Clamp(index, 0f, cards.Count - 1);
            elements.reel.anchoredPosition = new Vector2(-index * CardPitch, 0f);
            int nearest = Mathf.RoundToInt(index);
            if (nearest == activeIndex)
                return;

            if (activeIndex >= 0)
                SetCardActive(cards[activeIndex], false);
            activeIndex = nearest;
            SetCardActive(cards[activeIndex], true);
        }

        public void ShowRestoredResult(SupplyDepotItem result)
        {
            PrepareReel(result);
            if (result != null)
                SetReelPosition(WinnerIndex);
        }

        private void InitializeButtons()
        {
            if (initialized)
                return;

            elements.draw.onClick.AddListener(RequestDraw);
            elements.skip.onClick.AddListener(RequestSkip);
            elements.close.onClick.AddListener(RequestClose);
            elements.reopen.onClick.AddListener(RequestReopen);
            initialized = true;
        }

        private void RequestDraw() => DrawRequested?.Invoke();
        private void RequestSkip() => SkipRequested?.Invoke();
        private void RequestClose() => CloseRequested?.Invoke();
        private void RequestReopen() => ReopenRequested?.Invoke();

        private ReelCard CreateCard(int index)
        {
            var card = Instantiate(elements.cardTemplate.root, elements.reel, false);
            card.name = $"Card {index + 1}";
            card.anchoredPosition = new Vector2(index * CardPitch, 0f);
            card.gameObject.SetActive(true);
            return new ReelCard
            {
                root = card,
                opacity = card.GetComponent<CanvasGroup>(),
                background = card.GetComponent<UnityEngine.UI.Image>(),
                art = card.Find("Art").GetComponent<UnityEngine.UI.Image>(),
                accent = card.Find("Accent").GetComponent<UnityEngine.UI.Image>(),
                mystery = card.Find("Mystery").GetComponent<TMP_Text>(),
                itemName = card.Find("Name").GetComponent<TMP_Text>(),
                stars = card.Find("Stars").GetComponent<TMP_Text>(),
            };
        }

        private static void BindCard(ReelCard card, SupplyDepotItem item)
        {
            bool hasItem = item != null;
            card.art.sprite = item?.icon;
            card.art.enabled = hasItem && item.icon != null;
            card.mystery.enabled = !hasItem;
            card.itemName.text = hasItem ? item.name : "무작위 아이템";
            card.stars.text = hasItem ? Stars(item.grade) : string.Empty;
            card.stars.color = hasItem ? item.color : Color.white;
            card.accent.color = hasItem ? item.color : Selector;
            SetCardActive(card, false);
        }

        private static string Stars(int grade) => new string('★', Mathf.Clamp(grade, 0, 4));

        private static void SetCardActive(ReelCard card, bool active)
        {
            card.opacity.alpha = active ? 1f : 0.46f;
            card.background.color = active ? ActiveCard : InactiveCard;
            card.art.color = active ? Color.white : new Color32(189, 203, 220, 255);
        }

        private void SetDetails(SupplyDepotItem item)
        {
            elements.resultArt.sprite = item.icon;
            elements.resultArt.enabled = item.icon != null;
            elements.resultName.text = item.name;
            elements.resultStars.text = Stars(item.grade);
            elements.resultStars.color = item.color;
            elements.effect.text = item.effect;
            foreach (var row in synergyRows)
            {
                row.SetActive(false);
                Destroy(row);
            }
            synergyRows.Clear();

            if (item.synergies != null)
                foreach (var synergy in item.synergies)
                    AddSynergy(synergy);
            elements.synergySection.SetActive(synergyRows.Count > 0);
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(elements.details.content);
            elements.details.StopMovement();
            elements.details.verticalNormalizedPosition = 1f;
            resultRevealTime = 0f;
            elements.resultFade.alpha = 0f;
        }

        private void AddSynergy(SupplyDepotSynergy synergy)
        {
            var row = Instantiate(elements.synergyTemplate.root, elements.synergyList, false);
            row.name = synergy.name;
            var header = row.Find("Header");
            var icon = header.Find("Icon").GetComponent<UnityEngine.UI.Image>();
            icon.sprite = synergy.icon;
            icon.enabled = synergy.icon != null;
            header.Find("Name").GetComponent<TMP_Text>().text = synergy.name;
            row.Find("Thresholds").GetComponent<TMP_Text>().text = synergy.thresholds == null ||
                synergy.thresholds.Length == 0 ? string.Empty : string.Join(" / ", synergy.thresholds) + "세트";
            var lines = row.Find("Lines");
            var template = lines.Find("LineTemplate").GetComponent<TMP_Text>();
            if (synergy.compactLines != null)
                foreach (string effect in synergy.compactLines)
                {
                    var line = Instantiate(template, lines, false);
                    line.name = "Effect";
                    line.text = TierValues.Replace(effect ?? string.Empty, "<nobr>$&</nobr>");
                    line.gameObject.SetActive(true);
                }
            row.gameObject.SetActive(true);
            synergyRows.Add(row.gameObject);
        }
    }
}


