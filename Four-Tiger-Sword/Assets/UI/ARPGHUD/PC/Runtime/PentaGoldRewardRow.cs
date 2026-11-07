using System.Globalization;
using TMPro;
using UnityEngine;

namespace ArpgHud.PC
{
    [AddComponentMenu("PENTA/PC HUD/Gold Reward Row")]
    public sealed class PentaGoldRewardRow : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private CanvasGroup group;
        private RectTransform cachedRect;
        private float slideFrom, slideTarget, slideSeconds, entrySeconds;
        private double slideStartedAt;
        public long Amount { get; private set; }
        public bool IsGold { get; private set; } = true;
        public string ItemName { get; private set; }
        private GameObject _coin;
        private UnityEngine.UI.Image _itemIcon;
        private Color _goldColor;
        private bool _iconsReady;
        public float Alpha => group != null ? group.alpha : 0f;
        public bool Visible => gameObject.activeSelf;
        public RectTransform Rect => cachedRect != null ? cachedRect : (cachedRect = transform as RectTransform);
        internal double StartedAt;
        internal double LastMergedAt;

        public void Configure(TMP_Text value, CanvasGroup canvasGroup)
        {
            label = value;
            group = canvasGroup;
            if (group != null) { group.interactable = false; group.blocksRaycasts = false; }
            Hide();
        }

        internal void Show(int amount, double now, float riseDistance, float enterDuration)
        {
            SetContent(true, null, null);
            ShowRow(amount, now, riseDistance, enterDuration);
        }

        internal void ShowItem(string name, Sprite icon, double now, float riseDistance, float enterDuration)
        {
            SetContent(false, name, icon);
            ShowRow(0, now, riseDistance, enterDuration);
        }

        private void SetContent(bool gold, string name, Sprite icon)
        {
            if (!_iconsReady)
            {
                _iconsReady = true;
                _coin = transform.Find("CutGoldCoin")?.gameObject;
                if (label != null) _goldColor = label.color;
                var node = new GameObject("ItemIcon", typeof(RectTransform), typeof(UnityEngine.UI.Image));
                node.transform.SetParent(transform, false);
                var rect = (RectTransform)node.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(25, -9);
                rect.sizeDelta = new Vector2(30, 30);
                _itemIcon = node.GetComponent<UnityEngine.UI.Image>();
                _itemIcon.preserveAspect = true;
                _itemIcon.raycastTarget = false;
            }
            IsGold = gold;
            ItemName = name;
            if (_coin != null) _coin.SetActive(gold);
            _itemIcon.sprite = icon;
            _itemIcon.gameObject.SetActive(!gold && icon != null);
            if (label != null)
            {
                label.richText = false;
                label.color = gold ? _goldColor : new Color32(239, 232, 218, 255);
            }
        }

        private void ShowRow(int amount, double now, float riseDistance, float enterDuration)
        {
            Amount = amount;
            StartedAt = LastMergedAt = slideStartedAt = now;
            slideFrom = enterDuration > 0 ? -riseDistance : 0f;
            slideTarget = 0f;
            slideSeconds = enterDuration;
            entrySeconds = enterDuration;
            if (group != null) { group.interactable = false; group.blocksRaycasts = false; }
            // Reset alpha and transform before activation, including a row recycled in this frame.
            SetAlpha(enterDuration > 0 ? 0f : 1f);
            if (Rect != null)
            {
                Rect.anchorMin = Rect.anchorMax = new Vector2(1f, .5f);
                Rect.pivot = new Vector2(1f, 0f);
                Rect.anchoredPosition = new Vector2(0f, enterDuration > 0 ? -riseDistance : 0f);
            }
            Refresh();
            gameObject.SetActive(true);
        }

        internal void Merge(int amount, double now)
        {
            Amount = Amount > long.MaxValue - amount ? long.MaxValue : Amount + amount;
            LastMergedAt = now;
            Refresh();
        }

        internal void ShiftUp(float distance, double now, float duration)
        {
            slideFrom = SlidePosition(now);
            slideTarget += distance;
            slideStartedAt = now;
            slideSeconds = duration;
        }

        private static float Progress(double elapsed, float duration)
        {
            return duration <= 0 ? 1f : Mathf.Clamp01((float)(elapsed / duration));
        }

        private static float EaseOut(float progress)
        {
            float remaining = 1f - progress;
            return 1f - remaining * remaining * remaining;
        }

        private float SlidePosition(double now)
        {
            return Mathf.Lerp(slideFrom, slideTarget, EaseOut(Progress(now - slideStartedAt, slideSeconds)));
        }

        internal void TickVisual(double now, float hold, float fade, float exitDistance)
        {
            float entry = Progress(now - StartedAt, entrySeconds);
            float exit = Progress(now - LastMergedAt - hold, fade);
            // Zero-length fades are removed by the feed; before hold they must remain fully visible.
            if (now - LastMergedAt < hold) exit = 0f;
            SetAlpha(entry * (1f - exit));
            if (Rect != null)
                Rect.anchoredPosition = new Vector2(0f, SlidePosition(now) + exitDistance * EaseOut(exit));
        }

        internal void SetAlpha(float alpha) { if (group != null) group.alpha = alpha; }
        internal void Hide()
        {
            Amount = 0;
            StartedAt = LastMergedAt = slideStartedAt = 0;
            slideFrom = slideTarget = slideSeconds = entrySeconds = 0f;
            SetAlpha(0f);
            if (Rect != null) Rect.anchoredPosition = Vector2.zero;
            gameObject.SetActive(false);
        }
        private void Refresh()
        {
            if (label != null) label.text = IsGold
                ? "골드 +" + Amount.ToString("N0", CultureInfo.InvariantCulture)
                : ItemName + " 획득";
        }
    }
}


