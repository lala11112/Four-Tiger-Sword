// Optional adapter: copy to Assets/Baekho/Editor only when Unity MCP is installed.
using System;
using System.IO;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace StudioBaekho
{
    [McpForUnityTool("baekho_get_asset_catalog", Description = "Read explicitly marked BaekhoMapAsset prefab roots and enabled BaekhoPropSet prefab lists under Assets without changing source prefabs or open scenes.")]
    public static class BaekhoGetAssetCatalogTool
    {
        public sealed class Parameters
        {
            [ToolParameter("Absolute Unity project root expected by the caller; required before asset access")]
            public string expectedProjectRoot { get; set; }
        }

        public static object HandleCommand(JObject parameters)
        {
            try
            {
                JToken expected = parameters == null ? null : parameters["expectedProjectRoot"];
                if (expected == null || expected.Type != JTokenType.String || string.IsNullOrWhiteSpace(expected.Value<string>()) || !Path.IsPathRooted(expected.Value<string>()))
                    throw new InvalidDataException("expectedProjectRoot must be an absolute Unity project path.");
                string actualRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string expectedRoot = Path.GetFullPath(expected.Value<string>()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                StringComparison comparison = Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!string.Equals(actualRoot, expectedRoot, comparison))
                    throw new InvalidOperationException("Unity project mismatch. Expected " + expectedRoot + "; connected to " + actualRoot + ".");
                // Match the bridge's canonical project identity; reject ambiguous linked roots.
                for (string current = actualRoot; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                {
                    if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Symbolic links and junctions are not allowed in the Unity project path: " + current);
                }
                JObject catalog = JObject.Parse(BaekhoAssetCatalog.CreateJson());
                return new SuccessResponse("Prefab registrations and prop set catalog read.", catalog);
            }
            catch (Exception exception) { return new ErrorResponse(exception.Message); }
        }
    }
}
