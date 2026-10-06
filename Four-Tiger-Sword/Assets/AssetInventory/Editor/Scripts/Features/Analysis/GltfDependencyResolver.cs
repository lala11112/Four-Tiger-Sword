using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AssetInventory
{
    internal static class GltfDependencyResolver
    {
        internal static IReadOnlyList<string> ExtractLocalDependencyPaths(string gltfAssetPath, string gltfJson)
        {
            if (string.IsNullOrWhiteSpace(gltfAssetPath) || string.IsNullOrWhiteSpace(gltfJson)) return Array.Empty<string>();

            JObject root;
            try
            {
                root = JObject.Parse(gltfJson);
            }
            catch
            {
                return Array.Empty<string>();
            }

            string baseDirectory = NormalizeIndexedPath(Path.GetDirectoryName(gltfAssetPath));
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string uri in EnumerateDependencyUris(root))
            {
                if (!TryNormalizeLocalUri(uri, out string relativeUri)) continue;
                if (!TryCombineRelativePath(baseDirectory, relativeUri, out string dependencyPath)) continue;

                result.Add(dependencyPath);
            }

            return result.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static string NormalizeIndexedPath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace("\\", "/").Trim('/');
        }

        private static IEnumerable<string> EnumerateDependencyUris(JObject root)
        {
            foreach (JToken token in root.SelectTokens("$.buffers[*].uri"))
            {
                string value = token.Value<string>();
                if (!string.IsNullOrWhiteSpace(value)) yield return value;
            }

            foreach (JToken token in root.SelectTokens("$.images[*].uri"))
            {
                string value = token.Value<string>();
                if (!string.IsNullOrWhiteSpace(value)) yield return value;
            }
        }

        private static bool TryNormalizeLocalUri(string uri, out string relativePath)
        {
            relativePath = null;
            if (string.IsNullOrWhiteSpace(uri)) return false;

            string normalized = uri.Trim().Replace("\\", "/");
            if (normalized.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return false;

            int queryIndex = normalized.IndexOfAny(new[] {'?', '#'});
            if (queryIndex >= 0) normalized = normalized.Substring(0, queryIndex);

            if (Uri.TryCreate(normalized, UriKind.Absolute, out Uri absoluteUri) && !string.IsNullOrEmpty(absoluteUri.Scheme))
            {
                return false;
            }

            string unescaped = Uri.UnescapeDataString(normalized);
            if (string.IsNullOrWhiteSpace(unescaped)) return false;
            if (Path.IsPathRooted(unescaped)) return false;

            relativePath = NormalizeIndexedPath(unescaped);
            return relativePath.Length > 0;
        }

        private static bool TryCombineRelativePath(string baseDirectory, string relativePath, out string result)
        {
            result = null;
            List<string> parts = new List<string>();

            AppendPathParts(parts, baseDirectory);

            string[] relativeParts = relativePath.Split(new[] {'/'}, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in relativeParts)
            {
                if (part == ".") continue;
                if (part == "..")
                {
                    if (parts.Count == 0) return false;
                    parts.RemoveAt(parts.Count - 1);
                    continue;
                }

                parts.Add(part);
            }

            if (parts.Count == 0) return false;

            result = string.Join("/", parts);
            return true;
        }

        private static void AppendPathParts(List<string> parts, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            string[] baseParts = path.Split(new[] {'/'}, StringSplitOptions.RemoveEmptyEntries);
            parts.AddRange(baseParts.Where(part => part != "."));
        }
    }
}
