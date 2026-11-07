using System;
using System.Collections.Generic;
using System.Reflection;
using StudioBaekho;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Checks current-room enemy selection and pooled rendering without a running enemy AI or NavMesh.</summary>
public static class DungeonMinimapEnemyRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;

    [MenuItem("Tools/Studio Baekho/Dungeon/Run Minimap Enemy Regression")]
    private static void RunMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run minimap enemy fixtures in Edit Mode.");
        _checks = 0;
        var miniPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/DungeonMinimap.prefab");
        var fullPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/DungeonFullMap.prefab");
        Check(miniPrefab != null && fullPrefab != null, "Map prefabs are missing.");
        float savedScale = Time.timeScale;
        int frameRate = Application.targetFrameRate;
        bool cursorVisible = Cursor.visible;
        CursorLockMode cursorLock = Cursor.lockState;
        var existingServices = new HashSet<CombatHitStopService>(Resources.FindObjectsOfTypeAll<CombatHitStopService>());
        Scene previous = SceneManager.GetActiveScene();
        Scene isolated = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        DungeonMinimapPresenter presenter = null;
        try
        {
            SceneManager.SetActiveScene(isolated);
            var miniRoot = (GameObject)PrefabUtility.InstantiatePrefab(miniPrefab, isolated);
            var fullRoot = (GameObject)PrefabUtility.InstantiatePrefab(fullPrefab, isolated);
            var mini = miniRoot.GetComponent<DungeonMinimapView>();
            var full = fullRoot.GetComponent<DungeonMinimapView>();
            presenter = miniRoot.GetComponent<DungeonMinimapPresenter>();
            Check(mini != null && full != null && presenter != null, "Map presentation components are missing.");
            Canvas.ForceUpdateCanvases();
            // Overlay prefab roots may still carry zero scale before the next editor frame.
            miniRoot.transform.localScale = fullRoot.transform.localScale = Vector3.one;
            Time.timeScale = .75f;
            Exercise(presenter, mini, full);
            return $"Minimap enemies: {_checks} checks passed (current-room membership, dormant/boss enemies, "
                + "motion/height, death/inactive removal, clipping, pooling, discovery isolation, room transitions, "
                + "full-map exclusion, reactivation and replacement stages).";
        }
        finally
        {
            if (presenter != null) presenter.CloseFullMap();
            SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(isolated, true);
            foreach (var service in Resources.FindObjectsOfTypeAll<CombatHitStopService>())
                if (!existingServices.Contains(service)) Object.DestroyImmediate(service.gameObject);
            Time.timeScale = savedScale;
            Application.targetFrameRate = frameRate;
            Cursor.visible = cursorVisible;
            Cursor.lockState = cursorLock;
        }
    }

    private static void Exercise(DungeonMinimapPresenter presenter, DungeonMinimapView mini, DungeonMinimapView full)
    {
        var host = new GameObject("EnemyMapRun");
        var stage = host.AddComponent<StageManager>();
        var generator = new GameObject("EnemyMapGenerator").AddComponent<BaekhoDungeonGenerator>();
        var director = generator.gameObject.AddComponent<DungeonEncounterDirector>();
        stage.Configure(generator, director);
        var run = host.AddComponent<RunManager>();
        var player = new GameObject("EnemyMapPlayer").AddComponent<PlayerController>();
        Property(player, "Input", player.GetComponent<PlayerInputHandler>());
        Property(player, "Controller", player.GetComponent<CharacterController>());
        Property(player, "StatManager", player.GetComponent<PlayerStatManager>());
        run.stageManager = stage;
        run.player = player;
        Property(run, "State", RunState.Playing);
        var result = Result(director, 2);
        Install(stage, generator, director, result);
        player.transform.SetPositionAndRotation(result.root.transform.position, result.root.transform.rotation);
        presenter.Configure(run, mini, full);
        Invoke(presenter, "UpdateMap");
        Check(mini.IsVisible && mini.VisibleEnemyMarkerCount == 2 && mini.EnemyMarkerPoolCount == 2,
            "Current waiting room did not show exactly its living members, including dormant AI.");
        Check(full.EnemyMarkerPoolCount == 0 && full.VisibleEnemyMarkerCount == 0, "Presenter supplied enemy dots to the full map.");
        var source = result.startRoom.GetComponent<DungeonRoomEncounter>();
        var first = source.Members[0];
        var second = source.Members[1];
        Check(!second.Enemy.enabled && second.isActiveAndEnabled, "Fixture did not represent a dormant enemy.");
        var markers = Field<RectTransform>(mini, "markerLayer");
        var viewport = Field<RectTransform>(mini, "viewport");
        var dot0 = (RectTransform)markers.Find("EnemyMarker_0");
        var dot1 = (RectTransform)markers.Find("EnemyMarker_1");
        int pool0 = dot0.GetInstanceID(), pool1 = dot1.GetInstanceID();
        var graphic = dot0.GetComponent<DungeonMinimapIconGraphic>();
        Check(graphic.Kind == DungeonMinimapIconGraphic.Symbol.Enemy && !graphic.raycastTarget
            && graphic.GetComponent<CanvasRenderer>() != null && graphic.color.r > graphic.color.g && graphic.color.r > graphic.color.b,
            "Enemy symbol is missing, intercepts input, lacks a renderer or is not red.");
        Check(Field<DungeonMinimapIconGraphic>(mini, "playerArrow").transform.GetSiblingIndex()
            > markers.GetSiblingIndex(), "Enemy marker layer covers the player arrow.");
        int revision = presenter.Model.Revision;
        first.transform.position = result.root.transform.TransformPoint(new Vector3(3f, 50f, 4f));
        Invoke(presenter, "UpdateMap");
        float scale = Mathf.Min(viewport.rect.width, viewport.rect.height) / mini.WorldSpan;
        Check(Vector2.Distance(dot0.anchoredPosition, new Vector2(3f, 4f) * scale) < .01f,
            "Enemy world position was not transformed into dungeon-local XZ independently of height.");
        float partialEdge = mini.WorldSpan * .5f - dot0.sizeDelta.x * .25f / scale;
        first.transform.position = result.root.transform.TransformPoint(new Vector3(partialEdge, 0f, 0f));
        Invoke(presenter, "UpdateMap");
        Check(mini.VisibleEnemyMarkerCount == 1, "A partially clipped enemy dot was left visible at the viewport edge.");
        first.transform.position = result.root.transform.TransformPoint(new Vector3(60f, 80f, 0f));
        Invoke(presenter, "UpdateMap");
        Check(mini.VisibleEnemyMarkerCount == 1 && mini.EnemyMarkerPoolCount == 2,
            "An off-viewport enemy was pinned to an edge or destroyed its pooled marker.");
        Check(presenter.Model.Revision == revision && !presenter.Model.Areas[presenter.Model.GetRoomAreaIndex(3)].Visited,
            "Enemy motion discovered another room or changed exploration state.");
        first.transform.position = result.root.transform.TransformPoint(new Vector3(2f, 0f, 0f));
        Invoke(presenter, "UpdateMap");
        Check(mini.VisibleEnemyMarkerCount == 2 && dot0.GetInstanceID() == pool0, "Returning enemy created a new marker.");

        Property(first, "HasDied", true);
        Invoke(presenter, "UpdateMap");
        Check(mini.VisibleEnemyMarkerCount == 1, "A confirmed death retained its enemy dot.");
        Property(first, "HasDied", false);
        Set(first.Enemy, "_isDie", true);
        Invoke(presenter, "UpdateMap");
        Check(mini.VisibleEnemyMarkerCount == 1, "A dead actor retained its dot before corpse deletion.");
        Set(first.Enemy, "_isDie", false);
        second.gameObject.SetActive(false);
        Invoke(presenter, "UpdateMap");
        Check(mini.VisibleEnemyMarkerCount == 1, "An inactive enemy retained its dot.");
        second.gameObject.SetActive(true);
        second.enabled = false;
        Invoke(presenter, "UpdateMap");
        Check(mini.VisibleEnemyMarkerCount == 1, "A disabled encounter member retained its dot.");
        second.enabled = true;
        Invoke(presenter, "UpdateMap");
        Check(mini.VisibleEnemyMarkerCount == 2 && mini.EnemyMarkerPoolCount == 2
            && dot0.GetInstanceID() == pool0 && dot1.GetInstanceID() == pool1,
            "Member removal/reactivation grew or replaced the marker pool.");
        Object.DestroyImmediate(first.gameObject);
        Invoke(presenter, "UpdateMap");
        Check(mini.VisibleEnemyMarkerCount == 1 && mini.EnemyMarkerPoolCount == 2, "A destroyed member retained its dot or shrank the pool.");
        Property(source, "State", DungeonEncounterState.Fighting);
        Property(second, "HasDied", true);
        Invoke(presenter, "UpdateMap");
        Check(mini.VisibleEnemyMarkerCount == 0, "Enemy markers remained between waves.");
        EnemyMember(source, result.root.transform, new Vector3(1f, 0f, 1f), false);
        Invoke(presenter, "UpdateMap");
        Check(mini.VisibleEnemyMarkerCount == 1 && mini.EnemyMarkerPoolCount == 2,
            "A newly spawned wave did not reuse the enemy marker pool.");
        Property(source, "State", DungeonEncounterState.Cleared);
        Invoke(presenter, "UpdateMap");
        Check(mini.VisibleEnemyMarkerCount == 0, "Cleared room retained living-looking stale member dots.");
        Property(source, "State", DungeonEncounterState.Waiting);
        MovePlayer(presenter, player, result.root.transform, new Vector2(30f, 0f));
        Check(mini.VisibleEnemyMarkerCount == 1 && presenter.Model.Areas[presenter.Model.CurrentAreaIndex].RoomId == 2,
            "Switching to a boss room retained source-room enemies or omitted its boss.");
        MovePlayer(presenter, player, result.root.transform, new Vector2(15f, 0f));
        Check(presenter.Model.Areas[presenter.Model.CurrentAreaIndex].Kind == DungeonMinimapAreaKind.Corridor
            && mini.VisibleEnemyMarkerCount == 0, "Corridor inherited enemies from a nearby room.");
        MovePlayer(presenter, player, result.root.transform, new Vector2(15f, 20f));
        Check(presenter.Model.Areas[presenter.Model.CurrentAreaIndex].Kind == DungeonMinimapAreaKind.Corner
            && mini.VisibleEnemyMarkerCount == 0, "Corner inherited room enemy dots.");
        MovePlayer(presenter, player, result.root.transform, new Vector2(1000f, 1000f));
        Check(presenter.Model.CurrentAreaIndex == -1 && mini.VisibleEnemyMarkerCount == 0, "Position outside the map retained enemy dots.");
        MovePlayer(presenter, player, result.root.transform, Vector2.zero);
        Check(mini.VisibleEnemyMarkerCount == 1 && mini.EnemyMarkerPoolCount == 2, "Returning to a room failed to reuse its marker pool.");
        Property(director, "IsReady", false);
        Invoke(presenter, "UpdateMap");
        Check(mini.VisibleEnemyMarkerCount == 0, "Unready encounter data retained enemy dots.");
        Property(director, "IsReady", true);
        Invoke(presenter, "UpdateMap");
        Check(mini.VisibleEnemyMarkerCount == 1, "Ready room did not recover its marker.");

        full.SetEnemyPositions(new[] { Vector2.zero });
        Check(full.EnemyMarkerPoolCount == 0, "EntireDungeon mode allocated enemy markers.");
        presenter.ToggleFullMap();
        Invoke(presenter, "UpdateMap");
        Check(presenter.IsFullMapOpen && full.IsVisible && mini.VisibleEnemyMarkerCount == 0
            && full.VisibleEnemyMarkerCount == 0 && full.EnemyMarkerPoolCount == 0,
            "Opening the full map leaked enemy dots into either visible map.");
        presenter.CloseFullMap();
        Invoke(presenter, "UpdateMap");
        Check(mini.VisibleEnemyMarkerCount == 1 && dot0.GetInstanceID() == pool0, "Closing full map failed to restore pooled minimap enemies.");
        Invoke(presenter, "OnDisable");
        Check(!mini.IsVisible && mini.VisibleEnemyMarkerCount == 0, "Presenter deactivation retained visible enemy dots.");
        Invoke(presenter, "OnEnable");
        Invoke(presenter, "UpdateMap");
        Check(mini.VisibleEnemyMarkerCount == 1 && mini.EnemyMarkerPoolCount == 2 && dot0.GetInstanceID() == pool0,
            "Presenter reactivation lost or duplicated its pooled enemies.");

        var next = Result(director, 3);
        Install(stage, generator, director, next);
        player.transform.SetPositionAndRotation(next.root.transform.position, next.root.transform.rotation);
        Invoke(presenter, "OnStageStarted");
        Check(dot0 == null && dot1 == null && mini.EnemyMarkerPoolCount == 3 && mini.VisibleEnemyMarkerCount == 3,
            "Replacement stage retained old enemy graphics/cache or omitted same-ID new room members.");
        Check(!presenter.Model.Areas[presenter.Model.GetRoomAreaIndex(3)].Visited,
            "New stage enemy positions revealed an unvisited room.");
        mini.Clear();
        Check(mini.EnemyMarkerPoolCount == 0 && mini.VisibleEnemyMarkerCount == 0, "Clear retained pooled enemy graphics.");
    }

    private static BaekhoDungeonResult Result(DungeonEncounterDirector director, int startEnemyCount)
    {
        var result = new BaekhoDungeonResult { success = true, root = new GameObject("EnemyMapGeneration"),
            plan = new BaekhoDungeonPlan { startMapId = 1, bossMapId = 2, endMapId = 3 } };
        result.root.transform.SetPositionAndRotation(new Vector3(120f, 7f, -80f), Quaternion.Euler(0f, 90f, 0f));
        var rooms = Field<List<DungeonRoomEncounter>>(director, "_rooms");
        rooms.Clear();
        for (int i = 0; i < 3; i++)
        {
            var room = new GameObject("EnemyMapRoom_" + (i + 1)).AddComponent<BaekhoMapModule>();
            room.transform.SetParent(result.root.transform, false);
            room.transform.localPosition = new Vector3(i * 30f, 0f, 0f);
            room.localBounds = new Bounds(Vector3.zero, new Vector3(10f, 4f, 10f));
            var identity = room.gameObject.AddComponent<BaekhoGeneratedRoom>();
            identity.instanceId = i + 1;
            identity.role = i == 0 ? BaekhoRoomRole.Start : i == 1 ? BaekhoRoomRole.Boss : BaekhoRoomRole.End;
            var encounter = room.gameObject.AddComponent<DungeonRoomEncounter>();
            Property(encounter, "State", i == 1 ? DungeonEncounterState.Fighting : DungeonEncounterState.Waiting);
            rooms.Add(encounter);
            result.rooms.Add(room);
            result.plan.rooms.Add(new BaekhoPlacedModule { id = i + 1, prefab = room, position = room.transform.localPosition, role = identity.role });
            int count = i == 0 ? startEnemyCount : 1;
            for (int j = 0; j < count; j++)
                EnemyMember(encounter, result.root.transform, new Vector3(j + 1f, 0f, i == 0 ? j : 1f), true);
        }
        // A nearby free-standing enemy and nearby members of other rooms must not be globally scanned.
        EnemyMember(null, result.root.transform, Vector3.one, false);
        var corner = new GameObject("EnemyMapCornerMetadata").AddComponent<BaekhoMapModule>();
        corner.transform.SetParent(result.root.transform, false);
        corner.localBounds = new Bounds(Vector3.zero, new Vector3(6f, 4f, 6f));
        var link = new BaekhoDungeonLink { fromMapId = 1, toMapId = 2, width = 4f };
        link.segments.Add(new BaekhoCorridorSegment(new Vector3(5f, 0f, 0f), new Vector3(25f, 0f, 0f)));
        link.corners.Add(new BaekhoPlacedModule { id = 10, prefab = corner, position = new Vector3(15f, 0f, 20f) });
        result.plan.links.Add(link);
        result.startRoom = result.rooms[0];
        result.bossRoom = result.rooms[1];
        result.endRoom = result.rooms[2];
        return result;
    }

    private static DungeonEncounterMember EnemyMember(DungeonRoomEncounter room, Transform root, Vector3 local, bool dormant)
    {
        var host = new GameObject(room != null ? "Tracked enemy" : "Untracked nearby enemy");
        host.transform.SetParent(room != null ? room.transform : root, false);
        host.transform.position = root.TransformPoint(local);
        var enemy = host.AddComponent<Enemy>();
        var stats = host.GetComponent<EnemyStat>();
        stats.CurrentHp = 100f;
        enemy.EnemyStat = stats;
        enemy.enabled = !dormant;
        var member = host.AddComponent<DungeonEncounterMember>();
        Property(member, "Enemy", enemy);
        Set(member, "_stats", stats);
        if (room != null) Field<List<DungeonEncounterMember>>(room, "_members").Add(member);
        return member;
    }
    private static void Install(StageManager stage, BaekhoDungeonGenerator generator, DungeonEncounterDirector director, BaekhoDungeonResult result)
    {
        Property(stage, "Result", result); Property(stage, "IsReady", true);
        Property(generator, "CurrentRoot", result.root); Property(generator, "LastResult", result);
        Property(director, "IsReady", true); Set(director, "_root", result.root);
    }
    private static void MovePlayer(DungeonMinimapPresenter presenter, PlayerController player, Transform root, Vector2 local)
    {
        presenter.Model.ResetMotionHistory();
        Set(presenter, "_tracking", false);
        player.transform.position = root.TransformPoint(new Vector3(local.x, 0f, local.y));
        Invoke(presenter, "UpdateMap");
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Hidden).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
    private static void Property(object target, string name, object value)
        => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public).SetValue(target, value);
    private static void Invoke(object target, string name) => target.GetType().GetMethod(name, Hidden).Invoke(target, null);
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException("Minimap enemies: " + message); }
}
