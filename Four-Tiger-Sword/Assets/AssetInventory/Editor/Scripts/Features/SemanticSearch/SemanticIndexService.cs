using Brain;
using Database;
using ImpossibleRobert.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AssetInventory
{
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    internal static partial class SemanticIndexService
    {
        public const string DB_NAME = "AssetInventory.SemanticIndex.db";

        private const int InteractiveSearchTimeoutSeconds = 10;
        private const int EmbeddingBackendPreflightTimeoutSeconds = 3;
        private const int VectorScanBatchSize = 1000;
        private const int AssetIndexFilePageSize = 400;
        private const int StableKeyLookupBatchSize = 250;
        private const int EmbeddingBatchMaxAttempts = 3;
        private const string GenerationPropertyPrefix = "Generation-";
        private static IDatabaseConnection _db;

        public static string DatabasePath => IOUtils.PathCombine(Paths.GetSemanticIndexFolder(false), DB_NAME);

        public static bool Exists()
        {
            return File.Exists(DatabasePath);
        }

        public static IDatabaseConnection DB
        {
            get
            {
                EnsureInitialized();
                return _db;
            }
        }

        public static void EnsureInitialized()
        {
            if (_db != null && _db.IsConnected()) return;

            Directory.CreateDirectory(Paths.GetSemanticIndexFolder(false));
            string journalMode = SQLiteJournalModeResolver.Resolve(AI.Config.dbJournalMode, AI.Config.customStorageLocation, DatabasePath);
            _db = DatabaseFactory.CreateSQLiteConnection(DatabasePath, journalMode);
            CreateSchema(_db);
        }

        public static void CreateSchema(IDatabaseConnection db)
        {
            db.CreateTable<SemanticProfile>();
            db.CreateTable<SemanticItem>();
            db.CreateTable<SemanticVector>();
            db.CreateTable<SemanticProperty>();

            db.CreateIndex("SemanticProfile", new[] {"Provider", "Model", "Collection"});
            db.CreateIndex("SemanticItem", new[] {"ProfileId", "Collection", "StableKey"});
            db.CreateIndex("SemanticItem", new[] {"ProfileId", "AssetFileId"});
            db.CreateIndex("SemanticItem", new[] {"ProfileId", "Status"});
            db.CreateIndex("SemanticItem", new[] {"ProfileId", "Collection", "Status", "Id"});
            db.CreateIndex("SemanticVector", "SemanticItemId", true);
        }

        public static void Close()
        {
            _db?.Close();
            _db?.Dispose();
            _db = null;
        }

        public static bool DeleteIndex()
        {
            Close();
            bool deleted = false;
            foreach (string path in GetDatabaseFiles())
            {
                if (!File.Exists(path)) continue;
                File.Delete(path);
                deleted = true;
            }
            return deleted;
        }

        public static InventoryStats.SemanticIndexStatistics GetStats(bool includeIntegrityChecks = false)
        {
            InventoryStats.SemanticIndexStatistics stats = new InventoryStats.SemanticIndexStatistics
            {
                SidecarExists = Exists(),
                Status = Exists() ? "Available" : "Not created",
                Healthy = true
            };

            if (!stats.SidecarExists) return stats;

            try
            {
                EnsureInitialized();
                SemanticProfile profile = GetActiveProfileOrNull(SemanticContentBuilder.AssetCollection);
                if (profile != null)
                {
                    stats.ActiveProvider = profile.Provider;
                    stats.ActiveModel = profile.Model;
                    stats.Dimension = profile.Dimension;
                    stats.AssetItemsReady = CountItems(profile.Id, SemanticContentBuilder.AssetCollection, SemanticItem.ItemStatus.Ready);
                    stats.AssetItemsStale = CountItems(profile.Id, SemanticContentBuilder.AssetCollection, SemanticItem.ItemStatus.Dirty);
                    stats.AssetItemsError = CountItems(profile.Id, SemanticContentBuilder.AssetCollection, SemanticItem.ItemStatus.Error);
                    stats.DeletedItems = CountItems(profile.Id, SemanticContentBuilder.AssetCollection, SemanticItem.ItemStatus.Deleted);
                    stats.LastUpdatedAt = GetLastUpdatedAt(profile.Id);
                    stats.EligibleAssetCountLastRun = GetIntProperty($"Eligible-{profile.Id}");
                    stats.CoveragePercentLastRun = stats.EligibleAssetCountLastRun > 0
                        ? Mathf.Clamp01((float)stats.AssetItemsReady / stats.EligibleAssetCountLastRun) * 100f
                        : 0f;
                }

                SemanticProfile codeProfile = GetActiveProfileOrNull(SemanticContentBuilder.CodeCollection);
                if (codeProfile != null)
                {
                    stats.CodeChunksReady = CountItems(codeProfile.Id, SemanticContentBuilder.CodeCollection, SemanticItem.ItemStatus.Ready);
                    stats.CodeChunksStale = CountItems(codeProfile.Id, SemanticContentBuilder.CodeCollection, SemanticItem.ItemStatus.Dirty);
                    stats.CodeChunksError = CountItems(codeProfile.Id, SemanticContentBuilder.CodeCollection, SemanticItem.ItemStatus.Error);
                }

                stats.SemanticDatabaseSize = GetDatabaseFiles().Where(File.Exists).Sum(path => new FileInfo(path).Length);
                if (includeIntegrityChecks)
                {
                    stats.OrphanedItems = CountOrphanedItems();
                    stats.Status = stats.OrphanedItems > 0 ? "Repair recommended" : "Healthy";
                    stats.Healthy = stats.OrphanedItems == 0;
                }
                else
                {
                    stats.Status = "Healthy";
                    stats.Healthy = true;
                }
            }
            catch (Exception e)
            {
                stats.Healthy = false;
                stats.Status = $"Repair recommended: {e.Message}";
            }

            return stats;
        }

        public static async Task UpdateAssetIndex(SemanticIndexer progress, CancellationToken cancellationToken)
        {
            if (!AI.Actions.SemanticSearchEnabled) return;
            if (!AI.Config.semanticIndexAssets) return;

            if (!TryGetEmbeddingBackend(out EmbeddingProvider embeddingProvider, out string provider, out string model, out string serviceUrl))
            {
                Debug.LogWarning("Semantic search requires Ollama or LM Studio as the selected AI backend.");
                return;
            }

            if (!await IsEmbeddingBackendAvailable(embeddingProvider, serviceUrl, cancellationToken))
            {
                Debug.LogWarning($"Semantic index update skipped because {provider} is not reachable at {GetEmbeddingServiceUrl(embeddingProvider, serviceUrl)}.");
                return;
            }

            EnsureInitialized();

            SemanticProfile profile = GetOrCreateProfile(provider, model, SemanticContentBuilder.AssetCollection);
            int generation = NextGeneration(profile.Id);
            int eligibleCount = CountEligibleAssetFiles();

            SetIntProperty($"Eligible-{profile.Id}", eligibleCount);
            progress.SetSemanticProgressCount(eligibleCount);

            DateTime now = DateTime.UtcNow;
            int lastAssetFileId = 0;
            int processed = 0;
            int dirtyCount = 0;
            int embeddedCount = 0;

            while (true)
            {
                List<AssetInfo> files = LoadEligibleAssetFilePage(lastAssetFileId, AssetIndexFilePageSize);
                if (files.Count == 0) break;

                HashSet<int> packageIds = new HashSet<int>(files.Select(file => file.AssetId));
                HashSet<int> assetFileIds = new HashSet<int>(files.Select(file => file.Id));
                SemanticContentInputs inputs = SemanticContentBuilder.LoadInputs(packageIds, assetFileIds);

                List<SemanticPreparedWorkItem> prepared = new List<SemanticPreparedWorkItem>(files.Count);
                foreach (AssetInfo file in files)
                {
                    prepared.Add(new SemanticPreparedWorkItem(file, SemanticContentBuilder.BuildAssetContent(file, inputs)));
                }

                Dictionary<string, SemanticItem> existing = LoadExistingItems(profile.Id, prepared.Select(item => item.Content.StableKey));
                List<SemanticIndexWorkItem> pending = new List<SemanticIndexWorkItem>();

                foreach (SemanticPreparedWorkItem workItem in prepared)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    processed++;

                    AssetInfo file = workItem.File;
                    SemanticContent content = workItem.Content;
                    int progressValue = processed + embeddedCount;
                    progress.SetSemanticProgressCount(Math.Max(eligibleCount + dirtyCount, progressValue));
                    progress.SetProgress(file.FileName, progressValue);

                    if (!existing.TryGetValue(content.StableKey, out SemanticItem item))
                    {
                        item = new SemanticItem
                        {
                            ProfileId = profile.Id,
                            Collection = SemanticContentBuilder.AssetCollection,
                            StableKey = content.StableKey,
                            Status = SemanticItem.ItemStatus.Dirty
                        };
                        DB.Insert(item);
                        existing[content.StableKey] = item;
                    }

                    bool changed = item.ContentHash != content.Hash || item.Status != SemanticItem.ItemStatus.Ready;
                    item.AssetFileId = file.Id;
                    item.AssetId = file.AssetId;
                    item.Guid = file.Guid;
                    item.ContentHash = content.Hash;
                    item.LastSeenGeneration = generation;
                    item.UpdatedAt = now;
                    item.SourcePreview = content.SourcePreview;
                    item.ErrorMessage = null;
                    if (changed) item.Status = SemanticItem.ItemStatus.Dirty;
                    DB.Update(item);

                    if (!changed) continue;

                    pending.Add(new SemanticIndexWorkItem(item, content.Text));
                    dirtyCount++;
                    progress.SetSemanticProgressCount(Math.Max(eligibleCount + dirtyCount, processed + embeddedCount));
                }

                embeddedCount = await EmbedPendingWork(profile, pending, embeddingProvider, model, serviceUrl, progress, processed, embeddedCount, cancellationToken);
                lastAssetFileId = files[files.Count - 1].Id;
                await Task.Yield();
            }

            MarkUnseenItemsDeleted(profile.Id, generation);
            progress.SetSemanticProgressCount(processed + embeddedCount);
            profile.UpdatedAt = DateTime.UtcNow;
            DB.Update(profile);
        }

        public static List<SemanticSearchMatch> SearchAssets(string query, HashSet<int> allowedAssetFileIds, int maxResults, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(query) || !Exists()) return new List<SemanticSearchMatch>();
            if (allowedAssetFileIds != null && allowedAssetFileIds.Count == 0) return new List<SemanticSearchMatch>();

            int resultLimit = Mathf.Max(1, maxResults);
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(GetInteractiveSearchTimeoutSeconds()));
                CancellationToken searchToken = timeout.Token;

                EnsureInitialized();
                SemanticProfile profile = GetActiveProfileOrNull(SemanticContentBuilder.AssetCollection);
                if (profile == null || profile.Dimension <= 0) return new List<SemanticSearchMatch>();

                float[] queryVector = GetQueryEmbedding(query, profile, searchToken);
                if (queryVector == null || queryVector.Length != profile.Dimension) return new List<SemanticSearchMatch>();

                List<SemanticSearchMatch> matches = new List<SemanticSearchMatch>();
                int lastItemId = 0;
                while (true)
                {
                    searchToken.ThrowIfCancellationRequested();
                    List<SemanticVectorRow> rows = DB.Query<SemanticVectorRow>(
                        "select SemanticItem.Id as SemanticItemId, SemanticItem.AssetFileId as AssetFileId, SemanticVector.VectorBlob as VectorBlob " +
                        "from SemanticItem inner join SemanticVector on SemanticVector.SemanticItemId = SemanticItem.Id " +
                        "where SemanticItem.ProfileId=? and SemanticItem.Collection=? and SemanticItem.Status=? and SemanticItem.Id>? " +
                        $"order by SemanticItem.Id limit {VectorScanBatchSize}",
                        profile.Id,
                        SemanticContentBuilder.AssetCollection,
                        SemanticItem.ItemStatus.Ready,
                        lastItemId);
                    if (rows.Count == 0) break;

                    foreach (SemanticVectorRow row in rows)
                    {
                        searchToken.ThrowIfCancellationRequested();
                        if (allowedAssetFileIds != null && !allowedAssetFileIds.Contains(row.AssetFileId)) continue;

                        float[] vector = SemanticVectorUtils.FromBytes(row.VectorBlob);
                        float score = SemanticVectorUtils.Dot(queryVector, vector);
                        if (score < AI.Config.semanticMinScore) continue;
                        matches.Add(new SemanticSearchMatch(row.AssetFileId, score));
                    }

                    TrimSearchMatches(matches, resultLimit);
                    lastItemId = rows[rows.Count - 1].SemanticItemId;
                    if (rows.Count < VectorScanBatchSize) break;
                }

                return matches
                    .OrderByDescending(m => m.Score)
                    .ThenBy(m => m.AssetFileId)
                    .Take(resultLimit)
                    .ToList();
            }
        }

        internal static int GetInteractiveSearchTimeoutSeconds()
        {
            return AI.Config.aiTimeout > 0
                ? Mathf.Min(AI.Config.aiTimeout, InteractiveSearchTimeoutSeconds)
                : InteractiveSearchTimeoutSeconds;
        }

        internal static int GetEmbeddingBackendPreflightTimeoutSeconds()
        {
            return AI.Config.aiTimeout > 0
                ? Mathf.Min(AI.Config.aiTimeout, EmbeddingBackendPreflightTimeoutSeconds)
                : EmbeddingBackendPreflightTimeoutSeconds;
        }

        internal static bool HasSearchableAssetProfile()
        {
            if (!Exists()) return false;

            EnsureInitialized();
            SemanticProfile profile = GetActiveProfileOrNull(SemanticContentBuilder.AssetCollection);
            return profile != null && profile.Dimension > 0;
        }

        public static int RepairOrphans()
        {
            if (!Exists()) return 0;
            EnsureInitialized();

            HashSet<int> existingAssetFileIds = new HashSet<int>(DBAdapter.DB.QueryScalars<int>("select Id from AssetFile"));
            List<SemanticItem> orphaned = DB.Table<SemanticItem>()
                .Where(i => i.AssetFileId > 0)
                .ToList()
                .Where(i => !existingAssetFileIds.Contains(i.AssetFileId))
                .ToList();

            foreach (SemanticItem item in orphaned)
            {
                DB.Execute("delete from SemanticVector where SemanticItemId=?", item.Id);
                DB.Delete(item);
            }

            List<int> itemIds = DB.QueryScalars<int>("select Id from SemanticItem").ToList();
            HashSet<int> itemIdSet = new HashSet<int>(itemIds);
            List<SemanticVector> vectors = DB.Table<SemanticVector>().ToList();
            int orphanVectors = 0;
            foreach (SemanticVector vector in vectors)
            {
                if (itemIdSet.Contains(vector.SemanticItemId)) continue;
                DB.Delete(vector);
                orphanVectors++;
            }

            return orphaned.Count + orphanVectors;
        }

        private static SemanticProfile GetOrCreateProfile(string provider, string model, string collection)
        {
            SemanticProfile profile = DB.Table<SemanticProfile>()
                .FirstOrDefault(p => p.Provider == provider && p.Model == model && p.Collection == collection && p.Active);
            if (profile != null) return profile;

            DateTime now = DateTime.UtcNow;
            profile = new SemanticProfile
            {
                Provider = provider,
                Model = model,
                Collection = collection,
                Distance = "cosine",
                Encoding = "float32",
                Active = true,
                CreatedAt = now,
                UpdatedAt = now
            };
            DB.Insert(profile);
            return profile;
        }

        private static SemanticProfile GetActiveProfileOrNull(string collection)
        {
            if (!TryGetEmbeddingBackend(out _, out string provider, out string model, out _)) return null;

            if (!Exists()) return null;
            return DB.Table<SemanticProfile>()
                .FirstOrDefault(p => p.Provider == provider && p.Model == model && p.Collection == collection && p.Active);
        }

        private static float[] GetQueryEmbedding(string query, SemanticProfile profile, CancellationToken cancellationToken)
        {
            string queryHash = SemanticVectorUtils.HashText($"{profile.Id}:{query.Trim().ToLowerInvariant()}");
            string propertyName = $"Query-{queryHash}";
            SemanticProperty cached = DB.Find<SemanticProperty>(propertyName);
            if (cached != null)
            {
                try
                {
                    return SemanticVectorUtils.FromBytes(Convert.FromBase64String(cached.Value));
                }
                catch
                {
                    DB.Delete(cached);
                }
            }

            List<EmbeddingResult> result = Task.Run(() => EmbeddingEngine.EmbedTexts(
                    new List<string> {query},
                    profile.Model,
                    GetEmbeddingProviderForProfile(profile),
                    GetEmbeddingServiceUrlForProfile(profile),
                    GetInteractiveSearchTimeoutSeconds(),
                    cancellationToken), cancellationToken)
                .GetAwaiter()
                .GetResult();

            float[] vector = SemanticVectorUtils.Normalize(result.FirstOrDefault()?.embedding);
            if (vector == null || vector.Length == 0) return null;

            DB.InsertOrReplace(new SemanticProperty(propertyName, Convert.ToBase64String(SemanticVectorUtils.ToBytes(vector))));
            return vector;
        }

        private static void TrimSearchMatches(List<SemanticSearchMatch> matches, int resultLimit)
        {
            int threshold = resultLimit * 4;
            if (matches.Count <= threshold) return;

            matches.Sort(CompareSearchMatches);
            matches.RemoveRange(resultLimit, matches.Count - resultLimit);
        }

        private static int CompareSearchMatches(SemanticSearchMatch x, SemanticSearchMatch y)
        {
            int scoreCompare = y.Score.CompareTo(x.Score);
            return scoreCompare != 0 ? scoreCompare : x.AssetFileId.CompareTo(y.AssetFileId);
        }

        private static async Task<int> EmbedPendingWork(
            SemanticProfile profile,
            List<SemanticIndexWorkItem> pending,
            EmbeddingProvider embeddingProvider,
            string model,
            string serviceUrl,
            SemanticIndexer progress,
            int processed,
            int embeddedCount,
            CancellationToken cancellationToken)
        {
            int batchSize = Mathf.Max(1, AI.Config.semanticEmbeddingBatchSize);
            for (int i = 0; i < pending.Count; i += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                List<SemanticIndexWorkItem> batch = pending.Skip(i).Take(batchSize).ToList();
                embeddedCount += batch.Count;
                progress.SetProgress("Creating semantic embeddings", processed + embeddedCount);

                List<EmbeddingResult> embeddings = await EmbedBatchWithRetry(batch, embeddingProvider, model, serviceUrl, cancellationToken);
                SaveEmbeddingBatch(profile, batch, embeddings);
                await Task.Yield();
            }

            return embeddedCount;
        }

        private static async Task<List<EmbeddingResult>> EmbedBatchWithRetry(
            List<SemanticIndexWorkItem> batch,
            EmbeddingProvider embeddingProvider,
            string model,
            string serviceUrl,
            CancellationToken cancellationToken)
        {
            List<string> texts = batch.Select(b => b.Text).ToList();
            Exception lastException = null;

            for (int attempt = 1; attempt <= EmbeddingBatchMaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    return await EmbeddingEngine.EmbedTexts(
                        texts,
                        model,
                        embeddingProvider,
                        serviceUrl,
                        AI.Config.aiTimeout,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    lastException = e;
                    if (attempt >= EmbeddingBatchMaxAttempts) break;

                    Debug.LogWarning($"Semantic embedding batch failed on attempt {attempt}/{EmbeddingBatchMaxAttempts}: {e.Message}. Retrying...");
                    await Task.Delay(GetEmbeddingRetryDelay(attempt), cancellationToken);
                }
            }

            SaveEmbeddingBatchFailure(batch, lastException);
            throw new InvalidOperationException($"Semantic index update stopped because an embedding batch failed after {EmbeddingBatchMaxAttempts} attempts.", lastException);
        }

        private static int GetEmbeddingRetryDelay(int attempt)
        {
            return Mathf.Clamp(attempt * 500, 500, 2000);
        }

        private static void SaveEmbeddingBatch(SemanticProfile profile, List<SemanticIndexWorkItem> batch, List<EmbeddingResult> embeddings)
        {
            DateTime now = DateTime.UtcNow;
            DB.RunInTransaction(() =>
            {
                for (int i = 0; i < batch.Count; i++)
                {
                    SemanticItem item = batch[i].Item;
                    EmbeddingResult embedding = embeddings != null && i < embeddings.Count ? embeddings[i] : null;
                    float[] vector = SemanticVectorUtils.Normalize(embedding?.embedding);
                    if (vector == null || vector.Length == 0)
                    {
                        item.Status = SemanticItem.ItemStatus.Error;
                        item.ErrorMessage = embedding?.error ?? "Embedding response did not contain a vector.";
                        item.UpdatedAt = now;
                        DB.Update(item);
                        continue;
                    }

                    if (profile.Dimension <= 0)
                    {
                        profile.Dimension = vector.Length;
                        DB.Update(profile);
                    }

                    if (profile.Dimension != vector.Length)
                    {
                        item.Status = SemanticItem.ItemStatus.Error;
                        item.ErrorMessage = $"Expected {profile.Dimension} dimensions but received {vector.Length}.";
                        item.UpdatedAt = now;
                        DB.Update(item);
                        continue;
                    }

                    DB.Execute("delete from SemanticVector where SemanticItemId=?", item.Id);
                    DB.Insert(new SemanticVector
                    {
                        SemanticItemId = item.Id,
                        VectorBlob = SemanticVectorUtils.ToBytes(vector)
                    });

                    item.Status = SemanticItem.ItemStatus.Ready;
                    item.ErrorMessage = null;
                    item.UpdatedAt = now;
                    DB.Update(item);
                }
            });
        }

        private static void SaveEmbeddingBatchFailure(List<SemanticIndexWorkItem> batch, Exception exception)
        {
            DateTime now = DateTime.UtcNow;
            string message = exception?.Message ?? "Embedding request failed.";
            if (message.Length > 1000) message = message.Substring(0, 1000);

            DB.RunInTransaction(() =>
            {
                foreach (SemanticIndexWorkItem workItem in batch)
                {
                    SemanticItem item = workItem.Item;
                    item.Status = SemanticItem.ItemStatus.Error;
                    item.ErrorMessage = message;
                    item.UpdatedAt = now;
                    DB.Update(item);
                }
            });
        }

        internal static List<AssetInfo> LoadEligibleAssetFiles()
        {
            return LoadEligibleAssetFiles(BuildEligibleAssetFileFilter(out List<object> args), args);
        }

        internal static List<AssetInfo> LoadEligibleAssetFilePage(int lastAssetFileId, int limit)
        {
            if (limit <= 0) return new List<AssetInfo>();

            string filter = BuildEligibleAssetFileFilter(out List<object> args);
            if (filter == null) return new List<AssetInfo>();

            args.Add(lastAssetFileId);
            args.Add(limit);
            string query =
                "select Asset.*, AssetFile.*, AssetFile.Id as Id from AssetFile inner join Asset on Asset.Id = AssetFile.AssetId " +
                "left join Asset ParentAsset on ParentAsset.Id = Asset.ParentId " +
                $"where {filter} and AssetFile.Id>? " +
                "order by AssetFile.Id limit ?";

            return DBAdapter.DB.Query<AssetInfo>(query, args.ToArray());
        }

        private static int CountEligibleAssetFiles()
        {
            string filter = BuildEligibleAssetFileFilter(out List<object> args);
            if (filter == null) return 0;

            return DBAdapter.DB.ExecuteScalar<int>(
                "select count(*) from AssetFile inner join Asset on Asset.Id = AssetFile.AssetId " +
                "left join Asset ParentAsset on ParentAsset.Id = Asset.ParentId " +
                $"where {filter}",
                args.ToArray());
        }

        private static List<AssetInfo> LoadEligibleAssetFiles(string filter, List<object> args)
        {
            if (filter == null) return new List<AssetInfo>();

            string query =
                "select Asset.*, AssetFile.*, AssetFile.Id as Id from AssetFile inner join Asset on Asset.Id = AssetFile.AssetId " +
                "left join Asset ParentAsset on ParentAsset.Id = Asset.ParentId " +
                $"where {filter} " +
                "order by Asset.Id, AssetFile.Id";

            return DBAdapter.DB.Query<AssetInfo>(query, args.ToArray());
        }

        private static string BuildEligibleAssetFileFilter(out List<object> args)
        {
            string[] types = AI.ResolveExtensionList(AI.Config.semanticIndexExtensions);
            args = new List<object>();
            if (types.Length == 0) return null;

            List<string> placeholders = new List<string>();
            foreach (string type in types)
            {
                placeholders.Add("?");
                args.Add(type);
            }

            return
                "Asset.Exclude=0 and Asset.NoIndex=0 and (ParentAsset.NoIndex is null or ParentAsset.NoIndex=0) " +
                "and (AssetFile.Hidden is null or AssetFile.Hidden <> 1) " +
                "and (Asset.UseSemanticIndex is null or Asset.UseSemanticIndex <> 0) " +
                $"and AssetFile.Type in ({string.Join(",", placeholders)})";
        }

        private static Dictionary<string, SemanticItem> LoadExistingItems(int profileId, IEnumerable<string> stableKeys)
        {
            Dictionary<string, SemanticItem> result = new Dictionary<string, SemanticItem>(StringComparer.Ordinal);
            List<string> keys = stableKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            for (int i = 0; i < keys.Count; i += StableKeyLookupBatchSize)
            {
                List<string> batch = keys.Skip(i).Take(StableKeyLookupBatchSize).ToList();
                List<object> args = new List<object>
                {
                    profileId,
                    SemanticContentBuilder.AssetCollection
                };
                args.AddRange(batch.Cast<object>());

                string query =
                    "select * from SemanticItem where ProfileId=? and Collection=? " +
                    $"and StableKey in ({string.Join(",", batch.Select(_ => "?"))})";

                foreach (SemanticItem item in DB.Query<SemanticItem>(query, args.ToArray()))
                {
                    if (string.IsNullOrWhiteSpace(item.StableKey) || result.ContainsKey(item.StableKey)) continue;
                    result.Add(item.StableKey, item);
                }
            }

            return result;
        }

        private static void MarkUnseenItemsDeleted(int profileId, int generation)
        {
            DB.Execute(
                "update SemanticItem set Status=? where ProfileId=? and Collection=? and LastSeenGeneration<>?",
                SemanticItem.ItemStatus.Deleted,
                profileId,
                SemanticContentBuilder.AssetCollection,
                generation);
        }

        private static int NextGeneration(int profileId)
        {
            string propertyName = GenerationPropertyPrefix + profileId;
            int generation = GetIntProperty(propertyName) + 1;
            SetIntProperty(propertyName, generation);
            return generation;
        }

        private static int GetIntProperty(string name)
        {
            SemanticProperty property = DB.Find<SemanticProperty>(name);
            return property != null && int.TryParse(property.Value, out int result) ? result : 0;
        }

        private static void SetIntProperty(string name, int value)
        {
            DB.InsertOrReplace(new SemanticProperty(name, value.ToString()));
        }

        private static int CountItems(int profileId, string collection, SemanticItem.ItemStatus status)
        {
            return DB.ExecuteScalar<int>(
                "select count(*) from SemanticItem where ProfileId=? and Collection=? and Status=?",
                profileId,
                collection,
                status);
        }

        private static DateTime GetLastUpdatedAt(int profileId)
        {
            SemanticItem latest = DB.Query<SemanticItem>(
                    "select * from SemanticItem where ProfileId=? order by UpdatedAt desc limit 1",
                    profileId)
                .FirstOrDefault();
            return latest?.UpdatedAt ?? DateTime.MinValue;
        }

        private static int CountOrphanedItems()
        {
            HashSet<int> existingAssetFileIds = new HashSet<int>(DBAdapter.DB.QueryScalars<int>("select Id from AssetFile"));
            return DB.Table<SemanticItem>()
                .Where(i => i.AssetFileId > 0)
                .ToList()
                .Count(i => !existingAssetFileIds.Contains(i.AssetFileId));
        }

        private static EmbeddingProvider GetEmbeddingProvider()
        {
            return AI.Config.aiBackend == 2 ? EmbeddingProvider.LMStudio : EmbeddingProvider.Ollama;
        }

        private static string GetProviderName()
        {
            if (AI.Config.aiBackend == 0) return null;
            return GetEmbeddingProvider() == EmbeddingProvider.LMStudio ? "LM Studio" : "Ollama";
        }

        private static string GetEmbeddingServiceUrl()
        {
            return GetEmbeddingProvider() == EmbeddingProvider.LMStudio ? AI.Config.lmStudioServiceUrl : AI.Config.ollamaServiceUrl;
        }

        private static string GetEmbeddingModelName()
        {
            if (GetEmbeddingProvider() == EmbeddingProvider.LMStudio)
            {
                return string.IsNullOrWhiteSpace(AI.Config.semanticLmStudioEmbeddingModel)
                    ? AI.Config.lmStudioModel
                    : AI.Config.semanticLmStudioEmbeddingModel;
            }

            return string.IsNullOrWhiteSpace(AI.Config.semanticOllamaEmbeddingModel)
                ? EmbeddingEngine.DefaultOllamaEmbeddingModel
                : AI.Config.semanticOllamaEmbeddingModel;
        }

        internal static bool TryGetEmbeddingBackend(out EmbeddingProvider provider, out string providerName, out string model, out string serviceUrl)
        {
            provider = GetEmbeddingProvider();
            providerName = GetProviderName();
            model = GetEmbeddingModelName();
            serviceUrl = GetEmbeddingServiceUrl();
            return !string.IsNullOrWhiteSpace(providerName) && !string.IsNullOrWhiteSpace(model);
        }

        internal static async Task<bool> IsEmbeddingBackendAvailable(EmbeddingProvider provider, string serviceUrl, CancellationToken cancellationToken)
        {
            try
            {
                Uri baseUri = CreateServiceBaseUri(provider, serviceUrl);
                Uri requestUri = provider == EmbeddingProvider.LMStudio
                    ? new Uri(baseUri, "api/v0/models")
                    : baseUri;

                using (HttpClientHandler handler = new HttpClientHandler
                {
                    UseProxy = false,
                    AutomaticDecompression = DecompressionMethods.None,
                    AllowAutoRedirect = false
                })
                using (HttpClient client = new HttpClient(handler)
                {
                    Timeout = Timeout.InfiniteTimeSpan
                })
                using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(GetEmbeddingBackendPreflightTimeoutSeconds()));
                    using (HttpResponseMessage response = await client.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                    {
                        return response.IsSuccessStatusCode;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static Uri CreateServiceBaseUri(EmbeddingProvider provider, string serviceUrl)
        {
            string url = GetEmbeddingServiceUrl(provider, serviceUrl).TrimEnd('/') + "/";
            Uri baseUri = new Uri(url);
            if (!string.Equals(baseUri.Host, "localhost", StringComparison.OrdinalIgnoreCase)) return baseUri;

            UriBuilder builder = new UriBuilder(baseUri) { Host = "127.0.0.1" };
            return builder.Uri;
        }

        internal static string GetEmbeddingServiceUrl(EmbeddingProvider provider, string serviceUrl)
        {
            if (!string.IsNullOrWhiteSpace(serviceUrl)) return serviceUrl;
            return provider == EmbeddingProvider.LMStudio ? Intelligence.LMSTUDIO_SERVICE_URL : Intelligence.OLLAMA_SERVICE_URL;
        }

        private static EmbeddingProvider GetEmbeddingProviderForProfile(SemanticProfile profile)
        {
            return string.Equals(profile.Provider, "LM Studio", StringComparison.OrdinalIgnoreCase)
                ? EmbeddingProvider.LMStudio
                : EmbeddingProvider.Ollama;
        }

        private static string GetEmbeddingServiceUrlForProfile(SemanticProfile profile)
        {
            return GetEmbeddingProviderForProfile(profile) == EmbeddingProvider.LMStudio ? AI.Config.lmStudioServiceUrl : AI.Config.ollamaServiceUrl;
        }

        private static IEnumerable<string> GetDatabaseFiles()
        {
            string path = DatabasePath;
            yield return path;
            yield return path + "-wal";
            yield return path + "-shm";
        }

        private readonly struct SemanticIndexWorkItem
        {
            public readonly SemanticItem Item;
            public readonly string Text;

            public SemanticIndexWorkItem(SemanticItem item, string text)
            {
                Item = item;
                Text = text;
            }
        }

        private readonly struct SemanticPreparedWorkItem
        {
            public readonly AssetInfo File;
            public readonly SemanticContent Content;

            public SemanticPreparedWorkItem(AssetInfo file, SemanticContent content)
            {
                File = file;
                Content = content;
            }
        }

        internal readonly struct SemanticSearchMatch
        {
            public readonly int AssetFileId;
            public readonly float Score;

            public SemanticSearchMatch(int assetFileId, float score)
            {
                AssetFileId = assetFileId;
                Score = score;
            }
        }

        private sealed class SemanticVectorRow
        {
            public SemanticVectorRow()
            {
            }

            public int AssetFileId { get; set; }
            public int SemanticItemId { get; set; }
            public byte[] VectorBlob { get; set; }
        }
    }
}
