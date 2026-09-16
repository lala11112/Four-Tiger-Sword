using System.Collections;
using UnityEngine;

/// <summary>적별 화염 스택 및 서로 독립적으로 갱신되는 금 폼 방어 파열.</summary>
public class FormTargetEffects : MonoBehaviour
{
    private Enemy _enemy;

    private void OnEnable()
    {
        _enemy = GetComponent<Enemy>();
        if (_enemy != null) _enemy.OnDied += CancelFireExplosions;
    }

    public int FireStacks { get; private set; }
    public float BasicArmorTime { get; private set; }
    public float SkillArmorTime { get; private set; }
    public float ArmorReductionRatio => (BasicArmorTime > 0f ? 0.15f : 0f) + (SkillArmorTime > 0f ? 0.30f : 0f);
    public float EffectiveDefense
    {
        get
        {
            var stats = GetComponent<EnemyStat>();
            return stats == null ? 0f : Mathf.Max(0f, stats.GetStat(EnemyStatType.DEF)
                - stats.GetBaseStat(EnemyStatType.DEF) * ArmorReductionRatio);
        }
    }

    public void AddFireStack(HitInfo explosion, float delay)
    {
        if (!CanReceiveExplosion()) return;
        if (++FireStacks < 6) return;
        FireStacks = 0; // 폭발 자체는 추가 스택을 발생시키지 않습니다.
        // 예약 중에도 다음 6스택을 쌓을 수 있으며 각 폭발은 독립적으로 처리합니다.
        StartCoroutine(DelayedFireExplosion(explosion, delay));
    }

    private IEnumerator DelayedFireExplosion(HitInfo explosion, float delay)
    {
        // 히트스탑에 영향을 받지 않도록 실제 시간으로 기다립니다.
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        if (!CanReceiveExplosion()) yield break;
        var target = GetComponent<IDamageable>();
        if (target != null) DamageManager.Apply(explosion, target, gameObject);
    }

    private bool CanReceiveExplosion()
    {
        var stats = GetComponent<EnemyStat>();
        return isActiveAndEnabled && (stats == null || !stats.IsDead);
    }

    private void CancelFireExplosions()
    {
        StopAllCoroutines();
        FireStacks = 0;
    }

    public void ApplyArmorShred(bool skill)
    {
        if (skill) SkillArmorTime = 10f;
        else BasicArmorTime = 5f;
    }

    private void Update()
    {
        BasicArmorTime = Mathf.Max(0f, BasicArmorTime - Time.deltaTime);
        SkillArmorTime = Mathf.Max(0f, SkillArmorTime - Time.deltaTime);
    }

    private void OnDisable()
    {
        if (_enemy != null) _enemy.OnDied -= CancelFireExplosions;
        CancelFireExplosions();
        BasicArmorTime = SkillArmorTime = 0f;
    }
}
