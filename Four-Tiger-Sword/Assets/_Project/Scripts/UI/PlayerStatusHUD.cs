using UnityEngine;
using UnityEngine.UI;

public class PlayerStatusHUD : PlayerStatManager
{
    [Header("Bars")]
    [SerializeField] private Slider hpBar;
    [SerializeField] private Slider spBar;

    private PlayerStatManager statManager;
    
    private void Start()
    {
        statManager = FindFirstObjectByType<PlayerStatManager>();
    }

    private void Update()
    {
        if (statManager == null) return;
        hpBar.value = statManager.CurrentHp / statManager.MaxHp;
        spBar.value = statManager.CurrentSp / statManager.MaxSp;
    }
}   