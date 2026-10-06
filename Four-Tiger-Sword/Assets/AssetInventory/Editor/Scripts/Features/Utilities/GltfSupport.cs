using System;
using System.Collections.Generic;
using System.IO;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace AssetInventory
{
    internal static class GltfSupport
    {
        internal const string GltfFastPackageName = "com.unity.cloud.gltfast";
        internal const string UnityGltfPackageName = "org.khronos.unitygltf";
        internal const string PackageName = GltfFastPackageName;
        internal const string MissingImporterMessage = "Install Unity glTFast (com.unity.cloud.gltfast) to preview glTF and GLB models, import them through Unity's model pipeline, and add them to scenes. Khronos UnityGLTF (org.khronos.unitygltf) is also supported when already present.";
        private static readonly string[] SupportedImporterPackageNames =
        {
            GltfFastPackageName,
            UnityGltfPackageName
        };

        internal static bool IsGltfType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return false;

            string normalized = type.Trim().TrimStart('.').ToLowerInvariant();
            return normalized == "gltf" || normalized == "glb";
        }

        internal static bool IsGltfFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            string extension = Path.GetExtension(path);
            return IsGltfType(extension);
        }

        internal static bool IsImporterInstalled()
        {
            for (int i = 0; i < SupportedImporterPackageNames.Length; i++)
            {
                PackageInfo packageInfo = PackageInfo.FindForAssetPath("Packages/" + SupportedImporterPackageNames[i]);
                if (packageInfo != null) return true;
            }

            Dictionary<string, PackageInfo> packages = AssetStore.GetProjectPackagesSync();
            return IsPackageInstalledFromNames(packages?.Keys);
        }

        internal static bool IsPackageInstalledFromNames(IEnumerable<string> packageNames)
        {
            if (packageNames == null) return false;

            foreach (string packageName in packageNames)
            {
                if (IsSupportedImporterPackageName(packageName)) return true;
            }

            return false;
        }

        internal static bool IsSupportedImporterPackageName(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName)) return false;

            for (int i = 0; i < SupportedImporterPackageNames.Length; i++)
            {
                if (string.Equals(packageName, SupportedImporterPackageNames[i], StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        internal static string GetDependencyTargetPath(string gltfPath, string dependencyPath, string targetRoot)
        {
            if (string.IsNullOrWhiteSpace(dependencyPath)) return targetRoot;

            string normalizedTargetRoot = NormalizePath(targetRoot).TrimEnd('/');
            string normalizedGltfPath = NormalizePath(gltfPath);
            string normalizedDependencyPath = NormalizePath(dependencyPath);
            string gltfDirectory = NormalizePath(Path.GetDirectoryName(normalizedGltfPath));
            string relativeDependencyPath = Path.GetFileName(normalizedDependencyPath);

            if (!string.IsNullOrEmpty(gltfDirectory))
            {
                string gltfDirectoryPrefix = gltfDirectory.TrimEnd('/') + "/";
                if (normalizedDependencyPath.StartsWith(gltfDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    relativeDependencyPath = normalizedDependencyPath.Substring(gltfDirectoryPrefix.Length);
                }
            }

            return CombinePath(normalizedTargetRoot, relativeDependencyPath);
        }

        private static string CombinePath(string root, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return root;
            if (string.IsNullOrEmpty(root)) return NormalizePath(relativePath);

            return NormalizePath(root.TrimEnd('/') + "/" + relativePath.TrimStart('/'));
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace("\\", "/");
        }
    }
}
