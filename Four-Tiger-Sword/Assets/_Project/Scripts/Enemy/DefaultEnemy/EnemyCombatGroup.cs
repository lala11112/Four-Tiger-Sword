using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>One encounter's local awareness, destinations and fair attack-start pacing; never an attack-count cap.</summary>
[DisallowMultipleComponent]
public sealed class EnemyCombatGroup : MonoBehaviour
{
    private sealed class Member
    {
        public NavMeshAgent Agent;
        public float NextSighting;
        public bool HasPosition;
        public Vector3 Position;
    }

    private struct AttackRequest
    {
        public EnemyAction Action;
        public uint HurtVersion;
        public long Order;
    }

    private readonly Dictionary<Enemy, Member> _members = new();
    private readonly Dictionary<Enemy, AttackRequest> _requests = new();
    private readonly List<Enemy> _invalidRequests = new();
    private long _requestOrder;
    private float _nextAttackStart = float.NegativeInfinity;
    public NormalEnemyAISettings Settings { get; private set; } = new NormalEnemyAISettings();
    public int MemberCount => _members.Count;
    public int PendingAttackCount { get { PruneRequests(); return _requests.Count; } }

    public void Initialize(NormalEnemyAISettings settings)
    {
        Settings = (settings ?? NormalEnemyAISettings.Default).Copy();
    }

    public void Register(Enemy enemy)
    {
        if (enemy == null || enemy is Boss || enemy.Rank == EnemyRank.Boss) return;
        if (enemy.CombatGroup != null && enemy.CombatGroup != this)
            enemy.CombatGroup.Unregister(enemy);
        if (!_members.ContainsKey(enemy))
            _members.Add(enemy, new Member { Agent = enemy.GetComponent<NavMeshAgent>(), NextSighting = float.NegativeInfinity });
        enemy.SetCombatGroup(this);
    }

    public void Unregister(Enemy enemy)
    {
        if (ReferenceEquals(enemy, null)) return;
        ClearIntent(enemy);
        _members.Remove(enemy);
        if (enemy != null && enemy.CombatGroup == this) enemy.SetCombatGroup(null);
    }

    public void ClearIntent(Enemy enemy)
    {
        CancelAttackRequest(enemy);
        ReleasePosition(enemy);
    }

    private bool Available(Enemy enemy)
        => isActiveAndEnabled && enemy != null && _members.ContainsKey(enemy)
            && enemy.CombatGroup == this && enemy.isActiveAndEnabled && !enemy.IsDead
            && !enemy.CombatLabPassive && !enemy.EncounterPatrolOnly;

    /// <summary>Only a direct observation may originate an alert, never a remembered or relayed cue.</summary>
    public void ReportSighting(Enemy source, Vector3 position)
    {
        if (!Available(source) || source.IsSpawning || source.DetectedTarget == null) return;
        var member = _members[source];
        if (Time.time < member.NextSighting) return;
        member.NextSighting = Time.time + Settings.alertInterval;
        Broadcast(source, position);
    }

    /// <summary>Called only for a confirmed direct player hit, including a lethal hit's last warning.</summary>
    public void ReportDirectHit(Enemy source, Vector3 position)
    {
        if (!Available(source) || source.IsSpawning) return;
        Broadcast(source, position);
    }

    private void Broadcast(Enemy source, Vector3 position)
    {
        if (!KnockbackMotion.IsFinite(position.x) || !KnockbackMotion.IsFinite(position.y)
            || !KnockbackMotion.IsFinite(position.z)) return;
        float radiusSquared = Settings.alertRadius * Settings.alertRadius;
        foreach (var pair in _members)
        {
            var receiver = pair.Key;
            if (receiver == source || !Available(receiver)
                || (receiver.transform.position - source.transform.position).sqrMagnitude > radiusSquared) continue;
            // Copy a snapshot. Receiving a cue does not call ReportSighting or Broadcast.
            receiver.RememberEncounterPosition(position);
        }
    }

    private bool Ready(Enemy enemy)
        => Available(enemy) && enemy.Brain?.StateName == nameof(EnemyApproachState)
            && enemy.CanAttack && enemy.ActionDeltaTime > 0f && !enemy.IsHurt && !enemy.IsGroggy
            && !enemy.PendingGroggy && !enemy.IsRoot && !enemy.IsSelfDestructing
            && enemy.HasSelectedAttack && enemy.CurrentAction != null && !enemy.CurrentAction.IsActive
            && enemy.IsInExecuteRange && enemy.IsSkillReady(enemy.CurrentAction.SkillData);

    private void PruneRequests()
    {
        _invalidRequests.Clear();
        foreach (var pair in _requests)
            if (!Ready(pair.Key) || pair.Key.CurrentAction != pair.Value.Action
                || pair.Key.HurtVersion != pair.Value.HurtVersion)
                _invalidRequests.Add(pair.Key);
        foreach (var enemy in _invalidRequests) _requests.Remove(enemy);
    }

    /// <summary>Called by the approach-to-attack transition after all higher-priority reactions.</summary>
    public bool TryStartAttack(Enemy enemy)
    {
        PruneRequests();
        if (!Ready(enemy)) { CancelAttackRequest(enemy); return false; }
        if (!_requests.TryGetValue(enemy, out var request))
        {
            request = new AttackRequest { Action = enemy.CurrentAction, HurtVersion = enemy.HurtVersion, Order = ++_requestOrder };
            _requests.Add(enemy, request);
        }
        if (Time.time < _nextAttackStart) return false;
        foreach (var pending in _requests.Values)
            if (pending.Order < request.Order) return false;
        _requests.Remove(enemy);
        // Reserve this start before action callbacks can cause another enemy to tick.
        _nextAttackStart = Time.time + Settings.attackStartInterval;
        return true;
    }

    public void NotifyAttackStarted(Enemy enemy)
    {
        if (!Available(enemy)) return;
        CancelAttackRequest(enemy);
        ReleasePosition(enemy);
        // Immediate counters bypass the queue, but delay subsequent ordinary starts.
        _nextAttackStart = Mathf.Max(_nextAttackStart, Time.time + Settings.attackStartInterval);
    }

    public void CancelAttackRequest(Enemy enemy)
    {
        if (!ReferenceEquals(enemy, null)) _requests.Remove(enemy);
    }

    public bool IsPositionAvailable(Enemy owner, Vector3 position)
    {
        if (!Available(owner)) return true;
        float radius = Radius(_members[owner]);
        foreach (var pair in _members)
        {
            var other = pair.Key;
            if (other == owner || !Available(other)) continue;
            float separation = radius + Radius(pair.Value) + Settings.spacingPadding;
            if (GroundDistanceSquared(position, other.transform.position) < separation * separation) return false;
            if (pair.Value.HasPosition && GroundDistanceSquared(position, pair.Value.Position) < separation * separation)
                return false;
        }
        return true;
    }

    public void ReservePosition(Enemy owner, Vector3 position)
    {
        if (!Available(owner)) return;
        var member = _members[owner];
        member.Position = position;
        member.HasPosition = true;
    }

    public void ReleasePosition(Enemy owner)
    {
        if (!ReferenceEquals(owner, null) && _members.TryGetValue(owner, out var member)) member.HasPosition = false;
    }

    private static float Radius(Member member) => member.Agent != null ? Mathf.Max(0f, member.Agent.radius) : .5f;
    private static float GroundDistanceSquared(Vector3 a, Vector3 b)
    { a.y = b.y = 0f; return (a - b).sqrMagnitude; }

    private void OnDisable()
    {
        _requests.Clear();
        _nextAttackStart = float.NegativeInfinity;
        foreach (var member in _members.Values)
        { member.HasPosition = false; member.NextSighting = float.NegativeInfinity; }
    }

    private void OnDestroy()
    {
        foreach (var enemy in _members.Keys)
            if (enemy != null && enemy.CombatGroup == this) enemy.SetCombatGroup(null);
        _members.Clear();
        _requests.Clear();
    }
}
