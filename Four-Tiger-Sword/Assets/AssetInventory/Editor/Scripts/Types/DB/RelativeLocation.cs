using System;
using System.Collections.Generic;
using System.IO;
using SQLite;

namespace AssetInventory
{
    /// <summary>Maps a logical storage key and machine identifier to a normalized filesystem location.</summary>
    [Serializable]
    public sealed class RelativeLocation
    {
        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        [Indexed] public string Key { get; set; }
        [Indexed] public string System { get; set; }
        [Indexed] public string Location { get; set; }

        // runtime only
        public List<string> otherLocations;

        public RelativeLocation()
        {
        }

        /// <summary>Creates a machine-specific storage mapping for the supplied logical key and normalized location.</summary>
        public RelativeLocation(string key, string system, string location) : this()
        {
            Key = key;
            System = system;
            Location = location;
        }

        /// <summary>Stores a normalized forward-slash path, preserving a filesystem root separator; null clears the mapping.</summary>
        public void SetLocation(string location)
        {
            if (location == null)
            {
                Location = null;
                return;
            }

            string normalized = location.Replace("\\", "/");
            string root = null;
            try
            {
                root = Path.GetPathRoot(normalized)?.Replace("\\", "/");
            }
            catch (Exception)
            {
                // Keep the normalized input when root detection cannot handle a partial path.
            }

            bool isWindowsDriveRoot = normalized.Length == 3 && char.IsLetter(normalized[0]) && normalized[1] == ':' && normalized[2] == '/';
            Location = !isWindowsDriveRoot && (string.IsNullOrEmpty(root) || normalized.Length > root.Length) ? normalized.TrimEnd('/') : normalized;
        }

        public override string ToString()
        {
            return $"Location '{Key}' ({Location})";
        }
    }
}
