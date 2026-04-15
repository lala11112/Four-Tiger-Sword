using UnityEngine;
using UnityEngine.AI;

/// <summary>목(木) 궁극기: 적을 공중으로 띄웁니다 (에어본).</summary>
public class AirborneEffect : IStatusEffect
{
    private readonly float _duration;
    private readonly float _launchForce;
    private float _elapsed;

    private NavMeshAgent _agent;
    private Rigidbody    _rb;

    public bool IsExpired => _elapsed >= _duration;

    public AirborneEffect(float duration, float launchForce = 8f)
    {
        _duration    = duration;
        _launchForce = launchForce;
    }

    public void OnApply(GameObject target)
    {
        _agent = target.GetComponent<NavMeshAgent>();
        _rb    = target.GetComponent<Rigidbody>();

        if (_agent != null) _agent.enabled = false;
        _rb?.AddForce(Vector3.up * _launchForce, ForceMode.Impulse);
    }

    public void OnUpdate(float deltaTime) => _elapsed += deltaTime;

    public void OnRemove(GameObject target)
    {
        if (_agent != null) _agent.enabled = true;
    }
}
