using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PipeCableKit.Editor
{
    public static class PipeCableKitSetup
    {
        const string Root = "Assets/PipeCableKit";
        [Serializable] class Library { public MaterialEntry[] materials; public AssetEntry[] assets; }
        [Serializable] class MaterialEntry
        {
            public string name; public float[] color; public float metallic; public float roughness;
            public string basecolor_texture; public string normal_texture;
            public string metallic_smoothness_texture; public string hdrp_mask_texture;
        }
        // JsonUtility cannot deserialize nested arrays; endpoint positions follow the named shape contract.
        [Serializable] class AssetEntry { public string name; public float diameter_m; public string kind; public string shape; }

        [MenuItem("Tools/Pipe Cable Kit/Build Prefabs")]
        public static void BuildPrefabs()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            var pipelineName = pipeline ? pipeline.GetType().Name : "";
            BuildForShader(pipelineName.Contains("HDRender") ? "HDRP/Lit" : pipelineName.Contains("Universal") ? "Universal Render Pipeline/Lit" : "Standard");
        }

        public static void BuildForShader(string shaderName)
        {
            var jsonPath = Root + "/material_library.json";
            if (!File.Exists(jsonPath)) throw new FileNotFoundException("Pipe Cable Kit material_library.json is missing.", jsonPath);
            var library = JsonUtility.FromJson<Library>(File.ReadAllText(jsonPath));
            if (library == null || library.materials == null || library.assets == null)
                throw new InvalidDataException("Invalid Pipe Cable Kit material library.");
            Directory.CreateDirectory(Root + "/Materials");
            Directory.CreateDirectory(Root + "/Prefabs");
            AssetDatabase.Refresh();
            var shader = Shader.Find(shaderName);
            if (!shader) throw new InvalidOperationException("Required shader is unavailable: " + shaderName);
            var materials = new System.Collections.Generic.Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (var entry in library.materials)
            {
                if (string.IsNullOrEmpty(entry.name) || entry.color == null || entry.color.Length < 3)
                    throw new InvalidDataException("Invalid material entry.");
                var path = Root + "/Materials/" + entry.name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
                material.shader = shader;
                var color = new Color(entry.color[0], entry.color[1], entry.color[2], entry.color.Length > 3 ? entry.color[3] : 1f).gamma;
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                if (material.HasProperty("_Color")) material.SetColor("_Color", color);
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", Mathf.Clamp01(entry.metallic));
                var albedo = ImportTexture(entry.basecolor_texture, true, false);
                var normal = ImportTexture(entry.normal_texture, false, true);
                var packed = ImportTexture(shaderName == "HDRP/Lit" ? entry.hdrp_mask_texture : entry.metallic_smoothness_texture, false, false);
                SetTexture(material, "_BaseMap", albedo);
                SetTexture(material, "_MainTex", albedo);
                SetTexture(material, "_BaseColorMap", albedo);
                SetTexture(material, "_BumpMap", normal);
                SetTexture(material, "_NormalMap", normal);
                SetTexture(material, "_MetallicGlossMap", packed);
                SetTexture(material, "_MaskMap", packed);
                foreach (var keyword in new[] { "_NORMALMAP", "_NORMALMAP_TANGENT_SPACE", "_METALLICGLOSSMAP", "_METALLICSPECGLOSSMAP", "_MASKMAP" }) material.DisableKeyword(keyword);
                if (normal) material.EnableKeyword("_NORMALMAP");
                if (shaderName == "HDRP/Lit")
                {
                    if (normal) material.EnableKeyword("_NORMALMAP_TANGENT_SPACE");
                    if (packed) material.EnableKeyword("_MASKMAP");
                    SetFloat(material, "_NormalMapSpace", 0f);
                    SetFloat(material, "_SmoothnessRemapMin", 0f);
                    SetFloat(material, "_SmoothnessRemapMax", 1f);
                    SetFloat(material, "_AORemapMin", 0f);
                    SetFloat(material, "_AORemapMax", 1f);
                }
                else if (packed) material.EnableKeyword(shaderName == "Standard" ? "_METALLICGLOSSMAP" : "_METALLICSPECGLOSSMAP");
                SetFloat(material, "_SmoothnessTextureChannel", 0f);
                material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                SetFloat(material, "_BumpScale", 1f);
                SetFloat(material, "_NormalScale", 1f);
                SetFloat(material, "_GlossMapScale", packed ? 1f : 1f - Mathf.Clamp01(entry.roughness));
                var smoothness = packed ? 1f : 1f - Mathf.Clamp01(entry.roughness);
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
                if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
                EditorUtility.SetDirty(material);
                materials.Add(entry.name, material);
            }
            var count = 0;
            foreach (var entry in library.assets)
            {
                var modelPath = Root + "/Models/" + entry.name + ".fbx";
                var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
                if (!importer) throw new FileNotFoundException("FBX model is missing.", modelPath);
                importer.globalScale = 1f;
                importer.useFileScale = true;
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.isReadable = true;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.SaveAndReimport();
                // Use the actual FBX material slot identifiers, not renderer ordering.
                var imported = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                var slotNames = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                foreach (var renderer in imported.GetComponentsInChildren<Renderer>(true))
                    foreach (var slot in renderer.sharedMaterials)
                        if (slot) slotNames.Add(slot.name);
                foreach (var slotName in slotNames)
                {
                    Material mapped;
                    if (!materials.TryGetValue(slotName, out mapped))
                        throw new InvalidDataException("Unmapped FBX material slot: " + slotName + " in " + entry.name);
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), slotName), mapped);
                }
                importer.SaveAndReimport();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                var root = new GameObject(entry.name);
                try
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                    instance.transform.SetParent(root.transform, false);
                    instance.name = "Model";
                    // Unity's FBX handedness conversion imports the tube toward -X.
                    // Rotate the model, retaining a unit-scale prefab with sockets toward +X/+Y.
                    instance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    bool elbow = entry.name.IndexOf("Elbow", StringComparison.OrdinalIgnoreCase) >= 0;
                    var start = new GameObject("Start").transform;
                    start.SetParent(root.transform, false);
                    start.localRotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
                    var end = new GameObject("End").transform;
                    end.SetParent(root.transform, false);
                    end.localPosition = elbow ? new Vector3(1f, 1f, 0f) : Vector3.right;
                    end.localRotation = Quaternion.LookRotation(elbow ? Vector3.up : Vector3.right, elbow ? Vector3.forward : Vector3.up);
                    PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/" + entry.name + ".prefab");
                    count++;
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Pipe Cable Kit: built " + count + " prefabs using " + shaderName + ". Re-running updates kit materials and prefabs.");
        }

        static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        static void SetTexture(Material material, string property, Texture texture)
        {
            if (material.HasProperty(property)) material.SetTexture(property, texture);
        }

        static Texture2D ImportTexture(string relativePath, bool srgb, bool normal)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;
            var path = Root + "/" + relativePath.Replace('\\', '/');
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!importer) throw new FileNotFoundException("PBR texture is missing.", path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = srgb;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.maxTextureSize = 1024;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
