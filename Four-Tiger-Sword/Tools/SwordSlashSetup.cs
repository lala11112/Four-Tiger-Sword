using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class SwordSlashSetup
{
    public static string Run()
    {
        if (Application.isPlaying) throw new Exception("Edit Mode required.");
        const string folder = "Assets/_Project/SlashEffects";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/_Project", "SlashEffects");
        var shader = Shader.Find("FourTiger/SwordSlash");
        if (shader == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
            throw new Exception("Slash shader failed validation.");
        string matPath = folder + "/SwordSlash.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, matPath);
        }
        string[] names = { "Water", "Fire", "Wood", "Iron", "Earth" };
        Color[] colors = { new Color(.3f, 1.6f, 2.5f, .85f), new Color(2.5f, .65f, .1f, .85f),
            new Color(.4f, 2f, .65f, .85f), new Color(1.8f, 2f, 2.3f, .85f), new Color(2f, 1.4f, .3f, .85f) };
        int configured = 0;
        for (int i = 0; i < names.Length; i++)
        {
            string path = folder + "/" + names[i] + "Slash.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                var go = new GameObject(names[i] + "Slash");
                try
                {
                    go.AddComponent<SwordSlashVisual>().Tint = colors[i];
                    go.GetComponent<MeshRenderer>().sharedMaterial = material;
                    prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
            string dataName = names[i] == "Water" ? "WaterFromAction" : names[i] + "FormAction";
            var data = AssetDatabase.LoadAssetAtPath<WeaponActionDataSO>("Assets/_Project/Scripts/FormActions/" + dataName + ".asset");
            if (data == null) throw new Exception("Missing " + dataName);
            Undo.RecordObject(data, "Add sword slash effects");
            for (int j = 0; j < data.ComboSteps.Count; j++)
                if (Configure(data.ComboSteps[j], prefab, j)) configured++;
            if (data is WaterFormActionDataSO water && Configure(water.HeavyAttackStep, prefab, 0))
            {
                water.HeavyAttackStep.SlashVFXScale = 1.2f;
                configured++;
            }
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
        }
        return "Configured " + configured + " attack steps; shader validated; five slash prefabs created.";
    }

    static bool Configure(WeaponActionData step, GameObject prefab, int index)
    {
        if (step == null || step.SlashVFX != null) return false;
        step.SlashVFX = prefab;
        step.SlashVFXStartTime = -1f;
        step.SlashVFXOffset = Vector3.up;
        step.SlashVFXRotation = new Vector3(0f, 0f, index % 2 == 0 ? -18f : 18f);
        step.SlashVFXScale = 1f;
        step.SlashVFXLifetime = .22f;
        return true;
    }

    public static string Preview()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null;
        Texture2D image = null;
        var previous = RenderTexture.active;
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/SlashEffects/WaterSlash.prefab");
            var effect = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var visual = effect.GetComponent<SwordSlashVisual>();
            typeof(SwordSlashVisual).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(visual, null);
            var mesh = effect.GetComponent<MeshFilter>().sharedMesh;
            if (mesh.vertexCount != 147 || mesh.triangles.Length != 576) throw new Exception("Invalid slash geometry");
            var cameraGo = new GameObject("Slash preview camera");
            SceneManager.MoveGameObjectToScene(cameraGo, scene);
            var camera = cameraGo.AddComponent<Camera>();
            camera.scene = scene;
            camera.transform.position = new Vector3(0f, 4f, -4f);
            camera.transform.LookAt(new Vector3(0f, 0f, .7f));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.018f, .025f, .045f);
            camera.orthographic = true;
            camera.orthographicSize = 2.1f;
            target = new RenderTexture(768, 512, 24);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(768, 512, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 768, 512), 0, 0);
            image.Apply();
            string output = Path.GetFullPath("Temp/SwordSlashPreview.png");
            File.WriteAllBytes(output, image.EncodeToPNG());
            return output;
        }
        finally
        {
            RenderTexture.active = previous;
            if (target != null) UnityEngine.Object.DestroyImmediate(target);
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
