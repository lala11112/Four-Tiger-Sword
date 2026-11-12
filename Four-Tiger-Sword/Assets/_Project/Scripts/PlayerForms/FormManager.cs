using System;
using System.Collections.Generic;
public partial class FormManager
{
    public enum SwitchReason { Direct, Parry }
    private PlayerController _playerController;
    private readonly Func<ElementType, bool> _elementAvailability;
    public IForm CurrentForm{ get; private set; }
    private List<FormTransition> _transitions = new List<FormTransition>();
    private readonly HashSet<IForm> _registeredForms = new();
    private readonly Dictionary<Type, List<FormTransition>> _fastTransitions = new Dictionary<Type, List<FormTransition>>();


    public Func<bool> CanTransition{ get; set; }
    public event Action<IForm> OnFormChanged;
    public event Action<IForm, IForm> OnFormSwitchSucceeded;

    public FormManager(PlayerController playerController, Func<ElementType, bool> elementAvailability = null)
    {
        _playerController = playerController;
        _elementAvailability = elementAvailability;
    }

    public bool IsElementAvailable(ElementType element) => _elementAvailability?.Invoke(element) ?? true;

    public void Update()
    {
        foreach (var form in _registeredForms)
            form.UpdateCooldowns();

        (CurrentForm as BaseForm)?.UpdateSkillCooldownUI();

        if (_playerController.ActionDeltaTime <= 0f || !_playerController.CanChangeForm || (CanTransition != null && !CanTransition.Invoke()))
        {
            return;
        }

        var toTransition = GetTransition();
        if(toTransition != null && toTransition.TargetForm != CurrentForm)
        {
            TrySwitchForm(toTransition.TargetForm);
        }
    }

    public void CleanupTransientEffects()
    {
        ClearChainReadiness();
        _playerController.CancelPendingSwitchAttack();
        _playerController.StopAttackTrails(true);
        _playerController.CombatCamera?.Cancel(_playerController, true);
        // Buffs may belong to an unequipped form; clear every registered owner on shutdown/death.
        foreach (var form in _registeredForms)
            (form as BaseForm)?.CleanupTransientEffects();
    }

    public void CleanupStageEffects()
    {
        ClearChainReadiness();
        _playerController.CancelPendingSwitchAttack();
        foreach (var form in _registeredForms)
            (form as BaseForm)?.CleanupStageEffects();
    }

    public void AddTransition(IForm targetForm, Func<bool> condition)
    {
        if (targetForm == null) throw new ArgumentNullException(nameof(targetForm));
        _registeredForms.Add(targetForm);
        _transitions.Add(new FormTransition(targetForm, condition));
    }

    public void AddFastTransition(IForm fromForm, IForm toForm, Func<bool> condition)
    {
        if (fromForm == null) throw new ArgumentNullException(nameof(fromForm));
        if (toForm == null) throw new ArgumentNullException(nameof(toForm));
        _registeredForms.Add(fromForm);
        _registeredForms.Add(toForm);
        if (!_fastTransitions.TryGetValue(fromForm.GetType(), out var transitions))
        {
            transitions = new List<FormTransition>();
            _fastTransitions[fromForm.GetType()] = transitions;
        }
        transitions.Add(new FormTransition(toForm, condition));
    }

    /// <summary>Force an assignment for initial setup or editor tools without a gameplay switch.</summary>
    public void ChangeForm(IForm form) => AssignForm(form, true);

    public BaseForm GetForm(ElementType element)
    {
        foreach (var form in _registeredForms)
            if (form is BaseForm normalForm && normalForm.Element == element) return normalForm;
        return null;
    }

    /// <summary>A committed combo finisher changes form once independently of the direct-switch cooldown, without an E-style attack.</summary>
    public bool TrySwitchForFinisher(BaseForm target)
    {
        if (target == null || target == CurrentForm || CurrentForm == null || !IsElementAvailable(target.Element)
            || !target.CanBeginFinisher
            || !_registeredForms.Contains(target) || !_playerController.isActiveAndEnabled
            || _playerController.IsStageTransitioning || _playerController.IsShopPaused
            || _playerController.StatManager == null || _playerController.StatManager.CurrentHp <= 0f
            || !(_playerController.StateMachine?.CurrentState is PlayerAttackState attack) || attack.IsHeavyAttack)
            return false;
        IForm previous = CurrentForm;
        AssignForm(target, false);
        if (_playerController.ItemRuntime != null) _playerController.ItemRuntime.NotifyFormSwitchSucceeded();
        OnFormChanged?.Invoke(CurrentForm);
        OnFormSwitchSucceeded?.Invoke(previous, target);
        return true;
    }

    private void AssignForm(IForm form, bool publish)
    {
        if(CurrentForm == form) return;
        _playerController.CancelPendingSwitchAttack();
        _playerController.StopAttackTrails(true);
        _playerController.CombatCamera?.Cancel(_playerController);
        if (form != null) _registeredForms.Add(form);

        CurrentForm?.Unequip(_playerController);
        CurrentForm = form;
        CurrentForm?.Equip(_playerController);
        _playerController.WeaponManager?.SetForm(CurrentForm?.Element ?? ElementType.ELEMENT_NONE);
        if (publish)
        {
            _playerController.ItemRuntime?.RecalculateForCurrentForm();
            OnFormChanged?.Invoke(CurrentForm);
        }
    }

    /// <summary>Eligibility only. Initial/editor assignments use ChangeForm and never proc items.</summary>
    public bool CanSwitchForm(IForm target)
        => target is BaseForm
            && CurrentForm != null && target != CurrentForm && _registeredForms.Contains(target)
            && IsElementAvailable(target.Element)
            && _playerController.isActiveAndEnabled && _playerController.StatManager != null
            && _playerController.StatManager.CurrentHp > 0f && _playerController.CanChangeForm;

    public bool TrySwitchForm(IForm target, SwitchReason reason = SwitchReason.Direct)
    {
        // Numeric/Q selection is a plain form swap. Conditional attacks use F.
        if (reason != SwitchReason.Direct) return false;
        if (!CanSwitchForm(target)) return false;
        var state = _playerController.StateMachine?.CurrentState;
        bool permitted = (state is PlayerIdleState || state is PlayerRunState)
            && (CanTransition == null || CanTransition());
        if (!permitted) return false;

        IForm previous = CurrentForm;
        // New-form passives and freshly triggered effects are reconciled together: never
        // clamp against an intermediate maximum before the successful-switch buff exists.
        AssignForm(target, false);
        _playerController.StartFormChangeCooldown();
        _playerController.ItemRuntime?.NotifyFormSwitchSucceeded();
        OnFormChanged?.Invoke(CurrentForm);
        OnFormSwitchSucceeded?.Invoke(previous, target);
        return true;
    }

    /// <summary>Numeric form selection only; Q never requests a parry.</summary>
    public IForm GetRequestedForm() => GetDirectTransition()?.TargetForm;

    private FormTransition GetDirectTransition()
    {
        foreach(var transition in _transitions)
        {
            if (IsElementAvailable(transition.TargetForm.Element) && transition.Condition.Invoke())
            {
                return transition;
            }
        }

        return null;
    }

    private FormTransition GetTransition()
    {
        var directTransition = GetDirectTransition();
        if (directTransition != null) return directTransition;

        if(CurrentForm != null && _fastTransitions.TryGetValue(CurrentForm.GetType(), out var currentTransitions))
        {
            foreach(var transition in currentTransitions)
            {
                if (IsElementAvailable(transition.TargetForm.Element) && transition.Condition.Invoke())
                {
                    return transition;
                }
            }
        }

        return null;
    }
}
