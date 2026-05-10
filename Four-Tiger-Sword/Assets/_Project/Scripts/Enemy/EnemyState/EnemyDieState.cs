using UnityEngine;

public class EnemyDieState : MonoBehaviour, IPlayerState
{
    Enemy _enemy;

    public EnemyDieState(Enemy enemy) {_enemy = enemy;}

    public void Enter()
    {
        //죽음
        Debug.Log($"{_enemy.name} 사망");
        _enemy.Animator.CrossFade("Die", 0.1f);
        //Destroy(_enemy.gameObject);
    }

    public void Update()
    {

    }

    public void Exit()
    {

    }
}