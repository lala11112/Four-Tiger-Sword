using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using Brain;
using ImpossibleRobert.Common;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace AssetInventory
{
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    internal static partial class SemanticAssetSearch
    {
        private const float FullPhraseFileNameBoost = 0.08f;
        private const float FullPhrasePathBoost = 0.06f;
        private const float FullPhraseCaptionBoost = 0.05f;
        private const float FullPhrasePackageBoost = 0.02f;
        private const float DirectTokenBoost = 0.025f;
        private const float CaptionTokenBoost = 0.02f;
        private const float AllQueryTermsBoost = 0.055f;
        private const float AllQueryTermsInFileBoost = 0.075f;
        private const float PackageOnlyMatchPenalty = 0.035f;
        private const float PackageRepeatPenalty = 0.012f;
        private const float FolderRepeatPenalty = 0.006f;
        private const float MaxDiversityPenalty = 0.08f;
        private const float DynamicScoreWindow = 0.06f;
        private const float DynamicScoreRatio = 0.88f;
        private const float DynamicMinimumKeepWindow = 0.10f;
        private const int MinimumDynamicResultCount = 8;

        private static readonly Regex InlineFilterTokenPattern = new Regex(@"\b(?:withallpt|withanypt|withnonept|withnopt|withallft|withanyft|withnoneft|withnoft|pt|ft):(?:'[^']*'|""[^""]*""|\S+)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MultipleWhitespacePattern = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex WordPattern = new Regex(@"[a-z0-9]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly HashSet<string> StopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "the", "of", "for", "to", "with", "and", "or", "in", "on", "at", "by", "from", "is", "are"
        };
        private static readonly HashSet<string> AudioIntentTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "audio", "sound", "sounds", "sfx", "music", "song", "songs", "soundtrack", "ambience", "ambiance", "loop", "loops", "foley"
        };
        private static readonly HashSet<string> VisualIntentTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "icon", "icons", "sprite", "sprites", "image", "images", "texture", "textures", "model", "models", "prefab", "prefabs",
            "material", "materials", "animation", "animations", "anim", "vfx", "particle", "particles", "mesh", "scene", "scenes",
            "character", "characters", "portrait", "background", "environment"
        };
        private static readonly HashSet<string> CodeIntentTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "code", "script", "scripts", "class", "function", "method", "shader", "shaders", "csharp", "cs"
        };
        private static readonly HashSet<string> AudioTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "wav", "mp3", "ogg", "aif", "aiff", "flac", "m4a"
        };
        private static readonly HashSet<string> VisualTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "png", "jpg", "jpeg", "tga", "tif", "tiff", "bmp", "psd", "exr", "prefab", "fbx", "obj", "dae", "blend", "3ds",
            "mat", "anim", "controller", "vfx", "shader", "shadergraph", "unity"
        };
        private static readonly HashSet<string> CodeAndDocumentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cs", "js", "boo", "asmdef", "asmref", "txt", "md", "json", "xml", "yaml", "yml", "html", "css", "uss", "uxml", "dll", "pdb"
        };

        public static bool TryExecute(AssetSearch.Options opt, out AssetSearch.Result result)
        {
            result = null;

            if (!AI.Actions.SemanticSearchEnabled) return false;
            if (!AI.Config.enableSemanticSearch) return false;
            if (AI.Config.semanticSearchMode <= 0) return false;
            if (opt.InMemory != AssetSearch.InMemoryMode.None) return false;
            if (string.IsNullOrWhiteSpace(opt.SearchPhrase)) return false;
            string trimmedSearchPhrase = opt.SearchPhrase.TrimStart();
            if (trimmedSearchPhrase.StartsWith("=", StringComparison.Ordinal) || trimmedSearchPhrase.StartsWith("~", StringComparison.Ordinal)) return false;

            bool profile = AssetSearch.Diagnostics.IsEnabled;
            Stopwatch stopwatch = profile ? Stopwatch.StartNew() : null;
            long lastProfileMs = 0;
            List<string> profileSteps = profile ? new List<string>() : null;
            string semanticQuery = ResolveSemanticQueryText(opt);
            if (string.IsNullOrWhiteSpace(semanticQuery)) return false;
            RecordSemanticProfileStep(profileSteps, stopwatch, ref lastProfileMs, "ResolveQuery", semanticQuery);

            try
            {
                if (!SemanticIndexService.Exists())
                {
                    result = CreateFailureResult(opt, "Semantic search is enabled, but the semantic index has not been created yet. Run the Update Semantic Index action to build it.");
                    RecordSemanticProfileStep(profileSteps, stopwatch, ref lastProfileMs, "Preflight", "sidecar missing");
                    WriteSemanticSearchProfile(opt, semanticQuery, result, profileSteps, stopwatch);
                    return true;
                }
                if (!SemanticIndexService.HasSearchableAssetProfile())
                {
                    result = CreateFailureResult(opt, "Semantic search is enabled, but the semantic index is not ready for the selected AI backend and embedding model. Run the Update Semantic Index action to update it.");
                    RecordSemanticProfileStep(profileSteps, stopwatch, ref lastProfileMs, "Preflight", "profile missing");
                    WriteSemanticSearchProfile(opt, semanticQuery, result, profileSteps, stopwatch);
                    return true;
                }
                RecordSemanticProfileStep(profileSteps, stopwatch, ref lastProfileMs, "Preflight");

                HashSet<int> eligibleIdSet = null;
                AssetSearch.Options filterOptions = CloneWithoutTextSearch(opt);
                if (NeedsMainDatabaseFilter(filterOptions))
                {
                    AssetSearch.QueryResult qr = AssetSearch.BuildQuery(filterOptions);
                    if (qr.Error != null)
                    {
                        result = new AssetSearch.Result {Error = qr.Error, Files = new List<AssetInfo>(), InMemory = opt.InMemory};
                        RecordSemanticProfileStep(profileSteps, stopwatch, ref lastProfileMs, "BuildFilter", qr.Error);
                        WriteSemanticSearchProfile(opt, semanticQuery, result, profileSteps, stopwatch);
                        return true;
                    }

                    List<int> eligibleIds = DBAdapter.DB.QueryScalars<int>($"select distinct AssetFile.Id {qr.BaseQuery}", qr.Args.ToArray());
                    eligibleIdSet = new HashSet<int>(eligibleIds);
                    RecordSemanticProfileStep(profileSteps, stopwatch, ref lastProfileMs, "FilterSql", $"eligible={eligibleIdSet.Count}");
                    if (eligibleIdSet.Count == 0)
                    {
                        result = new AssetSearch.Result {Files = new List<AssetInfo>(), InMemory = opt.InMemory};
                        WriteSemanticSearchProfile(opt, semanticQuery, result, profileSteps, stopwatch);
                        return true;
                    }
                }
                else
                {
                    RecordSemanticProfileStep(profileSteps, stopwatch, ref lastProfileMs, "FilterSql", "skipped");
                }

                int requestedResultCount = opt.MaxResults > 0 ? opt.MaxResults * Math.Max(1, opt.CurrentPage) : Math.Max(1, AI.Config.semanticResultLimit);
                int semanticLimit = CalculateSemanticCandidateLimit(opt, requestedResultCount);

                List<SemanticIndexService.SemanticSearchMatch> matches = SemanticIndexService.SearchAssets(
                    semanticQuery,
                    eligibleIdSet,
                    semanticLimit,
                    CancellationToken.None);
                RecordSemanticProfileStep(profileSteps, stopwatch, ref lastProfileMs, "VectorSearch", $"matches={matches.Count}, limit={semanticLimit}");
                if (matches.Count == 0 && AI.Config.semanticSearchMode == 2)
                {
                    WriteSemanticSearchProfile(opt, semanticQuery, new AssetSearch.Result {Files = new List<AssetInfo>(), InMemory = opt.InMemory}, profileSteps, stopwatch);
                    return false;
                }

                List<SemanticRankedFile> ranked = Hydrate(matches, semanticQuery, requestedResultCount);
                RecordSemanticProfileStep(profileSteps, stopwatch, ref lastProfileMs, "Hydrate", $"rows={ranked.Count}");
                int total = ranked.Count;

                if (opt.MaxResults > 0)
                {
                    int offset = Math.Max(0, (opt.CurrentPage - 1) * opt.MaxResults);
                    ranked = ranked.Skip(offset).Take(opt.MaxResults).ToList();
                }

                result = new AssetSearch.Result
                {
                    Files = ranked.Select(r => r.File).ToList(),
                    ResultCount = total,
                    OriginalResultCount = total,
                    InMemory = opt.InMemory
                };
                Assets.ResolveParents(result.Files, opt.AllAssets);
                RecordSemanticProfileStep(profileSteps, stopwatch, ref lastProfileMs, "ResolveParents", $"rows={result.Files.Count}, allAssets={opt.AllAssets?.Count ?? 0}");
                WriteSemanticSearchProfile(opt, semanticQuery, result, profileSteps, stopwatch);
                return true;
            }
            catch (Exception e)
            {
                string failure = GetSemanticFailureMessage(e);
                Debug.LogWarning($"[Asset Inventory] Semantic search failed for '{semanticQuery}': {failure}");
                result = CreateFailureResult(opt, failure);
                RecordSemanticProfileStep(profileSteps, stopwatch, ref lastProfileMs, "Exception", e.GetType().Name);
                WriteSemanticSearchProfile(opt, semanticQuery, result, profileSteps, stopwatch);
                return true;
            }
        }

        private static void RecordSemanticProfileStep(List<string> profileSteps, Stopwatch stopwatch, ref long lastProfileMs, string label, string details = null)
        {
            if (profileSteps == null || stopwatch == null) return;

            long elapsedMs = stopwatch.ElapsedMilliseconds;
            long stepMs = elapsedMs - lastProfileMs;
            lastProfileMs = elapsedMs;

            profileSteps.Add(string.IsNullOrEmpty(details)
                ? $"{label}: {stepMs} ms"
                : $"{label}: {stepMs} ms ({details})");
        }

        private static void WriteSemanticSearchProfile(AssetSearch.Options opt, string semanticQuery, AssetSearch.Result result, List<string> profileSteps, Stopwatch stopwatch)
        {
            if (profileSteps == null || stopwatch == null) return;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("[Asset Inventory] Semantic search profile");
            sb.Append($", phrase='{TruncateProfileText(opt.SearchPhrase, 120)}'");
            sb.Append($", semantic='{TruncateProfileText(semanticQuery, 120)}'");
            sb.Append($", page={opt.CurrentPage}");
            sb.Append($", maxResults={opt.MaxResults}");
            sb.Append($", resultCount={result.ResultCount}");
            sb.Append($", returned={result.Files?.Count ?? 0}");
            if (!string.IsNullOrEmpty(result.Error)) sb.Append($", error='{TruncateProfileText(result.Error, 200)}'");
            sb.Append($", total={stopwatch.ElapsedMilliseconds} ms");

            foreach (string step in profileSteps)
            {
                sb.AppendLine();
                sb.Append("  - ");
                sb.Append(step);
            }

            Debug.Log(sb.ToString());
        }

        private static string TruncateProfileText(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }

        private static AssetSearch.Options CloneWithoutTextSearch(AssetSearch.Options opt)
        {
            return new AssetSearch.Options
            {
                SearchPhrase = ExtractFilterSearchPhrase(opt.SearchPhrase),
                SearchVariables = opt.SearchVariables,
                SelectedPackageSRPs = opt.SelectedPackageSRPs,
                SelectedPriceOption = opt.SelectedPriceOption,
                SearchPrice = opt.SearchPrice,
                SearchWidth = opt.SearchWidth,
                CheckMaxWidth = opt.CheckMaxWidth,
                SearchHeight = opt.SearchHeight,
                CheckMaxHeight = opt.CheckMaxHeight,
                SearchLength = opt.SearchLength,
                CheckMaxLength = opt.CheckMaxLength,
                SearchSize = opt.SearchSize,
                CheckMaxSize = opt.CheckMaxSize,
                SearchVertexCount = opt.SearchVertexCount,
                CheckMaxVertexCount = opt.CheckMaxVertexCount,
                SelectedPackageTag = opt.SelectedPackageTag,
                SelectedFileTag = opt.SelectedFileTag,
                SelectedPackageTypes = opt.SelectedPackageTypes,
                SelectedPublisher = opt.SelectedPublisher,
                SelectedAsset = opt.SelectedAsset,
                SelectedAssetId = opt.SelectedAssetId,
                SelectedAssetFileId = opt.SelectedAssetFileId,
                SelectedCategory = opt.SelectedCategory,
                IncludeCategorySubcategories = opt.IncludeCategorySubcategories,
                SelectedColorOption = opt.SelectedColorOption,
                SelectedColor = opt.SelectedColor,
                SelectedImageType = opt.SelectedImageType,
                SelectedPreviewFilter = opt.SelectedPreviewFilter,
                SelectedHiddenFilter = opt.SelectedHiddenFilter,
                RawSearchType = opt.RawSearchType,
                IgnoreExcludedExtensions = opt.IgnoreExcludedExtensions,
                CurrentPage = 1,
                MaxResults = 0,
                InMemory = opt.InMemory,
                AllAssets = opt.AllAssets,
                TagNames = opt.TagNames,
                Tags = opt.Tags,
                AssetNames = opt.AssetNames,
                PublisherNames = opt.PublisherNames,
                CategoryNames = opt.CategoryNames,
                ImageTypeOptions = opt.ImageTypeOptions
            };
        }

        internal static string ResolveSemanticQueryText(AssetSearch.Options opt)
        {
            string phrase = opt.SearchPhrase ?? string.Empty;
            if (opt.SearchVariables != null && opt.SearchVariables.Count > 0)
            {
                try
                {
                    phrase = VariableResolver.ReplaceVariables(phrase, opt.SearchVariables);
                }
                catch
                {
                    return string.Empty;
                }
            }

            phrase = InlineFilterTokenPattern.Replace(phrase, string.Empty);
            phrase = MultipleWhitespacePattern.Replace(phrase, " ").Trim();
            if (phrase.StartsWith("~", StringComparison.Ordinal)) return phrase.Substring(1).Trim();

            List<string> semanticTokens = new List<string>();
            foreach (string token in SplitSearchTokens(phrase))
            {
                if (token.StartsWith("-", StringComparison.Ordinal) && token.Length > 1) continue;
                semanticTokens.Add(token.StartsWith("+", StringComparison.Ordinal) && token.Length > 1 ? token.Substring(1) : token);
            }
            return string.Join(" ", semanticTokens).Trim();
        }

        internal static string ExtractFilterSearchPhrase(string phrase)
        {
            if (string.IsNullOrWhiteSpace(phrase)) return string.Empty;

            List<string> tokens = new List<string>();
            MatchCollection matches = InlineFilterTokenPattern.Matches(phrase);
            foreach (Match match in matches)
            {
                if (match.Success && !string.IsNullOrWhiteSpace(match.Value)) tokens.Add(match.Value);
            }

            string textWithoutInlineFilters = InlineFilterTokenPattern.Replace(phrase, string.Empty);
            textWithoutInlineFilters = MultipleWhitespacePattern.Replace(textWithoutInlineFilters, " ").Trim();
            if (textWithoutInlineFilters.StartsWith("~", StringComparison.Ordinal))
            {
                tokens.Add(textWithoutInlineFilters);
            }
            else
            {
                foreach (string token in SplitSearchTokens(textWithoutInlineFilters))
                {
                    if ((token.StartsWith("+", StringComparison.Ordinal) || token.StartsWith("-", StringComparison.Ordinal)) && token.Length > 1) tokens.Add(token);
                }
            }

            return string.Join(" ", tokens);
        }

        private static IEnumerable<string> SplitSearchTokens(string phrase)
        {
            if (string.IsNullOrWhiteSpace(phrase)) yield break;

            string[] tokens = phrase
                .Split(' ')
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToArray();
            foreach (string token in tokens) yield return token;
        }

        private static bool NeedsMainDatabaseFilter(AssetSearch.Options opt)
        {
            return !string.IsNullOrWhiteSpace(opt.SearchPhrase)
                || opt.SelectedPackageSRPs > 1
                || opt.SelectedPriceOption > 0
                || opt.SearchPrice > 0f
                || !string.IsNullOrWhiteSpace(opt.SearchWidth)
                || !string.IsNullOrWhiteSpace(opt.SearchHeight)
                || !string.IsNullOrWhiteSpace(opt.SearchLength)
                || !string.IsNullOrWhiteSpace(opt.SearchSize)
                || !string.IsNullOrWhiteSpace(opt.SearchVertexCount)
                || opt.SelectedPackageTag > 0
                || opt.SelectedFileTag > 0
                || opt.SelectedPackageTypes > 1
                || opt.SelectedPublisher > 0
                || opt.SelectedAsset > 0
                || opt.SelectedAssetId > 0
                || opt.SelectedAssetFileId > 0
                || opt.SelectedCategory > 0
                || opt.SelectedColorOption > 0
                || opt.SelectedImageType > 0
                || opt.SelectedPreviewFilter > 0
                || opt.SelectedHiddenFilter > 0
                || opt.RawSearchType != null
                || AssetSearch.GetConfiguredExcludedExtensions(opt.IgnoreExcludedExtensions).Length > 0;
        }

        private static int CalculateSemanticCandidateLimit(AssetSearch.Options opt, int requestedResultCount)
        {
            int configuredResultLimit = Math.Max(1, AI.Config.semanticResultLimit);
            int candidateLimit = Math.Max(1, AI.Config.semanticCandidateLimit);
            int requested = Math.Max(1, requestedResultCount);

            if (opt.MaxResults <= 0) return Math.Min(configuredResultLimit, candidateLimit);

            int buffered = requested + Math.Max(1, opt.MaxResults) * 4;
            int preferred = Math.Min(configuredResultLimit, buffered);
            return Math.Min(Math.Max(requested, preferred), candidateLimit);
        }

        private static string GetSemanticFailureMessage(Exception e)
        {
            if (e is OperationCanceledException)
            {
                return $"Semantic search timed out after {SemanticIndexService.GetInteractiveSearchTimeoutSeconds()} seconds while contacting {GetEmbeddingBackendDescription()}. Check that the selected AI backend and embedding model are available.";
            }

            HttpRequestException httpRequestException = FindException<HttpRequestException>(e);
            if (httpRequestException != null)
            {
                string details = GetMostSpecificExceptionMessage(httpRequestException);
                return $"Semantic search could not reach {GetEmbeddingBackendDescription()}. Start the selected AI backend, verify the service URL and embedding model, then try again.{FormatExceptionDetails(details)}";
            }

            return $"Semantic search failed while contacting {GetEmbeddingBackendDescription()}: {GetMostSpecificExceptionMessage(e)}";
        }

        private static string GetEmbeddingBackendDescription()
        {
            if (!SemanticIndexService.TryGetEmbeddingBackend(out EmbeddingProvider provider, out string providerName, out string model, out string serviceUrl))
            {
                return "the selected embedding backend";
            }

            string resolvedServiceUrl = SemanticIndexService.GetEmbeddingServiceUrl(provider, serviceUrl);
            return $"{providerName} at {resolvedServiceUrl} using embedding model '{model}'";
        }

        private static T FindException<T>(Exception e) where T : Exception
        {
            Exception current = e;
            while (current != null)
            {
                if (current is T match) return match;
                current = current.InnerException;
            }

            return null;
        }

        private static string GetMostSpecificExceptionMessage(Exception e)
        {
            if (e == null) return "Unknown error.";

            string message = e.Message;
            Exception current = e.InnerException;
            while (current != null)
            {
                if (!string.IsNullOrWhiteSpace(current.Message)) message = current.Message;
                current = current.InnerException;
            }

            return string.IsNullOrWhiteSpace(message) ? "Unknown error." : message;
        }

        private static string FormatExceptionDetails(string details)
        {
            return string.IsNullOrWhiteSpace(details) ? string.Empty : $" Details: {details}";
        }

        private static AssetSearch.Result CreateFailureResult(AssetSearch.Options opt, string error)
        {
            return new AssetSearch.Result
            {
                Error = error,
                Files = new List<AssetInfo>(),
                InMemory = opt.InMemory
            };
        }

        private static List<SemanticRankedFile> Hydrate(List<SemanticIndexService.SemanticSearchMatch> matches, string query, int requestedResultCount)
        {
            if (matches == null || matches.Count == 0) return new List<SemanticRankedFile>();

            Dictionary<int, float> scores = new Dictionary<int, float>();
            foreach (SemanticIndexService.SemanticSearchMatch match in matches)
            {
                if (!scores.TryGetValue(match.AssetFileId, out float existingScore) || match.Score > existingScore) scores[match.AssetFileId] = match.Score;
            }

            HashSet<string> allowedTypes = GetAllowedSemanticTypes();
            if (allowedTypes.Count == 0) return new List<SemanticRankedFile>();

            SemanticSearchQuery semanticQuery = SemanticSearchQuery.Create(query);
            List<AssetInfo> files = new List<AssetInfo>();
            const int batchSize = 500;
            List<int> ids = matches.Select(m => m.AssetFileId).ToList();
            for (int i = 0; i < ids.Count; i += batchSize)
            {
                List<int> batch = ids.Skip(i).Take(batchSize).ToList();
                string placeholders = string.Join(",", batch.Select(_ => "?"));
                files.AddRange(DBAdapter.DB.Query<AssetInfo>(
                    $"select *, AssetFile.Id as Id from AssetFile inner join Asset on Asset.Id = AssetFile.AssetId where AssetFile.Id in ({placeholders})",
                    batch.Cast<object>().ToArray()));
            }

            List<SemanticRankedFile> ranked = files
                .Where(file => IsAllowedSemanticType(file.Type, allowedTypes))
                .Select(file =>
                {
                    SemanticRankingEvaluation evaluation = EvaluateSemanticRanking(file, semanticQuery, scores[file.Id]);
                    return new SemanticRankedFile(file, evaluation.Score, evaluation.StrongLexicalMatch);
                })
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.File.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ApplyDiversityPenalty(ranked);
            ranked = ranked
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.File.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return ApplyDynamicCutoff(ranked, requestedResultCount);
        }

        internal static float ApplySemanticRankingAdjustments(AssetInfo file, SemanticSearchQuery query, float score)
        {
            return EvaluateSemanticRanking(file, query, score).Score;
        }

        private static SemanticRankingEvaluation EvaluateSemanticRanking(AssetInfo file, SemanticSearchQuery query, float score)
        {
            if (file == null || query == null || string.IsNullOrWhiteSpace(query.Phrase)) return new SemanticRankingEvaluation(score, false);

            SemanticFileText text = SemanticFileText.Create(file);
            bool strongLexicalMatch = false;

            if (Contains(file.FileName, query.Phrase))
            {
                score += FullPhraseFileNameBoost;
                strongLexicalMatch = true;
            }
            if (Contains(file.Path, query.Phrase))
            {
                score += FullPhrasePathBoost;
                strongLexicalMatch = true;
            }
            if (Contains(file.AICaption, query.Phrase))
            {
                score += FullPhraseCaptionBoost;
                strongLexicalMatch = true;
            }
            if (Contains(file.DisplayName, query.Phrase)) score += FullPhrasePackageBoost;

            int tokenCount = query.Tokens.Count;
            if (tokenCount > 0)
            {
                int fileHits = CountQueryHits(query.Tokens, text.FileTokens);
                int captionHits = CountQueryHits(query.Tokens, text.CaptionTokens);
                int contextHits = CountQueryHits(query.Tokens, text.ContextTokens);

                if (fileHits == tokenCount)
                {
                    score += AllQueryTermsInFileBoost;
                    strongLexicalMatch = true;
                }
                else if (fileHits + captionHits >= tokenCount)
                {
                    score += AllQueryTermsBoost;
                    strongLexicalMatch = true;
                }

                score += Math.Min(fileHits, tokenCount) * DirectTokenBoost;
                score += Math.Min(captionHits, tokenCount) * CaptionTokenBoost;

                if (contextHits > 0)
                {
                    if (fileHits == 0 && captionHits == 0) score -= PackageOnlyMatchPenalty + Math.Min(0.025f, contextHits * 0.005f);
                    else score += Math.Min(0.015f, contextHits * 0.005f);
                }
            }

            score += GetMediaIntentAdjustment(file.Type, query);
            return new SemanticRankingEvaluation(score, strongLexicalMatch);
        }

        private static bool Contains(string value, string term)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int CountQueryHits(List<SemanticQueryToken> queryTokens, HashSet<string> fieldTokens)
        {
            if (queryTokens == null || queryTokens.Count == 0 || fieldTokens == null || fieldTokens.Count == 0) return 0;

            int hits = 0;
            foreach (SemanticQueryToken token in queryTokens)
            {
                if (token.Matches(fieldTokens)) hits++;
            }
            return hits;
        }

        private static float GetMediaIntentAdjustment(string type, SemanticSearchQuery query)
        {
            string normalizedType = NormalizeType(type);
            if (string.IsNullOrEmpty(normalizedType)) return 0f;

            if (AudioTypes.Contains(normalizedType)) return query.HasAudioIntent ? 0.05f : -0.03f;
            if (CodeAndDocumentTypes.Contains(normalizedType)) return query.HasCodeIntent ? 0.03f : -0.05f;
            if (VisualTypes.Contains(normalizedType)) return !query.HasAudioIntent || query.HasVisualIntent ? 0.015f : -0.005f;
            return 0f;
        }

        private static void ApplyDiversityPenalty(List<SemanticRankedFile> ranked)
        {
            if (ranked == null || ranked.Count <= 1) return;

            Dictionary<int, int> packageCounts = new Dictionary<int, int>();
            Dictionary<string, int> folderCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ranked.Count; i++)
            {
                SemanticRankedFile rankedFile = ranked[i];
                int packageCount = packageCounts.TryGetValue(rankedFile.File.AssetId, out int existingPackageCount) ? existingPackageCount : 0;
                string folder = GetFolderKey(rankedFile.File.Path);
                int folderCount = folderCounts.TryGetValue(folder, out int existingFolderCount) ? existingFolderCount : 0;

                float penalty = rankedFile.StrongLexicalMatch
                    ? 0f
                    : Math.Min(MaxDiversityPenalty,
                        Math.Max(0, packageCount - 1) * PackageRepeatPenalty +
                        Math.Max(0, folderCount - 1) * FolderRepeatPenalty);

                if (penalty > 0f) ranked[i] = new SemanticRankedFile(rankedFile.File, rankedFile.Score - penalty, rankedFile.StrongLexicalMatch);

                packageCounts[rankedFile.File.AssetId] = packageCount + 1;
                if (!string.IsNullOrEmpty(folder)) folderCounts[folder] = folderCount + 1;
            }
        }

        private static List<SemanticRankedFile> ApplyDynamicCutoff(List<SemanticRankedFile> ranked, int requestedResultCount)
        {
            if (ranked == null || ranked.Count <= 1) return ranked ?? new List<SemanticRankedFile>();

            float topScore = ranked[0].Score;
            int minimumKeep = Math.Min(ranked.Count, Math.Min(Math.Max(1, requestedResultCount), MinimumDynamicResultCount));
            List<SemanticRankedFile> result = new List<SemanticRankedFile>(ranked.Count);
            for (int i = 0; i < ranked.Count; i++)
            {
                if (ranked[i].StrongLexicalMatch || ShouldKeepDynamicScore(ranked[i].Score, topScore, i, minimumKeep)) result.Add(ranked[i]);
            }

            return result;
        }

        internal static bool ShouldKeepDynamicScore(float score, float topScore, int rankIndex, int minimumKeep)
        {
            if (rankIndex < minimumKeep && topScore - score <= DynamicMinimumKeepWindow) return true;
            return score >= CalculateDynamicScoreFloor(topScore);
        }

        internal static float CalculateDynamicScoreFloor(float topScore)
        {
            return Math.Max(AI.Config.semanticMinScore, Math.Max(topScore - DynamicScoreWindow, topScore * DynamicScoreRatio));
        }

        private static HashSet<string> GetAllowedSemanticTypes()
        {
            string[] types = AI.ResolveExtensionList(AI.Config.semanticIndexExtensions);
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string type in types)
            {
                string normalized = NormalizeType(type);
                if (!string.IsNullOrEmpty(normalized)) result.Add(normalized);
            }
            return result;
        }

        private static bool IsAllowedSemanticType(string type, HashSet<string> allowedTypes)
        {
            return allowedTypes != null && allowedTypes.Contains(NormalizeType(type));
        }

        private static string NormalizeType(string type)
        {
            return string.IsNullOrWhiteSpace(type) ? string.Empty : type.Trim().TrimStart('.').ToLowerInvariant();
        }

        private static string GetFolderKey(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            string normalized = path.Replace('\\', '/');
            int index = normalized.LastIndexOf('/');
            return index <= 0 ? string.Empty : normalized.Substring(0, index);
        }

        private readonly struct SemanticRankedFile
        {
            public readonly AssetInfo File;
            public readonly float Score;
            public readonly bool StrongLexicalMatch;

            public SemanticRankedFile(AssetInfo file, float score, bool strongLexicalMatch = false)
            {
                File = file;
                Score = score;
                StrongLexicalMatch = strongLexicalMatch;
            }
        }

        private readonly struct SemanticRankingEvaluation
        {
            public readonly float Score;
            public readonly bool StrongLexicalMatch;

            public SemanticRankingEvaluation(float score, bool strongLexicalMatch)
            {
                Score = score;
                StrongLexicalMatch = strongLexicalMatch;
            }
        }

        internal sealed class SemanticSearchQuery
        {
            public readonly string Phrase;
            public readonly List<SemanticQueryToken> Tokens;
            public readonly bool HasAudioIntent;
            public readonly bool HasVisualIntent;
            public readonly bool HasCodeIntent;

            private SemanticSearchQuery(string phrase, List<SemanticQueryToken> tokens, bool hasAudioIntent, bool hasVisualIntent, bool hasCodeIntent)
            {
                Phrase = phrase;
                Tokens = tokens;
                HasAudioIntent = hasAudioIntent;
                HasVisualIntent = hasVisualIntent;
                HasCodeIntent = hasCodeIntent;
            }

            public static SemanticSearchQuery Create(string phrase)
            {
                string normalizedPhrase = phrase?.Trim() ?? string.Empty;
                List<SemanticQueryToken> tokens = new List<SemanticQueryToken>();
                bool hasAudioIntent = false;
                bool hasVisualIntent = false;
                bool hasCodeIntent = false;

                MatchCollection matches = WordPattern.Matches(ExpandIdentifierTerms(normalizedPhrase));
                foreach (Match match in matches)
                {
                    string token = match.Value.ToLowerInvariant();
                    if (token.Length <= 1 || StopWords.Contains(token)) continue;

                    if (AudioIntentTerms.Contains(token)) hasAudioIntent = true;
                    if (VisualIntentTerms.Contains(token)) hasVisualIntent = true;
                    if (CodeIntentTerms.Contains(token)) hasCodeIntent = true;
                    tokens.Add(SemanticQueryToken.Create(token));
                }

                return new SemanticSearchQuery(normalizedPhrase, tokens, hasAudioIntent, hasVisualIntent, hasCodeIntent);
            }
        }

        internal readonly struct SemanticQueryToken
        {
            private readonly string[] _variants;

            private SemanticQueryToken(string[] variants)
            {
                _variants = variants;
            }

            public static SemanticQueryToken Create(string token)
            {
                HashSet<string> variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {token};
                if (token.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && token.Length > 4) variants.Add(token.Substring(0, token.Length - 3) + "y");
                if (token.EndsWith("es", StringComparison.OrdinalIgnoreCase) && token.Length > 3) variants.Add(token.Substring(0, token.Length - 2));
                if (token.EndsWith("s", StringComparison.OrdinalIgnoreCase) && token.Length > 3) variants.Add(token.Substring(0, token.Length - 1));
                if (!token.EndsWith("s", StringComparison.OrdinalIgnoreCase) && token.Length > 2) variants.Add(token + "s");
                if (token.EndsWith("y", StringComparison.OrdinalIgnoreCase) && token.Length > 2) variants.Add(token.Substring(0, token.Length - 1) + "ies");
                return new SemanticQueryToken(variants.ToArray());
            }

            public bool Matches(HashSet<string> fieldTokens)
            {
                if (fieldTokens == null || fieldTokens.Count == 0 || _variants == null) return false;

                foreach (string variant in _variants)
                {
                    if (fieldTokens.Contains(variant)) return true;
                }
                return false;
            }
        }

        private sealed class SemanticFileText
        {
            public readonly HashSet<string> FileTokens;
            public readonly HashSet<string> CaptionTokens;
            public readonly HashSet<string> ContextTokens;

            private SemanticFileText(HashSet<string> fileTokens, HashSet<string> captionTokens, HashSet<string> contextTokens)
            {
                FileTokens = fileTokens;
                CaptionTokens = captionTokens;
                ContextTokens = contextTokens;
            }

            public static SemanticFileText Create(AssetInfo file)
            {
                HashSet<string> fileTokens = ExtractTokens(file.FileName, file.Path);
                HashSet<string> captionTokens = ExtractTokens(file.AICaption);
                HashSet<string> contextTokens = ExtractTokens(file.DisplayName, file.DisplayCategory, file.DisplayPublisher, file.SafeCategory, file.SafePublisher, file.Keywords);
                return new SemanticFileText(fileTokens, captionTokens, contextTokens);
            }
        }

        private static HashSet<string> ExtractTokens(params string[] values)
        {
            HashSet<string> tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (values == null) return tokens;

            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;

                MatchCollection matches = WordPattern.Matches(ExpandIdentifierTerms(value));
                foreach (Match match in matches)
                {
                    string token = match.Value.ToLowerInvariant();
                    if (token.Length <= 1 || StopWords.Contains(token)) continue;
                    tokens.Add(token);
                }
            }

            return tokens;
        }

        private static string ExpandIdentifierTerms(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            System.Text.StringBuilder sb = new System.Text.StringBuilder(value.Length + 8);
            char previous = '\0';
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                {
                    if (sb.Length > 0 && char.IsUpper(c) && char.IsLower(previous)) sb.Append(' ');
                    sb.Append(c);
                }
                else
                {
                    sb.Append(' ');
                }

                previous = c;
            }

            return sb.ToString();
        }
    }
}
