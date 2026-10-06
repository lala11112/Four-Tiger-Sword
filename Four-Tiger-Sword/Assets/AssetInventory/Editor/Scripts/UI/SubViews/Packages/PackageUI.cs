using ImpossibleRobert.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed class PackageUI : BasicEditorUI
    {
        private const string PackageRootClass = "ai-package-root";
        private const string PackageInfoHelpClass = "ai-package-info-help";
        private const string PackageSubtitleClass = "ai-package-subtitle";
        private const string PackageLocationRowClass = "ai-package-location-row";
        private const string PackageLocationFieldClass = "ai-package-location-field";
        private const string PackageReferenceSectionClass = "ai-package-reference-section";
        private const string PackageScrollClass = "ai-package-scroll";
        private const string PackageOverviewClass = "ai-package-overview";
        private const string PackageOverviewContentClass = "ai-package-overview-content";
        private const string PackageOverviewDetailsClass = "ai-package-overview-details";
        private const string PackageLocationBodyClass = "ai-package-location-body";
        private const string PackageLocationTextClass = "ai-package-location-text";
        private const string PackageLocationActionsClass = "ai-package-location-actions";
        private const string PackagePreviewClass = "ai-package-preview";
        private const string PackagePreviewImageClass = "ai-package-preview-image";
        private const string PackageInlineLookupClass = "ai-package-inline-lookup";
        private const string PackageSmallButtonClass = "ai-package-small-button";
        private const string PackagePipelineRowClass = "ai-package-pipeline-row";
        private const string PackagePipelineToggleClass = "ai-package-pipeline-toggle";
        private const string PackageDescriptionClass = "ai-package-description";

        private enum Mode
        {
            NewLocation,
            New,
            Edit
        }

        private Mode _mode;
        private AssetInfo _info;
        private Asset _asset;
        private Action<Asset> _onSave;

        private string _newLocation = "https://github.com/WetzoldStudios/traVRsal-sdk.git";
        private int _gitRefSource;
        private int _gitBranchIdx;
        private int _gitTagIdx;
        private int _gitPRIdx;
        private string _gitCommit;
        private string[] _gitRefSourceOptions;
        private GitHandler _git;
        private bool _initDone;

        private string[] _availablePublishers;
        private string[] _availableCategories;
        private string[] _availableUnityVersions;
        private string[] _availableLicenses;
        private Dictionary<string, string> _publisherToSafe;
        private Dictionary<string, string> _categoryToSafe;
        private bool _uitkActive;

        public static PackageUI ShowWindow()
        {
            PackageUI window = GetWindow<PackageUI>("Package Data");
            window.minSize = new Vector2(400, 500);

            return window;
        }

        public void Init(AssetInfo info, Action<Asset> onSave)
        {
            _info = info;
            _mode = info == null || info.Id == 0 ? Mode.NewLocation : Mode.Edit;

            if (_mode == Mode.Edit)
            {
                _asset = DBAdapter.DB.Find<Asset>(_info.AssetId); // load fresh from DB and store that exact copy later again
                _asset.PreviewTexture = _info.PreviewTexture;
                if (_asset.PreviewTexture == null)
                {
                    // create grey texture
                    _asset.PreviewTexture = new Texture2D(100, 100);
                    _asset.PreviewTexture.SetPixel(0, 0, Color.grey);
                    _asset.PreviewTexture.Apply();
                }
            }
            else
            {
                if (_info == null)
                {
                    _info = new AssetInfo();
                    _info.AssetSource = Asset.Source.CustomPackage;
                }
                _asset = _info.ToAsset();
            }
            _onSave = onSave;
            BuildIfReady();
        }

        private void EnsureReferenceOptions()
        {
            if (_gitRefSourceOptions != null) return;
            _gitRefSourceOptions = new[] {"HEAD", "Branch", "Tag", "Pull Request", "Commit"};
        }

        private void InitUI()
        {
            _initDone = true;
            EnsureReferenceOptions();

            LoadDistinctPublishers();
            LoadDistinctCategories();
            LoadDistinctUnityVersions();
            LoadDistinctLicenses();
        }

        private void LoadDistinctPublishers()
        {
            string query = "SELECT DISTINCT DisplayPublisher, SafePublisher FROM Asset WHERE DisplayPublisher IS NOT NULL AND DisplayPublisher != '' ORDER BY DisplayPublisher";
            List<Asset> results = DBAdapter.DB.Query<Asset>(query);

            _publisherToSafe = results
                .Where(a => !string.IsNullOrEmpty(a.DisplayPublisher))
                .GroupBy(a => a.DisplayPublisher)
                .ToDictionary(g => g.Key, g => g.First().SafePublisher ?? string.Empty);

            _availablePublishers = _publisherToSafe.Keys.OrderBy(k => k).ToArray();
        }

        private void LoadDistinctCategories()
        {
            string query = "SELECT DISTINCT DisplayCategory, SafeCategory FROM Asset WHERE DisplayCategory IS NOT NULL AND DisplayCategory != '' ORDER BY DisplayCategory";
            List<Asset> results = DBAdapter.DB.Query<Asset>(query);

            _categoryToSafe = results
                .Where(a => !string.IsNullOrEmpty(a.DisplayCategory))
                .GroupBy(a => a.DisplayCategory)
                .ToDictionary(g => g.Key, g => g.First().SafeCategory ?? string.Empty);

            _availableCategories = _categoryToSafe.Keys.OrderBy(k => k).ToArray();
        }

        private void LoadDistinctUnityVersions()
        {
            string query = "SELECT DISTINCT SupportedUnityVersions FROM Asset WHERE SupportedUnityVersions IS NOT NULL AND SupportedUnityVersions != ''";
            List<string> allVersions = DBAdapter.DB.QueryScalars<string>(query);

            // Split comma-separated values and get distinct individual versions
            List<string> distinctVersions = allVersions
                .SelectMany(v => v.Split(','))
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct()
                .ToList();

            // Sort using SemVer
            _availableUnityVersions = distinctVersions
                .OrderBy(v => new SemVer(v))
                .ToArray();
        }

        private void LoadDistinctLicenses()
        {
            string query = "SELECT DISTINCT License FROM Asset WHERE License IS NOT NULL AND License != '' ORDER BY License";
            _availableLicenses = DBAdapter.DB.QueryScalars<string>(query).ToArray();
        }

        private void CreateGUI()
        {
            _uitkActive = true;
            BuildContent();
        }

        private void BuildIfReady()
        {
            if (_uitkActive && rootVisualElement != null && rootVisualElement.childCount > 0)
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
            root.AddToClassList(PackageRootClass);

            if (ShouldCloseOnNextGUI())
            {
                Close();
                return;
            }

            if (_mode == Mode.NewLocation)
            {
                BuildNewLocationContent(root);
                return;
            }

            if (!_initDone) InitUI();

            BuildEditContent(root);
        }

        private void BuildNewLocationContent(VisualElement root)
        {
            EnsureReferenceOptions();

            Label subtitle = AssetInventoryUITK.CreateCopyLabel("Add a package from a Git repository.");
            subtitle.AddToClassList(PackageSubtitleClass);
            root.Add(subtitle);

            VisualElement location = AssetInventoryUITK.CreateSection("Repository");
            location.Add(AssetInventoryUITK.CreateHelpBox("Enter the URL of the Git repository.", MessageType.Info));

            TextField locationField = new TextField("Location")
            {
                value = _newLocation,
                tooltip = "Enter the full Git repository URL, for example https://github.com/owner/repository.git."
            };
            locationField.AddToClassList(PackageLocationFieldClass);
            locationField.RegisterValueChangedCallback(evt => _newLocation = evt.newValue);
            location.Add(locationField);

            Button next = AssetInventoryUITK.CreatePrimaryButton("Next", GatherGitInfo);
            next.SetEnabled(!string.IsNullOrWhiteSpace(_newLocation));
            next.tooltip = string.IsNullOrWhiteSpace(_newLocation)
                ? "Enter a repository URL to continue."
                : "Load branches, tags, and other references from this repository.";
            locationField.RegisterValueChangedCallback(evt =>
            {
                bool hasLocation = !string.IsNullOrWhiteSpace(evt.newValue);
                next.SetEnabled(hasLocation);
                next.tooltip = hasLocation
                    ? "Load branches, tags, and other references from this repository."
                    : "Enter a repository URL to continue.";
            });

            VisualElement row = new VisualElement();
            row.AddToClassList(PackageLocationRowClass);
            row.Add(AssetInventoryUITK.CreateFlexibleSpacer());
            row.Add(next);

            location.Add(row);
            root.Add(location);

            if (_git != null)
            {
                root.Add(BuildGitReferenceSection());
            }

            root.Add(AssetInventoryUITK.CreateFlexibleSpacer());
        }

        private VisualElement BuildGitReferenceSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Reference");
            section.AddToClassList(PackageReferenceSectionClass);

            if (!_git.IsValid)
            {
                string error = string.IsNullOrWhiteSpace(_git.LastError)
                    ? "Git references could not be loaded. Git support may be unavailable in this editor build."
                    : $"Connection error: {_git.LastError}";
                section.Add(AssetInventoryUITK.CreateHelpBox(error, MessageType.Error));
                return section;
            }

            section.Add(AssetInventoryUITK.CreateHelpBox("Select the correct repository reference.", MessageType.Info));

            PopupField<string> reference = new PopupField<string>(_gitRefSourceOptions.ToList(), Mathf.Clamp(_gitRefSource, 0, _gitRefSourceOptions.Length - 1));
            reference.RegisterValueChangedCallback(evt =>
            {
                _gitRefSource = Array.IndexOf(_gitRefSourceOptions, evt.newValue);
                BuildContent();
            });
            section.Add(AssetInventoryUITK.CreateFieldRow("Reference", reference));

            switch (_gitRefSource)
            {
                case 1:
                    section.Add(CreateGitReferencePicker("Branch", _git.ShortBranches, _gitBranchIdx, value => _gitBranchIdx = value));
                    break;

                case 2:
                    section.Add(CreateGitReferencePicker("Tag", _git.ShortTags, _gitTagIdx, value => _gitTagIdx = value));
                    break;

                case 3:
                    section.Add(CreateGitReferencePicker("Pull Request", _git.ShortPRs, _gitPRIdx, value => _gitPRIdx = value));
                    break;

                case 4:
                    TextField commit = new TextField
                    {
                        value = _gitCommit
                    };
                    commit.RegisterValueChangedCallback(evt => _gitCommit = evt.newValue);
                    section.Add(AssetInventoryUITK.CreateFieldRow("Commit Id", commit));
                    break;
            }

            return section;
        }

        private VisualElement CreateGitReferencePicker(string label, string[] options, int selectedIndex, Action<int> onChange)
        {
            if (options == null || options.Length == 0)
            {
                return AssetInventoryUITK.CreateHelpBox($"No {label.ToLowerInvariant()} references were found.", MessageType.Warning);
            }

            int clampedIndex = Mathf.Clamp(selectedIndex, 0, options.Length - 1);
            PopupField<string> picker = new PopupField<string>(options.ToList(), clampedIndex);
            picker.RegisterValueChangedCallback(evt => onChange?.Invoke(Array.IndexOf(options, evt.newValue)));
            return AssetInventoryUITK.CreateFieldRow(label, picker);
        }

        private void GatherGitInfo()
        {
            if (string.IsNullOrWhiteSpace(_newLocation)) return;

            _git = new GitHandler(_newLocation);
            _git.GatherRemoteInfo();
            BuildIfReady();
        }

        private void BuildEditContent(VisualElement root)
        {
            VisualElement info = AssetInventoryUITK.CreateHelpBox("Update package data. Technical names are required for filters and dropdowns.", MessageType.Info);
            info.AddToClassList(PackageInfoHelpClass);
            root.Add(info);

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList(PackageScrollClass);
            scroll.Add(BuildOverviewSection());
            scroll.Add(BuildIdentitySection());
            scroll.Add(BuildCompatibilitySection());
            scroll.Add(BuildCommercialSection());
            scroll.Add(BuildDescriptionSection());
            root.Add(scroll);

            VisualElement footer = AssetInventoryUITK.CreateWindowFooter();
            Button save = AssetInventoryUITK.CreatePrimaryButton(_mode == Mode.New ? "Create" : "Save", () =>
            {
                if (SaveData())
                {
                    Close();
                }
            });
            footer.Add(save);
            root.Add(footer);
        }

        private VisualElement BuildOverviewSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Package");
            section.AddToClassList(PackageOverviewClass);

            VisualElement content = new VisualElement();
            content.AddToClassList(PackageOverviewContentClass);

            VisualElement details = new VisualElement();
            details.AddToClassList(PackageOverviewDetailsClass);
            details.Add(AssetInventoryUITK.CreateKeyValueRow("Type", StringUtils.CamelCaseToWords(_asset.AssetSource.ToString()) + (_asset.AssetSource == Asset.Source.RegistryPackage ? $" ({_asset.PackageSource})" : "")));

            VisualElement locationBody = new VisualElement();
            locationBody.AddToClassList(PackageLocationBodyClass);
            Label locationLabel = new Label("Location");
            locationLabel.AddToClassList("ai-key-value-label");
            locationBody.Add(locationLabel);
            Label locationText = new Label(GetLocationDisplayText());
            locationText.AddToClassList(PackageLocationTextClass);
            locationBody.Add(locationText);
            details.Add(locationBody);

            if (_mode == Mode.Edit && CanChangeLocation(_asset))
            {
                VisualElement actions = new VisualElement();
                actions.AddToClassList(PackageLocationActionsClass);

                Button setLocation = AssetInventoryUITK.CreateSecondaryButton("Set Location...", ChangeLocation);
                setLocation.tooltip = "Reconnect this catalog entry to an existing file or folder without moving it.";
                actions.Add(setLocation);

                if (PackageLocationRelocator.SupportsRenameFile(_asset))
                {
                    Button rename = null;
                    rename = AssetInventoryUITK.CreateSecondaryButton("Rename File...", () => RenameFile(rename));
                    bool canRename = PackageLocationRelocator.CanRenameFile(_asset, out string renameReason, _ => false);
                    rename.SetEnabled(canRename);
                    rename.tooltip = canRename
                        ? "Rename the source file without changing the package name, identity, metadata, or directory. The extension is preserved automatically."
                        : renameReason;
                    actions.Add(rename);
                }
                details.Add(actions);
            }

            content.Add(details);

            if (_mode == Mode.Edit)
            {
                content.Add(BuildPreviewControl());
            }

            section.Add(content);
            return section;
        }

        private VisualElement BuildPreviewControl()
        {
            VisualElement preview = new VisualElement();
            preview.AddToClassList(PackagePreviewClass);

            if (_asset.PreviewTexture != null)
            {
                Image image = new Image
                {
                    image = _asset.PreviewTexture,
                    scaleMode = ScaleMode.ScaleToFit
                };
                image.AddToClassList(PackagePreviewImageClass);
                preview.Add(image);
            }

            Button change = AssetInventoryUITK.CreateSecondaryButton("Change...", ChangePreview);
            preview.Add(change);
            return preview;
        }

        private VisualElement BuildIdentitySection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Identity");

            section.Add(CreateTextFieldRow("Name", _asset.DisplayName, value => _asset.DisplayName = value, "Overrides the technical name."));

            TextField safeName = CreateTextField(_asset.SafeName, value => _asset.SafeName = value);
            safeName.SetEnabled(false);
            section.Add(AssetInventoryUITK.CreateFieldRow("Technical Name", safeName));

            section.Add(CreateLookupTextFieldRow("Publisher", _asset.DisplayPublisher, value => _asset.DisplayPublisher = value, _availablePublishers, value =>
            {
                _asset.DisplayPublisher = value;
                if (_publisherToSafe != null && _publisherToSafe.TryGetValue(value, out string safeValue))
                {
                    _asset.SafePublisher = safeValue;
                }
                BuildIfReady();
            }, "Overrides the technical publisher name."));

            section.Add(CreateTextFieldRow("Technical Publisher", _asset.SafePublisher, value => _asset.SafePublisher = value));

            section.Add(CreateLookupTextFieldRow("Category", _asset.DisplayCategory, value => _asset.DisplayCategory = value, _availableCategories, value =>
            {
                _asset.DisplayCategory = value;
                if (_categoryToSafe != null && _categoryToSafe.TryGetValue(value, out string safeValue))
                {
                    _asset.SafeCategory = safeValue;
                }
                BuildIfReady();
            }, "Overrides the technical category name."));

            section.Add(CreateTextFieldRow("Technical Category", _asset.SafeCategory, value => _asset.SafeCategory = value));
            return section;
        }

        private VisualElement BuildCompatibilitySection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Compatibility");
            section.Add(CreateTextFieldRow("Version", _asset.Version, value => _asset.Version = value));

            section.Add(AssetInventoryUITK.CreateFieldRow("Unity Versions",
                AssetInventoryUITK.CreateStringListControl(
                    this,
                    _asset.SupportedUnityVersions,
                    ",",
                    value => _asset.SupportedUnityVersions = value,
                    "Unity Versions",
                    "Comma-separated supported Unity versions.")));

            if (_availableUnityVersions != null && _availableUnityVersions.Length > 0)
            {
                section.Add(CreateKnownVersionPicker());
            }

            VisualElement pipelines = new VisualElement();
            pipelines.AddToClassList(PackagePipelineRowClass);
            pipelines.Add(CreatePipelineToggle("BIRP", _asset.BIRPCompatible, value => _asset.BIRPCompatible = value));
            pipelines.Add(CreatePipelineToggle("URP", _asset.URPCompatible, value => _asset.URPCompatible = value));
            pipelines.Add(CreatePipelineToggle("HDRP", _asset.HDRPCompatible, value => _asset.HDRPCompatible = value));
            section.Add(AssetInventoryUITK.CreateFieldRow("Render Pipelines", pipelines));
            return section;
        }

        private VisualElement CreateKnownVersionPicker()
        {
            string current = _availableUnityVersions.Contains(_asset.SupportedUnityVersions)
                ? _asset.SupportedUnityVersions
                : _availableUnityVersions[0];
            PopupField<string> picker = new PopupField<string>(_availableUnityVersions.ToList(), current);
            picker.RegisterValueChangedCallback(evt =>
            {
                _asset.SupportedUnityVersions = evt.newValue;
                BuildIfReady();
            });
            return AssetInventoryUITK.CreateFieldRow("Known Version", picker);
        }

        private Toggle CreatePipelineToggle(string label, bool value, Action<bool> onChange)
        {
            Toggle toggle = new Toggle(label)
            {
                value = value
            };
            toggle.AddToClassList(PackagePipelineToggleClass);
            toggle.RegisterValueChangedCallback(evt => onChange?.Invoke(evt.newValue));
            return toggle;
        }

        private VisualElement BuildCommercialSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("License & Price");
            section.Add(CreateLookupTextFieldRow("License", _asset.License, value => _asset.License = value, _availableLicenses, value =>
            {
                _asset.License = value;
                BuildIfReady();
            }));
            section.Add(CreateTextFieldRow("License Location", _asset.LicenseLocation, value => _asset.LicenseLocation = value));
            section.Add(CreateFloatFieldRow("Price EUR", _asset.PriceEur, value => _asset.PriceEur = value));
            section.Add(CreateFloatFieldRow("Price USD", _asset.PriceUsd, value => _asset.PriceUsd = value));
            section.Add(CreateFloatFieldRow("Price CNY", _asset.PriceCny, value => _asset.PriceCny = value));
            return section;
        }

        private VisualElement BuildDescriptionSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Description");
            TextField description = CreateTextField(_asset.Description, value => _asset.Description = value);
            description.multiline = true;
            description.AddToClassList(PackageDescriptionClass);
            section.Add(description);
            return section;
        }

        private VisualElement CreateTextFieldRow(string label, string value, Action<string> onChange, string tooltip = null)
        {
            return AssetInventoryUITK.CreateTextFieldRow(label, value, onChange, tooltip);
        }

        private TextField CreateTextField(string value, Action<string> onChange)
        {
            TextField field = new TextField
            {
                value = value ?? string.Empty
            };
            field.RegisterValueChangedCallback(evt => onChange?.Invoke(evt.newValue));
            return field;
        }

        private VisualElement CreateFloatFieldRow(string label, float value, Action<float> onChange)
        {
            return AssetInventoryUITK.CreateFloatFieldRow(label, value, onChange, isDelayed: false);
        }

        private VisualElement CreateLookupTextFieldRow(
            string label,
            string value,
            Action<string> onChange,
            string[] options,
            Action<string> onLookupSelect,
            string tooltip = null)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(PackageInlineLookupClass);

            TextField field = CreateTextField(value, onChange);
            field.tooltip = tooltip ?? string.Empty;
            field.AddToClassList("ai-inline-grow");
            row.Add(field);

            Button lookup = new Button(() => ShowDropdownMenu(options, onLookupSelect, row))
            {
                text = "..."
            };
            lookup.tooltip = "Select from existing values";
            lookup.AddToClassList(PackageSmallButtonClass);
            row.Add(lookup);

            return AssetInventoryUITK.CreateFieldRow(label, row);
        }

        internal bool ChangeLocationForTests(string selectedPath)
        {
            return TryChangeLocation(selectedPath, out _);
        }

        internal bool RenameFileForTests(string newBaseName)
        {
            return TryRenameFile(newBaseName, out _);
        }

        private void ChangeLocation()
        {
            string currentLocation = _asset.GetLocation(true);
            string selectedPath;

            if (RequiresFolderLocation(_asset))
            {
                string startFolder = Directory.Exists(currentLocation) ? currentLocation : Path.GetDirectoryName(currentLocation);
                selectedPath = EditorUtility.OpenFolderPanel("Select Package Location", startFolder ?? string.Empty, string.Empty);
            }
            else
            {
                string startFolder = string.IsNullOrWhiteSpace(currentLocation) ? string.Empty : Path.GetDirectoryName(currentLocation);
                selectedPath = EditorUtility.OpenFilePanel("Select Package Location", startFolder ?? string.Empty, string.Empty);
            }

            if (string.IsNullOrWhiteSpace(selectedPath)) return;

            if (!TryChangeLocation(selectedPath, out string errorMessage))
            {
                EditorUtility.DisplayDialog("Set Location Failed", errorMessage, "OK");
            }
            else
            {
                BuildIfReady();
            }
        }

        private bool TryChangeLocation(string selectedPath, out string errorMessage)
        {
            if (!PackageLocationRelocator.TrySetLocation(_asset, selectedPath, out errorMessage)) return false;

            SyncRelocatedLocation();
            return true;
        }

        private void RenameFile(Button anchor)
        {
            if (_asset == null || anchor == null) return;

            string sourcePath = _asset.GetLocation(true);
            string currentBaseName = string.IsNullOrWhiteSpace(sourcePath) ? string.Empty : Path.GetFileNameWithoutExtension(sourcePath);
            NameWindow.ShowAsDropDown(
                CommonUITK.ToScreenDropdownAnchor(this, anchor),
                currentBaseName,
                newBaseName =>
                {
                    if (!TryRenameFile(newBaseName, out string errorMessage))
                    {
                        EditorUtility.DisplayDialog("Rename File Failed", errorMessage, "OK");
                        BuildIfReady();
                        return;
                    }

                    BuildIfReady();
                    ShowNotification(new GUIContent("Package file renamed."), 1.5f);
                },
                false,
                "Filename (without extension)");
        }

        private bool TryRenameFile(string newBaseName, out string errorMessage)
        {
            if (!PackageLocationRelocator.TryRenameFile(_asset, newBaseName, out errorMessage)) return false;

            SyncRelocatedLocation();
            return true;
        }

        private void SyncRelocatedLocation()
        {
            if (_info != null) _info.SetLocation(_asset.Location);
            _onSave?.Invoke(_asset);
        }

        internal bool ShouldCloseOnNextGUI()
        {
            if (_mode != Mode.Edit) return false;
            if (_asset == null) return true;
            if (!string.IsNullOrEmpty(_asset.Location)) return false;

            return _asset.AssetSource != Asset.Source.AssetManager || _asset.ParentId > 0;
        }

        internal string GetLocationDisplayTextForTests()
        {
            return GetLocationDisplayText();
        }

        private string GetLocationDisplayText()
        {
            if (_asset == null) return null;
            if (!string.IsNullOrEmpty(_asset.Location)) return _asset.Location;

            return _asset.AssetSource == Asset.Source.AssetManager && _asset.ParentId <= 0 ? "-Remote-" : _asset.Location;
        }

        private static bool CanChangeLocation(Asset asset)
        {
            return PackageLocationRelocator.SupportsSetLocation(asset);
        }

        private static bool RequiresFolderLocation(Asset asset)
        {
            return asset.AssetSource == Asset.Source.Directory
                || (asset.AssetSource == Asset.Source.RegistryPackage
                    && (asset.PackageSource == PackageSource.Embedded
                        || asset.PackageSource == PackageSource.Local));
        }

        private void ChangePreview()
        {
            string assetPreviewFile = EditorUtility.OpenFilePanel("Select image", "", "png");
            if (string.IsNullOrEmpty(assetPreviewFile)) return;

            try
            {
                // load immediately
                byte[] fileData = File.ReadAllBytes(assetPreviewFile);
                Texture2D tex = new Texture2D(2, 2);
                tex.LoadImage(fileData);

                // copy file
                string targetDir = Path.Combine(Paths.GetPreviewFolder(), _asset.Id.ToString());
                string targetFile = Path.Combine(targetDir, "a-" + _asset.Id + Path.GetExtension(assetPreviewFile));
                Directory.CreateDirectory(targetDir);
                File.Copy(assetPreviewFile, targetFile, true);
                AssetUtils.RemoveFromPreviewCache(targetFile);

                // set once all critical parts are done
                _asset.PreviewTexture = tex;
                _info.PreviewTexture = tex;
                BuildIfReady();
            }
            catch (Exception e)
            {
                Debug.LogError("Error loading image: " + e.Message);
            }
        }

        private bool SaveData()
        {
            if (string.IsNullOrWhiteSpace(_asset.DisplayName) && string.IsNullOrWhiteSpace(_asset.SafeName))
            {
                EditorUtility.DisplayDialog("Error", "Either name or technical name must be set.", "OK");
                return false;
            }
            if ((_asset.SafeCategory != null && _asset.SafeCategory.Contains("/"))
                || (_asset.SafePublisher != null && _asset.SafePublisher.Contains("/"))
               )
            {
                EditorUtility.DisplayDialog("Error", "Safe items must not contain any forward slashes.", "OK");
                return false;
            }

            DBAdapter.DB.Update(_asset);

            _onSave?.Invoke(_asset);

            return true;
        }

        private void ShowDropdownMenu(string[] options, Action<string> onSelect)
        {
            ShowDropdownMenu(options, onSelect, null);
        }

        private void ShowDropdownMenu(string[] options, Action<string> onSelect, VisualElement anchor)
        {
            GenericMenu menu = new GenericMenu();

            if (options == null || options.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent("No existing values found"));
            }
            else
            {
                foreach (string option in options)
                {
                    string capturedOption = option;
                    menu.AddItem(new GUIContent(option), false, () =>
                    {
                        onSelect?.Invoke(capturedOption);
                    });
                }
            }

            if (anchor != null)
            {
                CommonUITK.ShowGenericMenu(menu, anchor);
            }
            else
            {
                menu.ShowAsContext();
            }
        }

        private void ShowHierarchicalVersionMenu(string[] options, Action<string> onSelect)
        {
            GenericMenu menu = new GenericMenu();

            if (options == null || options.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent("No existing values found"));
            }
            else
            {
                foreach (string option in options)
                {
                    string capturedOption = option;
                    string menuPath = BuildVersionMenuPath(option);
                    menu.AddItem(new GUIContent(menuPath), false, () =>
                    {
                        onSelect?.Invoke(capturedOption);
                    });
                }
            }

            menu.ShowAsContext();
        }

        private string BuildVersionMenuPath(string version)
        {
            string[] parts = version.Split('.');
            if (parts.Length == 0) return version;

            // Build hierarchical path: "2019/2019.4/2019.4.1"
            string path = parts[0];
            string accumulated = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                accumulated += "." + parts[i];
                path += "/" + accumulated;
            }

            return path;
        }
    }
}
