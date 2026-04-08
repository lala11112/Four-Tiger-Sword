public enum StatType
{
    // ① 생존 및 자원
    ST_HP,           // 최대 체력
    ST_SP,           // 최대 영력
    ST_STM,          // 최대 스테미나
    ST_SP_REGEN,     // 영력 회복 효율

    // ② 핵심 전투
    ST_ATK,          // 기본 공격력
    ST_DEF,          // 기본 방어력
    ST_CRT,          // 치명타 확률 (%)
    ST_CRTD,         // 치명타 피해량 (%, 기본 150)
    ST_PEN,          // 방어구 관통력 (%)

    // ③ 심화 전투 및 오행
    ST_ELM_ATK,      // 속성 공격력 (%)
    ST_ELM_RES,      // 속성 저항력
    ST_BRK,          // 강인도 파괴 효율
    ST_CDR,          // 스킬 쿨타임 감소 (%)
    ST_ANOMALY,      // 상태이상 축적력
    ST_ANOMALY_RES,  // 상태이상 내성

    // ④ 인 게이지
    ST_IN_GAGE,      // 현재 인 게이지
    ST_IN_GAGE_RATE, // 게이지 충전 효율

    // ⑤ 물리 제어
    ST_SPD,          // 이동 속도
    DEF_POISE,       // 방어 강인도
    KNOCKBACK_RESIST // 넉백 저항율 (0.0~1.0)
}