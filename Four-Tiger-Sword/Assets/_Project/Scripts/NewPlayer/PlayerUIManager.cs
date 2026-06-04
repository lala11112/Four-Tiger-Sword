using UnityEngine;
using UnityEngine.UI;

public class PlayerUIManager : MonoBehaviour
{
    public static PlayerUIManager Instance;
    [SerializeField] private Image hpBarImage;
    [SerializeField] private Image spBarImage;
    [SerializeField] private Image ultimateGaugeImage;
    public Image WaterGauge;

    public GameObject FireElement;
    public GameObject WaterElement;
    public GameObject WoodElement;
    public GameObject IronElement;
    public GameObject EarthElement;

    public Image SkillCoolTimeImage;

    private PlayerStatManager _statManager;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        else
        {
            Destroy(gameObject);
        }
    }

    public void Register(PlayerController controller)
    {
        _statManager = controller.StatManager;
        _statManager.OnHpChanged             += UpdateHpBar;
        _statManager.OnSpChanged             += UpdateSpBar;
        _statManager.OnUltimateGaugeChanged  += UpdateUltimateGaugeBar;
    }

    private void OnDestroy()
    {
        if (_statManager != null)
        {
            _statManager.OnHpChanged            -= UpdateHpBar;
            _statManager.OnSpChanged            -= UpdateSpBar;
            _statManager.OnUltimateGaugeChanged -= UpdateUltimateGaugeBar;
        }
    }

    private void UpdateHpBar(float currentHp, float maxHp)
    {
        if (hpBarImage != null)
            hpBarImage.fillAmount = currentHp / maxHp;
    }

    private void UpdateSpBar(float currentSp, float maxSp)
    {
        if (spBarImage != null)
            spBarImage.fillAmount = currentSp / maxSp;
    }

    private void UpdateUltimateGaugeBar(float current, float max)
    {
        if (ultimateGaugeImage != null)
            ultimateGaugeImage.fillAmount = current / max;
    }

    public void UpdateSkillCoolTime(float currentCoolTime, float maxCoolTime)
    {
        if (SkillCoolTimeImage != null)
            SkillCoolTimeImage.fillAmount = currentCoolTime / maxCoolTime;
    }
}
