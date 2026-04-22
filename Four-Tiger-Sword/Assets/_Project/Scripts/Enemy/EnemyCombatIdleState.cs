using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class EnemyCombatIdleState : IPlayerState
{
    private Enemy _enemy;

    // ScriptableObject를 오염시키지 않기 위해 런타임 쿨타임을 인스턴스별로 관리
    private readonly Dictionary<MonsterSkillData, float> _lastUseTimes = new();

    public EnemyCombatIdleState(Enemy enemy)
    {
        _enemy = enemy;
    }

    public void Enter()
    {
        
    }

    public void Update()
    {
        if(_enemy.CanAttack)
        {
            float currentDistance = Vector3.Distance(_enemy.transform.position, _enemy.DetectedTarget.position);
            _enemy.CurrentSkillData = DetermineSkill(currentDistance);
            if(_enemy.CurrentSkillData != null)
            {
                _enemy.CanUseSkill = true;
            }
        }
    }

    public void Exit()
    {

    }

    private MonsterSkillData DetermineSkill(float currentDistance)
    {
        List<MonsterSkillData> skills = new List<MonsterSkillData>();

        foreach(var skill in _enemy.MonsterSkillData.skillData)
        {
            float lastUse = _lastUseTimes.TryGetValue(skill, out float t) ? t : float.NegativeInfinity;
            if(skill.CanUse(currentDistance, lastUse, Time.time))
            {
                skills.Add(skill);
            }
        }

        if(skills.Count == 0) return null;

        float totalWeight = skills.Sum(skill => skill.Weight);
        float randomValue = Random.Range(0f, totalWeight);

        float currentWeight = 0f;
        
        foreach(var skill in skills)
        {
            currentWeight += skill.Weight;
            if(randomValue <= currentWeight)
            {
                _lastUseTimes[skill] = Time.time;
                return skill;
            }
        }

        _lastUseTimes[skills[0]] = Time.time;
        return skills[0];
    }
}
