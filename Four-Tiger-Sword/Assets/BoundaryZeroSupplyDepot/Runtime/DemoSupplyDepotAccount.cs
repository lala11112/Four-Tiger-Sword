using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoundaryZero.SupplyDepot
{
    [DisallowMultipleComponent]
    public sealed class DemoSupplyDepotAccount : MonoBehaviour, ISupplyDepotAccount
    {
        [Serializable]
        public sealed class ItemCount
        {
            public string itemId;
            [Min(0)] public int count;
        }

        [Min(0)] public int startingBalance = 1200;
        public List<ItemCount> startingItemCounts = new List<ItemCount>();

        readonly Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
        bool initialized;
        bool notifying;
        int balance;
        SupplyDepotReceipt currentReceipt;

        public int PermanentCurrency { get { Initialize(); return balance; } }
        public SupplyDepotReceipt CurrentReceipt => CopyReceipt(currentReceipt);
        public event Action Changed;

        public int GetItemCount(string itemId)
        {
            Initialize();
            return itemId != null && counts.TryGetValue(itemId, out int count) ? count : 0;
        }

        public bool TryPurchase(string itemId, int cost, out SupplyDepotReceipt receipt)
        {
            Initialize();
            receipt = null;
            if (notifying || currentReceipt != null || string.IsNullOrWhiteSpace(itemId) || cost < 0 || balance < cost)
                return false;

            int currentCount = GetItemCount(itemId);
            if (currentCount == int.MaxValue)
                return false;

            var committed = new SupplyDepotReceipt(Guid.NewGuid().ToString("N"), itemId);
            receipt = CopyReceipt(committed);
            counts[itemId] = currentCount + 1;
            balance -= cost;
            currentReceipt = committed;
            RaiseChanged();
            return true;
        }

        public void ResetForNextRun()
        {
            if (notifying || currentReceipt == null) return;
            currentReceipt = null;
            RaiseChanged();
        }

        void Initialize()
        {
            if (initialized) return;
            initialized = true;
            balance = Mathf.Max(0, startingBalance);
            if (startingItemCounts == null) return;
            foreach (var item in startingItemCounts)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.itemId)) continue;
                counts.TryGetValue(item.itemId, out int previous);
                counts[item.itemId] = (int)Math.Min(int.MaxValue, (long)previous + Mathf.Max(0, item.count));
            }
        }

        void RaiseChanged()
        {
            if (Changed == null) return;
            notifying = true;
            try
            {
                foreach (Action handler in Changed.GetInvocationList())
                {
                    try { handler(); }
                    catch (Exception exception) { Debug.LogException(exception, this); }
                }
            }
            finally { notifying = false; }
        }

        static SupplyDepotReceipt CopyReceipt(SupplyDepotReceipt receipt) =>
            receipt == null ? null : new SupplyDepotReceipt(receipt.transactionId, receipt.itemId);
    }
}
