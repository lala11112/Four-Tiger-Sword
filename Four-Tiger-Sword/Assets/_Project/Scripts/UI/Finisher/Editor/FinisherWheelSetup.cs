using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Creates the editable wheel prefab once and preserves authored UI resources.</summary>
public static class FinisherWheelSetup
{
    private const string Folder = "Assets/_Project/Scripts/UI/Finisher/Resources";
    private const string Path = Folder + "/FinisherWheelResources.asset";
    private const string PrefabFolder = "Assets/_Project/Scripts/UI/Finisher/Prefabs";
    private const string PrefabPath = PrefabFolder + "/FinisherWheel.prefab";

    [MenuItem("Tools/Studio Baekho/Finisher/Ensure UI Resources")]
    public static void InstallMenu() => Debug.Log(EnsureResources());

    public static string EnsureResources()
    {
        if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Create finisher UI resources in Edit Mode only.");
        EnsureFolder(Folder);
        var resources = AssetDatabase.LoadAssetAtPath<FinisherWheelResources>(Path);
        if (resources == null)
        {
            RequireUnusedAssetPath(Path);
            resources = ScriptableObject.CreateInstance<FinisherWheelResources>();
            AssetDatabase.CreateAsset(resources, Path);
        }
        var font = BoundaryZeroTypography.GetFont();
        if (resources.Font != font)
        {
            resources.Configure(font);
            EditorUtility.SetDirty(resources);
        }
        if (resources.Prefab == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            FinisherWheelView view;
            if (prefab != null)
            {
                view = prefab.GetComponent<FinisherWheelView>();
                ValidatePrefab(view, PrefabPath);
            }
            else
            {
                RequireUnusedAssetPath(PrefabPath);
                view = CreateDefaultPrefab(resources.Font);
            }
            resources.ConfigurePrefab(view);
            EditorUtility.SetDirty(resources);
        }
        else
            ValidatePrefab(resources.Prefab, AssetDatabase.GetAssetPath(resources.Prefab));
        AssetDatabase.SaveAssetIfDirty(resources);
        return "Finisher wheel resources ready: " + Path + "; prefab: " + AssetDatabase.GetAssetPath(resources.Prefab);
    }

    public static string EnsurePrefab() => EnsureResources();

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        int separator = folder.LastIndexOf('/');
        string parent = folder.Substring(0, separator);
        EnsureFolder(parent);
        if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, folder.Substring(separator + 1))))
            throw new InvalidOperationException("Could not create the finisher UI folder: " + folder);
    }

    private static void RequireUnusedAssetPath(string path)
    {
        string absolute = System.IO.Path.Combine(Application.dataPath, path.Substring("Assets/".Length));
        if (AssetDatabase.LoadMainAssetAtPath(path) != null || System.IO.File.Exists(absolute))
            throw new InvalidOperationException("The existing finisher UI asset has an unexpected type and will not be overwritten: " + path);
    }

    private static FinisherWheelView CreateDefaultPrefab(TMP_FontAsset font)
    {
        EnsureFolder(PrefabFolder);
        var preview = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        try
        {
            root = EditorUtility.CreateGameObjectWithHideFlags("FinisherWheel", HideFlags.DontSave,
                typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            SceneManager.MoveGameObjectToScene(root, preview);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 175;
            var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var content = Rect("Content", root.transform, Vector2.zero, Vector2.zero);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = content.offsetMax = Vector2.zero;
            var shade = content.gameObject.AddComponent<UnityEngine.UI.Image>();
            shade.color = new Color(0.012f, 0.02f, 0.035f, 0.55f);
            shade.raycastTarget = false;
            var wheelRect = Rect("Wheel", content, Vector2.zero, new Vector2(520f, 520f));
            var wheel = wheelRect.gameObject.AddComponent<FinisherWheelGraphic>();
            wheel.raycastTarget = false;
            Text(font, "Title", content, "변형 3타", new Vector2(0f, 344f), new Vector2(520f, 48f), 31f,
                new Color(0.96f, 0.97f, 1f));
            Text(font, "Instruction", content, "우클릭을 놓아 선택", new Vector2(0f, 302f), new Vector2(520f, 34f), 19f,
                new Color(0.73f, 0.79f, 0.87f));

            var elements = new TMP_Text[5];
            var roles = new TMP_Text[5];
            for (int i = 0; i < 5; i++)
            {
                float angle = i * 72f * Mathf.Deg2Rad;
                Vector2 position = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * 174f;
                var labelRoot = Rect("Sector" + FinisherWheelSelection.ElementAt(i), wheelRect, position, new Vector2(136f, 84f));
                elements[i] = Text(font, "Element", labelRoot, Name(FinisherWheelSelection.ElementAt(i)),
                    new Vector2(0f, 15f), new Vector2(136f, 45f), 34f, FinisherWheelGraphic.ElementColor(i));
                roles[i] = Text(font, "Role", labelRoot, "기본 3타", new Vector2(0f, -23f),
                    new Vector2(146f, 28f), 16f, new Color(0.70f, 0.76f, 0.83f));
            }
            var centerName = Text(font, "CurrentElement", wheelRect, "화", new Vector2(0f, 19f),
                new Vector2(150f, 48f), 31f, Color.white);
            Text(font, "CenterRole", wheelRect, "현재 폼 3타", new Vector2(0f, -24f), new Vector2(152f, 32f), 17f,
                new Color(0.75f, 0.81f, 0.87f));
            var selectionLabel = Text(font, "Selection", content, "화  ·  기본 3타", new Vector2(0f, -292f),
                new Vector2(640f, 40f), 24f, Color.white);
            Text(font, "Help", content, "중앙: 현재 폼 3타    ·    Esc: 취소", new Vector2(0f, -335f),
                new Vector2(740f, 32f), 18f, new Color(0.72f, 0.78f, 0.84f));
            var cursor = Rect("VirtualCursor", wheelRect, Vector2.zero, new Vector2(14f, 14f));
            cursor.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var cursorOutline = cursor.gameObject.AddComponent<UnityEngine.UI.Image>();
            cursorOutline.color = new Color(0.02f, 0.03f, 0.05f, 1f);
            cursorOutline.raycastTarget = false;
            var cursorCenter = Rect("Center", cursor, Vector2.zero, new Vector2(8f, 8f));
            var cursorFill = cursorCenter.gameObject.AddComponent<UnityEngine.UI.Image>();
            cursorFill.color = Color.white;
            cursorFill.raycastTarget = false;
            cursor.SetParent(content, false);
            cursor.SetAsLastSibling();

            var view = root.AddComponent<FinisherWheelView>();
            var serialized = new SerializedObject(view);
            Required(serialized, "canvas").objectReferenceValue = canvas;
            Required(serialized, "content").objectReferenceValue = content;
            Required(serialized, "virtualCursor").objectReferenceValue = cursor;
            Required(serialized, "wheel").objectReferenceValue = wheel;
            Required(serialized, "centerName").objectReferenceValue = centerName;
            Required(serialized, "selectionLabel").objectReferenceValue = selectionLabel;
            var sectors = Required(serialized, "sectors");
            sectors.arraySize = 5;
            for (int i = 0; i < 5; i++)
            {
                var sector = sectors.GetArrayElementAtIndex(i);
                sector.FindPropertyRelative("Element").intValue = (int)FinisherWheelSelection.ElementAt(i);
                sector.FindPropertyRelative("DisplayName").stringValue = Name(FinisherWheelSelection.ElementAt(i));
                sector.FindPropertyRelative("ElementLabel").objectReferenceValue = elements[i];
                sector.FindPropertyRelative("RoleLabel").objectReferenceValue = roles[i];
                sector.FindPropertyRelative("SelectedVisual").objectReferenceValue = null;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            canvas.enabled = true;
            content.gameObject.SetActive(true);
            root.SetActive(true);
            if (!view.ValidateBindings(out string bindingError))
                throw new InvalidOperationException("The generated finisher wheel has invalid bindings: " + bindingError);
            // The preview scene isolates construction. Strip the temporary flag only while serializing the asset.
            GameObject saved;
            root.hideFlags = HideFlags.None;
            try { saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); }
            finally { root.hideFlags = HideFlags.DontSave; }
            if (saved == null)
                throw new InvalidOperationException("Could not save the default finisher wheel prefab: " + PrefabPath);
            var result = saved.GetComponent<FinisherWheelView>();
            ValidatePrefab(result, PrefabPath);
            return result;
        }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static void ValidatePrefab(FinisherWheelView view, string path)
    {
        if (view == null || !EditorUtility.IsPersistent(view) || !PrefabUtility.IsPartOfPrefabAsset(view))
            throw new InvalidOperationException("The finisher UI reference must point to a prefab containing FinisherWheelView: " + path);
        if (!view.ValidateBindings(out string error))
            throw new InvalidOperationException("The finisher wheel prefab has invalid bindings: " + path + "; " + error);
    }

    private static SerializedProperty Required(SerializedObject serialized, string field)
        => serialized.FindProperty(field) ?? throw new InvalidOperationException("FinisherWheelView is missing serialized field '" + field + "'.");

    private static TMP_Text Text(TMP_FontAsset font, string objectName, Transform parent, string value,
        Vector2 position, Vector2 size, float fontSize, Color color)
    {
        RectTransform rect = Rect(objectName, parent, position, size);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.raycastTarget = false;
        text.richText = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }

    private static RectTransform Rect(string objectName, Transform parent, Vector2 position, Vector2 size)
    {
        var go = EditorUtility.CreateGameObjectWithHideFlags(objectName, HideFlags.DontSave, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.hideFlags = HideFlags.None;
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static string Name(ElementType element)
    {
        switch (element)
        {
            case ElementType.ELEMENT_FIRE: return "화";
            case ElementType.ELEMENT_EARTH: return "토";
            case ElementType.ELEMENT_GOLD: return "금";
            case ElementType.ELEMENT_WATER: return "수";
            case ElementType.ELEMENT_WOOD: return "목";
            default: return "현재";
        }
    }

    public static string RunSelectorRegression()
    {
        int checks = 0;
        Check(Vector2.zero, ElementType.ELEMENT_NONE, ref checks);
        Check(new Vector2(0f, 89.99f), ElementType.ELEMENT_NONE, ref checks);
        Check(new Vector2(0f, 90f), ElementType.ELEMENT_FIRE, ref checks);
        Check(new Vector2(float.NaN, 100f), ElementType.ELEMENT_NONE, ref checks);
        Check(new Vector2(float.PositiveInfinity, 100f), ElementType.ELEMENT_NONE, ref checks);
        for (int i = 0; i < 5; i++)
        {
            ElementType expected = FinisherWheelSelection.ElementAt(i);
            Check(Point(i * 72f, 170f), expected, ref checks);
            Check(Point(i * 72f, 900f), expected, ref checks);
            Check(Point(i * 72f + 35.99f, 170f), expected, ref checks);
            ElementType clockwise = FinisherWheelSelection.ElementAt((i + 1) % 5);
            Check(Point(i * 72f + 36f, 170f), clockwise, ref checks);
            Check(Point(i * 72f + 36.01f, 170f), clockwise, ref checks);
        }
        return checks + " finisher wheel selector checks passed.";
    }

    private static Vector2 Point(float angle, float radius)
    {
        float radians = angle * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians)) * radius;
    }

    private static void Check(Vector2 point, ElementType expected, ref int checks)
    {
        ElementType actual = FinisherWheelSelection.Resolve(point);
        if (actual != expected)
            throw new InvalidOperationException("Wheel selector at " + point + ": expected " + expected + ", got " + actual);
        checks++;
    }
}
