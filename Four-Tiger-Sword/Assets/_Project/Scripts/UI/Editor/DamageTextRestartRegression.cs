using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Pool;
using Object = UnityEngine.Object;

/// <summary>Exercises actual pool teardown when scene-owned canvases are destroyed before their manager.</summary>
public static class DamageTextRestartRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run damage-text lifecycle regression in Edit Mode.");
        int checks = 0;
        var previous = DamageTextManager.Instance;
        var root = new GameObject("__DamageTextRestartRegression") { hideFlags = HideFlags.HideAndDontSave };
        DamageTextManager firstManager = null, secondManager = null;
        Canvas firstCanvas = null, secondCanvas = null;
        try
        {
            DamageTextManager.Instance = null;
            var prefab = new GameObject("DamageTextPrefab"); prefab.transform.SetParent(root.transform, false);
            prefab.AddComponent<DamageText>(); prefab.SetActive(false);
            firstCanvas = Canvas("FirstCanvas", root.transform);
            firstManager = Manager("FirstManager", root.transform, prefab, firstCanvas);
            var firstPool = Pool(firstManager);
            var firstLabel = firstPool.Get(); firstPool.Release(firstLabel);
            Check(DamageTextManager.Instance == firstManager && firstPool.CountInactive == 1,
                "The initial scene did not own its manager and pooled label.", ref checks);

            Object.DestroyImmediate(firstCanvas.gameObject); firstCanvas = null;
            Check(firstLabel == null, "Destroying the scene canvas retained a pooled scene label.", ref checks);
            firstManager.ShowDamageText(5, Vector3.zero);
            Invoke(firstManager, "OnDestroy");
            Check(DamageTextManager.Instance == null && firstPool.CountInactive == 0 && Pool(firstManager) == null,
                "Manager teardown retained a singleton or stale pool entry.", ref checks);
            Object.DestroyImmediate(firstManager.gameObject); firstManager = null;

            secondCanvas = Canvas("SecondCanvas", root.transform);
            secondManager = Manager("SecondManager", root.transform, prefab, secondCanvas);
            var secondPool = Pool(secondManager); var secondLabel = secondPool.Get();
            Check(DamageTextManager.Instance == secondManager && !ReferenceEquals(firstPool, secondPool)
                && secondLabel != null && secondLabel.transform.parent == secondCanvas.transform,
                "A restarted scene reused the previous manager, pool or canvas.", ref checks);
            secondPool.Release(secondLabel);
            Object.DestroyImmediate(secondCanvas.gameObject); secondCanvas = null;
            Invoke(secondManager, "OnDestroy");
            Check(DamageTextManager.Instance == null && secondPool.CountInactive == 0,
                "The second teardown failed after a restart.", ref checks);
            return $"PASS: damage-text restart lifecycle ({checks} checks).";
        }
        finally
        {
            if (firstCanvas != null) Object.DestroyImmediate(firstCanvas.gameObject);
            if (secondCanvas != null) Object.DestroyImmediate(secondCanvas.gameObject);
            if (firstManager != null) Invoke(firstManager, "OnDestroy");
            if (secondManager != null) Invoke(secondManager, "OnDestroy");
            Object.DestroyImmediate(root);
            DamageTextManager.Instance = previous != null ? previous : null;
        }
    }

    private static Canvas Canvas(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas)); go.transform.SetParent(parent, false);
        return go.GetComponent<Canvas>();
    }

    private static DamageTextManager Manager(string name, Transform parent, GameObject prefab, Canvas canvas)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.SetActive(false);
        var manager = go.AddComponent<DamageTextManager>();
        typeof(DamageTextManager).GetField("damageTextPrefab", Hidden).SetValue(manager, prefab);
        typeof(DamageTextManager).GetField("worldSpaceCanvas", Hidden).SetValue(manager, canvas);
        Invoke(manager, "Awake");
        return manager;
    }

    private static ObjectPool<DamageText> Pool(DamageTextManager manager)
        => (ObjectPool<DamageText>)typeof(DamageTextManager).GetField("_pool", Hidden).GetValue(manager);
    private static void Invoke(DamageTextManager manager, string method)
        => typeof(DamageTextManager).GetMethod(method, Hidden).Invoke(manager, null);
    private static void Check(bool condition, string message, ref int checks)
    { checks++; if (!condition) throw new InvalidOperationException("Damage-text lifecycle: " + message); }
}
