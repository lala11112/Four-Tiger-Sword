using System;
using UnityEngine;

/// <summary>Per-boss damage absorption, retaining fractional combat damage.</summary>
public class BossShield : MonoBehaviour
{
    private float _currentAbsorption;
    public float CurrentAbsorption => _currentAbsorption;
    public bool IsActive => _currentAbsorption > 0f;
    public event Action OnShieldBroken;
    public event Action<float> OnShieldChanged;

    /// <summary>Authoring/reset replacement, never a damage-driven shield break.</summary>
    public void SetAbsorption(float amount)
    {
        if (amount < 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
        if (_currentAbsorption == amount) return;
        _currentAbsorption = amount;
        OnShieldChanged?.Invoke(amount);
    }

    public void Grant(float amount)
    {
        if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
        _currentAbsorption = Mathf.Min(float.MaxValue, _currentAbsorption + amount);
        OnShieldChanged?.Invoke(_currentAbsorption);
    }

    public float Absorb(float damage, Action<float> beforeNotify = null)
    {
        if (damage < 0f || float.IsNaN(damage) || float.IsInfinity(damage)) return damage;
        if (!IsActive || damage <= 0f) return damage;
        float absorbed = Mathf.Min(_currentAbsorption, damage);
        _currentAbsorption -= absorbed;
        beforeNotify?.Invoke(absorbed);
        OnShieldChanged?.Invoke(_currentAbsorption);
        if (!IsActive) OnShieldBroken?.Invoke();
        return damage - absorbed;
    }

    public void Clear()
    {
        SetAbsorption(0f);
    }
}
