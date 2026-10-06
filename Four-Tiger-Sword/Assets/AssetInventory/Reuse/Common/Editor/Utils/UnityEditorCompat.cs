using UnityEditor;
using UnityEngine;

namespace ImpossibleRobert.Common
{
    /// <summary>
    /// Version-safe wrappers for Object identity APIs deprecated in Unity 6.4+.
    /// Companion to <see cref="UnityUtils"/> (runtime) for editor code.
    /// </summary>
    public static class UnityEditorCompat
    {
        /// <summary>
        /// Version-safe wrapper for AssetPreview.IsLoadingAssetPreview.
        /// Uses the EntityId overload on Unity 6.3+ and the int overload on older versions.
        /// </summary>
        public static bool IsLoadingPreview(Object obj)
        {
#if UNITY_6000_3_OR_NEWER
            return AssetPreview.IsLoadingAssetPreview(obj.GetEntityId());
#else
            return AssetPreview.IsLoadingAssetPreview(obj.GetInstanceID());
#endif
        }

        /// <summary>
        /// Version-safe wrapper for importing a Unity package.
        /// Uses the AssetPackage API on Unity 6.7+ and AssetDatabase on older versions.
        /// </summary>
        public static void ImportPackage(string packagePath, bool interactive)
        {
#if UNITY_6000_7_OR_NEWER
            UnityEditor.AssetPackage.Package.Import(packagePath, interactive);
#else
            AssetDatabase.ImportPackage(packagePath, interactive);
#endif
        }
    }
}
