using System;
using System.IO;

namespace AssetInventory
{
    /// <summary>Describes an export template's identity, inheritance, required data, file operations, parameters, and discovered source paths.</summary>
    public class TemplateInfo
    {
        public string name;
        public string description;
        public int version = 1;
        public DateTime date;
        public bool readOnly;
        public bool isSample;
        public bool fixedTargetFolder;
        public string entryPath;
        public bool needsDataPath;
        public bool needsImagePath;
        public string[] packageFields;
        public string[] fileFields;

        public string inheritFrom;
        public string[] moveFiles;
        public string[] deleteFiles;
        public string[] parameters;

        // runtime
        [field: NonSerialized] public string path;
        [field: NonSerialized] public bool hasDescriptor;
        [field: NonSerialized] public bool hasFilesData;

        /// <summary>Returns the template display name declared by the descriptor, falling back to its filename when necessary.</summary>
        public string GetNameFromFile()
        {
            return Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));
        }

        /// <summary>Returns the template display name declared by the descriptor, falling back to its filename when necessary.</summary>
        public string GetNameFromFile(string filePath)
        {
            return Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(filePath));
        }

        /// <summary>Returns the full path of the template descriptor associated with this template.</summary>
        public string GetDescriptorPath()
        {
            return Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path)) + ".json");
        }
    }
}
