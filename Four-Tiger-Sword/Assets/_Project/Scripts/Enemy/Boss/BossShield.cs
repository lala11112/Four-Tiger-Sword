using System;
using UnityEngine;

/// <summary>
/// 보스의 피해 흡수 보호막을 관리합니다.
/// Boss로부터 분리되어 단일 책임(SRP)을 유지합니다.
/// UI, VFX 시스템은 이벤트를 구독하여 Boss를 직접 참조하지 않아도 됩니다. (DIP)
/// </summary>
public class BossShield : MonoBehaviour
{
    private int _currentAbsorption;

    public int  CurrentAbsorption => _currentAbsorption;
    public bool IsActive          => _currentAbsorption > 0;

    /// <summary>보호막이 완전히 소모됐을 때 발생합니다.</summary>
    public event Action OnShieldBroken;

    /// <summary>보호막 수치가 변경될 때마다 발생합니다. 인자: 현재 흡수량</summary>
    public event Action<int> OnShieldChanged;

    /// <summary>
    /// 보호막을 부여합니다. 기존 보호막에 합산됩니다.
    /// </summary>
    public void Grant(int amount)
    {
        if (amount <= 0) return;
        _currentAbsorption += amount;
        OnShieldChanged?.Invoke(_currentAbsorption);
    }

    /// <summary>
    /// 들어온 데미지를 보호막이 흡수합니다.
    /// 보호막을 초과하는 잔여 데미지를 반환합니다.
    /// </summary>
    public int Absorb(int damage)
    {
        if (!IsActive) return damage;

        int absorbed = Mathf.Min(_currentAbsorption, damage);
        _currentAbsorption -= absorbed;

        OnShieldChanged?.Invoke(_currentAbsorption);

        if (!IsActive)
            OnShieldBroken?.Invoke();

        return damage - absorbed;
    }

    /// <summary>보호막을 즉시 제거합니다.</summary>
    public void Clear()
    {
        if (!IsActive) return;
        _currentAbsorption = 0;
        OnShieldChanged?.Invoke(0);
        OnShieldBroken?.Invoke();
    }
}
