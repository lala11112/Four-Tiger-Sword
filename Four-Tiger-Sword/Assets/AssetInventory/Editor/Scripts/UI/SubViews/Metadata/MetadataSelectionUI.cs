using System;
using System.Collections.Generic;
using System.Linq;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed class MetadataSelectionUI : EditorWindow
    {
        private static readonly Vector2 WindowSize = new Vector2(320f, 300f);

        private List<AssetInfo> _assetInfo;
        private List<MetadataDefinition> _metas;
        private MetadataAssignment.Target _target;
        private Action _onSelect;

        public static MetadataSelectionUI ShowDropdown(Rect anchor, MetadataAssignment.Target target, List<AssetInfo> infos, Action onSelect = null)
        {
            MetadataSelectionUI window = CreateInstance<MetadataSelectionUI>();
            window.titleContent = new GUIContent("Add Metadata");
            window.minSize = WindowSize;
            window.Init(target, onSelect);
            window.SetAssets(infos);
            AssetInventoryUITK.ShowAsDropDown(window, anchor, WindowSize);
            return window;
        }

        public static MetadataSelectionUI ShowDropdown(EditorWindow owner, VisualElement anchor, MetadataAssignment.Target target, List<AssetInfo> infos, Action onSelect = null)
        {
            MetadataSelectionUI window = CreateInstance<MetadataSelectionUI>();
            window.titleContent = new GUIContent("Add Metadata");
            window.minSize = WindowSize;
            window.Init(target, onSelect);
            window.SetAssets(infos);
            AssetInventoryUITK.ShowAsDropDown(window, owner, anchor, WindowSize);
            return window;
        }

        public static MetadataSelectionUI ShowWindow(MetadataAssignment.Target target = MetadataAssignment.Target.Package, Action onSelect = null)
        {
            MetadataSelectionUI window = GetWindow<MetadataSelectionUI>("Add Metadata");
            window.minSize = WindowSize;
            window.Init(target, onSelect);
            return window;
        }

        public void Init(MetadataAssignment.Target target, Action onSelect = null)
        {
            _target = target;
            _onSelect = onSelect;
            _metas = Metadata.LoadDefinitions();
            RebuildIfReady();
        }

        public void SetAssets(List<AssetInfo> infos)
        {
            _assetInfo = infos;
            RebuildIfReady();
        }

        public void SetDefinitions(List<MetadataDefinition> definitions)
        {
            _metas = definitions;
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

            VisualElement header = new VisualElement();
            header.AddToClassList("ai-dialog-toolbar");
            header.Add(AssetInventoryUITK.CreateFlexibleSpacer());
            header.Add(AssetInventoryUITK.CreateIconButton("Manage metadata", "Settings", OpenMetadataManager));
            root.Add(header);

            if (_assetInfo == null)
            {
                VisualElement section = AssetInventoryUITK.CreateSection("Selection Required");
                section.Add(AssetInventoryUITK.CreateCopyLabel("Select a package or asset before adding metadata."));
                root.Add(section);
                root.Add(AssetInventoryUITK.CreateFlexibleSpacer());
                return;
            }

            if (_metas == null)
            {
                VisualElement section = AssetInventoryUITK.CreateSection("Metadata Fields");
                section.Add(AssetInventoryUITK.CreateCopyLabel("Metadata definitions have not been loaded yet."));
                root.Add(section);
                root.Add(AssetInventoryUITK.CreateFlexibleSpacer());
                return;
            }

            if (_metas.Count == 0)
            {
                VisualElement section = AssetInventoryUITK.CreateSection("Metadata Fields");
                section.Add(AssetInventoryUITK.CreateCopyLabel("No metadata fields defined yet. Use the metadata manager to create new definitions."));
                root.Add(section);
                root.Add(AssetInventoryUITK.CreateFlexibleSpacer());
                return;
            }

            List<MetadataDefinition> visibleMetas = _metas.Where(IsVisibleForSelection).ToList();
            if (visibleMetas.Count == 0)
            {
                VisualElement section = AssetInventoryUITK.CreateSection("Metadata Fields");
                section.Add(AssetInventoryUITK.CreateCopyLabel("All available custom metadata fields were assigned already. Use the metadata manager to create new ones if needed."));
                root.Add(section);
                root.Add(AssetInventoryUITK.CreateFlexibleSpacer());
                return;
            }

            ScrollView scroll = new ScrollView();
            scroll.AddToClassList("ai-metadata-selector-scroll");

            for (int i = 0; i < visibleMetas.Count; i++)
            {
                scroll.Add(CreateMetadataRow(visibleMetas[i], i));
            }

            root.Add(scroll);
        }

        private bool IsVisibleForSelection(MetadataDefinition meta)
        {
            if (meta == null) return false;
            if (meta.RestrictAssetSource && !_assetInfo.Any(info => info.AssetSource == meta.ApplicableSource)) return false;

            switch (_target)
            {
                case MetadataAssignment.Target.Package:
                    if (_assetInfo.Count == 1 && _assetInfo[0].PackageMetadata.Any(info => info.MetadataId == meta.Id)) return false;
                    break;

                case MetadataAssignment.Target.Asset:
                    break;
            }

            return true;
        }

        private Button CreateMetadataRow(MetadataDefinition meta, int index)
        {
            Button row = new Button(() => SelectMetadata(meta));
            row.text = string.Empty;
            string restriction = meta.RestrictAssetSource ? $", {StringUtils.CamelCaseToWords(meta.ApplicableSource.ToString())}" : string.Empty;
            AssetInventoryUITK.PopulateListRow(
                row,
                meta.Name,
                $"{meta.Type}{restriction}",
                trailing: AssetInventoryUITK.CreateStatusPill("Add", "ai-status-muted"),
                extraClasses: index % 2 == 1
                    ? new[] {"ai-metadata-selector-row", BaseVerticalCollectionView.itemAlternativeBackgroundUssClassName}
                    : new[] {"ai-metadata-selector-row"});
            row.tooltip = $"Add the {meta.Name} metadata field.";
            return row;
        }

        private void SelectMetadata(MetadataDefinition meta)
        {
            if (meta == null || _assetInfo == null) return;

            _assetInfo.ForEach(info =>
            {
                if (meta.RestrictAssetSource && info.AssetSource != meta.ApplicableSource) return;

                Metadata.AddAssignment(info, meta.Id, _target, true);
            });
            _onSelect?.Invoke();
            Close();
        }

        private static void OpenMetadataManager()
        {
            MetadataUI metasUI = MetadataUI.ShowWindow();
            metasUI.Init();
        }
    }
}
