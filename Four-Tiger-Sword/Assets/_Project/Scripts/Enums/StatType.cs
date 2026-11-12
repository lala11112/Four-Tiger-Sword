public enum StatType
{
    // ① 생존 및 자원
    ST_HP = 0,           // 최대 체력

    // ② 핵심 전투
    ST_ATK = 4,          // 기본 공격력
    ST_DEF = 5,          // 기본 방어력
    ST_CRT = 6,          // 치명타 확률 (%)
    ST_CRTD = 7,         // 치명타 피해량 (%, 기본 150)
    ST_PEN = 8,          // 방어구 관통력 (%)

    // ③ 심화 전투 및 오행
    ST_ELM_ATK = 9,      // 속성 공격력 (%)
    ST_ELM_RES = 10,      // 속성 저항력
    ST_BRK = 11,          // 강인도 파괴 효율
    ST_CDR = 12,          // 스킬 쿨타임 감소 (%)
    ST_ANOMALY = 13,      // 상태이상 축적력
    ST_ANOMALY_RES = 14,  // 상태이상 내성

    // ④ 인 게이지
    ST_IN_GAGE = 15,      // 현재 인 게이지
    ST_IN_GAGE_RATE = 16, // 게이지 충전 효율

    // ⑤ 물리 제어
    ST_SPD = 17,          // 이동 속도
    DEF_POISE = 18,       // 방어 강인도
    KNOCKBACK_RESIST = 19, // 넉백 저항율 (0.0~1.0)

    // ⑥ 공격 속도
    ST_ATK_SPD = 20,      // 공격 속도 배율 (1.0 = 기본, 2.0 = 2배 빠름)
}
