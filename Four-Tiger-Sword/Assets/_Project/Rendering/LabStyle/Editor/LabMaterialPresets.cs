using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FourTiger.Rendering.Editor
{
    public enum LabMaterialGroup
    {
        Character,
        Floor,
        Wall,
        PaintedSurface,
        Rubber,
        Metal,
        Prop,
        Emissive
    }

    /// <summary>
    /// Creates opt-in material copies. Property names and keyword families were checked
    /// against the project's installed All In 1 3D Shader 2.74 source.
    /// </summary>
    public static class LabMaterialPresets
    {
        private const string OutputFolder = "Assets/_Project/Rendering/LabStyle/Materials";
        private const string WorldTriplanarShaderPath = "Assets/Baekho/Shaders/BaekhoWorldTriplanarLit.shadergraph";

        /// <summary>
        /// Returns a new saved variant, or null for a shader/surface that needs manual review.
        /// Never changes the source or replaces an existing asset. The caller explicitly
        /// chooses which renderers or runtime source mappings receive the returned material.
        /// </summary>
        public static Material CreateVariant(Material source, string assetPath, LabMaterialGroup group)
        {
            if (source == null || source.shader == null)
                throw new ArgumentNullException(nameof(source));

            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath))
                throw new ArgumentException("The source must be a saved material asset.", nameof(source));

            assetPath = ValidateOutputPath(assetPath, sourcePath);
            bool allInOne = source.shader.name.StartsWith("AllIn13DShader/", StringComparison.Ordinal);
            bool urpLit = source.shader.name == "Universal Render Pipeline/Lit";
            bool worldTriplanar = AssetDatabase.GetAssetPath(source.shader) == WorldTriplanarShaderPath;
            if (!allInOne && !urpLit && !worldTriplanar)
                return Skip(source, "unsupported custom shader");

            if (IsSpecialSurface(source, allInOne))
                return Skip(source, "transparent, additive, or special depth surface");

            var variant = new Material(source) { name = Path.GetFileNameWithoutExtension(assetPath) };
            try
            {
                // Character designs and their existing ToonRamp, outlines, and face lighting
                // remain authored as-is. Shared scene lighting/grading supplies the common tone.
                if (group != LabMaterialGroup.Character)
                {
                    if (allInOne)
                        ApplyAllInOne(source, variant, group);
                    else
                        SetFloat(variant, "_Smoothness", Smoothness(group));

                    // Preserve the existing world-space mapping and Shader Graph shader.
                    if (worldTriplanar && group == LabMaterialGroup.Wall)
                        SetFloat(variant, "_Metallic", 0f);
                }

                EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
                AssetDatabase.CreateAsset(variant, assetPath);
                string sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);
                AssetDatabase.SetLabels(variant, new[]
                {
                    "LabStyle", "LabStyle_Group_" + group, "LabStyle_Source_" + sourceGuid
                });
                EditorUtility.SetDirty(variant);
                return variant;
            }
            catch
            {
                if (!AssetDatabase.Contains(variant))
                    UnityEngine.Object.DestroyImmediate(variant);
                throw;
            }
        }

        private static void ApplyAllInOne(Material source, Material variant, LabMaterialGroup group)
        {
            // Keep floors/walls smoothly lit so repeated planar geometry has no toon bands.
            bool classic = group == LabMaterialGroup.Floor || group == LabMaterialGroup.Wall;
            SelectKeyword(variant, "_LightModel", classic ? 1f : 2f,
                classic ? "_LIGHTMODEL_CLASSIC" : "_LIGHTMODEL_TOON",
                "_LIGHTMODEL_NONE", "_LIGHTMODEL_CLASSIC", "_LIGHTMODEL_TOON", "_LIGHTMODEL_TOONRAMP",
                "_LIGHTMODEL_HALFLAMBERT", "_LIGHTMODEL_FAKEGI", "_LIGHTMODEL_FASTLIGHTING");
            if (!classic)
            {
                SetFloat(variant, "_ToonCutoff", 0.35f);
                SetFloat(variant, "_ToonSmoothness", 0.35f);
            }

            SelectKeyword(variant, "_ShadingModel", 1f, "_SHADINGMODEL_PBR",
                "_SHADINGMODEL_BASIC", "_SHADINGMODEL_PBR");
            SelectKeyword(variant, "_SpecularModel", 1f, "_SPECULARMODEL_CLASSIC",
                "_SPECULARMODEL_NONE", "_SPECULARMODEL_CLASSIC", "_SPECULARMODEL_TOON",
                "_SPECULARMODEL_ANISOTROPIC", "_SPECULARMODEL_ANISOTROPICTOON");
            SetFloat(variant, "_SpecularAtten", group == LabMaterialGroup.Rubber ? 0.12f : 0.25f);
            SelectKeyword(variant, "_Reflections", 1f, "_REFLECTIONS_CLASSIC",
                "_REFLECTIONS_NONE", "_REFLECTIONS_CLASSIC", "_REFLECTIONS_TOON");
            SetFloat(variant, "_ReflectionsAtten", group == LabMaterialGroup.Metal ? 0.3f : 0.15f);
            SetFloat(variant, "_Smoothness", Smoothness(group));

            RecoverLegacySurfaceMaps(source, variant);

            // HDR color and masks are retained. Reduce only the additional multiplier on
            // existing active emitters; source brightness is not raised by this operation.
            if (source.IsKeywordEnabled("_EMISSION_ON") && source.HasProperty("_EmissionColor") &&
                source.GetColor("_EmissionColor").maxColorComponent > 0.0001f && source.HasProperty("_EmissionSelfGlow") &&
                source.GetFloat("_EmissionSelfGlow") > 1f)
            {
                SetFloat(variant, "_EmissionSelfGlow", Mathf.Min(1.5f, source.GetFloat("_EmissionSelfGlow")));
            }
        }

        private static void RecoverLegacySurfaceMaps(Material source, Material variant)
        {
            // Earlier shader switches left original URP map references serialized even
            // though their property names are no longer declared by All In 1. Read those
            // saved entries directly; do not guess a texture by file name or replace a
            // deliberately enabled All In 1 map.
            bool normalEnabled = source.IsKeywordEnabled("_NORMAL_MAP_ON");
            Texture activeNormal = source.HasProperty("_NormalMap") ? source.GetTexture("_NormalMap") : null;
            if (!normalEnabled && IsUnassignedOrVendorDefault(activeNormal) &&
                TrySavedTexture(source, "_BumpMap", out Texture normal, out Vector2 normalScale, out Vector2 normalOffset))
            {
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(normal)) as TextureImporter;
                if (importer != null && importer.textureType == TextureImporterType.NormalMap)
                {
                    SetTexture(variant, "_NormalMap", normal, normalScale, normalOffset);
                    SetFloat(variant, "_NormalMapEnabled", 1f);
                    variant.EnableKeyword("_NORMAL_MAP_ON");
                    if (TrySavedFloat(source, "_BumpScale", out float strength))
                        SetFloat(variant, "_NormalStrength", strength);
                }
            }

            Texture activeMetallic = source.HasProperty("_MetallicMap") ? source.GetTexture("_MetallicMap") : null;
            if (!source.IsKeywordEnabled("_METALLIC_MAP_ON") && IsUnassignedOrVendorDefault(activeMetallic) &&
                TrySavedTexture(source, "_MetallicGlossMap", out Texture metallic, out Vector2 metalScale, out Vector2 metalOffset))
            {
                // URP metallic workflow and All In 1 both use red for metallic and alpha
                // for smoothness. Confirm the original packed texture is imported as data.
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(metallic)) as TextureImporter;
                if (importer != null && !importer.sRGBTexture && importer.textureType == TextureImporterType.Default)
                {
                    SetTexture(variant, "_MetallicMap", metallic, metalScale, metalOffset);
                    SetFloat(variant, "_MetallicMapOn", 1f);
                    variant.EnableKeyword("_METALLIC_MAP_ON");
                }
            }
        }

        private static bool IsUnassignedOrVendorDefault(Texture texture)
        {
            if (texture == null) return true;
            string path = AssetDatabase.GetAssetPath(texture);
            return path.StartsWith("Assets/Plugins/AllIn13DShader/", StringComparison.Ordinal);
        }

        private static bool IsSpecialSurface(Material source, bool allInOne)
        {
            if (source.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent ||
                source.GetTag("RenderType", false) == "Transparent" ||
                source.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") ||
                source.IsKeywordEnabled("_ALLIN13D_SURFACE_TRANSPARENT") ||
                (source.HasProperty("_Surface") && source.GetFloat("_Surface") > 0.5f))
                return true;

            if (!allInOne) return false;
            return (source.HasProperty("_RenderPreset") && source.GetFloat("_RenderPreset") != 1f) ||
                   (source.HasProperty("_BlendDst") && source.GetFloat("_BlendDst") != 0f) ||
                   (source.HasProperty("_ZWrite") && source.GetFloat("_ZWrite") < 0.5f);
        }

        private static float Smoothness(LabMaterialGroup group)
        {
            switch (group)
            {
                case LabMaterialGroup.Floor: return 0.22f;
                case LabMaterialGroup.Wall: return 0.22f;
                case LabMaterialGroup.Rubber: return 0.18f;
                case LabMaterialGroup.Metal: return 0.42f;
                case LabMaterialGroup.Emissive: return 0.3f;
                default: return 0.28f;
            }
        }

        private static void SelectKeyword(Material material, string property, float value, string selected, params string[] family)
        {
            SetFloat(material, property, value);
            foreach (string keyword in family) material.DisableKeyword(keyword);
            material.EnableKeyword(selected);
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        private static void SetTexture(Material material, string property, Texture texture, Vector2 scale, Vector2 offset)
        {
            if (!material.HasProperty(property)) return;
            material.SetTexture(property, texture);
            material.SetTextureScale(property, scale);
            material.SetTextureOffset(property, offset);
        }

        private static bool TrySavedTexture(Material source, string name, out Texture texture, out Vector2 scale, out Vector2 offset)
        {
            texture = null;
            scale = Vector2.one;
            offset = Vector2.zero;
            using (var serialized = new SerializedObject(source))
            {
                SerializedProperty entries = serialized.FindProperty("m_SavedProperties.m_TexEnvs");
                if (entries == null) return false;
                for (int i = 0; i < entries.arraySize; i++)
                {
                    SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                    if (entry.FindPropertyRelative("first").stringValue != name) continue;
                    SerializedProperty data = entry.FindPropertyRelative("second");
                    texture = data.FindPropertyRelative("m_Texture").objectReferenceValue as Texture;
                    scale = data.FindPropertyRelative("m_Scale").vector2Value;
                    offset = data.FindPropertyRelative("m_Offset").vector2Value;
                    return texture != null;
                }
            }
            return false;
        }

        private static bool TrySavedFloat(Material source, string name, out float value)
        {
            value = 0f;
            using (var serialized = new SerializedObject(source))
            {
                SerializedProperty entries = serialized.FindProperty("m_SavedProperties.m_Floats");
                if (entries == null) return false;
                for (int i = 0; i < entries.arraySize; i++)
                {
                    SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                    if (entry.FindPropertyRelative("first").stringValue != name) continue;
                    value = entry.FindPropertyRelative("second").floatValue;
                    return true;
                }
            }
            return false;
        }

        private static Material Skip(Material source, string reason)
        {
            Debug.LogWarning("Lab Style skipped " + AssetDatabase.GetAssetPath(source) + ": " + reason + ".", source);
            return null;
        }

        private static string ValidateOutputPath(string path, string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("An output asset path is required.", nameof(path));
            path = path.Replace('\\', '/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
                !path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("/../") || path.Contains("/./"))
                throw new ArgumentException("Use a project-relative Assets/.../*.mat path.", nameof(path));
            if (string.Equals(path, sourcePath, StringComparison.OrdinalIgnoreCase) ||
                AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(path))
                throw new IOException("Lab Style never overwrites an existing asset: " + path);
            return path;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void CreateSelected(LabMaterialGroup group)
        {
            foreach (UnityEngine.Object selected in Selection.objects)
            {
                if (!(selected is Material source)) continue;
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
                if (string.IsNullOrEmpty(guid))
                {
                    Debug.LogWarning("Select saved material assets to create Lab Style variants.", source);
                    continue;
                }
                string name = source.name;
                foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
                string path = AssetDatabase.GenerateUniqueAssetPath(OutputFolder + "/" + name + "_" + guid.Substring(0, 8) + "_" + group + ".mat");
                Material created = CreateVariant(source, path, group);
                if (created != null) AssetDatabase.SaveAssetIfDirty(created);
            }
        }

        [MenuItem("Assets/Four Tiger/Rendering/Create Lab Style Variant/Metal")]
        private static void SelectedMetal() { CreateSelected(LabMaterialGroup.Metal); }
        [MenuItem("Assets/Four Tiger/Rendering/Create Lab Style Variant/Painted Surface")]
        private static void SelectedPaint() { CreateSelected(LabMaterialGroup.PaintedSurface); }
        [MenuItem("Assets/Four Tiger/Rendering/Create Lab Style Variant/Rubber")]
        private static void SelectedRubber() { CreateSelected(LabMaterialGroup.Rubber); }
        [MenuItem("Assets/Four Tiger/Rendering/Create Lab Style Variant/Floor")]
        private static void SelectedFloor() { CreateSelected(LabMaterialGroup.Floor); }
        [MenuItem("Assets/Four Tiger/Rendering/Create Lab Style Variant/Wall")]
        private static void SelectedWall() { CreateSelected(LabMaterialGroup.Wall); }
        [MenuItem("Assets/Four Tiger/Rendering/Create Lab Style Variant/Emissive")]
        private static void SelectedEmissive() { CreateSelected(LabMaterialGroup.Emissive); }
        [MenuItem("Assets/Four Tiger/Rendering/Create Lab Style Variant/Prop")]
        private static void SelectedProp() { CreateSelected(LabMaterialGroup.Prop); }
    }
}
