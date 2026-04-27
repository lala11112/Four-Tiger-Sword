using UnityEngine;
using System.Collections;

public class EnemyHurtState : MonoBehaviour, IPlayerState
{
    Enemy _enemy;

    public EnemyHurtState(Enemy enemy) { _enemy = enemy; }

    public void Enter()
    {
        //피격 애니메이션 재생
        _enemy.StartCoroutine(HurtRoutine());
    }

    public void Update()
    {
        //애니메이션 재생후, IsHurt를 false로 변경
        //Debug.Log("EnemyHurtState Exit");
    }

    public void Exit()
    {

    }

    private IEnumerator HurtRoutine()
    {
        yield return new WaitForSeconds(1f);
        _enemy.IsHurt = false;
    }
}