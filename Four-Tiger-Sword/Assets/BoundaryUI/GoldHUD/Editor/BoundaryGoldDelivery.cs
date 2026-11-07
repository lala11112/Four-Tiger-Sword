using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BoundaryZeroUI.Editor
{
    public static class BoundaryGoldDelivery
    {
        const string Root = "Assets/BoundaryUI/GoldHUD";
        public const string ScenePath = Root + "/Samples/BoundaryGoldHUD_Demo.unity";
        public const string PrefabPath = Root + "/Prefabs/BoundaryCompleteUI_Gold.prefab";
        static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../../Deliverables/11_골드_HUD_획득로그/경계ZERO_디자인수정"));

        [MenuItem("Tools/Boundary ZERO/Gold HUD/Prepare Demo and Prefabs")]
        public static void Prepare()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/BoundaryUI/Resources/BoundaryUI/Fonts/BoundaryFont.asset");
            if (font == null) throw new InvalidOperationException("The adopted Boundary ZERO font is missing.");
            font.TryAddCharacters("골드 0123456789,+- /층구역탐색연속획득일시정지초기화GPBVF", out string missing);
            EditorUtility.SetDirty(font);
            BoundaryGoldWidgetBuilder.BuildWidgets();
            Directory.CreateDirectory(Root + "/Samples"); Directory.CreateDirectory(Root + "/Prefabs");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("UI Preview Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(11, 17, 25, 255);
            camera.orthographic = true; camera.allowMSAA = true;
            var boot = new GameObject("Boundary ZERO UI with Gold");
            boot.AddComponent<BoundaryUIController>();
            PrefabUtility.SaveAsPrefabAsset(boot, PrefabPath);
            boot.AddComponent<BoundaryGoldHudDemo>();
            EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
            Validate();
        }

        public static void Validate()
        {
            foreach (var resource in new[] { "GoldBalance", "GoldRewardFeed" })
            {
                var prefab = Resources.Load<GameObject>("BoundaryUI/GoldHUD/" + resource);
                if (!prefab) throw new Exception("Missing gold prefab: " + resource);
                foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0) throw new Exception("Missing script: " + t.name);
                if (prefab.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).Any(g => g.raycastTarget)) throw new Exception("Gold widget blocks input.");
                if (prefab.GetComponentInChildren<Canvas>(true)) throw new Exception("Standalone widget unexpectedly owns Canvas.");
            }
            var feed = Resources.Load<GameObject>("BoundaryUI/GoldHUD/GoldRewardFeed");
            if (feed.GetComponentsInChildren<ArpgHud.PC.PentaGoldRewardRow>(true).Length != 3) throw new Exception("Expected three pooled reward rows.");
            var integrated = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!integrated || !integrated.GetComponent<BoundaryUIController>() || integrated.GetComponent<BoundaryGoldHudDemo>()) throw new Exception("Production prefab must contain controller without sample simulation.");
            Directory.CreateDirectory(Path.Combine(Output, "Validation"));
            File.WriteAllText(Path.Combine(Output, "Validation/editor-validation.json"), "{\"success\":true,\"unityVersion\":\"" + Application.unityVersion + "\",\"reference\":\"Boundary ZERO adopted QHD HUD\",\"pooledRows\":3}");
            Debug.Log("BOUNDARY_GOLD_ASSETS_VALIDATED");
        }

        public static void BuildAndExport()
        {
            Prepare();
            Directory.CreateDirectory(Output);
            string folder = Path.Combine(Output, "Windows"); Directory.CreateDirectory(folder);
            var product = PlayerSettings.productName;
            int width = PlayerSettings.defaultScreenWidth, height = PlayerSettings.defaultScreenHeight;
            bool background = PlayerSettings.runInBackground;
            var mode = PlayerSettings.fullScreenMode;
            try
            {
                PlayerSettings.productName = "Boundary ZERO Gold HUD";
                PlayerSettings.defaultScreenWidth = 2560; PlayerSettings.defaultScreenHeight = 1440;
                PlayerSettings.runInBackground = true; PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { ScenePath }, target = BuildTarget.StandaloneWindows64,
                    locationPathName = Path.Combine(folder, "BoundaryZERO_GoldHUD.exe"), options = BuildOptions.None
                });
                if (report.summary.result != BuildResult.Succeeded) throw new Exception("Gold HUD build failed: " + report.summary.result);
                Debug.Log("BOUNDARY_GOLD_BUILD_SUCCEEDED " + report.summary.totalSize);
            }
            finally
            {
                PlayerSettings.productName = product;
                PlayerSettings.defaultScreenWidth = width; PlayerSettings.defaultScreenHeight = height;
                PlayerSettings.runInBackground = background; PlayerSettings.fullScreenMode = mode;
            }
            AssetDatabase.SaveAssets(); Export();
        }

        public static void Export()
        {
            Directory.CreateDirectory(Output);
            AssetDatabase.ExportPackage(new[] { "Assets/BoundaryUI", "Assets/UI/ARPGHUD/PC/Runtime", "Assets/TextMesh Pro/Resources", "Assets/TextMesh Pro/Shaders" },
                Path.Combine(Output, "BoundaryZERO_GoldHUD.unitypackage"), ExportPackageOptions.Recurse | ExportPackageOptions.IncludeDependencies);
            File.WriteAllLines(Path.Combine(Output, "package-assets.txt"), AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith("Assets/BoundaryUI/", StringComparison.Ordinal) || p.StartsWith("Assets/UI/ARPGHUD/PC/Runtime/", StringComparison.Ordinal) || p.StartsWith("Assets/TextMesh Pro/", StringComparison.Ordinal)).OrderBy(p => p));
            Debug.Log("BOUNDARY_GOLD_PACKAGE_EXPORTED");
        }
    }
}
