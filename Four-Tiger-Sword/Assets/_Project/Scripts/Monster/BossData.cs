using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BossData_", menuName = "SainGum/Monster/BossData")]
public class BossData : ScriptableObject
{
    [Header("보스 기본 정보")]
    public string bossId; // 보스 고유 ID
    public string bossName; // 보스 이름
    public ElementType elementType; // 오행 속성 (목/화/토/금/수)

    [Header("기초 스탯")]
    public int baseHp; // 기본 체력
    public float baseAtk; // 기본 방어력
    public int baseDef; // 기본 방어력
    public float moveSpeed; // 이동 속도
    public float detectionRange; // 플레이어 감지 범위

    [Header("강인도 & 그로기")]
    public float maxPoise; // 최대 강인도 (0 되면 그로기)
    public float poiseRecoverRate; // 강인도 초당 회복량
    public float groggyDuration; // 그로기 지속 시간 (초)
    public float groggyDamageMult = 1.5f; // 그로기 상태 데미지 배율

    [Header("오행 약점 & 저항")]
    public ElementType weaknessElement; // 약점 속성
    public float weakDamageMult = 1.5f; // 약점 피격 시 데미지 배율
    public ElementType resistElement; // 저항 속성
    public float resistDamageMult = 0.5f; // 저항 피격 시 데미지 배율

    [Header("상태이상 면역")]
    public List<string> statusImmunityList = new List<string>(); // 면역 상태이상 목록

    [Header("페이즈 제어")]
    public int maxPhase = 3; // 최대 페이즈 수
    public List<float> phaseThresholds = new List<float> { 70f, 30f }; // 페이즈 전환 HP% 기준

    [Header("보상")]
    public string rewardGroupId; // 보상 드랍 테이블 ID
}
