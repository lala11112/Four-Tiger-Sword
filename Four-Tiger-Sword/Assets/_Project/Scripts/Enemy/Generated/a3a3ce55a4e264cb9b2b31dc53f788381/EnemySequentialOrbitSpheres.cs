using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Baekho.Generated.a3a3ce55a4e264cb9b2b31dc53f788381
{
    public sealed class EnemySequentialOrbitSpheres : EnemyAction
    {
        private sealed class Sphere
        {
            public GameObject Root;
            public Transform Target;
            public Vector3 Position;
            public Vector3 Direction;
            public float Age;
            public bool Launched;
            public bool Dead;
            public readonly HashSet<IDamageable> HitTargets = new HashSet<IDamageable>();
        }

        private readonly MonsterSequentialOrbitSpheresSO _data;
        private readonly Collider[] _overlapBuffer = new Collider[64];
        private readonly RaycastHit[] _castBuffer = new RaycastHit[64];
        private Sphere[] _spheres;
        private Transform _target;
        private NavMeshAgent _nav;
        private Animator _animator;
        private bool _exited;
        private bool _movementLocked;
        private bool _navSaved;
        private bool _rootMotionSaved;
        private bool _previousUpdateRotation;
        private bool _previousRootMotion;
        private int _liveCount;
        private int _obstacleMask;
        private float _attackSpeed;
        private float _telegraphDuration;
        private float _orbitDuration;
        private float _shotInterval;
        private float _homingDuration;
        private float _lifetime;
        private float _orbitRadius;
        private float _angularSpeed;
        private float _projectileSpeed;
        private float _projectileRadius;
        private float _turnSpeed;
        private float _targetHeight;
        private float _damage;
        private float _knockback;
        private float _prefabScale;
        private Vector3 _orbitCenter;
        private StaggerResistLevel _stagger;

        public EnemySequentialOrbitSpheres(MonsterSequentialOrbitSpheresSO data, Enemy enemy)
            : base(data, enemy)
        {
            _data = data;
        }

        public override void Enter()
        {
            if (IsActive) Exit();
            _exited = false;
            _liveCount = 0;
            ReadSettings();
            base.Enter();
            if (!IsActive || _exited) return;
            if (!OwnerCanContinue())
            {
                Exit();
                return;
            }

            _enemy.SetAnimationSpeed(_attackSpeed);
            _target = _enemy.DetectedTarget;
            _enemy.LockMovement();
            _movementLocked = true;
            _nav = _enemy.GetComponent<NavMeshAgent>();
            if (_nav != null)
            {
                _previousUpdateRotation = _nav.updateRotation;
                _navSaved = true;
                _nav.updateRotation = false;
                if (_nav.enabled && _nav.isOnNavMesh)
                {
                    _nav.ResetPath();
                    _nav.velocity = Vector3.zero;
                }
            }

            _animator = _enemy.Animator;
            if (_animator != null)
            {
                _previousRootMotion = _animator.applyRootMotion;
                _rootMotionSaved = true;
                _animator.applyRootMotion = false;
                if (!string.IsNullOrWhiteSpace(_data.animName)
                    && _animator.runtimeAnimatorController != null && _animator.layerCount > 0
                    && _animator.HasState(0, Animator.StringToHash(_data.animName)))
                    _animator.CrossFade(_data.animName, 0.05f, 0);
            }

            _spheres = new Sphere[Mathf.Clamp(_data.sphereCount, 1, 16)];
            for (int i = 0; i < _spheres.Length; i++)
            {
                var sphere = new Sphere();
                _spheres[i] = sphere;
                _liveCount++;
                sphere.Position = OrbitPosition(i, 0f);
                sphere.Direction = _enemy.transform.forward;
                CreateVisual(sphere);
            }
            if (_telegraphDuration <= 0f) EndTelegraph();
        }

        public override void Update()
        {
            if (!IsActive || IsFinished || _exited) return;
            if (!OwnerCanContinue())
            {
                Exit();
                return;
            }

            float previous = _timer;
            base.Update();
            if (!IsActive || IsFinished || _exited) return;
            float delta = Safe(_enemy.ActionDeltaTime, 0f, 60f, 0f);
            // Keep shared skill assets untouched while enforcing a finite action timeline.
            _timer = previous + delta * _attackSpeed;
            if (_timer >= _telegraphDuration) EndTelegraph();
            if (!IsActive || IsFinished || delta <= 0f) return;

            if (_target == null || !_target.gameObject.activeInHierarchy)
                _target = _enemy.DetectedTarget;

            Sphere[] spheres = _spheres;
            if (spheres == null) return;
            for (int i = 0; i < spheres.Length; i++)
            {
                if (!IsActive || IsFinished || _exited) return;
                Sphere sphere = spheres[i];
                if (sphere == null || sphere.Dead) continue;
                if (sphere.Root == null || !sphere.Root.activeInHierarchy)
                {
                    Retire(sphere);
                    continue;
                }

                float flightDelta = delta;
                if (!sphere.Launched)
                {
                    float launchTime = _orbitDuration + i * _shotInterval;
                    if (_timer < launchTime)
                    {
                        sphere.Position = OrbitPosition(i, _timer);
                        sphere.Root.transform.SetPositionAndRotation(sphere.Position, _enemy.transform.rotation);
                        continue;
                    }
                    sphere.Position = OrbitPosition(i, launchTime);
                    flightDelta = Mathf.Clamp((_timer - Mathf.Max(previous, launchTime)) / _attackSpeed, 0f, delta);
                    Launch(sphere);
                    if (!IsActive || IsFinished || _exited || !CanContinueAction) return;
                    if (i == spheres.Length - 1) EndNormalAttackArmor();
                }
                if (!sphere.Dead) Advance(sphere, flightDelta);
            }

            if (IsActive && !_exited && _liveCount == 0)
            {
                EndTelegraph();
                IsFinished = true;
            }
        }

        public override void Exit()
        {
            if (_exited) return;
            _exited = true;
            EndNormalAttackArmor();
            IsFinished = true;
            Sphere[] spheres = _spheres;
            _spheres = null;
            if (spheres != null)
                for (int i = 0; i < spheres.Length; i++) Retire(spheres[i]);
            _liveCount = 0;
            _target = null;

            if (_navSaved)
            {
                _navSaved = false;
                if (_nav != null) _nav.updateRotation = _previousUpdateRotation;
            }
            if (_rootMotionSaved)
            {
                _rootMotionSaved = false;
                if (_animator != null) _animator.applyRootMotion = _previousRootMotion;
            }
            if (_movementLocked)
            {
                _movementLocked = false;
                if (_enemy != null) _enemy.UnlockMovement();
            }
            base.Exit();
            if (_enemy != null) _enemy.SyncMovementLock();
        }

        public override void OnParried()
        {
            if (!IsActive || _exited || !_data.isParryable) return;
            base.OnParried();
            Exit();
        }

        private bool OwnerCanContinue()
        {
            return CanContinueAction && _enemy != null && _enemy.isActiveAndEnabled
                && _enemy.EnemyStat != null && !_enemy.EnemyStat.IsDead
                && !_enemy.IsHurt && !_enemy.IsGroggy && !_enemy.IsRoot
                && !_enemy.IsKnockbacking && !_enemy.IsSilenced && !_enemy.IsSelfDestructing;
        }

        private void ReadSettings()
        {
            _attackSpeed = Safe(_data.attackSpeed, 0.01f, 20f, 1f);
            _telegraphDuration = Safe(_data.telegraphDuration, 0f, 60f, 2f);
            _orbitDuration = Safe(_data.orbitDuration, 0f, 10f, 2f);
            _shotInterval = Safe(_data.shotInterval, 0.01f, 2f, 0.2f);
            _homingDuration = Safe(_data.homingDuration, 0f, 10f, 3f);
            _lifetime = _homingDuration + Safe(_data.straightDuration, 0.05f, 10f, 3f);
            _orbitRadius = Safe(_data.orbitRadius, 0.05f, 5f, 0.8f);
            _angularSpeed = Safe(_data.orbitAngularSpeed, -720f, 720f, 180f);
            _projectileSpeed = Safe(_data.projectileSpeed, 0.1f, 30f, 6f);
            _projectileRadius = Safe(_data.projectileRadius, 0.02f, 1f, 0.18f);
            _turnSpeed = Safe(_data.homingTurnSpeed, 0f, 720f, 180f) * Mathf.Deg2Rad;
            _targetHeight = Safe(_data.targetHeight, 0f, 5f, 1.5f);
            _damage = Safe(_data.damage, 0f, 1000f, 0.3f);
            _knockback = Safe(_data.knockbackForce, 0f, 100f, 0f);
            _prefabScale = Safe(_data.projectilePrefabScale, 0.01f, 10f, 1f);
            _obstacleMask = _data.obstacleMask.value;
            _stagger = (StaggerResistLevel)Mathf.Clamp((int)_data.staggerLevel, 0, 4);
            _orbitCenter = new Vector3(
                Safe(_data.orbitCenterOffset.x, -10f, 10f, 0f),
                Safe(_data.orbitCenterOffset.y, -10f, 10f, 1.5f),
                Safe(_data.orbitCenterOffset.z, -10f, 10f, -1f));
        }

        private float Safe(float value, float minimum, float maximum, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? fallback : Mathf.Clamp(value, minimum, maximum);
        }

        private bool Finite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private Vector3 OrbitPosition(int index, float time)
        {
            float angle = (360f * index / _spheres.Length + Mathf.Repeat(_angularSpeed * time, 360f)) * Mathf.Deg2Rad;
            Vector3 offset = _orbitCenter + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * _orbitRadius;
            return GetHitCenter(offset);
        }

        private void CreateVisual(Sphere sphere)
        {
            var root = new GameObject("Sequential Orbit Sphere");
            root.SetActive(false);
            sphere.Root = root;
            root.transform.SetPositionAndRotation(sphere.Position, _enemy.transform.rotation);

            GameObject visual;
            if (_data.projectilePrefab != null)
            {
                // An inactive parent prevents prefab OnEnable logic during setup.
                visual = Object.Instantiate(_data.projectilePrefab, root.transform, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localScale *= _prefabScale;
            }
            else
            {
                visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one * (_projectileRadius * 2f);
            }

            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
            {
                body.detectCollisions = false;
                body.useGravity = false;
                body.isKinematic = true;
            }
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null) behaviour.enabled = false;
            foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
                animator.applyRootMotion = false;
            foreach (ParticleSystem particles in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particles.main;
                main.stopAction = ParticleSystemStopAction.None;
            }

            StageTransientObject.Track(root);
            visual.SetActive(true);
            root.SetActive(true);
        }

        private void Launch(Sphere sphere)
        {
            EndAttackWarning();
            sphere.Launched = true;
            sphere.Age = 0f;
            sphere.Target = _target;
            Vector3 direction = _enemy.transform.forward;
            if (sphere.Target != null && sphere.Target.gameObject.activeInHierarchy)
            {
                Vector3 aim = sphere.Target.position + Vector3.up * _targetHeight - sphere.Position;
                if (Finite(aim) && aim.sqrMagnitude > 0.000001f) direction = aim.normalized;
            }
            sphere.Direction = direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector3.forward;
            UpdateVisual(sphere);
            EndTelegraph();
            if (!IsActive || IsFinished || _exited || !CanContinueAction) return;
            PlaySound(_data.SwingAudio, sphere.Position);
            if (FindOverlap(sphere, out Collider collider, out IDamageable receiver))
                Impact(sphere, collider, receiver);
        }

        private void Advance(Sphere sphere, float delta)
        {
            float remaining = Mathf.Min(delta, Mathf.Max(0f, _lifetime - sphere.Age));
            float homingPart = Mathf.Min(remaining, Mathf.Max(0f, _homingDuration - sphere.Age));
            if (homingPart > 0f)
            {
                Travel(sphere, homingPart, true);
                if (sphere.Dead || !IsActive || _exited) return;
                sphere.Age += homingPart;
                remaining -= homingPart;
            }
            if (remaining > 0f)
            {
                Travel(sphere, remaining, false);
                if (sphere.Dead || !IsActive || _exited) return;
                sphere.Age += remaining;
            }
            if (sphere.Age >= _lifetime - 0.000001f) Retire(sphere);
        }

        private void Travel(Sphere sphere, float duration, bool homing)
        {
            int steps = Mathf.Clamp(Mathf.CeilToInt(duration * 30f), 1, 128);
            float step = duration / steps;
            for (int i = 0; i < steps; i++)
            {
                if (sphere.Dead || !IsActive || IsFinished || _exited) return;
                if (sphere.Root == null || !sphere.Root.activeInHierarchy)
                {
                    Retire(sphere);
                    return;
                }
                if (homing && sphere.Target != null && sphere.Target.gameObject.activeInHierarchy)
                {
                    Vector3 desired = sphere.Target.position + Vector3.up * _targetHeight - sphere.Position;
                    if (Finite(desired) && desired.sqrMagnitude > 0.000001f)
                        sphere.Direction = Vector3.RotateTowards(sphere.Direction, desired.normalized, _turnSpeed * step, 0f).normalized;
                }
                MoveSphere(sphere, _projectileSpeed * step);
            }
        }

        private void MoveSphere(Sphere sphere, float distance)
        {
            if (FindOverlap(sphere, out Collider collider, out IDamageable receiver))
            {
                Impact(sphere, collider, receiver);
                return;
            }
            if (FindSweep(sphere, distance, out collider, out receiver, out float hitDistance))
            {
                sphere.Position += sphere.Direction * hitDistance;
                UpdateVisual(sphere);
                Impact(sphere, collider, receiver);
                return;
            }
            sphere.Position += sphere.Direction * distance;
            UpdateVisual(sphere);
        }

        private void UpdateVisual(Sphere sphere)
        {
            if (sphere.Root == null) return;
            Vector3 up = Mathf.Abs(Vector3.Dot(sphere.Direction, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            sphere.Root.transform.SetPositionAndRotation(sphere.Position, Quaternion.LookRotation(sphere.Direction, up));
        }

        private bool Classify(Collider collider, Sphere sphere, out IDamageable receiver)
        {
            receiver = null;
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) return false;
            Transform hit = collider.transform;
            if (hit == _enemy.transform || hit.IsChildOf(_enemy.transform)) return false;
            IDamageable candidate = collider.GetComponentInParent<IDamageable>();
            if (ReferenceEquals(candidate, _enemy)) return false;

            bool player = (_playerLayer.value & (1 << collider.gameObject.layer)) != 0;
            if (candidate is Component component && component != null)
            {
                player |= (_playerLayer.value & (1 << component.gameObject.layer)) != 0;
                if (sphere.Target != null)
                    player |= sphere.Target == component.transform || sphere.Target.IsChildOf(component.transform);
            }
            if (sphere.Target != null)
                player |= hit == sphere.Target || hit.IsChildOf(sphere.Target);
            if (player && candidate != null)
            {
                receiver = candidate;
                return true;
            }
            return !collider.isTrigger && (_obstacleMask & (1 << collider.gameObject.layer)) != 0;
        }

        private bool FindOverlap(Sphere sphere, out Collider selected, out IDamageable receiver)
        {
            selected = null;
            receiver = null;
            int count = Physics.OverlapSphereNonAlloc(sphere.Position, _projectileRadius, _overlapBuffer,
                ~0, QueryTriggerInteraction.Collide);
            Collider[] colliders = _overlapBuffer;
            if (count == _overlapBuffer.Length)
            {
                colliders = Physics.OverlapSphere(sphere.Position, _projectileRadius, ~0, QueryTriggerInteraction.Collide);
                count = colliders.Length;
            }

            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider collider = colliders[i];
                if (!Classify(collider, sphere, out IDamageable candidate)) continue;
                // A projectile already overlapping a wall must not damage through it.
                if (candidate == null)
                {
                    selected = collider;
                    receiver = null;
                    return true;
                }
                float distance = (collider.ClosestPoint(sphere.Position) - sphere.Position).sqrMagnitude;
                if (selected == null || distance < nearest)
                {
                    nearest = distance;
                    selected = collider;
                    receiver = candidate;
                }
            }
            return selected != null;
        }

        private bool FindSweep(Sphere sphere, float distance, out Collider selected,
            out IDamageable receiver, out float hitDistance)
        {
            selected = null;
            receiver = null;
            hitDistance = distance;
            if (distance <= 0f) return false;
            int count = Physics.SphereCastNonAlloc(sphere.Position, _projectileRadius, sphere.Direction,
                _castBuffer, distance, ~0, QueryTriggerInteraction.Collide);
            RaycastHit[] hits = _castBuffer;
            if (count == _castBuffer.Length)
            {
                hits = Physics.SphereCastAll(sphere.Position, _projectileRadius, sphere.Direction,
                    distance, ~0, QueryTriggerInteraction.Collide);
                count = hits.Length;
            }
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (!Classify(hit.collider, sphere, out IDamageable candidate)) continue;
                if (float.IsNaN(hit.distance) || float.IsInfinity(hit.distance)) continue;
                float current = Mathf.Clamp(hit.distance, 0f, distance);
                bool wallTie = candidate == null && receiver != null && Mathf.Abs(current - hitDistance) <= 0.0001f;
                if (selected == null || current < hitDistance || wallTie)
                {
                    selected = hit.collider;
                    receiver = candidate;
                    hitDistance = current;
                }
            }
            return selected != null;
        }

        private void Impact(Sphere sphere, Collider collider, IDamageable receiver)
        {
            if (sphere.Dead) return;
            bool shouldDamage = collider != null && receiver != null && sphere.HitTargets.Add(receiver);
            GameObject targetObject = collider != null ? collider.gameObject : null;
            Vector3 direction = sphere.Direction;
            Retire(sphere);
            if (!shouldDamage || !IsActive || IsFinished || _exited) return;
            if (!OwnerCanContinue())
            {
                Exit();
                return;
            }

            EnemyStat stats = _enemy.EnemyStat;
            var info = new HitInfo(
                _damage * Safe(stats.GetStat(EnemyStatType.ATK), 0f, 1000000f, 0f),
                _enemy.Element,
                Safe(stats.GetStat(EnemyStatType.CriticalChance), 0f, 1f, 0f),
                Safe(stats.GetStat(EnemyStatType.CriticalDamage), 0f, 100f, 1f),
                power: direction * _knockback,
                staggerResistLevel: _stagger,
                isParryable: _data.isParryable,
                source: this,
                impactSound: ResolveHitSound());
            BroadcastHit();
            if (!IsActive || IsFinished || !CanContinueAction) return;
            DamageResult result = DamageManager.Apply(info, receiver, targetObject);
            if (result.Outcome == DamageOutcome.Parried) OnParried();
            if (IsActive && !_exited && !OwnerCanContinue()) Exit();
        }

        private void Retire(Sphere sphere)
        {
            if (sphere == null || sphere.Dead) return;
            sphere.Dead = true;
            _liveCount = Mathf.Max(0, _liveCount - 1);
            GameObject root = sphere.Root;
            sphere.Root = null;
            sphere.Target = null;
            if (root != null)
            {
                root.SetActive(false);
                Object.Destroy(root);
            }
        }
    }
}
