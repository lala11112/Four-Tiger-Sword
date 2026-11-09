using UnityEngine;

public class EnemyGroggyState : IPlayerState
{
    private static readonly int GroggyHash = Animator.StringToHash("Groggy");
    private readonly Enemy _enemy;
    private float _remaining;
    private float? _savedAnimationSpeed;

    public EnemyGroggyState(Enemy enemy)
    {
        _enemy = enemy;
    }
    public void Enter()
    {
        var animator = _enemy.Animator;
        if (animator != null && animator.runtimeAnimatorController != null && animator.layerCount > 0
            && animator.HasState(0, GroggyHash))
        {
            animator.CrossFade(GroggyHash, 0.1f, 0);
        }
        else
        {
            // Legacy controllers without a groggy clip hold their current pose until recovery.
            _savedAnimationSpeed = _enemy.ActorClock != null ? _enemy.ActorClock.AnimationSpeed
                : animator != null ? animator.speed : 1f;
            _enemy.SetAnimationSpeed(0f);
        }
        _enemy.LockMovement();
        _remaining = _enemy.GroggyDuration;
        _enemy.IsHurt = false;
        _enemy.PendingGroggy = false;
    }
    public void Update()
    {
        _remaining -= _enemy.ActionDeltaTime;
        if (_remaining <= 0f) _enemy.IsGroggy = false;
    }

    public void Exit()
    {
        if (_savedAnimationSpeed.HasValue)
        {
            _enemy.SetAnimationSpeed(_savedAnimationSpeed.Value);
            _savedAnimationSpeed = null;
        }
        _enemy.UnlockMovement();
        _enemy.IsGroggy = false;
        _enemy.IsHurt = false;
        _enemy.PendingGroggy = false;
        //강인도 초기화. 
        _enemy.GetComponent<PoiseHandler>().ResetPoise();
    }

}
