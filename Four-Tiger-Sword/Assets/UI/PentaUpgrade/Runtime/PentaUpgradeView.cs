using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace Penta.Upgrade
{
    [DisallowMultipleComponent]
    public sealed class PentaUpgradeView : MonoBehaviour
    {
        [Serializable]
        sealed class NodeUI
        {
            public string Id;
            public UnityEngine.UI.Button Button;
            public PentaUpgradeChrome Frame;
            public PentaUpgradeSymbol Icon;
            public TMP_Text Label;
            public TMP_Text StateLabel;
        }

        [SerializeField] TMP_FontAsset font;
        [SerializeField] Sprite seal;
        [SerializeField] CanvasGroup visual;
        [SerializeField] PentaUpgradeViewport viewport;
        [SerializeField] PentaUpgradeLinks links;
        [SerializeField] List<NodeUI> nodeViews = new List<NodeUI>();
        [SerializeField] UnityEngine.UI.Button closeButton, fitButton, zoomInButton, zoomOutButton, focusButton;
        [SerializeField] UnityEngine.UI.Button[] parentButtons;
        [SerializeField] TMP_Text detailTitle, detailFamily, detailDescription, parentHint;
        [SerializeField] PentaUpgradeSymbol detailIcon;
        [SerializeField] string selectedId = "origin";
        [SerializeField] bool hosted;
        [SerializeField] UnityEngine.UI.Button purchaseButton;
        [SerializeField] TMP_Text purchaseLabel, purchaseCost, purchaseStatus, balanceLabel;
        [SerializeField] UnityEngine.UI.ScrollRect descriptionScroll;
        readonly Dictionary<string, PentaUpgradeNodeState> states = new Dictionary<string, PentaUpgradeNodeState>();
        bool purchaseBusy, started;

        public UnityEvent<bool> VisibilityChanged = new UnityEvent<bool>();
        public event Action<string> NodeSelected;
        public event Action<string> PurchaseRequested;
        public event Action CloseRequested;
        public bool IsBuilt => visual != null;
        public bool IsOpen { get; private set; }
        public string SelectedId => selectedId;
        public float Zoom => viewport ? viewport.Zoom : 1;
        public RectTransform Viewport => viewport ? viewport.viewport : null;
        public RectTransform NodeContent => viewport ? viewport.content : null;
        public UnityEngine.UI.Button PurchaseButton => purchaseButton;
        public TMP_Text PurchaseStatus => purchaseStatus;
        public UnityEngine.UI.ScrollRect DescriptionScroll => descriptionScroll;
        public int NodeCount => nodeViews.Count;
        bool previousCursorVisible;
        CursorLockMode previousCursorLock;

        static readonly Color White = new Color32(232, 238, 245, 255);
        static readonly Color Muted = new Color32(163, 181, 201, 255);
        static readonly Color Accent = new Color32(231, 43, 70, 255);
        static readonly Color Border = new Color(.76f, .84f, .95f, .22f);
        static readonly Vector2 GraphSize = PentaUpgradeVerticalLayout.Size;

        void OnEnable()
        {
            if (!IsBuilt) return;
            Wire();
            Select(selectedId);
            Open();
        }

        void OnDisable() { Close(); }

        void Start()
        {
            if (!started && viewport) HomeView();
            started = true;
        }

        void Update()
        {
            #if ENABLE_LEGACY_INPUT_MANAGER
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) RequestClose();
            #endif
        }

        public void Open()
        {
            if (!IsBuilt || IsOpen) return;
            Wire();
            if (!hosted)
            {
                previousCursorVisible = Cursor.visible;
                previousCursorLock = Cursor.lockState;
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            visual.alpha = 1;
            visual.interactable = visual.blocksRaycasts = true;
            IsOpen = true;
            VisibilityChanged.Invoke(true);
        }

        public void Close()
        {
            if (!IsOpen) return;
            visual.alpha = 0;
            visual.interactable = visual.blocksRaycasts = false;
            if (!hosted)
            {
                Cursor.visible = previousCursorVisible;
                Cursor.lockState = previousCursorLock;
            }
            IsOpen = false;
            VisibilityChanged.Invoke(false);
        }

        public void FitView()
        {
            HomeView();
        }

        public void HomeView()
        {
            Canvas.ForceUpdateCanvases();
            if (viewport) viewport.Home();
        }

        public void FocusSelected()
        {
            Canvas.ForceUpdateCanvases();
            var node = PentaUpgradeCatalog.Find(selectedId);
            if (viewport && node != null)
                viewport.FocusPoint(NodePosition(node), new Vector2(.5f, .5f));
        }

        public void SelectAndFocus(string id)
        {
            Select(id);
            FocusSelected();
        }

        public void SetZoom(float value)
        {
            if (viewport) viewport.SetZoom(value);
        }

        public UnityEngine.UI.Button GetNodeButton(string id)
        {
            var found = nodeViews.Find(n => n.Id == id);
            return found == null ? null : found.Button;
        }

        public void Select(string id)
        {
            var selected = PentaUpgradeCatalog.Find(id);
            if (selected == null || !IsBuilt) return;
            bool changed = selectedId != id;
            selectedId = id;
            var ancestors = new HashSet<string>();
            CollectAncestors(id, ancestors);
            foreach (var node in nodeViews)
            {
                bool active = node.Id == id;
                bool connected = ancestors.Contains(node.Id);
                node.Frame.Configure(PentaUpgradeChrome.Style.TreeNode, active, connected);
                node.Frame.raycastTarget = true;
                states.TryGetValue(node.Id, out var state);
                Color tint = state == null ? White : state.Availability == PentaUpgradeAvailability.Owned
                    ? new Color32(152, 220, 189, 255) : state.Availability == PentaUpgradeAvailability.Locked
                    ? new Color32(118, 132, 149, 255) : state.Availability == PentaUpgradeAvailability.Unaffordable
                    ? new Color32(215, 176, 138, 255) : White;
                node.Icon.color = tint;
                node.Icon.Refresh();
                node.Label.color = tint;
                if (state != null && !string.IsNullOrEmpty(state.DisplayName)) node.Label.text = state.DisplayName;
                if (node.StateLabel != null)
                {
                    node.StateLabel.text = string.Empty;
                    node.StateLabel.gameObject.SetActive(false);
                }
            }
            if (hosted) RefreshNodeLabelMasks();
            links.Highlight(ancestors);
            states.TryGetValue(id, out var selectedState);
            detailTitle.text = selectedState?.DisplayName ?? selected.Name;
            detailFamily.text = hosted ? selected.Family : selected.Family + "  /  내용 미확정";
            string description = selectedState?.Description ?? selected.Description;
            if (descriptionScroll != null && (changed || detailDescription.text != description))
            {
                descriptionScroll.StopMovement();
                descriptionScroll.content.anchoredPosition = Vector2.zero;
            }
            detailDescription.text = description;
            detailIcon.Configure(IconFor(selected), Color.white);
            if (!hosted)
            {
                var parents = selected.ParentIds.Select(PentaUpgradeCatalog.Find).Where(n => n != null).ToArray();
                PopulateLinks(parentButtons, parents);
                parentHint.gameObject.SetActive(parents.Length == 0);
            }
            RefreshPurchase();
            NodeSelected?.Invoke(id);
        }

        public void ApplyPresentation(IEnumerable<PentaUpgradeNodeState> values, string balance, string message = null)
        {
            states.Clear();
            if (values != null) foreach (var value in values) if (value != null && value.Id != null) states[value.Id] = value;
            if (balanceLabel != null) balanceLabel.text = balance;
            Select(selectedId);
            if (!string.IsNullOrEmpty(message)) SetPurchaseStatus(message);
        }

        public void SetHosted(bool value) => hosted = value;
        public void SetPurchaseBusy(bool value) { purchaseBusy = value; RefreshPurchase(); }
        public PentaUpgradeNodeState GetNodeState(string id) => states.TryGetValue(id, out var value) ? value : null;

        void RefreshPurchase()
        {
            states.TryGetValue(selectedId, out var state);
            if (purchaseButton != null) purchaseButton.interactable = state != null && state.CanPurchase && !purchaseBusy;
            if (purchaseCost != null) purchaseCost.text = state?.CostLabel ?? "—";
            SetPurchaseStatus(state?.StatusText);
            if (purchaseLabel != null) purchaseLabel.text = state == null ? "이용 불가" : state.Availability == PentaUpgradeAvailability.Owned
                ? "해금 완료" : state.Availability == PentaUpgradeAvailability.Locked ? "선행 각인 필요"
                : state.Availability == PentaUpgradeAvailability.Unaffordable ? "강화석 부족" : "각인 해금";
            if (purchaseLabel != null)
                purchaseLabel.color = purchaseButton.interactable ? White : Muted;
        }

        void SetPurchaseStatus(string value)
        {
            if (purchaseStatus == null) return;
            purchaseStatus.text = value ?? "";
            purchaseStatus.gameObject.SetActive(!string.IsNullOrEmpty(value));
        }

        void RequestClose()
        {
            if (hosted) CloseRequested?.Invoke(); else Close();
        }

        /// <summary>Fits the existing graph and its details inside the workshop frame.</summary>
        public void ConfigureGameplayUI(Vector2 frameSize)
        {
            hosted = true;
            var panel = transform.Find("UpgradeVisual/UpgradePanel") as RectTransform;
            if (panel == null || !IsBuilt) throw new InvalidOperationException("Build the Penta upgrade panel before configuring gameplay UI.");
            visual.GetComponent<PentaUpgradePanelFit>().enabled = false;
            panel.sizeDelta = frameSize;
            panel.localScale = Vector3.one;
            panel.GetComponent<PentaUpgradeChrome>().enabled = false;
            Hide(visual.transform, "GameDimmer");
            Hide(panel, "HeaderAccent", "HeaderSeal", "Title", "Subtitle", "HubLabel", "Close", "CloseKey",
                "HeaderRule", "HeaderSteel", "HeaderRed", "TreeHeading", "TreeHint",
                "FooterRule", "FooterMark", "Footer", "InputHints", "BasicTab", "EngravingTab");
            balanceLabel = null;

            ResizeControl(fitButton, 64, 204, 160, 48, 22);
            ResizeControl(zoomOutButton, 968, 204, 48, 48, 26);
            ResizeControl(zoomInButton, 1024, 204, 48, 48, 26);
            Resize((RectTransform)viewport.transform, 64, 266, 1008, 600);
            Fill(viewport.viewport);

            var detail = (RectTransform)panel.Find("SelectedNodePanel");
            Resize(detail, 1104, 204, 592, 662);
            Resize(detailFamily.rectTransform, 28, 26, 440, 34);
            detailFamily.fontSize = 22;
            detailFamily.color = Muted;
            Resize(detailTitle.rectTransform, 28, 62, 438, 62);
            detailTitle.enableAutoSizing = true;
            detailTitle.fontSizeMin = 22;
            detailTitle.fontSizeMax = 32;
            detailTitle.textWrappingMode = TextWrappingModes.Normal;
            Resize((RectTransform)detail.Find("IconMedallion"), 486, 27, 78, 78);
            Resize(detailIcon.rectTransform, 500, 41, 50, 50);
            Resize((RectTransform)detail.Find("DetailRule"), 28, 128, 536, 1);
            var heading = EnsureLabel(detail, "DescriptionHeading", 28, 151, 536, 36, 22, Muted);
            heading.text = "각인 효과";
            Hide(detail, "DraftNotice", "ConnectionsRule", "ParentsHeading", "RootHint", "ShowRoute");
            foreach (var parent in parentButtons) parent.gameObject.SetActive(false);

            var scrollRoot = detail.Find("DescriptionScroll") as RectTransform;
            if (scrollRoot == null) scrollRoot = Rect(detail, "DescriptionScroll", 28, 198, 536, 280);
            Resize(scrollRoot, 28, 198, 536, 280);
            descriptionScroll = scrollRoot.GetComponent<UnityEngine.UI.ScrollRect>();
            if (descriptionScroll == null) descriptionScroll = scrollRoot.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            var clip = scrollRoot.Find("Viewport") as RectTransform;
            if (clip == null) clip = Rect(scrollRoot, "Viewport", 0, 0, 536, 280);
            Fill(clip);
            var hit = clip.GetComponent<UnityEngine.UI.Image>();
            if (hit == null) hit = clip.gameObject.AddComponent<UnityEngine.UI.Image>();
            hit.color = new Color(0, 0, 0, .001f);
            hit.raycastTarget = true;
            if (clip.GetComponent<UnityEngine.UI.RectMask2D>() == null) clip.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            // Keep the hosted label local to the workshop prefab. Reparenting a child of
            // the delivered nested prefab does not survive saving the workshop asset.
            Hide(detail, "Description");
            detailDescription = EnsureLabel(clip, "DescriptionText", 0, 0, 536, 280, 24, White);
            var content = detailDescription.rectTransform;
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0, 1); content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0, 280);
            detailDescription.enableAutoSizing = false;
            detailDescription.margin = new Vector4(0f, 0f, 12f, 0f);
            detailDescription.lineSpacing = 7;
            detailDescription.overflowMode = TextOverflowModes.Overflow;
            detailDescription.raycastTarget = false;
            var fit = content.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            if (fit == null) fit = content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fit.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            descriptionScroll.viewport = clip; descriptionScroll.content = content;
            descriptionScroll.horizontal = false; descriptionScroll.vertical = true;
            descriptionScroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            descriptionScroll.inertia = false; descriptionScroll.scrollSensitivity = 30;
            purchaseStatus = EnsureLabel(detail, "PurchaseStatus", 28, 488, 536, 68, 22, new Color32(247, 151, 151, 255));
            purchaseCost = EnsureLabel(detail, "PurchaseCost", 28, 558, 536, 34, 22, White);
            purchaseCost.alignment = TextAlignmentOptions.MidlineRight;
            var purchase = detail.Find("Purchase")?.GetComponent<UnityEngine.UI.Button>();
            purchaseButton = purchase != null ? purchase : Button(detail, "Purchase", "각인 해금", 28, 598, 536, 52, true);
            ResizeControl(purchaseButton, 28, 598, 536, 52, 24);
            var unlockChrome = purchaseButton.GetComponent<PentaUpgradeChrome>();
            unlockChrome.Configure(PentaUpgradeChrome.Style.UnlockButton);
            purchaseLabel = purchaseButton.GetComponentInChildren<TMP_Text>(true);
            purchaseLabel.fontStyle = FontStyles.Bold;
            foreach (var node in nodeViews)
            {
                node.Label.rectTransform.sizeDelta = new Vector2(220, 56);
                node.Label.alignment = TextAlignmentOptions.Midline;
                node.Label.enableAutoSizing = true;
                node.Label.fontSizeMin = 22;
                node.Label.fontSizeMax = 26;
                node.Label.textWrappingMode = TextWrappingModes.Normal;
                node.StateLabel = node.Button.transform.Find("PurchaseState")?.GetComponent<TMP_Text>();
                ButtonFeedback(node.Button, node.Frame);
            }
            Wire();
            Select(selectedId);
        }

        void RefreshNodeLabelMasks()
        {
            var masks = new List<Rect>();
            var corners = new Vector3[4];
            foreach (var node in nodeViews)
            {
                AddMask(node.Label);
            }
            links.SetLabelMasks(masks.ToArray());

            void AddMask(TMP_Text label)
            {
                if (label == null || string.IsNullOrEmpty(label.text)) return;
                var rect = label.rectTransform;
                rect.GetWorldCorners(corners);
                Vector2 min = links.rectTransform.InverseTransformPoint(corners[0]);
                Vector2 max = links.rectTransform.InverseTransformPoint(corners[2]);
                float width = Mathf.Min(rect.rect.width, label.GetPreferredValues(label.text).x + 16f);
                float halfWidth = (max.x - min.x) * width / rect.rect.width * .5f;
                float centerX = (min.x + max.x) * .5f;
                // Keep the line gap centered on the same rect as the visible glyphs.
                masks.Add(UnityEngine.Rect.MinMaxRect(centerX - halfWidth, min.y - 4f, centerX + halfWidth, max.y + 4f));
            }
        }

        TMP_Text EnsureLabel(Transform parent, string name, float x, float y, float width, float height, float size, Color tint)
        {
            var existing = parent.Find(name)?.GetComponent<TMP_Text>();
            if (existing == null) return Label(parent, name, "", x, y, width, height, size, tint);
            Resize(existing.rectTransform, x, y, width, height);
            existing.fontSize = size;
            existing.color = tint;
            return existing;
        }

        static void Resize(RectTransform target, float x, float y, float width, float height)
        {
            target.anchoredPosition = new Vector2(x, -y);
            target.sizeDelta = new Vector2(width, height);
        }

        static void ResizeControl(UnityEngine.UI.Button button, float x, float y, float width, float height, float size)
        {
            Resize((RectTransform)button.transform, x, y, width, height);
            var label = button.GetComponentInChildren<TMP_Text>(true);
            Resize(label.rectTransform, 8, 0, width - 16, height);
            label.fontSize = size;
            ButtonFeedback(button, button.targetGraphic);
        }

        static void Hide(Transform parent, params string[] names)
        {
            foreach (string name in names)
            {
                var child = parent.Find(name);
                if (child != null) child.gameObject.SetActive(false);
            }
        }

        static void CollectAncestors(string id, HashSet<string> values)
        {
            if (!values.Add(id)) return;
            foreach (string parent in PentaUpgradeCatalog.Find(id).ParentIds) CollectAncestors(parent, values);
        }

        void PopulateLinks(UnityEngine.UI.Button[] buttons, PentaUpgradeNode[] values)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                button.gameObject.SetActive(i < values.Length);
                button.onClick.RemoveAllListeners();
                if (i >= values.Length) continue;
                string targetId = values[i].Id;
                states.TryGetValue(targetId, out var state);
                button.GetComponentInChildren<TMP_Text>().text = state?.DisplayName ?? values[i].Name;
                button.onClick.AddListener(() => SelectAndFocus(targetId));
            }
        }

        void Wire()
        {
            foreach (var node in nodeViews)
            {
                string id = node.Id;
                node.Button.onClick.RemoveAllListeners();
                node.Button.onClick.AddListener(() => Select(id));
            }
            Bind(closeButton, RequestClose);
            Bind(fitButton, HomeView);
            Bind(focusButton, FocusSelected);
            Bind(zoomInButton, () => SetZoom(Zoom + .2f));
            Bind(zoomOutButton, () => SetZoom(Zoom - .2f));
            if (purchaseButton != null) Bind(purchaseButton, () =>
            {
                if (!purchaseBusy && states.TryGetValue(selectedId, out var state) && state.CanPurchase)
                    PurchaseRequested?.Invoke(selectedId);
            });
        }

        static void Bind(UnityEngine.UI.Button button, UnityAction action)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        public void Build(TMP_FontAsset suppliedFont, Sprite suppliedSeal)
        {
            if (IsBuilt) return;
            font = suppliedFont;
            seal = suppliedSeal;
            var overlay = Rect(transform, "UpgradeVisual", 0, 0, 1920, 1080);
            Fill(overlay);
            visual = overlay.gameObject.AddComponent<CanvasGroup>();
            var dim = Rect(overlay, "GameDimmer", 0, 0, 1920, 1080).gameObject.AddComponent<UnityEngine.UI.Image>();
            Fill(dim.rectTransform);
            dim.color = new Color(13f / 255, 21f / 255, 33f / 255, .50f);
            dim.raycastTarget = true;

            var shell = Box(overlay, "UpgradePanel", 0, 0, 1760, 930, PentaUpgradeChrome.Style.Window);
            shell.rectTransform.anchorMin = shell.rectTransform.anchorMax = new Vector2(.5f, .5f);
            shell.rectTransform.pivot = new Vector2(.5f, .5f);
            shell.rectTransform.anchoredPosition = Vector2.zero;
            shell.raycastTarget = true;
            overlay.gameObject.AddComponent<PentaUpgradePanelFit>().Configure(shell.rectTransform);
            var panel = shell.transform;
            Rule(panel, "HeaderAccent", 64, 60, 3, 37, Accent);
            Seal(panel, "HeaderSeal", 72, 52, 42, 42);
            Label(panel, "Title", "각인 강화", 119, 47, 560, 50, 32, White);
            Label(panel, "Subtitle", "세로 줄기형 / 노드 내용 미확정", 121, 95, 620, 24, 14, Muted);
            Label(panel, "HubLabel", "허브  /  영구 성장", 1320, 63, 290, 30, 16, Muted, TextAlignmentOptions.Right);
            closeButton = Button(panel, "Close", "×", 1660, 53, 44, 40);
            Label(panel, "CloseKey", "ESC", 1660, 97, 44, 20, 12, Muted, TextAlignmentOptions.Center);
            Rule(panel, "HeaderRule", 60, 124, 1640, 1, Border);
            Rule(panel, "HeaderSteel", 60, 124, 110, 2, White);
            Rule(panel, "HeaderRed", 177, 124, 42, 2, Accent);
            Label(panel, "TreeHeading", "성장 트리", 64, 145, 240, 28, 18, White);
            Label(panel, "TreeHint", "위로 밀어 다음 갈래를 살펴보세요", 190, 146, 550, 26, 16, Muted);
            fitButton = Button(panel, "FitView", "시작점으로", 1000, 135, 118, 34);
            zoomOutButton = Button(panel, "ZoomOut", "−", 1128, 135, 42, 34);
            zoomInButton = Button(panel, "ZoomIn", "+", 1178, 135, 42, 34);

            BuildTree(panel);
            BuildDetails(panel);
            Rule(panel, "FooterRule", 60, 853, 1640, 1, Border);
            Rule(panel, "FooterMark", 64, 872, 3, 15, Accent);
            Label(panel, "Footer", "트리를 위로 밀어 다음 갈래를 살펴보세요", 78, 869, 710, 24, 14, Muted);
            Label(panel, "InputHints", "클릭  선택     드래그  이동     휠  확대 · 축소     ESC  닫기", 930, 869, 770, 26, 14, Muted, TextAlignmentOptions.Right);
            Wire();
            Select(selectedId);
            Open();
            HomeView();
        }

        static Vector2 NodePosition(PentaUpgradeNode node)
        {
            return PentaUpgradeVerticalLayout.Position(node.Id);
        }

        void BuildTree(Transform panel)
        {
            var scrollRoot = Rect(panel, "TreeNavigation", 60, 182, 1178, 650);
            viewport = scrollRoot.gameObject.AddComponent<PentaUpgradeViewport>();
            var clip = Rect(scrollRoot, "Viewport", 0, 0, 1178, 650);
            clip.pivot = new Vector2(.5f, .5f);
            clip.anchoredPosition = new Vector2(589, -325);
            var background = clip.gameObject.AddComponent<UnityEngine.UI.Image>();
            background.color = new Color(13f / 255, 21f / 255, 33f / 255, .10f);
            clip.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var content = Rect(clip, "TreeContent", 0, 0, GraphSize.x, GraphSize.y);
            content.anchorMin = content.anchorMax = new Vector2(.5f, .5f);
            content.pivot = new Vector2(.5f, .5f);
            content.anchoredPosition = Vector2.zero;
            viewport.viewport = clip;
            viewport.content = content;
            viewport.horizontal = viewport.vertical = true;
            viewport.inertia = true;
            viewport.decelerationRate = .08f;
            viewport.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            links = Rect(content, "Connections", 0, 0, GraphSize.x, GraphSize.y).gameObject.AddComponent<PentaUpgradeLinks>();
            var labelMasks = new List<Rect>();
            foreach (var node in PentaUpgradeCatalog.Nodes)
            {
                Vector2 center = NodePosition(node);
                bool origin = node.Id == "origin";
                var frame = Box(content, "Node_" + node.Id, center.x - 110, center.y - 75,
                    220, 190, PentaUpgradeChrome.Style.TreeNode);
                frame.raycastTarget = true;
                var button = frame.gameObject.AddComponent<UnityEngine.UI.Button>();
                ButtonFeedback(button, frame);
                var glyph = Glyph(frame.transform, "Glyph", 78, 43, 64, IconFor(node), Color.white);
                if (origin) { glyph.gameObject.SetActive(false); Seal(frame.transform, "OriginSeal", 69, 34, 82, 82); }
                var label = Label(frame.transform, "Name", node.Name, 0, 151,
                    220, 36, 24, White, TextAlignmentOptions.Center);
                float textWidth = Mathf.Min(220, label.GetPreferredValues(node.Name).x + 16);
                labelMasks.Add(new Rect(center.x - textWidth * .5f, -(center.y + 116), textWidth, 44));
                nodeViews.Add(new NodeUI { Id = node.Id, Button = button, Frame = frame, Icon = glyph, Label = label });
            }
            var edges = new List<PentaUpgradeLinks.Link>();
            foreach (var node in PentaUpgradeCatalog.Nodes)
                foreach (string parent in node.ParentIds)
                {
                    var route = PentaUpgradeVerticalLayout.Route(node.Id);
                    // The layout stores centre-to-centre routes. Stop at the diamond rims.
                    TrimDiamondPort(route, 0, 1);
                    TrimDiamondPort(route, route.Length - 1, route.Length - 2);
                    for (int i = 0; i < route.Length; i++) route[i].y = -route[i].y;
                    edges.Add(new PentaUpgradeLinks.Link { ParentId = parent, ChildId = node.Id,
                        Start = route[0], End = route[route.Length - 1], Points = route });
                }
            links.Configure(edges, labelMasks.ToArray());
        }

        static void TrimDiamondPort(Vector2[] route, int end, int adjacent)
        {
            Vector2 delta = route[adjacent] - route[end];
            float distance = Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
            // Some routes end in a short straight port; extend the trimming through it.
            if (distance < 68 && route.Length > 2)
            {
                int next = adjacent + (adjacent > end ? 1 : -1);
                Vector2 remaining = route[next] - route[adjacent];
                float remainingDistance = Mathf.Abs(remaining.x) + Mathf.Abs(remaining.y);
                route[end] = route[adjacent] + remaining * Mathf.Min(1, (68 - distance) / remainingDistance);
                route[adjacent] = route[end];
            }
            else if (distance > 0) route[end] += delta * Mathf.Min(1, 68 / distance);
        }

        void BuildDetails(Transform panel)
        {
            var detail = Box(panel, "SelectedNodePanel", 1270, 143, 430, 580, PentaUpgradeChrome.Style.Detail);
            var p = detail.transform;
            detailFamily = Label(p, "Family", "", 28, 34, 260, 30, 15, Accent);
            detailTitle = Label(p, "NodeTitle", "", 28, 72, 295, 50, 30, White);
            Box(p, "IconMedallion", 332, 40, 70, 70, PentaUpgradeChrome.Style.Diamond);
            detailIcon = Glyph(p, "SelectedIcon", 342, 50, 50, SymbolKind.Health, Color.white);
            Rule(p, "DetailRule", 28, 140, 374, 1, Border);
            Label(p, "DescriptionHeading", "효과 초안", 28, 163, 360, 30, 17, Muted);
            detailDescription = Label(p, "Description", "", 28, 207, 374, 152, 21, White);
            detailDescription.lineSpacing = 12;
            Label(p, "DraftNotice", "이름과 효과는 검토 중입니다", 28, 360, 374, 22, 14, Muted);
            Rule(p, "ConnectionsRule", 28, 386, 374, 1, Border);
            Label(p, "ParentsHeading", "앞선 각인", 28, 408, 360, 28, 16, Muted);
            parentButtons = new[] { Button(p, "Parent0", "", 28, 447, 374, 40) };
            parentHint = Label(p, "RootHint", "트리가 시작되는 자리", 28, 447, 374, 40, 16, Muted);
            focusButton = Button(p, "ShowRoute", "선택한 각인으로", 28, 520, 374, 36, true);
        }

        static SymbolKind IconFor(PentaUpgradeNode node)
        {
            switch (node.Family)
            {
                case "시작점": return SymbolKind.Upgrade;
                case "체력": case "부활": return SymbolKind.Health;
                case "상점": case "현상 사냥": return SymbolKind.Coins;
                case "전리품": case "오행 수집": return SymbolKind.Relic;
                case "회피": return SymbolKind.Dodge;
                case "탐색": return SymbolKind.Explore;
                default: return SymbolKind.Attack;
            }
        }

        public static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(width, height);
            return rt;
        }

        public static void Fill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        static PentaUpgradeChrome Box(Transform parent, string name, float x, float y, float width, float height, PentaUpgradeChrome.Style mode)
        {
            var rect = Rect(parent, name, x, y, width, height);
            rect.gameObject.AddComponent<CanvasRenderer>();
            var graphic = rect.gameObject.AddComponent<PentaUpgradeChrome>();
            graphic.Configure(mode);
            return graphic;
        }

        TMP_Text Label(Transform parent, string name, string value, float x, float y, float width, float height,
            float size, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        {
            var text = Rect(parent, name, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.text = value; text.fontSize = size; text.color = color;
            text.alignment = alignment; text.raycastTarget = false;
            if (size >= 28) text.fontStyle = FontStyles.Bold;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        static PentaUpgradeSymbol Glyph(Transform parent, string name, float x, float y, float size, SymbolKind kind, Color tint)
        {
            var rect = Rect(parent, name, x, y, size, size);
            rect.gameObject.AddComponent<CanvasRenderer>();
            var glyph = rect.gameObject.AddComponent<PentaUpgradeSymbol>();
            glyph.Configure(kind, tint);
            return glyph;
        }

        UnityEngine.UI.Button Button(Transform parent, string name, string value, float x, float y, float width, float height, bool primary = false)
        {
            var graphic = Box(parent, name, x, y, width, height, primary ? PentaUpgradeChrome.Style.PrimaryButton : PentaUpgradeChrome.Style.Button);
            graphic.raycastTarget = true;
            var button = graphic.gameObject.AddComponent<UnityEngine.UI.Button>();
            ButtonFeedback(button, graphic);
            Label(graphic.transform, "Label", value, 8, 0, width - 16, height, 16, White, TextAlignmentOptions.Center);
            return button;
        }

        public static void ButtonFeedback(UnityEngine.UI.Button button, UnityEngine.UI.Graphic graphic)
        {
            button.targetGraphic = graphic;
            button.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = new Color(.76f, .79f, .85f);
            colors.highlightedColor = new Color(1f, .88f, .91f);
            colors.selectedColor = new Color(.92f, .93f, 1f);
            colors.pressedColor = new Color(.58f, .58f, .67f);
            colors.disabledColor = new Color(.44f, .47f, .53f, .82f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = .1f;
            button.colors = colors;
        }

        void Seal(Transform parent, string name, float x, float y, float width, float height)
        {
            var art = Rect(parent, name, x, y, width, height).gameObject.AddComponent<UnityEngine.UI.Image>();
            art.sprite = seal;
            art.preserveAspect = true;
            art.raycastTarget = false;
        }

        static void Rule(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            var rule = Rect(parent, name, x, y, width, height).gameObject.AddComponent<UnityEngine.UI.Image>();
            rule.color = color; rule.raycastTarget = false;
        }
    }
}
