namespace ImpossibleRobert.Common
{
    public interface IImageUtilsBackend
    {
        bool TryResizeImage(string originalFile, string outputFile, int maxSize, bool scaleBeyondSize, string typeOverride);
        bool TryGetDimensions(string file, out int width, out int height);
        bool TryComputePerceptualHash(string filePath, int hashSize, out ulong hash);
    }
}
