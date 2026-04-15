using System;
using UnityEngine;

/// <summary>
/// 수(水) 폼 전용: 유수 게이지.
/// 타격 시 충전되며 100% 달성 시 5초간 버프를 발동합니다.
/// 버프 중에는 충전이 불가합니다. (SRP: 게이지 로직만 담당)
/// </summary>
public class WaterFlowGauge
{
    public const float MaxGauge    = 100f;
    public const float BuffDuration = 5f;

    public float CurrentGauge { get; private set; }
    public float GaugeRatio   => CurrentGauge / MaxGauge;
    public bool  IsBuffActive { get; private set; }

    private float _buffTimer;

    /// <summary>버프 발동 시 (공속 +30%, 이속 버프)</summary>
    public event Action OnBuffActivated;
    /// <summary>버프 종료 시</summary>
    public event Action OnBuffExpired;

    /// <summary>타격 1회당 호출합니다.</summary>
    public void Charge(float amount)
    {
        if (IsBuffActive) return;

        CurrentGauge = Mathf.Min(CurrentGauge + amount, MaxGauge);
        if (CurrentGauge >= MaxGauge)
            ActivateBuff();
    }

    /// <summary>UpdateCooldowns에서 매 프레임 호출됩니다.</summary>
    public void Tick(float deltaTime)
    {
        if (!IsBuffActive) return;

        _buffTimer -= deltaTime;
        if (_buffTimer <= 0f)
        {
            IsBuffActive = false;
            OnBuffExpired?.Invoke();
            Debug.Log("<color=cyan>[유수 게이지] 버프 종료</color>");
        }
    }

    private void ActivateBuff()
    {
        IsBuffActive = true;
        _buffTimer   = BuffDuration;
        CurrentGauge = 0f;
        OnBuffActivated?.Invoke();
        Debug.Log("<color=cyan>[유수 게이지] 버프 발동! 5초간 공속/이속 증가</color>");
    }
}
