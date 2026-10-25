using UnityEngine;
using UnityEngine.AI;

/// <summary>목(木) 스킬: 적 이동 속도를 slowRatio 만큼 감소시킵니다.</summary>
public class SlowEffect : IStatusEffect
{
    private readonly float _slowRatio;
    private readonly float _duration;
    private float _elapsed;

    private NavMeshAgent _agent;
    private float _originalSpeed;
    private CombatActorClock _clock;

    public bool IsExpired => _elapsed >= _duration;

    public SlowEffect(float slowRatio, float duration)
    {
        _slowRatio = slowRatio;
        _duration  = duration;
    }

    public void OnApply(GameObject target)
    {
        var actor = target.GetComponent<Enemy>() as Component ?? target.GetComponent<PlayerController>();
        if (actor != null)
        {
            _clock = CombatActorClock.GetOrCreate(actor);
            _clock.SetMovementModifier(this, 1f - _slowRatio);
            return;
        }
        _agent = target.GetComponent<NavMeshAgent>();
        if (_agent == null) return;
        _originalSpeed = _agent.speed;
        _agent.speed  *= (1f - _slowRatio);
    }

    public void OnUpdate(float deltaTime) => _elapsed += deltaTime;

    public void OnRemove(GameObject target)
    {
        if (_clock != null) _clock.RemoveMovementModifier(this);
        if (_agent != null)
            _agent.speed = _originalSpeed;
    }
}
