using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Temporary Game-view resolutions for visual review; restores the user's selection.</summary>
public static class BoundaryZeroReview
{
    private const string Key = "BoundaryZERO.Review.OriginalSize";
    private const string Label = "BoundaryZERO temporary review";
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static Type EditorType(string name) => typeof(Editor).Assembly.GetType("UnityEditor." + name, true);
    private static EditorWindow View()
    {
        var type = EditorType("GameView");
        return Resources.FindObjectsOfTypeAll(type).OfType<EditorWindow>().FirstOrDefault()
            ?? EditorWindow.GetWindow(type);
    }
    private static object Group()
    {
        var type = EditorType("GameViewSizes");
        var singleton = typeof(ScriptableSingleton<>).MakeGenericType(type)
            .GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
        return type.GetProperty("currentGroup", Flags).GetValue(singleton);
    }
    private static object Call(object group, string method, params object[] args)
        => group.GetType().GetMethod(method).Invoke(group, args);

    public static string SetResolution(int width, int height)
    {
        var view = View();
        var selected = view.GetType().GetProperty("selectedSizeIndex", Flags);
        if (SessionState.GetInt(Key, -1) < 0)
            SessionState.SetInt(Key, (int)selected.GetValue(view));
        var group = Group();
        var sizeType = EditorType("GameViewSize");
        int index = -1;
        int count = (int)Call(group, "GetTotalCount");
        for (int i = 0; i < count; i++)
        {
            var size = Call(group, "GetGameViewSize", i);
            if ((string)sizeType.GetProperty("baseText").GetValue(size) != Label) continue;
            sizeType.GetProperty("width").SetValue(size, width);
            sizeType.GetProperty("height").SetValue(size, height);
            index = i;
            break;
        }
        if (index < 0)
        {
            var fixedType = Enum.Parse(EditorType("GameViewSizeType"), "FixedResolution");
            var size = Activator.CreateInstance(sizeType, Flags, null,
                new object[] { fixedType, width, height, Label }, null);
            Call(group, "AddCustomSize", size);
            // IndexOf returns a custom-list index for custom sizes; the view uses the combined list.
            index = (int)Call(group, "GetTotalCount") - 1;
        }
        selected.SetValue(view, index);
        view.GetType().GetMethod("OnResized", Flags)?.Invoke(view, null);
        view.Repaint();
        EditorApplication.QueuePlayerLoopUpdate();
        return "Game view set to " + width + "x" + height + "; original selection retained.";
    }

    public static string RestoreResolution()
    {
        int original = SessionState.GetInt(Key, -1);
        if (original < 0) return "No review resolution to restore.";
        var view = View();
        view.GetType().GetProperty("selectedSizeIndex", Flags).SetValue(view, original);
        var group = Group();
        int count = (int)Call(group, "GetTotalCount");
        for (int i = count - 1; i >= 0; i--)
        {
            var size = Call(group, "GetGameViewSize", i);
            if ((string)size.GetType().GetProperty("baseText").GetValue(size) == Label
                && (bool)Call(group, "IsCustomSize", i))
                Call(group, "RemoveCustomSize", i);
        }
        SessionState.EraseInt(Key);
        view.Repaint();
        return "Original Game view resolution restored; temporary entry removed.";
    }
}
