using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Owns the spawned enemy's dormant/active state and death subscription.</summary>
[DisallowMultipleComponent]
public sealed class DungeonEncounterMember : MonoBehaviour
{
    public Enemy Enemy { get; private set; }
    // A boss may have zero HP while changing phase; only the actor confirms final death.
    public bool IsDead => Enemy == null || Enemy.IsDead;
    public bool IsAwake { get; private set; }
    public bool HasDied { get; private set; }
    private EnemyStat _stats;
    private NavMeshAgent _agent;
    private Collider[] _colliders;
    private bool[] _colliderStates;
    private bool _agentEnabled;
    private bool _enemyEnabled;
    private DungeonRoomEncounter _room;
    private bool _released;

    public void Initialize(DungeonRoomEncounter room)
    {
        _room = room;
        Enemy = GetComponent<Enemy>();
        _stats = GetComponent<EnemyStat>();
        _agent = GetComponent<NavMeshAgent>();
        if (Enemy == null || _stats == null || _agent == null)
            throw new InvalidOperationException($"{name} is not a complete enemy prefab.");
        _enemyEnabled = Enemy.enabled;
        _agentEnabled = _agent.enabled;
        _colliders = GetComponentsInChildren<Collider>(true);
        _colliderStates = new bool[_colliders.Length];
        for (int i = 0; i < _colliders.Length; i++)
        {
            _colliderStates[i] = _colliders[i].enabled;
            _colliders[i].enabled = false;
        }
        Enemy.enabled = false;
        _agent.enabled = false;
        Enemy.OnDied += OnDied;
    }

    public bool CanActivate(out string error)
    {
        error = null;
        if (_released || IsDead) { error = $"{name} is no longer available."; return false; }
        if (IsAwake)
        {
            if (_agentEnabled && (!_agent.enabled || !_agent.isOnNavMesh))
            { error = $"{name} left its room NavMesh."; return false; }
            return true;
        }
        var filter = new NavMeshQueryFilter { agentTypeID = _agent.agentTypeID, areaMask = _agent.areaMask };
        var floor = transform.position - Vector3.up * (_agent.baseOffset * Mathf.Abs(transform.lossyScale.y));
        if (!NavMesh.SamplePosition(floor, out var hit, 0.5f, filter) || Mathf.Abs(hit.position.y - floor.y) > 0.25f)
        { error = $"{name} no longer has valid navigation at its spawn position."; return false; }
        return true;
    }

    public bool ActivatePatrol(out string error)
    {
        Enemy.SetEncounterPatrolOnly(true);
        return Activate(false, out error);
    }

    public bool Activate(out string error) => Activate(true, out error);

    private bool Activate(bool restoreColliders, out string error)
    {
        if (!CanActivate(out error)) return false;
        if (!IsAwake)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = _agent.agentTypeID, areaMask = _agent.areaMask };
            var floor = transform.position - Vector3.up * (_agent.baseOffset * Mathf.Abs(transform.lossyScale.y));
            NavMesh.SamplePosition(floor, out var hit, 0.5f, filter);
            _agent.enabled = _agentEnabled;
            if (_agentEnabled && (!_agent.Warp(hit.position) || !_agent.isOnNavMesh))
            { _agent.enabled = false; error = $"{name} could not join its room NavMesh."; return false; }
        }
        for (int i = 0; i < _colliders.Length; i++)
            if (_colliders[i] != null) _colliders[i].enabled = restoreColliders && _colliderStates[i];
        Enemy.enabled = _enemyEnabled;
        IsAwake = true;
        return true;
    }

    public void Freeze()
    {
        if (Enemy != null) Enemy.enabled = false;
        if (_agent != null) _agent.enabled = false;
        if (_colliders != null)
            foreach (var collider in _colliders) if (collider != null) collider.enabled = false;
        IsAwake = false;
    }

    public void Release()
    {
        if (_released) return;
        _released = true;
        if (Enemy != null) Enemy.OnDied -= OnDied;
        _room = null;
    }

    private void OnDied()
    {
        if (_released) return;
        HasDied = true;
        _room?.NotifyEnemyRemoved(this);
    }
    private void OnDestroy()
    {
        if (!_released) _room?.NotifyEnemyRemoved(this);
        Release();
    }
}
