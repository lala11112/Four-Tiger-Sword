using System;

namespace BoundaryZero.SupplyDepot
{
    [Serializable]
    public sealed class SupplyDepotReceipt
    {
        public string transactionId;
        public string itemId;

        public SupplyDepotReceipt() { }

        public SupplyDepotReceipt(string transactionId, string itemId)
        {
            this.transactionId = transactionId;
            this.itemId = itemId;
        }
    }

    /// <summary>Game-owned transaction boundary. Call and notify on Unity's main thread.</summary>
    public interface ISupplyDepotAccount
    {
        int PermanentCurrency { get; }
        SupplyDepotReceipt CurrentReceipt { get; }
        event Action Changed;

        /// <summary>
        /// Atomically debit cost, grant itemId and store the current run receipt.
        /// Return false without changing any state when the purchase is refused.
        /// </summary>
        bool TryPurchase(string itemId, int cost, out SupplyDepotReceipt receipt);

        /// <summary>Clear the used flag and receipt for a new run without refunding the price.</summary>
        void ResetForNextRun();
    }
}
