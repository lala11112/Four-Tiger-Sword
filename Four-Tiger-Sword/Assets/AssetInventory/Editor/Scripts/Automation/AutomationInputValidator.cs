using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AssetInventory.Automation
{
    internal static class AutomationInputValidator
    {
        internal static AutomationResponse ValidatePaging(int page, int pageSize, int maximumPageSize)
        {
            if (page < 1)
            {
                return AutomationResponse.Error("Page must be 1 or greater.", errorCode: "invalid_input");
            }
            if (pageSize < 1 || pageSize > maximumPageSize)
            {
                return AutomationResponse.Error($"MaxResults must be between 1 and {maximumPageSize}.", errorCode: "invalid_input");
            }
            return null;
        }

        internal static AutomationResponse ValidateRange(long minimum, long maximum, string label)
        {
            if (minimum < 0 || maximum < 0)
            {
                return AutomationResponse.Error($"{label} bounds cannot be negative.", errorCode: "invalid_input");
            }
            if (minimum > 0 && maximum > 0 && minimum > maximum)
            {
                return AutomationResponse.Error($"Minimum {label} cannot exceed maximum {label}.", errorCode: "invalid_input");
            }
            return null;
        }

        internal static AutomationResponse ValidateRange(float minimum, float maximum, string label)
        {
            if (float.IsNaN(minimum) || float.IsInfinity(minimum) || float.IsNaN(maximum) || float.IsInfinity(maximum))
            {
                return AutomationResponse.Error($"{label} bounds must be finite numbers.", errorCode: "invalid_input");
            }
            if (minimum < 0f || maximum < 0f)
            {
                return AutomationResponse.Error($"{label} bounds cannot be negative.", errorCode: "invalid_input");
            }
            if (minimum > 0f && maximum > 0f && minimum > maximum)
            {
                return AutomationResponse.Error($"Minimum {label} cannot exceed maximum {label}.", errorCode: "invalid_input");
            }
            return null;
        }

        internal static bool TryParseDefinedEnum<T>(string value, out T result) where T : struct, Enum
        {
            return Enum.TryParse(value, true, out result) && Enum.IsDefined(typeof(T), result);
        }

        internal static bool TryParseIsoDate(string value, out DateTime result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(value)) return false;

            if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTimeOffset parsed)) return false;
            result = parsed.DateTime;
            return true;
        }

        internal static AutomationResponse ResolveOptionIndex(
            string parameterName,
            string requestedValue,
            IReadOnlyList<string> options,
            Func<string, string> displayNameSelector,
            out int index)
        {
            index = -1;
            if (string.IsNullOrWhiteSpace(requestedValue)) return null;

            string requested = requestedValue.Trim();
            List<OptionMatch> candidates = options
                .Select((value, optionIndex) => new OptionMatch(optionIndex, value == null ? null : displayNameSelector(value)))
                .Where(match => !string.IsNullOrWhiteSpace(match.DisplayName) && !match.DisplayName.StartsWith("-", StringComparison.Ordinal))
                .ToList();

            List<OptionMatch> exactMatches = candidates
                .Where(match => string.Equals(match.DisplayName, requested, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (exactMatches.Count == 1)
            {
                index = exactMatches[0].Index;
                return null;
            }

            List<OptionMatch> partialMatches = candidates
                .Where(match => match.DisplayName.IndexOf(requested, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            if (partialMatches.Count == 1)
            {
                index = partialMatches[0].Index;
                return null;
            }
            if (partialMatches.Count == 0)
            {
                return AutomationResponse.Error($"{parameterName} '{requestedValue}' was not found.", errorCode: "not_found");
            }

            return AutomationResponse.Error(
                $"{parameterName} '{requestedValue}' is ambiguous. Use an exact value.",
                new {matches = partialMatches.Select(match => match.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToArray()},
                "ambiguous_target");
        }

        internal static bool TryNormalizeAssetsPath(string path, out string normalizedPath)
        {
            normalizedPath = null;
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)) return false;

            string candidate = path.Trim().Replace('\\', '/').TrimEnd('/');
            if (!string.Equals(candidate, "Assets", StringComparison.OrdinalIgnoreCase) && !candidate.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return false;
            if (candidate.Split('/').Any(segment => segment == "." || segment == "..")) return false;

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot)) return false;

            string assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(Path.Combine(projectRoot, candidate.Replace('/', Path.DirectorySeparatorChar))).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception)
            {
                return false;
            }

            StringComparison pathComparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(fullPath, assetsRoot, pathComparison) && !fullPath.StartsWith(assetsRoot + Path.DirectorySeparatorChar, pathComparison)) return false;

            normalizedPath = "Assets" + fullPath.Substring(assetsRoot.Length).Replace('\\', '/');
            return true;
        }

        private readonly struct OptionMatch
        {
            internal int Index { get; }
            internal string DisplayName { get; }

            internal OptionMatch(int index, string displayName)
            {
                Index = index;
                DisplayName = displayName;
            }
        }
    }
}
