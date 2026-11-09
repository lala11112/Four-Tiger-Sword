using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>The optional airborne presentation and landing of a boss's one-time phase burst.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BossPhaseVisuals))]
public sealed class BossPhaseEntryFlight : MonoBehaviour
{
    [SerializeField] private BossPhaseBurstSkillSO _burstSkill;
    [SerializeField, Min(.01f)] private float _ascentSeconds = .65f;
    [SerializeField, Min(0f)] private float _hiddenSeconds = .8f;
    [SerializeField, Min(.01f), Tooltip("화면 밖에서 착지까지의 급강하 시간 (초)")]
    private float _descentSeconds = .25f;
    [SerializeField, Min(1f)] private float _minimumHeight = 12f;
    [SerializeField, Min(.1f)] private float _landingStandOff = 1.5f;

    private static readonly int Lift = Animator.StringToHash("MobilityJumpLift");
    private static readonly int SlamAir = Animator.StringToHash("MobilitySlamAir");
    private static readonly int SlamLand = Animator.StringToHash("MobilitySlamLand");
    private const float SlamImpactNormalized = .42f;
    private readonly List<(Renderer Renderer, bool ForceOff)> _hiddenRenderers = new();
    private readonly List<Collider> _disabledColliders = new();
    private Boss _boss;
    private BossPhaseVisuals _visuals;
    private NavMeshAgent _agent;
    private Transform _pivot;
    private BossMobilityNavigation.Context _navigation;
    private BossPhaseBurstAction _action;
    private Vector3 _restingPivot, _launchFloor, _lockedTarget, _landingFloor;
    private float _preludeElapsed, _height, _originalAnimationSpeed;
    private bool _warningStarted, _movedForWarning;

    public float AscentSeconds => _ascentSeconds;
    public float HiddenSeconds => _hiddenSeconds;
    public float DescentSeconds => _descentSeconds;
    public bool PreludeComplete => _action != null && _preludeElapsed >= _ascentSeconds + _hiddenSeconds;
    public bool VisualHidden => _hiddenRenderers.Count != 0;
    public Vector3 Center => _landingFloor;

    private void Awake()
    {
        _boss = GetComponent<Boss>();
        _visuals = GetComponent<BossPhaseVisuals>();
        _agent = GetComponent<NavMeshAgent>();
        _pivot = GetComponent<SwordRobotBossPresentation>()?.visualPivot;
    }

    public bool ShouldUse(MonsterSkillData skill) => isActiveAndEnabled && _burstSkill != null && skill == _burstSkill;
    internal bool IsRunning(BossPhaseBurstAction action) => isActiveAndEnabled && _action == action;

    internal bool DeferVisualSwap(int phaseIndex)
        => phaseIndex == 1 && isActiveAndEnabled && _burstSkill != null && _pivot != null
            && _visuals != null && _visuals.ActiveVisualPhase != 1;

    public bool TryBegin(BossPhaseBurstAction action)
    {
        if (_action != null || action == null || !ShouldUse(action.SkillData)
            || _boss == null || _visuals == null || _boss.PhaseIndex != 1
            || _boss.CurrentAction != action || _visuals.ActiveVisualPhase != 0) return false;
        if (!CanPrepare() || !TryPrepareLanding())
        {
            _visuals.ApplyPhase(1);
            _navigation = null;
            return false;
        }

        _action = action;
        _restingPivot = _pivot.localPosition;
        _originalAnimationSpeed = _boss.ActorClock != null ? _boss.ActorClock.AnimationSpeed : _boss.Animator.speed;
        _preludeElapsed = 0f;
        _height = GetOffscreenHeight();
        foreach (var collider in GetComponentsInChildren<Collider>(true))
        {
            if (!collider.enabled || collider.GetComponentInParent<Enemy>(true) != _boss) continue;
            _disabledColliders.Add(collider);
            collider.enabled = false;
        }
        _boss.SetAnimationSpeed(0f);
        PoseLift(0f);
        return true;
    }

    private bool CanPrepare()
        => _boss != null && !_boss.IsDead && _boss.CombatTarget != null && _agent != null && _agent.enabled
            && _agent.isOnNavMesh && _pivot != null && _boss.Animator != null
            && _boss.Animator.runtimeAnimatorController != null && _boss.Animator.HasState(0, Lift)
            && _boss.Animator.HasState(0, SlamAir) && _boss.Animator.HasState(0, SlamLand)
            && _visuals.phaseTwo?.root != null && _visuals.phaseTwo.animator != null
            && BossCombatValidation.Positive(_ascentSeconds) && BossCombatValidation.Nonnegative(_hiddenSeconds)
            && BossCombatValidation.Positive(_descentSeconds)
            && BossCombatValidation.Positive(_minimumHeight) && BossCombatValidation.Positive(_landingStandOff);

    private bool TryPrepareLanding()
    {
        var target = _boss.CombatTarget;
        var player = target.GetComponentInParent<PlayerController>();
        _navigation = new BossMobilityNavigation.Context(_boss, _agent, player != null ? player.transform : target,
            LayerMask.GetMask("Player"));
        _lockedTarget = target.position;
        return _navigation.Floor(_agent.nextPosition - Vector3.up * _navigation.BaseOffset, out _launchFloor)
            && TryChooseLanding(out _landingFloor);
    }

    private bool TryChooseLanding(out Vector3 floor)
    {
        var approach = BossMobilityNavigation.Flat(_lockedTarget - _launchFloor);
        float distance = approach.magnitude;
        if (distance > _landingStandOff)
        {
            Vector3 closest = _launchFloor + approach.normalized * (distance - _landingStandOff);
            for (int step = 0; step < 6; step++)
            {
                // Try the closest position first, then shorter approaches along the same line.
                Vector3 point = Vector3.Lerp(closest, _launchFloor, step / 6f);
                point.y = _lockedTarget.y;
                if (TryLanding(point, out floor)) return true;
                point.y = _launchFloor.y;
                if (TryLanding(point, out floor)) return true;
            }
        }
        return TryLanding(_launchFloor, out floor);
    }

    private bool TryLanding(Vector3 desired, out Vector3 floor)
    {
        floor = default;
        if (!_navigation.Available || !_navigation.Floor(desired, out floor)
            || !_navigation.Clear(floor + Vector3.up * _navigation.BaseOffset, floor.y)) return false;
        var path = new NavMeshPath();
        return NavMesh.CalculatePath(_launchFloor, floor, _navigation.Filter, path)
            && path.status == NavMeshPathStatus.PathComplete;
    }

    private float GetOffscreenHeight()
    {
        float height = _minimumHeight;
        var camera = Camera.main;
        if (camera == null) return height;
        var planes = GeometryUtility.CalculateFrustumPlanes(camera);
        // The lift must clear the whole silhouette, including a raised weapon.
        for (int i = 0; i < 6; i++)
        {
            var bounds = new Bounds(_pivot.position + Vector3.up * (height + 1.5f), new Vector3(6f, 5f, 6f));
            if (!GeometryUtility.TestPlanesAABB(planes, bounds)) break;
            height += 4f;
        }
        return height;
    }

    public float TickPrelude(float deltaTime)
    {
        if (_action == null || deltaTime <= 0f) return 0f;
        float previous = _preludeElapsed;
        _preludeElapsed = Mathf.Min(_ascentSeconds + _hiddenSeconds, previous + deltaTime);
        if (previous < _ascentSeconds)
        {
            float progress = Mathf.Clamp01(_preludeElapsed / _ascentSeconds);
            SetHeight(_height * (1f - (1f - progress) * (1f - progress)));
            PoseLift(progress);
            if (progress >= 1f) HideRenderers();
        }
        return Mathf.Max(0f, deltaTime - (_preludeElapsed - previous));
    }

    public bool TryBeginWarning()
    {
        if (!PreludeComplete || _warningStarted || _boss.CurrentAction != _action) return false;
        if (!TryWarp(_landingFloor) && (!TryChooseLanding(out var fallback) || !TryWarp(fallback))) return false;

        _warningStarted = true;
        SetHeight(_height);
        _visuals.ApplyPhase(1);
        _boss.SetAnimationSpeed(0f);
        SamplePose(SlamAir, 0f);
        return true;
    }

    private bool TryWarp(Vector3 candidate)
    {
        if (!TryLanding(candidate, out var floor)) return false;
        var position = floor + Vector3.up * _navigation.BaseOffset;
        if ((position - _agent.nextPosition).sqrMagnitude > .0001f && !_agent.Warp(position)) return false;
        _movedForWarning = true;
        _landingFloor = _agent.nextPosition - Vector3.up * _navigation.BaseOffset;
        _boss.transform.position = _agent.nextPosition;
        _agent.ResetPath();
        _agent.velocity = Vector3.zero;
        _boss.SyncMovementLock();
        return true;
    }

    public void TickDescent(float elapsed, float chargeDuration)
    {
        if (!_warningStarted) return;
        float duration = Mathf.Min(_descentSeconds, chargeDuration);
        float releaseAt = chargeDuration - duration;
        if (elapsed < releaseAt) return;
        if (VisualHidden) RestoreRenderers();
        float descent = Mathf.Clamp01((elapsed - releaseAt) / duration);
        // Accelerate into the floor instead of easing to a floating stop. The actual aerial
        // slam clip reaches its downward strike on the same actor-clock frame as the burst.
        SetHeight(_height * (1f - descent * descent));
        SamplePose(SlamLand, descent * SlamImpactNormalized);
    }

    public bool TryLand()
    {
        if (!_warningStarted || _boss.CurrentAction != _action || !LandingStillValid()) return false;
        _pivot.localPosition = _restingPivot;
        SamplePose(SlamLand, SlamImpactNormalized);
        return true;
    }

    private bool LandingStillValid() => TryLanding(_landingFloor, out var floor)
        && (floor - _landingFloor).sqrMagnitude <= .01f;

    private void RestoreSafeGround()
    {
        if (!_navigation.Available || LandingStillValid() || TryWarp(_launchFloor)) return;
        Vector3 originalLanding = _landingFloor;
        bool differentOrigins = (originalLanding - _launchFloor).sqrMagnitude > .01f;
        // Search both ends in a fixed order. The published burst is already cancelled.
        for (float radius = 2f; radius <= 12f; radius += 2f)
        {
            for (int direction = 0; direction < 8; direction++)
            {
                var offset = Quaternion.Euler(0f, direction * 45f, 0f) * Vector3.forward * radius;
                if (TryWarp(_launchFloor + offset) || differentOrigins && TryWarp(originalLanding + offset)) return;
            }
        }
    }

    public void FinishLanding() => RestoreColliders();

    public void EndFlight(bool cancelled)
    {
        if (_action == null) return;
        if (cancelled && _movedForWarning) RestoreSafeGround();
        if (_pivot != null) _pivot.localPosition = _restingPivot;
        RestoreRenderers();
        RestoreColliders();
        if (_boss != null && _boss.PhaseIndex == 1) _visuals.ApplyPhase(1);
        if (!_warningStarted && _boss != null) _boss.SetAnimationSpeed(_originalAnimationSpeed);
        _action = null;
        _navigation = null;
        _warningStarted = _movedForWarning = false;
    }

    private void SetHeight(float height)
        => _pivot.localPosition = _restingPivot + _pivot.parent.InverseTransformVector(Vector3.up * height);

    private void PoseLift(float normalizedTime) => SamplePose(Lift, normalizedTime);

    private void SamplePose(int state, float normalizedTime)
    {
        var animator = _boss.Animator;
        animator.Play(state, 0, normalizedTime);
        animator.Update(0f);
    }

    private void HideRenderers()
    {
        foreach (var renderer in _pivot.GetComponentsInChildren<Renderer>(true))
        {
            _hiddenRenderers.Add((renderer, renderer.forceRenderingOff));
            if (renderer is TrailRenderer trail)
            {
                trail.emitting = false;
                trail.Clear();
            }
            renderer.forceRenderingOff = true;
        }
    }

    private void RestoreRenderers()
    {
        foreach (var state in _hiddenRenderers)
            if (state.Renderer != null) state.Renderer.forceRenderingOff = state.ForceOff;
        _hiddenRenderers.Clear();
    }

    private void RestoreColliders()
    {
        foreach (var collider in _disabledColliders)
            if (collider != null) collider.enabled = true;
        _disabledColliders.Clear();
    }

    private void LateUpdate()
    {
        if (_action != null || _boss == null || _visuals == null || _boss.PhaseIndex != 1
            || _visuals.ActiveVisualPhase == 1) return;
        // A phase change can outlive an unavailable target or a rejected tactical entry.
        var tactical = (_boss.Brain as BossBrain)?.Tactical;
        if (!CanPrepare() || tactical?.IsEntryPending != true) _visuals.ApplyPhase(1);
    }

    private void OnDisable()
    {
        EndFlight(true);
        if (_boss != null && _boss.PhaseIndex == 1 && _visuals != null) _visuals.ApplyPhase(1);
    }
}
