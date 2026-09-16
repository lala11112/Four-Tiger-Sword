using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>폼을 바꿔도 이미 발생한 장판/지연 타격은 유지하며, 시전자 사망 시 종료합니다.</summary>
public class FormEffectRunner : MonoBehaviour
{
    private PlayerController _owner;
    private void Awake() => _owner = GetComponent<PlayerController>();
    private bool CanTick => _owner != null && _owner.StatManager.CurrentHp > 0f;

    public static IEnumerable<Component> Targets(Vector3 center, float radius)
    {
        var found = new HashSet<IDamageable>();
        foreach (var collider in Physics.OverlapSphere(center, radius, LayerMask.GetMask("Enemy")))
        {
            var target = collider.GetComponentInParent<IDamageable>();
            if (target is Component component && found.Add(target)) yield return component;
        }
    }

    public static void DamageArea(Vector3 center, float radius, HitInfo hit)
    {
        foreach (var target in Targets(center, radius))
            if (target != null) DamageManager.Apply(hit, (IDamageable)target, target.gameObject);
    }

    public IEnumerator DelayedExplosion(float impactDelay, float explosionDelay, float radius, HitInfo hit)
    {
        yield return new WaitForSeconds(impactDelay);
        if (!CanTick) yield break;
        if (!(_owner.StateMachine.CurrentState is PlayerSkillState)) yield break;
        Vector3 center = transform.position + transform.forward;
        yield return new WaitForSeconds(explosionDelay);
        if (!CanTick) yield break;
        DamageArea(center, radius, hit);
        _owner.ImpulseSource?.GenerateImpulse();
        // TODO: 폭렬참 지면 폭발 모션/VFX 연결.
    }

    public IEnumerator DelayedHits(Component target, HitInfo hit, GameObject vfx)
    {
        for (int i = 0; i < 5; i++)
        {
            yield return new WaitForSeconds(0.3f);
            if (!CanTick || target == null || !target.gameObject.activeInHierarchy) yield break;
            if (target.TryGetComponent<EnemyStat>(out var stats) && stats.IsDead) yield break;
            var result = DamageManager.Apply(hit, (IDamageable)target, target.gameObject);
            if (result.Applied && vfx != null) Instantiate(vfx, target.transform.position, Quaternion.identity);
        }
    }

    public IEnumerator Field(float delay, float duration, float radius, HitInfo hit, float healFraction)
    {
        yield return new WaitForSeconds(delay);
        if (!CanTick) yield break;
        if (healFraction > 0f ? !(_owner.StateMachine.CurrentState is PlayerUltimateState)
            : !(_owner.StateMachine.CurrentState is PlayerSkillState)) yield break;
        Vector3 center = transform.position + (healFraction > 0f ? Vector3.zero : transform.forward * 2f);
        float remaining = duration;
        while (remaining > 0f)
        {
            float interval = Mathf.Min(1f, remaining);
            yield return new WaitForSeconds(interval);
            if (!CanTick) yield break;
            if (healFraction > 0f)
            {
                // 현재 프로젝트에는 별도 아군 캐릭터가 없어 영역 내 플레이어를 회복합니다.
                foreach (var player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                    if (Vector3.Distance(player.transform.position, center) <= radius)
                        player.StatManager.Heal(player.StatManager.MaxHp * healFraction * interval);
            }
            else DamageArea(center, radius, new HitInfo(hit.BaseDamage * interval, hit.Element, hit.CriticalChance,
                hit.CriticalMultiplier, hit.PoiseDamageMultiplier, hit.ArmorPierce, hit.Power, hit.PoiseDamage,
                hit.StaggerResistLevel, hit.IsParryable, hit.Source, hit.TrueDamage));
            remaining -= interval;
        }
        // TODO: 가시 덩굴/치유 영역의 지속 VFX 연결.
    }

    private void OnDisable() => StopAllCoroutines();
}
