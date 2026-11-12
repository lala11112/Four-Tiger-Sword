public sealed partial class RunManager
{
    private string _appliedSupplyDepotTransactionId;
    private bool _supplyDepotClearRequested;

    private bool HasPendingSupplyDepotClear =>
        _supplyDepotClearRequested && _appliedSupplyDepotTransactionId != null;

    private bool TryApplySupplyDepotItem(out string error)
    {
        error = null;
        if (_appliedSupplyDepotTransactionId != null) return true;
        var receipt = UpgradeService.CurrentSupplyDepotReceipt;
        if (receipt == null) return true;

        var items = player != null ? player.GetComponent<PlayerItemRuntime>() : null;
        if (items != null && items.isActiveAndEnabled)
        {
            // Reserve this receipt before acquisition observers run synchronously.
            _appliedSupplyDepotTransactionId = receipt.transactionId;
            if (items.TryAddItem(receipt.itemId)) return true;
            _appliedSupplyDepotTransactionId = null;
        }

        error = "보급품을 지급하지 못했습니다. 보급 기록은 보관 중입니다. 다시 시도해 주세요.";
        return false;
    }

    private bool TryClearAppliedSupplyDepotReceipt(out string error)
    {
        error = null;
        if (!HasPendingSupplyDepotClear) return true;
        if (!UpgradeService.TryClearSupplyDepotReceipt(_appliedSupplyDepotTransactionId, out error)) return false;
        _supplyDepotClearRequested = false;
        return true;
    }

    // Explicit exits use the same save retry path as run settlement.
    private bool TrySaveSupplyDepotExit()
    {
        if (_appliedSupplyDepotTransactionId == null) return true;
        _supplyDepotClearRequested = true;
        if (TrySaveUpgradeRewards(out string error)) return true;
        BlockUpgradeSaving(error);
        return false;
    }
}
