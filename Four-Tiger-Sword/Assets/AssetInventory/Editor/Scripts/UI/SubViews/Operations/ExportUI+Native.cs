using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImpossibleRobert.Common;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public sealed partial class ExportUI
    {
        private const string ExportOptionsRootClass = "ai-export-options-root";
        private const string ExportOptionsHeaderClass = "ai-export-options-header";
        private const string ExportOptionsTitleClass = "ai-export-options-title";
        private const string ExportOptionsSourceClass = "ai-export-options-source";
        private const string ExportOptionsScrollClass = "ai-export-options-scroll";
        private const string ExportOptionsFooterClass = "ai-export-options-footer";
        private const string ExportOptionButtonRowClass = "ai-export-option-button-row";
        private const string ExportOptionInlineRowClass = "ai-export-option-inline-row";
        private const string ExportOptionCheckListClass = "ai-export-option-check-list";
        private const string ExportOptionCheckItemClass = "ai-export-option-check-item";
        private const string ExportOptionCheckToggleClass = "ai-export-option-check-toggle";
        private const string ExportOptionCheckLabelClass = "ai-export-option-check-label";
        private const string ExportOptionFolderFieldClass = "ai-export-option-folder-field";
        private const string ExportOptionFoldoutClass = "ai-export-option-foldout";
        private const string ExportOptionDevelopmentCardClass = "ai-export-option-development-card";
        private const string ExportOptionProgressClass = "ai-export-option-progress";
        private const string ExportOptionSuffixClass = "ai-list-hint";

#if UNITY_6000_7_OR_NEWER
        // Form builders are immutable configuration shared by every export view.
        [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
        private static readonly CommonFormBuilder NativeExportFormBuilder = AssetInventoryUITK.CreateFormBuilder(
            inlineClass: ExportOptionInlineRowClass,
            suffixClass: ExportOptionSuffixClass);
#if UNITY_6000_7_OR_NEWER
        // Form builders are immutable configuration shared by every export view.
        [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
        private static readonly CommonFormBuilder NativeExportLeadingToggleFormBuilder = AssetInventoryUITK.CreateFormBuilder(
            rowClass: ExportOptionCheckItemClass,
            labelClass: ExportOptionCheckLabelClass,
            toggleClass: ExportOptionCheckToggleClass,
            toggleFirst: true,
            labelTogglesControl: true);

        private ProgressBar _nativeExportProgress;
        private Button _nativeExportActionButton;
        private bool _lastNativeExportInProgress;

        private void BuildNativeOptions(VisualElement root)
        {
            EnsureTemplateSettings();
            _lastNativeExportInProgress = _exportInProgress;
            _nativeExportProgress = null;
            _nativeExportActionButton = null;

            VisualElement page = CommonUITK.CreateContainer(ExportOptionsRootClass);
            root.Add(page);

            if (!_fileMode)
            {
                page.Add(CreateNativeExportHeader());
            }

            page.Add(CreateNativeSourceSummary());

            ScrollView body = new ScrollView(ScrollViewMode.Vertical);
            body.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            body.verticalScrollerVisibility = ScrollerVisibility.Auto;
            body.AddToClassList(ExportOptionsScrollClass);
            body.SetEnabled(!_exportInProgress);
            page.Add(body);

            switch (_selectedExportOption)
            {
                case 0:
                    BuildNativeCsvOptions(body);
                    break;
                case 1:
                    BuildNativeLicenseOptions(body);
                    break;
                case 2:
                    BuildNativeAssetOptions(body);
                    break;
                case 3:
                    BuildNativeOverrideOptions(body);
                    break;
                case 4:
                    BuildNativeTemplateOptions(body);
                    break;
            }

            VisualElement footer = CreateNativeExportFooter();
            if (footer != null)
            {
                page.Add(footer);
            }
        }

        private VisualElement CreateNativeExportHeader()
        {
            VisualElement header = CommonUITK.CreateContainer(ExportOptionsHeaderClass);
            Button back = AssetInventoryUITK.CreateIconButton("Back to export types", "d_back@2x", () =>
            {
                _wizardActive = true;
                BuildContent();
            });
            header.Add(back);

            Label title = AssetInventoryUITK.CreateCopyLabel(GetSelectedExportTypeInfo().Name);
            title.AddToClassList(ExportOptionsTitleClass);
            header.Add(title);
            header.Add(AssetInventoryUITK.CreateFlexibleSpacer());
            return header;
        }

        private VisualElement CreateNativeSourceSummary()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Source");
            section.AddToClassList(ExportOptionsSourceClass);
            section.Add(AssetInventoryUITK.CreateKeyValueRow("Selection", GetNativeSourceText()));
            return section;
        }

        private string GetNativeSourceText()
        {
            if (_fileMode) return $"{_assets.Count:N0} files";
            if (_packageCount == 1) return $"Custom Selection ({_assets.First().GetDisplayName()})";
            return $"Custom Selection ({_packageCount:N0} packages)";
        }

        private void BuildNativeCsvOptions(VisualElement body)
        {
            VisualElement options = AssetInventoryUITK.CreateSection("CSV Options");
            options.Add(CreateToggleRow("Header Line", _addHeader, value =>
            {
                _addHeader = value;
                SaveCSVSettings();
            }));
            body.Add(options);

            VisualElement fieldsSection = AssetInventoryUITK.CreateSection();
            Foldout fields = CreateFoldout("Fields", _showFields, value => _showFields = value);
            fields.Add(CreateActionRow(
                AssetInventoryUITK.CreateSecondaryButton("Select All", () => SelectNativeExportFields(_exportFields, _ => true, true)),
                AssetInventoryUITK.CreateSecondaryButton("Select None", () => SelectNativeExportFields(_exportFields, _ => false, true)),
                AssetInventoryUITK.CreateSecondaryButton("Select Default", () => SelectNativeExportFields(_exportFields, field => field.isDefault, true)),
                AssetInventoryUITK.CreateSecondaryButton("Select Visible Columns", () => SelectNativeExportFields(_exportFields, field => field.isVisibleColumn, true))));
            fields.Add(CreateChecklist(_exportFields, field => field.field ?? field.pointer, true));
            fieldsSection.Add(fields);
            body.Add(fieldsSection);
        }

        private void BuildNativeLicenseOptions(VisualElement body)
        {
            VisualElement section = AssetInventoryUITK.CreateSection("License Export");
            section.Add(AssetInventoryUITK.CreateHelpBox("The export will only include packages that actually contain license data.", MessageType.Info));
            body.Add(section);
        }

        private void BuildNativeAssetOptions(VisualElement body)
        {
            VisualElement options = AssetInventoryUITK.CreateSection("Asset Files");
            options.Add(CreateToggleRow("Clear Target", _clearTarget, value => _clearTarget = value, "Deletes any previously existing export for the specific package, otherwise only copies new files."));
            options.Add(CreateToggleRow("Flatten", _flattenStructure, value => _flattenStructure = value, "Put all files in the target folder directly independent of the sub-folders they are contained in."));
            options.Add(CreateToggleRow("Download", _autoDownload, value => _autoDownload = value, "Triggers download of a package automatically in case it is not available yet in the cache."));
            options.Add(CreateToggleRow("Meta Files", _metaFiles, value => _metaFiles = value, "Exports meta files if they exist."));
            body.Add(options);

            if (!_fileMode)
            {
                VisualElement types = AssetInventoryUITK.CreateSection("File Types");
                int selectedMode = Mathf.Clamp((int)_exportFileSelectionMode, 0, ExportFileSelectionOptions.Count - 1);
                PopupField<string> selectionMode = new PopupField<string>(ExportFileSelectionOptions, selectedMode)
                {
                    tooltip = "Export every indexed file, or limit the export to selected file type groups."
                };
                selectionMode.RegisterValueChangedCallback(evt =>
                {
                    int mode = ExportFileSelectionOptions.IndexOf(evt.newValue);
                    _exportFileSelectionMode = mode == (int)ExportFileSelectionMode.CustomSelection
                        ? ExportFileSelectionMode.CustomSelection
                        : ExportFileSelectionMode.AllFileTypes;
                    BuildIfReady();
                });
                types.Add(AssetInventoryUITK.CreateFieldRow("Selection", selectionMode));

                if (_exportFileSelectionMode == ExportFileSelectionMode.CustomSelection)
                {
                    types.Add(CreateActionRow(
                        AssetInventoryUITK.CreateSecondaryButton("Typical", () => SelectNativeExportFileTypes(field => field.isDefault, false)),
                        AssetInventoryUITK.CreateSecondaryButton("Select All", () => SelectNativeExportFileTypes(_ => true, true)),
                        AssetInventoryUITK.CreateSecondaryButton("Clear", () => SelectNativeExportFileTypes(_ => false, false))));
                    types.Add(CreateChecklist(_exportTypes, field => field.pointer, false, GetExportTypeTooltip));
                    types.Add(CreateLeadingToggle(
                        "Other / Unclassified",
                        _includeOtherExportTypes,
                        value => _includeOtherExportTypes = value,
                        "Include files whose extensions do not belong to any registered file type group."));
                }
                body.Add(types);
            }

            body.Add(AssetInventoryUITK.CreateHelpBox("Make sure you own the appropriate rights in case you intend to use assets in other contexts than Unity.", MessageType.Warning));
        }

        private void BuildNativeOverrideOptions(VisualElement body)
        {
            VisualElement options = AssetInventoryUITK.CreateSection("Package Override");
            options.Add(CreateToggleRow("Override Existing", _overrideExisting, value => _overrideExisting = value));
            body.Add(options);

            VisualElement fields = AssetInventoryUITK.CreateSection("Fields to Override");
            fields.Add(CreateActionRow(
                AssetInventoryUITK.CreateSecondaryButton("Select All", () => SelectNativeExportFields(_overrideFields, _ => true, false)),
                AssetInventoryUITK.CreateSecondaryButton("Select None", () => SelectNativeExportFields(_overrideFields, _ => false, false))));
            fields.Add(CreateChecklist(_overrideFields, field => field.field ?? field.pointer, false));
            body.Add(fields);
        }

        private void BuildNativeTemplateOptions(VisualElement body)
        {
            if (_templates == null || _templates.Count == 0)
            {
                body.Add(AssetInventoryUITK.CreateHelpBox("There are no templates available. Create a template first and put it into the Asset Inventory templates folder.", MessageType.Warning));
                return;
            }

            _selectedTemplate = Mathf.Clamp(_selectedTemplate, 0, _templates.Count - 1);
            TemplateInfo curTemplate = _templates[_selectedTemplate];

            VisualElement templateSection = AssetInventoryUITK.CreateSection("Export Template");
            VisualElement templateControls = CommonUITK.CreateContainer(ExportOptionInlineRowClass);
            PopupField<string> templatePopup = new PopupField<string>(_templateNames.ToList(), _selectedTemplate);
            templatePopup.SetEnabled(!AI.Config.templateExportSettings.devMode);
            templatePopup.RegisterValueChangedCallback(evt =>
            {
                _selectedTemplate = Mathf.Max(0, _templateNames.ToList().IndexOf(evt.newValue));
                PrepareOverrides();
                BuildIfReady();
            });
            templateControls.Add(templatePopup);

            if (ShowAdvanced())
            {
                Button create = CreateNameActionButton("New...", "Create a new empty template.", CreateTemplate);
                Button copy = CreateNameActionButton("Copy...", "Creates a full independent copy of the original template including all files.", CopyTemplate);
                Button extend = CreateNameActionButton("Extend...", "Creates a template extension referencing the original template.", ExtendTemplate);
                Button delete = AssetInventoryUITK.CreateIconButton("Delete template", "TreeEditor.Trash", () => DeleteNativeTemplate(curTemplate));
                create.SetEnabled(!AI.Config.templateExportSettings.devMode);
                copy.SetEnabled(!AI.Config.templateExportSettings.devMode);
                extend.SetEnabled(!AI.Config.templateExportSettings.devMode);
                delete.SetEnabled(!AI.Config.templateExportSettings.devMode && !curTemplate.readOnly);
                templateControls.Add(create);
                templateControls.Add(copy);
                templateControls.Add(extend);
                templateControls.Add(delete);
            }
            templateSection.Add(AssetInventoryUITK.CreateFieldRow("Template", templateControls));
            body.Add(templateSection);

            BuildNativeTemplateConfiguration(body, curTemplate);

            if (ShowAdvanced() || AI.Config.templateExportSettings.devMode)
            {
                body.Add(BuildNativeTemplateDevelopment(curTemplate));
            }
        }

        private void BuildNativeTemplateConfiguration(VisualElement body, TemplateInfo curTemplate)
        {
            TemplateExportSettings settings = AI.Config.templateExportSettings;
            settings.environmentIndex = Mathf.Clamp(settings.environmentIndex, 0, settings.environments.Count - 1);
            TemplateExportEnvironment env = settings.environments[settings.environmentIndex];

            VisualElement section = AssetInventoryUITK.CreateSection("Environment");
            VisualElement environmentControls = CommonUITK.CreateContainer(ExportOptionInlineRowClass);
            PopupField<string> environmentPopup = new PopupField<string>(settings.environments.Select(e => e.name).ToList(), settings.environmentIndex);
            environmentPopup.RegisterValueChangedCallback(evt =>
            {
                settings.environmentIndex = Mathf.Max(0, settings.environments.FindIndex(environment => environment.name == evt.newValue));
                AI.SaveConfig();
                BuildIfReady();
            });
            environmentControls.Add(environmentPopup);

            if (ShowAdvanced())
            {
                environmentControls.Add(CreateNameActionButton("New...", "Create a new export configuration.", name =>
                {
                    settings.environments.Add(new TemplateExportEnvironment(name));
                    settings.environmentIndex = settings.environments.Count - 1;
                    AI.SaveConfig();
                }, "My Config"));

                Button delete = AssetInventoryUITK.CreateIconButton("Delete configuration", "TreeEditor.Trash", () =>
                {
                    if (settings.environments.Count <= 1) return;
                    settings.environments.RemoveAt(settings.environmentIndex);
                    settings.environmentIndex = Mathf.Clamp(settings.environmentIndex - 1, 0, settings.environments.Count - 1);
                    AI.SaveConfig();
                    BuildIfReady();
                });
                delete.SetEnabled(settings.environments.Count > 1);
                environmentControls.Add(delete);
            }
            section.Add(AssetInventoryUITK.CreateFieldRow("Configuration", environmentControls));

            if (curTemplate.fixedTargetFolder)
            {
                env.publishFolder = Path.GetDirectoryName(Paths.GetPreviewFolder());
                section.Add(CreateFolderRow("Target Folder", env.publishFolder, null));
            }
            else
            {
                section.Add(CreateFolderRow("Target Folder", env.publishFolder, newFolder =>
                {
                    env.publishFolder = newFolder;
                    AI.SaveConfig();
                }));
            }

            if (ShowAdvanced())
            {
                if (curTemplate.needsImagePath)
                {
                    section.Add(CreateTextFieldRow("Image Path", env.imagePath, value =>
                    {
                        env.imagePath = value;
                        AI.SaveConfig();
                    }));
                }

                if (curTemplate.needsDataPath)
                {
                    section.Add(CreateTextFieldRow("Data Path", env.dataPath, value =>
                    {
                        env.dataPath = value;
                        AI.SaveConfig();
                    }));

                    if (curTemplate.needsImagePath)
                    {
                        section.Add(CreateToggleRow("Exclude Images", env.excludeImages, value =>
                        {
                            env.excludeImages = value;
                            AI.SaveConfig();
                        }, "Will not export images for file search, so icons and textures are not made available for download."));
                    }
                }

                section.Add(CreateToggleRow("Internal Ids Only", env.internalIdsOnly, value =>
                {
                    env.internalIdsOnly = value;
                    AI.SaveConfig();
                }, "Name package detail files as package_[id].html instead of using Asset Store foreign ids for linked assets."));
            }

            body.Add(section);
        }

        private VisualElement BuildNativeTemplateDevelopment(TemplateInfo curTemplate)
        {
            Foldout foldout = CreateFoldout("Template Development Mode", AI.Config.templateExportSettings.devMode, value =>
            {
                AI.Config.templateExportSettings.devMode = value;
                if (!value && _watcher != null) StopTemplateWatcher();
                AI.SaveConfig();
                if (value && _watcher != null) _triggerExport = true;
                BuildIfReady();
            });

            if (!AI.Config.templateExportSettings.devMode)
            {
                return foldout;
            }

            VisualElement card = AssetInventoryUITK.CreateSection("Development Settings");
            card.AddToClassList(ExportOptionDevelopmentCardClass);

            card.Add(AssetInventoryUITK.CreateHelpBox("Development mode is active and the export will use the settings below. Close this section to deactivate it.", MessageType.Warning));
            card.Add(CreateFolderRow("Dev Folder", AI.Config.templateExportSettings.devFolder, newFolder =>
            {
                AI.Config.templateExportSettings.devFolder = newFolder;
                AI.SaveConfig();

                if (!string.IsNullOrWhiteSpace(newFolder))
                {
                    if (IOUtils.IsDirectoryEmpty(newFolder))
                    {
                        CompressionUtil.ExtractArchive(curTemplate.path, newFolder);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Folder not empty", "The development folder is not empty. The contents of the template were not automatically extracted there.", "OK");
                    }
                }
                BuildIfReady();
            }));

            if (!string.IsNullOrWhiteSpace(AI.Config.templateExportSettings.devFolder))
            {
                Button monitor = AssetInventoryUITK.CreateSecondaryButton(_watcher == null ? "Start Directory Monitoring" : "Stop Directory Monitoring", () =>
                {
                    if (_watcher == null)
                    {
                        StartTemplateWatcher(AI.Config.templateExportSettings.devFolder);
                    }
                    else
                    {
                        StopTemplateWatcher();
                    }
                    BuildIfReady();
                });

                VisualElement actions = CreateActionRow(
                    AssetInventoryUITK.CreateSecondaryButton("Publish", PackageDevTemplate),
                    monitor);

                if (!string.IsNullOrWhiteSpace(curTemplate.inheritFrom))
                {
                    Button overrideFile = AssetInventoryUITK.CreateSecondaryButton("Override File...", null);
                    overrideFile.clicked += () => ShowNativeOverrides(overrideFile);
                    actions.Add(overrideFile);
                }

                card.Add(actions);
            }

            card.Add(CreateFolderRow("Test Folder", AI.Config.templateExportSettings.testFolder, newFolder =>
            {
                AI.Config.templateExportSettings.testFolder = newFolder;
                AI.SaveConfig();
                BuildIfReady();
            }));

            card.Add(CreateIntegerRow("Detail Pages", AI.Config.templateExportSettings.maxDetailPages, value =>
            {
                AI.Config.templateExportSettings.maxDetailPages = Mathf.Max(0, value);
                AI.SaveConfig();
            }, "(0 = all)"));

            VisualElement flags = CommonUITK.CreateContainer(ExportOptionCheckListClass);
            if (curTemplate.needsDataPath && !string.IsNullOrWhiteSpace(AI.Config.templateExportSettings.testFolder))
            {
                flags.Add(CreateInlineToggle("Preserve Json", AI.Config.templateExportSettings.preserveJson, value =>
                {
                    AI.Config.templateExportSettings.preserveJson = value;
                    AI.SaveConfig();
                }, "Do not export data to Json but reuse already generated Json artifacts."));
            }
            if (!string.IsNullOrWhiteSpace(AI.Config.templateExportSettings.testFolder))
            {
                flags.Add(CreateInlineToggle("Publish Result", AI.Config.templateExportSettings.publishResult, value =>
                {
                    AI.Config.templateExportSettings.publishResult = value;
                    AI.SaveConfig();
                }, "Copy exported files from temporary to target directory."));
            }
            flags.Add(CreateInlineToggle("Open " + (Application.platform == RuntimePlatform.OSXEditor ? "Finder" : "Explorer"), AI.Config.templateExportSettings.revealResult, value =>
            {
                AI.Config.templateExportSettings.revealResult = value;
                AI.SaveConfig();
                }, "Open the file browser once the export is done."));
            card.Add(AssetInventoryUITK.CreateFieldRow("Flags", flags));

            if (string.IsNullOrWhiteSpace(AI.Config.templateExportSettings.testFolder))
            {
                Label hint = AssetInventoryUITK.CreateCopyLabel("Setting a test folder unlocks additional development flags.");
                hint.AddToClassList("ai-list-hint");
                card.Add(hint);
            }

            card.Add(CreateActionRow(
                CreateDescriptorButton(curTemplate),
                AssetInventoryUITK.CreateSecondaryButton("Start Local Server", () => StartNativeLocalServer(curTemplate))));
            foldout.Add(card);
            return foldout;
        }

        private VisualElement CreateNativeExportFooter()
        {
            if (_selectedExportOption == 4 && (_templates == null || _templates.Count == 0))
            {
                return null;
            }

            VisualElement footer = AssetInventoryUITK.CreateWindowFooter();
            footer.AddToClassList(ExportOptionsFooterClass);

            if (ShouldShowNativeProgress())
            {
                _nativeExportProgress = AssetInventoryUITK.CreateProgressBar(GetNativeProgressTitle(), GetNativeProgressValue());
                _nativeExportProgress.AddToClassList(ExportOptionProgressClass);
                footer.Add(_nativeExportProgress);
            }

            footer.Add(AssetInventoryUITK.CreateFlexibleSpacer());

            _nativeExportActionButton = AssetInventoryUITK.CreatePrimaryButton(GetNativeExportActionLabel(), () => RunNativeExportAction(GetNativeExportAction()));
            _nativeExportActionButton.SetEnabled(CanRunNativeExportAction());
            footer.Add(_nativeExportActionButton);
            return footer;
        }

        private VisualElement CreateToggleRow(string label, bool value, Action<bool> onChange, string tooltip = null)
        {
            return NativeExportFormBuilder.CreateToggleRow(label, value, onChange, tooltip);
        }

        private VisualElement CreateInlineToggle(string label, bool value, Action<bool> onChange, string tooltip = null)
        {
            return CreateLeadingToggle(label, value, onChange, tooltip);
        }

        private VisualElement CreateTextFieldRow(string label, string value, Action<string> onChange)
        {
            return NativeExportFormBuilder.CreateTextRow(label, value, onChange);
        }

        private VisualElement CreateIntegerRow(string label, int value, Action<int> onChange, string suffix = null)
        {
            return NativeExportFormBuilder.CreateIntegerRow(label, value, onChange, suffix);
        }

        private VisualElement CreateFolderRow(string label, string value, Action<string> onChange)
        {
            VisualElement row = CommonUITK.CreateContainer(ExportOptionInlineRowClass);
            TextField field = new TextField
            {
                value = value ?? string.Empty
            };
            field.AddToClassList(ExportOptionFolderFieldClass);
            field.SetEnabled(onChange != null);
            if (onChange != null)
            {
                field.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            }
            row.Add(field);

            Button browse = AssetInventoryUITK.CreateSecondaryButton("Select...", () =>
            {
                string folder = EditorUtility.OpenFolderPanel("Select folder", value ?? string.Empty, string.Empty);
                if (string.IsNullOrEmpty(folder)) return;
                field.SetValueWithoutNotify(folder);
                onChange?.Invoke(folder);
            });
            browse.SetEnabled(onChange != null);
            row.Add(browse);
            return AssetInventoryUITK.CreateFieldRow(label, row);
        }

        private Foldout CreateFoldout(string text, bool value, Action<bool> onChange)
        {
            return AssetInventoryUITK.CreateFoldout(text, value, onChange, $"Show or hide {text.ToLowerInvariant()} options.", ExportOptionFoldoutClass);
        }

        private VisualElement CreateActionRow(params VisualElement[] actions)
        {
            VisualElement row = CommonUITK.CreateContainer(ExportOptionButtonRowClass);
            for (int i = 0; i < actions.Length; i++)
            {
                if (actions[i] != null) row.Add(actions[i]);
            }
            return row;
        }

        private VisualElement CreateChecklist(List<ED> fields, Func<ED, string> getLabel, bool saveCsv, Func<ED, string> getTooltip = null)
        {
            VisualElement list = CommonUITK.CreateContainer(ExportOptionCheckListClass);
            for (int i = 0; i < fields.Count; i++)
            {
                ED field = fields[i];
                list.Add(CreateLeadingToggle(getLabel(field), field.isSelected, value =>
                {
                    field.isSelected = value;
                    if (saveCsv) SaveCSVSettings();
                }, getTooltip?.Invoke(field)));
            }
            return list;
        }

        private static string GetExportTypeTooltip(ED field)
        {
            if (field != null
                && Enum.TryParse(field.pointer, out AI.AssetGroup group)
                && AI.TypeGroups.TryGetValue(group, out string[] extensions))
            {
                return $"Include files in the {field.pointer} group: .{string.Join(", .", extensions)}.";
            }
            return "Include this file type group in the export.";
        }

        private VisualElement CreateLeadingToggle(string label, bool value, Action<bool> onChange, string tooltip = null)
        {
            return NativeExportLeadingToggleFormBuilder.CreateToggleRow(label, value, onChange, tooltip ?? label);
        }

        private Button CreateNameActionButton(string label, string tooltip, Action<string> callback, string defaultName = "My Template")
        {
            Button button = null;
            button = AssetInventoryUITK.CreateSecondaryButton(label, () =>
            {
                NameWindow.ShowAsDropDown(CommonUITK.ToScreenDropdownAnchor(this, button), defaultName, name =>
                {
                    callback?.Invoke(name);
                    BuildIfReady();
                });
            });
            button.tooltip = tooltip ?? string.Empty;
            return button;
        }

        private Button CreateDescriptorButton(TemplateInfo curTemplate)
        {
            if (curTemplate.hasDescriptor)
            {
                return AssetInventoryUITK.CreateSecondaryButton("Open Descriptor", () => EditorUtility.RevealInFinder(curTemplate.GetDescriptorPath()));
            }

            return AssetInventoryUITK.CreateSecondaryButton("Create Descriptor", () =>
            {
                string descriptor = curTemplate.GetDescriptorPath();
                File.WriteAllText(descriptor, JsonConvert.SerializeObject(curTemplate, Formatting.Indented));
                EditorUtility.DisplayDialog("Descriptor Created", $"Descriptor file '{descriptor}' has been created.", "OK");
                AssetDatabase.Refresh();
                LoadTemplates();
                BuildIfReady();
            });
        }

        private void SelectNativeExportFields(List<ED> fields, Func<ED, bool> selector, bool saveCsv)
        {
            for (int i = 0; i < fields.Count; i++)
            {
                fields[i].isSelected = selector(fields[i]);
            }
            if (saveCsv) SaveCSVSettings();
            BuildIfReady();
        }

        private void SelectNativeExportFileTypes(Func<ED, bool> selector, bool includeOther)
        {
            for (int i = 0; i < _exportTypes.Count; i++)
            {
                _exportTypes[i].isSelected = selector(_exportTypes[i]);
            }
            _includeOtherExportTypes = includeOther;
            BuildIfReady();
        }

        private void DeleteNativeTemplate(TemplateInfo curTemplate)
        {
            string templateName = !string.IsNullOrWhiteSpace(curTemplate.name)
                ? curTemplate.name
                : curTemplate.GetNameFromFile();
            if (!EditorUtility.DisplayDialog("Delete Template", $"Are you sure you want to delete the template '{templateName}'? This action cannot be undone.", "Delete", "Cancel")) return;

            if (curTemplate.hasDescriptor) File.Delete(curTemplate.GetDescriptorPath());
            File.Delete(curTemplate.path);
            AssetDatabase.Refresh();
            LoadTemplates();
            _selectedTemplate = Mathf.Clamp(_selectedTemplate, 0, Mathf.Max(0, _templates.Count - 1));
            BuildIfReady();
        }

        private void ShowNativeOverrides(Button anchor)
        {
            GenericMenu menu = new GenericMenu();
            if (_overrideCandidates == null || _overrideCandidates.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No override candidates"));
            }
            else
            {
                foreach (string file in _overrideCandidates)
                {
                    string relPath = file.Substring(_overridesFolder.Length + 1);
                    string target = Path.Combine(AI.Config.templateExportSettings.devFolder, relPath);
                    if (File.Exists(target))
                    {
                        menu.AddDisabledItem(new GUIContent(relPath));
                    }
                    else
                    {
                        menu.AddItem(new GUIContent(relPath), false, () => OverrideFile(file));
                    }
                }
            }
            CommonUITK.ShowGenericMenu(menu, anchor);
        }

        private void StartNativeLocalServer(TemplateInfo curTemplate)
        {
            TemplateExportEnvironment env = AI.Config.templateExportSettings.environments[AI.Config.templateExportSettings.environmentIndex];
#if UNITY_EDITOR_OSX
            string command = "/usr/bin/python3";
#else
            string command = "python";
#endif
            IOUtils.ExecuteCommand(command, "-m http.server 8000", env.publishFolder, false, true);
            AI.OpenURL("http://localhost:8000" + (!string.IsNullOrWhiteSpace(curTemplate.entryPath) ? $"/{curTemplate.entryPath}" : ""));
        }

        private void EnsureTemplateSettings()
        {
            if (AI.Config.templateExportSettings == null)
            {
                AI.Config.templateExportSettings = new TemplateExportSettings();
            }
            if (AI.Config.templateExportSettings.environments == null)
            {
                AI.Config.templateExportSettings.environments = new List<TemplateExportEnvironment>();
            }
            if (AI.Config.templateExportSettings.environments.Count == 0)
            {
                AI.Config.templateExportSettings.environments.Add(new TemplateExportEnvironment());
            }
            AI.Config.templateExportSettings.environmentIndex = Mathf.Clamp(
                AI.Config.templateExportSettings.environmentIndex,
                0,
                AI.Config.templateExportSettings.environments.Count - 1);
        }

        private ExportTypeInfo GetSelectedExportTypeInfo()
        {
            return _exportTypeInfos.FirstOrDefault(info => info.Index == _selectedExportOption) ?? _exportTypeInfos[0];
        }

        private string GetNativeExportActionLabel()
        {
            if (_exportInProgress) return "Export in progress...";
            if (_selectedExportOption == 4 && _watcher != null) return "Export (automatically upon changes)";
            return _selectedExportOption == 3 ? "Export" : "Export...";
        }

        private Action GetNativeExportAction()
        {
            switch (_selectedExportOption)
            {
                case 0:
                    return ExportCSV;
                case 1:
                    return ExportLicenses;
                case 2:
                    return ExportAssets;
                case 3:
                    return ExportOverrides;
                case 4:
                    return ExportTemplate;
                default:
                    return null;
            }
        }

        private bool CanRunNativeExportAction()
        {
            if (_exportInProgress) return false;
            if (_selectedExportOption == 4)
            {
                if (_templates == null || _templates.Count == 0) return false;
                if (_watcher != null) return false;
            }
            return true;
        }

        private void RunNativeExportAction(Action action)
        {
            if (!CanRunNativeExportAction()) return;
            action?.Invoke();
            BuildIfReady();
        }

        private bool ShouldShowNativeProgress()
        {
            return _exportInProgress && (_selectedExportOption == 2 || _selectedExportOption == 3 || _selectedExportOption == 4);
        }

        private float GetNativeProgressValue()
        {
            if (_maxProgress <= 0) return _exportInProgress ? 0.35f : 0f;
            return Mathf.Clamp01(_curProgress / (float)_maxProgress);
        }

        private string GetNativeProgressTitle()
        {
            if (_maxProgress <= 0) return "Export in progress...";
            return $"{_curProgress:N0}/{_maxProgress:N0}";
        }

        private void UpdateNativeExportProgress()
        {
            if (!_uitkActive) return;

            if (_lastNativeExportInProgress != _exportInProgress)
            {
                BuildIfReady();
                return;
            }

            if (_nativeExportProgress != null)
            {
                _nativeExportProgress.value = GetNativeProgressValue();
                _nativeExportProgress.title = GetNativeProgressTitle();
            }

            if (_nativeExportActionButton != null)
            {
                _nativeExportActionButton.text = GetNativeExportActionLabel();
                _nativeExportActionButton.SetEnabled(CanRunNativeExportAction());
            }
        }
    }
}
