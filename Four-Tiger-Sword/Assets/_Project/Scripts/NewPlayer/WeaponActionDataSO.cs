using System;
using System.Collections.Generic;
using UnityEngine;

// One form asset owns its actions; nested groups separate tuning responsibilities.
public abstract class WeaponActionDataSO : ScriptableObject
{
    public string WeaponName;

    [InspectorName("공격 목록 (Actions)")]
    public FormActionSet Actions = new FormActionSet();

    [InspectorName("공통 밸런스 (Balance)")]
    public FormBalanceSettings CommonBalance = new FormBalanceSettings();

    [InspectorName("공통 동작 (Execution)")]
    public FormExecutionSettings CommonExecution = new FormExecutionSettings();

    [InspectorName("공통 타격 피드백 (Feedback)")]
    public FormFeedbackSettings CommonFeedback = new FormFeedbackSettings();

    // Compatibility accessors used by the form runtime and CombatLab adapter.
    public List<WeaponActionData> ComboSteps { get => Actions.ComboSteps; set => Actions.ComboSteps = value; }
    public string ParryAnimationName { get => CommonExecution.ParryAnimationName; set => CommonExecution.ParryAnimationName = value; }
    public string AirAttackStartAnimationName { get => CommonExecution.AirAttackStartAnimationName; set => CommonExecution.AirAttackStartAnimationName = value; }
    public float AirAttackStartDuration { get => CommonExecution.AirAttackStartDuration; set => CommonExecution.AirAttackStartDuration = value; }
    public string AirAttackLoopAnimationName { get => CommonExecution.AirAttackLoopAnimationName; set => CommonExecution.AirAttackLoopAnimationName = value; }
    public WeaponActionData AirAttackStep { get => Actions.AirAttackStep; set => Actions.AirAttackStep = value; }
    public WeaponActionData CounterStep { get => Actions.CounterStep; set => Actions.CounterStep = value; }
    public List<WeaponActionData> SkillSteps { get => Actions.SkillSteps; set => Actions.SkillSteps = value; }
    public List<WeaponActionData> UltimateSteps { get => Actions.UltimateSteps; set => Actions.UltimateSteps = value; }
    public GameObject HitVFX { get => CommonFeedback.HitVFX; set => CommonFeedback.HitVFX = value; }
    public AudioClip HitSound { get => CommonFeedback.HitSound; set => CommonFeedback.HitSound = value; }
    public float UltimateCooldown { get => CommonBalance.UltimateCooldown; set => CommonBalance.UltimateCooldown = value; }
}

[Serializable]
public class FormActionSet
{
    [Tooltip("1타, 2타, 3타... 에 대한 데이터 리스트")]
    public List<WeaponActionData> ComboSteps;

    [Tooltip("착지 시 마무리 공격 데이터. AnimationName은 마무리 모션, Duration은 마무리 시간입니다.")]
    public WeaponActionData AirAttackStep;

    [Tooltip("패링 성공 후 반격 윈도우에서만 사용 가능한 반격 데이터")]
    public WeaponActionData CounterStep;

    [Tooltip("스킬 공격 데이터 (여러 단계 가능)")]
    public List<WeaponActionData> SkillSteps;

    [Tooltip("궁극기 공격 데이터 (여러 단계 가능). 수 폼의 고정 10연타는 첫 단계의 Hit Events를 순서대로 참조하여 히트스탑만 적용합니다. 해당 히트 설정이 없으면 첫 단계의 기본 히트스탑을 사용합니다.")]
    public List<WeaponActionData> UltimateSteps;
}

[Serializable]
public class FormBalanceSettings
{
    [Min(0f)] public float UltimateCooldown = 30f;
}

[Serializable]
public class FormExecutionSettings
{
    [Tooltip("이 폼의 패링 Animator 상태 이름")]
    public string ParryAnimationName = "Parry";

    [Tooltip("공중 공격 시작 Animator 상태 이름. 비어 있으면 시작 단계를 생략합니다.")]
    public string AirAttackStartAnimationName;

    [Tooltip("시작 모션 길이(초). 공격 속도 배율이 적용됩니다.")]
    [Min(0f)] public float AirAttackStartDuration = 0.15f;

    [Tooltip("시작 모션 이후 착지할 때까지 재생할 Animator 상태 이름. 반복 클립을 사용하세요.")]
    public string AirAttackLoopAnimationName;
}

[Serializable]
public class FormFeedbackSettings
{
    [Tooltip("적을 타격했을 때 적 위치에 스폰할 이펙트 프리팹")]
    public GameObject HitVFX;

    [Tooltip("적을 타격했을 때 재생할 사운드")]
    public AudioClip HitSound;
}
