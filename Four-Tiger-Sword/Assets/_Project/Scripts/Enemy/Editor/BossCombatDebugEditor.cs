using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[CustomEditor(typeof(Boss), true), CanEditMultipleObjects]
public sealed class BossCombatDebugEditor : Editor
{
    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();
        var diagnostics = new Foldout { text = "보스 전투 상태 (읽기 전용)", value = true };
        var status = new Label { enableRichText = false };
        diagnostics.Add(status);
        root.Add(diagnostics);
        var fields = new VisualElement();
        InspectorElement.FillDefaultInspector(fields, serializedObject, this);
        root.Add(fields);

        void Refresh()
        {
            if (!EditorApplication.isPlaying)
            {
                status.text = "플레이 모드에서 현재 패턴, 단계, 선택 이유와 실패 원인을 표시합니다.";
                return;
            }
            var text = new StringBuilder();
            foreach (var selected in targets)
            {
                if (!(selected is Boss boss) || boss == null) continue;
                if (text.Length > 0) text.AppendLine();
                text.AppendLine(boss.name);
                text.Append("대상: ").AppendLine(boss.CombatTarget != null ? boss.CombatTarget.name : "없음");
                text.Append("페이즈: ").AppendLine((boss.PhaseIndex + 1).ToString());
                var brain = boss.Brain as BossBrain;
                text.Append("공통 상태: ").AppendLine(brain != null ? brain.StateName : "초기화 대기");
                text.Append("페이즈 전환 중: ").AppendLine(boss.IsTransitioning ? "예" : "아니오");
                text.Append("그로기: ").AppendLine(boss.IsGroggy ? "예" : "아니오");
                if (boss.ActiveCombatProfile == null || brain?.Tactical == null)
                {
                    text.AppendLine(boss.ActiveCombatProfile == null ? "전투 프로필을 사용하지 않는 기존 보스입니다." : "전투 컨트롤러 초기화 대기 중입니다.");
                    continue;
                }
                var tactical = brain.Tactical;
                text.Append("상태: ").AppendLine(tactical.DebugState);
                text.Append("패턴: ").AppendLine(tactical.DebugPattern);
                text.Append("단계: ").AppendLine(tactical.DebugStep >= 0 ? tactical.DebugStep.ToString() : "없음");
                text.Append("판단 이유: ").AppendLine(tactical.DebugReason);
                text.Append("최근 실패: ").AppendLine(string.IsNullOrEmpty(tactical.LastFailure) ? "없음" : tactical.LastFailure);
            }
            status.text = text.ToString();
        }

        root.RegisterCallback<AttachToPanelEvent>(_ => Refresh());
        root.schedule.Execute(Refresh).Every(100);
        return root;
    }
}
