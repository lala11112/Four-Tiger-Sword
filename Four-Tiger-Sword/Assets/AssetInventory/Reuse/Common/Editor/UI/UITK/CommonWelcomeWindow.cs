using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public enum CommonWelcomeAccent
    {
        Blue,
        Cyan,
        Violet,
        Amber
    }

    public sealed class CommonWelcomeStep
    {
        public CommonWelcomeStep(string title, string description)
        {
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
        }

        public string Title { get; }
        public string Description { get; }
    }

    public sealed class CommonWelcomeContent
    {
        public Texture Logo { get; set; }
        public string ProductName { get; set; }
        public string Headline { get; set; }
        public string Description { get; set; }
        public string SectionTitle { get; set; }
        public string SectionDescription { get; set; }
        public IReadOnlyList<CommonWelcomeStep> Steps { get; set; }
        public CommonWelcomeAccent Accent { get; set; } = CommonWelcomeAccent.Blue;
    }

    /// <summary>
    /// Shared branded shell for concise product welcome windows.
    /// </summary>
    public static class CommonWelcomeWindow
    {
        public const float DefaultMinWidth = 520f;
        public const float DefaultMinHeight = 600f;
        public const float DefaultMaxWidth = 720f;
        public const float DefaultMaxHeight = 760f;

        const string AnchorAssetName = "CommonWelcomeWindow";
        const string AnchorPathSuffix = "Editor/UI/UITK/CommonWelcomeWindow.cs";
        const string StylePathSuffix = "Editor/UI/UITK/CommonWelcomeWindow.uss";
        const float CompactWidth = 560f;

        static readonly string[] AccentClasses =
        {
            "common-welcome-accent--blue",
            "common-welcome-accent--cyan",
            "common-welcome-accent--violet",
            "common-welcome-accent--amber"
        };

        public static T ShowUtility<T>(string title) where T : EditorWindow
        {
            T[] openWindows = Resources.FindObjectsOfTypeAll<T>();
            for (int i = 0; i < openWindows.Length; i++)
            {
                if (openWindows[i] != null)
                    openWindows[i].Close();
            }

            T window = ScriptableObject.CreateInstance<T>();
            ApplyDefaultConstraints(window, title);
            Rect mainWindow = EditorGUIUtility.GetMainWindowPosition();
            Rect centeredPosition = new Rect(
                Mathf.Round(mainWindow.x + (mainWindow.width - DefaultMinWidth) * 0.5f),
                Mathf.Round(mainWindow.y + (mainWindow.height - DefaultMinHeight) * 0.5f),
                DefaultMinWidth,
                DefaultMinHeight);
            window.ShowUtility();
            window.position = centeredPosition;
            window.Focus();
            return window;
        }

        public static void ApplyDefaultConstraints(EditorWindow window, string title = null)
        {
            if (window == null)
                throw new ArgumentNullException(nameof(window));

            window.minSize = new Vector2(DefaultMinWidth, DefaultMinHeight);
            window.maxSize = new Vector2(DefaultMaxWidth, DefaultMaxHeight);
            if (!string.IsNullOrWhiteSpace(title))
                window.titleContent = new GUIContent(title);
        }

        public static Button CreateAction(string text, string tooltip, Action click, bool primary = false)
        {
            Button button = CommonUITK.CreateButton(
                text,
                click,
                "common-welcome-action");
            button.tooltip = tooltip ?? string.Empty;
            button.EnableInClassList("common-welcome-action--primary", primary);
            return button;
        }

        public static VisualElement Build(
            VisualElement host,
            CommonWelcomeContent content,
            params Button[] actions)
        {
            if (host == null)
                throw new ArgumentNullException(nameof(host));
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            host.Clear();

            VisualElement root = CommonUITK.CreateContainer();
            CommonUITK.ApplyRoot(
                root,
                LoadStyleSheet(),
                "common-welcome-root",
                "common-welcome-theme-dark",
                "common-welcome-theme-light");
            ApplyAccent(root, content.Accent);
            root.RegisterCallback<GeometryChangedEvent>(evt =>
                root.EnableInClassList("common-welcome--compact", evt.newRect.width < CompactWidth));
            host.Add(root);

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("common-welcome-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            VisualElement body = CommonUITK.CreateContainer("common-welcome-content");
            body.Add(CreateHero(content));
            body.Add(CreateSectionHeading(content));
            body.Add(CreateSteps(content.Steps));
            scroll.Add(body);
            root.Add(scroll);

            if (actions != null && actions.Length > 0)
            {
                VisualElement footer = CommonUITK.CreateWindowFooter(
                    0f,
                    0f,
                    "common-welcome-footer");
                footer.style.marginTop = 0f;
                for (int i = 0; i < actions.Length; i++)
                {
                    if (actions[i] != null)
                        footer.Add(actions[i]);
                }

                root.Add(footer);
            }

            return root;
        }

        public static StyleSheet LoadStyleSheet()
        {
            return CommonUITK.LoadStyleSheetFromAnchor(
                AnchorAssetName,
                AnchorPathSuffix,
                StylePathSuffix);
        }

        static VisualElement CreateHero(CommonWelcomeContent content)
        {
            VisualElement hero = CommonUITK.CreateContainer("common-welcome-hero");
            if (content.Logo != null)
            {
                Image image = new Image
                {
                    image = content.Logo,
                    scaleMode = ScaleMode.ScaleToFit,
                    pickingMode = PickingMode.Ignore
                };
                image.AddToClassList("common-welcome-logo");
                hero.Add(image);
            }

            VisualElement copy = CommonUITK.CreateContainer("common-welcome-hero-copy");
            copy.Add(CommonUITK.CreateLabel(
                content.ProductName ?? string.Empty,
                "common-welcome-eyebrow"));
            copy.Add(CommonUITK.CreateLabel(
                content.Headline ?? string.Empty,
                "common-welcome-title"));
            copy.Add(CommonUITK.CreateLabel(
                content.Description ?? string.Empty,
                "common-welcome-intro"));
            hero.Add(copy);
            return hero;
        }

        static VisualElement CreateSectionHeading(CommonWelcomeContent content)
        {
            VisualElement heading = CommonUITK.CreateContainer("common-welcome-heading");
            heading.Add(CommonUITK.CreateLabel(
                content.SectionTitle ?? string.Empty,
                "common-welcome-section-title"));
            heading.Add(CommonUITK.CreateLabel(
                content.SectionDescription ?? string.Empty,
                "common-welcome-section-description"));
            return heading;
        }

        static VisualElement CreateSteps(IReadOnlyList<CommonWelcomeStep> steps)
        {
            VisualElement list = CommonUITK.CreateContainer("common-welcome-steps");
            if (steps == null)
                return list;

            for (int i = 0; i < steps.Count; i++)
            {
                CommonWelcomeStep step = steps[i];
                if (step == null)
                    continue;

                VisualElement card = CommonUITK.CreateContainer("common-welcome-step");
                VisualElement badge = CommonUITK.CreateContainer("common-welcome-step-badge");
                badge.Add(CommonUITK.CreateLabel(
                    (i + 1).ToString(),
                    "common-welcome-step-number"));
                card.Add(badge);

                VisualElement copy = CommonUITK.CreateContainer("common-welcome-step-copy");
                copy.Add(CommonUITK.CreateLabel(
                    step.Title,
                    "common-welcome-step-title"));
                copy.Add(CommonUITK.CreateLabel(
                    step.Description,
                    "common-welcome-step-description"));
                card.Add(copy);
                list.Add(card);
            }

            return list;
        }

        static void ApplyAccent(VisualElement root, CommonWelcomeAccent accent)
        {
            int selectedIndex = Mathf.Clamp((int)accent, 0, AccentClasses.Length - 1);
            for (int i = 0; i < AccentClasses.Length; i++)
            {
                root.EnableInClassList(AccentClasses[i], i == selectedIndex);
            }
        }
    }
}
