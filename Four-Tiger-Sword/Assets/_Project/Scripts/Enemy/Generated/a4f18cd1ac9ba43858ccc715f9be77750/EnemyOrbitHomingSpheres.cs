using System.Collections.Generic;
using UnityEngine;

namespace Baekho.Generated.a4f18cd1ac9ba43858ccc715f9be77750
{
    public sealed class EnemyOrbitHomingSpheres : EnemyAction
    {
        private enum Phase { Orbit, Homing, Straight }

        private sealed class Sphere
        {
            public GameObject Visual;
            public Vector3 Position;
            public Vector3 Direction;
            public bool Alive;
            public readonly HashSet<IDamageable> HitTargets = new HashSet<IDamageable>();
        }

        private const float MaxStep = 1f / 60f;
        private readonly MonsterOrbitHomingSpheresSO _data;
        private readonly Collider[] _overlapBuffer;
        private readonly RaycastHit[] _castBuffer;
        private Sphere[] _spheres;
        private Transform _target;
        private Animator _animator;
        private Phase _phase;
        private bool _movementLocked;
        private bool _rootMotionSaved;
        private bool _previousRootMotion;
        private bool _exiting;
        private int _count;
        private int _liveCount;
        private int _obstacleMask;
        private float _phaseTime;
        private float _speedMultiplier;
        private float _orbitDuration;
        private float _homingDuration;
        private float _straightDuration;
        private float _orbitRadius;
        private float _orbitAngularSpeed;
        private float _projectileSpeed;
        private float _radius;
        private float _turnRadians;
        private float _targetHeight;
        private float _damage;
        private float _knockback;
        private Vector3 _centerOffset;

        public EnemyOrbitHomingSpheres(MonsterOrbitHomingSpheresSO data, Enemy enemy)
            : base(data, enemy)
        {
            _data = data;
            _overlapBuffer = new Collider[64];
            _castBuffer = new RaycastHit[64];
        }

        public override void Enter()
        {
            if (IsActive) Exit();
            _exiting = false;
            if (!UsableOwner() || !SafeBaseTiming())
            {
                IsFinished = true;
                return;
            }

            ReadSettings();
            _phase = Phase.Orbit;
            _phaseTime = 0f;
            _liveCount = 0;
            _target = _enemy.DetectedTarget;
            _spheres = new Sphere[_count];

            base.Enter();
            if (!IsActive || IsFinished) return;

            _enemy.LockMovement();
            _movementLocked = true;
            _animator = _enemy.Animator;
            if (_animator != null)
            {
                _previousRootMotion = _animator.applyRootMotion;
                _rootMotionSaved = true;
                _animator.applyRootMotion = false;
                _enemy.SetAnimationSpeed(_speedMultiplier);
                if (!string.IsNullOrWhiteSpace(_data.animName)
                    && _animator.runtimeAnimatorController != null && _animator.layerCount > 0)
                {
                    int state = Animator.StringToHash(_data.animName);
                    if (_animator.HasState(0, state)) _animator.CrossFade(state, 0.1f, 0);
                }
            }

            for (int i = 0; i < _count && IsActive; i++)
            {
                Vector3 position = OrbitPosition(i, 0f);
                if (!Finite(position)) continue;
                GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                visual.name = "Orbit Homing Sphere " + (i + 1);
                Collider body = visual.GetComponent<Collider>();
                if (body != null) body.enabled = false;
                visual.transform.position = position;
                visual.transform.localScale = Vector3.one * (_radius * 2f);
                Renderer renderer = visual.GetComponent<Renderer>();
                if (renderer != null && _data.projectileMaterial != null)
                    renderer.sharedMaterial = _data.projectileMaterial;
                StageTransientObject.Track(visual);

                Sphere sphere = new Sphere
                {
                    Visual = visual,
                    Position = position,
                    Direction = _enemy.transform.forward,
                    Alive = true
                };
                _spheres[i] = sphere;
                _liveCount++;
                MoveSphere(sphere, position, false);
            }

            if (!IsActive) return;
            if (_liveCount == 0) Exit();
            else if (_orbitDuration <= 0f) AdvancePhase();
        }

        public override void Update()
        {
            if (!IsActive || IsFinished || _exiting) return;
            if (!UsableOwner() || !SafeBaseTiming())
            {
                Exit();
                return;
            }

            float delta = Safe(_enemy.ActionDeltaTime, 0f, 0f, 60f);
            float previousTimer = _timer;
            base.Update();
            _timer = previousTimer + delta * _speedMultiplier;
            if (delta <= 0f) return;

            float remaining = delta;
            while (IsActive && !IsFinished && !_exiting)
            {
                if (_liveCount <= 0)
                {
                    Exit();
                    return;
                }

                float duration = _phase == Phase.Orbit ? _orbitDuration
                    : _phase == Phase.Homing ? _homingDuration : _straightDuration;
                if (_phaseTime >= duration)
                {
                    AdvancePhase();
                    continue;
                }
                if (remaining <= 0f) break;

                float rate = _phase == Phase.Orbit ? _speedMultiplier : 1f;
                float untilBoundary = (duration - _phaseTime) / rate;
                float step = Mathf.Min(remaining, Mathf.Min(MaxStep, untilBoundary));
                if (step <= 0f)
                {
                    _phaseTime = duration;
                    continue;
                }

                _phaseTime = step >= untilBoundary
                    ? duration : Mathf.Min(duration, _phaseTime + step * rate);
                if (_phase == Phase.Orbit) UpdateOrbit();
                else UpdateFlight(step, _phase == Phase.Homing);
                remaining = Mathf.Max(0f, remaining - step);
            }
        }

        public override void Exit()
        {
            if (!IsActive || _exiting) return;
            _exiting = true;
            EndNormalAttackArmor();
            ClearSpheres();
            _target = null;

            if (_rootMotionSaved && _animator != null)
                _animator.applyRootMotion = _previousRootMotion;
            _rootMotionSaved = false;
            _animator = null;

            if (_movementLocked)
            {
                _movementLocked = false;
                if (_enemy != null)
                {
                    _enemy.UnlockMovement();
                    _enemy.SyncMovementLock();
                }
            }
            base.Exit();
            _exiting = false;
        }

        public override void OnParried()
        {
            base.OnParried();
            if (IsActive && _data != null && _data.isParryable) Exit();
        }

        private void ReadSettings()
        {
            _count = Mathf.Clamp(_data.sphereCount, 1, 16);
            _speedMultiplier = Safe(_data.attackSpeed, 1f, 0.01f, 100f);
            _orbitDuration = Safe(_data.orbitDuration, 2f, 0f, 10f);
            _homingDuration = Safe(_data.homingDuration, 3f, 0f, 10f);
            _straightDuration = Safe(_data.straightDuration, 3f, 0.05f, 10f);
            _orbitRadius = Safe(_data.orbitRadius, 0.8f, 0.05f, 5f);
            _orbitAngularSpeed = Safe(_data.orbitAngularSpeed, 180f, -720f, 720f);
            _projectileSpeed = Safe(_data.projectileSpeed, 6f, 0.1f, 30f);
            _radius = Safe(_data.projectileRadius, 0.18f, 0.02f, 1f);
            _turnRadians = Safe(_data.homingTurnSpeed, 180f, 0f, 720f) * Mathf.Deg2Rad;
            _targetHeight = Safe(_data.targetHeight, 1.5f, 0f, 5f);
            _damage = Safe(_data.damage, 0.3f, 0f, 1000f);
            _knockback = Safe(_data.knockbackForce, 0f, 0f, 100f);
            _obstacleMask = _data.obstacleMask.value;
            Vector3 offset = _data.orbitCenterOffset;
            _centerOffset = new Vector3(Safe(offset.x, 0f, -10f, 10f),
                Safe(offset.y, 1.5f, -10f, 10f), Safe(offset.z, -1f, -10f, 10f));
        }

        private void AdvancePhase()
        {
            if (!IsActive || _exiting) return;
            if (_phase == Phase.Straight)
            {
                Exit();
                return;
            }
            if (_phase == Phase.Homing)
            {
                _phase = Phase.Straight;
                _phaseTime = 0f;
                return;
            }

            _phase = Phase.Homing;
            _phaseTime = 0f;
            EndAttackWarning();
            EndTelegraph();
            if (!IsActive || _exiting || !CanContinueAction) return;
            PlaySwingSound();
            bool hasAim = TryAim(out Vector3 aim);
            Sphere[] spheres = _spheres;
            if (spheres == null) return;
            for (int i = 0; i < spheres.Length && IsActive && !_exiting && CanContinueAction; i++)
            {
                Sphere sphere = spheres[i];
                if (sphere == null || !sphere.Alive) continue;
                Vector3 direction = hasAim ? aim - sphere.Position : _enemy.transform.forward;
                if (Finite(direction) && direction.sqrMagnitude > 0.000001f)
                    sphere.Direction = direction.normalized;
                MoveSphere(sphere, sphere.Position, true);
            }
            EndNormalAttackArmor();
            if (IsActive && _liveCount <= 0) Exit();
        }

        private Vector3 OrbitPosition(int index, float time)
        {
            float angle = Mathf.PI * 2f * index / _count
                + time * _orbitAngularSpeed * Mathf.Deg2Rad;
            Vector3 offset = _centerOffset + new Vector3(Mathf.Cos(angle) * _orbitRadius,
                Mathf.Sin(angle) * _orbitRadius, 0f);
            return _enemy.transform.position + _enemy.transform.rotation * offset;
        }

        private void UpdateOrbit()
        {
            Sphere[] spheres = _spheres;
            if (spheres == null) return;
            for (int i = 0; i < spheres.Length && IsActive && !_exiting; i++)
            {
                Sphere sphere = spheres[i];
                if (sphere != null && sphere.Alive)
                    MoveSphere(sphere, OrbitPosition(i, _phaseTime), false);
            }
        }

        private void UpdateFlight(float delta, bool homing)
        {
            Vector3 aim = Vector3.zero;
            bool hasAim = homing && TryAim(out aim);
            Sphere[] spheres = _spheres;
            if (spheres == null) return;
            for (int i = 0; i < spheres.Length && IsActive && !_exiting; i++)
            {
                Sphere sphere = spheres[i];
                if (sphere == null || !sphere.Alive) continue;
                if (hasAim)
                {
                    Vector3 desired = aim - sphere.Position;
                    if (Finite(desired) && desired.sqrMagnitude > 0.000001f)
                        sphere.Direction = Vector3.RotateTowards(sphere.Direction,
                            desired.normalized, _turnRadians * delta, 0f).normalized;
                }
                MoveSphere(sphere, sphere.Position + sphere.Direction * (_projectileSpeed * delta), true);
            }
        }

        private bool TryAim(out Vector3 aim)
        {
            aim = Vector3.zero;
            if (_target == null || !_target.gameObject.activeInHierarchy) return false;
            aim = _target.position + Vector3.up * _targetHeight;
            return Finite(aim);
        }

        private void MoveSphere(Sphere sphere, Vector3 destination, bool canDamage)
        {
            if (!sphere.Alive || !IsActive || _exiting) return;
            if (sphere.Visual == null || !sphere.Visual.activeInHierarchy
                || !Finite(sphere.Position) || !Finite(destination))
            {
                Retire(sphere);
                return;
            }

            Vector3 movement = destination - sphere.Position;
            float distance = movement.magnitude;
            Vector3 direction = distance > 0.000001f ? movement / distance : Vector3.zero;
            if (!TryContact(sphere.Position, direction, distance, canDamage,
                out Collider collider, out IDamageable target, out float travel))
            {
                sphere.Position = destination;
                sphere.Visual.transform.position = destination;
                return;
            }

            sphere.Position += direction * travel;
            sphere.Visual.transform.position = sphere.Position;
            Retire(sphere);
            if (!canDamage || target == null || collider == null || !IsActive || _exiting
                || !sphere.HitTargets.Add(target) || _enemy.EnemyStat == null) return;

            EnemyStat stat = _enemy.EnemyStat;
            HitInfo info = new HitInfo(
                _damage * Safe(stat.GetStat(EnemyStatType.ATK), 0f, 0f, 1000000000f),
                _enemy.Element,
                Safe(stat.GetStat(EnemyStatType.CriticalChance), 0f, 0f, 1f),
                Safe(stat.GetStat(EnemyStatType.CriticalDamage), 1f, 0f, 1000f),
                power: sphere.Direction * _knockback,
                staggerResistLevel: (StaggerResistLevel)Mathf.Clamp((int)_data.staggerLevel, 0, 4),
                isParryable: _data.isParryable, source: this, impactSound: ResolveHitSound());
            BroadcastHit();
            if (!IsActive || IsFinished || !CanContinueAction) return;
            DamageResult result = DamageManager.Apply(info, target, collider.gameObject);
            if (result.Outcome == DamageOutcome.Parried) OnParried();
        }

        private bool TryContact(Vector3 origin, Vector3 direction, float distance, bool canDamage,
            out Collider collider, out IDamageable target, out float travel)
        {
            collider = null;
            target = null;
            travel = 0f;
            bool bestEnvironment = false;
            Collider[] overlaps = _overlapBuffer;
            int overlapCount = Physics.OverlapSphereNonAlloc(origin, _radius, overlaps,
                Physics.AllLayers, QueryTriggerInteraction.Collide);
            if (overlapCount >= overlaps.Length)
            {
                overlaps = Physics.OverlapSphere(origin, _radius, Physics.AllLayers,
                    QueryTriggerInteraction.Collide);
                overlapCount = overlaps.Length;
            }
            for (int i = 0; i < overlapCount; i++)
            {
                Collider candidate = overlaps[i];
                if (!Classify(candidate, canDamage, out IDamageable receiver, out bool environment)) continue;
                if (collider == null || (environment && !bestEnvironment))
                {
                    collider = candidate;
                    target = receiver;
                    bestEnvironment = environment;
                }
            }
            if (collider != null) return true;
            if (distance <= 0.000001f) return false;

            RaycastHit[] hits = _castBuffer;
            int hitCount = Physics.SphereCastNonAlloc(origin, _radius, direction, hits, distance,
                Physics.AllLayers, QueryTriggerInteraction.Collide);
            if (hitCount >= hits.Length)
            {
                hits = Physics.SphereCastAll(origin, _radius, direction, distance,
                    Physics.AllLayers, QueryTriggerInteraction.Collide);
                hitCount = hits.Length;
            }
            travel = distance;
            for (int i = 0; i < hitCount; i++)
            {
                Collider candidate = hits[i].collider;
                float hitDistance = hits[i].distance;
                if (!Finite(hitDistance) || hitDistance < 0f || hitDistance > distance
                    || !Classify(candidate, canDamage, out IDamageable receiver, out bool environment)) continue;
                if (collider == null || hitDistance < travel - 0.0001f
                    || (Mathf.Abs(hitDistance - travel) <= 0.0001f && environment && !bestEnvironment))
                {
                    collider = candidate;
                    target = receiver;
                    travel = hitDistance;
                    bestEnvironment = environment;
                }
            }
            return collider != null;
        }

        private bool Classify(Collider collider, bool canDamage,
            out IDamageable target, out bool environment)
        {
            target = null;
            environment = false;
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) return false;
            Transform hitTransform = collider.transform;
            if (hitTransform.IsChildOf(_enemy.transform)
                || collider.GetComponentInParent<Enemy>() == _enemy) return false;

            int layerBit = 1 << collider.gameObject.layer;
            bool player = (_playerLayer.value & layerBit) != 0
                || (_target != null && hitTransform.IsChildOf(_target));
            if (player)
            {
                if (!canDamage) return false;
                target = collider.GetComponentInParent<IDamageable>();
                return !ReferenceEquals(target, _enemy);
            }
            if (collider.isTrigger || (_obstacleMask & layerBit) == 0) return false;
            environment = true;
            return true;
        }

        private void Retire(Sphere sphere)
        {
            if (sphere == null || !sphere.Alive) return;
            sphere.Alive = false;
            _liveCount = Mathf.Max(0, _liveCount - 1);
            if (sphere.Visual != null)
            {
                sphere.Visual.SetActive(false);
                Object.Destroy(sphere.Visual);
            }
            sphere.Visual = null;
        }

        private void ClearSpheres()
        {
            Sphere[] spheres = _spheres;
            _spheres = null;
            if (spheres != null)
                for (int i = 0; i < spheres.Length; i++) Retire(spheres[i]);
            _liveCount = 0;
        }

        private bool UsableOwner()
        {
            return (!IsActive || CanContinueAction) && _enemy != null && _enemy.isActiveAndEnabled
                && _enemy.EnemyStat != null && !_enemy.EnemyStat.IsDead;
        }

        private bool SafeBaseTiming()
        {
            return _data != null && Finite(_data.attackSpeed)
                && _data.attackSpeed >= 0.01f && _data.attackSpeed <= 100f;
        }

        private float Safe(float value, float fallback, float minimum, float maximum)
        {
            return Mathf.Clamp(Finite(value) ? value : fallback, minimum, maximum);
        }

        private bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private bool Finite(Vector3 value)
        {
            return Finite(value.x) && Finite(value.y) && Finite(value.z);
        }
    }
}
