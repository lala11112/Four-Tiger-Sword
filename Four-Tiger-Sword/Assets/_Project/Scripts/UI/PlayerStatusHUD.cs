using UnityEngine;
using UnityEngine.UI;

public class PlayerStatusHUD : PlayerStatManager
{
    [Header("Bars")]
    [SerializeField] private Slider hpBar;

    private PlayerStatManager statManager;
    
    private void Start()
    {
        statManager = FindFirstObjectByType<PlayerStatManager>();
    }

    private void Update()
    {
        if (statManager == null) return;
        hpBar.value = statManager.CurrentHp / statManager.MaxHp;
    }
}
