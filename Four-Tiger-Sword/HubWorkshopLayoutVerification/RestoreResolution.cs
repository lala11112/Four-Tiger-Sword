var assembly = typeof(UnityEditor.EditorWindow).Assembly;
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var gameViewType = assembly.GetType("UnityEditor.GameView");
var gameView = UnityEngine.Resources.FindObjectsOfTypeAll(gameViewType).First();
int original = UnityEditor.SessionState.GetInt("HubLayout.OriginalGameViewIndex", 0);
gameViewType.GetMethod("SizeSelectionCallback", flags).Invoke(gameView, new object[] { original, null });
var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
var sizes = sizesType.BaseType.GetProperty("instance", flags).GetValue(null);
var group = sizesType.GetMethod("GetGroup", flags).Invoke(sizes, new object[] { System.Enum.Parse(assembly.GetType("UnityEditor.GameViewSizeGroupType"), "Standalone") });
var groupType = group.GetType();
int total = (int)groupType.GetMethod("GetTotalCount", flags).Invoke(group, null);
int index = UnityEditor.SessionState.GetInt("HubLayout.CustomIndex", -1);
bool removed = false;
if (index >= 0 && index == total - 1)
{
    var size = groupType.GetMethod("GetGameViewSize", flags).Invoke(group, new object[] { index });
    if ((string)size.GetType().GetProperty("baseText", flags).GetValue(size) == "HubWorkshopLayoutQA")
    {
        var remove = groupType.GetMethod("RemoveCustomSize", flags);
        try { remove.Invoke(group, new object[] { index }); }
        catch (System.Reflection.TargetInvocationException error)
        {
            if (!(error.InnerException is System.ArgumentOutOfRangeException)) throw;
        }
        if ((int)groupType.GetMethod("GetTotalCount", flags).Invoke(group, null) == total)
        {
            int builtin = (int)groupType.GetMethod("GetBuiltinCount", flags).Invoke(group, null);
            remove.Invoke(group, new object[] { index - builtin });
        }
        removed = (int)groupType.GetMethod("GetTotalCount", flags).Invoke(group, null) == total - 1;
    }
}
((UnityEditor.EditorWindow)gameView).Repaint();
return new { original, removed };
