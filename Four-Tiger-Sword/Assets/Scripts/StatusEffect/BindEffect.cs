using UnityEngine;
using UnityEngine.AI;

/// <summary>목(木) 속박 스택 최대 시: 적을 완전히 속박(이동 불가)합니다.</summary>
public class BindEffect : IStatusEffect
{
    private readonly float _duration;
    private float _elapsed;

    private NavMeshAgent _agent;

    public bool IsExpired => _elapsed >= _duration;

    public BindEffect(float duration = 1.5f)
    {
        _duration = duration;
    }

    public void OnApply(GameObject target)
    {
        _agent = target.GetComponent<NavMeshAgent>();
        if (_agent != null) _agent.isStopped = true;

        Debug.Log($"<color=green>[속박] {target.name} {_duration}초간 속박!</color>");
    }

    public void OnUpdate(float deltaTime) => _elapsed += deltaTime;

    public void OnRemove(GameObject target)
    {
        if (_agent != null) _agent.isStopped = false;
    }
}
