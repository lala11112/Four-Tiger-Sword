using UnityEngine;
using TMPro;
using System;
using System.Collections;

public class DamageText : MonoBehaviour
{
    public TextMeshProUGUI textMesh;
    public float speed = 0.05f;
    public float fadeSpeed = 1.5f;
    public float lifeTime = 0.7f;

    public float normalFontSize = 36f;
    public float criticalFontSize = 54f;

    private Color originalColor;
    private Action _returnToPool;

    public void Init(int damage, ElementType damageType = ElementType.ELEMENT_NONE, bool isCritical = false, Action returnToPool = null)
    {
        _returnToPool = returnToPool;

        textMesh.text = isCritical ? $"!{damage}!" : damage.ToString();
        textMesh.fontSize = isCritical ? criticalFontSize : normalFontSize;
        textMesh.color = GetDamageColor(damageType);
        originalColor = textMesh.color;

        textMesh.fontMaterial.SetFloat("_ZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);

        StartCoroutine(AnimateText());
    }

    private Color GetDamageColor(ElementType damageType)
    {
        return damageType switch
        {
            ElementType.ELEMENT_FIRE  => new Color(1f, 0.35f, 0f),
            ElementType.ELEMENT_WATER => new Color(0.2f, 0.6f, 1f),
            ElementType.ELEMENT_WOOD  => new Color(0.2f, 0.85f, 0.2f),
            ElementType.ELEMENT_EARTH => new Color(0.85f, 0.65f, 0.15f),
            ElementType.ELEMENT_GOLD  => new Color(0.75f, 0.75f, 0.85f),
            _                => Color.white,
        };
    }

    private IEnumerator AnimateText()
    {
        float elapsed = 0f;

        while (elapsed < lifeTime)
        {
            elapsed += Time.deltaTime;

            transform.position += Vector3.down * speed * Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / lifeTime);
            textMesh.color = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);

            yield return null;
        }

        _returnToPool?.Invoke();
    }
}
