using ImpossibleRobert.Common;
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AssetInventory
{
    /// <summary>
    /// Validation and error detection utilities for preview generation.
    /// Consolidates validation logic from PreviewManager and CustomPrefabPreviewGenerator.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static partial class PreviewValidation
    {
        private const byte AlphaVisibleThreshold = 8;
        private const int BackgroundTolerance = 12;
        private const int MinimumPinkVisiblePixels = 4;
        private const float PinkVisibleMajorityThreshold = 0.5f;

        private static ulong _textureIconHash;
        private static ulong _audioIconHash;

        public static bool IsErrorShader(string imagePath)
        {
            return EvaluateTextureFile(imagePath, IsErrorShader);
        }

        public static bool IsDefaultIcon(string imagePath)
        {
            return EvaluateTextureFile(imagePath, IsDefaultIcon);
        }

        public static bool IsErrorShader(Texture2D image)
        {
            if (image == null) return false;

            Texture2D readable = image.MakeReadable();
            try
            {
                int width = readable.width;
                int height = readable.height;
                Color32[] pixels = readable.GetPixels32();
                ColorRange background = FindBackgroundRange(pixels, width, height);
                int visiblePixels = 0;
                int pinkPixels = 0;

                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 color = pixels[i];
                    if (IsBackground(color.r, color.g, color.b, color.a, background)) continue;

                    visiblePixels++;
                    if (ImageUtils.IsMagentaPixel(color.r, color.g, color.b)) pinkPixels++;
                }

                return IsPinkErrorForeground(visiblePixels, pinkPixels);
            }
            finally
            {
                Object.DestroyImmediate(readable);
            }
        }

        public static bool IsDefaultIcon(Texture2D image)
        {
            if (image == null) return false;

            if (_textureIconHash == 0)
            {
                _textureIconHash = ImageUtils.ComputePerceptualHash((Texture2D)EditorGUIUtility.IconContent("d_texture icon").image);
            }
            if (_audioIconHash == 0)
            {
                _audioIconHash = ImageUtils.ComputePerceptualHash((Texture2D)EditorGUIUtility.IconContent("audioclip icon").image);
            }

            return ImageUtils.HasDominantColor(image, new UnityEngine.Color(128f / 255f, 216f / 255f, 255f / 255f))
                || ImageUtils.AreSimilar(image, _textureIconHash)
                || ImageUtils.AreSimilar(image, _audioIconHash);
        }

        private static bool EvaluateTextureFile(string imagePath, Func<Texture2D, bool> predicate)
        {
            Texture2D texture = null;
            try
            {
                texture = LoadTexture(imagePath);
                return predicate(texture);
            }
            finally
            {
                if (texture != null)
                {
                    Object.DestroyImmediate(texture);
                }
            }
        }

        private static Texture2D LoadTexture(string imagePath)
        {
            byte[] bytes = File.ReadAllBytes(IOUtils.ToLongPath(imagePath));
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (texture.LoadImage(bytes, false))
            {
                return texture;
            }

            Object.DestroyImmediate(texture);
            throw new InvalidDataException("Preview image could not be decoded.");
        }

        private static bool IsPinkErrorForeground(int visiblePixels, int pinkPixels)
        {
            if (visiblePixels <= 0 || pinkPixels < MinimumPinkVisiblePixels) return false;
            if (pinkPixels == visiblePixels) return true;

            return (float)pinkPixels / visiblePixels > PinkVisibleMajorityThreshold;
        }

        private static bool IsBackground(byte r, byte g, byte b, byte a, ColorRange background)
        {
            if (a <= AlphaVisibleThreshold) return true;
            return background.HasSamples && background.Contains(r, g, b, BackgroundTolerance);
        }

        private static ColorRange FindBackgroundRange(Color32[] pixels, int width, int height)
        {
            ColorRange range = new ColorRange();
            if (width == 0 || height == 0) return range;

            for (int x = 0; x < width; x++)
            {
                IncludeBackgroundSample(GetPixel(pixels, width, x, 0), ref range);
                if (height > 1) IncludeBackgroundSample(GetPixel(pixels, width, x, height - 1), ref range);
            }
            for (int y = 1; y < height - 1; y++)
            {
                IncludeBackgroundSample(GetPixel(pixels, width, 0, y), ref range);
                if (width > 1) IncludeBackgroundSample(GetPixel(pixels, width, width - 1, y), ref range);
            }

            return range;
        }

        private static Color32 GetPixel(Color32[] pixels, int width, int x, int y)
        {
            return pixels[(y * width) + x];
        }

        private static void IncludeBackgroundSample(Color32 color, ref ColorRange range)
        {
            if (color.a <= AlphaVisibleThreshold || ImageUtils.IsMagentaPixel(color.r, color.g, color.b)) return;
            range.Include(color.r, color.g, color.b);
        }

        private struct ColorRange
        {
            private byte _minR;
            private byte _maxR;
            private byte _minG;
            private byte _maxG;
            private byte _minB;
            private byte _maxB;

            public bool HasSamples { get; private set; }

            public void Include(byte r, byte g, byte b)
            {
                if (!HasSamples)
                {
                    _minR = r;
                    _maxR = r;
                    _minG = g;
                    _maxG = g;
                    _minB = b;
                    _maxB = b;
                    HasSamples = true;
                    return;
                }

                if (r < _minR) _minR = r;
                if (r > _maxR) _maxR = r;
                if (g < _minG) _minG = g;
                if (g > _maxG) _maxG = g;
                if (b < _minB) _minB = b;
                if (b > _maxB) _maxB = b;
            }

            public bool Contains(byte r, byte g, byte b, int tolerance)
            {
                return r >= _minR - tolerance && r <= _maxR + tolerance
                    && g >= _minG - tolerance && g <= _maxG + tolerance
                    && b >= _minB - tolerance && b <= _maxB + tolerance;
            }
        }
    }
}
