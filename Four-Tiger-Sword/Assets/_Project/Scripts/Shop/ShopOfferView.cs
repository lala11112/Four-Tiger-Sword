using System;
using BoundaryZero.Inventory;
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
    [SerializeField] private UnityEngine.UI.Image cardBackground, elementBadge;
    [SerializeField] private TMP_Text gradeLabel;
    [SerializeField] private Sprite normalBackground, selectedBackground;
    [SerializeField] private Sprite[] elementIcons;
    private string _id;
    private Action<string> _purchase;
    private Color? _normalGradeColor;

    public void Configure(UnityEngine.UI.Image image, TMP_Text nameText, TMP_Text body,
        TMP_Text cost, TMP_Text actionText, UnityEngine.UI.Button button)
    { icon = image; title = nameText; description = body; price = cost; buttonLabel = actionText; purchaseButton = button; }

    public void ConfigureCardVisuals(UnityEngine.UI.Image background, Sprite normal, Sprite selected,
        TMP_Text grade, UnityEngine.UI.Image badge, Sprite[] elements)
    { cardBackground = background; normalBackground = normal; selectedBackground = selected;
        gradeLabel = grade; elementBadge = badge; elementIcons = elements; }

    private void Awake()
    { if (purchaseButton != null) { purchaseButton.onClick.RemoveListener(Purchase); purchaseButton.onClick.AddListener(Purchase); } }
    private void OnDestroy() { if (purchaseButton != null) purchaseButton.onClick.RemoveListener(Purchase); }
    private void Purchase() { if (purchaseButton.interactable) _purchase?.Invoke(_id); }

    public void Render(ShopOfferViewState state, Action<string> purchase)
    {
        _id = state.Id;
        _purchase = purchase;
        title.text = gradeLabel != null ? state.Name : state.Grade > 0 ? $"{state.Name}  ·  {state.Grade}등급" : state.Name;
        description.text = gradeLabel != null ? string.Empty : state.Description;
        price.text = gradeLabel != null ? string.Empty : state.Price + " G";
        icon.sprite = state.Icon;
        icon.color = state.Icon != null ? Color.white : Color.clear;
        buttonLabel.text = gradeLabel != null ? string.Empty : state.ButtonLabel;
        purchaseButton.interactable = gradeLabel != null || state.CanBuy;
        Card(state.Selected, state.Element, state.Grade, false, state.PrimarySynergyName, state.PrimarySynergyIcon);
    }

    public void RenderOwned(ShopOwnedItemViewState state, Action<string> select, bool selling)
    {
        _id = state.Id;
        _purchase = select;
        title.text = gradeLabel != null ? state.Name : $"{state.Name}  ·  {state.Grade}등급";
        description.text = gradeLabel != null ? string.Empty : state.Description ?? string.Empty;
        price.text = gradeLabel != null ? string.Empty : selling ? state.CanSell ? state.SalePrice + " G" : "판매 불가" : string.Empty;
        icon.sprite = state.Icon;
        icon.color = state.Icon != null ? Color.white : Color.clear;
        buttonLabel.text = gradeLabel != null ? string.Empty : state.Selected ? "선택됨" : "선택";
        purchaseButton.interactable = state.CanSelect;
        Card(state.Selected, state.Element, state.Grade, state.IsMaterial, state.PrimarySynergyName, state.PrimarySynergyIcon);
    }

    private void Card(bool selected, ElementType element, int grade, bool material, string synergyName, Sprite synergyIcon)
    {
        if (cardBackground != null) cardBackground.sprite = selected ? selectedBackground : normalBackground;
        if (transform is RectTransform card) ItemRarityFrame.Show(card, grade);
        if (gradeLabel != null)
        {
            gradeLabel.text = (!string.IsNullOrEmpty(synergyName)
                ? ItemDisplayText.Meta(grade, synergyName) : $"{grade}등급 · {ElementName(element)}") + (material ? " · 재료" : string.Empty);
            _normalGradeColor ??= gradeLabel.color;
            var rarity = ItemRarityFrame.ColorFor(grade);
            gradeLabel.color = rarity.a > 0f ? rarity : _normalGradeColor.Value;
        }
        int index = element switch { ElementType.ELEMENT_FIRE => 0, ElementType.ELEMENT_WATER => 1,
            ElementType.ELEMENT_WOOD => 2, ElementType.ELEMENT_GOLD => 3, ElementType.ELEMENT_EARTH => 4, _ => -1 };
        if (elementBadge != null)
        { elementBadge.sprite = synergyIcon != null ? synergyIcon : index >= 0 && elementIcons != null && index < elementIcons.Length ? elementIcons[index] : null;
            elementBadge.color = elementBadge.sprite != null ? Color.white : Color.clear; }
    }

    public static string ElementName(ElementType element) => element switch
    { ElementType.ELEMENT_FIRE => "화", ElementType.ELEMENT_WATER => "수", ElementType.ELEMENT_WOOD => "목",
        ElementType.ELEMENT_GOLD => "금", ElementType.ELEMENT_EARTH => "토", _ => "공통" };
}
