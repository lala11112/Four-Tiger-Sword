using System.Threading.Tasks;
using AssetInventory.Automation;
using Unity.Pipeline.Commands;

namespace AssetInventory.Integration.UnityPipeline
{
    public static class PipelineImportExportCommands
    {
        [CliCommand("asset_inventory_import_asset", "Import an indexed file into the current project with dependency handling. Mutating and dry-run by default.", Tags = new[] {"asset-inventory", "import"})]
        public static async Task<AssetInventoryCommandResult> ImportAsset(
            [CliArg("file_id", "File ID from asset search or package-file results.", Required = true)] int fileId,
            [CliArg("target_folder", "Project-relative target folder under Assets. Uses the configured import folder when empty.")] string targetFolder = null,
            [CliArg("with_dependencies", "Import dependencies automatically.")] bool withDependencies = true,
            [CliArg("script_mode", "0 none, 2 direct, 3 extended, or 4 all package scripts.")] int scriptMode = 0,
            [CliArg("dry_run", "Preview the import without changing the project.")] bool dryRun = true,
            [CliArg("confirmation_token", "Token returned by the matching dry run.")] string confirmationToken = null)
        {
            AutomationResponse response = await ImportAutomation.ImportAsset(new ImportAutomation.ImportAssetRequest
            {
                FileId = fileId,
                TargetFolder = targetFolder,
                WithDependencies = withDependencies,
                ScriptMode = scriptMode,
                DryRun = dryRun,
                ConfirmationToken = confirmationToken
            });
            return PipelineResultAdapter.Convert(response);
        }

        [CliCommand("asset_inventory_export_csv", "Export package data to a CSV file. Mutating and dry-run by default.", Tags = new[] {"asset-inventory", "export"})]
        public static async Task<AssetInventoryCommandResult> ExportCsv(
            [CliArg("file_path", "Output CSV file path.", Required = true)] string filePath,
            [CliArg("search_query", "Package search phrase used to filter the export.")] string searchQuery = null,
            [CliArg("package_ids", "JSON array of package IDs to export.")] string packageIds = null,
            [CliArg("fields", "JSON array of field names returned by list_export_fields.")] string fields = null,
            [CliArg("separator", "Column separator.")] string separator = ";",
            [CliArg("add_header", "Include a header row.")] bool addHeader = true,
            [CliArg("dry_run", "Preview the file export without writing it.")] bool dryRun = true,
            [CliArg("confirmation_token", "Token returned by the matching dry run.")] string confirmationToken = null)
        {
            AutomationResponse response = await ExportAutomation.ExportCsv(new ExportAutomation.ExportCsvRequest
            {
                FilePath = filePath,
                SearchQuery = searchQuery,
                PackageIds = packageIds,
                Fields = fields,
                Separator = separator,
                AddHeader = addHeader,
                DryRun = dryRun,
                ConfirmationToken = confirmationToken
            });
            return PipelineResultAdapter.Convert(response);
        }

        [CliCommand("asset_inventory_export_html", "Export package data to an HTML folder using a template. Mutating and dry-run by default.", Tags = new[] {"asset-inventory", "export"})]
        public static async Task<AssetInventoryCommandResult> ExportHtml(
            [CliArg("template_name", "Template name returned by list_export_templates.", Required = true)] string templateName,
            [CliArg("output_folder", "Output folder path.", Required = true)] string outputFolder,
            [CliArg("search_query", "Package search phrase used to filter the export.")] string searchQuery = null,
            [CliArg("dry_run", "Preview the folder export without writing it.")] bool dryRun = true,
            [CliArg("confirmation_token", "Token returned by the matching dry run.")] string confirmationToken = null)
        {
            AutomationResponse response = await ExportAutomation.ExportHtml(new ExportAutomation.ExportHtmlRequest
            {
                TemplateName = templateName,
                OutputFolder = outputFolder,
                SearchQuery = searchQuery,
                DryRun = dryRun,
                ConfirmationToken = confirmationToken
            });
            return PipelineResultAdapter.Convert(response);
        }

        [CliCommand("asset_inventory_list_export_fields", "List field names available to CSV export. Read-only.", Tags = new[] {"asset-inventory", "export"})]
        public static AssetInventoryCommandResult ListExportFields()
        {
            return PipelineResultAdapter.Convert(ExportAutomation.ListExportFields());
        }

        [CliCommand("asset_inventory_list_export_templates", "List available HTML export templates. Read-only.", Tags = new[] {"asset-inventory", "export"})]
        public static AssetInventoryCommandResult ListExportTemplates()
        {
            return PipelineResultAdapter.Convert(ExportAutomation.ListExportTemplates());
        }
    }
}
