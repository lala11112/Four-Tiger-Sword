using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class EnemyCombatIdleState : IPlayerState
{
    private readonly Enemy _enemy;
    private NavMeshAgent _nav;
    private readonly List<MonsterSkillData> _candidates = new();
    private readonly List<float> _candidateWeights = new();

    private float _roamTimer;
    private const float RoamInterval = 5f;
    private const float RoamRadius   = 4f;

    public EnemyCombatIdleState(Enemy enemy)
    {
        _enemy = enemy;
    }

    public void Enter()
    {
        _nav = _enemy.GetComponent<NavMeshAgent>();
        _enemy.Animator.CrossFade("Move", 0.1f);
        _enemy.HasSelectedAttack = false;
        _enemy.CurrentAction = null;
        _roamTimer = RoamInterval; // 진입 즉시 배회 시작
    }

    public void Update()
    {
        if (_enemy.CanAttack)
            TrySelectAttack();
        else
            Roam();
    }

    public void Exit() { }

    // ── 공격 선택 (가중치 랜덤) ─────────────────────────────────────────────

    private void TrySelectAttack()
    {
        if (_enemy.DetectedTarget == null || _enemy.EnemyStat.MonsterSkillData?.skillData == null) return;

        float dist = Vector3.Distance(_enemy.transform.position, _enemy.DetectedTarget.position);

        // 현재 거리에서 선택 가능한 공격 목록 (engageRange 이내)
        var candidates = _candidates;
        candidates.Clear();
        _candidateWeights.Clear();
        float totalWeight = 0f;
        foreach (var skill in _enemy.EnemyStat.MonsterSkillData.skillData)
        {
            float weight = _enemy.GetAttackSelectionWeight(skill, dist);
            if (weight <= 0f) continue;
            candidates.Add(skill);
            _candidateWeights.Add(weight);
            totalWeight += weight;
        }

        if (candidates.Count == 0)
        {
            // 사거리 밖일 때만 접근하고, 쿨타임/최소 거리 때문에 선택할 수 없으면 배회합니다.
            bool outsideAllRanges = true;
            foreach (var skill in _enemy.EnemyStat.MonsterSkillData.skillData)
                if (skill != null && skill.weight > 0f && dist <= skill.engageRange)
                    outsideAllRanges = false;
            if (outsideAllRanges && _nav != null && _nav.enabled && _nav.isOnNavMesh)
                _nav.SetDestination(_enemy.DetectedTarget.position);
            else
                Roam();
            return;
        }

        // 가중치 합산 후 랜덤 롤
        float roll = Random.Range(0f, totalWeight);
        float cumulative = 0f;

        for (int i = 0; i < candidates.Count; i++)
        {
            var skill = candidates[i];
            cumulative += _candidateWeights[i];
            if (roll <= cumulative)
            {
                _enemy.CurrentAction = skill.CreateAction(_enemy);
                _enemy.HasSelectedAttack = _enemy.CurrentAction != null;
                return;
            }
        }

        // 부동소수점 오차 폴백
        var fallback = candidates[candidates.Count - 1];
        _enemy.CurrentAction = fallback.CreateAction(_enemy);
        _enemy.HasSelectedAttack = _enemy.CurrentAction != null;
    }

    // ── 배회 (쿨타임 중 플레이어 주변 랜덤 이동) ────────────────────────────

    private void Roam()
    {
        if (_enemy.DetectedTarget == null) return;

        FaceTarget();

        _roamTimer += Time.deltaTime;
        if (_roamTimer < RoamInterval) return;

        _roamTimer = 0f;
        /*
        Vector2 rand2D = Random.insideUnitCircle.normalized * RoamRadius;
        Vector3 targetPos = _enemy.DetectedTarget.position + new Vector3(rand2D.x, 0f, rand2D.y);
        _nav.SetDestination(targetPos);
        */

         //배회 할때 플레이어를 가로지르지 못하도록 막는 코드, 그런데 조금 작동을 잘 안하는거 같음
        Vector3 toEnemy = _enemy.transform.position - _enemy.DetectedTarget.position;
        toEnemy.y = 0f;
        Vector3 baseDir = toEnemy.sqrMagnitude > 0.001f ? toEnemy.normalized : _enemy.transform.forward;

        float randomAngle = Random.Range(-90f, 90f);
        Vector3 roamDir = Quaternion.Euler(0f, randomAngle, 0f) * baseDir;
        Vector3 targetPos = _enemy.DetectedTarget.position + roamDir * RoamRadius;
        if (_nav.enabled && _nav.isOnNavMesh) _nav.SetDestination(targetPos);
        
    }

    private void FaceTarget()
    {
        Vector3 dir = _enemy.DetectedTarget.position - _enemy.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            _enemy.transform.rotation = Quaternion.RotateTowards(
                _enemy.transform.rotation,
                Quaternion.LookRotation(dir),
                360f * Time.deltaTime);
    }
}
