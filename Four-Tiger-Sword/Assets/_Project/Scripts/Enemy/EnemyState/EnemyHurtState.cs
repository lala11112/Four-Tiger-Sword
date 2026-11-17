using UnityEngine;

public class EnemyHurtState : IPlayerState
{
    private static readonly int Hit = Animator.StringToHash("Hit");
    private static readonly int Idle = Animator.StringToHash("Idle");
    private static readonly int Move = Animator.StringToHash("Move");
    private const float BlendSeconds = .035f;
    private readonly Enemy _enemy;
    private readonly bool _waitForAnimation;
    private float _remaining;
    private uint _hurtVersion;
    private float _savedAnimatorSpeed;
    private bool _restoreAnimatorSpeed;
    private Animator _hitAnimator;
    private RuntimeAnimatorController _hitController;
    private AnimatorCullingMode _savedCulling;
    private bool _savedKeepState;
    private int _recoveryState;
    private int _animationRequestFrame;
    private bool _recovering;
    private float _hitProgress;
    private bool _resumeAttempted;

    public EnemyHurtState(Enemy enemy)
    {
        _enemy = enemy;
        _waitForAnimation = !(enemy is Boss);
    }

    public void Enter()
    {
        (_enemy.Brain as NormalEnemyBrain)?.SuspendPreparedAttackForHurt();
        _enemy.LockMovement();
        _savedAnimatorSpeed = _enemy.ActorClock != null ? _enemy.ActorClock.AnimationSpeed
            : _enemy.Animator != null ? _enemy.Animator.speed : 1f;
        RestartReaction();
    }

    private void RestartReaction()
    {
        _hurtVersion = _enemy.HurtVersion;
        _remaining = Mathf.Max(0f, _enemy.HurtDuration);
        var animator = _enemy.Animator;
        if (_waitForAnimation)
        {
            StartTrackedReaction(animator);
        }
        else if (animator != null && animator.runtimeAnimatorController != null && animator.HasState(0, Hit))
        {
            // Boss reactions retain their original timer and animation settings.
            animator.CrossFadeInFixedTime(Hit, BlendSeconds, 0, 0f);
        }
        else FreezeFallback();
    }

    private void StartTrackedReaction(Animator animator)
    {
        RestoreAnimator();
        _recovering = false;
        _hitProgress = 0f;
        _resumeAttempted = false;
        _recoveryState = 0;
        if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null
            && animator.layerCount > 0 && animator.HasState(0, Hit))
        {
            if (animator.HasState(0, Idle)) _recoveryState = Idle;
            else if (animator.HasState(0, Move)) _recoveryState = Move;
        }
        if (_recoveryState == 0)
        {
            FreezeFallback();
            return;
        }

        _hitAnimator = animator;
        _hitController = animator.runtimeAnimatorController;
        _savedCulling = animator.cullingMode;
        _savedKeepState = animator.keepAnimatorStateOnDisable;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.keepAnimatorStateOnDisable = true;
        _restoreAnimatorSpeed = true;
        _enemy.SetAnimationSpeed(1f);
        _animationRequestFrame = Time.frameCount;
        // Restart repeated hits from the first frame, including when the previous Hit is fading out.
        if (IsHit(animator.GetCurrentAnimatorStateInfo(0))
            || (animator.IsInTransition(0) && IsHit(animator.GetNextAnimatorStateInfo(0))))
            animator.Play(Hit, 0, 0f);
        else
            animator.CrossFadeInFixedTime(Hit, BlendSeconds, 0, 0f);
    }

    private void FreezeFallback()
    {
        // Incomplete enemy controllers must not keep walking while being knocked back.
        _restoreAnimatorSpeed = true;
        _enemy.SetAnimationSpeed(0f);
    }

    private static bool IsHit(AnimatorStateInfo state) => state.shortNameHash == Hit;

    private bool FinishTrackedReaction(bool readyToRecover)
    {
        var animator = _hitAnimator;
        if (animator == null || !animator.isActiveAndEnabled) return true;
        // Animator state still describes the previous evaluation on the frame we request a state.
        if (Time.frameCount == _animationRequestFrame) return false;

        bool transitioning = animator.IsInTransition(0);
        var current = animator.GetCurrentAnimatorStateInfo(0);
        var next = transitioning ? animator.GetNextAnimatorStateInfo(0) : default;
        bool nextIsHit = IsHit(next);
        if (!IsHit(current) && !nextIsHit)
        {
            if (_recovering || _hitProgress >= 1f || _resumeAttempted) return true;
            // Changing root motion or reinitializing a rig can clear Hit during the reaction.
            // Resume once from the last observed pose; an unusable controller falls back to timing.
            _resumeAttempted = true;
            _animationRequestFrame = Time.frameCount;
            animator.Play(Hit, 0, _hitProgress);
            return false;
        }
        if (_recovering) return false;

        var hit = nextIsHit ? next : current;
        _hitProgress = Mathf.Clamp01(hit.normalizedTime);
        // Looping Hit clips also finish after one cycle. A motion that cannot advance uses the timer.
        if (!readyToRecover || (hit.length > 0f && hit.speed * hit.speedMultiplier > 0f && _hitProgress < 1f))
            return false;

        _recovering = true;
        _animationRequestFrame = Time.frameCount;
        animator.CrossFadeInFixedTime(_recoveryState, BlendSeconds, 0, 0f);
        return false;
    }

    public void Update()
    {
        if (_hurtVersion != _enemy.HurtVersion) RestartReaction();
        else if (_hitAnimator != null && (_hitAnimator != _enemy.Animator
            || _hitController != _hitAnimator.runtimeAnimatorController))
            StartTrackedReaction(_enemy.Animator);
        _remaining -= _enemy.ActionDeltaTime;
        bool readyToRecover = _remaining <= 0f && !_enemy.IsKnockbacking;
        if (!FinishTrackedReaction(readyToRecover) || !readyToRecover) return;
        _enemy.IsHurt = false;
        // 정상 종료 시에만 그로기로 이어집니다. 사망 등으로 중단되면 예약하지 않습니다.
        if (_enemy.PendingGroggy)
        {
            _enemy.PendingGroggy = false;
            _enemy.IsGroggy = true;
        }
    }

    public void Exit()
    {
        if (_restoreAnimatorSpeed) _enemy.SetAnimationSpeed(_savedAnimatorSpeed);
        RestoreAnimator();
        _restoreAnimatorSpeed = false;
        _enemy.UnlockMovement();
        _enemy.IsHurt = false;
    }

    private void RestoreAnimator()
    {
        if (_hitAnimator != null)
        {
            _hitAnimator.cullingMode = _savedCulling;
            _hitAnimator.keepAnimatorStateOnDisable = _savedKeepState;
        }
        _hitAnimator = null;
        _hitController = null;
    }
}
