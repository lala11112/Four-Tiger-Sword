using System;
using StudioBaekho;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Isolated geometry/exploration checks; no scene assets or combat fixtures are required.</summary>
public static class DungeonMinimapModelRegression
{
    private static int _checks;

    [MenuItem("Tools/Studio Baekho/Dungeon/Run Minimap Model Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run minimap model regression in Edit Mode.");
        _checks = 0;
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        try
        {
            root = new GameObject("Minimap model regression") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(root, scene);
            CoordinatesAndRotations(root.transform);
            StubsUseConnectedSockets(root.transform);
            EmptyRoomsAndReset(root.transform);
            CornersAndCorridors(root.transform);
            BoundariesAndHysteresis(root.transform);
            SweptMovementAndTeleports(root.transform);
            return $"Minimap model: {_checks} checks passed (rotated bounds, corridor/stub geometry, "
                + "empty/start/shop rooms, reset/revisit, corners, boundary hysteresis, dash and teleport history).";
        }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void CoordinatesAndRotations(Transform root)
    {
        var module = Module(root, "Offset rectangular room", new Vector3(2f, 50f, -1f), new Vector3(8f, 6f, 12f));
        for (int turn = 0; turn < 4; ++turn)
        {
            var plan = new BaekhoDungeonPlan { startMapId = 7 };
            var room = Place(module, 7, new Vector3(10f, -80f, 20f), turn);
            plan.rooms.Add(room);
            var model = new DungeonMinimapModel();
            model.Reset(plan);
            var area = model.Areas[model.GetRoomAreaIndex(7)];
            Vector3 expectedCenter = room.position + room.Rotation * module.localBounds.center;
            Near(area.Center, new Vector2(expectedCenter.x, expectedCenter.z), "A rotated pivot shifted the room incorrectly.");
            Near(area.Size, (turn & 1) == 0 ? new Vector2(8f, 12f) : new Vector2(12f, 8f),
                "An odd quarter-turn did not swap XZ extents.");
            model.UpdatePosition(area.Center);
            Check(model.CurrentAreaIndex == 0 && area.Visited, "Room membership depended on geometry height or combat.");
        }

        var segments = new[]
        {
            new BaekhoCorridorSegment(new Vector3(0f, 99f, 0f), new Vector3(0f, 99f, 10f)),
            new BaekhoCorridorSegment(new Vector3(0f, -99f, 0f), new Vector3(10f, -99f, 0f)),
            new BaekhoCorridorSegment(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, -10f)),
            new BaekhoCorridorSegment(new Vector3(0f, 0f, 0f), new Vector3(-10f, 0f, 0f))
        };
        float[] angles = { 0f, -90f, -180f, 90f };
        for (int i = 0; i < segments.Length; ++i)
        {
            var plan = new BaekhoDungeonPlan();
            var link = new BaekhoDungeonLink { width = 2f };
            link.segments.Add(segments[i]);
            plan.links.Add(link);
            var model = new DungeonMinimapModel();
            model.Reset(plan);
            var area = model.Areas[0];
            Near(area.Size, new Vector2(2f, 10f), "Corridor width/length were swapped.");
            Near(Mathf.DeltaAngle(area.RotationDegrees, angles[i]), 0f, "Corridor UGUI rotation pointed in the wrong direction.");
            var point = new Vector2(segments[i].end.x, segments[i].end.z) * 0.9f;
            model.UpdatePosition(point);
            Check(model.CurrentAreaIndex == 0, "Rotated corridor membership used an axis-aligned rectangle.");
            var across = new Vector2(segments[i].end.z, -segments[i].end.x).normalized * 1.5f;
            model.UpdatePosition(area.Center + across, false);
            Check(model.CurrentAreaIndex == -1, "Corridor accepted a point beyond its width and hysteresis.");
        }
    }

    private static void StubsUseConnectedSockets(Transform root)
    {
        var module = Module(root, "Socket room", Vector3.zero, new Vector3(8f, 4f, 8f));
        module.connections = new[]
        {
            Socket(module.transform, "connected", new Vector3(0f, 0f, 4f), Vector3.forward, 4f),
            Socket(module.transform, "unused", new Vector3(4f, 0f, 0f), Vector3.right, 4f)
        };
        var plan = new BaekhoDungeonPlan { startMapId = 2 };
        plan.rooms.Add(Place(module, 2, new Vector3(10f, 0f, 20f), 1));
        var link = new BaekhoDungeonLink { fromMapId = 2, fromSocketIndex = 0, toMapId = 999, width = 3f };
        plan.links.Add(link);
        // A repeated connection must not draw the same doorway cue twice.
        plan.links.Add(link);
        var model = new DungeonMinimapModel();
        model.Reset(plan);
        Check(model.Stubs.Count == 1, "Unused or duplicate main-room sockets generated stubs.");
        var stub = model.Stubs[0];
        Check(stub.OwnerAreaIndex == model.GetRoomAreaIndex(2), "A doorway stub has the wrong reveal owner.");
        Near(stub.Center, new Vector2(15.5f, 20f), "A rotated socket stub was not placed outside its room.");
        Near(stub.Size, new Vector2(3f, 3f), "The stub did not use the corridor width and three-metre length.");
        Near(stub.RotationDegrees, -90f, "A stub did not inherit its socket's outward direction.");
        Check(model.Areas[stub.OwnerAreaIndex].Visited, "The start room did not reveal its owned doorway cue.");
    }

    private static void EmptyRoomsAndReset(Transform root)
    {
        var module = Module(root, "No enemies or markers", Vector3.zero, new Vector3(4f, 2f, 4f));
        module.encounterBudget = 0;
        var plan = new BaekhoDungeonPlan { startMapId = 0, shopMapId = 1, endMapId = 2 };
        plan.rooms.Add(Place(module, 0, Vector3.zero, role: BaekhoRoomRole.Start));
        plan.rooms.Add(Place(module, 1, new Vector3(10f, 0f, 0f), role: BaekhoRoomRole.Shop));
        plan.rooms.Add(Place(module, 2, new Vector3(20f, 0f, 0f), role: BaekhoRoomRole.End));
        var model = new DungeonMinimapModel();
        model.Reset(plan);
        Check(model.Areas[0].Visited && !model.Areas[1].Visited && !model.Areas[2].Visited,
            "Reset revealed more than the start room.");
        Check(model.CurrentAreaIndex == -1 && model.GetRoomAreaIndex(999) == -1,
            "Reset fabricated a current room or a missing-room lookup.");
        model.UpdatePosition(new Vector2(10f, 0f), false);
        Check(model.Areas[1].Visited && !model.Areas[2].Visited, "An empty shop required an encounter to be visited.");
        model.UpdatePosition(new Vector2(20f, 0f), false);
        Check(model.Areas[2].Visited, "An empty exit room required a clear event to be visited.");
        model.UpdatePosition(Vector2.zero, false);
        int revision = model.Revision;
        Check(!model.UpdatePosition(Vector2.zero) && model.Revision == revision,
            "An unchanged/revisited area continuously invalidated the view.");
        Check(model.Areas[1].Visited && model.Areas[2].Visited, "Leaving a room erased exploration.");
        model.Reset(plan);
        Check(model.Revision > revision && !model.Areas[1].Visited && !model.Areas[2].Visited,
            "Stage reset retained visits when room IDs were reused.");
        model.Reset(null);
        Check(model.Areas.Count == 0 && model.Stubs.Count == 0 && model.CurrentAreaIndex == -1,
            "Clearing the model retained geometry or a current room.");
    }

    private static void CornersAndCorridors(Transform root)
    {
        var module = Module(root, "Corner", Vector3.zero, new Vector3(4f, 4f, 6f));
        module.isCornerRoom = true;
        var plan = new BaekhoDungeonPlan();
        var link = new BaekhoDungeonLink { width = 2f };
        link.segments.Add(new BaekhoCorridorSegment(new Vector3(0f, 0f, -10f), new Vector3(0f, 0f, -2f)));
        link.corners.Add(Place(module, 10, Vector3.zero, 1));
        link.segments.Add(new BaekhoCorridorSegment(new Vector3(3f, 0f, 0f), new Vector3(10f, 0f, 0f)));
        plan.links.Add(link);
        var model = new DungeonMinimapModel();
        model.Reset(plan);
        int corner = model.GetRoomAreaIndex(10);
        Check(model.Areas.Count == 3 && corner >= 0 && model.Areas[corner].Kind == DungeonMinimapAreaKind.Corner,
            "A link-only corner was omitted from the map.");
        Near(model.Areas[corner].Size, new Vector2(6f, 4f), "A corner room's rotation was lost.");
        model.UpdatePosition(new Vector2(0f, -8f));
        model.UpdatePosition(Vector2.zero);
        model.UpdatePosition(new Vector2(8f, 0f));
        foreach (var area in model.Areas) Check(area.Visited, "Traversing a bent connection missed a segment or corner.");
    }

    private static void BoundariesAndHysteresis(Transform root)
    {
        var module = Module(root, "Boundary room", Vector3.zero, new Vector3(4f, 2f, 4f));
        var plan = new BaekhoDungeonPlan();
        plan.rooms.Add(Place(module, 0, Vector3.zero));
        plan.rooms.Add(Place(module, 1, new Vector3(3.8f, 0f, 0f)));
        var model = new DungeonMinimapModel();
        model.Reset(plan);
        model.UpdatePosition(Vector2.zero);
        model.UpdatePosition(new Vector2(1.9f, 0f));
        Check(model.CurrentAreaIndex == 0, "Overlapping doorway bounds displaced the previous current area.");
        model.UpdatePosition(new Vector2(2.34f, 0f));
        Check(model.CurrentAreaIndex == 0, "The 0.35m exit margin did not retain the previous area.");
        model.UpdatePosition(new Vector2(2.36f, 0f));
        Check(model.CurrentAreaIndex == 1, "The current area persisted beyond its hysteresis margin.");
        model.UpdatePosition(new Vector2(1.9f, 0f));
        Check(model.CurrentAreaIndex == 1, "Returning into overlapping bounds flickered the current area.");
        model.UpdatePosition(new Vector2(100f, 100f), false);
        Check(model.CurrentAreaIndex == -1, "A player outside all geometry retained a stale current area.");
    }

    private static void SweptMovementAndTeleports(Transform root)
    {
        var module = Module(root, "Short dash room", Vector3.zero, new Vector3(0.2f, 2f, 2f));
        var plan = new BaekhoDungeonPlan();
        plan.rooms.Add(Place(module, 4, Vector3.zero));
        var link = new BaekhoDungeonLink { width = 2f };
        link.segments.Add(new BaekhoCorridorSegment(new Vector3(2f, 0f, -0.1f), new Vector3(2f, 0f, 0.1f)));
        plan.links.Add(link);
        var model = new DungeonMinimapModel();
        model.Reset(plan);
        model.UpdatePosition(new Vector2(-5f, 0f));
        int revision = model.Revision;
        Check(model.UpdatePosition(new Vector2(5f, 0f)) && model.Revision > revision,
            "A dash across short regions did not revise exploration.");
        foreach (var area in model.Areas) Check(area.Visited, "Swept motion skipped a region between both sampled positions.");

        model.Reset(plan);
        model.UpdatePosition(new Vector2(-5f, 0f));
        model.ResetMotionHistory();
        model.UpdatePosition(new Vector2(5f, 0f));
        foreach (var area in model.Areas) Check(!area.Visited, "Resetting teleport/reenable history revealed the intervening path.");
        model.Reset(plan);
        model.UpdatePosition(new Vector2(-5f, 0f));
        model.UpdatePosition(new Vector2(5f, 0f), false);
        foreach (var area in model.Areas) Check(!area.Visited, "Untracked teleport motion revealed intervening geometry.");
        model.UpdatePosition(new Vector2(-5f, 0f));
        foreach (var area in model.Areas) Check(area.Visited, "Movement after a teleport did not start sweeping from its destination.");
        model.Reset(plan);
        model.UpdatePosition(Vector2.zero, false);
        Check(model.Areas[0].Visited, "A teleport destination was not visited.");
        revision = model.Revision;
        Check(!model.UpdatePosition(new Vector2(float.NaN, 0f)) && model.Revision == revision,
            "An invalid position corrupted exploration or its revision.");
    }

    private static BaekhoMapModule Module(Transform parent, string name, Vector3 center, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var module = go.AddComponent<BaekhoMapModule>();
        module.localBounds = new Bounds(center, size);
        return module;
    }

    private static BaekhoMapConnection Socket(Transform parent, string id, Vector3 position, Vector3 forward, float width)
    {
        var go = new GameObject(id);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = Quaternion.LookRotation(forward, Vector3.up);
        var socket = go.AddComponent<BaekhoMapConnection>();
        socket.id = id;
        socket.width = width;
        socket.height = 3f;
        return socket;
    }

    private static BaekhoPlacedModule Place(BaekhoMapModule module, int id, Vector3 position, int turn = 0,
        BaekhoRoomRole role = BaekhoRoomRole.Normal)
        => new() { id = id, prefab = module, position = position, quarterTurns = turn, role = role };

    private static void Near(Vector2 actual, Vector2 expected, string message)
        => Check((actual - expected).sqrMagnitude < 0.00001f, message + $" Actual {actual}, expected {expected}.");

    private static void Near(float actual, float expected, string message)
        => Check(Mathf.Abs(actual - expected) < 0.001f, message + $" Actual {actual}, expected {expected}.");

    private static void Check(bool condition, string message)
    {
        ++_checks;
        if (!condition) throw new InvalidOperationException("Minimap model: " + message);
    }
}
