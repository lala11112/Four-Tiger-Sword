using System;

namespace AssetInventory
{
    /// <summary>Named value map supplied to template exports for caller-defined placeholders.</summary>
    [Serializable]
    public class TemplateExportEnvironment
    {
        public string name = "Default";
        public string publishFolder;
        public string dataPath = "data/";
        public string imagePath = "Previews/";
        public bool excludeImages;
        public bool internalIdsOnly;

        public TemplateExportEnvironment()
        {
        }

        /// <summary>Creates a named template environment with an initially empty value map.</summary>
        public TemplateExportEnvironment(string name)
        {
            this.name = name;
        }
    }
}
