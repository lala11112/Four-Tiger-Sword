using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[CustomEditor(typeof(WeaponActionDataSO), true)]
[CanEditMultipleObjects]
public sealed class FormActionDataEditor : Editor
{
    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();
        var script = serializedObject.FindProperty("m_Script");
        if (script != null)
        {
            var scriptField = new PropertyField(script);
            scriptField.SetEnabled(false);
            root.Add(scriptField);
        }

        root.Add(new HelpBox(
            "밸런스: 피해·자원·버프 / 동작·판정: 애니메이션·시간·범위·이동 / 타격 피드백: 히트스탑·이펙트·사운드·카메라\n"
            + "히트스탑은 개별 히트 → 공격 타수 → 플레이어 공통값 순서로 적용됩니다. Override Hit Stop을 켜면 해당 설정을 사용하며, 지속 시간 0은 히트스탑을 끕니다.",
            HelpBoxMessageType.Info));

        var iterator = serializedObject.GetIterator();
        for (bool enterChildren = true; iterator.NextVisible(enterChildren); enterChildren = false)
        {
            if (iterator.propertyPath != "m_Script")
                root.Add(FormActionInspectorFields.CreateField(iterator));
        }
        return root;
    }
}

[CustomPropertyDrawer(typeof(WeaponActionData))]
[CustomPropertyDrawer(typeof(HitEvent))]
[CustomPropertyDrawer(typeof(AttackExecutionSettings))]
[CustomPropertyDrawer(typeof(AttackMovementSettings))]
[CustomPropertyDrawer(typeof(AttackFeedbackSettings))]
[CustomPropertyDrawer(typeof(HitFeedbackSettings))]
public sealed class FormActionGroupDrawer : PropertyDrawer
{
    public override VisualElement CreatePropertyGUI(SerializedProperty property)
    {
        var foldout = new Foldout
        {
            text = FormActionInspectorFields.Label(property),
            tooltip = property.tooltip,
            value = property.isExpanded,
            bindingPath = property.propertyPath
        };

        var child = property.Copy();
        var end = property.GetEndProperty();
        for (bool enterChildren = true;
             child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end);
             enterChildren = false)
        {
            foldout.Add(FormActionInspectorFields.CreateField(child));
        }
        return foldout;
    }
}

internal static class FormActionInspectorFields
{
    public static PropertyField CreateField(SerializedProperty property)
        => new PropertyField(property.Copy(), Label(property));

    public static string Label(SerializedProperty property)
        => ResolveField(property)?.GetCustomAttribute<InspectorNameAttribute>()?.displayName
           ?? property.displayName;

    private static FieldInfo ResolveField(SerializedProperty property)
    {
        Type type = property.serializedObject.targetObject.GetType();
        FieldInfo field = null;
        string[] parts = property.propertyPath.Split('.');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == "Array" && i + 1 < parts.Length
                && parts[i + 1].StartsWith("data[", StringComparison.Ordinal))
            {
                type = type.IsArray ? type.GetElementType()
                    : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)
                        ? type.GetGenericArguments()[0] : null;
                if (type == null) return null;
                field = null;
                i++;
                continue;
            }

            field = FindField(type, parts[i]);
            if (field == null) return null;
            type = field.FieldType;
        }
        return field;
    }

    private static FieldInfo FindField(Type type, string name)
    {
        while (type != null)
        {
            var field = type.GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) return field;
            type = type.BaseType;
        }
        return null;
    }
}
