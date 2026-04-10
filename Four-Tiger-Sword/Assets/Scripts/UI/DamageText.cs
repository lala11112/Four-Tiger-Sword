using UnityEngine;
using TMPro;
using System.Collections;

public class DamageText : MonoBehaviour
{
    public TextMeshProUGUI textMesh;
    public float speed = 0.5f;
    public float fadeSpeed = 1.5f;
    public float lifeTime = 0.7f;

    public float normalFontSize = 36f;
    public float criticalFontSize = 54f;

    private Color originalColor;

    public void Init(int damage, DamageType damageType = DamageType.Normal, bool isCritical = false)
    {
        textMesh.text = isCritical ? $"!{damage}!" : damage.ToString();
        textMesh.fontSize = isCritical ? criticalFontSize : normalFontSize;
        textMesh.color = GetDamageColor(damageType);
        originalColor = textMesh.color;
        StartCoroutine(AnimateText());
    }

    private Color GetDamageColor(DamageType damageType)
    {
        return damageType switch
        {
            DamageType.Fire  => new Color(1f, 0.35f, 0f),
            DamageType.Water => new Color(0.2f, 0.6f, 1f),
            DamageType.Wood  => new Color(0.2f, 0.85f, 0.2f),
            DamageType.Earth => new Color(0.85f, 0.65f, 0.15f),
            DamageType.Iron  => new Color(0.75f, 0.75f, 0.85f),
            _                => Color.white,
        };
    }

    private IEnumerator AnimateText()
    {
        float elapsed = 0f;

        while(elapsed < lifeTime)
        {
            elapsed += Time.deltaTime;

            transform.LookAt(transform.position + Camera.main.transform.rotation * Vector3.forward);

            transform.position += Vector3.up * speed * Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / lifeTime);
            textMesh.color = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);

            yield return null;
        }

        Destroy(gameObject);
    }
}
