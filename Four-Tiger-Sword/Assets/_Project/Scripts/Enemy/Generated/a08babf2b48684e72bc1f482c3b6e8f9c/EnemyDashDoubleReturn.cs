using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Baekho.Generated.a08babf2b48684e72bc1f482c3b6e8f9c
{
    public sealed class EnemyDashDoubleReturn : EnemyAction
    {
        private enum Phase { Startup, Outbound, Combo, Return, Recovery }
        private const float Skin = 0.03f;
        private const float ArrivalTolerance = 0.015f;

        private readonly MonsterDashDoubleReturnSO _data;
        private readonly HashSet<IDamageable> _firstTargets;
        private readonly HashSet<IDamageable> _secondTargets;
        private NavMeshAgent _nav;
        private Animator _animator;
        private Transform _target;
        private Phase _phase;
        private Vector3 _origin, _goal, _facing, _hitOffset;
        private float _phaseElapsed, _moveLimit;
        private float _speed, _startup, _dashSpeed, _returnSpeed, _maxDistance, _stopDistance;
        private float _firstDelay, _interval, _hitDuration, _returnDelay, _recovery, _comboDuration;
        private float _firstDamage, _secondDamage, _knockback, _hitRadius, _telegraphLimit;
        private float _bodyRadius, _bodyHeight;
        private int _obstacleMask;
        private bool _firstSwing, _secondSwing, _exited, _movementLocked;
        private bool _navigationChanged, _rootMotionChanged;
        private bool _previousUpdateRotation, _previousStopped, _previousRootMotion;

        public EnemyDashDoubleReturn(MonsterDashDoubleReturnSO data, Enemy enemy) : base(data, enemy)
        {
            _data = data;
            _firstTargets = new HashSet<IDamageable>();
            _secondTargets = new HashSet<IDamageable>();
        }

        public override void Enter()
        {
            if (IsActive) Exit();
            _exited = false;
            _movementLocked = false;
            _navigationChanged = false;
            _rootMotionChanged = false;
            _firstSwing = false;
            _secondSwing = false;
            _firstTargets.Clear();
            _secondTargets.Clear();
            SetPhase(Phase.Startup);
            ReadSettings();
            base.Enter();
            if (!IsActive || _exited) return;

            _nav = _enemy.GetComponent<NavMeshAgent>();
            _animator = _enemy.Animator;
            _target = _enemy.DetectedTarget;
            _enemy.SetAnimationSpeed(_speed);
            if (!CanContinue() || !NavigationReady() || _target == null)
            {
                Exit();
                return;
            }

            _origin = _nav.nextPosition;
            if (!Finite(_origin))
            {
                Exit();
                return;
            }
            _bodyRadius = Safe(_nav.radius, 0.5f, 0.05f, 5f);
            _bodyHeight = Safe(_nav.height, 2f, _bodyRadius * 2f, 20f);
            _previousStopped = _nav.isStopped;
            _previousUpdateRotation = _nav.updateRotation;
            _navigationChanged = true;
            _nav.updateRotation = false;
            _nav.ResetPath();
            _nav.velocity = Vector3.zero;
            _enemy.LockMovement();
            _movementLocked = true;

            if (_animator != null)
            {
                _previousRootMotion = _animator.applyRootMotion;
                _rootMotionChanged = true;
                _animator.applyRootMotion = false;
            }
            FaceCachedTarget();
            PlayState(_data.animName);
            if (_telegraphLimit <= 0f) EndTelegraph();
        }

        public override void Update()
        {
            if (!IsActive || IsFinished || _exited) return;
            float previousTime = _timer;
            float elapsed = Safe(_enemy.ActionDeltaTime, 0f, 0f, 60f) * _speed;
            base.Update();
            // 공유 SO를 변경하지 않고 비정상 속도 및 시간 값의 전파를 막습니다.
            _timer = previousTime + elapsed;
            if (!IsActive || IsFinished || _exited) return;
            if (!CanContinue() || !NavigationReady())
            {
                Exit();
                return;
            }
            if (_timer >= _telegraphLimit) EndTelegraph();
            if (!IsActive || IsFinished || !CanContinueAction || elapsed <= 0f) return;

            float remaining = elapsed;
            // 단계 경계에서 남은 시간을 다음 단계에 전달하여 낮은 프레임에서도 판정을 보존합니다.
            for (int transition = 0; transition < 12 && IsActive && !IsFinished; transition++)
            {
                switch (_phase)
                {
                    case Phase.Startup:
                        FaceCachedTarget();
                        ConsumeTime(ref remaining, _startup);
                        if (_phaseElapsed < _startup) return;
                        BeginDash();
                        break;

                    case Phase.Outbound:
                        if (!AdvanceMovement(_goal, _dashSpeed, ref remaining)) return;
                        if (!IsActive || IsFinished) return;
                        SetPhase(Phase.Combo);
                        break;

                    case Phase.Combo:
                        float previousComboTime = _phaseElapsed;
                        ConsumeTime(ref remaining, _comboDuration);
                        UpdateStrike(false, previousComboTime, _phaseElapsed);
                        if (!IsActive || IsFinished) return;
                        UpdateStrike(true, previousComboTime, _phaseElapsed);
                        if (!IsActive || IsFinished) return;
                        if (_phaseElapsed >= _firstDelay + _interval + _hitDuration) EndNormalAttackArmor();
                        if (_phaseElapsed < _comboDuration) return;
                        BeginReturn();
                        break;

                    case Phase.Return:
                        if (!AdvanceMovement(_origin, _returnSpeed, ref remaining)) return;
                        if (!IsActive || IsFinished) return;
                        SetPhase(Phase.Recovery);
                        break;

                    case Phase.Recovery:
                        ConsumeTime(ref remaining, _recovery);
                        if (_phaseElapsed >= _recovery) Exit();
                        return;
                }
            }
            if (IsActive && !IsFinished) Exit();
        }

        public override void Exit()
        {
            if (_exited) return;
            _exited = true;
            EndNormalAttackArmor();
            IsFinished = true;

            if (_navigationChanged && _nav != null)
            {
                _nav.updateRotation = _previousUpdateRotation;
                if (_nav.enabled && _nav.isOnNavMesh) _nav.velocity = Vector3.zero;
            }
            if (_rootMotionChanged && _animator != null)
                _animator.applyRootMotion = _previousRootMotion;
            _rootMotionChanged = false;

            if (_movementLocked)
            {
                _movementLocked = false;
                if (_enemy != null)
                {
                    _enemy.UnlockMovement();
                    _enemy.SyncMovementLock();
                }
            }
            if (_navigationChanged && _previousStopped && _nav != null
                && _nav.enabled && _nav.isOnNavMesh)
                _nav.isStopped = true;
            _navigationChanged = false;
            _firstTargets.Clear();
            _secondTargets.Clear();
            _target = null;
            base.Exit();
        }

        public override void OnParried()
        {
            base.OnParried();
            if (IsActive && _data.isParryable) Exit();
        }

        private void ReadSettings()
        {
            _speed = Safe(_data.attackSpeed, 1f, 0.01f, 20f);
            _startup = Safe(_data.startupTime, 0.15f, 0f, 5f);
            _dashSpeed = Safe(_data.dashSpeed, 18f, 1f, 60f);
            _returnSpeed = Safe(_data.returnSpeed, 20f, 1f, 60f);
            _maxDistance = Safe(_data.maxDashDistance, 6f, 0f, 30f);
            _stopDistance = Safe(_data.stopDistance, 1.2f, 0f, 10f);
            _firstDelay = Safe(_data.firstHitDelay, 0.05f, 0f, 5f);
            _hitDuration = Safe(_data.hitDuration, 0.08f, 0.01f, 1f);
            _interval = Mathf.Max(_hitDuration + 0.01f, Safe(_data.hitInterval, 0.2f, 0.01f, 5f));
            _returnDelay = Safe(_data.returnDelay, 0.05f, 0f, 5f);
            _recovery = Safe(_data.recoveryTime, 0.15f, 0f, 5f);
            _comboDuration = _firstDelay + _interval + _hitDuration + _returnDelay;
            _firstDamage = Safe(_data.firstDamage, 0.5f, 0f, 100f);
            _secondDamage = Safe(_data.secondDamage, 0.5f, 0f, 100f);
            _knockback = Safe(_data.knockbackForce, 0f, 0f, 100f);
            _hitRadius = Safe(_data.hitBoxRadius, 0.85f, 0.05f, 5f);
            _hitOffset = new Vector3(
                Safe(_data.hitBoxOffset.x, 0f, -5f, 5f),
                Safe(_data.hitBoxOffset.y, 1f, -5f, 5f),
                Safe(_data.hitBoxOffset.z, 0.9f, -5f, 5f));
            _obstacleMask = _data.obstacleMask.value;
            _telegraphLimit = Safe(_data.telegraphDuration, 0.15f, 0f, 30f);
        }

        private void BeginDash()
        {
            if (_target == null || !_target.gameObject.activeInHierarchy || !Finite(_target.position))
            {
                Exit();
                return;
            }
            Vector3 start = _nav.nextPosition;
            Vector3 offset = Flat(_target.position - start);
            float distance = offset.magnitude;
            if (!Finite(start) || !Finite(distance))
            {
                Exit();
                return;
            }
            _facing = distance > 0.001f ? offset / distance : Flat(_enemy.transform.forward).normalized;
            if (_facing.sqrMagnitude < 0.001f) _facing = Vector3.forward;
            _enemy.transform.rotation = Quaternion.LookRotation(_facing, Vector3.up);
            float travel = Mathf.Clamp(distance - _stopDistance, 0f, _maxDistance);
            _goal = start + _facing * travel;
            _moveLimit = travel / _dashSpeed + 0.25f;
            SetPhase(Phase.Outbound);
            PlayState(_data.dashAnimName);
        }

        private void BeginReturn()
        {
            EndTelegraph();
            if (!IsActive || IsFinished) return;
            _moveLimit = Flat(_origin - _nav.nextPosition).magnitude / _returnSpeed + 0.25f;
            SetPhase(Phase.Return);
            // 회전은 바꾸지 않아 전진 방향을 바라보면서 출발 위치로 후퇴합니다.
            PlayState(_data.returnAnimName);
        }

        private void SetPhase(Phase phase)
        {
            _phase = phase;
            _phaseElapsed = 0f;
        }

        private void ConsumeTime(ref float remaining, float duration)
        {
            float consumed = Mathf.Min(remaining, Mathf.Max(0f, duration - _phaseElapsed));
            _phaseElapsed += consumed;
            remaining = Mathf.Max(0f, remaining - consumed);
        }

        private bool AdvanceMovement(Vector3 destination, float speed, ref float remaining)
        {
            float budget = Mathf.Min(remaining, Mathf.Max(0f, _moveLimit - _phaseElapsed));
            bool ended = MoveSafely(destination, speed, budget, out float consumed);
            _phaseElapsed += consumed;
            remaining = Mathf.Max(0f, remaining - consumed);
            return ended || _phaseElapsed + 0.000001f >= _moveLimit;
        }

        private bool MoveSafely(Vector3 destination, float speed, float budget, out float consumed)
        {
            consumed = 0f;
            Vector3 before = _nav.nextPosition;
            Vector3 offset = Flat(destination - before);
            float distance = offset.magnitude;
            if (!Finite(before) || !Finite(distance))
            {
                Exit();
                return true;
            }
            if (distance <= ArrivalTolerance) return true;
            if (budget <= 0f) return false;

            Vector3 direction = offset / distance;
            float requested = Mathf.Min(distance, speed * budget);
            float permitted = requested;
            if (_nav.Raycast(before + direction * requested, out NavMeshHit boundary))
            {
                float along = Vector3.Dot(Flat(boundary.position - before), direction);
                permitted = Mathf.Min(permitted, Mathf.Max(0f, along - Skin));
            }

            Vector3 bottom = before + Vector3.up * (_bodyRadius + Skin);
            Vector3 top = before + Vector3.up * (Mathf.Max(_bodyRadius, _bodyHeight - _bodyRadius) + Skin);
            RaycastHit[] obstacles = Physics.CapsuleCastAll(bottom, top, _bodyRadius,
                direction, permitted + Skin, _obstacleMask, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit obstacle in obstacles)
            {
                Collider collider = obstacle.collider;
                if (collider == null || IsOwnerCollider(collider)) continue;
                permitted = Mathf.Min(permitted, Mathf.Max(0f, obstacle.distance - Skin));
            }

            _nav.velocity = Vector3.zero;
            if (permitted > 0f) _nav.Move(direction * permitted);
            Vector3 after = _nav.nextPosition;
            if (!Finite(after))
            {
                Exit();
                return true;
            }
            // 같은 프레임에 타격 단계로 넘어가도 NavMesh가 허용한 실제 위치를 사용합니다.
            _enemy.transform.position = after;
            float moved = Flat(after - before).magnitude;
            bool blocked = permitted + 0.0001f < requested || moved + 0.001f < permitted;
            bool arrived = Flat(destination - after).magnitude <= ArrivalTolerance;
            consumed = blocked || arrived ? Mathf.Min(budget, moved / speed) : budget;
            return blocked || arrived;
        }

        private void UpdateStrike(bool second, float previous, float current)
        {
            if (!IsActive || IsFinished) return;
            float start = _firstDelay + (second ? _interval : 0f);
            if (!CrossesHitWindow(previous, current, start, _hitDuration)) return;
            bool played = second ? _secondSwing : _firstSwing;
            if (!played)
            {
                if (second) _secondSwing = true;
                else _firstSwing = true;
                EndAttackWarning();
                EndTelegraph();
                if (!IsActive || IsFinished) return;
                PlayState(_data.animName);
                PlaySwingSound();
            }
            ExecuteStrike(second ? _secondTargets : _firstTargets,
                second ? _secondDamage : _firstDamage);
        }

        private void ExecuteStrike(HashSet<IDamageable> struck, float multiplier)
        {
            Vector3 center = GetHitCenter(_hitOffset);
            Collider[] colliders = Physics.OverlapSphere(center, _hitRadius,
                _playerLayer, QueryTriggerInteraction.Collide);
            foreach (Collider collider in colliders)
            {
                if (!IsActive || IsFinished) return;
                if (collider == null || IsOwnerCollider(collider)) continue;
                IDamageable receiver = collider.GetComponentInParent<IDamageable>();
                if (receiver == null || ReferenceEquals(receiver, _enemy) || struck.Contains(receiver)) continue;
                if (receiver is Component component && component == null) continue;
                if (!HasClearLine(collider, receiver)) continue;
                if (!struck.Add(receiver)) continue;

                EnemyStat stat = _enemy.EnemyStat;
                float attack = Safe(stat.GetStat(EnemyStatType.ATK), 0f, 0f, 1000000f);
                float chance = Safe(stat.GetStat(EnemyStatType.CriticalChance), 0f, 0f, 1f);
                float critical = Safe(stat.GetStat(EnemyStatType.CriticalDamage), 1f, 0f, 100f);
                StaggerResistLevel stagger = (StaggerResistLevel)Mathf.Clamp((int)_data.staggerLevel, 0, 4);
                HitInfo info = new HitInfo(multiplier * attack, _enemy.Element, chance, critical,
                    power: _facing * _knockback, staggerResistLevel: stagger,
                    isParryable: _data.isParryable, source: this, impactSound: ResolveHitSound());
                BroadcastHit();
                if (!IsActive || IsFinished || !CanContinueAction) return;
                DamageResult result = DamageManager.Apply(info, receiver, collider.gameObject);
                if (result.Outcome == DamageOutcome.Parried) OnParried();
                if (!IsActive || IsFinished) return;
                if (!CanContinue())
                {
                    Exit();
                    return;
                }
            }
        }

        private bool HasClearLine(Collider targetCollider, IDamageable receiver)
        {
            Vector3 origin = _enemy.transform.position + Vector3.up * Mathf.Clamp(_hitOffset.y, 0.1f, 5f);
            Vector3 ray = targetCollider.ClosestPoint(origin) - origin;
            float distance = ray.magnitude;
            if (distance <= 0.001f) return true;
            RaycastHit[] obstacles = Physics.RaycastAll(origin, ray / distance, distance,
                _obstacleMask, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit obstacle in obstacles)
            {
                Collider collider = obstacle.collider;
                if (collider == null || IsOwnerCollider(collider)) continue;
                if (ReferenceEquals(collider.GetComponentInParent<IDamageable>(), receiver)) continue;
                return false;
            }
            return true;
        }

        private void FaceCachedTarget()
        {
            if (_target == null || !_target.gameObject.activeInHierarchy) return;
            Vector3 direction = Flat(_target.position - _enemy.transform.position);
            if (Finite(direction) && Finite(direction.sqrMagnitude) && direction.sqrMagnitude > 0.001f)
                _enemy.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        private void PlayState(string stateName)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null
                || _animator.layerCount == 0 || string.IsNullOrWhiteSpace(stateName)) return;
            if (_animator.HasState(0, Animator.StringToHash(stateName)))
                _animator.CrossFadeInFixedTime(stateName, 0.035f, 0, 0f);
        }

        private bool CanContinue()
        {
            return CanContinueAction && _enemy != null && _enemy.isActiveAndEnabled && _enemy.EnemyStat != null
                && !_enemy.EnemyStat.IsDead && !_enemy.IsHurt && !_enemy.IsGroggy
                && !_enemy.IsRoot && !_enemy.IsKnockbacking && !_enemy.IsSilenced;
        }

        private bool NavigationReady()
        {
            return _nav != null && _nav.enabled && _nav.isOnNavMesh
                && _nav.updatePosition && !_nav.isOnOffMeshLink;
        }

        private bool IsOwnerCollider(Collider collider)
        {
            return collider.transform.IsChildOf(_enemy.transform);
        }

        private Vector3 Flat(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        private bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private bool Finite(Vector3 value)
        {
            return Finite(value.x) && Finite(value.y) && Finite(value.z);
        }

        private float Safe(float value, float fallback, float minimum, float maximum)
        {
            return Mathf.Clamp(Finite(value) ? value : fallback, minimum, maximum);
        }
    }
}
