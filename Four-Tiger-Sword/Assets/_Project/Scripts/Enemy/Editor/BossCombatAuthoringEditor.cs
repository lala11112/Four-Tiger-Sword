using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

[CustomEditor(typeof(BossCombatProfileSO)), CanEditMultipleObjects]
public sealed class BossCombatProfileEditor : Editor
{
    public override VisualElement CreateInspectorGUI()
        => BossCombatAuthoringInspector.Create(serializedObject, this,
            () => ((BossCombatProfileSO)target).Validate(),
            "전투 대상을 계속 유지하며 패턴 조건으로 공격을 고릅니다. 가시성은 패턴 조건이며 탐색·순찰 상태를 만들지 않습니다.");
}

[CustomEditor(typeof(BossTacticalPatternSO)), CanEditMultipleObjects]
public sealed class BossTacticalPatternEditor : Editor
{
    public override VisualElement CreateInspectorGUI()
        => BossCombatAuthoringInspector.Create(serializedObject, this,
            () => ((BossTacticalPatternSO)target).Validate(),
            "단계 인덱스는 0부터 시작합니다. Next -2는 다음 순서, -1은 종료입니다. 분기는 이전 행동이 끝난 경계에서만 평가합니다.");
}

internal static class BossCombatAuthoringInspector
{
    public static VisualElement Create(SerializedObject serialized, Editor editor,
        Func<IEnumerable<string>> validate, string instructions)
    {
        var root = new VisualElement();
        root.Add(new HelpBox(instructions, HelpBoxMessageType.Info));
        var messages = new VisualElement();
        root.Add(messages);
        var fields = new VisualElement();
        InspectorElement.FillDefaultInspector(fields, serialized, editor);
        root.Add(fields);
        Action refresh = () =>
        {
            messages.Clear();
            foreach (var selected in serialized.targetObjects)
            {
                IEnumerable<string> issues = selected is BossCombatProfileSO profile ? profile.Validate()
                    : selected is BossTacticalPatternSO pattern ? pattern.Validate() : validate();
                foreach (string issue in issues)
                    messages.Add(new HelpBox((serialized.isEditingMultipleObjects ? selected.name + ": " : "") + issue,
                        HelpBoxMessageType.Error));
            }
        };
        root.TrackSerializedObjectValue(serialized, _ => refresh());
        root.RegisterCallback<AttachToPanelEvent>(_ => refresh());
        return root;
    }
}
