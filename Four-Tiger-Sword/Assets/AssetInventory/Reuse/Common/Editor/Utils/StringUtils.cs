using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ImpossibleRobert.Common
{
    public static class StringUtils
    {
        private const long SEC = TimeSpan.TicksPerSecond;
        private const long MIN = TimeSpan.TicksPerMinute;
        private const long HOUR = TimeSpan.TicksPerHour;
        private const long DAY = TimeSpan.TicksPerDay;
        private static readonly Regex CAMEL_CASE_R1 = new Regex(@"(?<=[a-z])(?=[A-Z])|(?<=[0-9])(?=[A-Z])|(?<=[A-Z])(?=[0-9])|(?<=[0-9])(?=[a-z])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex CAMEL_CASE_R2 = new Regex(@"(?<= [A-Z])(?=[A-Z][a-z])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex CAMEL_CASE_R3 = new Regex(@"(?<=[^\s])(?=[(])|(?<=[)])(?=[^\s])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Precompiled regex patterns for performance
        private static readonly Regex ESCAPE_SQL_LIKE_PATTERN = new Regex(@"(like\s+'[^']*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ESCAPE_SQL_LIKE_ESCAPE_PATTERN = new Regex(@"(like\s+'[^']*')", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex STRIP_TAGS_PATTERN = new Regex("<.*?>", RegexOptions.Compiled);
        private static readonly Regex STRIP_TAGS_WITH_CONTENT_PATTERN = new Regex("<[^>]+?>.*?</[^>]+?>", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex STRIP_UNICODE_PATTERN = new Regex("&#.*?;", RegexOptions.Compiled);
        private static readonly Regex NORMALIZE_LINE_BREAKS_PATTERN = new Regex(@"\r\n?|\n", RegexOptions.Compiled);
        private static readonly Regex WHITESPACE_BEFORE_NEWLINE_PATTERN = new Regex(@"[ \t]+\n", RegexOptions.Compiled);
        private static readonly Regex MULTIPLE_NEWLINES_PATTERN = new Regex(@"\n{3,}", RegexOptions.Compiled);
        private static readonly Regex MULTIPLE_WHITESPACE_PATTERN = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex ANCHOR_TAG_PATTERN = new Regex(@"<a\s[^>]*href\s*=\s*[""']([^""']*)[""'][^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex BARE_URL_PATTERN = new Regex(@"(?<![""'=])\b(https?://[^\s<>""'\)]+)", RegexOptions.Compiled);
        private static readonly Regex LINK_MARKER_PATTERN = new Regex("\uE000(?<index>\\d+)\uE001", RegexOptions.Compiled);

        public static string ExtractTokens(string input, string tokenName, List<string> tokenValues)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(tokenName)) return input;

            // Pattern to match tokens with different value formats:
            // 1. Quoted strings (single or double quotes) - handles escaped tokens with spaces
            // 2. Non-whitespace sequences - handles simple tokens without spaces
            // 3. Empty tokens (just the token name followed by colon) - should be removed but not added to values
            string pattern = $@"\b{Regex.Escape(tokenName)}:((?:'[^']*'|""[^""]*""|\S+)?)";

            // Use a MatchEvaluator to both capture the token and remove it in one go. Make token name matching case-insensitive.
            string result = Regex.Replace(input, pattern, match =>
            {
                string value = match.Groups[1].Value;

                // Skip empty tokens (don't add them to the values list)
                if (string.IsNullOrEmpty(value))
                {
                    return string.Empty;
                }

                // Remove quotes if present (for escaped tokens)
                if ((value.StartsWith("'") && value.EndsWith("'")) ||
                    (value.StartsWith("\"") && value.EndsWith("\"")))
                {
                    value = value.Substring(1, value.Length - 2);
                }

                tokenValues.Add(value);

                // Return an empty string to remove this token from the original text.
                return string.Empty;
            }, RegexOptions.IgnoreCase);

            // remove any excess whitespace created by token removal
            result = MULTIPLE_WHITESPACE_PATTERN.Replace(result, " ").Trim();

            return result;
        }

        public static string ExtractTokens(string input, IEnumerable<string> tokenNames, List<string> tokenValues)
        {
            if (string.IsNullOrEmpty(input) || tokenNames == null) return input;

            List<string> names = new List<string>();
            foreach (string n in tokenNames)
            {
                if (!string.IsNullOrEmpty(n)) names.Add(Regex.Escape(n));
            }
            if (names.Count == 0) return input;

            string pattern = $@"\b(?:{string.Join("|", names)}):((?:'[^']*'|""[^""]*""|\S+)?)";

            string result = Regex.Replace(input, pattern, match =>
            {
                string value = match.Groups[1].Value;
                if (string.IsNullOrEmpty(value)) return string.Empty;
                if ((value.StartsWith("'") && value.EndsWith("'")) || (value.StartsWith("\"") && value.EndsWith("\"")))
                {
                    value = value.Substring(1, value.Length - 2);
                }
                tokenValues.Add(value);
                return string.Empty;
            }, RegexOptions.IgnoreCase);

            result = MULTIPLE_WHITESPACE_PATTERN.Replace(result, " ").Trim();
            return result;
        }

        public static string GetRelativeTimeDifference(DateTime date)
        {
            return GetRelativeTimeDifference(date, DateTime.Now);
        }

        public static string GetRelativeTimeDifference(DateTime date1, DateTime date2)
        {
            long ticks = date2.Ticks - date1.Ticks;
            if (ticks < 0) ticks = -ticks;

            if (ticks >= DAY)
            {
                int v = (int)(ticks / DAY);
                return v == 1 ? "1 day ago" : v.ToString(CultureInfo.InvariantCulture) + " days ago";
            }
            if (ticks >= HOUR)
            {
                int v = (int)(ticks / HOUR);
                return v == 1 ? "1 hour ago" : v.ToString(CultureInfo.InvariantCulture) + " hours ago";
            }
            if (ticks >= MIN)
            {
                int v = (int)(ticks / MIN);
                return v == 1 ? "1 minute ago" : v.ToString(CultureInfo.InvariantCulture) + " minutes ago";
            }

            int s = (int)(ticks / SEC);
            return s == 1 ? "1 second ago" : s.ToString(CultureInfo.InvariantCulture) + " seconds ago";
        }

        /// <summary>
        /// Formats a duration in seconds into a human-readable string (e.g., "1 Hour 30 Min").
        /// </summary>
        public static string FormatDuration(float totalSeconds, int maxComponents = 2)
        {
            if (totalSeconds < 0) totalSeconds = 0;

            // For durations < 10 seconds, show fractions
            bool showFractions = totalSeconds < 10;

            int days = (int)(totalSeconds / 86400);
            totalSeconds -= days * 86400;

            int hours = (int)(totalSeconds / 3600);
            totalSeconds -= hours * 3600;

            int minutes = (int)(totalSeconds / 60);
            totalSeconds -= minutes * 60;

            int seconds = (int)totalSeconds;
            float fractionalSeconds = totalSeconds;

            List<string> parts = new List<string>();

            if (days > 0)
            {
                parts.Add(days.ToString(CultureInfo.InvariantCulture) + " Day" + (days == 1 ? "" : "s"));
            }
            if (hours > 0)
            {
                parts.Add(hours.ToString(CultureInfo.InvariantCulture) + " Hour" + (hours == 1 ? "" : "s"));
            }
            if (minutes > 0)
            {
                parts.Add(minutes.ToString(CultureInfo.InvariantCulture) + " Min");
            }
            if (seconds > 0 || parts.Count == 0) // Always show seconds if it's the only component or if there's time left
            {
                if (showFractions && parts.Count == 0)
                {
                    // Show one decimal place for durations < 10 seconds
                    parts.Add(fractionalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " Sec");
                }
                else
                {
                    parts.Add(seconds.ToString(CultureInfo.InvariantCulture) + " Sec");
                }
            }

            // Limit to maxComponents if specified
            if (maxComponents > 0 && parts.Count > maxComponents)
            {
                parts = parts.GetRange(0, maxComponents);
            }

            return string.Join(" ", parts);
        }

        public static string EscapeSQL(string input)
        {
            // Replace underscores with escaped underscores inside 'like' clauses
            input = ESCAPE_SQL_LIKE_PATTERN.Replace(input, m =>
            {
                string likeClause = m.Groups[1].Value;
                likeClause = likeClause.Replace("_", "\\_");
                return likeClause;
            });

            // Add ESCAPE '\' behind each 'like' clause
            input = ESCAPE_SQL_LIKE_ESCAPE_PATTERN.Replace(input, "$1 ESCAPE '\\'");

            return input;
        }

        public static string Truncate(this string value, int maxLength)
        {
            if (value == null) return null;

            return value.Length <= maxLength
                ? value
                : value.Substring(0, maxLength);
        }

        public static string[] Split(string input, char[] separators)
        {
            if (string.IsNullOrEmpty(input)) return Array.Empty<string>();

            string[] parts = input.Split(separators, StringSplitOptions.None);

            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = parts[i].Trim();
            }

            return parts;
        }

        public static List<string> FlattenCommaSeparated(IEnumerable<string> inputs)
        {
            List<string> result = new List<string>();
            if (inputs == null) return result;

            foreach (string v in inputs)
            {
                if (string.IsNullOrWhiteSpace(v)) continue;
                foreach (string part in Split(v, new[] {','}))
                {
                    if (!string.IsNullOrEmpty(part)) result.Add(part);
                }
            }

            return result;
        }

        public static string CamelCaseToWords(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            string result = CAMEL_CASE_R1.Replace(input, " ");
            result = CAMEL_CASE_R2.Replace(result, " ");
            result = CAMEL_CASE_R3.Replace(result, " ");

            string[] words = result.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                words[i] = CapitalizeFirstLetter(words[i]);
            }

            return string.Join(" ", words);
        }

        private static string CapitalizeFirstLetter(string word)
        {
            if (string.IsNullOrEmpty(word)) return word;

            // Preserve the case of the rest of the word
            return char.ToUpper(word[0]) + word.Substring(1);
        }

        public static string GetShortHash(string input, int length = 6)
        {
            if (length < 1 || length > 10)
            {
                throw new ArgumentOutOfRangeException(nameof (length), "Length must be between 1 and 10.");
            }

            // Compute a simple hash from the input string.
            int hash = 0;
            foreach (char c in input)
            {
                hash = (hash * 31 + c); // Use a prime number multiplier
            }

            // Calculate the modulus based on the desired length
            int mod = (int)Math.Pow(10, length);

            // Reduce the hash to a number with the desired length
            int shortHash = Math.Abs(hash) % mod;

            // Return the hash as a string, padded with leading zeros if necessary
            return shortHash.ToString($"D{length}");
        }

        public static bool IsUrl(string url)
        {
            return Uri.IsWellFormedUriString(url, UriKind.Absolute);
        }

        public static bool IsUnicode(this string input)
        {
            // Iterate directly over string without allocating char array
            foreach (char c in input)
            {
                if (c > 255) return true;
            }
            return false;
        }

        public static string StripTags(string input, bool removeContentBetweenTags = false)
        {
            if (removeContentBetweenTags)
            {
                return STRIP_TAGS_WITH_CONTENT_PATTERN.Replace(input, string.Empty);
            }
            return STRIP_TAGS_PATTERN.Replace(input, string.Empty);
        }

        public static string StripUnicode(string input)
        {
            return STRIP_UNICODE_PATTERN.Replace(input, string.Empty);
        }

        public static string RemoveTrailing(this string source, string text)
        {
            if (source == null)
            {
                Debug.LogError("This should not happen, source is null");
                return null;
            }

            // Handle empty text case - return source unchanged
            if (string.IsNullOrEmpty(text)) return source;

            // Calculate final length once to avoid multiple substring allocations
            int textLength = text.Length;
            int endIndex = source.Length;

            while (endIndex >= textLength && source.Substring(endIndex - textLength, textLength) == text)
            {
                endIndex -= textLength;
            }

            return endIndex == source.Length ? source : source.Substring(0, endIndex);
        }

        public static string ToLowercaseFirstLetter(this string input)
        {
            if (string.IsNullOrEmpty(input) || char.IsLower(input[0]))
            {
                return input;
            }

            return char.ToLower(input[0]) + input.Substring(1);
        }

        public static string ToLabel(string input)
        {
            string result = input;

            // Normalize line breaks to \n
            result = NORMALIZE_LINE_BREAKS_PATTERN.Replace(result, "\n");

            // Translate some HTML tags
            result = result.Replace("<br>", "\n");
            result = result.Replace("</br>", "\n");
            result = result.Replace("<p>", "\n\n");
            result = result.Replace("<p >", "\n\n");
            result = result.Replace("<li>", "\n* ");
            result = result.Replace("<li >", "\n* ");
            result = result.Replace("&nbsp;", " ");
            result = result.Replace("&amp;", "&");

            // Remove remaining tags and also unicode tags
            result = StripUnicode(StripTags(result));

            // Remove whitespace from empty lines
            result = WHITESPACE_BEFORE_NEWLINE_PATTERN.Replace(result, "\n");

            // Ensure at max two consecutive line breaks
            result = MULTIPLE_NEWLINES_PATTERN.Replace(result, "\n\n");

            return result.Trim();
        }

        public static TextWithLinks ToLabelWithLinks(string input)
        {
            TextWithLinks result = new TextWithLinks(string.Empty);
            if (string.IsNullOrWhiteSpace(input))
            {
                result.Text = string.Empty;
                return result;
            }

            string text = input;
            List<TextLink> anchorLinks = new List<TextLink>();

            // Extract <a href="url">display text</a> before stripping tags
            text = ANCHOR_TAG_PATTERN.Replace(text, match =>
            {
                string url = match.Groups[1].Value.Trim();
                string displayText = StripTags(match.Groups[2].Value).Trim();
                if (string.IsNullOrEmpty(url)) return displayText;

                if (string.IsNullOrEmpty(displayText)) displayText = ShortenUrl(url);
                displayText = ToLabel(displayText);
                int linkIndex = anchorLinks.Count;
                anchorLinks.Add(new TextLink(displayText, url));
                return "\uE000" + linkIndex + "\uE001";
            });

            // Run normal ToLabel processing
            text = NORMALIZE_LINE_BREAKS_PATTERN.Replace(text, "\n");
            text = text.Replace("<br>", "\n");
            text = text.Replace("</br>", "\n");
            text = text.Replace("<p>", "\n\n");
            text = text.Replace("<p >", "\n\n");
            text = text.Replace("<li>", "\n* ");
            text = text.Replace("<li >", "\n* ");
            text = text.Replace("&nbsp;", " ");
            text = text.Replace("&amp;", "&");
            text = StripUnicode(StripTags(text));
            text = WHITESPACE_BEFORE_NEWLINE_PATTERN.Replace(text, "\n");
            text = MULTIPLE_NEWLINES_PATTERN.Replace(text, "\n\n");
            text = text.Trim();

            List<LinkRange> linkRanges = new List<LinkRange>();
            if (anchorLinks.Count > 0)
            {
                StringBuilder expanded = new StringBuilder(text.Length);
                int markerCursor = 0;
                MatchCollection markerMatches = LINK_MARKER_PATTERN.Matches(text);
                for (int i = 0; i < markerMatches.Count; i++)
                {
                    Match marker = markerMatches[i];
                    expanded.Append(text, markerCursor, marker.Index - markerCursor);

                    int linkIndex;
                    if (int.TryParse(marker.Groups["index"].Value, out linkIndex) && linkIndex >= 0 && linkIndex < anchorLinks.Count)
                    {
                        TextLink link = anchorLinks[linkIndex];
                        int startIndex = expanded.Length;
                        expanded.Append(link.DisplayText);
                        linkRanges.Add(new LinkRange(startIndex, link.DisplayText.Length, link.DisplayText, link.Url));
                    }
                    markerCursor = marker.Index + marker.Length;
                }
                expanded.Append(text, markerCursor, text.Length - markerCursor);
                text = expanded.ToString();
            }

            // Extract remaining bare URLs from the cleaned text, excluding text already owned by an anchor.
            MatchCollection urlMatches = BARE_URL_PATTERN.Matches(text);
            for (int i = 0; i < urlMatches.Count; i++)
            {
                Match match = urlMatches[i];
                if (OverlapsLinkRange(match.Index, match.Length, linkRanges)) continue;

                string url = match.Groups[1].Value.Trim();
                string shortened = ShortenUrl(url);
                linkRanges.Add(new LinkRange(match.Index, match.Length, shortened, url));
            }

            linkRanges.Sort((left, right) => left.StartIndex.CompareTo(right.StartIndex));
            StringBuilder output = new StringBuilder(text.Length);
            int cursor = 0;
            for (int i = 0; i < linkRanges.Count; i++)
            {
                LinkRange linkRange = linkRanges[i];
                if (linkRange.StartIndex < cursor || linkRange.StartIndex + linkRange.SourceLength > text.Length) continue;

                output.Append(text, cursor, linkRange.StartIndex - cursor);
                int startIndex = output.Length;
                output.Append(linkRange.DisplayText);
                result.Links.Add(new TextLink(linkRange.DisplayText, linkRange.Url, startIndex, linkRange.DisplayText.Length));
                cursor = linkRange.StartIndex + linkRange.SourceLength;
            }
            output.Append(text, cursor, text.Length - cursor);

            result.Text = output.ToString();
            return result;
        }

        private static bool OverlapsLinkRange(int startIndex, int length, List<LinkRange> linkRanges)
        {
            int endIndex = startIndex + length;
            for (int i = 0; i < linkRanges.Count; i++)
            {
                LinkRange linkRange = linkRanges[i];
                int linkEndIndex = linkRange.StartIndex + linkRange.SourceLength;
                if (startIndex < linkEndIndex && endIndex > linkRange.StartIndex) return true;
            }
            return false;
        }

        private static string ShortenUrl(string url)
        {
            try
            {
                Uri uri = new Uri(url);
                string host = uri.Host.Replace("www.", "");
                string path = uri.AbsolutePath;
                if (path.Length > 30) path = path.Substring(0, 27) + "...";
                if (path == "/") return host;
                return host + path;
            }
            catch
            {
                if (url.Length > 60) return url.Substring(0, 57) + "...";
                return url;
            }
        }

        private readonly struct LinkRange
        {
            public readonly int StartIndex;
            public readonly int SourceLength;
            public readonly string DisplayText;
            public readonly string Url;

            public LinkRange(int startIndex, int sourceLength, string displayText, string url)
            {
                StartIndex = startIndex;
                SourceLength = sourceLength;
                DisplayText = displayText;
                Url = url;
            }
        }

        public static string GetEnvVar(string key)
        {
            string value = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Process);
            if (string.IsNullOrWhiteSpace(value)) value = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.User);
            if (string.IsNullOrWhiteSpace(value)) value = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Machine);

            return value;
        }

        /// <summary>
        /// Formats bytes into a human-readable string (e.g., "1.5 MB", "256 KB").
        /// Thread-safe alternative to EditorUtility.FormatBytes.
        /// </summary>
        /// <param name="bytes">Number of bytes to format</param>
        /// <returns>Formatted string with appropriate unit (B, KB, MB, GB, TB)</returns>
        public static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "0 B";

            string[] sizes = {"B", "KB", "MB", "GB", "TB"};
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            // Format with up to 1 decimal place, but drop .0
            string formatted = len.ToString(len % 1 == 0 ? "0" : "0.0", CultureInfo.InvariantCulture);
            return $"{formatted} {sizes[order]}";
        }
    }

    public struct TextLink
    {
        public string DisplayText;
        public string Url;
        public int StartIndex;
        public int Length;

        public TextLink(string displayText, string url)
            : this(displayText, url, -1, 0)
        {
        }

        public TextLink(string displayText, string url, int startIndex, int length)
        {
            DisplayText = displayText;
            Url = url;
            StartIndex = startIndex;
            Length = length;
        }
    }

    public struct TextWithLinks
    {
        public string Text;
        public List<TextLink> Links;

        public bool HasLinks => Links != null && Links.Count > 0;

        public TextWithLinks(string text)
        {
            Text = text;
            Links = new List<TextLink>();
        }
    }
}
