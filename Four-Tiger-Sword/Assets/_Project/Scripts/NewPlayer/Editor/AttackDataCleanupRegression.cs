using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Checks optional camera authoring, serialization and playback without changing production assets.</summary>
public static class AttackDataCleanupRegression
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Studio Baekho/Combat/Run Attack Data Cleanup Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Attack data cleanup regression requires Edit Mode.");
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Attack data cleanup: " + message);
            checks++;
        }

        var owned = new List<Object>();
        string folder = null;
        try
        {
            Check(new AttackCameraSettings().V2 == null && new HitFeedbackSettings().CameraV2 == null,
                "New attacks and hits inherit camera settings without allocating individual overrides.");
            Check(typeof(AttackCameraSettings).GetField("V2", Fields).GetCustomAttribute<SerializeReference>() != null
                && typeof(HitFeedbackSettings).GetField("CameraV2", Fields).GetCustomAttribute<SerializeReference>() != null,
                "Optional attack and hit cameras use managed references so null survives asset reload.");
            Check(typeof(CombatCameraHitSettings).GetField("Motion", Fields) == null
                && typeof(CombatCameraHitSettings).GetField("Mode", Fields) == null
                && typeof(CombatCameraV2Settings).IsSubclassOf(typeof(CombatCameraHitSettings)),
                "Per-hit camera data contains only hit channels; attack motion extends the same common hit settings.");
            Check(typeof(WeaponActionDataSO).GetField("WeaponName", Fields) == null
                && typeof(HitBalanceSettings).GetField("StaggerResistLevel", Fields) == null
                && typeof(HitEvent).GetProperty("StaggerResistLevel", Fields) == null
                && typeof(AttackFeedbackSettings).GetField("EnableV2", Fields) == null,
                "Removed unused common fields cannot appear in new Inspector data.");
            Check(typeof(FireFormBalanceSettings).GetField("FireUltimateHealthDrainPercentPerSecond", Fields) == null
                && typeof(WaterFormActionDataSO).GetField("FormFeedback", Fields) == null
                && typeof(WaterFormActionDataSO).GetProperty("DelayedHitVFX", Fields) == null,
                "Retired fire upkeep and water delayed-hit feedback have no remaining serialized authoring slots.");
            Check(typeof(ActionBalanceSettings).GetField("StaggerResistLevel", Fields) != null
                && typeof(HitStopSettings).GetField("EnableV2", Fields) != null,
                "Cleanup preserves active attack stagger and special-action hit-stop controls.");

            var profile = ScriptableObject.CreateInstance<AttackCameraProfile>();
            owned.Add(profile);
            profile.name = "Profile";
            profile.EnableV2 = true;
            profile.V2.Mode = CombatFeedbackChannelMode.Override;
            profile.V2.Motion.FovOffset = new AnimationCurve(new Keyframe(0f, -2f, 3f, 4f), new Keyframe(1f, 1f, 5f, 6f));
            profile.V2.Impact.Mode = CombatFeedbackChannelMode.Override;
            profile.V2.Impact.PositionX = AnimationCurve.Constant(0f, 1f, .25f);
            profile.V2.Shake.Mode = CombatFeedbackChannelMode.Override;
            profile.V2.Shake.Translation = new Vector3(.15f, .05f, 0f);
            string profileBefore = EditorJsonUtility.ToJson(profile);
            Check(ReferenceEquals(CombatCameraV2Settings.Resolve(profile, null), profile.V2),
                "A missing attack override inherits the existing common camera profile directly.");
            Check(ReferenceEquals(CombatCameraV2Settings.Resolve(profile, new CombatCameraV2Settings()), profile.V2),
                "An untouched optional block does not replace a common profile.");
            var local = new CombatCameraV2Settings { Authored = true };
            local.Impact.Mode = CombatFeedbackChannelMode.Off;
            var resolved = CombatCameraV2Settings.Resolve(profile, local);
            Check(resolved.Mode == profile.V2.Mode && ReferenceEquals(resolved.Motion, profile.V2.Motion)
                && resolved.Impact.Mode == CombatFeedbackChannelMode.Off && ReferenceEquals(resolved.Shake, profile.V2.Shake),
                "A local Off channel suppresses that channel while other attack channels keep inheriting.");
            Check(EditorJsonUtility.ToJson(profile) == profileBefore && local.Mode == CombatFeedbackChannelMode.Inherit,
                "Resolving inherited camera settings never edits the common profile or local source.");

            var source = ScriptableObject.CreateInstance<FireFormActionDataSO>();
            owned.Add(source);
            source.name = "Form";
            source.ComboSteps = new List<WeaponActionData>
            {
                new WeaponActionData(),
                new WeaponActionData { Feedback = new AttackFeedbackSettings
                    { Camera = new AttackCameraSettings { CameraProfile = profile, V2 = local } } },
                new WeaponActionData { HitEvents = new List<HitEvent>
                    { new HitEvent { Feedback = new HitFeedbackSettings { CameraV2 = HitOff() } }, new HitEvent() } }
            };
            var empty = JsonUtility.FromJson<WeaponActionData>(JsonUtility.ToJson(source.ComboSteps[0]));
            Check(empty.Feedback.Camera.V2 == null, "JsonUtility cloning preserves a null attack-camera override.");
            var clone = JsonUtility.FromJson<WeaponActionData>(JsonUtility.ToJson(source.ComboSteps[2]));
            Check(clone.Feedback.Camera.V2 == null && clone.HitEvents[1].Feedback.CameraV2 == null
                && clone.HitEvents[0].Feedback.CameraV2?.GetType() == typeof(CombatCameraHitSettings)
                && clone.HitEvents[0].Feedback.CameraV2.Impact.Mode == CombatFeedbackChannelMode.Off,
                "JsonUtility cloning preserves null hit cameras and a hit-only explicit Off override.");
            Check(!ReferenceEquals(clone.HitEvents[0].Feedback.CameraV2, source.ComboSteps[2].HitEvents[0].Feedback.CameraV2),
                "Cloned hit overrides own their settings independently of the source.");
            var attackClone = JsonUtility.FromJson<WeaponActionData>(JsonUtility.ToJson(source.ComboSteps[1]));
            Check(attackClone.Feedback.Camera.V2?.GetType() == typeof(CombatCameraV2Settings)
                && attackClone.Feedback.Camera.V2.Impact.Mode == CombatFeedbackChannelMode.Off,
                "JsonUtility cloning preserves the concrete full attack-camera settings type.");

            var clip = AudioClip.Create("Attack data cleanup audio", 32, 1, 8000, false);
            owned.Add(clip);
            var feedback = new AttackFeedbackSettings();
            feedback.SwingSound = clip;
            Check(feedback.SwingAudio.Clip == clip && typeof(AttackFeedbackSettings).GetField("SwingSound", Fields) == null,
                "SwingSound writes the single serialized SwingAudio clip instead of a duplicate field.");
            feedback.SwingAudio.Clip = null;
            Check(feedback.SwingSound == null, "The swing compatibility accessor reads the same live clip.");
            source.HitSound = clip;
            Check(source.CommonFeedback.HitAudio.Clip == clip && typeof(FormFeedbackSettings).GetField("HitSound", Fields) == null,
                "The common hit compatibility accessor writes the single serialized HitAudio clip.");
            source.CommonFeedback.HitAudio.Clip = null;
            Check(source.HitSound == null, "The common hit compatibility accessor reads the same live clip.");

            CheckInspectorEditing(source, Check);
            folder = "Assets/__AttackDataCleanupRegression_" + Guid.NewGuid().ToString("N");
            Check(!string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets", folder.Substring(7))),
                "Create an isolated temporary folder for the asset round trip.");
            AssetDatabase.CreateAsset(profile, folder + "/Profile.asset");
            string path = folder + "/Form.asset";
            AssetDatabase.CreateAsset(source, path);
            EditorUtility.SetDirty(source);
            AssetDatabase.SaveAssetIfDirty(source);
            Resources.UnloadAsset(source);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<FireFormActionDataSO>(path);
            Check(reloaded != null && reloaded.ComboSteps[0].Feedback.Camera.V2 == null
                && reloaded.ComboSteps[2].HitEvents[1].Feedback.CameraV2 == null,
                "Asset save and reload preserve absent camera overrides as null.");
            var savedAttack = reloaded.ComboSteps[1].Feedback.Camera;
            var savedHit = reloaded.ComboSteps[2].HitEvents[0].Feedback.CameraV2;
            Check(savedAttack.V2?.GetType() == typeof(CombatCameraV2Settings)
                && savedAttack.CameraProfile == profile && savedAttack.V2.Impact.Mode == CombatFeedbackChannelMode.Off,
                "Asset reload preserves explicit attack settings and the common profile reference.");
            Check(savedHit?.GetType() == typeof(CombatCameraHitSettings) && savedHit.Authored
                && savedHit.Impact.Mode == CombatFeedbackChannelMode.Off && savedHit.Shake.Mode == CombatFeedbackChannelMode.Off,
                "Asset reload preserves hit-only Off settings without materializing attack motion.");
            Check(EditorJsonUtility.ToJson(profile) == profileBefore,
                "Saving an independently authored form does not mutate its shared camera profile.");
            CheckHitPlayback(profile, Check);
            return $"PASS: attack data cleanup ({checks} checks: unused fields, optional cameras, profile inheritance, JsonUtility and asset round trips, Inspector undo, audio aliases and per-hit Off playback).";
        }
        finally
        {
            if (folder != null && AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null && !EditorUtility.IsPersistent(owned[i])) Object.DestroyImmediate(owned[i]);
        }
    }

    // Run separately after migrating existing form assets; this check never saves those assets.
    public static string RunProjectAssets()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Attack data audit requires Edit Mode.");
        string[] guids = AssetDatabase.FindAssets("t:WeaponActionDataSO", new[] { "Assets/_Project/Scripts/FormActions" });
        int cameras = 0, hits = 0;
        if (guids.Length != 8) throw new InvalidOperationException("Expected eight existing form assets, found " + guids.Length);
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var form = AssetDatabase.LoadAssetAtPath<WeaponActionDataSO>(path);
            foreach (var step in Steps(form))
            {
                if (step.Feedback.Camera.V2 != null)
                {
                    cameras++;
                    if (!(form is WaterFormActionDataSO) || !step.Feedback.Camera.V2.HasOverrides)
                        throw new InvalidOperationException("Unexpected materialized attack camera: " + path);
                }
                foreach (var hit in step.HitEvents ?? new List<HitEvent>())
                {
                    if (hit == null) continue;
                    hits++;
                    if (hit.Feedback.CameraV2 != null)
                        throw new InvalidOperationException("A previously untouched hit camera must remain null: " + path);
                }
            }
        }
        if (cameras != 4 || hits != 31)
            throw new InvalidOperationException($"Unexpected migrated data counts: {cameras} attack overrides, {hits} null hit cameras.");
        return $"PASS: migrated attack assets ({guids.Length} forms, {cameras} authored water attack overrides, {hits} null hit cameras).";
    }

    private static void CheckInspectorEditing(FireFormActionDataSO source, Action<bool, string> check)
    {
        const string path = "Actions.ComboSteps.Array.data[0].Feedback.Camera.V2";
        using (var serialized = new SerializedObject(source))
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Regression add optional camera");
            try
            {
                var property = serialized.FindProperty(path);
                check(property != null && property.propertyType == SerializedPropertyType.ManagedReference
                    && property.managedReferenceValue == null, "The Inspector sees a missing camera as an editable null managed reference.");
                property.managedReferenceValue = new CombatCameraV2Settings { Authored = true };
                serialized.ApplyModifiedProperties();
                Undo.FlushUndoRecordObjects();
                check(source.ComboSteps[0].Feedback.Camera.V2?.Authored == true,
                    "Adding an optional camera through SerializedObject updates the underlying attack.");
                Undo.PerformUndo();
                serialized.Update();
                check(source.ComboSteps[0].Feedback.Camera.V2 == null, "Undo restores the common-camera state without a leftover block.");
                Undo.PerformRedo();
                serialized.Update();
                check(source.ComboSteps[0].Feedback.Camera.V2?.Authored == true, "Redo restores the authored optional camera.");
                serialized.FindProperty(path).managedReferenceValue = null;
                serialized.ApplyModifiedProperties();
                check(source.ComboSteps[0].Feedback.Camera.V2 == null, "Clearing an Inspector override restores the common-camera state.");
            }
            finally
            {
                Undo.ClearUndo(source);
                Undo.IncrementCurrentGroup();
            }
        }
    }

    private static void CheckHitPlayback(AttackCameraProfile profile, Action<bool, string> check)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var playerObject = new GameObject("__CleanupCameraPlayer");
            SceneManager.MoveGameObjectToScene(playerObject, scene);
            var player = playerObject.AddComponent<PlayerController>();
            var focus = new GameObject("Focus").transform;
            focus.SetParent(playerObject.transform, false);
            player.CameraTransform = focus;
            var cameraObject = new GameObject("__CleanupCamera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<CinemachineCamera>();
            camera.Follow = focus;
            var director = cameraObject.AddComponent<CombatCameraDirector>();
            var mixer = (CombatCameraV2Mixer)typeof(CombatCameraDirector).GetField("_mixer", Hidden).GetValue(director);
            check(director.BeginAttack(profile, player, null, Vector3.forward), "The playback fixture accepts the shared camera profile.");
            check(director.NotifyHit(player, null, Vector3.forward, HitOff(), 0)
                && mixer.ImpactCount == 0 && mixer.ShakeCount == 0,
                "An authored per-hit Off suppresses both inherited impact and shake during real director playback.");
            director.Cancel(player, true);
            check(director.BeginAttack(profile, player, null, Vector3.forward)
                && director.NotifyHit(player, null, Vector3.forward, null, 1)
                && mixer.ImpactCount == 1 && mixer.ShakeCount == 1,
                "A null per-hit override inherits and plays the common profile's impact and shake.");
            director.Cancel(player, true);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static CombatCameraHitSettings HitOff()
        => new CombatCameraHitSettings { Authored = true,
            Impact = new CombatCameraImpactV2 { Mode = CombatFeedbackChannelMode.Off },
            Shake = new CombatCameraShakeV2 { Mode = CombatFeedbackChannelMode.Off } };

    private static IEnumerable<WeaponActionData> Steps(WeaponActionDataSO form)
    {
        var groups = new[] { form.ComboSteps, form.SkillSteps, form.SwitchAttackSteps, form.UltimateSteps };
        foreach (var group in groups)
            if (group != null) foreach (var step in group) if (step != null) yield return step;
        if (form.AirAttackStep != null) yield return form.AirAttackStep;
        if (form.CounterStep != null) yield return form.CounterStep;
        if (form.FinisherStep != null) yield return form.FinisherStep;
        if (form is WaterFormActionDataSO water && water.HeavyAttackStep != null) yield return water.HeavyAttackStep;
    }
}
