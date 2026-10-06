using Automator;
using ImpossibleRobert.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Brain;
using Database;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
#if !USE_TUTORIALS || !USE_VECTOR_GRAPHICS || !USE_PSD_IMPORTER || !USE_VFX || (!USE_GLTF_IMPORTER && !USE_KHRONOS_UNITY_GLTF) || (!USE_TEXTMESHPRO && !UNITY_2023_2_OR_NEWER)
using UnityEditor.PackageManager;
#endif
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public partial class IndexUI
    {
        private const int AI_MODEL_FIELD_WIDTH = 250;
        private const string SettingsRootClass = "ai-settings-root";
        private const string SettingsRootScrollClass = "ai-settings-root-scroll";
        private const string SettingsSummaryClass = "ai-settings-summary";
        private const string SettingsCardClass = "ai-settings-card";
        private const string SettingsCardLabelClass = "ai-settings-card-label";
        private const string SettingsCardValueClass = "ai-settings-card-value";
        private const string SettingsContentClass = "ai-settings-content";
        private const string SettingsContentStackedClass = "ai-settings-content-stacked";
        private const string SettingsLeftColumnClass = "ai-settings-left-column";
        private const string SettingsSectionFoldoutClass = "ai-settings-section-foldout";
        private const string SettingsActionsSectionClass = "ai-settings-actions-section";
        private const string SettingsActionsFoldoutClass = "ai-settings-actions-foldout";
        private const string SettingsActionsHeaderClass = "ai-settings-actions-header";
        private const string SettingsActionsHeaderButtonsClass = "ai-settings-actions-header-buttons";
        private const string SettingsActionsListClass = "ai-settings-actions-list";
        private const string SettingsActionRowClass = "ai-settings-action-row";
        private const string SettingsActionToggleClass = "ai-settings-action-toggle";
        private const string SettingsActionTitleClass = "ai-settings-action-title";
        private const string SettingsActionRunningClass = "ai-settings-action-running";
        private const string SettingsActionEditButtonName = "settings-action-edit";
        private const string SettingsActionForceButtonName = "settings-action-force";
        private const string SettingsActionRunButtonName = "settings-action-run";
        private const string SettingsSidebarClass = "ai-settings-sidebar";
        private const string SettingsSidebarScrollClass = "ai-settings-sidebar-scroll";
        private const string SettingsIndexingSectionClass = "ai-settings-indexing-section";
        private const string SettingsFoldersSectionClass = "ai-settings-folders-section";
        private const string SettingsFoldersHeaderClass = "ai-settings-folders-header";
        private const string SettingsFoldersHeaderButtonsClass = "ai-settings-folders-header-buttons";
        private const string SettingsFoldersListClass = "ai-settings-folders-list";
        private const string SettingsFoldersScrollStateKey = "settings-folders-list";
        private const string SettingsFolderRowClass = "ai-settings-folder-row";
        private const string SettingsFolderToggleClass = "ai-settings-folder-toggle";
        private const string SettingsFolderTitleClass = "ai-settings-folder-title";
        private const string SettingsFolderSubtitleClass = "ai-settings-folder-subtitle";
        private const string SettingsFolderFineTuneButtonName = "settings-folder-fine-tune";
        private const string SettingsFolderSettingsButtonName = "settings-folder-settings";
        private const string SettingsRelativeLocationsClass = "ai-settings-relative-locations";
        private const string SettingsRelativeLocationRowClass = "ai-settings-relative-location-row";
        private const string SettingsAssetManagerSectionClass = "ai-settings-asset-manager-section";
        private const string SettingsImportSectionClass = "ai-settings-import-section";
        private const string SettingsPreviewsSectionClass = "ai-settings-previews-section";
        private const string SettingsBackupSectionClass = "ai-settings-backup-section";
        private const string SettingsAISectionClass = "ai-settings-ai-section";
        private const string SettingsFieldColumnClass = "ai-settings-field-column";
        private const string SettingsModelControlsClass = "ai-settings-model-controls";
        private const string SettingsAITestPanelClass = "ai-settings-ai-test-panel";
        private const string SettingsAITestImageClass = "ai-settings-ai-test-image";
        private const string SettingsLocationsSectionClass = "ai-settings-locations-section";
        private const string SettingsUIIntegrationSectionClass = "ai-settings-ui-integration-section";
        private const string SettingsAdvancedSectionClass = "ai-settings-advanced-section";
        private const string SettingsSubsectionTitleClass = "ai-settings-subsection-title";
        private const string SettingsGroupClass = "ai-settings-group";
        private const string SettingsGroupHeaderClass = "ai-settings-group-header";
        private const string SettingsGroupTitleClass = "ai-settings-group-title";
        private const string SettingsGroupDescriptionClass = "ai-settings-group-description";
        private const string SettingsGroupBodyClass = "ai-settings-group-body";
        private const string SettingsValueRowClass = "ai-settings-value-row";
        private const string SettingsValueLabelClass = "ai-settings-value-label";
        private const string SettingsValueControlClass = "ai-settings-value-control";
        private const string SettingsValueTextClass = "ai-settings-value-text";
        private const string SettingsValueInlineTextClass = "ai-settings-value-inline-text";
        private const string SettingsToggleRowClass = "ai-settings-toggle-row";
        private const string SettingsToggleInputClass = "ai-settings-toggle-input";
        private const string SettingsNumberFieldClass = "ai-settings-number-field";
        private const string SettingsFolderPathClass = "ai-settings-folder-path";
        private const string SettingsButtonRowClass = "ai-settings-button-row";
        private const string SettingsCompactNoteClass = "ai-settings-compact-note";
        private const string SettingsUpdateStatusClass = "ai-settings-update-status";
        private const string SettingsProgressGroupClass = "ai-settings-progress-group";
        private const string SettingsSidebarFoldoutClass = "ai-settings-sidebar-foldout";
        private const string SettingsSidebarSubsectionClass = "ai-settings-sidebar-subsection";
        private const float SettingsActionRowHeight = 34f;
        private const int SettingsActionNarrowMinVisibleRows = 6;
        private const int SettingsActionNarrowMaxVisibleRows = 8;
        private const int SettingsActionWideMinVisibleRows = 8;
        private const int SettingsActionWideMaxVisibleRows = 12;
        private const float SettingsFolderRowHeight = 46f;

        private static readonly CommonFormBuilder NativeSettingsFormBuilder = new CommonFormBuilder(
            new CommonFormBuilder.FormClasses
            {
                RowClass = SettingsValueRowClass,
                LabelClass = SettingsValueLabelClass,
                ControlClass = SettingsValueControlClass,
                ToggleClass = SettingsToggleRowClass,
                WrapControls = true
            });

        private Vector2 _folderScrollPos;
        private Vector2 _statsScrollPos;
        private Vector2 _settingsScrollPos;
        private Label _nativeSettingsActionsValue;
        private Label _nativeSettingsPackagesValue;
        private Label _nativeSettingsDatabaseValue;
        private Label _nativeSettingsLastUpdateValue;
        private VisualElement _nativeSettingsContent;
        private ScrollView _nativeSettingsRootScroll;
        private VisualElement _nativeSettingsLeftColumn;
        private VisualElement _nativeUpdateActionsSection;
        private CommonReorderableListView<UpdateAction> _nativeUpdateActionsList;
        private VisualElement _nativeIndexingSettingsSection;
        private VisualElement _nativeFoldersSettingsSection;
        private CommonReorderableListView<FolderSpec> _nativeFoldersList;
        private VisualElement _nativeImportSettingsSection;
        private VisualElement _nativePreviewsSettingsSection;
        private VisualElement _nativeBackupSettingsSection;
        private VisualElement _nativeAISettingsSection;
        private VisualElement _nativeLocationsSettingsSection;
        private VisualElement _nativeUIIntegrationSettingsSection;
        private VisualElement _nativeAdvancedSettingsSection;
        private VisualElement _nativeSettingsSidebar;
        private ScrollView _nativeSettingsSidebarScroll;
        private int _nativeUpdateActionsHash;
        private int _nativeIndexingSettingsHash;
        private int _nativeFoldersSettingsHash;
        private int _nativeImportSettingsHash;
        private int _nativePreviewsSettingsHash;
        private int _nativeBackupSettingsHash;
        private int _nativeAISettingsHash;
        private int _nativeLocationsSettingsHash;
        private int _nativeUIIntegrationSettingsHash;
        private int _nativeAdvancedSettingsHash;
        private int _nativeSettingsSidebarHash;

        private bool _showStatistics;
        private bool _showMaintenance;
        private bool _showDiskSpace;
        private long _dbSize;
        private long _backupSize;
        private long _cacheSize;
        private long _persistedCacheSize;
        private long _previewSize;
        private string _captionTest = "-no caption created yet-";
        private bool _captionTestRunning;

        internal static string AddCaptionTypeGroup(string value, AI.AssetGroup group)
        {
            return AddTypeGroup(value, group);
        }

        internal static string AddTypeGroup(string value, AI.AssetGroup group)
        {
            string token = "{" + group.ToString().ToLowerInvariant() + "}";
            List<string> tokens = StringUtils.Split(value, new[] {';', ','})
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();

            if (!tokens.Any(t => string.Equals(t, token, StringComparison.OrdinalIgnoreCase)))
            {
                tokens.Add(token);
            }

            return string.Join(";", tokens);
        }

        internal static string ToggleTypeGroup(string value, AI.AssetGroup group)
        {
            string token = "{" + group.ToString().ToLowerInvariant() + "}";
            List<string> tokens = StringUtils.Split(value, new[] {';', ','})
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();

            int removedCount = tokens.RemoveAll(t => string.Equals(t, token, StringComparison.OrdinalIgnoreCase));
            if (removedCount == 0)
            {
                tokens.Add(token);
            }

            return string.Join(";", tokens);
        }

        internal static bool ShouldShowSemanticIndexStats(bool featureEnabled)
        {
            return featureEnabled;
        }

        internal static bool ShouldShowSemanticSearchSettings(bool featureEnabled)
        {
            return featureEnabled;
        }

        internal static bool ShouldShowSemanticSearchToggle(bool featureEnabled)
        {
            return featureEnabled;
        }

        internal static bool ShouldShowCodeIndexStats(bool featureEnabled)
        {
            return featureEnabled;
        }

        internal static bool ShouldShowCodeSearchSettings(bool featureEnabled)
        {
            return featureEnabled;
        }

        internal static bool ShouldRefreshIndexStats(InventoryStats stats, bool semanticFeatureEnabled, bool codeFeatureEnabled)
        {
            if (stats == null) return true;

            return (ShouldShowSemanticIndexStats(semanticFeatureEnabled) && stats.SemanticIndex == null)
                || (ShouldShowCodeIndexStats(codeFeatureEnabled) && stats.CodeIndex == null);
        }

        private List<UpdateAction> _updateActions;

        private bool _calculatingFolderSizes;
        private bool _cleanupInProgress;
        private DateTime _lastFolderSizeCalculation;
        private long _curOllamaProgress;
        private long _maxOllamaProgress;
        private string _activeOllamaDownloadModel;

        private void InitUpdateActions()
        {
            _updateActions = AI.Actions.Actions.Where(action => !action.hidden && AI.Actions.IsAvailable(action)).ToList();
        }

        private static bool CanRemoveAction(UpdateAction action)
        {
            return action != null && action.key.StartsWith(ActionHandler.ACTION_USER);
        }

        private void RemoveAction(UpdateAction action)
        {
            if (!CanRemoveAction(action)) return;

            string key = action.key;
            int id = int.Parse(key.Split('-').Last());
            CustomAction ca = DBAdapter.DB.Find<CustomAction>(id);
            if (ca == null)
            {
                Debug.LogError($"Could not find action to delete: {key}. Restarting Unity might solve this.");
                return;
            }

            if (!EditorUtility.DisplayDialog("Confirm", $"Do you really want to delete the action '{ca.Name}'?", "Yes", "No")) return;

            DBAdapter.DB.Execute("delete from CustomActionStep where ActionId=?", ca.Id);
            DBAdapter.DB.Delete(ca);

            AI.Actions.Init(true);
            InitUpdateActions();
            RefreshNativeUpdateActionsSection(true);
        }

        private void CreateAction(string actionName)
        {
            CustomAction action = new CustomAction(actionName.Trim());
            DBAdapter.DB.Insert(action);

            AI.Actions.Init(true);
            InitUpdateActions();
            RefreshNativeUpdateActionsSection(true);
            EditAction(action.Id);
        }

        private void EditAction(string actionKey)
        {
            int id = int.Parse(actionKey.Split('-').Last());
            EditAction(id);
        }

        private void EditAction(int id)
        {
            SqliteActionRepository repository = new SqliteActionRepository();
            ActionDefinition action = repository.GetAction(id);

            ActionEditorWindow.Edit(repository, action, RefreshActions);
        }

        private void RefreshActions()
        {
            // Reload actions from database to get updated names/descriptions
            AI.Actions.Init(true);
            InitUpdateActions();
            RefreshNativeUpdateActionsSection(true);
        }

        private void OnActionsInitialized()
        {
            InitUpdateActions();
            RefreshNativeUpdateActionsSection(true);
        }

        private VisualElement BuildNativeUpdateActionsSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection();
            section.AddToClassList(SettingsActionsSectionClass);
            _nativeUpdateActionsSection = section;
            _nativeUpdateActionsHash = int.MinValue;
            RefreshNativeUpdateActionsSection(true);
            return section;
        }

        private void RefreshNativeUpdateActionsSection(bool force = false)
        {
            if (_nativeUpdateActionsSection == null || AI.Actions == null) return;

            int hash = GetNativeUpdateActionsHash();
            if (!force && _nativeUpdateActionsHash == hash)
            {
                ApplyNativeUpdateActionsListHeight();
                _nativeUpdateActionsList?.Refresh();
                return;
            }

            _nativeUpdateActionsHash = hash;
            _updateActions = AI.Actions.Actions.Where(action => !action.hidden && AI.Actions.IsAvailable(action)).ToList();

            _nativeUpdateActionsSection.Clear();
            Foldout foldout = CreateNativeSettingsFoldout(
                GetNativeUpdateActionsTitle(),
                AI.Config.showActionSettings,
                value =>
                {
                    AI.Config.showActionSettings = value;
                    AI.SaveConfig();
                    RefreshNativeUpdateActionsSection(true);
                },
                SettingsActionsFoldoutClass);
            _nativeUpdateActionsSection.Add(foldout);

            if (!AI.Config.showActionSettings)
            {
                _nativeUpdateActionsList = null;
                return;
            }

            foldout.Add(AssetInventoryUITK.CreateCopyLabel(
                "Checked actions run when you choose Run Actions. Use a row's play button to run it once. Optional actions appear after enabling their feature in Backup, Unity Asset Manager, Synty Importer, or Artificial Intelligence."));
            foldout.Add(BuildNativeUpdateActionsHeader());

            _nativeUpdateActionsList = new CommonReorderableListView<UpdateAction>(
                _updateActions,
                CreateNativeUpdateActionRow,
                BindNativeUpdateActionRow,
                SettingsActionRowHeight,
                "ai-reorderable-list",
                SettingsActionsListClass);
            _nativeUpdateActionsList.SetReorderable(false);
            _nativeUpdateActionsList.SetAddHandler((_, button) =>
            {
                NameWindow.ShowAsDropDown(
                    CommonUITK.ToScreenDropdownAnchor(this, button),
                    "My Action",
                    CreateAction);
            });
            _nativeUpdateActionsList.SetRemoveHandler(
                list => RemoveAction(list.SelectedItem),
                list => CanRemoveAction(list.SelectedItem));

            ApplyNativeUpdateActionsListHeight();
            foldout.Add(_nativeUpdateActionsList);
        }

        private string GetNativeUpdateActionsTitle()
        {
            return AI.Actions.AnyActionsInProgress
                ? $"Update Actions (Started {StringUtils.GetRelativeTimeDifference(AI.Actions.GetFirstActionStart())})"
                : "Update Actions";
        }

        private VisualElement BuildNativeUpdateActionsHeader()
        {
            VisualElement header = new VisualElement();
            header.AddToClassList(SettingsActionsHeaderClass);

            VisualElement buttons = new VisualElement();
            buttons.AddToClassList(SettingsActionsHeaderButtonsClass);
            buttons.Add(CreateNativeActionBatchButton("All", () => SetAllNativeActionsActive(true)));
            buttons.Add(CreateNativeActionBatchButton("Default", SetDefaultNativeActionsActive));
            buttons.Add(CreateNativeActionBatchButton("None", () => SetAllNativeActionsActive(false)));
            header.Add(buttons);

            return header;
        }

        private VisualElement CreateNativeUpdateActionRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("ai-list-row");
            row.AddToClassList(SettingsActionRowClass);

            Toggle toggle = new Toggle();
            toggle.AddToClassList(SettingsActionToggleClass);
            row.Add(toggle);

            VisualElement body = new VisualElement();
            body.AddToClassList("ai-list-row-body");

            Label title = new Label();
            title.AddToClassList("ai-list-row-title");
            title.AddToClassList(SettingsActionTitleClass);
            body.Add(title);
            row.Add(body);

            VisualElement actions = new VisualElement();
            actions.AddToClassList("ai-list-actions");

            Button edit = null;
            edit = AssetInventoryUITK.CreateIconButton("Edit Action", "editicon.sml", () => OnNativeEditActionClicked(edit));
            edit.name = SettingsActionEditButtonName;
            actions.Add(edit);

            Button force = null;
            force = AssetInventoryUITK.CreateIconButton("Force Run Action Now", "d_preAudioAutoPlayOff@2x", () => OnNativeForceRunActionClicked(force));
            force.name = SettingsActionForceButtonName;
            actions.Add(force);

            Button run = null;
            run = AssetInventoryUITK.CreateIconButton("Run Action Now", "d_PlayButton@2x", () => OnNativeRunActionClicked(run));
            run.name = SettingsActionRunButtonName;
            actions.Add(run);

            row.Add(actions);
            return row;
        }

        private void BindNativeUpdateActionRow(VisualElement element, UpdateAction action, int index)
        {
            if (element == null || action == null) return;

            element.userData = action;
            element.tooltip = action.description ?? string.Empty;
            element.EnableInClassList(SettingsActionRunningClass, action.IsRunning());

            Toggle toggle = element.Q<Toggle>(className: SettingsActionToggleClass);
            if (toggle != null)
            {
                toggle.userData = action;
                toggle.SetValueWithoutNotify(AI.Actions.IsActive(action));
                toggle.tooltip = "Include this action in regular Run Actions updates.";
                toggle.UnregisterValueChangedCallback(OnNativeUpdateActionToggleChanged);
                toggle.RegisterValueChangedCallback(OnNativeUpdateActionToggleChanged);
            }

            Label title = element.Q<Label>(className: SettingsActionTitleClass);
            if (title != null)
            {
                title.text = action.name ?? string.Empty;
                title.tooltip = action.description ?? string.Empty;
            }

            bool buttonsEnabled = !action.IsRunning() && !action.scheduled && !AI.Actions.AnyActionsInProgress;
            BindNativeActionButton(element.Q<Button>(SettingsActionEditButtonName), action, CanEditAction(action), buttonsEnabled);
            BindNativeActionButton(element.Q<Button>(SettingsActionForceButtonName), action, CanForceRunAction(action), buttonsEnabled);
            BindNativeActionButton(element.Q<Button>(SettingsActionRunButtonName), action, true, buttonsEnabled);
        }

        private static void BindNativeActionButton(Button button, UpdateAction action, bool visible, bool enabled)
        {
            if (button == null) return;

            button.userData = action;
            button.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            button.SetEnabled(enabled);
        }

        private void OnNativeEditActionClicked(Button button)
        {
            if (!(button?.userData is UpdateAction action)) return;

            EditAction(action.key);
        }

        private void OnNativeForceRunActionClicked(Button button)
        {
            if (!(button?.userData is UpdateAction action)) return;

            if (ConfirmForceRunAction(action)) RunNativeUpdateAction(action, true);
        }

        private void OnNativeRunActionClicked(Button button)
        {
            if (!(button?.userData is UpdateAction action)) return;

            RunNativeUpdateAction(action, false);
        }

        private void OnNativeUpdateActionToggleChanged(ChangeEvent<bool> evt)
        {
            if (!(evt.target is Toggle toggle) || !(toggle.userData is UpdateAction action)) return;

            AI.Actions.SetActive(action, evt.newValue);
            AI.SaveConfig();
            RefreshNativeUpdateActionRows();
            UpdateNativeSettingsSummary();
        }

        private void RefreshNativeUpdateActionRows()
        {
            _nativeUpdateActionsHash = GetNativeUpdateActionsHash();
            ApplyNativeUpdateActionsListHeight();
            _nativeUpdateActionsList?.Refresh();
        }

        private Button CreateNativeActionBatchButton(string label, Action click)
        {
            Button button = AssetInventoryUITK.CreateSecondaryButton(label, click);
            button.AddToClassList("ai-settings-action-batch-button");
            return button;
        }

        private void SetAllNativeActionsActive(bool enabled)
        {
            AI.Actions.SetAllActive(enabled);
            AI.SaveConfig();
            RefreshNativeUpdateActionRows();
            UpdateNativeSettingsSummary();
        }

        private void SetDefaultNativeActionsActive()
        {
            AI.Actions.SetDefaultActive();
            AI.SaveConfig();
            RefreshNativeUpdateActionRows();
            UpdateNativeSettingsSummary();
        }

        private void RunNativeUpdateAction(UpdateAction action, bool force)
        {
            if (action == null) return;

            _ = AI.Actions.RunAction(action, force);
            RefreshNativeUpdateActionsSection(true);
            _nativeSettingsSidebarHash = 0;
            UpdateNativeSettingsSidebar();
            MarkUITKShellDirty();
        }

        private bool CanForceRunAction(UpdateAction action)
        {
            return action != null && action.supportsForce && ShowAdvanced();
        }

        private static bool CanEditAction(UpdateAction action)
        {
            return action != null && action.key.StartsWith(ActionHandler.ACTION_USER);
        }

        private int GetNativeUpdateActionsHash()
        {
            unchecked
            {
                int hash = 17;
                hash = AddHash(hash, AI.Config.showActionSettings);
                hash = AddHash(hash, ShowAdvanced());
                hash = AddHash(hash, AI.Actions.AnyActionsInProgress);
                hash = AddHash(hash, AI.Actions.LastActionUpdate.Ticks);
                hash = AddHash(hash, AI.Config.assetManagerFeatureEnabled);
                hash = AddHash(hash, AI.Config.syntyFeatureEnabled);
                hash = AddHash(hash, AI.Config.packageBackupFeatureEnabled);
                hash = AddHash(hash, AI.Config.aiCaptionsFeatureEnabled);
                hash = AddHash(hash, AI.Config.semanticSearchFeatureEnabled);
                hash = AddHash(hash, AI.Config.codeSearchFeatureEnabled);

                foreach (UpdateAction action in AI.Actions.Actions.Where(action => !action.hidden && AI.Actions.IsAvailable(action)))
                {
                    hash = AddHash(hash, action.key);
                    hash = AddHash(hash, action.name);
                    hash = AddHash(hash, action.description);
                    hash = AddHash(hash, action.phase);
                    hash = AddHash(hash, action.supportsForce);
                    hash = AddHash(hash, action.scheduled);
                    hash = AddHash(hash, action.IsRunning());
                    hash = AddHash(hash, AI.Actions.IsActive(action));
                }

                return hash;
            }
        }

        private void ApplyNativeUpdateActionsListHeight()
        {
            if (_nativeUpdateActionsList == null) return;

            int count = _updateActions?.Count ?? 0;
            int maxVisibleRows = position.width < 900f
                ? SettingsActionNarrowMaxVisibleRows
                : SettingsActionWideMaxVisibleRows;
            int minVisibleRows = position.width < 900f
                ? SettingsActionNarrowMinVisibleRows
                : SettingsActionWideMinVisibleRows;
            int visibleRows = Mathf.Clamp(count, minVisibleRows, maxVisibleRows);
            float height = 38f + visibleRows * SettingsActionRowHeight;
            _nativeUpdateActionsList.style.height = height;
        }

        private void RefreshNativeSettingsBody()
        {
            if (_nativeSettingsBody == null) return;

            if (_nativeSettingsBody.childCount == 0)
            {
                RebuildNativeSettingsBody();
            }

            UpdateNativeSettingsSummary();
            RefreshNativeUpdateActionsSection();
            RefreshNativeIndexingSettingsSection();
            RefreshNativeFoldersSettingsSection();
            RefreshNativeAssetManagerSettingsSection();
            RefreshNativeSyntySettingsSection();
            RefreshNativeImportSettingsSection();
            RefreshNativePreviewsSettingsSection();
            RefreshNativeBackupSettingsSection();
            RefreshNativeAISettingsSection();
            RefreshNativeLocationsSettingsSection();
            RefreshNativeUIIntegrationSettingsSection();
            RefreshNativeAdvancedSettingsSection();
            UpdateNativeSettingsLayout();
            UpdateNativeSettingsSidebar();
        }

        private void RebuildNativeSettingsBody()
        {
            if (_nativeSettingsBody == null) return;

            _nativeScrollViewState.Capture("settings-root", _nativeSettingsRootScroll);
            _nativeSettingsBody.Clear();
            _nativeSettingsBody.AddToClassList(SettingsRootClass);

            ScrollView rootScroll = new ScrollView(ScrollViewMode.Vertical)
            {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Auto
            };
            rootScroll.AddToClassList(SettingsRootScrollClass);
            _nativeSettingsRootScroll = rootScroll;
            _nativeSettingsBody.Add(rootScroll);

            VisualElement summary = new VisualElement();
            summary.AddToClassList(SettingsSummaryClass);
            _nativeSettingsActionsValue = CreateNativeSettingsCard(summary, "Actions", "-");
            _nativeSettingsPackagesValue = CreateNativeSettingsCard(summary, "Indexed Packages", "-");
            _nativeSettingsDatabaseValue = CreateNativeSettingsCard(summary, "Database", "-");
            _nativeSettingsLastUpdateValue = CreateNativeSettingsCard(summary, "Last Update", "-");
            rootScroll.Add(summary);

            _nativeSettingsContent = new VisualElement();
            _nativeSettingsContent.AddToClassList(SettingsContentClass);

            _nativeSettingsLeftColumn = new VisualElement();
            _nativeSettingsLeftColumn.AddToClassList(SettingsLeftColumnClass);
            _nativeSettingsContent.Add(_nativeSettingsLeftColumn);

            _nativeSettingsLeftColumn.Add(BuildNativeUpdateActionsSection());
            _nativeSettingsLeftColumn.Add(BuildNativeIndexingSettingsSection());
            _nativeSettingsLeftColumn.Add(BuildNativeFoldersSettingsSection());
            _nativeSettingsLeftColumn.Add(BuildNativeAssetManagerSettingsSection());
            _nativeSettingsLeftColumn.Add(BuildNativeSyntySettingsSection());
            _nativeSettingsLeftColumn.Add(BuildNativeImportSettingsSection());
            _nativeSettingsLeftColumn.Add(BuildNativePreviewsSettingsSection());
            _nativeSettingsLeftColumn.Add(BuildNativeBackupSettingsSection());
            _nativeSettingsLeftColumn.Add(BuildNativeAISettingsSection());
            _nativeSettingsLeftColumn.Add(BuildNativeLocationsSettingsSection());
            _nativeSettingsLeftColumn.Add(BuildNativeUIIntegrationSettingsSection());
            _nativeSettingsLeftColumn.Add(BuildNativeAdvancedSettingsSection());

            _nativeSettingsSidebar = new VisualElement();
            _nativeSettingsSidebar.AddToClassList(SettingsSidebarClass);
            _nativeSettingsContent.Add(_nativeSettingsSidebar);

            rootScroll.Add(_nativeSettingsContent);

            UpdateNativeSettingsSummary();
            UpdateNativeSettingsLayout();
            _nativeSettingsSidebarHash = 0;
            UpdateNativeSettingsSidebar();
            _nativeScrollViewState.Restore("settings-root", rootScroll);
        }

        private static Label CreateNativeSettingsCard(VisualElement parent, string label, string value)
        {
            VisualElement card = new VisualElement();
            card.AddToClassList(SettingsCardClass);

            Label labelElement = new Label(label);
            labelElement.AddToClassList(SettingsCardLabelClass);
            card.Add(labelElement);

            Label valueElement = new Label(value);
            valueElement.AddToClassList(SettingsCardValueClass);
            card.Add(valueElement);

            parent.Add(card);
            return valueElement;
        }

        private void UpdateNativeSettingsSummary()
        {
            if (_nativeSettingsActionsValue == null) return;

            int enabledActions = 0;
            int totalActions = 0;
            if (AI.Actions != null)
            {
                totalActions = AI.Actions.Actions.Count(action => !action.hidden && AI.Actions.IsAvailable(action));
                enabledActions = AI.Actions.Actions.Count(action => !action.hidden && AI.Actions.IsAvailable(action) && AI.Actions.IsActive(action));
            }

            _nativeSettingsActionsValue.text = AI.Actions != null && AI.Actions.AnyActionsInProgress
                ? "Running"
                : $"{enabledActions:N0}/{totalActions:N0} enabled";

            _nativeSettingsPackagesValue.text = _stats == null
                ? "-"
                : $"{_stats.EnabledIndexedPackages:N0}/{_stats.IndexingEnabledPackages:N0}";

            _nativeSettingsDatabaseValue.text = _dbSize > 0
                ? EditorUtility.FormatBytes(_dbSize)
                : "-";

            _nativeSettingsLastUpdateValue.text = AI.Actions != null && AI.Actions.LastActionUpdate != DateTime.MinValue
                ? StringUtils.GetRelativeTimeDifference(AI.Actions.LastActionUpdate)
                : "-";
        }

        private void UpdateNativeSettingsLayout()
        {
            if (_nativeSettingsContent == null || _nativeSettingsLeftColumn == null || _nativeSettingsSidebar == null) return;

            bool stacked = position.width < 900f;
            _nativeSettingsContent.EnableInClassList(SettingsContentStackedClass, stacked);
            ApplyNativeUpdateActionsListHeight();
            if (stacked)
            {
                _nativeSettingsLeftColumn.style.marginRight = 0f;
                _nativeSettingsLeftColumn.style.marginBottom = 8f;
                _nativeSettingsLeftColumn.style.flexGrow = 0f;
                _nativeSettingsLeftColumn.style.width = Length.Percent(100f);
                _nativeSettingsSidebar.style.width = Length.Percent(100f);
                _nativeSettingsSidebar.style.minWidth = 0f;
                _nativeSettingsSidebar.style.flexGrow = 1f;
                _nativeSettingsSidebar.style.flexShrink = 1f;
            }
            else
            {
                _nativeSettingsLeftColumn.style.marginRight = StyleKeyword.Null;
                _nativeSettingsLeftColumn.style.marginBottom = StyleKeyword.Null;
                _nativeSettingsLeftColumn.style.flexGrow = StyleKeyword.Null;
                _nativeSettingsLeftColumn.style.width = StyleKeyword.Null;
                _nativeSettingsSidebar.style.width = StyleKeyword.Null;
                _nativeSettingsSidebar.style.minWidth = StyleKeyword.Null;
                _nativeSettingsSidebar.style.flexGrow = StyleKeyword.Null;
                _nativeSettingsSidebar.style.flexShrink = StyleKeyword.Null;
            }
        }

        private void UpdateNativeSettingsSidebar()
        {
            if (_nativeSettingsSidebar == null) return;

            int hash = GetNativeSettingsSidebarHash();
            if (_nativeSettingsSidebar.childCount > 0 && _nativeSettingsSidebarHash == hash) return;

            _nativeSettingsSidebarHash = hash;
            _nativeScrollViewState.Capture("settings-sidebar", _nativeSettingsSidebarScroll);
            _nativeSettingsSidebar.Clear();

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            _nativeSettingsSidebarScroll = scroll;
            scroll.AddToClassList(SettingsSidebarScrollClass);
            scroll.Add(BuildNativeSettingsUpdateSection());
            scroll.Add(BuildNativeSettingsStatisticsSection());
            scroll.Add(BuildNativeSettingsDiskSpaceSection());
            scroll.Add(BuildNativeSettingsMaintenanceSection());
            _nativeSettingsSidebar.Add(scroll);
            _nativeScrollViewState.Restore("settings-sidebar", scroll);
        }

        private int GetNativeSettingsSidebarHash()
        {
            unchecked
            {
                int hash = 17;
                hash = AddHash(hash, AssetInventoryUITK.GetAdvancedVisibilityStateHash());
                hash = AddHash(hash, _usageCalculationInProgress);
                hash = AddHash(hash, _usageCalculation?.CurrentMain);
                hash = AddHash(hash, AI.Actions != null && AI.Actions.AnyActionsInProgress);
                hash = AddHash(hash, AI.Actions != null && AI.Actions.CancellationRequested);
                hash = AddHash(hash, AI.Actions != null ? AI.Actions.LastActionUpdate.Ticks : 0L);
                hash = AddHash(hash, _stats?.TotalPackages ?? 0);
                hash = AddHash(hash, _stats?.IndexedPackages ?? 0);
                hash = AddHash(hash, _stats?.IndexablePackages ?? 0);
                hash = AddHash(hash, _stats?.IndexingEnabledPackages ?? 0);
                hash = AddHash(hash, _stats?.EnabledIndexedPackages ?? 0);
                hash = AddHash(hash, _stats?.NeedsIndexingPackages ?? 0);
                hash = AddHash(hash, _stats?.IndexedWithoutFutureIndexingPackages ?? 0);
                hash = AddHash(hash, _stats?.TotalFiles ?? 0);
                hash = AddHash(hash, _stats?.PurchasedAssets ?? 0);
                hash = AddHash(hash, _stats?.RegistryPackages ?? 0);
                hash = AddHash(hash, _stats?.CustomPackages ?? 0);
                hash = AddHash(hash, _stats?.DeprecatedPackages ?? 0);
                hash = AddHash(hash, _stats?.AbandonedPackages ?? 0);
                hash = AddHash(hash, _stats?.ExcludedPackages ?? 0);
                hash = AddHash(hash, _stats?.NoIndexPackages ?? 0);
                hash = AddHash(hash, _stats?.SubPackages ?? 0);
                hash = AddHash(hash, _dbSize);
                hash = AddHash(hash, _showStatistics);
                hash = AddHash(hash, _showDiskSpace);
                hash = AddHash(hash, _showMaintenance);
                hash = AddHash(hash, _lastFolderSizeCalculation.Ticks);
                hash = AddHash(hash, _previewSize);
                hash = AddHash(hash, _cacheSize);
                hash = AddHash(hash, _persistedCacheSize);
                hash = AddHash(hash, _backupSize);
                hash = AddHash(hash, _calculatingFolderSizes);
                hash = AddHash(hash, _cleanupInProgress);
                hash = AddHash(hash, Paths.ClearCacheInProgress);
                hash = AddHash(hash, DBAdapter.IsDBOpen());
                AddRunningActionProgressHash(ref hash);
                AddSemanticIndexHash(ref hash, _stats?.SemanticIndex);
                AddCodeIndexHash(ref hash, _stats?.CodeIndex);
                return hash;
            }
        }

        private void AddRunningActionProgressHash(ref int hash)
        {
            if (AI.Actions == null || !AI.Actions.AnyActionsInProgress) return;

            List<UpdateAction> actions = AI.Actions.GetRunningActions();
            foreach (UpdateAction action in actions)
            {
                hash = AddHash(hash, action?.name);
                if (action?.progress == null) continue;

                foreach (ActionProgress progress in action.progress)
                {
                    if (progress == null || !progress.IsRunning()) continue;

                    hash = AddHash(hash, progress.MainProgress);
                    hash = AddHash(hash, progress.MainCount);
                    hash = AddHash(hash, progress.CurrentMain);
                    hash = AddHash(hash, progress.SubProgress);
                    hash = AddHash(hash, progress.SubCount);
                    hash = AddHash(hash, progress.CurrentSub);
                }
            }
        }

        private static void AddSemanticIndexHash(ref int hash, InventoryStats.SemanticIndexStatistics semantic)
        {
            if (semantic == null) return;

            hash = AddHash(hash, semantic.SidecarExists);
            hash = AddHash(hash, semantic.Status);
            hash = AddHash(hash, semantic.Dimension);
            hash = AddHash(hash, semantic.SemanticDatabaseSize);
            hash = AddHash(hash, semantic.AssetItemsReady);
            hash = AddHash(hash, semantic.EligibleAssetCountLastRun);
            hash = AddHash(hash, semantic.AssetItemsStale);
            hash = AddHash(hash, semantic.AssetItemsError);
        }

        private static void AddCodeIndexHash(ref int hash, InventoryStats.CodeIndexStatistics code)
        {
            if (code == null) return;

            hash = AddHash(hash, code.SidecarExists);
            hash = AddHash(hash, code.Status);
            hash = AddHash(hash, code.FtsAvailable);
            hash = AddHash(hash, code.CodeDatabaseSize);
            hash = AddHash(hash, code.DocumentsReady);
            hash = AddHash(hash, code.ChunksReady);
            hash = AddHash(hash, code.DocumentsError);
            hash = AddHash(hash, code.ChunksError);
        }

        private static int AddHash(int hash, object value)
        {
            unchecked
            {
                return hash * 31 + (value?.GetHashCode() ?? 0);
            }
        }

        private VisualElement BuildNativeSettingsUpdateSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Update");
            section.Add(CreateNativeSettingsVisibilityBlock("settings.updateintro", () =>
                AssetInventoryUITK.CreateCopyLabel("Ensure to regularly update the index and fetch the newest updates from the Asset Store.")));

            if (_usageCalculationInProgress)
            {
                section.Add(AssetInventoryUITK.CreateStatusPill("Usage calculation in progress", SettingsUpdateStatusClass));
                if (!string.IsNullOrWhiteSpace(_usageCalculation?.CurrentMain))
                {
                    section.Add(CreateNativeSettingsNote(_usageCalculation.CurrentMain));
                }
                return section;
            }

            if (AI.Actions != null && AI.Actions.AnyActionsInProgress)
            {
                Button stopButton = AssetInventoryUITK.CreateSecondaryButton("Stop Actions", () =>
                {
                    AI.Actions.CancelAll();
                    _nativeSettingsSidebarHash = 0;
                    UpdateNativeSettingsSidebar();
                });
                stopButton.SetEnabled(!AI.Actions.CancellationRequested);
                section.Add(stopButton);

                Label running = new Label("Currently Running");
                running.AddToClassList(SettingsCompactNoteClass);
                section.Add(running);
                AddNativeSettingsRunningActions(section);

                section.Add(CreateNativeSettingsVisibilityBlock("settings.hints.indexinterruption", () =>
                    CreateNativeSettingsNote("Indexing can be interrupted and resumed at any time. Warnings and errors can appear in the console during the process and are usually normal.")));
                return section;
            }

            Button runButton = AssetInventoryUITK.CreatePrimaryButton("Run Actions", () =>
            {
                PerformFullUpdate();
                _nativeSettingsSidebarHash = 0;
                UpdateNativeSettingsSidebar();
            });
            runButton.tooltip = "Run all enabled actions in one go and perform all necessary updates.";
            section.Add(runButton);

            if (AI.Actions != null && AI.Actions.LastActionUpdate != DateTime.MinValue)
            {
                section.Add(CreateNativeSettingsVisibilityBlock("settings.lastupdate", () =>
                    CreateNativeSettingsNote($"Last updated {StringUtils.GetRelativeTimeDifference(AI.Actions.LastActionUpdate)}")));
            }

            return section;
        }

        private void AddNativeSettingsRunningActions(VisualElement section)
        {
            List<UpdateAction> actions = AI.Actions.GetRunningActions();
            foreach (UpdateAction action in actions)
            {
                if (action?.progress == null) continue;

                foreach (ActionProgress progress in action.progress)
                {
                    if (progress == null || !progress.IsRunning()) continue;

                    VisualElement group = new VisualElement();
                    group.AddToClassList(SettingsProgressGroupClass);
                    group.Add(new Label(action.name));
                    group.Add(AssetInventoryUITK.CreateProgressBar(
                        FormatNativeSettingsProgressTitle(progress.MainProgress, progress.MainCount, progress.CurrentMain),
                        GetNativeSettingsProgress(progress.MainProgress, progress.MainCount)));

                    if (!string.IsNullOrWhiteSpace(progress.CurrentSub))
                    {
                        group.Add(AssetInventoryUITK.CreateProgressBar(
                            FormatNativeSettingsProgressTitle(progress.SubProgress, progress.SubCount, progress.CurrentSub),
                            GetNativeSettingsProgress(progress.SubProgress, progress.SubCount)));
                    }

                    section.Add(group);
                }
            }
        }

        internal static Foldout CreateNativeSettingsFoldout(string title, bool value, Action<bool> onChange, params string[] classNames)
        {
            string tooltip;
            switch (title)
            {
                case "Indexing": tooltip = "Configure where packages are discovered and how source caches are indexed."; break;
                case "Additional Folders": tooltip = "Add and organize external package, archive, media, and development-package locations."; break;
                case "Unity Asset Manager": tooltip = "Configure packages synchronized from Unity Asset Manager projects."; break;
                case "Synty Importer": tooltip = "Configure experimental local cache indexing and optional Asset Store metadata enrichment for downloaded Synty packages."; break;
                case "Import": tooltip = "Control how selected assets are materialized and adapted to the current project."; break;
                case "Previews": tooltip = "Control preview discovery, validation, recreation, and Project-window integration."; break;
                case "Backup": tooltip = "Configure automatic version backups for selected packages."; break;
                case "Artificial Intelligence": tooltip = "Configure optional captions, semantic search, code search, and local model backends."; break;
                case "Locations": tooltip = "Review and move the database, cache, previews, backups, and index storage."; break;
                case "UI Integration": tooltip = "Choose where Asset Inventory appears in Unity menus and editor windows."; break;
                case "Advanced": tooltip = "Tune expert behavior, diagnostics, performance, and interface preferences."; break;
                default: tooltip = "Choose which update and indexing tasks run during maintenance."; break;
            }

            Foldout foldout = AssetInventoryUITK.CreateFoldout(title, value, onChange, tooltip, classNames);
            foldout.AddToClassList(SettingsSectionFoldoutClass);
            return foldout;
        }

        private Foldout CreateNativeSettingsSidebarFoldout(string title, bool value, Action<bool> onChange, string tooltip)
        {
            return AssetInventoryUITK.CreateFoldout(title, value, next =>
            {
                onChange?.Invoke(next);
                _nativeSettingsSidebarHash = 0;
                UpdateNativeSettingsSidebar();
            }, tooltip, SettingsSidebarFoldoutClass);
        }

        private VisualElement BuildNativeSettingsStatisticsSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection();
            Foldout foldout = CreateNativeSettingsSidebarFoldout("Statistics", _showStatistics, value => _showStatistics = value, "Show indexed package and file counts.");
            section.Add(foldout);

            if (!_showStatistics) return section;

            foldout.Add(CreateNativeSettingsVisibilityBlock("settings.statistics", BuildNativeSettingsStatisticsContent));

            if (_stats != null && _stats.NeedsIndexingPackages > 0 && AI.Actions != null && !AI.Actions.AnyActionsInProgress)
            {
                foldout.Add(CreateNativeSettingsVisibilityBlock("settings.hints.indexremaining", () =>
                    AssetInventoryUITK.CreateHelpBox("Included packages still need indexing. Review them in Packages to index cached or uncached packages explicitly.")));
            }

            return section;
        }

        private VisualElement BuildNativeSettingsStatisticsContent()
        {
            EnsureVisibleIndexStatsLoaded();

            VisualElement content = new VisualElement();
            int totalPackages = _assets?.Count ?? _stats?.TotalPackages ?? 0;
            content.Add(AssetInventoryUITK.CreateKeyValueRow("Total Packages", $"{totalPackages:N0}"));
            if (_stats != null)
            {
                content.Add(AssetInventoryUITK.CreateKeyValueRow("Indexing Enabled", $"{_stats.IndexingEnabledPackages:N0}"));
                content.Add(AssetInventoryUITK.CreateKeyValueRow("Indexed", $"{_stats.EnabledIndexedPackages:N0}/{_stats.IndexingEnabledPackages:N0}"));
                if (_stats.NeedsIndexingPackages > 0) content.Add(AssetInventoryUITK.CreateKeyValueRow("Needs Indexing", $"{_stats.NeedsIndexingPackages:N0}"));
                if (_stats.PurchasedAssets > 0) content.Add(AssetInventoryUITK.CreateKeyValueRow("Asset Store", $"{_stats.PurchasedAssets:N0}"));
                if (_stats.RegistryPackages > 0) content.Add(AssetInventoryUITK.CreateKeyValueRow("Registries", $"{_stats.RegistryPackages:N0}"));
                if (_stats.CustomPackages > 0) content.Add(AssetInventoryUITK.CreateKeyValueRow("Other Sources", $"{_stats.CustomPackages:N0}"));
                if (_stats.DeprecatedPackages > 0) content.Add(AssetInventoryUITK.CreateKeyValueRow("Deprecated", $"{_stats.DeprecatedPackages:N0}"));
                if (_stats.AbandonedPackages > 0) content.Add(AssetInventoryUITK.CreateKeyValueRow("Abandoned", $"{_stats.AbandonedPackages:N0}"));
                if (_stats.ExcludedPackages > 0) content.Add(BuildNativeSettingsExcludedPackagesRow());
                if (_stats.NoIndexPackages > 0) content.Add(BuildNativeSettingsNotIncludedPackagesRow());
                if (_stats.IndexedWithoutFutureIndexingPackages > 0) content.Add(AssetInventoryUITK.CreateKeyValueRow("Indexed, Future Off", $"{_stats.IndexedWithoutFutureIndexingPackages:N0}"));
                if (_stats.SubPackages > 0) content.Add(AssetInventoryUITK.CreateKeyValueRow("Sub-Packages", $"{_stats.SubPackages:N0}"));
                if (_stats.TotalFiles > 0) content.Add(AssetInventoryUITK.CreateKeyValueRow("Indexed Files", $"{_stats.TotalFiles:N0}"));
            }
            AddNativeSettingsSemanticIndexStats(content);
            AddNativeSettingsCodeIndexStats(content);
            return content;
        }

        private VisualElement BuildNativeSettingsExcludedPackagesRow()
        {
            VisualElement row = AssetInventoryUITK.CreateKeyValueRow("Excluded", $"{_stats.ExcludedPackages:N0}");
            Button showButton = AssetInventoryUITK.CreateIconButton("Show excluded packages", "d_animationvisibilitytoggleon", () =>
            {
                ShowPackageMaintenance(PackageSearch.MaintenanceOption.Excluded);
            });
            row.Add(showButton);
            return row;
        }

        private VisualElement BuildNativeSettingsNotIncludedPackagesRow()
        {
            VisualElement row = AssetInventoryUITK.CreateKeyValueRow("Not Included", $"{_stats.NoIndexPackages:N0}");
            Button showButton = AssetInventoryUITK.CreateIconButton("Review packages not included in indexing", "d_FilterByLabel", () =>
            {
                ShowPackageMaintenance(PackageSearch.MaintenanceOption.NoIndex);
            });
            row.Add(showButton);
            return row;
        }

        private void AddNativeSettingsSemanticIndexStats(VisualElement content)
        {
            if (AI.Actions == null || !ShouldShowSemanticIndexStats(AI.Actions.SemanticSearchEnabled)) return;

            InventoryStats.SemanticIndexStatistics semantic = _stats?.SemanticIndex;
            if (semantic == null)
            {
                content.Add(AssetInventoryUITK.CreateKeyValueRow("Semantic Index", "-not loaded-"));
                return;
            }

            content.Add(AssetInventoryUITK.CreateKeyValueRow("Semantic Index", semantic.Status ?? (semantic.SidecarExists ? "Available" : "Not created")));
            if (semantic.Dimension > 0) content.Add(AssetInventoryUITK.CreateKeyValueRow("Dimensions", $"{semantic.Dimension:N0}"));
            if (semantic.AssetItemsReady > 0 || semantic.EligibleAssetCountLastRun > 0)
            {
                string coverage = semantic.EligibleAssetCountLastRun > 0
                    ? $"{semantic.AssetItemsReady:N0}/{semantic.EligibleAssetCountLastRun:N0}"
                    : $"{semantic.AssetItemsReady:N0}";
                content.Add(AssetInventoryUITK.CreateKeyValueRow("Assets", coverage));
            }
            if (semantic.AssetItemsStale > 0) content.Add(AssetInventoryUITK.CreateKeyValueRow("Needs Update", $"{semantic.AssetItemsStale:N0}"));
            if (semantic.AssetItemsError > 0) content.Add(AssetInventoryUITK.CreateKeyValueRow("Errors", $"{semantic.AssetItemsError:N0}"));
        }

        private void AddNativeSettingsCodeIndexStats(VisualElement content)
        {
            if (AI.Actions == null || !ShouldShowCodeIndexStats(AI.Actions.CodeSearchEnabled)) return;

            InventoryStats.CodeIndexStatistics code = _stats?.CodeIndex;
            if (code == null)
            {
                content.Add(AssetInventoryUITK.CreateKeyValueRow("Code Index", "-not loaded-"));
                return;
            }

            content.Add(AssetInventoryUITK.CreateKeyValueRow("Code Index", code.Status ?? (code.SidecarExists ? "Available" : "Not created")));
            if (ShowAdvanced()) content.Add(AssetInventoryUITK.CreateKeyValueRow("Search Engine", code.FtsAvailable ? "FTS5" : "Fallback"));
            if (code.DocumentsReady > 0) content.Add(AssetInventoryUITK.CreateKeyValueRow("Files", $"{code.DocumentsReady:N0}"));
            if (code.ChunksReady > 0) content.Add(AssetInventoryUITK.CreateKeyValueRow("Snippets", $"{code.ChunksReady:N0}"));
            if (code.DocumentsError > 0 || code.ChunksError > 0) content.Add(AssetInventoryUITK.CreateKeyValueRow("Errors", $"{code.DocumentsError + code.ChunksError:N0}"));
        }

        private VisualElement BuildNativeSettingsDiskSpaceSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection();
            section.Add(CreateNativeSettingsVisibilityBlock("settings.diskspace", BuildNativeSettingsDiskSpaceContent));
            return section;
        }

        private VisualElement BuildNativeSettingsDiskSpaceContent()
        {
            Foldout foldout = CreateNativeSettingsSidebarFoldout("Disk Usage", _showDiskSpace, value => _showDiskSpace = value, "Show database, cache, preview, backup, and index storage usage.");

            if (_showDiskSpace)
            {
                AddNativeSettingsDatabaseDiskUsage(foldout);
                AddNativeSettingsIndexDiskUsage(foldout);

                Label generatedFiles = new Label("Generated Files");
                generatedFiles.AddToClassList(SettingsSidebarSubsectionClass);
                foldout.Add(generatedFiles);

                if (_lastFolderSizeCalculation != DateTime.MinValue)
                {
                    foldout.Add(AssetInventoryUITK.CreateKeyValueRow("Previews", EditorUtility.FormatBytes(_previewSize)));
                    foldout.Add(AssetInventoryUITK.CreateKeyValueRow("Cache", EditorUtility.FormatBytes(_cacheSize)));
                    foldout.Add(AssetInventoryUITK.CreateKeyValueRow("Persistent Cache", EditorUtility.FormatBytes(_persistedCacheSize)));
                    foldout.Add(AssetInventoryUITK.CreateKeyValueRow("Backups", EditorUtility.FormatBytes(_backupSize)));
                    foldout.Add(CreateNativeSettingsNote("Last updated " + _lastFolderSizeCalculation.ToShortTimeString()));
                }
                else
                {
                    foldout.Add(CreateNativeSettingsNote("Not calculated yet."));
                }

                Button refresh = AssetInventoryUITK.CreateSecondaryButton(_calculatingFolderSizes ? "Calculating..." : "Refresh", () =>
                {
                    CalcFolderSizes();
                    _nativeSettingsSidebarHash = 0;
                    UpdateNativeSettingsSidebar();
                });
                refresh.SetEnabled(!_calculatingFolderSizes);
                foldout.Add(refresh);
            }

            return foldout;
        }

        private void AddNativeSettingsDatabaseDiskUsage(VisualElement content)
        {
            content.Add(AssetInventoryUITK.CreateKeyValueRow("Database", _dbSize > 0 ? EditorUtility.FormatBytes(_dbSize) : "-"));
        }

        private void AddNativeSettingsIndexDiskUsage(VisualElement content)
        {
            InventoryStats.SemanticIndexStatistics semantic = _stats?.SemanticIndex;
            if (AI.Actions != null && ShouldShowSemanticIndexStats(AI.Actions.SemanticSearchEnabled) && semantic != null && semantic.SidecarExists)
            {
                content.Add(AssetInventoryUITK.CreateKeyValueRow("Semantic Index", EditorUtility.FormatBytes(semantic.SemanticDatabaseSize)));
            }

            InventoryStats.CodeIndexStatistics code = _stats?.CodeIndex;
            if (AI.Actions != null && ShouldShowCodeIndexStats(AI.Actions.CodeSearchEnabled) && code != null && code.SidecarExists)
            {
                content.Add(AssetInventoryUITK.CreateKeyValueRow("Code Index", EditorUtility.FormatBytes(code.CodeDatabaseSize)));
            }
        }

        private VisualElement BuildNativeSettingsMaintenanceSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection();
            Foldout foldout = CreateNativeSettingsSidebarFoldout("Maintenance", _showMaintenance, value => _showMaintenance = value, "Open repair, validation, and setup recovery tools.");
            section.Add(foldout);

            if (!_showMaintenance) return section;

            bool actionsIdle = AI.Actions == null || !AI.Actions.AnyActionsInProgress;
            foldout.Add(CreateNativeSettingsVisibilityBlock("settings.actions.maintenance", () =>
            {
                Button button = AssetInventoryUITK.CreateSecondaryButton("Maintenance Wizard...", () => MaintenanceUI.ShowWindow());
                button.SetEnabled(actionsIdle);
                return button;
            }));
            foldout.Add(CreateNativeSettingsVisibilityBlock("settings.actions.recreatepreviews", () =>
            {
                Button button = AssetInventoryUITK.CreateSecondaryButton("Previews Wizard...", () =>
                {
                    PreviewWizardUI previewsUI = PreviewWizardUI.ShowWindow();
                    previewsUI.Init(null, _assets);
                });
                button.SetEnabled(actionsIdle);
                return button;
            }));
            foldout.Add(CreateNativeSettingsVisibilityBlock("settings.actions.clearcache", () =>
            {
                Button button = AssetInventoryUITK.CreateSecondaryButton("Clear Cache", () =>
                {
                    Paths.ClearCache(() => UpdateStatistics(true));
                    _nativeSettingsSidebarHash = 0;
                    UpdateNativeSettingsSidebar();
                });
                button.tooltip = "Will delete the Extracted folder used for speeding up asset access. It will be recreated automatically when needed.";
                button.SetEnabled(actionsIdle && !Paths.ClearCacheInProgress);
                return button;
            }));
            foldout.Add(CreateNativeSettingsVisibilityBlock("settings.actions.cleardb", () =>
            {
                Button button = AssetInventoryUITK.CreateDestructiveButton("Clear Database", ClearDatabaseFromNativeSettings);
                button.tooltip = "Will reset the database to its initial empty state. ALL data in the index will be lost.";
                button.SetEnabled(actionsIdle);
                return button;
            }));

            VisualElement resetRow = new VisualElement();
            resetRow.AddToClassList(SettingsButtonRowClass);
            resetRow.Add(CreateNativeSettingsVisibilityBlock("settings.actions.resetconfig", () =>
            {
                Button button = AssetInventoryUITK.CreateSecondaryButton("Reset Configuration", AI.ResetConfig);
                button.tooltip = "Will reset the configuration to default values, also deleting all Additional Folder configurations.";
                button.SetEnabled(actionsIdle);
                return button;
            }));
            resetRow.Add(CreateNativeSettingsVisibilityBlock("settings.actions.resetuiconfig", () =>
            {
                Button button = AssetInventoryUITK.CreateSecondaryButton("Reset UI Customization", AI.ResetUICustomization);
                button.tooltip = "Will reset the visibility of UI elements to initial default values.";
                button.SetEnabled(actionsIdle);
                return button;
            }));
            foldout.Add(resetRow);

            foldout.Add(CreateNativeSettingsVisibilityBlock("settings.actions.optimizedb", () =>
            {
                Button button = AssetInventoryUITK.CreateSecondaryButton("Optimize Database", () => OptimizeDatabase());
                button.tooltip = "Compact and optimize the current database.";
                button.SetEnabled(actionsIdle && !_cleanupInProgress);
                return button;
            }));

            if (DBAdapter.IsDBOpen())
            {
                foldout.Add(CreateNativeSettingsVisibilityBlock("settings.actions.closedb", () =>
                {
                    Button button = AssetInventoryUITK.CreateSecondaryButton("Close Database", DBAdapter.Close);
                    button.tooltip = "Will allow safely copying the database in the file system. The database will reopen automatically upon activity.";
                    button.SetEnabled(actionsIdle);
                    return button;
                }));
            }

            return section;
        }

        internal VisualElement BuildNativeIndexingSettingsSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection();
            section.AddToClassList(SettingsIndexingSectionClass);
            _nativeIndexingSettingsSection = section;
            _nativeIndexingSettingsHash = int.MinValue;
            RefreshNativeIndexingSettingsSection(true);
            return section;
        }

        internal void RefreshNativeIndexingSettingsSection(bool force = false)
        {
            if (_nativeIndexingSettingsSection == null || AI.Config == null) return;

            int hash = GetNativeIndexingSettingsHash();
            if (!force && _nativeIndexingSettingsHash == hash) return;

            _nativeIndexingSettingsHash = hash;
            _nativeIndexingSettingsSection.Clear();

            Foldout foldout = CreateNativeSettingsFoldout("Indexing", AI.Config.showIndexingSettings, value =>
            {
                AI.Config.showIndexingSettings = value;
                AI.SaveConfig();
                RefreshNativeIndexingSettingsSection(true);
            });
            _nativeIndexingSettingsSection.Add(foldout);

            if (!AI.Config.showIndexingSettings) return;

            VisualElement participation = AddNativeSettingsGroup(
                foldout,
                "Indexing Participation",
                "Choose the default for newly discovered packages. Existing package choices are never changed automatically.");
            participation.Add(CreateNativeSettingsPopupRow(
                "New Packages",
                "Choose whether newly discovered packages are indexed automatically or wait for an explicit selection.",
                new[] {"Index Automatically", "Wait for My Selection"},
                AI.Config.noIndexByDefault ? 1 : 0,
                value =>
                {
                    AI.Config.noIndexByDefault = value == 1;
                    ApplyNativeIndexingSettingsChange();
                    RefreshNativeIndexingSettingsSection(true);
                }));
            participation.Add(CreateNativeSettingsValueRow(
                "Current Catalog",
                "Current package participation is controlled independently per package.",
                CreateNativeSettingsValueText(_stats == null
                    ? "Statistics not loaded"
                    : $"{_stats.IndexingEnabledPackages:N0} enabled, {_stats.NoIndexPackages:N0} not included, {_stats.ExcludedPackages:N0} excluded")));

            VisualElement participationActions = new VisualElement();
            participationActions.AddToClassList(SettingsButtonRowClass);
            Button reviewNotIncluded = AssetInventoryUITK.CreateSecondaryButton("Review Not Included", () => ShowPackageMaintenance(PackageSearch.MaintenanceOption.NoIndex));
            reviewNotIncluded.tooltip = "Open packages that will be skipped by future indexing runs.";
            participationActions.Add(reviewNotIncluded);
            Button reviewNeedsIndexing = AssetInventoryUITK.CreateSecondaryButton("Review Needs Indexing", () => ShowPackageMaintenance(PackageSearch.MaintenanceOption.NeedsIndexing));
            reviewNeedsIndexing.tooltip = "Open included packages that do not have indexed content yet.";
            participationActions.Add(reviewNeedsIndexing);
            participation.Add(CreateNativeSettingsValueRow("Review", "Review and change current packages explicitly.", participationActions));

            if (AI.Config.excludeByDefault)
            {
                participation.Add(AssetInventoryUITK.CreateHelpBox(
                    "The advanced Exclude default is also enabled. New packages remain excluded and cannot be processed until included again.",
                    MessageType.Warning));
            }

            VisualElement sources = AddNativeSettingsGroup(
                foldout,
                "Sources and Caches",
                "Choose which Unity-managed package locations are discovered. Custom paths are only needed when automatic detection is wrong.");
            sources.Add(CreateNativeSettingsVisibilityBlock("settings.locationintro", () =>
                AssetInventoryUITK.CreateHelpBox("Asset Inventory indexes Unity's Asset Store and registry caches. Add downloads from other sources under Additional Folders.")));

            sources.Add(CreateNativeSettingsPopupRow(
                "Asset Cache Location",
                "How to determine where Unity stores downloaded asset packages.",
                _assetCacheLocationOptions,
                AI.Config.assetCacheLocationType,
                value =>
                {
                    AI.Config.assetCacheLocationType = value;
                    ApplyNativeIndexingSettingsChange();
                    RefreshNativeIndexingSettingsSection(true);
                }));

            switch (AI.Config.assetCacheLocationType)
            {
                case 0:
                    sources.Add(CreateNativeSettingsVisibilityBlock("settings.actions.openassetcache", () =>
                        CreateNativeSettingsOpenFolderRow("Detected Asset Cache", Paths.GetAssetCacheFolder(), Paths.GetAssetCacheFolder())));
#if UNITY_2022_1_OR_NEWER
                    if (string.IsNullOrWhiteSpace(AssetStore.GetAssetCacheFolder()))
                    {
                        sources.Add(AssetInventoryUITK.CreateHelpBox("If Unity uses another Asset Store cache, set ASSETSTORE_CACHE_PATH or choose Custom below."));
                    }
#endif
                    break;

                case 1:
                    sources.Add(CreateNativeSettingsFolderRow(
                        "Custom Asset Cache",
                        Paths.GetAssetCacheFolder(),
                        AI.Config.assetCacheLocation,
                        value =>
                        {
                            AI.Config.assetCacheLocation = value;
                            Paths.ClearCaches();
                            AI.GetObserver().SetPath(Paths.GetAssetCacheFolder());
                            Paths.LoadRelativeLocations();
                            _requireLookupUpdate = ChangeImpact.Write;
                        },
                        "Select asset cache folder of Unity (ending with 'Asset Store-5.x')",
                        validate =>
                        {
                            if (!string.Equals(Path.GetFileName(validate), AI.ASSET_STORE_FOLDER_NAME, StringComparison.OrdinalIgnoreCase))
                            {
                                EditorUtility.DisplayDialog("Error", $"Not a valid Unity asset cache folder. It should point to a folder ending with '{AI.ASSET_STORE_FOLDER_NAME}'", "OK");
                                return false;
                            }
                            return true;
                        }));
                    sources.Add(CreateNativeSettingsVisibilityBlock("settings.customlocationwarning", () =>
                        AssetInventoryUITK.CreateHelpBox("Use a custom cache only when automatic detection is wrong. Asset Inventory cannot change where Unity downloads packages.", MessageType.Warning)));
                    break;
            }

            sources.Add(CreateNativeSettingsPopupRow(
                "Package Cache Location",
                "How to determine where Unity stores downloaded registry packages.",
                _assetCacheLocationOptions,
                AI.Config.packageCacheLocationType,
                value =>
                {
                    AI.Config.packageCacheLocationType = value;
                    ApplyNativeIndexingSettingsChange();
                    RefreshNativeIndexingSettingsSection(true);
                }));

            switch (AI.Config.packageCacheLocationType)
            {
                case 0:
                    sources.Add(CreateNativeSettingsVisibilityBlock("settings.actions.openpackagecache", () =>
                        CreateNativeSettingsOpenFolderRow("Detected Package Cache", Paths.GetPackageCacheFolder(), Paths.GetPackageCacheFolder())));
                    break;

                case 1:
                    sources.Add(CreateNativeSettingsFolderRow(
                        "Custom Package Cache",
                        Paths.GetPackageCacheFolder(),
                        AI.Config.packageCacheLocation,
                        value =>
                        {
                            AI.Config.packageCacheLocation = value;
                            ApplyNativeIndexingSettingsChange();
                        },
                        "Select package cache folder"));
                    break;
            }

            VisualElement content = AddNativeSettingsGroup(
                foldout,
                "Indexed Content",
                "Control how deeply packages are inspected and which searchable metadata is collected.");
            content.Add(CreateNativeSettingsToggleRow(
                "Index Sub-Packages",
                "Scan packages for nested .unitypackage files and index those too. Recommended for SRP support because SRP packages are often nested inside other packages.",
                AI.Config.indexSubPackages,
                value =>
                {
                    AI.Config.indexSubPackages = value;
                    ApplyNativeIndexingSettingsChange();
                }));

            if (ShowAdvanced())
            {
                content.Add(CreateNativeSettingsToggleRow("Extract Full Metadata", "Extract dimensions from images and length from audio files to make these searchable at the cost of a slower indexing process.", AI.Config.gatherExtendedMetadata, value => { AI.Config.gatherExtendedMetadata = value; ApplyNativeIndexingSettingsChange(); }));
                content.Add(CreateNativeSettingsToggleRow("Index Asset Package Contents", "Extract asset packages (.unitypackage) and make their contents searchable. Deactivate only if you are solely interested in package metadata.", AI.Config.indexAssetPackageContents, value => { AI.Config.indexAssetPackageContents = value; ApplyNativeIndexingSettingsChange(); }));
                content.Add(CreateNativeSettingsToggleRow("Exclude Hidden Packages", "Activate the exclude flag for packages hidden by the user on the Asset Store.", AI.Config.excludeHidden, value => { AI.Config.excludeHidden = value; ApplyNativeIndexingSettingsChange(); }));
                content.Add(CreateNativeSettingsIntegerRow("Directory Package Media Count", "Number of media entries to create for directory packages from evenly spaced previews. Set to 0 to disable.", AI.Config.directoryPackageMediaCount, value => { AI.Config.directoryPackageMediaCount = Mathf.Max(0, value); ApplyNativeIndexingSettingsChange(); }));
            }

            VisualElement downloads = AddNativeSettingsGroup(
                foldout,
                "Automatic Downloads",
                "Set cache retention and size limits for packages downloaded during indexing or preview creation.");
            downloads.Add(CreateNativeSettingsToggleRow(
                "Keep Downloaded Assets",
                "Do not delete automatically downloaded assets after indexing. Keep them in the cache instead.",
                AI.Config.keepAutoDownloads,
                value =>
                {
                    AI.Config.keepAutoDownloads = value;
                    ApplyNativeIndexingSettingsChange();
                }));
            downloads.Add(CreateNativeSettingsAutoDownloadLimitRow());

            if (ShowAdvanced())
            {
                VisualElement recovery = AddNativeSettingsGroup(
                    foldout,
                    "Recovery and Scheduling",
                    "Tune how interrupted, missing, or long-running indexing work is recovered and paced.");
                recovery.Add(CreateNativeSettingsToggleRow("Remove Unresolvable Files", "Remove database entries whose extracted source file no longer exists.", AI.Config.removeUnresolveableDBFiles, value => { AI.Config.removeUnresolveableDBFiles = value; ApplyNativeIndexingSettingsChange(); }));
                recovery.Add(CreateNativeSettingsToggleRow("Auto-Schedule for Reindexing", "Schedule affected packages for reindexing when source files cannot be resolved.", AI.Config.markUnresolveableForReindexing, value => { AI.Config.markUnresolveableForReindexing = value; ApplyNativeIndexingSettingsChange(); }));
                recovery.Add(CreateNativeSettingsEnumRow("Tag Slash Handling", "How to handle tags containing '/' characters from the Asset Store.", AI.Config.tagSlashHandling, value => { AI.Config.tagSlashHandling = value; ApplyNativeIndexingSettingsChange(); }));
                recovery.Add(CreateNativeSettingsCooldownRow());

                VisualElement defaults = AddNativeSettingsGroup(
                    foldout,
                    "Defaults for New Packages",
                    "Apply these flags when a package is first discovered. Existing package choices are not changed.");
                defaults.Add(CreateNativeSettingsToggleRow("Keep Cached", "Set the Keep Cached flag on newly discovered assets.", AI.Config.extractByDefault, value => { AI.Config.extractByDefault = value; ApplyNativeIndexingSettingsChange(); }));
                if (AI.Config.packageBackupFeatureEnabled)
                    defaults.Add(CreateNativeSettingsToggleRow("Backup", "Mark newly discovered packages to be backed up automatically.", AI.Config.backupByDefault, value => { AI.Config.backupByDefault = value; ApplyNativeIndexingSettingsChange(); }));
                if (AI.Config.aiCaptionsFeatureEnabled)
                    defaults.Add(CreateNativeSettingsToggleRow("AI Captions", "Set the AI Caption flag on newly discovered assets.", AI.Config.captionByDefault, value => { AI.Config.captionByDefault = value; ApplyNativeIndexingSettingsChange(); }));
                if (AI.Config.semanticSearchFeatureEnabled)
                    defaults.Add(CreateNativeSettingsToggleRow("Semantic Index", "Include newly discovered packages in the semantic asset index.", AI.Config.semanticIndexByDefault, value => { AI.Config.semanticIndexByDefault = value; ApplyNativeIndexingSettingsChange(); }));
                if (AI.Config.codeSearchFeatureEnabled)
                    defaults.Add(CreateNativeSettingsToggleRow("Code Index", "Include newly discovered packages in the code search index.", AI.Config.codeIndexByDefault, value => { AI.Config.codeIndexByDefault = value; ApplyNativeIndexingSettingsChange(); }));
                defaults.Add(CreateNativeSettingsToggleRow("Exclude", "Mark newly discovered packages as excluded so they are not shown in the normal package list and are not processed further.", AI.Config.excludeByDefault, value => { AI.Config.excludeByDefault = value; ApplyNativeIndexingSettingsChange(); }));
            }
        }

        private int GetNativeIndexingSettingsHash()
        {
            unchecked
            {
                int hash = 17;
                hash = AddHash(hash, AI.Config.showIndexingSettings);
                hash = AddHash(hash, ShowAdvanced());
                hash = AddHash(hash, AssetInventoryUITK.GetAdvancedVisibilityStateHash());
                hash = AddHash(hash, AI.Config.assetCacheLocationType);
                hash = AddHash(hash, AI.Config.assetCacheLocation);
                hash = AddHash(hash, Paths.GetAssetCacheFolder());
#if UNITY_2022_1_OR_NEWER
                hash = AddHash(hash, AssetStore.GetAssetCacheFolder());
#endif
                hash = AddHash(hash, AI.Config.packageCacheLocationType);
                hash = AddHash(hash, AI.Config.packageCacheLocation);
                hash = AddHash(hash, Paths.GetPackageCacheFolder());
                hash = AddHash(hash, AI.Config.indexSubPackages);
                hash = AddHash(hash, AI.Config.keepAutoDownloads);
                hash = AddHash(hash, AI.Config.limitAutoDownloads);
                hash = AddHash(hash, AI.Config.downloadLimit);
                hash = AddHash(hash, AI.Config.gatherExtendedMetadata);
                hash = AddHash(hash, AI.Config.indexAssetPackageContents);
                hash = AddHash(hash, AI.Config.excludeHidden);
                hash = AddHash(hash, AI.Config.removeUnresolveableDBFiles);
                hash = AddHash(hash, AI.Config.markUnresolveableForReindexing);
                hash = AddHash(hash, AI.Config.directoryPackageMediaCount);
                hash = AddHash(hash, AI.Config.extractByDefault);
                hash = AddHash(hash, AI.Config.backupByDefault);
                hash = AddHash(hash, AI.Config.captionByDefault);
                hash = AddHash(hash, AI.Config.semanticIndexByDefault);
                hash = AddHash(hash, AI.Config.codeIndexByDefault);
                hash = AddHash(hash, AI.Config.packageBackupFeatureEnabled);
                hash = AddHash(hash, AI.Config.aiCaptionsFeatureEnabled);
                hash = AddHash(hash, AI.Config.semanticSearchFeatureEnabled);
                hash = AddHash(hash, AI.Config.codeSearchFeatureEnabled);
                hash = AddHash(hash, AI.Config.noIndexByDefault);
                hash = AddHash(hash, AI.Config.excludeByDefault);
                hash = AddHash(hash, _stats?.IndexingEnabledPackages ?? 0);
                hash = AddHash(hash, _stats?.NoIndexPackages ?? 0);
                hash = AddHash(hash, _stats?.ExcludedPackages ?? 0);
                hash = AddHash(hash, AI.Config.tagSlashHandling);
                hash = AddHash(hash, AI.Config.useCooldown);
                hash = AddHash(hash, AI.Config.cooldownInterval);
                hash = AddHash(hash, AI.Config.cooldownDuration);
                return hash;
            }
        }

        private VisualElement BuildNativeFoldersSettingsSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection();
            section.AddToClassList(SettingsFoldersSectionClass);
            _nativeFoldersSettingsSection = section;
            _nativeFoldersSettingsHash = int.MinValue;
            RefreshNativeFoldersSettingsSection(true);
            return section;
        }

        private void RefreshNativeFoldersSettingsSection(bool force = false)
        {
            if (_nativeFoldersSettingsSection == null || AI.Config == null) return;

            int hash = GetNativeFoldersSettingsHash();
            if (!force && _nativeFoldersSettingsHash == hash)
            {
                RefreshNativeFolderRows();
                return;
            }

            CaptureNativeFoldersScrollState();
            _nativeFoldersSettingsHash = hash;
            _nativeFoldersSettingsSection.Clear();

            Foldout foldout = CreateNativeSettingsFoldout("Additional Folders", AI.Config.showFolderSettings, value =>
            {
                AI.Config.showFolderSettings = value;
                AI.SaveConfig();
                RefreshNativeFoldersSettingsSection(true);
            });
            _nativeFoldersSettingsSection.Add(foldout);

            if (!AI.Config.showFolderSettings)
            {
                _nativeFoldersList = null;
                return;
            }

            foldout.Add(CreateNativeSettingsVisibilityBlock("settings.foldersintro", () =>
                AssetInventoryUITK.CreateHelpBox("Add package archives, development packages, and media libraries stored outside Unity's caches.")));

            foldout.Add(BuildNativeFoldersHeader());

            _nativeFoldersList = new CommonReorderableListView<FolderSpec>(
                AI.Config.folders,
                CreateNativeFolderRow,
                BindNativeFolderRow,
                SettingsFolderRowHeight,
                "ai-reorderable-list",
                SettingsFoldersListClass);
            _nativeFoldersList.SetAddHandler((_, __) => AddNativeCustomFolder());
            _nativeFoldersList.SetRemoveHandler(
                list => RemoveNativeCustomFolder(list.SelectedIndex),
                list => list.SelectedIndex >= 0 && list.SelectedIndex < AI.Config.folders.Count);
            _nativeFoldersList.ItemIndexChanged += (_, __) => SaveNativeFoldersConfiguration(true);
            ApplyNativeFoldersListHeight();
            foldout.Add(_nativeFoldersList);
            RestoreNativeFoldersScrollState();

            if (HasLegacyCacheLocationFolder())
            {
                foldout.Add(AssetInventoryUITK.CreateHelpBox("This list contains an Asset Store cache. Configure that path under Indexing instead, then remove the duplicate folder here.", MessageType.Warning));
            }

            VisualElement relativeMappings = BuildNativeRelativeLocationMappings();
            if (relativeMappings != null) foldout.Add(relativeMappings);
        }

        private int GetNativeFoldersSettingsHash()
        {
            unchecked
            {
                int hash = 17;
                hash = AddHash(hash, AI.Config.showFolderSettings);
                hash = AddHash(hash, ShowAdvanced());
                hash = AddHash(hash, AssetInventoryUITK.GetAdvancedVisibilityStateHash());
                hash = AddHash(hash, AI.Config.folders?.Count ?? 0);
                if (AI.Config.folders != null)
                {
                    foreach (FolderSpec folder in AI.Config.folders)
                    {
                        hash = AddHash(hash, folder?.enabled ?? false);
                        hash = AddHash(hash, folder?.location);
                        hash = AddHash(hash, folder?.folderType ?? 0);
                        hash = AddHash(hash, folder?.scanFor ?? 0);
                    }
                }

                hash = AddHash(hash, AI.UserRelativeLocations?.Count ?? 0);
                if (AI.UserRelativeLocations != null)
                {
                    foreach (RelativeLocation location in AI.UserRelativeLocations)
                    {
                        hash = AddHash(hash, location?.Id ?? 0);
                        hash = AddHash(hash, location?.Key);
                        hash = AddHash(hash, location?.Location);
                        hash = AddHash(hash, location?.otherLocations?.Count ?? 0);
                    }
                }

                return hash;
            }
        }

        private VisualElement BuildNativeFoldersHeader()
        {
            VisualElement header = new VisualElement();
            header.AddToClassList(SettingsFoldersHeaderClass);

            Label title = new Label("Folders to Index");
            title.AddToClassList("ai-section-title");
            header.Add(title);

            VisualElement buttons = new VisualElement();
            buttons.AddToClassList(SettingsFoldersHeaderButtonsClass);
            buttons.Add(CreateNativeActionBatchButton("All", () => SetAllNativeFoldersActive(true)));
            buttons.Add(CreateNativeActionBatchButton("Invert", InvertNativeFoldersActive));
            buttons.Add(CreateNativeActionBatchButton("None", () => SetAllNativeFoldersActive(false)));
            header.Add(buttons);

            return header;
        }

        private void ApplyNativeFoldersListHeight()
        {
            if (_nativeFoldersList == null) return;

            int count = AI.Config?.folders?.Count ?? 0;
            int maxVisibleRows = position.width < 900f ? 4 : 5;
            int minVisibleRows = count == 0 ? 1 : Math.Min(count, position.width < 900f ? 2 : 3);
            int visibleRows = Mathf.Clamp(count, minVisibleRows, maxVisibleRows);
            _nativeFoldersList.style.height = 38f + visibleRows * SettingsFolderRowHeight;
        }

        private void RefreshNativeFolderRows()
        {
            _nativeFoldersSettingsHash = GetNativeFoldersSettingsHash();
            if (_nativeFoldersList == null) return;

            CaptureNativeFoldersScrollState();
            ApplyNativeFoldersListHeight();
            _nativeFoldersList.Refresh();
            RestoreNativeFoldersScrollState();
        }

        private void CaptureNativeFoldersScrollState()
        {
            ScrollView scroll = _nativeFoldersList?.ListView?.Q<ScrollView>();
            _nativeScrollViewState.Capture(SettingsFoldersScrollStateKey, scroll);
        }

        private void RestoreNativeFoldersScrollState()
        {
            ScrollView scroll = _nativeFoldersList?.ListView?.Q<ScrollView>();
            _nativeScrollViewState.Restore(SettingsFoldersScrollStateKey, scroll);
        }

        private VisualElement CreateNativeFolderRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(SettingsFolderRowClass);

            Toggle toggle = new Toggle();
            toggle.AddToClassList(SettingsFolderToggleClass);
            row.Add(toggle);

            VisualElement body = new VisualElement();
            body.AddToClassList("ai-list-row-body");

            Label title = new Label();
            title.AddToClassList("ai-list-row-title");
            title.AddToClassList(SettingsFolderTitleClass);
            body.Add(title);

            Label subtitle = new Label();
            subtitle.AddToClassList("ai-list-row-subtitle");
            subtitle.AddToClassList(SettingsFolderSubtitleClass);
            body.Add(subtitle);
            row.Add(body);

            VisualElement actions = new VisualElement();
            actions.AddToClassList("ai-list-actions");

            Button fineTune = null;
            fineTune = AssetInventoryUITK.CreateIconButton("Fine-tune indexed files and package creation", "CustomTool", () => OnNativeFolderFineTuneClicked(fineTune));
            fineTune.name = SettingsFolderFineTuneButtonName;
            actions.Add(fineTune);

            Button settings = null;
            settings = AssetInventoryUITK.CreateIconButton("Folder Settings", "Settings", () => OnNativeFolderSettingsClicked(settings));
            settings.name = SettingsFolderSettingsButtonName;
            actions.Add(settings);

            row.Add(actions);
            return row;
        }

        private void BindNativeFolderRow(VisualElement element, FolderSpec spec, int index)
        {
            if (element == null || spec == null) return;

            element.userData = spec;
            element.tooltip = spec.location ?? string.Empty;

            Toggle toggle = element.Q<Toggle>(className: SettingsFolderToggleClass);
            if (toggle != null)
            {
                toggle.userData = spec;
                toggle.SetValueWithoutNotify(spec.enabled);
                toggle.tooltip = "Rescan and update folder when running the action.";
                toggle.UnregisterValueChangedCallback(OnNativeFolderToggleChanged);
                toggle.RegisterValueChangedCallback(OnNativeFolderToggleChanged);
            }

            Label title = element.Q<Label>(className: SettingsFolderTitleClass);
            if (title != null)
            {
                title.text = string.IsNullOrWhiteSpace(spec.location) ? "-No folder selected-" : spec.location;
                title.tooltip = spec.location ?? string.Empty;
            }

            Label subtitle = element.Q<Label>(className: SettingsFolderSubtitleClass);
            if (subtitle != null)
            {
                subtitle.text = GetNativeFolderSubtitle(spec);
                subtitle.tooltip = subtitle.text;
            }

            BindNativeFolderButton(element.Q<Button>(SettingsFolderFineTuneButtonName), spec, spec.folderType == 1);
            BindNativeFolderButton(element.Q<Button>(SettingsFolderSettingsButtonName), spec, true);
        }

        private static void BindNativeFolderButton(Button button, FolderSpec spec, bool visible)
        {
            if (button == null) return;

            button.userData = spec;
            button.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnNativeFolderToggleChanged(ChangeEvent<bool> evt)
        {
            if (!(evt.target is Toggle toggle) || !(toggle.userData is FolderSpec spec)) return;

            spec.enabled = evt.newValue;
            SaveNativeFoldersConfiguration(false);
        }

        private void OnNativeFolderFineTuneClicked(Button button)
        {
            if (!(button?.userData is FolderSpec spec)) return;

            FolderFineTuneUI.ShowWindow(spec);
        }

        private void OnNativeFolderSettingsClicked(Button button)
        {
            if (!(button?.userData is FolderSpec spec)) return;

            FolderSettingsUI.ShowDropdown(this, button, spec);
        }

        private void SetAllNativeFoldersActive(bool active)
        {
            SetAllFoldersActive(active);
            RefreshNativeFolderRows();
        }

        private void InvertNativeFoldersActive()
        {
            InvertFoldersActive();
            RefreshNativeFolderRows();
        }

        private void AddNativeCustomFolder()
        {
            AddCustomFolderFromDialog();
            RefreshNativeFoldersSettingsSection(true);
        }

        private void RemoveNativeCustomFolder(int index)
        {
            if (RemoveCustomFolderAtIndex(index))
            {
                RefreshNativeFoldersSettingsSection(true);
            }
        }

        private void SaveNativeFoldersConfiguration(bool reloadRelativeLocations)
        {
            AI.SaveConfig();
            if (reloadRelativeLocations) Paths.LoadRelativeLocations();
            RefreshNativeFolderRows();
        }

        private string GetNativeFolderTypeLabel(FolderSpec spec)
        {
            int index = Mathf.Clamp(spec.folderType, 0, FolderTypes.Length - 1);
            return FolderTypes[index];
        }

        private string GetNativeFolderSubtitle(FolderSpec spec)
        {
            string folderType = GetNativeFolderTypeLabel(spec);
            if (spec.folderType == 1)
            {
                int mediaIndex = Mathf.Clamp(spec.scanFor, 0, MediaTypes.Length - 1);
                string mediaType = MediaTypes[mediaIndex].Trim(' ', '-');
                if (!string.IsNullOrWhiteSpace(mediaType)) folderType += $" | {mediaType}";
            }

            if (spec.storeRelative && !string.IsNullOrWhiteSpace(spec.relativeKey))
            {
                folderType += $" | Relative: {spec.relativeKey}";
            }

            return folderType;
        }

        private bool HasLegacyCacheLocationFolder()
        {
            return AI.Config.folders != null && AI.Config.folders.Any(spec => spec?.location != null && spec.location.Contains(AI.ASSET_STORE_FOLDER_NAME));
        }

        private VisualElement BuildNativeRelativeLocationMappings()
        {
            if (AI.UserRelativeLocations == null || AI.UserRelativeLocations.Count == 0) return null;

            VisualElement section = new VisualElement();
            section.AddToClassList(SettingsRelativeLocationsClass);

            VisualElement title = CreateNativeSettingsSubsectionTitle("Relative Location Mappings");
            section.Add(title);

            foreach (RelativeLocation location in AI.UserRelativeLocations)
            {
                section.Add(CreateNativeRelativeLocationRow(location));
            }

            return section;
        }

        private VisualElement CreateNativeRelativeLocationRow(RelativeLocation location)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(SettingsRelativeLocationRowClass);

            VisualElement body = new VisualElement();
            body.AddToClassList("ai-list-row-body");

            Label title = new Label(location.Key ?? string.Empty);
            title.AddToClassList("ai-list-row-title");
            body.Add(title);

            string otherSystems = GetRelativeLocationOtherSystemsTooltip(location);
            Label subtitle = new Label(string.IsNullOrWhiteSpace(location.Location) ? "-Not yet connected-" : location.Location);
            subtitle.AddToClassList("ai-list-row-subtitle");
            subtitle.tooltip = otherSystems;
            body.Add(subtitle);
            row.Add(body);

            Button delete = AssetInventoryUITK.CreateIconButton("Delete mapping", "TreeEditor.Trash", () => DeleteNativeRelativeLocation(location));
            bool canDelete = CanDeleteNativeRelativeLocation(location);
            delete.SetEnabled(canDelete);
            if (!canDelete) delete.tooltip = "Cannot delete only remaining mapping";
            row.Add(delete);

            Button select = AssetInventoryUITK.CreateSecondaryButton("...", () =>
            {
                SelectRelativeFolderMapping(location);
               RefreshNativeFoldersSettingsSection(true);
           });
            select.tooltip = "Select folder";
            row.Add(select);

            return row;
        }

        private static string GetRelativeLocationOtherSystemsTooltip(RelativeLocation location)
        {
            string otherSystems = "Mappings on other systems:\n\n";
            string otherLocs = location?.otherLocations == null ? string.Empty : string.Join("\n", location.otherLocations);
            return otherSystems + (string.IsNullOrWhiteSpace(otherLocs) ? "-None-" : otherLocs);
        }

        private bool CanDeleteNativeRelativeLocation(RelativeLocation location)
        {
            if (location == null || string.IsNullOrWhiteSpace(location.Location)) return false;

            bool hasOtherMappings = location.otherLocations != null && location.otherLocations.Count > 0;
            return hasOtherMappings || ShowAdvanced();
        }

        private void DeleteNativeRelativeLocation(RelativeLocation location)
        {
            if (location == null) return;
            if (!CanDeleteNativeRelativeLocation(location)) return;

            if (!EditorUtility.DisplayDialog("Confirmation", "Are you sure you want to delete this mapping? This will remove it from the database and the tool will no longer be able to access the folder.", "Yes", "Cancel")) return;

            DBAdapter.DB.Delete(location);
            Paths.LoadRelativeLocations();
           RefreshNativeFoldersSettingsSection(true);
       }

        private VisualElement BuildNativeImportSettingsSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection();
            section.AddToClassList(SettingsImportSectionClass);
            _nativeImportSettingsSection = section;
            _nativeImportSettingsHash = int.MinValue;
            RefreshNativeImportSettingsSection(true);
            return section;
        }

        private void RefreshNativeImportSettingsSection(bool force = false)
        {
            if (_nativeImportSettingsSection == null || AI.Config == null) return;

            int hash = GetNativeImportSettingsHash();
            if (!force && _nativeImportSettingsHash == hash) return;

            _nativeImportSettingsHash = hash;
            _nativeImportSettingsSection.Clear();

            Foldout foldout = CreateNativeSettingsFoldout("Import", AI.Config.showImportSettings, value =>
            {
                AI.Config.showImportSettings = value;
                AI.SaveConfig();
                RefreshNativeImportSettingsSection(true);
            });
            _nativeImportSettingsSection.Add(foldout);

            if (!AI.Config.showImportSettings) return;

            VisualElement renderPipeline = AddNativeSettingsGroup(
                foldout,
                "Render Pipeline",
                "Prefer native package variants and convert materials only when a matching variant is unavailable.");
            renderPipeline.Add(CreateNativeSettingsVisibilityBlock("settings.srpintro", () =>
                AssetInventoryUITK.CreateHelpBox("Asset Inventory uses a package's matching render-pipeline dependencies when available. Otherwise it can convert imported materials for the current project.")));

#if USE_URP
            renderPipeline.Add(CreateNativeSettingsToggleRow("Unity Converter", "Use Unity's built-in Render Pipeline Converter to persistently convert materialized assets. Only supports BIRP to URP.", AI.Config.useUnityPipelineConverter, value => AI.Config.useUnityPipelineConverter = value));
            renderPipeline.Add(CreateNativeSettingsNote("Acts project-wide. When active it takes precedence and the custom converter will be the fallback."));
#endif
            renderPipeline.Add(CreateNativeSettingsToggleRow("Custom Converter", "Converts only the actual imported assets. Supports BIRP to URP and BIRP to HDRP.", AI.Config.useCustomPipelineConverter, value => AI.Config.useCustomPipelineConverter = value));
            renderPipeline.Add(CreateNativeSettingsNote("Acts only on the actual imported assets and is much faster than the Unity converter."));

            VisualElement destination = AddNativeSettingsGroup(
                foldout,
                "Destination and Structure",
                "Define where imported files are materialized and how their original folder structure is preserved.");
            destination.Add(CreateNativeSettingsVisibilityBlock("settings.importstructureintro", () =>
                AssetInventoryUITK.CreateHelpBox("These settings control Import and double-click actions. Dragging a result into the Project window always uses the folder where it is dropped.")));

            destination.Add(CreateNativeSettingsPopupRow("Structure", "Structure to materialize the imported files in.", _importStructureOptions, AI.Config.importStructure, value => AI.Config.importStructure = value));
            destination.Add(CreateNativeSettingsPopupRow(
                "Filename Conflicts",
                "Choose how imports behave when the target already contains a different asset with the same filename.",
                _importCollisionOptions,
                (int)AI.Config.importCollisionMode,
                value => AI.Config.importCollisionMode = (AssetImportCollisionMode)value));
            destination.Add(CreateNativeSettingsPopupRow(
                "Destination",
                "Target folder for imported files.",
                _importDestinationOptions,
                AI.Config.importDestination,
                value =>
                {
                    AI.Config.importDestination = value;
                    RefreshNativeImportSettingsSection(true);
                }));

            if (AI.Config.importDestination == 2)
            {
                destination.Add(CreateNativeSettingsImportTargetFolderRow());
            }

            if (ShowAdvanced())
            {
                VisualElement reimport = AddNativeSettingsGroup(
                    foldout,
                    "Reimport and Cleanup",
                    "Control how existing project files are reorganized and simplified when they are imported again.");
                reimport.Add(CreateNativeSettingsToggleRow("Reorganize on Reimport", "When reimporting files that already exist in the project, move them to the target import structure first instead of overwriting at their current location.", AI.Config.reorganizeOnReimport, value => { AI.Config.reorganizeOnReimport = value; RefreshNativeImportSettingsSection(true); }));

                if (AI.Config.reorganizeOnReimport)
                {
                    reimport.Add(CreateNativeSettingsToggleRow("Delete Empty Folders", "Delete folders that become empty after files are moved during reorganization.", AI.Config.deleteEmptyFoldersOnReorganize, value => AI.Config.deleteEmptyFoldersOnReorganize = value));
                }
                reimport.Add(CreateNativeSettingsToggleRow("Remove LODs", "Remove LOD groups from imported prefabs and only keep the first one.", AI.Config.removeLODs, value => AI.Config.removeLODs = value));

                VisualElement dependencies = AddNativeSettingsGroup(
                    foldout,
                    "Dependencies and Project Safety",
                    "Resolve external references while protecting project-level settings from automatic package imports.");
                dependencies.Add(CreateNativeSettingsToggleRow("Calculate FBX Dependencies", "Scan FBX files for embedded texture references.", AI.Config.scanFBXDependencies, value => AI.Config.scanFBXDependencies = value));
                dependencies.Add(CreateNativeSettingsToggleRow("Cross-Package Dependencies", "If referenced GUIDs cannot be found in the current package, scan the whole database for a match.", AI.Config.allowCrossPackageDependencies, value => AI.Config.allowCrossPackageDependencies = value));
                dependencies.Add(CreateNativeSettingsToggleRow("Skip ProjectSettings", "When importing packages in automatic mode, skip files in the ProjectSettings folder to avoid overwriting project settings.", AI.Config.skipProjectSettings, value => AI.Config.skipProjectSettings = value));
            }
        }

        private int GetNativeImportSettingsHash()
        {
            unchecked
            {
                int hash = 17;
                hash = AddHash(hash, AI.Config.showImportSettings);
                hash = AddHash(hash, ShowAdvanced());
                hash = AddHash(hash, AssetInventoryUITK.GetAdvancedVisibilityStateHash());
#if USE_URP
                hash = AddHash(hash, AI.Config.useUnityPipelineConverter);
#endif
                hash = AddHash(hash, AI.Config.useCustomPipelineConverter);
                hash = AddHash(hash, AI.Config.importStructure);
                hash = AddHash(hash, AI.Config.importCollisionMode);
                hash = AddHash(hash, AI.Config.importDestination);
                hash = AddHash(hash, AI.Config.importFolder);
                hash = AddHash(hash, AI.Config.reorganizeOnReimport);
                hash = AddHash(hash, AI.Config.deleteEmptyFoldersOnReorganize);
                hash = AddHash(hash, AI.Config.scanFBXDependencies);
                hash = AddHash(hash, AI.Config.allowCrossPackageDependencies);
                hash = AddHash(hash, AI.Config.removeLODs);
                hash = AddHash(hash, AI.Config.skipProjectSettings);
                return hash;
            }
        }

        private VisualElement BuildNativePreviewsSettingsSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection();
            section.AddToClassList(SettingsPreviewsSectionClass);
            _nativePreviewsSettingsSection = section;
            _nativePreviewsSettingsHash = int.MinValue;
            RefreshNativePreviewsSettingsSection(true);
            return section;
        }

        private void RefreshNativePreviewsSettingsSection(bool force = false)
        {
            if (_nativePreviewsSettingsSection == null || AI.Config == null) return;

            int hash = GetNativePreviewsSettingsHash();
            if (!force && _nativePreviewsSettingsHash == hash) return;

            _nativePreviewsSettingsHash = hash;
            _nativePreviewsSettingsSection.Clear();

            Foldout foldout = CreateNativeSettingsFoldout("Previews", AI.Config.showPreviewSettings, value =>
            {
                AI.Config.showPreviewSettings = value;
                AI.SaveConfig();
                RefreshNativePreviewsSettingsSection(true);
            });
            _nativePreviewsSettingsSection.Add(foldout);

            if (!AI.Config.showPreviewSettings) return;

            VisualElement sources = AddNativeSettingsGroup(
                foldout,
                "Preview Sources",
                "Choose which existing images or fallback visuals may be used before Asset Inventory recreates a preview.");
            if (ShowAdvanced())
            {
                sources.Add(CreateNativeSettingsToggleRow("Use Provided Preview Images", "Extract preview images from the package if it provides previews already. This can require a moderate amount of space.", AI.Config.extractPreviews, value => { AI.Config.extractPreviews = value; MarkNativePreviewSettingsChanged(); }));
                sources.Add(CreateNativeSettingsToggleRow("Use Small Image Files Directly", "Do not create a separate preview file if an image file in an additional folder already fits the preview size.", AI.Config.directMediaPreviews, value => { AI.Config.directMediaPreviews = value; MarkNativePreviewSettingsChanged(); }));
                sources.Add(CreateNativeSettingsToggleRow("Use Fallback-Icons as Previews", "Show generic icons when a file preview is missing instead of an empty tile.", AI.Config.showIconsForMissingPreviews, value => { AI.Config.showIconsForMissingPreviews = value; MarkNativePreviewSettingsChanged(); }));
            }

            VisualElement recreation = AddNativeSettingsGroup(
                foldout,
                "Validation and Recreation",
                "Detect unusable previews and decide when missing output should be recreated automatically.");
            recreation.Add(CreateNativeSettingsToggleRow("Verify Previews", "Check preview images for empty/default icon output. Recommended, but it slows indexing and preview recreation.", AI.Config.verifyPreviews, value => { AI.Config.verifyPreviews = value; MarkNativePreviewSettingsChanged(); }));
            recreation.Add(CreateNativeSettingsToggleRow("Recreate Previews After Indexing", "Run preview recreation automatically once a package is indexed when previews are missing or erroneous.", AI.Config.recreatePreviewsAfterIndexing, value => { AI.Config.recreatePreviewsAfterIndexing = value; MarkNativePreviewSettingsChanged(); }));
            recreation.Add(CreateNativeSettingsToggleRow("Download Missing Packages", "Temporarily download packages for which previews are missing.", AI.Config.downloadPackagesForPreviews, value => { AI.Config.downloadPackagesForPreviews = value; MarkNativePreviewSettingsChanged(); }));
            if (ShowAdvanced())
            {
                recreation.Add(CreateNativeSettingsToggleRow("Confirm Preview Rescheduling", "Show a confirmation dialog before scheduling preview recreation in the Preview Wizard.", AI.Config.confirmPreviewRescheduling, value => { AI.Config.confirmPreviewRescheduling = value; MarkNativePreviewSettingsChanged(); }));
            }

            VisualElement projectWindow = AddNativeSettingsGroup(
                foldout,
                "Project Window",
                "Optionally replace Unity's Project-window icons with generated previews and animation playback.");
            projectWindow.Add(CreateNativeSettingsToggleRow(
                "Override Icons in Project Window",
                "Display custom preview icons for prefabs directly in Unity's Project window.",
                AI.Config.overrideProjectPreviews,
                value =>
                {
                    AI.Config.overrideProjectPreviews = value;
                    MarkNativePreviewSettingsChanged();
                    RefreshNativePreviewsSettingsSection(true);
                }));

            if (AI.Config.overrideProjectPreviews)
            {
                projectWindow.Add(CreateNativeSettingsToggleRow("Play Animations", "Play animated previews when available instead of showing static previews.", AI.Config.playProjectWindowAnimations, value => { AI.Config.playProjectWindowAnimations = value; MarkNativePreviewSettingsChanged(); }));
            }

            if (ShowAdvanced())
            {
                VisualElement processing = AddNativeSettingsGroup(
                    foldout,
                    "Processing and Cache",
                    "Balance preview throughput, fallback timing, materialization behavior, and retained cache size.");
                processing.Add(CreateNativeSettingsIntegerRow("Parallel Processing", "Number of previews to process simultaneously. Higher values can speed up preview generation but may use more memory and CPU.", AI.Config.parallelPreviewBatchSize, value => { AI.Config.parallelPreviewBatchSize = value; MarkNativePreviewSettingsChanged(); }));
                processing.Add(CreateNativeSettingsIntegerRow("Bulk Preview Threshold", "When more than this number of files from one page need previews, materialize the whole package instead of each file one by one.", AI.Config.bulkPreviewThreshold, value => { AI.Config.bulkPreviewThreshold = value; MarkNativePreviewSettingsChanged(); }));
                processing.Add(CreateNativeSettingsFloatRow("Wait Time", "Minimum time in seconds to wait for Unity's preview generation before giving up.", AI.Config.minPreviewWait, value => { AI.Config.minPreviewWait = value; MarkNativePreviewSettingsChanged(); }, CreateNativeSettingsInlineText("seconds")));
                processing.Add(CreateNativeSettingsPreviewExcludeExtensionsRow());
                processing.Add(CreateNativeSettingsToggleRow("Keep Cached on Audio Playback", "Set 'Keep Cached' on a package when previewing an audio clip from it so future playback starts quickly.", AI.Config.keepExtractedOnAudio, value => { AI.Config.keepExtractedOnAudio = value; MarkNativePreviewSettingsChanged(); }));
            }

            VisualElement customPipeline = AddNativeSettingsGroup(
                foldout,
                "Custom Preview Pipeline",
                "Configure preview scenes for assets that Unity's built-in thumbnail renderer cannot represent well.");
            customPipeline.Add(CreateNativeSettingsVisibilityBlock("settings.customprevintro", () =>
                AssetInventoryUITK.CreateHelpBox("Unity provides small previews for many 3D prefabs. The custom pipeline also supports UI, particles, and visual effects, with configurable lighting and camera settings.")));
            customPipeline.Add(CreateNativeSettingsValueRow("Custom Previews", null, AssetInventoryUITK.CreateSecondaryButton("Configure Custom Previews...", CustomPreviewSettingsUI.ShowWindow)));

#if UNITY_EDITOR_WIN && !NET_4_6
            customPipeline.Add(AssetInventoryUITK.CreateHelpBox("Editor Assembly Compatibility is set to .NET Standard, so preview processing is slower on Windows. Switch to .NET Framework unless the project specifically requires .NET Standard.", MessageType.Warning));
#endif
#if !USE_VECTOR_GRAPHICS
            customPipeline.Add(AssetInventoryUITK.CreateHelpBox("In order to see previews for SVG graphics, the 'com.unity.vectorgraphics' needs to be installed.", MessageType.Warning));
            customPipeline.Add(CreateNativeSettingsValueRow("Vector Graphics", null, AssetInventoryUITK.CreateSecondaryButton("Install Vector Graphics Package", () => Client.Add("com.unity.vectorgraphics"))));
#endif
#if !USE_PSD_IMPORTER
            customPipeline.Add(AssetInventoryUITK.CreateHelpBox("In order to import PSB files and recreate their previews, the 'com.unity.2d.psdimporter' package needs to be installed.", MessageType.Warning));
            customPipeline.Add(CreateNativeSettingsValueRow("2D PSD Importer", null, AssetInventoryUITK.CreateSecondaryButton("Install 2D PSD Importer Package", () => Client.Add("com.unity.2d.psdimporter"))));
#endif
#if !USE_SHADER_GRAPH
            customPipeline.Add(AssetInventoryUITK.CreateHelpBox("In order to see previews for Shader Graph assets, the 'com.unity.shadergraph' package needs to be installed.", MessageType.Warning));
            customPipeline.Add(CreateNativeSettingsValueRow("Shader Graph", null, AssetInventoryUITK.CreateSecondaryButton("Install Shader Graph Package", () => Client.Add("com.unity.shadergraph"))));
#endif
#if !USE_VFX && UNITY_6000_0_OR_NEWER
            customPipeline.Add(AssetInventoryUITK.CreateHelpBox("In order to generate previews for Visual Effect Graph assets, the 'com.unity.visualeffectgraph' package needs to be installed.", MessageType.Warning));
            customPipeline.Add(CreateNativeSettingsValueRow("Visual Effect Graph", null, AssetInventoryUITK.CreateSecondaryButton("Install Visual Effects Graph Package", () => Client.Add("com.unity.visualeffectgraph"))));
#endif
#if !USE_GLTF_IMPORTER && !USE_KHRONOS_UNITY_GLTF
            customPipeline.Add(AssetInventoryUITK.CreateHelpBox(GltfSupport.MissingImporterMessage, MessageType.Warning));
            customPipeline.Add(CreateNativeSettingsValueRow("glTF Importer", null, AssetInventoryUITK.CreateSecondaryButton("Install Unity glTFast Package", () => Client.Add(GltfSupport.PackageName))));
#endif
#if !USE_TEXTMESHPRO && !UNITY_2023_2_OR_NEWER
            customPipeline.Add(AssetInventoryUITK.CreateHelpBox("In order to see previews for TextMeshPro assets, the 'com.unity.textmeshpro' package needs to be installed.", MessageType.Warning));
            customPipeline.Add(CreateNativeSettingsValueRow("TextMeshPro", null, AssetInventoryUITK.CreateSecondaryButton("Install TextMeshPro Package", () => Client.Add("com.unity.textmeshpro@3.0.9"))));
#endif
#if USE_TEXTMESHPRO || UNITY_2023_2_OR_NEWER
            if (!TMPStep.AreTMPEssentialsImported())
            {
                customPipeline.Add(AssetInventoryUITK.CreateHelpBox("Import TextMeshPro Essentials to enable full text preview rendering.", MessageType.Warning));
                customPipeline.Add(CreateNativeSettingsValueRow("TMP Essentials", null, AssetInventoryUITK.CreateSecondaryButton("Import TMP Essentials", TMPStep.ImportEssentials)));
            }
#endif
        }

        private int GetNativePreviewsSettingsHash()
        {
            unchecked
            {
                int hash = 17;
                hash = AddHash(hash, AI.Config.showPreviewSettings);
                hash = AddHash(hash, ShowAdvanced());
                hash = AddHash(hash, AssetInventoryUITK.GetAdvancedVisibilityStateHash());
                hash = AddHash(hash, AI.Config.extractPreviews);
                hash = AddHash(hash, AI.Config.directMediaPreviews);
                hash = AddHash(hash, AI.Config.showIconsForMissingPreviews);
                hash = AddHash(hash, AI.Config.verifyPreviews);
                hash = AddHash(hash, AI.Config.recreatePreviewsAfterIndexing);
                hash = AddHash(hash, AI.Config.downloadPackagesForPreviews);
                hash = AddHash(hash, AI.Config.overrideProjectPreviews);
                hash = AddHash(hash, AI.Config.playProjectWindowAnimations);
                hash = AddHash(hash, AI.Config.confirmPreviewRescheduling);
                hash = AddHash(hash, AI.Config.parallelPreviewBatchSize);
                hash = AddHash(hash, AI.Config.bulkPreviewThreshold);
                hash = AddHash(hash, AI.Config.minPreviewWait);
                hash = AddHash(hash, AI.Config.excludePreviewExtensions);
                hash = AddHash(hash, AI.Config.excludedPreviewExtensions);
                hash = AddHash(hash, AI.Config.keepExtractedOnAudio);
#if USE_TEXTMESHPRO || UNITY_2023_2_OR_NEWER
                hash = AddHash(hash, TMPStep.AreTMPEssentialsImported());
#endif
                return hash;
            }
        }

        private VisualElement BuildNativeBackupSettingsSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection();
            section.AddToClassList(SettingsBackupSectionClass);
            _nativeBackupSettingsSection = section;
            _nativeBackupSettingsHash = int.MinValue;
            RefreshNativeBackupSettingsSection(true);
            return section;
        }

        private void RefreshNativeBackupSettingsSection(bool force = false)
        {
            if (_nativeBackupSettingsSection == null || AI.Config == null) return;

            int hash = GetNativeBackupSettingsHash();
            if (!force && _nativeBackupSettingsHash == hash) return;

            _nativeBackupSettingsHash = hash;
            _nativeBackupSettingsSection.Clear();

            Foldout foldout = CreateNativeSettingsFoldout("Backup", AI.Config.showBackupSettings, value =>
            {
                AI.Config.showBackupSettings = value;
                AI.SaveConfig();
                RefreshNativeBackupSettingsSection(true);
            });
            _nativeBackupSettingsSection.Add(foldout);

            if (!AI.Config.showBackupSettings) return;

            VisualElement packageBackups = AddNativeSettingsGroup(
                foldout,
                "Package Backups",
                "Keep recoverable copies of selected package versions independently from the Asset Store cache.");
            packageBackups.Add(CreateNativeSettingsToggleRow(
                "Enable Package Backups",
                "Show package backup controls and make the package backup action available. Existing backups and package selections are preserved when disabled.",
                AI.Config.packageBackupFeatureEnabled,
                value =>
                {
                    AI.Config.packageBackupFeatureEnabled = value;
                    OnNativeOptionalFeatureChanged(
                        () => RefreshNativeBackupSettingsSection(true));
                }));

            if (AI.Config.packageBackupFeatureEnabled)
            {
                if (!AI.Actions.CreateBackups)
                {
                    packageBackups.Add(CreateNativeSettingsNote("Not included in regular Run Actions updates."));
                }

                List<VisualElement> packageControls = new List<VisualElement> {CreateNativeSettingsValueText($"{_stats?.BackupPackages ?? 0:N0} (set per package in Packages view)")};
                if (ShowAdvanced())
                {
                    packageControls.Add(AssetInventoryUITK.CreateSecondaryButton("Show", () => ShowPackageMaintenance(PackageSearch.MaintenanceOption.MarkedForBackup)));
                }
                packageBackups.Add(CreateNativeSettingsValueRow("Activated Packages", null, packageControls.ToArray()));

                packageBackups.Add(CreateNativeSettingsToggleRow(
                    "Override Patch Versions",
                    "Remove all but the latest patch version of an asset inside the same minor version.",
                    AI.Config.onlyLatestPatchVersion,
                    value => AI.Config.onlyLatestPatchVersion = value));

                packageBackups.Add(CreateNativeSettingsIntegerRow(
                    "Backups per Asset",
                    "Number of versions to keep per asset.",
                    AI.Config.backupsPerAsset,
                    value => AI.Config.backupsPerAsset = Mathf.Max(1, value)));

            }

            VisualElement dataBackups = AddNativeSettingsGroup(
                foldout,
                "Database & Configuration Backups",
                "Protect Asset Inventory's catalog and configuration independently from package backup creation.");
            dataBackups.Add(CreateNativeSettingsToggleRow(
                "Enable Database & Config Backups",
                "Automatically create backups of the database and current configuration file at the configured interval.",
                AI.Config.enableDatabaseBackup,
                value =>
                {
                    AI.Config.enableDatabaseBackup = value;
                    RefreshNativeBackupSettingsSection(true);
                }));

            if (AI.Config.enableDatabaseBackup)
            {
                dataBackups.Add(CreateNativeSettingsIntegerRow(
                    "Backup Interval (days)",
                    "Number of days between automatic database backups.",
                    AI.Config.databaseBackupInterval,
                    value => AI.Config.databaseBackupInterval = Mathf.Max(1, value)));

                Button backupNow = AssetInventoryUITK.CreateSecondaryButton(DBAdapter.IsBackingUp ? "Backing Up..." : "Backup Now", () =>
                {
                    _ = DBAdapter.BackupDatabaseAsync(skipIntervalCheck: true);
                    RefreshNativeBackupSettingsSection(true);
                });
                backupNow.SetEnabled(!DBAdapter.IsBackingUp);

                dataBackups.Add(CreateNativeSettingsIntegerRow(
                    "Number of Backups to Keep",
                    "Maximum number of database backups to retain. Older backups will be automatically deleted.",
                    AI.Config.databaseBackupsToKeep,
                    value => AI.Config.databaseBackupsToKeep = Mathf.Max(1, value),
                    backupNow));
            }

            VisualElement storage = AddNativeSettingsGroup(
                foldout,
                "Backup Storage",
                "Package, database, and configuration backups share this location.");
            storage.Add(CreateNativeSettingsFolderRow(
                "Storage Folder",
                Paths.GetBackupFolder(false),
                AI.Config.backupFolder,
                value => AI.Config.backupFolder = value,
                "Select backup storage folder"));
        }

        private int GetNativeBackupSettingsHash()
        {
            unchecked
            {
                int hash = 17;
                hash = AddHash(hash, AI.Config.showBackupSettings);
                hash = AddHash(hash, ShowAdvanced());
                hash = AddHash(hash, _stats?.BackupPackages ?? 0);
                hash = AddHash(hash, AI.Config.packageBackupFeatureEnabled);
                hash = AddHash(hash, AI.Actions?.CreateBackups ?? false);
                hash = AddHash(hash, AI.Config.onlyLatestPatchVersion);
                hash = AddHash(hash, AI.Config.backupsPerAsset);
                hash = AddHash(hash, AI.Config.backupFolder);
                hash = AddHash(hash, AI.Config.enableDatabaseBackup);
                hash = AddHash(hash, AI.Config.databaseBackupInterval);
                hash = AddHash(hash, AI.Config.databaseBackupsToKeep);
                hash = AddHash(hash, DBAdapter.IsBackingUp);
                return hash;
            }
        }

        private VisualElement BuildNativeAISettingsSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection();
            section.AddToClassList(SettingsAISectionClass);
            _nativeAISettingsSection = section;
            _nativeAISettingsHash = int.MinValue;
            RefreshNativeAISettingsSection(true);
            return section;
        }

        private void RefreshNativeAISettingsSection(bool force = false)
        {
            if (_nativeAISettingsSection == null || AI.Config == null) return;

            int hash = GetNativeAISettingsHash();
            if (!force && _nativeAISettingsHash == hash) return;

            _nativeAISettingsHash = hash;
            _nativeAISettingsSection.Clear();

            Foldout foldout = CreateNativeSettingsFoldout("Artificial Intelligence", AI.Config.showAISettings, value =>
            {
                AI.Config.showAISettings = value;
                AI.SaveConfig();
                RefreshNativeAISettingsSection(true);
            });
            _nativeAISettingsSection.Add(foldout);

            if (!AI.Config.showAISettings) return;

            VisualElement features = AddNativeSettingsGroup(
                foldout,
                "Optional Features",
                "Enable only the capabilities you use. Their package controls and update actions appear automatically.");
            features.Add(CreateNativeSettingsToggleRow(
                "Enable AI Captions",
                "Show caption controls and make AI caption creation available. Existing captions and package selections are preserved when disabled.",
                AI.Config.aiCaptionsFeatureEnabled,
                value =>
                {
                    AI.Config.aiCaptionsFeatureEnabled = value;
                    OnNativeOptionalFeatureChanged(
                        () => RefreshNativeAISettingsSection(true));
                }));
            features.Add(CreateNativeSettingsToggleRow(
                "Enable Semantic Asset Search",
                "Show semantic package controls, search options, and index maintenance. Existing sidecar data and package selections are preserved when disabled.",
                AI.Config.semanticSearchFeatureEnabled,
                value =>
                {
                    AI.Config.semanticSearchFeatureEnabled = value;
                    OnNativeOptionalFeatureChanged(
                        () => RefreshNativeAISettingsSection(true));
                }));
            features.Add(CreateNativeSettingsToggleRow(
                "Enable Code Search",
                "Show Code Search, package controls, and index maintenance. Existing sidecar data and package selections are preserved when disabled.",
                AI.Config.codeSearchFeatureEnabled,
                value =>
                {
                    AI.Config.codeSearchFeatureEnabled = value;
                    OnNativeOptionalFeatureChanged(
                        () => RefreshNativeAISettingsSection(true),
                        true);
                }));

            if (AI.Config.aiCaptionsFeatureEnabled)
            {
                VisualElement captions = AddNativeSettingsGroup(
                    foldout,
                    "Captions",
                    "Choose which indexed assets receive generated descriptions and how caption requests are paced.");
                AddNativeAICaptionSettings(captions);
            }

            if (AI.Config.aiCaptionsFeatureEnabled || AI.Config.semanticSearchFeatureEnabled)
            {
                VisualElement backend = AddNativeSettingsGroup(
                    foldout,
                    "Local AI Engine",
                    "Select and configure the local model service used for captions and semantic embeddings.");
                AddNativeAIBackendSettings(backend);
            }

            if (AI.Config.aiCaptionsFeatureEnabled)
            {
                VisualElement test = AddNativeSettingsGroup(
                    foldout,
                    "Caption Test",
                    "Run the selected caption model against a known image before processing a large package collection.");
                AddNativeAITestImageSettings(test);
            }

            if (AI.Config.semanticSearchFeatureEnabled)
            {
                VisualElement semantic = AddNativeSettingsGroup(
                    foldout,
                    "Semantic Asset Search",
                    "Create embeddings for visual and descriptive asset discovery beyond exact text matches.");
                AddNativeSemanticSearchSettings(semantic);
            }

            if (AI.Config.codeSearchFeatureEnabled)
            {
                VisualElement code = AddNativeSettingsGroup(
                    foldout,
                    "Code Search",
                    "Control which project and package source files are indexed for fast code lookup.");
                AddNativeCodeSearchSettings(code);
            }
        }

        private int GetNativeAISettingsHash()
        {
            unchecked
            {
                int hash = 17;
                hash = AddHash(hash, AI.Config.showAISettings);
                hash = AddHash(hash, ShowAdvanced());
                hash = AddHash(hash, AssetInventoryUITK.GetAdvancedVisibilityStateHash());
                hash = AddHash(hash, _stats?.AIPackages ?? 0);
                hash = AddHash(hash, _stats?.SemanticIndexPackages ?? 0);
                hash = AddHash(hash, _stats?.CodeIndexPackages ?? 0);
                hash = AddHash(hash, AI.Config.aiCaptionsFeatureEnabled);
                hash = AddHash(hash, AI.Config.semanticSearchFeatureEnabled);
                hash = AddHash(hash, AI.Config.codeSearchFeatureEnabled);
                hash = AddHash(hash, AI.Actions?.CreateAICaptions ?? false);
                hash = AddHash(hash, AI.Actions?.UpdateSemanticIndex ?? false);
                hash = AddHash(hash, AI.Actions?.UpdateCodeIndex ?? false);
                hash = AddHash(hash, AI.Config.aiCaptionExtensions);
                hash = AddHash(hash, AI.Config.logAICaptions);
                hash = AddHash(hash, AI.Config.aiMaxCaptionLength);
                hash = AddHash(hash, AI.Config.aiPause);
                hash = AddHash(hash, AI.Config.aiTimeout);
                hash = AddHash(hash, AI.Config.aiBackend);
                hash = AddHash(hash, AI.Config.blipPath);
                hash = AddHash(hash, AI.Config.blipType);
                hash = AddHash(hash, AI.Config.aiContinueOnEmpty);
                hash = AddHash(hash, AI.Config.blipUseGPU);
                hash = AddHash(hash, AI.Config.blipChunkSize);
                hash = AddHash(hash, AI.Config.ollamaServiceUrl);
                hash = AddHash(hash, AI.Config.ollamaModel);
                hash = AddHash(hash, AI.Config.ollamaParallelRequests);
                hash = AddHash(hash, AI.Config.lmStudioServiceUrl);
                hash = AddHash(hash, AI.Config.lmStudioModel);
                hash = AddHash(hash, AI.Config.lmStudioParallelRequests);
                hash = AddHash(hash, AI.Config.semanticLmStudioEmbeddingModel);
                hash = AddHash(hash, AI.Config.semanticOllamaEmbeddingModel);
                hash = AddHash(hash, AI.Config.semanticIndexExtensions);
                hash = AddHash(hash, AI.Config.semanticEmbeddingBatchSize);
                hash = AddHash(hash, AI.Config.codeIndexProjectFiles);
                hash = AddHash(hash, AI.Config.codeIndexPackageFiles);
                hash = AddHash(hash, AI.Config.codeIndexAutoUpdateProjectChanges);
                hash = AddHash(hash, AI.Config.codeIndexExtensions);
                hash = AddHash(hash, AI.Config.codeIndexMaxFileSizeKb);
                hash = AddHash(hash, AI.Config.codeIndexSemanticRerank);
                // Availability getters start network probes, so only read the backend whose controls are visible.
                if (AI.Config.showAISettings && (AI.Config.aiCaptionsFeatureEnabled || AI.Config.semanticSearchFeatureEnabled))
                {
                    if (AI.Config.aiBackend == 1)
                    {
                        hash = AddHash(hash, Intelligence.IsOllamaInstalled);
                        hash = AddHash(hash, Intelligence.LoadingModels);
                        hash = AddHash(hash, Intelligence.DownloadingModel);
                        hash = AddHash(hash, _activeOllamaDownloadModel);
                        hash = AddHash(hash, _curOllamaProgress);
                        hash = AddHash(hash, _maxOllamaProgress);
                    }
                    else if (AI.Config.aiBackend == 2)
                    {
                        hash = AddHash(hash, Intelligence.IsLMStudioInstalled);
                        hash = AddHash(hash, Intelligence.LoadingLMStudioModels);
                    }
                }
                hash = AddHash(hash, _captionTestRunning);
                hash = AddHash(hash, _captionTest);
                return hash;
            }
        }

        private void AddNativeAICaptionSettings(VisualElement foldout)
        {
            if (!AI.Actions.CreateAICaptions)
            {
                foldout.Add(CreateNativeSettingsNote("Not included in regular Run Actions updates."));
            }

            List<VisualElement> packageControls = new List<VisualElement> {CreateNativeSettingsValueText($"{_stats?.AIPackages ?? 0:N0} (set per package in Packages view)")};
            if (ShowAdvanced())
            {
                packageControls.Add(AssetInventoryUITK.CreateSecondaryButton("Show", () => ShowPackageMaintenance(PackageSearch.MaintenanceOption.MarkedForAI)));
            }
            foldout.Add(CreateNativeSettingsValueRow("Activated Packages", null, packageControls.ToArray()));

            foldout.Add(CreateNativeSettingsTypeGroupStringListRow(
                "Create Captions for",
                "File extensions or type groups in curly braces (e.g. {prefabs}, {images}, {models}, {materials}) that should be captioned. Type groups automatically expand to all registered extensions for that group.",
                () => AI.Config.aiCaptionExtensions,
                value => AI.Config.aiCaptionExtensions = value,
                "Caption Extensions"));

            if (!ShowAdvanced()) return;

            foldout.Add(CreateNativeSettingsToggleRow("Log Created Captions", "Print finished captions to the console.", AI.Config.logAICaptions, value => AI.Config.logAICaptions = value));
            foldout.Add(CreateNativeSettingsIntegerRow("Max Caption Length", "Maximum allowed caption length to preserve memory and display quality.", AI.Config.aiMaxCaptionLength, value => AI.Config.aiMaxCaptionLength = Mathf.Max(1, value)));
            foldout.Add(CreateNativeSettingsFloatRow("Pause Between Calculations", "Pause after each AI inference request to reduce system load.", AI.Config.aiPause, value => AI.Config.aiPause = Mathf.Max(0f, value), CreateNativeSettingsInlineText("seconds")));
            foldout.Add(CreateNativeSettingsIntegerRow(
                "Request Timeout",
                "Cancel an individual AI request after this many seconds. Set to 0 to disable.",
                AI.Config.aiTimeout,
                value => AI.Config.aiTimeout = Mathf.Max(0, value),
                CreateNativeSettingsInlineText(AI.Config.aiTimeout == 0 ? "seconds (off)" : "seconds")));
        }

        private void AddNativeAIBackendSettings(VisualElement foldout)
        {
            foldout.Add(CreateNativeSettingsPopupRow(
                "Backend",
                "The technology to use for AI.",
                _aiBackendOptions,
                AI.Config.aiBackend,
                value =>
                {
                    AI.Config.aiBackend = value;
                    RefreshNativeAISettingsSection(true);
                }));

            switch (AI.Config.aiBackend)
            {
                case 0:
                    AddNativeBlipSettings(foldout);
                    break;
                case 1:
                    AddNativeOllamaSettings(foldout);
                    break;
                case 2:
                    AddNativeLMStudioSettings(foldout);
                    break;
            }
        }

        private void AddNativeBlipSettings(VisualElement foldout)
        {
            VisualElement installation = new VisualElement();
            installation.AddToClassList(SettingsFieldColumnClass);
            installation.Add(AssetInventoryUITK.CreateHelpBox("This backend requires the free Blip-Caption command-line tool. Follow the linked setup guide before testing the connection."));
            installation.Add(AssetInventoryUITK.CreateSecondaryButton("Salesforce Blip through Blip-Caption tool", () => AI.OpenURL("https://github.com/simonw/blip-caption")));
            foldout.Add(CreateNativeSettingsValueRow("Installation", "The model to be used for captioning. Local models are free of charge, but require a potent computer and graphics card.", installation));

            if (ShowAdvanced())
            {
                foldout.Add(CreateNativeSettingsFolderRow("Blip Folder", AI.Config.blipPath, AI.Config.blipPath, value => AI.Config.blipPath = value, "Select Blip folder"));
            }

            foldout.Add(CreateNativeSettingsPopupRow("Caption Model", "The variant of the model that should be used for AI captions.", _blipOptions, AI.Config.blipType, value => AI.Config.blipType = value));

            if (ShowAdvanced())
            {
                foldout.Add(CreateNativeSettingsToggleRow("Ignore Empty Results", "Do not stop captioning when encountering empty captions, which typically means the tooling is not properly set up.", AI.Config.aiContinueOnEmpty, value => AI.Config.aiContinueOnEmpty = value));
                foldout.Add(CreateNativeSettingsToggleRow("Use GPU", "Activate GPU acceleration if your system supports it.", AI.Config.blipUseGPU, value => AI.Config.blipUseGPU = value));
            }

            foldout.Add(CreateNativeSettingsIntegerRow("Batch Size", "Number of files that are captioned by the model at once.", AI.Config.blipChunkSize, value => AI.Config.blipChunkSize = Mathf.Max(1, value)));
        }

        private void AddNativeOllamaSettings(VisualElement foldout)
        {
            if (ShowAdvanced())
            {
                Button reset = AssetInventoryUITK.CreateSecondaryButton("Reset", () =>
                {
                    AI.Config.ollamaServiceUrl = Intelligence.OLLAMA_SERVICE_URL;
                    Intelligence.RefreshOllama();
                    OnNativeAIConfigChanged(true);
                });
                foldout.Add(CreateNativeSettingsTextRow("Service URL", "The URL of the Ollama service.", AI.Config.ollamaServiceUrl, value =>
                {
                    AI.Config.ollamaServiceUrl = value;
                    Intelligence.RefreshOllama();
                    RefreshNativeAISettingsSection(true);
                }, reset));
            }

            if (Intelligence.IsOllamaInstalled)
            {
                foldout.Add(CreateNativeOllamaModelSelector(
                    "Caption Model",
                    "The model to use for AI captions. Must be listed in the Ollama library and support vision input and analysis.",
                    AI.Config.ollamaModel,
                    model => AI.Config.ollamaModel = model,
                    ShowInstalledOllamaModels,
                    ShowSuggestedOllamaModels,
                    Intelligence.OLLAMA_LIBRARY,
                    true,
                    true));

                foldout.Add(CreateNativeSettingsIntegerRow("Batch Size", "Number of requests to send to Ollama in parallel.", AI.Config.ollamaParallelRequests, value => AI.Config.ollamaParallelRequests = Mathf.Max(1, value)));
                return;
            }

            foldout.Add(CreateNativeBackendUnavailableRow(
                "Ollama is not installed or active. Start it first and retry.",
                Intelligence.RefreshOllama,
                "Ollama Website",
                Intelligence.OLLAMA_WEBSITE));
        }

        private void AddNativeLMStudioSettings(VisualElement foldout)
        {
            if (ShowAdvanced())
            {
                Button reset = AssetInventoryUITK.CreateSecondaryButton("Reset", () =>
                {
                    AI.Config.lmStudioServiceUrl = Intelligence.LMSTUDIO_SERVICE_URL;
                    Intelligence.RefreshLMStudio();
                    OnNativeAIConfigChanged(true);
                });
                foldout.Add(CreateNativeSettingsTextRow("Service URL", "The URL of the LM Studio service.", AI.Config.lmStudioServiceUrl ?? Intelligence.LMSTUDIO_SERVICE_URL, value =>
                {
                    AI.Config.lmStudioServiceUrl = value;
                    Intelligence.RefreshLMStudio();
                    RefreshNativeAISettingsSection(true);
                }, reset));
            }

            if (Intelligence.IsLMStudioInstalled)
            {
                foldout.Add(CreateNativeLMStudioModelSelector(
                    "Caption Model",
                    "Select a vision model in LM Studio.",
                    AI.Config.lmStudioModel,
                    model => AI.Config.lmStudioModel = model,
                    ShowInstalledLMStudioModels,
                    true));

                foldout.Add(CreateNativeSettingsIntegerRow("Batch Size", "Number of requests to send in parallel.", AI.Config.lmStudioParallelRequests, value => AI.Config.lmStudioParallelRequests = Mathf.Max(1, value)));
                return;
            }

            foldout.Add(CreateNativeBackendUnavailableRow(
                "LM Studio is not installed or the server is not running. Start LM Studio and enable the local server first, then retry.",
                Intelligence.RefreshLMStudio,
                "LM Studio Website",
                Intelligence.LMSTUDIO_WEBSITE));
        }

        private VisualElement CreateNativeBackendUnavailableRow(string message, Action refresh, string websiteLabel, string websiteUrl)
        {
            VisualElement content = new VisualElement();
            content.AddToClassList(SettingsFieldColumnClass);
            content.Add(AssetInventoryUITK.CreateHelpBox(message, MessageType.Error));

            VisualElement buttons = new VisualElement();
            buttons.AddToClassList(SettingsModelControlsClass);
            buttons.Add(AssetInventoryUITK.CreateSecondaryButton("Refresh", () =>
            {
                refresh?.Invoke();
                RefreshNativeAISettingsSection(true);
            }));
            buttons.Add(AssetInventoryUITK.CreateSecondaryButton(websiteLabel, () => AI.OpenURL(websiteUrl)));
            content.Add(buttons);

            return CreateNativeSettingsValueRow("Installation", null, content);
        }

        private void AddNativeAITestImageSettings(VisualElement foldout)
        {
            VisualElement testPanel = new VisualElement();
            testPanel.AddToClassList(SettingsAITestPanelClass);

            Image image = new Image
            {
                image = Logo,
                scaleMode = ScaleMode.ScaleToFit
            };
            image.AddToClassList(SettingsAITestImageClass);
            testPanel.Add(image);

            Button createCaption = AssetInventoryUITK.CreateSecondaryButton(_captionTestRunning ? "Running..." : "Create Caption", () =>
            {
                TestCaptioning();
                RefreshNativeAISettingsSection(true);
            });
            createCaption.SetEnabled(!_captionTestRunning);
            testPanel.Add(createCaption);

            Button modelTester = AssetInventoryUITK.CreateSecondaryButton("Model Tester...", () =>
            {
                ModelTesterUI.ShowWindow(GetTestImageFolder(), AI.Config.aiCustomPrompt, OnModelTesterPromptChanged);
            });
            modelTester.SetEnabled(CanOpenNativeModelTester());
            if (AI.Config.aiBackend == 0) modelTester.style.display = DisplayStyle.None;
            testPanel.Add(modelTester);

            Label caption = CreateNativeSettingsValueText(_captionTest);
            caption.tooltip = _captionTest;
            foldout.Add(CreateNativeSettingsValueRow("Test Image", null, testPanel, caption));
        }

        private static bool CanOpenNativeModelTester()
        {
            if (AI.Config.aiBackend == 1) return Intelligence.IsOllamaInstalled && !Intelligence.LoadingModels;
            if (AI.Config.aiBackend == 2) return Intelligence.IsLMStudioInstalled && !Intelligence.LoadingLMStudioModels;
            return false;
        }

        private void AddNativeSemanticSearchSettings(VisualElement foldout)
        {
            if (!ShouldShowSemanticSearchSettings(AI.Actions.SemanticSearchEnabled)) return;

            if (!AI.Actions.UpdateSemanticIndex)
            {
                foldout.Add(CreateNativeSettingsNote("Not included in regular Run Actions updates."));
            }

            List<VisualElement> packageControls = new List<VisualElement> {CreateNativeSettingsValueText($"{_stats?.SemanticIndexPackages ?? 0:N0} (set per package in Packages view)")};
            if (ShowAdvanced())
            {
                packageControls.Add(AssetInventoryUITK.CreateSecondaryButton("Show", () => ShowPackageMaintenance(PackageSearch.MaintenanceOption.MarkedForSemanticIndex)));
            }
            foldout.Add(CreateNativeSettingsValueRow("Activated Packages", "Configure semantic index maintenance and embedding settings.", packageControls.ToArray()));

            if (AI.Config.aiBackend == 0)
            {
                foldout.Add(AssetInventoryUITK.CreateHelpBox("Semantic search needs an embedding backend. Select Ollama or LM Studio as the AI backend to configure semantic embeddings."));
                return;
            }

            if (AI.Config.aiBackend == 2)
            {
                foldout.Add(CreateNativeLMStudioModelSelector(
                    "Embedding Model",
                    "Model used to create vectors for semantic search. The backend is taken from the AI backend above.",
                    AI.Config.semanticLmStudioEmbeddingModel,
                    model => AI.Config.semanticLmStudioEmbeddingModel = model,
                    ShowInstalledLMStudioEmbeddingModels,
                    true));
            }
            else
            {
                string embeddingModel = string.IsNullOrWhiteSpace(AI.Config.semanticOllamaEmbeddingModel)
                    ? EmbeddingEngine.DefaultOllamaEmbeddingModel
                    : AI.Config.semanticOllamaEmbeddingModel;
                foldout.Add(CreateNativeOllamaModelSelector(
                    "Embedding Model",
                    "Model used to create vectors for semantic search. Recommended for Ollama: embeddinggemma. Fast option: all-minilm. Quality options include mxbai-embed-large and qwen3-embedding.",
                    embeddingModel,
                    model => AI.Config.semanticOllamaEmbeddingModel = model,
                    ShowInstalledOllamaEmbeddingModels,
                    ShowSuggestedOllamaEmbeddingModels,
                    Intelligence.OLLAMA_EMBEDDING_LIBRARY,
                    false,
                    true));
            }

            if (!ShowAdvanced()) return;

            foldout.Add(CreateNativeSettingsTypeGroupStringListRow(
                "Semantic File Types",
                "File extensions or type groups in curly braces (e.g. {images}, {prefabs}, {models}, {audio}) that should be included in the semantic asset index. Type groups automatically expand to all registered extensions for that group.",
                () => AI.Config.semanticIndexExtensions,
                value => AI.Config.semanticIndexExtensions = value,
                "Semantic File Types"));

            foldout.Add(CreateNativeSettingsIntegerRow("Embedding Batch Size", "Number of texts to send in a single embedding request.", AI.Config.semanticEmbeddingBatchSize, value => AI.Config.semanticEmbeddingBatchSize = Mathf.Max(1, value)));
        }

        private void AddNativeCodeSearchSettings(VisualElement foldout)
        {
            if (!ShouldShowCodeSearchSettings(AI.Actions.CodeSearchEnabled)) return;

            if (!AI.Actions.UpdateCodeIndex)
            {
                foldout.Add(CreateNativeSettingsNote("Not included in regular Run Actions updates."));
            }

            List<VisualElement> packageControls = new List<VisualElement> {CreateNativeSettingsValueText($"{_stats?.CodeIndexPackages ?? 0:N0} (set per package in Packages view)")};
            if (ShowAdvanced())
            {
                packageControls.Add(AssetInventoryUITK.CreateSecondaryButton("Show", () => ShowPackageMaintenance(PackageSearch.MaintenanceOption.MarkedForCodeIndex)));
            }
            foldout.Add(CreateNativeSettingsValueRow("Activated Packages", "Configure the dedicated code search sidecar index.", packageControls.ToArray()));

            foldout.Add(CreateNativeSettingsToggleRow("Index Project Files", "Include source files from the current Unity project.", AI.Config.codeIndexProjectFiles, value => AI.Config.codeIndexProjectFiles = value));
            foldout.Add(CreateNativeSettingsToggleRow("Index Package Files", "Include source files from indexed packages and package caches.", AI.Config.codeIndexPackageFiles, value => AI.Config.codeIndexPackageFiles = value));
            foldout.Add(CreateNativeSettingsToggleRow("Auto Track Project Changes", "Record source-file imports, moves, and deletions for future incremental code index updates.", AI.Config.codeIndexAutoUpdateProjectChanges, value => AI.Config.codeIndexAutoUpdateProjectChanges = value));
            foldout.Add(CreateNativeSettingsTypeGroupStringListRow(
                "Code File Types",
                "File extensions or type groups in curly braces (e.g. {scripts}, {shaders}) that should be included in the code search index.",
                () => AI.Config.codeIndexExtensions,
                value => AI.Config.codeIndexExtensions = value,
                "Code File Types"));
            foldout.Add(CreateNativeSettingsIntegerRow("Max File Size", "Skip individual source files above this size while building the code index.", AI.Config.codeIndexMaxFileSizeKb, value => AI.Config.codeIndexMaxFileSizeKb = Mathf.Max(1, value), CreateNativeSettingsInlineText("KB")));

            if (ShowAdvanced())
            {
                foldout.Add(CreateNativeSettingsToggleRow("Semantic Rerank", "Reserved for optional code embeddings in the code sidecar. The current code search uses lexical FTS ranking.", AI.Config.codeIndexSemanticRerank, value => AI.Config.codeIndexSemanticRerank = value));
            }
        }

        private VisualElement BuildNativeLocationsSettingsSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection();
            section.AddToClassList(SettingsLocationsSectionClass);
            _nativeLocationsSettingsSection = section;
            _nativeLocationsSettingsHash = int.MinValue;
            RefreshNativeLocationsSettingsSection(true);
            return section;
        }

        private void RefreshNativeLocationsSettingsSection(bool force = false)
        {
            if (_nativeLocationsSettingsSection == null || AI.Config == null) return;

            int hash = GetNativeLocationsSettingsHash();
            if (!force && _nativeLocationsSettingsHash == hash) return;

            _nativeLocationsSettingsHash = hash;
            _nativeLocationsSettingsSection.Clear();

            Foldout foldout = CreateNativeSettingsFoldout("Locations", AI.Config.showLocationSettings, value =>
            {
                AI.Config.showLocationSettings = value;
                AI.SaveConfig();
                RefreshNativeLocationsSettingsSection(true);
            });
            _nativeLocationsSettingsSection.Add(foldout);

            if (!AI.Config.showLocationSettings) return;

            foldout.Add(AssetInventoryUITK.CreateHelpBox("The database benefits from a fast drive. Move larger backups, previews, caches, and sidecar indexes to another drive when space is limited. Existing data must be moved separately."));

            string dbType = AI.Config?.databaseType ?? DatabaseFactory.SQLITE;
            string dbInfo = dbType == DatabaseFactory.MYSQL
                ? $"{dbType} - {AI.Config?.mysqlHost ?? "localhost"}"
                : $"{dbType} - {Paths.GetStorageFolder()}";
            TextField databaseField = CreateNativeSettingsReadOnlyTextField(dbInfo);
            List<VisualElement> databaseControls = new List<VisualElement> {databaseField};
            bool actionsIdle = AI.Actions == null || !AI.Actions.AnyActionsInProgress;
            if (dbType == DatabaseFactory.SQLITE)
            {
                Button browseDatabase = AssetInventoryUITK.CreateSecondaryButton("Browse...", () =>
                {
                    SetDatabaseLocation();
                    RefreshNativeLocationsSettingsSection(true);
                });
                browseDatabase.SetEnabled(actionsIdle);
                databaseControls.Add(browseDatabase);
            }

            Button configureDatabase = AssetInventoryUITK.CreateSecondaryButton("Configure...", () => DatabaseConfigurationUI.ShowWindow());
            configureDatabase.SetEnabled(actionsIdle);
            databaseControls.Add(configureDatabase);
            foldout.Add(CreateNativeSettingsValueRow("Database", null, databaseControls.ToArray()));

            foldout.Add(CreateNativeSettingsFolderRow(
                "Backups",
                Paths.GetBackupFolder(false),
                AI.Config.backupFolder,
                value => AI.Config.backupFolder = value,
                "Select backup storage folder"));

            foldout.Add(CreateNativeSettingsFolderRow(
                "Previews",
                Paths.GetPreviewFolder(null, true),
                AI.Config.previewFolder,
                value =>
                {
                    AI.Config.previewFolder = value;
                    Paths.RefreshPreviewCache();
                },
                "Select preview storage folder"));

            foldout.Add(CreateNativeSettingsFolderRow(
                "Cache",
                Paths.GetMaterializeFolder(),
                AI.Config.cacheFolder,
                value =>
                {
                    AI.Config.cacheFolder = value;
                    Paths.ClearCaches();
                },
                "Select cache storage folder"));

            foldout.Add(CreateNativeSettingsFolderRow(
                "Semantic Index",
                Paths.GetSemanticIndexFolder(false),
                AI.Config.semanticIndexFolder,
                value =>
                {
                    AI.Config.semanticIndexFolder = value;
                    SemanticIndexService.Close();
                    UpdateStatistics(true);
                },
                "Select semantic index folder"));

            foldout.Add(CreateNativeSettingsCacheLimitRow());

            foldout.Add(CreateNativeSettingsValueRow(
                "Configuration",
                null,
                CreateNativeSettingsReadOnlyTextField(AI.UsedConfigLocation),
                AssetInventoryUITK.CreateSecondaryButton("Open", () => EditorUtility.RevealInFinder(AI.UsedConfigLocation))));
            foldout.Add(AssetInventoryUITK.CreateHelpBox("For project-specific settings, copy this JSON file into the project. To move the global file, set ASSETINVENTORY_CONFIG_PATH. See the documentation for supported locations."));

            foldout.Add(CreateNativeSettingsFolderRow(
                "Custom HTML Templates",
                TemplateUtils.GetTemplateRootFolder(),
                AI.Config.customTemplateFolder,
                value => AI.Config.customTemplateFolder = value,
                "Select custom HTML template folder"));

            foldout.Add(CreateNativeSettingsValueRow(
                "FTP/SFTP Connections",
                null,
                AssetInventoryUITK.CreateSecondaryButton("Configure...", () => FTPAdminUI.ShowWindow())));
        }

        private int GetNativeLocationsSettingsHash()
        {
            unchecked
            {
                int hash = 17;
                hash = AddHash(hash, AI.Config.showLocationSettings);
                hash = AddHash(hash, AI.Config.databaseType);
                hash = AddHash(hash, AI.Config.mysqlHost);
                hash = AddHash(hash, Paths.GetStorageFolder());
                hash = AddHash(hash, AI.Actions != null && AI.Actions.AnyActionsInProgress);
                hash = AddHash(hash, AI.Config.backupFolder);
                hash = AddHash(hash, Paths.GetBackupFolder(false));
                hash = AddHash(hash, AI.Config.previewFolder);
                hash = AddHash(hash, Paths.GetPreviewFolder(null, true));
                hash = AddHash(hash, AI.Config.cacheFolder);
                hash = AddHash(hash, Paths.GetMaterializeFolder());
                hash = AddHash(hash, AI.Config.semanticIndexFolder);
                hash = AddHash(hash, Paths.GetSemanticIndexFolder(false));
                hash = AddHash(hash, AI.Config.limitCacheSize);
                hash = AddHash(hash, AI.Config.cacheLimit);
                hash = AddHash(hash, AI.CacheLimiter.IsRunning);
                hash = AddHash(hash, AI.CacheLimiter.CurrentSize);
                hash = AddHash(hash, AI.CacheLimiter.GetLimit());
                hash = AddHash(hash, AI.UsedConfigLocation);
                hash = AddHash(hash, AI.Config.customTemplateFolder);
                hash = AddHash(hash, TemplateUtils.GetTemplateRootFolder());
                return hash;
            }
        }

        private VisualElement CreateNativeSettingsCacheLimitRow()
        {
            Toggle toggle = new Toggle();
            toggle.AddToClassList(SettingsToggleRowClass);
            toggle.AddToClassList(SettingsToggleInputClass);
            toggle.tooltip = "Flag if to regularly scan the cache folder and remove old items until the size limit is reached again. Only items that are not marked as 'Keep Extracted' will be removed.";
            toggle.SetValueWithoutNotify(AI.Config.limitCacheSize);
            toggle.RegisterValueChangedCallback(evt =>
            {
                AI.Config.limitCacheSize = evt.newValue;
                ApplyNativeCacheLimiterSettings();
                AI.SaveConfig();
               RefreshNativeLocationsSettingsSection(true);
           });

            List<VisualElement> controls = new List<VisualElement> {toggle};
            if (AI.Config.limitCacheSize)
            {
                IntegerField limit = new IntegerField
                {
                    value = AI.Config.cacheLimit,
                    tooltip = "Cache size limit in gigabytes."
                };
                limit.AddToClassList(SettingsNumberFieldClass);
                limit.RegisterValueChangedCallback(evt =>
                {
                    AI.Config.cacheLimit = Mathf.Max(1, evt.newValue);
                    ApplyNativeCacheLimiterSettings();
                    AI.SaveConfig();
                   RefreshNativeLocationsSettingsSection(true);
               });
                controls.Add(limit);
                controls.Add(CreateNativeSettingsInlineText("Gb"));

                Button runCheck = AssetInventoryUITK.CreateSecondaryButton(AI.CacheLimiter.IsRunning ? "Calculating..." : "Run Check", () =>
                {
                    _ = AI.CacheLimiter.CheckAndClean();
                    RefreshNativeLocationsSettingsSection(true);
                });
                runCheck.SetEnabled(!AI.CacheLimiter.IsRunning);
                controls.Add(runCheck);

                if (AI.CacheLimiter.CurrentSize > 0)
                {
                    controls.Add(CreateNativeSettingsValueText($"Current Size: {EditorUtility.FormatBytes(AI.CacheLimiter.CurrentSize)}"));
                }
                else if (AI.CacheLimiter.CurrentSize > AI.CacheLimiter.GetLimit())
                {
                    controls.Add(CreateNativeSettingsValueText($"The current cache size with {EditorUtility.FormatBytes(AI.CacheLimiter.CurrentSize)} exceeds the limit due to persistent cache entries ('Keep Cached' setting per package) that will not be cleaned up."));
                }
            }

            return CreateNativeSettingsValueRow(
                "Limit Cache Size",
                "Flag if to regularly scan the cache folder and remove old items until the size limit is reached again. Only items that are not marked as 'Keep Extracted' will be removed.",
                controls.ToArray());
        }

        private void ApplyNativeCacheLimiterSettings()
        {
            AI.CacheLimiter.Enabled = AI.Config.limitCacheSize;
            AI.CacheLimiter.SetLimit(AI.Config.cacheLimit);
        }

        private VisualElement BuildNativeUIIntegrationSettingsSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection();
            section.AddToClassList(SettingsUIIntegrationSectionClass);
            _nativeUIIntegrationSettingsSection = section;
            _nativeUIIntegrationSettingsHash = int.MinValue;
            RefreshNativeUIIntegrationSettingsSection(true);
            return section;
        }

        private void RefreshNativeUIIntegrationSettingsSection(bool force = false)
        {
            if (_nativeUIIntegrationSettingsSection == null || AI.Config == null) return;

            int hash = GetNativeUIIntegrationSettingsHash();
            if (!force && _nativeUIIntegrationSettingsHash == hash) return;

            _nativeUIIntegrationSettingsHash = hash;
            _nativeUIIntegrationSettingsSection.Clear();

            Foldout foldout = CreateNativeSettingsFoldout("UI Integration", AI.Config.showUISettings, value =>
            {
                AI.Config.showUISettings = value;
                AI.SaveConfig();
                RefreshNativeUIIntegrationSettingsSection(true);
            });
            _nativeUIIntegrationSettingsSection.Add(foldout);

            if (!AI.Config.showUISettings) return;

            foldout.Add(CreateNativeSettingsSubsectionTitle("'Assets' Menu"));
            foldout.Add(CreateNativeSettingsDefineToggleRow(
                "Show Asset Inventory",
                AI.DEFINE_SYMBOL_HIDE_AI,
                "Shows or hides the Asset Inventory entry in the Assets menu."));
            foldout.Add(CreateNativeSettingsDefineToggleRow(
                "Show Asset Browser",
                AI.DEFINE_SYMBOL_HIDE_BROWSER,
                "Shows or hides the Asset Browser entry in the Assets menu."));

            foldout.Add(CreateNativeSettingsSubsectionTitle("'Tools' Menu"));
            foldout.Add(CreateNativeSettingsDefineToggleRow(
                "Show Asset Inventory",
                AI.DEFINE_SYMBOL_HIDE_TOOLS_MENU,
                "Shows or hides Asset Inventory commands in the Tools menu."));

            foldout.Add(CreateNativeSettingsSubsectionTitle("Editor Windows"));
            foldout.Add(CreateNativeSettingsDefineToggleRow(
                "Project Window Toolbar",
                AI.DEFINE_SYMBOL_HIDE_PROJECT_TOOLBAR,
                "Shows or hides the Asset Inventory toolbar integration in the Project window."));

            foldout.Add(CreateNativeSettingsSubsectionTitle("Browser"));
            foldout.Add(CreateNativeSettingsPopupRow(
                "Open Links With",
                "Select which browser to use when opening URLs from Asset Inventory.",
                _browserTypeOptions,
                AI.Config.browserType,
                value =>
                {
                    AI.Config.browserType = value;
                    RefreshNativeUIIntegrationSettingsSection(true);
                }));

            if (AI.Config.browserType == 1)
            {
                foldout.Add(CreateNativeSettingsTextRow(
                    "Browser Application",
                    "Full path to the browser executable to use for opening links.",
                    AI.Config.customBrowserPath,
                    value => AI.Config.customBrowserPath = value,
                    AssetInventoryUITK.CreateSecondaryButton("Browse...", BrowseNativeCustomBrowser)));
            }

            foldout.Add(CreateNativeSettingsToggleRow(
                "Keep Active in Background",
                "Keeps Project window previews and other closed-window integrations available after scripts reload even when the Asset Inventory window is closed. May slow down domain reloads when the database is remote or unavailable.",
                AI.Config.forceInitOnDomainReload,
                value => AI.Config.forceInitOnDomainReload = value));
        }

        private int GetNativeUIIntegrationSettingsHash()
        {
            unchecked
            {
                int hash = 17;
                hash = AddHash(hash, AI.Config.showUISettings);
                hash = AddHash(hash, EditorUtils.HasDefine(AI.DEFINE_SYMBOL_HIDE_AI));
                hash = AddHash(hash, EditorUtils.HasDefine(AI.DEFINE_SYMBOL_HIDE_BROWSER));
                hash = AddHash(hash, EditorUtils.HasDefine(AI.DEFINE_SYMBOL_HIDE_TOOLS_MENU));
                hash = AddHash(hash, EditorUtils.HasDefine(AI.DEFINE_SYMBOL_HIDE_PROJECT_TOOLBAR));
                hash = AddHash(hash, AI.Config.browserType);
                hash = AddHash(hash, AI.Config.customBrowserPath);
                hash = AddHash(hash, AI.Config.forceInitOnDomainReload);
                return hash;
            }
        }

        private VisualElement BuildNativeAdvancedSettingsSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection();
            section.AddToClassList(SettingsAdvancedSectionClass);
            _nativeAdvancedSettingsSection = section;
            _nativeAdvancedSettingsHash = int.MinValue;
            RefreshNativeAdvancedSettingsSection(true);
            return section;
        }

        private void RefreshNativeAdvancedSettingsSection(bool force = false)
        {
            if (_nativeAdvancedSettingsSection == null || AI.Config == null) return;

            int hash = GetNativeAdvancedSettingsHash();
            if (!force && _nativeAdvancedSettingsHash == hash) return;

            _nativeAdvancedSettingsHash = hash;
            _nativeAdvancedSettingsSection.Clear();

            bool visible = AI.Config.showAdvancedSettings || ShowAdvanced();
            _nativeAdvancedSettingsSection.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) return;

            Foldout foldout = CreateNativeSettingsFoldout("Advanced", AI.Config.showAdvancedSettings, value =>
            {
                AI.Config.showAdvancedSettings = value;
                AI.SaveConfig();
                RefreshNativeAdvancedSettingsSection(true);
            });
            _nativeAdvancedSettingsSection.Add(foldout);

            if (!AI.Config.showAdvancedSettings) return;

            VisualElement interfaceGroup = AddNativeSettingsGroup(
                foldout,
                "Interface and Progressive Disclosure",
                "Tune how advanced controls, filters, grids, and supporting selectors are presented.");
            interfaceGroup.Add(CreateNativeSettingsToggleRow(
                "Hide Advanced by Default",
                "Start in Standard mode with advanced features hidden. Use the eye icon in the upper-right toolbar to switch between Standard and Advanced mode.",
                AI.Config.hideAdvanced,
                value =>
                {
                    AI.Config.hideAdvanced = value;
                    MarkNativeAdvancedSettingsChanged(true, true);
                }));
            interfaceGroup.Add(CreateNativeSettingsToggleRow(
                "Color Closed Tag Filter Field",
                "Tint the selected Package Tag/File Tag filter field with the tag color. The dropdown list stays colored either way.",
                AI.Config.colorTagFilterClosedField,
                value =>
                {
                    AI.Config.colorTagFilterClosedField = value;
                    MarkNativeAdvancedSettingsChanged();
                }));
            interfaceGroup.Add(CreateNativeSettingsToggleRow(
                "Enlarge Grid Tiles",
                "Use all available grid space and snap to a different size only when the configured tile size allows it.",
                AI.Config.enlargeTiles,
                value =>
                {
                    AI.Config.enlargeTiles = value;
                    MarkNativeAdvancedSettingsChanged();
                }));
            interfaceGroup.Add(CreateNativeAdvancedFontSizeRow());
            interfaceGroup.Add(CreateNativeSettingsIntegerRow(
                "Tag Selection Window Height",
                "Height of the tag list window when selecting Add Tag.",
                AI.Config.tagListHeight,
                value =>
                {
                    AI.Config.tagListHeight = value;
                    MarkNativeAdvancedSettingsChanged();
                },
                CreateNativeSettingsInlineText("pixels")));
            interfaceGroup.Add(CreateNativeSettingsIntegerRow(
                "No Package Text Below",
                "Hide package text in grid mode below this tile size.",
                AI.Config.noPackageTileTextBelow,
                value =>
                {
                    AI.Config.noPackageTileTextBelow = value;
                    MarkNativeAdvancedSettingsChanged();
                },
                CreateNativeSettingsInlineText("tile size")));

            VisualElement online = AddNativeSettingsGroup(
                foldout,
                "Asset Store and Online Metadata",
                "Control external links, pricing, refresh cadence, and which package updates are surfaced.");
            online.Add(CreateNativeSettingsToggleRow(
                "Use Affiliate Links",
                "Support further development by allowing affiliate links when opening Asset Store pages.",
                AI.Config.useAffiliateLinks,
                value =>
                {
                    AI.Config.useAffiliateLinks = value;
                    MarkNativeAdvancedSettingsChanged();
                }));
            online.Add(CreateNativeSettingsToggleRow(
                "Fetch Original Price",
                "Show the non-discounted price instead of the current potentially discounted price.",
                AI.Config.showOriginalPrice,
                value =>
                {
                    AI.Config.showOriginalPrice = value;
                    MarkNativeAdvancedSettingsChanged();
                }));
            online.Add(CreateNativeSettingsIntegerRow(
                "Online Metadata Refresh Cycle",
                "Number of days after which Asset Store metadata should be refreshed.",
                AI.Config.assetStoreRefreshCycle,
                value =>
                {
                    AI.Config.assetStoreRefreshCycle = value;
                    MarkNativeAdvancedSettingsChanged();
                },
                CreateNativeSettingsInlineText("days")));
            online.Add(CreateNativeSettingsToggleRow(
                "Updates For Indirect Dependencies",
                "Show updates for packages that are indirect dependencies.",
                AI.Config.showIndirectPackageUpdates,
                value =>
                {
                    AI.Config.showIndirectPackageUpdates = value;
                    MarkNativeAdvancedSettingsChanged();
                }));
            online.Add(CreateNativeSettingsToggleRow(
                "Updates For Custom Packages",
                "Show custom packages in available updates even though they cannot be updated automatically.",
                AI.Config.showCustomPackageUpdates,
                value =>
                {
                    AI.Config.showCustomPackageUpdates = value;
                    MarkNativeAdvancedSettingsChanged();
                }));
            online.Add(CreateNativeSettingsToggleRow(
                "Auto-Refresh Purchases",
                "Update Asset Store purchases automatically when the tool starts.",
                AI.Config.autoRefreshPurchases,
                value =>
                {
                    AI.Config.autoRefreshPurchases = value;
                    MarkNativeAdvancedSettingsChanged(true);
                }));
            if (AI.Config.autoRefreshPurchases)
            {
                online.Add(CreateNativeSettingsIntegerRow(
                    "Refresh Period",
                    "Number of hours after which Asset Store purchases should be refreshed.",
                    AI.Config.purchasesRefreshPeriod,
                    value =>
                    {
                        AI.Config.purchasesRefreshPeriod = value;
                        MarkNativeAdvancedSettingsChanged();
                    },
                    CreateNativeSettingsInlineText("hours")));
            }

            online.Add(CreateNativeSettingsToggleRow(
                "Auto-Refresh Metadata",
                "Update package metadata in the background when selecting a package.",
                AI.Config.autoRefreshMetadata,
                value =>
                {
                    AI.Config.autoRefreshMetadata = value;
                    MarkNativeAdvancedSettingsChanged(true);
                }));
            if (AI.Config.autoRefreshMetadata)
            {
                online.Add(CreateNativeSettingsIntegerRow(
                    "Max Age",
                    "Maximum metadata age before it is loaded again.",
                    AI.Config.metadataTimeout,
                    value =>
                    {
                        AI.Config.metadataTimeout = value;
                        MarkNativeAdvancedSettingsChanged();
                    },
                    CreateNativeSettingsInlineText("hours")));
            }

            VisualElement background = AddNativeSettingsGroup(
                foldout,
                "Background Work and Performance",
                "Bound concurrent Unity requests, incremental refresh work, preview loading, and cache observation.");
            background.Add(CreateNativeSettingsIntegerRow(
                "Concurrent Requests to Unity API",
                "Maximum number of requests sent to the Unity backend at the same time.",
                AI.Config.maxConcurrentUnityRequests,
                value =>
                {
                    AI.Config.maxConcurrentUnityRequests = value;
                    MarkNativeAdvancedSettingsChanged();
                }));
            background.Add(CreateNativeSettingsIntegerRow(
                "Preview Image Load Chunk Size",
                "Number of preview images to load in parallel.",
                AI.Config.previewChunkSize,
                value =>
                {
                    AI.Config.previewChunkSize = value;
                    MarkNativeAdvancedSettingsChanged();
                }));
            background.Add(CreateNativeSettingsIntegerRow(
                "Package State Refresh Speed",
                "Number of packages to gather update information for in the background per cycle.",
                AI.Config.observationSpeed,
                value =>
                {
                    AI.Config.observationSpeed = value;
                    MarkNativeAdvancedSettingsChanged();
                }));
            background.Add(CreateNativeAdvancedObserverRow());
            if (AI.Config.autoStopObservation)
            {
                background.Add(CreateNativeSettingsIntegerRow(
                    "Observer Timeout",
                    "Time without incoming file events before the cache observer is stopped.",
                    AI.Config.observationTimeout,
                    value =>
                    {
                        AI.Config.observationTimeout = value;
                        MarkNativeAdvancedSettingsChanged();
                    },
                    CreateNativeSettingsInlineText("seconds")));
            }
            background.Add(CreateNativeSettingsToggleRow(
                "Sync Detail Fetching",
                "Fetch asset details before continuing with other index actions so new packages can be downloaded during the first run.",
                AI.Config.awaitNonBlocking,
                value =>
                {
                    AI.Config.awaitNonBlocking = value;
                    MarkNativeAdvancedSettingsChanged();
                }));

            VisualElement analysis = AddNativeSettingsGroup(
                foldout,
                "Analysis and Import Behavior",
                "Tune dependency analysis, reporting resolution, extraction strategy, and preview-scene safety.");
            analysis.Add(CreateNativeSettingsIntegerRow(
                "Reporting Batch Size",
                "Amount of GUIDs processed in a single request. Balance performance against UI responsiveness.",
                AI.Config.reportingBatchSize,
                value =>
                {
                    AI.Config.reportingBatchSize = value;
                    MarkNativeAdvancedSettingsChanged();
                }));
            analysis.Add(CreateNativeSettingsToggleRow(
                "Resolve Multiple Candidates",
                "When multiple origin candidates are found, automatically choose the latest indexed version as the best guess.",
                AI.Config.reportingAutoResolve,
                value =>
                {
                    AI.Config.reportingAutoResolve = value;
                    MarkNativeAdvancedSettingsChanged();
                }));
            analysis.Add(CreateNativeSettingsToggleRow(
                "Extract Single Audio Files",
                "Extract only individual audio files for previews instead of the full archive. This uses less cache space but can increase waiting time.",
                AI.Config.extractSingleFiles,
                value =>
                {
                    AI.Config.extractSingleFiles = value;
                    MarkNativeAdvancedSettingsChanged();
                }));
            analysis.Add(CreateNativeSettingsToggleRow(
                "Scan OBJ Material Dependencies",
                "Analyze OBJ importer metadata for external material dependencies. This experimental option can detect too many dependencies.",
                AI.Config.scanOBJMaterialDependencies,
                value =>
                {
                    AI.Config.scanOBJMaterialDependencies = value;
                    MarkNativeAdvancedSettingsChanged();
                }));
            analysis.Add(CreateNativeSettingsToggleRow(
                "Propose Save Scene for Previews",
                "Prompt to save an untitled scene before creating preview scenes. This can block preview recreation with a modal dialog.",
                AI.Config.proposeSaveSceneDialog,
                value =>
                {
                    AI.Config.proposeSaveSceneDialog = value;
                    MarkNativeAdvancedSettingsChanged();
                }));

            VisualElement diagnostics = AddNativeSettingsGroup(
                foldout,
                "Diagnostics",
                "Limit exception logging to the subsystems currently being investigated.");
            diagnostics.Add(CreateNativeAdvancedLogAreasRow());
            diagnostics.Add(CreateNativeSettingsToggleRow(
                "Asset Store Authentication Warnings",
                "Log one warning per Editor session when Unity authentication is missing or expired. Disabling this does not resume paused Asset Store requests.",
                AI.Config.logAssetStoreAuthenticationWarnings,
                value =>
                {
                    AI.Config.logAssetStoreAuthenticationWarnings = value;
                    MarkNativeAdvancedSettingsChanged();
                }));
        }

        private VisualElement CreateNativeAdvancedFontSizeRow()
        {
            SliderInt slider = new SliderInt(8, 20)
            {
                value = AI.Config.fontSize,
                showInputField = true,
                tooltip = "Font size for grids."
            };
            slider.style.width = 220f;
            slider.RegisterValueChangedCallback(evt =>
            {
                AI.Config.fontSize = evt.newValue;
                AI.SaveConfig();
                _requireAssetTreeRebuild = true;
                _requireSearchUpdate = true;
                Repaint();
            });
            return CreateNativeSettingsValueRow("Font Size", "Font size for grids.", slider);
        }

        private VisualElement CreateNativeAdvancedObserverRow()
        {
            const string tooltip = "Stop the cache observer after no new events arrive for the configured time to reduce background CPU usage.";
            Toggle toggle = new Toggle
            {
                tooltip = tooltip
            };
            toggle.AddToClassList(SettingsToggleRowClass);
            toggle.AddToClassList(SettingsToggleInputClass);
            toggle.SetValueWithoutNotify(AI.Config.autoStopObservation);
            toggle.RegisterValueChangedCallback(evt =>
            {
                AI.Config.autoStopObservation = evt.newValue;
                AI.SaveConfig();
                MarkNativeAdvancedSettingsChanged(true);
            });

            Label status = CreateNativeSettingsInlineText(AI.IsObserverActive() ? "currently active" : "currently inactive");
            return CreateNativeSettingsValueRow("Auto-Stop Cache Observer", tooltip, toggle, status);
        }

        private VisualElement CreateNativeAdvancedLogAreasRow()
        {
            const string tooltip = "Select which error areas should be logged to the console.";
            MaskField field = new MaskField(_logOptions.ToList(), AI.Config.logAreas)
            {
                tooltip = tooltip
            };
            field.style.width = 220f;
            field.RegisterValueChangedCallback(evt =>
            {
                AI.Config.logAreas = evt.newValue;
                AI.SaveConfig();
                MarkNativeAdvancedSettingsChanged();
            });
            return CreateNativeSettingsValueRow("Exception Logging", tooltip, field);
        }

        private void MarkNativeAdvancedSettingsChanged(bool refresh = false, bool refreshShell = false)
        {
            _requireAssetTreeRebuild = true;
            if (!AI.Config.autoStopObservation) AI.StartCacheObserver();
            if (refresh) RefreshNativeAdvancedSettingsSection(true);
            if (refreshShell) MarkUITKShellDirty();
            Repaint();
        }

        private int GetNativeAdvancedSettingsHash()
        {
            unchecked
            {
                int hash = 17;
                hash = AddHash(hash, AI.Config.showAdvancedSettings);
                hash = AddHash(hash, ShowAdvanced());
                hash = AddHash(hash, AssetInventoryUITK.GetAdvancedVisibilityStateHash());
                hash = AddHash(hash, AI.Config.hideAdvanced);
                hash = AddHash(hash, AI.Config.colorTagFilterClosedField);
                hash = AddHash(hash, AI.Config.useAffiliateLinks);
                hash = AddHash(hash, AI.Config.showOriginalPrice);
                hash = AddHash(hash, AI.Config.proposeSaveSceneDialog);
                hash = AddHash(hash, AI.Config.maxConcurrentUnityRequests);
                hash = AddHash(hash, AI.Config.assetStoreRefreshCycle);
                hash = AddHash(hash, AI.Config.previewChunkSize);
                hash = AddHash(hash, AI.Config.observationSpeed);
                hash = AddHash(hash, AI.Config.reportingBatchSize);
                hash = AddHash(hash, AI.Config.reportingAutoResolve);
                hash = AddHash(hash, AI.Config.extractSingleFiles);
                hash = AddHash(hash, AI.Config.scanOBJMaterialDependencies);
                hash = AddHash(hash, AI.Config.showIndirectPackageUpdates);
                hash = AddHash(hash, AI.Config.showCustomPackageUpdates);
                hash = AddHash(hash, AI.Config.enlargeTiles);
                hash = AddHash(hash, AI.Config.fontSize);
                hash = AddHash(hash, AI.Config.autoRefreshPurchases);
                hash = AddHash(hash, AI.Config.purchasesRefreshPeriod);
                hash = AddHash(hash, AI.Config.autoRefreshMetadata);
                hash = AddHash(hash, AI.Config.metadataTimeout);
                hash = AddHash(hash, AI.Config.autoStopObservation);
                hash = AddHash(hash, AI.Config.observationTimeout);
                hash = AddHash(hash, AI.IsObserverActive());
                hash = AddHash(hash, AI.Config.tagListHeight);
                hash = AddHash(hash, AI.Config.noPackageTileTextBelow);
                hash = AddHash(hash, AI.Config.awaitNonBlocking);
                hash = AddHash(hash, AI.Config.logAreas);
                hash = AddHash(hash, AI.Config.logAssetStoreAuthenticationWarnings);
                return hash;
            }
        }

        private VisualElement CreateNativeSettingsSubsectionTitle(string title)
        {
            Label label = new Label(title);
            label.AddToClassList(SettingsSubsectionTitleClass);
            return label;
        }

        private VisualElement AddNativeSettingsGroup(VisualElement parent, string title, string description)
        {
            VisualElement group = new VisualElement();
            group.AddToClassList(SettingsGroupClass);

            VisualElement header = new VisualElement();
            header.AddToClassList(SettingsGroupHeaderClass);
            Label titleLabel = new Label(title);
            titleLabel.AddToClassList(SettingsGroupTitleClass);
            header.Add(titleLabel);
            if (!string.IsNullOrWhiteSpace(description))
            {
                Label descriptionLabel = new Label(description);
                descriptionLabel.AddToClassList(SettingsGroupDescriptionClass);
                header.Add(descriptionLabel);
            }
            group.Add(header);

            VisualElement body = new VisualElement();
            body.AddToClassList(SettingsGroupBodyClass);
            group.Add(body);
            parent.Add(group);
            return body;
        }

        private VisualElement CreateNativeSettingsDefineToggleRow(string label, string defineSymbol, string tooltip)
        {
            bool hidden = EditorUtils.HasDefine(defineSymbol);
            Button button = AssetInventoryUITK.CreateSecondaryButton(hidden ? "Enable" : "Disable", () =>
            {
                if (EditorUtils.HasDefine(defineSymbol))
                {
                    EditorUtils.RemoveDefine(defineSymbol);
                }
                else
                {
                    EditorUtils.AddDefine(defineSymbol);
                }

               RefreshNativeUIIntegrationSettingsSection(true);
           });
            button.tooltip = tooltip;
            return CreateNativeSettingsValueRow(label, tooltip, button);
        }

        private VisualElement CreateNativeSettingsPopupRow(string label, string tooltip, IReadOnlyList<string> options, int selectedIndex, Action<int> onChange)
        {
            List<string> choices = options != null && options.Count > 0
                ? options.ToList()
                : new List<string> {"System Default", "Custom"};
            int clampedIndex = Mathf.Clamp(selectedIndex, 0, choices.Count - 1);
            PopupField<string> popup = new PopupField<string>(choices, clampedIndex)
            {
                tooltip = tooltip
            };
            popup.AddToClassList(SettingsFolderPathClass);
            popup.RegisterValueChangedCallback(evt =>
            {
                int index = choices.IndexOf(evt.newValue);
                if (index < 0) return;

                onChange?.Invoke(index);
               AI.SaveConfig();
           });
            return CreateNativeSettingsValueRow(label, tooltip, popup);
        }

        private VisualElement CreateNativeSettingsTextRow(string label, string tooltip, string value, Action<string> onChange, params VisualElement[] trailingControls)
        {
            TextField field = NativeSettingsFormBuilder.CreateTextField(
                value,
                newValue =>
                {
                    onChange?.Invoke(newValue);
                    AI.SaveConfig();
                },
                tooltip,
                true,
                false,
                SettingsFolderPathClass);

            List<VisualElement> controls = new List<VisualElement> {field};
            if (trailingControls != null) controls.AddRange(trailingControls.Where(control => control != null));
            return CreateNativeSettingsValueRow(label, tooltip, controls.ToArray());
        }

        private VisualElement CreateNativeSettingsTypeGroupStringListRow(string label, string tooltip, Func<string> getValue, Action<string> setValue, string listTitle)
        {
            string currentValue = getValue?.Invoke() ?? string.Empty;
            VisualElement stringList = AssetInventoryUITK.CreateStringListControl(
                this,
                currentValue,
                ";",
                newValue =>
                {
                    setValue?.Invoke(newValue);
                    OnNativeAIConfigChanged(true);
                },
                listTitle,
                tooltip,
                SettingsFolderPathClass);
            stringList.style.flexGrow = 1f;
            stringList.style.flexShrink = 1f;
            stringList.style.minWidth = 0f;

            Button typeGroup = null;
            typeGroup = AssetInventoryUITK.CreateIconButton("Select type groups. Click a selected group to remove it.", "_Popup", () =>
            {
                ShowTypeGroupMenu(
                    getValue,
                    newValue =>
                    {
                        setValue?.Invoke(newValue);
                        RefreshNativeAISettingsSection(true);
                    });
            });

            return CreateNativeSettingsValueRow(label, tooltip, stringList, typeGroup);
        }

        private VisualElement CreateNativeOllamaModelSelector(
            string label,
            string tooltip,
            string modelName,
            Action<string> setModel,
            Action showInstalledModels,
            Action showSuggestedModels,
            string catalogUrl,
            bool showVramWarning,
            bool allowDelete)
        {
            string currentModel = modelName ?? string.Empty;
            TextField field = new TextField
            {
                value = currentModel,
                isDelayed = true,
                tooltip = tooltip
            };
            field.AddToClassList(SettingsFolderPathClass);
            field.RegisterValueChangedCallback(evt =>
            {
                string newModel = evt.newValue?.Trim() ?? string.Empty;
                setModel?.Invoke(newModel);
                OnNativeAIConfigChanged(true);
            });

            VisualElement content = new VisualElement();
            content.AddToClassList(SettingsFieldColumnClass);

            VisualElement controls = new VisualElement();
            controls.AddToClassList(SettingsModelControlsClass);
            controls.Add(field);

            bool modelConfigured = !string.IsNullOrWhiteSpace(currentModel);
            bool modelDownloaded = modelConfigured && Intelligence.OllamaModelDownloaded(currentModel);
            if (modelConfigured && Intelligence.IsOllamaInstalled && !modelDownloaded)
            {
                Button download = AssetInventoryUITK.CreateSecondaryButton("Download Model", () => DownloadOllamaModel(currentModel));
                download.SetEnabled(!Intelligence.DownloadingModel);
                controls.Add(download);
            }

            controls.Add(AssetInventoryUITK.CreateSecondaryButton("Installed", () => showInstalledModels?.Invoke()));
            if (showSuggestedModels != null) controls.Add(AssetInventoryUITK.CreateSecondaryButton("Suggested", () => showSuggestedModels()));

            if (ShowAdvanced() && allowDelete && modelDownloaded)
            {
                Button delete = AssetInventoryUITK.CreateIconButton("Delete model", "TreeEditor.Trash", () => DeleteOllamaModel(currentModel));
                controls.Add(delete);
            }
            content.Add(controls);

            if (IsActiveOllamaDownload(currentModel))
            {
                content.Add(CreateNativeOllamaDownloadProgress());
            }

            if (showVramWarning)
            {
                VisualElement warning = CreateNativeOllamaVramWarning(currentModel);
                if (warning != null) content.Add(warning);
            }

            VisualElement catalog = CreateNativeModelCatalogLink(catalogUrl);
            if (catalog != null) content.Add(catalog);

            return CreateNativeSettingsValueRow(label, tooltip, content);
        }

        private VisualElement CreateNativeLMStudioModelSelector(
            string label,
            string tooltip,
            string modelName,
            Action<string> setModel,
            Action showInstalledModels,
            bool showModelState)
        {
            string currentModel = modelName ?? string.Empty;
            TextField field = new TextField
            {
                value = currentModel,
                isDelayed = true,
                tooltip = tooltip
            };
            field.AddToClassList(SettingsFolderPathClass);
            field.RegisterValueChangedCallback(evt =>
            {
                string newModel = evt.newValue?.Trim() ?? string.Empty;
                setModel?.Invoke(newModel);
                OnNativeAIConfigChanged(true);
            });

            VisualElement content = new VisualElement();
            content.AddToClassList(SettingsFieldColumnClass);

            VisualElement controls = new VisualElement();
            controls.AddToClassList(SettingsModelControlsClass);
            controls.Add(field);
            controls.Add(AssetInventoryUITK.CreateSecondaryButton("Installed", () => showInstalledModels?.Invoke()));
            content.Add(controls);

            if (Intelligence.LoadingLMStudioModels)
            {
                content.Add(AssetInventoryUITK.CreateHelpBox("Loading models..."));
            }
            else if (ShowAdvanced() && showModelState)
            {
                LMStudioModel lmStudioModel = FindLMStudioModel(currentModel);
                if (lmStudioModel != null && !string.IsNullOrEmpty(lmStudioModel.state))
                {
                    string stateText = lmStudioModel.state == "loaded" ? "Loaded" : "Not loaded";
                    content.Add(AssetInventoryUITK.CreateHelpBox($"Model state: {stateText}"));
                }
            }

            VisualElement catalog = CreateNativeModelCatalogLink(Intelligence.LMSTUDIO_LIBRARY);
            if (catalog != null) content.Add(catalog);

            return CreateNativeSettingsValueRow(label, tooltip, content);
        }

        private VisualElement CreateNativeOllamaDownloadProgress()
        {
            VisualElement group = new VisualElement();
            group.AddToClassList(SettingsProgressGroupClass);

            string progressText = _maxOllamaProgress > 0
                ? $"{EditorUtility.FormatBytes(_curOllamaProgress)}/{EditorUtility.FormatBytes(_maxOllamaProgress)}"
                : "Downloading...";
            float progress = _maxOllamaProgress > 0 ? Mathf.Clamp01((float)_curOllamaProgress / _maxOllamaProgress) : 0f;
            ProgressBar bar = new ProgressBar
            {
                value = progress * 100f,
                title = progressText
            };
            group.Add(bar);
            group.Add(AssetInventoryUITK.CreateSecondaryButton("Cancel", () => Intelligence.OllamaDownloadToken?.Cancel()));
            return group;
        }

        private VisualElement CreateNativeOllamaVramWarning(string modelName)
        {
            ModelInfo model = FindOllamaModel(modelName);
            if (model == null || (model.Size / 1024 / 1024) + 2000 <= SystemInfo.graphicsMemorySize) return null;

            return AssetInventoryUITK.CreateHelpBox($"This model may exceed available graphics memory ({model.Size / 1024 / 1024:N0} MB required, {SystemInfo.graphicsMemorySize:N0} MB available) and run much more slowly.", MessageType.Warning);
        }

        private static VisualElement CreateNativeModelCatalogLink(string catalogUrl)
        {
            if (string.IsNullOrWhiteSpace(catalogUrl)) return null;

            return AssetInventoryUITK.CreateSecondaryButton("Model Catalog", () => AI.OpenURL(catalogUrl));
        }

        private void OnNativeAIConfigChanged(bool refresh)
        {
            AI.SaveConfig();
           if (refresh) RefreshNativeAISettingsSection(true);
           Repaint();
        }

        private void OnNativeOptionalFeatureChanged(
            Action refreshFeatureSection,
            bool codeFeatureChanged = false)
        {
            if (codeFeatureChanged && !AI.Config.codeSearchFeatureEnabled)
            {
                CodeIndexAssetPostprocessor.CancelPendingUpdate();
            }

            refreshFeatureSection?.Invoke();
            RefreshNativeIndexingSettingsSection();
            RefreshNativeUpdateActionsSection(true);
            UpdateNativeSettingsSummary();
            EnsureVisibleIndexStatsLoaded();
            _nativeSettingsSidebarHash = 0;
            UpdateNativeSettingsSidebar();
            MarkUITKShellDirty();
        }

        private void BrowseNativeCustomBrowser()
        {
#if UNITY_EDITOR_OSX
            string path = EditorUtility.OpenFilePanel("Select Browser Application", "/Applications", "app");
#elif UNITY_EDITOR_LINUX
            string path = EditorUtility.OpenFilePanel("Select Browser Application", "/usr/bin", "");
#else
            string path = EditorUtility.OpenFilePanel("Select Browser Application", "C:\\Program Files", "exe");
#endif
            if (string.IsNullOrEmpty(path)) return;

            AI.Config.customBrowserPath = path;
            AI.SaveConfig();
           RefreshNativeUIIntegrationSettingsSection(true);
       }

        private void ApplyNativeIndexingSettingsChange()
        {
            Paths.ClearCaches();
            Paths.LoadRelativeLocations();
            _requireLookupUpdate = ChangeImpact.Write;
        }

        private VisualElement CreateNativeSettingsOpenFolderRow(string label, string folder, string tooltip)
        {
            TextField field = CreateNativeSettingsReadOnlyTextField(folder);
            Button open = AssetInventoryUITK.CreateSecondaryButton("Open", () => EditorUtility.RevealInFinder(folder));
            open.SetEnabled(!string.IsNullOrWhiteSpace(folder));
            return CreateNativeSettingsValueRow(label, tooltip, field, open);
        }

        private VisualElement CreateNativeSettingsAutoDownloadLimitRow()
        {
            Toggle toggle = new Toggle();
            toggle.AddToClassList(SettingsToggleRowClass);
            toggle.AddToClassList(SettingsToggleInputClass);
            toggle.tooltip = "Will not automatically download packages larger than specified.";
            toggle.SetValueWithoutNotify(AI.Config.limitAutoDownloads);
            toggle.RegisterValueChangedCallback(evt =>
            {
                AI.Config.limitAutoDownloads = evt.newValue;
                ApplyNativeIndexingSettingsChange();
                AI.SaveConfig();
               RefreshNativeIndexingSettingsSection(true);
           });

            List<VisualElement> controls = new List<VisualElement> {toggle};
            if (AI.Config.limitAutoDownloads)
            {
                IntegerField limit = new IntegerField
                {
                    value = AI.Config.downloadLimit,
                    tooltip = "Maximum package size in megabytes."
                };
                limit.AddToClassList(SettingsNumberFieldClass);
                limit.RegisterValueChangedCallback(evt =>
                {
                    AI.Config.downloadLimit = Mathf.Max(0, evt.newValue);
                    ApplyNativeIndexingSettingsChange();
                   AI.SaveConfig();
               });
                controls.Add(CreateNativeSettingsInlineText("to"));
                controls.Add(limit);
                controls.Add(CreateNativeSettingsInlineText("Mb"));
            }

            return CreateNativeSettingsValueRow("Limit Package Size", "Will not automatically download packages larger than specified.", controls.ToArray());
        }

        private VisualElement CreateNativeSettingsCooldownRow()
        {
            Toggle toggle = new Toggle();
            toggle.AddToClassList(SettingsToggleRowClass);
            toggle.AddToClassList(SettingsToggleInputClass);
            toggle.tooltip = "Will pause all hard disk activity regularly to allow the disk to cool down.";
            toggle.SetValueWithoutNotify(AI.Config.useCooldown);
            toggle.RegisterValueChangedCallback(evt =>
            {
                AI.Config.useCooldown = evt.newValue;
                ApplyNativeIndexingSettingsChange();
                AI.SaveConfig();
               RefreshNativeIndexingSettingsSection(true);
           });

            List<VisualElement> controls = new List<VisualElement> {toggle};
            if (AI.Config.useCooldown)
            {
                IntegerField interval = new IntegerField
                {
                    value = AI.Config.cooldownInterval,
                    tooltip = "Cooldown interval in minutes."
                };
                interval.AddToClassList(SettingsNumberFieldClass);
                interval.RegisterValueChangedCallback(evt =>
                {
                    AI.Config.cooldownInterval = Mathf.Max(1, evt.newValue);
                    ApplyNativeIndexingSettingsChange();
                   AI.SaveConfig();
               });

                IntegerField duration = new IntegerField
                {
                    value = AI.Config.cooldownDuration,
                    tooltip = "Cooldown duration in seconds."
                };
                duration.AddToClassList(SettingsNumberFieldClass);
                duration.RegisterValueChangedCallback(evt =>
                {
                    AI.Config.cooldownDuration = Mathf.Max(1, evt.newValue);
                    ApplyNativeIndexingSettingsChange();
                   AI.SaveConfig();
               });

                controls.Add(CreateNativeSettingsInlineText("every"));
                controls.Add(interval);
                controls.Add(CreateNativeSettingsInlineText("minutes for"));
                controls.Add(duration);
                controls.Add(CreateNativeSettingsInlineText("seconds"));
            }

            return CreateNativeSettingsValueRow("Pause indexing regularly", "Will pause all hard disk activity regularly to allow the disk to cool down.", controls.ToArray());
        }

        private VisualElement CreateNativeSettingsEnumRow<T>(string label, string tooltip, T value, Action<T> onChange) where T : Enum
        {
            EnumField field = NativeSettingsFormBuilder.CreateEnumField(
                value,
                newValue =>
                {
                    onChange?.Invoke(newValue);
                    AI.SaveConfig();
                },
                tooltip);
            field.style.width = 180f;

            return CreateNativeSettingsValueRow(label, tooltip, field);
        }

        private VisualElement CreateNativeSettingsImportTargetFolderRow()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string displayFolder = string.IsNullOrWhiteSpace(AI.Config.importFolder) ? "Assets" : AI.Config.importFolder.Replace("\\", "/");
            string absoluteFolder = Path.GetFullPath(Path.Combine(projectRoot ?? string.Empty, displayFolder));

            TextField field = new TextField
            {
                value = displayFolder,
                isReadOnly = true,
                tooltip = "Target folder for imported files."
            };
            field.AddToClassList(SettingsFolderPathClass);

            Button select = AssetInventoryUITK.CreateSecondaryButton("Browse...", () =>
            {
                string selectedFolder = EditorUtility.OpenFolderPanel("Select folder for imports", absoluteFolder, "");
                if (string.IsNullOrEmpty(selectedFolder)) return;

                string normalizedDataPath = Application.dataPath.Replace("\\", "/");
                string normalizedSelection = Path.GetFullPath(selectedFolder).Replace("\\", "/");
                if (!normalizedSelection.StartsWith(normalizedDataPath, StringComparison.OrdinalIgnoreCase))
                {
                    EditorUtility.DisplayDialog("Error", "Folder must be inside current project", "OK");
                    return;
                }

                string root = Path.GetDirectoryName(Application.dataPath);
                AI.Config.importFolder = normalizedSelection.Substring((root ?? string.Empty).Length + 1);
                AI.SaveConfig();
               RefreshNativeImportSettingsSection(true);
           });

            Button open = AssetInventoryUITK.CreateSecondaryButton("Open", () => EditorUtility.RevealInFinder(absoluteFolder));
            open.SetEnabled(!string.IsNullOrWhiteSpace(absoluteFolder));
            return CreateNativeSettingsValueRow("Target Folder", "Target folder for imported files.", field, select, open);
        }

        private void MarkNativePreviewSettingsChanged()
        {
            _requireSearchUpdate = true;
        }

        private VisualElement CreateNativeSettingsPreviewExcludeExtensionsRow()
        {
            Toggle toggle = new Toggle();
            toggle.AddToClassList(SettingsToggleRowClass);
            toggle.AddToClassList(SettingsToggleInputClass);
            toggle.tooltip = "File extensions or type groups in curly braces that should be skipped when creating preview images.";
            toggle.SetValueWithoutNotify(AI.Config.excludePreviewExtensions);
            toggle.RegisterValueChangedCallback(evt =>
            {
                AI.Config.excludePreviewExtensions = evt.newValue;
                MarkNativePreviewSettingsChanged();
                AI.SaveConfig();
               RefreshNativePreviewsSettingsSection(true);
           });

            List<VisualElement> controls = new List<VisualElement> {toggle};
            if (AI.Config.excludePreviewExtensions)
            {
                VisualElement stringList = AssetInventoryUITK.CreateStringListControl(
                    this,
                    AI.Config.excludedPreviewExtensions,
                    ",",
                    value =>
                    {
                        AI.Config.excludedPreviewExtensions = value;
                        MarkNativePreviewSettingsChanged();
                       AI.SaveConfig();
                   },
                    "Excluded Preview Extensions",
                    "File extensions or type groups in curly braces (e.g. {audio}, {images}, {models}) that should be skipped when creating preview images during media and archive indexing. Type groups automatically expand to all registered extensions for that group.",
                    SettingsFolderPathClass);
                stringList.style.flexGrow = 1f;
                stringList.style.flexShrink = 1f;
                stringList.style.minWidth = 0f;
                controls.Add(stringList);
            }

            return CreateNativeSettingsValueRow(
                "Exclude Extensions",
                "File extensions or type groups in curly braces that should be skipped when creating preview images.",
                controls.ToArray());
        }

        private VisualElement CreateNativeSettingsToggleRow(string label, string tooltip, bool value, Action<bool> onChange)
        {
            return NativeSettingsFormBuilder.CreateToggleRow(
                label,
                value,
                newValue =>
                {
                    onChange?.Invoke(newValue);
                    AI.SaveConfig();
                },
                tooltip,
                SettingsToggleInputClass);
        }

        private VisualElement CreateNativeSettingsIntegerRow(string label, string tooltip, int value, Action<int> onChange, params VisualElement[] trailingControls)
        {
            IntegerField field = NativeSettingsFormBuilder.CreateIntegerField(
                value,
                newValue =>
                {
                    onChange?.Invoke(newValue);
                    AI.SaveConfig();
                },
                tooltip,
                false,
                SettingsNumberFieldClass);

            List<VisualElement> controls = new List<VisualElement> {field};
            if (trailingControls != null) controls.AddRange(trailingControls.Where(control => control != null));
            return CreateNativeSettingsValueRow(label, tooltip, controls.ToArray());
        }

        private VisualElement CreateNativeSettingsFloatRow(string label, string tooltip, float value, Action<float> onChange, params VisualElement[] trailingControls)
        {
            FloatField field = NativeSettingsFormBuilder.CreateFloatField(
                value,
                newValue =>
                {
                    onChange?.Invoke(newValue);
                    AI.SaveConfig();
                },
                tooltip,
                false,
                SettingsNumberFieldClass);

            List<VisualElement> controls = new List<VisualElement> {field};
            if (trailingControls != null) controls.AddRange(trailingControls.Where(control => control != null));
            return CreateNativeSettingsValueRow(label, tooltip, controls.ToArray());
        }

        private VisualElement CreateNativeSettingsFolderRow(string label, string currentFolder, string configuredFolder, Action<string> onChange, string prompt, Func<string, bool> validator = null, Action afterChange = null)
        {
            bool hasCustomFolder = !string.IsNullOrWhiteSpace(configuredFolder);
            string activeFolder = hasCustomFolder ? configuredFolder : currentFolder;
            string displayFolder = hasCustomFolder
                ? configuredFolder
                : string.IsNullOrWhiteSpace(currentFolder) ? "[Default]" : $"[Default] {currentFolder}";
            TextField folder = new TextField
            {
                value = displayFolder,
                isReadOnly = true
            };
            folder.AddToClassList(SettingsFolderPathClass);

            Button openButton = AssetInventoryUITK.CreateSecondaryButton("Open", () =>
            {
                EditorUtility.RevealInFinder(activeFolder);
            });
            openButton.SetEnabled(!string.IsNullOrWhiteSpace(activeFolder));

            Button defaultButton = null;
            if (hasCustomFolder)
            {
                defaultButton = AssetInventoryUITK.CreateSecondaryButton("Default", () =>
                {
                    onChange?.Invoke(null);
                    AI.SaveConfig();
                    RefreshNativeBackupSettingsSection(true);
                    RefreshNativeLocationsSettingsSection(true);
                    RefreshNativeIndexingSettingsSection(true);
                    RefreshNativeImportSettingsSection(true);
                    afterChange?.Invoke();
                });
                defaultButton.tooltip = "Clear the custom location and use the default. Existing data is not moved.";
            }

            Button browseButton = AssetInventoryUITK.CreateSecondaryButton("Browse...", () =>
            {
                string selectedFolder = EditorUtility.OpenFolderPanel(prompt, activeFolder, "");
                if (string.IsNullOrWhiteSpace(selectedFolder)) return;

                string fullPath = Path.GetFullPath(selectedFolder);
                if (validator != null && !validator(fullPath)) return;

                onChange?.Invoke(fullPath);
                AI.SaveConfig();
                RefreshNativeBackupSettingsSection(true);
                RefreshNativeLocationsSettingsSection(true);
                RefreshNativeIndexingSettingsSection(true);
                RefreshNativeImportSettingsSection(true);
                afterChange?.Invoke();
            });

            return CreateNativeSettingsValueRow(label, null, folder, openButton, defaultButton, browseButton);
        }

        private TextField CreateNativeSettingsReadOnlyTextField(string value)
        {
            return NativeSettingsFormBuilder.CreateTextField(
                value,
                null,
                null,
                false,
                true,
                SettingsFolderPathClass);
        }

        private VisualElement CreateNativeSettingsValueRow(string label, string tooltip, params VisualElement[] controls)
        {
            return NativeSettingsFormBuilder.CreateRow(label, tooltip, controls);
        }

        private Label CreateNativeSettingsValueText(string value)
        {
            Label text = new Label(value ?? string.Empty);
            text.AddToClassList(SettingsValueTextClass);
            return text;
        }

        private Label CreateNativeSettingsInlineText(string value)
        {
            Label text = new Label(value ?? string.Empty);
            text.AddToClassList(SettingsValueInlineTextClass);
            return text;
        }

        private void ClearDatabaseFromNativeSettings()
        {
            if (!EditorUtility.DisplayDialog("Confirm", "This will reset the database to its initial empty state. ALL data in the index will be lost.", "Proceed", "Cancel")) return;

            if (DBAdapter.DeleteDB())
            {
                AssetUtils.ClearCache();
                _ = IOUtils.DeleteFileOrDirectory(Paths.GetPreviewFolder());
                _ = IOUtils.DeleteFileOrDirectory(Paths.GetMaterializeFolder());
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "Database seems to be in use by another program and could not be cleared.", "OK");
            }

            UpdateStatistics(true);
            _assets = new List<AssetInfo>();
            _requireAssetTreeRebuild = true;
            _nativeSettingsSidebarHash = 0;
            UpdateNativeSettingsSidebar();
        }

        private VisualElement CreateNativeSettingsVisibilityBlock(string key, Func<VisualElement> contentFactory)
        {
            return AssetInventoryUITK.CreateAdvancedVisibilityBlock(key, contentFactory, onVisibilityChanged: () =>
            {
                _nativeSettingsSidebarHash = 0;
               UpdateNativeSettingsSidebar();
           });
        }

        private static Label CreateNativeSettingsNote(string text)
        {
            Label label = AssetInventoryUITK.CreateCopyLabel(text);
            label.AddToClassList(SettingsCompactNoteClass);
            return label;
        }

        private static float GetNativeSettingsProgress(long progress, long count)
        {
            if (count <= 0) return 0f;
            return Mathf.Clamp01(progress / (float)count);
        }

        private static string FormatNativeSettingsProgressTitle(long progress, long count, string current)
        {
            string currentText = current?.Trim();
            if (!string.IsNullOrWhiteSpace(currentText) && (currentText.Contains("/") || currentText.Contains("\\")))
            {
                string fileName = Path.GetFileName(currentText.TrimEnd('/', '\\'));
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    currentText = fileName;
                }
            }

            return string.IsNullOrWhiteSpace(currentText)
                ? $"{progress:N0}/{count:N0}"
                : $"{progress:N0}/{count:N0} - {currentText}";
        }

        private void SetAllFoldersActive(bool active)
        {
            foreach (FolderSpec folder in AI.Config.folders)
            {
                folder.enabled = active;
            }

            AI.SaveConfig();
        }

        private void InvertFoldersActive()
        {
            foreach (FolderSpec folder in AI.Config.folders)
            {
                folder.enabled = !folder.enabled;
            }

            AI.SaveConfig();
        }

        private static bool ConfirmForceRunAction(UpdateAction action)
        {
            if (action.key == ActionHandler.ACTION_CODE_INDEX)
            {
                return EditorUtility.DisplayDialog(
                    "Rebuild Code Search Index",
                    "This will delete the local code search index and rebuild it. The main Asset Inventory database will not be changed.",
                    "Rebuild",
                    "Cancel");
            }

            if (action.key != ActionHandler.ACTION_SEMANTIC_INDEX) return true;

            return EditorUtility.DisplayDialog(
                "Rebuild Semantic Index",
                "This will delete the local semantic index and rebuild it. The main Asset Inventory database will not be changed.",
                "Rebuild",
                "Cancel");
        }

        private bool RemoveCustomFolderAtIndex(int folderIndex)
        {
            if (folderIndex < 0 || folderIndex >= AI.Config.folders.Count) return false;

            string folderLocation = AI.Config.folders[folderIndex].location;
            if (!EditorUtility.DisplayDialog("Remove Additional Folder",
                    $"Remove this additional folder from the list?\n\n{folderLocation}\n\nThe indexed data and the folder on disk will not be deleted, just the configuration here.",
                    "Remove", "Cancel")) return false;

            AI.Config.folders.RemoveAt(folderIndex);
            AI.SaveConfig();
            return true;
        }

        private void AddCustomFolderFromDialog()
        {
            string folder = EditorUtility.OpenFolderPanel("Select folder to index", "", "");
            if (string.IsNullOrEmpty(folder)) return;

            // make absolute and conform to OS separators
            folder = Path.GetFullPath(folder);

            // special case: a relative key is already defined for the folder to be added, replace it immediately
            folder = Paths.MakeRelative(folder);

            // don't allow adding Unity asset cache folders manually 
            if (folder.Contains(AI.ASSET_STORE_FOLDER_NAME))
            {
                EditorUtility.DisplayDialog("Attention", "You selected a custom Unity asset cache location. This should be done by setting the asset cache location above to custom.", "OK");
                return;
            }

            // ensure no trailing slash if root folder on Windows
            if (folder.Length > 1 && folder.EndsWith("/")) folder = folder.Substring(0, folder.Length - 1);

            FolderWizardUI wizardUI = FolderWizardUI.ShowWindow();
            wizardUI.Init(folder);
        }

        private static string GetTestImageFolder(string folderName = "Test")
        {
            string[] inventoryGuids = AssetDatabase.FindAssets("AssetInventory t:Folder");
            foreach (string guid in inventoryGuids)
            {
                string invPath = AssetDatabase.GUIDToAssetPath(guid);
                string testFolder = $"{invPath}/Editor/Images/{folderName}";
                if (AssetDatabase.IsValidFolder(testFolder))
                {
                    string projectRoot = Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length);
                    return Path.Combine(projectRoot, testFolder).Replace("\\", "/");
                }
            }
            return null;
        }

        private void DeleteOllamaModel()
        {
            DeleteOllamaModel(AI.Config.ollamaModel);
        }

        private void DeleteOllamaModel(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return;

            if (!EditorUtility.DisplayDialog("Confirm Delete", $"Are you sure you want to delete the Ollama model '{modelName}'?", "Delete", "Cancel"))
            {
                return;
            }
            _ = Intelligence.DeleteOllamaModelAsync(modelName);
            RefreshNativeAISettingsSection(true);
        }

        private void DownloadOllamaModel()
        {
            DownloadOllamaModel(AI.Config.ollamaModel);
        }

        private void DownloadOllamaModel(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return;

            string trimmedModelName = modelName.Trim();
            _activeOllamaDownloadModel = trimmedModelName;
            _curOllamaProgress = 0;
            _maxOllamaProgress = 0;
            RefreshNativeAISettingsSection(true);
            Task.Run(async () =>
            {
                await Intelligence.PullOllamaModelAsync(trimmedModelName, status =>
                {
                    _curOllamaProgress = status.Completed;
                    _maxOllamaProgress = status.Total;
                });
                if (string.Equals(_activeOllamaDownloadModel, trimmedModelName, StringComparison.OrdinalIgnoreCase))
                {
                    _activeOllamaDownloadModel = null;
                }
                EditorApplication.delayCall += () =>
                {
                    if (this != null) RefreshNativeAISettingsSection(true);
                };
            });
        }

        private bool IsActiveOllamaDownload(string modelName)
        {
            return Intelligence.DownloadingModel
                && !string.IsNullOrWhiteSpace(modelName)
                && string.Equals(_activeOllamaDownloadModel, modelName, StringComparison.OrdinalIgnoreCase);
        }

        private static ModelInfo FindOllamaModel(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return null;

            return Intelligence.OllamaModels?.FirstOrDefault(m => m.Name == modelName || m.Name.StartsWith(modelName + ":", StringComparison.OrdinalIgnoreCase));
        }

        private static LMStudioModel FindLMStudioModel(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return null;

            return Intelligence.LMStudioModels?.FirstOrDefault(m =>
                !string.IsNullOrEmpty(m.id) &&
                (string.Equals(m.id, modelName, StringComparison.OrdinalIgnoreCase) ||
                    m.id.IndexOf(modelName, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private void ShowInstalledOllamaModels()
        {
            IEnumerable<ModelInfo> models = Intelligence.OllamaModels;

            GenericMenu menu = new GenericMenu();
            if (models != null)
            {
                foreach (ModelInfo model in models.OrderBy(m => m.Name, StringComparer.InvariantCultureIgnoreCase))
                {
                    menu.AddItem(new GUIContent($"{model.Name} ({EditorUtility.FormatBytes(model.Size)}, {model.ParameterSize})"), false, () =>
                    {
                        AI.Config.ollamaModel = model.Name.Split(' ')[0];
                        OnNativeAIConfigChanged(true);
                    });
                }
                menu.AddItem(GUIContent.none, false, () => { });
                menu.AddItem(new GUIContent("Refresh"), false, Intelligence.RefreshOllama);
            }
            else
            {
                if (Intelligence.LoadingModels)
                {
                    menu.AddDisabledItem(new GUIContent("Loading models"));
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent("Models could not be loaded"));
                }
            }
            menu.ShowAsContext();
        }

        private void ShowInstalledOllamaEmbeddingModels()
        {
            IEnumerable<ModelInfo> models = Intelligence.OllamaModels;
            GenericMenu menu = new GenericMenu();
            if (models != null)
            {
                foreach (ModelInfo model in models.OrderBy(m => m.Name, StringComparer.InvariantCultureIgnoreCase))
                {
                    menu.AddItem(new GUIContent($"{model.Name} ({EditorUtility.FormatBytes(model.Size)}, {model.ParameterSize})"), false, () =>
                    {
                        AI.Config.semanticOllamaEmbeddingModel = model.Name.Split(' ')[0];
                        OnNativeAIConfigChanged(true);
                    });
                }
                menu.AddItem(GUIContent.none, false, () => { });
                menu.AddItem(new GUIContent("Refresh"), false, Intelligence.RefreshOllama);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent(Intelligence.LoadingModels ? "Loading models" : "Models could not be loaded"));
            }
            menu.ShowAsContext();
        }

        private void ShowSuggestedOllamaEmbeddingModels()
        {
            GenericMenu menu = new GenericMenu();
            string[] models =
            {
                "embeddinggemma (recommended)",
                "all-minilm (fast, small)",
                "mxbai-embed-large (quality)",
                "qwen3-embedding (quality, multilingual)"
            };
            foreach (string model in models)
            {
                menu.AddItem(new GUIContent(model), false, () =>
                {
                    AI.Config.semanticOllamaEmbeddingModel = model.Split(' ')[0];
                    OnNativeAIConfigChanged(true);
                });
            }
            menu.ShowAsContext();
        }

        private void ShowTypeGroupMenu(Func<string> getValue, Action<string> setValue)
        {
            GenericMenu menu = new GenericMenu();
            foreach (AI.AssetGroup group in Enum.GetValues(typeof (AI.AssetGroup)).Cast<AI.AssetGroup>().OrderBy(g => g.ToString(), StringComparer.InvariantCultureIgnoreCase))
            {
                AI.AssetGroup selectedGroup = group;
                string token = "{" + selectedGroup.ToString().ToLowerInvariant() + "}";
                bool isSelected = StringUtils.Split(getValue?.Invoke(), new[] {';', ','})
                    .Any(t => string.Equals(t, token, StringComparison.OrdinalIgnoreCase));

                menu.AddItem(new GUIContent(selectedGroup.ToString()), isSelected, () =>
                {
                    string currentValue = getValue?.Invoke() ?? string.Empty;
                    setValue?.Invoke(ToggleTypeGroup(currentValue, selectedGroup));
                    AI.SaveConfig();
                    Repaint();
                });
            }
            menu.ShowAsContext();
        }

        private void ShowSuggestedOllamaModels()
        {
            GenericMenu menu = new GenericMenu();
            foreach (string model in Intelligence.SuggestedOllamaModels)
            {
                menu.AddItem(new GUIContent(model), false, () =>
                {
                    AI.Config.ollamaModel = model.Split(' ')[0];
                    OnNativeAIConfigChanged(true);
                });
            }
            menu.ShowAsContext();
        }

        private void ShowInstalledLMStudioModels()
        {
            IEnumerable<LMStudioModel> models = Intelligence.LMStudioModels;

            GenericMenu menu = new GenericMenu();
            if (models != null)
            {
                IEnumerable<LMStudioModel> visionModels = models.Where(m => m.SupportsVision);

                if (visionModels.Any())
                {
                    foreach (LMStudioModel model in visionModels.OrderBy(m => m.id, StringComparer.InvariantCultureIgnoreCase))
                    {
                        string stateText = !string.IsNullOrEmpty(model.state) ? $" ({model.state})" : "";
                        string typeText = !string.IsNullOrEmpty(model.type) ? $" [{model.type}]" : "";
                        menu.AddItem(new GUIContent($"{model.id}{typeText}{stateText}"), false, () =>
                        {
                            AI.Config.lmStudioModel = model.id;
                            OnNativeAIConfigChanged(true);
                        });
                    }
                    menu.AddItem(GUIContent.none, false, () => { });
                    menu.AddItem(new GUIContent("Refresh"), false, Intelligence.RefreshLMStudio);
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent("No vision models found"));
                    menu.AddItem(new GUIContent("Refresh"), false, Intelligence.RefreshLMStudio);
                }
            }
            else
            {
                if (Intelligence.LoadingLMStudioModels)
                {
                    menu.AddDisabledItem(new GUIContent("Loading models"));
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent("No models found"));
                    menu.AddItem(new GUIContent("Refresh"), false, Intelligence.RefreshLMStudio);
                }
            }
            menu.ShowAsContext();
        }

        private void ShowInstalledLMStudioEmbeddingModels()
        {
            IEnumerable<LMStudioModel> models = Intelligence.LMStudioModels;

            GenericMenu menu = new GenericMenu();
            if (models != null)
            {
                IEnumerable<LMStudioModel> embeddingModels = models.Where(m => m.IsEmbedding);

                if (embeddingModels.Any())
                {
                    foreach (LMStudioModel model in embeddingModels.OrderBy(m => m.id, StringComparer.InvariantCultureIgnoreCase))
                    {
                        string stateText = !string.IsNullOrEmpty(model.state) ? $" ({model.state})" : "";
                        string typeText = !string.IsNullOrEmpty(model.type) ? $" [{model.type}]" : "";
                        menu.AddItem(new GUIContent($"{model.id}{typeText}{stateText}"), false, () =>
                        {
                            AI.Config.semanticLmStudioEmbeddingModel = model.id;
                            OnNativeAIConfigChanged(true);
                        });
                    }
                    menu.AddItem(GUIContent.none, false, () => { });
                    menu.AddItem(new GUIContent("Refresh"), false, Intelligence.RefreshLMStudio);
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent("No embedding models found"));
                    menu.AddItem(new GUIContent("Refresh"), false, Intelligence.RefreshLMStudio);
                }
            }
            else
            {
                if (Intelligence.LoadingLMStudioModels)
                {
                    menu.AddDisabledItem(new GUIContent("Loading models"));
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent("No models found"));
                    menu.AddItem(new GUIContent("Refresh"), false, Intelligence.RefreshLMStudio);
                }
            }
            menu.ShowAsContext();
        }

        private void OnModelTesterPromptChanged(string prompt)
        {
            AI.Config.aiCustomPrompt = string.IsNullOrEmpty(prompt) ? null : prompt;
            AI.SaveConfig();
        }

        private async void TestCaptioning()
        {
            _captionTestRunning = true;
            _captionTest = "Running...";
            RefreshNativeAISettingsSection(true);
            string path = AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:Texture2D AssetInventory").FirstOrDefault());
            string absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
            string modelName = null;
            if (AI.Config.aiBackend == 1)
            {
                modelName = AI.Config.ollamaModel;
            }
            else if (AI.Config.aiBackend == 2)
            {
                modelName = AI.Config.lmStudioModel;
            }
            List<CaptionResult> captionResult = await CaptionCreator.CaptionImage(new List<string> {absolutePath}, modelName, AI.Actions.CancellationToken);
            _captionTest = captionResult?.FirstOrDefault()?.caption;
            if (string.IsNullOrWhiteSpace(_captionTest))
            {
                _captionTest = "-Failed to create caption. Check tooling.-";
            }
            else
            {
                _captionTest = $"\"{_captionTest}\"";
            }
            _captionTestRunning = false;
            RefreshNativeAISettingsSection(true);
        }

        private void OptimizeDatabase(bool initOnly = false)
        {
            if (!initOnly)
            {
                long savings = DBAdapter.Optimize();
                UpdateStatistics(true);
                EditorUtility.DisplayDialog("Success", $"Database was optimized. Size reduction: {EditorUtility.FormatBytes(savings)}\n\nMake sure to also delete your Library folder every now and then, especially after long indexing runs, to ensure Unity's asset database only contains what you really need for maximum performance.", "OK");
            }

            AppProperty lastOpt = new AppProperty("LastOptimization", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            DBAdapter.DB.InsertOrReplace(lastOpt);
        }

        private void SelectRelativeFolderMapping(RelativeLocation location)
        {
            string folder = EditorUtility.OpenFolderPanel("Select folder to map to", location.Location, "");
            if (!string.IsNullOrEmpty(folder))
            {
                location.SetLocation(Path.GetFullPath(folder));
                if (location.Id > 0)
                {
                    DBAdapter.DB.Execute("UPDATE RelativeLocation SET Location = ? WHERE Id = ?", location.Location, location.Id);
                }
                else
                {
                    DBAdapter.DB.Insert(location);
                }
                Paths.LoadRelativeLocations();
            }
        }

        private async void CalcFolderSizes()
        {
            if (_calculatingFolderSizes) return;
            _calculatingFolderSizes = true;
            _lastFolderSizeCalculation = DateTime.Now;

            _backupSize = await Paths.GetBackupFolderSize();
            _cacheSize = await Paths.GetCacheFolderSize();
            _persistedCacheSize = await Paths.GetPersistedCacheSize();
            _previewSize = await Paths.GetPreviewFolderSize();

            _calculatingFolderSizes = false;
        }

        private void PerformFullUpdate()
        {
            AI.Actions.RunActions();
        }

        private void SetDatabaseLocation()
        {
            string targetFolder = EditorUtility.OpenFolderPanel("Select folder for database and cache", Paths.GetStorageFolder(), "");
            if (string.IsNullOrEmpty(targetFolder)) return;

            // check if same folder selected
            if (IOUtils.IsSameDirectory(targetFolder, Paths.GetStorageFolder())) return;

            // disallow selecting a drive/root directory (e.g., C:\, D:\, E:, or /)
            if (IOUtils.IsRootPath(targetFolder))
            {
                EditorUtility.DisplayDialog("Invalid Folder", "Please select a subfolder, not a drive root.", "OK");
                return;
            }

            // check for existing database
            if (File.Exists(Path.Combine(targetFolder, DBAdapter.DB_NAME)))
            {
                if (EditorUtility.DisplayDialog("Use Existing?", "The target folder contains a database. Switch to this one? Otherwise please select an empty directory.", "Switch", "Cancel"))
                {
                    AI.SwitchDatabase(targetFolder);
                    ReloadLookups();
                    PerformSearch();
                }

                return;
            }

            if (EditorUtility.DisplayDialog("Keep Old Database", "Should a new database be created or the current one moved?", "New", "Move..."))
            {
                AI.SwitchDatabase(targetFolder);
                ReloadLookups();
                PerformSearch();
                AssetStore.GatherAllMetadata();
                AssetStore.GatherProjectMetadata();
                return;
            }

            // show dedicated UI since the process is more complex now
            DBLocationUI relocateUI = DBLocationUI.ShowWindow();
            relocateUI.Init(targetFolder);
        }

        private IEnumerator UpdateStatisticsDelayed()
        {
            yield return null;
            UpdateStatistics(false);
        }

        private void EnsureVisibleIndexStatsLoaded()
        {
            if (!ShouldRefreshIndexStats(_stats, AI.Actions.SemanticSearchEnabled, AI.Actions.CodeSearchEnabled)) return;

            _stats = Assets.GetInventoryStats();
        }

        private void UpdateStatistics(bool force)
        {
            if (!force && _assets != null && _tags != null && _dbSize > 0)
            {
                // check if assets were already correctly initialized since this method is also used for initial bootstrapping
                if (_assets.Any(a => a.PackageDownloader == null || (a.ParentId > 0 && a.ParentInfo == null)))
                {
                    Assets.InitAssets(_assets);
                }
                if (ShouldRefreshIndexStats(_stats, AI.Actions.SemanticSearchEnabled, AI.Actions.CodeSearchEnabled))
                {
                    _stats = Assets.GetInventoryStats();
                }
                return;
            }

            if (AI.DEBUG_MODE) Debug.LogWarning("Update Statistics");
            if (Application.isPlaying) return;

            _assets = Assets.Load();
            _tags = Tagging.LoadTags();

            _stats = Assets.GetInventoryStats(_assets);

            if (AI.Config.tab == 3)
            {
                _dbSize = DBAdapter.GetDBSize();
            }
        }
    }
}
