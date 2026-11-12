using UnityEngine.AI;

public class EnemyCombatIdleState : IPlayerState
{
    private readonly Enemy _enemy;
    private NavMeshAgent _nav;
    private bool _oldUpdateRotation;
    private float _oldStoppingDistance;

    public EnemyCombatIdleState(Enemy enemy)
    {
        _enemy = enemy;
    }

    public void Enter()
    {
        var positioning = _enemy.TacticalPositioning;
        _nav = positioning.Navigation.Agent;
        _oldUpdateRotation = _nav.updateRotation;
        _oldStoppingDistance = _nav.stoppingDistance;
        _enemy.SetNavigationRotation(false);
        _nav.stoppingDistance = .1f;
        positioning.Navigation.RefreshAnimation();
        positioning.Reset();
        _enemy.HasSelectedAttack = false;
        _enemy.CurrentAction = null;
    }

    public void Update()
    {
        if (_enemy.CanAttack)
            TrySelectAttack();
        if (!_enemy.HasSelectedAttack) MaintainDistance();
    }

    public void Exit()
    {
        _enemy.TacticalPositioning.Reset();
        _enemy.SetNavigationRotation(_oldUpdateRotation);
        _nav.stoppingDistance = _oldStoppingDistance;
    }

    // ── 공격 선택 (가중치 랜덤) ─────────────────────────────────────────────

    private void TrySelectAttack()
    {
        var skill = _enemy.SelectAttackSkill();
        if (skill == null) return;
        _enemy.CurrentAction = skill.CreateAction(_enemy);
        _enemy.HasSelectedAttack = _enemy.CurrentAction != null;
    }

    private void MaintainDistance()
    {
        var target = _enemy.DetectedTarget;
        if (target == null || _enemy.IsKnockbacking) return;
        var positioning = _enemy.TacticalPositioning;
        positioning.FaceTarget(target);
        positioning.UpdateVisibleTarget(target, positioning.PreferredSkill(target), false);
    }
}
