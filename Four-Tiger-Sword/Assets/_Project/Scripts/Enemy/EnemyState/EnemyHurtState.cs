using UnityEngine;

public class EnemyHurtState : IPlayerState
{
    Enemy _enemy;
    private float _remaining;
    private uint _hurtVersion;
    private float _savedAnimatorSpeed;
    private bool _freezeFallback;

    public EnemyHurtState(Enemy enemy) { _enemy = enemy; }

    public void Enter()
    {
        _enemy.LockMovement();
        _savedAnimatorSpeed = _enemy.ActorClock.AnimationSpeed;
        RestartReaction();
    }

    private void RestartReaction()
    {
        _hurtVersion = _enemy.HurtVersion;
        _remaining = Mathf.Max(0f, _enemy.HurtDuration);
        if (_enemy.Animator.runtimeAnimatorController != null
            && _enemy.Animator.HasState(0, Animator.StringToHash("Hit")))
        {
            _enemy.Animator.CrossFadeInFixedTime("Hit", 0.035f, 0, 0f);
        }
        else
        {
            // Incomplete enemy controllers must not keep walking while being knocked back.
            _freezeFallback = true;
            _enemy.SetAnimationSpeed(0f);
        }
    }

    public void Update()
    {
        if (_hurtVersion != _enemy.HurtVersion) RestartReaction();
        _remaining -= _enemy.ActionDeltaTime;
        if (_remaining > 0f || _enemy.IsKnockbacking) return;
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
        if (_freezeFallback) _enemy.SetAnimationSpeed(_savedAnimatorSpeed);
        _freezeFallback = false;
        _enemy.UnlockMovement();
        _enemy.IsHurt = false;
    }
}
