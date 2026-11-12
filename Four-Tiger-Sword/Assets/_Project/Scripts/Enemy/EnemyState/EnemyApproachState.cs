using UnityEngine;
using UnityEngine.AI;

/// <summary>Moves within range of the selected attack and holds position until it can start.</summary>
public class EnemyApproachState : IPlayerState
{
    private readonly Enemy _enemy;
    private NavMeshAgent _nav;
    private float _previousStoppingDistance;
    private float _elapsed;
    private float _stuckTime;
    private float _repathTimer;
    private Vector3 _progressPosition;

    public EnemyApproachState(Enemy enemy)
    {
        _enemy = enemy;
    }

    public void Enter()
    {
        _nav = _enemy.GetComponent<NavMeshAgent>();
        _elapsed = _stuckTime = _repathTimer = 0f;
        _progressPosition = _enemy.transform.position;
        _previousStoppingDistance = _nav.stoppingDistance;
        if (_nav.enabled && _nav.isOnNavMesh) _nav.ResetPath();
        if (_enemy is not Boss)
        {
            var positioning = _enemy.TacticalPositioning;
            positioning.Navigation.RefreshAnimation();
            positioning.Reset();
            var skill = _enemy.CurrentAction?.SkillData;
            if (skill != null)
                _nav.stoppingDistance = Mathf.Min(Mathf.Min(_previousStoppingDistance, .1f),
                    Mathf.Max(0f, (skill.executeRange - skill.minimumRange) * .125f));
            return;
        }
        if (_enemy.CurrentAction != null)
            _nav.stoppingDistance = Mathf.Min(_previousStoppingDistance,
                Mathf.Max(0f, _enemy.CurrentAction.SkillData.executeRange * 0.8f));
        _enemy.Animator.CrossFade("Move", 0.1f);
    }

    public void Update()
    {
        if (_enemy is Boss) UpdateBossApproach();
        else UpdateNormalApproach();
    }

    private void UpdateNormalApproach()
    {
        var target = _enemy.DetectedTarget;
        var skill = _enemy.CurrentAction?.SkillData;
        if (target == null || skill == null) return;
        var positioning = _enemy.TacticalPositioning;
        if (_enemy.IsSilenced || !_enemy.IsSkillReady(skill))
        {
            _enemy.CancelCurrentAction();
            return;
        }
        if (_enemy.IsInExecuteRange)
        {
            // Waiting for the room's next attack start is progress, not a blocked approach.
            _stuckTime = 0f;
            _progressPosition = _enemy.transform.position;
            positioning.HoldForAttack(target);
            return;
        }

        _enemy.CombatGroup?.CancelAttackRequest(_enemy);
        if (_enemy.IsKnockbacking) return;
        if (!positioning.Navigation.Available || ApproachTimedOut())
        {
            _enemy.CancelFailedApproach();
            return;
        }
        positioning.UpdateVisibleTarget(target, skill, true);
    }

    private void UpdateBossApproach()
    {
        if (_enemy.DetectedTarget == null || _enemy.CurrentAction == null) return;
        if (!_nav.enabled || !_nav.isOnNavMesh || ApproachTimedOut())
        {
            _enemy.CancelFailedApproach();
            return;
        }
        if (_elapsed > 0.2f && !_nav.pathPending
            && _nav.pathStatus != NavMeshPathStatus.PathComplete)
        {
            _enemy.CancelFailedApproach();
            return;
        }
        _repathTimer -= _enemy.ActionDeltaTime;
        if (_repathTimer > 0f) return;
        _repathTimer = 0.2f;
        Vector3 destination = _enemy.DetectedTarget.position;
        float minRange = _enemy.CurrentAction.SkillData.minimumRange;
        Vector3 away = _enemy.transform.position - destination;
        away.y = 0f;
        if (away.magnitude < minRange)
        {
            if (away.sqrMagnitude < 0.001f) away = -_enemy.transform.forward;
            float desiredRange = (minRange + _enemy.CurrentAction.SkillData.executeRange) * 0.5f;
            destination += away.normalized * desiredRange;
        }
        if (!_nav.SetDestination(destination)) _enemy.CancelFailedApproach();
    }

    private bool ApproachTimedOut()
    {
        _elapsed += _enemy.ActionDeltaTime;
        _stuckTime += _enemy.ActionDeltaTime;
        if ((_enemy.transform.position - _progressPosition).sqrMagnitude >= 0.01f)
        {
            _progressPosition = _enemy.transform.position;
            _stuckTime = 0f;
        }
        return _elapsed >= _enemy.ApproachTimeout || _stuckTime >= _enemy.ApproachStuckTimeout;
    }

    public void Exit()
    {
        _nav.stoppingDistance = _previousStoppingDistance;
        if (_enemy is Boss)
        {
            if (_nav.enabled && _nav.isOnNavMesh) _nav.ResetPath();
            return;
        }
        _enemy.CombatGroup?.CancelAttackRequest(_enemy);
        _enemy.TacticalPositioning.Reset();
    }
}
