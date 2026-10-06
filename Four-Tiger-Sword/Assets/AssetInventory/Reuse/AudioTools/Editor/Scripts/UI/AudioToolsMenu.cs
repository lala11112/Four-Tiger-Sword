using UnityEditor;

namespace AudioTool
{
    internal static class AudioToolsMenu
    {
        [MenuItem(AudioToolsMenuPaths.AudioEditor, false, AudioToolsMenuPaths.AudioEditorPriority)]
        private static void ShowAudioEditor()
        {
            AudioEditorUI.ShowWindow();
        }
    }
}
