using AssetInventory.Automation;
using Unity.AI.MCP.Editor.ToolRegistry;

namespace AssetInventory.Integration.UnityAIAssistant
{
    public static class AssistantTagTools
    {
        public enum TagAction
        {
            Add,
            Remove
        }

        public sealed class ListTagsParams
        {
            [McpDescription("Filter tags by name.")]
            public string SearchPhrase { get; set; }
        }

        public sealed class TagPackageParams
        {
            [McpDescription("Package ID from package search results.", Required = true)]
            public int PackageId { get; set; }

            [McpDescription("Tag name. A new tag is created when necessary.", Required = true)]
            public string TagName { get; set; }

            [McpDescription("Add or Remove.", Required = true, EnumType = typeof(TagAction))]
            public string Action { get; set; }

            [McpDescription("Preview the tag mutation without executing it.", Default = true)]
            public bool DryRun { get; set; } = true;

            [McpDescription("Confirmation token returned by the matching dry run.")]
            public string ConfirmationToken { get; set; }
        }

        public sealed class TagAssetFileParams
        {
            [McpDescription("File ID from asset search or package-file results.", Required = true)]
            public int FileId { get; set; }

            [McpDescription("Tag name. A new tag is created when necessary.", Required = true)]
            public string TagName { get; set; }

            [McpDescription("Add or Remove.", Required = true, EnumType = typeof(TagAction))]
            public string Action { get; set; }

            [McpDescription("Preview the tag mutation without executing it.", Default = true)]
            public bool DryRun { get; set; } = true;

            [McpDescription("Confirmation token returned by the matching dry run.")]
            public string ConfirmationToken { get; set; }
        }

        [McpTool("AssetInventory_listTags", "List Asset Inventory tags. Read-only.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Tags"})]
        public static object ListTags(ListTagsParams parameters)
        {
            parameters = parameters ?? new ListTagsParams();
            return AssistantResponseAdapter.Convert(TagAutomation.ListTags(new TagAutomation.ListTagsRequest {SearchPhrase = parameters.SearchPhrase}));
        }

        [McpTool("AssetInventory_tagPackage", "Add or remove a tag on a package. Mutating and dry-run by default.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Tags"})]
        public static object TagPackage(TagPackageParams parameters)
        {
            parameters = parameters ?? new TagPackageParams();
            return AssistantResponseAdapter.Convert(TagAutomation.TagPackage(new TagAutomation.TagPackageRequest
            {
                PackageId = parameters.PackageId,
                TagName = parameters.TagName,
                Action = parameters.Action,
                DryRun = parameters.DryRun,
                ConfirmationToken = parameters.ConfirmationToken
            }));
        }

        [McpTool("AssetInventory_tagAssetFile", "Add or remove a tag on an individual indexed file. Mutating and dry-run by default.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Tags"})]
        public static object TagAssetFile(TagAssetFileParams parameters)
        {
            parameters = parameters ?? new TagAssetFileParams();
            return AssistantResponseAdapter.Convert(TagAutomation.TagAssetFile(new TagAutomation.TagAssetFileRequest
            {
                FileId = parameters.FileId,
                TagName = parameters.TagName,
                Action = parameters.Action,
                DryRun = parameters.DryRun,
                ConfirmationToken = parameters.ConfirmationToken
            }));
        }
    }
}
