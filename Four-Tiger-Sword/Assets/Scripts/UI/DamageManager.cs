using UnityEngine;

public static class DamageManager
{
    /// <summary>
    /// HitInfo를 기반으로 대상에게 데미지를 적용합니다.
    /// targetGO를 통해 WeakPoint, ArmorPierce 등의 상태를 검사합니다. (DIP: IDamageable 추상화 의존)
    /// </summary>
    public static void Apply(HitInfo info, IDamageable target, GameObject targetGO)
    {
        //var statHandler = targetGO.GetComponent<EnemyStatHandler>();

        // 약점 노출 상태이면 무조건 치명타
        //bool forceCrit  = statHandler != null && statHandler.IsWeakPointExposed;
        //bool isCritical = forceCrit || Random.value < info.CriticalChance;
        bool isCritical = Random.value < info.CriticalChance;

        int rawDamage = isCritical
            ? Mathf.RoundToInt(info.BaseDamage * info.CriticalMultiplier)
            : info.BaseDamage;

        // ArmorPierce: 방어력 감소량만큼 데미지 증가 (DEF 시스템 도입 전 플래그 보존)
        // 현재 구조에서 EffectiveDef가 0이면 차감 없음 — 추후 공식 추가
        //if (info.ArmorPierce && statHandler != null)
        //{
        //    rawDamage += Mathf.RoundToInt(statHandler.EffectiveDef);
        //}

        target.TakeDamage(rawDamage, info.Element, isCritical);
    }
}
