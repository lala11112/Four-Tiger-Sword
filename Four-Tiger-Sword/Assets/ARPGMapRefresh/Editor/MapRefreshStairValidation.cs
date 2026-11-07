#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Checks restored routes against the actual baked navigation of each derived room.</summary>
public static class MapRefreshStairValidation
{
    const string MissingStairGuid = "a0b56e7f0a62df34a888a8b7d8030442";
    const string RestoredName = "V3RestoredStructure";

    [Serializable] public sealed class Report
    {
        public bool passed;
        public int roomsChecked, expectedStructures, checkedStructures, connectedStructures;
        public float longestPath;
        public string reportPath;
        public List<string> errors = new List<string>();
    }

    public static string Run()
    {
        var report = new Report();
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            report.errors.Add("Run the restored-stair check in Edit Mode.");
            return Save(report);
        }
        var originalScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
        try
        {
            var entries = MapRefreshValidation.ReadManifest().entries;
            for (int index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                try { CheckRoom(entry, scene, index, report); }
                catch (Exception error) { report.errors.Add(entry.prefabPath + " — " + error.Message); }
            }
        }
        finally
        {
            if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
        if (report.roomsChecked != 23 || report.expectedStructures != 82 || report.checkedStructures != 82)
            report.errors.Add($"Expected 23 rooms and 82 restored stairs/slopes. Checked {report.roomsChecked} rooms and {report.checkedStructures}/{report.expectedStructures} structures.");
        report.passed = report.errors.Count == 0 && report.connectedStructures == 82;
        return Save(report);
    }

    static void CheckRoom(MapRefreshValidation.Entry entry, Scene scene, int index, Report report)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(entry.sourcePath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.prefabPath);
        if (!source || !prefab) throw new InvalidDataException("Missing source or derived prefab.");
        var placeholders = source.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name.Contains(MissingStairGuid)).ToList();
        report.expectedStructures += placeholders.Count;
        var room = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        if (!room) throw new InvalidDataException("Could not instantiate the derived prefab.");
        var surfaces = room.GetComponentsInChildren<NavMeshSurface>(true);
        try
        {
            foreach (var surface in surfaces) surface.RemoveData();
            room.transform.SetPositionAndRotation(new Vector3(50000f + index * 256f, 0f, 50000f), Quaternion.Euler(0f, index % 4 * 90f, 0f));
            var baked = room.GetComponent<NavMeshSurface>();
            if (!baked || !baked.isActiveAndEnabled || baked.agentTypeID != 0 || !baked.navMeshData || !EditorUtility.IsPersistent(baked.navMeshData))
                throw new InvalidDataException("The room requires its active, persistent agent-type-0 NavMesh bake.");
            baked.AddData();
            Physics.SyncTransforms();
            report.roomsChecked++;
            var restored = room.GetComponentsInChildren<Transform>(true).Where(t => t.name == RestoredName).ToArray();
            if (restored.Length != placeholders.Count)
                report.errors.Add($"{entry.prefabPath} — {restored.Length} restored structures for {placeholders.Count} original placeholders.");
            var filter = new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
            foreach (var wrapper in restored)
            {
                report.checkedStructures++;
                var parent = wrapper.parent;
                var local = room.transform.InverseTransformPoint(parent.position);
                string label = $"{entry.prefabPath} — {local.ToString("F2")}";
                var original = placeholders.Find(t => t.name == parent.name
                    && Vector3.Distance(source.transform.InverseTransformPoint(t.position), local) < .02f
                    && Quaternion.Angle(Quaternion.Inverse(source.transform.rotation) * t.rotation,
                        Quaternion.Inverse(room.transform.rotation) * parent.rotation) < .2f
                    && Vector3.Distance(t.localScale, parent.localScale) < .002f);
                if (!original)
                {
                    report.errors.Add(label + " — original placeholder pose changed or was reused.");
                    continue;
                }
                placeholders.Remove(original);
                if (!CheckRoute(wrapper, room.transform, filter, out float length, out string error))
                {
                    report.errors.Add(label + " — " + error);
                    continue;
                }
                report.connectedStructures++;
                report.longestPath = Mathf.Max(report.longestPath, length);
            }
        }
        finally
        {
            foreach (var surface in surfaces) if (surface) surface.RemoveData();
            Object.DestroyImmediate(room);
        }
    }

    static bool CheckRoute(Transform wrapper, Transform room, NavMeshQueryFilter filter, out float length, out string error)
    {
        length = 0f;
        error = null;
        var colliders = wrapper.GetComponentsInChildren<Collider>(true)
            .Where(c => c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy).ToArray();
        if (colliders.Length == 0 || colliders.Any(c => c is MeshCollider mesh && (!mesh.sharedMesh || !EditorUtility.IsPersistent(mesh.sharedMesh))))
        {
            error = "Missing active collision or persistent collision mesh.";
            return false;
        }
        var lowEnd = wrapper.TransformPoint(new Vector3(0f, 0f, -.5f));
        var highEnd = wrapper.TransformPoint(new Vector3(0f, 1f, .5f));
        var forward = Vector3.ProjectOnPlane(highEnd - lowEnd, Vector3.up);
        float run = forward.magnitude, rise = highEnd.y - lowEnd.y;
        if (run < .01f || rise < .01f)
        {
            error = "The restored wrapper has no positive run or rise.";
            return false;
        }
        // The legacy manifests use 2 m cells. Probe the center of the adjacent
        // authored landing cell, 1 m beyond each edge, away from the slope seam.
        var margin = forward / run;
        var settings = NavMesh.GetSettingsByID(filter.agentTypeID);
        if (!SampleLanding(lowEnd - margin, room, settings, filter, out var low, out var landingError))
        {
            error = "Low landing — " + landingError;
            return false;
        }
        if (!SampleLanding(highEnd + margin, room, settings, filter, out var high, out landingError))
        {
            error = "High landing — " + landingError;
            return false;
        }
        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(low, high, filter, path) || path.status != NavMeshPathStatus.PathComplete)
        {
            error = "NavMesh route between restored landings is " + path.status + ".";
            return false;
        }
        var corners = path.corners;
        for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
        if (corners.Length < 2 || length >= run + rise + 4f || !CrossesRestoredRamp(wrapper, corners))
        {
            error = $"Complete path bypasses the restored ramp or exceeds its local route limit ({length:F2}m, limit {run + rise + 4f:F2}m).";
            return false;
        }
        return true;
    }

    static bool SampleLanding(Vector3 target, Transform room, NavMeshBuildSettings settings,
        NavMeshQueryFilter filter, out Vector3 point, out string error)
    {
        point = default;
        error = null;
        if (!Physics.Raycast(target + Vector3.up * .3f, Vector3.down, out var floor, .6f, ~0, QueryTriggerInteraction.Ignore)
            || !floor.collider.transform.IsChildOf(room) || Mathf.Abs(floor.point.y - target.y) > .06f)
        {
            error = $"No physical floor at authored height {target.y:F2} m.";
            return false;
        }
        float radius = Mathf.Max(.01f, settings.agentRadius - .01f);
        float capsuleBottom = radius + .06f;
        float capsuleTop = Mathf.Max(capsuleBottom, settings.agentHeight - radius + .06f);
        var overlap = Physics.OverlapCapsule(target + Vector3.up * capsuleBottom,
            target + Vector3.up * capsuleTop, radius, ~0, QueryTriggerInteraction.Ignore);
        var blocker = Array.Find(overlap, collider => collider.transform.IsChildOf(room));
        if (blocker)
        {
            error = "Physical approach is blocked by " + blocker.name + ".";
            return false;
        }
        if (!NavMesh.SamplePosition(target, out var hit, .55f, filter))
        {
            error = $"No NavMesh at authored height {target.y:F2} m.";
            return false;
        }
        var offset = hit.position - target;
        float lateral = Vector3.ProjectOnPlane(offset, Vector3.up).magnitude;
        if (Mathf.Abs(offset.y) > .2f || lateral > .45f)
        {
            error = $"NavMesh sample is {offset.y:F2} m vertically and {lateral:F2} m laterally from the authored landing.";
            return false;
        }
        point = hit.position;
        return true;
    }

    static bool CrossesRestoredRamp(Transform ramp, Vector3[] corners)
    {
        // A short path via a neighbouring ramp can still pass the length limit.
        // Require a crossing through the actual restored ramp's midpoint plane.
        for (int i = 1; i < corners.Length; i++)
        {
            var a = ramp.InverseTransformPoint(corners[i - 1]);
            var b = ramp.InverseTransformPoint(corners[i]);
            if (a.z * b.z > 0f || Mathf.Abs(b.z - a.z) < .0001f) continue;
            var crossing = Vector3.Lerp(a, b, -a.z / (b.z - a.z));
            if (Mathf.Abs(crossing.x) <= .55f && crossing.y >= .2f && crossing.y <= .8f) return true;
        }
        return false;
    }

    static string Save(Report report)
    {
        Directory.CreateDirectory(MapRefreshValidation.VerificationRoot);
        report.reportPath = Path.Combine(MapRefreshValidation.VerificationRoot, "StairNavigation.json");
        var json = JsonUtility.ToJson(report, true);
        File.WriteAllText(report.reportPath, json);
        return json;
    }
}
#endif
