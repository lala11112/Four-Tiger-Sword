using UnityEngine;

public class EnemyGroggyState : IPlayerState
{
    private Enemy _enemy;
    private float _remaining;

    public EnemyGroggyState(Enemy enemy)
    {
        _enemy = enemy;
    }
    public void Enter()
    {
        _enemy.Animator.CrossFade("Groggy", 0.1f);
        _enemy.LockMovement();
        _remaining = _enemy.GroggyDuration;
        _enemy.IsHurt = false;
        _enemy.PendingGroggy = false;
    }
    public void Update()
    {
        _remaining -= Time.deltaTime;
        if (_remaining <= 0f) _enemy.IsGroggy = false;
    }

    public void Exit()
    {
        _enemy.UnlockMovement();
        _enemy.IsGroggy = false;
        _enemy.IsHurt = false;
        _enemy.PendingGroggy = false;
        //강인도 초기화. 
        _enemy.GetComponent<PoiseHandler>().ResetPoise();
    }

}
