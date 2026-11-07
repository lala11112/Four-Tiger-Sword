using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>A scoped SDF bake of the existing Korean typeface, with room for small UI text filtering.</summary>
public static class BoundaryZeroTypography
{
    public const string MaterialPath = "Assets/_Project/Font/BoundaryZERO NotoSansKR.mat";
    public const string FontPath = "Assets/_Project/Font/BoundaryZERO NotoSansKR SDF.asset";
    private const string OriginalFontPath = "Assets/_Project/Font/NotoSansKR SDF.asset";
    private const string SourcePath = "Assets/_Project/Font/NotoSansKR-VariableFont_wght.ttf";

    public static TMP_FontAsset GetFont()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font != null) return font;
        var source = AssetDatabase.LoadAssetAtPath<Font>(SourcePath);
        var original = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OriginalFontPath);
        if (source == null || original == null) throw new InvalidOperationException("The existing NotoSansKR typeface is missing.");
        font = TMP_FontAsset.CreateFontAsset(source, 48, 5, GlyphRenderMode.SDFAA, 1024, 1024,
            AtlasPopulationMode.Dynamic, true);
        if (font == null) throw new InvalidOperationException("Could not bake the BoundaryZERO Korean font.");
        font.name = "BoundaryZERO NotoSansKR SDF";
        var settings = new SerializedObject(font);
        settings.FindProperty("m_ClearDynamicDataOnBuild").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();
        font.fallbackFontAssetTable = new List<TMP_FontAsset> { original };
        font.normalStyle = original.normalStyle;
        font.boldStyle = original.boldStyle;
        var shader = Shader.Find("TextMeshPro/Distance Field");
        if (shader == null) throw new InvalidOperationException("The existing TMP Distance Field shader is missing.");
        font.material.shader = shader;
        font.material.name = "BoundaryZERO NotoSansKR SDF Material";
        font.material.SetFloat("_Sharpness", 0);
        font.material.SetFloat("_FaceDilate", 0);
        var ascii = new StringBuilder();
        for (int i = 32; i <= 126; i++) ascii.Append((char)i);
        font.TryAddCharacters(ascii.ToString());
        AssetDatabase.CreateAsset(font, FontPath);
        SaveFontResources(font);
        return font;
    }

    public static Material GetMaterial()
    {
        var font = GetFont();
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(font.material) { name = "BoundaryZERO NotoSansKR" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        if (material.shader != font.material.shader || material.GetTexture("_MainTex") != font.atlasTexture
            || !Mathf.Approximately(material.GetFloat("_GradientScale"), font.material.GetFloat("_GradientScale"))
            || !Mathf.Approximately(material.GetFloat("_Sharpness"), 0)
            || !Mathf.Approximately(material.GetFloat("_FaceDilate"), 0))
        {
            material.shader = font.material.shader;
            material.CopyPropertiesFromMaterial(font.material);
            material.shaderKeywords = font.material.shaderKeywords;
            material.SetFloat("_Sharpness", 0);
            material.SetFloat("_FaceDilate", 0);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
        }
        return material;
    }

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Apply BoundaryZERO typography in Edit Mode.");
        var font = GetFont();
        SeedInstalledContent(font);
        var material = GetMaterial();
        int count = 0;
        count += ApplyPrefab("Assets/_Project/Prefabs/UI/PlayerHUD.prefab", material, "BoundaryZeroHUD");
        count += ApplyPrefab("Assets/_Project/Prefabs/UI/DungeonMinimap.prefab", material, "Panel");
        count += ApplyPrefab("Assets/_Project/Prefabs/UI/DungeonFullMap.prefab", material, "BoundaryZeroSharp");
        count += ApplyPrefab("Assets/_Project/Scripts/UI/Finisher/Prefabs/FinisherWheel.prefab", material, "BoundaryZeroContent");
        count += ApplyPrefab("Assets/_Game/Shop/UI/GoldHUD.prefab", material, "Gold");
        count += ApplyPrefab("Assets/_Game/Shop/UI/TemporaryShop.prefab", material,
            "ShopPanel/BoundaryModalContent/Window/BoundaryZERO", "InteractionPrompt/BoundaryLabel");
        count += ApplyPrefab("Assets/_Game/Shop/UI/ShopOffer.prefab", material, "BoundaryZERO");
        count += ApplyPrefab("Assets/_Project/Prefabs/UI/BoundaryZeroRunOverlay.prefab", material, string.Empty);
        SaveFontResources(font);
        return "BoundaryZERO typography: original Noto Sans KR typeface baked at 48pt/padding5; dynamic font and material assigned to " + count + " generated UI labels.";
    }

    private static int ApplyPrefab(string path, Material material, params string[] scopes)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            int count = 0;
            foreach (string scope in scopes)
            {
                var target = string.IsNullOrEmpty(scope) ? root.transform : root.transform.Find(scope);
                if (target == null) throw new InvalidOperationException("BoundaryZERO typography target is missing: " + path + " / " + scope);
                foreach (var label in target.GetComponentsInChildren<TMP_Text>(true))
                {
                    label.font = GetFont();
                    label.fontSharedMaterial = material;
                    EditorUtility.SetDirty(label);
                    count++;
                }
            }
            if (PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                throw new InvalidOperationException("Could not save BoundaryZERO typography: " + path);
            return count;
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void SeedInstalledContent(TMP_FontAsset font)
    {
        var characters = new SortedSet<char>();
        void Add(string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            foreach (char character in value) if (!char.IsControl(character)) characters.Add(character);
        }
        for (int i = 32; i <= 126; i++) characters.Add((char)i);
        Add("구역 준비 중 탐사 완료 종료 실패 새 탐사 시작 다시 시도 골드 부족 구매 판매 합성 재료 추가 제외 체력 최대 보유 품절");
        string[] prefabs = {
            "Assets/_Project/Prefabs/UI/PlayerHUD.prefab", "Assets/_Project/Prefabs/UI/DungeonMinimap.prefab",
            "Assets/_Project/Prefabs/UI/DungeonFullMap.prefab", "Assets/_Project/Scripts/UI/Finisher/Prefabs/FinisherWheel.prefab",
            "Assets/_Game/Shop/UI/GoldHUD.prefab", "Assets/_Game/Shop/UI/TemporaryShop.prefab",
            "Assets/_Game/Shop/UI/ShopOffer.prefab", "Assets/_Project/Prefabs/UI/BoundaryZeroRunOverlay.prefab"
        };
        foreach (string path in prefabs)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) foreach (var label in prefab.GetComponentsInChildren<TMP_Text>(true)) Add(label.text);
        }
        foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition"))
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (item != null) { Add(item.displayName); Add(item.description); }
        }
        font.TryAddCharacters(new string(new List<char>(characters).ToArray()));
    }

    private static void SaveFontResources(TMP_FontAsset font)
    {
        if (!AssetDatabase.Contains(font.material)) AssetDatabase.AddObjectToAsset(font.material, font);
        EditorUtility.SetDirty(font.material);
        for (int i = 0; i < font.atlasTextures.Length; i++)
        {
            var atlas = font.atlasTextures[i];
            if (atlas == null) continue;
            atlas.name = "BoundaryZERO NotoSansKR Atlas " + i;
            if (!AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas, font);
            EditorUtility.SetDirty(atlas);
        }
        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssetIfDirty(font);
    }
}
