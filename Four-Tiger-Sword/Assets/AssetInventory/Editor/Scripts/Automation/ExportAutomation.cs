using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace AssetInventory.Automation
{
    internal static class ExportAutomation
    {
        internal sealed class ExportCsvRequest
        {
            public string FilePath { get; set; }
            public string SearchQuery { get; set; }
            public string PackageIds { get; set; }
            public string Fields { get; set; }
            public string Separator { get; set; } = ";";
            public bool AddHeader { get; set; } = true;
            public bool DryRun { get; set; } = true;
            public string ConfirmationToken { get; set; }
        }

        internal sealed class ExportHtmlRequest
        {
            public string TemplateName { get; set; }
            public string OutputFolder { get; set; }
            public string SearchQuery { get; set; }
            public bool DryRun { get; set; } = true;
            public string ConfirmationToken { get; set; }
        }

        internal static async Task<AutomationResponse> ExportCsv(ExportCsvRequest request)
        {
            AutomationResponse initError = AutomationResultHelper.EnsureInit();
            if (initError != null) return initError;
            if (request == null || string.IsNullOrWhiteSpace(request.FilePath))
            {
                return AutomationResponse.Error("FilePath is required.", errorCode: "invalid_input");
            }

            HashSet<int> packageIdFilter = null;
            if (!string.IsNullOrEmpty(request.PackageIds))
            {
                try
                {
                    List<int> ids = JsonConvert.DeserializeObject<List<int>>(request.PackageIds);
                    if (ids != null && ids.Count > 0) packageIdFilter = new HashSet<int>(ids);
                }
                catch (JsonException exception)
                {
                    return AutomationResponse.Error($"Invalid PackageIds JSON array: {exception.Message}", errorCode: "invalid_input");
                }
            }

            CSVExportSettings settings = new CSVExportSettings
            {
                separator = !string.IsNullOrEmpty(request.Separator) ? request.Separator : ";",
                addHeader = request.AddHeader
            };
            if (!string.IsNullOrEmpty(request.Fields))
            {
                try
                {
                    settings.selectedFields = JsonConvert.DeserializeObject<List<string>>(request.Fields);
                }
                catch (JsonException exception)
                {
                    return AutomationResponse.Error($"Invalid Fields JSON array: {exception.Message}", errorCode: "invalid_input");
                }
            }
            CSVExport.EnsureSettings(settings);

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(request.FilePath);
            }
            catch (Exception exception)
            {
                return AutomationResponse.Error($"FilePath is invalid: {exception.Message}", errorCode: "invalid_input");
            }

            List<AssetInfo> assets = Assets.LoadPackagesForExport(request.SearchQuery, packageIdFilter);
            string fileStamp = GetFileStamp(fullPath);
            string packageScope = string.Join(",", assets.Select(asset => asset.AssetId).OrderBy(id => id));
            string fieldsScope = settings.selectedFields == null ? string.Empty : string.Join(",", settings.selectedFields);
            AutomationResponse confirmation = AutomationMutationGuard.RequireConfirmation(
                "export_csv",
                request.DryRun,
                request.ConfirmationToken,
                new {filePath = fullPath, fileExists = File.Exists(fullPath), packageCount = assets.Count, fields = settings.selectedFields, settings.separator, settings.addHeader},
                fullPath, fileStamp, packageScope, fieldsScope, settings.separator, settings.addHeader.ToString());
            if (confirmation != null) return confirmation;

            CSVExport exporter = new CSVExport();
            await exporter.Run(assets, settings, fullPath);
            return AutomationResponse.Success($"Exported {assets.Count} packages to '{fullPath}'.", new {filePath = fullPath, packageCount = assets.Count});
        }

        internal static async Task<AutomationResponse> ExportHtml(ExportHtmlRequest request)
        {
            AutomationResponse initError = AutomationResultHelper.EnsureInit();
            if (initError != null) return initError;
            if (request == null || string.IsNullOrWhiteSpace(request.TemplateName))
            {
                return AutomationResponse.Error("TemplateName is required.", errorCode: "invalid_input");
            }
            if (string.IsNullOrWhiteSpace(request.OutputFolder))
            {
                return AutomationResponse.Error("OutputFolder is required.", errorCode: "invalid_input");
            }

            List<TemplateInfo> templates = TemplateUtils.LoadTemplates();
            TemplateInfo selectedTemplate = templates.FirstOrDefault(template => template.GetNameFromFile() == request.TemplateName)
                ?? templates.FirstOrDefault(template => template.name != null && template.name.Equals(request.TemplateName, StringComparison.OrdinalIgnoreCase));
            if (selectedTemplate == null)
            {
                return AutomationResponse.Error($"Template '{request.TemplateName}' not found. Use the list export templates command to see available templates.", errorCode: "not_found");
            }

            string outputFolder;
            try
            {
                outputFolder = Path.GetFullPath(request.OutputFolder);
            }
            catch (Exception exception)
            {
                return AutomationResponse.Error($"OutputFolder is invalid: {exception.Message}", errorCode: "invalid_input");
            }

            List<AssetInfo> assets = Assets.LoadPackagesForExport(request.SearchQuery);
            string packageScope = string.Join(",", assets.Select(asset => asset.AssetId).OrderBy(id => id));
            AutomationResponse confirmation = AutomationMutationGuard.RequireConfirmation(
                "export_html",
                request.DryRun,
                request.ConfirmationToken,
                new {outputFolder, folderExists = Directory.Exists(outputFolder), packageCount = assets.Count, templateId = selectedTemplate.GetNameFromFile(), templateName = selectedTemplate.name},
                outputFolder, Directory.Exists(outputFolder).ToString(), selectedTemplate.GetNameFromFile(), selectedTemplate.version.ToString(), packageScope);
            if (confirmation != null) return confirmation;

            Directory.CreateDirectory(outputFolder);
            TemplateExportEnvironment environment = new TemplateExportEnvironment
            {
                name = "Automation Export",
                publishFolder = outputFolder,
                dataPath = "data/",
                imagePath = "Previews/",
                excludeImages = false,
                internalIdsOnly = false
            };
            if (AI.Config.templateExportSettings == null) AI.Config.templateExportSettings = new TemplateExportSettings();

            TemplateExport exporter = new TemplateExport();
            await exporter.Run(assets, selectedTemplate, templates, AI.Config.templateExportSettings, environment);
            return AutomationResponse.Success($"Exported {assets.Count} packages to '{outputFolder}' using template '{selectedTemplate.name}'.", new
            {
                outputFolder,
                packageCount = assets.Count,
                templateName = selectedTemplate.name
            });
        }

        internal static AutomationResponse ListExportFields()
        {
            AutomationResponse initError = AutomationResultHelper.EnsureInit();
            if (initError != null) return initError;

            List<string> allFields = CSVExport.GetAllFields();
            List<string> defaultFields = CSVExport.GetDefaultFields();
            HashSet<string> defaultSet = new HashSet<string>(defaultFields);
            return AutomationResponse.Success($"Found {allFields.Count} export fields ({defaultFields.Count} defaults).", new
            {
                fields = allFields.Select(field => new {name = field, isDefault = defaultSet.Contains(field)}).ToArray()
            });
        }

        internal static AutomationResponse ListExportTemplates()
        {
            AutomationResponse initError = AutomationResultHelper.EnsureInit();
            if (initError != null) return initError;

            List<TemplateInfo> templates = TemplateUtils.LoadTemplates().Where(template => !string.IsNullOrWhiteSpace(template.name)).ToList();
            return AutomationResponse.Success($"Found {templates.Count} export templates.", new
            {
                templates = templates.Select(template => new
                {
                    id = template.GetNameFromFile(),
                    name = template.name,
                    description = template.description,
                    isSample = template.isSample,
                    version = template.version
                }).ToArray()
            });
        }

        private static string GetFileStamp(string path)
        {
            if (!File.Exists(path)) return "missing";
            FileInfo file = new FileInfo(path);
            return $"{file.Length}:{file.LastWriteTimeUtc.Ticks}";
        }
    }
}
