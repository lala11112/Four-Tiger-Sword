using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    public static class CommonInspectorTooltips
    {
        public static string Resolve(
            SerializedProperty property,
            string label = null,
            Func<SerializedProperty, string, string> productResolver = null)
        {
            if (property == null)
                return string.Empty;
            if (!string.IsNullOrWhiteSpace(property.tooltip))
                return property.tooltip;

            string productTooltip = productResolver?.Invoke(property, label);
            if (!string.IsNullOrWhiteSpace(productTooltip))
                return productTooltip;

            string subject = string.IsNullOrWhiteSpace(label) ? property.displayName : label;
            if (string.IsNullOrWhiteSpace(subject))
                subject = ObjectNames.NicifyVariableName(property.name);
            string lowerSubject = subject.ToLowerInvariant();

            if (property.isArray && property.propertyType != SerializedPropertyType.String)
                return "Edit the ordered " + lowerSubject + " entries.";

            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    return "Enable or disable " + lowerSubject + ".";
                case SerializedPropertyType.Enum:
                    return "Choose how " + lowerSubject + " is configured.";
                case SerializedPropertyType.ObjectReference:
                    return "Assign the " + lowerSubject + " used by this object.";
                case SerializedPropertyType.Color:
                    return "Set the color used for " + lowerSubject + ".";
                case SerializedPropertyType.String:
                    return "Enter the " + lowerSubject + ".";
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Float:
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector3:
                case SerializedPropertyType.Vector4:
                case SerializedPropertyType.Rect:
                case SerializedPropertyType.Bounds:
                    return "Set the " + lowerSubject + ".";
                default:
                    return "Configure the " + lowerSubject + " settings.";
            }
        }
    }

    /// <summary>
    /// Tracks the serialized properties represented by a semantic inspector and exposes any
    /// remaining top-level properties in a safe advanced foldout.
    /// </summary>
    public sealed class CommonSerializedInspectorLayout
    {
        readonly SerializedObject _owner;
        readonly Func<SerializedProperty, string, string> _tooltipResolver;
        readonly HashSet<string> _representedPaths = new HashSet<string>(StringComparer.Ordinal);

        public CommonSerializedInspectorLayout(
            SerializedObject owner,
            Func<SerializedProperty, string, string> tooltipResolver = null)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _tooltipResolver = tooltipResolver;
        }

        public SerializedObject Owner => _owner;

        public SerializedProperty Property(string path)
        {
            return _owner.FindProperty(path);
        }

        public void Consume(params string[] paths)
        {
            if (paths == null)
                return;

            for (int i = 0; i < paths.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(paths[i]))
                    _representedPaths.Add(paths[i]);
            }
        }

        public PropertyField Field(
            string path,
            string label = null,
            string name = null,
            string tooltip = null)
        {
            Consume(path);
            SerializedProperty property = Property(path);
            string resolvedTooltip = !string.IsNullOrWhiteSpace(tooltip)
                ? tooltip
                : CommonInspectorTooltips.Resolve(property, label, _tooltipResolver);
            return CommonInspectorElements.CreatePropertyField(
                _owner,
                path,
                label,
                name,
                resolvedTooltip);
        }

        public VisualElement Group(params string[] paths)
        {
            VisualElement group = CommonUITK.CreateContainer("common-inspector-property-group");
            if (paths == null)
                return group;

            for (int i = 0; i < paths.Length; i++)
            {
                PropertyField field = Field(paths[i]);
                if (field != null)
                    group.Add(field);
            }
            return group;
        }

        public VisualElement Group(params VisualElement[] elements)
        {
            VisualElement group = CommonUITK.CreateContainer("common-inspector-property-group");
            AddChildren(group, elements);
            return group;
        }

        public VisualElement Section(string title, string name, params VisualElement[] children)
        {
            return CommonInspectorElements.CreateSection(title, name, children);
        }

        public Foldout Foldout(string title, string name, bool expanded, params VisualElement[] children)
        {
            return CommonInspectorElements.CreateFoldoutSection(title, name, expanded, children);
        }

        public Foldout RemainingFoldout(string title, string name, bool expanded = false)
        {
            VisualElement group = CommonUITK.CreateContainer("common-inspector-property-group");
            SerializedProperty iterator = _owner.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.propertyPath == "m_Script" || _representedPaths.Contains(iterator.propertyPath))
                    continue;

                SerializedProperty copy = iterator.Copy();
                PropertyField field = CommonInspectorElements.CreatePropertyField(
                    _owner,
                    copy.propertyPath,
                    tooltip: CommonInspectorTooltips.Resolve(copy, productResolver: _tooltipResolver));
                if (field != null)
                    group.Add(field);
            }

            Foldout foldout = Foldout(title, name, expanded, group);
            foldout.style.display = group.childCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            return foldout;
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
    }
}
