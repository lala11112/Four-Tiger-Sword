/// <summary>
/// 보스 단일 페이즈의 런타임 행동 계약.
/// 새 페이즈 타입을 추가해도 Boss 클래스를 수정할 필요가 없습니다. (OCP)
/// </summary>
public interface IBossPhase
{
    /// <summary>
    /// 이 페이즈로 전환되는 HP 비율 (0.0 ~ 1.0).
    /// HP가 이 값 이하로 내려가면 전환이 발동됩니다.
    /// </summary>
    float HpThreshold { get; }

    /// <summary>
    /// 이 페이즈에서 사용할 스킬셋.
    /// null을 반환하면 Boss가 이전 스킬셋을 그대로 유지합니다.
    /// </summary>
    MonsterSkillDataSO SkillSet { get; }

    /// <summary>페이즈 진입 직후 한 번 호출됩니다.</summary>
    void OnEnter(Boss boss);

    /// <summary>페이즈 종료 직전 한 번 호출됩니다.</summary>
    void OnExit(Boss boss);

    /// <summary>
    /// 매 프레임 호출됩니다.
    /// 상시 지속 효과(자동 회복, 하수인 감지 등) 구현에 사용합니다.
    /// </summary>
    void OnUpdate(Boss boss);

    /// <summary>
    /// 들어오는 데미지를 수정하여 반환합니다.
    /// 페이즈별 데미지 감소율, 보호막과 별개인 수치 보정 등에 사용합니다.
    /// </summary>
    int ModifyIncomingDamage(int rawDamage, Boss boss);
}
