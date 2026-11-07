using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Explicit installation of the two reactions used by the sword robot's final combo hit.</summary>
public static class PlayerKnockdownAnimationSetup
{
    public const string ControllerPath = "Assets/_Project/Animations/AnimController/BDJ.controller";
    public const string Folder = "Assets/_Project/Generated/Bosses/SwordRobotBoss/PlayerReactions";
    private const string Sources = "Assets/Nephilite Studios/Straight Sword Animation Set/Animations/Straight Sword Animations/Nephilim Animations (Remastered)/Straight Sword Critical Strike/";

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Player knockdown animation installation requires Edit Mode.");
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null || controller.layers.Length == 0)
            throw new InvalidOperationException("The live player Animator controller is missing: " + ControllerPath);
        var downSource = LoadClip(Sources + "core_riposte_death_01.fbx");
        var upSource = LoadClip(Sources + "core_main_riposte_getup_01.fbx");
        EnsureFolder(Folder);
        var down = CopyClip(downSource, Folder + "/Player_Knockdown.anim");
        var up = CopyClip(upSource, Folder + "/Player_GetUp.anim");
        var machine = controller.layers[0].stateMachine;
        int changed = EnsureState(machine, "Knockdown", down, new Vector3(650f, 300f, 0f))
            + EnsureState(machine, "GetUp", up, new Vector3(650f, 370f, 0f));
        if (changed > 0)
        {
            EditorUtility.SetDirty(machine);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
        }
        return $"Player down/get-up reactions ready; {changed} states initialized in {ControllerPath}.";
    }

    private static AnimationClip LoadClip(string path)
    {
        var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .FirstOrDefault(value => !value.name.StartsWith("__preview__", StringComparison.Ordinal));
        if (clip == null || !clip.humanMotion)
            throw new InvalidOperationException("A humanoid player reaction clip is required: " + path);
        return clip;
    }

    private static AnimationClip CopyClip(AnimationClip source, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null) return existing;
        var copy = Object.Instantiate(source);
        copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
        AnimationUtility.SetAnimationEvents(copy, Array.Empty<AnimationEvent>());
        var settings = AnimationUtility.GetAnimationClipSettings(copy);
        settings.loopTime = false;
        settings.loopBlend = false;
        settings.keepOriginalOrientation = true;
        settings.keepOriginalPositionY = true;
        settings.keepOriginalPositionXZ = true;
        settings.loopBlendOrientation = true;
        settings.loopBlendPositionY = true;
        settings.loopBlendPositionXZ = true;
        AnimationUtility.SetAnimationClipSettings(copy, settings);
        AssetDatabase.CreateAsset(copy, path);
        AssetDatabase.SaveAssetIfDirty(copy);
        return copy;
    }

    private static int EnsureState(AnimatorStateMachine machine, string name, AnimationClip clip, Vector3 position)
    {
        var state = machine.states.FirstOrDefault(child => child.state.name == name).state;
        if (state != null && state.motion != null) return 0;
        if (state == null) state = machine.AddState(name, position);
        Undo.RecordObject(state, "Install player knockdown reaction");
        state.motion = clip;
        state.speed = 1f;
        state.writeDefaultValues = true;
        EditorUtility.SetDirty(state);
        return 1;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        int separator = folder.LastIndexOf('/');
        string parent = folder.Substring(0, separator);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folder.Substring(separator + 1));
    }
}
