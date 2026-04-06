using UnityEngine;
using UnityEngine.UI;

public class PlayerStatusHUD : MonoBehaviour
{
    [Header("Bars")]
    [SerializeField] private Slider hpBar;  // HP 슬라이더
    [SerializeField] private Slider spBar;  // SP 슬라이더

    private PlayerStatManager statManager;

    private void Start()
    {        // 씬에서 PlayerStatManager 찾기
        statManager = FindFirstObjectByType<PlayerStatManager>();
    }

    private void Update()
    {
        if (statManager == null) return;

        // 0~1 비율로 바 업데이트
        hpBar.value = statManager.CurrentHP / statManager.MaxHP;
        spBar.value = statManager.CurrentSP / statManager.MaxSP;
    }
}