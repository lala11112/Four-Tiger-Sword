using UnityEngine;

public class PlayerStatManager : MonoBehaviour
{
    // ─── 기본 스탯 (Inspector에서 수정 가능) ───
    [Header("Base Stats")]
    [SerializeField] private float maxHP = 100f;   // 최대 체력
    [SerializeField] private float maxSP = 50f;    // 최대 기력
    [SerializeField] private float atk = 10f;      // 공격력
    [SerializeField] private float def = 5f;       // 방어력
    [SerializeField] private float spd = 5f;       // 이동속도
    [SerializeField] private float crt = 0.05f;    // 크리티컬 확률 (5%)

    // ─── 현재 수치 (게임 중 변동) ───
    private float currentHP;
        private float currentSP;

    // ─── 외부에서 읽기 전용으로 접근하는 프로퍼티 ───
    public float MaxHP => maxHP;
    public float MaxSP => maxSP;
    public float CurrentHP => currentHP;
    public float CurrentSP => currentSP;
    public float ATK => atk;
    public float DEF => def;
    public float SPD => spd;
    public float CRT => crt;

    // 게임 시작 시 현재 수치를 최대치로 초기화
    private void Awake()
    {
                currentHP = maxHP;
        currentSP = maxSP;
    }

    // 데미지 받기: 실제 데미지 = 받은 데미지 - 방어력 (최소 0)
    public void TakeDamage(float damage)
    {
        float actual = Mathf.Max(0, damage - def);  // 방어력만큼 깎음
        currentHP = Mathf.Max(0, currentHP - actual); // HP는 0 이하로 안 내려감
    }

    // 기력 사용: 스킬 쓸 때 호출
    public void UseSP(float amount)
    {
        currentSP = Mathf.Max(0, currentSP - amount);
            }

    // 체력 회복
    public void Heal(float amount)
    {
        currentHP = Mathf.Min(maxHP, currentHP + amount); // 최대치 넘지 않게
    }

    // 기력 회복
    public void RecoverSP(float amount)
    {
        currentSP = Mathf.Min(maxSP, currentSP + amount);
    }

    // 살아있는지 체크
        public bool IsAlive => currentHP > 0;
}