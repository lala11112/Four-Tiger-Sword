using System;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
#if UNITY_6000_7_OR_NEWER
    // UI class maps and the shared logo reference are immutable editor code-lifetime state.
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    internal static partial class AssetInventoryUITK
    {
        internal const string RootClass = "ai-uitk-root";
        internal const string SectionClass = "ai-section";
        internal const string ButtonClass = "ai-button";
        internal const string PrimaryButtonClass = "ai-button-primary";
        internal const string SecondaryButtonClass = "ai-button-secondary";
        internal const string DestructiveButtonClass = "ai-button-destructive";
        internal const string CompactSearchToolbarClass = "ai-compact-search-toolbar";
        internal const string FooterClass = "ai-footer";
        internal const string WindowFooterClass = "ai-window-footer";
        internal const string NavigationFooterClass = "ai-navigation-footer";
        internal const string NavigationFooterGroupClass = "ai-navigation-footer-group";
        internal const string NavigationFooterLeftClass = "ai-navigation-footer-left";
        internal const string NavigationFooterCenterClass = "ai-navigation-footer-center";
        internal const string NavigationFooterRightClass = "ai-navigation-footer-right";
        internal const string NavigationFooterSummaryClass = "ai-navigation-footer-summary";
        internal const string NavigationFooterPagerClass = "ai-navigation-footer-pager";
        internal const string NavigationFooterPagerLabelClass = "ai-navigation-footer-pager-label";
        internal const string NavigationFooterButtonClass = "ai-navigation-footer-button";
        internal const string NavigationFooterIconButtonClass = "ai-navigation-footer-icon-button";
        internal const string NavigationFooterActiveClass = "ai-navigation-footer-active";
        internal const string NavigationFooterSegmentedClass = "ai-navigation-footer-segmented";
        internal const string NavigationFooterSegmentButtonClass = "ai-navigation-footer-segment-button";
        internal const string NavigationFooterSegmentIconOnlyButtonClass = "ai-navigation-footer-segment-icon-only-button";
        internal const string NavigationFooterSegmentIconClass = "ai-navigation-footer-segment-icon";
        internal const string NavigationFooterSegmentFirstClass = "ai-navigation-footer-segment-first";
        internal const string NavigationFooterSegmentMiddleClass = "ai-navigation-footer-segment-middle";
        internal const string NavigationFooterSegmentLastClass = "ai-navigation-footer-segment-last";
        internal const string NavigationFooterSliderClass = "ai-navigation-footer-slider";
        internal const string ResultGridClass = "ai-result-grid";

        private const string AnchorPathSuffix = "/Editor/Scripts/UI/Base/AssetInventoryUITK.cs";
        private const string StylePathSuffix = "/Editor/Scripts/UI/AssetInventoryEditor.uss";
        private const string DarkClass = "ai-uitk-dark";
        private const string LightClass = "ai-uitk-light";
        private const string SectionTitleClass = "ai-section-title";
        private const string HelpBoxClass = "ai-help-box";
        private const string HelpBoxIconClass = "ai-help-box-icon";
        private const string HelpBoxTextClass = "ai-help-box-text";
        private const string KeyValueRowClass = "ai-key-value-row";
        private const string KeyValueLabelClass = "ai-key-value-label";
        private const string KeyValueTextClass = "ai-key-value-text";
        private const string FieldRowClass = "ai-field-row";
        private const string FieldLabelClass = "ai-field-label";
        private const string FieldControlClass = "ai-field-control";
        private const string WindowSearchFieldClass = "ai-window-search-field";
        private const string IconButtonClass = "ai-icon-button";
        private const string IconButtonImageClass = "ai-icon-button-image";
        private const string RemovablePillClass = "ai-removable-pill";
        private const string RemovablePillTextClass = "ai-removable-pill-text";
        private const string RemovablePillIconClass = "ai-removable-pill-icon";
        private const string InlineControlRowClass = "ai-inline-control-row";
        private const string InlineGrowClass = "ai-inline-grow";
        private const string ProgressBarClass = "ai-progress-bar";
        private const string LogoHeaderClass = "ai-logo-header";
        private const string LogoHeaderImageClass = "ai-logo-header-image";
        private const string AdvancedVisibilityBlockClass = "ai-advanced-visibility-block";
        private const string AdvancedVisibilityDefaultClass = "ai-advanced-visibility-default";
        private const string AdvancedVisibilityAdvancedClass = "ai-advanced-visibility-advanced";
        private const string AdvancedVisibilityInlineClass = "ai-advanced-visibility-inline";
        private const string AdvancedVisibilityBodyClass = "ai-advanced-visibility-body";
        private const string AdvancedVisibilityControlsClass = "ai-advanced-visibility-controls";
        private const string AdvancedVisibilityInlineControlsClass = "ai-advanced-visibility-controls-inline";
        private const string AdvancedVisibilityToggleClass = "ai-advanced-visibility-toggle";
        private const string AdvancedVisibilityShownToggleClass = "ai-advanced-visibility-toggle-shown";
        private const string AdvancedVisibilityAdvancedToggleClass = "ai-advanced-visibility-toggle-advanced";
        private const string AdvancedVisibilityCompactToggleClass = "ai-advanced-visibility-toggle-compact";
        private const string AdvancedVisibilityToggleIconClass = "ai-advanced-visibility-toggle-icon";
        private const string SavedSearchesClass = "ai-saved-searches";
        private const string SavedSearchGroupClass = "ai-saved-search-group";
        private const string SavedSearchPillClass = "ai-saved-search-pill";
        private const string SavedSearchPillWithMenuClass = "ai-saved-search-pill-with-menu";
        private const string SavedSearchActiveClass = "ai-saved-search-active";
        private const string SavedSearchPillContentClass = "ai-saved-search-pill-content";
        private const string SavedSearchIconClass = "ai-saved-search-icon";
        private const string SavedSearchTextClass = "ai-saved-search-text";
        private const string SavedSearchMenuClass = "ai-saved-search-menu";
        private const string ResultGridTileClass = "ai-result-grid-tile";
        private const string ResultGridPreviewClass = "ai-result-grid-preview";
        private const string ResultGridBadgesClass = "ai-result-grid-badges";
        private const string ResultGridBadgeClass = "ai-result-grid-badge";
        private const string ResultGridLabelClass = "ai-result-grid-label";
        private const string ResultGridSubtitleClass = "ai-result-grid-subtitle";
        private const string ResultGridPathTextClass = "ai-result-grid-path-text";
        private const string ResultGridSizeControlsClass = "ai-result-grid-size-controls";
        private const string ResultGridModePopupClass = "ai-result-grid-mode-popup";
        private const string InspectorPaneClass = "ai-inspector-pane";
        private const string InspectorHeaderClass = "ai-inspector-header";
        private const string InspectorLeadingClass = "ai-inspector-leading";
        private const string InspectorTabStripClass = "ai-inspector-tabs";
        private const string InspectorTabClass = "ai-inspector-tab";
        private const string InspectorSelectedTabClass = "ai-inspector-tab-selected";
        private const string InspectorTrailingClass = "ai-inspector-trailing";
        private const string InspectorBodyClass = "ai-inspector-body";
        private const string SearchablePopupClass = "ai-searchable-popup";
        private const string SearchablePopupButtonClass = "ai-searchable-popup-button";
        private const string SearchablePopupLabelClass = "ai-searchable-popup-label";
        private const string SearchablePopupArrowClass = "ai-searchable-popup-arrow";
        private const string EmptyStateClass = "ai-empty-state";
        private const string EmptyStateIconClass = "ai-empty-state-icon";
        private const string EmptyStateTitleClass = "ai-empty-state-title";
        private const string EmptyStateDetailClass = "ai-empty-state-detail";
        private const string EmptyStateActionsClass = "ai-empty-state-actions";
        private const string ResizablePaneRootClass = "ai-resizable-pane-layout";
        private const string ResizablePaneMainClass = "ai-resizable-pane-main";
        private const string ResizablePaneHostClass = "ai-resizable-pane-host";
        private const string ResizablePaneLeadingClass = "ai-resizable-pane-leading";
        private const string ResizablePaneTrailingClass = "ai-resizable-pane-trailing";
        private const string ResizablePaneContentClass = "ai-resizable-pane-content";
        private const string ResizablePaneDividerClass = "ai-resizable-pane-divider";
        private const string ResizablePaneDividerLineClass = "ai-resizable-pane-divider-line";
        private const string ResizablePaneCollapsedClass = "ai-resizable-pane-collapsed";
        private const string ResizablePaneCompactClass = "ai-resizable-pane-compact";
        private const string ResizablePaneWideClass = "ai-resizable-pane-wide";
        private const string ResizablePaneResizingClass = "ai-resizable-pane-resizing";

        private static Texture2D _logo;
        private static readonly CommonUITK.AdvancedVisibilityClasses AdvancedVisibilityClasses = new CommonUITK.AdvancedVisibilityClasses
        {
            RootClass = AdvancedVisibilityBlockClass,
            DefaultClass = AdvancedVisibilityDefaultClass,
            AdvancedClass = AdvancedVisibilityAdvancedClass,
            InlineClass = AdvancedVisibilityInlineClass,
            BodyClass = AdvancedVisibilityBodyClass,
            ControlsClass = AdvancedVisibilityControlsClass,
            InlineControlsClass = AdvancedVisibilityInlineControlsClass,
            ToggleButtonClass = AdvancedVisibilityToggleClass
        };
        private static readonly CommonUITK.SegmentedControlClasses NavigationSegmentedClasses = new CommonUITK.SegmentedControlClasses
        {
            RootClass = NavigationFooterSegmentedClass,
            ButtonBaseClass = ButtonClass,
            ButtonClass = NavigationFooterSegmentButtonClass,
            IconOnlyButtonClass = NavigationFooterSegmentIconOnlyButtonClass,
            ActiveButtonClass = NavigationFooterActiveClass,
            IconClass = NavigationFooterSegmentIconClass,
            FirstButtonClass = NavigationFooterSegmentFirstClass,
            MiddleButtonClass = NavigationFooterSegmentMiddleClass,
            LastButtonClass = NavigationFooterSegmentLastClass
        };
        private static readonly CommonUITK.SavedSearchPillClasses SavedSearchPillClasses = new CommonUITK.SavedSearchPillClasses
        {
            GroupClass = SavedSearchGroupClass,
            PillClass = SavedSearchPillClass,
            PillWithMenuClass = SavedSearchPillWithMenuClass,
            ActiveClass = SavedSearchActiveClass,
            PillContentClass = SavedSearchPillContentClass,
            IconClass = SavedSearchIconClass,
            TextClass = SavedSearchTextClass,
            MenuClass = SavedSearchMenuClass,
            ButtonClass = ButtonClass,
            IconButtonClass = IconButtonClass,
            IconButtonImageClass = IconButtonImageClass
        };
        private static readonly CommonFormBuilder DefaultFormBuilder = CreateFormBuilder();
        private static readonly CommonPaginationControl.PaginationClasses NavigationPaginationClasses = new CommonPaginationControl.PaginationClasses
        {
            RootClass = NavigationFooterPagerClass,
            ButtonBaseClass = ButtonClass,
            ButtonStyleClass = SecondaryButtonClass,
            ButtonClass = NavigationFooterButtonClass,
            PageButtonClass = NavigationFooterPagerLabelClass
        };
        private static readonly CommonGridSizeControl.GridSizeClasses ResultGridSizeClasses = new CommonGridSizeControl.GridSizeClasses
        {
            RootClass = ResultGridSizeControlsClass,
            ModePopupClass = ResultGridModePopupClass,
            SliderClass = NavigationFooterSliderClass
        };
        private static readonly CommonTabbedPane.TabbedPaneClasses InspectorPaneClasses = new CommonTabbedPane.TabbedPaneClasses
        {
            RootClass = InspectorPaneClass,
            HeaderClass = InspectorHeaderClass,
            LeadingClass = InspectorLeadingClass,
            TabStripClass = InspectorTabStripClass,
            TabClass = InspectorTabClass,
            SelectedTabClass = InspectorSelectedTabClass,
            TrailingClass = InspectorTrailingClass,
            BodyClass = InspectorBodyClass
        };
        private static readonly CommonSearchablePopupField.SearchablePopupClasses SearchablePopupClasses = new CommonSearchablePopupField.SearchablePopupClasses
        {
            RootClass = SearchablePopupClass,
            ButtonClass = SearchablePopupButtonClass,
            LabelClass = SearchablePopupLabelClass,
            ArrowClass = SearchablePopupArrowClass
        };
        private static readonly CommonEmptyState.EmptyStateClasses EmptyStateClasses = new CommonEmptyState.EmptyStateClasses
        {
            RootClass = EmptyStateClass,
            IconClass = EmptyStateIconClass,
            TitleClass = EmptyStateTitleClass,
            DetailClass = EmptyStateDetailClass,
            ActionsClass = EmptyStateActionsClass
        };
        private static readonly CommonResizableSidePaneLayout.LayoutClasses ResizablePaneClasses = new CommonResizableSidePaneLayout.LayoutClasses
        {
            RootClass = ResizablePaneRootClass,
            MainClass = ResizablePaneMainClass,
            HostClass = ResizablePaneHostClass,
            LeadingHostClass = ResizablePaneLeadingClass,
            TrailingHostClass = ResizablePaneTrailingClass,
            ContentClass = ResizablePaneContentClass,
            DividerClass = ResizablePaneDividerClass,
            DividerLineClass = ResizablePaneDividerLineClass,
            CollapsedClass = ResizablePaneCollapsedClass,
            CompactClass = ResizablePaneCompactClass,
            WideClass = ResizablePaneWideClass,
            ResizingClass = ResizablePaneResizingClass
        };

        private static readonly CommonOrderedSection.OrderedSectionClasses OrderedSectionClasses = new CommonOrderedSection.OrderedSectionClasses
        {
            RootClass = "ai-ordered-section",
            CustomizationClass = "ai-ordered-section-customizing",
            ControlsClass = "ai-ordered-section-controls",
            MoveButtonClass = "ai-ordered-section-move",
            BodyClass = "ai-ordered-section-body"
        };

        private static readonly CommonProgressOverlay.ProgressOverlayClasses ProgressOverlayClasses = new CommonProgressOverlay.ProgressOverlayClasses
        {
            RootClass = "ai-progress-overlay",
            PanelClass = "ai-progress-overlay-panel",
            TitleClass = "ai-progress-overlay-title",
            ProgressClass = "ai-progress-overlay-bar",
            DetailsClass = "ai-progress-overlay-details"
        };

        internal static Texture2D Logo
        {
            get
            {
                if (_logo == null) _logo = CommonUIStyles.LoadTexture("AssetInventory");
                return _logo;
            }
        }

        internal static void ApplyWindowStyles(VisualElement root, bool standalone = true)
        {
            if (root == null) return;

            CommonUITK.ApplyRoot(root, LoadStyleSheet(), RootClass, DarkClass, LightClass);
            if (standalone) CommonUITK.ApplyWindowLayout(root);
        }

        internal static StyleSheet LoadStyleSheet()
        {
            return CommonUITK.LoadStyleSheetFromAnchor("AssetInventoryUITK", AnchorPathSuffix, StylePathSuffix);
        }

        internal static StyleSheet LoadStyleSheetForTests()
        {
            return LoadStyleSheet();
        }

        internal static VisualElement CreateSection(string title = null)
        {
            return CommonUITK.CreateSection(title, SectionClass, SectionTitleClass);
        }

        internal static void HideEmptySections(VisualElement root)
        {
            if (root == null) return;

            foreach (VisualElement child in root.Children())
            {
                HideEmptySections(child);
            }

            if (root.ClassListContains(SectionClass) && !HasVisibleSectionContent(root))
            {
                root.style.display = DisplayStyle.None;
            }
        }

        private static bool HasVisibleSectionContent(VisualElement section)
        {
            foreach (VisualElement child in section.Children())
            {
                if (child.ClassListContains(SectionTitleClass)) continue;
                if (HasVisibleMeaningfulContent(child)) return true;
            }
            return false;
        }

        private static bool HasVisibleMeaningfulContent(VisualElement element)
        {
            if (element == null || element.style.display.value == DisplayStyle.None) return false;
            if (element.ClassListContains(SectionTitleClass)) return false;

            if (element is Label label)
            {
                return !string.IsNullOrWhiteSpace(label.text);
            }
            if (element.GetType() != typeof(VisualElement)) return true;

            foreach (VisualElement child in element.Children())
            {
                if (HasVisibleMeaningfulContent(child)) return true;
            }
            return false;
        }

        internal static VisualElement CreateHelpBox(string text, MessageType type = MessageType.Info)
        {
            Texture iconTexture = EditorGUIUtility.IconContent(GetIconName(type)).image;
            return CommonUITK.CreateIconTextBox(text, iconTexture, HelpBoxClass, GetHelpBoxClass(type), HelpBoxIconClass, HelpBoxTextClass);
        }

        internal static VisualElement CreateKeyValueRow(string label, string value)
        {
            return CommonUITK.CreateKeyValueRow(label, value, KeyValueRowClass, KeyValueLabelClass, KeyValueTextClass);
        }

        internal static VisualElement CreateFieldRow(string label, VisualElement field)
        {
            return DefaultFormBuilder.CreateRow(label, field?.tooltip, field);
        }

        internal static ToolbarSearchField CreateWindowSearchField(
            string value,
            string tooltip,
            Action<string> onChange,
            params string[] classNames)
        {
            ToolbarSearchField field = new ToolbarSearchField
            {
                value = value ?? string.Empty,
                tooltip = tooltip ?? string.Empty
            };
            CommonUITK.AddClasses(field, WindowSearchFieldClass);
            CommonUITK.AddClasses(field, classNames);
            if (onChange != null)
            {
                field.RegisterValueChangedCallback(evt => onChange(evt.newValue ?? string.Empty));
            }
            return field;
        }

        internal static CommonFormBuilder CreateFormBuilder(
            string rowClass = null,
            string labelClass = null,
            string controlClass = null,
            string inlineClass = null,
            string fieldClass = null,
            string toggleClass = null,
            string suffixClass = null,
            bool wrapControls = false,
            bool toggleFirst = false,
            bool labelTogglesControl = false)
        {
            return new CommonFormBuilder(new CommonFormBuilder.FormClasses
            {
                RowClass = rowClass ?? FieldRowClass,
                LabelClass = labelClass ?? FieldLabelClass,
                ControlClass = controlClass ?? FieldControlClass,
                InlineClass = inlineClass ?? InlineControlRowClass,
                FieldClass = fieldClass,
                ToggleClass = toggleClass,
                SuffixClass = suffixClass,
                WrapControls = wrapControls,
                ToggleFirst = toggleFirst,
                LabelTogglesControl = labelTogglesControl
            });
        }

        internal static VisualElement CreateToggleFieldRow(string label, bool value, Action<bool> onChange, string tooltip = null, params string[] toggleClasses)
        {
            return DefaultFormBuilder.CreateToggleRow(label, value, onChange, tooltip, toggleClasses);
        }

        internal static VisualElement CreateTextFieldRow(
            string label,
            string value,
            Action<string> onChange,
            string tooltip = null,
            bool isDelayed = false,
            bool isReadOnly = false,
            params string[] fieldClasses)
        {
            return DefaultFormBuilder.CreateTextRow(label, value, onChange, tooltip, isDelayed, isReadOnly, fieldClasses);
        }

        internal static VisualElement CreateIntegerFieldRow(
            string label,
            int value,
            Action<int> onChange,
            string suffix = null,
            string tooltip = null,
            bool isDelayed = true,
            params string[] fieldClasses)
        {
            return DefaultFormBuilder.CreateIntegerRow(label, value, onChange, suffix, tooltip, isDelayed, fieldClasses);
        }

        internal static VisualElement CreateFloatFieldRow(
            string label,
            float value,
            Action<float> onChange,
            string suffix = null,
            string tooltip = null,
            bool isDelayed = true,
            params string[] fieldClasses)
        {
            return DefaultFormBuilder.CreateFloatRow(label, value, onChange, suffix, tooltip, isDelayed, fieldClasses);
        }

        internal static VisualElement CreateEnumFieldRow<TEnum>(string label, TEnum value, Action<TEnum> onChange, string tooltip = null, params string[] fieldClasses)
            where TEnum : Enum
        {
            return DefaultFormBuilder.CreateEnumRow(label, value, onChange, tooltip, fieldClasses);
        }

        internal static VisualElement CreateColorFieldRow(string label, Color value, Action<Color> onChange, string tooltip = null, params string[] fieldClasses)
        {
            return DefaultFormBuilder.CreateColorRow(label, value, onChange, tooltip, fieldClasses);
        }

        internal static Label CreateCopyLabel(string text)
        {
            return CommonUITK.CreateLabel(text, "ai-section-copy");
        }

        internal static Label CreateLabel(string text, params string[] classNames)
        {
            return CommonUITK.CreateLabel(text, classNames);
        }

        internal static Label CreateMutedLabel(string text)
        {
            return CommonUITK.CreateLabel(text, "ai-status-muted", "ai-section-copy");
        }

        internal static Label CreateSectionTitle(string text)
        {
            return CommonUITK.CreateLabel(text, SectionTitleClass);
        }

        internal static Label CreateStatusPill(string text, string extraClass = null)
        {
            Label pill = CommonUITK.CreateLabel(text, "ai-status-pill", extraClass);
            pill.tooltip = text ?? string.Empty;
            return pill;
        }

        internal static CommonEmptyState CreateEmptyState(string title, string detail = null, params VisualElement[] actions)
        {
            CommonEmptyState empty = new CommonEmptyState(EmptyStateClasses);
            empty.SetContent(title, detail, actions: actions);
            return empty;
        }

        internal static Foldout CreateFoldout(string title, bool value, Action<bool> onChange, string tooltip = null, params string[] classNames)
        {
            return CommonUITK.CreateFoldout(title, value, onChange, tooltip, classNames);
        }

        internal static VisualElement PopulateListRow(
            VisualElement row,
            string title,
            string subtitle,
            VisualElement leading = null,
            VisualElement trailing = null,
            bool collectionItem = false,
            params string[] extraClasses)
        {
            CommonUITK.AddClasses(row, "ai-list-row");
            if (!collectionItem) CommonUITK.ApplyListRowStyle(row);
            return CommonUITK.PopulateTitleSubtitleActionRow(
                row,
                title,
                subtitle,
                leading,
                trailing,
                "ai-list-row-body",
                "ai-list-row-title",
                "ai-list-row-subtitle",
                extraClasses);
        }

        internal static VisualElement CreateRemovablePill(string text, string tooltip, Action remove, string extraClass = null)
        {
            return CommonUITK.CreateRemovablePill(
                text,
                tooltip,
                "d_TreeEditor.Trash",
                remove,
                RemovablePillTextClass,
                RemovablePillIconClass,
                "ai-status-pill",
                RemovablePillClass,
                extraClass);
        }

        internal static VisualElement CreateResultGridTile(params Label[] badges)
        {
            VisualElement tile = CommonUITK.CreateContainer(ResultGridTileClass);

            Image preview = new Image
            {
                name = "preview",
                scaleMode = ScaleMode.ScaleToFit
            };
            preview.AddToClassList(ResultGridPreviewClass);
            tile.Add(preview);

            VisualElement badgeContainer = CommonUITK.CreateContainer(ResultGridBadgesClass);
            badgeContainer.name = "badges";
            if (badges != null)
            {
                for (int i = 0; i < badges.Length; i++)
                {
                    if (badges[i] != null) badgeContainer.Add(badges[i]);
                }
            }
            tile.Add(badgeContainer);

            Label label = new ResultGridTextLabel();
            label.AddToClassList(ResultGridLabelClass);
            label.name = "label";
            tile.Add(label);

            Label subtitle = new ResultGridTextLabel();
            subtitle.AddToClassList(ResultGridSubtitleClass);
            subtitle.name = "subtitle";
            tile.Add(subtitle);
            return tile;
        }

        internal static void SetResultGridText(Label label, string text, bool isPath)
        {
            label.EnableInClassList(ResultGridPathTextClass, isPath);
            if (label is ResultGridTextLabel resultGridLabel)
            {
                resultGridLabel.SetText(text);
                return;
            }

            label.text = text ?? string.Empty;
        }

        internal static string FitTextWithMiddleEllipsis(string text, Func<string, bool> fits)
        {
            string fullText = text ?? string.Empty;
            if (fullText.Length == 0 || fits == null || fits(fullText)) return fullText;

            const string ellipsis = "\u2026";
            int low = 0;
            int high = fullText.Length - 1;
            string best = ellipsis;
            while (low <= high)
            {
                int preservedCharacters = (low + high) / 2;
                string candidate = CreateMiddleEllipsisCandidate(fullText, ellipsis, preservedCharacters);
                if (fits(candidate))
                {
                    best = candidate;
                    low = preservedCharacters + 1;
                }
                else
                {
                    high = preservedCharacters - 1;
                }
            }

            return best;
        }

        private static string CreateMiddleEllipsisCandidate(string text, string ellipsis, int preservedCharacters)
        {
            int suffixLength = (preservedCharacters + 1) / 2;
            int prefixLength = preservedCharacters - suffixLength;
            return text.Substring(0, prefixLength)
                + ellipsis
                + text.Substring(text.Length - suffixLength, suffixLength);
        }

        internal static Label CreateResultGridBadge(string text, string name, string modifierClass)
        {
            Label badge = CreateStatusPill(text, ResultGridBadgeClass);
            badge.name = name;
            if (!string.IsNullOrEmpty(modifierClass)) badge.AddToClassList(modifierClass);
            return badge;
        }

        internal static CommonGridSizeControl CreateGridSizeControl(
            int value,
            int minimum,
            int maximum,
            Action<int> onSizeChanged,
            bool showModePopup = true)
        {
            CommonGridSizeControl control = new CommonGridSizeControl(value, minimum, maximum, onSizeChanged, ResultGridSizeClasses);
            control.ModePopup.style.display = showModePopup ? DisplayStyle.Flex : DisplayStyle.None;
            return control;
        }

        internal static CommonTabbedPane CreateTabbedInspectorPane()
        {
            return new CommonTabbedPane(InspectorPaneClasses);
        }

        internal static CommonResizableSidePaneLayout CreateResizableSidePaneLayout(
            VisualElement main,
            CommonResizableSidePaneLayout.PaneDefinition leading = null,
            CommonResizableSidePaneLayout.PaneDefinition trailing = null,
            CommonResizableSidePaneLayout.LayoutOptions options = null)
        {
            return new CommonResizableSidePaneLayout(main, leading, trailing, options, ResizablePaneClasses);
        }

        internal static CommonOrderedSection CreateOrderedSection(string group, string key, VisualElement content, Action onOrderChanged)
        {
            UISection section = AI.Config.GetSection(group);
            return new CommonOrderedSection(
                content,
                AI.UICustomizationMode,
                !section.IsFirst(key),
                !section.IsLast(key),
                () =>
                {
                    section.MoveUp(key);
                    AI.SaveConfig();
                    onOrderChanged?.Invoke();
                },
                () =>
                {
                    section.MoveDown(key);
                    AI.SaveConfig();
                    onOrderChanged?.Invoke();
                },
                OrderedSectionClasses);
        }

        internal static CommonProgressOverlay CreateProgressOverlay()
        {
            return new CommonProgressOverlay(ProgressOverlayClasses);
        }

        internal static CommonSearchablePopupField CreateSearchablePopupField(
            EditorWindow owner,
            string[] items,
            int value,
            Action<int> onValueChanged,
            bool showBracketedValues = false,
            bool treatSlashLiterally = false)
        {
            return new CommonSearchablePopupField(
                owner,
                items,
                value,
                onValueChanged,
                showBracketedValues,
                treatSlashLiterally,
                classes: SearchablePopupClasses);
        }

        internal static CommonSearchablePopupField CreateSearchablePopupField(
            EditorWindow owner,
            SearchablePopup.PopupItem[] items,
            int value,
            Action<int> onValueChanged,
            bool tintSelectedField = false,
            bool showBracketedValues = false,
            bool treatSlashLiterally = false)
        {
            return new CommonSearchablePopupField(
                owner,
                items,
                value,
                onValueChanged,
                tintSelectedField,
                showBracketedValues,
                treatSlashLiterally,
                classes: SearchablePopupClasses);
        }

        internal static Button CreatePrimaryButton(string text, Action click)
        {
            return CommonUITK.CreateButton(text, click, ButtonClass, PrimaryButtonClass);
        }

        internal static Button CreateSecondaryButton(string text, Action click)
        {
            return CommonUITK.CreateButton(text, click, ButtonClass, SecondaryButtonClass);
        }

        internal static Button CreateDestructiveButton(string text, Action click)
        {
            return CommonUITK.CreateButton(text, click, ButtonClass, DestructiveButtonClass);
        }

        internal static Button CreateIconButton(string tooltip, string iconName, Action click)
        {
            return CommonUITK.CreateIconButton(tooltip, iconName, click, ButtonClass, IconButtonClass, IconButtonImageClass);
        }

        internal static VisualElement CreateSavedSearchPillGroup(
            string label,
            string tooltip,
            string iconName,
            string htmlColor,
            bool active,
            bool hasMenu,
            Action onClick,
            Action<VisualElement> onMenuClick,
            object userData)
        {
            return CommonUITK.CreateSavedSearchPillGroup(
                label,
                tooltip,
                iconName,
                htmlColor,
                active,
                hasMenu,
                onClick,
                onMenuClick,
                userData,
                SavedSearchPillClasses);
        }

        internal static VisualElement CreateSavedSearchStrip()
        {
            return CommonUITK.CreateContainer(SavedSearchesClass);
        }

        internal static Button FindSavedSearchPill(VisualElement group)
        {
            return group?.Q<Button>(className: SavedSearchPillClass);
        }

        internal static void SetSavedSearchActive(Button button, bool active)
        {
            button?.EnableInClassList(SavedSearchActiveClass, active);
        }

        internal static VisualElement CreateAdvancedVisibilityBlock(string key, Func<VisualElement> contentFactory, bool alwaysShow = false, bool inlineControls = false, Action onVisibilityChanged = null)
        {
            EnsureAdvancedUI();
            bool isAdvanced = AI.Config.advancedUI.Contains(key);
            VisualElement block = CommonUITK.CreateAdvancedVisibilityBlock(
                key,
                contentFactory,
                isAdvanced,
                AI.ShowAdvanced(),
                AI.UICustomizationMode,
                (blockKey, advanced) =>
                {
                    if (advanced)
                    {
                        AI.Config.advancedUI.Add(blockKey);
                    }
                    else
                    {
                        AI.Config.advancedUI.Remove(blockKey);
                    }

                    AI.SaveConfig();
                    onVisibilityChanged?.Invoke();
                },
                alwaysShow,
                inlineControls,
                AdvancedVisibilityClasses);

            if (!AI.UICustomizationMode) return block;

            VisualElement controls = null;
            for (int i = 0; i < block.childCount; i++)
            {
                VisualElement child = block[i];
                if (!child.ClassListContains(AdvancedVisibilityControlsClass)) continue;

                controls = child;
                break;
            }

            Button toggle = controls?.Q<Button>(className: AdvancedVisibilityToggleClass);
            if (toggle == null) return block;

            toggle.EnableInClassList(AdvancedVisibilityShownToggleClass, !isAdvanced);
            toggle.EnableInClassList(AdvancedVisibilityAdvancedToggleClass, isAdvanced);
            toggle.tooltip = isAdvanced
                ? "Only shown in Advanced mode. Click to show it in Standard mode."
                : "Shown in Standard mode. Click to move it to Advanced mode.";

            toggle.text = string.Empty;
            toggle.AddToClassList(AdvancedVisibilityCompactToggleClass);
            Image icon = new Image
            {
                image = EditorGUIUtility.IconContent(isAdvanced ? "animationvisibilitytoggleoff" : "animationvisibilitytoggleon").image,
                scaleMode = ScaleMode.ScaleToFit
            };
            icon.pickingMode = PickingMode.Ignore;
            icon.AddToClassList(AdvancedVisibilityToggleIconClass);
            toggle.Add(icon);

            return block;
        }

        internal static int GetAdvancedVisibilityStateHash()
        {
            EnsureAdvancedUI();
            return CommonUITK.GetAdvancedVisibilityStateHash(AI.ShowAdvanced(), AI.UICustomizationMode, AI.Config.advancedUI);
        }

        internal static bool AdvancedVisibilityStateChanged(ref int previousStateHash)
        {
            EnsureAdvancedUI();
            return CommonUITK.AdvancedVisibilityStateChanged(ref previousStateHash, AI.ShowAdvanced(), AI.UICustomizationMode, AI.Config.advancedUI);
        }

        internal static VisualElement CreateStringListControl(
            EditorWindow owner,
            string value,
            string separator,
            Action<string> onChange,
            string popupTitle,
            string tooltip,
            string textFieldClass = null,
            string editButtonClass = null)
        {
            return CommonUITK.CreateStringListControl(
                owner,
                value,
                separator,
                onChange,
                popupTitle,
                tooltip,
                InlineControlRowClass,
                textFieldClass,
                InlineGrowClass,
                ButtonClass,
                IconButtonClass,
                IconButtonImageClass,
                editButtonClass);
        }

        internal static Button CreateButton(string text, Action click)
        {
            return CommonUITK.CreateButton(text, click, ButtonClass);
        }

        internal static VisualElement CreateFooter()
        {
            return CommonUITK.CreateContainer(FooterClass);
        }

        internal static VisualElement CreateWindowFooter()
        {
            return CommonUITK.CreateWindowFooter(0f, 0f, FooterClass, WindowFooterClass);
        }

        internal static VisualElement CreateNavigationFooter()
        {
            return CommonUITK.CreateContainer(NavigationFooterClass);
        }

        internal static CommonUITK.ThreeZoneLayout CreateNavigationFooterLayout()
        {
            CommonUITK.ThreeZoneLayout footer = CommonUITK.CreateThreeZoneLayout(
                NavigationFooterClass,
                NavigationFooterGroupClass,
                NavigationFooterLeftClass,
                NavigationFooterCenterClass,
                NavigationFooterRightClass);
            PreserveNavigationFooterLeadingContentWidth(footer);
            return footer;
        }

        private static void PreserveNavigationFooterLeadingContentWidth(CommonUITK.ThreeZoneLayout footer)
        {
            float reservedWidth = -1f;

            void UpdateReservedWidth()
            {
                if (footer.Root.panel == null) return;

                float contentWidth = 0f;
                for (int i = 0; i < footer.Left.childCount; i++)
                {
                    VisualElement child = footer.Left[i];
                    if (child.resolvedStyle.display == DisplayStyle.None ||
                        child.resolvedStyle.position == Position.Absolute ||
                        float.IsNaN(child.layout.width))
                    {
                        continue;
                    }

                    contentWidth += child.resolvedStyle.marginLeft;
                    contentWidth += child.layout.width;
                    contentWidth += child.resolvedStyle.marginRight;
                }

                contentWidth = Mathf.Ceil(contentWidth);
                if (Mathf.Abs(contentWidth - reservedWidth) < 0.5f) return;

                reservedWidth = contentWidth;
                footer.Left.style.minWidth = contentWidth;
            }

            footer.Root.RegisterCallback<AttachToPanelEvent>(_ =>
                footer.Root.schedule.Execute(UpdateReservedWidth));
            footer.Root.RegisterCallback<GeometryChangedEvent>(_ => UpdateReservedWidth());
            footer.Left.RegisterCallback<GeometryChangedEvent>(_ => UpdateReservedWidth());
        }

        internal static VisualElement CreateNavigationFooterGroup(string extraClass = null)
        {
            return CommonUITK.CreateContainer(NavigationFooterGroupClass, extraClass);
        }

        internal static CommonPaginationControl CreatePaginationControl(EditorWindow owner)
        {
            CommonPaginationControl control = new CommonPaginationControl(owner, NavigationPaginationClasses);
            control.AddToClassList(NavigationFooterGroupClass);
            return control;
        }

        internal static Button CreateNavigationFooterIconButton(string tooltip, string iconName, bool active, Action click)
        {
            Button button = CreateIconButton(tooltip, iconName, click);
            button.AddToClassList(NavigationFooterButtonClass);
            button.AddToClassList(NavigationFooterIconButtonClass);
            SetNavigationFooterButtonActive(button, active);
            return button;
        }

        internal static void SetNavigationFooterButtonActive(Button button, bool active)
        {
            button?.EnableInClassList(NavigationFooterActiveClass, active);
        }

        internal static VisualElement CreateSegmentedControl(GUIContent[] options, int selectedIndex, Action<int> onSelect)
        {
            return CommonUITK.CreateSegmentedControl(options, selectedIndex, onSelect, NavigationSegmentedClasses);
        }

        internal static void RefreshSegmentedControl(VisualElement control, int selectedIndex)
        {
            CommonUITK.RefreshSegmentedControl(control, selectedIndex, NavigationFooterActiveClass);
        }

        internal static ProgressBar CreateProgressBar(string title, float progress)
        {
            return CommonUITK.CreateProgressBar(title, progress, ProgressBarClass);
        }

        internal static VisualElement CreateLogoHeader(float logoSize)
        {
            VisualElement header = new VisualElement();
            header.AddToClassList(LogoHeaderClass);

            if (Logo != null)
            {
                Image image = new Image
                {
                    image = Logo,
                    scaleMode = ScaleMode.ScaleToFit
                };
                image.AddToClassList(LogoHeaderImageClass);
                image.style.width = logoSize;
                image.style.height = logoSize;
                header.Add(image);
            }

            return header;
        }

        internal static VisualElement CreateFlexibleSpacer()
        {
            return CommonUITK.CreateFlexibleSpacer();
        }

        internal static void ShowAsDropDown(EditorWindow window, Rect guiAnchor, Vector2 windowSize)
        {
            CommonUITK.ShowAsDropDown(window, guiAnchor, windowSize);
        }

        internal static void ShowAsDropDown(EditorWindow window, EditorWindow owner, VisualElement anchor, Vector2 windowSize)
        {
            CommonUITK.ShowAsDropDown(window, owner, anchor, windowSize);
        }

        private static void EnsureAdvancedUI()
        {
            if (AI.Config.advancedUI == null)
            {
                AI.Config.ResetAdvancedUI();
            }
        }

        private static string GetHelpBoxClass(MessageType type)
        {
            switch (type)
            {
                case MessageType.Error:
                    return "ai-help-box-error";
                case MessageType.Warning:
                    return "ai-help-box-warning";
                case MessageType.None:
                    return "ai-help-box-neutral";
                default:
                    return "ai-help-box-info";
            }
        }

        private static string GetIconName(MessageType type)
        {
            switch (type)
            {
                case MessageType.Error:
                    return "console.erroricon";
                case MessageType.Warning:
                    return "console.warnicon";
                case MessageType.None:
                    return "console.infoicon";
                default:
                    return "console.infoicon";
            }
        }

        private sealed class ResultGridTextLabel : Label
        {
            private const float MeasureTolerance = 0.5f;
            private const string TwoLineMeasureText = "Ag\nAg";

            private string _fullText = string.Empty;
            private bool _refreshScheduled;

            public ResultGridTextLabel()
            {
                RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            }

            public void SetText(string value)
            {
                string fullText = value ?? string.Empty;
                if (string.Equals(_fullText, fullText, StringComparison.Ordinal))
                {
                    return;
                }

                _fullText = fullText;
                if (_fullText.Length == 0)
                {
                    _refreshScheduled = false;
                    if (!string.IsNullOrEmpty(text)) text = string.Empty;
                    return;
                }

                if (contentRect.width > 0f && contentRect.height > 0f)
                {
                    RefreshDisplayedText();
                }
                else if (!string.Equals(text, _fullText, StringComparison.Ordinal))
                {
                    text = _fullText;
                }
                ScheduleRefresh();
            }

            private void OnGeometryChanged(GeometryChangedEvent evt)
            {
                ScheduleRefresh();
            }

            private void ScheduleRefresh()
            {
                if (_refreshScheduled) return;

                _refreshScheduled = true;
                schedule.Execute(() =>
                {
                    _refreshScheduled = false;
                    RefreshDisplayedText();
                });
            }

            private void RefreshDisplayedText()
            {
                if (_fullText.Length == 0) return;

                float availableWidth = contentRect.width;
                if (availableWidth <= 0f || float.IsNaN(availableWidth))
                {
                    return;
                }

                string fittedText = FitTextWithMiddleEllipsis(
                    _fullText,
                    candidate => FitsAvailableSpace(candidate, availableWidth));
                if (!string.Equals(text, fittedText, StringComparison.Ordinal)) text = fittedText;
            }

            private bool FitsAvailableSpace(string candidate, float availableWidth)
            {
                if (resolvedStyle.whiteSpace == WhiteSpace.NoWrap)
                {
                    Vector2 measured = MeasureTextSize(
                        candidate,
                        0f,
                        VisualElement.MeasureMode.Undefined,
                        0f,
                        VisualElement.MeasureMode.Undefined);
                    return measured.x <= availableWidth + MeasureTolerance;
                }

                Vector2 wrapped = MeasureTextSize(
                    candidate,
                    availableWidth,
                    VisualElement.MeasureMode.Exactly,
                    0f,
                    VisualElement.MeasureMode.Undefined);
                Vector2 twoLines = MeasureTextSize(
                    TwoLineMeasureText,
                    availableWidth,
                    VisualElement.MeasureMode.Exactly,
                    0f,
                    VisualElement.MeasureMode.Undefined);
                return wrapped.y <= twoLines.y + MeasureTolerance;
            }
        }
    }
}
