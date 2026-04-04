using UnityEngine;

// 플레이어의 HP와 SP를 관리하는 스크립트
// Player 오브젝트에 붙여서 사용
public class PlayerStatManager : MonoBehaviour
{
    [Header("HP")]
    [SerializeField] private float maxHP = 100f;   // 최대 체력 (Inspector에서 조절 가능)
    private float currentHP;                        // 현재 체력

    [Header("SP")]
    [SerializeField] private float maxSP = 50f;     // 최대 스태미나
    private float currentSP;                        // 현재 스태미나

    // 프로퍼티: 다른 스크립트(HUD 등)에서 값을 읽을 수 있게 공개
    public float MaxHP => maxHP;
    public float CurrentHP => currentHP;
    public float MaxSP => maxSP;
    public float CurrentSP => currentSP;

    // 게임 시작 시 HP/SP를 최대치로 초기화
    private void Awake()
    {
        currentHP = maxHP;
        currentSP = maxSP;
    }

    // 데미지 받기 (0 이하로 안 내려감)
    public void TakeDamage(float amount)
    {
                currentHP = Mathf.Max(0, currentHP - amount);
    }

    // 스태미나 소모 (스킬/대시 등에 사용)
    public void UseSP(float amount)
    {
        currentSP = Mathf.Max(0, currentSP - amount);
    }

    // 체력 회복 (최대치 초과 안 됨)
    public void Heal(float amount)
    {
        currentHP = Mathf.Min(maxHP, currentHP + amount);
    }

    // 스태미나 회복
    public void RecoverSP(float amount)
    {
        currentSP = Mathf.Min(maxSP, currentSP + amount);
    }
}