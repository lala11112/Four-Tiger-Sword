using System;
using UnityEngine;

/// <summary>Presentation contract. Views never mutate the wallet, inventory or stock.</summary>
public interface IShopView
{
    event Action<string> PurchaseRequested;
    event Action CloseRequested;
    void Render(ShopViewState state);
}

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
}

public sealed class ShopOfferViewState
{
    public string Id, Name, Description, ButtonLabel;
    public Sprite Icon;
    public int Price;
    public bool CanBuy;
}
