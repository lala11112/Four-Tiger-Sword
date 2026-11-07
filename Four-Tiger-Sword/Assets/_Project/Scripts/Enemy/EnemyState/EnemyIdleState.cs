using UnityEngine;

/// <summary>Alternates short walks around the original spawn point with quiet observation.</summary>
public class EnemyIdleState : IPlayerState
{
    private readonly Enemy _enemy;
    private EnemyNavigation _navigation;
    private float _rest;
    private float _walkTime;
    private float _stuckTime;
    private Vector3 _progress;
    private float _oldStoppingDistance;
    private bool _walking;

    public EnemyIdleState(Enemy enemy) { _enemy = enemy; }

    public void Enter()
    {
        _navigation = new EnemyNavigation(_enemy);
        if (_navigation.Agent != null)
        {
            _oldStoppingDistance = _navigation.Agent.stoppingDistance;
            _navigation.Agent.stoppingDistance = .15f;
        }
        _enemy.ActorClock?.SetMovementModifier(this, .55f);
        Rest();
    }

    public void Update()
    {
        if (!_navigation.Available || _enemy.IsKnockbacking) return;
        if (_walking)
        {
            _walkTime += _enemy.ActionDeltaTime;
            _stuckTime += _enemy.ActionDeltaTime;
            if ((_enemy.transform.position - _progress).sqrMagnitude > .01f)
            { _progress = _enemy.transform.position; _stuckTime = 0f; }
            var agent = _navigation.Agent;
            if (_walkTime >= 5f || _stuckTime >= 1.5f || (!agent.pathPending &&
                (agent.pathStatus != UnityEngine.AI.NavMeshPathStatus.PathComplete
                || agent.remainingDistance <= agent.stoppingDistance + .05f))) Rest();
            return;
        }
        _rest -= _enemy.ActionDeltaTime;
        if (_rest > 0f) return;
        Vector3 fromHome = _enemy.transform.position - _enemy.PatrolOrigin;
        fromHome.y = 0f;
        if (fromHome.sqrMagnitude > 9f)
        {
            // Pursuit may end away from the patrol area. Walk back along the room's floor first.
            if (!TryWalk(_enemy.PatrolOrigin, false)) Rest();
            return;
        }
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * 3f;
            Vector3 destination = _enemy.PatrolOrigin + new Vector3(offset.x, 0f, offset.y);
            if ((destination - _enemy.transform.position).sqrMagnitude < .75f * .75f
                || !TryWalk(destination, true)) continue;
            return;
        }
        Rest();
    }

    private bool TryWalk(Vector3 destination, bool patrol)
    {
        if (!_navigation.TryMove(destination, patrol)) return false;
        _walking = true;
        _walkTime = _stuckTime = 0f;
        _progress = _enemy.transform.position;
        _navigation.Animate(true);
        return true;
    }

    private void Rest()
    {
        _walking = false;
        _rest = Random.Range(1f, 3f);
        _navigation.Stop();
    }

    public void Exit()
    {
        _navigation.Stop();
        if (_navigation.Agent != null) _navigation.Agent.stoppingDistance = _oldStoppingDistance;
        _enemy.ActorClock?.RemoveMovementModifier(this);
    }
}
