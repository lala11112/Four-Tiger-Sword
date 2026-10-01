using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class EnemySelfDestruct : EnemyAction
{
    private readonly MonsterSelfDestructSO _data;
    private GameObject _warning;
    private Vector3 _center;
    private Quaternion _rotation;
    private float _elapsed;
    private bool _previousRootMotion;
    private bool _movementLocked;
    private bool _exploded;

    public EnemySelfDestruct(MonsterSelfDestructSO data, Enemy enemy) : base(data, enemy)
    {
        _data = data;
    }

    public override void Enter()
    {
        base.Enter();
        _elapsed = 0f;
        _exploded = false;
        _rotation = _enemy.transform.rotation;
        _center = GetHitCenter(_data.explosionOffset);
        _enemy.LockMovement();
        _movementLocked = true;
        var nav = _enemy.GetComponent<NavMeshAgent>();
        if (nav != null && nav.enabled && nav.isOnNavMesh)
        {
            nav.ResetPath();
            nav.velocity = Vector3.zero;
        }
        if (_enemy.Animator != null)
        {
            _previousRootMotion = _enemy.Animator.applyRootMotion;
            _enemy.Animator.applyRootMotion = false;
            _enemy.Animator.CrossFade(string.IsNullOrEmpty(_data.animName) ? "Move" : _data.animName, 0.1f);
        }
        if (_data.warningEffectPrefab != null)
        {
            _warning = Object.Instantiate(_data.warningEffectPrefab,
                _center + _data.warningEffectOffset, _rotation);
            _warning.transform.localScale = _data.warningEffectScale;
        }
    }

    public override void Update()
    {
        if (!IsActive || IsFinished || _exploded) return;
        base.Update();
        _elapsed += Time.deltaTime;
        if (_elapsed < Mathf.Max(0f, _data.explosionDelay)) return;

        _exploded = true; // 프레임/콜라이더 수와 무관하게 한 번만 폭발합니다.
        EndTelegraph();
        ClearWarning();
        if (_data.explosionEffectPrefab != null)
        {
            var effect = Object.Instantiate(_data.explosionEffectPrefab, _center, _rotation);
            Object.Destroy(effect, Mathf.Max(0.1f, _data.explosionEffectLifetime));
        }
        BroadcastHit();
        var hitTargets = new HashSet<IDamageable>();
        foreach (var col in Physics.OverlapSphere(_center, _data.explosionRadius,
                     _playerLayer, QueryTriggerInteraction.Collide))
        {
            var target = col.GetComponentInParent<IDamageable>();
            if (target == null || ReferenceEquals(target, _enemy) || !hitTargets.Add(target)) continue;
            Vector3 direction = (col.transform.position - _center).normalized;
            DamageManager.Apply(new HitInfo(
                _data.damage * _enemy.EnemyStat.GetStat(EnemyStatType.ATK), _enemy.Element,
                _enemy.EnemyStat.GetStat(EnemyStatType.CriticalChance),
                _enemy.EnemyStat.GetStat(EnemyStatType.CriticalDamage),
                power: direction * _data.knockbackForce, staggerResistLevel: _data.staggerLevel, isParryable: _data.isParryable, source: this),
                target, col.gameObject);
        }
        _enemy.CompleteSelfDestruct(this);
    }

    public override void Exit()
    {
        if (!IsActive) return;
        ClearWarning();
        if (_movementLocked)
        {
            _enemy.UnlockMovement();
            _movementLocked = false;
        }
        if (_enemy.Animator != null) _enemy.Animator.applyRootMotion = _previousRootMotion;
        base.Exit();
    }

    private void ClearWarning()
    {
        if (_warning != null) Object.Destroy(_warning);
        _warning = null;
    }
}
