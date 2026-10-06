using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed class MetadataEditorUI : BasicEditorUI
    {
        private MetadataDefinition _def;

        public static MetadataEditorUI ShowWindow()
        {
            MetadataEditorUI window = GetWindow<MetadataEditorUI>("Metadata Definition");
            window.minSize = new Vector2(500, 250);
            window.maxSize = window.minSize;
            return window;
        }

        public void Init(MetadataDefinition metadataDefinition = null)
        {
            _def = metadataDefinition;
            if (_def == null) _def = new MetadataDefinition();
            Build();
        }

        private void CreateGUI()
        {
            Build();
        }

        private void Build()
        {
            if (_def == null) _def = new MetadataDefinition();

            VisualElement root = rootVisualElement;
            if (root == null) return;

            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);

            ScrollView body = new ScrollView(ScrollViewMode.Vertical)
            {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Auto
            };
            body.style.flexGrow = 1f;
            body.style.minHeight = 0f;

            VisualElement helpBox = AssetInventoryUITK.CreateHelpBox(
                "Define the additional data that can be attached to packages, then optionally restrict where the field is available.",
                MessageType.Info);
            helpBox.style.flexShrink = 0f;
            body.Add(helpBox);

            VisualElement section = AssetInventoryUITK.CreateSection("Field Definition");
            section.style.flexShrink = 0f;
            Button saveButton = AssetInventoryUITK.CreatePrimaryButton(_def.Id > 0 ? "Update" : "Create", SaveDefinition);

            TextField nameField = new TextField
            {
                value = _def.Name ?? string.Empty
            };
            nameField.RegisterValueChangedCallback(evt =>
            {
                _def.Name = evt.newValue;
                saveButton.SetEnabled(!string.IsNullOrWhiteSpace(_def.Name));
            });
            section.Add(AssetInventoryUITK.CreateFieldRow("Name", nameField));

            EnumField typeField = new EnumField(_def.Type);
            typeField.RegisterValueChangedCallback(evt =>
            {
                _def.Type = (MetadataDefinition.DataType)evt.newValue;
                Build();
            });
            section.Add(AssetInventoryUITK.CreateFieldRow("Type", typeField));

            if (_def.Type == MetadataDefinition.DataType.SingleSelect)
            {
                VisualElement valuesField = AssetInventoryUITK.CreateStringListControl(
                    this,
                    _def.ValueList,
                    ",",
                    value => _def.ValueList = value,
                    "Possible Values",
                    "Comma-separated selectable values.");
                section.Add(AssetInventoryUITK.CreateFieldRow("Possible Values", valuesField));
            }

            Toggle restrictSource = new Toggle
            {
                value = _def.RestrictAssetSource
            };
            restrictSource.RegisterValueChangedCallback(evt =>
            {
                _def.RestrictAssetSource = evt.newValue;
                Build();
            });
            section.Add(AssetInventoryUITK.CreateFieldRow("Restrict to Asset Source", restrictSource));

            if (_def.RestrictAssetSource)
            {
                EnumField sourceField = new EnumField(_def.ApplicableSource);
                sourceField.RegisterValueChangedCallback(evt => _def.ApplicableSource = (Asset.Source)evt.newValue);
                section.Add(AssetInventoryUITK.CreateFieldRow("Asset Source", sourceField));
            }

            body.Add(section);
            root.Add(body);

            saveButton.SetEnabled(!string.IsNullOrWhiteSpace(_def.Name));
            VisualElement footer = AssetInventoryUITK.CreateWindowFooter();
            footer.style.flexShrink = 0f;
            footer.Add(saveButton);
            root.Add(footer);
        }

        private void SaveDefinition()
        {
            if (Metadata.AddDefinition(_def) != null) Close();
        }
    }
}
