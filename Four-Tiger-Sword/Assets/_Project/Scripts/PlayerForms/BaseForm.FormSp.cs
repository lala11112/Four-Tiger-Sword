using System;
using UnityEngine;

public abstract partial class BaseForm
{
    // Double precision avoids accumulating visible drift across thousands of frame ticks.
    private double _currentFormSp;
    public float CurrentFormSp => (float)Math.Min(_currentFormSp, MaxFormSp);
    public float MaxFormSp => PositiveFormSpSetting(FormSpConfig?.MaxSp ?? 100f, 100f);
    public bool IsFormSpExhausted { get; private set; }

    /// <summary>Published after this form's SP or exhaustion changes; read the committed state from the form.</summary>
    public event Action<BaseForm> OnFormSpChanged;

    private FormSpSettings FormSpConfig => _weaponActionData != null ? _weaponActionData.CommonBalance?.FormSp : null;
    protected float FormSpAttackMultiplier => IsFormSpExhausted
        ? FormSpRatio(FormSpConfig?.ExhaustedAttackMultiplier ?? 0.8f, 0.8f) : 1f;
    protected float FormSpAttackSpeedMultiplier => IsFormSpExhausted
        ? FormSpRatio(FormSpConfig?.ExhaustedAttackSpeedMultiplier ?? 0.8f, 0.8f) : 1f;
    protected float LiveAttackStat => _playerController.StatManager.GetStat(StatType.ST_ATK) * FormSpAttackMultiplier;
    // Some authored actions ignore ordinary attack-speed buffs. Exhaustion still slows their whole timeline.
    protected float FixedActionSpeed => Mathf.Max(0.1f, FormSpAttackSpeedMultiplier);
    protected virtual float CurrentAttackAnimationSpeed => AttackSpeed;

    /// <summary>Called once per world-time tick by FormManager, independently of action SP and actor hit-stop.</summary>
    public void UpdateFormSp(float deltaTime, bool isActive, bool inCombat)
    {
        if (!FiniteFormSpSetting(deltaTime) || deltaTime <= 0f) return;
        float previous = CurrentFormSp;
        bool wasExhausted = IsFormSpExhausted;
        double max = MaxFormSp;
        _currentFormSp = Math.Max(0d, Math.Min(_currentFormSp, max));
        if (isActive)
        {
            if (inCombat)
            {
                float duration = PositiveFormSpSetting(FormSpConfig?.DepletionSeconds ?? 30f, 30f);
                _currentFormSp = Math.Max(0d, _currentFormSp - max * deltaTime / duration);
                // Snap only sub-frame numeric noise at the depletion boundary.
                if (_currentFormSp <= max * 0.000001d) _currentFormSp = 0d;
                if (_currentFormSp == 0d) IsFormSpExhausted = true;
            }
        }
        else
        {
            float duration = PositiveFormSpSetting(FormSpConfig?.RecoverySeconds ?? 20f, 20f);
            _currentFormSp = Math.Min(max, _currentFormSp + max * deltaTime / duration);
            float ratio = Mathf.Clamp(PositiveFormSpSetting(FormSpConfig?.ExhaustionRecoveryRatio ?? 0.2f, 0.2f), 0.01f, 1f);
            if (_currentFormSp + max * 0.000001d >= max * ratio) IsFormSpExhausted = false;
        }

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
    private static float FormSpRatio(float value, float fallback)
        => FiniteFormSpSetting(value) ? Mathf.Clamp01(value) : fallback;
}
