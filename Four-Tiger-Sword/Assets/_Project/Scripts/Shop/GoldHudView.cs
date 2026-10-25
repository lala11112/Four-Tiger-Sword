using TMPro;
using UnityEngine;

public sealed class GoldHudView : MonoBehaviour, IGoldView
{
    [SerializeField] private CanvasGroup visibility;
    [SerializeField] private TMP_Text goldText;
    public void Configure(CanvasGroup group, TMP_Text label) { visibility = group; goldText = label; }
    public void Render(int gold, bool visible)
    {
        goldText.text = gold.ToString("N0") + " G";
        visibility.alpha = visible ? 1f : 0f;
        visibility.interactable = visibility.blocksRaycasts = false;
    }
}
