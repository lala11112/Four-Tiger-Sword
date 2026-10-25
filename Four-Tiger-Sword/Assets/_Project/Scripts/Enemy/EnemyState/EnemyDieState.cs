using UnityEngine;

public class EnemyDieState : IPlayerState
{
    Enemy _enemy;

    public EnemyDieState(Enemy enemy) {_enemy = enemy;}

    public void Enter()
    {
        //죽음
        Debug.Log($"{_enemy.name} 사망");
        if (_enemy.Animator.HasState(0, Animator.StringToHash("Die")))
        {
            _enemy.Animator.applyRootMotion = true;
            _enemy.Animator.CrossFade("Die", 0.1f, 0);
        }
        else
        {
            // Drone/Chang can fade from their current pose without a Die animation.
            _enemy.Animator.applyRootMotion = false;
            _enemy.SetAnimationSpeed(0f);
        }
        _enemy.LockMovement();
        foreach (var collider in _enemy.GetComponentsInChildren<Collider>(true))
        {
            if (collider.GetComponentInParent<Enemy>() == _enemy)
                collider.enabled = false;
        }
        _enemy.GetComponent<EnemyDeathFade>()?.BeginFade();
    }

    public void Update()
    {

    }

    public void Exit()
    {

    }
}
