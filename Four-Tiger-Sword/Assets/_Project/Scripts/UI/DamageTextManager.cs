using UnityEngine;

public class DamageTextManager : MonoBehaviour
{
    public static DamageTextManager Instance;

    [SerializeField] private GameObject damageTextPrefab;
    [SerializeField] private Canvas worldSpaceCanvas;

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

    public void Register(Enemy enemy)
    {
        enemy.OnDamaged += (damage, damageType, isCritical) =>
            ShowDamageText(damage, enemy.transform.position, damageType, isCritical);
    }

    public void Register(PlayerController player)
    {
        player.StatManager.OnDamageTaken += (damage, damageType, isCritical) =>
            ShowDamageText(damage, player.transform.position, damageType, isCritical);
    }

    public void ShowDamageText(int damage, Vector3 worldPosition, ElementType damageType = ElementType.ELEMENT_NONE, bool isCritical = false)
    {
        Vector3 offset = new Vector3(Random.Range(-0.5f, 0.5f), 1.5f, Random.Range(-0.5f, 0.5f));
        GameObject obj = Instantiate(damageTextPrefab, worldPosition + offset, Quaternion.identity, worldSpaceCanvas.transform);
        DamageText damageText = obj.GetComponent<DamageText>();
        damageText.Init(damage, damageType, isCritical);
    }
}
