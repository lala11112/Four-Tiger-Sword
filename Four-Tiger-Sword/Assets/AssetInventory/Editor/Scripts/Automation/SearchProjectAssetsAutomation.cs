using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetInventory.Automation
{
    internal static class SearchProjectAssetsAutomation
    {
        internal sealed class SearchProjectAssetsRequest
        {
            public string SearchPhrase { get; set; }
            public string Type { get; set; }
            public string AssetGroup { get; set; }
            public string ImageType { get; set; }
            public int MinWidth { get; set; }
            public int MaxWidth { get; set; }
            public int MinHeight { get; set; }
            public int MaxHeight { get; set; }
            public long MinSize { get; set; }
            public long MaxSize { get; set; }
            public int MinVertexCount { get; set; }
            public int MaxVertexCount { get; set; }
            public int MaxResults { get; set; } = 25;
            public int Page { get; set; } = 1;
        }

        internal static AutomationResponse SearchProjectAssets(SearchProjectAssetsRequest request)
        {
            request = request ?? new SearchProjectAssetsRequest();

            AutomationResponse inputError = AutomationInputValidator.ValidatePaging(request.Page, request.MaxResults, 100)
                ?? AutomationInputValidator.ValidateRange(request.MinWidth, request.MaxWidth, "width")
                ?? AutomationInputValidator.ValidateRange(request.MinHeight, request.MaxHeight, "height")
                ?? AutomationInputValidator.ValidateRange(request.MinSize, request.MaxSize, "file size")
                ?? AutomationInputValidator.ValidateRange(request.MinVertexCount, request.MaxVertexCount, "vertex count");
            if (inputError != null) return inputError;
            if (!string.IsNullOrWhiteSpace(request.Type) && !string.IsNullOrWhiteSpace(request.AssetGroup))
            {
                return AutomationResponse.Error("Type and AssetGroup are mutually exclusive. Specify only one.", errorCode: "invalid_input");
            }
            if (!string.IsNullOrWhiteSpace(request.SearchPhrase) && request.SearchPhrase.TrimStart().StartsWith("=", StringComparison.Ordinal))
            {
                return AutomationResponse.Error("Expert SQL is not supported for current-project searches.", errorCode: "invalid_input");
            }

            ProjectAssetSearch.Options options = ProjectAssetSearch.Options.CreateDefault();
            options.SearchPhrase = request.SearchPhrase ?? string.Empty;
            options.MaxResults = request.MaxResults;
            options.CurrentPage = request.Page;

            if (!string.IsNullOrEmpty(request.Type))
            {
                options.RawSearchType = request.Type.TrimStart('.').ToLowerInvariant();
            }
            if (!string.IsNullOrEmpty(request.AssetGroup))
            {
                if (!AutomationInputValidator.TryParseDefinedEnum(request.AssetGroup, out AI.AssetGroup assetGroup))
                {
                    return AutomationResponse.Error($"Invalid AssetGroup '{request.AssetGroup}'.", errorCode: "invalid_input");
                }
                options.RawSearchType = assetGroup.ToString();
            }
            if (!string.IsNullOrEmpty(request.ImageType) && options.ImageTypeOptions != null)
            {
                AutomationResponse optionError = AutomationInputValidator.ResolveOptionIndex("ImageType", request.ImageType, options.ImageTypeOptions, value => value, out int imageTypeIndex);
                if (optionError != null) return optionError;
                options.SelectedImageType = imageTypeIndex;
            }

            options.MinWidth = request.MinWidth;
            options.MaxWidth = request.MaxWidth;
            options.MinHeight = request.MinHeight;
            options.MaxHeight = request.MaxHeight;
            options.MinSizeBytes = request.MinSize;
            options.MaxSizeBytes = request.MaxSize;
            options.MinVertexCount = request.MinVertexCount;
            options.MaxVertexCount = request.MaxVertexCount;

            ProjectAssetSearch.Result result = ProjectAssetSearch.Execute(options);
            List<object> items = result.Files.Select(AutomationResultHelper.ToAssetFileResult).ToList();
            return AutomationResponse.Success($"Found {result.ResultCount} project assets (showing page {options.CurrentPage}).", new
            {
                results = items,
                totalCount = result.ResultCount,
                page = options.CurrentPage,
                pageSize = options.MaxResults,
                totalPages = (result.ResultCount + options.MaxResults - 1) / options.MaxResults
            });
        }
    }
}
