using UnityEngine;
using TMPro;
using System.Collections;

public class DamageText : MonoBehaviour
{
    public TextMeshProUGUI textMesh;
    public float speed = 1.5f;
    public float fadeSpeed = 1.5f;
    public float lifeTime = 3f;

    private Color originalColor;

    public void Init(int Damage)
    {
        textMesh.text = Damage.ToString();
        originalColor = textMesh.color;
        StartCoroutine(AnimateText());
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
