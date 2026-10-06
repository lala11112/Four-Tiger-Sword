using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed class ProjectSelectionUI : EditorWindow
    {
        private static readonly Vector2 WindowSize = new Vector2(350f, 250f);

        private Action<AssetInfo> _onSelection;
        private List<AssetInfo> _assetInfo;

        public static ProjectSelectionUI ShowDropdown(Rect anchor, List<AssetInfo> infos, Action<AssetInfo> onSelection = null)
        {
            ProjectSelectionUI window = CreateInstance<ProjectSelectionUI>();
            window.titleContent = new GUIContent("Select Asset Manager Project");
            window.minSize = WindowSize;
            window.Init(onSelection);
            window.SetAssets(infos);
            AssetInventoryUITK.ShowAsDropDown(window, anchor, WindowSize);
            return window;
        }

        public static ProjectSelectionUI ShowWindow(Action<AssetInfo> onSelection = null)
        {
            ProjectSelectionUI window = GetWindow<ProjectSelectionUI>("Select Asset Manager Project");
            window.minSize = WindowSize;
            window.Init(onSelection);
            return window;
        }

        public void Init(Action<AssetInfo> onSelection = null)
        {
            _onSelection = onSelection;
            if (rootVisualElement != null && rootVisualElement.childCount > 0)
            {
                BuildContent();
            }
        }

        public void SetAssets(List<AssetInfo> infos)
        {
            _assetInfo = infos == null
                ? new List<AssetInfo>()
                : infos
                    .Where(a => a != null && a.AssetSource == Asset.Source.AssetManager)
                    .OrderBy(GetOrganization)
                    .ThenBy(GetProjectLabel)
                    .ToList();

            if (rootVisualElement != null && rootVisualElement.childCount > 0)
            {
                BuildContent();
            }
        }

        private void CreateGUI()
        {
            BuildContent();
        }

        private void BuildContent()
        {
            VisualElement root = rootVisualElement;
            if (root == null) return;

            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);

            if (_assetInfo == null || _assetInfo.Count == 0)
            {
                VisualElement section = AssetInventoryUITK.CreateSection("Asset Manager Projects");
                section.Add(AssetInventoryUITK.CreateCopyLabel("No Asset Manager projects created or indexed yet. Update the index under Settings to sync Unity Cloud project data."));
                root.Add(section);
                root.Add(AssetInventoryUITK.CreateFlexibleSpacer());
                return;
            }

            ScrollView scroll = new ScrollView();
            scroll.AddToClassList("ai-project-selector-scroll");

            string lastOrg = null;
            for (int i = 0; i < _assetInfo.Count; i++)
            {
                AssetInfo asset = _assetInfo[i];
                string organization = GetOrganization(asset);
                if (lastOrg != organization)
                {
                    Label orgLabel = AssetInventoryUITK.CreateCopyLabel($"Org: {organization}");
                    orgLabel.AddToClassList("ai-project-org-label");
                    scroll.Add(orgLabel);
                    lastOrg = organization;
                }

                scroll.Add(CreateProjectRow(asset, i));
            }

            root.Add(scroll);
        }

        private Button CreateProjectRow(AssetInfo asset, int index)
        {
            Button row = new Button(() => SelectAsset(asset));
            row.text = string.Empty;
            string location = asset == null || string.IsNullOrWhiteSpace(asset.Location) ? "Project root" : asset.Location;
            AssetInventoryUITK.PopulateListRow(
                row,
                GetProjectTitle(asset),
                location,
                trailing: AssetInventoryUITK.CreateStatusPill("Select", "ai-status-muted"),
                extraClasses: index % 2 == 1 ? new[] {"ai-project-row", BaseVerticalCollectionView.itemAlternativeBackgroundUssClassName} : new[] {"ai-project-row"});
            row.tooltip = $"Use {GetProjectTitle(asset)} as the target project.";
            return row;
        }

        private void SelectAsset(AssetInfo asset)
        {
            _onSelection?.Invoke(asset);
            Close();
        }

        private static string GetOrganization(AssetInfo asset)
        {
            Asset root = asset?.ToAsset().GetRootAsset();
            string organization = root?.OriginalLocation;
            return string.IsNullOrWhiteSpace(organization) ? "Unknown Organization" : organization;
        }

        private static string GetProjectLabel(AssetInfo asset)
        {
            string rootName = GetProjectTitle(asset);
            if (!string.IsNullOrWhiteSpace(asset?.Location))
            {
                return rootName + "/" + asset.Location;
            }

            return rootName;
        }

        private static string GetProjectTitle(AssetInfo asset)
        {
            Asset root = asset?.ToAsset().GetRootAsset();
            if (!string.IsNullOrWhiteSpace(root?.DisplayName)) return root.DisplayName;
            if (!string.IsNullOrWhiteSpace(root?.SafeName)) return root.SafeName;
            return "Unnamed Project";
        }
    }
}
