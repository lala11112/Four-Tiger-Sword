var assembly = typeof(UnityEditor.EditorWindow).Assembly;
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var gameViewType = assembly.GetType("UnityEditor.GameView");
var gameView = UnityEngine.Resources.FindObjectsOfTypeAll(gameViewType).First();
var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
var sizes = sizesType.BaseType.GetProperty("instance", flags).GetValue(null);
var group = sizesType.GetMethod("GetGroup", flags).Invoke(sizes, new object[] { System.Enum.Parse(assembly.GetType("UnityEditor.GameViewSizeGroupType"), "Standalone") });
var groupType = group.GetType();
int total = (int)groupType.GetMethod("GetTotalCount", flags).Invoke(group, null);
var sizeType = assembly.GetType("UnityEditor.GameViewSize");
int index = -1;
object size = null;
for (int i = 0; i < total; i++)
{
    var candidate = groupType.GetMethod("GetGameViewSize", flags).Invoke(group, new object[] { i });
    if ((string)sizeType.GetProperty("baseText", flags).GetValue(candidate) == "HubWorkshopLayoutQA")
    { index = i; size = candidate; break; }
}
int width = UnityEditor.SessionState.GetInt("HubLayout.Width", 1280);
int height = UnityEditor.SessionState.GetInt("HubLayout.Height", 720);
if (size == null)
{
    var fixedResolution = System.Enum.Parse(assembly.GetType("UnityEditor.GameViewSizeType"), "FixedResolution");
    size = System.Activator.CreateInstance(sizeType, flags, null, new object[] { fixedResolution, width, height, "HubWorkshopLayoutQA" }, null);
    groupType.GetMethod("AddCustomSize", flags).Invoke(group, new[] { size });
    index = total;
}
sizeType.GetProperty("width", flags).SetValue(size, width);
sizeType.GetProperty("height", flags).SetValue(size, height);
UnityEditor.SessionState.SetInt("HubLayout.CustomIndex", index);
gameViewType.GetMethod("SizeSelectionCallback", flags).Invoke(gameView, new object[] { index, null });
((UnityEditor.EditorWindow)gameView).Repaint();
return new { width, height, index };
