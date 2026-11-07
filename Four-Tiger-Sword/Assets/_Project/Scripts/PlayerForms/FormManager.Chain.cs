using System.Collections.Generic;
using UnityEngine;

public partial class FormManager
{
    private sealed class ChainReadiness
    {
        public float Remaining;
        public ulong Sequence;
    }

    private readonly Dictionary<BaseForm, ChainReadiness> _chainReadiness = new();
    private ulong _chainSequence;

    private bool ChainOwnerAvailable => _playerController != null
        && _playerController.isActiveAndEnabled && CurrentForm != null
        && !_playerController.IsStageTransitioning && !_playerController.IsGameplayModalPaused
        && _playerController.StatManager != null && _playerController.StatManager.CurrentHp > 0f;

    /// <summary>Newest authored, ready destination; current form and empty attack slots are excluded.</summary>
    public BaseForm ReadyChainSkill
    {
        get
        {
            if (!ChainOwnerAvailable) return null;
            BaseForm selected = null;
            ulong sequence = 0;
            foreach (var pair in _chainReadiness)
            {
                BaseForm form = pair.Key;
                if (pair.Value.Remaining <= 0f || form == CurrentForm
                    || !_registeredForms.Contains(form) || form.ChainSettings?.Enabled != true
                    || !form.CanSwitchAttack) continue;
                if (selected == null || pair.Value.Sequence > sequence)
                {
                    selected = form;
                    sequence = pair.Value.Sequence;
                }
            }
            return selected;
        }
    }

    // State-specific recovery gates belong to PlayerStateMachineSetup.
    public bool CanUseReadyChainSkill => ChainOwnerAvailable
        && _playerController.ActionDeltaTime > 0f && !_playerController.IsFinisherSelecting
        && _playerController.PendingHitReaction == PlayerHitReaction.None && ReadyChainSkill != null;

    public float GetChainReadyRemaining(ElementType element)
    {
        var form = GetForm(element);
        return form != null && form.ChainSettings?.Enabled == true
            && _chainReadiness.TryGetValue(form, out var ready) ? Mathf.Max(0f, ready.Remaining) : 0f;
    }

    /// <summary>Tick before state transitions, so a token at its expiry cannot be consumed.</summary>
    public void UpdateChainReadiness(float deltaTime)
    {
        if (!ChainOwnerAvailable || !IsFinitePositive(deltaTime)) return;
        foreach (var ready in _chainReadiness.Values)
            ready.Remaining = Mathf.Max(0f, ready.Remaining - deltaTime);
    }

    public void NotifyFinalComboHit(ElementType source)
    {
        if (!ChainOwnerAvailable || CurrentForm.Element != source) return;
        PrepareChains(FormChainTrigger.OtherFormFinalHit, source);
    }

    public void NotifySuccessfulDodge()
    {
        if (!ChainOwnerAvailable) return;
        PrepareChains(FormChainTrigger.SuccessfulDodge, ElementType.ELEMENT_NONE);
    }

    private void PrepareChains(FormChainTrigger trigger, ElementType source)
    {
        foreach (var registered in _registeredForms)
        {
            if (!(registered is BaseForm form)) continue;
            var settings = form.ChainSettings;
            if (settings?.Enabled != true || settings.Trigger != trigger
                || !IsFinitePositive(settings.ReadyDuration)
                || (trigger == FormChainTrigger.OtherFormFinalHit && form.Element == source)) continue;
            if (!_chainReadiness.TryGetValue(form, out var ready))
                _chainReadiness.Add(form, ready = new ChainReadiness());
            ready.Remaining = settings.ReadyDuration;
            ready.Sequence = ++_chainSequence;
        }
    }

    /// <summary>Commit only in switch-state Enter, after the outgoing state's Exit has finished.</summary>
    public bool TryUseReadyChainSkill(out BaseForm target)
    {
        target = null;
        if (!CanUseReadyChainSkill
            || !(_playerController.StateMachine?.CurrentState is PlayerSwitchAttackState)) return false;
        BaseForm candidate = ReadyChainSkill;
        if (candidate == null) return false;
        IForm previous = CurrentForm;
        AssignForm(candidate, false);
        _chainReadiness[candidate].Remaining = 0f;
        // Reconcile destination item effects before the attack snapshots damage.
        _playerController.ItemRuntime?.NotifyFormSwitchSucceeded();
        OnFormChanged?.Invoke(CurrentForm);
        OnFormSwitchSucceeded?.Invoke(previous, candidate);
        target = candidate;
        return true;
    }

    private void ClearChainReadiness()
    {
        _chainReadiness.Clear();
        if (_playerController.Input != null) _playerController.Input.ChainSkillBuffer.Consume();
    }

    private static bool IsFinitePositive(float value)
        => value > 0f && !float.IsInfinity(value) && !float.IsNaN(value);
}
