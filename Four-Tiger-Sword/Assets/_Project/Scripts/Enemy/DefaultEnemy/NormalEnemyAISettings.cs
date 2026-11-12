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

    public NormalEnemyAISettings Copy() => new NormalEnemyAISettings
    {
        alertRadius = Valid(alertRadius, 8f),
        alertInterval = Valid(alertInterval, .5f),
        attackStartInterval = Valid(attackStartInterval, .2f),
        destinationUpdateInterval = Mathf.Max(.01f, Valid(destinationUpdateInterval, .25f)),
        directionHoldDuration = Valid(directionHoldDuration, 1f),
        spacingPadding = Valid(spacingPadding, .25f)
    };

    private static float Valid(float value, float fallback)
        => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Max(0f, value);
}
