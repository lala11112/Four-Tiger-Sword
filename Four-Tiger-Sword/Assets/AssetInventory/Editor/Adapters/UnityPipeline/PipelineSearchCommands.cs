using AssetInventory.Automation;
using Unity.Pipeline.Commands;

namespace AssetInventory.Integration.UnityPipeline
{
    public static class PipelineSearchCommands
    {
        [CliCommand("asset_inventory_search_assets", "Search individual files across indexed packages. Read-only.", Tags = new[] {"asset-inventory", "search"})]
        public static AssetInventoryCommandResult SearchAssets(
            [CliArg("search_phrase", "Query supporting AND terms, -exclusions, ~exact phrases, tags, and one leading = SQL predicate.", Required = true)] string searchPhrase,
            [CliArg("type", "File extension without a dot.")] string type = null,
            [CliArg("asset_group", "Asset category filter.")] string assetGroup = null,
            [CliArg("package_tag", "Package tag filter.")] string packageTag = null,
            [CliArg("file_tag", "File tag filter.")] string fileTag = null,
            [CliArg("publisher", "Publisher name filter.")] string publisher = null,
            [CliArg("category", "Category filter.")] string category = null,
            [CliArg("include_subcategories", "Include matching subcategories.")] bool includeSubcategories = true,
            [CliArg("srp_compatibility", "All, Auto, BIRP, URP, or HDRP.")] string srpCompatibility = null,
            [CliArg("price_option", "All, Free, or Paid.")] string priceOption = null,
            [CliArg("min_width", "Minimum image width in pixels.")] int minWidth = 0,
            [CliArg("max_width", "Maximum image width in pixels.")] int maxWidth = 0,
            [CliArg("min_height", "Minimum image height in pixels.")] int minHeight = 0,
            [CliArg("max_height", "Maximum image height in pixels.")] int maxHeight = 0,
            [CliArg("min_size", "Minimum file size in bytes.")] long minSize = 0,
            [CliArg("max_size", "Maximum file size in bytes.")] long maxSize = 0,
            [CliArg("max_results", "Results per page, from 1 through 100.")] int maxResults = 25,
            [CliArg("page", "One-based page number.")] int page = 1)
        {
            return PipelineResultAdapter.Convert(SearchAssetsAutomation.SearchAssets(new SearchAssetsAutomation.SearchAssetsRequest
            {
                SearchPhrase = searchPhrase,
                Type = type,
                AssetGroup = assetGroup,
                PackageTag = packageTag,
                FileTag = fileTag,
                Publisher = publisher,
                Category = category,
                IncludeSubcategories = includeSubcategories,
                SrpCompatibility = srpCompatibility,
                PriceOption = priceOption,
                MinWidth = minWidth,
                MaxWidth = maxWidth,
                MinHeight = minHeight,
                MaxHeight = maxHeight,
                MinSize = minSize,
                MaxSize = maxSize,
                MaxResults = maxResults,
                Page = page
            }));
        }

        [CliCommand("asset_inventory_search_packages", "Search packages by name, source, status, and metadata. Read-only.", Tags = new[] {"asset-inventory", "search"})]
        public static AssetInventoryCommandResult SearchPackages(
            [CliArg("search_phrase", "Query supporting AND terms, -exclusions, and ~exact phrases.")] string searchPhrase = null,
            [CliArg("source", "All, AssetStore, CustomPackage, Directory, Registry, Archive, or AssetManager.")] string source = null,
            [CliArg("srp_compatibility", "All, BIRP, URP, or HDRP.")] string srpCompatibility = null,
            [CliArg("maintenance", "Package maintenance filter.")] string maintenance = null,
            [CliArg("deprecation", "All, NotDeprecated, or Deprecated.")] string deprecation = null,
            [CliArg("price_option", "All, Free, or Paid.")] string priceOption = null,
            [CliArg("tag", "Package tag filter.")] string tag = null,
            [CliArg("publisher", "Publisher name filter.")] string publisher = null,
            [CliArg("category", "Category filter.")] string category = null,
            [CliArg("include_subcategories", "Include matching subcategories.")] bool includeSubcategories = true,
            [CliArg("only_in_project", "Only packages used in the current project.")] bool onlyInProject = false,
            [CliArg("search_description", "Also search package descriptions.")] bool searchDescription = false,
            [CliArg("min_size_mb", "Minimum package size in MB.")] float minSizeMb = 0,
            [CliArg("max_size_mb", "Maximum package size in MB.")] float maxSizeMb = 0,
            [CliArg("updated_before", "ISO 8601 upper update-date bound.")] string updatedBefore = null,
            [CliArg("updated_after", "ISO 8601 lower update-date bound.")] string updatedAfter = null,
            [CliArg("purchased_before", "ISO 8601 upper purchase-date bound.")] string purchasedBefore = null,
            [CliArg("purchased_after", "ISO 8601 lower purchase-date bound.")] string purchasedAfter = null,
            [CliArg("max_results", "Results per page, from 1 through 100.")] int maxResults = 25,
            [CliArg("page", "One-based page number.")] int page = 1)
        {
            return PipelineResultAdapter.Convert(SearchPackagesAutomation.SearchPackages(new SearchPackagesAutomation.SearchPackagesRequest
            {
                SearchPhrase = searchPhrase,
                Source = source,
                SrpCompatibility = srpCompatibility,
                Maintenance = maintenance,
                Deprecation = deprecation,
                PriceOption = priceOption,
                Tag = tag,
                Publisher = publisher,
                Category = category,
                IncludeSubcategories = includeSubcategories,
                OnlyInProject = onlyInProject,
                SearchDescription = searchDescription,
                MinSizeMB = minSizeMb,
                MaxSizeMB = maxSizeMb,
                UpdatedBefore = updatedBefore,
                UpdatedAfter = updatedAfter,
                PurchasedBefore = purchasedBefore,
                PurchasedAfter = purchasedAfter,
                MaxResults = maxResults,
                Page = page
            }));
        }

        [CliCommand("asset_inventory_search_project_assets", "Search live files in the current project's Assets folder. Read-only.", Tags = new[] {"asset-inventory", "search"})]
        public static AssetInventoryCommandResult SearchProjectAssets(
            [CliArg("search_phrase", "Query supporting AND terms and -exclusions.", Required = true)] string searchPhrase,
            [CliArg("type", "File extension without a dot.")] string type = null,
            [CliArg("asset_group", "Asset category filter.")] string assetGroup = null,
            [CliArg("image_type", "Texture type such as albedo or normal.")] string imageType = null,
            [CliArg("min_width", "Minimum image width in pixels.")] int minWidth = 0,
            [CliArg("max_width", "Maximum image width in pixels.")] int maxWidth = 0,
            [CliArg("min_height", "Minimum image height in pixels.")] int minHeight = 0,
            [CliArg("max_height", "Maximum image height in pixels.")] int maxHeight = 0,
            [CliArg("min_size", "Minimum file size in bytes.")] long minSize = 0,
            [CliArg("max_size", "Maximum file size in bytes.")] long maxSize = 0,
            [CliArg("min_vertex_count", "Minimum model vertex count.")] int minVertexCount = 0,
            [CliArg("max_vertex_count", "Maximum model vertex count.")] int maxVertexCount = 0,
            [CliArg("max_results", "Results per page, from 1 through 100.")] int maxResults = 25,
            [CliArg("page", "One-based page number.")] int page = 1)
        {
            return PipelineResultAdapter.Convert(SearchProjectAssetsAutomation.SearchProjectAssets(new SearchProjectAssetsAutomation.SearchProjectAssetsRequest
            {
                SearchPhrase = searchPhrase,
                Type = type,
                AssetGroup = assetGroup,
                ImageType = imageType,
                MinWidth = minWidth,
                MaxWidth = maxWidth,
                MinHeight = minHeight,
                MaxHeight = maxHeight,
                MinSize = minSize,
                MaxSize = maxSize,
                MinVertexCount = minVertexCount,
                MaxVertexCount = maxVertexCount,
                MaxResults = maxResults,
                Page = page
            }));
        }

        [CliCommand("asset_inventory_list_package_files", "List files in one package with optional search and type filters. Read-only.", Tags = new[] {"asset-inventory", "search"})]
        public static AssetInventoryCommandResult ListPackageFiles(
            [CliArg("package_id", "Package ID from package search results.", Required = true)] int packageId,
            [CliArg("type", "File extension without a dot.")] string type = null,
            [CliArg("asset_group", "Asset category filter.")] string assetGroup = null,
            [CliArg("search_phrase", "Query supporting AND terms, -exclusions, ~exact phrases, and one leading = SQL predicate.")] string searchPhrase = null,
            [CliArg("max_results", "Results per page, from 1 through 200.")] int maxResults = 50,
            [CliArg("page", "One-based page number.")] int page = 1)
        {
            return PipelineResultAdapter.Convert(ListPackageFilesAutomation.ListPackageFiles(new ListPackageFilesAutomation.ListPackageFilesRequest
            {
                PackageId = packageId,
                Type = type,
                AssetGroup = assetGroup,
                SearchPhrase = searchPhrase,
                MaxResults = maxResults,
                Page = page
            }));
        }
    }
}
