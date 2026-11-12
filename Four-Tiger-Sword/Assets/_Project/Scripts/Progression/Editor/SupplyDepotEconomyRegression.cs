using System;
using System.IO;
using BoundaryZero.SupplyDepot;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class SupplyDepotEconomyRegression
{
    private const string ItemCatalogPath = "Assets/_Project/Generated/BoundaryInventory/ItemCatalog.asset";
    private const string DepotCatalogPath = "Assets/_Project/Generated/BoundaryInventory/HubSupplyDepotCatalog.asset";

    [MenuItem("Tools/Studio Baekho/Progression/Run Supply Depot Economy Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        var settings = Resources.Load<PlayerUpgradeSettingsSO>("PlayerUpgradeSettings");
        var items = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemCatalogPath);
        var depot = AssetDatabase.LoadAssetAtPath<SupplyDepotCatalog>(DepotCatalogPath);
        Check(settings != null && items != null && depot != null, "Production depot and upgrade assets exist.");
        Check(depot.price == 100 && depot.items.Count > 0 && depot.items.Count == items.items.Length,
            "The depot costs 100 and covers the live item catalog.");

        string itemId = depot.items[0].id;
        VerifyAtomicPurchase(settings, itemId);
        VerifyAdapter(settings, items, depot, itemId);
        VerifyLegacySaves(settings, itemId);
        return "Supply depot economy: atomic purchase, live catalog adapter, and legacy save round trip passed.";
    }

    private static void VerifyAtomicPurchase(PlayerUpgradeSettingsSO settings, string itemId)
    {
        var store = new MemoryStore(FundedProfile(100));
        var service = new PlayerUpgradeService(settings, store);
        Check(service.IsReady, "The funded account loads.");
        Check(!service.TryPurchaseSupplyDepot(itemId, 101, out _, out _)
            && !service.TryPurchaseSupplyDepot(" ", 100, out _, out _)
            && !service.TryPurchaseSupplyDepot(itemId, -1, out _, out _)
            && store.SaveCount == 0, "Invalid or unaffordable purchases do not write.");

        int changes = 0;
        bool committedBeforeNotification = false;
        bool reentryRejected = false;
        Action observer = () =>
        {
            changes++;
            var current = service.CurrentSupplyDepotReceipt;
            committedBeforeNotification = service.Balance == 0 && store.Profile.currency == 0 && current?.itemId == itemId
                && current.transactionId == store.Profile.supplyDepotTransactionId;
            reentryRejected = !service.TryClearSupplyDepotReceipt(current?.transactionId, out _);
        };
        service.Changed += observer;
        store.FailSaves = true;
        Check(!service.TryPurchaseSupplyDepot(itemId, 100, out var failed, out _)
            && failed == null && service.CurrentSupplyDepotReceipt == null && service.Balance == 100
            && store.Profile.currency == 100 && changes == 0, "A failed write spends nothing and publishes no receipt.");
        store.FailSaves = false;
        Check(service.TryPurchaseSupplyDepot(itemId, 100, out var receipt, out _)
            && receipt?.itemId == itemId && committedBeforeNotification && reentryRejected && changes == 1,
            "The exact balance can be spent once and observers see the fully committed transaction.");
        service.Changed -= observer;

        string transaction = receipt.transactionId;
        receipt.itemId = "changed-by-caller";
        service.CurrentSupplyDepotReceipt.transactionId = "changed-by-caller";
        Check(service.CurrentSupplyDepotReceipt.transactionId == transaction && service.CurrentSupplyDepotReceipt.itemId == itemId,
            "Callers cannot change the saved receipt through a returned object.");
        Check(!service.TryPurchaseSupplyDepot(itemId, 0, out _, out _)
            && !service.TryClearSupplyDepotReceipt("another-transaction", out _) && store.SaveCount == 1,
            "A receipt blocks another purchase and only its matching transaction can clear it.");
        store.FailSaves = true;
        Check(!service.TryClearSupplyDepotReceipt(transaction, out _) && service.CurrentSupplyDepotReceipt?.transactionId == transaction,
            "A failed end-of-run write retains the receipt for retry.");
        store.FailSaves = false;
        Check(service.TryClearSupplyDepotReceipt(transaction, out _) && service.TryClearSupplyDepotReceipt(transaction, out _)
            && service.CurrentSupplyDepotReceipt == null && service.Balance == 0 && store.SaveCount == 2,
            "Run completion clears the receipt once without refunding the price.");
    }

    private static void VerifyAdapter(PlayerUpgradeSettingsSO settings, ItemCatalog items, SupplyDepotCatalog depot, string itemId)
    {
        var store = new MemoryStore(FundedProfile(200)) { FailLoads = true };
        var service = new PlayerUpgradeService(settings, store);
        WithAccount(service, items, depot, account =>
        {
            Check(!account.IsReady && account.PermanentCurrency == 0 && !string.IsNullOrEmpty(account.LastError),
                "A load failure disables purchase and provides a status message.");
            store.FailLoads = false;
            account.RefreshAccount();
            Check(account.IsReady && account.LastError == null && account.PermanentCurrency == 200,
                "Opening again retries a failed load and reflects the shared wallet.");
            Check(!account.TryPurchase("missing-item", 100, out _) && !account.TryPurchase(itemId, 99, out _)
                && store.SaveCount == 0 && !string.IsNullOrEmpty(account.LastError),
                "The adapter rejects missing runtime IDs and prices other than the configured price.");
            account.RefreshAccount();
            Check(account.LastError == null, "Reopening clears a previous purchase error.");

            bool observedPurchase = false;
            account.Changed += () => observedPurchase = account.PermanentCurrency == 100 && account.CurrentReceipt?.itemId == itemId;
            Check(account.TryPurchase(itemId, 100, out var receipt) && receipt?.itemId == itemId
                && account.PermanentCurrency == 100 && observedPurchase && account.LastError == null,
                "The production adapter forwards the committed balance and receipt.");
            account.RefreshAccount();
            Check(account.CurrentReceipt?.transactionId == receipt.transactionId
                && !account.TryPurchase(itemId, 100, out _) && account.PermanentCurrency == 100,
                "Reopening restores the same purchase and does not permit a second draw.");
        });

        var retired = FundedProfile(200);
        retired.supplyDepotItemId = "retired-test-item";
        retired.supplyDepotTransactionId = Guid.NewGuid().ToString("N");
        var retiredStore = new MemoryStore(retired);
        WithAccount(new PlayerUpgradeService(settings, retiredStore), items, depot, account =>
        {
            Check(account.CurrentReceipt?.itemId == retired.supplyDepotItemId && !string.IsNullOrEmpty(account.LastError),
                "A saved receipt missing from the live catalog remains visible to the account with an explanatory status.");
            Check(!account.TryPurchase(itemId, 100, out _) && retiredStore.SaveCount == 0,
                "A missing saved item cannot enable a second purchase.");
        });
    }

    private static void VerifyLegacySaves(PlayerUpgradeSettingsSO settings, string itemId)
    {
        string[] profiles =
        {
            "{\"version\":1,\"currency\":100,\"healthLevel\":0,\"attackLevel\":0,\"defenseLevel\":0}",
            "{\"version\":2,\"currency\":100,\"healthLevel\":0,\"attackLevel\":0,\"defenseLevel\":0,\"unlockedNodeIds\":[]}"
        };
        foreach (string json in profiles)
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, json);
                var service = new PlayerUpgradeService(settings, new PlayerUpgradeFileStore(path));
                Check(service.IsReady && service.CurrentSupplyDepotReceipt == null && service.Balance == 100,
                    "An older profile loads with no depot receipt.");
                Check(service.TryPurchaseSupplyDepot(itemId, 100, out var receipt, out _), "Purchase saves to the existing profile file.");
                var reopened = new PlayerUpgradeService(settings, new PlayerUpgradeFileStore(path));
                Check(reopened.IsReady && reopened.Balance == 0 && reopened.CurrentSupplyDepotReceipt?.itemId == itemId
                    && reopened.CurrentSupplyDepotReceipt.transactionId == receipt.transactionId,
                    "A fresh account instance restores both the spent balance and the same entitlement.");
                Check(reopened.TryClearSupplyDepotReceipt(receipt.transactionId, out _), "Clearing the receipt updates the existing profile file.");
                var afterRun = new PlayerUpgradeService(settings, new PlayerUpgradeFileStore(path));
                Check(afterRun.IsReady && afterRun.Balance == 0 && afterRun.CurrentSupplyDepotReceipt == null,
                    "The cleared receipt stays cleared across a restart.");
            }
            finally
            {
                File.Delete(path);
                File.Delete(path + ".bak");
            }
        }
        var incomplete = FundedProfile(100);
        incomplete.supplyDepotItemId = itemId;
        Check(!incomplete.Validate(out _), "A partial receipt is rejected as corrupt.");
    }

    private static void WithAccount(PlayerUpgradeService service, ItemCatalog items, SupplyDepotCatalog depot,
        Action<HubSupplyDepotAccount> verify)
    {
        using var scope = PlayerUpgradeService.OverrideForTesting(service);
        var host = new GameObject("Supply depot account regression") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            host.SetActive(false);
            var account = host.AddComponent<HubSupplyDepotAccount>();
            account.Configure(items, depot);
            host.SetActive(true);
            account.RefreshAccount();
            verify(account);
        }
        finally { Object.DestroyImmediate(host); }
    }

    private static PlayerUpgradeProfile FundedProfile(int amount)
    {
        var profile = PlayerUpgradeProfile.New();
        profile.currency = amount;
        return profile;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Supply depot economy regression: " + message);
    }

    private sealed class MemoryStore : IPlayerUpgradeStore
    {
        public PlayerUpgradeProfile Profile { get; private set; }
        public int SaveCount { get; private set; }
        public bool FailLoads, FailSaves;

        public MemoryStore(PlayerUpgradeProfile profile) => Profile = profile.Copy();

        public bool TryLoad(out PlayerUpgradeProfile profile, out string error)
        {
            profile = FailLoads ? null : Profile.Copy();
            error = FailLoads ? "Simulated load failure." : null;
            return !FailLoads;
        }

        public bool TrySave(PlayerUpgradeProfile profile, out string error)
        {
            error = FailSaves ? "Simulated save failure." : null;
            if (FailSaves) return false;
            Profile = profile.Copy();
            SaveCount++;
            return true;
        }
    }
}
