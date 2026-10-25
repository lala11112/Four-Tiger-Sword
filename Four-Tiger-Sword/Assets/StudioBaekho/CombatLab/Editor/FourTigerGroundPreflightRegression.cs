// Explicit Edit Mode regression. Uses one owned temporary collider and leaves
// all scene objects, source assets and RunManager instances untouched.
using System;
using System.Collections.Generic;
using System.Reflection;
using StudioBaekho.CombatLab.FourTigerSword;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class FourTigerGroundPreflightRegression
{
    [Serializable] private sealed class Result { public int checks; public string[] passed; }
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run the ground preflight regression in Edit Mode.");
        var passed = new List<string>();
        var method = typeof(FourTigerCombatAdapter).GetMethod("ValidateGroundColliders", BindingFlags.Static | BindingFlags.NonPublic);
        if (method == null) throw new InvalidOperationException("Ground preflight contract changed.");
        void Check(bool expected, Collider[] colliders, bool fixedScenario, bool playing, bool runtimeRun, string label)
        {
            bool accepted = true;
            try { method.Invoke(null, new object[] { colliders, 0, fixedScenario, playing, runtimeRun }); }
            catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { accepted = false; }
            if (accepted != expected) throw new InvalidOperationException(label);
            passed.Add(label);
        }
        var empty = Array.Empty<Collider>();
        Check(true, empty, true, false, true, "Fixed-v2 Edit Mode preflight allows the active RunManager to create its floor in Play Mode");
        Check(false, empty, true, true, true, "A procedural fixed experiment still requires the real floor before runtime readiness and Begin");
        Check(false, empty, true, false, false, "Fixed Edit Mode without an active RunManager still rejects a missing floor");
        Check(false, empty, false, false, true, "Legacy and live Edit Mode validation retain the existing floor requirement");
        Check(false, empty, false, true, true, "Legacy and live Play Mode validation retain the existing floor requirement");
        var preview = EditorSceneManager.NewPreviewScene();
        GameObject owned = null;
        try
        {
            owned = new GameObject("CombatLab ground preflight regression") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(owned, preview);
            var collider = owned.AddComponent<BoxCollider>();
            var colliders = new Collider[] { collider };
            Check(true, colliders, true, true, true, "An enabled non-trigger collider on the floor layer satisfies runtime validation");
            collider.enabled = false;
            Check(false, colliders, true, true, true, "A disabled collider cannot satisfy runtime floor validation");
            collider.enabled = true; collider.isTrigger = true;
            Check(false, colliders, true, true, true, "A trigger cannot satisfy runtime floor validation");
            collider.isTrigger = false; owned.layer = 1;
            Check(false, colliders, true, true, true, "A collider on another layer cannot satisfy runtime floor validation");
            owned.layer = 0; owned.SetActive(false);
            Check(false, colliders, true, true, true, "An inactive floor cannot satisfy runtime validation");
        }
        finally
        {
            if (owned != null) UnityEngine.Object.DestroyImmediate(owned);
            EditorSceneManager.ClosePreviewScene(preview);
        }
        return JsonUtility.ToJson(new Result { checks = passed.Count, passed = passed.ToArray() });
    }
}
