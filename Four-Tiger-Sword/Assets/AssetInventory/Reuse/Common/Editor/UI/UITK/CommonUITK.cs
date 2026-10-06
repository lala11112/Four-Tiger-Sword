using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    /// <summary>
    /// Small UI Toolkit helpers for editor tooling. Product-specific styling stays in each tool's USS.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static partial class CommonUITK
    {
        public const string WindowFooterClass = "common-window-footer";
        public const string WindowBodyClass = "common-window-body";

        /// <summary>
        /// Applies standalone window spacing without insetting scrollbars. Mark split or tab hosts with WindowBodyClass and apply this layout to their individual scrolling panes. Inspector roots retain their own padding unless explicitly opted into this layout.
        /// </summary>
        public static void ApplyWindowLayout(VisualElement root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            StyleSheet sheet = LoadStyleSheetFromAnchor(
                "CommonUITK", "Editor/UI/UITK/CommonUITK.cs", "Editor/UI/UITK/CommonWindow.uss");
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            root.AddToClassList("common-window-root");
            root.style.paddingLeft = 0f;
            root.style.paddingRight = 0f;
            root.style.paddingTop = 8f;
            root.style.paddingBottom = 0f;
        }

        static readonly Dictionary<string, StyleSheet> StyleSheetCache = new Dictionary<string, StyleSheet>();

        public sealed class AdvancedVisibilityClasses
        {
            public string RootClass;
            public string DefaultClass;
            public string AdvancedClass;
            public string InlineClass;
            public string BodyClass;
            public string ControlsClass;
            public string InlineControlsClass;
            public string ToggleButtonClass;
        }

        public sealed class SavedSearchPillClasses
        {
            public string GroupClass;
            public string PillClass;
            public string PillWithMenuClass;
            public string ActiveClass;
            public string PillContentClass;
            public string IconClass;
            public string TextClass;
            public string MenuClass;
            public string ButtonClass;
            public string IconButtonClass;
            public string IconButtonImageClass;
        }

        public sealed class SegmentedControlClasses
        {
            public string RootClass;
            public string ButtonBaseClass;
            public string ButtonClass;
            public string IconOnlyButtonClass;
            public string ActiveButtonClass;
            public string IconClass;
            public string FirstButtonClass;
            public string MiddleButtonClass;
            public string LastButtonClass;
        }

        public sealed class WizardShellClasses
        {
            public string RootClass;
            public string SidebarClass;
            public string SidebarTitleClass;
            public string StepButtonClass;
            public string ActiveStepClass;
            public string CompletedStepClass;
            public string LogoClass;
            public string ContentClass;
            public string HeaderClass;
            public string TitleClass;
            public string DescriptionClass;
            public string BodyClass;
            public string FooterClass;
            public bool NumberSteps = true;
        }

        public sealed class WizardStep
        {
            public string Title;
            public bool IsActive;
            public bool IsCompleted;
            public bool IsEnabled = true;
            public Action OnClick;
        }

        public sealed class ThreeZoneLayout
        {
            internal ThreeZoneLayout(VisualElement root, VisualElement left, VisualElement center, VisualElement right)
            {
                Root = root;
                Left = left;
                Center = center;
                Right = right;
            }

            public VisualElement Root { get; }
            public VisualElement Left { get; }
            public VisualElement Center { get; }
            public VisualElement Right { get; }
        }

        public static StyleSheet LoadStyleSheetFromAnchor(string anchorAssetName, string anchorPathSuffix, string stylePathSuffix)
        {
            if (string.IsNullOrWhiteSpace(anchorAssetName) ||
                string.IsNullOrWhiteSpace(anchorPathSuffix) ||
                string.IsNullOrWhiteSpace(stylePathSuffix))
            {
                return null;
            }

            string normalizedAnchorSuffix = anchorPathSuffix.Replace('\\', '/');
            string normalizedStyleSuffix = stylePathSuffix.Replace('\\', '/');
            string cacheKey = anchorAssetName + "|" + normalizedAnchorSuffix + "|" + normalizedStyleSuffix;
            if (StyleSheetCache.TryGetValue(cacheKey, out StyleSheet cachedStyleSheet) && cachedStyleSheet != null)
                return cachedStyleSheet;

            string[] guids = AssetDatabase.FindAssets(anchorAssetName + " t:MonoScript");
            for (int i = 0; i < guids.Length; i++)
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(guids[i]).Replace('\\', '/');
                if (!scriptPath.EndsWith(normalizedAnchorSuffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string packageRoot = scriptPath.Substring(0, scriptPath.Length - normalizedAnchorSuffix.Length);
                StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(packageRoot + normalizedStyleSuffix);
                if (styleSheet != null)
                    StyleSheetCache[cacheKey] = styleSheet;
                return styleSheet;
            }

            return null;
        }

        public static void ApplyRoot(VisualElement root, StyleSheet styleSheet, string rootClass, string darkClass = null, string lightClass = null)
        {
            if (root == null) return;

            AddClasses(root, rootClass);
            if (!string.IsNullOrWhiteSpace(darkClass))
            {
                root.EnableInClassList(darkClass, EditorGUIUtility.isProSkin);
            }
            if (!string.IsNullOrWhiteSpace(lightClass))
            {
                root.EnableInClassList(lightClass, !EditorGUIUtility.isProSkin);
            }

            root.style.flexGrow = 1f;
            if (styleSheet != null && !root.styleSheets.Contains(styleSheet))
            {
                root.styleSheets.Add(styleSheet);
            }
        }

        public static VisualElement CreateContainer(params string[] classNames)
        {
            VisualElement element = new VisualElement();
            AddClasses(element, classNames);
            return element;
        }

        public static ThreeZoneLayout CreateThreeZoneLayout(
            string rootClass,
            string groupClass,
            string leftClass,
            string centerClass,
            string rightClass)
        {
            VisualElement root = CreateContainer(rootClass);
            VisualElement left = CreateContainer(groupClass, leftClass);
            VisualElement center = CreateContainer(groupClass, centerClass);
            VisualElement right = CreateContainer(groupClass, rightClass);
            root.Add(left);
            root.Add(center);
            root.Add(right);
            return new ThreeZoneLayout(root, left, center, right);
        }

        public static VisualElement CreateSection(string title, string sectionClass, string titleClass)
        {
            VisualElement section = CreateContainer(sectionClass);
            if (!string.IsNullOrWhiteSpace(title))
            {
                Label heading = CreateLabel(title, titleClass);
                section.Add(heading);
            }

            return section;
        }

        public static VisualElement CreateIconTextBox(string text, Texture icon, string boxClass, string modifierClass, string iconClass, string textClass)
        {
            VisualElement box = CreateContainer(boxClass, modifierClass);
            if (icon != null)
            {
                Image image = new Image
                {
                    image = icon,
                    scaleMode = ScaleMode.ScaleToFit
                };
                AddClasses(image, iconClass);
                box.Add(image);
            }

            Label label = CreateLabel(text ?? string.Empty, textClass);
            box.Add(label);
            return box;
        }

        public static HelpBox CreateHelpBox(string text, HelpBoxMessageType type, params string[] classNames)
        {
            HelpBox helpBox = new HelpBox(text ?? string.Empty, type);
            AddClasses(helpBox, classNames);
            return helpBox;
        }

        public static VisualElement CreateKeyValueRow(string label, string value, string rowClass, string labelClass, string valueClass)
        {
            VisualElement row = CreateContainer(rowClass);
            row.Add(CreateLabel(label, labelClass));
            row.Add(CreateLabel(value ?? string.Empty, valueClass));
            return row;
        }

        public static VisualElement CreateFieldRow(string label, VisualElement field, string rowClass, string labelClass, string controlClass)
        {
            VisualElement row = CreateContainer(rowClass);
            row.Add(CreateLabel(label, labelClass));
            if (field != null)
            {
                AddClasses(field, controlClass);
                row.Add(field);
            }

            return row;
        }

        public static Label CreateLabel(string text, params string[] classNames)
        {
            Label label = new Label(text ?? string.Empty);
            AddClasses(label, classNames);
            return label;
        }

        public static Color GetReadableTextColor(Color background)
        {
            float luminance = 0.2126f * background.r + 0.7152f * background.g + 0.0722f * background.b;
            return luminance > 0.58f
                ? new Color(0.08f, 0.08f, 0.08f, 1f)
                : new Color(0.96f, 0.96f, 0.96f, 1f);
        }

        public static Button CreateButton(string text, Action click, params string[] classNames)
        {
            Button button = new Button
            {
                text = text ?? string.Empty
            };
            if (click != null)
            {
                button.clicked += click;
            }
            AddClasses(button, classNames);
            return button;
        }

        /// <summary>
        /// Creates an opaque, non-shrinking command surface for the bottom of a standalone window.
        /// Insets should match the padding of the parent so the surface reaches the window edges.
        /// </summary>
        public static VisualElement CreateWindowFooter(
            float horizontalInset = 0f,
            float bottomInset = 0f,
            params string[] classNames)
        {
            float safeHorizontalInset = Mathf.Max(0f, horizontalInset);
            float safeBottomInset = Mathf.Max(0f, bottomInset);
            float horizontalPadding = Mathf.Max(12f, safeHorizontalInset);
            VisualElement footer = CreateContainer(WindowFooterClass);
            AddClasses(footer, classNames);
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.flexWrap = Wrap.Wrap;
            footer.style.justifyContent = Justify.Center;
            footer.style.alignItems = Align.Center;
            footer.style.flexShrink = 0f;
            footer.style.minWidth = 0f;
            footer.style.minHeight = 48f;
            footer.style.marginLeft = -safeHorizontalInset;
            footer.style.marginRight = -safeHorizontalInset;
            footer.style.marginTop = 8f;
            footer.style.marginBottom = -safeBottomInset;
            footer.style.paddingLeft = horizontalPadding;
            footer.style.paddingRight = horizontalPadding;
            footer.style.paddingTop = 8f;
            footer.style.paddingBottom = 8f;
            footer.style.borderTopWidth = 1f;
            footer.style.borderTopColor = EditorGUIUtility.isProSkin
                ? new Color(150f / 255f, 150f / 255f, 150f / 255f, 0.32f)
                : new Color(75f / 255f, 75f / 255f, 75f / 255f, 0.28f);
            footer.style.backgroundColor = EditorGUIUtility.isProSkin
                ? new Color(48f / 255f, 48f / 255f, 48f / 255f, 1f)
                : new Color(207f / 255f, 207f / 255f, 207f / 255f, 1f);
            footer.RegisterCallback<AttachToPanelEvent>(_ => PreserveWindowFooterButtonWidths(footer));
            return footer;
        }

        private static void PreserveWindowFooterButtonWidths(VisualElement footer)
        {
            footer.Query<Button>().ForEach(button => button.style.flexShrink = 0f);
        }

        public static Foldout CreateFoldout(string text, bool value, Action<bool> onChange, string tooltip = null, params string[] classNames)
        {
            Foldout foldout = new Foldout
            {
                text = text ?? string.Empty,
                value = value,
                tooltip = tooltip ?? string.Empty
            };
            AddClasses(foldout, classNames);
            if (onChange != null)
            {
                foldout.RegisterValueChangedCallback(evt =>
                {
                    if (evt.target == foldout) onChange(evt.newValue);
                });
            }
            return foldout;
        }

        public static Button CreateIconButton(string tooltip, string iconName, Action click, string buttonClass, string iconButtonClass, string iconImageClass)
        {
            Button button = CreateButton(string.Empty, click, buttonClass, iconButtonClass);
            button.tooltip = tooltip ?? string.Empty;

            Texture iconTexture = string.IsNullOrWhiteSpace(iconName) ? null : EditorGUIUtility.IconContent(iconName).image;
            if (iconTexture != null)
            {
                Image image = new Image
                {
                    image = iconTexture,
                    scaleMode = ScaleMode.ScaleToFit
                };
                AddClasses(image, iconImageClass);
                button.Add(image);
            }
            else
            {
                button.text = tooltip ?? string.Empty;
            }

            return button;
        }

        public static VisualElement CreateRemovablePill(
            string text,
            string tooltip,
            string iconName,
            Action remove,
            string textClass,
            string iconClass,
            params string[] rootClasses)
        {
            VisualElement pill = CreateContainer(rootClasses);
            pill.tooltip = tooltip ?? string.Empty;
            pill.focusable = true;
            pill.tabIndex = 0;
            pill.Add(CreateLabel(text, textClass));

            Texture iconTexture = string.IsNullOrWhiteSpace(iconName) ? null : EditorGUIUtility.IconContent(iconName).image;
            if (iconTexture != null)
            {
                Image icon = new Image
                {
                    image = iconTexture,
                    scaleMode = ScaleMode.ScaleToFit,
                    tooltip = tooltip ?? string.Empty
                };
                AddClasses(icon, iconClass);
                icon.style.display = DisplayStyle.None;
                pill.Add(icon);

                bool hovered = false;
                bool focused = false;
                Action updateIconVisibility = () =>
                    icon.style.display = hovered || focused ? DisplayStyle.Flex : DisplayStyle.None;
                pill.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    hovered = true;
                    updateIconVisibility();
                });
                pill.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    hovered = false;
                    updateIconVisibility();
                });
                pill.RegisterCallback<FocusInEvent>(_ =>
                {
                    focused = true;
                    updateIconVisibility();
                });
                pill.RegisterCallback<FocusOutEvent>(_ =>
                {
                    focused = false;
                    updateIconVisibility();
                });
            }

            pill.RegisterCallback<ClickEvent>(_ => remove?.Invoke());
            pill.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter && evt.keyCode != KeyCode.Space) return;
                remove?.Invoke();
                evt.StopPropagation();
            });
            return pill;
        }

        public static VisualElement CreateSavedSearchPillGroup(
            string label,
            string tooltip,
            string iconName,
            string htmlColor,
            bool active,
            bool hasMenu,
            Action onClick,
            Action<VisualElement> onMenuClick,
            object userData,
            SavedSearchPillClasses classes)
        {
            VisualElement group = CreateContainer(classes?.GroupClass);

            Button pill = CreateSavedSearchPill(label, tooltip, iconName, htmlColor, active, hasMenu, onClick, userData, classes);
            group.Add(pill);

            if (hasMenu)
            {
                Button menuButton = null;
                menuButton = CreateIconButton(
                    "Saved search actions",
                    "icon dropdown",
                    () => onMenuClick?.Invoke(menuButton),
                    classes?.ButtonClass,
                    classes?.IconButtonClass,
                    classes?.IconButtonImageClass);
                AddClasses(menuButton, classes?.MenuClass);
                EnableClass(menuButton, classes?.ActiveClass, active);
                ApplySavedSearchPillColor(menuButton, htmlColor, active);
                group.Add(menuButton);
            }

            return group;
        }

        public static Button CreateSavedSearchPill(
            string label,
            string tooltip,
            string iconName,
            string htmlColor,
            bool active,
            bool hasMenu,
            Action onClick,
            object userData,
            SavedSearchPillClasses classes)
        {
            Button button = new Button(() => onClick?.Invoke())
            {
                tooltip = tooltip ?? string.Empty,
                userData = userData
            };
            AddClasses(button, classes?.PillClass);
            EnableClass(button, classes?.PillWithMenuClass, hasMenu);
            EnableClass(button, classes?.ActiveClass, active);
            ApplySavedSearchPillColor(button, htmlColor, active);

            string labelText = string.IsNullOrWhiteSpace(label) ? "Search" : label;
            Texture iconTexture = string.IsNullOrWhiteSpace(iconName) ? null : EditorGUIUtility.IconContent(iconName).image;
            if (iconTexture == null)
            {
                button.text = labelText;
                return button;
            }

            VisualElement content = CreateContainer(classes?.PillContentClass);
            Image image = new Image
            {
                image = iconTexture,
                scaleMode = ScaleMode.ScaleToFit
            };
            AddClasses(image, classes?.IconClass);
            content.Add(image);

            Label text = CreateLabel(labelText, classes?.TextClass);
            content.Add(text);
            button.Add(content);
            return button;
        }

        public static void ApplySavedSearchPillColor(Button button, string htmlColor, bool active = false)
        {
            if (button == null || string.IsNullOrWhiteSpace(htmlColor)) return;

            string normalized = htmlColor.StartsWith("#", StringComparison.Ordinal) ? htmlColor : "#" + htmlColor;
            if (!ColorUtility.TryParseHtmlString(normalized, out Color color)) return;

            Color borderColor = new Color(color.r, color.g, color.b, active ? 1f : 0.72f);
            Color backgroundColor = new Color(color.r, color.g, color.b, active ? 0.58f : 0.16f);
            button.style.borderLeftColor = borderColor;
            button.style.borderRightColor = borderColor;
            button.style.borderTopColor = borderColor;
            button.style.borderBottomColor = borderColor;
            button.style.backgroundColor = backgroundColor;
        }

        public static VisualElement CreateSegmentedControl(IReadOnlyList<GUIContent> options, int selectedIndex, Action<int> onSelect, SegmentedControlClasses classes)
        {
            VisualElement root = CreateContainer(classes?.RootClass);
            if (options == null) return root;

            for (int i = 0; i < options.Count; i++)
            {
                int index = i;
                Button button = CreateSegmentButton(options[i], () => onSelect?.Invoke(index), classes);
                button.userData = index;
                EnableClass(button, classes?.ActiveButtonClass, index == selectedIndex);
                if (i == 0) AddClasses(button, classes?.FirstButtonClass);
                if (i > 0 && i < options.Count - 1) AddClasses(button, classes?.MiddleButtonClass);
                if (i == options.Count - 1) AddClasses(button, classes?.LastButtonClass);
                root.Add(button);
            }

            return root;
        }

        public static void RefreshSegmentedControl(VisualElement root, int selectedIndex, string activeButtonClass)
        {
            if (root == null || string.IsNullOrWhiteSpace(activeButtonClass)) return;

            foreach (VisualElement child in root.Children())
            {
                if (child.userData is int index)
                {
                    child.EnableInClassList(activeButtonClass, index == selectedIndex);
                }
            }
        }

        public static VisualElement CreateAdvancedVisibilityBlock(
            string key,
            Func<VisualElement> contentFactory,
            bool isAdvanced,
            bool showAdvanced,
            bool customizationMode,
            Action<string, bool> setAdvanced,
            bool alwaysShow = false,
            bool inlineControls = false,
            AdvancedVisibilityClasses classes = null)
        {
            VisualElement root = CreateContainer(classes?.RootClass);
            root.userData = key;

            if (!customizationMode && !alwaysShow && isAdvanced && !showAdvanced)
            {
                root.style.display = DisplayStyle.None;
                return root;
            }

            VisualElement content = contentFactory?.Invoke();
            if (content != null)
            {
                AddClasses(content, classes?.BodyClass);
                root.Add(content);
            }

            if (customizationMode)
            {
                AddClasses(root, isAdvanced ? classes?.AdvancedClass : classes?.DefaultClass);
                if (inlineControls)
                {
                    AddClasses(root, classes?.InlineClass);
                }

                VisualElement controls = CreateContainer(classes?.ControlsClass);
                if (inlineControls)
                {
                    AddClasses(controls, classes?.InlineControlsClass);
                }
                Button toggle = CreateButton(isAdvanced ? "Show" : "Hide", () =>
                {
                    setAdvanced?.Invoke(key, !isAdvanced);
                }, classes?.ToggleButtonClass);
                toggle.tooltip = isAdvanced
                    ? "Show this element by default."
                    : "Move this element into advanced mode.";
                controls.Add(toggle);
                root.Add(controls);
            }

            return root;
        }

        public static int GetAdvancedVisibilityStateHash(bool showAdvanced, bool customizationMode, IEnumerable<string> advancedKeys)
        {
            unchecked
            {
                int keyCount = 0;
                int keyHashXor = 0;
                int keyHashSum = 0;
                if (advancedKeys != null)
                {
                    foreach (string key in advancedKeys)
                    {
                        int keyHash = StringComparer.Ordinal.GetHashCode(key ?? string.Empty);
                        keyCount++;
                        keyHashXor ^= keyHash;
                        keyHashSum += keyHash;
                    }
                }

                int hash = 17;
                hash = hash * 31 + (showAdvanced ? 1 : 0);
                hash = hash * 31 + (customizationMode ? 1 : 0);
                hash = hash * 31 + keyCount;
                hash = hash * 31 + keyHashXor;
                hash = hash * 31 + keyHashSum;
                return hash;
            }
        }

        public static bool AdvancedVisibilityStateChanged(ref int previousStateHash, bool showAdvanced, bool customizationMode, IEnumerable<string> advancedKeys)
        {
            int currentStateHash = GetAdvancedVisibilityStateHash(showAdvanced, customizationMode, advancedKeys);
            if (previousStateHash == currentStateHash)
            {
                return false;
            }

            previousStateHash = currentStateHash;
            return true;
        }

        public static VisualElement CreateStringListControl(
            EditorWindow owner,
            string value,
            string separator,
            Action<string> onChange,
            string popupTitle,
            string tooltip,
            string rowClass,
            string textFieldClass,
            string growClass,
            string buttonClass,
            string iconButtonClass,
            string iconImageClass,
            string editButtonClass = null,
            string editTooltip = "Edit list",
            string editIconName = "editicon.sml")
        {
            VisualElement row = CreateContainer(rowClass);

            string currentValue = value ?? string.Empty;
            string currentSeparator = string.IsNullOrEmpty(separator) ? "," : separator;
            TextField field = new TextField
            {
                value = currentValue,
                tooltip = tooltip ?? popupTitle ?? string.Empty
            };
            AddClasses(field, textFieldClass, growClass);
            field.RegisterValueChangedCallback(evt =>
            {
                currentValue = evt.newValue;
                onChange?.Invoke(evt.newValue);
            });
            row.Add(field);

            Button edit = null;
            edit = CreateIconButton(editTooltip, editIconName, () =>
            {
                StringListWindow.ShowAsDropDown(ToScreenDropdownAnchor(owner, edit), currentValue, currentSeparator, result =>
                {
                    currentValue = result;
                    field.SetValueWithoutNotify(result);
                    onChange?.Invoke(result);
                }, popupTitle ?? tooltip);
            }, buttonClass, iconButtonClass, iconImageClass);
            AddClasses(edit, editButtonClass);
            row.Add(edit);

            return row;
        }

        public static ProgressBar CreateProgressBar(string title, float progress, string className)
        {
            ProgressBar progressBar = new ProgressBar
            {
                title = title ?? string.Empty,
                lowValue = 0f,
                highValue = 1f,
                value = Mathf.Clamp01(progress)
            };
            AddClasses(progressBar, className);
            return progressBar;
        }

        public static VisualElement CreateLogoHeader(string title, Texture logo, float logoSize, string headerClass, string imageClass, string titleClass)
        {
            VisualElement header = CreateContainer(headerClass);
            if (logo != null)
            {
                Image image = new Image
                {
                    image = logo,
                    scaleMode = ScaleMode.ScaleToFit
                };
                AddClasses(image, imageClass);
                image.style.width = logoSize;
                image.style.height = logoSize;
                header.Add(image);
            }

            header.Add(CreateLabel(title, titleClass));
            return header;
        }

        // Standalone rows reuse Unity's collection styling. ListView/TreeView own these classes themselves.
        public static void ApplyListRowStyle(VisualElement row, int index = -1)
        {
            row.AddToClassList(BaseVerticalCollectionView.itemUssClassName);
            if (index >= 0) row.EnableInClassList(BaseVerticalCollectionView.itemAlternativeBackgroundUssClassName, index % 2 == 1);
            if (row is Button && row.ClassListContains(Button.ussClassName))
            {
                row.RemoveFromClassList(Button.ussClassName);
                row.RegisterCallback<FocusInEvent>(_ => row.AddToClassList(BaseVerticalCollectionView.itemSelectedVariantUssClassName));
                row.RegisterCallback<FocusOutEvent>(_ => row.RemoveFromClassList(BaseVerticalCollectionView.itemSelectedVariantUssClassName));
            }
        }

        public static VisualElement CreateTitleSubtitleActionRow(
            string title,
            string subtitle,
            VisualElement side,
            string rowClass,
            string bodyClass,
            string titleClass,
            string subtitleClass,
            params string[] extraRowClasses)
        {
            VisualElement row = CreateContainer(rowClass);
            return PopulateTitleSubtitleActionRow(
                row,
                title,
                subtitle,
                null,
                side,
                bodyClass,
                titleClass,
                subtitleClass,
                extraRowClasses);
        }

        public static VisualElement PopulateTitleSubtitleActionRow(
            VisualElement row,
            string title,
            string subtitle,
            VisualElement leading,
            VisualElement side,
            string bodyClass,
            string titleClass,
            string subtitleClass,
            params string[] extraRowClasses)
        {
            if (row == null) row = new VisualElement();
            AddClasses(row, extraRowClasses);

            if (leading != null) row.Add(leading);

            VisualElement body = CreateContainer(bodyClass);
            Label titleLabel = CreateLabel(title, titleClass);
            titleLabel.name = "title";
            body.Add(titleLabel);

            Label subtitleLabel = CreateLabel(subtitle ?? string.Empty, subtitleClass);
            subtitleLabel.name = "subtitle";
            subtitleLabel.style.display = string.IsNullOrWhiteSpace(subtitle) ? DisplayStyle.None : DisplayStyle.Flex;
            body.Add(subtitleLabel);
            row.Add(body);

            if (side != null) row.Add(side);
            return row;
        }

        public static void SetTitleSubtitleRowText(VisualElement row, string title, string subtitle)
        {
            if (row == null) return;

            Label titleLabel = row.Q<Label>("title");
            if (titleLabel != null)
            {
                titleLabel.text = title ?? string.Empty;
                titleLabel.style.display = string.IsNullOrWhiteSpace(title) ? DisplayStyle.None : DisplayStyle.Flex;
            }

            Label subtitleLabel = row.Q<Label>("subtitle");
            if (subtitleLabel != null)
            {
                subtitleLabel.text = subtitle ?? string.Empty;
                subtitleLabel.style.display = string.IsNullOrWhiteSpace(subtitle) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        public static VisualElement CreateWizardShell(
            string sidebarTitle,
            IReadOnlyList<WizardStep> steps,
            string title,
            string description,
            VisualElement body,
            VisualElement footer,
            Texture logo,
            WizardShellClasses classes)
        {
            VisualElement root = CreateContainer(classes?.RootClass);

            VisualElement sidebar = CreateContainer(classes?.SidebarClass);
            sidebar.Add(CreateLabel(sidebarTitle, classes?.SidebarTitleClass));
            if (steps != null)
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    WizardStep step = steps[i];
                    string stepTitle = step?.Title ?? string.Empty;
                    Button button = CreateButton(
                        classes?.NumberSteps == false ? stepTitle : $"{i + 1}. {stepTitle}",
                        step?.OnClick,
                        classes?.StepButtonClass);
                    EnableClass(button, classes?.ActiveStepClass, step?.IsActive == true);
                    EnableClass(button, classes?.CompletedStepClass, step?.IsCompleted == true);
                    button.SetEnabled(step?.IsEnabled == true);
                    sidebar.Add(button);
                }
            }

            sidebar.Add(CreateFlexibleSpacer());
            if (logo != null)
            {
                Image image = new Image
                {
                    image = logo,
                    scaleMode = ScaleMode.ScaleToFit
                };
                AddClasses(image, classes?.LogoClass);
                sidebar.Add(image);
            }
            root.Add(sidebar);

            VisualElement content = CreateContainer(classes?.ContentClass);
            VisualElement header = CreateContainer(classes?.HeaderClass);
            header.Add(CreateLabel(title, classes?.TitleClass));
            if (!string.IsNullOrWhiteSpace(description))
            {
                header.Add(CreateLabel(description, classes?.DescriptionClass));
            }
            content.Add(header);

            if (body != null)
            {
                AddClasses(body, classes?.BodyClass);
                content.Add(body);
            }

            if (footer != null)
            {
                AddClasses(footer, classes?.FooterClass);
                content.Add(footer);
            }

            root.Add(content);
            return root;
        }

        public static VisualElement CreateFlexibleSpacer()
        {
            VisualElement spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            return spacer;
        }

        private static Button CreateSegmentButton(GUIContent content, Action click, SegmentedControlClasses classes)
        {
            Button button = CreateButton(content?.text ?? string.Empty, click, classes?.ButtonBaseClass, classes?.ButtonClass);
            button.tooltip = content?.tooltip ?? string.Empty;

            if (content?.image != null)
            {
                if (string.IsNullOrEmpty(content.text))
                {
                    AddClasses(button, classes?.IconOnlyButtonClass);
                }

                Image image = new Image
                {
                    image = content.image,
                    scaleMode = ScaleMode.ScaleToFit
                };
                AddClasses(image, classes?.IconClass);
                button.Add(image);
            }

            return button;
        }

        public static Rect ToScreenDropdownAnchor(Rect guiAnchor)
        {
            Vector2 screenPosition = GUIUtility.GUIToScreenPoint(new Vector2(guiAnchor.x, guiAnchor.y));
            return new Rect(screenPosition.x, screenPosition.y, Mathf.Max(1f, guiAnchor.width), Mathf.Max(1f, guiAnchor.height));
        }

        public static Rect ToScreenDropdownAnchor(EditorWindow owner, VisualElement anchor)
        {
            if (owner == null || anchor == null)
            {
                return new Rect(0f, 0f, 1f, 1f);
            }

            Rect anchorBounds = anchor.worldBound;
            return new Rect(
                owner.position.x + anchorBounds.x,
                owner.position.y + anchorBounds.y,
                Mathf.Max(1f, anchorBounds.width),
                Mathf.Max(1f, anchorBounds.height));
        }

        public static void ShowGenericMenu(GenericMenu menu, VisualElement anchor)
        {
            if (menu == null || anchor == null) return;

            Rect anchorBounds = anchor.worldBound;
            menu.DropDown(new Rect(
                anchorBounds.x,
                anchorBounds.y,
                Mathf.Max(1f, anchorBounds.width),
                Mathf.Max(1f, anchorBounds.height)));
        }

        public static void ShowAsDropDown(EditorWindow window, Rect guiAnchor, Vector2 windowSize)
        {
            if (window == null) return;

            ApplyDropDownWindowStyle(window);
            window.ShowAsDropDown(ToScreenDropdownAnchor(guiAnchor), windowSize);
        }

        public static void ShowAsDropDown(EditorWindow window, EditorWindow owner, VisualElement anchor, Vector2 windowSize)
        {
            if (window == null) return;

            ApplyDropDownWindowStyle(window);
            window.ShowAsDropDown(ToScreenDropdownAnchor(owner, anchor), windowSize);
        }

        internal static void ApplyDropDownWindowStyle(EditorWindow window)
        {
            if (window == null) return;

            VisualElement root = window.rootVisualElement;
            Color borderColor = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.30f)
                : new Color(0f, 0f, 0f, 0.32f);
            root.style.borderLeftWidth = 1f;
            root.style.borderRightWidth = 1f;
            root.style.borderTopWidth = 1f;
            root.style.borderBottomWidth = 1f;
            root.style.borderLeftColor = borderColor;
            root.style.borderRightColor = borderColor;
            root.style.borderTopColor = borderColor;
            root.style.borderBottomColor = borderColor;
        }

        /// <summary>
        /// Stops a handled UI Toolkit event before a child control or ancestor handles it again.
        /// Unity 2022.3 also requires cancelling the legacy default action explicitly.
        /// </summary>
        public static void ConsumeEvent(EventBase evt, bool immediate = false)
        {
            UIToolkitEventUtils.ConsumeEvent(evt, immediate);
        }

        public static void AddClasses(VisualElement element, params string[] classNames)
        {
            if (element == null || classNames == null) return;

            for (int i = 0; i < classNames.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(classNames[i]))
                {
                    element.AddToClassList(classNames[i]);
                }
            }
        }

        private static void EnableClass(VisualElement element, string className, bool enabled)
        {
            if (element == null || string.IsNullOrWhiteSpace(className)) return;

            element.EnableInClassList(className, enabled);
        }
    }
}
