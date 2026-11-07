using System;
using UnityEngine;

public abstract partial class BaseForm
{
    // Retained for resource API compatibility; staying in one form no longer consumes SP.
    private double _currentFormSp;
    public float CurrentFormSp => (float)Math.Min(_currentFormSp, MaxFormSp);
    public float MaxFormSp => PositiveFormSpSetting(FormSpConfig?.MaxSp ?? 100f, 100f);
    public bool IsFormSpExhausted { get; private set; }

    /// <summary>Published after this form's SP or exhaustion changes; read the committed state from the form.</summary>
    public event Action<BaseForm> OnFormSpChanged;

    private FormSpSettings FormSpConfig => _weaponActionData != null ? _weaponActionData.CommonBalance?.FormSp : null;
    protected float FormSpAttackSpeedMultiplier => 1f;
    protected float LiveAttackStat => _playerController.StatManager.GetStat(StatType.ST_ATK);
    // Some authored actions ignore ordinary attack-speed buffs and use their fixed timeline.
    protected float FixedActionSpeed => 1f;
    protected virtual float CurrentAttackAnimationSpeed => AttackSpeed;

    /// <summary>Keep the retired form endurance resource full, without affecting shared action SP.</summary>
    public void UpdateFormSp(float deltaTime, bool isActive, bool inCombat)
    {
        if (!FiniteFormSpSetting(deltaTime) || deltaTime <= 0f) return;
        float previous = CurrentFormSp;
        bool wasExhausted = IsFormSpExhausted;
        _currentFormSp = MaxFormSp;
        IsFormSpExhausted = false;

        if (isActive && wasExhausted != IsFormSpExhausted) RefreshFormSpAnimationSpeed();
        if (previous != CurrentFormSp || wasExhausted != IsFormSpExhausted) OnFormSpChanged?.Invoke(this);
    }

    private void RefreshFormSpAnimationSpeed()
    {
        if (_playerController?.FormManager?.CurrentForm != this) return;
        var state = _playerController.StateMachine?.CurrentState;
        if (state is PlayerAttackState || state is PlayerAirAttackState || state is PlayerSkillState
            || state is PlayerSwitchAttackState || state is PlayerCounterState || state is PlayerUltimateState)
            _playerController.SetAnimationSpeed(CurrentAttackAnimationSpeed);
    }

    private static bool FiniteFormSpSetting(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static float PositiveFormSpSetting(float value, float fallback)
        => FiniteFormSpSetting(value) && value > 0f ? value : fallback;
}
