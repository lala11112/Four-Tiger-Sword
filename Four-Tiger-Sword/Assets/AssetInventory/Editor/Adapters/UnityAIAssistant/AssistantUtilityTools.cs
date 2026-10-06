using AssetInventory.Automation;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;

namespace AssetInventory.Integration.UnityAIAssistant
{
    internal static class AssistantResponseAdapter
    {
        internal static object Convert(AutomationResponse response)
        {
            if (response.Succeeded) return Response.Success(response.Message, response.Data);
            return Response.Error(response.Message, new {errorCode = response.ErrorCode, details = response.Data});
        }
    }

    public static class AssistantUtilityTools
    {
        public sealed class CheckAssetParams
        {
            [McpDescription("Asset GUID from search or package-file results.", Required = true)]
            public string Guid { get; set; }
        }

        public sealed class PackageDetailsParams
        {
            [McpDescription("Package ID from package or asset search results.", Required = true)]
            public int PackageId { get; set; }
        }

        public sealed class AddToSceneParams
        {
            [McpDescription("Project-relative prefab or model path, such as Assets/ThirdParty/MyPrefab.prefab.", Required = true)]
            public string ProjectPath { get; set; }

            [McpDescription("World X position. Defaults to the current Scene view pivot.")]
            public float? PositionX { get; set; }

            [McpDescription("World Y position. Defaults to the current Scene view pivot.")]
            public float? PositionY { get; set; }

            [McpDescription("World Z position. Defaults to the current Scene view pivot.")]
            public float? PositionZ { get; set; }

            [McpDescription("Unique name of an existing GameObject in the active scene to parent under.")]
            public string ParentGameObject { get; set; }

            [McpDescription("Preview the exact scene mutation without executing it.", Default = true)]
            public bool DryRun { get; set; } = true;

            [McpDescription("Confirmation token returned by the matching dry run.")]
            public string ConfirmationToken { get; set; }
        }

        public sealed class DownloadPackageParams
        {
            [McpDescription("Package ID to download.", Required = true)]
            public int PackageId { get; set; }

            [McpDescription("Preview the download without starting it.", Default = true)]
            public bool DryRun { get; set; } = true;

            [McpDescription("Confirmation token returned by the matching dry run.")]
            public string ConfirmationToken { get; set; }
        }

        public sealed class DownloadProgressParams
        {
            [McpDescription("Package ID used with downloadPackage.", Required = true)]
            public int PackageId { get; set; }
        }

        [McpTool("AssetInventory_getInventoryStats", "Get database overview: total packages, indexed count, file count, and source breakdown.", EnabledByDefault = true, Groups = new[] {"Asset Inventory"})]
        public static object GetInventoryStats()
        {
            return AssistantResponseAdapter.Convert(UtilityAutomation.GetInventoryStats());
        }

        [McpTool("AssetInventory_openWindow", "Open the Asset Inventory editor window.", EnabledByDefault = true, Groups = new[] {"Asset Inventory"})]
        public static object OpenWindow()
        {
            return AssistantResponseAdapter.Convert(UtilityAutomation.OpenAssetInventory());
        }

        [McpTool("AssetInventory_closeWindow", "Close the Asset Inventory editor window.", EnabledByDefault = true, Groups = new[] {"Asset Inventory"})]
        public static object CloseWindow()
        {
            return AssistantResponseAdapter.Convert(UtilityAutomation.CloseAssetInventory());
        }

        [McpTool("AssetInventory_checkAssetInProject", "Check whether an asset GUID exists in the current project and return its project path.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Search"})]
        public static object CheckAssetInProject(CheckAssetParams parameters)
        {
            parameters = parameters ?? new CheckAssetParams();
            return AssistantResponseAdapter.Convert(UtilityAutomation.CheckAssetInProject(new UtilityAutomation.CheckAssetRequest {Guid = parameters.Guid}));
        }

        [McpTool("AssetInventory_getAssetGroupTypes", "Get Asset Inventory file-group classifications and extensions.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Search"})]
        public static object GetAssetGroupTypes()
        {
            return AssistantResponseAdapter.Convert(UtilityAutomation.GetAssetGroupTypes());
        }

        [McpTool("AssetInventory_getPackageDetails", "Get full package metadata, compatibility, tags, and media.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Search"})]
        public static object GetPackageDetails(PackageDetailsParams parameters)
        {
            parameters = parameters ?? new PackageDetailsParams();
            return AssistantResponseAdapter.Convert(PackageDetailsAutomation.GetPackageDetails(new PackageDetailsAutomation.PackageDetailsRequest {PackageId = parameters.PackageId}));
        }

        [McpTool("AssetInventory_addToScene", "Instantiate an imported prefab or model into the active scene. Mutating and dry-run by default.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Import"})]
        public static object AddToScene(AddToSceneParams parameters)
        {
            parameters = parameters ?? new AddToSceneParams();
            return AssistantResponseAdapter.Convert(SceneAutomation.AddToScene(new SceneAutomation.AddToSceneRequest
            {
                ProjectPath = parameters.ProjectPath,
                PositionX = parameters.PositionX,
                PositionY = parameters.PositionY,
                PositionZ = parameters.PositionZ,
                ParentGameObject = parameters.ParentGameObject,
                DryRun = parameters.DryRun,
                ConfirmationToken = parameters.ConfirmationToken
            }));
        }

        [McpTool("AssetInventory_downloadPackage", "Download a package so its files become extractable. Mutating and dry-run by default.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Import"})]
        public static object DownloadPackage(DownloadPackageParams parameters)
        {
            parameters = parameters ?? new DownloadPackageParams();
            return AssistantResponseAdapter.Convert(DownloadAutomation.DownloadPackage(new DownloadAutomation.DownloadPackageRequest
            {
                PackageId = parameters.PackageId,
                DryRun = parameters.DryRun,
                ConfirmationToken = parameters.ConfirmationToken
            }));
        }

        [McpTool("AssetInventory_getDownloadProgress", "Check download state, progress percentage, and bytes transferred for a package.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Import"})]
        public static object GetDownloadProgress(DownloadProgressParams parameters)
        {
            parameters = parameters ?? new DownloadProgressParams();
            return AssistantResponseAdapter.Convert(DownloadAutomation.GetDownloadProgress(new DownloadAutomation.DownloadProgressRequest {PackageId = parameters.PackageId}));
        }
    }
}
