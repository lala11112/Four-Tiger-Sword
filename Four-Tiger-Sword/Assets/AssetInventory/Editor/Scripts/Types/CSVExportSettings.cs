using System;
using System.Collections.Generic;

namespace AssetInventory
{
    /// <summary>Serializable settings controlling csv export behavior in Asset Inventory.</summary>
    [Serializable]
    public sealed class CSVExportSettings
    {
        public string separator = ";";
        public bool addHeader = true;
        public List<string> selectedFields;
        public string exportFile;

        /// <summary>Initializes missing field selection and separator values to the supported CSV defaults.</summary>
        public void EnsureDefaults()
        {
            if (string.IsNullOrEmpty(separator)) separator = ";";
            if (selectedFields == null) selectedFields = CSVExport.GetDefaultFields();
        }
    }
}