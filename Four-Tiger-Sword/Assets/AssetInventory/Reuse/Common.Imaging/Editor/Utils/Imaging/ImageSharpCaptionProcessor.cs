using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImpossibleRobert.Common;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImpossibleRobert.Common.Imaging
{
    public static class ImageSharpCaptionProcessor
    {
        public static async Task<(byte[] imageBytes, string mimeType)> ProcessImageForCaption(
            string filePath,
            int minSize = 32,
            CancellationToken cancellationToken = default,
            bool preferJpeg = false)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            bool isPng = ext == ".png";
            bool isJpeg = ext == ".jpg" || ext == ".jpeg";
            bool forceReencode = preferJpeg && isPng;
            string mime = (!isPng || forceReencode) ? "image/jpeg" : "image/png";

            if ((isPng || isJpeg) && !forceReencode)
            {
                try
                {
                    Tuple<int, int> dims = ImageUtils.GetDimensions(filePath, true, ext);
                    if (dims != null && dims.Item1 > 0 && dims.Item2 >= 2 &&
                        dims.Item1 >= minSize && dims.Item2 >= minSize)
                    {
                        byte[] raw;
                        using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                        using (MemoryStream ms = new MemoryStream(fs.CanSeek ? (int)fs.Length : 0))
                        {
                            await fs.CopyToAsync(ms, 81920, cancellationToken);
                            raw = ms.ToArray();
                        }

                        return (raw, mime);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Header read failed. Decode and re-encode through the permissive path below.
                }
            }

            using (Image<Rgba32> img = await Image.LoadAsync<Rgba32>(filePath, cancellationToken))
            {
                int w = img.Width;
                int h = img.Height;

                if (h < 2) throw new InvalidOperationException("Image height is too small");

                double scale = Math.Max((float)minSize / w, (float)minSize / h);
                if (scale > 1.0)
                {
                    int newW = (int)Math.Ceiling(w * scale);
                    int newH = (int)Math.Ceiling(h * scale);
                    img.Mutate(x => x.Resize(newW, newH));
                }

                using (MemoryStream ms = new MemoryStream())
                {
                    IImageEncoder encoder = (isPng && !forceReencode) ? new PngEncoder() : (IImageEncoder)new JpegEncoder();
                    await img.SaveAsync(ms, encoder, cancellationToken);
                    byte[] imgBytes = ms.ToArray();
                    return (imgBytes, mime);
                }
            }
        }
    }
}
