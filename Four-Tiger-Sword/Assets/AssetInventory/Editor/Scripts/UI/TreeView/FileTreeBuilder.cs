using System.Collections.Generic;
using System.Linq;

namespace AssetInventory
{
    internal static class FileTreeBuilder
    {
        public struct ModelResult
        {
            public TreeModel<FileTreeElement> Model;
            public Dictionary<string, FileTreeElement> PathToElementMap;
        }

        public static ModelResult BuildModel(List<string> paths)
        {
            List<FileTreeElement> treeElements = new List<FileTreeElement>();
            FileTreeElement root = new FileTreeElement("Root", -1, 0);
            treeElements.Add(root);

            Dictionary<string, FileTreeElement> pathToElementMap = new Dictionary<string, FileTreeElement>();
            int idCounter = 1;

            foreach (string path in paths.OrderBy(p => p))
            {
                string[] parts = path.Split('/');
                string currentPath = "";

                for (int i = 0; i < parts.Length; i++)
                {
                    string part = parts[i];
                    if (string.IsNullOrEmpty(currentPath)) currentPath = part;
                    else currentPath += "/" + part;

                    if (!pathToElementMap.TryGetValue(currentPath, out FileTreeElement node))
                    {
                        bool isFolder = i < parts.Length - 1;
                        int depth = i;

                        node = new FileTreeElement(part, depth, idCounter++)
                        {
                            Path = currentPath,
                            IsFolder = isFolder
                        };

                        pathToElementMap[currentPath] = node;
                        treeElements.Add(node);
                    }
                }
            }

            FileTreeElement rootElement = TreeElementUtility.ListToTree(treeElements);
            SortTree(rootElement);
            TreeElementUtility.TreeToList(rootElement, treeElements);

            TreeModel<FileTreeElement> model = new TreeModel<FileTreeElement>(treeElements);

            return new ModelResult
            {
                Model = model,
                PathToElementMap = pathToElementMap
            };
        }

        public static ModelResult BuildModel(List<AssetFile> files)
        {
            List<string> paths = new List<string>(files.Count);
            foreach (AssetFile file in files)
            {
                if (!string.IsNullOrEmpty(file.Path)) paths.Add(file.Path);
            }
            return BuildModel(paths);
        }

        public static void SortTree(TreeElement node)
        {
            if (node.Children != null && node.Children.Count > 0)
            {
                node.Children = node.Children
                    .OrderByDescending(c => c is FileTreeElement fte && fte.IsFolder)
                    .ThenBy(c => c.TreeName)
                    .ToList();

                foreach (TreeElement child in node.Children)
                {
                    SortTree(child);
                }
            }
        }
    }
}
