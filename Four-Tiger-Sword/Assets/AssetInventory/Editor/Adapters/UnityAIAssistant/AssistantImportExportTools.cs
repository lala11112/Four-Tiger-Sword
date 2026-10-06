using System.Threading.Tasks;
using AssetInventory.Automation;
using Unity.AI.MCP.Editor.ToolRegistry;

namespace AssetInventory.Integration.UnityAIAssistant
{
    public static class AssistantImportExportTools
    {
        public sealed class ImportAssetParams
        {
            [McpDescription("File ID from asset search or package-file results.", Required = true)]
            public int FileId { get; set; }

            [McpDescription("Project-relative target folder under Assets. Uses the configured import folder when empty.")]
            public string TargetFolder { get; set; }

            [McpDescription("Import dependencies automatically.", Default = true)]
            public bool WithDependencies { get; set; } = true;

            [McpDescription("Script mode: 0 none, 2 direct, 3 extended, or 4 all package scripts.", Default = 0)]
            public int ScriptMode { get; set; }

            [McpDescription("Preview the import without changing the project.", Default = true)]
            public bool DryRun { get; set; } = true;

            [McpDescription("Confirmation token returned by the matching dry run.")]
            public string ConfirmationToken { get; set; }
        }

        public sealed class ExportCsvParams
        {
            [McpDescription("Output CSV file path.", Required = true)]
            public string FilePath { get; set; }

            [McpDescription("Package search phrase used to filter the export.")]
            public string SearchQuery { get; set; }

            [McpDescription("JSON array of package IDs to export.")]
            public string PackageIds { get; set; }

            [McpDescription("JSON array of field names returned by listExportFields.")]
            public string Fields { get; set; }

            [McpDescription("Column separator.", Default = ";")]
            public string Separator { get; set; } = ";";

            [McpDescription("Include a header row.", Default = true)]
            public bool AddHeader { get; set; } = true;

            [McpDescription("Preview the file export without writing it.", Default = true)]
            public bool DryRun { get; set; } = true;

            [McpDescription("Confirmation token returned by the matching dry run.")]
            public string ConfirmationToken { get; set; }
        }

        public sealed class ExportHtmlParams
        {
            [McpDescription("Template name returned by listExportTemplates.", Required = true)]
            public string TemplateName { get; set; }

            [McpDescription("Output folder path.", Required = true)]
            public string OutputFolder { get; set; }

            [McpDescription("Package search phrase used to filter the export.")]
            public string SearchQuery { get; set; }

            [McpDescription("Preview the folder export without writing it.", Default = true)]
            public bool DryRun { get; set; } = true;

            [McpDescription("Confirmation token returned by the matching dry run.")]
            public string ConfirmationToken { get; set; }
        }

        public sealed class ListExportFieldsParams
        {
        }

        public sealed class ListExportTemplatesParams
        {
        }

        [McpTool("AssetInventory_importAsset", "Import an indexed file into the current project with dependency handling. Mutating and dry-run by default.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Import"})]
        public static async Task<object> ImportAsset(ImportAssetParams parameters)
        {
            parameters = parameters ?? new ImportAssetParams();
            AutomationResponse response = await ImportAutomation.ImportAsset(new ImportAutomation.ImportAssetRequest
            {
                FileId = parameters.FileId,
                TargetFolder = parameters.TargetFolder,
                WithDependencies = parameters.WithDependencies,
                ScriptMode = parameters.ScriptMode,
                DryRun = parameters.DryRun,
                ConfirmationToken = parameters.ConfirmationToken
            });
            return AssistantResponseAdapter.Convert(response);
        }

        [McpTool("AssetInventory_exportCSV", "Export package data to a CSV file. Mutating and dry-run by default.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Export"})]
        public static async Task<object> ExportCsv(ExportCsvParams parameters)
        {
            parameters = parameters ?? new ExportCsvParams();
            AutomationResponse response = await ExportAutomation.ExportCsv(new ExportAutomation.ExportCsvRequest
            {
                FilePath = parameters.FilePath,
                SearchQuery = parameters.SearchQuery,
                PackageIds = parameters.PackageIds,
                Fields = parameters.Fields,
                Separator = parameters.Separator,
                AddHeader = parameters.AddHeader,
                DryRun = parameters.DryRun,
                ConfirmationToken = parameters.ConfirmationToken
            });
            return AssistantResponseAdapter.Convert(response);
        }

        [McpTool("AssetInventory_exportHTML", "Export package data to an HTML folder using a template. Mutating and dry-run by default.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Export"})]
        public static async Task<object> ExportHtml(ExportHtmlParams parameters)
        {
            parameters = parameters ?? new ExportHtmlParams();
            AutomationResponse response = await ExportAutomation.ExportHtml(new ExportAutomation.ExportHtmlRequest
            {
                TemplateName = parameters.TemplateName,
                OutputFolder = parameters.OutputFolder,
                SearchQuery = parameters.SearchQuery,
                DryRun = parameters.DryRun,
                ConfirmationToken = parameters.ConfirmationToken
            });
            return AssistantResponseAdapter.Convert(response);
        }

        [McpTool("AssetInventory_listExportFields", "List field names available to CSV export. Read-only.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Export"})]
        public static object ListExportFields(ListExportFieldsParams parameters)
        {
            return AssistantResponseAdapter.Convert(ExportAutomation.ListExportFields());
        }

        [McpTool("AssetInventory_listExportTemplates", "List available HTML export templates. Read-only.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Export"})]
        public static object ListExportTemplates(ListExportTemplatesParams parameters)
        {
            return AssistantResponseAdapter.Convert(ExportAutomation.ListExportTemplates());
        }
    }
}
