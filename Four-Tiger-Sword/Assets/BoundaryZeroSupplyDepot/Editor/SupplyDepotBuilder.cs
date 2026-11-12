using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace BoundaryZero.SupplyDepot.Editor
{
    public static class SupplyDepotBuilder
    {
        private const string Root = "Assets/BoundaryZeroSupplyDepot";
        private const string PrefabPath = Root + "/Prefabs/SupplyDepot.prefab";
        private const string ScenePath = Root + "/Samples/SupplyDepotDemo.unity";
        private const string CatalogPath = Root + "/Settings/SupplyDepotCatalog.asset";
        private const string FontPath = Root + "/Fonts/SupplyDepot SDF.asset";

        private static readonly Color Paper = Hex("#E8EEF5");
        private static readonly Color Subtle = Hex("#A4B7C9");
        private static readonly Color Quiet = Hex("#70869A");
        private static readonly Color Border = Hex("#8199B442");
        private static readonly Color Red = Hex("#ED2D4D");
        private static readonly Color Clear = Color.clear;
        private static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        private static readonly Vector2 CenterPoint = new Vector2(0.5f, 0.5f);

        [Serializable]
        private sealed class JsonCatalog
        {
            public JsonItem[] items;
        }

        [Serializable]
        private sealed class JsonItem
        {
            public string id;
            public string name;
            public string effect;
            public int grade;
            public string color;
            public JsonSynergy[] synergies;
        }

        [Serializable]
        private sealed class JsonSynergy
        {
            public string id;
            public string name;
            public string[] compactLines;
            public int[] thresholds;
        }

        [MenuItem("Tools/Boundary ZERO/Supply Depot/Create Demo")]
        public static void BuildAll()
        {
            NewSceneMode sceneMode = GetDemoSceneMode();
            foreach (string folder in new[] { "Prefabs", "Samples", "Settings", "Fonts" })
                if (!AssetDatabase.IsValidFolder($"{Root}/{folder}"))
                    AssetDatabase.CreateFolder(Root, folder);

            SupplyDepotCatalog catalog = BuildCatalog();
            GameObject prefab = BuildPrefab(catalog);
            BuildScene(prefab, catalog, sceneMode);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Built {PrefabPath} and {ScenePath}");
        }

        private static NewSceneMode GetDemoSceneMode()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.path == ScenePath)
                    throw new InvalidOperationException(
                        "Close SupplyDepotDemo before regenerating it. Save any edits you want to keep first.");
                if (!string.IsNullOrEmpty(scene.path))
                    continue;
                if (scene.isDirty || SceneManager.sceneCount > 1)
                    throw new InvalidOperationException(
                        "Save all untitled scenes before using Tools > Boundary ZERO > Supply Depot > Create Demo.");
                return NewSceneMode.Single;
            }
            return SceneManager.sceneCount == 0 ? NewSceneMode.Single : NewSceneMode.Additive;
        }

        private static SupplyDepotCatalog BuildCatalog()
        {
            var source = RequiredAsset<TextAsset>(Root + "/Data/catalog.json");
            var json = JsonUtility.FromJson<JsonCatalog>(source.text);
            if (json?.items == null || json.items.Length == 0)
                throw new InvalidDataException("Supply depot catalog contains no items.");

            var catalog = AssetDatabase.LoadAssetAtPath<SupplyDepotCatalog>(CatalogPath);
            bool create = catalog == null;
            if (create)
                catalog = ScriptableObject.CreateInstance<SupplyDepotCatalog>();

            catalog.currencyIcon = LoadSprite("Art/UI/seal.png");
            catalog.frameSprite = LoadSprite("Art/UI/frame.png");
            catalog.backdrop = LoadSprite("Art/UI/hub.png");
            catalog.font = BuildFont();
            var items = new List<SupplyDepotItem>();
            var ids = new HashSet<string>();
            foreach (var entry in json.items)
            {
                if (string.IsNullOrWhiteSpace(entry.id) || !ids.Add(entry.id))
                    throw new InvalidDataException($"Invalid or duplicate item ID '{entry.id}'.");
                var item = new SupplyDepotItem
                {
                    id = entry.id,
                    name = entry.name,
                    effect = entry.effect,
                    grade = entry.grade,
                    color = Hex(entry.color),
                    icon = LoadSprite($"Art/Items/{entry.id}.png"),
                    synergies = new List<SupplyDepotSynergy>(),
                };
                if (entry.synergies != null)
                    foreach (var group in entry.synergies)
                        item.synergies.Add(new SupplyDepotSynergy
                        {
                            id = group.id,
                            name = group.name,
                            icon = LoadSprite($"Art/Synergies/{group.id}.png"),
                            compactLines = group.compactLines,
                            thresholds = group.thresholds,
                        });
                items.Add(item);
            }
            catalog.items = items;
            if (create)
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        private static TMP_FontAsset BuildFont()
        {
            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (fontAsset != null)
                return fontAsset;

            var font = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/Source.otf");
            if (font == null)
                font = RequiredAsset<Font>(Root + "/Fonts/Source.ttf");
            fontAsset = TMP_FontAsset.CreateFontAsset(font, 60, 6,
                UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, true);
            if (fontAsset == null || fontAsset.material == null || fontAsset.atlasTexture == null)
                throw new InvalidOperationException("Could not create the supply depot TMP font asset.");
            fontAsset.name = "SupplyDepot SDF";
            fontAsset.material.name = "SupplyDepot SDF Material";
            fontAsset.atlasTexture.name = "SupplyDepot SDF Atlas";
            AssetDatabase.CreateAsset(fontAsset, FontPath);
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            AssetDatabase.AddObjectToAsset(fontAsset.atlasTexture, fontAsset);
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssetIfDirty(fontAsset);
            return fontAsset;
        }

        private static T RequiredAsset<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new FileNotFoundException($"Missing supply depot {typeof(T).Name}: {path}");
            return asset;
        }

        private static Sprite LoadSprite(string path)
        {
            string assetPath = $"{Root}/{path}";
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                throw new FileNotFoundException($"Missing supply depot texture: {assetPath}");
            if (importer.textureType != TextureImporterType.Sprite ||
                importer.spriteImportMode != SpriteImportMode.Single || importer.mipmapEnabled ||
                importer.textureCompression != TextureImporterCompression.Uncompressed ||
                importer.maxTextureSize != 4096 || !importer.alphaIsTransparency)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 4096;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            return RequiredAsset<Sprite>(assetPath);
        }

        private static GameObject BuildPrefab(SupplyDepotCatalog catalog)
        {
            var root = new GameObject("SupplyDepot", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            try
            {
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 60;
                var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1440f, 920f);
                scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;

                var layer = Fill(root.transform, "Interface");
                var window = Center(layer, "Window", 1250f, 640f);
                window.anchoredPosition = new Vector2(0f, -6f);
                var elements = new SupplyDepotView.Elements
                {
                    window = window.gameObject,
                    frame = Picture(window, "Frame", catalog.frameSprite, false, true),
                };
                AddHeader(window, catalog, elements, out var titleSeal, out var walletSeal);
                AddMachine(window, catalog.font, elements);
                AddResult(window, catalog.font, elements);
                AddFooter(window, catalog, elements, out var costSeal);
                elements.currencyIcons = new[] { titleSeal, walletSeal, costSeal };

                var reopen = Center(layer, "Reopen", 190f, 48f);
                elements.reopenContainer = reopen.gameObject;
                elements.reopen = RedButton(reopen, catalog.font, null, "보급고", out _, out _);
                reopen.gameObject.SetActive(false);

                var view = root.AddComponent<SupplyDepotView>();
                view.AssignEditorElements(elements);
                var controller = root.AddComponent<SupplyDepotController>();
                controller.catalog = catalog;
                controller.view = view;
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void AddHeader(RectTransform window, SupplyDepotCatalog catalog,
            SupplyDepotView.Elements elements, out UnityEngine.UI.Image titleSeal, out UnityEngine.UI.Image walletSeal)
        {
            var header = Box(window, "Header", 40f, 32f, 1170f, 58f);
            Bar(header, "Rule", 0f, 57f, 1170f, 1f, Border);
            Bar(header, "Light Accent", 0f, 56f, 93f, 2f, Paper);
            Bar(header, "Red Accent", 100f, 56f, 43f, 2f, Red);
            Bar(header, "Title Marker", 0f, 4f, 3f, 31f, Red);
            titleSeal = Picture(Box(header, "Title Seal", 19f, 4f, 24f, 31f), "Icon", catalog.currencyIcon);
            Label(Box(header, "Title", 56f, -1f, 260f, 42f), catalog.font, "보급고", 32f, Paper,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);

            walletSeal = Picture(Box(header, "Wallet Seal", 907f, 8f, 17f, 23f), "Icon", catalog.currencyIcon);
            Label(Box(header, "Currency", 939f, 6f, 43f, 28f), catalog.font, "강화석", 12f, Subtle);
            elements.balance = Label(Box(header, "Balance", 986f, 0f, 64f, 38f), catalog.font, "1,200", 25f, Paper,
                TextAlignmentOptions.MidlineRight);
            elements.balance.enableAutoSizing = true;
            elements.balance.fontSizeMin = 12f;
            elements.balance.fontSizeMax = 25f;

            var close = Box(header, "Close", 1070f, 0f, 100f, 38f);
            elements.close = Button(close, new Color32(21, 34, 50, 255));
            Outline(close, "#A8B9CB5C");
            Label(Box(close, "Shortcut", 10f, 0f, 20f, 38f), catalog.font, "Esc", 10f, Subtle);
            Label(Box(close, "Label", 36f, 0f, 36f, 38f), catalog.font, "닫기", 13f, Paper);
            Label(Box(close, "Cross", 74f, 0f, 18f, 38f), catalog.font, "×", 20f, Paper,
                TextAlignmentOptions.Center);
        }

        private static void AddMachine(RectTransform window, TMP_FontAsset font, SupplyDepotView.Elements elements)
        {
            var heading = Box(window, "Reel Heading", 40f, 110f, 845f, 32f);
            elements.stageLabel = Label(Box(heading, "Stage", 0f, 0f, 120f, 27f), font, "무작위 보급", 17f, Paper);
            Bar(heading, "Rule", 124f, 13f, 690f, 1f, Hex("#7C95AF24"));
            Label(Box(heading, "Count", 818f, 0f, 27f, 27f), font, "1개", 11f, Subtle,
                TextAlignmentOptions.MidlineRight);
            const float machineHeight = 368f;
            const float reelTop = 85f;
            var machine = Box(window, "Reel Machine", 40f, 150f, 845f, machineHeight);
            Picture(machine, "Gradient", Visual("MachineGradient", 2, 128, (x, y) =>
                Color.Lerp(Hex("#0A131F"), Hex("#132131"), y / 127f)), false);
            Bar(machine, "Top Border", 0f, 0f, 845f, 1f, Border);
            Bar(machine, "Bottom Border", 0f, machineHeight - 1f, 845f, 1f, Border);
            Bar(machine, "Upper Corner H", 0f, 0f, 17f, 1f, Quiet);
            Bar(machine, "Upper Corner V", 0f, 0f, 1f, 17f, Quiet);
            Bar(machine, "Lower Corner H", 828f, machineHeight - 1f, 17f, 1f, Quiet);
            Bar(machine, "Lower Corner V", 844f, machineHeight - 17f, 1f, 17f, Quiet);
            var ticks = Box(machine, "Rail Ticks", 0f, 8f, 845f, 9f);
            for (int i = 1; i < 50; i++)
            {
                float visibility = Mathf.Clamp01(Mathf.Min(i, 50 - i) / 7f);
                Bar(ticks, $"Tick {i:00}", i * 17f, 0f, 1f, 9f, new Color(0.56f, 0.68f, 0.78f, visibility * 0.19f));
            }

            var clip = Box(machine, "Reel Viewport", 0f, reelTop, 845f, 206f);
            clip.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            elements.reel = Center(clip, "Reel", 158f, 176f);
            var template = Center(elements.reel, "CardTemplate", 158f, 176f);
            var accent = Bar(template, "Accent", 16f, 0f, 126f, 1f, Subtle);
            var cardArt = FillImage(Box(template, "Art", 29f, 14f, 100f, 100f), Color.white);
            cardArt.preserveAspect = true;
            elements.cardTemplate = new SupplyDepotView.ReelCard
            {
                root = template,
                opacity = template.gameObject.AddComponent<CanvasGroup>(),
                background = FillImage(template, Hex("#203041")),
                art = cardArt,
                accent = accent,
                mystery = Label(Box(template, "Mystery", 29f, 12f, 100f, 100f), font, "?", 55f, Paper,
                    TextAlignmentOptions.Center, FontStyles.Italic),
                itemName = Label(Box(template, "Name", 8f, 119f, 142f, 22f), font, "무작위 아이템", 12f, Paper,
                    TextAlignmentOptions.Center),
                stars = Label(Box(template, "Stars", 8f, 143f, 142f, 19f), font, "", 11f, Paper,
                    TextAlignmentOptions.Center),
            };
            Outline(template, "#9CB8D34A");
            template.gameObject.SetActive(false);

            var fade = Visual("ReelEdge", 128, 2, (x, y) => new Color(1f, 1f, 1f, 1f - x / 127f));
            var left = Picture(Box(machine, "Left Reel Fade", 0f, reelTop, 72f, 206f), "Gradient", fade, false);
            left.color = Hex("#101C2A");
            var right = Picture(Box(machine, "Right Reel Fade", 773f, reelTop, 72f, 206f), "Gradient", fade, false);
            right.color = Hex("#101C2A");
            right.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);

            var selector = Center(machine, "Selector", 166f, 190f);
            selector.anchoredPosition = new Vector2(0f, -4f);
            var triangle = Visual("SelectorPointer", 28, 18, (x, y) =>
                Mathf.Abs(x + 0.5f - 14f) <= (y + 1f) * 14f / 18f ? Color.white : Clear);
            var topPointer = Picture(Box(selector, "Top Pointer", 76f, -16f, 14f, 9f), "Pointer", triangle, false);
            var bottomPointer = Picture(Box(selector, "Bottom Pointer", 76f, 197f, 14f, 9f), "Pointer", triangle, false);
            topPointer.color = Red;
            bottomPointer.color = Red;
            bottomPointer.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            var selectorBorder = Outline(selector, "#C5D6E5D2");
            elements.selectorMarks = new[]
            {
                topPointer, bottomPointer, selectorBorder[0], selectorBorder[1], selectorBorder[2], selectorBorder[3],
            };
            Bar(selector, "Top Notch", 0f, 0f, 23f, 2f, Paper);
            Bar(selector, "Top Notch Upright", 0f, 0f, 2f, 7f, Paper);
            Bar(selector, "Bottom Notch", 143f, 188f, 23f, 2f, Paper);
            Bar(selector, "Bottom Notch Upright", 164f, 183f, 2f, 7f, Paper);

            elements.machineLight = Bar(machine, "Indicator", 15f, machineHeight - 21f, 4f, 4f, Quiet);
            Bar(machine, "Rail Rule", 27f, machineHeight - 19f, 803f, 1f, Hex("#8CACC62B"));
            var skip = Box(machine, "Skip", 695f, machineHeight - 34f, 135f, 27f);
            elements.skip = Button(skip, Hex("#111F2D"));
            Label(Fill(skip, "Label"), font, "연출 건너뛰기  ››", 11f, Paper, TextAlignmentOptions.Center);
            skip.gameObject.SetActive(false);
        }

        private static void AddResult(RectTransform window, TMP_FontAsset font, SupplyDepotView.Elements elements)
        {
            var panel = Box(window, "Result Panel", 910f, 110f, 300f, 408f);
            Picture(panel, "Gradient", Visual("ResultGradient", 2, 128, (x, y) =>
                Color.Lerp(Hex("#0B1624"), Hex("#132131"), y / 127f)), false);
            Outline(panel, "#9FBBD137");
            elements.resultTopBorder = Bar(panel, "Top Highlight", 0f, 0f, 300f, 1f, Hex("#C5D6E5D2"));
            elements.resultAccent = Bar(panel, "Accent", 0f, 0f, 40f, 2f, Red);
            Label(Box(panel, "Heading", 17f, 12f, 264f, 28f), font, "획득 아이템", 15f, Paper);

            var empty = Center(panel, "Empty", 260f, 210f);
            empty.anchoredPosition = new Vector2(0f, -18f);
            elements.empty = empty.gameObject;
            var diamond = Center(empty, "Diamond", 96f, 96f);
            diamond.anchoredPosition = new Vector2(0f, 28f);
            FillImage(diamond, Hex("#11223370"));
            Outline(diamond, "#839FB748");
            diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var question = Center(empty, "Question", 96f, 96f);
            question.anchoredPosition = diamond.anchoredPosition;
            Label(question, font, "?", 38f, Hex("#7992A9AA"), TextAlignmentOptions.Center, FontStyles.Italic);
            Label(Box(empty, "Empty State", 0f, 155f, 260f, 25f), font, "미확정", 15f, Quiet,
                TextAlignmentOptions.Center);
            Label(Box(empty, "Dash", 0f, 181f, 260f, 20f), font, "—", 13f, Quiet,
                TextAlignmentOptions.Center);

            var filled = Box(panel, "Filled", 17f, 48f, 266f, 341f);
            elements.filled = filled.gameObject;
            elements.resultFade = filled.gameObject.AddComponent<CanvasGroup>();
            elements.resultArt = Picture(Box(filled, "Result Art", 0f, 0f, 69f, 69f), "Image", null);
            elements.resultStars = Label(Box(filled, "Grade", 75f, 10f, 185f, 20f), font, "", 15f, Paper);
            elements.resultName = Label(Box(filled, "Item Name", 75f, 31f, 185f, 31f), font, "", 19f, Paper,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            elements.resultName.enableAutoSizing = true;
            elements.resultName.fontSizeMin = 13f;
            elements.resultName.fontSizeMax = 19f;

            var details = Box(filled, "Details Scroll", 0f, 80f, 266f, 261f);
            elements.details = details.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            elements.details.horizontal = false;
            elements.details.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            elements.details.scrollSensitivity = 24f;
            var viewport = Fill(details, "Viewport", right: 12f);
            FillImage(viewport, Color.white, true);
            viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
            var content = TopStretch(viewport, "Content");
            Vertical(content, 12f);
            var fitter = content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            elements.details.viewport = viewport;
            elements.details.content = content;
            AddScrollbar(details, elements.details);

            var individual = TopStretch(content, "Individual Effect");
            FillImage(individual, Hex("#142435"));
            Vertical(individual, 3f, new RectOffset(11, 10, 10, 10));
            Label(TopStretch(individual, "Heading"), font, "개별 효과", 11f, Subtle);
            elements.effect = Label(TopStretch(individual, "Effect"), font, "", 12f, Paper);
            elements.effect.enableWordWrapping = true;

            var synergies = TopStretch(content, "Synergies");
            Vertical(synergies, 8f);
            elements.synergySection = synergies.gameObject;
            var rule = TopStretch(synergies, "Rule");
            FillImage(rule, Hex("#B6A58B40"));
            rule.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 1f;
            Label(TopStretch(synergies, "Heading"), font, "시너지 효과", 11f, Subtle);
            elements.synergyList = TopStretch(synergies, "Groups");
            Vertical(elements.synergyList, 14f);
            elements.synergyTemplate = AddSynergyTemplate(elements.synergyList, font);
            filled.gameObject.SetActive(false);
        }

        private static SupplyDepotView.SynergyRow AddSynergyTemplate(RectTransform groups, TMP_FontAsset font)
        {
            var row = TopStretch(groups, "SynergyTemplate");
            Vertical(row, 4f);
            var header = TopStretch(row, "Header");
            header.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 22f;
            var icon = FillImage(Box(header, "Icon", 0f, 1f, 20f, 20f), Color.white);
            icon.preserveAspect = true;
            var name = Label(Fill(header, "Name", left: 29f), font, "", 14f, Hex("#E8C17F"),
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            var threshold = Label(TopStretch(row, "Thresholds"), font, "", 11f, Subtle);
            var lines = TopStretch(row, "Lines");
            Vertical(lines, 5f, new RectOffset(0, 0, 3, 0));
            var template = Label(TopStretch(lines, "LineTemplate"), font, "", 12f, Paper);
            template.enableWordWrapping = true;
            template.richText = true;
            template.gameObject.SetActive(false);
            row.gameObject.SetActive(false);
            return new SupplyDepotView.SynergyRow
            {
                root = row,
                icon = icon,
                synergyName = name,
                thresholds = threshold,
                lines = lines,
                lineTemplate = template,
            };
        }

        private static void AddScrollbar(RectTransform details, UnityEngine.UI.ScrollRect scroll)
        {
            var track = Box(details, "Scrollbar", 259f, 0f, 6f, 261f);
            FillImage(track, Clear, true);
            var area = Fill(track, "Sliding Area");
            var handle = FillImage(Fill(area, "Handle"), Hex("#708DA763"), true);
            var scrollbar = track.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();
            scrollbar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handle.rectTransform;
            scrollbar.targetGraphic = handle;
            scrollbar.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;
        }

        private static void AddFooter(RectTransform window, SupplyDepotCatalog catalog,
            SupplyDepotView.Elements elements, out UnityEngine.UI.Image costSeal)
        {
            var footer = Box(window, "Footer", 40f, 538f, 1170f, 64f);
            Bar(footer, "Rule", 0f, 0f, 1170f, 1f, Border);
            Label(Box(footer, "Cost Caption", 0f, 16f, 68f, 48f), catalog.font, "보급 비용", 12f, Subtle);
            costSeal = Picture(Box(footer, "Cost Seal", 68f, 30f, 13f, 21f), "Icon", catalog.currencyIcon);
            elements.price = Label(Box(footer, "Cost", 89f, 15f, 100f, 50f), catalog.font, "100", 23f, Paper);
            elements.draw = RedButton(Box(footer, "Draw", 980f, 16f, 190f, 48f), catalog.font,
                "F", "보급 받기", out elements.drawLabel, out elements.drawSlash);
        }

        private static UnityEngine.UI.Button RedButton(RectTransform rect, TMP_FontAsset font,
            string shortcut, string title, out TMP_Text label, out UnityEngine.UI.Image slash)
        {
            var sprite = Visual("DrawButton", 190, 48, (x, y) =>
            {
                int fromTop = 47 - y;
                int left = fromTop < 12 ? 12 - fromTop : 0;
                int right = 189 - (47 - fromTop) / 4;
                if (x < left || x > right)
                    return Clear;
                return y == 0 || y == 47 || x == left || x == right ? Color.white :
                    Color.Lerp(Hex("#CFD3DA"), Color.white, x / 189f);
            });
            var button = Button(rect, Color.white, sprite);
            var colors = button.colors;
            colors.disabledColor = Hex("#23384E");
            colors.normalColor = Hex("#ED2D4D");
            colors.highlightedColor = Hex("#FF526C");
            colors.pressedColor = Hex("#BD1D39");
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;

            if (shortcut != null)
            {
                var key = Box(rect, "Shortcut", 42f, 16f, 16f, 17f);
                Outline(key, "#FFC3CF80");
                Label(Fill(key, "Key"), font, shortcut, 10f, Paper, TextAlignmentOptions.Center);
            }
            label = Label(Box(rect, "Label", shortcut == null ? 0f : 64f, 0f, shortcut == null ? 190f : 101f, 48f),
                font, title, 17f, Paper, shortcut == null ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft);
            slash = Bar(rect, "Slash", 173f, 4f, 4f, 40f, Hex("#F18B9E"));
            slash.rectTransform.pivot = CenterPoint;
            slash.rectTransform.anchoredPosition = new Vector2(175f, -24f);
            slash.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -14f);
            return button;
        }

        private static void BuildScene(GameObject prefab, SupplyDepotCatalog catalog, NewSceneMode sceneMode)
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene demo = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, sceneMode);
            try
            {
                SceneManager.SetActiveScene(demo);
                var camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Hex("#080F19");
                camera.tag = "MainCamera";

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, demo);
                var view = instance.GetComponent<SupplyDepotView>();
                var controller = instance.GetComponent<SupplyDepotController>();
                var account = new GameObject("Demo Supply Account").AddComponent<DemoSupplyDepotAccount>();
                controller.openOnStart = true;
                controller.accountProvider = account;
                view.EnableEditorDemoReopen();
                PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
                PrefabUtility.RecordPrefabInstancePropertyModifications(view);

                var backdrop = Fill(instance.transform, "Demo Backdrop");
                backdrop.SetAsFirstSibling();
                Picture(backdrop, "Hub", catalog.backdrop, false).color = Hex("#555E6B");
                FillImage(Fill(backdrop, "Shade"), Hex("#030A13A8"));
                var location = Box(backdrop, "Location", 32f, 27f, 185f, 18f);
                var brand = Label(Box(location, "Brand", 0f, 0f, 74f, 18f), catalog.font, "경계 ZERO", 10f, Subtle);
                brand.characterSpacing = 9f;
                Bar(location, "Rule", 76f, 8f, 18f, 1f, Quiet);
                Label(Box(location, "Hub", 108f, 0f, 75f, 18f), catalog.font, "거점", 10f, Paper);

                new GameObject("EventSystem", typeof(EventSystem), typeof(SupplyDepotDemoInput));
                if (!EditorSceneManager.SaveScene(demo, ScenePath))
                    throw new IOException($"Could not save {ScenePath}.");
            }
            finally
            {
                if (sceneMode == NewSceneMode.Additive)
                {
                    if (previous.IsValid() && previous.isLoaded)
                        SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(demo, true);
                }
            }
        }

        private static Sprite Visual(string name, int width, int height, Func<int, int, Color> pixel)
        {
            string path = $"{Root}/Settings/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null)
                return existing;

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.name = $"{name} Texture";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = pixel(x, y);
            texture.SetPixels(pixels);
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), CenterPoint, 100f,
                0, SpriteMeshType.FullRect);
            sprite.name = name;
            AssetDatabase.CreateAsset(sprite, path);
            AssetDatabase.AddObjectToAsset(texture, sprite);
            return sprite;
        }

        private static Color Hex(string value)
        {
            if (ColorUtility.TryParseHtmlString(value, out var color))
                return color;
            throw new InvalidDataException($"Invalid supply depot color '{value}'.");
        }

        private static RectTransform RectChild(Transform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            child.SetParent(parent, false);
            return child;
        }

        private static RectTransform Fill(Transform parent, string name, float left = 0f, float right = 0f)
        {
            var child = RectChild(parent, name);
            child.anchorMin = Vector2.zero;
            child.anchorMax = Vector2.one;
            child.offsetMin = new Vector2(left, 0f);
            child.offsetMax = new Vector2(-right, 0f);
            return child;
        }

        private static RectTransform Center(Transform parent, string name, float width, float height)
        {
            var child = RectChild(parent, name);
            child.anchorMin = child.anchorMax = child.pivot = CenterPoint;
            child.sizeDelta = new Vector2(width, height);
            return child;
        }

        private static RectTransform Box(Transform parent, string name, float x, float y, float width, float height)
        {
            var child = RectChild(parent, name);
            child.anchorMin = child.anchorMax = child.pivot = TopLeft;
            child.anchoredPosition = new Vector2(x, -y);
            child.sizeDelta = new Vector2(width, height);
            return child;
        }

        private static RectTransform TopStretch(Transform parent, string name)
        {
            var child = RectChild(parent, name);
            child.anchorMin = TopLeft;
            child.anchorMax = Vector2.one;
            child.pivot = new Vector2(0.5f, 1f);
            child.sizeDelta = Vector2.zero;
            return child;
        }

        private static UnityEngine.UI.Image FillImage(RectTransform rect, Color color, bool raycast = false)
        {
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private static UnityEngine.UI.Image Picture(RectTransform parent, string name, Sprite sprite,
            bool preserveAspect = true, bool raycast = false)
        {
            var image = FillImage(Fill(parent, name), Color.white, raycast);
            image.sprite = sprite;
            image.preserveAspect = preserveAspect;
            return image;
        }

        private static UnityEngine.UI.Image Bar(Transform parent, string name, float x, float y,
            float width, float height, Color color) => FillImage(Box(parent, name, x, y, width, height), color);

        private static UnityEngine.UI.Image[] Outline(RectTransform rect, string color)
        {
            var top = FillImage(Edge(rect, "Top Edge", new Vector2(0f, 1f), Vector2.one,
                new Vector2(0f, -1f), Vector2.zero), Hex(color));
            var bottom = FillImage(Edge(rect, "Bottom Edge", Vector2.zero, new Vector2(1f, 0f),
                Vector2.zero, new Vector2(0f, 1f)), Hex(color));
            var left = FillImage(Edge(rect, "Left Edge", Vector2.zero, new Vector2(0f, 1f),
                Vector2.zero, new Vector2(1f, 0f)), Hex(color));
            var right = FillImage(Edge(rect, "Right Edge", new Vector2(1f, 0f), Vector2.one,
                new Vector2(-1f, 0f), Vector2.zero), Hex(color));
            return new[] { top, bottom, left, right };
        }

        private static RectTransform Edge(Transform parent, string name, Vector2 minAnchor, Vector2 maxAnchor,
            Vector2 minOffset, Vector2 maxOffset)
        {
            var child = RectChild(parent, name);
            child.anchorMin = minAnchor;
            child.anchorMax = maxAnchor;
            child.offsetMin = minOffset;
            child.offsetMax = maxOffset;
            return child;
        }

        private static TMP_Text Label(RectTransform rect, TMP_FontAsset font, string content, float size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, FontStyles style = FontStyles.Normal)
        {
            // TMP can apply its default dimensions as the component is first initialized.
            Vector2 sizeDelta = rect.sizeDelta;
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            rect.sizeDelta = sizeDelta;
            label.font = font;
            label.fontSize = size;
            label.color = color;
            label.fontStyle = style;
            label.text = content;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.richText = false;
            label.enableWordWrapping = false;
            return label;
        }

        private static UnityEngine.UI.Button Button(RectTransform rect, Color color, Sprite sprite = null)
        {
            var image = FillImage(rect, color, true);
            image.sprite = sprite;
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Hex("#D8EEFF");
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = Hex("#9AB0C7");
            colors.disabledColor = Hex("#718396");
            button.colors = colors;
            return button;
        }

        private static void Vertical(RectTransform rect, float spacing, RectOffset padding = null)
        {
            var layout = rect.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding ?? new RectOffset();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperLeft;
        }
    }
}

