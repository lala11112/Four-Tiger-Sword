using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Penta.BossUI.Editor
{
    /// <summary>Installs the groggy strip in existing HP bars and packages the reusable assets.</summary>
    public static class BossGroggyDelivery
    {
        public const string PrefabPath = "Assets/UI/PentaBossGroggy/Prefabs/BossGroggyGauge.prefab";
        private const string ModulePath = "Assets/UI/PentaBossGroggy";
        private const string MenuPath = "GameObject/경계 ZERO/UI/보스 HP 아래에 그로기 게이지 추가";

        public static void Prepare()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null)
            {
                var priorSize = ((RectTransform)existing.transform).sizeDelta;
                if (!existing.GetComponent<CanvasRenderer>() || priorSize == new Vector2(640f, 22f))
                {
                    var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
                    try
                    {
                        if (!contents.GetComponent<CanvasRenderer>()) contents.AddComponent<CanvasRenderer>();
                        if (priorSize == new Vector2(640f, 22f)) ((RectTransform)contents.transform).sizeDelta = new Vector2(640f, 14f);
                        PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(contents); }
                }
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            AssetDatabase.Refresh();
            var go = new GameObject("BossGroggyGauge", typeof(RectTransform), typeof(CanvasRenderer), typeof(BossGroggyGauge));
            try
            {
                var rect = (RectTransform)go.transform;
                rect.sizeDelta = new Vector2(640f, 14f);
                var gauge = go.GetComponent<BossGroggyGauge>();
                gauge.raycastTarget = false;
                gauge.maskable = false;
                gauge.ResetGauge();
                PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [MenuItem(MenuPath, false, 20)]
        private static void AttachSelected()
        {
            try
            {
                Selection.activeGameObject = AttachTo((RectTransform)Selection.activeTransform).gameObject;
            }
            catch (ArgumentException exception)
            {
                Debug.LogError(exception.Message, Selection.activeGameObject);
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogError(exception.Message, Selection.activeGameObject);
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool CanAttachSelected() => Selection.activeTransform is RectTransform rect &&
            rect.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(rect);

        /// <summary>Splits the existing HP fill area into adjacent HP and groggy rows.</summary>
        public static BossGroggyHPIntegration AttachTo(RectTransform selected)
        {
            if (selected == null) throw new ArgumentNullException(nameof(selected));
            if (!selected.gameObject.scene.IsValid() || EditorUtility.IsPersistent(selected))
                throw new ArgumentException("Open the HP prefab in Prefab Mode or select its scene instance.", nameof(selected));

            var slider = selected.GetComponent<UnityEngine.UI.Slider>();
            if (slider == null)
            {
                foreach (var candidate in selected.GetComponentsInParent<UnityEngine.UI.Slider>(true))
                {
                    if (candidate.fillRect != selected) continue;
                    slider = candidate;
                    break;
                }
            }

            var hp = slider ? slider.fillRect : selected;
            var hpImage = hp ? hp.GetComponent<UnityEngine.UI.Image>() : null;
            bool horizontalFill = hpImage && hpImage.type == UnityEngine.UI.Image.Type.Filled &&
                hpImage.fillMethod == UnityEngine.UI.Image.FillMethod.Horizontal &&
                hpImage.fillOrigin == (int)UnityEngine.UI.Image.OriginHorizontal.Left;
            if (slider && slider.direction != UnityEngine.UI.Slider.Direction.LeftToRight)
                throw new InvalidOperationException("The HP Slider must fill from left to right.");
            if (!horizontalFill && !(slider && hpImage && hpImage.type == UnityEngine.UI.Image.Type.Simple))
                throw new InvalidOperationException("Select the red HP Fill Image (Filled / Horizontal / Left) or its left-to-right Slider.");

            var parent = hp.parent as RectTransform;
            if (!parent) throw new InvalidOperationException("The HP fill needs a RectTransform parent.");
            var existing = FindIntegration(parent, hpImage);
            if (existing) return existing;
            // A Slider uses a dedicated HP row so it can keep driving both fill anchors.
            if (slider && parent.parent is RectTransform outer)
            {
                existing = FindIntegration(outer, hpImage);
                if (existing) return existing;
            }

            if (hpImage.preserveAspect || hpImage.useSpriteMesh || hp.localRotation != Quaternion.identity || hp.localScale != Vector3.one)
                throw new InvalidOperationException("The HP fill must stretch its rectangular sprite with unit scale and no rotation.");
            if (hp.GetComponent<UnityEngine.UI.Mask>() || hp.GetComponent<UnityEngine.UI.RectMask2D>() ||
                hp.GetComponent<UnityEngine.UI.ContentSizeFitter>() || hp.GetComponent<UnityEngine.UI.AspectRatioFitter>() ||
                parent.GetComponent<UnityEngine.UI.LayoutGroup>())
                throw new InvalidOperationException("Use a freely anchored HP fill inside its clipping parent, without a layout driver on the fill or its parent.");

            var sprite = GetNeutralSprite(hpImage.overrideSprite);
            var min = hp.anchorMin;
            var max = hp.anchorMax;
            var lower = hp.offsetMin;
            var upper = hp.offsetMax;
            float splitAnchor = Mathf.Lerp(min.y, max.y, .25f);
            float splitOffset = Mathf.Lerp(lower.y, upper.y, .25f);
            float depth = hp.anchoredPosition3D.z;
            int hpIndex = hp.GetSiblingIndex();
            const string action = "Attach Groggy Inside Boss HP";
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(action);
            try
            {
                var track = CreateStrip("GroggyTrack", parent, sprite, hpImage.maskable, new Color32(44, 44, 44, 255), action);
                var trackRect = track.rectTransform;
                trackRect.SetSiblingIndex(hpIndex);
                SetEdges(trackRect, new Vector2(slider ? 0f : min.x, min.y),
                    new Vector2(slider ? 1f : max.x, splitAnchor), lower,
                    new Vector2(upper.x, splitOffset), depth);
                var fill = CreateStrip("GroggyFill", trackRect, sprite, hpImage.maskable, new Color32(155, 155, 155, 255), action);
                fill.type = UnityEngine.UI.Image.Type.Filled;
                fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
                fill.fillOrigin = (int)UnityEngine.UI.Image.OriginHorizontal.Left;
                SetEdges(fill.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 0f);

                var integration = Undo.AddComponent<BossGroggyHPIntegration>(track.gameObject);
                var serialized = new SerializedObject(integration);
                serialized.FindProperty("hpFill").objectReferenceValue = hpImage;
                serialized.FindProperty("track").objectReferenceValue = track;
                serialized.FindProperty("fill").objectReferenceValue = fill;
                serialized.ApplyModifiedProperties();
                integration.ResetGauge();

                if (slider)
                {
                    var row = CreateRect("BossHpRow", parent, action);
                    row.SetSiblingIndex(hpIndex + 1);
                    SetEdges(row, new Vector2(0f, splitAnchor), new Vector2(1f, max.y),
                        new Vector2(0f, splitOffset), new Vector2(0f, upper.y), depth);
                    Undo.SetTransformParent(hp, row, action);
                    Undo.RecordObject(hp, action);
                    SetEdges(hp, new Vector2(min.x, 0f), new Vector2(max.x, 1f),
                        new Vector2(lower.x, 0f), new Vector2(upper.x, 0f), 0f);
                }
                else
                {
                    Undo.RecordObject(hp, action);
                    SetEdges(hp, new Vector2(min.x, splitAnchor), max, new Vector2(lower.x, splitOffset), upper, depth);
                }

                PrefabUtility.RecordPrefabInstancePropertyModifications(hp);
                Undo.CollapseUndoOperations(group);
                return integration;
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }

        private static BossGroggyHPIntegration FindIntegration(RectTransform parent, UnityEngine.UI.Image hp)
        {
            foreach (Transform sibling in parent)
            {
                var integration = sibling.GetComponent<BossGroggyHPIntegration>();
                if (integration && integration.HpFill == hp) return integration;
            }
            return null;
        }

        private static RectTransform CreateRect(string name, RectTransform parent, string action)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, action);
            Undo.SetTransformParent(go.transform, parent, action);
            var rect = (RectTransform)go.transform;
            Undo.RecordObject(rect, action);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            return rect;
        }

        private static UnityEngine.UI.Image CreateStrip(string name, RectTransform parent, Sprite sprite,
            bool maskable, Color color, string action)
        {
            var rect = CreateRect(name, parent, action);
            var image = Undo.AddComponent<UnityEngine.UI.Image>(rect.gameObject);
            Undo.RecordObject(image, action);
            image.sprite = sprite;
            image.color = color;
            image.maskable = maskable;
            image.raycastTarget = false;
            return image;
        }

        private static void SetEdges(RectTransform rect, Vector2 min, Vector2 max, Vector2 lower, Vector2 upper, float depth)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = lower;
            rect.offsetMax = upper;
            var position = rect.anchoredPosition3D;
            position.z = depth;
            rect.anchoredPosition3D = position;
        }

        private static Sprite GetNeutralSprite(Sprite sprite)
        {
            if (!sprite) return null;
            if (sprite.packed)
                throw new InvalidOperationException("Unpack the HP sprite before attaching the groggy strip.");

            var rect = sprite.rect;
            int width = Mathf.RoundToInt(rect.width);
            int height = Mathf.RoundToInt(rect.height);
            var source = sprite.texture;
            Color32[] pixels = ReadSpritePixels(source, rect, width, height);
            bool colored = false;
            foreach (var pixel in pixels)
            {
                if (pixel.a == 0) continue;
                int darkest = Mathf.Min(pixel.r, Mathf.Min(pixel.g, pixel.b));
                int brightest = Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));
                if (brightest == darkest) continue;
                colored = true;
                break;
            }
            if (!colored) return sprite;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string guid, out long localId))
                throw new InvalidOperationException("Save the colored HP sprite as a project asset before attaching.");

            const string folder = ModulePath + "/Generated";
            string revision = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(sprite)).ToString();
            string path = $"{folder}/Neutral_{guid}_{localId}_{revision}.asset";
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is Sprite cached) return cached;

            for (int i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];
                byte luminance = (byte)Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));
                pixels[i] = new Color32(luminance, luminance, luminance, pixel.a);
            }

            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(ModulePath, "Generated");
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = sprite.name + "_Neutral",
                filterMode = source.filterMode,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            var neutral = Sprite.Create(texture, new Rect(0f, 0f, width, height),
                new Vector2(sprite.pivot.x / width, sprite.pivot.y / height),
                sprite.pixelsPerUnit, 0, SpriteMeshType.FullRect, sprite.border);
            neutral.name = texture.name;
            AssetDatabase.CreateAsset(texture, path);
            AssetDatabase.AddObjectToAsset(neutral, texture);
            AssetDatabase.SaveAssets();
            return neutral;
        }

        private static Color32[] ReadSpritePixels(Texture2D source, Rect rect, int width, int height)
        {
            if (source.isReadable)
            {
                var original = source.GetPixels32();
                var pixels = new Color32[width * height];
                int x = Mathf.RoundToInt(rect.x);
                int y = Mathf.RoundToInt(rect.y);
                for (int row = 0; row < height; row++)
                    Array.Copy(original, (y + row) * source.width + x, pixels, row * width, width);
                return pixels;
            }
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("Use an editor with graphics enabled, or make the HP sprite texture readable for batch attachment.");

            var active = RenderTexture.active;
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                var scale = new Vector2(rect.width / source.width, rect.height / source.height);
                var offset = new Vector2(rect.x / source.width, rect.y / source.height);
                Graphics.Blit(source, target, scale, offset);
                RenderTexture.active = target;
                readable.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                return readable.GetPixels32();
            }
            finally
            {
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(readable);
            }
        }

        public static void Export()
        {
            Prepare();
            ValidateImported();
            BossHealthGroggyPrefabBuilder.Prepare();
            BossHealthGroggyPrefabBuilder.ValidatePrefab();
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Deliverables/14_보스_그로기/BoundaryZERO_BossGroggy.unitypackage"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            // Include the editable sample's actual art, font and shader dependencies.
            var paths = new System.Collections.Generic.List<string>
            {
                ModulePath + "/Runtime", ModulePath + "/Editor", ModulePath + "/Prefabs"
            };
            if (AssetDatabase.IsValidFolder(ModulePath + "/Samples")) paths.Add(ModulePath + "/Samples");
            string sample = ModulePath + "/Samples/BossHPWithGroggy_Sample.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(sample))
                foreach (string dependency in AssetDatabase.GetDependencies(sample, true))
                    if (dependency.StartsWith("Assets/", StringComparison.Ordinal) && !paths.Contains(dependency))
                        paths.Add(dependency);
            const string fontLicense = "Assets/UI/PentaUpgrade/Art/Pretendard-LICENSE.txt";
            if (File.Exists(fontLicense)) paths.Add(fontLicense);
            const string fontShaderProperties = "Assets/TextMesh Pro/Shaders/TMPro_Properties.cginc";
            if (paths.Contains("Assets/TextMesh Pro/Shaders/TMP_SDF-Mobile.shader"))
                paths.Add(fontShaderProperties);
            foreach (string file in Directory.GetFiles(ModulePath, "*", SearchOption.TopDirectoryOnly))
                if (Path.GetFileName(file).StartsWith("README", StringComparison.OrdinalIgnoreCase)
                    && !file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) paths.Add(file.Replace('\\', '/'));
            AssetDatabase.ExportPackage(paths.ToArray(), output, ExportPackageOptions.Recurse);
            File.Copy(output, Path.Combine(Path.GetDirectoryName(output), "BoundaryZERO_BossHP_Groggy_Replacement.unitypackage"), true);
            Debug.Log("Boss groggy production package exported: " + Path.GetFullPath(output));
        }

        /// <summary>Validates in an isolated preview scene without saving scenes or changing assets.</summary>
        public static void ValidateImported()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Require(prefab != null, "Production prefab is missing. Run Prepare first.");
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0, "Missing prefab script.");
            var source = prefab.GetComponent<BossGroggyGauge>();
            Require(prefab.GetComponent<CanvasRenderer>() != null, "Gauge has no CanvasRenderer.");
            Require(source != null && source.Normalized == 0f && !source.IsBroken, "Prefab must start at zero.");
            Require(!source.raycastTarget && !source.maskable, "Prefab must ignore input and parent clipping.");
            Require(prefab.GetComponentInChildren<Mask>(true) == null && prefab.GetComponentInChildren<RectMask2D>(true) == null, "Prefab must not contain clipping masks.");
            Require(prefab.GetComponentInChildren<Canvas>(true) == null, "Prefab must not contain a Canvas.");
            Require(prefab.GetComponentInChildren<UnityEngine.EventSystems.EventSystem>(true) == null, "Prefab must not contain an EventSystem.");
            Require(prefab.GetComponentsInChildren<Graphic>(true).Length == 1, "Prefab must contain only the groggy graphic, with no HP/background.");
            Require(source.rectTransform.sizeDelta == new Vector2(640f, 14f), "Prefab must be 640 x 14.");
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                ValidateAttachment(scene, false);
                ValidateAttachment(scene, true);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            Debug.Log("Boss groggy prefab and attachment validation passed.");
        }

        private static void ValidateAttachment(UnityEngine.SceneManagement.Scene scene, bool useSlider)
        {
            var root = new GameObject("Temporary HP validation", typeof(RectTransform), typeof(UnityEngine.UI.RectMask2D));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var frame = (RectTransform)root.transform;
            frame.sizeDelta = new Vector2(1000f, 40f);
            var go = new GameObject("HP Fill", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            var hp = go.GetComponent<UnityEngine.UI.Image>();
            hp.rectTransform.SetParent(frame, false);
            SetEdges(hp.rectTransform, Vector2.zero, Vector2.one, new Vector2(4f, 2f), new Vector2(-4f, -2f), 0f);
            var slider = useSlider ? root.AddComponent<UnityEngine.UI.Slider>() : null;
            if (slider)
            {
                slider.fillRect = hp.rectTransform;
                slider.normalizedValue = .5f;
            }
            else
            {
                hp.type = UnityEngine.UI.Image.Type.Filled;
                hp.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
                hp.fillAmount = .5f;
            }

            var selection = slider ? frame : hp.rectTransform;
            var first = AttachTo(selection);
            Require(first == AttachTo(selection) && first.HpFill == hp, "Attachment must be idempotent and preserve the HP Image.");
            Require(first.Track.rectTransform.parent == frame && first.Track.maskable && first.Fill.maskable &&
                !first.Track.raycastTarget && !first.Fill.raycastTarget, "Both groggy graphics must stay within the HP clip and ignore input.");
            Require(slider ? slider.fillRect == hp.rectTransform && Mathf.Approximately(slider.normalizedValue, .5f)
                : Mathf.Approximately(hp.fillAmount, .5f), "Attachment changed the HP state.");

            RectTransform track = first.Track.rectTransform;
            Require(Mathf.Approximately(track.rect.width, 992f) && Mathf.Approximately(track.rect.height, 9f),
                "Groggy must occupy the bottom quarter of the full HP fill area.");
            Require(Vector3.Distance(track.TransformPoint(track.rect.xMin, track.rect.yMax, 0),
                hp.rectTransform.TransformPoint(hp.rectTransform.rect.xMin, hp.rectTransform.rect.yMin, 0)) < .001f,
                "The left edges must align and the groggy top must meet the HP bottom.");
            if (slider)
            {
                foreach (float health in new[] { 0f, .5f, 1f })
                {
                    slider.normalizedValue = health;
                    Require(Mathf.Approximately(track.rect.width, 992f), "Slider health changed the groggy width.");
                }
            }
            Require(Mathf.Approximately(track.rect.width, hp.rectTransform.rect.width), "Full HP and groggy widths differ.");
            frame.sizeDelta = new Vector2(500f, 20f);
            Require(Mathf.Approximately(track.rect.width, 492f) && Mathf.Approximately(track.rect.height, 4f),
                "Groggy must resize with the existing HP anchors and padding.");
            first.SetGroggy(3f, 4f);
            Require(Mathf.Approximately(first.Fill.fillAmount, .25f) && !first.IsBroken, "The groggy fill must decrease as buildup increases.");
            first.SetRemaining(0f);
            Require(first.Fill.fillAmount == 0f && first.IsBroken, "The groggy fill must empty at the break threshold.");
            first.ResetGauge();
            Require(first.Fill.fillAmount == 1f && first.Normalized == 0f && !first.IsBroken, "Reset must restore the full groggy fill.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
