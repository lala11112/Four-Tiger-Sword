using System;
using System.Threading.Tasks;

namespace AssetInventory.Automation
{
    internal static class ImportAutomation
    {
        internal sealed class ImportAssetRequest
        {
            public int FileId { get; set; }
            public string TargetFolder { get; set; }
            public bool WithDependencies { get; set; } = true;
            public int ScriptMode { get; set; }
            public bool DryRun { get; set; } = true;
            public string ConfirmationToken { get; set; }
        }

        internal static async Task<AutomationResponse> ImportAsset(ImportAssetRequest request)
        {
            AutomationResponse initError = AutomationResultHelper.EnsureInit();
            if (initError != null) return initError;
            if (request == null)
            {
                return AutomationResponse.Error("Request is required.", errorCode: "invalid_input");
            }
            if (request.ScriptMode != 0 && request.ScriptMode != 2 && request.ScriptMode != 3 && request.ScriptMode != 4)
            {
                return AutomationResponse.Error("ScriptMode must be 0, 2, 3, or 4.", errorCode: "invalid_input");
            }

            AssetFile file = DBAdapter.DB.Find<AssetFile>(request.FileId);
            if (file == null)
            {
                return AutomationResponse.Error($"Asset file with ID {request.FileId} not found.", errorCode: "not_found");
            }
            Asset asset = DBAdapter.DB.Find<Asset>(file.AssetId);
            if (asset == null)
            {
                return AutomationResponse.Error($"Parent package for file ID {request.FileId} not found.", errorCode: "not_found");
            }

            file.CheckIfInProject();
            if (file.InProject)
            {
                bool existingIsPrefab = AssetUtils.IsPrefab(file.ProjectPath);
                return AutomationResponse.Success($"File '{file.FileName}' is already in the project.", new
                {
                    importedPath = file.ProjectPath,
                    isPrefab = existingIsPrefab,
                    alreadyInProject = true
                });
            }

            string requestedTargetFolder = !string.IsNullOrWhiteSpace(request.TargetFolder) ? request.TargetFolder : AI.GetImportFolder();
            if (!AutomationInputValidator.TryNormalizeAssetsPath(requestedTargetFolder, out string targetFolder))
            {
                return AutomationResponse.Error("TargetFolder must be a normalized project-relative folder under Assets and cannot contain path traversal.", errorCode: "invalid_input");
            }

            AutomationResponse confirmation = AutomationMutationGuard.RequireConfirmation(
                "import_asset",
                request.DryRun,
                request.ConfirmationToken,
                new {fileId = request.FileId, fileName = file.FileName, packageId = file.AssetId, targetFolder, request.WithDependencies, request.ScriptMode},
                request.FileId.ToString(), file.AssetId.ToString(), file.Guid, file.Path, targetFolder, request.WithDependencies.ToString(), request.ScriptMode.ToString());
            if (confirmation != null) return confirmation;

            AssetInfo info = new AssetInfo().CopyFrom(asset, file);
            AssetImportResult importResult = await Assets.CopyToWithResult(info, targetFolder, request.WithDependencies, request.ScriptMode, logCollisionWarnings: false);
            if (importResult.HasCollisions)
            {
                return AutomationResponse.Error(Assets.FormatImportCollisionSummary(importResult.Collisions), errorCode: "conflict");
            }
            if (string.IsNullOrEmpty(importResult.ImportedPath))
            {
                return AutomationResponse.Error($"Failed to import file '{file.FileName}'. The file might not be downloadable or extractable.");
            }

            bool isPrefab = AssetUtils.IsPrefab(importResult.ImportedPath);
            return AutomationResponse.Success($"File '{file.FileName}' imported to '{importResult.ImportedPath}'.", new
            {
                importedPath = importResult.ImportedPath,
                isPrefab,
                alreadyInProject = false
            });
        }
    }
}
