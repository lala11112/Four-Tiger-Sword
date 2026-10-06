using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using ImpossibleRobert.Common;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed class MetadataUI : EditorWindow
    {
        private List<MetadataDefinition> _metas;
        private string _searchTerm;
        private bool _runtimeHooksRegistered;
        private VisualElement _listContainer;
        private readonly List<MetadataView> _metadataViews = new List<MetadataView>();
        private VisualElement _metadataSection;
        private Label _metadataSectionTitle;
        private VisualElement _metadataEmptyState;

        public static MetadataUI ShowWindow()
        {
            MetadataUI window = GetWindow<MetadataUI>("Metadata Management");
            window.minSize = new Vector2(300, 250);

            return window;
        }

        private void OnEnable()
        {
            RegisterRuntimeHooks();
            if (_metas == null) Init();
        }

        private void OnDisable()
        {
            UnregisterRuntimeHooks();
        }

        private void RegisterRuntimeHooks()
        {
            if (_runtimeHooksRegistered) return;

            _runtimeHooksRegistered = true;
            Metadata.OnDefinitionsChanged -= Init;
            Metadata.OnDefinitionsChanged += Init;
        }

        private void UnregisterRuntimeHooks()
        {
            if (!_runtimeHooksRegistered) return;

            _runtimeHooksRegistered = false;
            Metadata.OnDefinitionsChanged -= Init;
        }

        public void Init()
        {
            _metas = Metadata.LoadDefinitions();
            Metadata.LoadAssignments(null, false);
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

            ToolbarSearchField searchField = AssetInventoryUITK.CreateWindowSearchField(
                _searchTerm,
                "Filter metadata fields by name.",
                value =>
                {
                    _searchTerm = value;
                    RefreshMetadataList();
                },
                "ai-standalone-search-field");
            root.Add(searchField);

            _listContainer = new VisualElement();
            _listContainer.style.flexGrow = 1f;
            root.Add(_listContainer);
            BuildMetadataList();

            root.Add(AssetInventoryUITK.CreateFlexibleSpacer());

            VisualElement footer = AssetInventoryUITK.CreateWindowFooter();
            footer.Add(AssetInventoryUITK.CreatePrimaryButton("New...", CreateDefinition));

            Button deleteAll = AssetInventoryUITK.CreateDestructiveButton("Delete All", DeleteAllDefinitions);
            bool hasDeletableDefinitions = _metas != null && _metas.Any(meta => !meta.IsPredefined);
            deleteAll.SetEnabled(hasDeletableDefinitions);
            deleteAll.tooltip = hasDeletableDefinitions
                ? "Delete all user-created metadata fields. Built-in fields are kept."
                : "There are no user-created metadata fields to delete.";
            footer.Add(deleteAll);
            root.Add(footer);
        }

        private void BuildMetadataList()
        {
            if (_listContainer == null) return;

            _listContainer.Clear();
            _metadataViews.Clear();

            if (_metas == null)
            {
                _listContainer.Add(AssetInventoryUITK.CreateHelpBox("Metadata definitions have not been loaded yet.", MessageType.Warning));
            }
            else if (_metas.Count == 0)
            {
                _listContainer.Add(AssetInventoryUITK.CreateHelpBox("No metadata fields defined yet.", MessageType.Info));
            }
            else
            {
                _metadataSection = AssetInventoryUITK.CreateSection("0 Metadata Fields");
                _metadataSectionTitle = _metadataSection.Q<Label>();
                ScrollView list = new ScrollView(ScrollViewMode.Vertical);
                list.AddToClassList("ai-list");
                for (int i = 0; i < _metas.Count; i++)
                {
                    VisualElement row = CreateMetadataRow(_metas[i], i);
                    _metadataViews.Add(new MetadataView(_metas[i], row));
                    list.Add(row);
                }
                _metadataSection.Add(list);
                _listContainer.Add(_metadataSection);
                _metadataEmptyState = AssetInventoryUITK.CreateHelpBox("No metadata fields match the current search.", MessageType.Info);
                _listContainer.Add(_metadataEmptyState);
                RefreshMetadataList();
            }
        }

        private void RefreshMetadataList()
        {
            int visibleCount = 0;
            for (int i = 0; i < _metadataViews.Count; i++)
            {
                MetadataView view = _metadataViews[i];
                bool visible = string.IsNullOrWhiteSpace(_searchTerm) ||
                               view.Definition.Name.IndexOf(_searchTerm, System.StringComparison.OrdinalIgnoreCase) >= 0;
                view.Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (visible)
                    visibleCount++;
            }

            if (_metadataSectionTitle != null)
                _metadataSectionTitle.text = $"{visibleCount:N0} Metadata Field{(visibleCount == 1 ? string.Empty : "s")}";
            if (_metadataSection != null)
                _metadataSection.style.display = visibleCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (_metadataEmptyState != null)
                _metadataEmptyState.style.display = visibleCount == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private readonly struct MetadataView
        {
            internal readonly MetadataDefinition Definition;
            internal readonly VisualElement Root;

            internal MetadataView(MetadataDefinition definition, VisualElement root)
            {
                Definition = definition;
                Root = root;
            }
        }

        private VisualElement CreateMetadataRow(MetadataDefinition meta, int index)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("ai-metadata-row");
            VisualElement actions = new VisualElement();
            actions.AddToClassList("ai-list-actions");
            if (meta.IsPredefined)
            {
                actions.Add(AssetInventoryUITK.CreateStatusPill("Built-in", "ai-status-muted"));
            }

            Button edit = AssetInventoryUITK.CreateIconButton(
                meta.IsPredefined ? "Built-in metadata fields cannot be edited." : "Edit metadata",
                "editicon.sml",
                () => EditDefinition(meta));
            edit.SetEnabled(!meta.IsPredefined);
            actions.Add(edit);

            Button delete = AssetInventoryUITK.CreateIconButton(
                meta.IsPredefined ? "Built-in metadata fields cannot be removed." : "Remove metadata completely",
                "TreeEditor.Trash",
                () => DeleteDefinition(meta));
            delete.SetEnabled(!meta.IsPredefined);
            actions.Add(delete);

            string restriction = meta.RestrictAssetSource ? $", {StringUtils.CamelCaseToWords(meta.ApplicableSource.ToString())}" : string.Empty;
            string subtitle = $"{meta.Type}{restriction}";
            AssetInventoryUITK.PopulateListRow(
                row,
                meta.Name,
                subtitle,
                trailing: actions,
                extraClasses: index % 2 == 1 ? new[] {BaseVerticalCollectionView.itemAlternativeBackgroundUssClassName} : null);

            Label titleLabel = row.Q<Label>(className: "ai-list-row-title");
            if (titleLabel != null) titleLabel.tooltip = meta.Name;
            Label subtitleLabel = row.Q<Label>(className: "ai-list-row-subtitle");
            if (subtitleLabel != null) subtitleLabel.tooltip = subtitle;

            return row;
        }

        private static void CreateDefinition()
        {
            MetadataEditorUI metaUI = MetadataEditorUI.ShowWindow();
            metaUI.Init();
        }

        private static void EditDefinition(MetadataDefinition meta)
        {
            if (meta == null || meta.IsPredefined) return;

            MetadataEditorUI metaUI = MetadataEditorUI.ShowWindow();
            metaUI.Init(meta);
        }

        private static void DeleteDefinition(MetadataDefinition meta)
        {
            if (EditorUtility.DisplayDialog("Delete Metadata Definitions", "Are you sure you want to delete this metadata definition and all connected data? This action cannot be undone.", "Delete", "Cancel"))
            {
                Metadata.DeleteDefinition(meta);
            }
        }

        private void DeleteAllDefinitions()
        {
            if (!EditorUtility.DisplayDialog("Delete All Metadata Definitions", "Are you sure you want to delete all metadata definitions? This action cannot be undone.", "Delete", "Cancel"))
                return;

            List<MetadataDefinition> definitions = new List<MetadataDefinition>(_metas);
            foreach (MetadataDefinition meta in definitions)
            {
                Metadata.DeleteDefinition(meta);
            }
        }
    }
}
