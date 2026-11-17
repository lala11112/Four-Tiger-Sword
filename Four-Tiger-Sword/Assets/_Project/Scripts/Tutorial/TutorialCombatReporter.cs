using System;
using UnityEngine;

public enum TutorialCombatAction { GroundHit, Dash, Skill, AirHit, AirLanded, Ultimate }

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController))]
public sealed class TutorialCombatReporter : MonoBehaviour
{
    private PlayerController _player;
    private TutorialCombatAction? _expectedAction;

    public event Action<TutorialCombatAction> ActionCompleted;
    public Enemy Target { get; private set; }
    internal bool IsPlayerProtected { get; private set; }

    private void OnEnable()
    {
        _player = GetComponent<PlayerController>();
        _player.TutorialCombat = this;
    }

    private void OnDisable()
    {
        _expectedAction = null;
        SetTrainingProtection(false, null, false);
        if (_player != null && _player.TutorialCombat == this) _player.TutorialCombat = null;
    }

    public void SetTrainingProtection(bool playerProtected, Enemy target, bool targetProtected)
    {
        if (Target != null && Target.TutorialTrainingProtection == this)
            Target.TutorialTrainingProtection = null;

        IsPlayerProtected = isActiveAndEnabled && playerProtected;
        Target = isActiveAndEnabled ? target : null;
        if (Target != null && targetProtected) Target.TutorialTrainingProtection = this;
    }

    public void ResetLessonResources(TutorialCombatAction action)
    {
        if (!isActiveAndEnabled || _player == null) return;
        _expectedAction = action;
        if (action == TutorialCombatAction.Dash)
        {
            _player.ResetTutorialDashCooldown();
        }
        else if (_player.FormManager?.CurrentForm is BaseForm form)
        {
            if (action == TutorialCombatAction.Skill) form.ResetTutorialSkillCooldown();
            else if (action == TutorialCombatAction.Ultimate)
            {
                if (form is IToggleUltimateForm toggle && toggle.IsUltimateActive) toggle.DeactivateUltimate();
                form.ResetTutorialUltimateCooldown();
                var stats = _player.StatManager;
                if (stats != null) stats.AddUltimateGauge(stats.MaxUltimateGauge - stats.CurrentUltimateGauge);
            }
        }
    }

    internal void ReportHit(TutorialCombatAction action, Enemy enemy)
    {
        if (enemy != null && enemy == Target) Report(action);
    }

    internal void Report(TutorialCombatAction action)
    {
        if (!isActiveAndEnabled || _expectedAction != action) return;
        _expectedAction = null;
        ActionCompleted?.Invoke(action);
    }
}
