using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

namespace BoundaryZero.Inventory.Editor
{
    public static class InventoryPackageBuilder
    {
        private const string Root = "Assets/BoundaryInventory";
        private const string CatalogPath = Root + "/Catalog/InventoryCatalog.asset";
        private const string FontPath = Root + "/Fonts/Pretendard-Regular SDF.asset";
        private const string PrefabPath = Root + "/Prefabs/InventoryCanvas.prefab";
        private const string DemoPath = Root + "/Demo/InventoryPreview.unity";
        private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        private const string DeliveryFolder = "BoundaryInventoryExport";

        [Serializable]
        private sealed class ValidationReport
        {
            public string unityVersion;
            public string prefab;
            public string demoScene;
            public string demoInputModule;
            public int items;
            public int synergies;
            public int distinctDemoItems;
            public int demoEventSystems;
            public int itemSlots;
            public int synergyRows;
            public int populatedScrollAreas;
            public int bakedGlyphs;
            public string[] i02Synergies;
            public string[] maximumTierSynergies;
            public string[] checks;
            public string[] exportedPaths;
            public long packageBytes;
        }

        public static void PrepareTmp()
        {
            if (!AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath))
                TMP_PackageResourceImporter.ImportResources(true, false, false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        // Run with -batchmode without -quit so the asynchronous Essentials import can finish.
        public static void PrepareTmpBatch()
        {
            DateTime deadline = DateTime.UtcNow.AddMinutes(2);
            int settledUpdates = 0;
            EditorApplication.update += WaitForImport;
            try
            {
                PrepareTmp();
            }
            catch (Exception error)
            {
                EditorApplication.update -= WaitForImport;
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }

            void WaitForImport()
            {
                if (!EditorApplication.isUpdating && !EditorApplication.isCompiling &&
                    AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath) &&
                    Shader.Find("TextMeshPro/Mobile/Distance Field"))
                {
                    if (++settledUpdates < 2)
                        return;
                    EditorApplication.update -= WaitForImport;
                    Debug.Log("Boundary ZERO Inventory TMP Essentials are ready.");
                    EditorApplication.Exit(0);
                }
                else if (DateTime.UtcNow >= deadline)
                {
                    EditorApplication.update -= WaitForImport;
                    Debug.LogError("TMP Essentials import did not finish within two minutes.");
                    EditorApplication.Exit(1);
                }
                else
                    settledUpdates = 0;
            }
        }

        public static void Build()
        {
            PrepareTmp();
            Require(AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath),
                "TMP Essentials import is pending. Run Build after PrepareTmp completes.");

            Directory.CreateDirectory(Root + "/Prefabs");
            Directory.CreateDirectory(Root + "/Demo");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            InventoryCatalog catalog = CreateCatalog();
            CreatePrefab(catalog);
            CreateDemo();

            AssetDatabase.SaveAssets();
            ValidationReport report = ValidateAssets();
            string packagePath = OutputPath("BOUNDARY_INVENTORY_PACKAGE", "Unity/BoundaryZERO_Inventory_Items.unitypackage");
            Directory.CreateDirectory(Path.GetDirectoryName(packagePath));
            report.exportedPaths = ExportPaths();
            AssetDatabase.ExportPackage(report.exportedPaths, packagePath, ExportPackageOptions.Default);
            report.packageBytes = new FileInfo(packagePath).Length;
            Require(report.packageBytes > 0, "Unity did not write a nonempty package.");
            WriteReport(report, "Validation/unity-build.json");
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                CapturePreview();
            Debug.Log("Boundary ZERO Inventory package exported: " + packagePath);
        }

        public static void VerifyImported()
        {
            ValidationReport report = ValidateAssets();
            WriteReport(report, "Validation/unity-import.json");
            Debug.Log("Boundary ZERO Inventory import validation passed.");
        }

        public static void CapturePreview()
        {
            Require(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null,
                "Preview capture needs an Editor session with a graphics device.");
            Scene scene = EditorSceneManager.OpenScene(DemoPath, OpenSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var view = instance.GetComponent<InventoryView>();
            var cameraObject = new GameObject("TemporaryInventoryPreviewCamera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            var target = new RenderTexture(1920, 1080, 24);
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(14, 17, 22, 255);
                camera.orthographic = true;
                camera.targetTexture = target;
                var canvas = instance.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                view.SetOwnedItems(view.Catalog.ReadData().items.Select(item => item.id));
                view.Open();
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                Color background = image.GetPixel(0, 0);
                int visibleSamples = 0;
                for (int y = 32; y < image.height; y += 32)
                for (int x = 32; x < image.width; x += 32)
                    if (Vector4.Distance(image.GetPixel(x, y), background) > 0.04f)
                        visibleSamples++;
                Require(visibleSamples > 40, "Preview capture did not contain visible inventory art.");
                string output = OutputPath("BOUNDARY_INVENTORY_PREVIEW", "Validation/unity-preview.png");
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                File.WriteAllBytes(output, image.EncodeToPNG());
                Debug.Log("Boundary ZERO Inventory preview captured: " + output);
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        public static void RefreshInstalledCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<InventoryCatalog>(CatalogPath);
            Require(catalog, "Installed presentation catalog is missing.");
            InventoryData data = catalog.ReadData();
            ValidateData(data);
            var bindings = catalog.itemIcons.ToDictionary(binding => binding.id, StringComparer.Ordinal);
            SpriteBinding[] activeBindings = data.items.Select(item => bindings[item.id]).ToArray();
            ValidateBindings(activeBindings, data.items.Select(item => item.id), 512);
            catalog.font = CreateFont(data);
            catalog.itemIcons = activeBindings;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
        }

        private static InventoryCatalog CreateCatalog()
        {
            TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Catalog/catalog.json");
            Require(json, "Catalog/catalog.json is missing.");

            InventoryCatalog catalog = AssetDatabase.LoadAssetAtPath<InventoryCatalog>(CatalogPath);
            if (!catalog)
            {
                catalog = ScriptableObject.CreateInstance<InventoryCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.json = json;
            InventoryData data = catalog.ReadData();
            ValidateData(data);
            catalog.itemIcons = data.items.Select(item =>
                new SpriteBinding(item.id, ImportSprite("Items/" + item.id + ".png", 512))).ToArray();
            catalog.synergyIcons = data.synergies.Select(synergy =>
                new SpriteBinding(synergy.id, ImportSprite("Synergies/" + synergy.id + ".png", 256))).ToArray();
            catalog.windowFrame = ImportSprite("UI/menu-window.png", 4096);
            catalog.seal = ImportSprite("UI/seal.png", 2048);
            catalog.font = CreateFont(data);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        private static Sprite ImportSprite(string relativePath, int maxSize)
        {
            string path = Root + "/Art/" + relativePath;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Require(importer, "Missing texture importer: " + path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            var spriteSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(spriteSettings);
            spriteSettings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(spriteSettings);
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = maxSize;
            importer.SaveAndReimport();
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Require(sprite, "Failed to load imported sprite: " + path);
            if (maxSize <= 512)
                Require(sprite.rect.size == new Vector2(maxSize, maxSize),
                    $"Expected a {maxSize}×{maxSize} icon: {path}");
            return sprite;
        }

        private static TMP_FontAsset CreateFont(InventoryData data)
        {
            Font source = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/Pretendard-Regular.otf");
            Require(source, "Pretendard-Regular.otf is missing.");
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (!font)
            {
                font = TMP_FontAsset.CreateFontAsset(source, 64, 8, GlyphRenderMode.SDFAA, 2048, 2048,
                    AtlasPopulationMode.Dynamic, true);
                Require(font && font.material && font.material.shader, "TMP SDF font material could not be created.");
                font.name = "Pretendard-Regular SDF";
                AssetDatabase.CreateAsset(font, FontPath);
            }

            var fontProperties = new SerializedObject(font);
            fontProperties.FindProperty("m_ClearDynamicDataOnBuild").boolValue = false;
            fontProperties.ApplyModifiedPropertiesWithoutUndo();
            string characters = FontCharacters(data);
            font.ReadFontAssetDefinition();
            if (!font.HasCharacters(characters, out uint[] missing, false, false))
            {
                font.TryAddCharacters(missing);
                Require(font.HasCharacters(characters, out missing, false, false),
                    "Pretendard is missing glyphs: " + string.Join(", ", missing ?? Array.Empty<uint>()));
            }
            foreach (Texture2D atlas in font.atlasTextures)
                if (atlas && !AssetDatabase.Contains(atlas))
                    AssetDatabase.AddObjectToAsset(atlas, font);
            if (!AssetDatabase.Contains(font.material))
                AssetDatabase.AddObjectToAsset(font.material, font);
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssetIfDirty(font);
            return font;
        }

        private static string FontCharacters(InventoryData data)
        {
            string runtimePath = Root + "/Runtime";
            IEnumerable<char> runtime = Directory.GetFiles(runtimePath, "*.cs", SearchOption.AllDirectories)
                .SelectMany(File.ReadAllText)
                .Where(character => character > 127);
            return new string(JsonUtility.ToJson(data)
                .Concat(runtime)
                .Concat(Enumerable.Range(32, 95).Select(code => (char)code))
                .Where(character => !char.IsControl(character) && !char.IsSurrogate(character))
                .Distinct()
                .ToArray());
        }

        private static void CreatePrefab(InventoryCatalog catalog)
        {
            var root = new GameObject("InventoryCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster), typeof(InventoryView));
            try
            {
                Canvas canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                InventoryView view = root.GetComponent<InventoryView>();
                view.BuildView(catalog);
                Require(!view.IsOpen && view.OwnedItemCount == 0, "New production inventory should be closed and empty.");
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Require(prefab, "Failed to save the inventory prefab.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void CreateDemo()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Require(prefab, "Production prefab is missing.");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var demo = new GameObject("InventoryDemo").AddComponent<InventoryDemo>();
            demo.view = instance.GetComponent<InventoryView>();

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem));
            demo.eventSystem = eventSystem.GetComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
            Require(EditorSceneManager.SaveScene(scene, DemoPath), "Failed to save the inventory preview scene.");
        }

        private static void ValidateData(InventoryData data)
        {
            Require(data != null && data.items?.Length == 77 && data.synergies?.Length == 11,
                "Expected 77 items and 11 synergies in the catalog.");
            var itemIds = new HashSet<string>(data.items.Select(item => item.id), StringComparer.Ordinal);
            var synergyIds = new HashSet<string>(data.synergies.Select(synergy => synergy.id), StringComparer.Ordinal);
            Require(itemIds.Count == data.items.Length && synergyIds.Count == data.synergies.Length, "Catalog IDs must be unique.");
            Require(data.synergies.All(set => string.IsNullOrEmpty(set.summary)), "Synergy summaries must be empty.");
            Require(data.synergies.All(set => set.tiers.Select(tier => tier.count)
                .SequenceEqual(set.id == "S19" ? new[] { 1, 2, 3 } : new[] { 2, 4, 6, 8 })),
                "Catalog tier thresholds differ from the current rules.");
            Require(data.items.Where(item => item.synergies.Contains("S19")).Select(item => item.id).ToHashSet()
                .SetEquals(new[] { "I89", "I91", "I122" }), "Harvest membership differs from the current rules.");
            Require(!synergyIds.Contains("S07"), "S07 is excluded from the handoff.");
            Require(data.items.All(item => item.synergies.All(synergyIds.Contains)), "Item has an unknown synergy.");
        }

        private static ValidationReport ValidateAssets()
        {
            Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoPath), "Inventory preview scene is missing.");
            Scene demoScene = EditorSceneManager.OpenScene(DemoPath, OpenSceneMode.Single);
            var catalog = AssetDatabase.LoadAssetAtPath<InventoryCatalog>(CatalogPath);
            Require(catalog && catalog.windowFrame && catalog.seal && catalog.font, "Catalog art or font references are missing.");
            Require(Resources.Load<TMP_Settings>("TMP Settings"), "The package must supply TMP Settings to a clean project.");
            InventoryData data = catalog.ReadData();
            ValidateData(data);
            ValidateBindings(catalog.itemIcons, data.items.Select(item => item.id), 512);
            ValidateBindings(catalog.synergyIcons, data.synergies.Select(synergy => synergy.id), 256);
            catalog.font.ReadFontAssetDefinition();
            Require(catalog.font.material && catalog.font.material.shader &&
                catalog.font.atlasTextures.All(atlas => atlas && AssetDatabase.Contains(atlas)),
                "The font atlas or its material is not persistent.");
            Require(catalog.font.HasCharacters(FontCharacters(data), out uint[] missingGlyphs, false, false),
                "The persistent font lacks required glyphs: " + string.Join(", ", missingGlyphs ?? Array.Empty<uint>()));

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Require(prefab, "Production prefab is missing.");
            ValidateNoMissingScripts(prefab);
            Require(prefab.GetComponentsInChildren<EventSystem>(true).Length == 0 &&
                prefab.GetComponentsInChildren<InventoryDemo>(true).Length == 0,
                "Production prefab must not contain demo ownership or an EventSystem.");
            Canvas canvas = prefab.GetComponent<Canvas>();
            var scaler = prefab.GetComponent<UnityEngine.UI.CanvasScaler>();
            Require(canvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay && scaler &&
                scaler.referenceResolution == new Vector2(1920, 1080) && scaler.matchWidthOrHeight == 0.5f &&
                prefab.GetComponent<UnityEngine.UI.GraphicRaycaster>(),
                "Production canvas is not configured for 1920×1080 overlay interaction.");
            Require(prefab.GetComponent<InventoryView>() && !prefab.GetComponent<InventoryView>().IsOpen,
                "Production prefab must serialize as closed.");

            GameObject[] roots = demoScene.GetRootGameObjects();
            foreach (GameObject root in roots)
                ValidateNoMissingScripts(root);
            var eventSystems = roots.SelectMany(root => root.GetComponentsInChildren<EventSystem>(true)).ToArray();
            Require(eventSystems.Length == 1, "Preview scene needs exactly one EventSystem.");
            var modules = eventSystems[0].GetComponents<BaseInputModule>();
            Require(modules.Length == 1 && modules[0].enabled, "Preview EventSystem needs one enabled input module.");
            var demos = roots.SelectMany(root => root.GetComponentsInChildren<InventoryDemo>(true)).ToArray();
            Require(demos.Length == 1 && demos[0].view && demos[0].eventSystem == eventSystems[0] &&
                PrefabUtility.GetCorrespondingObjectFromSource(demos[0].view.gameObject) == prefab,
                "Preview must reference its own EventSystem and an instance of the production prefab.");

            var report = new ValidationReport
            {
                unityVersion = Application.unityVersion,
                prefab = PrefabPath,
                demoScene = DemoPath,
                demoInputModule = modules[0].GetType().Name,
                items = data.items.Length,
                synergies = data.synergies.Length,
                demoEventSystems = eventSystems.Length,
                bakedGlyphs = catalog.font.characterTable.Count
            };
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, demoScene);
            try
            {
                ValidateBehavior(instance.GetComponent<InventoryView>(), data, eventSystems[0], report);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
            return report;
        }

        private static void ValidateBindings(SpriteBinding[] bindings, IEnumerable<string> ids, int size)
        {
            Require(bindings != null && bindings.Select(binding => binding.id).SequenceEqual(ids) &&
                bindings.All(binding => binding.sprite && binding.sprite.rect.size == new Vector2(size, size) &&
                    AssetDatabase.Contains(binding.sprite)), "Catalog sprite bindings are incomplete or unordered.");
        }

        private static void ValidateNoMissingScripts(GameObject root)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) == 0,
                    "Missing script on " + child.name);
        }

        private static void ValidateBehavior(InventoryView view, InventoryData data, EventSystem eventSystem,
            ValidationReport report)
        {
            Require(!view.IsOpen && view.OwnedItemCount == 0 &&
                view.GetSynergyStates().All(state => state.Count == 0 && state.ActiveTier == 0),
                "Fresh production instance is not empty and closed.");
            var events = new List<bool>();
            view.VisibilityChanged += events.Add;
            view.Open();
            view.Open();
            view.Close();
            view.Toggle();
            Require(view.IsOpen && events.SequenceEqual(new[] { true, false, true }), "Visibility event or toggle failed.");

            view.SetOwnedItems(new[] { "I02", "I02", "unknown-item" });
            string[] expectedI02 = data.items.Single(item => item.id == "I02").synergies.OrderBy(id => id).ToArray();
            report.i02Synergies = view.GetSynergyStates().Where(state => state.Count == 1).Select(state => state.Id)
                .OrderBy(id => id).ToArray();
            Require(view.OwnedItemCount == 1 && expectedI02.Length > 1 && report.i02Synergies.SequenceEqual(expectedI02),
                "Duplicate/unknown item handling or I02's multiple synergies failed.");
            view.SetItemOwned("I02", true);
            Require(view.OwnedItemCount == 1, "Repeated item acquisition must be idempotent.");
            view.SetItemOwned("I02", false);
            Require(view.OwnedItemCount == 0 && view.GetSynergyStates().All(state => state.Count == 0),
                "Item removal must update all memberships.");

            var reachedMaximumTier = new List<string>();
            foreach (var synergy in data.synergies)
            {
                string[] members = data.items.Where(item => item.synergies.Contains(synergy.id))
                    .Select(item => item.id).ToArray();
                int[] expectedTiers = synergy.tiers.Select(tier => tier.count).ToArray();
                int maximumTier = expectedTiers.Max();
                Require(members.Length >= maximumTier, "Highest tier cannot be reached for " + synergy.id);
                for (int count = 0; count <= members.Length; count++)
                {
                    view.SetOwnedItems(members.Take(count));
                    var state = view.GetSynergyStates().Single(value => value.Id == synergy.id);
                    int expectedActive = expectedTiers.LastOrDefault(tier => tier <= count);
                    int expectedNext = expectedTiers.FirstOrDefault(tier => tier > count);
                    Require(state.Count == count && state.ActiveTier == expectedActive && state.RequiredCount == maximumTier
                        && state.NextTier == expectedNext,
                        "Tier update failed for " + synergy.id + " at " + count + " owned items.");
                }
                reachedMaximumTier.Add(synergy.id);
            }

            view.SetOwnedItems(data.items.Select(item => item.id));
            report.distinctDemoItems = view.OwnedItemCount;
            report.maximumTierSynergies = reachedMaximumTier.ToArray();
            var fullStates = view.GetSynergyStates();
            Require(report.distinctDemoItems == data.items.Length && fullStates.Count == data.synergies.Length &&
                fullStates.All(state => state.Id != "S07" && state.ActiveTier == state.RequiredCount
                    && state.DisplayCount == state.RequiredCount
                    && state.Count == data.items.Count(item => item.synergies.Contains(state.Id))),
                "Full demo inventory did not reach the highest tier of every synergy.");

            Canvas.ForceUpdateCanvases();
            Transform[] hierarchy = view.GetComponentsInChildren<Transform>(true);
            report.itemSlots = hierarchy.Count(child => child.name.StartsWith("Item_I", StringComparison.Ordinal));
            report.synergyRows = hierarchy.Count(child => child.name.StartsWith("Synergy_S", StringComparison.Ordinal));
            Require(report.itemSlots == data.items.Length && report.synergyRows == data.synergies.Length &&
                hierarchy.All(child => child.name != "Synergy_S07"), "Rendered item/synergy hierarchy is incomplete.");
            var scrolls = view.GetComponentsInChildren<UnityEngine.UI.ScrollRect>(true);
            report.populatedScrollAreas = scrolls.Count(scroll => scroll.vertical && !scroll.horizontal &&
                scroll.content && scroll.viewport && scroll.content.IsChildOf(scroll.viewport) &&
                (scroll.viewport.GetComponent<UnityEngine.UI.RectMask2D>() || scroll.viewport.GetComponent<UnityEngine.UI.Mask>()) &&
                scroll.content.rect.height > scroll.viewport.rect.height && scroll.viewport.rect.height > 0);
            Require(report.populatedScrollAreas == 2, "Item grid and synergy list need independently scrollable, clipped content.");

            Transform tooltip = hierarchy.Single(child => child.name == "Tooltip");
            Require(tooltip.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).All(graphic => !graphic.raycastTarget),
                "Tooltip graphics must allow pointer rays through.");
            CanvasGroup tooltipGroup = tooltip.GetComponent<CanvasGroup>();
            Require(!tooltipGroup || !tooltipGroup.blocksRaycasts, "Tooltip canvas group must allow pointer rays through.");
            var pointer = new PointerEventData(eventSystem);
            Transform slot = hierarchy.Single(child => child.name == "Item_I02");
            Require(slot.GetComponent<UnityEngine.UI.Graphic>().raycastTarget &&
                ExecuteEvents.Execute(slot.gameObject, pointer, ExecuteEvents.pointerEnterHandler) && tooltip.gameObject.activeSelf,
                "Item pointer enter did not show its tooltip.");
            string itemTooltip = tooltip.GetComponentInChildren<TMP_Text>().text;
            Require(itemTooltip.Contains(data.items.Single(item => item.id == "I02").effect) &&
                expectedI02.All(id => itemTooltip.Contains(data.synergies.Single(synergy => synergy.id == id).name)),
                "Item tooltip omitted its solo effect or a synergy.");
            ExecuteEvents.Execute(slot.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            Require(!tooltip.gameObject.activeSelf, "Item pointer exit did not hide the tooltip.");
            Transform row = hierarchy.Single(child => child.name == "Synergy_S16");
            Require(ExecuteEvents.Execute(row.gameObject, pointer, ExecuteEvents.pointerEnterHandler) &&
                tooltip.gameObject.activeSelf, "Synergy pointer enter did not show its tooltip.");
            string setTooltip = tooltip.GetComponentInChildren<TMP_Text>().text;
            Require(data.synergies.Single(synergy => synergy.id == "S16").tiers.All(tier => setTooltip.Contains(tier.text)),
                "Synergy tooltip omitted a tier.");
            var close = hierarchy.Single(child => child.name == "Close").GetComponent<UnityEngine.UI.Button>();
            close.onClick.Invoke();
            Require(!view.IsOpen && !tooltip.gameObject.activeSelf, "Close button did not dismiss inventory and tooltip.");
            report.checks = new[]
            {
                "No missing scripts or production demo input.",
                "Production starts empty and closed. Visibility events fire on transitions.",
                "Duplicate and unknown IDs are ignored. I02 counts in each matching synergy.",
                "All 11 synergies progress through 0/2/4/6/8 at the expected ownership thresholds. S07 is absent.",
                "Item and synergy viewports independently scroll with clipped content.",
                "Pointer events show item effects and synergy tiers. Tooltip graphics allow pointer rays through.",
                "All 92 catalog icons and the prepopulated Korean TMP font persist."
            };
        }

        private static string[] ExportPaths()
        {
            string[] featureAssets = Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .Select(path => path.Replace('\\', '/')).ToArray();
            var paths = new HashSet<string>(featureAssets, StringComparer.Ordinal);
            foreach (string dependency in AssetDatabase.GetDependencies(new[] { PrefabPath, DemoPath, TmpSettingsPath }, true))
                if (dependency.StartsWith("Assets/TextMesh Pro/", StringComparison.Ordinal) && !AssetDatabase.IsValidFolder(dependency))
                    paths.Add(dependency);

            // Shader includes can be omitted from GetDependencies on some Editor versions.
            var shaders = new Queue<string>(paths.Where(path => path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)));
            while (shaders.Count > 0)
            {
                string shader = shaders.Dequeue();
                foreach (Match include in Regex.Matches(File.ReadAllText(shader), "#include\\s+\"([^\"]+)\""))
                {
                    string path = (Path.GetDirectoryName(shader) + "/" + include.Groups[1].Value).Replace('\\', '/');
                    if (File.Exists(path) && paths.Add(path))
                        shaders.Enqueue(path);
                }
            }

            foreach (string license in Directory.GetFiles("Assets/TextMesh Pro", "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) &&
                    (Path.GetFileName(path).Contains("OFL") || Path.GetFileName(path).Contains("License") || Path.GetFileName(path).Contains("Attribution"))))
                paths.Add(license.Replace('\\', '/'));
            return paths.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }

        private static string OutputPath(string variable, string relativePath)
        {
            string configured = Environment.GetEnvironmentVariable(variable);
            return Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Directory.GetParent(Application.dataPath).FullName, DeliveryFolder, relativePath)
                : configured);
        }

        private static void WriteReport(ValidationReport report, string defaultPath)
        {
            string reportPath = OutputPath("BOUNDARY_INVENTORY_VALIDATION", defaultPath);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
