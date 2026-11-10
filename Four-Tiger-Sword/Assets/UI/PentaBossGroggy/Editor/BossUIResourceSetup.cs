using TMPro;
using UnityEditor;
using UnityEngine;

namespace Penta.BossUI.Editor
{
    internal static class BossUIResourceSetup
    {
        [InitializeOnLoadMethod]
        private static void ScheduleSetup()
        {
            EditorApplication.delayCall += EnsureEssentials;
        }

        private static void EnsureEssentials()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += EnsureEssentials;
                return;
            }
            if (TMP_Settings.LoadDefaultSettings()) return;

            AssetDatabase.importPackageCompleted += OnImported;
            AssetDatabase.importPackageFailed += OnFailed;
            TMP_PackageResourceImporter.ImportResources(true, false, false);
        }

        private static void OnImported(string packageName)
        {
            if (packageName != "TMP Essential Resources") return;
            Unsubscribe();
            if (TMP_Settings.LoadDefaultSettings())
                TMPro_EventManager.ON_RESOURCES_LOADED();
        }

        private static void OnFailed(string packageName, string error)
        {
            if (packageName != "TMP Essential Resources") return;
            Unsubscribe();
            Debug.LogError("Boss UI requires TMP Essential Resources. Import them from the TextMeshPro menu. " + error);
        }

        private static void Unsubscribe()
        {
            AssetDatabase.importPackageCompleted -= OnImported;
            AssetDatabase.importPackageFailed -= OnFailed;
        }
    }
}
