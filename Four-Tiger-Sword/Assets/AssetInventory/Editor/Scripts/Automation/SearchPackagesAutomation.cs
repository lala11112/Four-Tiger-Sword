using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetInventory.Automation
{
    internal static class SearchPackagesAutomation
    {
        internal enum SourceFilter
        {
            All,
            AssetStore,
            CustomPackage,
            Directory,
            Registry,
            Archive,
            AssetManager
        }

        internal enum DeprecationFilter
        {
            All,
            NotDeprecated,
            Deprecated
        }

        internal enum PackageSrpFilter
        {
            All,
            BIRP,
            URP,
            HDRP
        }

        internal sealed class SearchPackagesRequest
        {
            public string SearchPhrase { get; set; }
            public string Source { get; set; }
            public string SrpCompatibility { get; set; }
            public string Maintenance { get; set; }
            public string Deprecation { get; set; }
            public string PriceOption { get; set; }
            public string Tag { get; set; }
            public string Publisher { get; set; }
            public string Category { get; set; }
            public bool IncludeSubcategories { get; set; } = true;
            public bool OnlyInProject { get; set; }
            public bool SearchDescription { get; set; }
            public float MinSizeMB { get; set; }
            public float MaxSizeMB { get; set; }
            public string UpdatedBefore { get; set; }
            public string UpdatedAfter { get; set; }
            public string PurchasedBefore { get; set; }
            public string PurchasedAfter { get; set; }
            public int MaxResults { get; set; } = 25;
            public int Page { get; set; } = 1;
        }

        internal static AutomationResponse SearchPackages(SearchPackagesRequest request)
        {
            AutomationResponse initError = AutomationResultHelper.EnsureInit();
            if (initError != null) return initError;
            request = request ?? new SearchPackagesRequest();

            AutomationResponse inputError = AutomationInputValidator.ValidatePaging(request.Page, request.MaxResults, 100)
                ?? AutomationInputValidator.ValidateRange(request.MinSizeMB, request.MaxSizeMB, "package size");
            if (inputError != null) return inputError;

            DateTime? updatedBefore = null;
            DateTime? updatedAfter = null;
            DateTime? purchasedBefore = null;
            DateTime? purchasedAfter = null;
            if (!string.IsNullOrWhiteSpace(request.UpdatedBefore))
            {
                if (!AutomationInputValidator.TryParseIsoDate(request.UpdatedBefore, out DateTime parsed)) return InvalidDate("UpdatedBefore", request.UpdatedBefore);
                updatedBefore = parsed;
            }
            if (!string.IsNullOrWhiteSpace(request.UpdatedAfter))
            {
                if (!AutomationInputValidator.TryParseIsoDate(request.UpdatedAfter, out DateTime parsed)) return InvalidDate("UpdatedAfter", request.UpdatedAfter);
                updatedAfter = parsed;
            }
            if (!string.IsNullOrWhiteSpace(request.PurchasedBefore))
            {
                if (!AutomationInputValidator.TryParseIsoDate(request.PurchasedBefore, out DateTime parsed)) return InvalidDate("PurchasedBefore", request.PurchasedBefore);
                purchasedBefore = parsed;
            }
            if (!string.IsNullOrWhiteSpace(request.PurchasedAfter))
            {
                if (!AutomationInputValidator.TryParseIsoDate(request.PurchasedAfter, out DateTime parsed)) return InvalidDate("PurchasedAfter", request.PurchasedAfter);
                purchasedAfter = parsed;
            }
            if (updatedAfter.HasValue && updatedBefore.HasValue && updatedAfter.Value > updatedBefore.Value)
            {
                return AutomationResponse.Error("UpdatedAfter cannot be later than UpdatedBefore.", errorCode: "invalid_input");
            }
            if (purchasedAfter.HasValue && purchasedBefore.HasValue && purchasedAfter.Value > purchasedBefore.Value)
            {
                return AutomationResponse.Error("PurchasedAfter cannot be later than PurchasedBefore.", errorCode: "invalid_input");
            }

            PackageSearch.Options options = PackageSearch.Options.CreateDefault();
            options.SearchPhrase = request.SearchPhrase ?? string.Empty;

            if (!string.IsNullOrEmpty(request.Source))
            {
                if (!AutomationInputValidator.TryParseDefinedEnum(request.Source, out SourceFilter source))
                {
                    return AutomationResponse.Error($"Invalid Source '{request.Source}'.", errorCode: "invalid_input");
                }
                options.SelectedPackageListing = GetPackageListingIndex(source);
            }
            if (!string.IsNullOrEmpty(request.SrpCompatibility))
            {
                if (!AutomationInputValidator.TryParseDefinedEnum(request.SrpCompatibility, out PackageSrpFilter srp))
                {
                    return AutomationResponse.Error($"Invalid SrpCompatibility '{request.SrpCompatibility}'. Use All, BIRP, URP, or HDRP.", errorCode: "invalid_input");
                }
                switch (srp)
                {
                    case PackageSrpFilter.BIRP: options.SelectedSRPs = 2; break;
                    case PackageSrpFilter.URP: options.SelectedSRPs = 3; break;
                    case PackageSrpFilter.HDRP: options.SelectedSRPs = 4; break;
                }
            }
            if (!string.IsNullOrEmpty(request.Maintenance))
            {
                if (!AutomationInputValidator.TryParseDefinedEnum(request.Maintenance, out PackageSearch.MaintenanceOption maintenance))
                {
                    return AutomationResponse.Error($"Invalid Maintenance '{request.Maintenance}'.", errorCode: "invalid_input");
                }
                options.SelectedMaintenance = maintenance;
            }
            if (!string.IsNullOrEmpty(request.Deprecation))
            {
                if (!AutomationInputValidator.TryParseDefinedEnum(request.Deprecation, out DeprecationFilter deprecation))
                {
                    return AutomationResponse.Error($"Invalid Deprecation '{request.Deprecation}'. Use All, NotDeprecated, or Deprecated.", errorCode: "invalid_input");
                }
                options.SelectedDeprecation = GetDeprecationIndex(deprecation);
            }
            if (!string.IsNullOrEmpty(request.PriceOption))
            {
                if (!AutomationInputValidator.TryParseDefinedEnum(request.PriceOption, out SearchAssetsAutomation.PriceFilter price))
                {
                    return AutomationResponse.Error($"Invalid PriceOption '{request.PriceOption}'. Use All, Free, or Paid.", errorCode: "invalid_input");
                }
                switch (price)
                {
                    case SearchAssetsAutomation.PriceFilter.Free: options.SelectedPriceOption = 1; break;
                    case SearchAssetsAutomation.PriceFilter.Paid: options.SelectedPriceOption = 2; break;
                }
            }
            if (!string.IsNullOrEmpty(request.Tag))
            {
                AutomationResponse optionError = AutomationInputValidator.ResolveOptionIndex("Tag", request.Tag, options.TagNames, value => value, out int tagIndex);
                if (optionError != null) return optionError;
                options.SelectedPackageTag = tagIndex;
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
            options.OnlyInProject = request.OnlyInProject;
            options.SearchDescription = request.SearchDescription;
            options.MaxResults = request.MaxResults;
            options.CurrentPage = request.Page;
            options.UsePackageSizeRange = request.MinSizeMB > 0f || request.MaxSizeMB > 0f;
            options.MinPackageSizeMB = request.MinSizeMB;
            options.MaxPackageSizeMB = request.MaxSizeMB;
            options.UseUpdateDateRange = updatedBefore.HasValue || updatedAfter.HasValue;
            options.UpdateBeforeDate = updatedBefore;
            options.UpdateAfterDate = updatedAfter;
            options.UsePurchaseDateRange = purchasedBefore.HasValue || purchasedAfter.HasValue;
            options.PurchaseBeforeDate = purchasedBefore;
            options.PurchaseAfterDate = purchasedAfter;

            PackageSearch.Result result = PackageSearch.Execute(options);
            if (!string.IsNullOrEmpty(result.Error))
            {
                return AutomationResponse.Error(result.Error, errorCode: "query_failed");
            }

            List<object> items = result.Packages.Select(AutomationResultHelper.ToPackageResult).ToList();
            return AutomationResponse.Success($"Found {result.ResultCount} packages (showing page {options.CurrentPage}).", new
            {
                results = items,
                totalCount = result.ResultCount,
                page = options.CurrentPage,
                pageSize = options.MaxResults,
                totalPages = (result.ResultCount + options.MaxResults - 1) / options.MaxResults
            });
        }

        private static AutomationResponse InvalidDate(string parameterName, string value)
        {
            return AutomationResponse.Error($"{parameterName} '{value}' is not a valid ISO 8601 date.", errorCode: "invalid_input");
        }

        internal static int GetPackageListingIndex(SourceFilter source)
        {
            switch (source)
            {
                case SourceFilter.AssetStore: return 2;
                case SourceFilter.Registry: return 3;
                case SourceFilter.CustomPackage: return 4;
                case SourceFilter.Directory: return 5;
                case SourceFilter.Archive: return 6;
                case SourceFilter.AssetManager: return 7;
                default: return 0;
            }
        }

        internal static int GetDeprecationIndex(DeprecationFilter deprecation)
        {
            switch (deprecation)
            {
                case DeprecationFilter.NotDeprecated: return 2;
                case DeprecationFilter.Deprecated: return 3;
                default: return 0;
            }
        }
    }
}
