using TMPro;
using UnityEditor;
using UnityEngine;

namespace BoundaryZero.SupplyDepot.Editor
{
    // A fresh Unity project has the TMP runtime package but may not yet have its
    // standard Resources assets. Reuse the installed package's canonical importer.
    public static class SupplyDepotTextResources
    {
        private static bool importRequested;

        [InitializeOnLoadMethod]
        private static void Schedule()
        {
            EditorApplication.delayCall += Ensure;
        }

        private static void Ensure()
        {
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Ensure;
                return;
            }
            if (Resources.Load<TMP_Settings>("TMP Settings") != null || importRequested)
                return;
            importRequested = true;
            TMP_PackageResourceImporter.ImportResources(true, false, false);
        }
    }
}
