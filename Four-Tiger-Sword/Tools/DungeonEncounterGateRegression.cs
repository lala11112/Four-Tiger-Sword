// Run in Edit Mode with Unity Pipeline run_script, entry DungeonEncounterGateRegression.Run.
// All fixtures live in a temporary preview scene; no project asset or open scene is changed.
using System;
using System.Collections.Generic;
using LabDoor;
using StudioBaekho;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class DungeonEncounterGateRegression
{
    private sealed class Fixture
    {
        public GameObject root;
        public BaekhoMapModule room;
        public BaekhoMapConnection socket;
        public DungeonEncounterGate gate;
        public CharacterController player;
    }

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run the gate regression in Edit Mode.");
        int checks = 0;
        Action<bool, string> check = (value, message) =>
        {
            if (!value) throw new Exception("Dungeon gate regression: " + message);
            checks++;
        };
        Scene scene = EditorSceneManager.NewPreviewScene();
        BaekhoCorridorStyle style = null;
        try
        {
            GameObject source = MakeDoor(scene);
            foreach (float rootYaw in new[] { 0f, 90f, 180f, 270f })
            foreach (float socketYaw in new[] { 0f, 90f, 180f, 270f })
            {
                Fixture fixture = MakeFixture(scene, new Vector3(130f, 8f, -75f), rootYaw, socketYaw, false);
                fixture.gate.Initialize(fixture.socket, 4f, source);
                var sliding = fixture.gate.DoorRoot.GetComponent<SlidingDoor>();
                BoxCollider blocker = Blocker(fixture.gate);
                var obstacle = fixture.gate.GetComponentInChildren<NavMeshObstacle>(true);
                check(sliding.OpenAmount == 1f && fixture.gate.DoorRoot.activeSelf,
                    "Initialization must open the door under an inactive staging root.");
                check(blocker != null && !blocker.enabled && obstacle != null && !obstacle.enabled,
                    "An open door must not leave an active lock collider or navigation obstacle.");
                check(Approximately(fixture.gate.transform.position, fixture.socket.transform.position)
                    && Quaternion.Angle(fixture.gate.transform.rotation, fixture.socket.transform.rotation) < 0.01f,
                    "The gate must use the socket world transform for every room and socket rotation.");

                Vector3 before = Point(fixture, 0f, 1.5f, -1f);
                check(!fixture.gate.HasEntered(fixture.player, before, Point(fixture, 0f, 1.5f, 0.87f)),
                    "The player must fully clear the closing slab before locking.");
                check(fixture.gate.HasEntered(fixture.player, before, Point(fixture, 0f, 1.5f, 0.90f)),
                    "A translated and rotated gate must detect inward passage at its safe plane.");
                check(fixture.gate.HasEntered(fixture.player, Point(fixture, 0f, 1.5f, -3f),
                    Point(fixture, 0f, 1.5f, 12f)), "A dash crossing the entire entry area must be detected.");
                check(!fixture.gate.HasEntered(fixture.player, Point(fixture, 0f, 1.5f, 2f), before),
                    "Outward passage must not start an encounter.");
                check(fixture.gate.HasEntered(fixture.player, Point(fixture, 1.71f, 1.5f, -1f),
                    Point(fixture, 1.71f, 1.5f, 1.5f)),
                    "A controller hugging a jamb within its skin must still start the encounter.");
                check(!fixture.gate.HasEntered(fixture.player, Point(fixture, 2.5f, 1.5f, -1f),
                    Point(fixture, 2.5f, 1.5f, 1.5f)), "Movement entirely beside the aperture must be ignored.");
                check(!fixture.gate.HasEntered(fixture.player, Point(fixture, 0f, 5f, -1f),
                    Point(fixture, 0f, 5f, 1.5f)), "Passing above the doorway must be ignored.");
                check(!fixture.gate.HasEntered(fixture.player, Point(fixture, 0f, -2f, -1f),
                    Point(fixture, 0f, -2f, 1.5f)), "Passing below the doorway must be ignored.");
                check(fixture.gate.ContainsEntryInterior(fixture.player, Point(fixture, 0f, 1.5f, 1f))
                    && !fixture.gate.ContainsEntryInterior(fixture.player, Point(fixture, 0f, 1.5f, 0.2f))
                    && !fixture.gate.ContainsEntryInterior(fixture.player, Point(fixture, 0f, 1.5f, 8f)),
                    "Startup entry containment must be safe and bounded rather than an infinite half-space.");

                fixture.gate.Lock();
                check(fixture.gate.IsLocked && blocker.enabled && obstacle.enabled && obstacle.carving,
                    "Lock must enable the physical and navigation barriers immediately.");
                check(sliding.OpenAmount == 0f, "Edit Mode lock must fully close the door.");
                fixture.gate.Open();
                check(!fixture.gate.IsLocked && !blocker.enabled && !obstacle.enabled && sliding.OpenAmount == 1f,
                    "Opening must release both barriers and open the door.");
                fixture.gate.Lock();
                fixture.gate.Dispose();
                check(!blocker.enabled && !obstacle.enabled && sliding.OpenAmount == 1f
                    && !fixture.gate.HasEntered(fixture.player, before, Point(fixture, 0f, 1.5f, 2f)),
                    "Disposal must unblock the entrance and stop future entry checks.");
                UnityEngine.Object.DestroyImmediate(fixture.root);
                UnityEngine.Object.DestroyImmediate(fixture.player.gameObject);
            }

            var realPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/LabDoor4x3/Prefabs/35_industrial_sliding_door.prefab");
            check(realPrefab != null, "The configured industrial sliding door asset must exist.");
            Fixture real = MakeFixture(scene, new Vector3(-30f, 3f, 55f), 90f, 270f, true);
            real.socket.height = 1.5f;
            real.gate.Initialize(real.socket, 2f, realPrefab);
            check(Approximately(real.gate.DoorRoot.transform.localScale, new Vector3(0.5f, 0.5f, 1f)),
                "New industrial doors must fit opening width and height while preserving depth.");
            check(real.gate.DoorRoot.GetComponent<SlidingDoor>().OpenAmount == 1f,
                "The real industrial prefab must initialize open.");
            check(real.gate.HasEntered(real.player, Point(real, 0f, 0.75f, -1f), Point(real, 0f, 0.75f, 1f)),
                "The real prefab's rotated/scaled box colliders must produce the correct entry depth.");
            real.gate.Lock();
            real.gate.gameObject.SetActive(false);
            // Ordinary MonoBehaviours do not receive play-mode lifecycle callbacks in an
            // Edit Mode preview scene. Invoke the same callback explicitly for this harness.
            typeof(DungeonEncounterGate).GetMethod("OnDisable",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(real.gate, null);
            check(!Blocker(real.gate).enabled && !real.gate.GetComponentInChildren<NavMeshObstacle>(true).enabled,
                "Disabling a gate must release the lock.");

            Fixture reused = MakeFixture(scene, new Vector3(65f, -4f, 80f), 270f, 90f, true);
            GameObject authored = UnityEngine.Object.Instantiate(realPrefab, reused.root.transform, false);
            authored.transform.SetPositionAndRotation(reused.socket.transform.TransformPoint(new Vector3(0.07f, 0f, -1.08f)),
                reused.socket.transform.rotation * Quaternion.Euler(0f, 180f, 0f));
            authored.SetActive(false);
            Vector3 authoredPosition = authored.transform.localPosition;
            Quaternion authoredRotation = authored.transform.localRotation;
            Vector3 authoredScale = authored.transform.localScale;
            reused.socket.runtimeDoor = authored;
            int doorsBefore = reused.root.GetComponentsInChildren<SlidingDoor>(true).Length;
            reused.gate.Initialize(reused.socket, 4f, null);
            check(reused.gate.DoorRoot == authored && authored.activeSelf
                && reused.root.GetComponentsInChildren<SlidingDoor>(true).Length == doorsBefore,
                "An explicitly assigned inactive door must be reactivated and reused without duplication.");
            check(Approximately(authoredPosition, authored.transform.localPosition)
                && Quaternion.Angle(authoredRotation, authored.transform.localRotation) < 0.01f
                && Approximately(authoredScale, authored.transform.localScale),
                "An authored recessed door must preserve its position, rotation, and scale.");
            check(!reused.gate.HasEntered(reused.player, Point(reused, 0f, 1.5f, -1f), Point(reused, 0f, 1.5f, 1.9f))
                && reused.gate.HasEntered(reused.player, Point(reused, 0f, 1.5f, -1f), Point(reused, 0f, 1.5f, 2.1f)),
                "The safe plane must follow the actual recessed leaves rather than the socket plane.");

            style = ScriptableObject.CreateInstance<BaekhoCorridorStyle>();
            GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(cap, scene);
            style.cap.prefab = cap;
            style.cap.localBounds = new Bounds(Vector3.zero, Vector3.one);
            BaekhoDungeonGeometry.FinishDoor(reused.room, 0, 4f, style, reused.root.transform);
            check(authored.activeSelf, "A connected exit must retain its authored runtime door.");
            BaekhoDungeonGeometry.FinishDoor(reused.room, 0, 0f, style, reused.root.transform);
            check(!authored.activeSelf && reused.root.transform.Find("Closed_" + reused.socket.id) != null,
                "An unused exit must hide its authored runtime door and receive its permanent closure.");
            int alignedMapCases = CheckWallAlignment(scene, realPrefab, source, check);
            return "PASS: " + checks + " dungeon gate checks (16 translated/cardinal orientations; safe entry, dash, jamb, "
                + "real prefab fitting, authored reuse, lock lifecycle, unused-door closure, and "
                + alignedMapCases + " selected-map wall alignments).";
        }
        finally
        {
            if (style != null) UnityEngine.Object.DestroyImmediate(style);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static int CheckWallAlignment(Scene scene, GameObject realPrefab, GameObject fixtureDoor,
        Action<bool, string> check)
    {
        // Large wall-center Y and lateral offsets must never move the door away from
        // the walkable floor or the aperture center. Only the wall's normal plane matters.
        Fixture explicitInset = MakeFixture(scene, new Vector3(27f, 14f, -60f), 90f, 270f, false);
        explicitInset.socket.closedWall.localCenter = explicitInset.room.transform.InverseTransformPoint(
            explicitInset.socket.transform.TransformPoint(new Vector3(1.75f, 9f, -1.25f)));
        explicitInset.socket.closedWall.size = new Vector3(4f, 18f, 2.5f);
        explicitInset.gate.Initialize(explicitInset.socket, 4f, fixtureDoor);
        check(Approximately(explicitInset.gate.DoorRoot.transform.localPosition, new Vector3(0f, 0f, -1.25f)),
            "Wall alignment must preserve aperture X and floor Y while moving only to the measured wall plane.");
        check(!explicitInset.gate.HasEntered(explicitInset.player, Point(explicitInset, 0f, 1.5f, -1f),
                Point(explicitInset, 0f, 1.5f, 2.10f))
            && explicitInset.gate.HasEntered(explicitInset.player, Point(explicitInset, 0f, 1.5f, -1f),
                Point(explicitInset, 0f, 1.5f, 2.16f)),
            "Entry must wait for the player's capsule to clear the inset closing slab.");
        explicitInset.gate.Lock();
        check(Mathf.Abs(Blocker(explicitInset.gate).center.z + 1.25f) < 0.001f,
            "The immediate lock collider must follow the inset leaves rather than the corridor boundary.");
        UnityEngine.Object.DestroyImmediate(explicitInset.root);
        UnityEngine.Object.DestroyImmediate(explicitInset.player.gameObject);

        var settings = AssetDatabase.LoadAssetAtPath<BaekhoDungeonSettings>(
            "Assets/_Game/ProceduralDungeon/DungeonSettings.asset");
        check(settings != null && settings.maps != null && settings.maps.Length > 0,
            "Selected dungeon settings must provide real room prefabs for wall alignment checks.");
        var modules = new HashSet<BaekhoMapModule>();
        foreach (BaekhoMapModule module in settings.maps) if (module != null) modules.Add(module);
        foreach (BaekhoMapModule module in new[] { settings.startRoom, settings.endRoom, settings.bossRoom })
            if (module != null) modules.Add(module);
        var playerObject = new GameObject("WallAlignmentRegressionCapsule");
        SceneManager.MoveGameObjectToScene(playerObject, scene);
        var player = playerObject.AddComponent<CharacterController>();
        player.height = 1.8f;
        player.radius = 0.35f;
        player.skinWidth = 0.08f;
        int alignedCases = 0;
        int preservedCases = 0;
        foreach (BaekhoMapModule prefab in modules)
        foreach (float roomYaw in new[] { 0f, 90f, 180f, 270f })
        {
            var host = new GameObject("WallAlignmentRegressionRoot");
            SceneManager.MoveGameObjectToScene(host, scene);
            // Keep imported NavMeshSurface components and their data inactive throughout
            // the preview test, as they are during the real dungeon's staging build.
            host.SetActive(false);
            host.transform.SetPositionAndRotation(new Vector3(87f, 11f, -63f), Quaternion.Euler(0f, 37f, 0f));
            BaekhoMapModule room = UnityEngine.Object.Instantiate(prefab, host.transform, false);
            room.transform.localPosition = new Vector3(13f, 4f, -21f);
            room.transform.localRotation = Quaternion.Euler(0f, roomYaw, 0f);
            room.transform.localScale = Vector3.one;
            foreach (BaekhoMapConnection socket in room.connections)
            {
                string context = prefab.name + "/" + socket.id + " at room yaw " + roomYaw;
                var wrapper = new GameObject("WallAlignedGate_" + socket.id);
                wrapper.transform.SetParent(room.transform, false);
                var gate = wrapper.AddComponent<DungeonEncounterGate>();
                if (socket.runtimeDoor != null)
                {
                    GameObject existing = socket.runtimeDoor;
                    Vector3 position = existing.transform.position;
                    Quaternion rotation = existing.transform.rotation;
                    Vector3 scale = existing.transform.localScale;
                    gate.Initialize(socket, Mathf.Min(settings.corridorWidth, socket.width), realPrefab);
                    check(gate.DoorRoot == existing && Approximately(existing.transform.position, position)
                        && Quaternion.Angle(existing.transform.rotation, rotation) < 0.01f
                        && Approximately(existing.transform.localScale, scale),
                        "Wall correction must preserve the authored runtime door: " + context);
                    preservedCases++;
                    continue;
                }
                var wall = socket.closedWall;
                if (wall == null || wall.size.x <= 0f || wall.size.y <= 0f || wall.size.z <= 0f) continue;
                Vector3 wallCenter = room.transform.TransformPoint(wall.localCenter);
                float wallDepth = Vector3.Dot(wallCenter - socket.transform.position, socket.transform.forward);
                if (wall.template != null)
                {
                    Vector3 actualWallCenter = wall.template.transform.TransformPoint(wall.templateLocalBounds.center);
                    check(Mathf.Abs(Vector3.Dot(actualWallCenter - socket.transform.position,
                        socket.transform.forward) - wallDepth) < 0.001f,
                        "Prepared wall metadata must agree with the neighboring wall geometry: " + context);
                }
                gate.Initialize(socket, Mathf.Min(settings.corridorWidth, socket.width), realPrefab);
                Vector3 displacement = gate.DoorRoot.transform.position - socket.transform.position;
                check(Mathf.Abs(Vector3.Dot(displacement, socket.transform.forward) - wallDepth) < 0.001f
                    && Mathf.Abs(Vector3.Dot(displacement, socket.transform.right)) < 0.001f
                    && Mathf.Abs(displacement.y) < 0.001f,
                    "The generated door must lie in the actual wall while retaining the socket floor and aperture: " + context);
                check(Quaternion.Angle(gate.DoorRoot.transform.rotation, socket.transform.rotation) < 0.01f,
                    "Wall correction must preserve doorway orientation: " + context);
                gate.Lock();
                Bounds leaves = ClosedLeafBounds(gate);
                BoxCollider blocker = Blocker(gate);
                var obstacle = gate.GetComponentInChildren<NavMeshObstacle>(true);
                check(blocker.enabled && obstacle.enabled && obstacle.carving
                    && Mathf.Abs(blocker.center.z - leaves.center.z) < 0.001f
                    && Mathf.Abs(blocker.size.z - Mathf.Max(0.1f, leaves.size.z)) < 0.001f
                    && Approximately(obstacle.center, blocker.center)
                    && Approximately(obstacle.size, blocker.size),
                    "Both navigation and collision locks must follow the actual inset closing leaves: " + context);
                gate.Open(true);
                var fixture = new Fixture { gate = gate, socket = socket, player = player };
                float safeDepth = -leaves.min.z + player.radius + player.skinWidth + 0.05f;
                Vector3 before = Point(fixture, 0f, socket.height * 0.5f, -1f);
                check(!gate.HasEntered(player, before, Point(fixture, 0f, socket.height * 0.5f, safeDepth - 0.02f))
                    && gate.HasEntered(player, before, Point(fixture, 0f, socket.height * 0.5f, safeDepth + 0.02f)),
                    "The safe entry plane must move with the actual selected room's inset door: " + context);
                if (wallDepth < -0.1f)
                    check(!gate.HasEntered(player, before, Point(fixture, 0f, socket.height * 0.5f, 0.90f)),
                        "Crossing the old corridor-boundary threshold must not close a recessed door early: " + context);
                check(gate.HasEntered(player, before, Point(fixture, 0f, socket.height * 0.5f, safeDepth + 12f)),
                    "Dash entry must still detect the inset plane: " + context);
                alignedCases++;
            }
            UnityEngine.Object.DestroyImmediate(host);
        }
        check(alignedCases > 0 && alignedCases % 4 == 0,
            "The current selected maps must exercise measured wall alignment in all four room rotations.");
        if (preservedCases > 0)
            check(preservedCases % 4 == 0, "Authored selected-map doors must be preserved in all four room rotations.");
        UnityEngine.Object.DestroyImmediate(playerObject);
        return alignedCases;
    }

    private static Bounds ClosedLeafBounds(DungeonEncounterGate gate)
    {
        var door = gate.DoorRoot.GetComponentInChildren<SlidingDoor>(true);
        Bounds bounds = default;
        bool found = false;
        foreach (Transform leaf in new[] { door.LeftLeaf, door.RightLeaf })
        foreach (BoxCollider box in leaf.GetComponentsInChildren<BoxCollider>(true))
        {
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = box.center + Vector3.Scale(box.size * 0.5f,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 point = gate.transform.InverseTransformPoint(box.transform.TransformPoint(corner));
                if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                else bounds.Encapsulate(point);
            }
        }
        if (!found) throw new Exception("The real sliding door must expose leaf box colliders for alignment verification.");
        return bounds;
    }

    private static Fixture MakeFixture(Scene scene, Vector3 position, float rootYaw, float socketYaw, bool active)
    {
        var root = new GameObject("GateRegressionRoom");
        SceneManager.MoveGameObjectToScene(root, scene);
        root.SetActive(false);
        root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, rootYaw, 0f));
        var room = root.AddComponent<BaekhoMapModule>();
        room.localBounds = new Bounds(Vector3.up * 2f, new Vector3(16f, 4f, 16f));
        var entrance = new GameObject("Socket");
        entrance.transform.SetParent(root.transform, false);
        entrance.transform.localPosition = new Vector3(3f, 1.5f, -4f);
        entrance.transform.localRotation = Quaternion.Euler(0f, socketYaw, 0f);
        var socket = entrance.AddComponent<BaekhoMapConnection>();
        socket.id = "RegressionEntry";
        socket.width = 4f;
        socket.height = 3f;
        room.connections = new[] { socket };
        var wrapper = new GameObject("Gate");
        wrapper.transform.SetParent(root.transform, false);
        var gate = wrapper.AddComponent<DungeonEncounterGate>();
        var playerObject = new GameObject("GateRegressionCapsule");
        SceneManager.MoveGameObjectToScene(playerObject, scene);
        var player = playerObject.AddComponent<CharacterController>();
        player.height = 1.8f;
        player.radius = 0.35f;
        player.skinWidth = 0.08f;
        root.SetActive(active);
        return new Fixture { root = root, room = room, socket = socket, gate = gate, player = player };
    }

    private static GameObject MakeDoor(Scene scene)
    {
        var source = new GameObject("GateRegressionDoorPrefab");
        SceneManager.MoveGameObjectToScene(source, scene);
        source.SetActive(false);
        var left = new GameObject("Left", typeof(BoxCollider));
        var right = new GameObject("Right", typeof(BoxCollider));
        left.transform.SetParent(source.transform, false);
        right.transform.SetParent(source.transform, false);
        left.transform.localPosition = new Vector3(-1f, 1.5f, 0f);
        right.transform.localPosition = new Vector3(1f, 1.5f, 0f);
        left.GetComponent<BoxCollider>().size = new Vector3(2f, 3f, 0.8f);
        right.GetComponent<BoxCollider>().size = new Vector3(2f, 3f, 0.8f);
        source.AddComponent<SlidingDoor>().Setup(left.transform, right.transform);
        return source;
    }

    private static Vector3 Point(Fixture fixture, float x, float y, float inwardDepth)
        => fixture.gate.transform.TransformPoint(new Vector3(x, y, -inwardDepth));

    private static BoxCollider Blocker(DungeonEncounterGate gate)
    {
        foreach (BoxCollider candidate in gate.GetComponentsInChildren<BoxCollider>(true))
            if (candidate.gameObject.name.StartsWith("EncounterLock_", StringComparison.Ordinal)) return candidate;
        return null;
    }

    private static bool Approximately(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 0.000001f;
}
