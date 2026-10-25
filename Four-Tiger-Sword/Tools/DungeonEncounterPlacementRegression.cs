// Unity Pipeline run_script entry: DungeonEncounterPlacementRegression.Run (Edit Mode).
// Uses an additive scene so the planner's Physics queries share the default physics world.
using System;
using System.Collections.Generic;
using System.Text;
using StudioBaekho;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class DungeonEncounterPlacementRegression
{
    private const int Seed = 74129;
    private static int _checks;

    public static string Run()
    {
        Check(!Application.isPlaying, "Placement regression requires Edit Mode.");
        _checks = 0;
        var settings = AssetDatabase.LoadAssetAtPath<BaekhoDungeonSettings>(
            "Assets/_Game/ProceduralDungeon/DungeonSettings.asset");
        Check(settings != null && settings.maps != null && settings.maps.Length > 0, "DungeonSettings has no ordinary maps.");
        var catalog = FindCatalog();
        var costs = catalog.GetValidatedCosts();
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var instances = new List<GameObject>();
        var ownedData = new List<NavMeshData>();
        var summary = new StringBuilder();
        int reused = 0, built = 0, total = 0;
        try
        {
            SceneManager.SetActiveScene(scene);
            for (int mapIndex = 0; mapIndex < settings.maps.Length; mapIndex++)
            {
                var source = settings.maps[mapIndex];
                Check(source != null, "Ordinary map " + mapIndex + " is missing.");
                var room = UnityEngine.Object.Instantiate(source);
                instances.Add(room.gameObject);
                SceneManager.MoveGameObjectToScene(room.gameObject, scene);
                room.transform.SetPositionAndRotation(new Vector3(10000f + mapIndex * 120f, 50f, 10000f),
                    Quaternion.Euler(0f, (mapIndex % 4) * 90f, 0f));
                room.transform.localScale = Vector3.one;
                var authoredSurface = source.GetComponent<NavMeshSurface>();
                bool hasBake = authoredSurface != null && authoredSurface.navMeshData != null;
                NavMeshData authoredData = hasBake ? authoredSurface.navMeshData : null;
                Check(DungeonEncounterPlacement.TryPrepareNavigation(room, out var owned, out var navigationError),
                    source.name + ": " + navigationError);
                if (owned != null) ownedData.Add(owned);
                var surface = room.GetComponent<NavMeshSurface>();
                Check(surface != null && surface.navMeshData != null, source.name + ": missing prepared navigation.");
                if (hasBake)
                {
                    Check(owned == null && surface.navMeshData == authoredData, source.name + ": baked asset was replaced.");
                    reused++;
                }
                else
                {
                    Check(owned != null && !EditorUtility.IsPersistent(owned), source.name + ": missing runtime data ownership.");
                    built++;
                }
                var preparedData = surface.navMeshData;
                Check(DungeonEncounterPlacement.TryPrepareNavigation(room, out var secondOwned, out navigationError),
                    source.name + ": second prepare failed: " + navigationError);
                if (secondOwned != null) ownedData.Add(secondOwned);
                Check(secondOwned == null && surface.navMeshData == preparedData, source.name + ": repeated preparation rebuilt navigation.");

                var composition = BaekhoEncounterPlanner.Plan(room.encounterBudget, costs, Seed + mapIndex);
                var prefabs = Expand(catalog, composition.Counts);
                Check(prefabs.Count > 0, source.name + ": default encounter budget produced no monsters.");
                Check(DungeonEncounterPlacement.TryPlan(room, prefabs, Seed + mapIndex, 3f, out var first, out var error),
                    source.name + ": " + error);
                Check(DungeonEncounterPlacement.TryPlan(room, prefabs, Seed + mapIndex, 3f, out var repeated, out error),
                    source.name + ": repeated planning failed: " + error);
                Check(first.Count == prefabs.Count && repeated.Count == first.Count, source.name + ": incomplete placement list.");
                float minHeight = float.PositiveInfinity, maxHeight = float.NegativeInfinity;
                for (int i = 0; i < first.Count; i++)
                {
                    var placement = first[i];
                    Check(placement.prefab == prefabs[i] && placement.prefab == repeated[i].prefab &&
                        placement.position.Equals(repeated[i].position) && placement.rotation.Equals(repeated[i].rotation),
                        source.name + ": seed changed an exact placement pose at index " + i + ".");
                    var agent = placement.prefab.GetComponent<NavMeshAgent>();
                    float offset = agent.baseOffset * Mathf.Abs(placement.prefab.transform.lossyScale.y);
                    Vector3 floor = placement.position - Vector3.up * offset;
                    var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
                    Check(NavMesh.SamplePosition(floor, out var hit, 0.1f, filter) &&
                        Mathf.Abs(hit.position.y - floor.y) <= 0.1f, source.name + ": pivot lost the prefab agent baseOffset.");
                    float localHeight = room.transform.InverseTransformPoint(floor).y;
                    minHeight = Mathf.Min(minHeight, localHeight);
                    maxHeight = Mathf.Max(maxHeight, localHeight);
                    bool nearMarker = false;
                    foreach (var marker in room.GetComponentsInChildren<BaekhoGameplayMarker>())
                    {
                        if (marker.kind != "enemy") continue;
                        Vector3 delta = floor - marker.transform.position;
                        if (new Vector2(delta.x, delta.z).sqrMagnitude <= 3.1f * 3.1f &&
                            Mathf.Abs(delta.y) <= 0.5f && Quaternion.Angle(placement.rotation, marker.transform.rotation) < 0.01f)
                        { nearMarker = true; break; }
                    }
                    Check(nearMarker, source.name + ": position crossed a marker tier/radius or lost room rotation.");
                }
                total += first.Count;
                summary.Append(source.name).Append(": ").Append(first.Count).Append(" enemies, ")
                    .Append(hasBake ? "baked" : "runtime bake").Append(", local floor y ")
                    .Append(minHeight.ToString("0.###")).Append("..").Append(maxHeight.ToString("0.###")).AppendLine();
            }

            var single = CreateSingleMarkerRoom(scene, instances);
            Check(DungeonEncounterPlacement.TryPrepareNavigation(single, out var singleData, out var singleError),
                "Single-marker fixture navigation: " + singleError);
            if (singleData != null) ownedData.Add(singleData);
            var crowdedPrefabs = new List<GameObject>();
            for (int i = 0; i < 6; i++) crowdedPrefabs.Add(catalog.enemies[i % catalog.enemies.Length].prefab);
            Check(DungeonEncounterPlacement.TryPlan(single, crowdedPrefabs, Seed, 3f, out var crowded, out singleError),
                "Several enemies around one marker: " + singleError);
            Check(crowded.Count == 6, "Single-marker fixture omitted enemies.");
            for (int i = 0; i < crowded.Count; i++)
            for (int j = 0; j < i; j++)
            {
                float minimum = crowded[i].prefab.GetComponent<NavMeshAgent>().radius +
                    crowded[j].prefab.GetComponent<NavMeshAgent>().radius + 0.2f;
                Vector3 delta = crowded[i].position - crowded[j].position;
                Check(new Vector2(delta.x, delta.z).magnitude >= minimum - 0.001f,
                    "Enemies placed around a single marker overlap.");
            }
            var onlyMarker = single.GetComponentInChildren<BaekhoGameplayMarker>();
            onlyMarker.kind = "reward";
            Check(!DungeonEncounterPlacement.TryPlan(single, crowdedPrefabs, Seed, 3f, out var rejected, out singleError) &&
                rejected.Count == 0 && !string.IsNullOrEmpty(singleError), "A room with no enemy marker accepted an encounter.");
            Check(DungeonEncounterPlacement.TryPlan(single, new List<GameObject>(), Seed, 3f, out var empty, out singleError) &&
                empty.Count == 0, "An empty encounter should need no enemy marker.");
            onlyMarker.kind = "enemy";
            single.groundObjects[0].GetComponent<Collider>().enabled = false;
            Physics.SyncTransforms();
            Check(!DungeonEncounterPlacement.TryPlan(single, crowdedPrefabs, Seed, 3f, out rejected, out singleError) &&
                rejected.Count == 0, "NavMesh without physical room ground accepted enemy placements.");
            summary.Append("Single marker: 6 enemies; spacing, missing marker, empty encounter and missing ground checks passed.");
            return _checks + " checks passed; " + settings.maps.Length + " maps, " + total +
                " placements; baked data reused " + reused + ", missing data built " + built + ".\n" + summary;
        }
        finally
        {
            foreach (var instance in instances)
            {
                if (instance == null) continue;
                foreach (var surface in instance.GetComponentsInChildren<NavMeshSurface>(true)) surface.RemoveData();
                UnityEngine.Object.DestroyImmediate(instance);
            }
            foreach (var data in ownedData)
                if (data != null && !EditorUtility.IsPersistent(data)) UnityEngine.Object.DestroyImmediate(data);
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static DungeonEnemyCatalog FindCatalog()
    {
        var candidates = AssetDatabase.FindAssets("t:DungeonEnemyCatalog");
        Check(candidates.Length == 1, "Expected exactly one configured dungeon enemy catalog before running placement regression.");
        var catalog = AssetDatabase.LoadAssetAtPath<DungeonEnemyCatalog>(AssetDatabase.GUIDToAssetPath(candidates[0]));
        Check(catalog != null, "Configured dungeon enemy catalog could not be loaded.");
        return catalog;
    }

    private static List<GameObject> Expand(DungeonEnemyCatalog catalog, int[] counts)
    {
        var prefabs = new List<GameObject>();
        for (int species = 0; species < counts.Length; species++)
            for (int count = 0; count < counts[species]; count++) prefabs.Add(catalog.enemies[species].prefab);
        return prefabs;
    }

    private static BaekhoMapModule CreateSingleMarkerRoom(Scene scene, List<GameObject> instances)
    {
        var root = new GameObject("Single-marker placement fixture");
        instances.Add(root);
        SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.SetPositionAndRotation(new Vector3(30000f, 50f, 30000f), Quaternion.Euler(0f, 90f, 0f));
        var room = root.AddComponent<BaekhoMapModule>();
        room.localBounds = new Bounds(Vector3.zero, new Vector3(12f, 0f, 12f));
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "floor_fixture";
        floor.transform.SetParent(root.transform, false);
        floor.transform.localPosition = Vector3.down * 0.5f;
        floor.transform.localScale = new Vector3(12f, 1f, 12f);
        room.groundObjects = new[] { floor };
        var markerObject = new GameObject("Only enemy marker");
        markerObject.transform.SetParent(root.transform, false);
        var marker = markerObject.AddComponent<BaekhoGameplayMarker>();
        marker.id = "enemy-only";
        marker.kind = "enemy";
        return room;
    }

    private static void Check(bool condition, string error)
    {
        if (!condition) throw new InvalidOperationException(error);
        _checks++;
    }
}
