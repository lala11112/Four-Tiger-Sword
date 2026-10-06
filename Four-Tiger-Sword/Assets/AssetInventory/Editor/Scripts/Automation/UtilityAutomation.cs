using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace AssetInventory.Automation
{
    internal static class UtilityAutomation
    {
        internal sealed class CheckAssetRequest
        {
            public string Guid { get; set; }
        }

        internal static AutomationResponse GetInventoryStats()
        {
            AutomationResponse initError = AutomationResultHelper.EnsureInit();
            if (initError != null) return initError;

            InventoryStats stats = Assets.GetInventoryStats();
            stats.DatabaseSize = DBAdapter.GetDBSize();
            return AutomationResponse.Success($"Inventory contains {stats.TotalPackages} packages ({stats.IndexedPackages} indexed) with {stats.TotalFiles} files.", stats);
        }

        internal static AutomationResponse OpenAssetInventory()
        {
            EditorWindow.GetWindow<IndexUI>("Asset Inventory");
            return AutomationResponse.Success("Asset Inventory window opened.");
        }

        internal static AutomationResponse CloseAssetInventory()
        {
            IndexUI[] windows = UnityEngine.Resources.FindObjectsOfTypeAll<IndexUI>();
            if (windows.Length == 0)
            {
                return AutomationResponse.Success("Asset Inventory window was not open.");
            }

            foreach (IndexUI window in windows)
            {
                window.Close();
            }
            return AutomationResponse.Success("Asset Inventory window closed.");
        }

        internal static AutomationResponse CheckAssetInProject(CheckAssetRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Guid))
            {
                return AutomationResponse.Error("GUID is required.", errorCode: "invalid_input");
            }

            string projectPath = AssetDatabase.GUIDToAssetPath(request.Guid);
            bool exists = !string.IsNullOrEmpty(projectPath) && File.Exists(projectPath);
            return AutomationResponse.Success(exists ? $"Asset found at '{projectPath}'." : "Asset not found in project.", new
            {
                exists,
                projectPath = exists ? projectPath : null
            });
        }

        internal static AutomationResponse GetAssetGroupTypes()
        {
            Dictionary<string, string[]> groups = AI.TypeGroups.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value);
            return AutomationResponse.Success($"Found {groups.Count} asset group types.", new {groups});
        }
    }
}
