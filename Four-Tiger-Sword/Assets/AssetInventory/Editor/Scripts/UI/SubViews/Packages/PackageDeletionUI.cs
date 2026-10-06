using ImpossibleRobert.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed class PackageDeletionUI : BasicEditorUI
    {
        private enum DeletionMode
        {
            DatabaseOnly = 0,
            FileSystemOnly = 1,
            Both = 2,
            ForgetContent = 3
        }

        private AssetInfo _info;
        private List<AssetInfo> _bulkInfos;
        private Action _onComplete;
        private DeletionMode _selectedMode = DeletionMode.DatabaseOnly;
        private bool _canDeleteFromFileSystem;
        private int _deletableFromFileSystemCount;
        private long _totalSize;

        private bool IsBulkMode => _bulkInfos != null && _bulkInfos.Count > 0;

        public static PackageDeletionUI ShowWindow(bool isBulk = false)
        {
            PackageDeletionUI window = GetWindow<PackageDeletionUI>(isBulk ? "Delete Packages" : "Delete Package");
            window.maxSize = new Vector2(500, 400);
            window.minSize = window.maxSize;

            return window;
        }

        public void Init(AssetInfo info, Action onComplete = null)
        {
            _info = info;
            _bulkInfos = null;
            _onComplete = onComplete;

            // Determine available options based on package type and state
            _canDeleteFromFileSystem = CanDeleteFromFileSystem(info);
            _deletableFromFileSystemCount = _canDeleteFromFileSystem ? 1 : 0;

            // Set default selection
            _selectedMode = DeletionMode.DatabaseOnly;
            Build();
        }

        public void Init(List<AssetInfo> infos, Action onComplete = null)
        {
            _info = null;
            _bulkInfos = infos;
            _onComplete = onComplete;

            // Determine available options based on package types and states
            _deletableFromFileSystemCount = infos.Count(CanDeleteFromFileSystem);
            _canDeleteFromFileSystem = _deletableFromFileSystemCount > 0;
            _totalSize = infos.Where(i => i.ParentId <= 0).Sum(i => i.PackageSize);

            // Set default selection
            _selectedMode = DeletionMode.DatabaseOnly;
            Build();
        }

        private static bool CanDeleteFromFileSystem(AssetInfo info)
        {
            return info.ParentId <= 0 && info.IsDownloaded && info.SafeName != Asset.NONE
                && info.AssetSource != Asset.Source.RegistryPackage && info.AssetSource != Asset.Source.AssetManager
                && info.AssetSource != Asset.Source.Directory;
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

            if (_info == null && !IsBulkMode)
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("No package selected.", MessageType.Error));
                return;
            }

            root.Add(BuildPackageInformation());
            root.Add(BuildDeletionOptions());
            root.Add(AssetInventoryUITK.CreateHelpBox(GetWarningText(), GetWarningMessageType()));
            root.Add(AssetInventoryUITK.CreateFlexibleSpacer());

            VisualElement footer = AssetInventoryUITK.CreateWindowFooter();
            footer.Add(AssetInventoryUITK.CreateDestructiveButton("Delete", PerformDeletion));
            root.Add(footer);
        }

        private VisualElement BuildPackageInformation()
        {
            if (IsBulkMode)
            {
                VisualElement section = AssetInventoryUITK.CreateSection("Bulk Selection");
                int packageCount = _bulkInfos.Count(i => i.ParentId <= 0);
                int subPackageCount = _bulkInfos.Count - packageCount;

                section.Add(AssetInventoryUITK.CreateKeyValueRow("Selected Packages", $"{packageCount:N0}"));
                if (subPackageCount > 0) section.Add(AssetInventoryUITK.CreateKeyValueRow("Sub-Packages", $"{subPackageCount:N0}"));
                if (_canDeleteFromFileSystem) section.Add(AssetInventoryUITK.CreateKeyValueRow("Deletable from Disk", $"{_deletableFromFileSystemCount:N0}"));
                section.Add(AssetInventoryUITK.CreateKeyValueRow("Total Size", EditorUtility.FormatBytes(_totalSize)));
                return section;
            }

            VisualElement info = AssetInventoryUITK.CreateSection("Package Information");
            info.Add(AssetInventoryUITK.CreateKeyValueRow("Name", _info.GetDisplayName()));
            info.Add(AssetInventoryUITK.CreateKeyValueRow("Type", StringUtils.CamelCaseToWords(_info.AssetSource.ToString())));
            if (!string.IsNullOrEmpty(_info.Version)) info.Add(AssetInventoryUITK.CreateKeyValueRow("Version", _info.Version));
            if (_info.IsDownloaded && !string.IsNullOrEmpty(_info.GetLocation(true))) info.Add(AssetInventoryUITK.CreateKeyValueRow("Location", _info.GetLocation(true)));
            return info;
        }

        private VisualElement BuildDeletionOptions()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Deletion Options");
            section.AddToClassList("ai-option-section");
            section.Add(CreateDeletionOption(DeletionMode.DatabaseOnly, "Delete from Index", true));
            section.Add(CreateDeletionOption(DeletionMode.FileSystemOnly, "Delete from File System", _canDeleteFromFileSystem));
            section.Add(CreateDeletionOption(DeletionMode.Both, "Delete from Index and File System", _canDeleteFromFileSystem));
            section.Add(CreateDeletionOption(DeletionMode.ForgetContent, "Forget Indexed Content", true));
            return section;
        }

        private VisualElement CreateDeletionOption(DeletionMode mode, string label, bool enabled)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("ai-option-row");
            CommonUITK.ApplyListRowStyle(row, (int)mode);
            row.EnableInClassList(BaseVerticalCollectionView.itemSelectedVariantUssClassName, _selectedMode == mode);
            row.EnableInClassList("ai-option-disabled", !enabled);
            row.SetEnabled(enabled);

            Label marker = new Label(_selectedMode == mode ? "(x)" : "( )");
            marker.AddToClassList("ai-option-marker");
            row.Add(marker);

            Label text = new Label(label);
            text.AddToClassList("ai-option-label");
            row.Add(text);

            if (enabled)
            {
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    _selectedMode = mode;
                    Build();
                });
            }

            return row;
        }

        private string GetWarningText()
        {
            string packageText = IsBulkMode ? "packages" : "package";
            string fileText = IsBulkMode ? "files" : "file";
            string entryText = IsBulkMode ? "entries" : "entry";
            switch (_selectedMode)
            {
                case DeletionMode.DatabaseOnly:
                    return $"The {packageText} will be removed from the index only. The {fileText} will remain in the cache and the {packageText} will reappear after the next index update.";
                case DeletionMode.FileSystemOnly:
                    return $"The {packageText} will be removed from the file system. The index {entryText} will remain and be marked as not downloaded.";
                case DeletionMode.Both:
                    return $"The {packageText} will be permanently removed from both the index and the file system.";
                case DeletionMode.ForgetContent:
                    return $"All indexed files and previews will be removed. The {packageText} will remain registered but in an unindexed state, ready to be indexed fresh.";
                default:
                    return string.Empty;
            }
        }

        private MessageType GetWarningMessageType()
        {
            return _selectedMode == DeletionMode.DatabaseOnly || _selectedMode == DeletionMode.Both
                ? MessageType.Warning
                : MessageType.Info;
        }

        private void PerformDeletion()
        {
            List<AssetInfo> targets = IsBulkMode ? _bulkInfos : new List<AssetInfo> {_info};

            foreach (AssetInfo info in targets)
            {
                switch (_selectedMode)
                {
                    case DeletionMode.DatabaseOnly:
                        // Delete from database only
                        Assets.RemovePackage(info, false);
                        break;

                    case DeletionMode.FileSystemOnly:
                        // Delete from file system only
                        if (CanDeleteFromFileSystem(info) && File.Exists(info.GetLocation(true)))
                        {
                            File.Delete(info.GetLocation(true));
                            info.SetLocation(null);
                            info.PackageSize = 0;
                            info.CurrentState = Asset.State.New;
                            info.Refresh();
                            DBAdapter.DB.Execute("update Asset set Location=null, PackageSize=0, CurrentState=? where Id=?", Asset.State.New, info.AssetId);
                        }
                        break;

                    case DeletionMode.Both:
                        // Delete from both database and file system
                        Assets.RemovePackage(info, CanDeleteFromFileSystem(info));
                        break;

                    case DeletionMode.ForgetContent:
                        // Remove indexed content only (files + previews)
                        Assets.ForgetPackage(info, false, true);
                        break;
                }
            }

            _onComplete?.Invoke();
            Close();
        }
    }
}
