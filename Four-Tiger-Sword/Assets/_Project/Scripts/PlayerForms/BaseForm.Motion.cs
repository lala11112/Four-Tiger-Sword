using UnityEngine;

public abstract partial class BaseForm
{
    private Collider _softTargetCollider;
    private readonly AttackMotionProfile _motionProfile = new AttackMotionProfile();
    private WeaponActionData _motionStep;
    private float _approachTravel;
    private float _previousMoveTime;
    private float _motionDistanceScale;
    private float _rotationDeadline;
    private float _followEndTime;
    private bool _hadMotionTarget;
    private bool _motionTargetLost;

    private bool UsesApproach(WeaponActionData step)
        => step.UseTargetApproach && (_currentAction == ActionType.Attack || _currentAction == ActionType.HeavyAttack);

    private void ResetTargetApproach(WeaponActionData step, bool keepCurrent = false)
    {
        FindSoftTarget(keepCurrent);
        ResetMotion(step);
    }

    private void ResetMotion(WeaponActionData step)
    {
        _motionStep = step;
        _approachTravel = 0f;
        _previousMoveTime = 0f;
        _motionTargetLost = false;
        if (step == null) return;
        _motionProfile.Reset(step, UsesApproach(step));
        _hadMotionTarget = _softTarget != null && IsTargetValid(_softTargetOwner, _softTargetCollider);
        _motionDistanceScale = _hadMotionTarget ? 1f : Mathf.Clamp01(step.UntargetedDistanceMultiplier);
        _rotationDeadline = Mathf.Clamp(step.RotationEndTime, 0f, Mathf.Max(0f, step.Duration));
        // Finish facing the target before translation and before the first active hit.
        if (_motionProfile.TotalDistance > 0f || (UsesApproach(step) && step.FollowMovingTarget))
            _rotationDeadline = Mathf.Min(_rotationDeadline, _motionProfile.StartTime);
        if (step.HitEvents != null && step.HitEvents.Count > 0)
        {
            foreach (var hit in step.HitEvents)
                if (hit != null && hit.Duration > 0f)
                    _rotationDeadline = Mathf.Min(_rotationDeadline, Mathf.Max(0f, hit.StartTime));
        }
        else if (step.HitDuration > 0f)
            _rotationDeadline = Mathf.Min(_rotationDeadline, Mathf.Max(0f, step.HitStartTime));

        // Multi-hit steps keep following through their last active hit, not just the first lunge.
        _followEndTime = _motionProfile.EndTime;
        if (step.HitEvents != null && step.HitEvents.Count > 0)
        {
            foreach (var hit in step.HitEvents)
                if (hit != null && hit.Duration > 0f)
                    _followEndTime = Mathf.Max(_followEndTime, hit.StartTime + hit.Duration);
        }
        else if (step.HitDuration > 0f)
            _followEndTime = Mathf.Max(_followEndTime, step.HitStartTime + step.HitDuration);
        _followEndTime = Mathf.Clamp(_followEndTime, 0f, Mathf.Max(0f, step.Duration));
    }

    private void FollowAttackTarget(WeaponActionData step, float previousTime, float currentTime)
    {
        float elapsed = Mathf.Max(0f, Mathf.Min(currentTime, _followEndTime)
            - Mathf.Max(previousTime, _rotationDeadline));
        if (elapsed <= 0f) return;
        Vector3 direction = Vector3.ProjectOnPlane(_softTarget.position - _playerController.transform.position, Vector3.up);
        if (direction.sqrMagnitude < 0.001f) return;
        _playerController.transform.rotation = Quaternion.RotateTowards(_playerController.transform.rotation,
            Quaternion.LookRotation(direction), Mathf.Max(0f, step.TargetFollowRotationSpeed) * elapsed);
    }

    private void UpdateAttackFacing(float previousTime, float currentTime)
    {
        if (_softTarget == null) return;
        Vector3 direction = Vector3.ProjectOnPlane(_softTarget.position - _playerController.transform.position, Vector3.up);
        if (direction.sqrMagnitude < 0.001f) return;
        if (_rotationDeadline <= 0f)
        {
            if (previousTime == 0f) _playerController.transform.rotation = Quaternion.LookRotation(direction);
            return;
        }
        if (previousTime >= _rotationDeadline) return;
        float remaining = _rotationDeadline - previousTime;
        float elapsed = Mathf.Clamp(currentTime - previousTime, 0f, remaining);
        float angle = Vector3.Angle(_playerController.transform.forward, direction);
        // Adaptive speed allows even a 180-degree turn to finish within the windup.
        float speed = Mathf.Max(SoftTargetRotationSpeed, angle / remaining);
        _playerController.transform.rotation = Quaternion.RotateTowards(_playerController.transform.rotation,
            Quaternion.LookRotation(direction), speed * elapsed);
    }

    private static float LimitApproachDistance(float requested, float surfaceGap, float stopDistance, float remainingBudget)
        => Mathf.Min(Mathf.Max(0f, requested), Mathf.Max(0f, surfaceGap - stopDistance), Mathf.Max(0f, remainingBudget));

    private float LimitTargetApproach(WeaponActionData step, Vector3 direction, float requested)
    {
        if (Vector3.Angle(_playerController.transform.forward, direction) > Mathf.Clamp(step.ApproachAlignmentAngle, 0f, 90f))
            return 0f;
        CharacterController controller = _playerController.Controller;
        Vector3 center = controller.bounds.center;
        Vector3 closest = _softTargetCollider.ClosestPoint(center);
        float radius = controller.radius * Mathf.Max(Mathf.Abs(controller.transform.lossyScale.x), Mathf.Abs(controller.transform.lossyScale.z));
        float gap = Vector3.ProjectOnPlane(closest - center, Vector3.up).magnitude - radius;
        return LimitApproachDistance(requested, gap, step.StopDistance, step.MaxApproachDistance - _approachTravel);
    }

    protected void MoveForward(WeaponActionData step)
    {
        if (_motionStep != step || _timer < _previousMoveTime) ResetMotion(step);
        float previousTime = _previousMoveTime;
        float currentTime = Mathf.Clamp(_timer, 0f, Mathf.Max(0f, step.Duration));
        _previousMoveTime = currentTime;
        bool useApproach = UsesApproach(step);
        bool targetAlive = _softTarget != null && IsTargetValid(_softTargetOwner, _softTargetCollider);
        if (!targetAlive)
        {
            ClearSoftTarget();
            if (_hadMotionTarget) _motionTargetLost = true;
        }

        UpdateAttackFacing(previousTime, currentTime);
        bool followTarget = useApproach && step.FollowMovingTarget && targetAlive && !_motionTargetLost;
        if (followTarget) FollowAttackTarget(step, previousTime, currentTime);
        float distance = _motionProfile.Delta(previousTime, currentTime) * _motionDistanceScale;
        if (followTarget)
        {
            // Only this frame's chase time is spent. Stopping/turning never banks a later burst.
            float elapsed = Mathf.Max(0f, Mathf.Min(currentTime, _followEndTime)
                - Mathf.Max(previousTime, _motionProfile.StartTime));
            distance = Mathf.Max(0f, step.TargetFollowSpeed) * elapsed;
        }
        Vector3 direction = _playerController.transform.forward;
        if (useApproach)
        {
            // The same absolute budget applies to empty swings. Lost targets never cause a free lunge.
            distance = Mathf.Min(distance, Mathf.Max(0f, step.MaxApproachDistance - _approachTravel));
            if (_motionTargetLost) distance = 0f;
            else if (targetAlive)
            {
                direction = Vector3.ProjectOnPlane(_softTarget.position - _playerController.transform.position, Vector3.up).normalized;
                distance = LimitTargetApproach(step, direction, distance);
            }
        }

        Vector3 before = _playerController.transform.position;
        Vector3 delta = direction * distance;
        delta.y = _playerController.VerticalVelocity * Time.deltaTime;
        _playerController.Controller.Move(delta);
        _approachTravel += Vector3.ProjectOnPlane(_playerController.transform.position - before, Vector3.up).magnitude;
        // Blocked movement is consumed, never accumulated into a later catch-up teleport.
    }
}
