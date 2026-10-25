using System;
using System.IO;
using UnityEditor;
using UnityEngine.InputSystem;

public static class FinisherInputSetup
{
    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Configure finisher input in Edit Mode.");
        const string path = "Assets/PlayerInputActions.inputactions";
        var source = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
        if (source == null) throw new FileNotFoundException(path);
        // Work on a clone so a failed write cannot leave the imported source half edited.
        var working = InputActionAsset.FromJson(source.ToJson());
        try
        {
            var map = working.FindActionMap("Player", true);
            var action = map.FindAction("FinisherSelect", false);
            if (action == null)
                action = map.AddAction("FinisherSelect", InputActionType.Button);
            action.expectedControlType = "Button";
            bool hasBinding = false;
            foreach (var binding in action.bindings)
                if (binding.path == "<Mouse>/rightButton") hasBinding = true;
            if (!hasBinding) action.AddBinding("<Mouse>/rightButton");
            if (source.ToJson() == working.ToJson()) return "FinisherSelect RMB binding already configured.";
            File.WriteAllText(path, working.ToJson());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return "Added Player/FinisherSelect RMB binding; existing bindings preserved.";
        }
        finally { UnityEngine.Object.DestroyImmediate(working); }
    }
}
