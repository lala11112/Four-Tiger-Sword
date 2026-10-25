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
        if (property.propertyType == SerializedPropertyType.ManagedReference && property.name == "FinisherStep")
            return CreateOptionalFinisher(property);
        return CreateGroup(property);
    }

    private static Foldout CreateGroup(SerializedProperty property)
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

    private static VisualElement CreateOptionalFinisher(SerializedProperty property)
    {
        var root = new VisualElement();
        var serialized = property.serializedObject;
        string path = property.propertyPath;
        Action rebuild = null;
        rebuild = () =>
        {
            serialized.UpdateIfRequiredOrScript();
            var current = serialized.FindProperty(path);
            root.Clear();
            if (current == null) return;
            bool canCopy = true, canClear = false;
            foreach (var target in serialized.targetObjects)
            {
                var data = target as WeaponActionDataSO;
                canCopy &= data?.ComboSteps != null && data.ComboSteps.Count > 2 && data.ComboSteps[2] != null;
                canClear |= data?.FinisherStep != null;
            }
            root.Add(new HelpBox("강화 3타는 비워두면 일반 3타를 그대로 사용합니다. 별도 편집이 필요할 때 일반 3타를 복사하세요.", HelpBoxMessageType.Info));
            var copy = new Button(() =>
            {
                foreach (var target in serialized.targetObjects)
                {
                    if (!(target is WeaponActionDataSO data) || data.ComboSteps == null
                        || data.ComboSteps.Count < 3 || data.ComboSteps[2] == null) continue;
                    Undo.RecordObject(data, "Copy normal third stage to finisher");
                    data.FinisherStep = FinisherDataSetup.CloneStep(data.ComboSteps[2]);
                    EditorUtility.SetDirty(data);
                }
                serialized.Update();
                rebuild();
            }) { text = "일반 3타를 복사해 별도 편집" };
            copy.SetEnabled(canCopy);
            root.Add(copy);
            var clear = new Button(() =>
            {
                foreach (var target in serialized.targetObjects)
                {
                    if (!(target is WeaponActionDataSO data)) continue;
                    Undo.RecordObject(data, "Use normal third stage for finisher");
                    data.FinisherStep = null;
                    EditorUtility.SetDirty(data);
                }
                serialized.Update();
                rebuild();
            }) { text = "별도 설정 지우고 일반 3타 사용" };
            clear.SetEnabled(canClear);
            root.Add(clear);
            if (!current.hasMultipleDifferentValues && current.managedReferenceValue != null)
            {
                var group = CreateGroup(current);
                root.Add(group);
                group.Bind(serialized);
            }
        };
        rebuild();
        root.TrackPropertyValue(property.Copy(), _ => rebuild());
        return root;
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
