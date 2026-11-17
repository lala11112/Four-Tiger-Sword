using UnityEngine;

public class EnemyTraceState : IPlayerState
{
    private readonly Enemy _enemy;
    private float _previousStoppingDistance;
    private bool _previousUpdateRotation;
    public Transform _target;

    public EnemyTraceState(Enemy enemy) { _enemy = enemy; }

    public void Enter()
    {
        _target = _enemy.DetectedTarget;
        var positioning = _enemy.TacticalPositioning;
        var agent = positioning.Navigation.Agent;
        _previousStoppingDistance = agent.stoppingDistance;
        // The trace endpoint sits inside the combat threshold. Leave enough margin to reach it.
        agent.stoppingDistance = Mathf.Min(_previousStoppingDistance, .05f);
        // Turn toward the player at combat speed instead of the agent's slow steering.
        _previousUpdateRotation = agent.updateRotation;
        _enemy.SetNavigationRotation(false);
        positioning.Navigation.RefreshAnimation();
        positioning.Reset();
    }

    public void Update()
    {
        _target = _enemy.DetectedTarget;
        var positioning = _enemy.TacticalPositioning;
        positioning.Navigation.SyncAnimation();
        if (_target != null)
            positioning.UpdateVisibleTarget(_target, positioning.PreferredSkill(_target), true,
                Mathf.Max(0f, _enemy.CombatRange - .1f));
        else if (_enemy.HasTargetMemory)
            positioning.Investigate(_enemy.LastKnownTargetPosition);
        if (_enemy.IsKnockbacking) return;
        // Face the visible player; while investigating, face where we are heading.
        if (_target != null) positioning.FaceTarget(_target);
        else if (positioning.Navigation.Available && positioning.Navigation.Agent.velocity.sqrMagnitude > .01f)
            positioning.FaceDirection(positioning.Navigation.Agent.velocity);
        else if (_enemy.HasTargetMemory)
            positioning.FaceDirection(_enemy.LastKnownTargetPosition - _enemy.transform.position);
    }

    public void Exit()
    {
        var positioning = _enemy.TacticalPositioning;
        positioning.Navigation.Agent.stoppingDistance = _previousStoppingDistance;
        _enemy.SetNavigationRotation(_previousUpdateRotation);
        positioning.Reset();
    }
}
