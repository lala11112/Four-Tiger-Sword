using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Imports the supplied health artwork and upgrades the shared monster bar in place.</summary>
public static class BoundaryZeroEnemyHealthSetup
{
    public const string AssetRoot = "Assets/_Project/Sprites/UI/BoundaryZERO/EnemyHealth";
    public const string MonsterPrefabPath = "Assets/_Project/Prefabs/HpBar.prefab";

    private static readonly string[] SourceNames =
    {
        "몬스터_바탕.png", "몬스터_체력.png", "몬스터_피해잔상.png", "몬스터_프레임.png",
        "보스_바탕.png", "보스_체력.png", "보스_피해잔상.png", "보스_프레임.png"
    };

    private static readonly string[] DestinationNames =
    {
        "monster_background.png", "monster_fill.png", "monster_trail.png", "monster_frame.png",
        "boss_background.png", "boss_fill.png", "boss_trail.png", "boss_frame.png"
    };

    public static string ImportAssets(string sourceDirectory)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Import enemy health artwork in Edit Mode.");
        if (string.IsNullOrWhiteSpace(sourceDirectory)) throw new ArgumentException("The health artwork source directory is required.", nameof(sourceDirectory));
        string sourceRoot = Path.Combine(Path.GetFullPath(sourceDirectory), "PNG");
        // Validate the eight explicit inputs before changing the project; no package code is loaded.
        foreach (string sourceName in SourceNames)
        {
            string source = Path.Combine(sourceRoot, sourceName);
            if (!File.Exists(source)) throw new FileNotFoundException("Missing enemy health artwork.", source);
        }

        EnsureFolder(AssetRoot);
        for (int i = 0; i < SourceNames.Length; i++)
        {
            string destination = AssetRoot + "/" + DestinationNames[i];
            string destinationFile = Path.Combine(Application.dataPath, destination.Substring("Assets/".Length));
            File.Copy(Path.Combine(sourceRoot, SourceNames[i]), destinationFile, true);
            AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(destination) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Could not import health artwork: " + destination);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 4096;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.sRGBTexture = true;
            importer.spritePixelsPerUnit = 100f;
            importer.spriteBorder = Vector4.zero;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spritePivot = new Vector2(.5f, .5f);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }
        return "Imported eight BoundaryZERO monster/boss health sprites.";
    }

    public static UnityEngine.Sprite Sprite(string fileName)
        => AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(AssetRoot + "/" + fileName)
            ?? throw new InvalidOperationException("Enemy health sprite is missing: " + fileName);

    public static string ApplyMonster()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Apply monster health artwork in Edit Mode.");
        // Resolve every required sprite before opening the existing shared prefab for editing.
        Sprite("monster_background.png");
        Sprite("monster_trail.png");
        Sprite("monster_fill.png");
        Sprite("monster_frame.png");
        var root = PrefabUtility.LoadPrefabContents(MonsterPrefabPath);
        try
        {
            var background = root.transform.Find("Canvas/HpBarBackground") as RectTransform;
            if (background == null) throw new InvalidOperationException("The shared monster health background is missing.");
            var fill = RequiredImage(background, "HpBar");
            var trail = RequiredImage(background, "HpBarTrail");
            var frame = RequiredImage(background, "HpBarFrame");
            var image = background.GetComponent<UnityEngine.UI.Image>();
            if (image == null) throw new InvalidOperationException("The shared monster health background has no Image.");

            const float scale = 2f / 256f;
            background.sizeDelta = new Vector2(256f, 56f) * scale;
            SetImage(image, "monster_background.png", false);
            PlaceFill(fill.rectTransform, scale);
            PlaceFill(trail.rectTransform, scale);
            SetImage(fill, "monster_fill.png", true);
            SetImage(trail, "monster_trail.png", true);
            var frameRect = frame.rectTransform;
            frameRect.anchorMin = frameRect.anchorMax = frameRect.pivot = new Vector2(.5f, .5f);
            frameRect.anchoredPosition = Vector2.zero;
            frameRect.sizeDelta = background.sizeDelta;
            frameRect.localScale = Vector3.one;
            frameRect.localRotation = Quaternion.identity;
            SetImage(frame, "monster_frame.png", false);

            // The supplied background already includes the empty track; retain the old object and IDs.
            var track = background.Find("HpBarTrack");
            if (track != null) track.gameObject.SetActive(false);
            trail.transform.SetAsLastSibling();
            fill.transform.SetAsLastSibling();
            frame.transform.SetAsLastSibling();
            if (PrefabUtility.SaveAsPrefabAsset(root, MonsterPrefabPath) == null)
                throw new InvalidOperationException("Could not save the shared monster health prefab.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return "BoundaryZERO monster artwork applied to the existing shared health bar.";
    }

    private static UnityEngine.UI.Image RequiredImage(Transform parent, string childName)
    {
        var child = parent.Find(childName);
        var image = child != null ? child.GetComponent<UnityEngine.UI.Image>() : null;
        return image ?? throw new InvalidOperationException("Existing monster health layer is missing: " + childName);
    }

    private static void PlaceFill(RectTransform rect, float scale)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(22f, -24f) * scale;
        rect.sizeDelta = new Vector2(218f, 10f) * scale;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static void SetImage(UnityEngine.UI.Image image, string fileName, bool filled)
    {
        image.sprite = Sprite(fileName);
        image.color = Color.white;
        image.enabled = true;
        image.gameObject.SetActive(true);
        image.raycastTarget = false;
        image.preserveAspect = false;
        image.useSpriteMesh = false;
        image.type = filled ? UnityEngine.UI.Image.Type.Filled : UnityEngine.UI.Image.Type.Simple;
        if (!filled) return;
        image.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
        image.fillOrigin = (int)UnityEngine.UI.Image.OriginHorizontal.Left;
        image.fillAmount = 1f;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
    }
}
