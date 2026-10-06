using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public abstract class BasicEditorUI : EditorWindow
    {
        public static Texture2D Logo => AssetInventoryUITK.Logo;

        protected static bool ShowAdvanced()
        {
            return AI.ShowAdvanced();
        }
    }
}
