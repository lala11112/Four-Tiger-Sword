using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AssetInventory
{
    internal readonly struct CodeChunkData
    {
        public readonly string ChunkKey;
        public readonly int StartLine;
        public readonly int EndLine;
        public readonly string Symbol;
        public readonly string Content;
        public readonly string ContentHash;

        public CodeChunkData(string chunkKey, int startLine, int endLine, string symbol, string content)
        {
            ChunkKey = chunkKey;
            StartLine = startLine;
            EndLine = endLine;
            Symbol = symbol;
            Content = content;
            ContentHash = SemanticVectorUtils.HashText(content);
        }
    }

    internal static class CodeSnippetBuilder
    {
        private const int MaxChunkLines = 90;
        private const int MaxChunkChars = 16 * 1024;
        private const int ChunkOverlapLines = 12;
        private const int MaxSnippetLines = 9;
        private const int MaxLineSegmentChars = MaxChunkChars;
        private static readonly Regex CSharpTypeRegex = new Regex(
            @"^\s*(?:(?:public|private|protected|internal|static|sealed|partial|abstract|readonly|unsafe|new)\s+)*(?:class|struct|interface|enum|record)\s+([A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled);
        private static readonly Regex CSharpMethodRegex = new Regex(
            @"^\s*(?:(?:public|private|protected|internal|static|sealed|partial|abstract|async|override|virtual|extern|unsafe|new)\s+)*[A-Za-z_][A-Za-z0-9_<>,\[\]\.?]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(",
            RegexOptions.Compiled);
        private static readonly Regex ShaderRegex = new Regex(@"\bShader\s+""([^""]+)""", RegexOptions.Compiled);

        public static List<CodeChunkData> CreateChunks(string text, string language)
        {
            string normalized = NormalizeText(text);
            List<IndexedLine> lines = CreateIndexedLines(normalized);
            List<CodeChunkData> result = new List<CodeChunkData>();
            if (lines.Count == 0 || string.IsNullOrWhiteSpace(normalized)) return result;

            int start = 0;
            int chunkIndex = 0;
            while (start < lines.Count)
            {
                int maxEnd = FindChunkEnd(lines, start);
                int end = FindPreferredChunkEnd(lines, start, maxEnd);

                string content = JoinLines(lines, start, end).Trim('\n', '\r');
                if (!string.IsNullOrWhiteSpace(content))
                {
                    string symbol = ExtractSymbol(lines, start, end, language);
                    result.Add(new CodeChunkData($"chunk:{chunkIndex}", lines[start].LineNumber, lines[end - 1].LineNumber, symbol, content));
                    chunkIndex++;
                }

                if (end >= lines.Count) break;
                start = GetNextChunkStart(start, end);
            }

            return result;
        }

        public static string BuildSnippet(string chunkContent, int chunkStartLine, IReadOnlyCollection<string> terms)
        {
            string normalized = NormalizeText(chunkContent);
            string[] lines = normalized.Split('\n');
            if (lines.Length <= MaxSnippetLines) return FormatLines(lines, chunkStartLine, 0, lines.Length);

            int matchLine = FindBestMatchLine(lines, terms);
            int start = Math.Max(0, matchLine - MaxSnippetLines / 2);
            if (start + MaxSnippetLines > lines.Length) start = Math.Max(0, lines.Length - MaxSnippetLines);
            return FormatLines(lines, chunkStartLine, start, Math.Min(lines.Length, start + MaxSnippetLines));
        }

        public static string NormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static int FindBestMatchLine(string[] lines, IReadOnlyCollection<string> terms)
        {
            if (terms == null || terms.Count == 0) return 0;

            int bestLine = 0;
            int bestScore = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                int score = 0;
                string line = lines[i];
                foreach (string term in terms)
                {
                    if (!string.IsNullOrWhiteSpace(term) && line.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) score++;
                }
                if (score <= bestScore) continue;
                bestScore = score;
                bestLine = i;
            }
            return bestLine;
        }

        private static string FormatLines(string[] lines, int chunkStartLine, int start, int end)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = start; i < end; i++)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append((chunkStartLine + i).ToString().PadLeft(4));
                sb.Append("  ");
                sb.Append(lines[i]);
            }
            return sb.ToString();
        }

        private static List<IndexedLine> CreateIndexedLines(string normalized)
        {
            string[] physicalLines = normalized.Split('\n');
            List<IndexedLine> result = new List<IndexedLine>(physicalLines.Length);
            for (int i = 0; i < physicalLines.Length; i++)
            {
                string line = physicalLines[i];
                int lineNumber = i + 1;
                if (line.Length <= MaxLineSegmentChars)
                {
                    result.Add(new IndexedLine(line, lineNumber));
                    continue;
                }

                int offset = 0;
                while (offset < line.Length)
                {
                    int length = Math.Min(MaxLineSegmentChars, line.Length - offset);
                    result.Add(new IndexedLine(line.Substring(offset, length), lineNumber));
                    offset += length;
                }
            }
            return result;
        }

        private static int FindChunkEnd(IReadOnlyList<IndexedLine> lines, int start)
        {
            int end = start;
            int charCount = 0;
            while (end < lines.Count && end - start < MaxChunkLines)
            {
                int addition = lines[end].Text.Length + (end > start ? 1 : 0);
                if (end > start && charCount + addition > MaxChunkChars) break;

                charCount += addition;
                end++;
            }
            if (end <= start) end = Math.Min(lines.Count, start + 1);
            return end;
        }

        private static int FindPreferredChunkEnd(IReadOnlyList<IndexedLine> lines, int start, int maxEnd)
        {
            int end = maxEnd;
            while (end < lines.Count && end > start + 20 && !string.IsNullOrWhiteSpace(lines[end - 1].Text))
            {
                end--;
            }
            return end <= start ? maxEnd : end;
        }

        private static string JoinLines(IReadOnlyList<IndexedLine> lines, int start, int end)
        {
            StringBuilder sb = new StringBuilder(Math.Min(MaxChunkChars, 1024));
            for (int i = start; i < end; i++)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(lines[i].Text);
            }
            return sb.ToString();
        }

        private static int GetNextChunkStart(int start, int end)
        {
            int chunkLineCount = end - start;
            int overlap = Math.Min(ChunkOverlapLines, Math.Max(0, chunkLineCount / 4));
            int nextStart = end - overlap;
            return nextStart <= start ? start + 1 : nextStart;
        }

        private static string ExtractSymbol(IReadOnlyList<IndexedLine> lines, int start, int end, string language)
        {
            if (string.Equals(language, "C#", StringComparison.OrdinalIgnoreCase))
            {
                return ExtractCSharpSymbol(lines, start, end);
            }
            if (string.Equals(language, "Shader", StringComparison.OrdinalIgnoreCase))
            {
                return ExtractShaderSymbol(lines, start, end);
            }
            return string.Empty;
        }

        private static string ExtractCSharpSymbol(IReadOnlyList<IndexedLine> lines, int start, int end)
        {
            List<string> symbols = new List<string>();
            for (int i = start; i < end; i++)
            {
                string line = lines[i].Text;
                Match typeMatch = CSharpTypeRegex.Match(line);
                if (typeMatch.Success)
                {
                    symbols.Add(typeMatch.Groups[1].Value);
                    continue;
                }

                Match methodMatch = CSharpMethodRegex.Match(line);
                if (methodMatch.Success) symbols.Add(methodMatch.Groups[1].Value);
                if (symbols.Count >= 4) break;
            }
            return string.Join(", ", symbols.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private static string ExtractShaderSymbol(IReadOnlyList<IndexedLine> lines, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                string line = lines[i].Text;
                Match match = ShaderRegex.Match(line);
                if (match.Success) return match.Groups[1].Value;
                if (line.IndexOf("SubShader", StringComparison.OrdinalIgnoreCase) >= 0) return "SubShader";
                if (line.IndexOf("Pass", StringComparison.OrdinalIgnoreCase) >= 0) return "Pass";
            }
            return string.Empty;
        }

        private readonly struct IndexedLine
        {
            public readonly string Text;
            public readonly int LineNumber;

            public IndexedLine(string text, int lineNumber)
            {
                Text = text;
                LineNumber = lineNumber;
            }
        }
    }
}
