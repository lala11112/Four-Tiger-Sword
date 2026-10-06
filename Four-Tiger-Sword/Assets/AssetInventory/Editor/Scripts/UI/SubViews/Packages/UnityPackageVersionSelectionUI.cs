using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed class UnityPackageVersionSelectionUI : EditorWindow
    {
        private static readonly Vector2 WindowSize = new Vector2(320f, 240f);

        private AssetInfo _info;
        private Action<string> _callback;
        private Dictionary<int, List<BackupInfo>> _backupState;
        private List<BackupInfo> _availableVersions;

        public static UnityPackageVersionSelectionUI ShowDropdown(Rect anchor, AssetInfo info, Dictionary<int, List<BackupInfo>> backupState, Action<string> callback)
        {
            UnityPackageVersionSelectionUI window = CreateInstance<UnityPackageVersionSelectionUI>();
            window.titleContent = new GUIContent("Select Backup Version");
            window.minSize = WindowSize;
            window.Init(info, backupState, callback);
            AssetInventoryUITK.ShowAsDropDown(window, anchor, WindowSize);
            return window;
        }

        public static UnityPackageVersionSelectionUI ShowDropdown(EditorWindow owner, VisualElement anchor, AssetInfo info, Dictionary<int, List<BackupInfo>> backupState, Action<string> callback)
        {
            UnityPackageVersionSelectionUI window = CreateInstance<UnityPackageVersionSelectionUI>();
            window.titleContent = new GUIContent("Select Backup Version");
            window.minSize = WindowSize;
            window.Init(info, backupState, callback);
            AssetInventoryUITK.ShowAsDropDown(window, owner, anchor, WindowSize);
            return window;
        }

        public static UnityPackageVersionSelectionUI ShowWindow(AssetInfo info = null, Dictionary<int, List<BackupInfo>> backupState = null, Action<string> callback = null)
        {
            UnityPackageVersionSelectionUI window = GetWindow<UnityPackageVersionSelectionUI>("Select Backup Version");
            window.minSize = WindowSize;
            window.Init(info, backupState, callback);
            return window;
        }

        public void Init(AssetInfo info, Dictionary<int, List<BackupInfo>> backupState, Action<string> callback)
        {
            _info = info;
            _callback = callback;
            _backupState = backupState;

            int backupKey = AssetBackup.GetBackupKey(_info);
            if (_backupState != null && backupKey != 0)
            {
                if (_backupState.TryGetValue(backupKey, out List<BackupInfo> versions))
                {
                    _availableVersions = versions.ToList();
                }
                else
                {
                    _availableVersions = new List<BackupInfo>();
                }
            }
            else
            {
                _availableVersions = new List<BackupInfo>();
            }

            RebuildIfReady();
        }

        private void CreateGUI()
        {
            BuildContent();
        }

        private void RebuildIfReady()
        {
            if (rootVisualElement != null && rootVisualElement.childCount > 0)
            {
                BuildContent();
            }
        }

        private void BuildContent()
        {
            VisualElement root = rootVisualElement;
            if (root == null) return;

            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);

            if (_info == null)
            {
                VisualElement section = AssetInventoryUITK.CreateSection("Package");
                section.Add(AssetInventoryUITK.CreateCopyLabel("No package selected."));
                root.Add(section);
                root.Add(AssetInventoryUITK.CreateFlexibleSpacer());
                return;
            }

            if (_availableVersions == null || _availableVersions.Count == 0)
            {
                VisualElement section = AssetInventoryUITK.CreateSection("Backup Versions");
                section.Add(AssetInventoryUITK.CreateCopyLabel("No backup versions available for this package."));
                root.Add(section);
                root.Add(AssetInventoryUITK.CreateFlexibleSpacer());

                string defaultVersion = _info.GetVersion(true);
                if (!string.IsNullOrWhiteSpace(defaultVersion))
                {
                    VisualElement footer = AssetInventoryUITK.CreateWindowFooter();
                    footer.Add(AssetInventoryUITK.CreateSecondaryButton($"Use Current Version ({defaultVersion})", () => SelectVersion(null)));
                    root.Add(footer);
                }
                return;
            }

            ScrollView scroll = new ScrollView();
            scroll.AddToClassList("ai-version-selector-scroll");

            for (int i = 0; i < _availableVersions.Count; i++)
            {
                scroll.Add(CreateVersionRow(_availableVersions[i], i));
            }

            root.Add(scroll);

            if (ShouldShowClearSelection())
            {
                VisualElement footer = AssetInventoryUITK.CreateWindowFooter();
                footer.Add(AssetInventoryUITK.CreateSecondaryButton("Clear Selection (use default)", () => SelectVersion(null)));
                root.Add(footer);
            }
        }

        private Button CreateVersionRow(BackupInfo backupInfo, int index)
        {
            string version = backupInfo?.version;
            string currentSelectedVersion = _info.ForcedUnityPackageVersion;
            string currentVersion = _info.GetVersion(true);
            bool isCurrent = version == currentSelectedVersion;
            bool isDefault = string.IsNullOrWhiteSpace(currentSelectedVersion) && version == currentVersion;

            Button row = new Button(() =>
            {
                if (!isCurrent) SelectVersion(version);
            });
            row.text = string.Empty;
            row.tooltip = isCurrent ? "Currently selected" : "Select this version";

            string pillText = isCurrent ? "Selected" : isDefault ? "Current" : "Use";
            string pillClass = isCurrent || isDefault ? null : "ai-status-muted";
            AssetInventoryUITK.PopulateListRow(
                row,
                string.IsNullOrWhiteSpace(version) ? "-none-" : version,
                string.Empty,
                trailing: AssetInventoryUITK.CreateStatusPill(pillText, pillClass),
                extraClasses: index % 2 == 1
                    ? new[] {"ai-version-selector-row", BaseVerticalCollectionView.itemAlternativeBackgroundUssClassName, isCurrent || isDefault ? "ai-list-row-selected" : null}
                    : new[] {"ai-version-selector-row", isCurrent || isDefault ? "ai-list-row-selected" : null});
            return row;
        }

        private bool ShouldShowClearSelection()
        {
            if (_info == null) return false;

            string currentSelectedVersion = _info.ForcedUnityPackageVersion;
            string currentVersion = _info.GetVersion(true);
            return !string.IsNullOrWhiteSpace(currentSelectedVersion) && currentSelectedVersion != currentVersion;
        }

        private void SelectVersion(string version)
        {
            _callback?.Invoke(version);
            Close();
        }
    }
}
