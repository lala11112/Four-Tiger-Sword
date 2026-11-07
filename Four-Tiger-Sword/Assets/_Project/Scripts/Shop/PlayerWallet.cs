using System;
using UnityEngine;

/// <summary>Gold belongs to the player for the current run, independent of any shop or UI.</summary>
[DisallowMultipleComponent]
public sealed class PlayerWallet : MonoBehaviour
{
    public int Gold { get; private set; }
    public event Action<int> GoldChanged;
    public event Action<int> GoldGained;
    internal bool IsInitialized { get; private set; }
    private bool _transactionInProgress;

    /// <summary>Called once when a new run starts. Stage changes and retries must not reset gold.</summary>
    public void Initialize(int startingGold)
    {
        if (IsInitialized) return;
        if (startingGold < 0) throw new ArgumentOutOfRangeException(nameof(startingGold));
        IsInitialized = true;
        Gold = startingGold;
        NotifyGoldChanged();
    }

    /// <summary>Invalid amounts and integer overflow are rejected without changing the balance.</summary>
    public bool AddGold(int amount)
    {
        if (!IsInitialized || _transactionInProgress || amount < 0 || amount > int.MaxValue - Gold) return false;
        if (amount == 0) return true;
        Gold += amount;
        NotifyGoldChanged();
        NotifyGoldGained(amount);
        return true;
    }

    public bool TrySpend(int amount)
    {
        if (!IsInitialized || _transactionInProgress || amount < 0 || amount > Gold) return false;
        if (amount == 0) return true;
        Gold -= amount;
        NotifyGoldChanged();
        return true;
    }

    /// <summary>
    /// Fulfill before publishing payment. A rejected grant leaves gold untouched, so no refund
    /// can overflow or notify observers about a payment that did not actually complete.
    /// </summary>
    internal bool TryPurchase(int amount, Func<bool> fulfill)
    {
        if (!IsInitialized || _transactionInProgress || amount < 0 || amount > Gold || fulfill == null) return false;
        _transactionInProgress = true;
        try
        {
            if (!fulfill()) return false;
            Gold -= amount;
            if (amount != 0) NotifyGoldChanged();
            return true;
        }
        finally { _transactionInProgress = false; }
    }

    /// <summary>Preflights credit capacity before consuming the sold item; callbacks cannot reenter the wallet.</summary>
    internal bool TrySale(int amount, Func<bool> fulfill)
    {
        if (!IsInitialized || _transactionInProgress || amount < 0 || amount > int.MaxValue - Gold
            || fulfill == null) return false;
        _transactionInProgress = true;
        try
        {
            if (!fulfill()) return false;
            Gold += amount;
            if (amount != 0) NotifyGoldChanged();
            if (amount != 0) NotifyGoldGained(amount);
            return true;
        }
        finally { _transactionInProgress = false; }
    }

    private void NotifyGoldGained(int amount)
    {
        if (GoldGained == null) return;
        foreach (Action<int> callback in GoldGained.GetInvocationList())
        {
            try { callback(amount); }
            catch (Exception error) { Debug.LogWarning("[Shop] GoldGained observer failed: " + error, this); }
        }
    }

    private void NotifyGoldChanged()
    {
        if (GoldChanged == null) return;
        int balance = Gold;
        foreach (Action<int> callback in GoldChanged.GetInvocationList())
        {
            try { callback(balance); }
            catch (Exception error) { Debug.LogWarning("[Shop] GoldChanged observer failed: " + error, this); }
        }
    }
}
