using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    /// <summary>
    /// Hosts an arbitrary Unity editor preview without reducing preview support on Unity 2022.3.
    /// Retained previews are preferred when the upstream editor provides one.
    /// </summary>
    public static class CommonEditorPreviewBridge
    {
        public static VisualElement Create(UnityEditor.Editor editor, string elementName = null)
        {
            if (editor == null)
                return null;

#if UNITY_2023_2_OR_NEWER
            VisualElement previewWindow = new VisualElement();
            VisualElement retainedPreview = editor.CreatePreview(previewWindow);
            if (retainedPreview != null || previewWindow.childCount > 0)
            {
                if (retainedPreview != null &&
                    retainedPreview != previewWindow &&
                    retainedPreview.parent == null)
                {
                    previewWindow.Add(retainedPreview);
                }

                previewWindow.name = elementName;
                return previewWindow;
            }
#endif

            IMGUIContainer compatibilityPreview = new IMGUIContainer(() =>
            {
                if (editor == null || editor.target == null)
                    return;

                Rect previewRect = GUILayoutUtility.GetRect(
                    0f,
                    10000f,
                    0f,
                    10000f,
                    GUILayout.ExpandWidth(true),
                    GUILayout.ExpandHeight(true));
                editor.OnPreviewGUI(previewRect, EditorStyles.whiteLabel);
            })
            {
                name = elementName
            };
            return compatibilityPreview;
        }
    }
}
