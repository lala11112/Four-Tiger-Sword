using System.Collections.Generic;
using UnityEngine;

/// <summary>Per-actor pressure sensing and mobility selection. Never interrupts an action itself.</summary>
public sealed class BossMobilityController
{
    private readonly Boss _boss;
    private readonly Queue<float> _hits = new Queue<float>();
    private readonly List<BossMobilityAction> _candidates = new List<BossMobilityAction>(2);
    private int _lastHitFrame = -1;
    private float _escapeReadyAt, _approachReadyAt, _sharedReadyAt, _retryAt;
    private Transform _target;
    private BossMobilitySettings Settings => _boss.Mobility;
    private float Now => _boss.ActionTime;
    public bool EscapePending { get; private set; }
    public int RecentHitCount { get { TrimHits(); return _hits.Count; } }
    public float FarDuration { get; private set; }
    public float CornerDuration { get; private set; }
    public float EscapeCooldownRemaining => Mathf.Max(0f, _escapeReadyAt - Now);
    public float ApproachCooldownRemaining => Mathf.Max(0f, _approachReadyAt - Now);
    public float SharedCooldownRemaining => Mathf.Max(0f, _sharedReadyAt - Now);
    public string LastReason { get; private set; } = "";

    public BossMobilityController(Boss boss) { _boss = boss; }

    private bool CanObserve => Settings != null && Settings.enabled && _boss.isActiveAndEnabled
        && !_boss.IsDead && !_boss.IsTransitioning && !_boss.CombatLabPassive
        && !_boss.IsHurt && !_boss.IsGroggy && !_boss.PendingGroggy && !_boss.IsRoot
        && !_boss.IsSilenced && !_boss.IsKnockbacking && _boss.CombatTarget != null;

    public void RecordHit(float healthDamage, int frame)
    {
        if (!CanObserve || !BossCombatValidation.Positive(healthDamage)
            || _boss.CurrentAction is BossMobilityAction || frame == _lastHitFrame) return;
        SynchronizeTarget();
        _lastHitFrame = frame;
        TrimHits();
        _hits.Enqueue(Now);
        if (_hits.Count >= Mathf.Max(1, Settings.hitCount))
        { EscapePending = true; LastReason = "Repeated damage"; }
    }

    public void Observe(float delta)
    {
        if (!CanObserve) { ClearIntent(); return; }
        SynchronizeTarget();
        TrimHits();
        if (_boss.CurrentAction is BossMobilityAction) return;
        if (!BossCombatValidation.Positive(delta)) return;
        Vector3 offset = _boss.CombatTarget.position - _boss.transform.position;
        offset.y = 0f;
        FarDuration = offset.magnitude >= Settings.farDistance ? FarDuration + delta : 0f;
        CornerDuration = BossMobilityNavigation.IsCornered(_boss, Settings) ? CornerDuration + delta : 0f;
        if (CornerDuration >= Settings.cornerDuration)
        { EscapePending = true; LastReason = "Corner pressure"; }
    }

    /// <summary>Called only at an outer pattern boundary or during ordinary positioning.</summary>
    public bool TrySelect(out BossMobilityAction action)
    {
        action = null;
        if (!CanObserve || _boss.CurrentAction != null || Now < _sharedReadyAt || Now < _retryAt) return false;
        bool escapeReady = EscapePending && Now >= _escapeReadyAt;
        bool approachReady = FarDuration >= Settings.farDuration && Now >= _approachReadyAt;
        if (!escapeReady && !approachReady) return false;
        _retryAt = Now + Mathf.Max(.01f, Settings.retryInterval);
        if (escapeReady && Settings.escapeJump != null
            && Settings.escapeJump.TryCreateAction(_boss, out action))
        { LastReason = "Escape jump selected"; return true; }
        if (approachReady)
        {
            _candidates.Clear();
            AddCandidate(Settings.dashSlash);
            AddCandidate(Settings.jumpSlam);
            if (_candidates.Count > 0)
            {
                action = _candidates[Random.Range(0, _candidates.Count)];
                LastReason = "Approach selected: " + action.Kind;
                _candidates.Clear();
                return true;
            }
        }
        LastReason = "No safe mobility destination";
        return false;
    }

    private void AddCandidate(BossMobilitySkillSO skill)
    {
        if (skill != null && skill.TryCreateAction(_boss, out var action)) _candidates.Add(action);
    }

    public void Complete(BossMobilityAction action)
    {
        if (action == null || Settings == null) return;
        if (action.Kind == BossMobilityKind.EscapeJump) _escapeReadyAt = Now + Settings.escapeCooldown;
        else _approachReadyAt = Now + Settings.approachCooldown;
        _sharedReadyAt = Now + Settings.sharedCooldown;
        ClearIntent();
        LastReason = "Mobility finished: " + action.Kind;
    }

    public void ClearIntent()
    {
        _hits.Clear(); _candidates.Clear(); _lastHitFrame = -1;
        EscapePending = false; FarDuration = CornerDuration = 0f;
        _target = null;
    }

    private void SynchronizeTarget()
    {
        if (_target == _boss.CombatTarget) return;
        ClearIntent();
        _target = _boss.CombatTarget;
    }

    private void TrimHits()
    {
        if (Settings == null) { _hits.Clear(); return; }
        while (_hits.Count > 0 && Now - _hits.Peek() > Settings.hitWindow) _hits.Dequeue();
    }
}
