using System;
using System.Collections.Generic;
using System.IO;
using ImpossibleRobert.Common;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using UnityEditor;
using UnityEngine;
using Color = UnityEngine.Color;

namespace ImpossibleRobert.Common.Imaging
{
    [InitializeOnLoad]
    internal sealed class ImageSharpImageUtilsBackend : IImageUtilsBackend
    {
        static ImageSharpImageUtilsBackend()
        {
            ImageUtils.RegisterBackend(new ImageSharpImageUtilsBackend());
        }

        public bool TryResizeImage(string originalFile, string outputFile, int maxSize, bool scaleBeyondSize, string typeOverride)
        {
            return ImageSharpImageUtils.ResizeImage(originalFile, outputFile, maxSize, scaleBeyondSize, typeOverride);
        }

        public bool TryGetDimensions(string file, out int width, out int height)
        {
            width = 0;
            height = 0;

            try
            {
                IImageInfo imageInfo = Image.Identify(IOUtils.ToLongPath(file));
                if (imageInfo == null)
                {
                    return false;
                }

                width = imageInfo.Width;
                height = imageInfo.Height;
                return true;
            }
            catch (Exception e)
            {
                if (ImageUtils.LogImageOperations)
                {
                    Debug.LogWarning($"Could not determine image dimensions for '{file}': {e.Message}");
                }
                return false;
            }
        }

        public bool TryComputePerceptualHash(string filePath, int hashSize, out ulong hash)
        {
            hash = 0UL;

            try
            {
                hash = ImageSharpImageUtils.ComputePerceptualHash(filePath, hashSize);
                return true;
            }
            catch (Exception e)
            {
                if (ImageUtils.LogImageOperations)
                {
                    Debug.LogWarning($"Could not compute image hash for '{filePath}': {e.Message}");
                }
                return false;
            }
        }
    }

    public static class ImageSharpImageUtils
    {
        public static bool ResizeImage(string originalFile, string outputFile, int maxSize, bool scaleBeyondSize = true, string typeOverride = null)
        {
            Image originalImage = null;
            try
            {
                ImageUtils.ResizeParams? paramsResult = ImageUtils.CalculateResizeParams(
                    originalFile, outputFile, maxSize, scaleBeyondSize, typeOverride,
                    () =>
                    {
                        originalImage = Image.Load(IOUtils.ToLongPath(originalFile));
                        return (originalImage.Width, originalImage.Height);
                    },
                    out bool fileCopied);

                if (fileCopied) return true;
                if (paramsResult == null) return false;

                ImageUtils.ResizeParams resizeParams = paramsResult.Value;
                originalImage ??= Image.Load(IOUtils.ToLongPath(originalFile));
                originalImage.Mutate(x => x.Resize(resizeParams.NewWidth, resizeParams.NewHeight));
                originalImage.SaveAsPng(IOUtils.ToLongPath(outputFile));
            }
            catch (Exception e)
            {
                if (ImageUtils.LogImageOperations)
                {
                    Debug.LogWarning($"Could not resize image '{originalFile}': {e.Message}");
                }
                return false;
            }
            finally
            {
                originalImage?.Dispose();
            }

            return true;
        }

        public static bool HasDominantColor(Image<Rgba32> image, Color target, float marginPercent = 0.02f, float coverageThreshold = 0.3f)
        {
            int width = image.Width;
            int height = image.Height;
            int total = width * height;
            int matchCount = 0;
            int targetR = Mathf.RoundToInt(target.r * 255f);
            int targetG = Mathf.RoundToInt(target.g * 255f);
            int targetB = Mathf.RoundToInt(target.b * 255f);
            int marginR = (int)Math.Ceiling(targetR * marginPercent);
            int marginG = (int)Math.Ceiling(targetG * marginPercent);
            int marginB = (int)Math.Ceiling(targetB * marginPercent);

            image.ProcessPixelRows(pixelAccessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    Span<Rgba32> row = pixelAccessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        Rgba32 p = row[x];
                        if (Math.Abs(p.R - targetR) <= marginR &&
                            Math.Abs(p.G - targetG) <= marginG &&
                            Math.Abs(p.B - targetB) <= marginB)
                        {
                            matchCount++;
                        }
                    }
                }
            });

            return matchCount > total * coverageThreshold;
        }

        public static bool IsErrorPreview(Image<Rgba32> image, float requiredRatio = 0.06f)
        {
            int width = image.Width;
            int height = image.Height;
            int total = width * height;
            int pinkCount = 0;

            image.ProcessPixelRows(pixelAccessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    Span<Rgba32> row = pixelAccessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        Rgba32 c = row[x];
                        if (ImageUtils.IsMagentaPixel(c.R, c.G, c.B))
                        {
                            pinkCount++;
                        }
                    }
                }
            });

            return (float)pinkCount / total >= requiredRatio;
        }

        public static bool IsLowDiversityPinkPreview(Image<Rgba32> image, int maxDistinctColors = 20)
        {
            int width = image.Width;
            int height = image.Height;
            HashSet<(byte, byte, byte)> buckets = new HashSet<(byte, byte, byte)>();
            bool hasPink = false;

            image.ProcessPixelRows(pixelAccessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    Span<Rgba32> row = pixelAccessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        Rgba32 c = row[x];
                        buckets.Add(((byte)(c.R >> 3), (byte)(c.G >> 3), (byte)(c.B >> 3)));
                        if (!hasPink && ImageUtils.IsMagentaPixel(c.R, c.G, c.B))
                        {
                            hasPink = true;
                        }
                    }
                }
            });

            return hasPink && buckets.Count <= maxDistinctColors;
        }

        public static ulong ComputePerceptualHash(string filePath, int hashSize = 8)
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(IOUtils.ToLongPath(filePath));
            return ComputePerceptualHash(image, hashSize);
        }

        public static ulong ComputePerceptualHash(Image<Rgba32> image, int hashSize = 8)
        {
            using Image<Rgba32> clone = image.Clone(ctx => ctx.Resize(hashSize, hashSize).Grayscale());
            ulong hash = 0UL;
            double sum = 0.0;
            double[] pixels = new double[hashSize * hashSize];
            int idx = 0;
            for (int y = 0; y < hashSize; y++)
            {
                for (int x = 0; x < hashSize; x++)
                {
                    double l = clone[x, y].R;
                    pixels[idx++] = l;
                    sum += l;
                }
            }

            double avg = sum / pixels.Length;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i] > avg) hash |= 1UL << i;
            }
            return hash;
        }

        public static bool AreSimilar(string fileA, string fileB, double maxFractionDifferent = 0.15)
        {
            ulong hashA = ComputePerceptualHash(fileA);
            ulong hashB = ComputePerceptualHash(fileB);
            int dist = ImageUtils.HammingDistance(hashA, hashB);
            double fraction = (double)dist / (8 * 8);
            return fraction <= maxFractionDifferent;
        }

        public static bool AreSimilar(Image<Rgba32> imgA, Image<Rgba32> imgB, double maxFractionDifferent = 0.15)
        {
            ulong hashA = ComputePerceptualHash(imgA);
            ulong hashB = ComputePerceptualHash(imgB);
            int dist = ImageUtils.HammingDistance(hashA, hashB);
            double fraction = (double)dist / (8 * 8);
            return fraction <= maxFractionDifferent;
        }

        public static bool AreSimilar(Image<Rgba32> imgA, ulong hashB, double maxFractionDifferent = 0.15)
        {
            ulong hashA = ComputePerceptualHash(imgA);
            int dist = ImageUtils.HammingDistance(hashA, hashB);
            double fraction = (double)dist / (8 * 8);
            return fraction <= maxFractionDifferent;
        }

        public static Image<Rgba32> ToImage(Texture2D tex)
        {
            byte[] pngData = tex.EncodeToPNG();
            return Image.Load<Rgba32>(pngData);
        }
    }
}
