using AssetInventory.Automation;
using Unity.AI.MCP.Editor.ToolRegistry;

namespace AssetInventory.Integration.UnityAIAssistant
{
    public static class AssistantSearchTools
    {
        public enum SrpFilter
        {
            All,
            Auto,
            BIRP,
            URP,
            HDRP
        }

        public enum PriceFilter
        {
            All,
            Free,
            Paid
        }

        public enum PackageSrpFilter
        {
            All,
            BIRP,
            URP,
            HDRP
        }

        public enum SourceFilter
        {
            All,
            AssetStore,
            CustomPackage,
            Directory,
            Registry,
            Archive,
            AssetManager
        }

        public enum DeprecationFilter
        {
            All,
            NotDeprecated,
            Deprecated
        }

        public sealed class SearchAssetsParams
        {
            [McpDescription("Search query. Supports AND terms, -exclusions, ~exact phrases, tag filters, and a leading = for one expert SQL WHERE expression.", Required = true)]
            public string SearchPhrase { get; set; }

            [McpDescription("File extension without a dot, such as prefab, png, or fbx.")]
            public string Type { get; set; }

            [McpDescription("Asset category filter.", EnumType = typeof(AI.AssetGroup))]
            public string AssetGroup { get; set; }

            [McpDescription("Filter by package tag.")]
            public string PackageTag { get; set; }

            [McpDescription("Filter by file tag.")]
            public string FileTag { get; set; }

            [McpDescription("Filter by publisher name.")]
            public string Publisher { get; set; }

            [McpDescription("Filter by category.")]
            public string Category { get; set; }

            [McpDescription("Include matching subcategories.", Default = true)]
            public bool IncludeSubcategories { get; set; } = true;

            [McpDescription("Render-pipeline compatibility filter.", EnumType = typeof(SrpFilter))]
            public string SrpCompatibility { get; set; }

            [McpDescription("Price filter.", EnumType = typeof(PriceFilter))]
            public string PriceOption { get; set; }

            [McpDescription("Minimum image width in pixels.")]
            public int MinWidth { get; set; }

            [McpDescription("Maximum image width in pixels.")]
            public int MaxWidth { get; set; }

            [McpDescription("Minimum image height in pixels.")]
            public int MinHeight { get; set; }

            [McpDescription("Maximum image height in pixels.")]
            public int MaxHeight { get; set; }

            [McpDescription("Minimum file size in bytes.")]
            public long MinSize { get; set; }

            [McpDescription("Maximum file size in bytes.")]
            public long MaxSize { get; set; }

            [McpDescription("Results per page, from 1 through 100.", Default = 25)]
            public int MaxResults { get; set; } = 25;

            [McpDescription("One-based page number.", Default = 1)]
            public int Page { get; set; } = 1;
        }

        public sealed class SearchPackagesParams
        {
            [McpDescription("Search query supporting AND terms, -exclusions, and ~exact phrases.")]
            public string SearchPhrase { get; set; }

            [McpDescription("Package source filter.", EnumType = typeof(SourceFilter))]
            public string Source { get; set; }

            [McpDescription("Render-pipeline compatibility filter.", EnumType = typeof(PackageSrpFilter))]
            public string SrpCompatibility { get; set; }

            [McpDescription("Package maintenance filter.", EnumType = typeof(PackageSearch.MaintenanceOption))]
            public string Maintenance { get; set; }

            [McpDescription("Package deprecation filter.", EnumType = typeof(DeprecationFilter))]
            public string Deprecation { get; set; }

            [McpDescription("Price filter.", EnumType = typeof(PriceFilter))]
            public string PriceOption { get; set; }

            [McpDescription("Filter by package tag.")]
            public string Tag { get; set; }

            [McpDescription("Filter by publisher name.")]
            public string Publisher { get; set; }

            [McpDescription("Filter by category.")]
            public string Category { get; set; }

            [McpDescription("Include matching subcategories.", Default = true)]
            public bool IncludeSubcategories { get; set; } = true;

            [McpDescription("Only return packages with files used in the current project.")]
            public bool OnlyInProject { get; set; }

            [McpDescription("Also search package descriptions.")]
            public bool SearchDescription { get; set; }

            [McpDescription("Minimum package size in MB.")]
            public float MinSizeMB { get; set; }

            [McpDescription("Maximum package size in MB.")]
            public float MaxSizeMB { get; set; }

            [McpDescription("Packages updated before this ISO 8601 date.")]
            public string UpdatedBefore { get; set; }

            [McpDescription("Packages updated after this ISO 8601 date.")]
            public string UpdatedAfter { get; set; }

            [McpDescription("Packages purchased before this ISO 8601 date.")]
            public string PurchasedBefore { get; set; }

            [McpDescription("Packages purchased after this ISO 8601 date.")]
            public string PurchasedAfter { get; set; }

            [McpDescription("Results per page, from 1 through 100.", Default = 25)]
            public int MaxResults { get; set; } = 25;

            [McpDescription("One-based page number.", Default = 1)]
            public int Page { get; set; } = 1;
        }

        public sealed class SearchProjectAssetsParams
        {
            [McpDescription("Search query supporting AND terms and -exclusions. Expert SQL and tag filters are not supported.", Required = true)]
            public string SearchPhrase { get; set; }

            [McpDescription("File extension without a dot.")]
            public string Type { get; set; }

            [McpDescription("Asset category filter.", EnumType = typeof(AI.AssetGroup))]
            public string AssetGroup { get; set; }

            [McpDescription("Texture type such as albedo, normal, specular, metal, occlusion, or emission.")]
            public string ImageType { get; set; }

            [McpDescription("Minimum image width in pixels.")]
            public int MinWidth { get; set; }

            [McpDescription("Maximum image width in pixels.")]
            public int MaxWidth { get; set; }

            [McpDescription("Minimum image height in pixels.")]
            public int MinHeight { get; set; }

            [McpDescription("Maximum image height in pixels.")]
            public int MaxHeight { get; set; }

            [McpDescription("Minimum file size in bytes.")]
            public long MinSize { get; set; }

            [McpDescription("Maximum file size in bytes.")]
            public long MaxSize { get; set; }

            [McpDescription("Minimum model vertex count.")]
            public int MinVertexCount { get; set; }

            [McpDescription("Maximum model vertex count.")]
            public int MaxVertexCount { get; set; }

            [McpDescription("Results per page, from 1 through 100.", Default = 25)]
            public int MaxResults { get; set; } = 25;

            [McpDescription("One-based page number.", Default = 1)]
            public int Page { get; set; } = 1;
        }

        public sealed class ListPackageFilesParams
        {
            [McpDescription("Package ID from package search results.", Required = true)]
            public int PackageId { get; set; }

            [McpDescription("File extension without a dot.")]
            public string Type { get; set; }

            [McpDescription("Asset category filter.", EnumType = typeof(AI.AssetGroup))]
            public string AssetGroup { get; set; }

            [McpDescription("Search within names and paths. Supports AND terms, -exclusions, ~exact phrases, and a leading = for one expert SQL WHERE expression.")]
            public string SearchPhrase { get; set; }

            [McpDescription("Results per page, from 1 through 200.", Default = 50)]
            public int MaxResults { get; set; } = 50;

            [McpDescription("One-based page number.", Default = 1)]
            public int Page { get; set; } = 1;
        }

        [McpTool("AssetInventory_searchAssets", "Search individual files across indexed packages. Read-only.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Search"})]
        public static object SearchAssets(SearchAssetsParams parameters)
        {
            parameters = parameters ?? new SearchAssetsParams();
            return AssistantResponseAdapter.Convert(SearchAssetsAutomation.SearchAssets(new SearchAssetsAutomation.SearchAssetsRequest
            {
                SearchPhrase = parameters.SearchPhrase,
                Type = parameters.Type,
                AssetGroup = parameters.AssetGroup,
                PackageTag = parameters.PackageTag,
                FileTag = parameters.FileTag,
                Publisher = parameters.Publisher,
                Category = parameters.Category,
                IncludeSubcategories = parameters.IncludeSubcategories,
                SrpCompatibility = parameters.SrpCompatibility,
                PriceOption = parameters.PriceOption,
                MinWidth = parameters.MinWidth,
                MaxWidth = parameters.MaxWidth,
                MinHeight = parameters.MinHeight,
                MaxHeight = parameters.MaxHeight,
                MinSize = parameters.MinSize,
                MaxSize = parameters.MaxSize,
                MaxResults = parameters.MaxResults,
                Page = parameters.Page
            }));
        }

        [McpTool("AssetInventory_searchPackages", "Search packages by name, source, status, and metadata. Read-only.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Search"})]
        public static object SearchPackages(SearchPackagesParams parameters)
        {
            parameters = parameters ?? new SearchPackagesParams();
            return AssistantResponseAdapter.Convert(SearchPackagesAutomation.SearchPackages(new SearchPackagesAutomation.SearchPackagesRequest
            {
                SearchPhrase = parameters.SearchPhrase,
                Source = parameters.Source,
                SrpCompatibility = parameters.SrpCompatibility,
                Maintenance = parameters.Maintenance,
                Deprecation = parameters.Deprecation,
                PriceOption = parameters.PriceOption,
                Tag = parameters.Tag,
                Publisher = parameters.Publisher,
                Category = parameters.Category,
                IncludeSubcategories = parameters.IncludeSubcategories,
                OnlyInProject = parameters.OnlyInProject,
                SearchDescription = parameters.SearchDescription,
                MinSizeMB = parameters.MinSizeMB,
                MaxSizeMB = parameters.MaxSizeMB,
                UpdatedBefore = parameters.UpdatedBefore,
                UpdatedAfter = parameters.UpdatedAfter,
                PurchasedBefore = parameters.PurchasedBefore,
                PurchasedAfter = parameters.PurchasedAfter,
                MaxResults = parameters.MaxResults,
                Page = parameters.Page
            }));
        }

        [McpTool("AssetInventory_searchProjectAssets", "Search live files in the current project's Assets folder. Read-only.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Search"})]
        public static object SearchProjectAssets(SearchProjectAssetsParams parameters)
        {
            parameters = parameters ?? new SearchProjectAssetsParams();
            return AssistantResponseAdapter.Convert(SearchProjectAssetsAutomation.SearchProjectAssets(new SearchProjectAssetsAutomation.SearchProjectAssetsRequest
            {
                SearchPhrase = parameters.SearchPhrase,
                Type = parameters.Type,
                AssetGroup = parameters.AssetGroup,
                ImageType = parameters.ImageType,
                MinWidth = parameters.MinWidth,
                MaxWidth = parameters.MaxWidth,
                MinHeight = parameters.MinHeight,
                MaxHeight = parameters.MaxHeight,
                MinSize = parameters.MinSize,
                MaxSize = parameters.MaxSize,
                MinVertexCount = parameters.MinVertexCount,
                MaxVertexCount = parameters.MaxVertexCount,
                MaxResults = parameters.MaxResults,
                Page = parameters.Page
            }));
        }

        [McpTool("AssetInventory_listPackageFiles", "List files in one package with optional search and type filters. Read-only.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Search"})]
        public static object ListPackageFiles(ListPackageFilesParams parameters)
        {
            parameters = parameters ?? new ListPackageFilesParams();
            return AssistantResponseAdapter.Convert(ListPackageFilesAutomation.ListPackageFiles(new ListPackageFilesAutomation.ListPackageFilesRequest
            {
                PackageId = parameters.PackageId,
                Type = parameters.Type,
                AssetGroup = parameters.AssetGroup,
                SearchPhrase = parameters.SearchPhrase,
                MaxResults = parameters.MaxResults,
                Page = parameters.Page
            }));
        }
    }
}
