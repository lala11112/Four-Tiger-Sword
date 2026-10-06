using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using ImpossibleRobert.Common;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public class UninstallPackageUI : BasicEditorUI
    {
        private AssetInfo _info;
        private string _displayName;
        private bool _fileMode;
        private Dictionary<string, List<string>> _usages;
        private bool _calculating;
        private bool _deleteEmptyFolders = true;

        private TreeModel<FileTreeElement> _treeModel;
        private TreeView _treeView;

        // Async analysis fields
        private bool _analyzingUsages;
        private int _analysisProgress;
        private int _analysisTotal;
        private bool _cancellationRequested;
        private Dictionary<string, FileTreeElement> _pathToElementMap;
        private IVisualElementScheduledItem _progressUpdate;
        private ProgressBar _progressBar;
        private Button _deleteButton;

        public static UninstallPackageUI ShowWindow()
        {
            UninstallPackageUI window = GetWindow<UninstallPackageUI>("Uninstall Package");
            window.minSize = new Vector2(500, 400);
            return window;
        }

        public void Init(AssetInfo info, AssetInfo usageInfo = null)
        {
            _info = info;
            _displayName = info.GetDisplayName();
            _fileMode = false;
            titleContent = new GUIContent("Uninstall Package");
            _calculating = true;
            _usages = new Dictionary<string, List<string>>();

            AnalyzePackage(usageInfo);
        }

        public void Init(List<AssetInfo> files)
        {
            _info = files[0];
            _fileMode = true;
            _displayName = files.Count == 1 ? Path.GetFileName(files[0].GetPath(true)) : $"{files.Count:N0} Files";
            titleContent = new GUIContent("Remove from Project");
            _calculating = true;
            _usages = new Dictionary<string, List<string>>();

            AnalyzeFiles(files);
        }

        private void CreateGUI()
        {
            Build();
        }

        private void AnalyzePackage(AssetInfo usageInfo)
        {
            List<string> projectFiles = new List<string>();
            List<string> packageGuids = new List<string>();

            if (usageInfo?.HasChildInfos == true)
            {
                foreach (AssetInfo child in usageInfo.EnumerateChildInfos())
                {
                    if (!string.IsNullOrEmpty(child.ProjectPath))
                    {
                        projectFiles.Add(child.ProjectPath);
                        if (!string.IsNullOrEmpty(child.Guid)) packageGuids.Add(child.Guid);
                    }
                }
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "Usage information not available. Please run 'Identify Used Packages' first.", "OK");
                Close();
                return;
            }

            BuildTreeFromPaths(projectFiles, packageGuids);
        }

        private void AnalyzeFiles(List<AssetInfo> files)
        {
            HashSet<string> projectFiles = new HashSet<string>();
            List<string> guids = new List<string>();

            foreach (AssetInfo file in files)
            {
                if (string.IsNullOrEmpty(file.ProjectPath)) continue;

                projectFiles.Add(file.ProjectPath);
                if (!string.IsNullOrEmpty(file.Guid)) guids.Add(file.Guid);

                // resolve forward dependencies
                string[] deps = AssetDatabase.GetDependencies(file.ProjectPath, true);
                foreach (string dep in deps)
                {
                    if (dep == file.ProjectPath) continue;
                    if (!dep.StartsWith("Assets/")) continue;

                    projectFiles.Add(dep);
                    string depGuid = AssetDatabase.AssetPathToGUID(dep);
                    if (!string.IsNullOrEmpty(depGuid)) guids.Add(depGuid);
                }
            }

            BuildTreeFromPaths(projectFiles.OrderBy(p => p).ToList(), guids);
        }

        private void BuildTreeFromPaths(List<string> projectFiles, List<string> guids)
        {
            FileTreeBuilder.ModelResult result = FileTreeBuilder.BuildModel(projectFiles);
            _treeModel = result.Model;
            _pathToElementMap = result.PathToElementMap;

            // Mark folders that AssetDatabase recognizes
            foreach (KeyValuePair<string, FileTreeElement> kvp in _pathToElementMap)
            {
                if (!kvp.Value.IsFolder && AssetDatabase.IsValidFolder(kvp.Key))
                {
                    kvp.Value.IsFolder = true;
                }
            }

            _calculating = false;
            Build();

            _ = AnalyzeUsagesAsync(guids);
        }

        private async Task AnalyzeUsagesAsync(List<string> packageGuids)
        {
            _analyzingUsages = true;
            _cancellationRequested = false;
            _analysisProgress = 0;

            HashSet<string> packageGuidSet = new HashSet<string>(packageGuids);

            Dictionary<string, List<string>> usages = await ProjectDependencyAnalysis.FindReferencesAsync(packageGuidSet, (current, total) =>
            {
                if (_cancellationRequested) return;
                _analysisProgress = current;
                _analysisTotal = total;
            });

            if (!_cancellationRequested)
            {
                foreach (KeyValuePair<string, List<string>> kvp in usages)
                {
                    _usages[kvp.Key] = kvp.Value;

                    if (_pathToElementMap.TryGetValue(kvp.Key, out FileTreeElement element))
                    {
                        element.Usages = kvp.Value;
                    }
                }

                FileTreeSelection.DeselectConflicting(_treeModel);
            }

            _analyzingUsages = false;
            Build();
        }

        private void Build()
        {
            StopProgressRefresh();

            VisualElement root = rootVisualElement;
            if (root == null) return;

            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);
            root.AddToClassList("ai-uninstall-root");

            if (_info == null)
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("Select project files before opening this window.", MessageType.Info));
                return;
            }

            VisualElement targetSection = AssetInventoryUITK.CreateSection("Selection");
            targetSection.Add(AssetInventoryUITK.CreateKeyValueRow(_fileMode ? "File" : "Package", _displayName));
            root.Add(targetSection);

            if (_calculating)
            {
                root.Add(AssetInventoryUITK.CreateProgressBar(_fileMode ? "Loading files..." : "Loading package files...", 0.35f));
                return;
            }

            if (_treeModel == null || _treeModel.NumberOfDataElements <= 1) // 1 is root
            {
                root.Add(AssetInventoryUITK.CreateHelpBox(_fileMode ? "No matching files found in the project." : "No files from this package found in the project.", MessageType.Info));
                return;
            }

            root.Add(AssetInventoryUITK.CreateHelpBox("Select files to delete. Files with warning icons are used by other assets.", MessageType.Info));

            if (_analyzingUsages)
            {
                VisualElement progressRow = new VisualElement();
                progressRow.AddToClassList("ai-progress-row");
                _progressBar = AssetInventoryUITK.CreateProgressBar(GetUsageProgressLabel(), GetUsageProgressValue());
                progressRow.Add(_progressBar);
                progressRow.Add(AssetInventoryUITK.CreateSecondaryButton("Cancel", () =>
                {
                    _cancellationRequested = true;
                    StopProgressRefresh();
                    Build();
                }));
                root.Add(progressRow);
                _progressUpdate = root.schedule.Execute(RefreshProgress).Every(250);
            }

            root.Add(BuildSelectionActions());
            root.Add(BuildTreeSection());
            root.Add(BuildFooter());
            RefreshActions();
        }

        private VisualElement BuildSelectionActions()
        {
            VisualElement actions = new VisualElement();
            actions.AddToClassList("ai-uninstall-actions");

            Button selectAll = AssetInventoryUITK.CreateSecondaryButton("Select All", () =>
            {
                FileTreeSelection.SelectAll(_treeModel);
                RefreshTreeAndActions();
            });
            actions.Add(selectAll);

            if (FileTreeSelection.HasConflicting(_treeModel))
            {
                Button deselectConflicting = AssetInventoryUITK.CreateSecondaryButton("Deselect Conflicting", () =>
                {
                    FileTreeSelection.DeselectConflicting(_treeModel);
                    RefreshTreeAndActions();
                });
                actions.Add(deselectConflicting);
            }

            return actions;
        }

        private VisualElement BuildTreeSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Files");
            section.AddToClassList("ai-uninstall-tree-section");

            _treeView = new TreeView
            {
                fixedItemHeight = 22f,
                selectionType = SelectionType.None,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                showBorder = true,
                horizontalScrollingEnabled = true,
                makeItem = CreateTreeRow,
                bindItem = BindTreeRow
            };
            _treeView.AddToClassList("ai-uninstall-tree");
            _treeView.SetRootItems(CreateTreeItems(_treeModel.Root));
            _treeView.Rebuild();
            _treeView.ExpandAll();
            section.Add(_treeView);

            return section;
        }

        private VisualElement BuildFooter()
        {
            VisualElement footer = AssetInventoryUITK.CreateWindowFooter();
            footer.AddToClassList("ai-uninstall-footer");

            Toggle deleteEmptyFolders = new Toggle("Delete Empty Folders");
            deleteEmptyFolders.AddToClassList("ai-uninstall-toggle");
            deleteEmptyFolders.SetValueWithoutNotify(_deleteEmptyFolders);
            deleteEmptyFolders.RegisterValueChangedCallback(evt => _deleteEmptyFolders = evt.newValue);
            footer.Add(deleteEmptyFolders);

            _deleteButton = AssetInventoryUITK.CreateDestructiveButton(string.Empty, DeleteSelectedFiles);
            footer.Add(_deleteButton);

            return footer;
        }

        private List<TreeViewItemData<FileTreeElement>> CreateTreeItems(FileTreeElement parent)
        {
            List<TreeViewItemData<FileTreeElement>> result = new List<TreeViewItemData<FileTreeElement>>();
            if (parent?.Children == null) return result;

            foreach (TreeElement child in parent.Children)
            {
                if (child is FileTreeElement element)
                {
                    result.Add(CreateTreeItem(element));
                }
            }

            return result;
        }

        private TreeViewItemData<FileTreeElement> CreateTreeItem(FileTreeElement element)
        {
            return new TreeViewItemData<FileTreeElement>(element.TreeId, element, CreateTreeItems(element));
        }

        private VisualElement CreateTreeRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("ai-uninstall-tree-row");

            Toggle toggle = new Toggle();
            toggle.name = "selectionToggle";
            toggle.AddToClassList("ai-uninstall-tree-toggle");
            toggle.RegisterValueChangedCallback(evt =>
            {
                FileTreeElement element = toggle.userData as FileTreeElement;
                if (element == null) return;
                if (IsRuleControlled(element)) return;

                FileTreeSelection.SetSelected(element, evt.newValue);
                RefreshTreeAndActions();
            });
            row.Add(toggle);

            Image icon = new Image
            {
                name = "itemIcon",
                scaleMode = ScaleMode.ScaleToFit
            };
            icon.AddToClassList("ai-uninstall-tree-icon");
            row.Add(icon);

            Label label = AssetInventoryUITK.CreateCopyLabel(string.Empty);
            label.name = "itemLabel";
            label.displayTooltipWhenElided = true;
            label.AddToClassList("ai-uninstall-tree-label");
            row.Add(label);

            Image warning = new Image
            {
                name = "warningIcon",
                image = EditorGUIUtility.IconContent("console.warnicon.sml").image,
                scaleMode = ScaleMode.ScaleToFit
            };
            warning.AddToClassList("ai-uninstall-warning-icon");
            row.Add(warning);

            return row;
        }

        private void BindTreeRow(VisualElement row, int index)
        {
            FileTreeElement element = _treeView.GetItemDataForIndex<FileTreeElement>(index);
            Toggle toggle = row.Q<Toggle>("selectionToggle");
            Image icon = row.Q<Image>("itemIcon");
            Label label = row.Q<Label>("itemLabel");
            Image warning = row.Q<Image>("warningIcon");

            bool ruleControlled = IsRuleControlled(element);
            toggle.userData = element;
            toggle.SetEnabled(!ruleControlled);
            toggle.SetValueWithoutNotify(element.IsAutoIncluded || (!element.IsAutoExcluded && element.IsSelected));

            icon.image = GetTreeIcon(element);
            icon.tooltip = element.Path;

            label.text = element.TreeName;
            label.tooltip = element.Path;

            List<string> activeUsages = FileTreeSelection.GetActiveUsages(element, _treeModel);
            bool hasWarning = activeUsages.Count > 0;
            warning.style.display = hasWarning ? DisplayStyle.Flex : DisplayStyle.None;
            warning.tooltip = hasWarning ? "Used by:\n" + string.Join("\n", activeUsages) : string.Empty;
        }

        private static bool IsRuleControlled(FileTreeElement element)
        {
            return element != null && (element.IsAutoExcluded || element.IsAutoIncluded);
        }

        private static Texture GetTreeIcon(FileTreeElement element)
        {
            if (element == null) return null;
            if (element.IsFolder) return EditorGUIUtility.IconContent("Folder Icon").image;

            Texture icon = AssetDatabase.GetCachedIcon(element.Path);
            return icon != null ? icon : EditorGUIUtility.IconContent("DefaultAsset Icon").image;
        }

        private void RefreshTreeAndActions()
        {
            _treeView?.RefreshItems();
            RefreshActions();
        }

        private void RefreshActions()
        {
            if (_deleteButton == null || _treeModel == null) return;

            int countToDelete = CountSelectedFiles();
            _deleteButton.text = $"Delete {countToDelete:N0} File{(countToDelete == 1 ? string.Empty : "s")}";
            _deleteButton.SetEnabled(countToDelete > 0);
        }

        private string GetUsageProgressLabel()
        {
            return _analysisTotal > 0
                ? $"Analyzing usage: {_analysisProgress:N0}/{_analysisTotal:N0} (optional)"
                : "Analyzing usage (optional)";
        }

        private float GetUsageProgressValue()
        {
            return _analysisTotal > 0 ? (float)_analysisProgress / _analysisTotal : 0.25f;
        }

        private void RefreshProgress()
        {
            if (!_analyzingUsages)
            {
                StopProgressRefresh();
                Build();
                return;
            }

            if (_progressBar == null) return;
            _progressBar.title = GetUsageProgressLabel();
            _progressBar.value = GetUsageProgressValue();
        }

        private void StopProgressRefresh()
        {
            _progressUpdate?.Pause();
            _progressUpdate = null;
        }

        private int CountSelectedFiles()
        {
            return _treeModel.GetData()
                .Count(e => e.Depth >= 0 && !e.IsFolder && e.IsSelected);
        }

        private void DeleteSelectedFiles()
        {
            List<string> filesToDelete = _treeModel.GetData()
                .Where(e => e.Depth >= 0 && !e.IsFolder && e.IsSelected)
                .Select(e => e.Path)
                .ToList();

            if (filesToDelete.Count == 0) return;

            if (_analyzingUsages)
            {
                string message = $"Usage analysis is still running. Some files may be used by other assets.\n\nAre you sure you want to delete {filesToDelete.Count:N0} files?\nThis cannot be undone.";
                if (!EditorUtility.DisplayDialog("Confirm Delete", message, "Delete", "Cancel")) return;
            }

            // Cancel analysis if still running
            if (_analyzingUsages)
            {
                _cancellationRequested = true;
            }

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string file in filesToDelete)
                {
                    AssetDatabase.DeleteAsset(file);
                }

                if (_deleteEmptyFolders)
                {
                    CleanEmptyFolders(filesToDelete);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.Refresh();
            AI.TriggerPackageRefresh();
            Close();
        }

        private void CleanEmptyFolders(List<string> deletedFiles)
        {
            HashSet<string> directories = new HashSet<string>();
            foreach (string file in deletedFiles)
            {
                string dir = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(dir)) directories.Add(dir);
            }

            bool deleted;
            do
            {
                deleted = false;
                List<string> currentDirs = directories.ToList();
                directories.Clear();

                foreach (string dir in currentDirs)
                {
                    if (Directory.Exists(dir) && IOUtils.IsDirectoryEmpty(dir))
                    {
                        AssetDatabase.DeleteAsset(dir);
                        deleted = true;

                        string parent = Path.GetDirectoryName(dir);
                        if (!string.IsNullOrEmpty(parent) && parent.Contains("Assets"))
                        {
                            directories.Add(parent);
                        }
                    }
                }
            } while (deleted);
        }

        private void OnInspectorUpdate()
        {
            if (_analyzingUsages) RefreshProgress();
        }

        private void OnDestroy()
        {
            StopProgressRefresh();
            if (_analyzingUsages)
            {
                _cancellationRequested = true;
            }
        }
    }
}
