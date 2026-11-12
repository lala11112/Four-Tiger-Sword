#if UNITY_EDITOR
using System;
using UnityEngine;

namespace BoundaryZero.SupplyDepot
{
    public static class SupplyDepotTransactionCheck
    {
        [UnityEditor.MenuItem("Tools/Boundary Zero/Verify Supply Depot Transactions")]
        public static void Run()
        {
            var fixture = new GameObject("Supply Depot Transaction Check");
            try
            {
                var account = fixture.AddComponent<DemoSupplyDepotAccount>();
                account.startingBalance = 200;
                account.startingItemCounts.Add(new DemoSupplyDepotAccount.ItemCount { itemId = "I01", count = 2 });
                account.startingItemCounts.Add(new DemoSupplyDepotAccount.ItemCount { itemId = "FULL", count = int.MaxValue });

                int changes = 0;
                bool observedCommit = false;
                bool reentrantDenied = false;
                account.Changed += () =>
                {
                    changes++;
                    if (changes != 1) return;
                    observedCommit = account.PermanentCurrency == 100 && account.GetItemCount("I01") == 3 &&
                        account.CurrentReceipt?.itemId == "I01";
                    reentrantDenied = !account.TryPurchase("I02", 100, out _);
                };

                Require(!account.TryPurchase("I01", 201, out _), "insufficient currency must fail");
                Require(!account.TryPurchase("I01", -1, out _), "negative cost must fail");
                Require(!account.TryPurchase("FULL", 100, out _), "full item stack must fail");
                Require(account.PermanentCurrency == 200 && account.GetItemCount("I01") == 2 &&
                    account.CurrentReceipt == null && changes == 0, "failed purchase mutated account state");

                Require(account.TryPurchase("I01", 100, out var first), "first purchase did not commit");
                Require(observedCommit && reentrantDenied && changes == 1, "change observer did not see atomic commit");
                Require(!account.TryPurchase("I02", 100, out _), "second purchase in the run committed");
                Require(account.GetItemCount("I02") == 0 && account.PermanentCurrency == 100 && changes == 1,
                    "second attempt changed currency or inventory");
                var firstId = first.transactionId;
                first.itemId = "I02";
                Require(account.CurrentReceipt?.itemId == "I01", "mutating a receipt altered the stored result");

                account.ResetForNextRun();
                Require(account.CurrentReceipt == null && account.PermanentCurrency == 100 &&
                    account.GetItemCount("I01") == 3 && changes == 2, "reset must only clear the run receipt");
                account.ResetForNextRun();
                Require(changes == 2, "repeated reset emitted a redundant change");
                Require(account.TryPurchase("I02", 100, out var second), "new run was not eligible");
                Require(second.transactionId != firstId && account.PermanentCurrency == 0 &&
                    account.GetItemCount("I02") == 1 && changes == 3, "new run did not commit a distinct purchase");
                Debug.Log("Supply Depot transaction verification passed.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fixture);
            }
        }

        static void Require(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException("Supply Depot transaction check: " + detail);
        }
    }
}
#endif
