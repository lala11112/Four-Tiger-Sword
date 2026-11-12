using UnityEngine;

public class EnemyTraceState : IPlayerState
{
    private readonly Enemy _enemy;
    private float _previousStoppingDistance;
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
        positioning.Navigation.RefreshAnimation();
        positioning.Reset();
    }

    public void Update()
    {
        _target = _enemy.DetectedTarget;
        var positioning = _enemy.TacticalPositioning;
        if (_target != null)
            positioning.UpdateVisibleTarget(_target, positioning.PreferredSkill(_target), true,
                Mathf.Max(0f, _enemy.CombatRange - .1f));
        else if (_enemy.HasTargetMemory)
            positioning.Investigate(_enemy.LastKnownTargetPosition);
    }

    public void Exit()
    {
        var positioning = _enemy.TacticalPositioning;
        positioning.Navigation.Agent.stoppingDistance = _previousStoppingDistance;
        positioning.Reset();
    }
}
