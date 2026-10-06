using AssetInventory.Automation;
using Unity.Pipeline.Commands;

namespace AssetInventory.Integration.UnityPipeline
{
    public static class PipelineTagCommands
    {
        [CliCommand("asset_inventory_list_tags", "List Asset Inventory tags. Read-only.", Tags = new[] {"asset-inventory", "tags"})]
        public static AssetInventoryCommandResult ListTags(
            [CliArg("search_phrase", "Filter tags by name.")] string searchPhrase = null)
        {
            return PipelineResultAdapter.Convert(TagAutomation.ListTags(new TagAutomation.ListTagsRequest {SearchPhrase = searchPhrase}));
        }

        [CliCommand("asset_inventory_tag_package", "Add or remove a tag on a package. Mutating and dry-run by default.", Tags = new[] {"asset-inventory", "tags"})]
        public static AssetInventoryCommandResult TagPackage(
            [CliArg("package_id", "Package ID from package search results.", Required = true)] int packageId,
            [CliArg("tag_name", "Tag name. A new tag is created when necessary.", Required = true)] string tagName,
            [CliArg("action", "Add or Remove.", Required = true)] string action,
            [CliArg("dry_run", "Preview the tag mutation without executing it.")] bool dryRun = true,
            [CliArg("confirmation_token", "Token returned by the matching dry run.")] string confirmationToken = null)
        {
            return PipelineResultAdapter.Convert(TagAutomation.TagPackage(new TagAutomation.TagPackageRequest
            {
                PackageId = packageId,
                TagName = tagName,
                Action = action,
                DryRun = dryRun,
                ConfirmationToken = confirmationToken
            }));
        }

        [CliCommand("asset_inventory_tag_asset_file", "Add or remove a tag on an individual indexed file. Mutating and dry-run by default.", Tags = new[] {"asset-inventory", "tags"})]
        public static AssetInventoryCommandResult TagAssetFile(
            [CliArg("file_id", "File ID from asset search or package-file results.", Required = true)] int fileId,
            [CliArg("tag_name", "Tag name. A new tag is created when necessary.", Required = true)] string tagName,
            [CliArg("action", "Add or Remove.", Required = true)] string action,
            [CliArg("dry_run", "Preview the tag mutation without executing it.")] bool dryRun = true,
            [CliArg("confirmation_token", "Token returned by the matching dry run.")] string confirmationToken = null)
        {
            return PipelineResultAdapter.Convert(TagAutomation.TagAssetFile(new TagAutomation.TagAssetFileRequest
            {
                FileId = fileId,
                TagName = tagName,
                Action = action,
                DryRun = dryRun,
                ConfirmationToken = confirmationToken
            }));
        }
    }
}
