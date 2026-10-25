var path = System.IO.File.ReadAllText("BaekhoMap18Import/generated-prefab-path.txt").Trim();
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
var preview = UnityEditor.AssetPreview.GetAssetPreview(prefab);
if (preview == null) return new { available = false };
var rt = UnityEngine.RenderTexture.GetTemporary(preview.width, preview.height, 0, UnityEngine.RenderTextureFormat.ARGB32);
var previous = UnityEngine.RenderTexture.active;
UnityEngine.Texture2D readable = null;
try {
    UnityEngine.Graphics.Blit(preview, rt);
    UnityEngine.RenderTexture.active = rt;
    readable = new UnityEngine.Texture2D(preview.width, preview.height, UnityEngine.TextureFormat.RGBA32, false);
    readable.ReadPixels(new UnityEngine.Rect(0, 0, preview.width, preview.height), 0, 0);
    readable.Apply();
    System.IO.File.WriteAllBytes("BaekhoMap18Import/prefab-preview.png", UnityEngine.ImageConversion.EncodeToPNG(readable));
    return new { available = true, path = "BaekhoMap18Import/prefab-preview.png" };
} finally {
    UnityEngine.RenderTexture.active = previous;
    UnityEngine.RenderTexture.ReleaseTemporary(rt);
    if (readable != null) UnityEngine.Object.DestroyImmediate(readable);
}

