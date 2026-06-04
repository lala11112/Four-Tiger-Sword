using UnityEngine;

public static class DamageManager
{    
    private static ElementEffectiveManager _elementEffectiveManager;
    public static void Apply(HitInfo info, IDamageable target, GameObject targetGO)
    {
        if (_elementEffectiveManager == null)
        {
            _elementEffectiveManager = Resources.Load<ElementEffectiveManager>("ElementEffectiveManager");
            _elementEffectiveManager.Initialize();
        }

        float finalDamage = info.BaseDamage;
        ElementType attackType = info.Element;
        ElementType defenseType = targetGO.GetComponent<Enemy>()?.Element ?? targetGO.GetComponent<PlayerController>()?.Element ?? ElementType.ELEMENT_NONE;
        float effective = _elementEffectiveManager.GetElementEffective(attackType, defenseType);
        finalDamage = finalDamage * effective;


        //var statHandler = targetGO.GetComponent<EnemyStatHandler>();

        // 약점 노출 상태이면 무조건 치명타
        //bool forceCrit  = statHandler != null && statHandler.IsWeakPointExposed;
        //bool isCritical = forceCrit || Random.value < info.CriticalChance;
        bool isCritical = Random.value < info.CriticalChance;

        finalDamage = isCritical
            ? Mathf.RoundToInt(finalDamage * info.CriticalMultiplier)
            : finalDamage;

        // ArmorPierce: 방어력 감소량만큼 데미지 증가 (DEF 시스템 도입 전 플래그 보존)
        // 현재 구조에서 EffectiveDef가 0이면 차감 없음 — 추후 공식 추가
        //if (info.ArmorPierce && statHandler != null)
        //{
        //    rawDamage += Mathf.RoundToInt(statHandler.EffectiveDef);
        //}

        target.TakeDamage(finalDamage, attackType, isCritical, info.Power, info.PoiseDamage, info.StaggerResistLevel, info.IsParryable);
    }
}
