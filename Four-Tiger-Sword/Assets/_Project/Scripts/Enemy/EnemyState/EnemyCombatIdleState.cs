using UnityEngine;
using UnityEngine.AI;

public class EnemyCombatIdleState : IPlayerState
{
    private readonly Enemy _enemy;
    private NavMeshAgent _nav;
    private EnemyNavigation _navigation;
    private float _repathTimer;
    private bool _oldUpdateRotation;
    private float _oldStoppingDistance;

    public EnemyCombatIdleState(Enemy enemy)
    {
        _enemy = enemy;
    }

    public void Enter()
    {
        _nav = _enemy.GetComponent<NavMeshAgent>();
        _navigation = new EnemyNavigation(_enemy);
        _oldUpdateRotation = _nav.updateRotation;
        _oldStoppingDistance = _nav.stoppingDistance;
        _enemy.SetNavigationRotation(false);
        _nav.stoppingDistance = .1f;
        _navigation.Stop();
        _enemy.HasSelectedAttack = false;
        _enemy.CurrentAction = null;
        _repathTimer = 0f;
    }

    public void Update()
    {
        if (_enemy.CanAttack)
            TrySelectAttack();
        if (!_enemy.HasSelectedAttack) MaintainDistance();
    }

    public void Exit()
    {
        _navigation.Stop();
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

    // 공격을 고를 수 없을 때는 현재 거리와 가장 가까운 유효 스킬의 선호 거리를 유지합니다.
    private void MaintainDistance()
    {
        if (_enemy.DetectedTarget == null) return;
        FaceTarget();
        Vector3 away = _enemy.transform.position - _enemy.DetectedTarget.position;
        away.y = 0f;
        float distance = away.magnitude;
        float preferred = 0f;
        float closest = float.PositiveInfinity;
        var skills = _enemy.ActiveSkillSet?.skillData;
        if (skills != null)
            foreach (var skill in skills)
            {
                if (skill == null || skill is MonsterSelfDestructSO || skill.weight <= 0f
                    || skill.minimumRange > skill.executeRange || skill.executeRange < 0f
                    || skill.engageRange < skill.minimumRange) continue;
                float range = skill.preferredRange > 0f ? skill.preferredRange : skill.executeRange;
                float difference = Mathf.Abs(distance - range);
                if (difference >= closest) continue;
                closest = difference;
                preferred = range;
            }
        if (float.IsPositiveInfinity(closest) || Mathf.Abs(distance - preferred) <= .5f)
        {
            _navigation.Stop();
            _repathTimer = 0f;
            return;
        }
        _repathTimer -= _enemy.ActionDeltaTime;
        if (_repathTimer > 0f) return;
        _repathTimer = .3f;
        if (away.sqrMagnitude < .001f) away = -_enemy.transform.forward;
        Vector3 destination = _enemy.DetectedTarget.position + away.normalized * preferred;
        destination.y = _enemy.transform.position.y;
        if (_navigation.TryMove(destination, false, _enemy.DetectedTarget)) _navigation.Animate(true);
        else _navigation.Stop();
    }

    private void FaceTarget()
    {
        Vector3 dir = _enemy.DetectedTarget.position - _enemy.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            _enemy.transform.rotation = Quaternion.RotateTowards(
                _enemy.transform.rotation,
                Quaternion.LookRotation(dir),
                360f * _enemy.ActionDeltaTime);
    }
}
