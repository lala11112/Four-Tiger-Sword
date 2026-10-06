using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public sealed partial class AboutWindow : EditorWindow
    {
        private const int WINDOW_WIDTH = 690;
        private const int WINDOW_HEIGHT = 340;
        private const float LOGO_SIZE = 180;
        private const float GRID_LOGO_SIZE = 100;
        private const float CONTENT_WIDTH = 600;
        private const float UITK_GRID_LOGO_SIZE = 78;
        private const float UITK_TOOL_CARD_WIDTH = 292;
        private const float UITK_TOOL_CARD_HEIGHT = 128;

        private string _toolId;
        private Action _customSection;

        // Cached data — resolved once in Reload(), reused every frame
        private ToolCatalog _catalog;
        private ToolInfo _toolInfo;
        private string _version;
        private List<ToolInfo> _otherTools;
        private Texture2D _logo;
        private Dictionary<string, Texture2D> _gridLogos;
        private ScrollView _scrollView;

        public static void Show(string toolId, Action customSection = null)
        {
            AboutWindow window = GetWindow<AboutWindow>(true, "About");
            window._toolId = toolId;
            window._customSection = customSection;
            window.minSize = new Vector2(WINDOW_WIDTH, WINDOW_HEIGHT);
            window.Reload();
            window.ShowUtility();
        }

        private void CreateGUI()
        {
            Build();
        }

        private void Reload()
        {
            _catalog = ToolCatalog.Load();
            _toolInfo = _catalog.GetTool(_toolId);
            _version = ToolCatalog.ResolveVersion(_toolId);
            _otherTools = _catalog.GetOtherTools(_toolId);
            _logo = _toolInfo != null ? CommonUIStyles.LoadTexture(_toolInfo.logoTextureName) : null;
            _gridLogos = new Dictionary<string, Texture2D>();
            if (_otherTools != null)
            {
                foreach (ToolInfo t in _otherTools)
                {
                    _gridLogos[t.id] = CommonUIStyles.LoadTexture(t.logoTextureName);
                }
            }
            BuildIfReady();
        }

        private void BuildIfReady()
        {
            if (rootVisualElement != null && rootVisualElement.panel != null)
            {
                Build();
            }
        }

        private void Build()
        {
            VisualElement root = rootVisualElement;
            if (root == null) return;

            root.Clear();
            root.style.flexGrow = 1f;

            if (string.IsNullOrEmpty(_toolId))
            {
                root.Add(new HelpBox("Tool ID not set. Please reopen this window.", HelpBoxMessageType.Warning));
                return;
            }
            if (_catalog == null || _gridLogos == null)
            {
                Reload();
                return;
            }

            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.style.flexGrow = 1f;
            _scrollView.Add(CreateContentCached(_catalog, _toolInfo, _version, _otherTools, _logo, _gridLogos));
            root.Add(_scrollView);
        }

        /// <summary>
        /// Draws about content inline (e.g. embedded in a tab). Caches data in a static holder
        /// so repeated OnGUI calls don't re-resolve textures and versions every frame.
        /// </summary>
        [Obsolete("Use CreateContent to embed the retained UI Toolkit about view. This IMGUI compatibility method will be removed in the next major version.")]
        public static void DrawContent(string toolId, Action customSection = null)
        {
            // Use a simple static cache keyed by toolId to avoid per-frame asset lookups
            if (_inlineCache == null || _inlineCache.ToolId != toolId)
            {
                _inlineCache = new InlineCache(toolId);
            }

            DrawContentCached(
                _inlineCache.Catalog, _inlineCache.ToolInfo, _inlineCache.Version,
                _inlineCache.OtherTools, _inlineCache.Logo, _inlineCache.GridLogos,
                customSection);
        }

        public static VisualElement CreateContent(string toolId, Action<VisualElement> customSection = null)
        {
            if (_inlineCache == null || _inlineCache.ToolId != toolId)
            {
                _inlineCache = new InlineCache(toolId);
            }

            return CreateContentCached(
                _inlineCache.Catalog, _inlineCache.ToolInfo, _inlineCache.Version,
                _inlineCache.OtherTools, _inlineCache.Logo, _inlineCache.GridLogos,
                customSection);
        }

        private static InlineCache _inlineCache;

        private sealed class InlineCache
        {
            public readonly string ToolId;
            public readonly ToolCatalog Catalog;
            public readonly ToolInfo ToolInfo;
            public readonly string Version;
            public readonly List<ToolInfo> OtherTools;
            public readonly Texture2D Logo;
            public readonly Dictionary<string, Texture2D> GridLogos;

            public InlineCache(string toolId)
            {
                ToolId = toolId;
                Catalog = ToolCatalog.Load();
                ToolInfo = Catalog.GetTool(toolId);
                Version = ToolCatalog.ResolveVersion(toolId);
                OtherTools = Catalog.GetOtherTools(toolId);
                Logo = ToolInfo != null ? CommonUIStyles.LoadTexture(ToolInfo.logoTextureName) : null;
                GridLogos = new Dictionary<string, Texture2D>();
                if (OtherTools != null)
                {
                    foreach (ToolInfo t in OtherTools)
                    {
                        GridLogos[t.id] = CommonUIStyles.LoadTexture(t.logoTextureName);
                    }
                }
            }
        }

        private static void DrawContentCached(
            ToolCatalog catalog, ToolInfo toolInfo, string version,
            List<ToolInfo> otherTools, Texture2D logo,
            Dictionary<string, Texture2D> gridLogos,
            Action customSection)
        {
            if (toolInfo == null)
            {
                EditorGUILayout.HelpBox("Tool not found in tools.json.", MessageType.Warning);
                return;
            }

            GUIStyle textColor = EditorGUIUtility.isProSkin ? CommonUIStyles.whiteCenter : CommonUIStyles.blackCenter;

            EditorGUILayout.Space(6);

            // Publisher heading
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"A tool by {catalog.publisher}", CommonUIStyles.centerHeading, GUILayout.Width(350), GUILayout.Height(50));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Logo
            if (logo != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Box(logo, EditorStyles.centeredGreyMiniLabel, GUILayout.MaxWidth(LOGO_SIZE), GUILayout.MaxHeight(LOGO_SIZE));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            // Version
            EditorGUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"Version {version}", textColor, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(6);

            // Links row
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(toolInfo.webLink))
            {
                if (GUILayout.Button("Online Resources", CommonUIStyles.centerLinkLabel)) Application.OpenURL(toolInfo.webLink);
                EditorGUILayout.LabelField(" | ", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(10));
            }
            if (!string.IsNullOrEmpty(catalog.discordLink))
            {
                if (GUILayout.Button("Join Discord", CommonUIStyles.centerLinkLabel)) Application.OpenURL(catalog.discordLink);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(6);

            // Review CTA
            if (!string.IsNullOrEmpty(toolInfo.assetStoreLink))
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MaxWidth(480));
                EditorGUILayout.LabelField($"Enjoying {toolInfo.name}?", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("If you like this asset, please consider leaving a review on the Unity Asset Store.", EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space(2);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Write Review", GUILayout.Width(160))) Application.OpenURL(toolInfo.assetStoreLink);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                EditorGUILayout.Space(6);
            }

            // Other tools grid
            if (otherTools != null && otherTools.Count > 0)
            {
                EditorGUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.BeginVertical(GUILayout.MaxWidth(480));

                EditorGUILayout.LabelField("Other Tools You Might Like", CommonUIStyles.centerHeading, GUILayout.Height(30));
                EditorGUILayout.Space(4);

                DrawToolGrid(otherTools, gridLogos, catalog.discordLink);

                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(6);

            // Custom section (at the bottom)
            customSection?.Invoke();

            EditorGUILayout.Space(8);
        }

        private static VisualElement CreateContentCached(
            ToolCatalog catalog,
            ToolInfo toolInfo,
            string version,
            List<ToolInfo> otherTools,
            Texture2D logo,
            Dictionary<string, Texture2D> gridLogos,
            Action<VisualElement> customSection = null)
        {
            VisualElement root = new VisualElement();
            root.style.flexGrow = 1f;
            root.style.alignItems = Align.Center;
            root.style.minWidth = 0f;
            root.style.paddingLeft = 16f;
            root.style.paddingRight = 16f;
            root.style.paddingTop = 14f;
            root.style.paddingBottom = 16f;

            if (toolInfo == null)
            {
                root.Add(new HelpBox("Tool not found in tools.json.", HelpBoxMessageType.Warning));
                return root;
            }

            Label publisher = CreateCenteredLabel($"A tool by {catalog.publisher}", 18, true);
            publisher.style.marginBottom = 10f;
            root.Add(publisher);

            if (logo != null)
            {
                Image logoImage = new Image
                {
                    image = logo,
                    scaleMode = ScaleMode.ScaleToFit
                };
                logoImage.style.width = LOGO_SIZE;
                logoImage.style.height = LOGO_SIZE;
                logoImage.style.marginBottom = 8f;
                root.Add(logoImage);
            }

            Label versionLabel = CreateCenteredLabel($"Version {version}", 12, false);
            versionLabel.style.marginBottom = 10f;
            root.Add(versionLabel);

            VisualElement links = new VisualElement();
            links.style.flexDirection = FlexDirection.Row;
            links.style.flexWrap = Wrap.Wrap;
            links.style.justifyContent = Justify.Center;
            links.style.alignItems = Align.Center;
            links.style.marginBottom = 10f;
            if (!string.IsNullOrEmpty(toolInfo.webLink))
            {
                links.Add(CreateLinkButton("Online Resources", () => Application.OpenURL(toolInfo.webLink)));
            }
            if (!string.IsNullOrEmpty(toolInfo.webLink) && !string.IsNullOrEmpty(catalog.discordLink))
            {
                Label separator = new Label("|");
                separator.style.marginLeft = 8f;
                separator.style.marginRight = 8f;
                links.Add(separator);
            }
            if (!string.IsNullOrEmpty(catalog.discordLink))
            {
                links.Add(CreateLinkButton("Join Discord", () => Application.OpenURL(catalog.discordLink)));
            }
            if (links.childCount > 0)
            {
                root.Add(links);
            }

            if (!string.IsNullOrEmpty(toolInfo.assetStoreLink))
            {
                VisualElement review = CreatePanel(CONTENT_WIDTH);
                review.style.minHeight = 104f;
                review.Add(CreatePanelTitle($"Enjoying {toolInfo.name}?"));
                Label reviewText = new Label("If you like this asset, please consider leaving a review on the Unity Asset Store.");
                reviewText.style.whiteSpace = WhiteSpace.Normal;
                reviewText.style.marginBottom = 6f;
                review.Add(reviewText);

                Button reviewButton = new Button(() => Application.OpenURL(toolInfo.assetStoreLink)) {text = "Write Review"};
                reviewButton.style.alignSelf = Align.Center;
                reviewButton.style.minWidth = 160f;
                review.Add(reviewButton);
                root.Add(review);
            }

            if (otherTools != null && otherTools.Count > 0)
            {
                Label otherTitle = CreateCenteredLabel("Other Tools You Might Like", 16, true);
                otherTitle.style.marginTop = 8f;
                otherTitle.style.marginBottom = 8f;
                root.Add(otherTitle);
                root.Add(CreateToolGrid(otherTools, gridLogos, catalog.discordLink));
            }

            customSection?.Invoke(root);
            return root;
        }

        private static Label CreateCenteredLabel(string text, int fontSize, bool bold)
        {
            Label label = new Label(text);
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.fontSize = fontSize;
            if (bold)
            {
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
            }
            return label;
        }

        private static Label CreatePanelTitle(string text)
        {
            Label label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginBottom = 4f;
            return label;
        }

        private static VisualElement CreatePanel(float width)
        {
            VisualElement panel = new VisualElement();
            panel.style.width = width;
            panel.style.maxWidth = Length.Percent(100f);
            panel.style.flexShrink = 0f;
            panel.style.borderBottomWidth = 1f;
            panel.style.borderTopWidth = 1f;
            panel.style.borderLeftWidth = 1f;
            panel.style.borderRightWidth = 1f;
            panel.style.borderBottomColor = new Color(0.5f, 0.5f, 0.5f, 0.35f);
            panel.style.borderTopColor = new Color(0.5f, 0.5f, 0.5f, 0.35f);
            panel.style.borderLeftColor = new Color(0.5f, 0.5f, 0.5f, 0.35f);
            panel.style.borderRightColor = new Color(0.5f, 0.5f, 0.5f, 0.35f);
            panel.style.backgroundColor = new Color(1f, 1f, 1f, EditorGUIUtility.isProSkin ? 0.04f : 0.28f);
            panel.style.paddingLeft = 10f;
            panel.style.paddingRight = 10f;
            panel.style.paddingTop = 9f;
            panel.style.paddingBottom = 9f;
            panel.style.marginBottom = 10f;
            return panel;
        }

        private static Button CreateLinkButton(string text, Action click)
        {
            Button button = new Button(click) {text = text};
            button.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
            button.style.borderBottomWidth = 0f;
            button.style.borderTopWidth = 0f;
            button.style.borderLeftWidth = 0f;
            button.style.borderRightWidth = 0f;
            button.style.color = EditorGUIUtility.isProSkin
                ? new Color(0.45f, 0.68f, 0.95f, 1f)
                : new Color(0.12f, 0.36f, 0.68f, 1f);
            return button;
        }

        private static VisualElement CreateToolGrid(List<ToolInfo> tools, Dictionary<string, Texture2D> logos, string discordLink)
        {
            VisualElement grid = new VisualElement();
            grid.style.width = CONTENT_WIDTH;
            grid.style.maxWidth = Length.Percent(100f);
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.justifyContent = Justify.Center;
            grid.style.alignItems = Align.FlexStart;

            foreach (ToolInfo tool in tools)
            {
                logos.TryGetValue(tool.id, out Texture2D logo);
                grid.Add(CreateToolCard(tool, logo, discordLink));
            }

            return grid;
        }

        private static VisualElement CreateToolCard(ToolInfo tool, Texture2D logo, string discordLink)
        {
            VisualElement card = CreatePanel(UITK_TOOL_CARD_WIDTH);
            card.style.flexDirection = FlexDirection.Row;
            card.style.height = UITK_TOOL_CARD_HEIGHT;
            card.style.minHeight = UITK_TOOL_CARD_HEIGHT;
            card.style.maxHeight = UITK_TOOL_CARD_HEIGHT;
            card.style.marginLeft = 4f;
            card.style.marginRight = 4f;

            if (logo != null)
            {
                Image image = new Image
                {
                    image = logo,
                    scaleMode = ScaleMode.ScaleToFit
                };
                image.style.width = UITK_GRID_LOGO_SIZE;
                image.style.height = UITK_GRID_LOGO_SIZE;
                image.style.marginRight = 8f;
                image.style.flexShrink = 0f;
                card.Add(image);
            }

            VisualElement text = new VisualElement();
            text.style.flexGrow = 1f;
            text.style.flexShrink = 1f;
            text.style.minWidth = 0f;

            string displayName = tool.isAddon ? $"{tool.name} (Add-on)" : tool.name;
            Label name = CreatePanelTitle(displayName);
            name.style.whiteSpace = WhiteSpace.Normal;
            text.Add(name);

            Label description = new Label(tool.description);
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.marginBottom = 4f;
            description.style.flexShrink = 1f;
            text.Add(description);

            if (!string.IsNullOrEmpty(tool.assetStoreLink))
            {
                text.Add(CreateLinkButton("View on Asset Store", () => Application.OpenURL(tool.assetStoreLink)));
            }
            else if (!string.IsNullOrEmpty(discordLink))
            {
                text.Add(CreateLinkButton("Join Beta", () => Application.OpenURL(discordLink)));
            }
            else if (!string.IsNullOrEmpty(tool.webLink))
            {
                text.Add(CreateLinkButton("Learn More", () => Application.OpenURL(tool.webLink)));
            }

            card.Add(text);
            return card;
        }

        private static void DrawToolGrid(List<ToolInfo> tools, Dictionary<string, Texture2D> logos, string discordLink)
        {
            const int columns = 2;
            const float cellPadding = 8;
            float cellWidth = (480 - cellPadding) / columns;

            for (int i = 0; i < tools.Count; i += columns)
            {
                GUILayout.BeginHorizontal();
                for (int j = 0; j < columns && i + j < tools.Count; j++)
                {
                    if (j > 0) GUILayout.Space(cellPadding);
                    ToolInfo tool = tools[i + j];
                    logos.TryGetValue(tool.id, out Texture2D tex);
                    DrawToolCell(tool, tex, cellPadding, discordLink, cellWidth);
                }
                // fill remaining cells if odd count
                if (i + columns > tools.Count && tools.Count % columns != 0)
                {
                    GUILayout.Space(cellPadding);
                    GUILayout.BeginVertical(GUILayout.Width(cellWidth));
                    GUILayout.FlexibleSpace();
                    GUILayout.EndVertical();
                }
                GUILayout.EndHorizontal();
                EditorGUILayout.Space(cellPadding);
            }
        }

        private static void DrawToolCell(ToolInfo tool, Texture2D tex, float padding, string discordLink, float cellWidth)
        {
            GUILayout.BeginVertical(CommonUIStyles.sectionBox, GUILayout.Width(cellWidth));

            GUILayout.BeginHorizontal();

            // Logo thumbnail
            if (tex != null)
            {
                GUILayout.Box(tex, GUIStyle.none, GUILayout.Width(GRID_LOGO_SIZE), GUILayout.Height(GRID_LOGO_SIZE));
            }
            else
            {
                GUILayout.Space(GRID_LOGO_SIZE);
            }

            GUILayout.Space(padding);

            // Text column — constrain width to prevent addon badge from expanding the cell
            float textWidth = cellWidth - GRID_LOGO_SIZE - padding - 12;
            GUILayout.BeginVertical(GUILayout.Width(textWidth), GUILayout.MaxWidth(textWidth));

            // Name
            string displayName = tool.isAddon ? $"{tool.name} (Add-on)" : tool.name;
            EditorGUILayout.LabelField(displayName, EditorStyles.boldLabel);

            EditorGUILayout.LabelField(tool.description, EditorStyles.wordWrappedMiniLabel);

            // Link button
            if (!string.IsNullOrEmpty(tool.assetStoreLink))
            {
                if (GUILayout.Button("View on Asset Store", EditorStyles.linkLabel)) Application.OpenURL(tool.assetStoreLink);
            }
            else if (!string.IsNullOrEmpty(discordLink))
            {
                if (GUILayout.Button("Join Beta", EditorStyles.linkLabel)) Application.OpenURL(discordLink);
            }
            else if (!string.IsNullOrEmpty(tool.webLink))
            {
                if (GUILayout.Button("Learn More", EditorStyles.linkLabel)) Application.OpenURL(tool.webLink);
            }
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }
    }
}
