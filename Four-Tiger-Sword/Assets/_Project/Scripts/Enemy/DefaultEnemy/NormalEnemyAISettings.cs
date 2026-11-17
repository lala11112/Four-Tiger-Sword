using System;
using UnityEngine;

/// <summary>Common ordinary-enemy tactics. Individual attacks keep their authored ranges and cooldowns.</summary>
[Serializable]
public sealed class NormalEnemyAISettings
{
    public static NormalEnemyAISettings Default { get; } = new NormalEnemyAISettings();

    [Min(0f), Tooltip("같은 방에서 경보가 전달되는 거리 (m)")]
    public float alertRadius = 8f;
    [Min(0f), Tooltip("직접 관측한 위치의 최소 전파 간격 (초)")]
    public float alertInterval = .5f;
    [Min(0f), Tooltip("일반 공격 시작 간격. 진행 중인 공격 수에는 제한이 없습니다.")]
    public float attackStartInterval = .2f;
    [Min(.01f)] public float destinationUpdateInterval = .25f;
    [Min(0f)] public float directionHoldDuration = 1f;
    [Min(0f)] public float spacingPadding = .25f;
    [Min(30f), Tooltip("전투 중 플레이어 쪽으로 도는 속도 (도/초). 발견 직후 추적과 접근에도 적용됩니다.")]
    public float turnSpeed = 540f;

    [Header("쿨타임 중 측면 이동")]
    [Tooltip("공격 쿨타임 동안 플레이어 주위를 돌며 간을 봅니다.")]
    public bool strafeEnabled = true;
    [Min(0f), Tooltip("한 번에 플레이어 주위를 도는 각도 (도)")]
    public float strafeAngleStep = 35f;
    [Range(.1f, 1f), Tooltip("측면 이동 중 이동 속도 배율")]
    public float strafeSpeedMultiplier = .6f;
    [Min(0f), Tooltip("측면 이동 방향을 바꾸는 최소 간격 (초)")]
    public float strafeSwitchMin = .8f;
    [Min(0f), Tooltip("측면 이동 방향을 바꾸는 최대 간격 (초)")]
    public float strafeSwitchMax = 1.8f;
    [Range(0f, 1f), Tooltip("방향 전환 시 반 발짝 들어왔다 빠지는 페인트를 할 확률")]
    public float feintChance = .25f;
    [Min(0f), Tooltip("페인트로 파고드는 거리 (m)")]
    public float feintDepth = 1f;

    [Header("포위")]
    [Tooltip("무리가 플레이어 주위 각도를 나눠 맡아 측후방으로 퍼집니다.")]
    public bool surroundEnabled = true;
    [Range(0f, 90f), Tooltip("배정된 포위 각도 쪽으로 이동 방향을 기울이는 최대 각도 (도)")]
    public float surroundBias = 60f;

    [Header("공격 리듬")]
    [Range(0f, .9f), Tooltip("공격 선택 주기의 무작위 편차 (0.3 = 기준 쿨타임 ±30%). 피격 중에도 흐릅니다.")]
    public float attackCooldownJitter = .3f;
    [Min(0f), Tooltip("쿨타임 중 플레이어가 공격 사거리 안으로 먼저 들어오면 남은 쿨타임을 이만큼 줄입니다 (초, 한 번)")]
    public float punishCooldownCut = .5f;

    public NormalEnemyAISettings Copy() => new NormalEnemyAISettings
    {
        alertRadius = Valid(alertRadius, 8f),
        alertInterval = Valid(alertInterval, .5f),
        attackStartInterval = Valid(attackStartInterval, .2f),
        destinationUpdateInterval = Mathf.Max(.01f, Valid(destinationUpdateInterval, .25f)),
        directionHoldDuration = Valid(directionHoldDuration, 1f),
        spacingPadding = Valid(spacingPadding, .25f),
        turnSpeed = Mathf.Max(30f, Valid(turnSpeed, 540f)),
        strafeEnabled = strafeEnabled,
        strafeAngleStep = Valid(strafeAngleStep, 35f),
        strafeSpeedMultiplier = Mathf.Clamp(Valid(strafeSpeedMultiplier, .6f), .1f, 1f),
        strafeSwitchMin = Valid(strafeSwitchMin, .8f),
        strafeSwitchMax = Mathf.Max(Valid(strafeSwitchMin, .8f), Valid(strafeSwitchMax, 1.8f)),
        feintChance = Mathf.Clamp01(Valid(feintChance, .25f)),
        feintDepth = Valid(feintDepth, 1f),
        surroundEnabled = surroundEnabled,
        surroundBias = Mathf.Clamp(Valid(surroundBias, 60f), 0f, 90f),
        attackCooldownJitter = Mathf.Clamp(Valid(attackCooldownJitter, .3f), 0f, .9f),
        punishCooldownCut = Valid(punishCooldownCut, .5f)
    };

    private static float Valid(float value, float fallback)
        => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Max(0f, value);
}
