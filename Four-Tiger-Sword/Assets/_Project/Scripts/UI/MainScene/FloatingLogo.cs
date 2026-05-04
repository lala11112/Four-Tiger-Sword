using UnityEngine;
using System.Collections;

public class FloatingLogo : MonoBehaviour
{
    [Header("Settings")]
    public float floatHeight = 20f;
    public float floatSpeed = 2f;
    public float duration = 4f;
    public float fadeInDuration = 1f;

    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private Vector2 startPos;
        void Start()
    {
        rectTransform = GetComponent<RectTransform>();
        startPos = rectTransform.anchoredPosition;

        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        canvasGroup.alpha = 0f; // 처음엔 안 보임
        StartCoroutine(FloatRoutine());
    }

    IEnumerator FloatRoutine()
    {
        float timer = 0f;

        while (timer < duration)
        {
            float t = 1f - (timer / duration);
            float damping = t * t * (3f - 2f * t);

            float yOffset = Mathf.Sin(Time.time * floatSpeed) * floatHeight * damping;

            // Fade in
            if (timer < fadeInDuration)
                canvasGroup.alpha = timer / fadeInDuration;
            else
                canvasGroup.alpha = 1f;

            rectTransform.anchoredPosition = new Vector2(startPos.x, startPos.y + yOffset);

            timer += Time.deltaTime;
            yield return null;
        }

        rectTransform.anchoredPosition = startPos;
    }
}