namespace AssetInventory.Automation
{
    internal static class DownloadAutomation
    {
        internal sealed class DownloadPackageRequest
        {
            public int PackageId { get; set; }
            public bool DryRun { get; set; } = true;
            public string ConfirmationToken { get; set; }
        }

        internal sealed class DownloadProgressRequest
        {
            public int PackageId { get; set; }
        }

        internal static AutomationResponse DownloadPackage(DownloadPackageRequest request)
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
            if (info.IsDownloaded)
            {
                return AutomationResponse.Success($"Package '{info.GetDisplayName()}' is already downloaded.", new {state = "Downloaded", isAlreadyDownloaded = true});
            }

            AssetDownloader downloader = info.PackageDownloader ?? new AssetDownloader(info);
            if (!downloader.IsDownloadSupported())
            {
                return AutomationResponse.Error($"Download is not supported for package '{info.GetDisplayName()}' (source: {info.AssetSource}).", errorCode: "unsupported_operation");
            }

            AutomationResponse confirmation = AutomationMutationGuard.RequireConfirmation(
                "download_package",
                request.DryRun,
                request.ConfirmationToken,
                new {packageId = request.PackageId, packageName = info.GetDisplayName(), source = info.AssetSource.ToString(), packageSize = info.PackageSize},
                request.PackageId.ToString(), info.GetDisplayName(), info.AssetSource.ToString(), info.PackageSize.ToString());
            if (confirmation != null) return confirmation;

            info.PackageDownloader = downloader;
            downloader.Download(true);
            return AutomationResponse.Success($"Download started for package '{info.GetDisplayName()}'. Use the download progress command to monitor it.", new
            {
                state = "Downloading",
                isAlreadyDownloaded = false,
                packageSize = info.PackageSize
            });
        }

        internal static AutomationResponse GetDownloadProgress(DownloadProgressRequest request)
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
            if (info.IsDownloaded)
            {
                return AutomationResponse.Success($"Package '{info.GetDisplayName()}' is downloaded.", new
                {
                    state = "Downloaded",
                    progress = 1.0f,
                    bytesDownloaded = info.PackageSize,
                    bytesTotal = info.PackageSize
                });
            }
            if (info.PackageDownloader == null)
            {
                return AutomationResponse.Success($"No download in progress for package '{info.GetDisplayName()}'.", new
                {
                    state = "None",
                    progress = 0f,
                    bytesDownloaded = 0L,
                    bytesTotal = info.PackageSize
                });
            }

            info.PackageDownloader.RefreshState();
            AssetDownloadState downloadState = info.PackageDownloader.GetState();
            return AutomationResponse.Success($"Package '{info.GetDisplayName()}': {downloadState.state} ({downloadState.progress:P0}).", new
            {
                state = downloadState.state.ToString(),
                progress = downloadState.progress,
                bytesDownloaded = downloadState.bytesDownloaded,
                bytesTotal = downloadState.bytesTotal
            });
        }
    }
}
