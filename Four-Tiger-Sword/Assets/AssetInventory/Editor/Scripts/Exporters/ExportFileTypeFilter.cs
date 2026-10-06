using System;
using System.Collections.Generic;

namespace AssetInventory
{
    internal enum ExportFileSelectionMode
    {
        AllFileTypes = 0,
        CustomSelection = 1
    }

    internal sealed class ExportFileTypeFilter
    {
        private readonly bool _includeAll;
        private readonly bool _includeOther;
        private readonly HashSet<string> _knownExtensions;
        private readonly HashSet<string> _selectedExtensions;

        private ExportFileTypeFilter(
            bool includeAll,
            bool includeOther,
            HashSet<string> knownExtensions,
            HashSet<string> selectedExtensions)
        {
            _includeAll = includeAll;
            _includeOther = includeOther;
            _knownExtensions = knownExtensions;
            _selectedExtensions = selectedExtensions;
        }

        internal static ExportFileTypeFilter CreateAll()
        {
            return new ExportFileTypeFilter(
                true,
                true,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        internal static ExportFileTypeFilter CreateCustom(IEnumerable<AI.AssetGroup> selectedGroups, bool includeOther)
        {
            HashSet<string> knownExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<AI.AssetGroup, string[]> entry in AI.TypeGroups)
            {
                knownExtensions.UnionWith(entry.Value);
            }

            HashSet<string> selectedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (selectedGroups != null)
            {
                foreach (AI.AssetGroup group in selectedGroups)
                {
                    if (AI.TypeGroups.TryGetValue(group, out string[] extensions))
                    {
                        selectedExtensions.UnionWith(extensions);
                    }
                }
            }

            return new ExportFileTypeFilter(false, includeOther, knownExtensions, selectedExtensions);
        }

        internal static IReadOnlyList<AI.AssetGroup> GetAvailableGroups()
        {
            Array values = Enum.GetValues(typeof (AI.AssetGroup));
            List<AI.AssetGroup> result = new List<AI.AssetGroup>(values.Length);
            foreach (AI.AssetGroup group in values)
            {
                if (AI.TypeGroups.ContainsKey(group)) result.Add(group);
            }
            return result;
        }

        internal bool Includes(string fileType)
        {
            if (_includeAll) return true;

            string extension = fileType ?? string.Empty;
            if (_selectedExtensions.Contains(extension)) return true;

            return _includeOther && !_knownExtensions.Contains(extension);
        }
    }
}
