using ImpossibleRobert.Common;
using UnityEditor;

namespace AudioTool
{
    internal static class AudioToolsAbout
    {
        [MenuItem(AudioToolsMenuPaths.About, false, AudioToolsMenuPaths.AboutPriority)]
        private static void ShowAbout()
        {
            AboutWindow.Show("AudioTools");
        }
    }
}
