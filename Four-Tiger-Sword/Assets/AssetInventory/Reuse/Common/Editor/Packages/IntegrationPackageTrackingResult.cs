using System;
using System.Text.RegularExpressions;

namespace ImpossibleRobert.Common.Editor.Packages
{
    public enum IntegrationPackageRevisionRelation
    {
        Unknown,
        Same,
        AvailableNewer,
        InstalledNewer,
        DifferentUnordered
    }

    public static class IntegrationPackageRevisionComparer
    {
        private static readonly Regex SemanticVersion = new Regex(@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z", RegexOptions.CultureInvariant);

        public static IntegrationPackageRevisionRelation Compare(IntegrationPackageManifest installed, IntegrationPackageManifest available)
        {
            if (installed == null || available == null ||
                !string.Equals(installed.packageId, available.packageId, StringComparison.Ordinal) ||
                installed.payloadKind != available.payloadKind ||
                !string.Equals(installed.ownerPackageName, available.ownerPackageName, StringComparison.Ordinal))
                return IntegrationPackageRevisionRelation.Unknown;

            if (string.Equals(installed.contentFingerprint, available.contentFingerprint, StringComparison.Ordinal))
                return IntegrationPackageRevisionRelation.Same;

            if (!TryCompareVersions(installed.ownerVersion, available.ownerVersion, out int order) || order == 0)
                return IntegrationPackageRevisionRelation.DifferentUnordered;

            return order < 0 ? IntegrationPackageRevisionRelation.AvailableNewer : IntegrationPackageRevisionRelation.InstalledNewer;
        }

        public static bool TryCompareVersions(string left, string right, out int result)
        {
            result = 0;
            Match a = SemanticVersion.Match(left ?? string.Empty);
            Match b = SemanticVersion.Match(right ?? string.Empty);
            if (!a.Success || !b.Success)
                return false;

            string aPrerelease = a.Groups[4].Value;
            string bPrerelease = b.Groups[4].Value;
            string[] aParts = aPrerelease.Split('.');
            string[] bParts = bPrerelease.Split('.');
            if (aPrerelease.Length > 0 && !ValidPrerelease(aParts) || bPrerelease.Length > 0 && !ValidPrerelease(bParts))
                return false;

            for (int index = 1; index <= 3; index++)
            {
                result = CompareNumeric(a.Groups[index].Value, b.Groups[index].Value);
                if (result != 0)
                    return true;
            }

            if (aPrerelease.Length == 0 || bPrerelease.Length == 0)
            {
                result = aPrerelease.Length == bPrerelease.Length ? 0 : aPrerelease.Length == 0 ? 1 : -1;
                return true;
            }

            for (int index = 0; index < Math.Min(aParts.Length, bParts.Length); index++)
            {
                bool aNumeric = IsNumeric(aParts[index]);
                bool bNumeric = IsNumeric(bParts[index]);
                result = aNumeric && bNumeric ? CompareNumeric(aParts[index], bParts[index]) :
                    aNumeric != bNumeric ? (aNumeric ? -1 : 1) :
                    Math.Sign(string.CompareOrdinal(aParts[index], bParts[index]));
                if (result != 0)
                    return true;
            }

            result = Math.Sign(aParts.Length.CompareTo(bParts.Length));
            return true;
        }

        private static bool ValidPrerelease(string[] parts)
        {
            foreach (string part in parts)
            {
                if (part.Length == 0 || IsNumeric(part) && part.Length > 1 && part[0] == '0')
                    return false;
            }
            return true;
        }

        private static bool IsNumeric(string value)
        {
            foreach (char character in value)
            {
                if (character < '0' || character > '9')
                    return false;
            }
            return value.Length > 0;
        }

        private static int CompareNumeric(string left, string right)
        {
            int lengthOrder = left.Length.CompareTo(right.Length);
            return lengthOrder != 0 ? Math.Sign(lengthOrder) : Math.Sign(string.CompareOrdinal(left, right));
        }
    }
}
