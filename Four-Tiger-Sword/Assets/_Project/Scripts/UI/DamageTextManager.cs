using UnityEngine;
using UnityEngine.Pool;

public class DamageTextManager : MonoBehaviour
{
    public static DamageTextManager Instance;

    [SerializeField] private GameObject damageTextPrefab;
    [SerializeField] private Canvas worldSpaceCanvas;

    [SerializeField] private int defaultPoolCapacity = 10;
    [SerializeField] private int maxPoolSize = 20;

    private ObjectPool<DamageText> _pool;

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
            return;
        }

        _pool = new ObjectPool<DamageText>(
            createFunc: CreateDamageText,
            actionOnGet: dt => dt.gameObject.SetActive(true),
            actionOnRelease: dt => dt.gameObject.SetActive(false),
            actionOnDestroy: dt => Destroy(dt.gameObject),
            collectionCheck: false,
            defaultCapacity: defaultPoolCapacity,
            maxSize: maxPoolSize
        );
    }

    private DamageText CreateDamageText()
    {
        GameObject go = Instantiate(damageTextPrefab, worldSpaceCanvas.transform);
        return go.GetComponent<DamageText>();
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

    public void ShowDamageText(float damage, Vector3 worldPosition, ElementType damageType = ElementType.ELEMENT_NONE, bool isCritical = false)
    {
        Vector3 offset = new Vector3(Random.Range(-0.5f, 0.5f), 1.5f, Random.Range(-0.5f, 0.5f));

        DamageText dt = _pool.Get();
        dt.transform.position = worldPosition + offset;

        dt.Init(Mathf.RoundToInt(damage), damageType, isCritical, () => _pool.Release(dt));
    }
}
