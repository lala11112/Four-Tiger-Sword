using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

/// <summary>Font roles from the supplied BoundaryZERO font usage guide.</summary>
public static class BoundaryZeroTypography
{
    public enum Role { Body, KoreanTitle, Numeric }
    public const string Root = "Assets/_Project/Font/BoundaryZERO/";
    public const string FontPath = Root + "BoundaryFont.asset";
    public const string MaterialPath = Root + "BoundaryFont.mat";
    public static readonly string[] PrefabPaths = {
        "Assets/_Project/Prefabs/UI/PlayerHUD.prefab",
        "Assets/_Project/Prefabs/UI/DungeonMinimap.prefab",
        "Assets/_Project/Prefabs/UI/DungeonFullMap.prefab",
        "Assets/_Project/Prefabs/UI/HubGatePrompt.prefab",
        "Assets/_Project/Prefabs/UI/HubUpgradeMenu.prefab",
        "Assets/_Project/Prefabs/UI/HubWorkshop.prefab",
        "Assets/_Project/Scripts/UI/Finisher/Prefabs/FinisherWheel.prefab",
        "Assets/_Game/Shop/UI/GoldHUD.prefab",
        "Assets/_Game/Shop/UI/TemporaryShop.prefab",
        "Assets/_Game/Shop/UI/ShopOffer.prefab",
        "Assets/_Project/Prefabs/UI/BoundaryZeroRunOverlay.prefab"
    };

    private static string AssetName(Role role) => role == Role.KoreanTitle ? "TitleKorean"
        : role == Role.Numeric ? "TitleLatin" : "BoundaryFont";

    public static TMP_FontAsset GetFont(Role role = Role.Body)
    {
        string name = AssetName(role);
        string path = Root + name + ".asset";
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font != null) return font;
        string sourceName = role == Role.KoreanTitle ? "BlackHanSans-Regular.ttf"
            : role == Role.Numeric ? "ChakraPetch-Bold.ttf" : "Pretendard-Regular.otf";
        var source = AssetDatabase.LoadAssetAtPath<Font>(Root + "Fonts/" + sourceName);
        if (source == null) throw new InvalidOperationException("Missing supplied font: " + sourceName);
        bool numeric = role == Role.Numeric;
        font = TMP_FontAsset.CreateFontAsset(source, numeric ? 80 : 50, numeric ? 8 : 5,
            GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, !numeric);
        if (font == null) throw new InvalidOperationException("Could not create TMP font: " + sourceName);
        font.name = name;
        var face = font.faceInfo;
        face.scale = 1;
        font.faceInfo = face;
        var serialized = new SerializedObject(font);
        serialized.FindProperty("m_ClearDynamicDataOnBuild").boolValue = !numeric;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        // These two sources already contain their intended weight.
        if (role != Role.Body) font.boldStyle = 0;
        font.fallbackFontAssetTable = role == Role.KoreanTitle
            ? new List<TMP_FontAsset> { GetFont() } : new List<TMP_FontAsset>();
        var shader = Shader.Find("TextMeshPro/Distance Field");
        if (shader == null) throw new InvalidOperationException("TMP Distance Field shader is missing.");
        font.material.shader = shader;
        font.material.name = name + " SDF Material";
        font.material.SetFloat("_Sharpness", 0);
        font.material.SetFloat("_FaceDilate", 0);
        string ascii = new string(Enumerable.Range(32, 95).Select(i => (char)i).ToArray());
        font.TryAddCharacters(ascii);
        if (numeric) font.atlasPopulationMode = AtlasPopulationMode.Static;
        AssetDatabase.CreateAsset(font, path);
        SaveFontResources(font);
        return font;
    }

    public static Material GetMaterial(Role role = Role.Body)
    {
        var font = GetFont(role);
        string path = Root + AssetName(role) + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(font.material) { name = AssetName(role) };
            AssetDatabase.CreateAsset(material, path);
        }
        if (material.shader != font.material.shader || material.GetTexture("_MainTex") != font.atlasTexture
            || !Mathf.Approximately(material.GetFloat("_GradientScale"), font.material.GetFloat("_GradientScale")))
        {
            material.shader = font.material.shader;
            material.CopyPropertiesFromMaterial(font.material);
            material.shaderKeywords = font.material.shaderKeywords;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
        }
        return material;
    }

    public static bool Assign(TMP_Text label, Role role = Role.Body)
    {
        var font = GetFont(role);
        var material = GetMaterial(role);
        var style = role == Role.Body ? label.fontStyle : FontStyles.Normal;
        if (label.font == font && label.fontSharedMaterial == material && label.fontStyle == style) return false;
        label.font = font;
        label.fontSharedMaterial = material;
        label.fontStyle = style;
        EditorUtility.SetDirty(label);
        if (PrefabUtility.IsPartOfPrefabInstance(label)) PrefabUtility.RecordPrefabInstancePropertyModifications(label);
        return true;
    }

    [MenuItem("Tools/Studio Baekho/BoundaryZERO/Apply Supplied Fonts")]
    public static string Apply()
    {
        RequireEditMode();
        SeedInstalledContent();
        int count = 0;
        foreach (string path in PrefabPaths)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                count += ApplyRoot(root);
                if (PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                    throw new InvalidOperationException("Could not save font assignments: " + path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        foreach (string guid in AssetDatabase.FindAssets("t:FinisherWheelResources"))
        {
            var resources = AssetDatabase.LoadAssetAtPath<FinisherWheelResources>(AssetDatabase.GUIDToAssetPath(guid));
            if (resources.Font == GetFont()) continue;
            resources.Configure(GetFont());
            EditorUtility.SetDirty(resources);
            AssetDatabase.SaveAssetIfDirty(resources);
        }
        foreach (Role role in Enum.GetValues(typeof(Role))) SaveFontResources(GetFont(role));
        return "BoundaryZERO fonts assigned to " + count + " prefab labels; Pretendard body, Black Han Sans titles, Chakra Petch result values.";
    }

    public static string ApplyScene(string path)
    {
        RequireEditMode();
        if (!EditorBuildSettings.scenes.Any(s => s.enabled && s.path == path))
            throw new InvalidOperationException("Typography is limited to enabled project scenes: " + path);
        var scene = SceneManager.GetSceneByPath(path);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (!opened && scene.isDirty)
            throw new InvalidOperationException("Save the existing scene edits before applying typography: " + path);
        var active = SceneManager.GetActiveScene();
        if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            int count = 0;
            foreach (var root in scene.GetRootGameObjects()) count += ApplyRoot(root, true);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save " + path);
            foreach (Role role in Enum.GetValues(typeof(Role))) SaveFontResources(GetFont(role));
            return path + ": " + count + " label font overrides updated.";
        }
        finally
        {
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static int ApplyRoot(GameObject root, bool scene = false)
    {
        int count = 0;
        foreach (var label in root.GetComponentsInChildren<TMP_Text>(true))
        {
            // Combat/world-space labels are outside the supplied UI typography guide.
            if (scene && label.GetComponentInParent<Canvas>(true) == null) continue;
            bool menu = label.GetComponentInParent<TitleManager>(true) != null
                && label.transform.parent != null
                && new[] { "NewGame", "Continue", "Settings", "Quit" }.Contains(label.transform.parent.name);
            if (Assign(label, menu ? Role.KoreanTitle : Role.Body)) count++;
        }
        foreach (var overlay in root.GetComponentsInChildren<BoundaryZeroRunOverlay>(true))
        {
            overlay.ConfigureTypography(GetFont(), GetFont(Role.KoreanTitle), GetFont(Role.Numeric));
            EditorUtility.SetDirty(overlay);
            if (PrefabUtility.IsPartOfPrefabInstance(overlay)) PrefabUtility.RecordPrefabInstancePropertyModifications(overlay);
        }
        foreach (var result in root.GetComponentsInChildren<StageResultUI>(true))
        {
            var serialized = new SerializedObject(result);
            serialized.FindProperty("fontAsset").objectReferenceValue = GetFont();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (PrefabUtility.IsPartOfPrefabInstance(result)) PrefabUtility.RecordPrefabInstancePropertyModifications(result);
        }
        return count;
    }

    private static void SeedInstalledContent()
    {
        var characters = new SortedSet<char>();
        void Add(string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            foreach (char character in value) if (!char.IsControl(character)) characters.Add(character);
        }
        Add("구역 준비 중 탐사 완료 종료 실패 새 게임 이어하기 설정 보상 저장 불러오기 다시 시도 거점 귀환 골드 부족 구매 판매 합성 재료 추가 제외 체력 최대 보유 품절 강화석 스테이지 사망 전투 방 개 확인 필요");
        foreach (string path in PrefabPaths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException("Typography target is missing: " + path);
            foreach (var label in prefab.GetComponentsInChildren<TMP_Text>(true)) Add(label.text);
        }
        foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition"))
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (item != null) { Add(item.displayName); Add(item.description); }
        }
        GetFont().TryAddCharacters(new string(characters.ToArray()));
        GetFont(Role.KoreanTitle).TryAddCharacters("경계 새 게임 이어하기 설정 종료 경계 돌파 탐사 완료 탐사 종료");
        GetFont(Role.Numeric);
    }

    private static void SaveFontResources(TMP_FontAsset font)
    {
        if (!AssetDatabase.Contains(font.material)) AssetDatabase.AddObjectToAsset(font.material, font);
        EditorUtility.SetDirty(font.material);
        for (int i = 0; i < font.atlasTextures.Length; i++)
        {
            var atlas = font.atlasTextures[i];
            if (atlas == null) continue;
            atlas.name = font.name + " Atlas " + i;
            if (!AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas, font);
            EditorUtility.SetDirty(atlas);
        }
        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssetIfDirty(font);
    }

    private static void RequireEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Apply BoundaryZERO fonts in Edit Mode.");
    }
}
