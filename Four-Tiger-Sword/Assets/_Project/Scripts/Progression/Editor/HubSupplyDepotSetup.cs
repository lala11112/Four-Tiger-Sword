using System;
using System.Collections.Generic;
using System.Linq;
using BoundaryZero.SupplyDepot;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Connects the imported depot UI to the Hub's authored supply station.</summary>
public static class HubSupplyDepotSetup
{
    private const string HubPath = "Assets/_Project/Scenes/Hub Scene.unity";
    private const string PrefabPath = "Assets/BoundaryZeroSupplyDepot/Prefabs/SupplyDepot.prefab";
    private const string ItemsPath = "Assets/_Project/Generated/BoundaryInventory/ItemCatalog.asset";
    private const string Art = "Assets/_Project/Sprites/UI/BoundaryZERO/";
    private const string UndoName = "Set Up Hub Supply Depot";
    private const string DimmerName = "SupplyDepotDimmer";
    private const string PromptName = "SupplyDepotInteractionPrompt";
    private const string StatusName = "SupplyDepotStatus";
    private const float Radius = 1.4f;
    private const int SortOrder = 90;
    private static readonly Color Ink = new Color32(231, 237, 242, 255);

    [MenuItem("Tools/Studio Baekho/Progression/Set Up Hub Supply Depot")]
    private static void SetupMenu() => Debug.Log(Setup());

    public static string Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Set up the Hub supply depot in Edit Mode.");

        var hub = LoadedHub();
        var world = Components<HubWorldController>(hub).Single();
        var player = Components<PlayerController>(hub).Single();
        var station = FindStation(hub);
        var front = StationFront(station);
        var prefab = RequiredAsset<GameObject>(PrefabPath);
        var items = RequiredAsset<ItemCatalog>(ItemsPath);
        var plate = RequiredAsset<Sprite>(Art + "HUD/interaction_plate.png");
        var keycap = RequiredAsset<Sprite>(Art + "Controls/keycap_34.png");
        var catalog = HubSupplyDepotCatalogBuilder.Build();

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);
        try
        {
            var depot = Components<SupplyDepotController>(hub).SingleOrDefault();
            if (depot == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, hub);
                Undo.RegisterCreatedObjectUndo(instance, UndoName);
                depot = instance.GetComponent<SupplyDepotController>();
            }
            if (depot == null || PrefabUtility.GetCorrespondingObjectFromSource(depot.gameObject) != prefab
                || depot.transform.parent != null || depot.view == null)
                throw new InvalidOperationException("The Hub depot must be a root instance of the imported prefab.");

            var account = Ensure<HubSupplyDepotAccount>(depot.gameObject);
            Change(account, () => account.Configure(items, catalog));
            Change(depot, () =>
            {
                depot.catalog = catalog;
                depot.accountProvider = account;
                depot.openOnStart = false;
                depot.automaticKeyboardInput = true;
                depot.manageCursor = false;
            });

            var canvas = depot.GetComponent<Canvas>();
            Change(canvas, () => canvas.sortingOrder = SortOrder);
            var view = new SerializedObject(depot.view);
            view.FindProperty("showReopenButton").boolValue = false;
            view.ApplyModifiedProperties();
            var window = ViewObject(view, "window");
            SetActive(window, false);
            SetActive(ViewObject(view, "reopenContainer"), false);

            var dimmer = BuildDimmer(depot.transform);
            var prompt = BuildPrompt(depot.transform, catalog.font, plate, keycap);
            var statusRect = Place(Child(window.transform.Find("Footer"), StatusName), Vector2.zero, Vector2.zero,
                new Vector2(220, 0), new Vector2(730, 48));
            var status = Label(statusRect, catalog.font, string.Empty, 16, new Color32(255, 180, 191, 255),
                TextAlignmentOptions.MidlineLeft);

            var interaction = Ensure<HubSupplyDepotInteraction>(world.gameObject);
            Change(interaction, () => interaction.Configure(player, station, world, depot, account,
                prompt.gameObject, dimmer.gameObject, status, station.InverseTransformPoint(front), Radius));

            string result = Validate();
            EditorSceneManager.MarkSceneDirty(hub);
            Undo.CollapseUndoOperations(undoGroup);
            return "Connected the Hub supply depot. " + result;
        }
        catch
        {
            Undo.RevertAllDownToGroup(undoGroup);
            throw;
        }
    }

    public static string Validate()
    {
        var hub = LoadedHub();
        var depot = Components<SupplyDepotController>(hub).Single();
        var account = Components<HubSupplyDepotAccount>(hub).Single();
        var interaction = Components<HubSupplyDepotInteraction>(hub).Single();
        var player = Components<PlayerController>(hub).Single();
        var world = Components<HubWorldController>(hub).Single();
        var station = FindStation(hub);
        var events = Components<EventSystem>(hub).Single();
        if (!events.isActiveAndEnabled || !events.TryGetComponent<InputSystemUIInputModule>(out var input) || !input.isActiveAndEnabled)
            throw new InvalidOperationException("The Hub depot requires the Hub's active Input System UI module.");

        var canvas = depot.GetComponent<Canvas>();
        var scaler = depot.GetComponent<UnityEngine.UI.CanvasScaler>();
        if (!depot.isActiveAndEnabled || depot.transform.parent != null || canvas == null || !canvas.enabled
            || canvas.renderMode != RenderMode.ScreenSpaceOverlay || canvas.sortingOrder != SortOrder
            || scaler == null || !scaler.enabled || scaler.uiScaleMode != UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize
            || !depot.TryGetComponent<UnityEngine.UI.GraphicRaycaster>(out var raycaster) || !raycaster.enabled
            || PrefabUtility.GetCorrespondingObjectFromSource(depot.gameObject) != RequiredAsset<GameObject>(PrefabPath))
            throw new InvalidOperationException("The depot Canvas or prefab link is missing or invalid.");

        var view = new SerializedObject(depot.view);
        var window = ViewObject(view, "window");
        var dimmer = depot.transform.Find(DimmerName)?.GetComponent<UnityEngine.UI.Image>();
        var prompt = depot.transform.Find(PromptName);
        var status = window.transform.Find("Footer/" + StatusName)?.GetComponent<TMP_Text>();
        if (depot.openOnStart || !depot.automaticKeyboardInput || depot.manageCursor || depot.accountProvider != account
            || view.FindProperty("showReopenButton").boolValue || ViewObject(view, "reopenContainer").activeSelf
            || dimmer == null || !dimmer.raycastTarget || dimmer.rectTransform.GetSiblingIndex() != 0
            || dimmer.rectTransform.anchorMin != Vector2.zero || dimmer.rectTransform.anchorMax != Vector2.one
            || dimmer.rectTransform.offsetMin != Vector2.zero || dimmer.rectTransform.offsetMax != Vector2.zero
            || prompt == null || status == null || status.font == null)
            throw new InvalidOperationException("The depot modal or its controls are configured incorrectly.");
        if (!Application.isPlaying && (window.activeSelf || dimmer.gameObject.activeSelf || prompt.gameObject.activeSelf))
            throw new InvalidOperationException("The depot window, dimmer and prompt must start hidden.");

        var binding = new SerializedObject(interaction);
        if (!interaction.isActiveAndEnabled || interaction.gameObject != world.gameObject
            || !station.GetComponentsInChildren<Collider>().Any(collider => collider.enabled)
            || (interaction.InteractionPosition - StationFront(station)).sqrMagnitude > .0001f
            || !Mathf.Approximately(binding.FindProperty("interactionRadius").floatValue, Radius)
            || binding.FindProperty("player").objectReferenceValue != player
            || binding.FindProperty("station").objectReferenceValue != station
            || binding.FindProperty("world").objectReferenceValue != world
            || binding.FindProperty("depot").objectReferenceValue != depot
            || binding.FindProperty("account").objectReferenceValue != account
            || binding.FindProperty("dimmer").objectReferenceValue != dimmer.gameObject
            || binding.FindProperty("prompt").objectReferenceValue != prompt.gameObject
            || binding.FindProperty("status").objectReferenceValue != status)
            throw new InvalidOperationException("The depot interaction is missing its Hub station or UI references.");

        var items = RequiredAsset<ItemCatalog>(ItemsPath);
        var catalog = depot.catalog;
        if (catalog == null || catalog.font == null || catalog.items == null || catalog.items.Count != items.items.Length
            || catalog.items.Any(item => item == null || items.Find(item.id) == null)
            || catalog.items.Select(item => item.id).Distinct(StringComparer.Ordinal).Count() != items.items.Length)
            throw new InvalidOperationException("The depot must use the current gameplay item IDs.");
        return $"PASS: one linked depot modal and station prompt with {catalog.items.Count} gameplay items.";
    }

    private static Scene LoadedHub()
    {
        var hub = SceneManager.GetSceneByPath(HubPath);
        if (!hub.isLoaded) throw new InvalidOperationException("Load the Hub Scene before setting up its supply depot.");
        return hub;
    }

    private static Transform FindStation(Scene hub) => hub.GetRootGameObjects()
        .Single(root => root.name == "EasternLab_Model").transform.Find("Architecture/05_Supply")
        ?? throw new InvalidOperationException("The Hub supply station 05_Supply is missing.");

    private static Vector3 StationFront(Transform station)
    {
        var renderers = station.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) throw new InvalidOperationException("The Hub supply station has no renderer bounds.");
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        return new Vector3(bounds.center.x, station.position.y, bounds.min.z - .55f);
    }

    private static RectTransform BuildDimmer(Transform root)
    {
        var rect = Child(root, DimmerName);
        Change(rect, () =>
        {
            rect.SetAsFirstSibling();
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.anchoredPosition3D = Vector3.zero;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        });
        var image = Ensure<UnityEngine.UI.Image>(rect.gameObject);
        Change(image, () =>
        {
            image.color = new Color(0f, 0f, 0f, .52f);
            image.raycastTarget = true;
        });
        SetActive(rect.gameObject, false);
        return rect;
    }

    private static RectTransform BuildPrompt(Transform root, TMP_FontAsset font, Sprite plate, Sprite keycap)
    {
        var anchor = new Vector2(.5f, 0f);
        var prompt = Place(Child(root, PromptName), anchor, anchor, new Vector2(0, 70), new Vector2(330, 60));
        Picture(prompt, "Plate", plate, Vector2.zero, new Vector2(330, 60), .92f);
        var keyPosition = new Vector2(14, 12);
        var keySize = new Vector2(36, 36);
        Picture(prompt, "Keycap", keycap, keyPosition, keySize, 1f);
        var key = Place(Child(prompt, "Key"), Vector2.zero, Vector2.zero, keyPosition, keySize);
        Label(key, font, "F", 20, Ink, TextAlignmentOptions.Center);
        var label = Place(Child(prompt, "Label"), Vector2.zero, Vector2.zero, new Vector2(63, 8), new Vector2(253, 44));
        Label(label, font, "보급고 열기", 18, Ink, TextAlignmentOptions.MidlineLeft);
        SetActive(prompt.gameObject, false);
        return prompt;
    }

    private static RectTransform Child(Transform parent, string name)
    {
        if (parent == null) throw new InvalidOperationException("The imported depot UI hierarchy is incomplete.");
        var existing = parent.Find(name);
        if (existing != null) return existing as RectTransform
            ?? throw new InvalidOperationException($"{name} requires a RectTransform.");
        var child = new GameObject(name, typeof(RectTransform)) { layer = LayerMask.NameToLayer("UI") };
        if (child.scene != parent.gameObject.scene) SceneManager.MoveGameObjectToScene(child, parent.gameObject.scene);
        Undo.RegisterCreatedObjectUndo(child, UndoName);
        Undo.SetTransformParent(child.transform, parent, UndoName);
        return (RectTransform)child.transform;
    }

    private static RectTransform Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        Change(rect, () =>
        {
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition3D = position;
            rect.sizeDelta = size;
        });
        return rect;
    }

    private static void Picture(Transform parent, string name, Sprite sprite, Vector2 position, Vector2 size, float alpha)
    {
        var rect = Place(Child(parent, name), Vector2.zero, Vector2.zero, position, size);
        var image = Ensure<UnityEngine.UI.Image>(rect.gameObject);
        Change(image, () =>
        {
            image.sprite = sprite;
            image.color = new Color(1f, 1f, 1f, alpha);
            image.raycastTarget = false;
        });
    }

    private static TMP_Text Label(RectTransform rect, TMP_FontAsset font, string value, int size, Color color, TextAlignmentOptions alignment)
    {
        var label = Ensure<TextMeshProUGUI>(rect.gameObject);
        Change(label, () =>
        {
            label.font = font;
            label.text = value;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
        });
        return label;
    }

    private static GameObject ViewObject(SerializedObject view, string key) =>
        view.FindProperty("elements").FindPropertyRelative(key).objectReferenceValue as GameObject
        ?? throw new InvalidOperationException($"The imported depot view is missing {key}.");

    private static T RequiredAsset<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path)
        ?? throw new InvalidOperationException($"Missing Hub depot asset {path}.");

    private static IEnumerable<T> Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true));

    private static T Ensure<T>(GameObject root) where T : Component => root.GetComponent<T>() ?? Undo.AddComponent<T>(root);

    private static void SetActive(GameObject target, bool active)
    {
        if (target.activeSelf != active) Change(target, () => target.SetActive(active));
    }

    private static void Change(Object target, Action edit)
    {
        Undo.RecordObject(target, UndoName);
        edit();
        if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }
}
