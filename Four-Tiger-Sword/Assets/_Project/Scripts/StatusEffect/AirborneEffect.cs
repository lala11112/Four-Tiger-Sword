using UnityEngine;
using UnityEngine.AI;

/// <summary>NavMeshAgent를 끄지 않고 높이 오프셋으로 띄워 AI의 NavMesh 호출을 유지합니다.</summary>
public class AirborneEffect : IStatusEffect
{
    private readonly float _duration;
    private readonly float _height;
    private float _elapsed;
    private NavMeshAgent _agent;
    private Enemy _enemy;
    private float _baseOffset;
    private Transform _target;
    private float _baseY;
    public bool IsExpired => _elapsed >= _duration;
    public AirborneEffect(float duration, float launchForce = 8f)
    {
        _duration = Mathf.Max(0.01f, duration);
        _height = Mathf.Max(0f, launchForce * 0.2f);
    }
    public void OnApply(GameObject target)
    {
        _target = target.transform;
        _baseY = _target.position.y;
        _agent = target.GetComponent<NavMeshAgent>();
        if (_agent != null) _baseOffset = _agent.baseOffset;
        _enemy = target.GetComponent<Enemy>();
        if (_enemy != null) { _enemy.RootDuration = _duration; _enemy.IsRoot = true; }
    }
    public void OnUpdate(float dt)
    {
        _elapsed += dt;
        float lift = Mathf.Sin(Mathf.Clamp01(_elapsed / _duration) * Mathf.PI) * _height;
        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            _agent.baseOffset = _baseOffset + lift;
        else if (_target != null)
            _target.position = new Vector3(_target.position.x, _baseY + lift, _target.position.z);
    }
    public void OnRemove(GameObject target)
    {
        if (_agent != null) _agent.baseOffset = _baseOffset;
        if (_target != null) _target.position = new Vector3(_target.position.x, _baseY, _target.position.z);
        if (_enemy != null) _enemy.IsRoot = false;
    }
}
