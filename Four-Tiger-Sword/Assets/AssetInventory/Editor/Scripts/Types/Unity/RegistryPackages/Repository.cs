using System;
using UnityEditor.PackageManager;

namespace AssetInventory
{
    /// <summary>Source-control repository metadata declared by a Unity registry package.</summary>
    [Serializable]
    public sealed class Repository
    {
        public string type;
        public string url;
        public string revision;
        public string path;

        public Repository()
        {
        }

        /// <summary>Creates repository metadata from the supplied Unity package repository descriptor.</summary>
        public Repository(RepositoryInfo repository)
        {
            type = repository.type;
            url = repository.url;
            revision = repository.revision;
            path = repository.path;
        }

        public override string ToString()
        {
            return $"Repository '{type}' ({url})";
        }
    }
}
