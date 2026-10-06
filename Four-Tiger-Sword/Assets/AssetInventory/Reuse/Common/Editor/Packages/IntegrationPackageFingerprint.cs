using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ImpossibleRobert.Common.Editor.Packages
{
    public static class IntegrationPackageFingerprint
    {
        private const string Algorithm = "wetzold-integration-payload-v1";

        public static bool IsGuid(string value)
        {
            return value != null && value.Length == 32 && value.All(IsLowerHex);
        }

        public static bool IsSha256(string value)
        {
            return value != null && value.Length == 64 && value.All(IsLowerHex);
        }

        public static bool IsSafePath(string value)
        {
            if (string.IsNullOrEmpty(value) || value.StartsWith("/", StringComparison.Ordinal) || value.Contains("\\"))
                return false;

            return value.Split('/').All(part => part.Length > 0 && part != "." && part != ".." && part.IndexOf(':') < 0);
        }

        public static string Compute(IEnumerable<IntegrationPackageEntry> entries)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, new UTF8Encoding(false), true))
            {
                Write(writer, Algorithm);
                foreach (IntegrationPackageEntry entry in entries.OrderBy(item => item.path, StringComparer.Ordinal).ThenBy(item => item.guid, StringComparer.Ordinal))
                {
                    Write(writer, entry.path);
                    Write(writer, entry.guid);
                    writer.Write(entry.folder);
                    Write(writer, entry.assetSha256 ?? string.Empty);
                    Write(writer, entry.metaSha256);
                }

                writer.Flush();
                using (SHA256 hash = SHA256.Create())
                    return ToHex(hash.ComputeHash(stream.ToArray()));
            }
        }

        public static string HashFile(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return ToHex(hash.ComputeHash(stream));
        }

        public static string TrackingGuid(string packageId, bool installed)
        {
            if (!IsGuid(packageId))
                throw new ArgumentException("Package ID must be a lower-case asset GUID.", nameof(packageId));

            using (MD5 hash = MD5.Create())
                return ToHex(hash.ComputeHash(Encoding.UTF8.GetBytes(packageId + (installed ? ":installed-manifest-v1" : ":available-descriptor-v1"))));
        }

        private static void Write(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static bool IsLowerHex(char value)
        {
            return value >= '0' && value <= '9' || value >= 'a' && value <= 'f';
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes)
                builder.Append(value.ToString("x2"));
            return builder.ToString();
        }
    }
}
