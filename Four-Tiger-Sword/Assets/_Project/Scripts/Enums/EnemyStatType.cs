public enum EnemyStatType
{
    // 생존
    HP,               // 최대 체력

    // 전투
    ATK,              // 기본 공격력
    DEF,              // 기본 방어력
    CriticalChance,   // 크리티컬 확률
    CriticalDamage,   // 크리티컬 데미지
    AttackCooldown,   // 공격 쿨타임 (초)

    // 이동 및 감지
    CombatRange,      // 전투 사거리
    CombatExitRange,  // 전투 종료 사거리

    // 물리 제어
    KnockbackResistance, // 넉백 저항력
    KnockbackDuration,   // 넉백 지속 시간 (초)
}
