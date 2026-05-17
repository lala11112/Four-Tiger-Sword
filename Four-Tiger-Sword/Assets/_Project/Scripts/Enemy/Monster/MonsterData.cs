using UnityEngine;

[CreateAssetMenu(fileName = "MonsterData_", menuName = "SainGum/Monster/MonsterData")]
public class MonsterData : ScriptableObject
{
    [Header("기본 정보")]
    public string monsterId; // 몬스터 고유 ID
    public string displayName; // 게임 내 표시 이름

    [Header("오행 상성 및 상태이상")]
    public ElementType elementType; // 오행 속성 (목/화/토/금/수)

    [Tooltip("상태이상 축적 저항치")]
    public float anomalyResist; // 상태이상 저항 (높을수록 잘 안 걸림)

    [Header("생존 및 방어 스탯")]
    public int maxHp; // 최대 체력
    public int baseDef; // 기본 방어력
    public StaggerResistLevel staggerResistLevel; // 경직 저항 등급 

    [Header("공격 및 물리 제어 스탯")]
    public int baseAtk; // 기본 공격력
    public float atkStaggerValue; // 기본 시경직 수치(플레이어에게 주는 경직량)
    public float knockbackPowerMult; // 넉백 파워 배율 (밀어내는 힘)
    public float moveSpeed; // 이동 속도

    [Header("AI 인지")]
    public float detectRange; // 플레이어 감지 범위
    public float atkRange; // 공격 사거리

    //나중에 활용예정
    //public AggroType aggroType; // 어그로 타입 (선공형, 비선공형, 도주형)

    [Header("리소스 연동")]
    public string modelId; // 3D 모델 ID (프리팹 연결용) 이걸 굳이 사용해야하나?
    public string animSetId; // 애니메이션 세트 ID

    [Header("보상")]
    public string dropTableId; // 드랍 테이블 ID
    public int dropExp; // 처치 시 경험치
}
