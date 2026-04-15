public readonly struct HitInfo
{
    public readonly int   BaseDamage;
    public readonly DamageType Element;
    public readonly float CriticalChance;
    public readonly float CriticalMultiplier;

    /// <summary>경직(Poise) 데미지 배율. 기본값 1.0 (화: 1.5, 금 스킬: 강제 붕괴)</summary>
    public readonly float PoiseDamageMultiplier;

    /// <summary>true일 경우 적의 방어력(DEF)을 완전히 무시합니다. (금: 15% 확률)</summary>
    public readonly bool ArmorPierce;

    public HitInfo(int damage, DamageType element,
                   float critChance, float critMultiplier,
                   float poiseDamageMultiplier = 1f,
                   bool  armorPierce = false)
    {
        BaseDamage            = damage;
        Element               = element;
        CriticalChance        = critChance;
        CriticalMultiplier    = critMultiplier;
        PoiseDamageMultiplier = poiseDamageMultiplier;
        ArmorPierce           = armorPierce;
    }
}
