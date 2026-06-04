using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 적 공격 예고(telegraph)와 플레이어 패링 시스템 사이의 이벤트 버스.
/// 소스(EnemyAction 인스턴스)별로 예고를 추적하므로 여러 적이 동시에 공격해도
/// 플레이어가 패링을 시도한 적의 콤보에 대해서만 멀티히트 윈도우가 연장됩니다.
/// </summary>
public static class ParryEventBus
{
    private static readonly HashSet<object> _activeSources = new HashSet<object>();

    /// <summary>현재 하나 이상의 적이 패링 가능한 공격을 예고 중인지 여부</summary>
    public static bool IsAnyTelegraphActive => _activeSources.Count > 0;

    /// <summary>
    /// 현재 TakeDamage를 유발한 공격 액션의 소스. BroadcastHit 후 동기적으로 읽어야 합니다.
    /// </summary>
    public static object CurrentAttackSource { get; private set; }

    /// <summary>첫 번째 예고 시작 시 발생</summary>
    public static event Action OnFirstTelegraphStart;

    /// <summary>마지막 예고 종료 시 발생</summary>
    public static event Action OnLastTelegraphEnd;

    /// <summary>특정 소스의 예고가 현재 활성 상태인지 확인합니다.</summary>
    public static bool IsTelegraphActive(object source)
        => source != null && _activeSources.Contains(source);

    /// <summary>적 공격 액션의 Enter 시 호출됩니다.</summary>
    public static void BroadcastTelegraphStart(object source)
    {
        if (source == null || _activeSources.Contains(source)) return;
        bool wasEmpty = _activeSources.Count == 0;
        _activeSources.Add(source);
        if (wasEmpty)
            OnFirstTelegraphStart?.Invoke();
    }

    /// <summary>예고 구간이 끝나거나 액션의 Exit 시 호출됩니다.</summary>
    public static void BroadcastTelegraphEnd(object source)
    {
        if (source == null) return;
        _activeSources.Remove(source);
        if (_activeSources.Count == 0)
            OnLastTelegraphEnd?.Invoke();
    }

    /// <summary>
    /// 실제 타격이 발생하기 직전에 호출됩니다.
    /// PlayerController.TakeDamage에서 CurrentAttackSource를 읽어 패링 소스를 추적합니다.
    /// </summary>
    public static void BroadcastHit(object source)
    {
        CurrentAttackSource = source;
    }

    /// <summary>씬 전환 또는 전투 종료 시 모든 상태를 강제 초기화합니다.</summary>
    public static void ResetAll()
    {
        bool hadActive = _activeSources.Count > 0;
        _activeSources.Clear();
        CurrentAttackSource = null;
        if (hadActive)
            OnLastTelegraphEnd?.Invoke();
    }
}
