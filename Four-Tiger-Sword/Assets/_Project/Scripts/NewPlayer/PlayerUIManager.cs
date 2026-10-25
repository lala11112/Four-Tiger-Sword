using UnityEngine;
using UnityEngine.UI;

public class PlayerUIManager : MonoBehaviour
{
    public static PlayerUIManager Instance;
    [SerializeField] private Image hpBarImage;
    [SerializeField] private Image spBarImage;
    [SerializeField] private Image ultimateGaugeImage;

    public GameObject FireElement;
    public GameObject WaterElement;
    public GameObject WoodElement;
    public GameObject IronElement;
    public GameObject EarthElement;

    public Image SkillCoolTimeImage;

    private PlayerStatManager _statManager;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => Instance = null;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }

        else
        {
            Destroy(gameObject);
        }
    }

    public void Register(PlayerController controller)
    {
        Unregister();
        if (controller == null || controller.StatManager == null) return;
        _statManager = controller.StatManager;
        _statManager.OnHpChanged             += UpdateHpBar;
        _statManager.OnSpChanged             += UpdateSpBar;
        _statManager.OnUltimateGaugeChanged  += UpdateUltimateGaugeBar;
        UpdateHpBar(_statManager.CurrentHp, _statManager.MaxHp);
        UpdateSpBar(_statManager.CurrentSp, _statManager.MaxSp);
        UpdateUltimateGaugeBar(_statManager.CurrentUltimateGauge, _statManager.MaxUltimateGauge);
    }

    private void OnDestroy()
    {
        Unregister();
        if (Instance == this) Instance = null;
    }

    private void Unregister()
    {
        if (_statManager != null)
        {
            _statManager.OnHpChanged            -= UpdateHpBar;
            _statManager.OnSpChanged            -= UpdateSpBar;
            _statManager.OnUltimateGaugeChanged -= UpdateUltimateGaugeBar;
        }
        _statManager = null;
    }

    private void UpdateHpBar(float currentHp, float maxHp)
    {
        if (hpBarImage != null)
            hpBarImage.fillAmount = maxHp > 0f ? Mathf.Clamp01(currentHp / maxHp) : 0f;
    }

    private void UpdateSpBar(float currentSp, float maxSp)
    {
        if (spBarImage != null)
            spBarImage.fillAmount = maxSp > 0f ? Mathf.Clamp01(currentSp / maxSp) : 0f;
    }

    private void UpdateUltimateGaugeBar(float current, float max)
    {
        if (ultimateGaugeImage != null)
            ultimateGaugeImage.fillAmount = max > 0f ? Mathf.Clamp01(current / max) : 0f;
    }

    public void UpdateSkillCoolTime(float currentCoolTime, float maxCoolTime)
    {
        if (SkillCoolTimeImage != null)
            SkillCoolTimeImage.fillAmount = maxCoolTime > 0f ? Mathf.Clamp01(currentCoolTime / maxCoolTime) : 0f;
    }
}
