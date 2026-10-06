using ImpossibleRobert.Common;

namespace AudioTool
{
    internal static class AudioToolsMenuPaths
    {
        public const string ToolsRoot = "Tools/Audio Tools/";
        public const string AudioEditor = ToolsRoot + "Audio Editor";
        public const string Welcome = ToolsRoot + "Welcome";
        public const string About = ToolsRoot + "About";

        public const int AudioEditorPriority = WetzoldToolMenu.AudioToolsRootPriority;
        public const int WelcomePriority = WetzoldToolMenu.WelcomePriority;
        public const int AboutPriority = WetzoldToolMenu.AboutPriority;
        public const int DebugPriority = WetzoldToolMenu.DebugPriority;
    }
}
