using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AssetInventory
{
    /// <summary>Finds TextMeshPro SDF materials that were changed to an SRP Lit shader and can safely restore the standard distance-field shader.</summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public sealed partial class ChangedTextMeshProMaterialsValidator : Validator
    {
        private const string BaseTmpShaderName = "TextMeshPro/Distance Field";
        private const string UrpAssetVersionTypeName = "UnityEditor.Rendering.Universal.AssetVersion";

        private static readonly HashSet<string> KnownConvertedShaderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Universal Render Pipeline/Simple Lit",
            "Universal Render Pipeline/Lit",
            "HDRP/Lit"
        };

        private static readonly HashSet<string> TmpKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "BEVEL_ON",
            "GLOW_ON",
            "MASK_HARD",
            "MASK_SOFT",
            "MASK_TEX",
            "OUTLINE_ON",
            "RATIOS_OFF",
            "UNDERLAY_INNER",
            "UNDERLAY_ON",
            "UNITY_UI_ALPHACLIP",
            "UNITY_UI_CLIP_RECT"
        };

        private readonly List<MaterialIssue> _issues = new List<MaterialIssue>();

        private sealed class MaterialPropertySnapshot
        {
            public readonly Dictionary<string, float> Floats = new Dictionary<string, float>(StringComparer.Ordinal);
            public readonly Dictionary<string, Color> Colors = new Dictionary<string, Color>(StringComparer.Ordinal);
            public readonly Dictionary<string, TexturePropertyValue> Textures = new Dictionary<string, TexturePropertyValue>(StringComparer.Ordinal);
        }

        private sealed class TexturePropertyValue
        {
            public Texture Texture;
            public Vector2 Scale = Vector2.one;
            public Vector2 Offset = Vector2.zero;
        }

        private sealed class MaterialIssue
        {
            public string Path;
            public long LocalId;
            public string MaterialName;
            public string ShaderName;

            public string DisplayText
            {
                get
                {
                    string materialSuffix = string.IsNullOrEmpty(MaterialName) ? string.Empty : $" [{MaterialName}]";
                    return $"{Path}{materialSuffix}: {ShaderName}";
                }
            }
        }

        public ChangedTextMeshProMaterialsValidator()
        {
            Type = ValidatorType.FileSystem;
            Speed = ValidatorSpeed.Slow;
            Name = "Changed TextMesh Pro Materials";
            Description = "Finds TMP SDF materials changed to an SRP Lit shader. Repair restores TextMeshPro/Distance Field; specialized overlay, surface, mobile, masking, or two-pass variants must be reassigned manually.";
            FixCaption = "Restore Base SDF";
        }

        /// <inheritdoc/>
        public override async Task Validate()
        {
            CurrentState = State.Scanning;
            _issues.Clear();
            FileIssues = new List<string>();

            List<string> candidatePaths = GatherCandidateAssetPaths();
            int progressId = MetaProgress.Start("Checking TextMesh Pro materials");
            try
            {
                for (int i = 0; i < candidatePaths.Count; i++)
                {
                    if (CancellationRequested) break;

                    string path = candidatePaths[i];
                    MetaProgress.Report(progressId, i + 1, candidatePaths.Count, path);
                    Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
                    for (int assetIndex = 0; assetIndex < assets.Length; assetIndex++)
                    {
                        Material material = assets[assetIndex] as Material;
                        if (material == null ||
                            !PipelineConverter.TryGetSerializedMaterialBlock(material, out string materialBlock) ||
                            !IsRepairCandidate(material, materialBlock) ||
                            !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string _, out long localId))
                        {
                            continue;
                        }

                        _issues.Add(new MaterialIssue
                        {
                            Path = path,
                            LocalId = localId,
                            MaterialName = material.name,
                            ShaderName = material.shader != null ? material.shader.name : "<missing>"
                        });
                    }

                    if (i > 0 && i % 50 == 0) await Task.Yield();
                }
            }
            finally
            {
                MetaProgress.Remove(progressId);
            }

            FileIssues = _issues.Select(issue => issue.DisplayText).ToList();
            CurrentState = State.Completed;
        }

        /// <inheritdoc/>
        public override async Task Fix()
        {
            CurrentState = State.Fixing;
            List<MaterialIssue> pendingIssues = _issues.ToList();
            HashSet<string> repairedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int progressId = MetaProgress.Start("Restoring TextMesh Pro materials");
            try
            {
                for (int i = 0; i < pendingIssues.Count; i++)
                {
                    if (CancellationRequested) break;

                    MaterialIssue issue = pendingIssues[i];
                    MetaProgress.Report(progressId, i + 1, pendingIssues.Count, issue.Path);
                    Material material = FindMaterial(issue.Path, issue.LocalId);
                    if (material != null && RepairMaterial(material))
                    {
                        repairedPaths.Add(issue.Path);
                    }

                    if (i > 0 && i % 20 == 0) await Task.Yield();
                }

                if (repairedPaths.Count > 0)
                {
                    AssetDatabase.SaveAssets();
                    foreach (string path in repairedPaths)
                    {
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    }
                }
            }
            finally
            {
                MetaProgress.Remove(progressId);
            }

            await Validate();
        }

        internal static bool IsRepairCandidate(Material material, string materialBlock)
        {
            if (material == null || material.shader == null) return false;
            if (PipelineConverter.IsTextMeshProShaderName(material.shader.name)) return false;
            if (!KnownConvertedShaderNames.Contains(material.shader.name)) return false;
            return PipelineConverter.HasSerializedTextMeshProSdfSignature(materialBlock);
        }

        internal static bool RepairMaterial(Material material)
        {
            if (material == null ||
                !PipelineConverter.TryGetSerializedMaterialBlock(material, out string materialBlock) ||
                !IsRepairCandidate(material, materialBlock))
            {
                return false;
            }

            Shader shader = Shader.Find(BaseTmpShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[Asset Inventory] Cannot repair TMP material '{AssetDatabase.GetAssetPath(material)}' because shader '{BaseTmpShaderName}' was not found.");
                return false;
            }

            HashSet<string> keywords = ReadTmpKeywords(materialBlock);
            string path = AssetDatabase.GetAssetPath(material);
            MaterialPropertySnapshot properties = ReadMaterialProperties(materialBlock, path);
            if (material.HasProperty("_BaseMap"))
            {
                Texture baseMap = material.GetTexture("_BaseMap");
                if (baseMap != null &&
                    (!properties.Textures.TryGetValue("_MainTex", out TexturePropertyValue mainTexture) ||
                        mainTexture.Texture == null))
                {
                    properties.Textures["_MainTex"] = new TexturePropertyValue
                    {
                        Texture = baseMap,
                        Scale = material.GetTextureScale("_BaseMap"),
                        Offset = material.GetTextureOffset("_BaseMap")
                    };
                }
            }

            Undo.RegisterCompleteObjectUndo(material, "Restore TextMesh Pro Material");
            material.shader = shader;
            RestoreMaterialProperties(material, properties);
            material.shaderKeywords = keywords.ToArray();
            material.renderQueue = -1;
            material.SetOverrideTag("RenderType", string.Empty);
            material.SetShaderPassEnabled("MOTIONVECTORS", true);
            material.SetShaderPassEnabled("DepthOnly", true);
            material.SetShaderPassEnabled("SHADOWCASTER", true);
            EditorUtility.SetDirty(material);

            RemoveUrpAssetVersionSubAssets(path);
            return true;
        }

        private static List<string> GatherCandidateAssetPaths()
        {
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddAssetPaths("t:Material", paths);
            AddAssetPaths("t:TMP_FontAsset", paths);
            return paths
                .Where(path => path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void AddAssetPaths(string filter, HashSet<string> paths)
        {
            string[] guids = AssetDatabase.FindAssets(filter);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrEmpty(path)) paths.Add(path);
            }
        }

        private static Material FindMaterial(string path, long localId)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < assets.Length; i++)
            {
                Material material = assets[i] as Material;
                if (material == null ||
                    !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string _, out long candidateLocalId))
                {
                    continue;
                }
                if (candidateLocalId == localId) return material;
            }
            return null;
        }

        private static HashSet<string> ReadTmpKeywords(string materialBlock)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            MatchCollection matches = Regex.Matches(
                materialBlock,
                @"^\s*-\s+([A-Z][A-Z0-9_]+)\s*$",
                RegexOptions.Multiline);
            for (int i = 0; i < matches.Count; i++)
            {
                string keyword = matches[i].Groups[1].Value;
                if (TmpKeywords.Contains(keyword)) result.Add(keyword);
            }
            return result;
        }

        private static MaterialPropertySnapshot ReadMaterialProperties(string materialBlock, string materialPath)
        {
            MaterialPropertySnapshot result = new MaterialPropertySnapshot();

            MatchCollection floatMatches = Regex.Matches(
                materialBlock,
                @"^[ \t]*-[ \t]+(_[A-Za-z0-9_]+):[ \t]*([-+0-9.eE]+)[ \t]*$",
                RegexOptions.Multiline);
            for (int i = 0; i < floatMatches.Count; i++)
            {
                if (float.TryParse(
                        floatMatches[i].Groups[2].Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float value))
                {
                    result.Floats[floatMatches[i].Groups[1].Value] = value;
                }
            }

            MatchCollection colorMatches = Regex.Matches(
                materialBlock,
                @"^[ \t]*-[ \t]+(_[A-Za-z0-9_]+):[ \t]*\{r:[ \t]*([^,}]+),[ \t]*g:[ \t]*([^,}]+),[ \t]*b:[ \t]*([^,}]+),[ \t]*a:[ \t]*([^,}]+)\}",
                RegexOptions.Multiline);
            for (int i = 0; i < colorMatches.Count; i++)
            {
                result.Colors[colorMatches[i].Groups[1].Value] = new Color(
                    ParseFloat(colorMatches[i].Groups[2].Value),
                    ParseFloat(colorMatches[i].Groups[3].Value),
                    ParseFloat(colorMatches[i].Groups[4].Value),
                    ParseFloat(colorMatches[i].Groups[5].Value));
            }

            MatchCollection textureMatches = Regex.Matches(
                materialBlock,
                @"^[ \t]*-[ \t]+(_[A-Za-z0-9_]+):[ \t]*\r?\n" +
                @"[ \t]*m_Texture:[ \t]*\{fileID:[ \t]*(-?\d+)" +
                @"(?:,[ \t]*guid:[ \t]*([a-fA-F0-9]{32}),\s*type:[ \t]*\d+)?\}[ \t]*\r?\n" +
                @"[ \t]*m_Scale:[ \t]*\{x:[ \t]*([^,}]+),[ \t]*y:[ \t]*([^,}]+)\}[ \t]*\r?\n" +
                @"[ \t]*m_Offset:[ \t]*\{x:[ \t]*([^,}]+),[ \t]*y:[ \t]*([^,}]+)\}",
                RegexOptions.Multiline);
            for (int i = 0; i < textureMatches.Count; i++)
            {
                long.TryParse(textureMatches[i].Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long fileId);
                string guid = textureMatches[i].Groups[3].Value;
                result.Textures[textureMatches[i].Groups[1].Value] = new TexturePropertyValue
                {
                    Texture = ResolveTexture(materialPath, guid, fileId),
                    Scale = new Vector2(
                        ParseFloat(textureMatches[i].Groups[4].Value, 1f),
                        ParseFloat(textureMatches[i].Groups[5].Value, 1f)),
                    Offset = new Vector2(
                        ParseFloat(textureMatches[i].Groups[6].Value),
                        ParseFloat(textureMatches[i].Groups[7].Value))
                };
            }
            if (!result.Textures.ContainsKey("_MainTex") &&
                result.Textures.TryGetValue("_BaseMap", out TexturePropertyValue baseMap))
            {
                result.Textures["_MainTex"] = baseMap;
            }

            return result;
        }

        private static void RestoreMaterialProperties(Material material, MaterialPropertySnapshot properties)
        {
            foreach (KeyValuePair<string, float> property in properties.Floats)
            {
                if (material.HasProperty(property.Key)) material.SetFloat(property.Key, property.Value);
            }
            foreach (KeyValuePair<string, Color> property in properties.Colors)
            {
                if (material.HasProperty(property.Key)) material.SetColor(property.Key, property.Value);
            }
            foreach (KeyValuePair<string, TexturePropertyValue> property in properties.Textures)
            {
                if (!material.HasProperty(property.Key)) continue;
                material.SetTexture(property.Key, property.Value.Texture);
                material.SetTextureScale(property.Key, property.Value.Scale);
                material.SetTextureOffset(property.Key, property.Value.Offset);
            }
        }

        private static Texture ResolveTexture(string materialPath, string guid, long localId)
        {
            if (localId == 0) return null;

            string texturePath = string.IsNullOrEmpty(guid)
                ? materialPath
                : AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(texturePath)) return null;

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(texturePath);
            for (int i = 0; i < assets.Length; i++)
            {
                Texture texture = assets[i] as Texture;
                if (texture == null ||
                    !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture, out string _, out long textureLocalId))
                {
                    continue;
                }
                if (textureLocalId == localId) return texture;
            }
            return AssetDatabase.LoadAssetAtPath<Texture>(texturePath);
        }

        private static float ParseFloat(string value, float fallback = 0f)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
                ? result
                : fallback;
        }

        private static void RemoveUrpAssetVersionSubAssets(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < assets.Length; i++)
            {
                Object asset = assets[i];
                if (asset == null || asset.GetType().FullName != UrpAssetVersionTypeName) continue;
                Undo.DestroyObjectImmediate(asset);
            }
        }
    }
}
