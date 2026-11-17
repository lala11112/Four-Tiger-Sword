using UnityEngine;
using UnityEngine.AI;

public class EnemyCombatIdleState : IPlayerState
{
    private const float FeintDuration = .45f;
    private const float PunishDistanceRatio = .7f;

    private readonly Enemy _enemy;
    private NavMeshAgent _nav;
    private bool _oldUpdateRotation;
    private float _oldStoppingDistance;
    private int _strafeSide = 1;
    private float _nextStrafeSwitch;
    private float _feintUntil;
    private bool _punishUsed;

    public EnemyCombatIdleState(Enemy enemy)
    {
        _enemy = enemy;
    }

    public void Enter()
    {
        var positioning = _enemy.TacticalPositioning;
        _nav = positioning.Navigation.Agent;
        _oldUpdateRotation = _nav.updateRotation;
        _oldStoppingDistance = _nav.stoppingDistance;
        _enemy.SetNavigationRotation(false);
        _nav.stoppingDistance = .1f;
        positioning.Navigation.RefreshAnimation();
        positioning.Reset();
        _enemy.HasSelectedAttack = false;
        _enemy.CurrentAction = null;

        var settings = _enemy.AISettings;
        _strafeSide = Random.value < .5f ? -1 : 1;
        _nextStrafeSwitch = _enemy.ActionTime + Random.Range(settings.strafeSwitchMin, settings.strafeSwitchMax);
        _feintUntil = float.NegativeInfinity;
        _punishUsed = false;
    }

    public void Update()
    {
        _enemy.TacticalPositioning.Navigation.SyncAnimation();
        if (_enemy.Brain is NormalEnemyBrain || _enemy.CanAttack)
            TrySelectAttack();
        if (_enemy.HasSelectedAttack) return;
        if (!TryStrafe()) MaintainDistance();
    }

    public void Exit()
    {
        _enemy.TacticalPositioning.Reset();
        _enemy.SetNavigationRotation(_oldUpdateRotation);
        _nav.stoppingDistance = _oldStoppingDistance;
    }

    // ── 공격 선택 (가중치 랜덤) ─────────────────────────────────────────────

    private void TrySelectAttack()
    {
        if (_enemy.Brain is NormalEnemyBrain brain)
        {
            brain.TryPrepareScheduledAttack();
            return;
        }
        var skill = _enemy.SelectAttackSkill();
        if (skill == null) return;
        _enemy.CurrentAction = skill.CreateAction(_enemy);
        _enemy.HasSelectedAttack = _enemy.CurrentAction != null;
    }

    private void MaintainDistance()
    {
        var target = _enemy.DetectedTarget;
        if (target == null || _enemy.IsKnockbacking) return;
        var positioning = _enemy.TacticalPositioning;
        positioning.FaceTarget(target);
        positioning.UpdateVisibleTarget(target, positioning.PreferredSkill(target), false);
    }

    // ── 쿨타임 중 측면 이동 ─────────────────────────────────────────────────

    /// <summary>Circles and feints while the attack cooldown runs instead of standing still.</summary>
    private bool TryStrafe()
    {
        var target = _enemy.DetectedTarget;
        var settings = _enemy.AISettings;
        // A ready attack with no usable skill keeps the original range-keeping behavior.
        if (target == null || _enemy.IsKnockbacking || !settings.strafeEnabled || _enemy.IsAttackCooldownReady)
            return false;
        var positioning = _enemy.TacticalPositioning;
        var skill = positioning.PreferredSkill(target);
        if (skill == null) return false;

        TryPunishCloseIn(target, skill, settings);

        float now = _enemy.ActionTime;
        bool replan = false;
        if (now >= _nextStrafeSwitch)
        {
            if (Random.value < .6f) _strafeSide = -_strafeSide;
            if (Random.value < settings.feintChance) _feintUntil = now + FeintDuration;
            _nextStrafeSwitch = now + Random.Range(settings.strafeSwitchMin, settings.strafeSwitchMax);
            replan = true;
        }
        bool feinting = now < _feintUntil;
        positioning.FaceTarget(target);
        return positioning.Strafe(target, skill, ref _strafeSide, feinting ? settings.feintDepth : 0f, replan);
    }

    /// <summary>A player who closes in during the cooldown gets punished a little sooner, once per cycle.</summary>
    private void TryPunishCloseIn(Transform target, MonsterSkillData skill, NormalEnemyAISettings settings)
    {
        if (_punishUsed || settings.punishCooldownCut <= 0f) return;
        float preferred = skill.preferredRange > 0f ? skill.preferredRange : skill.executeRange;
        float distance = Vector3.ProjectOnPlane(target.position - _enemy.transform.position, Vector3.up).magnitude;
        if (distance > preferred * PunishDistanceRatio) return;
        _punishUsed = true;
        _enemy.ReduceAttackCooldown(settings.punishCooldownCut);
    }
}
