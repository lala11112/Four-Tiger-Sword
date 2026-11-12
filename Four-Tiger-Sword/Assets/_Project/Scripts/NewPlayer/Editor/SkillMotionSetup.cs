using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Bakes only the shipped Fire/Water E skills; never changes clips or runtime root motion.</summary>
public static class SkillMotionSetup
{
    private const string ControllerPath = "Assets/_Project/Animations/AnimController/BDJ.controller";
    private const string ModelPath = "Assets/Character.fbx";
    private const string ActionFolder = "Assets/_Project/Scripts/FormActions/";
    private const float SampleRate = 120f;
    private const float CurveErrorMetres = .0005f;
    // Character.fbx is a 1.4-scale child of the unit-scale Player in Game Scene and Hub Scene.
    public const float PlayerModelScale = 1.4f;

    public sealed class MotionSample
    {
        public float[] Times;
        public float[] ForwardPositions;
        public float TotalDistance;
        public float Duration;
        public AnimationCurve Progress;
    }

    private sealed class Bake
    {
        public string Path;
        public WeaponActionDataSO Data;
        public WeaponActionData Step;
        public MotionSample Motion;
    }

    [MenuItem("Tools/Studio Baekho/Combat/Bake Player Skill Motion")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        RequireEditMode();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) throw new InvalidOperationException("Missing player Animator controller.");
        var specs = new[]
        {
            (File: "FireFormAction.asset", State: "ComboSlash", Type: typeof(FireFormActionDataSO)),
            (File: "WaterFormV2.asset", State: "WaterSkill", Type: typeof(WaterFormActionDataSO)),
            (File: "WaterFormLab/WaterFormActionSO_V2.asset", State: "WaterSkill", Type: typeof(WaterFormActionDataSO))
        };
        var bakes = new List<Bake>();
        var samples = new Dictionary<(AnimationClip, float), MotionSample>();
        // Validate and sample the entire set before writing any of the loaded Inspector values.
        foreach (var spec in specs)
        {
            string path = ActionFolder + spec.File;
            var data = AssetDatabase.LoadAssetAtPath<WeaponActionDataSO>(path);
            if (data == null || !spec.Type.IsInstanceOfType(data) || data.SkillSteps?.Count != 1)
                throw new InvalidOperationException("Unexpected player skill data: " + path);
            var step = data.SkillSteps[0];
            if (step?.Execution?.Movement == null || step.AnimationName != spec.State)
                throw new InvalidOperationException("Unexpected skill animation or missing movement: " + path);
            var state = FindState(controller.layers[0].stateMachine, spec.State);
            if (state == null || !(state.motion is AnimationClip clip) || state.speedParameterActive
                || !Mathf.Approximately(state.speed, 1f) || !Mathf.Approximately(state.cycleOffset, 0f))
                throw new InvalidOperationException("Expected a single clip at speed one: " + spec.State);
            var key = (clip, step.Duration);
            if (!samples.TryGetValue(key, out var sample)) samples[key] = sample = Sample(clip, step.Duration);
            if (sample.TotalDistance <= .001f)
                throw new InvalidOperationException("Skill clip has no usable forward root motion: " + spec.State);
            bakes.Add(new Bake { Path = path, Data = data, Step = step, Motion = sample });
        }

        var report = new StringBuilder();
        foreach (var bake in bakes)
        {
            Undo.RecordObject(bake.Data, "Bake player skill forward motion");
            bake.Step.UseDistanceMovement = true;
            bake.Step.ForwardDistance = bake.Motion.TotalDistance;
            bake.Step.MovementStartTime = 0f;
            bake.Step.MovementEndTime = bake.Motion.Duration;
            bake.Step.MovementProgress = new AnimationCurve(bake.Motion.Progress.keys);
            EditorUtility.SetDirty(bake.Data);
            AssetDatabase.SaveAssetIfDirty(bake.Data);
            report.AppendLine($"{bake.Path}: {bake.Step.AnimationName}, {bake.Motion.TotalDistance:F4} m, "
                + $"{bake.Motion.Duration:F3} s, {bake.Motion.Times.Length} samples, {bake.Motion.Progress.length} keys.");
        }
        return "Baked player E-skill movement from the 1.4-scale player Avatar at 120 Hz.\n" + report;
    }

    /// <summary>Samples a disposable rig in isolation; no scene, source clip or asset is changed.</summary>
    public static MotionSample Sample(AnimationClip clip, float duration)
    {
        RequireEditMode();
        if (clip == null || !clip.humanMotion || !Finite(duration) || duration <= 0f
            || duration > clip.length + .001f)
            throw new ArgumentException("A Humanoid skill clip and an execution duration within it are required.");
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) throw new InvalidOperationException("Missing actual player model: " + ModelPath);
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject rig = null;
        var graph = default(PlayableGraph);
        try
        {
            rig = Object.Instantiate(model);
            SceneManager.MoveGameObjectToScene(rig, scene);
            rig.hideFlags = HideFlags.HideAndDontSave;
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            rig.transform.localScale = Vector3.one * PlayerModelScale;
            var animator = rig.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException("The player model needs its valid Humanoid Avatar.");
            animator.runtimeAnimatorController = null;
            animator.fireEvents = false;
            animator.applyRootMotion = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("Player Skill Motion Sampling");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            var output = AnimationPlayableOutput.Create(graph, "Skill", animator);
            output.SetSourcePlayable(playable);
            graph.Play();
            graph.Evaluate(0f);

            var forward = Vector3.forward;
            int count = Mathf.CeilToInt(duration * SampleRate);
            var sample = new MotionSample
            {
                Duration = duration,
                Times = new float[count + 1],
                ForwardPositions = new float[count + 1]
            };
            float signedDistance = 0f;
            for (int i = 1; i <= count; i++)
            {
                sample.Times[i] = Mathf.Min(i / SampleRate, duration);
                graph.Evaluate(sample.Times[i] - sample.Times[i - 1]);
                float delta = Vector3.Dot(animator.deltaPosition, forward);
                if (!Finite(delta)) throw new InvalidOperationException("Nonfinite skill root displacement.");
                signedDistance += delta;
                // A recoil and its return must not add travel. This is the forward envelope, not
                // the sum of positive deltas, which would turn recovery swaying into a slow drift.
                sample.ForwardPositions[i] = Mathf.Max(sample.ForwardPositions[i - 1], signedDistance);
            }
            sample.TotalDistance = sample.ForwardPositions[count];
            sample.Progress = CreateProgress(sample);
            return sample;
        }
        finally
        {
            if (graph.IsValid()) graph.Destroy();
            if (rig != null) Object.DestroyImmediate(rig);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static AnimationCurve CreateProgress(MotionSample sample)
    {
        int last = sample.Times.Length - 1;
        if (sample.TotalDistance <= 0f) return AnimationCurve.Linear(0f, 0f, 1f, 1f);
        var indices = new List<int> { 0 };
        void Reduce(int first, int end)
        {
            int worst = -1;
            float error = CurveErrorMetres;
            for (int i = first + 1; i < end; i++)
            {
                float t = (sample.Times[i] - sample.Times[first]) / (sample.Times[end] - sample.Times[first]);
                float deviation = Mathf.Abs(sample.ForwardPositions[i]
                    - Mathf.Lerp(sample.ForwardPositions[first], sample.ForwardPositions[end], t));
                if (deviation > error) { error = deviation; worst = i; }
            }
            if (worst < 0) { indices.Add(end); return; }
            Reduce(first, worst);
            Reduce(worst, end);
        }
        Reduce(0, last);
        var keys = indices.Select(i => new Keyframe(sample.Times[i] / sample.Duration,
            sample.ForwardPositions[i] / sample.TotalDistance)).ToArray();
        var curve = new AnimationCurve(keys);
        // Linear tangents retain plateaus and cannot overshoot the monotonic sampled envelope.
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
        }
        return curve;
    }

    private static AnimatorState FindState(AnimatorStateMachine machine, string name)
    {
        foreach (var child in machine.states)
            if (child.state.name == name) return child.state;
        foreach (var child in machine.stateMachines)
        {
            var found = FindState(child.stateMachine, name);
            if (found != null) return found;
        }
        return null;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static void RequireEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Skill motion baking and sampling require Edit Mode.");
    }
}
