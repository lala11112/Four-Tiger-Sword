using System;
using System.Collections.Generic;
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
    private double _earnedGoldRemainder;
    private readonly Queue<(int amount, double ratio)> _pendingRoomGold = new();
    private bool _payingRoomGold;

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
        CommitGoldCredit(amount);
        return true;
    }

    /// <summary>Applies the run's gold gain rate to the base reward. Generated bonuses are credited once.</summary>
    public bool TryAddEarnedGold(int baseAmount, int generatedBonus = 0)
        => TryAddEarnedGold(baseAmount, generatedBonus, EarnedGoldRatio);

    private double EarnedGoldRatio => TryGetComponent(out PlayerItemRuntime items) ? items.EarnedGoldRatio : 0d;

    private bool TryAddEarnedGold(int baseAmount, int generatedBonus, double ratio)
    {
        if (!IsInitialized || _transactionInProgress || baseAmount < 0 || generatedBonus < 0) return false;
        double accrued = _earnedGoldRemainder + baseAmount * ratio;
        double bonus = Math.Floor(accrued + 0.000001d);
        long baseCredit = (long)baseAmount + generatedBonus;
        if (baseCredit + bonus > int.MaxValue - Gold) return false;

        _earnedGoldRemainder = Math.Max(0d, accrued - bonus);
        int credit = (int)(baseCredit + bonus);
        if (credit > 0) CommitGoldCredit(credit);
        return true;
    }

    internal void AwardCombatRoomGold()
    {
        if (!TryGetComponent(out PlayerItemRuntime items)) return;
        int amount = items.CombatRoomClearGold;
        if (amount == 0) return;
        _pendingRoomGold.Enqueue((amount, items.EarnedGoldRatio));
        PayPendingRoomGold();
    }

    private void LateUpdate()
    {
        if (_pendingRoomGold.Count > 0) PayPendingRoomGold();
    }

    internal void FinishPendingRoomGold()
    {
        PayPendingRoomGold();
        _pendingRoomGold.Clear();
    }

    private void PayPendingRoomGold()
    {
        if (_payingRoomGold) return;
        _payingRoomGold = true;
        try
        {
            while (_pendingRoomGold.Count > 0)
            {
                var reward = _pendingRoomGold.Peek();
                if (!TryAddEarnedGold(reward.amount, 0, reward.ratio)) return;
                // Run settlement may have cleared the queue from a gold observer.
                if (_pendingRoomGold.Count > 0) _pendingRoomGold.Dequeue();
            }
        }
        finally { _payingRoomGold = false; }
    }

    private void CommitGoldCredit(int amount)
    {
        Gold += amount;
        NotifyGoldChanged();
        NotifyGoldGained(amount);
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
