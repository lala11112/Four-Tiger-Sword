using System;
using UnityEngine;

/// <summary>Presentation contract. Views never mutate the wallet, inventory or stock.</summary>
public interface IShopView
{
    event Action<string> PurchaseRequested;
    event Action CloseRequested;
    event Action<ShopTab> TabRequested;
    event Action<string> ItemSelected;
    event Action<string> OfferSelected;
    event Action<string> FusionMaterialToggled;
    event Action<string> SellRequested;
    event Action FusionRequested;
    event Action<int> FusionSlotCleared;
    void Render(ShopViewState state);
}

/// <summary>Optional presentation-only keyboard routing for an authored shop layout.</summary>
public interface IShopLayoutView
{
    bool TryHandleBack();
    void ToggleBuyInventory();
}

/// <summary>Optional new actions keep existing shop adapters compatible.</summary>
public interface IShopProgressionView
{
    event Action RefreshRequested;
    event Action<string> FusionResultSelected;
    event Action FusionChoiceCancelled;
}

/// <summary>Optional motion contract. The owner keeps its pause until the close callback completes.</summary>
public interface IAnimatedModalView
{
    event Action Disabled;
    bool IsAvailable { get; }
    bool IsInteractionReady { get; }
    void PlayClose(Action completed);
    void HideImmediate();
}

public enum ShopTab { Buy, Sell, Fuse }
public enum ShopDetailSource { None, Offer, Owned }

public interface IGoldView
{
    void Render(int gold, bool visible);
}

public sealed class ShopViewState
{
    public bool Visible;
    public bool ShowInteractionPrompt;
    public int Gold;
    public float Hp, MaxHp;
    public string Message;
    public ShopOfferViewState[] Offers = Array.Empty<ShopOfferViewState>();
    public ShopTab Tab;
    public ShopOwnedItemViewState[] OwnedItems = Array.Empty<ShopOwnedItemViewState>();
    public ShopOwnedItemViewState SelectedSale, FusionFirst, FusionSecond, FusionResult;
    public ShopOfferViewState SelectedOffer;
    public ShopOwnedItemViewState SelectedOwned;
    public ShopDetailSource DetailSource;
    public bool CanSell, CanFuse;
    public bool UsesSynergyCatalog;
    public bool CanRefresh, FusionChoiceActive;
    public int RefreshCost, RefreshCount;
    public ShopOwnedItemViewState[] FusionCandidates = Array.Empty<ShopOwnedItemViewState>();
    public string FusionOddsText, EmptyMessage;
}

public sealed class ShopOfferViewState
{
    public string Id, Name, Description, ButtonLabel;
    public Sprite Icon;
    public string PrimarySynergyName;
    public Sprite PrimarySynergyIcon;
    public ElementType Element;
    public int Price, Grade;
    public bool CanBuy;
    public bool Selected, IsHealing;
}

public sealed class ShopOwnedItemViewState
{
    public string Id, Name, Description;
    public int Grade, SalePrice;
    public Sprite Icon;
    public string PrimarySynergyName;
    public Sprite PrimarySynergyIcon;
    public ElementType Element;
    public bool CanSelect, Selected, CanSell;
    public bool IsMaterial, CanAddMaterial;
}
