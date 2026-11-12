using System.Collections.Generic;
using UnityEngine;

namespace Baekho.Generated.a3355a1c8240244a995e4f671b0dc48bb
{
    public sealed class EnemyTripleSphereBurst : EnemyAction
    {
        private sealed class Projectile
        {
            public GameObject Visual;
            public Vector3 Position;
            public Vector3 Direction;
            public float Age;
            public bool Alive = true;
            public readonly HashSet<IDamageable> HitTargets = new HashSet<IDamageable>();
        }

        private readonly MonsterTripleSphereBurstSO _data;
        private readonly List<Projectile> _projectiles = new List<Projectile>();
        private bool _exitHandled;
        private bool _movementLocked;
        private bool _rootMotionChanged;
        private bool _previousRootMotion;
        private Animator _animator;
        private float _startup;
        private float _interval;
        private int _count;
        private int _fired;
        private float _speed;
        private float _radius;
        private float _lifetime;
        private float _damage;
        private float _knockback;
        private float _targetHeight;
        private Vector3 _spawnOffset;
        private Material _material;
        private bool _hasTargetPosition;
        private Vector3 _lastTargetPosition;

        public EnemyTripleSphereBurst(MonsterTripleSphereBurstSO data, Enemy enemy)
            : base(data, enemy)
        {
            _data = data;
        }

        public override void Enter()
        {
            if (IsActive) Exit();
            ClearProjectiles();
            _data.ValidateRuntime();
            _exitHandled = false;
            _movementLocked = false;
            _rootMotionChanged = false;
            _startup = _data.startupTime;
            _interval = _data.shotInterval;
            _count = _data.projectileCount;
            _speed = _data.projectileSpeed;
            _radius = _data.projectileRadius;
            _lifetime = _data.projectileLifetime;
            _damage = _data.damage;
            _knockback = _data.knockbackForce;
            _targetHeight = _data.targetHeight;
            _spawnOffset = _data.spawnOffset;
            _material = _data.projectileMaterial;
            _fired = 0;
            _hasTargetPosition = false;

            base.Enter();
            if (!IsActive || _exitHandled) return;
            if (OwnerInterrupted())
            {
                Exit();
                return;
            }

            _enemy.LockMovement();
            _movementLocked = true;
            _animator = _enemy.Animator;
            if (_animator != null)
            {
                _previousRootMotion = _animator.applyRootMotion;
                _rootMotionChanged = true;
                _animator.applyRootMotion = false;
                if (_animator.runtimeAnimatorController != null && _animator.layerCount > 0
                    && _animator.HasState(0, Animator.StringToHash(_data.animName)))
                    _animator.CrossFade(_data.animName, 0.01f, 0);
            }
            RefreshTarget();
            FaceRememberedTarget();
        }

        public override void Update()
        {
            if (!IsActive || IsFinished || _exitHandled) return;
            if (OwnerInterrupted())
            {
                Exit();
                return;
            }

            float delta = _enemy.ActionDeltaTime;
            if (!Finite(delta) || delta < 0f)
            {
                Exit();
                return;
            }
            float rate = Safe(_data.attackSpeed, 1f, 0.01f, 10f);
            _data.attackSpeed = rate;
            float cursor = _timer;
            base.Update();
            if (!IsActive || _exitHandled) return;
            if (!Finite(_timer) || OwnerInterrupted())
            {
                Exit();
                return;
            }
            if (delta <= 0f) return;

            if (_fired < _count)
            {
                RefreshTarget();
                FaceRememberedTarget();
            }

            // 프레임이 여러 발사 시각을 넘겨도 각 발사 전후의 이동 시간을 분리합니다.
            float end = _timer;
            while (_fired < _count)
            {
                float scheduled = _startup + _fired * _interval;
                if (scheduled > end) break;
                float launchTime = Mathf.Max(cursor, scheduled);
                if (!AdvanceProjectiles((launchTime - cursor) / rate)) return;
                cursor = launchTime;
                _fired++;
                if (_fired == 1)
                {
                    EndAttackWarning();
                    EndTelegraph();
                }
                if (!IsActive || _exitHandled || !CanContinueAction) return;
                SpawnProjectile();
                if (!IsActive || _exitHandled || !CanContinueAction) return;
                if (_fired >= _count) EndNormalAttackArmor();
            }

            if (!AdvanceProjectiles(Mathf.Max(0f, end - cursor) / rate)) return;
            if (_fired >= _count && _projectiles.Count == 0)
            {
                EndTelegraph();
                IsFinished = true;
            }
        }

        public override void OnParried()
        {
            base.OnParried();
            if (WasParried) Exit();
        }

        public override void Exit()
        {
            if (_exitHandled) return;
            _exitHandled = true;
            EndNormalAttackArmor();
            ClearProjectiles();
            if (_rootMotionChanged)
            {
                _rootMotionChanged = false;
                if (_animator != null) _animator.applyRootMotion = _previousRootMotion;
            }
            if (_movementLocked)
            {
                _movementLocked = false;
                if (_enemy != null) _enemy.UnlockMovement();
            }
            base.Exit();
        }

        private bool OwnerInterrupted()
        {
            return !CanContinueAction || _enemy == null || !_enemy.isActiveAndEnabled
                || _enemy.EnemyStat == null || _enemy.EnemyStat.IsDead
                || _enemy.IsHurt || _enemy.IsGroggy || _enemy.IsRoot
                || _enemy.IsKnockbacking || _enemy.IsSelfDestructing
                || _enemy.IsSilenced || _enemy.CombatLabPassive;
        }

        private void RefreshTarget()
        {
            Transform target = _enemy.DetectedTarget;
            Vector3 position;
            if (target != null) position = target.position;
            else if (_enemy.HasTargetMemory) position = _enemy.LastKnownTargetPosition;
            else return;
            if (!FiniteVector(position)) return;
            _lastTargetPosition = position;
            _hasTargetPosition = true;
        }

        private void FaceRememberedTarget()
        {
            if (!_hasTargetPosition) return;
            Vector3 direction = _lastTargetPosition - _enemy.transform.position;
            direction.y = 0f;
            if (ValidDirection(direction))
                _enemy.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        private void SpawnProjectile()
        {
            RefreshTarget();
            FaceRememberedTarget();
            Vector3 origin = GetHitCenter(_spawnOffset);
            if (!FiniteVector(origin))
            {
                Exit();
                return;
            }
            Vector3 direction = _hasTargetPosition
                ? _lastTargetPosition + Vector3.up * _targetHeight - origin
                : _enemy.transform.forward;
            if (!ValidDirection(direction)) direction = _enemy.transform.forward;
            if (!ValidDirection(direction)) direction = Vector3.forward;
            direction.Normalize();

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "Triple Sphere Burst Projectile";
            Collider visualCollider = visual.GetComponent<Collider>();
            if (visualCollider != null)
            {
                visualCollider.enabled = false;
                Object.Destroy(visualCollider);
            }
            visual.transform.SetPositionAndRotation(origin, Quaternion.identity);
            visual.transform.localScale = Vector3.one * (_radius * 2f);
            Renderer renderer = visual.GetComponent<Renderer>();
            if (renderer != null && _material != null) renderer.sharedMaterial = _material;
            StageTransientObject.Track(visual);

            var projectile = new Projectile
            {
                Visual = visual,
                Position = origin,
                Direction = direction
            };
            _projectiles.Add(projectile);
            PlaySwingSound();
            // SphereCast가 놓치는 시작점 내부 중첩도 즉시 처리합니다.
            MoveProjectile(projectile, 0f);
        }

        private bool AdvanceProjectiles(float delta)
        {
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                Projectile projectile = _projectiles[i];
                MoveProjectile(projectile, delta);
                // 피해 콜백의 패링/중단으로 Exit가 목록을 비울 수 있습니다.
                if (!IsActive || _exitHandled) return false;
                if (!projectile.Alive) _projectiles.RemoveAt(i);
            }
            return IsActive && !_exitHandled;
        }

        private void MoveProjectile(Projectile projectile, float delta)
        {
            if (!projectile.Alive) return;
            if (projectile.Visual == null || !projectile.Visual.activeInHierarchy)
            {
                RemoveProjectile(projectile);
                return;
            }
            float remaining = Mathf.Max(0f, _lifetime - projectile.Age);
            if (remaining <= 0f)
            {
                RemoveProjectile(projectile);
                return;
            }
            float step = Mathf.Min(Mathf.Max(0f, delta), remaining);
            float distance = _speed * step;
            if (FindContact(projectile.Position, projectile.Direction, distance,
                out Collider contact, out IDamageable target, out float contactDistance))
            {
                projectile.Position += projectile.Direction * contactDistance;
                projectile.Visual.transform.position = projectile.Position;
                ResolveContact(projectile, contact, target);
                return;
            }

            projectile.Position += projectile.Direction * distance;
            projectile.Age += step;
            projectile.Visual.transform.position = projectile.Position;
            if (step >= remaining) RemoveProjectile(projectile);
        }

        private bool FindContact(Vector3 position, Vector3 direction, float distance,
            out Collider contact, out IDamageable target, out float contactDistance)
        {
            contact = null;
            target = null;
            contactDistance = 0f;

            Collider[] overlaps = Physics.OverlapSphere(position, _radius,
                Physics.AllLayers, QueryTriggerInteraction.Collide);
            foreach (Collider candidate in overlaps)
            {
                if (!Classify(candidate, out IDamageable candidateTarget)) continue;
                // 동시에 겹친 경우 환경을 우선해 벽 내부에서 피해가 새지 않게 합니다.
                if (contact == null || (candidateTarget == null && target != null))
                {
                    contact = candidate;
                    target = candidateTarget;
                }
            }
            if (contact != null) return true;
            if (distance <= 0f) return false;

            RaycastHit[] hits = Physics.SphereCastAll(position, _radius, direction,
                distance, Physics.AllLayers, QueryTriggerInteraction.Collide);
            float nearest = float.PositiveInfinity;
            foreach (RaycastHit hit in hits)
            {
                if (!Finite(hit.distance)
                    || !Classify(hit.collider, out IDamageable candidateTarget)) continue;
                float candidateDistance = Mathf.Clamp(hit.distance, 0f, distance);
                bool nearer = candidateDistance < nearest;
                bool environmentTie = candidateTarget == null && target != null
                    && Mathf.Abs(candidateDistance - nearest) <= 0.00001f;
                if (contact == null || nearer || environmentTie)
                {
                    nearest = candidateDistance;
                    contact = hit.collider;
                    target = candidateTarget;
                    contactDistance = candidateDistance;
                }
            }
            return contact != null;
        }

        private bool Classify(Collider collider, out IDamageable target)
        {
            target = null;
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                return false;
            Transform hitTransform = collider.transform;
            if (hitTransform == _enemy.transform || hitTransform.IsChildOf(_enemy.transform))
                return false;
            IDamageable candidate = collider.GetComponentInParent<IDamageable>();
            if (ReferenceEquals(candidate, _enemy)) return false;
            if (candidate != null && IsPlayerCollider(collider))
            {
                target = candidate;
                return true;
            }
            // 플레이어의 피해용 트리거는 포함하고, 기타 센서 트리거는 통과합니다.
            return !collider.isTrigger;
        }

        private bool IsPlayerCollider(Collider collider)
        {
            for (Transform current = collider.transform; current != null; current = current.parent)
            {
                if ((_playerLayer.value & (1 << current.gameObject.layer)) != 0) return true;
            }
            return false;
        }

        private void ResolveContact(Projectile projectile, Collider contact, IDamageable target)
        {
            // 비관통 탄환이며 콜백 전에 비활성화하여 재진입 중 중복 피격을 막습니다.
            RemoveProjectile(projectile);
            if (target == null || contact == null || !projectile.HitTargets.Add(target)) return;
            if (OwnerInterrupted())
            {
                Exit();
                return;
            }

            float baseDamage = _damage * _enemy.EnemyStat.GetStat(EnemyStatType.ATK);
            if (!Finite(baseDamage) || baseDamage < 0f) baseDamage = 0f;
            float criticalChance = Safe(_enemy.EnemyStat.GetStat(EnemyStatType.CriticalChance), 0f, 0f, 1f);
            float criticalMultiplier = Safe(_enemy.EnemyStat.GetStat(EnemyStatType.CriticalDamage), 1f, 0f, 1000f);
            var info = new HitInfo(baseDamage, _enemy.Element, criticalChance, criticalMultiplier,
                power: projectile.Direction * _knockback,
                staggerResistLevel: _data.staggerLevel,
                isParryable: _data.isParryable,
                source: this,
                impactSound: ResolveHitSound());
            BroadcastHit();
            if (!IsActive || IsFinished || !CanContinueAction) return;
            DamageResult result = DamageManager.Apply(info, target, contact.gameObject);
            if (result.Outcome == DamageOutcome.Parried)
                OnParried();
            else if (IsActive && OwnerInterrupted())
                Exit();
        }

        private void RemoveProjectile(Projectile projectile)
        {
            projectile.Alive = false;
            if (projectile.Visual != null)
            {
                projectile.Visual.SetActive(false);
                Object.Destroy(projectile.Visual);
                projectile.Visual = null;
            }
        }

        private void ClearProjectiles()
        {
            for (int i = 0; i < _projectiles.Count; i++) RemoveProjectile(_projectiles[i]);
            _projectiles.Clear();
        }

        private bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private bool FiniteVector(Vector3 value)
        {
            return Finite(value.x) && Finite(value.y) && Finite(value.z);
        }

        private bool ValidDirection(Vector3 value)
        {
            return FiniteVector(value) && Finite(value.sqrMagnitude) && value.sqrMagnitude > 0.000001f;
        }

        private float Safe(float value, float fallback, float minimum, float maximum)
        {
            return Finite(value) ? Mathf.Clamp(value, minimum, maximum) : fallback;
        }
    }
}
