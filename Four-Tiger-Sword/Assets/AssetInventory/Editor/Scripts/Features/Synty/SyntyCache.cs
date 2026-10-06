using System;
using System.IO;

namespace AssetInventory
{
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    internal static partial class SyntyCache
    {
        private const long MinimumPackageSize = 1024;
        private static string _rootOverride;

        internal static string Root
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_rootOverride)) return _rootOverride;
                string configuredRoot = AI.Config?.syntyCacheFolder;
                return string.IsNullOrWhiteSpace(configuredRoot) ? DefaultRoot : configuredRoot;
            }
        }

        internal static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Synty", "Downloads");

        internal static string GetPackagePath(string authoritativeFileName)
        {
            if (string.IsNullOrWhiteSpace(authoritativeFileName)) return null;

            string fileName = Path.GetFileName(authoritativeFileName.Trim());
            if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(fileName, authoritativeFileName.Trim(), StringComparison.Ordinal)) return null;

            string root = Path.GetFullPath(Root);
            string candidate = Path.GetFullPath(Path.Combine(root, fileName));
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

            return candidate.Replace("\\", "/");
        }

        internal static bool IsValidPackage(string path, long expectedLength = -1)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
                FileInfo info = new FileInfo(path);
                if (info.Length < MinimumPackageSize) return false;
                if (expectedLength > 0 && info.Length != expectedLength) return false;

                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    return stream.ReadByte() == 0x1f && stream.ReadByte() == 0x8b;
                }
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsCanonicalCachePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string canonical = GetPackagePath(Path.GetFileName(path));
            return !string.IsNullOrWhiteSpace(canonical) && Paths.AreEquivalentPaths(canonical, path);
        }

        internal static string ReadVersion(string packagePath)
        {
            try
            {
                string path = packagePath + ".ver";
                return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        internal static bool IsImporterPartialActive(string finalPath)
        {
            try
            {
                string partial = finalPath + ".part";
                return File.Exists(partial) && DateTime.UtcNow - File.GetLastWriteTimeUtc(partial) < TimeSpan.FromMinutes(10);
            }
            catch
            {
                return false;
            }
        }

        internal static string NormalizeVersion(string version)
        {
            return string.IsNullOrWhiteSpace(version) ? string.Empty : version.Trim().TrimStart('v', 'V').Replace('_', '.');
        }

        internal static void SetRootOverrideForTests(string root)
        {
            _rootOverride = root;
        }
    }
}
