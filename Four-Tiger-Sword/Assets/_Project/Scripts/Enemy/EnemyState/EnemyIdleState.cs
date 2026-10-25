public class EnemyIdleState : IPlayerState
{
    Enemy _enemy;
    private bool _missingIdleReported;

    public EnemyIdleState(Enemy enemy) {_enemy = enemy;}

    public void Enter()
    {
        var nav = _enemy.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (nav != null && nav.enabled && nav.isOnNavMesh) nav.ResetPath();
        var animator = _enemy.Animator;
        if (animator != null && animator.layerCount > 0 && animator.HasState(0, UnityEngine.Animator.StringToHash("Idle")))
            animator.CrossFade("Idle", 0.1f, 0);
        else if (animator != null && !_missingIdleReported)
        {
            _missingIdleReported = true;
            UnityEngine.Debug.LogWarning($"{_enemy.name}: Animator has no Idle state on layer 0; retaining its current animation.", animator);
        }
    }

    public void Update()
    {
        //그냥 대기 또는 아직 미정.
    }

    public void Exit()
    {
        
    }

}
