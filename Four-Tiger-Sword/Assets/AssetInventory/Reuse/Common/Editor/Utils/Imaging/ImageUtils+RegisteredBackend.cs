using UnityEngine;

namespace ImpossibleRobert.Common
{
#if UNITY_6000_7_OR_NEWER
    // Backend registration and immutable image metadata are editor code-load scoped and must persist through Play Mode.
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static partial class ImageUtils
    {
        private static IImageUtilsBackend _registeredBackend;

        public static void RegisterBackend(IImageUtilsBackend backend)
        {
            _registeredBackend = backend;
        }

        internal static bool TryResizeWithRegisteredBackend(string originalFile, string outputFile, int maxSize, bool scaleBeyondSize, string typeOverride)
        {
            return _registeredBackend != null &&
                   _registeredBackend.TryResizeImage(originalFile, outputFile, maxSize, scaleBeyondSize, typeOverride);
        }

        internal static bool TryGetRegisteredImageDimensions(string file, out int width, out int height)
        {
            width = 0;
            height = 0;
            return _registeredBackend != null && _registeredBackend.TryGetDimensions(file, out width, out height);
        }

        internal static bool TryComputePerceptualHashWithRegisteredBackend(string filePath, int hashSize, out ulong hash)
        {
            hash = 0UL;
            return _registeredBackend != null && _registeredBackend.TryComputePerceptualHash(filePath, hashSize, out hash);
        }

        internal static void LogMissingImageBackend(string operation, string filePath)
        {
            if (!LogImageOperations)
            {
                return;
            }

            Debug.LogWarning($"Could not {operation} '{filePath}': install Common Imaging to enable optional image processing.");
        }
    }
}
