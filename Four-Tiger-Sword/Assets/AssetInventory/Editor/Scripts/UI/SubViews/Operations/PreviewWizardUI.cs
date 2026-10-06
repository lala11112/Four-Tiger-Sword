using ImpossibleRobert.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed class PreviewWizardUI : EditorWindow
    {
        private const string BASE_JOIN = "inner join Asset on Asset.Id = AssetFile.AssetId left join Asset ParentAsset on ParentAsset.Id = Asset.ParentId where Asset.Exclude = 0 and Asset.NoIndex = 0 and (ParentAsset.NoIndex is null or ParentAsset.NoIndex = 0)";
        private const string PreviewOverviewText = "When indexing Unity packages, preview images are typically bundled with them. These are often good but not always. This can result in empty previews, pink images, dark images and more. Colors and lighting will also differ between Unity versions where the previews were initially created. Audio files will for example have different shades of yellow. Bundled preview images are limited to 128 by 128 pixels.\n\nAsset Inventory can easily recreate preview images and offers advanced options like creating bigger previews.";
        private static readonly Vector2 MinWindowSize = new Vector2(540f, 300f);
        private static readonly Vector2 MaxWindowSize = new Vector2(540f, 1500f);

        [Serializable]
        private class TypeCount
        {
            public int Count { get; set; }
            public string Type { get; set; }
        }

        private List<AssetInfo> _assets;
        private List<AssetInfo> _allAssets;
        private int _totalFiles;
        private int _providedFiles;
        private int _originalFiles;
        private int _recreatedFiles;
        private int _erroneousFiles;
        private int _missingFiles;
        private int _noPrevFiles;
        private int _scheduledFiles;
        private int _imageFiles;
        private bool _showAdv;
        private bool _showPreviewOverview;
        private bool _showTypeBreakdown;
        private List<TypeCount> _typeBreakdown;
        private PreviewPipeline _previewPipeline;
        private IVisualElementScheduledItem _statusUpdate;
        private ProgressBar _runningProgress;
        private readonly IncorrectPreviewsValidator _validator = new IncorrectPreviewsValidator();

        public static PreviewWizardUI ShowWindow()
        {
            PreviewWizardUI window = GetWindow<PreviewWizardUI>("Previews Wizard");
            window.ApplyWindowConstraints();

            return window;
        }

        private void OnEnable()
        {
            ApplyWindowConstraints();
            if (_allAssets == null || _allAssets.Count == 0) _allAssets = Assets.Load();
        }

        private void OnDisable()
        {
            StopStatusRefresh();
        }

        public void Init(List<AssetInfo> assets = null, List<AssetInfo> allAssets = null)
        {
            _assets = assets;
            _allAssets = allAssets;

            GeneratePreviewOverview();
        }

        private void ApplyWindowConstraints()
        {
            minSize = MinWindowSize;
            maxSize = MaxWindowSize;
        }

        private void GeneratePreviewOverview()
        {
            string assetFilter = PreviewPipeline.GetAssetFilter(_assets);
            string countQuery = "select count(*) from AssetFile";

            _totalFiles = DBAdapter.DB.ExecuteScalar<int>($"{countQuery} {BASE_JOIN} {assetFilter}");
            _imageFiles = DBAdapter.DB.ExecuteScalar<int>($"{countQuery} {BASE_JOIN} {assetFilter} and AssetFile.Type in ('" + string.Join("','", AI.TypeGroups[AI.AssetGroup.Images]) + "')");
            _providedFiles = DBAdapter.DB.ExecuteScalar<int>($"{countQuery} {BASE_JOIN} and AssetFile.PreviewState = ? {assetFilter}", AssetFile.PreviewOptions.Provided);
            _originalFiles = DBAdapter.DB.ExecuteScalar<int>($"{countQuery} {BASE_JOIN} and AssetFile.PreviewState = ? {assetFilter}", AssetFile.PreviewOptions.UseOriginal);
            _recreatedFiles = DBAdapter.DB.ExecuteScalar<int>($"{countQuery} {BASE_JOIN} and AssetFile.PreviewState = ? {assetFilter}", AssetFile.PreviewOptions.Custom);
            _erroneousFiles = DBAdapter.DB.ExecuteScalar<int>($"{countQuery} {BASE_JOIN} and AssetFile.PreviewState = ? {assetFilter}", AssetFile.PreviewOptions.Error);
            _missingFiles = DBAdapter.DB.ExecuteScalar<int>($"{countQuery} {BASE_JOIN} and AssetFile.PreviewState = ? {assetFilter}", AssetFile.PreviewOptions.None);
            _noPrevFiles = DBAdapter.DB.ExecuteScalar<int>($"{countQuery} {BASE_JOIN} and AssetFile.PreviewState = ? {assetFilter}", AssetFile.PreviewOptions.NotApplicable);
            _scheduledFiles = CountScheduledFiles(assetFilter);

            // Get type breakdown for scheduled files
            string typeBreakdownQuery = $"select count(*) as Count, AssetFile.Type as Type from AssetFile {BASE_JOIN} and AssetFile.PreviewState in (?, ?) {assetFilter} group by AssetFile.Type";
            _typeBreakdown = DBAdapter.DB.Query<TypeCount>(typeBreakdownQuery, AssetFile.PreviewOptions.Redo, AssetFile.PreviewOptions.RedoMissing).ToList();

            BuildIfReady();
        }

        private static string GetEligibleAssetSubquery(string assetFilter = "")
        {
            return $"select Asset.Id from Asset left join Asset ParentAsset on ParentAsset.Id = Asset.ParentId where Asset.Exclude = 0 and Asset.NoIndex = 0 and (ParentAsset.NoIndex is null or ParentAsset.NoIndex = 0) {assetFilter}";
        }

        private static int CountScheduledFiles(string assetFilter)
        {
            const string countQuery = "select count(*) from AssetFile";
            if (string.IsNullOrEmpty(assetFilter))
            {
                return DBAdapter.DB.ExecuteScalar<int>($"{countQuery} {BASE_JOIN} and AssetFile.PreviewState = ?", AssetFile.PreviewOptions.Redo)
                    + DBAdapter.DB.ExecuteScalar<int>($"{countQuery} {BASE_JOIN} and AssetFile.PreviewState = ?", AssetFile.PreviewOptions.RedoMissing);
            }

            return DBAdapter.DB.ExecuteScalar<int>($"{countQuery} {BASE_JOIN} and AssetFile.PreviewState in (?, ?) {assetFilter}", AssetFile.PreviewOptions.Redo, AssetFile.PreviewOptions.RedoMissing);
        }

        private void Schedule(AssetFile.PreviewOptions state)
        {
            string assetFilter = PreviewPipeline.GetAssetFilter(_assets);
            // Use subquery syntax compatible with both SQLite and MySQL
            string query = $"update AssetFile set PreviewState = ? where PreviewState = ? and AssetId in ({GetEligibleAssetSubquery(assetFilter)})";
            DBAdapter.DB.Execute(query, (state == AssetFile.PreviewOptions.Custom || state == AssetFile.PreviewOptions.Provided || state == AssetFile.PreviewOptions.Redo) ? AssetFile.PreviewOptions.Redo : AssetFile.PreviewOptions.RedoMissing, state);

            GeneratePreviewOverview();
        }

        private void Schedule(string queryExt = "")
        {
            string assetFilter = PreviewPipeline.GetAssetFilter(_assets);

            // Use subquery syntax compatible with both SQLite and MySQL
            string query = $"update AssetFile set PreviewState = ? where PreviewState in (1,2,3) and AssetId in ({GetEligibleAssetSubquery(assetFilter)}) {queryExt}";
            DBAdapter.DB.Execute(query, AssetFile.PreviewOptions.Redo);

            query = $"update AssetFile set PreviewState = ? where PreviewState not in (1,2,3) and AssetId in ({GetEligibleAssetSubquery(assetFilter)}) {queryExt}";
            DBAdapter.DB.Execute(query, AssetFile.PreviewOptions.RedoMissing);

            GeneratePreviewOverview();
        }

        private void ScheduleByType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return;

            string assetFilter = PreviewPipeline.GetAssetFilter(_assets);

            string query = $"update AssetFile set PreviewState = ? where PreviewState in (1,2,3) and Type = ? and AssetId in ({GetEligibleAssetSubquery(assetFilter)})";
            DBAdapter.DB.Execute(query, AssetFile.PreviewOptions.Redo, type);

            query = $"update AssetFile set PreviewState = ? where PreviewState not in (1,2,3) and Type = ? and AssetId in ({GetEligibleAssetSubquery(assetFilter)})";
            DBAdapter.DB.Execute(query, AssetFile.PreviewOptions.RedoMissing, type);

            GeneratePreviewOverview();
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
            _runningProgress = null;

            root.Add(CreatePreviewHelp());

            ScrollView scroll = new ScrollView();
            scroll.AddToClassList("ai-preview-wizard-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            scroll.Add(CreateSelectionSection());
            scroll.Add(CreateOverviewSection());
            scroll.Add(CreateTypeBreakdownSection());
            scroll.Add(CreateAdvancedSection());
            root.Add(scroll);

            root.Add(BuildFooter());
            UpdateStatusRefresh();
        }

        private VisualElement CreatePreviewHelp()
        {
            VisualElement help = AssetInventoryUITK.CreateHelpBox("Recreate missing or incorrect previews.", MessageType.None);
            help.AddToClassList("ai-preview-help-box");

            Label summary = help.Q<Label>();
            if (summary == null) return help;

            summary.RemoveFromHierarchy();

            VisualElement content = new VisualElement();
            content.AddToClassList("ai-preview-help-content");

            VisualElement summaryRow = new VisualElement();
            summaryRow.AddToClassList("ai-preview-help-summary");
            summaryRow.Add(summary);

            Label details = new Label(PreviewOverviewText);
            details.AddToClassList("ai-preview-help-details");
            details.style.display = _showPreviewOverview ? DisplayStyle.Flex : DisplayStyle.None;

            Button toggle = AssetInventoryUITK.CreateButton(_showPreviewOverview ? "Hide details" : "Why recreate previews?", null);
            toggle.AddToClassList("ai-link-button");
            toggle.AddToClassList("ai-preview-help-details-toggle");
            toggle.tooltip = _showPreviewOverview
                ? "Hide the preview recreation explanation."
                : "Explain why recreating previews can improve their consistency and quality.";
            toggle.clicked += () =>
            {
                _showPreviewOverview = !_showPreviewOverview;
                details.style.display = _showPreviewOverview ? DisplayStyle.Flex : DisplayStyle.None;
                toggle.text = _showPreviewOverview ? "Hide details" : "Why recreate previews?";
                toggle.tooltip = _showPreviewOverview
                    ? "Hide the preview recreation explanation."
                    : "Explain why recreating previews can improve their consistency and quality.";
            };
            summaryRow.Add(toggle);

            content.Add(summaryRow);
            content.Add(details);
            help.Add(content);

            return help;
        }

        private VisualElement CreateSelectionSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Current Selection");

            VisualElement selected = new VisualElement();
            selected.AddToClassList("ai-inline-control-row");

            Label value = AssetInventoryUITK.CreateCopyLabel(GetSelectionText());
            value.AddToClassList("ai-inline-grow");
            selected.Add(value);

            if (_assets != null && _assets.Count > 0)
            {
                Button clear = AssetInventoryUITK.CreateIconButton("Clear selection", "TreeEditor.Trash", () =>
                {
                    _assets = null;
                    GeneratePreviewOverview();
                });
                clear.AddToClassList("ai-preview-clear-selection-button");
                selected.Add(clear);
            }

            section.Add(AssetInventoryUITK.CreateFieldRow("Packages", selected));
            return section;
        }

        private string GetSelectionText()
        {
            if (_assets == null || _assets.Count == 0) return "-Full Database-";
            return _assets.Count == 1 ? $"{_assets.Count:N0} ({_assets[0].GetDisplayName()})" : $"{_assets.Count:N0}";
        }

        private VisualElement CreateOverviewSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Preview Overview");

            Button totalSchedule = AssetInventoryUITK.CreateSecondaryButton("Schedule Recreation", ScheduleAllPreviews);
            totalSchedule.SetEnabled(_totalFiles > 0);
            totalSchedule.tooltip = _totalFiles > 0
                ? $"Schedule preview recreation for all {_totalFiles:N0} files."
                : "No files are available to schedule.";
            section.Add(CreateOverviewRow("Total Files", _totalFiles, null, totalSchedule));

            section.Add(CreatePreviewStateRow("Pre-Provided", _providedFiles, AssetFile.PreviewOptions.Provided, "Preview images that were provided with the package."));
            section.Add(CreatePreviewStateRow("Recreated", _recreatedFiles, AssetFile.PreviewOptions.Custom, "Preview images that were recreated by Asset Inventory."));
            section.Add(CreatePreviewStateRow("Missing", _missingFiles, AssetFile.PreviewOptions.None, "Files that do not have a preview image yet but should have one."));
            section.Add(CreatePreviewStateRow("Erroneous", _erroneousFiles, AssetFile.PreviewOptions.Error, "Preview images where a previous recreation attempt failed."));
            section.Add(CreatePreviewStateRow("Not Applicable", _noPrevFiles, AssetFile.PreviewOptions.NotApplicable, "Files for which typically no previews are created, e.g. documents, scripts, controllers. Only a generic icon will be shown.", advancedOnly: true));
            section.Add(CreatePreviewStateRow("Using Original", _originalFiles, AssetFile.PreviewOptions.UseOriginal, "Image files that are used directly as previews since they are small and don't need recreation.", advancedOnly: true, additionalDisableCondition: AI.Config.directMediaPreviews));

            Button imageSchedule = AssetInventoryUITK.CreateSecondaryButton("Schedule Recreation", () =>
            {
                Schedule("and AssetFile.Type in ('" + string.Join("','", AI.TypeGroups[AI.AssetGroup.Images]) + "')");
            });
            imageSchedule.SetEnabled(_imageFiles > 0);
            imageSchedule.tooltip = _imageFiles > 0
                ? $"Schedule preview recreation for all {_imageFiles:N0} image files."
                : "No image files are available to schedule.";
            section.Add(CreateOverviewRow("Image Files", _imageFiles, null, imageSchedule));

            Label scheduled = AssetInventoryUITK.CreateStatusPill($"{_scheduledFiles:N0}");
            scheduled.AddToClassList(_scheduledFiles > 0 ? "ai-status-progress" : "ai-status-muted");
            scheduled.AddToClassList("ai-preview-scheduled-count");
            section.Add(CreateOverviewRow("Scheduled", string.Empty, null, scheduled));

            int rowIndex = 0;
            foreach (VisualElement row in section.Children())
            {
                if (row.ClassListContains("ai-preview-overview-row")) CommonUITK.ApplyListRowStyle(row, rowIndex++);
            }
            return section;
        }

        private VisualElement CreatePreviewStateRow(string label, int count, AssetFile.PreviewOptions previewState, string tooltip, bool advancedOnly = false, bool additionalDisableCondition = false)
        {
            bool showActions = !advancedOnly || ShouldShowAdvanced();
            VisualElement actions = new VisualElement();
            actions.AddToClassList("ai-preview-row-actions");

            if (showActions)
            {
                Button search = AssetInventoryUITK.CreateIconButton("Show in Search", "Search Icon", () => OpenSearchWithFilter(previewState));
                actions.Add(search);

                Button schedule = AssetInventoryUITK.CreateSecondaryButton("Schedule Recreation", () => SchedulePreviewState(previewState, count));
                schedule.SetEnabled(count > 0 && !additionalDisableCondition);
                schedule.tooltip = additionalDisableCondition
                    ? "This action is unavailable while direct media previews are enabled."
                    : count > 0
                        ? $"Schedule preview recreation for these {count:N0} files."
                        : "No files in this state are available to schedule.";
                actions.Add(schedule);
            }

            return CreateOverviewRow(label, count, tooltip, showActions ? actions : null, true);
        }

        private VisualElement CreateOverviewRow(string label, int count, string tooltip, VisualElement side, bool indented = false)
        {
            return CreateOverviewRow(label, $"{count:N0}", tooltip, side, indented);
        }

        private VisualElement CreateOverviewRow(string label, string countText, string tooltip, VisualElement side, bool indented = false)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("ai-preview-overview-row");
            CommonUITK.ApplyListRowStyle(row);
            if (indented) row.AddToClassList("ai-preview-overview-row-indented");
            row.tooltip = tooltip ?? string.Empty;

            VisualElement body = new VisualElement();
            body.AddToClassList("ai-preview-row-body");

            Label title = AssetInventoryUITK.CreateCopyLabel(label);
            title.AddToClassList("ai-preview-row-title");
            body.Add(title);

            if (!string.IsNullOrEmpty(countText))
            {
                Label value = AssetInventoryUITK.CreateCopyLabel(countText);
                value.AddToClassList("ai-preview-row-count");
                body.Add(value);
            }

            row.Add(body);
            if (side != null)
            {
                side.AddToClassList("ai-preview-row-side");
                row.Add(side);
            }

            return row;
        }

        private VisualElement CreateTypeBreakdownSection()
        {
            Foldout foldout = AssetInventoryUITK.CreateFoldout(
                "Scheduled by Type",
                _showTypeBreakdown,
                value => _showTypeBreakdown = value,
                "Show how many queued previews belong to each file type.",
                "ai-preview-foldout");

            if (_typeBreakdown != null && _typeBreakdown.Count > 0)
            {
                VisualElement list = new VisualElement();
                list.AddToClassList("ai-list");
                list.AddToClassList("ai-preview-type-list");

                int rowIndex = 0;
                foreach (TypeCount item in _typeBreakdown.OrderByDescending(t => t.Count))
                {
                    list.Add(CreateTypeBreakdownRow(item, rowIndex));
                    rowIndex++;
                }

                foldout.Add(list);
            }
            else
            {
                foldout.Add(AssetInventoryUITK.CreateHelpBox("No files scheduled for recreation.", MessageType.None));
            }

            VisualElement actions = new VisualElement();
            actions.AddToClassList("ai-preview-action-row");

            Button clean = AssetInventoryUITK.CreateSecondaryButton("Clean Queue", () => CleanQueue());
            clean.tooltip = "Will remove accidentally scheduled items for which no preview can be created (e.g. cs files).";
            clean.SetEnabled(!AI.Actions.ActionsInProgress);
            if (AI.Actions.ActionsInProgress) clean.tooltip = "Wait for the current Asset Inventory action to finish.";
            actions.Add(clean);

            Button scheduleByType = null;
            scheduleByType = AssetInventoryUITK.CreateSecondaryButton("Schedule by Type...", () => ShowScheduleByTypePopup(scheduleByType));
            scheduleByType.tooltip = "Schedule all files of a specific type/extension for preview recreation.";
            scheduleByType.SetEnabled(!AI.Actions.ActionsInProgress);
            if (AI.Actions.ActionsInProgress) scheduleByType.tooltip = "Wait for the current Asset Inventory action to finish.";
            actions.Add(scheduleByType);

            foldout.Add(actions);
            return foldout;
        }

        private VisualElement CreateTypeBreakdownRow(TypeCount item, int rowIndex)
        {
            VisualElement row = new VisualElement();
            VisualElement actions = new VisualElement();
            actions.AddToClassList("ai-list-actions");
            Button remove = AssetInventoryUITK.CreateIconButton("Remove this type from queue", "TreeEditor.Trash", () => ClearQueue(item.Type));
            actions.Add(remove);

            AssetInventoryUITK.PopulateListRow(
                row,
                string.IsNullOrWhiteSpace(item.Type) ? "-Unknown-" : item.Type,
                $"{item.Count:N0}",
                trailing: actions,
                extraClasses: rowIndex % 2 == 1
                    ? new[] {"ai-preview-type-row", BaseVerticalCollectionView.itemAlternativeBackgroundUssClassName}
                    : new[] {"ai-preview-type-row"});

            return row;
        }

        private VisualElement CreateAdvancedSection()
        {
            Foldout foldout = AssetInventoryUITK.CreateFoldout(
                "Advanced",
                _showAdv,
                value => _showAdv = value,
                "Show queue cleanup and preview recovery actions.",
                "ai-preview-foldout");

            VisualElement actions = new VisualElement();
            actions.AddToClassList("ai-preview-action-row");

            Button showFolder = AssetInventoryUITK.CreateSecondaryButton("Show Preview Folder", ShowPreviewFolder);
            actions.Add(showFolder);

            Button restore = AssetInventoryUITK.CreateSecondaryButton("Revert to Provided", RestorePreviews);
            restore.tooltip = "Will replace existing recreated previews with those provided originally within the packages.";
            restore.SetEnabled(!AI.Actions.ActionsInProgress);
            if (AI.Actions.ActionsInProgress) restore.tooltip = "Wait for the current Asset Inventory action to finish.";
            actions.Add(restore);

            Button clear = AssetInventoryUITK.CreateSecondaryButton("Clear Queue", () => ClearQueue());
            clear.tooltip = "Will remove the scheduled items from the queue again.";
            clear.SetEnabled(!AI.Actions.ActionsInProgress);
            if (AI.Actions.ActionsInProgress) clear.tooltip = "Wait for the current Asset Inventory action to finish.";
            actions.Add(clear);

            foldout.Add(actions);
            return foldout;
        }

        private VisualElement BuildFooter()
        {
            VisualElement footer = AssetInventoryUITK.CreateWindowFooter();
            footer.AddToClassList("ai-preview-footer");

            if (_validator.IsRunning)
            {
                footer.Add(CreateRunningProgressRow("Cancel", () => _validator.CancellationRequested = true));
                return footer;
            }

            if (IsPreviewPipelineRunning())
            {
                Button stop = null;
                footer.Add(CreateRunningProgressRow("Stop", () =>
                {
                    if (_previewPipeline != null) _previewPipeline.CancellationRequested = true;
                    stop?.SetEnabled(false);
                }, button => stop = button));
                return footer;
            }

            Button recreate = AssetInventoryUITK.CreatePrimaryButton($"Recreate {_scheduledFiles:N0} Scheduled", () => _ = RecreatePreviews());
            recreate.SetEnabled(_scheduledFiles > 0);
            recreate.tooltip = _scheduledFiles > 0
                ? $"Recreate previews for the {_scheduledFiles:N0} scheduled files."
                : "Schedule files for preview recreation first.";
            recreate.AddToClassList("ai-preview-footer-primary");
            footer.Add(recreate);

            Button verify = AssetInventoryUITK.CreateSecondaryButton("Verify", InspectPreviews);
            verify.tooltip = "Inspect all preview images and check for issues like containing Unity default placeholders or shader errors.";
            footer.Add(verify);

            Button refresh = AssetInventoryUITK.CreateSecondaryButton("Refresh", GeneratePreviewOverview);
            refresh.tooltip = "Recalculate the preview overview.";
            footer.Add(refresh);
            return footer;
        }

        private VisualElement CreateRunningProgressRow(string buttonText, Action click, Action<Button> configureButton = null)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("ai-progress-row");
            row.AddToClassList("ai-preview-progress-row");

            string progressTitle = GetRunningProgressTitle();
            _runningProgress = AssetInventoryUITK.CreateProgressBar(progressTitle, GetRunningProgressValue());
            _runningProgress.tooltip = progressTitle;
            row.Add(_runningProgress);

            Button button = AssetInventoryUITK.CreateSecondaryButton(buttonText, click);
            configureButton?.Invoke(button);
            row.Add(button);

            return row;
        }

        private void ScheduleAllPreviews()
        {
            if (!AI.Config.confirmPreviewRescheduling || EditorUtility.DisplayDialog("Confirm", $"Are you sure you want to schedule recreation for all {_totalFiles:N0} files? This will replace all existing previews.", "Continue", "Cancel"))
            {
                Schedule();
            }
        }

        private void SchedulePreviewState(AssetFile.PreviewOptions previewState, int count)
        {
            bool shouldSchedule = true;

            if (previewState == AssetFile.PreviewOptions.Error)
            {
                shouldSchedule = !AI.Config.confirmPreviewRescheduling || EditorUtility.DisplayDialog("Confirm", $"Are you sure you want to schedule recreation for {count:N0} erroneous files? These files had previous recreation errors, probably due to shader errors.", "Continue", "Cancel");
            }
            else if (previewState == AssetFile.PreviewOptions.NotApplicable)
            {
                shouldSchedule = !AI.Config.confirmPreviewRescheduling || EditorUtility.DisplayDialog("Confirm", $"Are you sure you want to schedule recreation for {count:N0} files marked as not applicable? These files typically don't have previews (e.g., scripts, documents).", "Continue", "Cancel");
            }

            if (shouldSchedule)
            {
                Schedule(previewState);
            }
        }

        private void ShowScheduleByTypePopup(Button anchor)
        {
            NameWindow.ShowAsDropDown(CommonUITK.ToScreenDropdownAnchor(this, anchor), string.Empty, ScheduleByType, false, "Extension (e.g. prefab)");
        }

        private void ShowPreviewFolder()
        {
            string path = Paths.GetPreviewFolder();
            if (_assets != null && _assets.Count == 1)
            {
                path = IOUtils.ToShortPath(_assets[0].GetPreviewFolder(Paths.GetPreviewFolder()));
            }
            EditorUtility.RevealInFinder(path);
        }

        private void CleanQueue()
        {
            List<string> types = new List<string>();
            types.AddRange(AI.TypeGroups[AI.AssetGroup.Audio]);
            types.AddRange(AI.TypeGroups[AI.AssetGroup.Fonts]);
            types.AddRange(AI.TypeGroups[AI.AssetGroup.Images]);
            types.AddRange(AI.TypeGroups[AI.AssetGroup.Materials]);
            types.AddRange(AI.TypeGroups[AI.AssetGroup.Models]);
            types.AddRange(AI.TypeGroups[AI.AssetGroup.Prefabs]);
            types.AddRange(AI.TypeGroups[AI.AssetGroup.Videos]);
            if (AI.Config.generateAnimPreviews) types.AddRange(AI.TypeGroups[AI.AssetGroup.Animations]);
            string previewTypes = "'" + string.Join("','", types) + "'";

            string assetFilter = PreviewPipeline.GetAssetFilter(_assets, "AssetId");
            string query = $@"
                UPDATE AssetFile
                SET PreviewState = ?
                WHERE 
                  (PreviewState = ? or PreviewState = ?)
                  AND (
                      Type NOT IN ({previewTypes})
                  )
                  AND AssetId IN (
                      {GetEligibleAssetSubquery()}
                  )
                  {assetFilter};
                ";
            DBAdapter.DB.Execute(query, AssetFile.PreviewOptions.NotApplicable, AssetFile.PreviewOptions.Redo, AssetFile.PreviewOptions.RedoMissing);

            GeneratePreviewOverview();
        }

        private void ClearQueue(string type = null)
        {
            string confirmMessage = type != null
                ? $"Are you sure you want to remove all '{type}' files from the preview recreation queue?"
                : "Are you sure you want to clear the preview recreation queue? The previous state of items is not always known (except for missing). This might result in items being marked as recreated instead of pre-provided. That is usually not an issue though.";

            if (AI.Config.confirmPreviewRescheduling && !EditorUtility.DisplayDialog("Confirm", confirmMessage, "Continue", "Cancel")) return;

            string assetFilter = PreviewPipeline.GetAssetFilter(_assets, "AssetId");
            string typeFilter = type != null ? "AND Type = ?" : "";

            string query = $@"
                UPDATE AssetFile
                SET PreviewState = ?
                WHERE 
                  PreviewState = ?
                  {typeFilter}
                  AND AssetId IN (
                      {GetEligibleAssetSubquery()}
                  )
                  {assetFilter};
                ";

            if (type != null)
            {
                DBAdapter.DB.Execute(query, AssetFile.PreviewOptions.None, AssetFile.PreviewOptions.RedoMissing, type);
                DBAdapter.DB.Execute(query, AssetFile.PreviewOptions.Custom, AssetFile.PreviewOptions.Redo, type);
            }
            else
            {
                DBAdapter.DB.Execute(query, AssetFile.PreviewOptions.None, AssetFile.PreviewOptions.RedoMissing);
                DBAdapter.DB.Execute(query, AssetFile.PreviewOptions.Custom, AssetFile.PreviewOptions.Redo);
            }

            GeneratePreviewOverview();
        }

        private async void InspectPreviews()
        {
            if (_validator.CurrentState == Validator.State.Scanning || _validator.CurrentState == Validator.State.Fixing) return;

            string assetFilter = PreviewPipeline.GetAssetFilter(_assets);
            string query = $"select * from AssetFile where (PreviewState = ? or PreviewState = ?) and AssetId in ({GetEligibleAssetSubquery(assetFilter)})";
            List<AssetInfo> files = DBAdapter.DB.Query<AssetInfo>(query, AssetFile.PreviewOptions.Provided, AssetFile.PreviewOptions.Custom).ToList();

            _validator.CancellationRequested = false;
            BuildIfReady();
            await _validator.Validate(files);
            BuildIfReady();
            if (_validator.DBIssues.Count > 0)
            {
                int defaultCount = _validator.DBIssues.Count(f => f.URPCompatible);
                int errorCount = _validator.DBIssues.Count(f => !f.URPCompatible);
                string message = $"Found {_validator.DBIssues.Count:N0} issues with preview images.\n\nDefault previews: {defaultCount:N0} (Mark for recreation)\nShader errors: {errorCount:N0} (Mark as error)\n\nDo you want to proceed?";
                if (EditorUtility.DisplayDialog("Preview Issues Found", message, "Yes", "No"))
                {
                    await _validator.Fix();
                    AI.TriggerPackageRefresh();
                    GeneratePreviewOverview();
                }
            }
            else
            {
                string msg = "All preview images appear correct.";
                if (_scheduledFiles > 0) msg += $" {_scheduledFiles:N0} files already scheduled for recreation.";
                EditorUtility.DisplayDialog("No Issues Found", msg, "OK");
            }
        }

        private async void RestorePreviews()
        {
            int restored = 0;
            try
            {
                BuildIfReady();
                await AI.Actions.RunWithProgress<PreviewPipeline>(
                    ActionHandler.ACTION_PREVIEWS_RESTORE,
                    "Restoring previews",
                    async imp =>
                    {
                        _previewPipeline = imp;
                        BuildIfReady();
                        restored = await imp.RestorePreviews(_assets, _allAssets);
                    });

                Debug.Log($"Previews restored: {restored}");

                AI.TriggerPackageRefresh();
                GeneratePreviewOverview();
            }
            catch (Exception e)
            {
                Debug.LogError($"Preview restore failed: {e.Message}");
                BuildIfReady();
            }
        }

        private async Task RecreatePreviews()
        {
            CleanQueue();

            int created = 0;
            await AI.Actions.RunWithProgress<PreviewPipeline>(
                ActionHandler.ACTION_PREVIEWS_RECREATE,
                "Recreating previews",
                async imp =>
                {
                    _previewPipeline = imp;
                    BuildIfReady();
                    created = await imp.RecreateScheduledPreviews(_assets, _allAssets, _assets == null || _assets.Count == 1);
                });

            Debug.Log($"Preview recreation done: {created} created.");

            AI.TriggerPackageRefresh();
            GeneratePreviewOverview();
        }

        private void OnInspectorUpdate()
        {
            if (IsOperationRunning())
            {
                RefreshRunningStatus();
            }
        }

        private void OpenSearchWithFilter(AssetFile.PreviewOptions previewState)
        {
            // Get the IndexUI window (assuming it's already open)
            IndexUI indexWindow = GetWindow<IndexUI>(null, false);
            if (indexWindow == null) return;

            // Build search phrase based on preview state
            string searchPhrase = $"=AssetFile.PreviewState={(int)previewState}";

            // If _assets is null or empty, pass null to search all packages
            // Otherwise, pass the first asset to filter by that package
            AssetInfo filterAsset = (_assets != null && _assets.Count > 0) ? _assets[0] : null;

            indexWindow.OpenInSearch(filterAsset, force: true, showFilterTab: true, searchPhrase: searchPhrase);
            indexWindow.Focus();
        }

        private bool IsOperationRunning()
        {
            return _validator.IsRunning || IsPreviewPipelineRunning();
        }

        private bool IsPreviewPipelineRunning()
        {
            return _previewPipeline != null && _previewPipeline.IsRunning();
        }

        private void UpdateStatusRefresh()
        {
            if (IsOperationRunning())
            {
                StartStatusRefresh();
            }
            else
            {
                StopStatusRefresh();
            }
        }

        private void StartStatusRefresh()
        {
            if (_statusUpdate != null) return;
            _statusUpdate = rootVisualElement.schedule.Execute(RefreshRunningStatus).Every(250);
            RefreshRunningStatus();
        }

        private void StopStatusRefresh()
        {
            _statusUpdate?.Pause();
            _statusUpdate = null;
        }

        private void RefreshRunningStatus()
        {
            if (!IsOperationRunning())
            {
                StopStatusRefresh();
                GeneratePreviewOverview();
                return;
            }

            if (_runningProgress == null)
            {
                BuildIfReady();
                return;
            }

            string progressTitle = GetRunningProgressTitle();
            _runningProgress.title = progressTitle;
            _runningProgress.tooltip = progressTitle;
            _runningProgress.value = GetRunningProgressValue();
        }

        private string GetRunningProgressTitle()
        {
            if (_validator.IsRunning)
            {
                return $"Progress: {_validator.Progress}/{Math.Max(1, _validator.MaxProgress)}";
            }

            if (IsPreviewPipelineRunning())
            {
                if (_assets == null || _assets.Count > 1)
                {
                    return $"Progress: {_previewPipeline.MainProgress}/{Math.Max(1, _previewPipeline.MainCount)} packages - {_previewPipeline.CurrentSub}";
                }

                return $"Progress: {_previewPipeline.SubProgress}/{Math.Max(1, _previewPipeline.SubCount)} - {_previewPipeline.CurrentSub}";
            }

            return string.Empty;
        }

        private float GetRunningProgressValue()
        {
            if (_validator.IsRunning) return GetSafeProgress(_validator.Progress, _validator.MaxProgress);
            if (IsPreviewPipelineRunning()) return GetSafeProgress(_previewPipeline.SubProgress, _previewPipeline.SubCount);
            return 0f;
        }

        private static float GetSafeProgress(int progress, int count)
        {
            if (count <= 0) return 0f;
            return Mathf.Clamp01(progress / (float)count);
        }

        private static bool ShouldShowAdvanced()
        {
            return AI.ShowAdvanced();
        }
    }
}
