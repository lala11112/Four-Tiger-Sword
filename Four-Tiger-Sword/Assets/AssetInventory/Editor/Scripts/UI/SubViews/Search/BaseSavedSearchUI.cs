using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public abstract class BaseSavedSearchUI<T> : BasicEditorUI where T : class
    {
        protected T _savedSearch;
        private Action<T> _onSave;
        private Button _updateButton;

        protected void InitSavedSearch(T savedSearch, Action<T> onSave = null)
        {
            _savedSearch = savedSearch;
            _onSave = onSave;
            BuildContent();
        }

        protected abstract string GetName();
        protected abstract void SetName(string searchName);
        protected abstract string GetIcon();
        protected abstract void SetIcon(string icon);
        protected abstract string GetColor();
        protected abstract void SetColor(string color);
        protected abstract string GetSearchPhrase();
        protected abstract string GetSearchDetails();
        protected abstract void UpdateDatabase();

        protected void BuildContent()
        {
            VisualElement root = rootVisualElement;
            if (root == null) return;

            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);

            if (_savedSearch == null)
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("Select a saved search before editing.", MessageType.Info));
                return;
            }

            root.Add(BuildSummarySection());
            root.Add(BuildEditorSection());
            root.Add(AssetInventoryUITK.CreateFlexibleSpacer());

            _updateButton = AssetInventoryUITK.CreatePrimaryButton("Update", Save);
            _updateButton.SetEnabled(CanSave());
            VisualElement footer = AssetInventoryUITK.CreateWindowFooter();
            footer.Add(_updateButton);
            root.Add(footer);
        }

        private VisualElement BuildSummarySection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Saved Search");
            string searchPhrase = GetSearchPhrase();
            if (!string.IsNullOrEmpty(searchPhrase))
            {
                section.Add(AssetInventoryUITK.CreateKeyValueRow("Search Phrase", searchPhrase));
            }

            string details = GetSearchDetails();
            if (!string.IsNullOrEmpty(details))
            {
                section.Add(AssetInventoryUITK.CreateKeyValueRow("Filters", details));
            }

            return section;
        }

        private VisualElement BuildEditorSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Display");

            TextField nameField = new TextField
            {
                value = GetName() ?? string.Empty
            };
            nameField.RegisterValueChangedCallback(evt =>
            {
                SetName(evt.newValue);
                _updateButton?.SetEnabled(CanSave());
            });
            section.Add(AssetInventoryUITK.CreateFieldRow("Name", nameField));

            Color currentColor = Color.white;
            string colorStr = GetColor();
            if (!string.IsNullOrEmpty(colorStr))
            {
                ColorUtility.TryParseHtmlString("#" + colorStr, out currentColor);
            }

            ColorField colorField = new ColorField
            {
                value = currentColor,
                showAlpha = false,
                hdr = false
            };
            colorField.RegisterValueChangedCallback(evt => SetColor(ColorUtility.ToHtmlStringRGB(evt.newValue)));
            section.Add(AssetInventoryUITK.CreateFieldRow("Color", colorField));

            section.Add(AssetInventoryUITK.CreateFieldRow("Icon", CreateIconRow()));
            return section;
        }

        private VisualElement CreateIconRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("ai-icon-preview-row");

            string icon = GetIcon();
            if (!string.IsNullOrEmpty(icon))
            {
                Texture iconTexture = IconSelectionUI.GetIconTexture(icon);
                if (iconTexture != null)
                {
                    Image image = new Image
                    {
                        image = iconTexture,
                        scaleMode = ScaleMode.ScaleToFit
                    };
                    image.AddToClassList("ai-icon-preview");
                    row.Add(image);
                }
                else
                {
                    row.Add(AssetInventoryUITK.CreateCopyLabel(icon));
                }
            }
            else
            {
                Label empty = new Label("No icon selected");
                empty.AddToClassList("ai-icon-preview-empty");
                row.Add(empty);
            }

            row.Add(AssetInventoryUITK.CreateFlexibleSpacer());
            Button selectButton = AssetInventoryUITK.CreateSecondaryButton("Select...", null);
            selectButton.clicked += () => SelectIcon(selectButton);
            row.Add(selectButton);
            if (!string.IsNullOrEmpty(icon))
            {
                row.Add(AssetInventoryUITK.CreateSecondaryButton("Clear", ClearIcon));
            }

            return row;
        }

        private void SelectIcon(VisualElement anchor)
        {
            IconSelectionUI.ShowDropdown(this, anchor, iconName =>
            {
                SetIcon(iconName);
                BuildContent();
            });
        }

        private void ClearIcon()
        {
            SetIcon(null);
            BuildContent();
        }

        private bool CanSave()
        {
            return !string.IsNullOrWhiteSpace(GetName()) || !string.IsNullOrWhiteSpace(GetIcon());
        }

        private void Save()
        {
            if (!CanSave())
            {
                EditorUtility.DisplayDialog("Invalid Name", "Please enter a name or set an icon for the saved search.", "OK");
                return;
            }

            UpdateDatabase();
            _onSave?.Invoke(_savedSearch);

            Close();
        }
    }
}
