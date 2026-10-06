using Brain;
using Database;
using ImpossibleRobert.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    internal static partial class CodeIndexService
    {
        public const string DB_NAME = "AssetInventory.CodeIndex.db";

        private const int SchemaVersion = 1;
        private const int PackageCodePackagePageSize = 100;
        private const int PackageCodeFilePageSize = 400;
        private const int PackageMaterializationBatchSize = 25;
        private const int StableKeyLookupBatchSize = 250;
        private const string GenerationProperty = "Generation";
        private const string SchemaVersionProperty = "SchemaVersion";
        private const string FtsAvailableProperty = "FtsAvailable";
        private static IDatabaseConnection _db;
        private static bool? _ftsAvailable;
        private static bool _ftsCoverageChecked;
        private static bool _ftsCoverageUsable = true;

        public static string DatabasePath => IOUtils.PathCombine(Paths.GetSemanticIndexFolder(false), DB_NAME);

        public static IDatabaseConnection DB
        {
            get
            {
                EnsureInitialized();
                return _db;
            }
        }

        public static bool Exists()
        {
            return File.Exists(DatabasePath);
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
            db.CreateTable<CodeDocument>();
            db.CreateTable<CodeChunk>();
            db.CreateTable<CodeIndexProperty>();
            db.CreateTable<CodeEmbeddingProfile>();
            db.CreateTable<CodeChunkVector>();

            db.CreateIndex("CodeDocument", "StableKey", true);
            db.CreateIndex("CodeDocument", new[] {"Status", "SourceKind", "Extension"});
            db.CreateIndex("CodeDocument", new[] {"AssetFileId", "Status"});
            db.CreateIndex("CodeDocument", new[] {"Guid", "Status"});
            db.CreateIndex("CodeChunk", new[] {"DocumentId", "Status"});
            db.CreateIndex("CodeChunk", "StableKey", true);
            db.CreateIndex("CodeChunkVector", new[] {"CodeChunkId", "ProfileId"}, true);

            bool ftsAvailable = TryCreateFtsTable(db);
            _ftsAvailable = ftsAvailable;
            db.InsertOrReplace(new CodeIndexProperty(SchemaVersionProperty, SchemaVersion.ToString()));
            db.InsertOrReplace(new CodeIndexProperty(FtsAvailableProperty, ftsAvailable ? "true" : "false"));
        }

        public static void Close()
        {
            _db?.Close();
            _db?.Dispose();
            _db = null;
            _ftsAvailable = null;
            _ftsCoverageChecked = false;
            _ftsCoverageUsable = true;
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

        public static InventoryStats.CodeIndexStatistics GetStats(bool includeIntegrityChecks = false)
        {
            InventoryStats.CodeIndexStatistics stats = new InventoryStats.CodeIndexStatistics
            {
                SidecarExists = Exists(),
                Status = Exists() ? "Available" : "Not created",
                Healthy = true
            };
            if (!stats.SidecarExists) return stats;

            try
            {
                EnsureInitialized();
                stats.FtsAvailable = IsFtsAvailable();
                stats.CodeDatabaseSize = GetDatabaseFiles().Where(File.Exists).Sum(path => new FileInfo(path).Length);
                stats.DocumentsReady = CountDocuments(CodeDocument.DocumentStatus.Ready);
                stats.DocumentsDeleted = CountDocuments(CodeDocument.DocumentStatus.Deleted);
                stats.DocumentsError = CountDocuments(CodeDocument.DocumentStatus.Error);
                stats.ChunksReady = CountChunks(CodeChunk.ChunkStatus.Ready);
                stats.ChunksDeleted = CountChunks(CodeChunk.ChunkStatus.Deleted);
                stats.ChunksError = CountChunks(CodeChunk.ChunkStatus.Error);
                stats.LastUpdatedAt = GetLastUpdatedAt();
                if (includeIntegrityChecks)
                {
                    stats.OrphanedChunks = CountOrphanedChunks();
                    stats.Status = stats.OrphanedChunks > 0 ? "Repair recommended" : "Healthy";
                    stats.Healthy = stats.OrphanedChunks == 0;
                }
                else
                {
                    stats.Status = "Healthy";
                }
            }
            catch (Exception e)
            {
                stats.Healthy = false;
                stats.Status = $"Repair recommended: {e.Message}";
            }
            return stats;
        }

        public static async Task UpdateIndex(CodeIndexer progress, CancellationToken cancellationToken)
        {
            if (!AI.Actions.CodeSearchEnabled) return;

            progress.SetCodeMainProgress("Preparing code search index", 0, 1);
            await Task.Yield();

            if (!await CanUpdateCodeIndex(cancellationToken)) return;

            EnsureInitialized();
            cancellationToken.ThrowIfCancellationRequested();

            HashSet<string> allowedExtensions = GetAllowedExtensions();
            List<ProjectCodeFile> projectFiles = AI.Config.codeIndexProjectFiles
                ? LoadProjectCodeFiles(allowedExtensions, cancellationToken)
                : new List<ProjectCodeFile>();
            await Task.Yield();

            int packageCount = AI.Config.codeIndexPackageFiles
                ? CountEligiblePackageCodeFilePackages(allowedExtensions)
                : 0;

            int generation = NextGeneration();
            int mainCount = (projectFiles.Count > 0 ? 1 : 0) + packageCount;
            progress.SetCodeProgressCount(mainCount);

            DateTime now = DateTime.UtcNow;
            int mainProgress = 0;
            if (projectFiles.Count > 0)
            {
                mainProgress++;
                progress.SetCodeMainProgress("Project files", mainProgress, mainCount);
                await IndexProjectCodeFiles(progress, projectFiles, generation, now, cancellationToken);
                progress.ClearCodeSubProgress();
            }

            if (AI.Config.codeIndexPackageFiles)
            {
                mainProgress = await IndexPackageCandidates(progress, allowedExtensions, generation, now, mainProgress, mainCount, cancellationToken);
            }

            progress.ClearCodeSubProgress();
            MarkUnseenDeleted(generation, now);
            SetProperty("LastUpdatedAt", now.Ticks.ToString());
        }

        public static async Task UpdateProjectChanges(CodeIndexer progress, CodeIndexAssetChanges changes, CancellationToken cancellationToken)
        {
            if (!AI.Actions.CodeSearchEnabled) return;
            if (!AI.Config.codeIndexProjectFiles || changes.IsEmpty) return;
            if (!await CanUpdateCodeIndex(cancellationToken)) return;

            EnsureInitialized();
            cancellationToken.ThrowIfCancellationRequested();

            DateTime now = DateTime.UtcNow;
            List<string> deletedPaths = NormalizeChangedPaths(changes.DeletedPaths);
            List<CodeIndexCandidate> changedCandidates = LoadProjectCandidates(changes.ChangedPaths, cancellationToken);
            int totalChanges = changedCandidates.Count + deletedPaths.Count;
            if (totalChanges == 0) return;
            progress.SetCodeProgressCount(totalChanges);

            int generation = GetIntProperty(GenerationProperty);
            int processed = 0;
            Dictionary<string, CodeDocument> existing = LoadExistingDocuments(changedCandidates.Select(candidate => candidate.StableKey));
            foreach (CodeIndexCandidate candidate in changedCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processed++;
                progress.SetProgress(candidate.Path, processed);
                IndexCandidate(candidate, existing, generation, now);
            }

            foreach (string path in deletedPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processed++;
                progress.SetProgress(path, processed);
                MarkProjectDocumentDeleted(path, now);
            }

            if (processed > 0) SetProperty("LastUpdatedAt", now.Ticks.ToString());
        }

        public static CodeSearch.Result Search(CodeSearch.Options options)
        {
            if (!AI.Actions.CodeSearchEnabled) return new CodeSearch.Result();

            CodeSearch.Result result = new CodeSearch.Result
            {
                IndexExists = Exists()
            };
            if (!result.IndexExists) return result;

            EnsureInitialized();
            result.FtsAvailable = IsFtsAvailable();

            CodeSearchQuery query = CodeSearchQuery.Parse(options.SearchPhrase);
            SearchScope searchScope = SearchScopeModel.Normalize(options.Scope, true);
            if (!SearchScopeModel.UsesCodeProjectSearch(searchScope) && !SearchScopeModel.UsesCodeIndexSearch(searchScope))
            {
                result.Error = "No code source is selected.";
                return result;
            }
            if (!query.HasSearchText && !query.HasFilters)
            {
                result.DocumentCount = CountDocuments(CodeDocument.DocumentStatus.Ready, searchScope);
                return result;
            }

            List<CodeSearchRow> rows;
            int matchCount;
            int documentCount;
            if (result.FtsAvailable && query.HasSearchText && EnsureFtsCoverage())
            {
                rows = SearchFts(query, options, out matchCount, out documentCount);
                if (rows == null) rows = SearchFallback(query, options, out matchCount, out documentCount);
            }
            else
            {
                rows = SearchFallback(query, options, out matchCount, out documentCount);
            }

            List<ScoredCodeSearchRow> scored = rows
                .Select(row => new ScoredCodeSearchRow(row, CalculateScore(row, query, result.FtsAvailable)))
                .OrderByDescending(row => row.Score)
                .ThenBy(row => row.Row.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Row.StartLine)
                .ToList();

            IReadOnlyCollection<string> highlightTerms = query.HighlightTerms;
            int fileSkip = GetCodeSearchFileSkip(options);
            int maxFiles = Mathf.Max(1, options.MaxFiles);
            foreach (IGrouping<int, ScoredCodeSearchRow> group in scored.GroupBy(row => row.Row.DocumentId).Skip(fileSkip))
            {
                if (result.Files.Count >= maxFiles) break;

                ScoredCodeSearchRow first = group.First();
                CodeSearch.CodeSearchFileResult fileResult = new CodeSearch.CodeSearchFileResult
                {
                    DocumentId = first.Row.DocumentId,
                    Path = first.Row.Path,
                    FileName = first.Row.FileName,
                    PhysicalPath = first.Row.PhysicalPath,
                    PackageName = first.Row.PackageName,
                    Language = first.Row.Language,
                    Extension = first.Row.Extension,
                    SourceKind = first.Row.SourceKind,
                    Score = first.Score
                };

                foreach (ScoredCodeSearchRow scoredRow in group.Take(Mathf.Max(1, options.MaxMatchesPerFile)))
                {
                    CodeSearchRow row = scoredRow.Row;
                    fileResult.Matches.Add(new CodeSearch.CodeSearchMatch
                    {
                        ChunkId = row.ChunkId,
                        StartLine = row.StartLine,
                        EndLine = row.EndLine,
                        Symbol = row.Symbol,
                        Content = row.Content,
                        Snippet = CodeSnippetBuilder.BuildSnippet(row.Content, row.StartLine, highlightTerms),
                        Score = scoredRow.Score
                    });
                }

                result.Files.Add(fileResult);
            }

            result.ResultCount = matchCount;
            result.DocumentCount = documentCount;
            return result;
        }

        public static int RepairOrphans()
        {
            if (!Exists()) return 0;
            EnsureInitialized();

            HashSet<int> documentIds = new HashSet<int>(DB.QueryScalars<int>("select Id from CodeDocument"));
            List<CodeChunk> orphanedChunks = DB.Table<CodeChunk>().ToList().Where(c => !documentIds.Contains(c.DocumentId)).ToList();
            foreach (CodeChunk chunk in orphanedChunks)
            {
                DeleteFtsRow(chunk.Id);
                DB.Delete(chunk);
            }

            HashSet<int> chunkIds = new HashSet<int>(DB.QueryScalars<int>("select Id from CodeChunk"));
            List<CodeChunkVector> vectors = DB.Table<CodeChunkVector>().ToList().Where(v => !chunkIds.Contains(v.CodeChunkId)).ToList();
            foreach (CodeChunkVector vector in vectors) DB.Delete(vector);
            return orphanedChunks.Count + vectors.Count;
        }

        internal static bool TryProbeFts5(IDatabaseConnection db)
        {
            try
            {
                db.Execute("DROP TABLE IF EXISTS temp.AssetInventoryCodeFtsProbe");
                db.Execute("CREATE VIRTUAL TABLE temp.AssetInventoryCodeFtsProbe USING fts5(Content)");
                db.Execute("DROP TABLE IF EXISTS temp.AssetInventoryCodeFtsProbe");
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static string BuildFtsQueryForTests(string query)
        {
            return CodeSearchQuery.Parse(query).BuildFtsQuery();
        }

        private static bool TryCreateFtsTable(IDatabaseConnection db)
        {
            if (!TryProbeFts5(db)) return false;

            db.Execute("CREATE VIRTUAL TABLE IF NOT EXISTS CodeChunkFts USING fts5(DocumentId UNINDEXED, ChunkId UNINDEXED, StableKey UNINDEXED, Path, FileName, Language, Symbol, Content, tokenize = 'unicode61 tokenchars ''_''')");
            return true;
        }

        private static bool IsFtsAvailable()
        {
            if (_ftsAvailable.HasValue) return _ftsAvailable.Value;
            _ftsAvailable = TryCreateFtsTable(DB);
            SetProperty(FtsAvailableProperty, _ftsAvailable.Value ? "true" : "false");
            return _ftsAvailable.Value;
        }

        private static bool EnsureFtsCoverage()
        {
            if (!IsFtsAvailable()) return false;
            if (_ftsCoverageChecked) return _ftsCoverageUsable;

            _ftsCoverageChecked = true;
            try
            {
                int missingRows = CountMissingFtsRows();
                if (missingRows > 0) RepairMissingFtsRows();
                _ftsCoverageUsable = true;
            }
            catch (Exception e)
            {
                _ftsCoverageUsable = false;
                Debug.LogWarning($"Code search FTS index is incomplete and could not be repaired, falling back to token search: {e.Message}");
            }
            return _ftsCoverageUsable;
        }

        private static int CountMissingFtsRows()
        {
            return DB.ExecuteScalar<int>(
                "select count(*) from CodeChunk c " +
                "join CodeDocument d on d.Id = c.DocumentId " +
                "left join CodeChunkFts f on f.rowid = c.Id " +
                "where d.Status=? and c.Status=? and f.rowid is null",
                CodeDocument.DocumentStatus.Ready,
                CodeChunk.ChunkStatus.Ready);
        }

        private static void RepairMissingFtsRows()
        {
            DB.Execute(
                "insert or replace into CodeChunkFts(rowid, DocumentId, ChunkId, StableKey, Path, FileName, Language, Symbol, Content) " +
                "select c.Id, d.Id, c.Id, c.StableKey, d.Path, d.FileName, d.Language, c.Symbol, c.Content " +
                "from CodeChunk c join CodeDocument d on d.Id = c.DocumentId " +
                "left join CodeChunkFts f on f.rowid = c.Id " +
                "where d.Status=? and c.Status=? and f.rowid is null",
                CodeDocument.DocumentStatus.Ready,
                CodeChunk.ChunkStatus.Ready);
        }

        private static void IndexCandidate(CodeIndexCandidate candidate, Dictionary<string, CodeDocument> existing, int generation, DateTime now)
        {
            CodeDocument document = existing.TryGetValue(candidate.StableKey, out CodeDocument found) ? found : null;
            if (document != null
                && document.ContentHash == candidate.ContentHash
                && document.Size == candidate.Size
                && document.LastWriteTicks == candidate.LastWriteTicks
                && document.Status == CodeDocument.DocumentStatus.Ready)
            {
                ApplyCandidateMetadata(document, candidate, generation, now);
                DB.Update(document);
                return;
            }

            if (document == null)
            {
                document = new CodeDocument
                {
                    StableKey = candidate.StableKey
                };
                ApplyCandidateMetadata(document, candidate, generation, now);
                DB.Insert(document);
                existing[candidate.StableKey] = document;
            }
            else
            {
                ApplyCandidateMetadata(document, candidate, generation, now);
                DB.Update(document);
            }

            List<CodeChunkData> chunks = CodeSnippetBuilder.CreateChunks(candidate.Content, candidate.Language);
            ReplaceChunks(document, chunks, now);
        }

        private static void ApplyCandidateMetadata(CodeDocument document, CodeIndexCandidate candidate, int generation, DateTime now)
        {
            document.SourceKind = candidate.SourceKind;
            document.AssetId = candidate.AssetId;
            document.AssetFileId = candidate.AssetFileId;
            document.Guid = candidate.Guid;
            document.Extension = candidate.Extension;
            document.Status = CodeDocument.DocumentStatus.Ready;
            document.Path = candidate.Path;
            document.FileName = candidate.FileName;
            document.PhysicalPath = candidate.PhysicalPath;
            document.PackageName = candidate.PackageName;
            document.Language = candidate.Language;
            document.Size = candidate.Size;
            document.LastWriteTicks = candidate.LastWriteTicks;
            document.ContentHash = candidate.ContentHash;
            document.LastSeenGeneration = generation;
            document.UpdatedAt = now;
            document.ErrorMessage = null;
        }

        private static void ReplaceChunks(CodeDocument document, List<CodeChunkData> chunks, DateTime now)
        {
            DB.RunInTransaction(() =>
            {
                List<int> existingChunkIds = DB.QueryScalars<int>("select Id from CodeChunk where DocumentId=?", document.Id);
                foreach (int id in existingChunkIds)
                {
                    DeleteFtsRow(id);
                    DB.Execute("delete from CodeChunkVector where CodeChunkId=?", id);
                }
                DB.Execute("delete from CodeChunk where DocumentId=?", document.Id);

                foreach (CodeChunkData chunkData in chunks)
                {
                    CodeChunk chunk = new CodeChunk
                    {
                        DocumentId = document.Id,
                        StableKey = $"{document.StableKey}:{chunkData.ChunkKey}",
                        ChunkKey = chunkData.ChunkKey,
                        ContentHash = chunkData.ContentHash,
                        Status = CodeChunk.ChunkStatus.Ready,
                        StartLine = chunkData.StartLine,
                        EndLine = chunkData.EndLine,
                        Symbol = chunkData.Symbol,
                        Content = chunkData.Content,
                        UpdatedAt = now
                    };
                    DB.Insert(chunk);
                    InsertFtsRow(document, chunk);
                }
            });
        }

        private static void InsertFtsRow(CodeDocument document, CodeChunk chunk)
        {
            if (!IsFtsAvailable()) return;

            DB.Execute(
                "insert into CodeChunkFts(rowid, DocumentId, ChunkId, StableKey, Path, FileName, Language, Symbol, Content) values (?, ?, ?, ?, ?, ?, ?, ?, ?)",
                chunk.Id,
                document.Id,
                chunk.Id,
                chunk.StableKey,
                document.Path,
                document.FileName,
                document.Language,
                chunk.Symbol,
                chunk.Content);
        }

        private static void DeleteFtsRow(int chunkId)
        {
            if (!IsFtsAvailable()) return;
            DB.Execute("delete from CodeChunkFts where rowid=?", chunkId);
        }

        private static async Task<bool> CanUpdateCodeIndex(CancellationToken cancellationToken)
        {
            if (!AI.Config.codeIndexSemanticRerank) return true;

            if (!SemanticIndexService.TryGetEmbeddingBackend(out EmbeddingProvider embeddingProvider, out string provider, out _, out string serviceUrl))
            {
                Debug.LogWarning("Code index semantic rerank requires Ollama or LM Studio as the selected AI backend.");
                return false;
            }

            if (await SemanticIndexService.IsEmbeddingBackendAvailable(embeddingProvider, serviceUrl, cancellationToken)) return true;

            Debug.LogWarning($"Code index update skipped because semantic code rerank needs {provider}, but it is not reachable at {SemanticIndexService.GetEmbeddingServiceUrl(embeddingProvider, serviceUrl)}.");
            return false;
        }

        private static List<ProjectCodeFile> LoadProjectCodeFiles(HashSet<string> allowedExtensions, CancellationToken cancellationToken)
        {
            List<ProjectCodeFile> result = new List<ProjectCodeFile>();
            if (allowedExtensions == null || allowedExtensions.Count == 0) return result;

            string[] guids = AssetDatabase.FindAssets("", new[] {"Assets"});
            foreach (string guid in guids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ProjectCodeFile file = CreateProjectCodeFile(path, guid, allowedExtensions);
                if (file != null) result.Add(file);
            }
            return result;
        }

        private static List<CodeIndexCandidate> LoadProjectCandidates(IEnumerable<string> paths, CancellationToken cancellationToken)
        {
            HashSet<string> allowedExtensions = GetAllowedExtensions();
            List<CodeIndexCandidate> result = new List<CodeIndexCandidate>();
            foreach (string path in NormalizeChangedPaths(paths))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string guid = AssetDatabase.AssetPathToGUID(path);
                CodeIndexCandidate candidate = CreateProjectCandidate(path, guid, allowedExtensions);
                if (candidate != null) result.Add(candidate);
            }
            return result;
        }

        private static CodeIndexCandidate CreateProjectCandidate(string path, string guid, HashSet<string> allowedExtensions)
        {
            ProjectCodeFile file = CreateProjectCodeFile(path, guid, allowedExtensions);
            return file == null ? null : CreateProjectCandidate(file);
        }

        private static ProjectCodeFile CreateProjectCodeFile(string path, string guid, HashSet<string> allowedExtensions)
        {
            if (string.IsNullOrWhiteSpace(path) || Directory.Exists(path)) return null;
            string extension = NormalizeExtension(Path.GetExtension(path));
            if (allowedExtensions == null || !allowedExtensions.Contains(extension)) return null;
            if (string.IsNullOrWhiteSpace(guid)) return null;

            string normalizedPath = NormalizePath(path);
            return new ProjectCodeFile
            {
                StableKey = $"project:{guid}",
                Path = normalizedPath,
                PhysicalPath = Path.GetFullPath(path),
                Guid = guid,
                Extension = extension,
                FileName = Path.GetFileName(normalizedPath),
                Language = GetLanguage(extension)
            };
        }

        private static CodeIndexCandidate CreateProjectCandidate(ProjectCodeFile file)
        {
            return CreateCandidate(
                file.StableKey,
                CodeDocument.SourceKindType.Project,
                file.Path,
                file.PhysicalPath,
                file.Guid,
                0,
                0,
                null,
                file.Extension);
        }

        private static async Task IndexProjectCodeFiles(
            CodeIndexer progress,
            List<ProjectCodeFile> files,
            int generation,
            DateTime now,
            CancellationToken cancellationToken)
        {
            Dictionary<string, CodeDocument> existing = LoadExistingDocuments(files.Select(file => file.StableKey));
            for (int i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ProjectCodeFile file = files[i];
                int fileProgress = i + 1;
                progress.SetCodeSubProgress(file.Path, fileProgress, files.Count);

                if (!TryTouchUnchangedProjectDocument(file, existing, generation, now))
                {
                    CodeIndexCandidate candidate = CreateProjectCandidate(file);
                    if (candidate != null) IndexCandidate(candidate, existing, generation, now);
                }

                if (fileProgress % PackageMaterializationBatchSize == 0) await Task.Yield();
            }
        }

        private static bool TryTouchUnchangedProjectDocument(
            ProjectCodeFile file,
            Dictionary<string, CodeDocument> existing,
            int generation,
            DateTime now)
        {
            if (file == null || existing == null) return false;
            if (!existing.TryGetValue(file.StableKey, out CodeDocument document)) return false;
            if (document.Status != CodeDocument.DocumentStatus.Ready) return false;
            if (!string.Equals(document.Path, file.Path, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(document.Extension, file.Extension, StringComparison.OrdinalIgnoreCase)) return false;
            if (!File.Exists(file.PhysicalPath)) return false;

            FileInfo fileInfo = new FileInfo(file.PhysicalPath);
            if (fileInfo.Length > GetMaxCodeFileBytes()) return false;
            if (document.Size != fileInfo.Length || document.LastWriteTicks != fileInfo.LastWriteTimeUtc.Ticks) return false;

            ApplyCandidateMetadata(document, new CodeIndexCandidate
            {
                StableKey = file.StableKey,
                SourceKind = CodeDocument.SourceKindType.Project,
                Path = file.Path,
                FileName = file.FileName,
                PhysicalPath = file.PhysicalPath,
                Guid = file.Guid,
                Extension = file.Extension,
                Language = file.Language,
                Size = fileInfo.Length,
                LastWriteTicks = fileInfo.LastWriteTimeUtc.Ticks,
                ContentHash = document.ContentHash
            }, generation, now);
            DB.Update(document);
            return true;
        }

        private static async Task<int> IndexPackageCandidates(
            CodeIndexer progress,
            HashSet<string> allowedExtensions,
            int generation,
            DateTime now,
            int mainProgress,
            int mainCount,
            CancellationToken cancellationToken)
        {
            if (allowedExtensions == null || allowedExtensions.Count == 0) return mainProgress;

            int lastAssetId = 0;
            while (true)
            {
                List<Asset> packages = LoadEligiblePackageCodeFilePackagePage(allowedExtensions, lastAssetId, PackageCodePackagePageSize);
                if (packages.Count == 0) break;

                foreach (Asset package in packages)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int fileCount = CountEligiblePackageCodeFiles(allowedExtensions, package.Id);
                    if (fileCount == 0) continue;

                    mainProgress++;
                    progress.SetCodeMainProgress(GetPackageProgressName(package), mainProgress, mainCount);
                    await IndexPackageCodeFiles(progress, allowedExtensions, package.Id, fileCount, generation, now, cancellationToken);
                    progress.ClearCodeSubProgress();
                }

                lastAssetId = packages[packages.Count - 1].Id;
                await Task.Yield();
            }
            return mainProgress;
        }

        private static async Task IndexPackageCodeFiles(
            CodeIndexer progress,
            HashSet<string> allowedExtensions,
            int assetId,
            int fileCount,
            int generation,
            DateTime now,
            CancellationToken cancellationToken)
        {
            int lastAssetFileId = 0;
            int fileProgress = 0;
            progress.SetCodeSubProgress("Preparing package code files", 0, fileCount);
            while (true)
            {
                List<AssetInfo> files = LoadEligiblePackageCodeFilePage(allowedExtensions, assetId, lastAssetFileId, PackageCodeFilePageSize);
                if (files.Count == 0) break;

                Dictionary<string, CodeDocument> existing = LoadExistingDocuments(files.Select(file => GetPackageStableKey(file.Id)));
                foreach (AssetInfo info in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    fileProgress++;
                    progress.SetCodeSubProgress(GetPackageFileProgressName(info), fileProgress, fileCount);

                    if (!TryTouchUnchangedPackageDocument(info, existing, generation, now))
                    {
                        CodeIndexCandidate candidate = await CreatePackageCandidate(info, cancellationToken);
                        if (candidate != null) IndexCandidate(candidate, existing, generation, now);
                    }

                    if (fileProgress % PackageMaterializationBatchSize == 0) await Task.Yield();
                }

                lastAssetFileId = files[files.Count - 1].Id;
                await Task.Yield();
            }
        }

        private static async Task<CodeIndexCandidate> CreatePackageCandidate(AssetInfo info, CancellationToken cancellationToken)
        {
            string extension = NormalizeExtension(info.Type);
            string physicalPath = ResolvePhysicalPath(info);
            if (physicalPath == null)
            {
                physicalPath = await Assets.EnsureMaterialized(info, true, cancellationToken);
            }
            if (physicalPath == null) return null;

            string displayPath = string.IsNullOrWhiteSpace(info.Path) ? physicalPath : info.Path;
            return CreateCandidate(
                GetPackageStableKey(info.Id),
                CodeDocument.SourceKindType.IndexedPackage,
                displayPath,
                physicalPath,
                info.Guid,
                info.AssetId,
                info.Id,
                info.GetDisplayName(),
                extension);
        }

        private static bool TryTouchUnchangedPackageDocument(
            AssetInfo info,
            Dictionary<string, CodeDocument> existing,
            int generation,
            DateTime now)
        {
            if (info == null || existing == null) return false;
            string stableKey = GetPackageStableKey(info.Id);
            if (!existing.TryGetValue(stableKey, out CodeDocument document)) return false;
            if (document.Status != CodeDocument.DocumentStatus.Ready) return false;
            if (document.SourceKind != CodeDocument.SourceKindType.IndexedPackage) return false;
            if (document.AssetFileId != info.Id) return false;

            string extension = NormalizeExtension(info.Type);
            string displayPath = NormalizePath(string.IsNullOrWhiteSpace(info.Path) ? document.Path : info.Path);
            if (string.IsNullOrWhiteSpace(displayPath)) return false;
            if (!string.Equals(document.Path, displayPath, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(document.Extension, extension, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(document.Guid ?? string.Empty, info.Guid ?? string.Empty, StringComparison.OrdinalIgnoreCase)) return false;

            string physicalPath = ResolvePhysicalPath(info);
            if (physicalPath != null)
            {
                FileInfo fileInfo = new FileInfo(physicalPath);
                if (fileInfo.Length > GetMaxCodeFileBytes()) return false;
                if (document.Size != fileInfo.Length || document.LastWriteTicks != fileInfo.LastWriteTimeUtc.Ticks) return false;

                ApplyCandidateMetadata(document, new CodeIndexCandidate
                {
                    StableKey = stableKey,
                    SourceKind = CodeDocument.SourceKindType.IndexedPackage,
                    Path = displayPath,
                    FileName = Path.GetFileName(displayPath),
                    PhysicalPath = physicalPath,
                    Guid = info.Guid,
                    AssetId = info.AssetId,
                    AssetFileId = info.Id,
                    PackageName = info.GetDisplayName(),
                    Extension = extension,
                    Language = GetLanguage(extension),
                    Size = fileInfo.Length,
                    LastWriteTicks = fileInfo.LastWriteTimeUtc.Ticks,
                    ContentHash = document.ContentHash
                }, generation, now);
                DB.Update(document);
                return true;
            }

            if (info.InProject || info.AssetSource == Asset.Source.Directory || info.AssetSource == Asset.Source.RegistryPackage) return false;
            if (info.Size <= 0 || info.Size > GetMaxCodeFileBytes()) return false;
            if (document.Size != info.Size) return false;

            ApplyCandidateMetadata(document, new CodeIndexCandidate
            {
                StableKey = stableKey,
                SourceKind = CodeDocument.SourceKindType.IndexedPackage,
                Path = displayPath,
                FileName = Path.GetFileName(displayPath),
                PhysicalPath = document.PhysicalPath,
                Guid = info.Guid,
                AssetId = info.AssetId,
                AssetFileId = info.Id,
                PackageName = info.GetDisplayName(),
                Extension = extension,
                Language = GetLanguage(extension),
                Size = document.Size,
                LastWriteTicks = document.LastWriteTicks,
                ContentHash = document.ContentHash
            }, generation, now);
            DB.Update(document);
            return true;
        }

        private static string GetPackageStableKey(int assetFileId)
        {
            return $"assetfile:{assetFileId}";
        }

        private static string GetPackageProgressName(Asset package)
        {
            if (package == null) return "Package code files";
            if (!string.IsNullOrWhiteSpace(package.DisplayName)) return package.DisplayName;
            if (!string.IsNullOrWhiteSpace(package.SafeName)) return package.SafeName;
            if (!string.IsNullOrWhiteSpace(package.Location)) return Path.GetFileName(package.GetLocation(true));
            return $"Package {package.Id}";
        }

        private static string GetPackageFileProgressName(AssetInfo info)
        {
            if (info == null) return "Package code file";
            if (!string.IsNullOrWhiteSpace(info.Path)) return info.Path;
            if (!string.IsNullOrWhiteSpace(info.FileName)) return info.FileName;
            return $"Package code file {info.Id}";
        }

        private static int CountEligiblePackageCodeFiles(HashSet<string> allowedExtensions)
        {
            if (allowedExtensions == null || allowedExtensions.Count == 0) return 0;

            List<string> placeholders = new List<string>();
            List<object> args = new List<object>();
            foreach (string extension in allowedExtensions)
            {
                placeholders.Add("?");
                args.Add(extension);
            }
            args.Add(Asset.Source.CurrentProject);

            string query =
                "select count(*) from AssetFile inner join Asset on Asset.Id = AssetFile.AssetId " +
                "left join Asset ParentAsset on ParentAsset.Id = Asset.ParentId " +
                "where Asset.Exclude=0 and Asset.NoIndex=0 and (ParentAsset.NoIndex is null or ParentAsset.NoIndex=0) " +
                "and (AssetFile.Hidden is null or AssetFile.Hidden <> 1) " +
                "and (Asset.UseCodeIndex is null or Asset.UseCodeIndex <> 0) " +
                $"and AssetFile.Type in ({string.Join(",", placeholders)}) and Asset.AssetSource <> ?";

            return DBAdapter.DB.ExecuteScalar<int>(query, args.ToArray());
        }

        internal static int CountEligiblePackageCodeFilePackages(HashSet<string> allowedExtensions)
        {
            if (allowedExtensions == null || allowedExtensions.Count == 0) return 0;

            List<string> placeholders = new List<string>();
            List<object> args = new List<object>();
            args.Add(Asset.Source.CurrentProject);
            foreach (string extension in allowedExtensions)
            {
                placeholders.Add("?");
                args.Add(extension);
            }

            string query =
                "select count(*) from Asset left join Asset ParentAsset on ParentAsset.Id = Asset.ParentId " +
                "where Asset.Exclude=0 and Asset.NoIndex=0 and (ParentAsset.NoIndex is null or ParentAsset.NoIndex=0) " +
                "and (Asset.UseCodeIndex is null or Asset.UseCodeIndex <> 0) " +
                "and Asset.AssetSource <> ? and exists (" +
                "select 1 from AssetFile where AssetFile.AssetId = Asset.Id " +
                "and (AssetFile.Hidden is null or AssetFile.Hidden <> 1) " +
                $"and AssetFile.Type in ({string.Join(",", placeholders)}))";

            return DBAdapter.DB.ExecuteScalar<int>(query, args.ToArray());
        }

        private static int CountEligiblePackageCodeFiles(HashSet<string> allowedExtensions, int assetId)
        {
            if (allowedExtensions == null || allowedExtensions.Count == 0 || assetId <= 0) return 0;

            List<string> placeholders = new List<string>();
            List<object> args = new List<object>();
            foreach (string extension in allowedExtensions)
            {
                placeholders.Add("?");
                args.Add(extension);
            }
            args.Add(assetId);

            string query =
                "select count(*) from AssetFile inner join Asset on Asset.Id = AssetFile.AssetId " +
                "left join Asset ParentAsset on ParentAsset.Id = Asset.ParentId " +
                "where Asset.Exclude=0 and Asset.NoIndex=0 and (ParentAsset.NoIndex is null or ParentAsset.NoIndex=0) " +
                "and (AssetFile.Hidden is null or AssetFile.Hidden <> 1) " +
                $"and AssetFile.Type in ({string.Join(",", placeholders)}) and AssetFile.AssetId = ?";

            return DBAdapter.DB.ExecuteScalar<int>(query, args.ToArray());
        }

        internal static List<Asset> LoadEligiblePackageCodeFilePackagePage(HashSet<string> allowedExtensions, int lastAssetId, int limit)
        {
            if (allowedExtensions == null || allowedExtensions.Count == 0 || limit <= 0) return new List<Asset>();

            List<string> placeholders = new List<string>();
            List<object> args = new List<object>();
            args.Add(Asset.Source.CurrentProject);
            args.Add(lastAssetId);
            foreach (string extension in allowedExtensions)
            {
                placeholders.Add("?");
                args.Add(extension);
            }
            args.Add(limit);

            string query =
                "select Asset.* from Asset left join Asset ParentAsset on ParentAsset.Id = Asset.ParentId " +
                "where Asset.Exclude=0 and Asset.NoIndex=0 and (ParentAsset.NoIndex is null or ParentAsset.NoIndex=0) " +
                "and (Asset.UseCodeIndex is null or Asset.UseCodeIndex <> 0) " +
                "and Asset.AssetSource <> ? and Asset.Id > ? and exists (" +
                "select 1 from AssetFile where AssetFile.AssetId = Asset.Id " +
                "and (AssetFile.Hidden is null or AssetFile.Hidden <> 1) " +
                $"and AssetFile.Type in ({string.Join(",", placeholders)})) " +
                "order by Asset.Id limit ?";

            return DBAdapter.DB.Query<Asset>(query, args.ToArray());
        }

        internal static List<AssetInfo> LoadEligiblePackageCodeFilePage(HashSet<string> allowedExtensions, int lastAssetFileId, int limit)
        {
            if (allowedExtensions == null || allowedExtensions.Count == 0 || limit <= 0) return new List<AssetInfo>();

            List<string> placeholders = new List<string>();
            List<object> args = new List<object>();
            foreach (string extension in allowedExtensions)
            {
                placeholders.Add("?");
                args.Add(extension);
            }
            args.Add(Asset.Source.CurrentProject);
            args.Add(lastAssetFileId);
            args.Add(limit);

            string query =
                "select Asset.*, AssetFile.*, AssetFile.Id as Id from AssetFile inner join Asset on Asset.Id = AssetFile.AssetId " +
                "left join Asset ParentAsset on ParentAsset.Id = Asset.ParentId " +
                "where Asset.Exclude=0 and Asset.NoIndex=0 and (ParentAsset.NoIndex is null or ParentAsset.NoIndex=0) " +
                "and (AssetFile.Hidden is null or AssetFile.Hidden <> 1) " +
                "and (Asset.UseCodeIndex is null or Asset.UseCodeIndex <> 0) " +
                $"and AssetFile.Type in ({string.Join(",", placeholders)}) and Asset.AssetSource <> ? " +
                "and AssetFile.Id > ? " +
                "order by AssetFile.Id limit ?";

            return DBAdapter.DB.Query<AssetInfo>(query, args.ToArray());
        }

        internal static List<AssetInfo> LoadEligiblePackageCodeFilePage(HashSet<string> allowedExtensions, int assetId, int lastAssetFileId, int limit)
        {
            if (allowedExtensions == null || allowedExtensions.Count == 0 || assetId <= 0 || limit <= 0) return new List<AssetInfo>();

            List<string> placeholders = new List<string>();
            List<object> args = new List<object>();
            foreach (string extension in allowedExtensions)
            {
                placeholders.Add("?");
                args.Add(extension);
            }
            args.Add(assetId);
            args.Add(lastAssetFileId);
            args.Add(limit);

            string query =
                "select Asset.*, AssetFile.*, AssetFile.Id as Id from AssetFile inner join Asset on Asset.Id = AssetFile.AssetId " +
                "left join Asset ParentAsset on ParentAsset.Id = Asset.ParentId " +
                "where Asset.Exclude=0 and Asset.NoIndex=0 and (ParentAsset.NoIndex is null or ParentAsset.NoIndex=0) " +
                "and (AssetFile.Hidden is null or AssetFile.Hidden <> 1) " +
                "and (Asset.UseCodeIndex is null or Asset.UseCodeIndex <> 0) " +
                $"and AssetFile.Type in ({string.Join(",", placeholders)}) and AssetFile.AssetId = ? " +
                "and AssetFile.Id > ? " +
                "order by AssetFile.Id limit ?";

            return DBAdapter.DB.Query<AssetInfo>(query, args.ToArray());
        }

        internal static Dictionary<string, CodeDocument> LoadExistingDocuments(IEnumerable<string> stableKeys)
        {
            EnsureInitialized();
            Dictionary<string, CodeDocument> result = new Dictionary<string, CodeDocument>(StringComparer.Ordinal);
            if (stableKeys == null) return result;

            List<string> keys = stableKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            for (int index = 0; index < keys.Count; index += StableKeyLookupBatchSize)
            {
                int count = Math.Min(StableKeyLookupBatchSize, keys.Count - index);
                List<string> batch = keys.GetRange(index, count);
                string placeholders = string.Join(",", batch.Select(_ => "?"));
                foreach (CodeDocument document in DB.Query<CodeDocument>(
                    $"select * from CodeDocument where StableKey in ({placeholders})",
                    batch.Cast<object>().ToArray()))
                {
                    result[document.StableKey] = document;
                }
            }
            return result;
        }

        private static List<string> NormalizeChangedPaths(IEnumerable<string> paths)
        {
            if (paths == null) return new List<string>();

            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                string normalized = NormalizePath(path);
                if (string.IsNullOrWhiteSpace(normalized)) continue;
                result.Add(normalized);
            }
            return result.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void MarkProjectDocumentDeleted(string path, DateTime now)
        {
            string normalizedPath = NormalizePath(path);
            if (string.IsNullOrWhiteSpace(normalizedPath)) return;

            DB.RunInTransaction(() =>
            {
                List<int> documentIds = DB.QueryScalars<int>(
                    "select Id from CodeDocument where SourceKind=? and Status<>? and Path=? COLLATE NOCASE",
                    CodeDocument.SourceKindType.Project,
                    CodeDocument.DocumentStatus.Deleted,
                    normalizedPath);
                foreach (int documentId in documentIds)
                {
                    DB.Execute(
                        "update CodeChunk set Status=?, UpdatedAt=? where DocumentId=?",
                        CodeChunk.ChunkStatus.Deleted,
                        now,
                        documentId);
                    DB.Execute(
                        "update CodeDocument set Status=?, UpdatedAt=?, ErrorMessage=null where Id=?",
                        CodeDocument.DocumentStatus.Deleted,
                        now,
                        documentId);
                }
            });
        }

        private static string ResolvePhysicalPath(AssetInfo info)
        {
            if (info == null) return null;
            if (info.InProject && File.Exists(info.ProjectPath)) return Path.GetFullPath(info.ProjectPath);
            if (info.AssetSource == Asset.Source.Directory || info.AssetSource == Asset.Source.RegistryPackage)
            {
                string path = info.GetSourcePath(true);
                return File.Exists(path) ? path : null;
            }
            return null;
        }

        private static CodeIndexCandidate CreateCandidate(
            string stableKey,
            CodeDocument.SourceKindType sourceKind,
            string displayPath,
            string physicalPath,
            string guid,
            int assetId,
            int assetFileId,
            string packageName,
            string extension)
        {
            if (string.IsNullOrWhiteSpace(physicalPath) || !File.Exists(physicalPath)) return null;

            FileInfo fileInfo = new FileInfo(physicalPath);
            if (fileInfo.Length > GetMaxCodeFileBytes()) return null;

            string content = ReadTextFile(physicalPath);
            if (string.IsNullOrWhiteSpace(content) || LooksBinary(content)) return null;

            return new CodeIndexCandidate
            {
                StableKey = stableKey,
                SourceKind = sourceKind,
                Path = NormalizePath(displayPath),
                PhysicalPath = physicalPath,
                Guid = guid,
                AssetId = assetId,
                AssetFileId = assetFileId,
                PackageName = packageName,
                Extension = extension,
                FileName = Path.GetFileName(displayPath),
                Language = GetLanguage(extension),
                Size = fileInfo.Length,
                LastWriteTicks = fileInfo.LastWriteTimeUtc.Ticks,
                Content = CodeSnippetBuilder.NormalizeText(content),
                ContentHash = SemanticVectorUtils.HashText(content)
            };
        }

        private static string ReadTextFile(string path)
        {
            try
            {
                using (StreamReader reader = new StreamReader(path, Encoding.UTF8, true))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Code search could not read '{path}': {e.Message}");
                return null;
            }
        }

        private static long GetMaxCodeFileBytes()
        {
            return Math.Max(1, AI.Config.codeIndexMaxFileSizeKb) * AssetInventorySettings.Size.KB;
        }

        private static bool LooksBinary(string text)
        {
            int limit = Math.Min(text.Length, 4096);
            for (int i = 0; i < limit; i++)
            {
                if (text[i] == '\0') return true;
            }
            return false;
        }

        private static List<CodeSearchRow> SearchFts(CodeSearchQuery query, CodeSearch.Options options, out int matchCount, out int documentCount)
        {
            matchCount = 0;
            documentCount = 0;
            string ftsQuery = query.BuildFtsQuery();
            if (string.IsNullOrWhiteSpace(ftsQuery)) return SearchFallback(query, options, out matchCount, out documentCount);

            List<object> args = new List<object> {ftsQuery};
            string filterSql = BuildSearchFilters(query, options, args, true);
            string sql =
                "select c.Id as ChunkId, c.DocumentId, c.StartLine, c.EndLine, c.Symbol, c.Content, " +
                "d.Path, d.FileName, d.PhysicalPath, d.PackageName, d.Language, d.Extension, d.SourceKind, " +
                "bm25(CodeChunkFts) as LexicalScore " +
                "from CodeChunkFts join CodeChunk c on c.Id = CodeChunkFts.rowid " +
                "join CodeDocument d on d.Id = c.DocumentId " +
                "where CodeChunkFts match ? and d.Status=? and c.Status=? " +
                filterSql +
                " order by LexicalScore limit ?";

            try
            {
                CountFtsMatches(query, options, ftsQuery, out matchCount, out documentCount);
                args.Add(GetCodeSearchCandidateLimit(options, 4));
                return DB.Query<CodeSearchRow>(sql, args.ToArray());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Code search FTS query failed, falling back to token search: {e.Message}");
                return null;
            }
        }

        private static List<CodeSearchRow> SearchFallback(CodeSearchQuery query, CodeSearch.Options options, out int matchCount, out int documentCount)
        {
            matchCount = 0;
            documentCount = 0;
            List<object> args = new List<object>();
            List<string> wheres = BuildFallbackSearchWheres(query, args);
            string filterSql = BuildSearchFilters(query, options, args, false);
            string sql =
                "select c.Id as ChunkId, c.DocumentId, c.StartLine, c.EndLine, c.Symbol, c.Content, " +
                "d.Path, d.FileName, d.PhysicalPath, d.PackageName, d.Language, d.Extension, d.SourceKind, " +
                "0 as LexicalScore " +
                "from CodeChunk c join CodeDocument d on d.Id = c.DocumentId " +
                $"where {string.Join(" and ", wheres)} " +
                filterSql +
                " order by d.Path, c.StartLine limit ?";

            CountFallbackMatches(query, options, out matchCount, out documentCount);
            args.Add(GetCodeSearchCandidateLimit(options, 8));
            return DB.Query<CodeSearchRow>(sql, args.ToArray());
        }

        private static List<string> BuildFallbackSearchWheres(CodeSearchQuery query, List<object> args)
        {
            List<string> wheres = new List<string>
            {
                "d.Status=?",
                "c.Status=?"
            };
            args.Add(CodeDocument.DocumentStatus.Ready);
            args.Add(CodeChunk.ChunkStatus.Ready);

            foreach (string term in query.SearchTerms)
            {
                wheres.Add("(c.Content like ? escape '\\' or c.Symbol like ? escape '\\' or d.Path like ? escape '\\' or d.FileName like ? escape '\\')");
                string like = "%" + EscapeLike(term) + "%";
                args.Add(like);
                args.Add(like);
                args.Add(like);
                args.Add(like);
            }

            return wheres;
        }

        private static void CountFtsMatches(CodeSearchQuery query, CodeSearch.Options options, string ftsQuery, out int matchCount, out int documentCount)
        {
            List<object> args = new List<object> {ftsQuery};
            string filterSql = BuildSearchFilters(query, options, args, true);
            string fromWhere =
                "from CodeChunkFts join CodeChunk c on c.Id = CodeChunkFts.rowid " +
                "join CodeDocument d on d.Id = c.DocumentId " +
                "where CodeChunkFts match ? and d.Status=? and c.Status=? " +
                filterSql;
            object[] queryArgs = args.ToArray();
            matchCount = DB.ExecuteScalar<int>("select count(*) " + fromWhere, queryArgs);
            documentCount = DB.ExecuteScalar<int>("select count(distinct c.DocumentId) " + fromWhere, queryArgs);
        }

        private static void CountFallbackMatches(CodeSearchQuery query, CodeSearch.Options options, out int matchCount, out int documentCount)
        {
            List<object> args = new List<object>();
            List<string> wheres = BuildFallbackSearchWheres(query, args);
            string filterSql = BuildSearchFilters(query, options, args, false);
            string fromWhere =
                "from CodeChunk c join CodeDocument d on d.Id = c.DocumentId " +
                $"where {string.Join(" and ", wheres)} " +
                filterSql;
            object[] queryArgs = args.ToArray();
            matchCount = DB.ExecuteScalar<int>("select count(*) " + fromWhere, queryArgs);
            documentCount = DB.ExecuteScalar<int>("select count(distinct c.DocumentId) " + fromWhere, queryArgs);
        }

        private static int GetCodeSearchFileSkip(CodeSearch.Options options)
        {
            int currentPage = Math.Max(1, options.CurrentPage);
            return (currentPage - 1) * Mathf.Max(1, options.MaxFiles);
        }

        private static int GetCodeSearchCandidateLimit(CodeSearch.Options options, int multiplier)
        {
            int requestedFiles = Math.Max(1, options.CurrentPage) * Mathf.Max(1, options.MaxFiles);
            long candidateLimit = (long)requestedFiles * Mathf.Max(1, options.MaxMatchesPerFile) * Math.Max(1, multiplier);
            candidateLimit = Math.Max(50L, candidateLimit);
            return candidateLimit > int.MaxValue ? int.MaxValue : (int)candidateLimit;
        }

        private static string BuildSearchFilters(CodeSearchQuery query, CodeSearch.Options options, List<object> args, bool statusAlreadyAdded)
        {
            StringBuilder sb = new StringBuilder();
            if (statusAlreadyAdded)
            {
                args.Add(CodeDocument.DocumentStatus.Ready);
                args.Add(CodeChunk.ChunkStatus.Ready);
            }

            SearchScope searchScope = SearchScopeModel.Normalize(options.Scope, true);
            bool includeProject = SearchScopeModel.UsesCodeProjectSearch(searchScope);
            bool includeIndex = SearchScopeModel.UsesCodeIndexSearch(searchScope);
            if (includeProject != includeIndex)
            {
                sb.Append(" and d.SourceKind=?");
                args.Add(includeProject ? CodeDocument.SourceKindType.Project : CodeDocument.SourceKindType.IndexedPackage);
            }
            if (!string.IsNullOrWhiteSpace(query.ExtensionFilter))
            {
                sb.Append(" and d.Extension=?");
                args.Add(NormalizeExtension(query.ExtensionFilter));
            }
            if (!string.IsNullOrWhiteSpace(query.PathFilter))
            {
                sb.Append(" and d.Path like ? escape '\\'");
                args.Add("%" + EscapeLike(query.PathFilter) + "%");
            }
            if (!string.IsNullOrWhiteSpace(query.SymbolFilter))
            {
                sb.Append(" and c.Symbol like ? escape '\\'");
                args.Add("%" + EscapeLike(query.SymbolFilter) + "%");
            }
            if (!string.IsNullOrWhiteSpace(query.PackageFilter))
            {
                sb.Append(" and d.PackageName like ? escape '\\'");
                args.Add("%" + EscapeLike(query.PackageFilter) + "%");
            }
            if (query.ProjectOnly.HasValue)
            {
                sb.Append(" and d.SourceKind=?");
                args.Add(query.ProjectOnly.Value ? CodeDocument.SourceKindType.Project : CodeDocument.SourceKindType.IndexedPackage);
            }
            return sb.ToString();
        }

        private static float CalculateScore(CodeSearchRow row, CodeSearchQuery query, bool ftsUsed)
        {
            float score = ftsUsed ? 1f / (1f + Math.Abs(row.LexicalScore)) : 0.35f;
            foreach (string term in query.SearchTerms)
            {
                if (string.IsNullOrWhiteSpace(term)) continue;
                if (!string.IsNullOrEmpty(row.FileName) && row.FileName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) score += 0.4f;
                if (!string.IsNullOrEmpty(row.Path) && row.Path.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) score += 0.2f;
                if (!string.IsNullOrEmpty(row.Symbol) && row.Symbol.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) score += 0.35f;
                if (!string.IsNullOrEmpty(row.Content) && row.Content.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) score += 0.1f;
            }
            if (!string.IsNullOrWhiteSpace(query.SymbolFilter) && !string.IsNullOrWhiteSpace(row.Symbol)) score += 0.25f;
            return score;
        }

        private static void MarkUnseenDeleted(int generation, DateTime now)
        {
            DB.RunInTransaction(() =>
            {
                DB.Execute(
                    "update CodeChunk set Status=? where DocumentId in (select Id from CodeDocument where (LastSeenGeneration is null or LastSeenGeneration <> ?) and Status=?)",
                    CodeChunk.ChunkStatus.Deleted,
                    generation,
                    CodeDocument.DocumentStatus.Ready);
                DB.Execute(
                    "update CodeDocument set Status=?, UpdatedAt=? where (LastSeenGeneration is null or LastSeenGeneration <> ?) and Status=?",
                    CodeDocument.DocumentStatus.Deleted,
                    now,
                    generation,
                    CodeDocument.DocumentStatus.Ready);
            });
        }

        private static int NextGeneration()
        {
            int generation = GetIntProperty(GenerationProperty) + 1;
            SetProperty(GenerationProperty, generation.ToString());
            return generation;
        }

        private static int CountDocuments(CodeDocument.DocumentStatus status)
        {
            return DB.ExecuteScalar<int>("select count(*) from CodeDocument where Status=?", status);
        }

        private static int CountDocuments(CodeDocument.DocumentStatus status, SearchScope searchScope)
        {
            bool includeProject = SearchScopeModel.UsesCodeProjectSearch(searchScope);
            bool includeIndex = SearchScopeModel.UsesCodeIndexSearch(searchScope);
            if (includeProject == includeIndex) return CountDocuments(status);

            CodeDocument.SourceKindType sourceKind = includeProject
                ? CodeDocument.SourceKindType.Project
                : CodeDocument.SourceKindType.IndexedPackage;
            return DB.ExecuteScalar<int>("select count(*) from CodeDocument where Status=? and SourceKind=?", status, sourceKind);
        }

        private static int CountChunks(CodeChunk.ChunkStatus status)
        {
            return DB.ExecuteScalar<int>("select count(*) from CodeChunk where Status=?", status);
        }

        private static int CountOrphanedChunks()
        {
            HashSet<int> documentIds = new HashSet<int>(DB.QueryScalars<int>("select Id from CodeDocument"));
            return DB.Table<CodeChunk>().ToList().Count(c => !documentIds.Contains(c.DocumentId));
        }

        private static DateTime GetLastUpdatedAt()
        {
            string ticks = GetProperty("LastUpdatedAt");
            return long.TryParse(ticks, out long result) ? new DateTime(result, DateTimeKind.Utc) : DateTime.MinValue;
        }

        private static string GetProperty(string name)
        {
            CodeIndexProperty property = DB.Find<CodeIndexProperty>(name);
            return property?.Value;
        }

        private static int GetIntProperty(string name)
        {
            return int.TryParse(GetProperty(name), out int result) ? result : 0;
        }

        private static void SetProperty(string name, string value)
        {
            DB.InsertOrReplace(new CodeIndexProperty(name, value));
        }

        private static string[] GetDatabaseFiles()
        {
            return new[]
            {
                DatabasePath,
                DatabasePath + "-wal",
                DatabasePath + "-shm"
            };
        }

        private static HashSet<string> GetAllowedExtensions()
        {
            string[] extensions = AI.ResolveExtensionList(AI.Config.codeIndexExtensions);
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string extension in extensions)
            {
                string normalized = NormalizeExtension(extension);
                if (!string.IsNullOrWhiteSpace(normalized)) result.Add(normalized);
            }
            return result;
        }

        private static string NormalizeExtension(string extension)
        {
            return string.IsNullOrWhiteSpace(extension) ? string.Empty : extension.Trim().TrimStart('.').ToLowerInvariant();
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/');
        }

        private static string EscapeLike(string value)
        {
            return value?.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") ?? string.Empty;
        }

        private static string GetLanguage(string extension)
        {
            switch (NormalizeExtension(extension))
            {
                case "cs":
                    return "C#";
                case "shader":
                case "shadergraph":
                case "shadersubgraph":
                case "compute":
                case "raytrace":
                case "cginc":
                case "hlsl":
                case "hlslinc":
                case "shaderinclude":
                    return "Shader";
                case "uxml":
                case "xml":
                    return "XML";
                case "uss":
                case "css":
                    return "Style";
                case "json":
                    return "JSON";
                case "md":
                    return "Markdown";
                default:
                    return NormalizeExtension(extension).ToUpperInvariant();
            }
        }

        private sealed class ProjectCodeFile
        {
            public string StableKey;
            public string Path;
            public string FileName;
            public string PhysicalPath;
            public string Guid;
            public string Extension;
            public string Language;
        }

        private sealed class CodeIndexCandidate
        {
            public string StableKey;
            public CodeDocument.SourceKindType SourceKind;
            public string Path;
            public string FileName;
            public string PhysicalPath;
            public string Guid;
            public int AssetId;
            public int AssetFileId;
            public string PackageName;
            public string Extension;
            public string Language;
            public long Size;
            public long LastWriteTicks;
            public string Content;
            public string ContentHash;
        }

        private sealed class CodeSearchRow
        {
            public int ChunkId { get; set; }
            public int DocumentId { get; set; }
            public int StartLine { get; set; }
            public int EndLine { get; set; }
            public string Symbol { get; set; }
            public string Content { get; set; }
            public string Path { get; set; }
            public string FileName { get; set; }
            public string PhysicalPath { get; set; }
            public string PackageName { get; set; }
            public string Language { get; set; }
            public string Extension { get; set; }
            public CodeDocument.SourceKindType SourceKind { get; set; }
            public float LexicalScore { get; set; }
        }

        private readonly struct ScoredCodeSearchRow
        {
            public readonly CodeSearchRow Row;
            public readonly float Score;

            public ScoredCodeSearchRow(CodeSearchRow row, float score)
            {
                Row = row;
                Score = score;
            }
        }

        private sealed class CodeSearchQuery
        {
            public readonly List<string> SearchTerms = new List<string>();
            public readonly List<string> Phrases = new List<string>();
            public string PathFilter;
            public string ExtensionFilter;
            public string SymbolFilter;
            public string PackageFilter;
            public bool? ProjectOnly;

            public bool HasSearchText => SearchTerms.Count > 0 || Phrases.Count > 0;
            public bool HasFilters => !string.IsNullOrWhiteSpace(PathFilter)
                || !string.IsNullOrWhiteSpace(ExtensionFilter)
                || !string.IsNullOrWhiteSpace(SymbolFilter)
                || !string.IsNullOrWhiteSpace(PackageFilter)
                || ProjectOnly.HasValue;
            public IReadOnlyCollection<string> HighlightTerms => SearchTerms.Concat(Phrases).ToList();

            public static CodeSearchQuery Parse(string rawQuery)
            {
                CodeSearchQuery query = new CodeSearchQuery();
                foreach (QueryToken token in Tokenize(rawQuery))
                {
                    string value = token.Value;
                    int separator = value.IndexOf(':');
                    if (!token.Quoted && separator > 0)
                    {
                        string key = value.Substring(0, separator).ToLowerInvariant();
                        string filterValue = value.Substring(separator + 1);
                        if (ApplyFilter(query, key, filterValue)) continue;
                    }

                    if (token.Quoted && value.Contains(" "))
                    {
                        query.Phrases.Add(value.Trim());
                    }
                    else if (!string.IsNullOrWhiteSpace(value))
                    {
                        query.SearchTerms.Add(value.Trim());
                    }
                }
                return query;
            }

            public string BuildFtsQuery()
            {
                List<string> parts = new List<string>();
                foreach (string phrase in Phrases)
                {
                    string escaped = EscapeFtsPhrase(phrase);
                    if (!string.IsNullOrWhiteSpace(escaped)) parts.Add($"\"{escaped}\"");
                }
                foreach (string term in SearchTerms)
                {
                    string token = EscapeFtsToken(term);
                    if (!string.IsNullOrWhiteSpace(token)) parts.Add(token.Length > 1 ? token + "*" : token);
                }
                return string.Join(" AND ", parts);
            }

            private static bool ApplyFilter(CodeSearchQuery query, string key, string value)
            {
                switch (key)
                {
                    case "path":
                        query.PathFilter = value;
                        return true;
                    case "ext":
                    case "extension":
                        query.ExtensionFilter = value;
                        return true;
                    case "symbol":
                        query.SymbolFilter = value;
                        return true;
                    case "package":
                    case "pkg":
                        query.PackageFilter = value;
                        return true;
                    case "source":
                        if (string.Equals(value, "project", StringComparison.OrdinalIgnoreCase)) query.ProjectOnly = true;
                        else if (string.Equals(value, "package", StringComparison.OrdinalIgnoreCase)) query.ProjectOnly = false;
                        return true;
                    case "project":
                        if (bool.TryParse(value, out bool projectOnly)) query.ProjectOnly = projectOnly;
                        return true;
                    default:
                        return false;
                }
            }

            private static List<QueryToken> Tokenize(string rawQuery)
            {
                List<QueryToken> result = new List<QueryToken>();
                if (string.IsNullOrWhiteSpace(rawQuery)) return result;

                StringBuilder current = new StringBuilder();
                bool quoted = false;
                bool currentQuoted = false;
                for (int i = 0; i < rawQuery.Length; i++)
                {
                    char c = rawQuery[i];
                    if (c == '"')
                    {
                        quoted = !quoted;
                        currentQuoted = true;
                        continue;
                    }
                    if (!quoted && char.IsWhiteSpace(c))
                    {
                        FlushToken(result, current, currentQuoted);
                        currentQuoted = false;
                        continue;
                    }
                    current.Append(c);
                }
                FlushToken(result, current, currentQuoted);
                return result;
            }

            private static void FlushToken(List<QueryToken> result, StringBuilder current, bool quoted)
            {
                if (current.Length == 0) return;
                result.Add(new QueryToken(current.ToString(), quoted));
                current.Length = 0;
            }

            private static string EscapeFtsToken(string value)
            {
                if (string.IsNullOrWhiteSpace(value)) return string.Empty;
                StringBuilder sb = new StringBuilder(value.Length);
                foreach (char c in value)
                {
                    if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
                }
                return sb.ToString();
            }

            private static string EscapeFtsPhrase(string value)
            {
                return value?.Replace("\"", "\"\"").Trim() ?? string.Empty;
            }

            private readonly struct QueryToken
            {
                public readonly string Value;
                public readonly bool Quoted;

                public QueryToken(string value, bool quoted)
                {
                    Value = value;
                    Quoted = quoted;
                }
            }
        }
    }
}
