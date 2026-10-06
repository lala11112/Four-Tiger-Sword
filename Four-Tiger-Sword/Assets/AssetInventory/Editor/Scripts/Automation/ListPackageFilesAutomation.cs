using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetInventory.Automation
{
    internal static class ListPackageFilesAutomation
    {
        internal sealed class ListPackageFilesRequest
        {
            public int PackageId { get; set; }
            public string Type { get; set; }
            public string AssetGroup { get; set; }
            public string SearchPhrase { get; set; }
            public int MaxResults { get; set; } = 50;
            public int Page { get; set; } = 1;
        }

        internal static AutomationResponse ListPackageFiles(ListPackageFilesRequest request)
        {
            AutomationResponse initError = AutomationResultHelper.EnsureInit();
            if (initError != null) return initError;
            if (request == null)
            {
                return AutomationResponse.Error("Request is required.", errorCode: "invalid_input");
            }
            AutomationResponse pagingError = AutomationInputValidator.ValidatePaging(request.Page, request.MaxResults, 200);
            if (pagingError != null) return pagingError;
            if (!string.IsNullOrWhiteSpace(request.Type) && !string.IsNullOrWhiteSpace(request.AssetGroup))
            {
                return AutomationResponse.Error("Type and AssetGroup are mutually exclusive. Specify only one.", errorCode: "invalid_input");
            }
            if (!ExpertSqlGuard.TryValidateSearchPhrase(request.SearchPhrase, out string sqlError))
            {
                return AutomationResponse.Error(sqlError, errorCode: "unsafe_query");
            }
            AssetInfo package = Assets.GetPackage(request.PackageId);
            if (package == null)
            {
                return AutomationResponse.Error($"Package with ID {request.PackageId} not found.", errorCode: "not_found");
            }

            AssetSearch.Options options = AssetSearch.Options.CreateDefault();
            options.SelectedAssetId = request.PackageId;
            options.SearchPhrase = request.SearchPhrase ?? string.Empty;
            options.MaxResults = request.MaxResults;
            options.CurrentPage = request.Page;

            if (!string.IsNullOrEmpty(request.Type))
            {
                string extension = request.Type.TrimStart('.').ToLowerInvariant();
                string[] allTypes = Assets.LoadTypes();
                int typeIndex = Array.FindIndex(allTypes, type => type.Split('/').LastOrDefault()?.ToLowerInvariant() == extension);
                if (typeIndex < 0)
                {
                    return AutomationResponse.Error($"Type '{request.Type}' was not found in the indexed file types.", errorCode: "not_found");
                }
                options.RawSearchType = allTypes[typeIndex];
            }
            else if (!string.IsNullOrEmpty(request.AssetGroup))
            {
                if (!AutomationInputValidator.TryParseDefinedEnum(request.AssetGroup, out AI.AssetGroup assetGroup))
                {
                    return AutomationResponse.Error($"Invalid AssetGroup '{request.AssetGroup}'.", errorCode: "invalid_input");
                }
                options.RawSearchType = assetGroup.ToString();
            }

            AssetSearch.Result result = AssetSearch.Execute(options);
            if (!string.IsNullOrEmpty(result.Error))
            {
                return AutomationResponse.Error(result.Error, errorCode: "query_failed");
            }

            List<object> items = result.Files.Select(AutomationResultHelper.ToAssetFileResult).ToList();
            return AutomationResponse.Success($"Found {result.ResultCount} files in package '{package.GetDisplayName()}' (showing page {options.CurrentPage}).", new
            {
                packageId = request.PackageId,
                results = items,
                totalCount = result.ResultCount,
                page = options.CurrentPage,
                pageSize = options.MaxResults,
                totalPages = (result.ResultCount + options.MaxResults - 1) / options.MaxResults
            });
        }
    }
}
