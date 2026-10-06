using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed class FolderSettingsUI : EditorWindow
    {
        private static readonly Vector2 WindowSize = new Vector2(460f, 430f);
        private static readonly string[] PackageModeOptions = {"Root Folder", "First Level Directories", "Second Level Directories"};

        private FolderSpec _spec;

        public static FolderSettingsUI ShowDropdown(Rect anchor, FolderSpec spec)
        {
            FolderSettingsUI window = CreateInstance<FolderSettingsUI>();
            window.titleContent = new GUIContent("Folder Settings");
            window.minSize = WindowSize;
            window.Init(spec);
            AssetInventoryUITK.ShowAsDropDown(window, anchor, WindowSize);
            return window;
        }

        public static FolderSettingsUI ShowDropdown(EditorWindow owner, VisualElement anchor, FolderSpec spec)
        {
            FolderSettingsUI window = CreateInstance<FolderSettingsUI>();
            window.titleContent = new GUIContent("Folder Settings");
            window.minSize = WindowSize;
            window.Init(spec);
            AssetInventoryUITK.ShowAsDropDown(window, owner, anchor, WindowSize);
            return window;
        }

        public static FolderSettingsUI ShowWindow(FolderSpec spec = null)
        {
            FolderSettingsUI window = GetWindow<FolderSettingsUI>("Folder Settings");
            window.minSize = WindowSize;
            window.Init(spec);
            return window;
        }

        public void Init(FolderSpec spec)
        {
            _spec = spec;
            BuildIfReady();
        }

        private void CreateGUI()
        {
            BuildContent();
        }

        private void BuildIfReady()
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

            if (_spec == null)
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("No folder selected.", MessageType.Warning));
                root.Add(AssetInventoryUITK.CreateFlexibleSpacer());
                return;
            }

            ScrollView scroll = new ScrollView();
            scroll.AddToClassList("ai-folder-settings-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;

            if (ShouldShowAdvanced())
            {
                scroll.Add(CreateLocationSection());
            }

            scroll.Add(CreateContentSection());
            scroll.Add(CreateOptionsSection());

            if (ShouldShowAdvanced())
            {
                scroll.Add(CreateAdvancedSection());
            }

            root.Add(scroll);
        }

        private VisualElement CreateContentSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection();

            List<string> folderTypes = new List<string>(IndexUI.FolderTypes);
            int selectedFolderType = Mathf.Clamp(_spec.folderType, 0, folderTypes.Count - 1);
            PopupField<string> contentType = new PopupField<string>(folderTypes, selectedFolderType);
            contentType.tooltip = "Type of content to scan for.";
            contentType.RegisterValueChangedCallback(evt =>
            {
                _spec.folderType = folderTypes.IndexOf(evt.newValue);
                SaveAndRebuild();
            });
            section.Add(AssetInventoryUITK.CreateFieldRow("Content", contentType));

            switch (_spec.folderType)
            {
                case 0:
                    section.Add(CreateDisabledToggleRow("Assign Package", true, "Files are connected to a package named after the package file."));
                    section.Add(CreateAssignTagSection());
                    break;

                case 1:
                    AddMediaFields(section);
                    break;

                case 2:
                    section.Add(CreateDisabledToggleRow("Assign Package", true, "Files are connected to a package named after the archive."));
                    section.Add(CreateStringListField("Exclude Extensions", _spec.excludedExtensions, value =>
                    {
                        _spec.excludedExtensions = value;
                        SaveConfig();
                    }, "Excluded Extensions", "e.g. blend,max"));
                    section.Add(CreatePreviewToggle());
                    section.Add(CreateDetectUnityToggle());
                    section.Add(CreateAssignTagSection());
                    break;

                case 3:
                    section.Add(CreatePreviewToggle());
                    section.Add(CreateDetectUnityToggle());
                    section.Add(CreateAssignTagSection());
                    break;
            }

            return section;
        }

        private void AddMediaFields(VisualElement section)
        {
            List<string> mediaTypes = new List<string>(IndexUI.MediaTypes);
            int selectedScanFor = Mathf.Clamp(_spec.scanFor, 0, mediaTypes.Count - 1);
            PopupField<string> scanFor = new PopupField<string>(mediaTypes, selectedScanFor);
            scanFor.tooltip = "File types to search for.";
            scanFor.RegisterValueChangedCallback(evt =>
            {
                _spec.scanFor = mediaTypes.IndexOf(evt.newValue);
                SaveAndRebuild();
            });
            section.Add(AssetInventoryUITK.CreateFieldRow("Find", scanFor));

            if (_spec.scanFor == 7)
            {
                TextField pattern = new TextField {value = _spec.pattern ?? string.Empty, tooltip = "e.g. *.jpg;*.wav"};
                pattern.RegisterValueChangedCallback(evt =>
                {
                    _spec.pattern = evt.newValue;
                    SaveConfig();
                });
                section.Add(AssetInventoryUITK.CreateFieldRow("Pattern", pattern));
            }

            section.Add(CreateStringListField("Exclude Extensions", _spec.excludedExtensions, value =>
            {
                _spec.excludedExtensions = value;
                SaveConfig();
            }, "Excluded Extensions", "e.g. blend,max"));

            section.Add(CreatePreviewToggle());
            section.Add(CreateToggleRow("Remove Orphans", _spec.removeOrphans, "Checks for deleted files and removes them from the index.", value =>
            {
                _spec.removeOrphans = value;
                SaveConfig();
            }));

            section.Add(CreateToggleRow("Assign Package", _spec.attachToPackage, $"Connects indexed files to packages. Otherwise lists them under '{Asset.NONE}'.", value =>
            {
                _spec.attachToPackage = value;
                SaveAndRebuild();
            }));

            if (_spec.attachToPackage)
            {
                List<string> packageModes = new List<string>(PackageModeOptions);
                int selectedPackageMode = Mathf.Clamp(_spec.packageMode, 0, packageModes.Count - 1);
                PopupField<string> packageMode = new PopupField<string>(packageModes, selectedPackageMode);
                packageMode.tooltip = "Controls how the folder structure maps to packages.";
                packageMode.RegisterValueChangedCallback(evt =>
                {
                    _spec.packageMode = packageModes.IndexOf(evt.newValue);
                    SaveConfig();
                });
                section.Add(AssetInventoryUITK.CreateFieldRow("Package Mode", packageMode));
            }

            section.Add(CreateDetectUnityToggle());
            if (_spec.attachToPackage) section.Add(CreateAssignTagSection());
        }

        private VisualElement CreateOptionsSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection();
            section.Add(CreateToggleRow("Check File Sizes", _spec.checkSize, "Updates files when their size changes. Can slow indexing on network or slow drives.", value =>
            {
                _spec.checkSize = value;
                SaveConfig();
            }));
            return section;
        }

        private VisualElement CreateAdvancedSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection();

            section.Add(CreateStringListField("Exclude Directories", _spec.excludedDirectories, value =>
            {
                _spec.excludedDirectories = value;
                SaveConfig();
            }, "Excluded Directories", "e.g. node_modules,temp,build"));

            Button relativeButton = AssetInventoryUITK.CreateSecondaryButton(_spec.storeRelative ? "Disable..." : "Enable...", OpenRelativeSettings);
            relativeButton.tooltip = "Persists file paths relative to a named base folder so the database can be reused from different systems.";
            section.Add(AssetInventoryUITK.CreateFieldRow("Store Relative", relativeButton));
            return section;
        }

        private VisualElement CreateAssignTagSection()
        {
            VisualElement container = new VisualElement();
            container.AddToClassList("ai-folder-settings-subgroup");

            container.Add(CreateToggleRow("Assign Tags", _spec.assignTag, "Assigns tags to all found packages for easier filtering.", value =>
            {
                _spec.assignTag = value;
                SaveAndRebuild();
            }));

            if (_spec.assignTag)
            {
                container.Add(CreateStringListField("Tags", _spec.tag, value =>
                {
                    _spec.tag = value;
                    SaveConfig();
                }, "Package Tags", "e.g. essential,2d"));
            }

            return container;
        }

        private VisualElement CreateLocationSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection();
            string expandedPath = _spec.GetLocation(true);
            bool pathExists = !string.IsNullOrEmpty(expandedPath) && Directory.Exists(expandedPath);

            Label path = AssetInventoryUITK.CreateCopyLabel(string.IsNullOrWhiteSpace(expandedPath) ? "<Invalid Path>" : expandedPath);
            path.AddToClassList("ai-folder-location-path");
            if (!pathExists) path.AddToClassList("ai-folder-location-missing");
            section.Add(AssetInventoryUITK.CreateFieldRow("Location", path));

            if (!pathExists)
            {
                section.Add(AssetInventoryUITK.CreateHelpBox("Folder not found. Use Change Location to point to the new folder location.", MessageType.Warning));
            }

            VisualElement actions = new VisualElement();
            actions.AddToClassList("ai-list-actions");
            actions.AddToClassList("ai-folder-location-actions");
            if (pathExists)
            {
                Button open = AssetInventoryUITK.CreateSecondaryButton("Open", () => EditorUtility.RevealInFinder(expandedPath));
                open.tooltip = "Reveal the folder in the file browser.";
                actions.Add(open);

                Button rename = AssetInventoryUITK.CreateSecondaryButton("Rename...", null);
                rename.clicked += () => OnRenameFolder(rename);
                rename.tooltip = "Rename the folder on disk and update the path.";
                actions.Add(rename);
            }

            Button change = AssetInventoryUITK.CreateSecondaryButton("Change...", OnChangeLocation);
            change.tooltip = "Select this source folder at its new location. Asset Inventory updates its indexed paths automatically.";
            actions.Add(change);
            section.Add(AssetInventoryUITK.CreateFieldRow("Actions", actions));
            return section;
        }

        private VisualElement CreatePreviewToggle()
        {
            return CreateToggleRow("Create Previews", _spec.createPreviews, "Generates previews and additional metadata but requires more indexing time.", value =>
            {
                _spec.createPreviews = value;
                SaveConfig();
            });
        }

        private VisualElement CreateDetectUnityToggle()
        {
            return CreateToggleRow("Unity Projects", _spec.detectUnityProjects, "Detect Unity projects and index only the Assets folder when the selected folder is a project root.", value =>
            {
                _spec.detectUnityProjects = value;
                SaveConfig();
            });
        }

        private static VisualElement CreateDisabledToggleRow(string label, bool value, string tooltip)
        {
            VisualElement row = AssetInventoryUITK.CreateToggleFieldRow(label, value, null, tooltip);
            Toggle toggle = row.Q<Toggle>();
            toggle.SetEnabled(false);
            return row;
        }

        private static VisualElement CreateToggleRow(string label, bool value, string tooltip, Action<bool> onChange)
        {
            return AssetInventoryUITK.CreateToggleFieldRow(label, value, onChange, tooltip, "ai-folder-settings-toggle");
        }

        private VisualElement CreateStringListField(string label, string value, Action<string> onChange, string tooltip, string placeholder)
        {
            VisualElement fieldRow = AssetInventoryUITK.CreateStringListControl(
                this,
                value,
                ",",
                onChange,
                tooltip,
                tooltip,
                "ai-folder-settings-text-field",
                "ai-folder-settings-list-button");

            VisualElement container = new VisualElement();
            container.AddToClassList("ai-folder-settings-list-field");
            container.Add(AssetInventoryUITK.CreateFieldRow(label, fieldRow));
            if (!string.IsNullOrWhiteSpace(placeholder))
            {
                Label hint = AssetInventoryUITK.CreateCopyLabel(placeholder);
                hint.AddToClassList("ai-folder-settings-hint");
                container.Add(hint);
            }
            return container;
        }

        private void OpenRelativeSettings()
        {
            RelativeUI relativeUI = RelativeUI.ShowWindow();
            relativeUI.Init(_spec);
            Close();
        }

        private void SaveConfig()
        {
            AI.SaveConfig();
        }

        private void SaveAndRebuild()
        {
            AI.SaveConfig();
            BuildContent();
        }

        private static bool ShouldShowAdvanced()
        {
            return AI.ShowAdvanced();
        }

        private void OnRenameFolder(Button anchor)
        {
            string currentPath = _spec.GetLocation(true);
            if (string.IsNullOrEmpty(currentPath) || !Directory.Exists(currentPath))
            {
                EditorUtility.DisplayDialog("Invalid Path", "The folder does not exist and cannot be renamed.", "OK");
                return;
            }

            string currentFolderName = Path.GetFileName(currentPath);
            if (string.IsNullOrEmpty(currentFolderName))
            {
                // Handle root paths - can't rename root
                EditorUtility.DisplayDialog("Cannot Rename", "Root directories cannot be renamed.", "OK");
                return;
            }

            NameWindow.ShowAsDropDown(CommonUITK.ToScreenDropdownAnchor(this, anchor), currentFolderName, newName =>
            {
                if (currentFolderName == newName) return;
                if (string.IsNullOrWhiteSpace(newName))
                {
                    EditorUtility.DisplayDialog("Invalid Name", "Folder name cannot be empty.", "OK");
                    return;
                }

                string parentDir = Path.GetDirectoryName(currentPath);
                if (string.IsNullOrEmpty(parentDir))
                {
                    EditorUtility.DisplayDialog("Error", "Cannot determine parent directory.", "OK");
                    return;
                }

                string newPath = Path.Combine(parentDir, newName);

                if (Directory.Exists(newPath) || File.Exists(newPath))
                {
                    EditorUtility.DisplayDialog("Folder Exists", $"A folder named '{newName}' already exists in that location.", "OK");
                    return;
                }

                if (!FolderLocationRelocator.TryGetRelocationGroup(_spec, true, out List<FolderSpec> movingSpecs, out string groupError))
                {
                    EditorUtility.DisplayDialog("Rename Failed", groupError, "OK");
                    return;
                }

                if (!FolderLocationRelocator.TryCreatePlan(_spec, newPath, movingSpecs, FolderLocationRelocator.Operation.RenameOnDisk, out FolderLocationRelocator.Plan plan, out string planError))
                {
                    EditorUtility.DisplayDialog("Rename Failed", planError, "OK");
                    return;
                }

                bool folderMoved = false;
                bool relocationApplied = false;
                try
                {
                    Directory.Move(currentPath, newPath);
                    folderMoved = true;

                    if (!FolderLocationRelocator.TryApply(plan, AI.TrySaveConfig, out string applyError))
                    {
                        string rollbackError = TryRollbackFolderMove(newPath, currentPath);
                        string message = $"Failed to update the indexed paths: {applyError}";
                        if (!string.IsNullOrWhiteSpace(rollbackError)) message += $" The folder could not be moved back: {rollbackError}";
                        EditorUtility.DisplayDialog("Rename Failed", message, "OK");
                        return;
                    }

                    relocationApplied = true;
                    TriggerPackageRefreshSafely();
                    Close();
                }
                catch (Exception e)
                {
                    if (relocationApplied)
                    {
                        Debug.LogWarning($"The folder was renamed and its indexed paths were updated, but the settings popup could not finish closing: {e.Message}");
                        return;
                    }

                    string rollbackError = folderMoved ? TryRollbackFolderMove(newPath, currentPath) : null;
                    string message = $"Failed to rename folder: {e.Message}";
                    if (!string.IsNullOrWhiteSpace(rollbackError)) message += $" The folder could not be moved back: {rollbackError}";
                    EditorUtility.DisplayDialog("Rename Failed", message, "OK");
                }
            }, false, "Rename Folder");
        }

        private void OnChangeLocation()
        {
            string currentPath = _spec.GetLocation(true);
            string defaultPath = FindNearestExistingFolder(currentPath);

            string folder = EditorUtility.OpenFolderPanel("Select New Folder Location", defaultPath, "");
            if (string.IsNullOrEmpty(folder)) return;

            string normalizedNewPath;
            try
            {
                normalizedNewPath = Paths.NormalizePathForComparison(Path.GetFullPath(folder));
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Invalid Path", $"The selected folder path is invalid: {exception.Message}", "OK");
                return;
            }

            if (!Directory.Exists(normalizedNewPath))
            {
                EditorUtility.DisplayDialog("Invalid Path", "The selected folder does not exist.", "OK");
                return;
            }

            if (normalizedNewPath.IndexOf(AI.ASSET_STORE_FOLDER_NAME, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                EditorUtility.DisplayDialog("Attention", "You selected a custom Unity asset cache location. Configure the asset cache location under Indexing instead.", "OK");
                return;
            }

            if (Paths.AreEquivalentPaths(currentPath, normalizedNewPath)) return;

            if (!FolderLocationRelocator.TryGetRelocationGroup(_spec, true, out List<FolderSpec> completeGroup, out string groupError))
            {
                EditorUtility.DisplayDialog("Location Change Failed", groupError, "OK");
                return;
            }

            bool oldRootExists = !string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath);
            List<FolderSpec> movingSpecs = new List<FolderSpec> {_spec};
            if (!oldRootExists)
            {
                movingSpecs = completeGroup;
            }
            else if (completeGroup.Count > 1)
            {
                List<FolderSpec> requiredGroup = completeGroup.Where(spec => spec.folderType == _spec.folderType).ToList();
                bool hasIndependentSources = requiredGroup.Count < completeGroup.Count;
                if (!hasIndependentSources)
                {
                    bool moveAll = EditorUtility.DisplayDialog(
                        "Shared Source Location",
                        $"{completeGroup.Count} Additional Folder entries use this source tree, including entries whose indexed data cannot be separated safely. Move all of them to the selected location?",
                        "Move All",
                        "Cancel");
                    if (!moveAll) return;
                    movingSpecs = completeGroup;
                }
                else
                {
                    int choice = EditorUtility.DisplayDialogComplex(
                        "Shared Source Location",
                        $"{completeGroup.Count} Additional Folder entries use this source tree. Sources with the same content type must move together, while other content types can remain at the old location.",
                        "Move All",
                        requiredGroup.Count == 1 ? "Only This" : "Same Type Only",
                        "Cancel");
                    if (choice == 2) return;
                    movingSpecs = choice == 0 ? completeGroup : requiredGroup;
                }
            }

            FolderLocationRelocator.Operation operation = oldRootExists
                ? FolderLocationRelocator.Operation.ChangeExistingLocation
                : FolderLocationRelocator.Operation.MoveMissingLocation;
            if (!FolderLocationRelocator.TryCreatePlan(_spec, normalizedNewPath, movingSpecs, operation, out FolderLocationRelocator.Plan plan, out string planError))
            {
                EditorUtility.DisplayDialog("Location Change Failed", planError, "OK");
                return;
            }

            if (!FolderLocationRelocator.TryApply(plan, AI.TrySaveConfig, out string applyError))
            {
                EditorUtility.DisplayDialog("Location Change Failed", $"No changes were saved. {applyError}", "OK");
                return;
            }

            TriggerPackageRefreshSafely();
            Close();
        }

        private static void TriggerPackageRefreshSafely()
        {
            try
            {
                AI.TriggerPackageRefresh();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"The source location was updated, but refreshing the package view failed: {exception.Message}");
            }
        }

        private static string FindNearestExistingFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try
            {
                string candidate = Path.GetFullPath(path);
                while (!string.IsNullOrWhiteSpace(candidate))
                {
                    if (Directory.Exists(candidate)) return candidate;
                    string parent = Path.GetDirectoryName(candidate);
                    if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, candidate, StringComparison.OrdinalIgnoreCase)) break;
                    candidate = parent;
                }
            }
            catch (Exception)
            {
                return string.Empty;
            }

            return string.Empty;
        }

        private static string TryRollbackFolderMove(string currentPath, string previousPath)
        {
            try
            {
                if (!Directory.Exists(currentPath)) return "The renamed folder could no longer be found.";
                if (Directory.Exists(previousPath) || File.Exists(previousPath)) return "The original folder path is already occupied.";
                Directory.Move(currentPath, previousPath);
                return null;
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }
    }
}
