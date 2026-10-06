using System;
using System.Reflection;
using ImpossibleRobert.Common;
using UnityEditor;

namespace AssetInventory
{
    internal static class UnityPackageImport
    {
        public static void Import(string archivePath, bool interactive, object assetOrigin)
        {
            Assembly assembly = typeof(AssetDatabase).Assembly;
            Type packageUtility = assembly.GetType("UnityEditor.AssetPackage.Utility") ?? typeof(AssetDatabase);
            Import(archivePath, interactive, assetOrigin, packageUtility, UnityEditorCompat.ImportPackage);
        }

        internal static void Import(string archivePath, bool interactive, object assetOrigin, Type packageUtility, Action<string, bool> fallback)
        {
            if (assetOrigin != null && packageUtility != null)
            {
                // Unity 6.6 moved the origin-aware wrapper out of AssetDatabase.
                // Let that wrapper select Unity's import flags instead of hard-coding enum values.
                MethodInfo importPackageMethod = packageUtility.GetMethod("ImportPackage", BindingFlags.NonPublic | BindingFlags.Static,
                    null, new[] {typeof(string), assetOrigin.GetType(), typeof(bool)}, null);
                if (importPackageMethod != null)
                {
                    importPackageMethod.Invoke(null, new[] {archivePath, assetOrigin, interactive});
                    return;
                }
            }

            // Missing internal APIs must never turn an import into a silent no-op.
            fallback(archivePath, interactive);
        }
    }
}
