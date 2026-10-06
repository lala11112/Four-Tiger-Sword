using AssetInventory.Automation;
using Unity.Pipeline.Commands;

namespace AssetInventory.Integration.UnityPipeline
{
    public static class PipelineUtilityCommands
    {
        [CliCommand("asset_inventory_get_inventory_stats", "Get Asset Inventory database statistics. Read-only.", Tags = new[] {"asset-inventory"})]
        public static AssetInventoryCommandResult GetInventoryStats()
        {
            return PipelineResultAdapter.Convert(UtilityAutomation.GetInventoryStats());
        }

        [CliCommand("asset_inventory_open_window", "Open the Asset Inventory editor window.", Tags = new[] {"asset-inventory", "window"})]
        public static AssetInventoryCommandResult OpenWindow()
        {
            return PipelineResultAdapter.Convert(UtilityAutomation.OpenAssetInventory());
        }

        [CliCommand("asset_inventory_close_window", "Close the Asset Inventory editor window.", Tags = new[] {"asset-inventory", "window"})]
        public static AssetInventoryCommandResult CloseWindow()
        {
            return PipelineResultAdapter.Convert(UtilityAutomation.CloseAssetInventory());
        }

        [CliCommand("asset_inventory_check_asset_in_project", "Check whether an asset GUID exists in the current project. Read-only.", Tags = new[] {"asset-inventory", "search"})]
        public static AssetInventoryCommandResult CheckAssetInProject(
            [CliArg("guid", "Asset GUID from search or package-file results.", Required = true)] string guid)
        {
            return PipelineResultAdapter.Convert(UtilityAutomation.CheckAssetInProject(new UtilityAutomation.CheckAssetRequest {Guid = guid}));
        }

        [CliCommand("asset_inventory_get_asset_group_types", "List Asset Inventory file-group classifications and extensions. Read-only.", Tags = new[] {"asset-inventory", "search"})]
        public static AssetInventoryCommandResult GetAssetGroupTypes()
        {
            return PipelineResultAdapter.Convert(UtilityAutomation.GetAssetGroupTypes());
        }

        [CliCommand("asset_inventory_get_package_details", "Get full metadata, compatibility, tags, and media for one package. Read-only.", Tags = new[] {"asset-inventory", "search"})]
        public static AssetInventoryCommandResult GetPackageDetails(
            [CliArg("package_id", "Package ID from package or asset search results.", Required = true)] int packageId)
        {
            return PipelineResultAdapter.Convert(PackageDetailsAutomation.GetPackageDetails(new PackageDetailsAutomation.PackageDetailsRequest {PackageId = packageId}));
        }

        [CliCommand("asset_inventory_add_to_scene", "Instantiate an imported prefab or model in the active scene. Mutating and dry-run by default.", Tags = new[] {"asset-inventory", "import"})]
        public static AssetInventoryCommandResult AddToScene(
            [CliArg("project_path", "Project-relative prefab or model path.", Required = true)] string projectPath,
            [CliArg("position_x", "World X position. Defaults to the Scene view pivot.")] float? positionX = null,
            [CliArg("position_y", "World Y position. Defaults to the Scene view pivot.")] float? positionY = null,
            [CliArg("position_z", "World Z position. Defaults to the Scene view pivot.")] float? positionZ = null,
            [CliArg("parent_game_object", "Unique parent GameObject name in the active scene.")] string parentGameObject = null,
            [CliArg("dry_run", "Preview the exact mutation without executing it.")] bool dryRun = true,
            [CliArg("confirmation_token", "Token returned by the matching dry run.")] string confirmationToken = null)
        {
            return PipelineResultAdapter.Convert(SceneAutomation.AddToScene(new SceneAutomation.AddToSceneRequest
            {
                ProjectPath = projectPath,
                PositionX = positionX,
                PositionY = positionY,
                PositionZ = positionZ,
                ParentGameObject = parentGameObject,
                DryRun = dryRun,
                ConfirmationToken = confirmationToken
            }));
        }

        [CliCommand("asset_inventory_download_package", "Download a package so its files become extractable. Mutating and dry-run by default.", Tags = new[] {"asset-inventory", "import"})]
        public static AssetInventoryCommandResult DownloadPackage(
            [CliArg("package_id", "Package ID to download.", Required = true)] int packageId,
            [CliArg("dry_run", "Preview the download without starting it.")] bool dryRun = true,
            [CliArg("confirmation_token", "Token returned by the matching dry run.")] string confirmationToken = null)
        {
            return PipelineResultAdapter.Convert(DownloadAutomation.DownloadPackage(new DownloadAutomation.DownloadPackageRequest
            {
                PackageId = packageId,
                DryRun = dryRun,
                ConfirmationToken = confirmationToken
            }));
        }

        [CliCommand("asset_inventory_get_download_progress", "Get package download state, progress, and byte counts. Read-only.", Tags = new[] {"asset-inventory", "import"})]
        public static AssetInventoryCommandResult GetDownloadProgress(
            [CliArg("package_id", "Package ID used with the download command.", Required = true)] int packageId)
        {
            return PipelineResultAdapter.Convert(DownloadAutomation.GetDownloadProgress(new DownloadAutomation.DownloadProgressRequest {PackageId = packageId}));
        }
    }
}
