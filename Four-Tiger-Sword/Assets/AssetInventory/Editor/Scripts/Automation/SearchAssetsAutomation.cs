using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetInventory.Automation
{
    internal static class SearchAssetsAutomation
    {
        internal enum SrpFilter
        {
            All,
            Auto,
            BIRP,
            URP,
            HDRP
        }

        internal enum PriceFilter
        {
            All,
            Free,
            Paid
        }

        internal sealed class SearchAssetsRequest
        {
            public string SearchPhrase { get; set; }
            public string Type { get; set; }
            public string AssetGroup { get; set; }
            public string PackageTag { get; set; }
            public string FileTag { get; set; }
            public string Publisher { get; set; }
            public string Category { get; set; }
            public bool IncludeSubcategories { get; set; } = true;
            public string SrpCompatibility { get; set; }
            public string PriceOption { get; set; }
            public int MinWidth { get; set; }
            public int MaxWidth { get; set; }
            public int MinHeight { get; set; }
            public int MaxHeight { get; set; }
            public long MinSize { get; set; }
            public long MaxSize { get; set; }
            public int MaxResults { get; set; } = 25;
            public int Page { get; set; } = 1;
        }

        internal static AutomationResponse SearchAssets(SearchAssetsRequest request)
        {
            AutomationResponse initError = AutomationResultHelper.EnsureInit();
            if (initError != null) return initError;
            request = request ?? new SearchAssetsRequest();

            AutomationResponse inputError = AutomationInputValidator.ValidatePaging(request.Page, request.MaxResults, 100)
                ?? AutomationInputValidator.ValidateRange(request.MinWidth, request.MaxWidth, "width")
                ?? AutomationInputValidator.ValidateRange(request.MinHeight, request.MaxHeight, "height")
                ?? AutomationInputValidator.ValidateRange(request.MinSize, request.MaxSize, "file size");
            if (inputError != null) return inputError;
            if (!string.IsNullOrWhiteSpace(request.Type) && !string.IsNullOrWhiteSpace(request.AssetGroup))
            {
                return AutomationResponse.Error("Type and AssetGroup are mutually exclusive. Specify only one.", errorCode: "invalid_input");
            }

            if (!ExpertSqlGuard.TryValidateSearchPhrase(request.SearchPhrase, out string sqlError))
            {
                return AutomationResponse.Error(sqlError, errorCode: "unsafe_query");
            }

            AssetSearch.Options options = AssetSearch.Options.CreateDefault();
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
            if (!string.IsNullOrEmpty(request.AssetGroup))
            {
                if (!AutomationInputValidator.TryParseDefinedEnum(request.AssetGroup, out AI.AssetGroup assetGroup))
                {
                    return AutomationResponse.Error($"Invalid AssetGroup '{request.AssetGroup}'.", errorCode: "invalid_input");
                }
                options.RawSearchType = assetGroup.ToString();
            }
            if (!string.IsNullOrEmpty(request.PackageTag))
            {
                AutomationResponse optionError = AutomationInputValidator.ResolveOptionIndex("PackageTag", request.PackageTag, options.TagNames, value => value, out int tagIndex);
                if (optionError != null) return optionError;
                options.SelectedPackageTag = tagIndex;
            }
            if (!string.IsNullOrEmpty(request.FileTag))
            {
                AutomationResponse optionError = AutomationInputValidator.ResolveOptionIndex("FileTag", request.FileTag, options.TagNames, value => value, out int tagIndex);
                if (optionError != null) return optionError;
                options.SelectedFileTag = tagIndex;
            }
            if (!string.IsNullOrEmpty(request.Publisher))
            {
                AutomationResponse optionError = AutomationInputValidator.ResolveOptionIndex("Publisher", request.Publisher, options.PublisherNames, value => value.Split('/').LastOrDefault() ?? value, out int publisherIndex);
                if (optionError != null) return optionError;
                options.SelectedPublisher = publisherIndex;
            }
            if (!string.IsNullOrEmpty(request.Category))
            {
                AutomationResponse optionError = AutomationInputValidator.ResolveOptionIndex("Category", request.Category, options.CategoryNames, value => value, out int categoryIndex);
                if (optionError != null) return optionError;
                options.SelectedCategory = categoryIndex;
            }
            options.IncludeCategorySubcategories = request.IncludeSubcategories;

            if (!string.IsNullOrEmpty(request.SrpCompatibility))
            {
                if (!AutomationInputValidator.TryParseDefinedEnum(request.SrpCompatibility, out SrpFilter srp))
                {
                    return AutomationResponse.Error($"Invalid SrpCompatibility '{request.SrpCompatibility}'. Use All, Auto, BIRP, URP, or HDRP.", errorCode: "invalid_input");
                }
                switch (srp)
                {
                    case SrpFilter.Auto: options.SelectedPackageSRPs = 1; break;
                    case SrpFilter.BIRP: options.SelectedPackageSRPs = 3; break;
                    case SrpFilter.URP: options.SelectedPackageSRPs = 4; break;
                    case SrpFilter.HDRP: options.SelectedPackageSRPs = 5; break;
                }
            }
            if (!string.IsNullOrEmpty(request.PriceOption))
            {
                if (!AutomationInputValidator.TryParseDefinedEnum(request.PriceOption, out PriceFilter price))
                {
                    return AutomationResponse.Error($"Invalid PriceOption '{request.PriceOption}'. Use All, Free, or Paid.", errorCode: "invalid_input");
                }
                switch (price)
                {
                    case PriceFilter.Free: options.SelectedPriceOption = 1; break;
                    case PriceFilter.Paid: options.SelectedPriceOption = 2; break;
                }
            }

            options.MinWidth = request.MinWidth;
            options.MaxWidth = request.MaxWidth;
            options.MinHeight = request.MinHeight;
            options.MaxHeight = request.MaxHeight;
            options.MinSizeBytes = request.MinSize;
            options.MaxSizeBytes = request.MaxSize;

            AssetSearch.Result result = AssetSearch.Execute(options);
            if (!string.IsNullOrEmpty(result.Error))
            {
                return AutomationResponse.Error(result.Error, errorCode: "query_failed");
            }

            List<object> items = result.Files.Select(AutomationResultHelper.ToAssetFileResult).ToList();
            return AutomationResponse.Success($"Found {result.ResultCount} assets (showing page {options.CurrentPage}).", new
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
