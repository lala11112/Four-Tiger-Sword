using System.IO;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
#if UNITY_6000_7_OR_NEWER
    // The controller owns editor callbacks and intentionally persists for the loaded-code lifetime.
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public sealed partial class WelcomeWindow : BasicEditorUI
    {
        private const string PackageId = "com.wetzold.asset-inventory";
        private const int WelcomeRevision = 1;

        private static readonly CommonWelcomeController<WelcomeWindow> WelcomeController =
            new CommonWelcomeController<WelcomeWindow>(PackageId, WelcomeRevision, OpenWindow);

        [InitializeOnLoadMethod]
        private static void ScheduleAutomaticWelcome()
        {
            WelcomeController.ScheduleAutomaticOpen();
        }

        public static void ShowWindow()
        {
            WelcomeController.OpenManually();
        }

        private static WelcomeWindow OpenWindow()
        {
            return CommonWelcomeWindow.ShowUtility<WelcomeWindow>("Welcome to Asset Inventory");
        }

        private void OnEnable()
        {
            CommonWelcomeWindow.ApplyDefaultConstraints(this, "Welcome to Asset Inventory");
        }

        private void CreateGUI()
        {
            CommonWelcomeContent content = new CommonWelcomeContent
            {
                Logo = CommonUIStyles.LoadTexture("AssetInventory"),
                ProductName = "ASSET INVENTORY",
                Headline = "Find any asset, right when you need it",
                Description =
                    "Manage, search, and organize Asset Store packages, project content, and local files from one focused Unity workspace.",
                SectionTitle = "Build a searchable asset library",
                SectionDescription =
                    "Bring your sources together, find the right content quickly, and keep a growing collection understandable.",
                Steps = new[]
                {
                    new CommonWelcomeStep(
                        "Bring your assets together",
                        "Launch Asset Inventory and choose the folders, packages, and project content that belong in your working library."),
                    new CommonWelcomeStep(
                        "Search the complete collection",
                        "Use previews, metadata, package context, and focused filters to narrow a large library to the right asset."),
                    new CommonWelcomeStep(
                        "Organize and maintain",
                        "Keep useful context close to each asset and use the maintenance tools to keep the library dependable over time.")
                }
            };

            Button launch = CommonWelcomeWindow.CreateAction(
                "Launch Asset Inventory",
                "Open the Asset Inventory workspace.",
                MenuIntegration.ShowWindow,
                true);
            Button documentation = CommonWelcomeWindow.CreateAction(
                "Read Documentation",
                "Open the Asset Inventory documentation.",
                OpenLocalDocsPdf);
            Button community = CommonWelcomeWindow.CreateAction(
                "Join Community",
                "Open the Wetzold Studios community.",
                () => AI.OpenURL(AI.DISCORD_LINK));
            CommonWelcomeWindow.Build(rootVisualElement, content, launch, documentation, community);
        }

        private static void OpenLocalDocsPdf()
        {
            // Open Documentation/Documentation.pdf relative to the installed tool folder; fallback to online docs
            string projectRoot = Path.GetDirectoryName(Application.dataPath);

            // Resolve this script's asset path
            string scriptAssetPath = null;
            try
            {
                WelcomeWindow temp = CreateInstance<WelcomeWindow>();
                MonoScript ms = MonoScript.FromScriptableObject(temp);
                scriptAssetPath = AssetDatabase.GetAssetPath(ms);
                DestroyImmediate(temp);
            }
            catch
            {
                // ignored
            }

            if (!string.IsNullOrEmpty(scriptAssetPath))
            {
                // Convert to full filesystem path
                string full = Path.GetFullPath(Path.Combine(projectRoot, scriptAssetPath));
                string dir = Path.GetDirectoryName(full);

                // Walk up a few levels to find a Documentation/Documentation.pdf next to the tool root
                for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
                {
                    string candidate = Path.Combine(dir, "Documentation", "Documentation.pdf");
                    if (File.Exists(candidate))
                    {
                        EditorUtility.OpenWithDefaultApp(candidate);
                        return;
                    }
                    dir = Path.GetDirectoryName(dir);
                }
            }

            AI.OpenURL(AI.HOME_LINK);
        }
    }
}
