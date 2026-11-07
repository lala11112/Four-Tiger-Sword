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
        root.Add(new HelpBox("연계 스킬의 조건·준비 시간·효과는 Chain Skill에서, 공격 동작은 Actions의 교체 스킬에서 설정합니다. 교체 스킬을 비워두면 F로 발동할 수 없습니다.",
            HelpBoxMessageType.Info));
        root.Add(new HelpBox("카메라는 Camera Profile의 공통 설정을 사용합니다. 개별 카메라 설정은 차이가 필요한 공격에만 추가하세요. 개별 타격은 해당 공격의 카메라 설정을 상속합니다.",
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

    internal static Foldout CreateGroup(SerializedProperty property)
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

[CustomPropertyDrawer(typeof(CombatCameraHitSettings), true)]
public sealed class CombatCameraOverrideDrawer : PropertyDrawer
{
    public override VisualElement CreatePropertyGUI(SerializedProperty property)
    {
        if (property.propertyType != SerializedPropertyType.ManagedReference)
            return FormActionGroupDrawer.CreateGroup(property);

        var root = new VisualElement();
        var serialized = property.serializedObject;
        string path = property.propertyPath;
        bool isAttack = fieldInfo.FieldType == typeof(CombatCameraV2Settings);
        Action rebuild = null;
        rebuild = () =>
        {
            serialized.UpdateIfRequiredOrScript();
            var current = serialized.FindProperty(path);
            root.Clear();
            if (current == null) return;
            bool mixed = current.hasMultipleDifferentValues;
            bool custom = current.managedReferenceValue != null;
            root.Add(new Label(mixed ? "카메라 설정이 서로 다릅니다. 개별 값은 에셋을 하나씩 선택해 편집하세요."
                : custom ? FormActionInspectorFields.Label(current) : "카메라: 공통 설정 사용"));
            var toggle = new Button(() =>
            {
                foreach (var target in serialized.targetObjects)
                {
                    using var single = new SerializedObject(target);
                    var value = single.FindProperty(path);
                    if (value == null) continue;
                    if (mixed && value.managedReferenceValue != null) continue;
                    value.managedReferenceValue = custom && !mixed ? null
                        : isAttack ? (CombatCameraHitSettings)new CombatCameraV2Settings()
                        : new CombatCameraHitSettings();
                    single.ApplyModifiedProperties();
                }
                rebuild();
            }) { text = custom && !mixed ? "개별 설정 지우고 공통 설정 사용" : "개별 카메라 설정 추가" };
            if (mixed)
            {
                bool hasEmpty = false;
                foreach (var target in serialized.targetObjects)
                {
                    using var single = new SerializedObject(target);
                    var value = single.FindProperty(path);
                    hasEmpty |= value != null && value.managedReferenceValue == null;
                }
                toggle.SetEnabled(hasEmpty);
            }
            root.Add(toggle);
            if (!mixed && custom)
            {
                var group = FormActionGroupDrawer.CreateGroup(current);
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
