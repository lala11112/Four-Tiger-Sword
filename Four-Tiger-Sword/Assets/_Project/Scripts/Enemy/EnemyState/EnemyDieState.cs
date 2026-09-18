using UnityEngine;

public class EnemyDieState : IPlayerState
{
    Enemy _enemy;

    public EnemyDieState(Enemy enemy) {_enemy = enemy;}

    public void Enter()
    {
        //죽음
        Debug.Log($"{_enemy.name} 사망");
        _enemy.Animator.applyRootMotion = true;
        _enemy.Animator.CrossFade("Die", 0.1f);
        _enemy.LockMovement();
        foreach (var collider in _enemy.GetComponentsInChildren<Collider>(true))
        {
            if (collider.GetComponentInParent<Enemy>() == _enemy)
                collider.enabled = false;
        }
        //Destroy(_enemy.gameObject);
    }

    public void Update()
    {

    }

    public void Exit()
    {

    }
}
