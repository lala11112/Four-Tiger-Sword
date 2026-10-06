namespace AssetInventory.Automation
{
    internal static class PackageDetailsAutomation
    {
        internal sealed class PackageDetailsRequest
        {
            public int PackageId { get; set; }
        }

        internal static AutomationResponse GetPackageDetails(PackageDetailsRequest request)
        {
            AutomationResponse initError = AutomationResultHelper.EnsureInit();
            if (initError != null) return initError;
            if (request == null)
            {
                return AutomationResponse.Error("Request is required.", errorCode: "invalid_input");
            }

            AssetInfo info = Assets.GetPackage(request.PackageId);
            if (info == null)
            {
                return AutomationResponse.Error($"Package with ID {request.PackageId} not found.", errorCode: "not_found");
            }

            return AutomationResponse.Success($"Details for package '{info.GetDisplayName()}'.", new
            {
                package = AutomationResultHelper.ToPackageDetailResult(info)
            });
        }
    }
}
