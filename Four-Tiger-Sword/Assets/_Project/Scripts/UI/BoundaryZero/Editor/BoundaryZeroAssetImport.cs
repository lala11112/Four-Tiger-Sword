using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Applies the supplied export manifest without touching gameplay item sprites.</summary>
public static class BoundaryZeroAssetImport
{
    public const string AssetRoot = "Assets/_Project/Sprites/UI/BoundaryZERO";
    [Serializable] private sealed class Manifest { public Entry[] assets; }
    [Serializable] private sealed class Entry
    {
        public string path, group;
        public int width, height;
        public float[] border;
        public float pivotX, pivotY;
    }

    [MenuItem("Tools/Studio Baekho/BoundaryZERO/Import UI Textures")]
    public static string Apply() => ApplyGroup(null);

    public static string ApplyMain() => ApplyGroup("Main");

    private static string ApplyGroup(string group)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Import UI in Edit Mode.");
        AssetDatabase.Refresh();
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(AssetRoot + "/manifest.json"));
        int count = 0;
        foreach (var entry in manifest.assets)
        {
            if (entry.group == "Items" || (group != null && entry.group != group)) continue;
            string path = AssetRoot + "/" + entry.path.Substring(4);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing UI image: " + path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.sRGBTexture = true;
            importer.maxTextureSize = Mathf.NextPowerOfTwo(Mathf.Max(entry.width, entry.height));
            importer.spritePixelsPerUnit = 100f;
            importer.spriteBorder = entry.border != null && entry.border.Length == 4
                ? new Vector4(entry.border[0], entry.border[1], entry.border[2], entry.border[3]) : Vector4.zero;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(entry.pivotX, entry.pivotY);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            count++;
        }
        return "BoundaryZERO: imported " + count + " UI sprites; original item icons unchanged.";
    }
}
