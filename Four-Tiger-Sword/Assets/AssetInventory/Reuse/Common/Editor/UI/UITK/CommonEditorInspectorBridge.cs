using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    /// <summary>
    /// Hosts an arbitrary Unity editor inside a retained visual tree.
    /// Unity's InspectorElement owns binding and any upstream IMGUI fallback.
    /// </summary>
    public static class CommonEditorInspectorBridge
    {
        public static VisualElement Create(UnityEditor.Editor editor, string elementName = null)
        {
            if (editor == null)
                return null;

            InspectorElement inspector = new InspectorElement(editor)
            {
                name = elementName
            };
            return inspector;
        }
    }
}
