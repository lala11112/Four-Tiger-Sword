using ImpossibleRobert.Common;
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed class DBLocationUI : BasicEditorUI
    {
        private string _newFolder;
        private long _dbSize = -1;
        private long _backupSize = -1;
        private long _previewSize = -1;
        private long _cacheSize = -1;
        private long _targetSpace = -1;
        private bool _calculating;
        private bool _ignoreCache = true;
        private bool _sameDrive;

        public static DBLocationUI ShowWindow()
        {
            DBLocationUI window = GetWindow<DBLocationUI>("Change Database Location");
            window.minSize = new Vector2(520, 420);

            return window;
        }

        public void Init(string newFolder)
        {
            _newFolder = newFolder;
            CalculateSizes();
            Build();
        }

        private async void CalculateSizes()
        {
            _calculating = true;
            Build();

            _dbSize = new FileInfo(DBAdapter.GetDBPath()).Length;
            _targetSpace = IOUtils.GetFreeSpace(_newFolder);
            _sameDrive = IOUtils.IsSameDrive(DBAdapter.GetDBPath(), _newFolder);
            if (string.IsNullOrEmpty(AI.Config.backupFolder)) _backupSize = await Paths.GetBackupFolderSize();
            if (string.IsNullOrEmpty(AI.Config.cacheFolder)) _cacheSize = await Paths.GetCacheFolderSize();
            if (string.IsNullOrEmpty(AI.Config.previewFolder)) _previewSize = await Paths.GetPreviewFolderSize();

            _calculating = false;
            Build();
        }

        private void CreateGUI()
        {
            Build();
        }

        private void Build()
        {
            VisualElement root = rootVisualElement;
            if (root == null) return;

            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);

            if (string.IsNullOrWhiteSpace(_newFolder))
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("Select a target folder first before starting this wizard.", MessageType.Info));
                return;
            }

            long spaceRequired = Math.Max(0, _dbSize);
            string curFolder = DBAdapter.GetDBPath().Replace("\\", "/");

            VisualElement scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            root.Add(scroll);

            VisualElement location = AssetInventoryUITK.CreateSection("Database Location");
            location.Add(AssetInventoryUITK.CreateKeyValueRow("Current Database", curFolder));
            location.Add(AssetInventoryUITK.CreateKeyValueRow("Current Size", FormatSize(_dbSize)));
            location.Add(AssetInventoryUITK.CreateKeyValueRow("Target Folder", _newFolder.Replace("\\", "/")));
            location.Add(AssetInventoryUITK.CreateKeyValueRow("Target Free Space", FormatSize(_targetSpace)));
            scroll.Add(location);

#if UNITY_EDITOR_WIN
            if (!IOUtils.IsSameDrive(curFolder, _newFolder))
            {
                scroll.Add(AssetInventoryUITK.CreateHelpBox("This wizard cannot move the database between drives. Close Unity, move the database folder manually, then select its new location here.", MessageType.Error));
                return;
            }
#endif

            if (string.IsNullOrEmpty(AI.Config.previewFolder) || string.IsNullOrEmpty(AI.Config.backupFolder) || string.IsNullOrEmpty(AI.Config.cacheFolder))
            {
                scroll.Add(AssetInventoryUITK.CreateHelpBox("Related data is stored beside the database by default. Choose which folders should move with it and which should remain in place.", MessageType.Info));
                VisualElement relatedFolders = AssetInventoryUITK.CreateSection("Related Folders");

                if (string.IsNullOrEmpty(AI.Config.previewFolder))
                {
                    relatedFolders.Add(CreateFolderRow("Previews Folder", Paths.GetPreviewFolder(), _previewSize, null, () =>
                    {
                        AI.Config.previewFolder = Paths.GetPreviewFolder();
                        AI.SaveConfig();
                        Build();
                    }));

                    spaceRequired += Math.Max(0, _previewSize);
                }

                if (string.IsNullOrEmpty(AI.Config.backupFolder))
                {
                    relatedFolders.Add(CreateFolderRow("Backup Folder", Paths.GetBackupFolder(), _backupSize, null, () =>
                    {
                        AI.Config.backupFolder = Paths.GetBackupFolder();
                        AI.SaveConfig();
                        Build();
                    }));

                    spaceRequired += Math.Max(0, _backupSize);
                }

                if (string.IsNullOrEmpty(AI.Config.cacheFolder))
                {
                    Toggle ignoreCacheToggle = new Toggle("Ignore and Delete")
                    {
                        value = _ignoreCache,
                        tooltip = "The cache will be recreated on demand, so moving it is optional. Ignoring it saves time but may cause delays the next time cached packages are opened."
                    };
                    ignoreCacheToggle.RegisterValueChangedCallback(evt =>
                    {
                        _ignoreCache = evt.newValue;
                        Build();
                    });

                    relatedFolders.Add(CreateFolderRow("Cache Folder", Paths.GetMaterializeFolder(), _ignoreCache ? -2 : _cacheSize, ignoreCacheToggle, () =>
                    {
                        AI.Config.cacheFolder = Paths.GetMaterializeFolder();
                        AI.SaveConfig();
                        Build();
                    }));

                    spaceRequired += _ignoreCache ? 0 : Math.Max(0, _cacheSize);
                }

                int rowIndex = 0;
                foreach (VisualElement row in relatedFolders.Children())
                {
                    if (row.ClassListContains("ai-list-row")) CommonUITK.ApplyListRowStyle(row, rowIndex++);
                }
                scroll.Add(relatedFolders);
            }

            bool spaceIssues = !_sameDrive && spaceRequired > 0 && spaceRequired > _targetSpace;
            VisualElement required = AssetInventoryUITK.CreateSection("Move Summary");
            required.Add(AssetInventoryUITK.CreateKeyValueRow("Space Required", FormatSize(spaceRequired)));
            required.Add(AssetInventoryUITK.CreateKeyValueRow("Target Free Space", FormatSize(_targetSpace)));
            scroll.Add(required);

            if (!_sameDrive && spaceIssues)
            {
                scroll.Add(AssetInventoryUITK.CreateHelpBox("The target drive does not have enough space to move the database and all related files. Please select a different location or free up some space.", MessageType.Error));
            }

            Button moveButton = AssetInventoryUITK.CreatePrimaryButton(_calculating ? "Calculating disk space..." : "Move Database", () => MoveDatabase(_newFolder));
            moveButton.SetEnabled(!_calculating && !spaceIssues);
            VisualElement footer = AssetInventoryUITK.CreateWindowFooter();
            footer.Add(moveButton);
            root.Add(footer);
        }

        private static VisualElement CreateFolderRow(string title, string path, long size, VisualElement extraControl, Action leaveInPlace)
        {
            VisualElement side = new VisualElement();
            side.AddToClassList("ai-db-folder-side");

            Label sizeLabel = new Label(size == -2 ? "will be deleted" : FormatSize(size));
            sizeLabel.AddToClassList("ai-db-size-label");
            side.Add(sizeLabel);

            VisualElement actions = new VisualElement();
            actions.AddToClassList("ai-db-folder-actions");
            if (extraControl != null)
            {
                extraControl.AddToClassList("ai-db-inline-toggle");
                actions.Add(extraControl);
            }
            actions.Add(AssetInventoryUITK.CreateSecondaryButton("Leave in Place", leaveInPlace));
            side.Add(actions);

            VisualElement row = CommonUITK.CreateTitleSubtitleActionRow(
                title,
                path?.Replace("\\", "/") ?? string.Empty,
                side,
                "ai-list-row",
                "ai-list-row-body",
                "ai-list-row-title",
                "ai-list-row-subtitle",
                "ai-db-folder-row");
            row.Q<Label>(className: "ai-list-row-title")?.AddToClassList("ai-db-folder-title");
            row.Q<Label>(className: "ai-list-row-subtitle")?.AddToClassList("ai-db-folder-path");
            CommonUITK.ApplyListRowStyle(row);
            return row;
        }

        private static string FormatSize(long size)
        {
            return size < 0 ? "calculating..." : EditorUtility.FormatBytes(size);
        }

        private void MoveDatabase(string targetFolder)
        {
            string targetDBFile = Path.Combine(targetFolder, Path.GetFileName(DBAdapter.GetDBPath()));
            if (File.Exists(targetDBFile)) File.Delete(targetDBFile);
            DBAdapter.Close();

            try
            {
                EditorUtility.DisplayProgressBar("Moving Database", "Moving database to new location...", 0.1f);
                File.Move(DBAdapter.GetDBPath(), targetDBFile);
                EditorUtility.ClearProgressBar();

                if (string.IsNullOrEmpty(AI.Config.previewFolder) && Directory.Exists(Paths.GetPreviewFolder()))
                {
                    EditorUtility.DisplayProgressBar("Moving Preview Images", "Copying preview images to new location...", 0.3f);
                    Directory.Move(Paths.GetPreviewFolder(), Paths.GetPreviewFolder(targetFolder, true, false));
                    EditorUtility.ClearProgressBar();
                }

                if (string.IsNullOrEmpty(AI.Config.backupFolder) && Directory.Exists(Paths.GetBackupFolder()))
                {
                    EditorUtility.DisplayProgressBar("Moving Backups", "Copying backups to new location...", 0.6f);
                    Directory.Move(Paths.GetBackupFolder(), Paths.GetBackupFolder(false, targetFolder));
                    EditorUtility.ClearProgressBar();
                }

                if (string.IsNullOrEmpty(AI.Config.cacheFolder) && Directory.Exists(Paths.GetMaterializeFolder()))
                {
                    if (!_ignoreCache)
                    {
                        EditorUtility.DisplayProgressBar("Moving Cache", "Copying cache to new location...", 0.6f);
                        Directory.Move(Paths.GetMaterializeFolder(), Paths.GetMaterializeFolder(targetFolder, true));
                        EditorUtility.ClearProgressBar();
                    }
                    else
                    {
                        _ = IOUtils.DeleteFileOrDirectory(Paths.GetMaterializeFolder());
                    }
                }

                // set new location
                AI.SwitchDatabase(targetFolder);
                Close();
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Error Moving Data",
                    "There were errors moving the existing database to a new location. Check the error log for details. Try moving the left-over files manually with Unity closed.\n\n" + e.Message,
                    "OK");
            }

            EditorUtility.ClearProgressBar();
        }
    }
}
