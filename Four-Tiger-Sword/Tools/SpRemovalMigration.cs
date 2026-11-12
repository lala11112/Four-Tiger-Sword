using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Reserializes only resource-bearing project data after the SP API removal.</summary>
public static class SpRemovalMigration
{
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run the data migration in Edit Mode.");
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (string filter in new[] { "t:PlayerStatData", "t:WeaponActionDataSO", "t:ItemCatalog" })
            foreach (string guid in AssetDatabase.FindAssets(filter, new[] { "Assets/_Project" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".asset", StringComparison.Ordinal)) paths.Add(path);
            }
        AssetDatabase.ForceReserializeAssets(paths, ForceReserializeAssetsOptions.ReserializeAssets);
        return "Reserialized " + paths.Count + " player, form, and item catalog assets without retired resource fields.";
    }
}
