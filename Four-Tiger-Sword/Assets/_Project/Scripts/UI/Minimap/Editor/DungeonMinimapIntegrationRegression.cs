using System;
using System.Reflection;
using StudioBaekho;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Exercises the installed presentation and stage lifecycle in an isolated Edit Mode scene.</summary>
public static class DungeonMinimapIntegrationRegression
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/DungeonMinimap.prefab";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;

    [MenuItem("Tools/Studio Baekho/Dungeon/Run Minimap Integration Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run minimap integration regression in Edit Mode.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        CheckAsset(prefab != null, "Minimap prefab is missing.");
        _checks = 0;
        foreach (var graphic in prefab.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
            Check(graphic.GetComponent<CanvasRenderer>() != null, "Prefab graphic has no CanvasRenderer: " + graphic.name);
        Scene previous = SceneManager.GetActiveScene();
        Scene isolated = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        int targetFrameRate = Application.targetFrameRate;
        bool cursorVisible = Cursor.visible;
        CursorLockMode cursorLock = Cursor.lockState;
        try
        {
            SceneManager.SetActiveScene(isolated);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, isolated);
            var view = instance.GetComponent<DungeonMinimapView>();
            var presenter = instance.GetComponent<DungeonMinimapPresenter>();
            Check(view != null && presenter != null, "Minimap prefab lacks its view or presenter.");
            foreach (var graphic in instance.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                Check(graphic.GetComponent<CanvasRenderer>() != null, "Instantiated graphic has no CanvasRenderer: " + graphic.name);
            Canvas.ForceUpdateCanvases();
            Lifecycle(view, presenter);
            Layout(instance);
            return $"Minimap integration: {_checks} checks passed (discovery, markers, transformed roots, " +
                "height, state changes, UI reactivation, replacement stages, input pass-through and three aspect ratios).";
        }
        finally
        {
            SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(isolated, true);
            Application.targetFrameRate = targetFrameRate;
            Cursor.visible = cursorVisible;
            Cursor.lockState = cursorLock;
        }
    }

    public static string ValidateInstalledScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        DungeonMinimapPresenter found = null;
        int count = 0;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var presenter in root.GetComponentsInChildren<DungeonMinimapPresenter>(true))
            { found = presenter; count++; }
        CheckAsset(count == 1 && found != null, "Expected exactly one minimap presenter in the active scene.");
        CheckAsset(found.Run != null && found.Run.gameObject.scene == scene, "Minimap RunManager reference is missing or belongs to another scene.");
        CheckAsset(found.View != null && found.View.gameObject.scene == scene, "Minimap view reference is missing or belongs to another scene.");
        foreach (var graphic in found.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
        {
            CheckAsset(graphic.GetComponent<CanvasRenderer>() != null, "Minimap graphic has no CanvasRenderer: " + graphic.name);
            CheckAsset(!graphic.raycastTarget, "Minimap blocks input: " + graphic.name);
        }
        return "Installed minimap: one presenter, explicit run/view bindings, CanvasRenderers and input pass-through verified.";
    }

    private static void Lifecycle(DungeonMinimapView view, DungeonMinimapPresenter presenter)
    {
        var runObject = new GameObject("MinimapRunFixture");
        var run = runObject.AddComponent<RunManager>();
        var stage = runObject.AddComponent<StageManager>();
        var generator = new GameObject("GeneratorFixture").AddComponent<BaekhoDungeonGenerator>();
        stage.Configure(generator, null);
        var player = new GameObject("PlayerFixture").AddComponent<PlayerController>();
        run.stageManager = stage;
        run.player = player;
        var result = Result();
        Auto(stage, "Result", result);
        Auto(stage, "IsReady", true);
        Auto(generator, "CurrentRoot", result.root);
        Auto(run, "State", RunState.Playing);
        player.transform.SetPositionAndRotation(result.root.transform.TransformPoint(Vector3.zero), result.root.transform.rotation);
        presenter.Configure(run, view);
        Invoke(presenter, "UpdateMap");

        var content = Field<RectTransform>(view, "mapContent");
        var markers = Field<RectTransform>(view, "markerLayer");
        var viewport = Field<RectTransform>(view, "viewport");
        var arrow = Field<DungeonMinimapIconGraphic>(view, "playerArrow");
        Check(view.IsVisible && view.BuiltAreaCount == 3, "Ready stage did not build its room geometry.");
        Check(presenter.Model.Areas[presenter.Model.GetRoomAreaIndex(1)].Visited, "Start room was not discovered.");
        Check(markers.childCount == 2 && !markers.Find("ShopMarker").gameObject.activeSelf
            && !markers.Find("ExitMarker").gameObject.activeSelf, "Undiscovered markers leaked.");
        Check(content.childCount == 3, "Initial build created unexpected map geometry.");

        player.transform.SetPositionAndRotation(result.root.transform.TransformPoint(new Vector3(20f, 0f, 0f)),
            result.root.transform.rotation * Quaternion.Euler(0f, 90f, 0f));
        Invoke(presenter, "UpdateMap");
        Check(markers.Find("ShopMarker").gameObject.activeSelf && !markers.Find("ExitMarker").gameObject.activeSelf,
            "Visiting a shop failed to reveal only its own marker.");
        float scale = Mathf.Min(viewport.rect.width, viewport.rect.height) / view.WorldSpan;
        Check(Vector2.Distance(content.anchoredPosition, new Vector2(-20f * scale, 0f)) < .01f,
            "Translated/rotated root did not map the player into dungeon-local XZ.");
        Check(Mathf.Abs(Mathf.DeltaAngle(arrow.rectTransform.localEulerAngles.z, -90f)) < .01f,
            "Player arrow does not follow dungeon-local facing.");
        int revision = presenter.Model.Revision;
        player.transform.position = result.root.transform.TransformPoint(new Vector3(20f, 50f, 0f));
        Invoke(presenter, "UpdateMap");
        Check(presenter.Model.Revision == revision && markers.Find("ShopMarker").gameObject.activeSelf,
            "Height changes altered room discovery.");

        Invoke(presenter, "OnDisable");
        Check(!view.IsVisible, "Disabled presenter left the minimap visible.");
        Invoke(presenter, "OnEnable");
        Invoke(presenter, "UpdateMap");
        Check(view.IsVisible && presenter.Model.Areas[presenter.Model.GetRoomAreaIndex(2)].Visited
            && content.childCount == 3 && markers.childCount == 2, "Reactivation lost exploration or duplicated UI.");

        foreach (RunState state in new[] { RunState.Preparing, RunState.Error, RunState.Won, RunState.Lost })
        {
            Auto(run, "State", state);
            Invoke(presenter, "OnRunStateChanged", state);
            Invoke(presenter, "UpdateMap");
            Check(!view.IsVisible, "Minimap remained visible during " + state + ".");
        }
        Auto(run, "State", RunState.Playing);
        player.transform.position = result.root.transform.TransformPoint(new Vector3(200f, 0f, 0f));
        Invoke(presenter, "UpdateMap");
        Check(!markers.Find("ShopMarker").gameObject.activeSelf, "Out-of-viewport shop icon remained visible.");
        foreach (var graphic in view.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
        {
            Check(graphic.GetComponent<CanvasRenderer>() != null, "Generated map graphic has no CanvasRenderer: " + graphic.name);
            Check(!graphic.raycastTarget, "Map graphic blocks input: " + graphic.name);
        }

        var next = Result();
        Auto(stage, "Result", next);
        Auto(generator, "CurrentRoot", next.root);
        player.transform.SetPositionAndRotation(next.root.transform.TransformPoint(Vector3.zero), next.root.transform.rotation);
        Invoke(presenter, "OnStageStarted");
        Check(view.IsVisible && !presenter.Model.Areas[presenter.Model.GetRoomAreaIndex(2)].Visited,
            "Replacement stage reused discovery from matching old room IDs.");
        Check(content.childCount == 3 && markers.childCount == 2, "Replacement stage duplicated old geometry or icons.");
        view.Clear();
        Check(view.BuiltAreaCount == 0 && content.childCount == 0 && markers.childCount == 0 && !view.IsVisible,
            "Clearing the view left geometry, markers or visibility behind.");
    }

    private static BaekhoDungeonResult Result()
    {
        var result = new BaekhoDungeonResult { success = true, root = new GameObject("GeneratedFixture") };
        result.root.transform.SetPositionAndRotation(new Vector3(125f, 7f, -80f), Quaternion.Euler(0f, 90f, 0f));
        var module = new GameObject("ModuleFixture").AddComponent<BaekhoMapModule>();
        module.localBounds = new Bounds(Vector3.zero, new Vector3(10f, 4f, 10f));
        result.plan = new BaekhoDungeonPlan { startMapId = 1, shopMapId = 2, endMapId = 3 };
        for (int i = 0; i < 3; i++)
        {
            var role = i == 0 ? BaekhoRoomRole.Start : i == 1 ? BaekhoRoomRole.Shop : BaekhoRoomRole.End;
            var position = new Vector3(i * 20f, 0f, 0f);
            result.plan.rooms.Add(new BaekhoPlacedModule { id = i + 1, prefab = module, position = position, role = role });
            var room = new GameObject("RoomFixture_" + i).AddComponent<BaekhoMapModule>();
            room.transform.SetParent(result.root.transform, false);
            room.transform.localPosition = position;
            var anchor = new GameObject("AnchorFixture").transform;
            anchor.SetParent(room.transform, false);
            var anchors = room.gameObject.AddComponent<StageMapAnchors>();
            anchors.shopPoint = anchor;
            anchors.exitPoint = anchor;
            result.rooms.Add(room);
            if (i == 0) result.startRoom = room;
            else if (i == 1) result.shopRoom = room;
            else result.endRoom = room;
        }
        return result;
    }

    private static void Layout(GameObject instance)
    {
        var view = instance.GetComponent<DungeonMinimapView>();
        var panel = (RectTransform)Field<RectTransform>(view, "viewport").parent;
        var formsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/PlayerHUD.prefab");
        var goldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Shop/UI/GoldHUD.prefab");
        Check(formsPrefab != null && goldPrefab != null, "Existing HUD assets are missing.");
        RectTransform forms = NamedRect(formsPrefab, "FormsGroup"), skills = NamedRect(formsPrefab, "SkillsGroup");
        RectTransform gold = NamedRect(goldPrefab, "Gold");
        Check(panel.anchorMin == Vector2.one && panel.anchorMax == Vector2.one && panel.pivot == Vector2.one,
            "Minimap panel is not anchored to the upper-right corner.");
        Check(Vector2.Distance(panel.anchoredPosition, new Vector2(-48f, -198f)) < .01f
            && Vector2.Distance(panel.sizeDelta, new Vector2(300f, 300f)) < .01f, "Minimap panel dimensions differ from the approved layout.");
        Check(Vector2.Distance(forms.anchoredPosition, new Vector2(-48f, -24f)) < .01f, "Form HUD moved.");
        Check(Field<RectTransform>(view, "viewport").GetComponent<UnityEngine.UI.RectMask2D>() != null,
            "Minimap viewport has no clipping mask.");
        var mapScaler = instance.GetComponent<UnityEngine.UI.CanvasScaler>();
        var formScaler = formsPrefab.GetComponent<UnityEngine.UI.CanvasScaler>();
        var goldScaler = goldPrefab.GetComponent<UnityEngine.UI.CanvasScaler>();
        foreach (Vector2 screen in new[] { new Vector2(1280f, 720f), new Vector2(1920f, 1080f), new Vector2(1920f, 1200f) })
        {
            Rect mapRect = ScreenRect(panel, mapScaler, screen), formRect = ScreenRect(forms, formScaler, screen);
            Rect goldRect = ScreenRect(gold, goldScaler, screen), skillRect = ScreenRect(skills, formScaler, screen);
            Check(!mapRect.Overlaps(formRect) && !mapRect.Overlaps(goldRect) && !mapRect.Overlaps(skillRect)
                && !goldRect.Overlaps(skillRect), "HUD regions overlap at " + screen + ".");
            Check(mapRect.xMin >= 0f && mapRect.yMin >= 0f && mapRect.xMax <= screen.x && mapRect.yMax <= screen.y,
                "Minimap leaves the screen at " + screen + ".");
            float mapScale = Scale(mapScaler, screen);
            Check(Mathf.Abs((mapRect.yMin - goldRect.yMax) / mapScale - 24f) < .05f
                && Mathf.Abs(mapRect.xMax - goldRect.xMax) < .05f, "Gold is not aligned 24 units below the minimap at " + screen + ".");
        }
    }

    private static float Scale(UnityEngine.UI.CanvasScaler scaler, Vector2 screen)
    {
        CheckAsset(scaler != null && scaler.uiScaleMode == UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize,
            "HUD requires Scale With Screen Size.");
        Vector2 reference = scaler.referenceResolution;
        return Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(screen.x / reference.x, 2f),
            Mathf.Log(screen.y / reference.y, 2f), scaler.matchWidthOrHeight));
    }

    private static Rect ScreenRect(RectTransform rect, UnityEngine.UI.CanvasScaler scaler, Vector2 screen)
    {
        float scale = Scale(scaler, screen);
        Vector2 anchor = Vector2.Scale(rect.anchorMin, screen);
        Vector2 size = rect.sizeDelta * scale;
        return new Rect(anchor + rect.anchoredPosition * scale - Vector2.Scale(size, rect.pivot), size);
    }

    private static RectTransform NamedRect(GameObject root, string name)
    {
        foreach (var rect in root.GetComponentsInChildren<RectTransform>(true)) if (rect.name == name) return rect;
        throw new InvalidOperationException("Missing HUD rect: " + name);
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Hidden).GetValue(target);
    private static void Auto(object target, string property, object value) => target.GetType()
        .GetField("<" + property + ">k__BackingField", Hidden).SetValue(target, value);
    private static void Invoke(object target, string method, params object[] args) => target.GetType()
        .GetMethod(method, Hidden).Invoke(target, args);
    private static void Check(bool condition, string message) { _checks++; CheckAsset(condition, message); }
    private static void CheckAsset(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Minimap integration: " + message); }
}
