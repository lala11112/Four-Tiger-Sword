using UnityEngine;

[CreateAssetMenu(fileName = "PlayerStatData", menuName = "Data/PlayerStatData")]
public class PlayerStatData : ScriptableObject
{
    [Header("① 생존 및 자원")]
    [Tooltip("플레이어 최대 체력. 0이 되면 사망 처리됩니다.")]
    public float baseHP = 100f;
    [Tooltip("스킬 사용 자원(영력). 스킬 시전 시 소모되며 시간이 지나면 자동 회복됩니다.")]
    public float baseSP = 50f;
    [Tooltip("대쉬·회피 전용 자원(스테미나). 소진 시 대쉬/회피 불가.")]
    public float baseSTM = 100f;
    [Tooltip("대쉬 1회 소모 스테미나.")]
    public float dashStaminaCost = 25f;
    [Tooltip("달리기 중 초당 소모 스테미나.")]
    public float runStaminaCostPerSecond = 10f;
    [Tooltip("스테미나 소모 후 회복이 시작될 때까지 대기 시간(초).")]
    public float staminaRegenDelay = 2f;
    [Tooltip("스테미나 초당 자동 회복량.")]
    public float staminaRegenRate = 20f;
    [Tooltip("영력(SP) 초당 자동 회복량.")]
    public float baseSpRegen = 50f;
    [Tooltip("영력(SP) 소모 후 회복이 시작될 때까지 대기 시간(초).")]
    public float baseSpRegenDelay = 2f;

    [Header("② 핵심 전투")]
    [Tooltip("기본 물리 공격력. 데미지 계산의 기반 수치입니다.")]
    public float baseATK = 10f;
    [Tooltip("기본 방어력. 받는 물리 피해를 줄여줍니다.")]
    public float baseDEF = 5f;
    [Tooltip("치명타 발동 확률(%). 0~100 사이로 고정됩니다.")]
    public float baseCRT = 5f;
    [Tooltip("치명타 발동 시 피해 배율(%). 150 = 1.5배 데미지.")]
    public float baseCRTD = 150f;
    [Tooltip("적 방어력 무시 비율(%). 높을수록 방어력이 높은 적에게 유리합니다.")]
    public float basePEN = 0f;

    [Header("③ 심화 전투 및 오행")]
    [Tooltip("오행 속성 데미지 보너스(%). 속성 공격 시 추가 피해에 반영됩니다.")]
    public float baseElmAtk = 0f;
    [Tooltip("오행 속성 피해 감소 수치. 높을수록 속성 공격을 적게 받습니다.")]
    public float baseElmRes = 0f;
    [Tooltip("보스 강인도를 깎는 효율 배율. 1이 기본, 값이 클수록 빠르게 강인도를 파괴합니다.")]
    public float baseBRK = 1f;
    [Tooltip("스킬 쿨타임 감소율(%). 높을수록 스킬을 더 자주 사용할 수 있습니다.")]
    public float baseCDR = 0f;
    [Tooltip("적에게 상태이상을 거는 축적 속도. 값이 높을수록 빠르게 상태이상이 쌓입니다.")]
    public float baseAnomaly = 0f;
    [Tooltip("내가 받는 상태이상 저항력. 높을수록 상태이상 축적이 느려집니다.")]
    public float baseAnomalyRes = 0f;

    [Header("④ 인 게이지")]
    [Tooltip("인(忍) 게이지의 초기값.")]
    public float baseInGage = 0f;
    [Tooltip("인 게이지 충전 효율 배율. 1 = 기본 속도, 2 = 2배 속도.")]
    public float baseInGageRate = 1f;

    [Header("⑤ 물리 제어")]
    [Tooltip("이동 속도(m/s). PlayerStatManager에서 최솟값 2로 고정됩니다.")]
    public float baseSPD = 5f;
    [Tooltip("방어 강인도. 0이 되면 자세가 무너져 경직/다운 판정이 발생합니다.")]
    public float baseDefPoise = 50f;
    [Tooltip("넉백 저항율. 0.0 = 완전히 날아감 / 1.0 = 전혀 밀리지 않음.")]
    public float baseKnockbackResist = 0.2f;
}