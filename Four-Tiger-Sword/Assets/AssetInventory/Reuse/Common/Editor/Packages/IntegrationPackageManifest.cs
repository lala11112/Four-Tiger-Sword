using System;
using System.Collections.Generic;
using UnityEngine;

namespace ImpossibleRobert.Common.Editor.Packages
{
    public enum IntegrationPackagePayloadKind
    {
        Integration,
        Samples
    }

    [Serializable]
    public sealed class IntegrationPackageEntry
    {
        public string guid;
        public string path;
        public bool folder;
        public string assetSha256;
        public string metaSha256;
    }

    [Serializable]
    public sealed class IntegrationPackageManifest
    {
        public int schemaVersion;
        public string packageId;
        public string ownerPackageName;
        public string ownerVersion;
        public IntegrationPackagePayloadKind payloadKind;
        public string contentFingerprint;
        public IntegrationPackageEntry[] entries;

        public static bool TryRead(string json, out IntegrationPackageManifest manifest, out string error)
        {
            bool valid = TryReadCore(json, out manifest, out error);
            if (!valid) manifest = null;
            return valid;
        }

        private static bool TryReadCore(string json, out IntegrationPackageManifest manifest, out string error)
        {
            manifest = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Package manifest is empty.";
                return false;
            }

            try
            {
                manifest = JsonUtility.FromJson<IntegrationPackageManifest>(json);
            }
            catch (Exception exception)
            {
                error = "Package manifest JSON is malformed: " + exception.Message;
                return false;
            }

            if (manifest == null || manifest.schemaVersion != 1)
            {
                error = "Package manifest schema is missing or unsupported.";
                return false;
            }

            if (!IntegrationPackageFingerprint.IsGuid(manifest.packageId) ||
                string.IsNullOrWhiteSpace(manifest.ownerPackageName) ||
                string.IsNullOrWhiteSpace(manifest.ownerVersion) ||
                !Enum.IsDefined(typeof(IntegrationPackagePayloadKind), manifest.payloadKind) ||
                manifest.entries == null || manifest.entries.Length == 0)
            {
                error = "Package manifest identity or entries are missing.";
                return false;
            }

            HashSet<string> guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (IntegrationPackageEntry entry in manifest.entries)
            {
                if (entry == null || !IntegrationPackageFingerprint.IsGuid(entry.guid) ||
                    !IntegrationPackageFingerprint.IsSafePath(entry.path) ||
                    !IntegrationPackageFingerprint.IsSha256(entry.metaSha256) ||
                    (!entry.folder && !IntegrationPackageFingerprint.IsSha256(entry.assetSha256)) ||
                    (entry.folder && !string.IsNullOrEmpty(entry.assetSha256)) ||
                    !guids.Add(entry.guid) || !paths.Add(entry.path) ||
                    entry.guid == IntegrationPackageFingerprint.TrackingGuid(manifest.packageId, true) ||
                    entry.path.EndsWith("/IntegrationPackageManifest.json", StringComparison.Ordinal))
                {
                    error = "Package manifest contains an invalid or duplicate payload entry.";
                    return false;
                }
            }

            if (!IntegrationPackageFingerprint.IsSha256(manifest.contentFingerprint) ||
                !string.Equals(manifest.contentFingerprint, IntegrationPackageFingerprint.Compute(manifest.entries), StringComparison.Ordinal))
            {
                error = "Package manifest content fingerprint does not match its entries.";
                return false;
            }

            return true;
        }
    }

    [Serializable]
    public sealed class IntegrationPackageDescriptor
    {
        public IntegrationPackageManifest manifest;
        public string archiveSha256;

        public static bool TryRead(string json, out IntegrationPackageDescriptor descriptor, out string error)
        {
            descriptor = null;
            error = string.Empty;
            try
            {
                descriptor = JsonUtility.FromJson<IntegrationPackageDescriptor>(json);
            }
            catch (Exception exception)
            {
                error = "Package descriptor JSON is malformed: " + exception.Message;
                return false;
            }

            if (descriptor == null || !IntegrationPackageFingerprint.IsSha256(descriptor.archiveSha256))
            {
                error = "Package descriptor archive checksum is missing or invalid.";
                return false;
            }

            if (descriptor.manifest == null || !IntegrationPackageManifest.TryRead(JsonUtility.ToJson(descriptor.manifest), out _, out error))
                return false;

            return true;
        }
    }
}
