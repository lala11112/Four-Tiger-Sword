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
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void ShowDamageText(int Damage, Vector3 worldPosition)
    {
        Vector3 offset = new Vector3(0f, 1f, 0f);
        GameObject obj = Instantiate(damageTextPrefab, worldPosition + offset, Quaternion.identity, worldSpaceCanvas.transform);
        DamageText damageText = obj.GetComponent<DamageText>();
        damageText.Init(Damage);
    }
}
