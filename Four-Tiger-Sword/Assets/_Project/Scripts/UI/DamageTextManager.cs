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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => Instance = null;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // The canvas and every pooled label belong to this scene. Keep their owner
            // in the same scene so a new run cannot inherit destroyed canvas/label references.
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        _pool = new ObjectPool<DamageText>(
            createFunc: CreateDamageText,
            actionOnGet: dt => { if (dt != null) dt.gameObject.SetActive(true); },
            actionOnRelease: dt => { if (dt != null) dt.gameObject.SetActive(false); },
            actionOnDestroy: dt => { if (dt != null) Destroy(dt.gameObject); },
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
        // Presentation must not interrupt damage/death notifications during scene teardown.
        if (_pool == null || worldSpaceCanvas == null || damageTextPrefab == null) return;
        Vector3 offset = new Vector3(Random.Range(-0.5f, 0.5f), 1.5f, Random.Range(-0.5f, 0.5f));

        var ownerPool = _pool;
        DamageText dt = ownerPool.Get();
        if (dt == null) return;
        dt.transform.position = worldPosition + offset;

        dt.Init(Mathf.RoundToInt(damage), damageType, isCritical, () =>
        {
            if (this != null && dt != null && object.ReferenceEquals(_pool, ownerPool)) ownerPool.Release(dt);
        });
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        _pool?.Dispose();
        _pool = null;
    }
}
