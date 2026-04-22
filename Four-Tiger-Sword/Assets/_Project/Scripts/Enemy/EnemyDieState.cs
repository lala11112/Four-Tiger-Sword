using UnityEngine;

public class EnemyDieState : MonoBehaviour, IPlayerState
{
    Enemy _enemy;

    public EnemyDieState(Enemy enemy) {_enemy = enemy;}

    public void Enter()
    {
        //죽음
        Debug.Log($"{_enemy.name} 사망");
        Destroy(_enemy.gameObject);
    }

    public void Update()
    {

    }

    public void Exit()
    {

    }
}