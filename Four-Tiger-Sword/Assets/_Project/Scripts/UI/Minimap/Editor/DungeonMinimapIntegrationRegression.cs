using System;
using System.Collections.Generic;
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
        var definition = ScriptableObject.CreateInstance<RunDefinitionSO>();
        definition.chapters = new[] { new ChapterDefinition { displayName = "Chapter fixture",
            stages = new[] { new StageDefinition(), new StageDefinition() } } };
        DungeonMinimapPresenter presenter = null;
        try
        {
            SceneManager.SetActiveScene(isolated);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, isolated);
            var view = instance.GetComponent<DungeonMinimapView>();
            presenter = instance.GetComponent<DungeonMinimapPresenter>();
            Check(view != null && presenter != null, "Minimap prefab lacks its view or presenter.");
            foreach (var graphic in instance.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                Check(graphic.GetComponent<CanvasRenderer>() != null, "Instantiated graphic has no CanvasRenderer: " + graphic.name);
            Canvas.ForceUpdateCanvases();
            Lifecycle(view, presenter, definition);
            Layout(instance);
            return $"Minimap integration: {_checks} checks passed (discovery, markers, transformed roots, " +
                "height, stage labels, reward notices, UI reactivation, replacement stages, input pass-through and multiple aspect ratios).";
        }
        finally
        {
            if (presenter != null && Field<DungeonRewardNotice>(presenter, "_rewardNotice") is { } notice && notice != null)
                Object.DestroyImmediate(notice.gameObject);
            SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(isolated, true);
            Object.DestroyImmediate(definition);
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

    private static void Lifecycle(DungeonMinimapView view, DungeonMinimapPresenter presenter, RunDefinitionSO definition)
    {
        var runObject = new GameObject("MinimapRunFixture");
        var run = runObject.AddComponent<RunManager>();
        var stage = runObject.AddComponent<StageManager>();
        var generator = new GameObject("GeneratorFixture").AddComponent<BaekhoDungeonGenerator>();
        var director = generator.gameObject.AddComponent<DungeonEncounterDirector>();
        stage.Configure(generator, director);
        var player = new GameObject("PlayerFixture").AddComponent<PlayerController>();
        run.stageManager = stage;
        run.player = player;
        run.definition = definition;
        Auto(run, "CurrentChapterIndex", 0);
        Auto(run, "CurrentStageIndex", 0);
        var result = Result(ordinaryRooms: 3);
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
        Check(view.IsVisible && view.BuiltAreaCount == result.rooms.Count, "Ready stage did not build its room geometry.");
        Check(view.StageLabel != null && view.StageLabel.gameObject.activeSelf && view.StageLabel.text == run.ProgressLabel,
            "Ready stage did not display the current chapter/stage in the minimap.");
        Check(presenter.Model.Areas[presenter.Model.GetRoomAreaIndex(1)].Visited, "Start room was not discovered.");
        Check(markers.childCount == 2 && !markers.Find("ShopMarker").gameObject.activeSelf
            && !markers.Find("ExitMarker").gameObject.activeSelf, "Undiscovered markers leaked.");
        Check(content.childCount == result.rooms.Count, "Initial build created unexpected map geometry.");

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
        Check(!view.StageLabel.gameObject.activeSelf && string.IsNullOrEmpty(view.StageLabel.text),
            "Disabled presenter retained its stage label.");
        Invoke(presenter, "OnEnable");
        Invoke(presenter, "UpdateMap");
        Check(view.IsVisible && presenter.Model.Areas[presenter.Model.GetRoomAreaIndex(2)].Visited
            && content.childCount == result.rooms.Count && markers.childCount == 2, "Reactivation lost exploration or duplicated UI.");
        Check(view.StageLabel.gameObject.activeSelf && view.StageLabel.text == run.ProgressLabel,
            "Reactivation failed to restore the current stage label.");

        foreach (RunState state in new[] { RunState.Preparing, RunState.Error, RunState.Won, RunState.Lost })
        {
            Auto(run, "State", state);
            Invoke(presenter, "OnRunStateChanged", state);
            Invoke(presenter, "UpdateMap");
            Check(!view.IsVisible, "Minimap remained visible during " + state + ".");
            Check(!view.StageLabel.gameObject.activeSelf && string.IsNullOrEmpty(view.StageLabel.text),
                "Minimap retained stale progress during " + state + ".");
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

        var notice = CheckRewardNotices(presenter, director, result, player, markers);
        var next = Result();
        string previousLabel = view.StageLabel.text;
        Auto(run, "CurrentStageIndex", 1);
        Auto(stage, "Result", next);
        Auto(generator, "CurrentRoot", next.root);
        player.transform.SetPositionAndRotation(next.root.transform.TransformPoint(Vector3.zero), next.root.transform.rotation);
        Invoke(presenter, "OnStageStarted");
        Check(view.IsVisible && !presenter.Model.Areas[presenter.Model.GetRoomAreaIndex(2)].Visited,
            "Replacement stage reused discovery from matching old room IDs.");
        Check(content.childCount == 3 && markers.childCount == 2, "Replacement stage duplicated old geometry or icons.");
        Check(view.StageLabel.text == run.ProgressLabel && view.StageLabel.text != previousLabel,
            "Replacement stage retained its previous chapter/stage label.");
        Check(!notice.gameObject.activeSelf, "Replacement stage left a notice for the abandoned chest.");
        view.Clear();
        Check(view.BuiltAreaCount == 0 && content.childCount == 0 && markers.childCount == 0 && !view.IsVisible,
            "Clearing the view left geometry, markers or visibility behind.");
        Check(!view.StageLabel.gameObject.activeSelf && string.IsNullOrEmpty(view.StageLabel.text),
            "Clearing the view retained its stage label.");
    }

    private static DungeonRewardNotice CheckRewardNotices(DungeonMinimapPresenter presenter,
        DungeonEncounterDirector director, BaekhoDungeonResult result, PlayerController player, Transform markers)
    {
        var first = CreatePlacedChest(director, result.rooms[3]);
        var second = CreatePlacedChest(director, result.rooms[4]);
        Auto(director, "IsReady", true);
        Invoke(presenter, "UpdateMap");
        var notice = Field<DungeonRewardNotice>(presenter, "_rewardNotice");
        Check(notice != null && notice.gameObject.activeInHierarchy && markers.childCount == 4,
            "Two unopened chests did not produce a notice and two map markers.");
        var panel = NamedRect(notice.gameObject, "NoticePlate");
        var icon = NamedRect(notice.gameObject, "RewardChest");
        var label = notice.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        Check(panel.sizeDelta == new Vector2(490f, 57.5f) && panel.anchoredPosition == new Vector2(0f, -106f)
            && icon.sizeDelta == new Vector2(30f, 30f) && label != null && Mathf.Approximately(label.fontSize, 30f),
            "The reward notice did not scale its panel, icon and text by 25% at its existing position.");
        Check(!notice.GetComponent<CanvasGroup>().blocksRaycasts, "The persistent reward notice blocks input.");

        player.transform.position = result.startRoom.transform.position;
        Invoke(presenter, "OnDisable");
        Check(!notice.gameObject.activeSelf, "A disabled minimap left the reward notice on screen.");
        Invoke(presenter, "OnEnable");
        Invoke(presenter, "UpdateMap");
        Check(notice.gameObject.activeInHierarchy, "Unopened chests did not restore the notice in another room.");

        Invoke(first, "BeginUse");
        Invoke(presenter, "UpdateMap");
        Check(notice.gameObject.activeInHierarchy && markers.childCount == 3,
            "Opening one of two chests removed the remaining chest's notice or marker.");
        Invoke(second, "BeginUse");
        Invoke(presenter, "UpdateMap");
        Check(!notice.gameObject.activeSelf && markers.childCount == 2,
            "The notice or marker remained after the last chest was opened.");

        CreatePlacedChest(director, result.rooms[5]);
        SetField(presenter, "_nextChestRefresh", 0f);
        Invoke(presenter, "UpdateMap");
        Check(notice.gameObject.activeInHierarchy, "A newly placed chest did not show the notice again.");
        return notice;
    }

    private static DungeonRewardChest CreatePlacedChest(DungeonEncounterDirector director, BaekhoMapModule room)
    {
        var encounter = room.gameObject.AddComponent<DungeonRoomEncounter>();
        Auto(encounter, "State", DungeonEncounterState.Cleared);
        Auto(encounter, "WasEntered", true);
        Field<List<DungeonEncounterMember>>(encounter, "_members").Add(room.gameObject.AddComponent<DungeonEncounterMember>());
        SetField(encounter, "_confirmedDefeats", 1);
        Field<List<DungeonRoomEncounter>>(director, "_rooms").Add(encounter);

        var chest = new GameObject("CombatRewardChest").AddComponent<DungeonRewardChest>();
        chest.transform.SetParent(room.transform, false);
        var model = new GameObject("PlacedModel").transform;
        model.SetParent(chest.transform, false);
        SetField(chest, "_model", model);
        SetField(chest, "_outlines", Array.Empty<GameObject>());
        Invoke(chest, "CreatePrompt");
        Invoke(chest, "AdvancePresentation", 1f);
        return chest;
    }

    private static BaekhoDungeonResult Result(int ordinaryRooms = 0)
    {
        var result = new BaekhoDungeonResult { success = true, root = new GameObject("GeneratedFixture") };
        result.root.transform.SetPositionAndRotation(new Vector3(125f, 7f, -80f), Quaternion.Euler(0f, 90f, 0f));
        var module = new GameObject("ModuleFixture").AddComponent<BaekhoMapModule>();
        module.localBounds = new Bounds(Vector3.zero, new Vector3(10f, 4f, 10f));
        result.plan = new BaekhoDungeonPlan { startMapId = 1, shopMapId = 2, endMapId = 3 };
        for (int i = 0; i < 3 + ordinaryRooms; i++)
        {
            var role = i switch
            {
                0 => BaekhoRoomRole.Start,
                1 => BaekhoRoomRole.Shop,
                2 => BaekhoRoomRole.End,
                _ => BaekhoRoomRole.Normal
            };
            var position = new Vector3(i * 20f, 0f, 0f);
            result.plan.rooms.Add(new BaekhoPlacedModule { id = i + 1, prefab = module, position = position, role = role });
            var room = new GameObject("RoomFixture_" + i).AddComponent<BaekhoMapModule>();
            room.transform.SetParent(result.root.transform, false);
            room.transform.localPosition = position;
            var identity = room.gameObject.AddComponent<BaekhoGeneratedRoom>();
            identity.instanceId = i + 1;
            identity.role = role;
            var anchor = new GameObject("AnchorFixture").transform;
            anchor.SetParent(room.transform, false);
            var anchors = room.gameObject.AddComponent<StageMapAnchors>();
            anchors.shopPoint = anchor;
            anchors.exitPoint = anchor;
            result.rooms.Add(room);
            if (i == 0) result.startRoom = room;
            else if (i == 1) result.shopRoom = room;
            else if (i == 2) result.endRoom = room;
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
        RectTransform forms = NamedRect(formsPrefab, "Forms"), skills = NamedRect(formsPrefab, "Skills");
        RectTransform vitals = NamedRect(formsPrefab, "Vitals"), chainSkills = NamedRect(formsPrefab, "ChainSkills");
        RectTransform gold = NamedRect(goldPrefab, "Gold");
        Check(panel.anchorMin == Vector2.one && panel.anchorMax == Vector2.one && panel.pivot == Vector2.one,
            "BoundaryZERO minimap must remain fixed to the upper-right corner.");
        Check(Vector2.Distance(panel.anchoredPosition, new Vector2(-60.75f, -54f)) < .01f
            && Vector2.Distance(panel.sizeDelta, new Vector2(245f, 255f)) < .01f
            && Vector3.Distance(panel.localScale, new Vector3(1.65f, 1.65f, 1f)) < .01f,
            "Minimap does not match the QHD guide frame.");
        Check(forms.anchorMin == Vector2.zero && forms.anchorMax == Vector2.zero && forms.pivot == Vector2.zero,
            "The five-element HUD must remain anchored to the lower-left corner.");
        Check(skills.anchorMin == new Vector2(1f, 0f) && skills.anchorMax == new Vector2(1f, 0f)
            && skills.pivot == new Vector2(1f, 0f), "E/R skills must remain anchored to the lower-right corner.");
        Check(vitals.anchorMin == new Vector2(0f, 1f) && vitals.anchorMax == new Vector2(0f, 1f)
            && vitals.pivot == new Vector2(0f, 1f), "Vitals must remain anchored to the upper-left corner.");
        Check(Field<RectTransform>(view, "viewport").GetComponent<UnityEngine.UI.RectMask2D>() != null,
            "Minimap viewport has no clipping mask.");
        var label = view.StageLabel.rectTransform;
        var viewport = Field<RectTransform>(view, "viewport");
        Check(label.parent == panel && !label.IsChildOf(viewport)
            && label.anchorMin == Vector2.zero && label.anchorMax == Vector2.zero && label.pivot == Vector2.zero,
            "The stage label must be fixed to the panel's lower-left corner, outside the scrolling viewport hierarchy.");
        Check(!view.StageLabel.raycastTarget, "The stage label blocks input.");
        var mapHint = panel.Find("MapHint") as RectTransform;
        Check(mapHint != null && forms.Find("MapHint") == null,
            "The map hint must follow the minimap after the forms move to the lower-left corner.");
        var mapScaler = instance.GetComponent<UnityEngine.UI.CanvasScaler>();
        var formScaler = formsPrefab.GetComponent<UnityEngine.UI.CanvasScaler>();
        var goldScaler = goldPrefab.GetComponent<UnityEngine.UI.CanvasScaler>();
        foreach (Vector2 screen in new[] { new Vector2(1280f, 720f), new Vector2(1920f, 1080f), new Vector2(2560f, 1440f), new Vector2(3440f, 1440f) })
        {
            Rect mapRect = ScreenRect(panel, mapScaler, screen), formRect = ScreenRect(forms, formScaler, screen);
            Rect goldRect = ScreenRect(gold, goldScaler, screen), skillRect = ScreenRect(skills, formScaler, screen);
            float mapScale = Scale(mapScaler, screen);
            float formScale = Scale(formScaler, screen);
            CheckRect(mapRect, new Rect(screen.x - 465f * mapScale, screen.y - 474.75f * mapScale,
                404.25f * mapScale, 420.75f * mapScale), "minimap frame", screen);
            CheckRect(ScreenRect(viewport, mapScaler, screen),
                new Rect(screen.x - 458.4f * mapScale, screen.y - 451.65f * mapScale,
                    366.3f * mapScale, 366.3f * mapScale), "minimap content", screen);
            CheckRect(formRect, new Rect(55f * formScale, 46f * formScale, 630f * formScale, 174f * formScale),
                "five-element group", screen);
            CheckRect(skillRect, new Rect(screen.x - 360.3f * formScale, 35.45f * formScale,
                297f * formScale, 206.55f * formScale), "E/R group", screen);
            var housing = formsPrefab.transform.Find("BoundaryZeroHUD/Vitals/Housing") as RectTransform;
            Rect housingRect = ScreenRect(housing, formScaler, screen);
            CheckRect(housingRect, new Rect(52f * formScale, screen.y - 248f * formScale,
                625f * formScale, 202f * formScale), "health housing", screen);
            for (int i = 0; i < 5; i++)
            {
                Rect mount = ScreenRect((RectTransform)forms.Find("Mount" + i), formScaler, screen);
                Rect icon = ScreenRect((RectTransform)forms.Find("Element" + i), formScaler, screen);
                Rect key = ScreenRect((RectTransform)forms.Find("Key" + i), formScaler, screen);
                CheckRect(mount, new Rect((64f + 126f * i) * formScale, 94f * formScale,
                    108f * formScale, 114f * formScale), "element mount " + i, screen);
                CheckRect(icon, new Rect((85f + 126f * i) * formScale, 121f * formScale,
                    66f * formScale, 66f * formScale), "element icon " + i, screen);
                CheckRect(key, new Rect((100f + 126f * i) * formScale, 56.5f * formScale,
                    36f * formScale, 34.5f * formScale), "element key " + i, screen);
            }
            for (int i = 0; i < 2; i++)
            {
                string skill = i == 0 ? "SkillE" : "SkillR";
                Rect mount = ScreenRect((RectTransform)skills.Find(skill + "/Mount"), formScaler, screen);
                Rect icon = ScreenRect((RectTransform)skills.Find(skill + "/IconColorReveal"), formScaler, screen);
                Rect key = ScreenRect((RectTransform)skills.Find(skill + "/Key"), formScaler, screen);
                CheckRect(mount, new Rect(screen.x - (360.3f - 164.7f * i) * formScale, 80f * formScale,
                    132.3f * formScale, 162f * formScale), skill + " mount", screen);
                CheckRect(icon, new Rect(screen.x - (341.4f - 164.7f * i) * formScale, 128.6f * formScale,
                    94.5f * formScale, 94.5f * formScale), skill + " icon", screen);
                CheckRect(key, new Rect(screen.x - (326.55f - 164.7f * i) * formScale, 35.45f * formScale,
                    64.8f * formScale, 37.8f * formScale), skill + " key", screen);
            }
            Rect stageRect = ScreenRect(label, mapScaler, screen), viewportRect = ScreenRect(viewport, mapScaler, screen);
            Check(viewportRect.Contains(stageRect.min) && viewportRect.Contains(stageRect.max)
                && mapRect.Contains(stageRect.min) && mapRect.Contains(stageRect.max),
                "Stage label leaves the scaled minimap interior at " + screen + ".");
            Rect hintRect = ScreenRect(mapHint, mapScaler, screen);
            Check(hintRect.xMin >= mapRect.xMin && hintRect.xMax <= mapRect.xMax
                && hintRect.yMax <= mapRect.yMin, "Map hint does not sit below its frame at " + screen + ".");
            Rect chainRect = ScreenRect(chainSkills, formScaler, screen);
            Check(chainRect.yMin >= formRect.yMax && !chainRect.Overlaps(skillRect)
                && !chainRect.Overlaps(mapRect), "Conditional chain skills overlap the corner HUD at " + screen + ".");
            Check(!mapRect.Overlaps(formRect) && !mapRect.Overlaps(goldRect) && !mapRect.Overlaps(skillRect)
                && !goldRect.Overlaps(skillRect) && !formRect.Overlaps(skillRect)
                && !housingRect.Overlaps(mapRect), "HUD regions overlap at " + screen + ".");
            foreach (Rect region in new[] { mapRect, formRect, goldRect, skillRect, housingRect, chainRect, hintRect })
                Check(region.xMin >= 0f && region.yMin >= 0f && region.xMax <= screen.x && region.yMax <= screen.y,
                    "A HUD region leaves the screen at " + screen + ": " + region + ".");
        }
    }

    private static float Scale(UnityEngine.UI.CanvasScaler scaler, Vector2 screen)
    {
        CheckAsset(scaler != null && scaler.uiScaleMode == UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize,
            "HUD requires Scale With Screen Size.");
        CheckAsset(scaler.referenceResolution == new Vector2(2560f, 1440f)
            && Mathf.Abs(scaler.matchWidthOrHeight - .5f) < .001f, "HUD requires the shared QHD canvas scale.");
        Vector2 reference = scaler.referenceResolution;
        return Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(screen.x / reference.x, 2f),
            Mathf.Log(screen.y / reference.y, 2f), scaler.matchWidthOrHeight));
    }

    private static Rect ScreenRect(RectTransform rect, UnityEngine.UI.CanvasScaler scaler, Vector2 screen)
    {
        return ScreenRect(rect, scaler, screen, out _);
    }

    private static Rect ScreenRect(RectTransform rect, UnityEngine.UI.CanvasScaler scaler, Vector2 screen,
        out Vector2 scale)
    {
        CheckAsset(rect != null, "A HUD layout rect is missing.");
        if (rect == scaler.transform)
        {
            scale = Vector2.one * Scale(scaler, screen);
            return new Rect(Vector2.zero, screen);
        }
        CheckAsset(rect.parent is RectTransform && rect.localRotation == Quaternion.identity,
            "The HUD layout measurement requires an unrotated rect beneath its canvas.");
        Rect parent = ScreenRect((RectTransform)rect.parent, scaler, screen, out var parentScale);
        scale = Vector2.Scale(parentScale, new Vector2(rect.localScale.x, rect.localScale.y));
        Vector2 anchorSpan = rect.anchorMax - rect.anchorMin;
        Vector2 anchor = parent.min + Vector2.Scale(parent.size, rect.anchorMin + Vector2.Scale(anchorSpan, rect.pivot));
        Vector2 size = Vector2.Scale(Vector2.Scale(parent.size, anchorSpan)
            + Vector2.Scale(rect.sizeDelta, parentScale), new Vector2(rect.localScale.x, rect.localScale.y));
        return new Rect(anchor + Vector2.Scale(rect.anchoredPosition, parentScale) - Vector2.Scale(size, rect.pivot), size);
    }

    private static void CheckRect(Rect actual, Rect expected, string name, Vector2 screen)
    {
        Check(Vector2.Distance(actual.position, expected.position) < .05f
            && Vector2.Distance(actual.size, expected.size) < .05f,
            name + " differs from the QHD guide at " + screen + ": " + actual + ".");
    }

    private static RectTransform NamedRect(GameObject root, string name)
    {
        foreach (var rect in root.GetComponentsInChildren<RectTransform>(true)) if (rect.name == name) return rect;
        throw new InvalidOperationException("Missing HUD rect: " + name);
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Hidden).GetValue(target);
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
    private static void Auto(object target, string property, object value) => target.GetType()
        .GetField("<" + property + ">k__BackingField", Hidden).SetValue(target, value);
    private static void Invoke(object target, string method, params object[] args) => target.GetType()
        .GetMethod(method, Hidden).Invoke(target, args);
    private static void Check(bool condition, string message) { _checks++; CheckAsset(condition, message); }
    private static void CheckAsset(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Minimap integration: " + message); }
}
