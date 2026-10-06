using ImpossibleRobert.Common;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers;
using Unity.SharpZipLib.GZip;
using Unity.SharpZipLib.Tar;
using UnityEngine;
using CompressionType = SharpCompress.Common.CompressionType;

namespace AssetInventory
{
    public static class CompressionUtil
    {
        internal enum ArchiveEntryFilterMode
        {
            NonContentArtifacts,
            CacheMaterialization
        }

        private static readonly string[] FinderMetadataNames =
        {
            ".DS_Store",
            "Thumbs.db",
            "desktop.ini"
        };

        private static readonly string[] ReservedWindowsNames =
        {
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "COM1",
            "COM2",
            "COM3",
            "COM4",
            "COM5",
            "COM6",
            "COM7",
            "COM8",
            "COM9",
            "LPT1",
            "LPT2",
            "LPT3",
            "LPT4",
            "LPT5",
            "LPT6",
            "LPT7",
            "LPT8",
            "LPT9"
        };

        private static readonly char[] PortableInvalidFileNameChars =
        {
            '<',
            '>',
            ':',
            '"',
            '|',
            '?',
            '*'
        };

        // more performant implementation using SharpCompress, especially on Linux
        public static void ExtractGz(string archive, string targetFolder, CancellationToken ct)
        {
            ExtractGz(archive, targetFolder, ct, ArchiveEntryFilterMode.NonContentArtifacts);
        }

        internal static bool ExtractGz(string archive, string targetFolder, CancellationToken ct, ArchiveEntryFilterMode filterMode)
        {
            Directory.CreateDirectory(targetFolder);

            try
            {
                using FileStream stream = File.OpenRead(archive);
                ReaderOptions readerOptions = new ReaderOptions {LeaveStreamOpen = false};
                using IReader reader = ReaderFactory.Open(stream, readerOptions);
                while (reader.MoveToNextEntry())
                {
                    if (ct.IsCancellationRequested)
                    {
                        AssetInventoryCacheDeletionGuard.TryDeleteExtractionTarget(targetFolder, "canceled archive extraction cleanup");
                        return false;
                    }
                    if (!reader.Entry.IsDirectory)
                    {
                        if (!TryGetSafeArchiveEntryPath(targetFolder, reader.Entry.Key, filterMode, out string fullOutputPath)) continue;

                        string directoryName = Path.GetDirectoryName(fullOutputPath);
                        if (!string.IsNullOrEmpty(directoryName)) Directory.CreateDirectory(IOUtils.ToLongPath(directoryName));

                        using Stream entryStream = reader.OpenEntryStream();
                        using FileStream fileStream = File.Create(IOUtils.ToLongPath(fullOutputPath));
                        entryStream.CopyTo(fileStream);
                    }
                }
                return true;
            }
            catch (UnauthorizedAccessException uaEx)
            {
                Debug.LogError($"Permission denied extracting '{archive}': {uaEx.Message}");
            }
            catch (ArchiveException archEx)
            {
                Debug.LogError($"Archive format error for '{archive}': {archEx.Message}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not extract archive '{archive}'. It may be corrupted or the process was interrupted: {e.Message}");
            }
            return false;
        }

        public static string ExtractGzFile(string archive, string fileName, string targetFolder, CancellationToken ct)
        {
            return ExtractGzFile(archive, fileName, targetFolder, ct, ArchiveEntryFilterMode.NonContentArtifacts);
        }

        internal static string ExtractGzFile(string archive, string fileName, string targetFolder, CancellationToken ct, ArchiveEntryFilterMode filterMode)
        {
            Stream rawStream = File.OpenRead(archive);
            GZipInputStream gzipStream = new GZipInputStream(rawStream);

            string destFile = null;

            // fileName will be ID/asset, whole folder is needed though
            string folderName = fileName.Split(new[] {'/', '\\'}, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

            try
            {
                Stream inputStream = IsZipped(archive) ? gzipStream : rawStream;

                using (TarInputStream tarStream = new TarInputStream(inputStream, Encoding.Default))
                {
                    TarEntry entry;
                    bool found = false;
                    while ((entry = tarStream.GetNextEntry()) != null)
                    {
                        if (ct.IsCancellationRequested) break;
                        if (entry.IsDirectory) continue;
                        if (entry.Name.Contains(folderName))
                        {
                            if (!TryGetSafeArchiveEntryPath(targetFolder, entry.Name, filterMode, out destFile)) continue;

                            string directoryName = Path.GetDirectoryName(destFile);
                            if (!string.IsNullOrEmpty(directoryName)) Directory.CreateDirectory(IOUtils.ToLongPath(directoryName));

                            using (FileStream fileStream = File.Create(IOUtils.ToLongPath(destFile)))
                            {
                                tarStream.CopyEntryContents(fileStream);
                            }
                            found = true;
                        }
                        else if (found)
                        {
                            // leave the loop if the files were found and the next entry is not in the same folder
                            // assumption is the files appear consecutively
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not extract file from archive '{archive}'. The process was either interrupted or the file is corrupted: {e.Message}");
            }

            gzipStream.Close();
            rawStream.Close();

            return destFile;
        }

        private static bool IsZipped(string fileName)
        {
            using (FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] buffer = new byte[2];
                fs.Read(buffer, 0, buffer.Length);
                return buffer[0] == 0x1F && buffer[1] == 0x8B;
            }
        }

        public static bool IsFirstArchiveVolume(string file)
        {
            if (string.IsNullOrEmpty(file)) return true;

            string fileName = Path.GetFileName(file).ToLowerInvariant();
            if (fileName.EndsWith(".rar"))
            {
                Match match = Regex.Match(fileName, @"\.part(\d+)\.rar$");
                if (match.Success)
                {
                    int partNumber = int.Parse(match.Groups[1].Value);
                    return partNumber == 1;
                }
                return true;
            }
            return true;
        }

        public static void CompressFolder(string source, string target)
        {
            using FileStream zipStream = File.Create(target);
            WriterOptions options = new WriterOptions(CompressionType.Deflate);
            using IWriter writer = WriterFactory.Open(zipStream, ArchiveType.Zip, options);
            writer.WriteAll(source, "*", SearchOption.AllDirectories);
        }

        public static void CreateEmptyZip(string zipPath)
        {
            using FileStream zipStream = File.Create(zipPath);
            using IWriter writer = WriterFactory.Open(zipStream, ArchiveType.Zip, new WriterOptions(CompressionType.Deflate));
            // No entries added: creates an empty zip.
        }

        public static bool ExtractArchive(string archiveFile, string targetFolder, CancellationToken ct = default(CancellationToken))
        {
            return ExtractArchive(archiveFile, targetFolder, ct, ArchiveEntryFilterMode.NonContentArtifacts);
        }

        internal static bool ExtractArchive(string archiveFile, string targetFolder, CancellationToken ct, ArchiveEntryFilterMode filterMode)
        {
            Directory.CreateDirectory(targetFolder);

            try
            {
                // CRITICAL: Open archive file with FileShare.Read to allow other Unity editors to read it simultaneously
                // This prevents exclusive locking of Unity cache packages during extraction
                using (FileStream archiveStream = new FileStream(archiveFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    ReaderOptions readerOptions = new ReaderOptions {LeaveStreamOpen = true};
                    IReader reader;
                    try
                    {
                        reader = ReaderFactory.Open(archiveStream, readerOptions);
                    }
                    catch (InvalidFormatException)
                    {
                        archiveStream.Position = 0;
                        using IArchive archive = ArchiveFactory.Open(archiveStream, readerOptions);
                        using IReader archiveReader = archive.ExtractAllEntries();
                        return ExtractArchiveEntries(archiveReader, archiveFile, targetFolder, ct, filterMode);
                    }

                    using (reader)
                    {
                        return ExtractArchiveEntries(reader, archiveFile, targetFolder, ct, filterMode);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not extract archive '{archiveFile}'. The process was potentially interrupted, the file is corrupted or the path too long: {e.Message}");
                return false;
            }
        }

        private static bool ExtractArchiveEntries(IReader reader, string archiveFile, string targetFolder, CancellationToken ct, ArchiveEntryFilterMode filterMode)
        {
            while (reader.MoveToNextEntry())
            {
                if (ct.IsCancellationRequested)
                {
                    AssetInventoryCacheDeletionGuard.TryDeleteExtractionTarget(targetFolder, "canceled archive extraction cleanup");
                    return false;
                }
                if (string.IsNullOrEmpty(reader.Entry.Key)) continue;

                if (!reader.Entry.IsDirectory)
                {
                    string entryKey = reader.Entry.Key;
                    try
                    {
                        if (!TryGetSafeArchiveEntryPath(targetFolder, entryKey, filterMode, out string fullOutputPath)) continue;

                        string directoryName = Path.GetDirectoryName(fullOutputPath);
                        if (!string.IsNullOrEmpty(directoryName)) Directory.CreateDirectory(IOUtils.ToLongPath(directoryName));
                        using (Stream entryStream = reader.OpenEntryStream())
                        using (FileStream fileStream = File.Create(IOUtils.ToLongPath(fullOutputPath)))
                        {
                            entryStream.CopyTo(fileStream);
                        }
                    }
                    catch (Exception e)
                    {
                        if (e is ArgumentException || e is IOException)
                        {
                            // can happen for paths containing : and other illegal characters
                            Debug.LogWarning($"Could not extract file '{entryKey}' from archive '{archiveFile}': {e.Message}");
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
            }

            return true;
        }

        internal static bool ShouldMaterializeArchiveEntry(string entryName, ArchiveEntryFilterMode filterMode)
        {
            if (string.IsNullOrWhiteSpace(entryName)) return false;

            string normalized = NormalizeArchiveEntryKey(entryName);
            if (string.IsNullOrWhiteSpace(normalized)) return false;
            if (normalized.StartsWith("/", StringComparison.Ordinal)) return false;
            if (Regex.IsMatch(normalized, "^[A-Za-z]:/")) return false;
            if (IOUtils.PathContainsInvalidChars(normalized)) return false;

            string[] parts = normalized.Split(new[] {'/'}, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;

            foreach (string part in parts)
            {
                if (part == "." || part == "..") return false;
                if (part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal)) return false;
                if (part.IndexOfAny(PortableInvalidFileNameChars) >= 0) return false;
                if (part.Any(char.IsControl)) return false;
                if (string.Equals(part, "__MACOSX", StringComparison.OrdinalIgnoreCase)) return false;
                if (part.StartsWith("._", StringComparison.Ordinal)) return false;
                if (FinderMetadataNames.Any(name => string.Equals(part, name, StringComparison.OrdinalIgnoreCase))) return false;
                if (IsReservedWindowsName(part)) return false;
            }

            return filterMode != ArchiveEntryFilterMode.CacheMaterialization || !AssetImporter.IsIgnoredPath(normalized, false);
        }

        private static bool TryGetSafeArchiveEntryPath(string targetFolder, string entryName, ArchiveEntryFilterMode filterMode, out string fullOutputPath)
        {
            fullOutputPath = null;
            if (!ShouldMaterializeArchiveEntry(entryName, filterMode)) return false;

            string normalized = NormalizeArchiveEntryKey(entryName);
            string shortTargetFolder = IOUtils.ToShortPath(targetFolder);
            string normalizedTargetFolder = IOUtils.NormalizePath(shortTargetFolder);
            string combinedShortPath = Path.Combine(shortTargetFolder, normalized);
            string normalizedFullOutputPath = IOUtils.NormalizePath(combinedShortPath);

            if (string.IsNullOrEmpty(normalizedTargetFolder) || string.IsNullOrEmpty(normalizedFullOutputPath)) return false;

#if UNITY_EDITOR_WIN
            StringComparison pathComparison = StringComparison.OrdinalIgnoreCase;
#else
            StringComparison pathComparison = StringComparison.Ordinal;
#endif
            string targetPrefix = normalizedTargetFolder.TrimEnd('/') + "/";
            if (!normalizedFullOutputPath.StartsWith(targetPrefix, pathComparison)) return false;

            fullOutputPath = Path.Combine(targetFolder, normalized);
            return true;
        }

        private static string NormalizeArchiveEntryKey(string entryName)
        {
            return entryName?.Replace('\\', '/');
        }

        private static bool IsReservedWindowsName(string segment)
        {
            int dotIndex = segment.IndexOf('.');
            string name = dotIndex >= 0 ? segment.Substring(0, dotIndex) : segment;
            return ReservedWindowsNames.Any(reserved => string.Equals(name, reserved, StringComparison.OrdinalIgnoreCase));
        }
    }
}
