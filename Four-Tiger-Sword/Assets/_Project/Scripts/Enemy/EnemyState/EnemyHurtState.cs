using UnityEngine;

public class EnemyHurtState : IPlayerState
{
    Enemy _enemy;
    private float _remaining;

    public EnemyHurtState(Enemy enemy) { _enemy = enemy; }

    public void Enter()
    {
        //피격 애니메이션 재생
        _enemy.Animator.CrossFade("Hit", 0.1f);
        _enemy.LockMovement();
        _remaining = 1f;
    }

    public void Update()
    {
        _remaining -= Time.deltaTime;
        if (_remaining > 0f) return;
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
        Debug.Log("EnemyHurtState Exit");
        _enemy.UnlockMovement();
        _enemy.IsHurt = false;
    }
}
