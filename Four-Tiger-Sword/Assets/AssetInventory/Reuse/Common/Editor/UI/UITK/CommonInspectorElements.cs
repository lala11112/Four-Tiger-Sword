using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public enum CommonInspectorStatusType
    {
        Info,
        Success,
        Warning,
        Error,
        Pending
    }

    /// <summary>
    /// Shared UI Toolkit building blocks for polished commercial-tool inspectors.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static partial class CommonInspectorElements
    {
        const string AnchorAssetName = "CommonInspectorElements";
        const string AnchorPathSuffix = "Editor/UI/UITK/CommonInspectorElements.cs";
        const string StylePathSuffix = "Editor/UI/UITK/CommonInspector.uss";
        const string SectionTitleClass = "common-inspector-section__title";
        const string TitlelessSectionClass = "common-inspector-section--titleless";
        static readonly string[] s_StatusClasses =
        {
            "common-status--info",
            "common-status--success",
            "common-status--warning",
            "common-status--error",
            "common-status--pending"
        };
        static readonly string[] s_HelpBoxClasses =
        {
            "common-inspector-help-box--none",
            "common-inspector-help-box--info",
            "common-inspector-help-box--warning",
            "common-inspector-help-box--error"
        };
        static readonly ConditionalWeakTable<VisualElement, InspectorInteractionState> s_InteractionStates =
            new ConditionalWeakTable<VisualElement, InspectorInteractionState>();

        public static VisualElement CreateRoot(
            string name,
            StyleSheet productStyleSheet = null,
            params string[] productClasses)
        {
            VisualElement root = new VisualElement
            {
                name = name
            };
            CommonUITK.ApplyRoot(
                root,
                LoadSharedStyleSheet(),
                "common-inspector-root",
                "common-inspector-theme-dark",
                "common-inspector-theme-light");
            CommonUITK.ApplyRoot(root, productStyleSheet, null);
            CommonUITK.AddClasses(root, productClasses);
            root.RegisterCallback<GeometryChangedEvent>(evt =>
                ApplyResponsiveClasses(root, evt.newRect.width));
            return root;
        }

        public static StyleSheet LoadSharedStyleSheet()
        {
            return CommonUITK.LoadStyleSheetFromAnchor(
                AnchorAssetName,
                AnchorPathSuffix,
                StylePathSuffix);
        }

        public static VisualElement CreateSection(
            string title,
            string name,
            params VisualElement[] children)
        {
            VisualElement section = CommonUITK.CreateSection(
                title,
                "common-inspector-section",
                SectionTitleClass);
            section.name = name;
            section.EnableInClassList(TitlelessSectionClass, string.IsNullOrWhiteSpace(title));
            AddChildren(section, children);
            ConfigureRepeatedSectionTitleSuppression(section, title);
            return section;
        }

        public static Foldout CreateFoldoutSection(
            string title,
            string name,
            bool expanded,
            params VisualElement[] children)
        {
            Foldout foldout = CommonUITK.CreateFoldout(
                title,
                expanded,
                null,
                null,
                "common-inspector-section",
                "common-inspector-section--foldout");
            foldout.name = name;
            foldout.viewDataKey = "common-inspector-" + MakeElementName(name ?? title);
            AddChildren(foldout, children);
            return foldout;
        }

        public static PropertyField CreatePropertyField(
            SerializedObject owner,
            string propertyPath,
            string label = null,
            string name = null,
            string tooltip = null)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));

            SerializedProperty property = owner.FindProperty(propertyPath);
            if (property == null)
                return null;

            PropertyField field = string.IsNullOrWhiteSpace(label)
                ? new PropertyField(property)
                : new PropertyField(property, label);
            field.name = string.IsNullOrWhiteSpace(name)
                ? "common-inspector-field-" + MakeElementName(propertyPath)
                : name;
            string resolvedTooltip = !string.IsNullOrWhiteSpace(tooltip)
                ? tooltip
                : CommonInspectorTooltips.Resolve(property, label);
            if (!string.IsNullOrWhiteSpace(resolvedTooltip))
                field.tooltip = resolvedTooltip;
            field.AddToClassList("common-inspector-property-field");
            field.AddToClassList("unity-base-field__aligned");
            return field;
        }

        public static VisualElement CreateDefaultProperties(
            SerializedObject owner,
            params string[] excludedPropertyPaths)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));

            VisualElement container = CommonUITK.CreateContainer("common-inspector-property-group");
            HashSet<string> excluded = new HashSet<string>(excludedPropertyPaths ?? Array.Empty<string>());
            SerializedProperty iterator = owner.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.propertyPath == "m_Script" || excluded.Contains(iterator.propertyPath))
                    continue;

                PropertyField field = CreatePropertyField(owner, iterator.propertyPath);
                if (field != null)
                    container.Add(field);
            }

            return container;
        }

        public static Label CreateMutedText(string text, string name = null)
        {
            Label label = CommonUITK.CreateLabel(text ?? string.Empty, "common-inspector-muted");
            label.name = name;
            return label;
        }

        public static HelpBox CreateHelpBox(
            string text,
            HelpBoxMessageType type = HelpBoxMessageType.Info,
            string name = null)
        {
            HelpBox box = CommonUITK.CreateHelpBox(
                text ?? string.Empty,
                type,
                "common-inspector-help-box");
            box.name = name;
            SetHelpBoxMessageType(box, type);
            return box;
        }

        public static void SetHelpBoxMessageType(HelpBox box, HelpBoxMessageType type)
        {
            if (box == null)
                return;

            box.messageType = type;
            string activeClass = "common-inspector-help-box--" + type.ToString().ToLowerInvariant();
            for (int i = 0; i < s_HelpBoxClasses.Length; i++)
                box.EnableInClassList(s_HelpBoxClasses[i], s_HelpBoxClasses[i] == activeClass);
        }

        public static Label CreateStatus(
            string text,
            CommonInspectorStatusType statusType,
            string name = null)
        {
            Label label = CommonUITK.CreateLabel(text ?? string.Empty, "common-inspector-status");
            label.name = name;
            SetStatus(label, text, statusType);
            return label;
        }

        public static void SetStatus(
            Label label,
            string text,
            CommonInspectorStatusType statusType)
        {
            if (label == null)
                return;

            label.text = text ?? string.Empty;
            string activeClass = "common-status--" + statusType.ToString().ToLowerInvariant();
            for (int i = 0; i < s_StatusClasses.Length; i++)
                label.EnableInClassList(s_StatusClasses[i], s_StatusClasses[i] == activeClass);
        }

        public static Button CreateButton(
            string text,
            string tooltip,
            Action click,
            bool primary = false,
            bool destructive = false)
        {
            Button button = CommonUITK.CreateButton(
                text,
                click,
                "common-inspector-button",
                primary ? "common-inspector-button--primary" : null,
                destructive ? "common-inspector-button--destructive" : null);
            button.tooltip = tooltip ?? string.Empty;
            return button;
        }

        public static Button CreateCompactCreateButton(Action click, string name, string tooltip)
        {
            Button button = CommonUITK.CreateButton(
                "+",
                click,
                "common-inspector-compact-create");
            button.name = name;
            button.tooltip = tooltip ?? string.Empty;
            return button;
        }

        public static VisualElement CreateAssetReferenceRow(
            VisualElement field,
            Button createButton,
            string name)
        {
            VisualElement row = CommonUITK.CreateContainer(
                "common-inspector-form-row",
                "common-inspector-asset-row");
            row.name = name;
            if (field != null)
            {
                field.AddToClassList("common-inspector-form-field");
                row.Add(field);
            }
            if (createButton != null)
                row.Add(createButton);
            return row;
        }

        public static VisualElement CreateAssetReferenceRow(
            SerializedObject owner,
            string propertyPath,
            string label,
            string rowName,
            string buttonName,
            string createTooltip,
            Action createAction)
        {
            SerializedProperty property = owner.FindProperty(propertyPath);
            PropertyField field = CreatePropertyField(owner, propertyPath, label);
            if (field == null)
                return null;

            Button createButton = CreateCompactCreateButton(createAction, buttonName, createTooltip);
            VisualElement row = CreateAssetReferenceRow(field, createButton, rowName);
            if (property != null)
            {
                row.TrackPropertyValue(
                    property,
                    _ => SetCompactCreateButtonAvailable(createButton, property.objectReferenceValue == null));
            }
            SetCompactCreateButtonAvailable(
                createButton,
                property == null || property.objectReferenceValue == null);
            return row;
        }

        public static void SetCompactCreateButtonAvailable(Button button, bool available)
        {
            if (button == null)
                return;

            button.style.display = DisplayStyle.Flex;
            button.style.visibility = available ? Visibility.Visible : Visibility.Hidden;
            button.SetEnabled(available);
        }

        public static VisualElement CreateButtonRow(params VisualElement[] controls)
        {
            VisualElement row = CommonUITK.CreateContainer("common-inspector-button-row");
            AddChildren(row, controls);
            return row;
        }

        public static VisualElement CreateValueAlignedRow(VisualElement content, string name = null)
        {
            VisualElement row = CommonUITK.CreateContainer("common-inspector-value-row");
            row.name = name;
            row.Add(CommonUITK.CreateContainer("common-inspector-value-row__spacer"));
            if (content != null)
            {
                content.AddToClassList("common-inspector-value-row__content");
                row.Add(content);
            }
            return row;
        }

        public static VisualElement CreateCollectionCard(string name, int headerActionCount = 0)
        {
            VisualElement card = CommonUITK.CreateContainer("common-inspector-collection-card");
            card.name = name;
            ConfigureCollectionCard(card, headerActionCount);
            return card;
        }

        public static void ConfigureCollectionCard(VisualElement card, int headerActionCount = 0)
        {
            if (card == null)
                throw new ArgumentNullException(nameof(card));

            card.AddToClassList("common-inspector-collection-card");
            card.AddToClassList("common-inspector-collection-card--actions-" + Math.Max(0, headerActionCount));
            card.RegisterCallback<FocusInEvent>(_ => card.AddToClassList("common-inspector-collection-card--focus-within"));
            card.RegisterCallback<FocusOutEvent>(evt =>
            {
                VisualElement next = evt.relatedTarget as VisualElement;
                if (!IsDescendantOf(next, card))
                    card.RemoveFromClassList("common-inspector-collection-card--focus-within");
            });
        }

        public static Foldout CreateCollectionFoldout(
            string title,
            string name,
            bool expanded,
            string tooltip = null)
        {
            Foldout foldout = CommonUITK.CreateFoldout(
                title,
                expanded,
                null,
                tooltip,
                "common-inspector-collection-card__foldout");
            foldout.name = name;
            foldout.viewDataKey = "common-inspector-card-" + MakeElementName(name ?? title);
            return foldout;
        }

        public static VisualElement CreateCollectionHeaderActions(VisualElement card, string name)
        {
            if (card == null)
                throw new ArgumentNullException(nameof(card));

            VisualElement actions = CommonUITK.CreateContainer("common-inspector-collection-card__actions");
            actions.name = name;
            card.Add(actions);
            return actions;
        }

        public static Button CreateCollectionHeaderButton(
            string text,
            string tooltip,
            Action click,
            bool destructive = false)
        {
            Button button = CommonUITK.CreateButton(
                text,
                click,
                "common-inspector-collection-card__header-button",
                destructive ? "common-inspector-collection-card__header-button--destructive" : null);
            button.tooltip = tooltip ?? string.Empty;
            return button;
        }

        public static Button CreateCollectionHeaderIconButton(
            string iconName,
            string fallbackText,
            string tooltip,
            Action click,
            bool destructive = false)
        {
            Button button = CreateCollectionHeaderButton(string.Empty, tooltip, click, destructive);
            GUIContent content = EditorGUIUtility.IconContent(iconName);
            if (content.image == null)
            {
                button.text = fallbackText ?? string.Empty;
                return button;
            }

            Image image = new Image
            {
                image = content.image,
                pickingMode = PickingMode.Ignore,
                scaleMode = ScaleMode.ScaleToFit
            };
            image.AddToClassList("common-inspector-collection-card__header-icon");
            button.Add(image);
            return button;
        }

        public static VisualElement CreateCollectionFooter(params VisualElement[] controls)
        {
            VisualElement footer = CommonUITK.CreateContainer("common-inspector-collection-card__footer");
            AddChildren(footer, controls);
            return footer;
        }

        public static VisualElement CreateCollectionAddArea(params VisualElement[] controls)
        {
            VisualElement addArea = CommonUITK.CreateContainer("common-inspector-collection-add-area");
            AddChildren(addArea, controls);
            return addArea;
        }

        public static void TrackVisibility(
            VisualElement tracker,
            SerializedProperty property,
            Func<SerializedProperty, bool> predicate,
            params VisualElement[] elements)
        {
            if (tracker == null)
                throw new ArgumentNullException(nameof(tracker));
            if (property == null)
                return;
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            Action refresh = () => SetVisible(
                property.hasMultipleDifferentValues || predicate(property),
                elements);
            tracker.TrackPropertyValue(property, _ => refresh());
            refresh();
        }

        public static void TrackVisibility(
            VisualElement tracker,
            IReadOnlyList<SerializedProperty> properties,
            Func<bool> predicate,
            params VisualElement[] elements)
        {
            if (tracker == null)
                throw new ArgumentNullException(nameof(tracker));
            if (properties == null)
                throw new ArgumentNullException(nameof(properties));
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            Action refresh = () =>
            {
                bool hasMixedValue = false;
                for (int i = 0; i < properties.Count; i++)
                {
                    if (properties[i] != null && properties[i].hasMultipleDifferentValues)
                    {
                        hasMixedValue = true;
                        break;
                    }
                }
                SetVisible(hasMixedValue || predicate(), elements);
            };

            for (int i = 0; i < properties.Count; i++)
            {
                SerializedProperty property = properties[i];
                if (property != null)
                    tracker.TrackPropertyValue(property, _ => refresh());
            }
            refresh();
        }

        public static void SetVisible(bool visible, params VisualElement[] elements)
        {
            if (elements == null)
                return;

            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i] != null)
                    elements[i].style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>
        /// Schedules a recurring inspector refresh without allowing it to run during pointer-driven
        /// field edits. Retained inspector refreshes can otherwise invalidate a numeric field's drag
        /// capture before the user releases the mouse.
        /// </summary>
        public static IVisualElementScheduledItem SchedulePeriodicRefresh(
            VisualElement inspectorRoot,
            Action refresh,
            long intervalMs)
        {
            return SchedulePeriodicRefresh(inspectorRoot, refresh, null, intervalMs);
        }

        /// <summary>
        /// Schedules a recurring inspector refresh with a lightweight fallback that remains active
        /// during pointer-driven field edits. The interaction refresh must not update bindings,
        /// rebuild retained containers, or otherwise disturb the active field's pointer capture.
        /// </summary>
        public static IVisualElementScheduledItem SchedulePeriodicRefresh(
            VisualElement inspectorRoot,
            Action refresh,
            Action interactionRefresh,
            long intervalMs)
        {
            if (inspectorRoot == null)
                throw new ArgumentNullException(nameof(inspectorRoot));
            if (refresh == null)
                throw new ArgumentNullException(nameof(refresh));
            if (intervalMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(intervalMs));

            InspectorInteractionState interactionState = GetInteractionState(inspectorRoot);
            return inspectorRoot.schedule.Execute(() =>
            {
                if (interactionState.BlocksPeriodicRefresh)
                    interactionRefresh?.Invoke();
                else
                    refresh();
            }).Every(intervalMs);
        }

        /// <summary>
        /// Observes the lifetime of pointer interactions inside an inspector root. Capture transfers
        /// do not end the interaction; only the matching pointer up, cancellation, or panel detach
        /// invokes <paramref name="ended"/>.
        /// </summary>
        public static void TrackPointerInteraction(
            VisualElement inspectorRoot,
            Action started,
            Action ended)
        {
            if (inspectorRoot == null)
                throw new ArgumentNullException(nameof(inspectorRoot));
            if (started == null)
                throw new ArgumentNullException(nameof(started));
            if (ended == null)
                throw new ArgumentNullException(nameof(ended));

            GetInteractionState(inspectorRoot).Register(started, ended);
        }

        internal static bool IsPeriodicRefreshBlocked(VisualElement inspectorRoot)
        {
            if (inspectorRoot == null)
                throw new ArgumentNullException(nameof(inspectorRoot));

            return GetInteractionState(inspectorRoot).BlocksPeriodicRefresh;
        }

        static InspectorInteractionState GetInteractionState(VisualElement inspectorRoot)
        {
            return s_InteractionStates.GetValue(
                inspectorRoot,
                root => new InspectorInteractionState(root));
        }

        static void AddChildren(VisualElement parent, VisualElement[] children)
        {
            if (children == null)
                return;

            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != null)
                    parent.Add(children[i]);
            }
        }

        static void ApplyResponsiveClasses(VisualElement root, float width)
        {
            root.EnableInClassList("common-inspector--compact", width > 0f && width < 360f);
            root.EnableInClassList("common-inspector--wide", width >= 680f);
        }

        static void ConfigureRepeatedSectionTitleSuppression(VisualElement section, string title)
        {
            if (section == null || string.IsNullOrWhiteSpace(title))
                return;

            Label sectionTitle = section.Q<Label>(className: SectionTitleClass);
            if (sectionTitle == null || TrySuppressRepeatedSectionTitle(section, sectionTitle, title))
                return;

            EventCallback<GeometryChangedEvent> onGeometryChanged = null;
            onGeometryChanged = _ =>
            {
                if (!TrySuppressRepeatedSectionTitle(section, sectionTitle, title))
                    return;

                section.UnregisterCallback(onGeometryChanged);
            };
            section.RegisterCallback(onGeometryChanged);
        }

        static bool TrySuppressRepeatedSectionTitle(
            VisualElement section,
            Label sectionTitle,
            string title)
        {
            Label firstContentLabel = FindFirstContentLabel(section, sectionTitle);
            if (firstContentLabel == null)
                return false;

            if (string.Equals(title.Trim(), firstContentLabel.text.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                sectionTitle.RemoveFromHierarchy();
                section.AddToClassList("common-inspector-section--native-title");
                section.AddToClassList(TitlelessSectionClass);
            }

            return true;
        }

        static Label FindFirstContentLabel(VisualElement section, Label sectionTitle)
        {
            int childCount = section.hierarchy.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Label label = FindFirstVisibleLabel(section.hierarchy[i], sectionTitle);
                if (label != null)
                    return label;
            }

            return null;
        }

        static Label FindFirstVisibleLabel(VisualElement element, Label sectionTitle)
        {
            // The title may be nested in a header row, so skip it at any depth.
            if (element == null || ReferenceEquals(element, sectionTitle) || element.resolvedStyle.display == DisplayStyle.None)
                return null;

            Label label = element as Label;
            if (label != null && !string.IsNullOrWhiteSpace(label.text))
                return label;

            int childCount = element.hierarchy.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Label childLabel = FindFirstVisibleLabel(element.hierarchy[i], sectionTitle);
                if (childLabel != null)
                    return childLabel;
            }

            return null;
        }

        static string MakeElementName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "element";

            char[] characters = value.ToCharArray();
            for (int i = 0; i < characters.Length; i++)
            {
                char character = characters[i];
                if (!char.IsLetterOrDigit(character) && character != '_' && character != '-')
                    characters[i] = '-';
            }

            return new string(characters);
        }

        static bool IsDescendantOf(VisualElement element, VisualElement ancestor)
        {
            while (element != null)
            {
                if (ReferenceEquals(element, ancestor))
                    return true;
                element = element.parent;
            }
            return false;
        }

        sealed class InspectorInteractionState
        {
            readonly HashSet<int> _activePointerIds = new HashSet<int>();
            readonly HashSet<int> _buttonPointerIds = new HashSet<int>();
            readonly VisualElement _root;
            Action _started;
            Action _ended;

            public InspectorInteractionState(VisualElement root)
            {
                _root = root;
                root.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
                root.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
                root.RegisterCallback<PointerCancelEvent>(OnPointerCancel, TrickleDown.TrickleDown);
                root.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            }

            public bool IsPointerInteractionActive => _activePointerIds.Count > 0;
            public bool BlocksPeriodicRefresh => _activePointerIds.Count > _buttonPointerIds.Count;

            public void Register(Action started, Action ended)
            {
                _started += started;
                _ended += ended;
            }

            void OnPointerDown(PointerDownEvent evt)
            {
                bool wasActive = IsPointerInteractionActive;
                _activePointerIds.Add(evt.pointerId);
                if (IsButtonInteraction(evt.target as VisualElement))
                    _buttonPointerIds.Add(evt.pointerId);
                else
                    _buttonPointerIds.Remove(evt.pointerId);
                if (!wasActive)
                    _started?.Invoke();
            }

            void OnPointerUp(PointerUpEvent evt)
            {
                EndPointerInteraction(evt.pointerId);
            }

            void OnPointerCancel(PointerCancelEvent evt)
            {
                EndPointerInteraction(evt.pointerId);
            }

            void OnDetachFromPanel(DetachFromPanelEvent evt)
            {
                bool wasActive = IsPointerInteractionActive;
                _activePointerIds.Clear();
                _buttonPointerIds.Clear();
                if (wasActive)
                    _ended?.Invoke();
            }

            void EndPointerInteraction(int pointerId)
            {
                bool wasActive = IsPointerInteractionActive;
                _activePointerIds.Remove(pointerId);
                _buttonPointerIds.Remove(pointerId);
                if (wasActive && !IsPointerInteractionActive)
                    _ended?.Invoke();
            }

            bool IsButtonInteraction(VisualElement element)
            {
                while (element != null)
                {
                    if (element is Button)
                        return true;
                    if (ReferenceEquals(element, _root))
                        return false;
                    element = element.parent;
                }

                return false;
            }
        }
    }
}
