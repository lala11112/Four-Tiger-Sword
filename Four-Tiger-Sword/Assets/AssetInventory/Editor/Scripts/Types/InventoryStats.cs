namespace AssetInventory
{
    /// <summary>Snapshot of catalog totals, storage use, source distribution, and optional semantic and code index statistics.</summary>
    public sealed class InventoryStats
    {
        public int TotalPackages;
        public int IndexedPackages;
        public int IndexablePackages;
        public int IndexingEnabledPackages;
        public int EnabledIndexedPackages;
        public int NeedsIndexingPackages;
        public int IndexedWithoutFutureIndexingPackages;
        public int SubPackages;
        public int TotalFiles;
        public long DatabaseSize;
        public int PurchasedAssets;
        public int DeprecatedPackages;
        public int AbandonedPackages;
        public int ExcludedPackages;
        public int NoIndexPackages;
        public int BackupPackages;
        public int AIPackages;
        public int SemanticIndexPackages;
        public int CodeIndexPackages;
        public int CustomPackages;
        public int RegistryPackages;
        public SemanticIndexStatistics SemanticIndex;
        public CodeIndexStatistics CodeIndex;

        public int AllPackages => TotalPackages + SubPackages;

        public SourceBreakdown BySource;

        /// <summary>Catalog package counts grouped by Asset Store, custom, directory, registry, archive, and Asset Manager source.</summary>
        public sealed class SourceBreakdown
        {
            public int AssetStore;
            public int Custom;
            public int Directory;
            public int Registry;
            public int Archive;
            public int AssetManager;
        }

        /// <summary>Health, provider, model, coverage, cache, and maintenance statistics for the optional semantic-search index.</summary>
        public sealed class SemanticIndexStatistics
        {
            public bool SidecarExists;
            public bool Healthy;
            public string Status;
            public string ActiveProvider;
            public string ActiveModel;
            public int Dimension;
            public long SemanticDatabaseSize;
            public long FastCacheSize;
            public int AssetItemsReady;
            public int AssetItemsStale;
            public int AssetItemsError;
            public int CodeChunksReady;
            public int CodeChunksStale;
            public int CodeChunksError;
            public int OrphanedItems;
            public int DeletedItems;
            public int EligibleAssetCountLastRun;
            public float CoveragePercentLastRun;
            public System.DateTime LastUpdatedAt;
            public System.DateTime LastFullRebuildAt;
            public bool FastCacheAvailable;
            public bool FastCacheBuilt;
            public bool FastCacheStale;
            public float FastCacheTombstoneRatio;
        }

        /// <summary>Health, size, document, chunk, and maintenance statistics for the optional code-search index.</summary>
        public sealed class CodeIndexStatistics
        {
            public bool SidecarExists;
            public bool Healthy;
            public string Status;
            public bool FtsAvailable;
            public long CodeDatabaseSize;
            public int DocumentsReady;
            public int DocumentsDeleted;
            public int DocumentsError;
            public int ChunksReady;
            public int ChunksDeleted;
            public int ChunksError;
            public int OrphanedChunks;
            public System.DateTime LastUpdatedAt;
        }
    }
}
