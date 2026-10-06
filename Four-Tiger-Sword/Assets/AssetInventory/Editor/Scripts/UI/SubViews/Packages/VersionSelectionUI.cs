using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.UIElements;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace AssetInventory
{
    public sealed class VersionSelectionUI : EditorWindow
    {
        private static readonly Vector2 WindowSize = new Vector2(430f, 320f);
        private const int MaxMetadataRetryCount = 20;

        private AssetInfo _info;
        private PackageInfo _packageInfo;
        private Action<string> _callback;
        private Repository _repository;
        private bool _metadataRetryScheduled;
        private int _metadataRetryCount;

        public static VersionSelectionUI ShowDropdown(Rect anchor, AssetInfo info, Action<string> callback)
        {
            VersionSelectionUI window = CreateInstance<VersionSelectionUI>();
            window.titleContent = new GUIContent("Select Package Version");
            window.minSize = WindowSize;
            window.Init(info, callback);
            AssetInventoryUITK.ShowAsDropDown(window, anchor, WindowSize);
            return window;
        }

        public static VersionSelectionUI ShowDropdown(EditorWindow owner, VisualElement anchor, AssetInfo info, Action<string> callback)
        {
            VersionSelectionUI window = CreateInstance<VersionSelectionUI>();
            window.titleContent = new GUIContent("Select Package Version");
            window.minSize = WindowSize;
            window.Init(info, callback);
            AssetInventoryUITK.ShowAsDropDown(window, owner, anchor, WindowSize);
            return window;
        }

        public static VersionSelectionUI ShowWindow(AssetInfo info = null, Action<string> callback = null)
        {
            VersionSelectionUI window = GetWindow<VersionSelectionUI>("Select Package Version");
            window.minSize = WindowSize;
            window.Init(info, callback);
            return window;
        }

        public void Init(AssetInfo info, Action<string> callback)
        {
            _info = info;
            _callback = callback;
            _packageInfo = null;
            _repository = null;
            _metadataRetryScheduled = false;
            _metadataRetryCount = 0;
            if (!string.IsNullOrWhiteSpace(info?.Repository)) _repository = JsonConvert.DeserializeObject<Repository>(info.Repository);
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

            if (_info.PackageSource == PackageSource.Git)
            {
                BuildGitReferenceContent(root);
                return;
            }

            if (_packageInfo == null) _packageInfo = AssetStore.GetPackageInfo(_info.SafeName, true);

            if (_packageInfo == null && !AssetStore.IsMetadataAvailable())
            {
                VisualElement section = AssetInventoryUITK.CreateSection("Package Metadata");
                section.Add(AssetInventoryUITK.CreateCopyLabel("Loading package metadata..."));
                root.Add(section);
                root.Add(AssetInventoryUITK.CreateFlexibleSpacer());
                ScheduleMetadataRetry();
                return;
            }

            if (_packageInfo == null) _packageInfo = AssetStore.GetPackageInfo(_info.SafeName);
            if (_packageInfo == null)
            {
                ScheduleMetadataRetry();

                VisualElement section = AssetInventoryUITK.CreateSection("Package Metadata");
                section.Add(AssetInventoryUITK.CreateCopyLabel("Could not find matching package metadata."));
                root.Add(section);
                root.Add(AssetInventoryUITK.CreateFlexibleSpacer());

                if (!string.IsNullOrWhiteSpace(_info.LatestVersion))
                {
                    VisualElement footer = AssetInventoryUITK.CreateWindowFooter();
                    footer.Add(AssetInventoryUITK.CreateSecondaryButton($"Install Indexed {_info.LatestVersion}", () => SelectVersion(_info.LatestVersion)));
                    root.Add(footer);
                }
                return;
            }

            if (_packageInfo.versions.all.Length == 0)
            {
                VisualElement section = AssetInventoryUITK.CreateSection("Versions");
                if (_packageInfo.source == PackageSource.Embedded)
                {
                    section.Add(AssetInventoryUITK.CreateCopyLabel("This is an embedded package with no other versions available."));
                }
                else
                {
                    section.Add(AssetInventoryUITK.CreateCopyLabel("Could not find any other versions."));
                }
                root.Add(section);
                root.Add(AssetInventoryUITK.CreateFlexibleSpacer());
                return;
            }

            ScrollView scroll = new ScrollView();
            scroll.AddToClassList("ai-version-selector-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;

            List<string> versions = _packageInfo.versions.all.Reverse().ToList();
            for (int i = 0; i < versions.Count; i++)
            {
                scroll.Add(CreateVersionRow(versions[i], i));
            }

            root.Add(scroll);
        }

        private void BuildGitReferenceContent(VisualElement root)
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Git Reference");
            section.Add(AssetInventoryUITK.CreateCopyLabel($"This is a Git reference to '{_repository?.url}'."));
            root.Add(section);
            root.Add(AssetInventoryUITK.CreateFlexibleSpacer());

            string indexedVersion = _info.GetVersion(true);
            string buttonText = string.IsNullOrWhiteSpace(indexedVersion)
                ? "Install Indexed"
                : $"Install Indexed {indexedVersion}";

            VisualElement footer = AssetInventoryUITK.CreateWindowFooter();
            footer.Add(AssetInventoryUITK.CreateSecondaryButton(buttonText, () => SelectVersion(_info.LatestVersion)));
            root.Add(footer);
        }

        private VisualElement CreateVersionRow(string version, int index)
        {
            bool compatible = _packageInfo.versions.compatible.Contains(version);
#if UNITY_2022_2_OR_NEWER
            bool recommended = version == _packageInfo.versions.recommended;
#else
            bool recommended = version == _packageInfo.versions.verified;
#endif
            bool installed = AssetStore.IsInstalled(_packageInfo.name, version);
            bool canInstall = (compatible || AI.ShowAdvanced()) && !installed;

            VisualElement row = new VisualElement();
            row.tooltip = GetInstallTooltip(canInstall, compatible, installed);
            row.RegisterCallback<ClickEvent>(_ =>
            {
                if (canInstall) SelectVersion(version);
            });

            VisualElement actions = new VisualElement();
            actions.AddToClassList("ai-list-actions");
            actions.AddToClassList("ai-version-row-actions");

            if (recommended)
            {
                actions.Add(AssetInventoryUITK.CreateStatusPill("Recommended"));
            }

            if (installed)
            {
                actions.Add(AssetInventoryUITK.CreateStatusPill("Installed", "ai-status-progress"));
            }
            else if (compatible)
            {
                actions.Add(AssetInventoryUITK.CreateStatusPill("Compatible", "ai-status-muted"));
            }
            else
            {
                actions.Add(AssetInventoryUITK.CreateStatusPill("Incompatible", "ai-status-warning"));
            }

            string changeLogURL = _info.GetChangeLogURL(version);
            Button changeLog = AssetInventoryUITK.CreateIconButton("Open changelog", "_Help", () =>
            {
                if (!string.IsNullOrWhiteSpace(changeLogURL)) AI.OpenURL(changeLogURL);
            });
            changeLog.AddToClassList("ai-version-changelog-button");
            changeLog.SetEnabled(!string.IsNullOrWhiteSpace(changeLogURL));
            changeLog.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            actions.Add(changeLog);

            List<string> classes = new List<string> {"ai-version-selector-row", "ai-registry-version-row"};
            if (index % 2 == 1) classes.Add(BaseVerticalCollectionView.itemAlternativeBackgroundUssClassName);
            if (recommended || installed) classes.Add("ai-list-row-selected");
            if (!canInstall) classes.Add("ai-version-row-disabled");
            AssetInventoryUITK.PopulateListRow(row, version, string.Empty, trailing: actions, extraClasses: classes.ToArray());
            return row;
        }

        private void ScheduleMetadataRetry()
        {
            if (_metadataRetryScheduled || rootVisualElement == null || _metadataRetryCount >= MaxMetadataRetryCount) return;

            _metadataRetryCount++;
            _metadataRetryScheduled = true;
            rootVisualElement.schedule.Execute(() =>
            {
                _metadataRetryScheduled = false;
                if (this != null)
                {
                    _packageInfo = null;
                    BuildContent();
                }
            }).StartingIn(500);
        }

        private static string GetInstallTooltip(bool canInstall, bool compatible, bool installed)
        {
            if (installed) return "This version is already installed.";
            if (!compatible && !AI.ShowAdvanced()) return "Only compatible versions can be installed unless advanced mode is enabled.";
            return canInstall ? "Install this version" : string.Empty;
        }

        private void SelectVersion(string version)
        {
            _callback?.Invoke(version);
            Close();
        }
    }
}
