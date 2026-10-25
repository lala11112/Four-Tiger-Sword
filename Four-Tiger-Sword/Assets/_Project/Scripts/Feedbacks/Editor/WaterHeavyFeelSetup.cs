using System;
using MoreMountains.Feedbacks;
using MoreMountains.FeedbacksForThirdParty;
using MoreMountains.Tools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Authors the water heavy attack's Feel preset without changing an open scene.</summary>
public static class WaterHeavyFeelSetup
{
    public const string PrefabPath = "Assets/_Project/Prefabs/Feedbacks/WaterHeavyFeel.prefab";
    public const string WaterActionPath = "Assets/_Project/Scripts/FormActions/WaterFormLab/WaterFormActionSO_V2.asset";

    [MenuItem("Tools/Combat/Set Up Water Heavy Feel")]
    private static void SetUpFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Author the water heavy Feel preset outside Play Mode.");

        var water = AssetDatabase.LoadAssetAtPath<WaterFormActionDataSO>(WaterActionPath);
        var profile = water != null && water.HeavyAttackStep != null
            ? water.HeavyAttackStep.CameraProfile : null;
        if (profile == null)
            throw new InvalidOperationException("The water heavy attack needs an existing camera profile.");

        bool reused = System.IO.File.Exists(PrefabPath)
            || AssetDatabase.LoadMainAssetAtPath(PrefabPath) != null;
        var existing = reused ? AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) : null;
        var prefab = reused ? (existing != null ? existing.GetComponent<CombatFeelFeedback>() : null)
            : CreatePrefab();
        if (prefab == null)
            throw new InvalidOperationException("The existing Feel prefab has no CombatFeelFeedback. It was not overwritten.");

        if (profile.FeelFeedbackPrefab != prefab)
        {
            profile.FeelFeedbackPrefab = prefab;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
        }

        return (reused ? "Reused existing " : "Created ") + PrefabPath
            + " and assigned it to the water heavy attack's camera profile.";
    }

    private static CombatFeelFeedback CreatePrefab()
    {
        EnsureFolder("Assets/_Project/Prefabs", "Feedbacks");
        var previewScene = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        try
        {
            // DontSave keeps temporary construction out of the active scene's serialized state.
            root = EditorUtility.CreateGameObjectWithHideFlags("WaterHeavyFeel", HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(root, previewScene);
            root.hideFlags = HideFlags.None;
            var bridge = root.AddComponent<CombatFeelFeedback>();
            bridge.OverrideImpulseDirection = true;
            bridge.CameraSpaceImpulseDirection = Vector3.down;

            bridge.ZoomFeedbacks = CreatePlayer("Attack Zoom", root.transform, TimescaleModes.Scaled);
            var zoom = (MMF_Property)bridge.ZoomFeedbacks.AddFeedback(typeof(MMF_Property));
            zoom.Label = "Water heavy zoom (FOV offset)";
            zoom.Target = new MMPropertyReceiver
            {
                TargetObject = root,
                TargetComponent = bridge,
                TargetPropertyName = nameof(CombatFeelFeedback.ZoomOffset),
                RelativeValue = false,
                FloatRemapZero = 0f,
                FloatRemapOne = 1f
            };
            zoom.RelativeValues = false;
            zoom.Duration = 1f; // The runtime bridge scales this to the attack duration.
            zoom.LevelCurve = new MMTweenType(new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.12f, -4f),
                new Keyframe(0.35f, -2f), new Keyframe(0.75f, 0f), new Keyframe(1f, 0f)),
                "", "Mode", (int)MMF_Property.Modes.OverTime, (int)MMF_Property.Modes.ToDestination);
            zoom.RemapLevelZero = 0f;
            zoom.RemapLevelOne = 1f;

            bridge.FlashGroup = CreateFlash(root.transform);
            bridge.HitFeedbacks = CreatePlayer("Confirmed Hit", root.transform, TimescaleModes.Unscaled);
            var impulse = (MMF_CinemachineImpulseSource)bridge.HitFeedbacks.AddFeedback(typeof(MMF_CinemachineImpulseSource));
            impulse.Label = "Confirmed hit impulse";
            impulse.ClearImpulseOnStop = false;
            // The bridge supplies the player's existing source and direction before playback.
            impulse.Velocity = Vector3.zero;

            var flash = (MMF_CanvasGroup)bridge.HitFeedbacks.AddFeedback(typeof(MMF_CanvasGroup));
            flash.Label = "Pale cyan hit flash";
            flash.TargetCanvasGroup = bridge.FlashGroup;
            flash.RelativeValues = false;
            flash.Duration = 0.12f;
            flash.AlphaCurve = new MMTweenType(new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.12f, 1f), new Keyframe(1f, 0f)));
            flash.RemapZero = 0f;
            flash.RemapOne = 0.16f;
            flash.Timing.TimescaleMode = TimescaleModes.Unscaled;

            var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (saved == null)
                throw new InvalidOperationException("Failed to save the water heavy Feel prefab.");
            return saved.GetComponent<CombatFeelFeedback>();
        }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(previewScene);
        }
    }

    private static MMF_Player CreatePlayer(string name, Transform parent, TimescaleModes timescale)
    {
        var player = CreateChild(name, parent).AddComponent<MMF_Player>();
        player.InitializationMode = MMFeedbacks.InitializationModes.Script;
        player.AutoInitialization = false;
        player.AutoPlayOnStart = false;
        player.AutoPlayOnEnable = false;
        player.PlayerTimescaleMode = timescale;
        player.ForceTimescaleMode = true;
        player.ForcedTimescaleMode = timescale;
        player.StopFeedbacksOnDisable = true;
        // Restoring an impulse feedback clears all Cinemachine impulses, so cleanup is explicit.
        player.RestoreInitialValuesOnDisable = false;
        return player;
    }

    private static CanvasGroup CreateFlash(Transform parent)
    {
        var canvasObject = CreateChild("Hit Flash Canvas", parent, typeof(RectTransform));
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var group = canvasObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        var imageObject = CreateChild("Full Screen Flash", canvasObject.transform, typeof(RectTransform));
        var rect = (RectTransform)imageObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var image = imageObject.AddComponent<UnityEngine.UI.Image>();
        image.color = new Color(0.8f, 0.94f, 1f, 1f);
        image.raycastTarget = false;
        image.maskable = false;
        return group;
    }

    private static GameObject CreateChild(string name, Transform parent, params Type[] components)
    {
        var child = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave, components);
        child.transform.SetParent(parent, false);
        child.hideFlags = HideFlags.None;
        return child;
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name))
            AssetDatabase.CreateFolder(parent, name);
    }
}
