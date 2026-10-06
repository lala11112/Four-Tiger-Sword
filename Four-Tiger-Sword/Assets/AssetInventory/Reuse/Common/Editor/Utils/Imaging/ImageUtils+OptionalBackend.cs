#if !UNITY_EDITOR_WIN || !NET_4_6
namespace ImpossibleRobert.Common
{
    public static partial class ImageUtils
    {
        public static bool ResizeImage(string originalFile, string outputFile, int maxSize, bool scaleBeyondSize = true, string typeOverride = null)
        {
#if UNITY_EDITOR_OSX
            if (TryResizeImageNative(originalFile, outputFile, maxSize, scaleBeyondSize, typeOverride))
            {
                return true;
            }
#endif
            if (TryResizeWithRegisteredBackend(originalFile, outputFile, maxSize, scaleBeyondSize, typeOverride))
            {
                return true;
            }

            LogMissingImageBackend("resize image", originalFile);
            return false;
        }

        public static ulong ComputePerceptualHash(string filePath, int hashSize = 8)
        {
#if UNITY_EDITOR_OSX
            if (TryComputePerceptualHashNative(filePath, out ulong nativeHash, hashSize))
            {
                return nativeHash;
            }
#endif
            if (TryComputePerceptualHashWithRegisteredBackend(filePath, hashSize, out ulong hash))
            {
                return hash;
            }

            LogMissingImageBackend("compute perceptual hash for", filePath);
            return 0UL;
        }

        public static bool AreSimilar(string fileA, string fileB, double maxFractionDifferent = 0.15)
        {
            ulong hashA = ComputePerceptualHash(fileA);
            ulong hashB = ComputePerceptualHash(fileB);
            int dist = HammingDistance(hashA, hashB);
            double fraction = (double)dist / (8 * 8);
            return fraction <= maxFractionDifferent;
        }
    }
}
#endif
