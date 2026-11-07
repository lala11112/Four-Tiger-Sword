using UnityEngine;
using System.Collections.Generic;

public class EnemyDefaultAttack : EnemyAction
{
    private readonly MonsterDefaultAttackSO _data;
    private readonly HashSet<IDamageable> _hitTargets = new HashSet<IDamageable>();
    private bool _swingPlayed;
    private bool _attackAnimationStarted;
    public override bool SupportsBossPolicy => true;
    protected override float BossInitialStartupDuration => _data.hitStartTime;

    public EnemyDefaultAttack(MonsterDefaultAttackSO data, Enemy enemy)
        : base(data, enemy)
    {
        _data = data;
    }

    public override void Enter()
    {
        base.Enter();
        if (!CanContinueAction) return;
        _hitTargets.Clear();
        _swingPlayed = false;
        _attackAnimationStarted = string.IsNullOrWhiteSpace(_data.startupAnimName) || _data.animationStartDelay <= 0f;
        if (_attackAnimationStarted)
            _enemy.Animator?.CrossFade(_data.animName, 0.01f);
        else
            _enemy.Animator?.CrossFadeInFixedTime(_data.startupAnimName, .03f, 0, 0f);

        // TODO: _enemy.Animator?.SetTrigger(_data.animName);
    }

    public override void Update()
    {
        if (!CanContinueAction) return;
        float previousTime = _timer;
        base.Update();
        if (!CanContinueAction) return;
        if (!_attackAnimationStarted && _timer >= _data.animationStartDelay)
        {
            _attackAnimationStarted = true;
            _enemy.Animator?.CrossFadeInFixedTime(_data.animName, .03f, 0, 0f);
        }

        TickBossPreparation(previousTime, _timer, 0f, _data.hitStartTime);
        if (_timer >= _data.hitStartTime + _data.hitDuration)
            SetBossStage(BossAttackStage.Recovery);

        if (CrossesHitWindow(previousTime, Mathf.Min(_timer, _data.attackDuration),
            _data.hitStartTime, _data.hitDuration))
        {
            if (!_swingPlayed)
            {
                _swingPlayed = true;
                PlaySwingSound();
            }
            // telegraphDuration을 hitStartTime으로 설정하면 베이스 클래스가 이 시점에 자동으로 EndTelegraph() 호출
            BroadcastHit(); // TakeDamage에서 이 액션을 소스로 식별하기 위해
            ExecuteHit();
            if (!CanContinueAction) return;
        }

        if (_timer >= _data.hitStartTime + _data.hitDuration) EndNormalAttackArmor();
        if (_timer >= _data.attackDuration)
            IsFinished = true;
    }

    private void ExecuteHit()
    {
        Vector3 center = GetHitCenter(_data.hitBoxOffset);

        foreach (var col in GetOverlap(center))
        {
            if (!CanContinueAction) return;
            var damageable = col.GetComponentInParent<IDamageable>();
            if (damageable == null || !_hitTargets.Add(damageable)) continue;
            DamageManager.Apply(
                new HitInfo(_data.damage * _enemy.EnemyStat.GetStat(EnemyStatType.ATK), _enemy.Element, _enemy.EnemyStat.GetStat(EnemyStatType.CriticalChance),
                            _enemy.EnemyStat.GetStat(EnemyStatType.CriticalDamage), power: GetKnockbackDirection(col) * _data.knockbackForce, staggerResistLevel: _data.staggerLevel, isParryable: _data.isParryable, source: this, impactSound: ResolveHitSound()),
                damageable, col.gameObject);
            if (!CanContinueAction) return;
        }
    }

    private Collider[] GetOverlap(Vector3 center)
    {
        switch (_data.hitBoxShape)
        {
            case HitBoxShape.Sphere:
                return Physics.OverlapSphere(center, _data.hitBoxRadius, _playerLayer);
            case HitBoxShape.Box:
                return Physics.OverlapBox(center, _data.hitBoxSize * 0.5f,
                           _enemy.transform.rotation, _playerLayer);
            case HitBoxShape.Capsule:
                float offset = _data.hitBoxHeight * 0.5f - _data.hitBoxRadius;
                return Physics.OverlapCapsule(
                    center + _enemy.transform.up * offset,
                    center - _enemy.transform.up * offset,
                    _data.hitBoxRadius, _playerLayer);
            default:
                return System.Array.Empty<Collider>();
        }
    }
}
