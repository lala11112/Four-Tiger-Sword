using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// 다중 타격(콤보) 공격 실행 로직.
/// MonsterComboAttackSO의 hits 리스트를 순서대로 판정하며,
/// 각 타격은 독립적인 히트 목록을 가져 중복 피해를 방지한다.
/// </summary>
public class EnemyComboAttack : EnemyAction
{
    private readonly MonsterComboAttackSO _data;
    private readonly List<HashSet<IDamageable>> _hitTargets = new List<HashSet<IDamageable>>();
    private bool[] _swingPlayed;
    private bool[] _animationPlayed;
    private bool[] _stepStarted;
    private Vector3[] _stepDirections;
    private readonly NavMeshAgent _nav;
    private EnemyHitEvent _executingHit;
    private int _bossAimStrike;
    private float _normalArmorEnd;
    public override bool SupportsBossPolicy => true;
    protected override float BossInitialStartupDuration => _data.hits[0].hitStartTime;

    public EnemyComboAttack(MonsterComboAttackSO data, Enemy enemy)
        : base(data, enemy)
    {
        _data = data;
        _nav = enemy != null ? enemy.GetComponent<NavMeshAgent>() : null;
    }

    public override void Enter()
    {
        _normalArmorEnd = 0f;
        foreach (var hit in _data.hits)
        {
            if (hit == null || hit.hitDuration <= 0f) continue;
            float end = hit.hitStartTime + hit.hitDuration;
            if (!float.IsNaN(end) && !float.IsInfinity(end)) _normalArmorEnd = Mathf.Max(_normalArmorEnd, end);
        }
        base.Enter();
        if (!CanContinueAction) return;
        _bossAimStrike = 0;

        _hitTargets.Clear();
        _swingPlayed = new bool[_data.hits.Count];
        _animationPlayed = new bool[_data.hits.Count];
        _stepStarted = new bool[_data.hits.Count];
        _stepDirections = new Vector3[_data.hits.Count];
        for (int i = 0; i < _data.hits.Count; i++)
            _hitTargets.Add(new HashSet<IDamageable>());

        _enemy.Animator?.CrossFade(_data.animName, 0.01f);
        PlaySwingAnimations(0f);

        // TODO: _enemy.Animator?.SetTrigger(_data.animName);
    }

    public override void Update()
    {
        if (!CanContinueAction) return;

        float previousTime = _timer;
        base.Update();
        if (!CanContinueAction) return;
        PlaySwingAnimations(_timer);

        for (int i = 0; i < _data.hits.Count; i++)
        {
            EnemyHitEvent e = _data.hits[i];
            if (HasBossPolicy && e != null)
            {
                float startupStart = i == 0 ? 0f : _data.hits[i - 1].hitStartTime + _data.hits[i - 1].hitDuration;
                float activeEnd = e.hitStartTime + e.hitDuration;
                if (_timer >= startupStart && previousTime < activeEnd)
                {
                    if (_bossAimStrike != i)
                    {
                        _bossAimStrike = i;
                        BeginBossPreparation();
                    }
                    TickBossPreparation(previousTime, Mathf.Min(_timer, activeEnd), startupStart, e.hitStartTime);
                }
            }
            if (e != null) StepForward(i, e, previousTime, Mathf.Min(_timer, _data.attackDuration));
            if (e != null && CrossesHitWindow(previousTime, Mathf.Min(_timer, _data.attackDuration),
                e.hitStartTime, e.hitDuration))
            {
                if (!_swingPlayed[i])
                {
                    _swingPlayed[i] = true;
                    PlaySwingSound(e.SwingAudio);
                }
                ExecuteHit(i, e);
                if (!CanContinueAction) return;
            }
        }

        if (_timer >= _normalArmorEnd) EndNormalAttackArmor();
        if (HasBossPolicy && _data.hits.Count > 0)
        {
            var last = _data.hits[_data.hits.Count - 1];
            if (_timer >= last.hitStartTime + last.hitDuration) SetBossStage(BossAttackStage.Recovery);
        }

        if (_timer >= _data.attackDuration)
        {
            // telegraphDuration을 attackDuration으로 설정하면 베이스 클래스가 이 시점에 자동으로 EndTelegraph() 호출
            IsFinished = true;
        }
    }

    private void StepForward(int index, EnemyHitEvent hit, float previousTime, float currentTime)
    {
        if (hit.stepDistance <= 0f || hit.stepDuration <= 0f || !CanContinueAction
            || _nav == null || !_nav.enabled || !_nav.isOnNavMesh) return;
        float before = Mathf.Clamp01((previousTime - hit.stepStartTime) / hit.stepDuration);
        float after = Mathf.Clamp01((currentTime - hit.stepStartTime) / hit.stepDuration);
        if (after <= before) return;
        if (!_stepStarted[index])
        {
            _stepStarted[index] = true;
            _stepDirections[index] = HasBossPolicy ? BossAimDirection : _enemy.transform.forward;
        }
        Vector3 direction = _stepDirections[index];
        direction.y = 0f;
        direction.Normalize();
        // Integrate the authored window, including frames crossing its boundaries.
        // Attack speed changes the duration, never the total stride distance.
        float distance = hit.stepDistance * (Mathf.SmoothStep(0f, 1f, after) - Mathf.SmoothStep(0f, 1f, before));
        if (_enemy.DetectedTarget != null)
        {
            Vector3 toTarget = _enemy.DetectedTarget.position - _enemy.transform.position;
            toTarget.y = 0f;
            float forward = Vector3.Dot(toTarget, direction);
            float sideSquared = Mathf.Max(0f, toTarget.sqrMagnitude - forward * forward);
            float clearanceSquared = _data.stepStopDistance * _data.stepStopDistance;
            if (forward > 0f && sideSquared < clearanceSquared)
                distance = Mathf.Min(distance, Mathf.Max(0f, forward - Mathf.Sqrt(clearanceSquared - sideSquared)));
        }
        // Move the actor and hit origin together; never push the skeleton out of its capsule.
        // Blocked progress is consumed rather than stored for a later teleport.
        if (_nav.Raycast(_enemy.transform.position + direction * distance, out var edge))
        {
            Vector3 toEdge = edge.position - _enemy.transform.position;
            toEdge.y = 0f;
            distance = Mathf.Min(distance, Mathf.Max(0f, Vector3.Dot(toEdge, direction) - .05f));
        }
        if (distance > 0f) _nav.Move(direction * distance);
    }

    private void ExecuteHit(int index, EnemyHitEvent e)
    {
        BroadcastHit(); // TakeDamage에서 이 액션을 소스로 식별하기 위해
        Vector3 center = GetHitCenter(e.hitBoxOffset);
        float damage = e.damage > 0 ? e.damage : _data.baseDamage;

        foreach (var col in Physics.OverlapSphere(center, _data.hitBoxRadius, _playerLayer))
        {
            if (!CanContinueAction) return;
            var damageable = col.GetComponentInParent<IDamageable>();
            if (damageable == null || !_hitTargets[index].Add(damageable)) continue;
            _executingHit = e;
            try
            {
                DamageManager.Apply(
                    new HitInfo(damage * _enemy.EnemyStat.GetStat(EnemyStatType.ATK), _enemy.Element, _enemy.EnemyStat.GetStat(EnemyStatType.CriticalChance),
                                _enemy.EnemyStat.GetStat(EnemyStatType.CriticalDamage), power: GetKnockbackDirection(col) * (e.overrideReaction ? e.knockbackForce : _data.knockbackForce),
                                staggerResistLevel: e.overrideReaction ? e.staggerLevel : _data.staggerLevel, isParryable: _data.isParryable, source: this, impactSound: ResolveHitSound(e.HitAudio)),
                    damageable, col.gameObject);
            }
            finally { _executingHit = null; }
            if (!CanContinueAction) return;
        }
    }

    public override bool TryGetPlayerKnockdown(out float downSeconds, out float getUpSeconds)
    {
        downSeconds = getUpSeconds = 0f;
        if (_executingHit == null) return false;
        // Existing authored knockdown flags remain valid without overrideReaction.
        // An explicit per-hit override can also keep a common knockdown attack normal.
        if (_executingHit.overrideReaction || _executingHit.knockdown)
        {
            downSeconds = _executingHit.downSeconds;
            getUpSeconds = _executingHit.getUpSeconds;
            return _executingHit.knockdown;
        }
        return base.TryGetPlayerKnockdown(out downSeconds, out getUpSeconds);
    }

    private void PlaySwingAnimations(float time)
    {
        for (int i = 0; i < _data.hits.Count; i++)
        {
            EnemyHitEvent hit = _data.hits[i];
            if (_animationPlayed[i] || hit == null || time < hit.animationStartTime) continue;
            _animationPlayed[i] = true;
            if (!string.IsNullOrWhiteSpace(hit.animationState))
                _enemy.Animator?.CrossFade(hit.animationState, .03f, 0, 0f);
        }
    }
}
