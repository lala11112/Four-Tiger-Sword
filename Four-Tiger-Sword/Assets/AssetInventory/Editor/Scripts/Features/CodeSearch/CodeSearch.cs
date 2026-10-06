using System.Collections.Generic;

namespace AssetInventory
{
    /// <summary>Search facade for querying code records with structured options and result metadata.</summary>
    public static class CodeSearch
    {
        /// <summary>Controls the code-search phrase, result page, maximum files, and maximum matches retained per file.</summary>
        public sealed class Options
        {
            public string SearchPhrase = string.Empty;
            internal SearchScope Scope = SearchScope.All;
            public int MaxFiles = 100;
            public int MaxMatchesPerFile = 5;
            public int CurrentPage = 1;
        }

        /// <summary>Reports code-index availability, total matches and documents, ranked file results, and any nonfatal search error.</summary>
        public sealed class Result
        {
            public bool IndexExists;
            public bool FtsAvailable;
            public int ResultCount;
            public int DocumentCount;
            public string Error;
            public List<CodeSearchFileResult> Files = new List<CodeSearchFileResult>();
        }

        /// <summary>Groups the ranked code matches for one indexed source file with its package, language, path, and aggregate relevance score.</summary>
        public sealed class CodeSearchFileResult
        {
            public int DocumentId;
            public string Path;
            public string FileName;
            public string PhysicalPath;
            public string PackageName;
            public string Language;
            public string Extension;
            public CodeDocument.SourceKindType SourceKind;
            public List<CodeSearchMatch> Matches = new List<CodeSearchMatch>();
            public float Score;
        }

        /// <summary>One ranked code-search match with its source range, symbol, snippet, content, and relevance score.</summary>
        public sealed class CodeSearchMatch
        {
            public int ChunkId;
            public int StartLine;
            public int EndLine;
            public string Symbol;
            public string Snippet;
            public string Content;
            public float Score;
        }

        /// <summary>Searches the optional code index for the supplied phrase and returns ranked matches grouped by source file.</summary>
        public static Result Execute(Options options)
        {
            return CodeIndexService.Search(options ?? new Options());
        }
    }
}
