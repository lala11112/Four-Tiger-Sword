using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Penta.Upgrade.Samples;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace Penta.Upgrade.Editor
{
    public static class PentaUpgradeDelivery
    {
        public const string Root = "Assets/UI/PentaUpgrade";
        public const string PrefabPath = Root + "/Prefabs/PENTA_Upgrade.prefab";
        public const string PanelPrefabPath = Root + "/Prefabs/BoundaryZERO_UpgradePanel.prefab";
        public const string ScenePath = Root + "/Samples/PENTA_Upgrade_Demo.unity";
        const string FontPath = Root + "/Art/BoundaryUpgradeKorean.asset";
        const string FontSource = Root + "/Art/Pretendard-Regular.otf";
        const string SealPath = Root + "/Art/Seal.png";
        const string BackgroundPath = Root + "/Samples/boundary-hud-reference.png";

        public static bool IsBuilt => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null
            && AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath) != null
            && AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null;

        [MenuItem("Tools/PENTA Upgrade/Create or rebuild assets and demo")]
        public static void Prepare()
        {
            // Batch startup can contain one empty, unsaved scene. Unity refuses
            // additive scene creation until it has a path. Never save or replace
            // an interactive user's untitled scene or any scene with objects.
            string batchAnchor = null;
            var initial = SceneManager.GetActiveScene();
            if (Application.isBatchMode && SceneManager.sceneCount == 1
                && initial.IsValid() && string.IsNullOrEmpty(initial.path)
                && initial.rootCount == 0)
            {
                Directory.CreateDirectory(Root + "/Samples");
                AssetDatabase.Refresh();
                batchAnchor = AssetDatabase.GenerateUniqueAssetPath(Root + "/Samples/PENTA_Upgrade_BatchAnchor.unity");
                if (!EditorSceneManager.SaveScene(initial, batchAnchor))
                    throw new InvalidOperationException("Could not prepare the empty batch startup scene.");
            }
            try { PrepareAssets(); }
            finally
            {
                if (batchAnchor != null)
                {
                    // Restore the original empty, untitled startup state before
                    // deleting only the temporary scene created by this call.
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    AssetDatabase.DeleteAsset(batchAnchor);
                }
            }
        }

        static void PrepareAssets()
        {
            var loadedDemo = SceneManager.GetSceneByPath(ScenePath);
            if (loadedDemo.IsValid() && loadedDemo.isLoaded)
                throw new InvalidOperationException("Close the upgrade demo scene before rebuilding its generated assets.");
            foreach (var directory in new[] { "/Art", "/Prefabs", "/Samples" }) Directory.CreateDirectory(Root + directory);
            AssetDatabase.Refresh();
            var font = PrepareFont();
            var seal = PrepareSeal();
            PrepareBackground();
            var previous = SceneManager.GetActiveScene();
            var temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(temporary);
                var go = CanvasObject("PENTA_Upgrade", 300);
                var view = go.AddComponent<PentaUpgradeView>();
                view.Build(font, seal);
                view.Select("origin");
                view.Open();
                PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
                var panel = new GameObject("BoundaryZERO_UpgradePanel", typeof(RectTransform));
                panel.layer = 5;
                Stretch((RectTransform)panel.transform);
                var panelView = panel.AddComponent<PentaUpgradeView>();
                panelView.Build(font, seal);
                panelView.Select("origin");
                panelView.Open();
                PrefabUtility.SaveAsPrefabAsset(panel, PanelPrefabPath);
            }
            finally
            {
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(temporary, true);
            }
            CreateDemo();
            AssetDatabase.SaveAssets();
        }

        static Sprite PrepareSeal()
        {
            var importer = AssetImporter.GetAtPath(SealPath) as TextureImporter;
            if (!importer) throw new InvalidOperationException("Boundary seal missing: " + SealPath);
            if (importer.textureType != TextureImporterType.Sprite || !importer.alphaIsTransparency
                || importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            var seal = AssetDatabase.LoadAssetAtPath<Sprite>(SealPath);
            if (!seal) throw new InvalidOperationException("Boundary seal sprite import failed: " + SealPath);
            return seal;
        }

        static void PrepareBackground()
        {
            var importer = AssetImporter.GetAtPath(BackgroundPath) as TextureImporter;
            if (!importer) throw new InvalidOperationException("Demo background missing: " + BackgroundPath);
            if (importer.npotScale != TextureImporterNPOTScale.None || importer.maxTextureSize != 3896
                || importer.mipmapEnabled || importer.wrapMode != TextureWrapMode.Clamp
                || importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 4096;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }

        static string RequiredCharacters()
        {
            var characters = new HashSet<char>();
            foreach (string file in Directory.GetFiles(Root, "*.cs", SearchOption.AllDirectories))
                foreach (char character in File.ReadAllText(file))
                    if (!char.IsControl(character)) characters.Add(character);
            foreach (var node in PentaUpgradeCatalog.Nodes)
                foreach (char character in node.Name + node.Description + node.Family) characters.Add(character);
            return new string(characters.OrderBy(x => x).ToArray());
        }

        static TMP_FontAsset PrepareFont()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (!font)
            {
                var source = AssetDatabase.LoadAssetAtPath<Font>(FontSource);
                if (!source) throw new InvalidOperationException("Korean font source missing: " + FontSource);
                font = TMP_FontAsset.CreateFontAsset(source, 48, 6, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                font.name = "BoundaryUpgradeKorean";
                AssetDatabase.CreateAsset(font, FontPath);
                if (font.material) AssetDatabase.AddObjectToAsset(font.material, font);
            }
            font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            font.isMultiAtlasTexturesEnabled = true;
            // Prewarm all UI source strings, including every concept description.
            font.TryAddCharacters(RequiredCharacters());
            foreach (var atlas in font.atlasTextures)
                if (atlas && !AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas, font);
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
            return font;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        static GameObject CanvasObject(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.layer = 5;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.matchWidthOrHeight = .5f;
            return go;
        }

        static void CreateDemo()
        {
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var background = CanvasObject("Sample Gameplay Background", -10);
                var backing = new GameObject("Navy Backing", typeof(RectTransform), typeof(Image));
                backing.transform.SetParent(background.transform, false);
                var backingRect = (RectTransform)backing.transform;
                backingRect.anchorMin = Vector2.zero;
                backingRect.anchorMax = Vector2.one;
                backingRect.offsetMin = backingRect.offsetMax = Vector2.zero;
                backing.GetComponent<Image>().color = new Color32(7, 13, 23, 255);
                backing.GetComponent<Image>().raycastTarget = false;
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(BackgroundPath);
                if (!texture) throw new InvalidOperationException("Demo background missing: " + BackgroundPath);
                var image = new GameObject("Gameplay Reference", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
                image.transform.SetParent(background.transform, false);
                var rect = (RectTransform)image.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                image.GetComponent<RawImage>().texture = texture;
                image.GetComponent<RawImage>().raycastTarget = false;
                var fit = image.GetComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fit.aspectRatio = (float)texture.width / texture.height;
                var panelCanvas = CanvasObject("Upgrade Panel Host Canvas", 300);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath), panelCanvas.transform);
                Stretch((RectTransform)instance.transform);
                instance.transform.SetAsLastSibling();
                var host = new GameObject("PENTA Upgrade Demo");
                host.AddComponent<PentaUpgradeDemo>().View = instance.GetComponent<PentaUpgradeView>();
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [MenuItem("Tools/PENTA Upgrade/Add to current scene")]
        public static void AddToCurrentScene()
        {
            if (!IsBuilt) Prepare();
            var scene = SceneManager.GetActiveScene();
            if (scene.GetRootGameObjects().Any(x => x.GetComponentInChildren<PentaUpgradeView>(true))) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
            Undo.RegisterCreatedObjectUndo(go, "Add PENTA Upgrade");
            Selection.activeGameObject = go;
        }

        static Canvas SelectedCanvas()
        {
            var selected = Selection.activeGameObject;
            return selected && selected.scene.IsValid() ? selected.GetComponentInParent<Canvas>(true) : null;
        }

        [MenuItem("Tools/PENTA Upgrade/Add panel to selected Canvas")]
        public static void AddPanelToSelectedCanvas()
        {
            var canvas = SelectedCanvas();
            if (!canvas) throw new InvalidOperationException("Select a scene Canvas or an object inside it first.");
            if (!IsBuilt) Prepare();
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath), canvas.transform);
            Undo.RegisterCreatedObjectUndo(go, "Add BoundaryZERO Upgrade Panel");
            Stretch((RectTransform)go.transform);
            go.transform.SetAsLastSibling();
            PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
            Selection.activeGameObject = go;
        }

        [MenuItem("Tools/PENTA Upgrade/Add panel to selected Canvas", true)]
        static bool CanAddPanelToSelectedCanvas() => SelectedCanvas() != null;

        [MenuItem("Tools/PENTA Upgrade/Open demo")]
        public static void OpenDemo()
        {
            if (!IsBuilt) Prepare();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("Tools/PENTA Upgrade/Validate assets")]
        public static void ValidateImported()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab || !prefab.GetComponent<PentaUpgradeView>() || !prefab.GetComponent<PentaUpgradeView>().IsBuilt)
                throw new InvalidOperationException("Upgrade prefab is incomplete.");
            if (prefab.GetComponentsInChildren<MonoBehaviour>(true).Any(x => x == null))
                throw new InvalidOperationException("Upgrade prefab contains missing scripts.");
            if (prefab.GetComponentInChildren<EventSystem>(true) || prefab.GetComponentInChildren<PentaUpgradeDemo>(true))
                throw new InvalidOperationException("Production prefab contains demo-only components.");
            ValidateReferences(prefab);
            ValidateDraftLabels(prefab);
            var panel = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
            if (!panel || !panel.GetComponent<PentaUpgradeView>() || !panel.GetComponent<PentaUpgradeView>().IsBuilt)
                throw new InvalidOperationException("Upgrade panel prefab is incomplete.");
            ValidateReferences(panel);
            ValidateDraftLabels(panel);
            if (panel.GetComponentInChildren<Canvas>(true) || panel.GetComponentInChildren<UnityEngine.UI.CanvasScaler>(true)
                || panel.GetComponentInChildren<UnityEngine.UI.GraphicRaycaster>(true) || panel.GetComponentInChildren<EventSystem>(true)
                || panel.GetComponentInChildren<PentaUpgradeDemo>(true))
                throw new InvalidOperationException("Panel must use the host Canvas and EventSystem without nested canvas components.");
            var panelRect = panel.transform as RectTransform;
            if (!panelRect || panelRect.anchorMin != Vector2.zero || panelRect.anchorMax != Vector2.one
                || panelRect.offsetMin != Vector2.zero || panelRect.offsetMax != Vector2.zero || panel.layer != 5)
                throw new InvalidOperationException("Panel root must stretch to the host Canvas on the UI layer.");
            if (!panel.GetComponentInChildren<PentaUpgradePanelFit>(true))
                throw new InvalidOperationException("Panel responsive fit component is missing.");
            var canvas = prefab.GetComponent<Canvas>();
            if (!canvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay || canvas.sortingOrder < 200)
                throw new InvalidOperationException("Expected high-order screen overlay canvas.");
            if (PentaUpgradeCatalog.Nodes.Count != 38) throw new InvalidOperationException("Expected 38 nodes: free origin and 37 upgrades.");
            int[] counts = { 1, 4, 15, 13, 5 };
            for (int tier = 0; tier < counts.Length; tier++)
                if (PentaUpgradeCatalog.Nodes.Count(x => x.Tier == tier) != counts[tier])
                    throw new InvalidOperationException("Unexpected concept tier layout.");
            if (PentaUpgradeCatalog.Nodes.Sum(x => x.ParentIds.Count) != 37)
                throw new InvalidOperationException("Expected 37 simple tree connections.");
            ValidateConceptGraph();
            ValidatePrefabGraph(prefab);
            ValidatePrefabGraph(panel);
            foreach (var node in PentaUpgradeCatalog.Nodes)
            {
                int expectedParents = node.Id == "origin" ? 0 : 1;
                if (node.ParentIds.Count != expectedParents)
                    throw new InvalidOperationException("Expected one clear parent per non-origin node: " + node.Id);
                foreach (string parentId in node.ParentIds)
                    if (PentaUpgradeCatalog.Find(parentId) == null) throw new InvalidOperationException("Missing parent: " + parentId);
            }
            if (prefab.GetComponentsInChildren<Transform>(true).Any(x => x.name == "ChildrenHeading"
                || x.name == "Child0" || x.name == "Child1" || x.name == "Child2" || x.name == "Child3"))
                throw new InvalidOperationException("Removed successor navigation remains in upgrade prefab.");
            if (prefab.GetComponentsInChildren<TMP_Text>(true).Any(x => x.text.Contains("이어지는 각인")))
                throw new InvalidOperationException("Removed successor heading remains in upgrade prefab.");
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font) font.ReadFontAssetDefinition();
            string content = string.Concat(PentaUpgradeCatalog.Nodes.Select(x => x.Name + x.Description + x.Family));
            if (!font || !font.HasCharacters(content, out uint[] missing, true, true))
                throw new InvalidOperationException("Upgrade Korean font glyph validation failed.");
            if (!AssetDatabase.LoadAssetAtPath<Sprite>(SealPath)) throw new InvalidOperationException("Boundary seal sprite missing.");
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)) throw new InvalidOperationException("Upgrade demo missing.");
        }

        static Dictionary<string, string> ExpectedParents() => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "atk", "origin" }, { "hp", "origin" }, { "footwork", "origin" }, { "gold", "origin" },
            { "crit", "atk" }, { "bossdmg", "atk" }, { "heal", "hp" }, { "guard", "hp" }, { "bossguard", "hp" },
            { "counter", "footwork" }, { "pursuit", "footwork" }, { "firstblood", "gold" }, { "discount", "gold" },
            { "sell", "gold" }, { "reroll", "origin" }, { "severe", "crit" }, { "pity", "crit" },
            { "shield", "heal" }, { "revive", "guard" }, { "refresh", "discount" },
            { "speed", "atk" }, { "reach", "speed" }, { "opener", "reach" }, { "combo", "speed" },
            { "finisher", "combo" }, { "critheal", "severe" }, { "armor", "hp" }, { "crowdguard", "armor" },
            { "roomheal", "heal" }, { "escape", "footwork" }, { "backstab", "counter" }, { "haste", "pursuit" },
            { "choices", "origin" }, { "hold", "reroll" },
            { "synergy", "choices" }, { "fusion", "synergy" }, { "stock", "refresh" }
        };

        static void ValidateConceptGraph()
        {
            var expected = ExpectedParents();
            var nodes = PentaUpgradeCatalog.Nodes;
            if (nodes.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != nodes.Count)
                throw new InvalidOperationException("Concept node IDs must be unique.");
            var origin = PentaUpgradeCatalog.Find("origin");
            if (origin == null || origin.Tier != 0 || origin.ParentIds.Count != 0)
                throw new InvalidOperationException("Free origin must be the only root.");
            foreach (var node in nodes)
            {
                if (node.Id == "origin") continue;
                if (!expected.TryGetValue(node.Id, out string expectedParent) || node.ParentIds.Count != 1
                    || node.ParentIds[0] != expectedParent)
                    throw new InvalidOperationException("Unexpected concept branch or parent: " + node.Id);
                var parent = PentaUpgradeCatalog.Find(expectedParent);
                if (parent == null || parent.Tier != node.Tier - 1 && !(parent.Id == "origin" && node.Tier == 2))
                    throw new InvalidOperationException("Invalid tier for concept parent: " + node.Id);
                var visited = new HashSet<string>(StringComparer.Ordinal);
                var cursor = node;
                while (cursor.Id != "origin")
                {
                    if (!visited.Add(cursor.Id)) throw new InvalidOperationException("Concept cycle: " + node.Id);
                    if (cursor.ParentIds.Count != 1 || (cursor = PentaUpgradeCatalog.Find(cursor.ParentIds[0])) == null)
                        throw new InvalidOperationException("Concept is unreachable from the origin: " + node.Id);
                }
            }
        }

        static void ValidatePrefabGraph(GameObject prefab)
        {
            var view = prefab.GetComponent<PentaUpgradeView>();
            var nodeViews = new SerializedObject(view).FindProperty("nodeViews");
            if (nodeViews == null || nodeViews.arraySize != 38)
                throw new InvalidOperationException(prefab.name + " must contain exactly 38 node controls.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < nodeViews.arraySize; i++)
            {
                var item = nodeViews.GetArrayElementAtIndex(i);
                string id = item.FindPropertyRelative("Id").stringValue;
                var button = item.FindPropertyRelative("Button").objectReferenceValue as UnityEngine.UI.Button;
                if (!ids.Add(id) || PentaUpgradeCatalog.Find(id) == null || !button || view.GetNodeButton(id) != button)
                    throw new InvalidOperationException(prefab.name + " has an invalid or duplicate node control: " + id);
            }
            var links = prefab.GetComponentsInChildren<PentaUpgradeLinks>(true);
            if (links.Length != 1 || links[0].ConnectionCount != 37)
                throw new InvalidOperationException(prefab.name + " must contain exactly 37 graph links.");
            var serializedLinks = new SerializedObject(links[0]).FindProperty("links");
            var expected = ExpectedParents();
            var children = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < serializedLinks.arraySize; i++)
            {
                var link = serializedLinks.GetArrayElementAtIndex(i);
                string child = link.FindPropertyRelative("ChildId").stringValue;
                string parent = link.FindPropertyRelative("ParentId").stringValue;
                if (!children.Add(child) || !expected.TryGetValue(child, out string expectedParent) || parent != expectedParent)
                    throw new InvalidOperationException(prefab.name + " graph link differs from its concept branch: " + child);
            }
        }

        static void ValidateDraftLabels(GameObject prefab)
        {
            var labels = prefab.GetComponentsInChildren<TMP_Text>(true);
            if (!labels.Any(x => x.name == "Subtitle" && x.text == "세로 줄기형 / 노드 내용 미확정")
                || !labels.Any(x => x.name == "DescriptionHeading" && x.text == "효과 초안")
                || !labels.Any(x => x.name == "Family" && x.text.Contains("내용 미확정"))
                || !labels.Any(x => x.text == "이름과 효과는 검토 중입니다"))
                throw new InvalidOperationException(prefab.name + " must clearly mark node content as an unconfirmed draft.");
        }

        static void ValidateReferences(GameObject prefab)
        {
            foreach (var component in prefab.GetComponentsInChildren<Component>(true))
            {
                if (!component) throw new InvalidOperationException(prefab.name + " contains missing scripts.");
                var serialized = new SerializedObject(component);
                var property = serialized.GetIterator();
                while (property.NextVisible(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference
                        && property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0)
                        throw new InvalidOperationException(prefab.name + " has a missing reference: " + component.name + "/" + property.propertyPath);
            }
        }

        static string OutputDirectory => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../Deliverables/10_영구강화_UI_Unity/경계ZERO_디자인수정/세로줄기_v7"));

        [MenuItem("Tools/PENTA Upgrade/Export package only")]
        public static void ExportPackageOnly()
        {
            ValidateImported();
            string output = OutputDirectory;
            Directory.CreateDirectory(output);
            var assets = new HashSet<string> { FontSource, BackgroundPath };
            IncludeDirectory(assets, Root);
            IncludeDirectory(assets, "Assets/TextMesh Pro/Resources");
            IncludeDirectory(assets, "Assets/TextMesh Pro/Shaders");
            var list = assets.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            AssetDatabase.ExportPackage(list, Path.Combine(output, "BoundaryZERO_UpgradePanel.unitypackage"), ExportPackageOptions.Default);
            File.WriteAllLines(Path.Combine(output, "package-assets.txt"), list);
        }

        static void IncludeDirectory(HashSet<string> assets, string directory)
        {
            if (!Directory.Exists(directory)) throw new InvalidOperationException("Required export directory missing: " + directory);
            foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                if (file.Replace('\\', '/') == Root + "/Art/PentaUpgradeKorean.asset") continue;
                string path = file.Replace('\\', '/');
                assets.Add(path);
                foreach (string dependency in AssetDatabase.GetDependencies(path, true))
                    if (dependency.StartsWith("Assets/", StringComparison.Ordinal)) assets.Add(dependency);
            }
        }

        [MenuItem("Tools/PENTA Upgrade/Build Windows preview and export package")]
        public static void BuildAndExport()
        {
            Prepare();
            ValidateImported();
            ExportPackageOnly();
            string output = OutputDirectory;
            bool background = PlayerSettings.runInBackground;
            int width = PlayerSettings.defaultScreenWidth, height = PlayerSettings.defaultScreenHeight;
            var mode = PlayerSettings.fullScreenMode;
            string company = PlayerSettings.companyName, product = PlayerSettings.productName;
            try
            {
                PlayerSettings.runInBackground = true;
                PlayerSettings.defaultScreenWidth = 1920;
                PlayerSettings.defaultScreenHeight = 1080;
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.companyName = "PENTA";
                PlayerSettings.productName = "경계ZERO 각인 강화";
                string player = Path.Combine(output, "BoundaryZERO_Upgrade_Windows/BoundaryZERO_Upgrade.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(player));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath }, locationPathName = player,
                    target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("Upgrade preview build failed: " + report.summary.result + " (" + report.summary.totalErrors + " errors)");
            }
            finally
            {
                PlayerSettings.runInBackground = background;
                PlayerSettings.defaultScreenWidth = width;
                PlayerSettings.defaultScreenHeight = height;
                PlayerSettings.fullScreenMode = mode;
                PlayerSettings.companyName = company;
                PlayerSettings.productName = product;
            }
        }
    }
}
