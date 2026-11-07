using System;
using System.Linq;
using EasternLab.GateAddon;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Connects the imported gate to the Hub's player, loading flow, and interaction prompt.</summary>
public static class HubGateSetup
{
    public const string GatePrefabPath = "Assets/EasternLab_GateAddon/WarpGate_Addon.prefab";
    public const string PromptPrefabPath = "Assets/_Project/Prefabs/UI/HubGatePrompt.prefab";
    private const string HubPath = "Assets/_Project/Scenes/Hub Scene.unity";
    private const string Art = "Assets/_Project/Sprites/UI/BoundaryZERO/";
    private const string UndoName = "Connect Hub Gate";

    [MenuItem("Tools/Studio Baekho/Progression/Set Up Hub Gate")]
    private static void SetupMenu() => Debug.Log(Setup());

    public static string Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Set up the Hub gate in Edit Mode.");
        if (!EditorBuildSettings.scenes.Any(scene => scene.enabled
            && System.IO.Path.GetFileNameWithoutExtension(scene.path) == "Game Scene"))
            throw new InvalidOperationException("Game Scene must be enabled in the build scene list.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GatePrefabPath);
        if (prefab == null) throw new InvalidOperationException("The imported WarpGate_Addon prefab is missing.");
        BuildPromptPrefab();
        var hub = SceneManager.GetSceneByPath(HubPath);
        if (!hub.isLoaded) hub = EditorSceneManager.OpenScene(HubPath, OpenSceneMode.Additive);
        var map = hub.GetRootGameObjects().Single(root => root.name == "EasternLab_Model");
        var settings = Components<GateAddonSettings>(hub).SingleOrDefault();
        if (settings == null)
        {
            var gate = (GameObject)PrefabUtility.InstantiatePrefab(prefab, map.transform);
            gate.name = "WarpGate_Addon";
            settings = gate.GetComponent<GateAddonSettings>();
            Undo.RegisterCreatedObjectUndo(gate, UndoName);
        }
        if (settings == null || settings.transit == null || settings.panel == null || settings.view == null)
            throw new InvalidOperationException("The imported gate is missing its settings or required references.");
        if (settings.transform.parent != map.transform)
            Undo.SetTransformParent(settings.transform, map.transform, UndoName);
        Undo.RecordObject(settings.transform, UndoName);
        settings.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        settings.transform.localScale = Vector3.one;
        Record(settings.transform);
        Undo.RecordObject(settings.gameObject, UndoName);
        settings.gameObject.SetActive(true);
        Record(settings.gameObject);

        var player = Components<PlayerController>(hub).Single();
        var camera = Components<Camera>(hub).Single(value => value.CompareTag("MainCamera"));
        var world = Components<HubWorldController>(hub).Single();
        var workshop = Components<HubWorkshopInteraction>(hub).Single();
        var overlay = Components<BoundaryZeroRunOverlay>(hub).Single();
        Undo.RecordObjects(new UnityEngine.Object[] { settings, settings.transit, settings.panel, settings.view }, UndoName);
        settings.targetScene = "Game Scene";
        settings.playerRoot = player.transform;
        settings.playerCamera = camera;
        settings.addMissingColliders = false;
        settings.transit.externalSceneLoading = true;
        settings.transit.playConversionEffect = false;
        settings.panel.useBuiltInInput = false;
        settings.panel.showPrompt = false;
        settings.ApplyDestination();
        Record(settings);
        Record(settings.transit);
        Record(settings.panel);
        Record(settings.view);

        var promptCanvas = hub.GetRootGameObjects().SingleOrDefault(root => root.name == "HubGatePrompt");
        if (promptCanvas == null)
        {
            promptCanvas = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(PromptPrefabPath), hub);
            Undo.RegisterCreatedObjectUndo(promptCanvas, UndoName);
        }
        var prompt = promptCanvas.transform.Find("GatePrompt").gameObject;
        var action = prompt.transform.Find("Content/Action").GetComponent<TMP_Text>();
        var status = prompt.transform.Find("Content/Status").GetComponent<TMP_Text>();
        var controller = world.GetComponent<HubGateController>();
        if (controller == null) controller = Undo.AddComponent<HubGateController>(world.gameObject);
        Undo.RecordObject(controller, UndoName);
        controller.Configure(player, settings, world, workshop, overlay, prompt, action, status, 2f);
        Record(controller);
        Undo.RecordObject(prompt, UndoName);
        prompt.SetActive(false);
        Record(prompt);
        string validation = Validate();
        EditorSceneManager.MarkSceneDirty(hub);
        if (!EditorSceneManager.SaveScene(hub)) throw new InvalidOperationException("Could not save the Hub gate connection.");
        return "Hub gate and dedicated prompt connected. " + validation;
    }

    public static string Validate()
    {
        var hub = SceneManager.GetSceneByPath(HubPath);
        if (!hub.isLoaded) throw new InvalidOperationException("Load the Hub Scene before validating its gate.");
        var settings = Components<GateAddonSettings>(hub).Single();
        var player = Components<PlayerController>(hub).Single();
        var camera = Components<Camera>(hub).Single(value => value.CompareTag("MainCamera"));
        var controller = Components<HubGateController>(hub).Single();
        var root = settings.transform;
        if (root.parent == null || root.parent.name != "EasternLab_Model"
            || root.localPosition.sqrMagnitude > .000001f || Quaternion.Angle(root.localRotation, Quaternion.identity) > .001f
            || (root.localScale - Vector3.one).sqrMagnitude > .000001f)
            throw new InvalidOperationException("The gate must use its imported identity pose directly under EasternLab_Model.");
        if (!settings.gameObject.activeInHierarchy || settings.targetScene != "Game Scene"
            || settings.playerRoot != player.transform || settings.playerCamera != camera || settings.addMissingColliders)
            throw new InvalidOperationException("The Hub gate settings are not connected to the Hub player and Game Scene.");
        var transit = settings.transit;
        if (transit == null || transit.targetScene != "Game Scene" || transit.playerRoot != player.transform
            || transit.playerCamera != camera || !transit.externalSceneLoading || transit.playConversionEffect)
            throw new InvalidOperationException("The Hub gate must delegate scene loading to its Hub controller.");
        if (settings.panel == null || settings.panel.useBuiltInInput || settings.panel.showPrompt
            || settings.panel.playerCamera != camera || settings.view == null || settings.view.playerCamera != camera)
            throw new InvalidOperationException("The gate panel must use the shared Hub interaction and camera.");
        if (controller.Settings != settings || transit.onTraverse.GetPersistentEventCount() != 0)
            throw new InvalidOperationException("The gate requires one runtime controller subscription and no persistent traversal calls.");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(GatePrefabPath).GetComponent<GateAddonSettings>();
        var originalTransit = source.transit;
        if (!Near(transit.crossingPlaneOffset, originalTransit.crossingPlaneOffset)
            || !Near(transit.apertureRadius, originalTransit.apertureRadius)
            || !Near(transit.armDistance, originalTransit.armDistance)
            || !Near(transit.crossingThreshold, originalTransit.crossingThreshold)
            || !Near(transit.cooldown, originalTransit.cooldown)
            || transit.preservePortalPose != originalTransit.preservePortalPose
            || !Near(transit.activation.openingDuration, source.transit.activation.openingDuration)
            || !Near(transit.activation.closingDuration, source.transit.activation.closingDuration))
            throw new InvalidOperationException("The gate's authored animation timing and crossing parameters must be preserved.");
        if (settings.GetComponentsInChildren<MeshCollider>(true).Any())
            throw new InvalidOperationException("Gate portal and decorative meshes must not have generated world colliders.");
        var promptRoot = hub.GetRootGameObjects().Single(value => value.name == "HubGatePrompt");
        var prompt = promptRoot.transform.Find("GatePrompt");
        var action = prompt.Find("Content/Action").GetComponent<TMP_Text>();
        var status = prompt.Find("Content/Status").GetComponent<TMP_Text>();
        if (!promptRoot.activeInHierarchy || promptRoot.GetComponent<Canvas>().sortingOrder != 75
            || (!Application.isPlaying && prompt.gameObject.activeSelf)
            || action == null || status == null || status.textWrappingMode != TextWrappingModes.Normal
            || status.overflowMode != TextOverflowModes.Ellipsis || status.rectTransform.sizeDelta.y < 60f)
            throw new InvalidOperationException("The dedicated gate prompt must start hidden and contain separate bounded action/status rows.");
        return "PASS: one imported gate, local player/camera, delegated loading, original timing/crossing, and a separate hidden prompt.";
    }

    private static void BuildPromptPrefab()
    {
        bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(PromptPrefabPath) != null;
        Scene preview = default;
        GameObject root;
        if (existing) root = PrefabUtility.LoadPrefabContents(PromptPrefabPath);
        else
        {
            preview = EditorSceneManager.NewPreviewScene();
            root = new GameObject("HubGatePrompt", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(root, preview);
        }
        try
        {
            root.name = "HubGatePrompt";
            root.layer = LayerMask.NameToLayer("UI");
            root.SetActive(true);
            var canvas = Ensure<Canvas>(root);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 75;
            var scaler = Ensure<UnityEngine.UI.CanvasScaler>(root);
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            var prompt = Child("GatePrompt", root.transform);
            prompt.anchorMin = prompt.anchorMax = prompt.pivot = new Vector2(.5f, 0f);
            prompt.anchoredPosition = new Vector2(0f, 64f);
            prompt.sizeDelta = new Vector2(720f, 144f);
            var plate = Image(prompt, "HUD/interaction_plate.png");
            plate.color = new Color(1f, 1f, 1f, .96f);
            var horizontal = Ensure<UnityEngine.UI.HorizontalLayoutGroup>(prompt.gameObject);
            horizontal.padding = new RectOffset(24, 24, 20, 20);
            horizontal.spacing = 18f;
            horizontal.childAlignment = TextAnchor.MiddleLeft;
            horizontal.childControlWidth = horizontal.childControlHeight = true;
            horizontal.childForceExpandWidth = horizontal.childForceExpandHeight = false;
            var key = Child("Keycap", prompt);
            Image(key, "Controls/keycap_50.png");
            var keyGroup = Ensure<CanvasGroup>(key.gameObject);
            keyGroup.alpha = 1f;
            keyGroup.interactable = keyGroup.blocksRaycasts = false;
            Fixed(key, 48, 48);
            var keyLabel = Text("Key", key, "F", 22, new Color32(231, 237, 242, 255));
            keyLabel.alignment = TextAlignmentOptions.Center;
            Stretch(keyLabel.rectTransform);
            var content = Child("Content", prompt);
            Fixed(content, 606, 104);
            var vertical = Ensure<UnityEngine.UI.VerticalLayoutGroup>(content.gameObject);
            vertical.padding = new RectOffset();
            vertical.spacing = 8f;
            vertical.childAlignment = TextAnchor.UpperLeft;
            vertical.childControlWidth = vertical.childControlHeight = true;
            vertical.childForceExpandWidth = true;
            vertical.childForceExpandHeight = false;
            var action = Text("Action", content, "경계 개방", 22, new Color32(231, 237, 242, 255));
            action.fontStyle = FontStyles.Bold;
            action.textWrappingMode = TextWrappingModes.NoWrap;
            Fixed(action.rectTransform, 606, 36);
            var status = Text("Status", content, "오른쪽 단말에서 경계를 개방한 뒤 통로 안으로 걸어 들어가세요.", 18,
                new Color32(162, 175, 188, 255));
            status.textWrappingMode = TextWrappingModes.Normal;
            status.alignment = TextAlignmentOptions.TopLeft;
            Fixed(status.rectTransform, 606, 60);
            key.SetSiblingIndex(0);
            content.SetSiblingIndex(1);
            action.transform.SetSiblingIndex(0);
            status.transform.SetSiblingIndex(1);
            prompt.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(prompt);
            prompt.gameObject.SetActive(false);
            if (PrefabUtility.SaveAsPrefabAsset(root, PromptPrefabPath) == null)
                throw new InvalidOperationException("Could not save the reusable Hub gate prompt.");
        }
        finally
        {
            if (existing) PrefabUtility.UnloadPrefabContents(root);
            else if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static System.Collections.Generic.IEnumerable<T> Components<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));

    private static T Ensure<T>(GameObject target) where T : Component
    {
        var value = target.GetComponent<T>();
        return value != null ? value : target.AddComponent<T>();
    }

    private static RectTransform Child(string name, Transform parent)
    {
        var child = parent.Find(name) as RectTransform;
        if (child != null) return child;
        var created = new GameObject(name, typeof(RectTransform));
        created.layer = LayerMask.NameToLayer("UI");
        created.transform.SetParent(parent, false);
        return created.GetComponent<RectTransform>();
    }

    private static UnityEngine.UI.Image Image(RectTransform target, string path)
    {
        var image = Ensure<UnityEngine.UI.Image>(target.gameObject);
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + path);
        if (image.sprite == null) throw new InvalidOperationException("Missing BoundaryZERO gate-prompt sprite: " + path);
        image.type = UnityEngine.UI.Image.Type.Sliced;
        image.color = Color.white;
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text Text(string name, Transform parent, string value, float size, Color color)
    {
        var text = Ensure<TextMeshProUGUI>(Child(name, parent).gameObject);
        text.font = BoundaryZeroTypography.GetFont();
        text.fontSharedMaterial = BoundaryZeroTypography.GetMaterial();
        text.text = value;
        text.fontSize = size;
        text.enableAutoSizing = false;
        text.margin = Vector4.zero;
        text.color = color;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private static void Fixed(RectTransform rect, float width, float height)
    {
        rect.sizeDelta = new Vector2(width, height);
        var layout = Ensure<UnityEngine.UI.LayoutElement>(rect.gameObject);
        layout.minWidth = layout.preferredWidth = width;
        layout.minHeight = layout.preferredHeight = height;
        layout.flexibleWidth = layout.flexibleHeight = 0f;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static bool Near(float actual, float expected) => Mathf.Abs(actual - expected) < .0001f;

    private static void Record(UnityEngine.Object target)
    {
        EditorUtility.SetDirty(target);
        if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }
}
