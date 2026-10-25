using System;
using TMPro;
using UnityEngine;

public sealed class ShopOfferView : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.Image icon;
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text description;
    [SerializeField] private TMP_Text price;
    [SerializeField] private TMP_Text buttonLabel;
    [SerializeField] private UnityEngine.UI.Button purchaseButton;
    private string _id;
    private Action<string> _purchase;

    public void Configure(UnityEngine.UI.Image image, TMP_Text nameText, TMP_Text body,
        TMP_Text cost, TMP_Text actionText, UnityEngine.UI.Button button)
    { icon = image; title = nameText; description = body; price = cost; buttonLabel = actionText; purchaseButton = button; }

    private void Awake() => purchaseButton.onClick.AddListener(Purchase);
    private void OnDestroy() { if (purchaseButton != null) purchaseButton.onClick.RemoveListener(Purchase); }
    private void Purchase() { if (purchaseButton.interactable) _purchase?.Invoke(_id); }

    public void Render(ShopOfferViewState state, Action<string> purchase)
    {
        _id = state.Id;
        _purchase = purchase;
        title.text = state.Name;
        description.text = state.Description;
        price.text = state.Price + " G";
        icon.sprite = state.Icon;
        icon.color = state.Icon != null ? Color.white : new Color(.32f, .66f, .57f, .65f);
        buttonLabel.text = state.ButtonLabel;
        purchaseButton.interactable = state.CanBuy;
    }
}
