using StringComparison = System.StringComparison;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public static class MediaManager
    {
        internal static void Load(AssetInfo info, bool download = true)
        {
            // when already downloading don't trigger again
            if (info.IsMediaLoading()) return;

            info.DisposeMedia();
            if (info.ParentInfo != null)
            {
                Load(info.ParentInfo, download);
                info.AllMedia = info.ParentInfo.AllMedia;
                info.Media = info.ParentInfo.Media;
                return;
            }

            info.AllMedia = DBAdapter.DB.Query<AssetMedia>("select * from AssetMedia where AssetId=? order by [Order]", info.AssetId).ToList();
            info.Media = info.AllMedia
                .Where(m => m.Type == "main"
                    || m.Type == "screenshot"
                    || m.Type == "synty_main"
                    || m.Type == "synty_screenshot"
                    || m.Type == "youtube"
                    || m.Type == "vimeo"
                    || m.Type == "sketchfab"
                    || m.Type == "attachment_video"
                    || m.Type == "attachment_audio"
                    || m.Type == "mixcloud"
                    || m.Type == "soundcloud")
                .ToList();
            if (download) DownloadMedia(info);
        }

        /// <param name="info">The asset info containing the media</param>
        /// <param name="media">The specific media item to load</param>
        internal static async Task LoadFullMediaOnDemand(AssetInfo info, AssetMedia media)
        {
            if (media.Texture != null || media.IsDownloading || media.DownloadFailed) return;

            // Skip for special types that don't need full media
            if (media.Type == "youtube" || media.Type == "vimeo" || media.Type == "sketchfab" ||
                media.Type == "attachment_audio" || media.Type == "attachment_video" ||
                media.Type == "soundcloud" || media.Type == "mixcloud")
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(media.Url))
            {
                media.DownloadFailed = true;
                return;
            }

            media.IsDownloading = true;
            try
            {
                string targetFile = info.ToAsset().GetMediaFile(media, Paths.GetPreviewFolder(), false);

                bool isLocalFile = !string.IsNullOrWhiteSpace(media.Url) &&
                    !media.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !media.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                    !media.Url.StartsWith("//", StringComparison.OrdinalIgnoreCase);

                if (isLocalFile)
                {
                    if (File.Exists(media.Url))
                    {
                        media.Texture = await LoadTextureWithRoundedCorners(media.Url);
                    }
                }
                else
                {
                    if (!File.Exists(targetFile))
                    {
                        await AssetUtils.LoadImageAsync(media.Url, targetFile);
                    }

                    if (File.Exists(targetFile))
                    {
                        media.Texture = await LoadTextureWithRoundedCorners(targetFile);
                    }
                }

                media.DownloadFailed = media.Texture == null;
            }
            catch (System.Exception e)
            {
                media.DownloadFailed = true;
                Debug.LogWarning($"Could not load package media '{media.Url}': {e.Message}");
            }
            finally
            {
                media.IsDownloading = false;
            }
        }

        private static async void DownloadMedia(AssetInfo info)
        {
            List<AssetMedia> files = info.Media.Where(m => !m.IsDownloading).OrderBy(m => m.Order).ToList();

            // Process sequentially to avoid overwhelming the system during scrolling
            foreach (AssetMedia file in files)
            {
                await DownloadFileAsync(info, file);
            }
        }

        private static async Task DownloadFileAsync(AssetInfo info, AssetMedia file)
        {
            if (info.Media == null) return; // happens when cancelled
            if (file.IsDownloading) return;

            if (!string.IsNullOrWhiteSpace(file.ThumbnailUrl))
            {
                bool isLocalFile = !string.IsNullOrWhiteSpace(file.ThumbnailUrl) &&
                    !file.ThumbnailUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !file.ThumbnailUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                    !file.ThumbnailUrl.StartsWith("//", StringComparison.OrdinalIgnoreCase);

                if (isLocalFile && File.Exists(file.ThumbnailUrl))
                {
                    // Load directly from local file, no need to copy
                    if (info.Media != null)
                    {
                        file.ThumbnailTexture = await LoadTextureWithRoundedCorners(file.ThumbnailUrl);
                    }
                }
                else
                {
                    string thumbnailFile = info.ToAsset().GetMediaThumbnailFile(file, Paths.GetPreviewFolder(), false);
                    if (!File.Exists(thumbnailFile))
                    {
                        if (info.Media == null) return; // happens when cancelled
                        file.IsDownloading = true;
                        await AssetUtils.LoadImageAsync(file.ThumbnailUrl, thumbnailFile);
                        if (info.Media == null) return; // happens when cancelled
                        file.IsDownloading = false;
                    }
                    if (info.Media != null && !file.IsDownloading && File.Exists(thumbnailFile))
                    {
                        file.ThumbnailTexture = await LoadTextureWithRoundedCorners(thumbnailFile);
                    }
                    else
                    {
                        // fallback icon
                        file.ThumbnailTexture = ((Texture2D)EditorGUIUtility.IconContent("d_PlayButton").image).MakeReadable();
                    }
                }
            }
            else if (file.Type == "attachment_audio")
            {
                file.ThumbnailTexture = ((Texture2D)EditorGUIUtility.IconContent("audioclip icon").image).MakeReadable();
            }

            // Note: Full media is now loaded on-demand via LoadFullMediaOnDemand() to save memory
        }

        private static async Task<Texture2D> LoadTextureWithRoundedCorners(string filePath)
        {
            Texture2D texture = await AssetUtils.LoadLocalTexture(filePath, false);
            if (texture == null) return null;

            if (AI.Config.mediaCornerRadius > 0)
            {
                Texture2D roundedTexture = texture.WithRoundedCorners(AI.Config.mediaCornerRadius);
                // Dispose of the original texture since we only need the rounded version
                Object.DestroyImmediate(texture);
                return roundedTexture;
            }

            return texture;
        }
    }
}
