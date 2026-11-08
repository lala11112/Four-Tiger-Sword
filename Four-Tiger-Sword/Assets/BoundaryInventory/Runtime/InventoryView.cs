using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace BoundaryZero.Inventory
{
    [DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("Boundary Zero/Inventory View")]
    public sealed class InventoryView : MonoBehaviour
    {
        private const int Columns = 6;
        private const int MinimumSlots = Columns * 3;
        private const float WindowWidth = 1364f;
        private const float WindowHeight = 744f;
        private static readonly Color Steel = new Color32(232, 238, 245, 255);
        private static readonly Color Muted = new Color32(176, 190, 204, 255);
        private static readonly Color Line = new Color32(135, 154, 174, 81);
        private static readonly Color Red = new Color32(231, 43, 70, 255);
        private static readonly Vector2 TopLeft = new Vector2(0, 1);
        private static readonly Vector2 Center = new Vector2(.5f, .5f);

        [Serializable]
        private sealed class ItemCell
        {
            public string id;
            public GameObject root;
        }

        [Serializable]
        private sealed class SetRow
        {
            public string id;
            public GameObject root;
            public TMP_Text status;
            public TMP_Text count;
        }

        [SerializeField] private InventoryCatalog catalog;
        [SerializeField] private RectTransform presentation;
        [SerializeField] private RectTransform window;
        [SerializeField] private UnityEngine.UI.Button closeButton;
        [SerializeField] private TMP_Text ownedCount;
        [SerializeField] private GameObject emptySets;
        [SerializeField] private UnityEngine.UI.ScrollRect itemScroll;
        [SerializeField] private UnityEngine.UI.ScrollRect setScroll;
        [SerializeField] private ItemCell[] itemCells = Array.Empty<ItemCell>();
        [SerializeField] private SetRow[] setRows = Array.Empty<SetRow>();
        [SerializeField] private GameObject[] placeholders = Array.Empty<GameObject>();
        [SerializeField] private RectTransform tooltip;
        [SerializeField] private TMP_Text tooltipText;

        private readonly HashSet<string> owned = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, ItemDefinition> items = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, SynergyDefinition> synergies = new Dictionary<string, SynergyDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, SynergyState> statesById = new Dictionary<string, SynergyState>(StringComparer.Ordinal);
        private IReadOnlyList<SynergyState> states = Array.Empty<SynergyState>();
        private InventoryData data;
        private InventoryData displayData;
        private InventoryHoverTarget hover;
        private bool initialized;

        public InventoryCatalog Catalog => catalog;
        public bool IsOpen => presentation != null && presentation.gameObject.activeSelf;
        public int OwnedItemCount => owned.Count;
        public int OwnedCount => OwnedItemCount;
        public event Action<bool> VisibilityChanged;

        private void Awake() => Initialize();
        private void OnDisable() => HideTooltip();

        private void OnDestroy()
        {
            if (closeButton != null)
                closeButton.onClick.RemoveListener(Close);
            if (itemScroll != null)
                itemScroll.onValueChanged.RemoveListener(OnScroll);
            if (setScroll != null)
                setScroll.onValueChanged.RemoveListener(OnScroll);
        }

        private void Initialize()
        {
            if (initialized || presentation == null)
                return;
            initialized = true;
            ReadDisplayData();
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);
            if (itemScroll != null)
                itemScroll.onValueChanged.AddListener(OnScroll);
            if (setScroll != null)
                setScroll.onValueChanged.AddListener(OnScroll);
            presentation.gameObject.SetActive(false);
            Refresh();
        }

        private void ReadDisplayData()
        {
            data = displayData ?? (catalog != null ? catalog.ReadData() : new InventoryData());
            items.Clear();
            synergies.Clear();
            foreach (var item in data.items ?? Array.Empty<ItemDefinition>())
                if (item != null && !string.IsNullOrEmpty(item.id))
                    items[item.id] = item;
            foreach (var set in data.synergies ?? Array.Empty<SynergyDefinition>())
                if (set != null && !string.IsNullOrEmpty(set.id))
                    synergies[set.id] = set;
            owned.RemoveWhere(id => !items.ContainsKey(id));
            foreach (var row in setRows)
            {
                var name = row.root != null ? row.root.transform.Find("Name") : null;
                if (name != null && name.TryGetComponent<TMP_Text>(out var label))
                    label.text = synergies.TryGetValue(row.id, out var definition) ? definition.name : string.Empty;
            }
        }

        /// <summary>Uses host-owned display copy for the existing authored IDs. Null restores the standalone catalog.</summary>
        public void SetDisplayData(InventoryData source)
        {
            displayData = source;
            if (!initialized) { Initialize(); return; }
            ReadDisplayData();
            Refresh();
        }

        public void Open() => SetVisible(true);
        public void Close() => SetVisible(false);
        public void Toggle() => SetVisible(!IsOpen);

        private void SetVisible(bool value)
        {
            Initialize();
            if (presentation == null || IsOpen == value)
                return;
            HideTooltip();
            presentation.gameObject.SetActive(value);
            if (value)
                FitWindow();
            VisibilityChanged?.Invoke(value);
        }

        /// <summary>Replaces the owned set. Duplicate and unknown IDs are ignored.</summary>
        public void SetOwnedItems(IEnumerable<string> itemIds)
        {
            Initialize();
            var replacement = new HashSet<string>(StringComparer.Ordinal);
            if (itemIds != null)
                foreach (var id in itemIds)
                    if (!string.IsNullOrEmpty(id) && items.ContainsKey(id))
                        replacement.Add(id);
            if (owned.SetEquals(replacement))
                return;
            owned.Clear();
            owned.UnionWith(replacement);
            Refresh();
        }

        /// <summary>Adds or removes one catalog ID without creating duplicate stacks.</summary>
        public void SetItemOwned(string id, bool isOwned)
        {
            Initialize();
            if (string.IsNullOrEmpty(id) || !items.ContainsKey(id))
                return;
            if (isOwned ? owned.Add(id) : owned.Remove(id))
                Refresh();
        }

        /// <summary>Returns the latest count and highest active tier for every catalog set.</summary>
        public IReadOnlyList<SynergyState> GetSynergyStates()
        {
            Initialize();
            return states;
        }

        private void Refresh()
        {
            HideTooltip();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var id in owned)
            {
                var tags = items[id].synergies;
                if (tags == null)
                    continue;
                for (var i = 0; i < tags.Length; i++)
                {
                    var tag = tags[i];
                    if (string.IsNullOrEmpty(tag) || Array.IndexOf(tags, tag) != i || !synergies.ContainsKey(tag))
                        continue;
                    counts.TryGetValue(tag, out var count);
                    counts[tag] = count + 1;
                }
            }

            statesById.Clear();
            var list = new List<SynergyState>();
            foreach (var set in data.synergies ?? Array.Empty<SynergyDefinition>())
            {
                if (set == null || string.IsNullOrEmpty(set.id) || statesById.ContainsKey(set.id))
                    continue;
                counts.TryGetValue(set.id, out var count);
                SynergyTier active = null;
                var next = 0;
                foreach (var tier in set.tiers ?? Array.Empty<SynergyTier>())
                {
                    if (tier == null || (tier.count != 2 && tier.count != 4 && tier.count != 6))
                        continue;
                    if (tier.count <= count && (active == null || tier.count > active.count))
                        active = tier;
                    if (tier.count > count && (next == 0 || tier.count < next))
                        next = tier.count;
                }
                var state = new SynergyState(set.id, count, active?.count ?? 0, next, active?.text);
                list.Add(state);
                statesById.Add(set.id, state);
            }
            states = list.AsReadOnly();

            ownedCount.text = owned.Count + "종";
            foreach (var cell in itemCells)
                cell.root.SetActive(owned.Contains(cell.id));

            var slots = Mathf.Max(MinimumSlots, Mathf.CeilToInt(owned.Count / (float)Columns) * Columns);
            var emptySlots = slots - owned.Count;
            for (var i = 0; i < placeholders.Length; i++)
                placeholders[i].SetActive(i < emptySlots);

            bool anyActive = false;
            foreach (var row in setRows)
            {
                var active = statesById.TryGetValue(row.id, out var state) && state.IsActive;
                row.root.SetActive(active);
                if (!active)
                    continue;
                anyActive = true;
                row.status.text = state.ActiveTier + "세트 적용";
                row.count.text = state.DisplayCount + "/6";
            }
            emptySets.SetActive(!anyActive);
        }

        internal void ShowTooltip(InventoryHoverTarget target, string id, bool isSet)
        {
            if (!IsOpen || tooltip == null || target == null || !target.isActiveAndEnabled)
                return;
            var body = new StringBuilder();
            if (isSet)
            {
                if (!synergies.TryGetValue(id, out var set))
                    return;
                AppendTitle(body, set.name);
                AppendSet(body, set, true);
            }
            else
            {
                if (!items.TryGetValue(id, out var item))
                    return;
                AppendTitle(body, item.name);
                body.Append("<size=16><color=#b5c1ce>").Append(item.description).Append("</color></size>\n\n")
                    .Append("<size=15><color=#a8b9c9>개별 효과</color></size>\n").Append(item.effect)
                    .Append("\n\n<size=15><color=#a8b9c9>시너지</color></size>\n");
                foreach (var tag in item.synergies ?? Array.Empty<string>())
                    if (!string.IsNullOrEmpty(tag) && synergies.TryGetValue(tag, out var set))
                        AppendSet(body, set, false);
            }

            if (hover != target)
                HideTooltip();
            hover = target;
            target.SetHighlight(true);
            tooltipText.text = body.ToString().TrimEnd();
            PositionTooltip(target.transform as RectTransform);
            tooltip.gameObject.SetActive(true);
        }

        private static void AppendTitle(StringBuilder body, string value) =>
            body.Append("<size=25><b>").Append(value).Append("</b></size>\n");

        private void AppendSet(StringBuilder body, SynergyDefinition set, bool allTiers)
        {
            if (!statesById.TryGetValue(set.id, out var state))
                return;
            if (!allTiers)
                body.Append("<b>").Append(set.name).Append("</b>   ");
            body.Append("<size=15><color=#bbc9d6>").Append(state.DisplayCount).Append("/6 · ")
                .Append(state.Count).Append("종 보유</color></size>\n");
            if (allTiers)
            {
                body.Append('\n');
                foreach (var tier in set.tiers ?? Array.Empty<SynergyTier>())
                {
                    if (tier == null)
                        continue;
                    var active = tier.count == state.ActiveTier;
                    body.Append(active ? "<color=#fa8396>◆ " : "<color=#bbc9d6>◇ ")
                        .Append(tier.count).Append("세트</color> · ").Append(tier.text)
                        .Append(active ? " <color=#fa8396>· 적용 중</color>\n" : "\n");
                }
                if (!string.IsNullOrEmpty(set.effect))
                    body.Append("\n<size=15><color=#bbc9d6>").Append(set.effect).Append("</color></size>");
            }
            else
            {
                body.Append(state.IsActive
                    ? state.ActiveTier + "세트 적용 중 · " + state.ActiveEffect
                    : "미활성 · 적용 효과 없음");
                if (!state.IsActive && state.NextTier > 0)
                    body.Append("\n<size=15><color=#bbc9d6>").Append(state.NextTier).Append("세트까지 ")
                        .Append(state.NextTier - state.Count).Append("종 필요</color></size>");
                body.Append("\n\n");
            }
        }

        internal void HideTooltip(InventoryHoverTarget target = null)
        {
            if (target != null && target != hover)
                return;
            if (hover != null)
                hover.SetHighlight(false);
            hover = null;
            if (tooltip != null)
                tooltip.gameObject.SetActive(false);
        }

        private void OnScroll(Vector2 _) => HideTooltip();

        private void OnRectTransformDimensionsChange()
        {
            if (IsOpen)
            {
                FitWindow();
                if (hover != null)
                    PositionTooltip(hover.transform as RectTransform);
            }
        }

        private void FitWindow()
        {
            if (window == null)
                return;
            var rect = presentation.rect;
            var scale = Mathf.Min(1f, (rect.width - 32) / WindowWidth, (rect.height - 32) / WindowHeight);
            window.localScale = Vector3.one * Mathf.Max(.01f, scale);
        }

        private void PositionTooltip(RectTransform target)
        {
            if (target == null)
                return;
            const float margin = 12f, gap = 11f;
            var rect = presentation.rect;
            var scale = window.localScale.x;
            var logicalWidth = Mathf.Max(1, Mathf.Min(458f, (rect.width - margin * 2) / scale));
            var logicalHeight = tooltipText.GetPreferredValues(tooltipText.text, logicalWidth - 36f, 0).y + 30f;
            tooltip.sizeDelta = new Vector2(logicalWidth, logicalHeight);
            tooltip.localScale = Vector3.one * scale;

            var topLeft = (Vector2)presentation.InverseTransformPoint(target.TransformPoint(new Vector2(target.rect.xMin, target.rect.yMax)));
            var topRight = (Vector2)presentation.InverseTransformPoint(target.TransformPoint(new Vector2(target.rect.xMax, target.rect.yMax)));
            var width = logicalWidth * scale;
            var height = logicalHeight * scale;
            var x = topRight.x + gap;
            if (x + width > rect.xMax - margin)
                x = topLeft.x - gap - width;
            x = Mathf.Clamp(x, rect.xMin + margin, Mathf.Max(rect.xMin + margin, rect.xMax - margin - width));
            var y = Mathf.Clamp(topLeft.y, Mathf.Min(rect.yMax - margin, rect.yMin + margin + height), rect.yMax - margin);
            tooltip.anchoredPosition = new Vector2(x - rect.xMin, y - rect.yMax);
        }

        /// <summary>Creates the native uGUI hierarchy for a new view. Call once before using the view.</summary>
        public void BuildView(InventoryCatalog source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (presentation != null)
            {
                if (source != catalog)
                    throw new InvalidOperationException("Build a new InventoryView to change the catalog.");
                Initialize();
                return;
            }

            catalog = source;
            var definitions = catalog.ReadData();
            presentation = Node("InventoryPresentation", (RectTransform)transform);
            Fill(presentation);
            Graphic(presentation, new Color32(5, 16, 28, 217), true);

            window = Node("Window", presentation);
            window.anchorMin = window.anchorMax = window.pivot = Center;
            window.sizeDelta = new Vector2(WindowWidth, WindowHeight);
            var frame = Graphic(window, catalog.windowFrame != null ? Color.white : new Color32(15, 26, 41, 255));
            frame.sprite = catalog.windowFrame;

            Plate("TitleAccent", window, 70, 48, 3, 34, Red);
            Icon("Seal", window, catalog.seal, 82, 43, 38, 38);
            Label("Title", window, "인벤토리", 133, 37, 500, 54, 39, Steel).fontStyle = FontStyles.Bold;
            Plate("HeaderRule", window, 70, 101, 1224, 1, Line);
            Plate("HeaderRuleLight", window, 70, 100, 80, 2, Steel);
            Plate("HeaderRuleRed", window, 157, 100, 44, 2, Red);
            var close = Place("Close", window, 1185, 43, 109, 36);
            var closeBorder = Graphic(close, new Color32(166, 177, 188, 141), true);
            var closeSurface = Plate("Fill", close, 1, 1, 107, 34, new Color32(16, 30, 45, 255));
            closeButton = close.gameObject.AddComponent<UnityEngine.UI.Button>();
            closeButton.targetGraphic = closeSurface;
            var navigation = closeButton.navigation;
            navigation.mode = UnityEngine.UI.Navigation.Mode.None;
            closeButton.navigation = navigation;
            closeBorder.raycastTarget = true;
            var closeText = Label("Label", close, "닫기", 0, 0, 109, 36, 18, Steel);
            closeText.alignment = TextAlignmentOptions.Center;

            var left = Place("OwnedItems", window, 70, 141, 740, 533);
            Label("Heading", left, "보유 아이템", 0, 0, 135, 32, 25, Steel).fontStyle = FontStyles.Bold;
            ownedCount = Label("Count", left, "0종", 141, 2, 78, 30, 18, Muted);
            Plate("SectionRule", left, 0, 42, 740, 1, Line);
            itemScroll = Scroll("ItemScroll", left, 0, 57, 740, 476, out var grid);
            var layout = grid.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
            layout.padding = new RectOffset(1, 1, 1, 1);
            layout.cellSize = new Vector2(112, 112);
            layout.spacing = new Vector2(10, 10);
            layout.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = Columns;
            AddFitter(grid);

            var cells = new List<ItemCell>();
            var knownItems = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in definitions.items ?? Array.Empty<ItemDefinition>())
            {
                if (item == null || string.IsNullOrEmpty(item.id) || !knownItems.Add(item.id))
                    continue;
                var cell = Slot("Item_" + item.id, grid, true, out var fill);
                Icon("Icon", cell, catalog.ItemIcon(item.id), 14, 14, 84, 84);
                var accent = Plate("HoverAccent", cell, 0, 0, 34, 2, Red);
                accent.enabled = false;
                cell.gameObject.AddComponent<InventoryHoverTarget>().Configure(this, item.id, false,
                    fill, accent, fill.color, new Color32(37, 54, 71, 255));
                cells.Add(new ItemCell { id = item.id, root = cell.gameObject });
            }
            itemCells = cells.ToArray();
            placeholders = new GameObject[MinimumSlots];
            for (var i = 0; i < placeholders.Length; i++)
                placeholders[i] = Slot("EmptySlot_" + (i + 1), grid, false, out _).gameObject;

            var right = Place("Synergies", window, 875, 141, 419, 533);
            Label("Heading", right, "활성 시너지", 0, 0, 320, 32, 25, Steel).fontStyle = FontStyles.Bold;
            Plate("SectionRule", right, 0, 42, 419, 1, Line);
            setScroll = Scroll("SynergyScroll", right, 0, 57, 419, 476, out var setList);
            var rowsLayout = setList.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            rowsLayout.childControlHeight = false;
            rowsLayout.childControlWidth = true;
            rowsLayout.childForceExpandHeight = false;
            rowsLayout.childForceExpandWidth = true;
            AddFitter(setList);
            var rows = new List<SetRow>();
            var knownSets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var set in definitions.synergies ?? Array.Empty<SynergyDefinition>())
            {
                if (set == null || string.IsNullOrEmpty(set.id) || !knownSets.Add(set.id))
                    continue;
                var row = Node("Synergy_" + set.id, setList);
                row.sizeDelta = new Vector2(406, 81);
                var rowFill = Graphic(row, new Color32(133, 153, 172, 0), true);
                Icon("Mark", row, catalog.SynergyIcon(set.id), 10, 25, 32, 32);
                Label("Name", row, set.name, 57, 16, 267, 29, 22, Steel);
                var status = Label("Tier", row, "", 57, 46, 270, 22, 16, Muted);
                var count = Label("Count", row, "", 325, 20, 70, 41, 20, Steel);
                count.alignment = TextAlignmentOptions.MidlineRight;
                Plate("Rule", row, 0, 80, 407, 1, new Color32(129, 146, 162, 38));
                row.gameObject.AddComponent<InventoryHoverTarget>().Configure(this, set.id, true,
                    rowFill, null, rowFill.color, new Color32(133, 153, 172, 25));
                rows.Add(new SetRow { id = set.id, root = row.gameObject, status = status, count = count });
            }
            setRows = rows.ToArray();
            emptySets = Label("EmptyMessage", setList, "같은 시너지 아이템 2종부터 활성화됩니다.", 0, 0,
                405, 42, 17, Muted).gameObject;

            tooltip = Place("Tooltip", presentation, 0, 0, 458, 200);
            var tooltipGroup = tooltip.gameObject.AddComponent<CanvasGroup>();
            tooltipGroup.blocksRaycasts = false;
            tooltipGroup.interactable = false;
            var tooltipBorder = Graphic(tooltip, new Color32(117, 136, 153, 255));
            var tooltipShadow = tooltip.gameObject.AddComponent<UnityEngine.UI.Shadow>();
            tooltipShadow.effectColor = new Color32(3, 9, 16, 128);
            tooltipShadow.effectDistance = new Vector2(2, -3);
            var tipFill = Node("Fill", tooltip);
            Fill(tipFill, 1, 1, 1, 1);
            Graphic(tipFill, new Color32(14, 26, 40, 255));
            Plate("Accent", tooltip, 0, 0, 39, 2, Red);
            tooltipText = Label("Text", tooltip, "", 18, 15, 422, 170, 18, Steel);
            Fill(tooltipText.rectTransform, 18, 15, 18, 15);
            tooltipText.richText = true;
            tooltipText.textWrappingMode = TextWrappingModes.Normal;
            tooltipText.alignment = TextAlignmentOptions.TopLeft;
            tooltipBorder.raycastTarget = false;

            Initialize();
        }

        private static RectTransform Slot(string name, RectTransform grid, bool occupied, out UnityEngine.UI.Image fill)
        {
            var slot = Node(name, grid);
            Graphic(slot, occupied ? new Color32(148, 168, 185, 102) : new Color32(131, 152, 170, 61), occupied);
            var panel = Node("Fill", slot);
            Fill(panel, 1, 1, 1, 1);
            fill = Graphic(panel, occupied ? new Color32(28, 43, 58, 255) : new Color32(16, 30, 44, 255));
            return slot;
        }

        private static UnityEngine.UI.ScrollRect Scroll(string name, RectTransform parent, float x,
            float y, float width, float height, out RectTransform content)
        {
            var root = Place(name, parent, x, y, width, height);
            var scroll = root.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.inertia = false;
            scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 44;

            var viewport = Node("Viewport", root);
            Fill(viewport, 0, 0, 12, 0);
            Graphic(viewport, Color.white, true);
            viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
            content = Node("Content", viewport);
            content.anchorMin = TopLeft;
            content.anchorMax = Vector2.one;
            content.pivot = TopLeft;
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            scroll.viewport = viewport;
            scroll.content = content;

            var track = Node("Scrollbar", root);
            track.anchorMin = Vector2.right;
            track.anchorMax = Vector2.one;
            track.pivot = new Vector2(1, .5f);
            track.sizeDelta = new Vector2(5, 0);
            track.anchoredPosition = Vector2.zero;
            Graphic(track, new Color32(62, 78, 95, 90), true);
            var scrollbar = track.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();
            scrollbar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
            var navigation = scrollbar.navigation;
            navigation.mode = UnityEngine.UI.Navigation.Mode.None;
            scrollbar.navigation = navigation;
            var area = Node("SlidingArea", track);
            Fill(area);
            var handle = Node("Handle", area);
            Fill(handle);
            scrollbar.targetGraphic = Graphic(handle, new Color32(100, 118, 135, 255), true);
            scrollbar.handleRect = handle;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;
            return scroll;
        }

        private static void AddFitter(RectTransform content)
        {
            var fitter = content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        }

        private static RectTransform Node(string name, RectTransform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static RectTransform Place(string name, RectTransform parent, float x, float y, float w, float h)
        {
            var rect = Node(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = TopLeft;
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            return rect;
        }

        private static void Fill(RectTransform rect, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = Center;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static UnityEngine.UI.Image Graphic(RectTransform rect, Color color, bool raycast = false)
        {
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private static UnityEngine.UI.Image Plate(string name, RectTransform parent, float x, float y,
            float width, float height, Color color) =>
            Graphic(Place(name, parent, x, y, width, height), color);

        private static void Icon(string name, RectTransform parent, Sprite sprite, float x, float y, float w, float h)
        {
            var image = Plate(name, parent, x, y, w, h, Color.white);
            image.sprite = sprite;
            image.preserveAspect = true;
            image.enabled = sprite != null;
        }

        private TMP_Text Label(string name, RectTransform parent, string value, float x, float y,
            float width, float height, float size, Color color)
        {
            var rect = Place(name, parent, x, y, width, height);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (catalog.font != null)
                label.font = catalog.font;
            label.text = value;
            label.color = color;
            label.fontSize = size;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.richText = false;
            label.raycastTarget = false;
            return label;
        }
    }
}
