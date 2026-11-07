using System;
using System.Collections.Generic;
using System.Reflection;
using StudioBaekho;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Full-map presentation regressions isolated from the user's open scene and game state.</summary>
public static class DungeonFullMapRegression
{
    private const string MiniPath = "Assets/_Project/Prefabs/UI/DungeonMinimap.prefab";
    private const string FullPath = "Assets/_Project/Prefabs/UI/DungeonFullMap.prefab";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static float _fixtureWorldTime;

    [MenuItem("Tools/Studio Baekho/Dungeon/Run Full Map Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run full-map regression in Edit Mode.");
        _checks = 0;
        var miniPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MiniPath);
        var fullPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FullPath);
        Check(miniPrefab != null && fullPrefab != null, "Minimap/full-map prefab is missing.");
        Graphics(miniPrefab);
        Graphics(fullPrefab);
        var existingServices = new HashSet<CombatHitStopService>(Resources.FindObjectsOfTypeAll<CombatHitStopService>());
        Scene previous = SceneManager.GetActiveScene();
        Scene isolated = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        int frameRate = Application.targetFrameRate;
        float timeScale = Time.timeScale;
        bool cursorVisible = Cursor.visible;
        CursorLockMode cursorLock = Cursor.lockState;
        try
        {
            SceneManager.SetActiveScene(isolated);
            var miniRoot = (GameObject)PrefabUtility.InstantiatePrefab(miniPrefab, isolated);
            var fullRoot = (GameObject)PrefabUtility.InstantiatePrefab(fullPrefab, isolated);
            Graphics(miniRoot);
            Graphics(fullRoot);
            var mini = miniRoot.GetComponent<DungeonMinimapView>();
            var full = fullRoot.GetComponent<DungeonMinimapView>();
            var presenter = miniRoot.GetComponent<DungeonMinimapPresenter>();
            Check(mini != null && full != null && presenter != null, "Map presentation components are missing.");
            Check(full.Mode == DungeonMinimapView.MapMode.EntireDungeon
                && mini.Mode == DungeonMinimapView.MapMode.FollowPlayer, "Prefab map modes are incorrect.");
            Check(fullRoot.GetComponent<Canvas>().sortingOrder == 160, "Full-map canvas order must be 160.");
            Canvas.ForceUpdateCanvases();
            // Overlay prefab roots can retain a serialized zero scale until the next editor frame.
            // This synchronous fixture needs invertible transforms for viewport/world conversions.
            miniRoot.transform.localScale = Vector3.one;
            fullRoot.transform.localScale = Vector3.one;
            // Use a non-default speed so a close incorrectly writing one cannot pass.
            Time.timeScale = .75f;
            _fixtureWorldTime = Time.timeScale;
            Exercise(presenter, mini, full);
            return $"Full map: {_checks} checks passed (shared exploration, toggle/close, fit, transformed coordinates, " +
                "markers, pointer selection/hover, blocked clicks, owned pause, repeat opens, modal conflicts, " +
                "disabled views/runs, stage replacement and targeted input interception).";
        }
        finally
        {
            SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(isolated, true);
            foreach (var service in Resources.FindObjectsOfTypeAll<CombatHitStopService>())
                if (!existingServices.Contains(service)) Object.DestroyImmediate(service.gameObject);
            Application.targetFrameRate = frameRate;
            Time.timeScale = timeScale;
            Cursor.visible = cursorVisible;
            Cursor.lockState = cursorLock;
        }
    }

    public static string ValidateInstalledScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        DungeonMinimapPresenter presenter = null;
        int count = 0;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var candidate in root.GetComponentsInChildren<DungeonMinimapPresenter>(true))
            { presenter = candidate; count++; }
        Check(count == 1 && presenter != null && presenter.FullMapView != null, "Expected one presenter explicitly bound to a full map.");
        Check(presenter.FullMapView.gameObject.scene == scene
            && presenter.FullMapView.Mode == DungeonMinimapView.MapMode.EntireDungeon,
            "Full-map reference must use this scene's EntireDungeon view.");
        Graphics(presenter.FullMapView.gameObject);
        return "Installed full map: explicit scene-local binding, EntireDungeon mode, renderers and targeted travel input verified.";
    }

    private static void Exercise(DungeonMinimapPresenter presenter, DungeonMinimapView mini, DungeonMinimapView full)
    {
        var host = new GameObject("FullMapRunFixture");
        var run = host.AddComponent<RunManager>();
        var stage = host.AddComponent<StageManager>();
        var generator = new GameObject("GeneratorFixture").AddComponent<BaekhoDungeonGenerator>();
        stage.Configure(generator, null);
        var player = new GameObject("PlayerFixture").AddComponent<PlayerController>();
        Auto(player, "Input", player.GetComponent<PlayerInputHandler>());
        Auto(player, "Controller", player.GetComponent<CharacterController>());
        Auto(player, "StatManager", player.GetComponent<PlayerStatManager>());
        player.StatManager.EnsureInitialized();
        run.stageManager = stage;
        run.player = player;
        var result = Result();
        InstallResult(stage, generator, result);
        Auto(run, "State", RunState.Playing);
        player.transform.position = result.root.transform.TransformPoint(result.plan.rooms[0].WorldBounds.center);
        presenter.Configure(run, mini, full);
        Invoke(presenter, "UpdateMap");
        Check(mini.IsVisible && !full.IsVisible && !presenter.IsFullMapOpen, "A stage did not begin in minimap mode.");

        var model = presenter.Model;
        float worldTime = Time.timeScale;
        presenter.ToggleFullMap();
        Invoke(presenter, "UpdateMap");
        Check(presenter.IsFullMapOpen && full.IsVisible && !mini.IsVisible, "Toggle did not switch to full-map presentation.");
        Check(run.IsMapOpen && player.IsMapPaused && Mathf.Approximately(Time.timeScale, 0f),
            "Opening the full map did not acquire its owned player/world pause.");
        Check(Cursor.visible && Cursor.lockState == CursorLockMode.None, "Opening the full map did not release the pointer.");
        Check(ReferenceEquals(Field<DungeonMinimapModel>(mini, "_model"), model)
            && ReferenceEquals(Field<DungeonMinimapModel>(full, "_model"), model), "The views do not share the same exploration model.");
        Check(full.BuiltAreaCount == model.Areas.Count, "Full map omitted rooms, corridors or corners.");
        var content = Field<RectTransform>(full, "mapContent");
        var markers = Field<RectTransform>(full, "markerLayer");
        var viewport = Field<RectTransform>(full, "viewport");
        var arrow = Field<DungeonMinimapIconGraphic>(full, "playerArrow").rectTransform;
        Check(markers.childCount == 2 && !markers.Find("ShopMarker").gameObject.activeSelf
            && !markers.Find("ExitMarker").gameObject.activeSelf, "Opening the full map revealed undiscovered destinations.");
        int start = model.GetRoomAreaIndex(result.plan.startMapId), shop = model.GetRoomAreaIndex(result.plan.shopMapId);
        Check(Vector2.Distance(InViewport(viewport, arrow), InViewport(viewport, (RectTransform)content.GetChild(start))) < .05f,
            "Full-map player does not coincide with its transformed start-room position.");
        Check(arrow.anchoredPosition.sqrMagnitude > 1f, "Full-map player was incorrectly pinned to the viewport center.");
        Fits(viewport, content);
        Vector2 fixedOrigin = content.anchoredPosition, fixedRoomSize = ((RectTransform)content.GetChild(start)).sizeDelta;
        int geometryCount = content.childCount, firstGeometry = content.GetChild(0).GetInstanceID();
        int miniCount = Field<RectTransform>(mini, "mapContent").childCount;

        Vector2 shopCenter = model.Areas[shop].Center;
        player.transform.SetPositionAndRotation(result.root.transform.TransformPoint(new Vector3(shopCenter.x, 35f, shopCenter.y)),
            result.root.transform.rotation * Quaternion.Euler(0f, 90f, 0f));
        Invoke(presenter, "UpdateMap");
        Check(model.Areas[shop].Visited && markers.Find("ShopMarker").gameObject.activeSelf
            && !markers.Find("ExitMarker").gameObject.activeSelf, "Full-map exploration or per-room icon discovery failed.");
        Check(Vector2.Distance(InViewport(viewport, arrow), InViewport(viewport, (RectTransform)content.GetChild(shop))) < .05f
            && Mathf.Abs(Mathf.DeltaAngle(arrow.localEulerAngles.z, -90f)) < .01f,
            "Root rotation, height or character facing shifted the full-map player.");
        Check(Vector2.Distance(content.anchoredPosition, fixedOrigin) < .01f
            && Vector2.Distance(((RectTransform)content.GetChild(start)).sizeDelta, fixedRoomSize) < .01f,
            "Full-map fit changed when the player moved or discovered a room.");
        var shopRect = (RectTransform)content.GetChild(shop);
        float scale = shopRect.sizeDelta.x / model.Areas[shop].Size.x;
        Vector3 anchor = result.root.transform.InverseTransformPoint(result.shopRoom.GetComponent<StageMapAnchors>().shopPoint.position);
        Vector2 expectedMarkerDelta = (new Vector2(anchor.x, anchor.z) - shopCenter) * scale;
        var markerRect = (RectTransform)markers.Find("ShopMarker");
        Vector2 actualMarkerDelta = InViewport(viewport, markerRect) - InViewport(viewport, shopRect);
        Check(Vector2.Distance(actualMarkerDelta, expectedMarkerDelta) < .05f,
            "Full-map shop marker ignored its actual anchor location. actual=" + actualMarkerDelta.ToString("F5")
            + ", expected=" + expectedMarkerDelta.ToString("F5") + ", shopCenter=" + shopCenter.ToString("F5")
            + ", anchor=" + anchor.ToString("F5") + ", scale=" + scale.ToString("F5")
            + ", markerAnchored=" + markerRect.anchoredPosition.ToString("F5") + ", viewportRect=" + viewport.rect
            + ", markerParentRect=" + ((RectTransform)markerRect.parent).rect
            + ", markerViewport=" + InViewport(viewport, markerRect).ToString("F5")
            + ", shopViewport=" + InViewport(viewport, shopRect).ToString("F5"));
        Pointer(presenter, mini, full, model, result, run);
        for (int i = 0; i < 4; i++)
        {
            if (i == 0)
            {
                // Edit Mode motion completes synchronously; the user-dismissal path
                // must still release ownership once and remain safe when repeated.
                presenter.RequestCloseFullMap();
                presenter.RequestCloseFullMap();
            }
            else presenter.CloseFullMap();
            Invoke(presenter, "UpdateMap");
            Check(mini.IsVisible && !full.IsVisible && model.Areas[shop].Visited, "Close lost discovery or failed to restore the minimap.");
            Check(!run.IsMapOpen && !player.IsMapPaused && Mathf.Approximately(Time.timeScale, worldTime),
                "Closing failed to release only the full map's pause.");
            presenter.ToggleFullMap();
            Invoke(presenter, "UpdateMap");
        }
        Check(content.childCount == geometryCount && content.GetChild(0).GetInstanceID() == firstGeometry
            && markers.childCount == 2 && Field<RectTransform>(mini, "mapContent").childCount == miniCount,
            "Repeated toggles rebuilt or duplicated map geometry/icons.");
        Check(Mathf.Approximately(Time.timeScale, 0f), "An open full map lost its world pause.");
        Graphics(mini.gameObject);
        Graphics(full.gameObject);

        Auto(run, "IsShopOpen", true);
        Invoke(run, "NotifyShopOpenChanged", true);
        Invoke(presenter, "UpdateMap");
        Closed(presenter, full, "Shop opening");
        presenter.ToggleFullMap();
        Check(!presenter.IsFullMapOpen, "Full map opened over the shop.");
        Auto(run, "IsShopOpen", false);
        Invoke(run, "NotifyShopOpenChanged", false);
        presenter.ToggleFullMap();
        Set(player, "_finisherAttack", new PlayerAttackState(player));
        Invoke(presenter, "UpdateMap");
        Closed(presenter, full, "Finisher selection");
        presenter.ToggleFullMap();
        Check(!presenter.IsFullMapOpen, "Full map opened over finisher selection.");
        Set(player, "_finisherAttack", null);

        foreach (RunState state in new[] { RunState.Preparing, RunState.Error, RunState.Won, RunState.Lost })
        {
            State(run, RunState.Playing);
            Invoke(presenter, "UpdateMap");
            presenter.ToggleFullMap();
            State(run, state);
            Invoke(presenter, "UpdateMap");
            Closed(presenter, full, state.ToString());
            Check(!mini.IsVisible, "Minimap remained visible during " + state + ".");
            presenter.ToggleFullMap();
            Check(!presenter.IsFullMapOpen, "Full map opened during " + state + ".");
        }
        State(run, RunState.Playing);
        Invoke(presenter, "UpdateMap");
        presenter.ToggleFullMap();
        Invoke(presenter, "OnDisable");
        Closed(presenter, full, "Presenter deactivation");
        Invoke(presenter, "OnEnable");
        Invoke(presenter, "UpdateMap");
        Check(!presenter.IsFullMapOpen && mini.IsVisible && model.Areas[shop].Visited, "Reactivation lost visits or reopened the full map.");
        presenter.ToggleFullMap();
        run.enabled = false;
        Invoke(presenter, "UpdateMap");
        Closed(presenter, full, "Run deactivation");
        Check(!mini.IsVisible, "A disabled run left the minimap visible.");
        run.enabled = true;
        Invoke(presenter, "UpdateMap");
        presenter.ToggleFullMap();
        var next = Result();
        InstallResult(stage, generator, next);
        player.transform.position = next.root.transform.TransformPoint(next.plan.rooms[0].WorldBounds.center);
        Invoke(presenter, "OnStageStarted");
        Closed(presenter, full, "Replacement stage");
        Check(!model.Areas[model.GetRoomAreaIndex(next.plan.shopMapId)].Visited, "Replacement stage retained old exploration.");
        Check(content.childCount == geometryCount && markers.childCount == 2, "Replacement stage duplicated full-map geometry or markers.");
    }

    private static void Pointer(DungeonMinimapPresenter presenter, DungeonMinimapView mini, DungeonMinimapView full,
        DungeonMinimapModel model, BaekhoDungeonResult result, RunManager run)
    {
        var viewport = Field<RectTransform>(full, "viewport");
        var content = Field<RectTransform>(full, "mapContent");
        var markers = Field<RectTransform>(full, "markerLayer");
        int start = model.GetRoomAreaIndex(result.plan.startMapId);
        int shop = model.GetRoomAreaIndex(result.plan.shopMapId);
        int exit = model.GetRoomAreaIndex(result.plan.endMapId);
        int corner = model.GetRoomAreaIndex(10);
        int corridor = -1;
        for (int i = 0; i < model.Areas.Count; i++)
            if (model.Areas[i].Kind == DungeonMinimapAreaKind.Corridor) { corridor = i; break; }
        Check(corner >= 0 && corridor >= 0, "Pointer fixture requires a corner and corridor.");
        model.UpdatePosition(model.Areas[corner].Center, false);
        model.UpdatePosition(model.Areas[corridor].Center, false);
        model.UpdatePosition(model.Areas[shop].Center, false);
        mini.RefreshExploration(model);
        full.RefreshExploration(model);
        int revision = model.Revision;
        var originalAvailability = Field<Func<int, string>>(full, "_getTravelBlockReason");
        int clicks = 0, clickedRoom = -1;
        Action<int> observe = id => { clicks++; clickedRoom = id; };
        full.RoomClicked += observe;
        try
        {
            full.SetTravelAvailability(id => id == result.plan.shopMapId ? "검증용 이동 제한" : null);
            Vector2 startPoint = Screen((RectTransform)content.GetChild(start));
            Vector2 shopPoint = Screen((RectTransform)content.GetChild(shop));
            Check(full.TryGetRoomAtScreenPoint(startPoint, null, out int roomId) && roomId == result.plan.startMapId,
                "A visited room was not selectable through its rendered rectangle.");
            Check(!full.TryGetRoomAtScreenPoint(Screen((RectTransform)content.GetChild(exit)), null, out roomId),
                "An undiscovered exit room was selectable.");
            Check(!full.TryGetRoomAtScreenPoint(Screen((RectTransform)content.GetChild(corner)), null, out roomId),
                "A visited corner was selectable as a destination room.");
            Check(!full.TryGetRoomAtScreenPoint(Screen((RectTransform)content.GetChild(corridor)), null, out roomId),
                "A visited corridor was selectable as a destination room.");
            Vector2 background = RectTransformUtility.WorldToScreenPoint(null,
                viewport.TransformPoint(new Vector3(viewport.rect.xMin + 1f, viewport.rect.yMax - 1f, 0f)));
            Check(!full.TryGetRoomAtScreenPoint(background, null, out roomId), "Empty map background selected a room.");
            Vector2 outside = RectTransformUtility.WorldToScreenPoint(null,
                viewport.TransformPoint(new Vector3(viewport.rect.xMin - 20f, viewport.rect.yMin - 20f, 0f)));
            Check(!full.TryGetRoomAtScreenPoint(outside, null, out roomId), "A point outside the clipped viewport selected a room.");

            full.UpdatePointer(shopPoint, null);
            var outline = Field<RectTransform>(full, "_hoverOutline");
            var status = Field<TMPro.TMP_Text>(full, "travelStatus");
            Check(outline != null && outline.gameObject.activeSelf && Field<int>(full, "_hoveredAreaIndex") == shop,
                "Hover did not highlight the visited room.");
            Check(status != null && status.text.Contains("검증용 이동 제한"), "Hover did not display its authoritative block reason.");
            full.ClickPointer(shopPoint, null);
            Check(clicks == 0, "A blocked room emitted a travel click.");

            // A visible destination icon owns its click even when it visually overlaps another visited room.
            var icon = (RectTransform)markers.Find("ShopMarker");
            Vector2 iconPosition = icon.anchoredPosition;
            try
            {
                icon.position = ((RectTransform)content.GetChild(start)).position;
                Vector2 iconPoint = Screen(icon);
                Check(full.TryGetRoomAtScreenPoint(iconPoint, null, out roomId) && roomId == result.plan.shopMapId,
                    "An overlapping destination icon did not prioritize its own room.");
                full.ClickPointer(iconPoint, null);
                Check(clicks == 0, "An icon bypassed its owner's travel restriction.");
            }
            finally { icon.anchoredPosition = iconPosition; }

            full.ClickPointer(startPoint, null);
            Check(clicks == 1 && clickedRoom == result.plan.startMapId, "An allowed room click was not emitted exactly once.");
            full.ClearPointer();
            Check(!outline.gameObject.activeSelf && Field<int>(full, "_hoveredAreaIndex") == -1,
                "Leaving the viewport retained its hover highlight.");
            Check(model.Revision == revision, "Pointer selection or highlighting mutated exploration.");
            Graphics(mini.gameObject);
            Graphics(full.gameObject);

            presenter.CloseFullMap();
            Closed(presenter, full, "Pointer fixture close");
            Check(!full.TryGetRoomAtScreenPoint(startPoint, null, out roomId), "A hidden full map remained selectable.");
            full.ClickPointer(startPoint, null);
            Check(clicks == 1, "A hidden full map emitted a travel click.");
            presenter.ToggleFullMap();
            Invoke(presenter, "UpdateMap");
            Check(run.IsMapOpen && Mathf.Approximately(Time.timeScale, 0f), "The full map could not reopen after pointer close.");

            full.enabled = false;
            // Edit Mode fixtures explicitly dispatch lifecycle callbacks without advancing the user's game.
            Invoke(full, "OnDisable");
            Closed(presenter, full, "Standalone full-view deactivation");
            Check(!run.IsMapOpen && !run.player.IsMapPaused, "Disabling only the independent full view retained its run-owned pause.");
            full.enabled = true;
            Invoke(presenter, "UpdateMap");
            presenter.ToggleFullMap();
            Invoke(presenter, "UpdateMap");
            Check(full.IsVisible && run.IsMapOpen, "The full view failed to reopen after its own reactivation.");
            Check(model.Revision == revision, "View deactivation or reactivation erased/changed exploration.");
        }
        finally
        {
            full.RoomClicked -= observe;
            full.SetTravelAvailability(originalAvailability);
            full.ClearPointer();
        }
    }

    private static BaekhoDungeonResult Result()
    {
        var result = new BaekhoDungeonResult { success = true, root = new GameObject("GeneratedFullMapFixture") };
        result.root.transform.SetPositionAndRotation(new Vector3(125f, 7f, -80f), Quaternion.Euler(0f, 90f, 0f));
        var module = new GameObject("ModuleFixture").AddComponent<BaekhoMapModule>();
        module.localBounds = new Bounds(new Vector3(1f, 0f, -2f), new Vector3(10f, 4f, 20f));
        var socket = new GameObject("SocketFixture").AddComponent<BaekhoMapConnection>();
        socket.transform.SetParent(module.transform, false);
        socket.transform.localPosition = new Vector3(5f, 0f, 0f);
        socket.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        socket.width = 4f;
        module.connections = new[] { socket };
        result.plan = new BaekhoDungeonPlan { startMapId = 1, shopMapId = 2, endMapId = 3 };
        var positions = new[] { new Vector3(-120f, 0f, -30f), new Vector3(0f, 0f, 25f), new Vector3(220f, 0f, 120f) };
        for (int i = 0; i < positions.Length; i++)
        {
            var role = i == 0 ? BaekhoRoomRole.Start : i == 1 ? BaekhoRoomRole.Shop : BaekhoRoomRole.End;
            var placed = new BaekhoPlacedModule { id = i + 1, prefab = module, position = positions[i], role = role, quarterTurns = i };
            result.plan.rooms.Add(placed);
            var room = new GameObject("RoomFixture_" + i).AddComponent<BaekhoMapModule>();
            room.transform.SetParent(result.root.transform, false);
            room.transform.localPosition = positions[i];
            room.transform.localRotation = placed.Rotation;
            var anchor = new GameObject("AnchorFixture").transform;
            anchor.SetParent(room.transform, false);
            anchor.localPosition = new Vector3(1.5f, 0f, -2f);
            var anchors = room.gameObject.AddComponent<StageMapAnchors>();
            anchors.shopPoint = anchors.exitPoint = anchor;
            result.rooms.Add(room);
            if (i == 0) result.startRoom = room;
            else if (i == 1) result.shopRoom = room;
            else result.endRoom = room;
        }
        var link = new BaekhoDungeonLink { fromMapId = 1, toMapId = 2, fromSocketIndex = 0, toSocketIndex = 0, width = 4f };
        link.segments.Add(new BaekhoCorridorSegment(new Vector3(-115f, 0f, -30f), new Vector3(0f, 0f, 25f)));
        link.corners.Add(new BaekhoPlacedModule { id = 10, prefab = module, position = new Vector3(-50f, 0f, 80f), quarterTurns = 1 });
        result.plan.links.Add(link);
        return result;
    }

    private static void Fits(RectTransform viewport, RectTransform content)
    {
        var corners = new Vector3[4];
        foreach (RectTransform rect in content)
        {
            rect.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                Vector2 point = viewport.InverseTransformPoint(corner);
                Rect bounds = viewport.rect;
                Check(point.x >= bounds.xMin - .1f && point.x <= bounds.xMax + .1f
                    && point.y >= bounds.yMin - .1f && point.y <= bounds.yMax + .1f,
                    "Full-map fit cropped a room, rotated corridor, corner or entrance stub.");
            }
        }
    }
    private static void Graphics(GameObject root)
    {
        var view = root.GetComponent<DungeonMinimapView>();
        bool full = view != null && view.Mode == DungeonMinimapView.MapMode.EntireDungeon;
        var pointer = full ? Field<DungeonMapPointerHandler>(view, "pointerHandler") : null;
        var surface = pointer != null ? pointer.GetComponent<UnityEngine.UI.Graphic>() : null;
        var blocker = full ? Field<UnityEngine.UI.Graphic>(view, "inputBlocker") : null;
        var backdrop = full ? root.GetComponent<BoundaryZeroBackdrop>() : null;
        var captureBlocker = backdrop != null ? Field<UnityEngine.UI.RawImage>(backdrop, "background") : null;
        if (full)
            Check(root.GetComponent<UnityEngine.UI.GraphicRaycaster>() != null && pointer != null
                && surface != null && blocker != null && surface != blocker,
                "The full map lacks its two targeted input graphics or raycaster.");
        foreach (var graphic in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
        {
            // The capture surface protects the one hidden capture frame, and the authored close
            // button remains a raycast target. The root CanvasGroup gates both when hidden.
            bool intercepts = full && ((view.IsVisible && (graphic == surface || graphic == blocker))
                || graphic == captureBlocker || graphic.GetComponent<UnityEngine.UI.Button>() != null);
            Check(graphic.GetComponent<CanvasRenderer>() != null && graphic.raycastTarget == intercepts,
                "Graphic lacks its renderer or intercepts input: " + graphic.name);
        }
        if (view != null)
        {
            var group = Field<CanvasGroup>(view, "visibility");
            bool interactive = full && view.IsVisible;
            Check(group != null && group.interactable == interactive && group.blocksRaycasts == interactive,
                "Map CanvasGroup interception does not match its presentation mode/visibility.");
        }
    }
    private static Vector2 Screen(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(null, rect.position);
    private static Vector2 InViewport(RectTransform viewport, RectTransform rect) => viewport.InverseTransformPoint(rect.position);
    private static void Closed(DungeonMinimapPresenter presenter, DungeonMinimapView full, string reason)
    {
        Check(!presenter.IsFullMapOpen && !full.IsVisible, reason + " did not close the full map.");
        Check(presenter.Run == null || (!presenter.Run.IsMapOpen && !presenter.Run.player.IsMapPaused),
            reason + " retained map ownership or the player's map gate.");
        Check(Mathf.Approximately(Time.timeScale, _fixtureWorldTime), reason + " did not restore the previous world speed.");
    }
    private static void InstallResult(StageManager stage, BaekhoDungeonGenerator generator, BaekhoDungeonResult result)
    { Auto(stage, "Result", result); Auto(stage, "IsReady", true); Auto(generator, "CurrentRoot", result.root); }
    private static void State(RunManager run, RunState state)
    { Auto(run, "State", state); (Field<Action<RunState>>(run, "StateChanged"))?.Invoke(state); }
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Hidden).GetValue(owner);
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Hidden).SetValue(owner, value);
    private static void Auto(object owner, string name, object value) => Set(owner, "<" + name + ">k__BackingField", value);
    private static void Invoke(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Hidden).Invoke(owner, args);
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException("Full-map regression: " + message); }
}
