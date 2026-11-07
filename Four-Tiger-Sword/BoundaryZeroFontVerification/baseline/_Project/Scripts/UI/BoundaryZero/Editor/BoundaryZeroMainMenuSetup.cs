using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>Authors the supplied main-menu layout as a standalone entry scene.</summary>
public static class BoundaryZeroMainMenuSetup
{
    public const string ScenePath = "Assets/_Project/Scenes/TitleScene.unity";
    private const string SpriteRoot = "Assets/_Project/Sprites/UI/BoundaryZERO/Main/";

    [MenuItem("Tools/Studio Baekho/BoundaryZERO/Create Main Scene")]
    public static string Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Create the main scene in Edit Mode.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            throw new InvalidOperationException("TitleScene already exists; edit it in place instead of replacing it.");
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(12, 21, 32, 255);
            camera.cullingMask = 0;
            cameraObject.transform.position = new Vector3(0, 0, -10);

            var root = new GameObject("BoundaryZeroMainMenu", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster), typeof(TitleManager));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1440);
            scaler.matchWidthOrHeight = .5f;
            var backdrop = Rect("Backdrop", root.transform, 0, 0, 2560, 1440);
            backdrop.anchorMin = Vector2.zero; backdrop.anchorMax = Vector2.one;
            backdrop.offsetMin = backdrop.offsetMax = Vector2.zero;
            backdrop.gameObject.AddComponent<BoundaryZeroMainBackdrop>().raycastTarget = false;

            var frame = Rect("ReferenceFrame", root.transform, 0, 0, 2560, 1440);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(.5f, .5f);
            frame.anchoredPosition = Vector2.zero;
            frame.gameObject.AddComponent<BoundaryZeroWindowFit>().Configure(new Vector2(2560, 1440), 1, 0);
            var blade = Rect("BladeLogoParent", frame, 166, 238, 1120, 660);
            Picture("BladeLogo", blade, "blade_logo.png", 80, 47, 938, 603);
            var korean = Rect("KoreanWordmarkParent", frame, 366, 857, 760, 109);
            Picture("KoreanWordmark", korean, "korean_wordmark.png", 1, 0, 128, 64);
            var zero = Rect("ZeroWordmarkParent", frame, 364, 1002, 810, 208);
            Picture("ZeroWordmark", zero, "zero_wordmark.png", 7, 13, 802, 176);
            var accent = Rect("WordmarkAccent", frame, 376, 982, 64, 4).gameObject.AddComponent<UnityEngine.UI.Image>();
            accent.color = new Color32(235, 37, 72, 255); accent.raycastTarget = false;

            var start = MenuButton(frame, "NewGame", "새 게임", 0, true);
            MenuButton(frame, "Continue", "이어하기", 1, false);
            MenuButton(frame, "Settings", "설정", 2, false);
            var quit = MenuButton(frame, "Quit", "종료", 3, true);
            var startNavigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit,
                selectOnDown = quit, selectOnUp = quit };
            start.navigation = startNavigation;
            quit.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit,
                selectOnDown = start, selectOnUp = start };

            var overlayPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/BoundaryZeroRunOverlay.prefab");
            if (overlayPrefab == null) throw new InvalidOperationException("The authored loading overlay is missing.");
            var overlay = ((GameObject)PrefabUtility.InstantiatePrefab(overlayPrefab, scene)).GetComponent<BoundaryZeroRunOverlay>();
            overlay.Hide(); overlay.StageLabel.gameObject.SetActive(false);
            var manager = root.GetComponent<TitleManager>();
            manager.Configure(start, quit, overlay);
            UnityEventTools.AddPersistentListener(start.onClick, manager.OnStartButton);
            UnityEventTools.AddPersistentListener(quit.onClick, manager.OnQuitButton);

            var eventObject = new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            var input = eventObject.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            input.AssignDefaultActions();
            eventObject.GetComponent<EventSystem>().firstSelectedGameObject = start.gameObject;

            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Could not save TitleScene.");
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            return "Created TitleScene with BoundaryZERO main art, start/quit bindings, loading overlay, and first build-scene registration.";
        }
        finally
        {
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static UnityEngine.UI.Button MenuButton(Transform parent, string name, string text, int row, bool available)
    {
        var image = Picture(name, parent, "menu_selected.png", 1600, 470 + row * 130, 700, 103);
        image.raycastTarget = true;
        var button = image.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.normalColor = new Color(1, 1, 1, 0);
        colors.highlightedColor = colors.selectedColor = Color.white;
        colors.pressedColor = new Color(.75f, .8f, .9f, 1);
        colors.disabledColor = new Color(1, 1, 1, 0);
        colors.fadeDuration = .1f;
        button.colors = colors;
        button.interactable = available;
        var label = Rect("Label", image.transform, 110, 0, 535, 103).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = BoundaryZeroTypography.GetFont();
        label.fontSharedMaterial = BoundaryZeroTypography.GetMaterial();
        label.text = text; label.fontSize = 34; label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.color = available ? new Color32(230, 237, 244, 255) : new Color32(78, 95, 111, 255);
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        Picture("Separator", image.transform, "menu_separator.png", 110, 102, 545, 3);
        return button;
    }

    private static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    private static UnityEngine.UI.Image Picture(string name, Transform parent, string asset, float x, float y, float w, float h)
    {
        var image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<UnityEngine.UI.Image>();
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpriteRoot + asset)
            ?? throw new InvalidOperationException("Missing main-menu sprite: " + asset);
        image.preserveAspect = true; image.raycastTarget = false;
        return image;
    }
}
