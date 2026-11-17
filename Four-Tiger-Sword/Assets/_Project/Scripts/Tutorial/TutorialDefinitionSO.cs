using System;
using UnityEngine;

public enum TutorialProgressStatus { NotStarted, InMap, HubLessons, Completed, Skipped }
public enum TutorialShopPhase { None, Buy, Sell, Fuse, Complete }
public enum TutorialStep
{
    Move, EnterTraining, Attack, Dodge, Skill, AirAttack, Ultimate, DefeatTraining,
    ClaimChest, OpenMap, TravelToStart, ReachShop, Buy, Sell, Fuse, EnterFreeCombat,
    FreeCombat, ReachExit, MapComplete
}

public enum TutorialHighlightTarget
{
    Automatic, None, Movement, TrainingEnemy, TrainingChest, StartRoomOnMap, Shop, FreeCombatRoom, ExitPortal
}

[Serializable]
public sealed class TutorialStepDefinition
{
    public TutorialStep step;
    public string title;
    [TextArea] public string body;
    public string keys;
    public TutorialHighlightTarget highlightTarget;
}

[CreateAssetMenu(menuName = "Four Tiger Sword/Tutorial Definition")]
public sealed class TutorialDefinitionSO : ScriptableObject
{
    public RunDefinitionSO runDefinition;
    public int startRoomId = 0, trainingRoomId = 1, shopRoomId = 2, freeCombatRoomId = 3, exitRoomId = 4;
    public Vector3 movementTargetLocal = new(4f, 1.05f, 3f);
    public Vector3 retrySpawnLocal = new(-6f, 1.05f, -6f);
    [Min(0)] public int supportStones = 120;
    public TutorialStepDefinition[] steps = Array.Empty<TutorialStepDefinition>();

    public TutorialStepDefinition GetStep(TutorialStep step)
    {
        var configured = FindStep(step);
        if (configured != null) return configured;
        var text = step switch
        {
            TutorialStep.Move => ("이동", "표시된 지점까지 이동하세요.", "W A S D"),
            TutorialStep.EnterTraining => ("전투 훈련", "열린 문을 지나 첫 몬스터방으로 들어가세요.", "W A S D"),
            TutorialStep.Attack => ("기본 공격", "연습 적을 기본 공격으로 한 번 맞히세요.", "마우스 왼쪽 버튼"),
            TutorialStep.Dodge => ("회피", "회피를 사용해 빠르게 이동하세요.", "왼쪽 Shift"),
            TutorialStep.Skill => ("스킬", "스킬을 사용하세요. 사용 후에는 재사용 대기시간이 적용됩니다.", "E"),
            TutorialStep.AirAttack => ("점프 공격", "점프한 뒤 공격해 적을 맞히고 착지하세요.", "Space → 마우스 왼쪽 버튼"),
            TutorialStep.Ultimate => ("궁극기", "가득 찬 게이지로 불 속성 궁극기를 활성화하세요.", "R"),
            TutorialStep.DefeatTraining => ("첫 전투 마무리", "배운 기술을 사용해 연습 적을 처치하세요.", "공격 · 스킬 · 점프 공격"),
            TutorialStep.ClaimChest => ("클리어 보상", "상자를 열고 아이템을 선택해 획득하세요.", "상자 가까이에서 F"),
            TutorialStep.OpenMap => ("전체 지도", "지도를 열어 방과 연결된 길을 확인하세요.", "Tab"),
            TutorialStep.TravelToStart => ("빠른 이동", "지도에서 시작방을 선택하세요. 방문하고 클리어한 방으로 이동할 수 있습니다.", "시작방 클릭"),
            TutorialStep.ReachShop => ("상점으로 이동", "첫 전투방을 지나 표시된 상점으로 이동하세요.", "W A S D"),
            TutorialStep.Buy => ("아이템 구매", "상점을 열고 준비된 아이템을 구매하세요.", "상점 가까이에서 F → 구매"),
            TutorialStep.Sell => ("아이템 판매", "판매 탭에서 보유 아이템 하나를 판매하세요.", "판매 탭 → 아이템 선택 → 판매"),
            TutorialStep.Fuse => ("아이템 합성", "남은 같은 등급 아이템 두 개를 합성하고 결과를 획득하세요.", "합성 탭 → 재료 2개 선택 → 합성"),
            TutorialStep.EnterFreeCombat => ("자유 전투", "상점을 닫고 두 번째 몬스터방으로 이동하세요.", "Esc → W A S D"),
            TutorialStep.FreeCombat => ("자유 전투", "배운 기술을 자유롭게 사용해 모든 적을 처치하세요.", "공격 · 회피 · 스킬 · 점프 공격 · 궁극기"),
            TutorialStep.ReachExit => ("훈련장 클리어", "클리어방의 포탈을 통과해 거점으로 돌아가세요.", "포탈 안으로 이동"),
            _ => ("훈련장 완료", "거점에서 강화석 사용법을 배워봅시다.", "거점으로 돌아가기")
        };
        return new TutorialStepDefinition { step = step, title = text.Item1, body = text.Item2, keys = text.Item3 };
    }

    public TutorialHighlightTarget ResolveHighlightTarget(TutorialStep step)
    {
        var configured = FindStep(step)?.highlightTarget ?? TutorialHighlightTarget.Automatic;
        if (configured != TutorialHighlightTarget.Automatic) return configured;

        return step switch
        {
            TutorialStep.Move => TutorialHighlightTarget.Movement,
            TutorialStep.EnterTraining or TutorialStep.Attack or TutorialStep.Dodge or TutorialStep.Skill
                or TutorialStep.AirAttack or TutorialStep.Ultimate or TutorialStep.DefeatTraining => TutorialHighlightTarget.TrainingEnemy,
            TutorialStep.ClaimChest => TutorialHighlightTarget.TrainingChest,
            TutorialStep.TravelToStart => TutorialHighlightTarget.StartRoomOnMap,
            TutorialStep.ReachShop or TutorialStep.Buy or TutorialStep.Sell or TutorialStep.Fuse => TutorialHighlightTarget.Shop,
            TutorialStep.EnterFreeCombat => TutorialHighlightTarget.FreeCombatRoom,
            TutorialStep.ReachExit => TutorialHighlightTarget.ExitPortal,
            _ => TutorialHighlightTarget.None
        };
    }

    private TutorialStepDefinition FindStep(TutorialStep step)
    {
        foreach (var item in steps ?? Array.Empty<TutorialStepDefinition>())
            if (item != null && item.step == step) return item;
        return null;
    }
}
