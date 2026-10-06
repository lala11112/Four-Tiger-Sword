using System.Collections.Generic;

namespace AssetInventory
{
    public static class FileTreeSelection
    {
        public static void SetSelected(FileTreeElement element, bool selected)
        {
            element.IsSelected = selected;

            if (element.HasChildren)
            {
                foreach (TreeElement child in element.Children)
                {
                    if (child is FileTreeElement fileChild)
                    {
                        SetSelected(fileChild, selected);
                    }
                }
            }
        }

        public static void SelectAll(TreeModel<FileTreeElement> model)
        {
            foreach (FileTreeElement element in model.GetData())
            {
                if (element.Depth >= 0) element.IsSelected = true;
            }
        }

        public static void DeselectConflicting(TreeModel<FileTreeElement> model)
        {
            foreach (FileTreeElement element in model.GetData())
            {
                if (element.Depth >= 0 && GetActiveUsages(element, model).Count > 0)
                {
                    element.IsSelected = false;
                }
            }
        }

        public static bool HasConflicting(TreeModel<FileTreeElement> model)
        {
            foreach (FileTreeElement element in model.GetData())
            {
                if (element.Depth >= 0 && GetActiveUsages(element, model).Count > 0) return true;
            }
            return false;
        }

        public static List<string> GetActiveUsages(FileTreeElement element, TreeModel<FileTreeElement> model)
        {
            List<string> activeUsages = new List<string>();
            if (element == null || element.Usages == null || element.Usages.Count == 0 || model == null) return activeUsages;

            foreach (string usage in element.Usages)
            {
                FileTreeElement usageElement = FindByPath(model, usage);
                if (usageElement == null || !usageElement.IsSelected || usageElement.IsAutoExcluded)
                {
                    activeUsages.Add(usage);
                }
            }

            return activeUsages;
        }

        private static FileTreeElement FindByPath(TreeModel<FileTreeElement> model, string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            foreach (FileTreeElement element in model.GetData())
            {
                if (element.Depth >= 0 && element.Path == path) return element;
            }

            return null;
        }
    }
}
