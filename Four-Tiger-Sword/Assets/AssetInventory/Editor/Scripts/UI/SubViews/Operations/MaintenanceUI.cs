using System;
using System.Collections.Generic;
using System.Linq;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public sealed partial class MaintenanceUI : EditorWindow
    {
        public static event Action OnMaintenanceDone;

        private readonly List<Validator> _validators = new List<Validator>();
        private readonly List<Validator> _visibleValidators = new List<Validator>();
        private int _fixableItems;
        private string _validatorSearch = string.Empty;
        private ListView _validatorList;
        private Label _summaryLabel;
        private Button _fixAllButton;
        private VisualElement _emptyState;
        private IVisualElementScheduledItem _refreshSchedule;

        public MaintenanceUI()
        {
            Init();
        }

        public static MaintenanceUI ShowWindow()
        {
            MaintenanceUI window = GetWindow<MaintenanceUI>("Maintenance Wizard");
            window.minSize = new Vector2(640, 420);

            return window;
        }

        private void Init()
        {
            _validators.Clear();
            _validators.Add(new ScheduledPreviewRecreationValidator());
            _validators.Add(new ChangedTextMeshProMaterialsValidator());
            _validators.Add(new SubPackageRenderPipelineValidator());
            _validators.Add(new OutdatedPackagesValidator());
            _validators.Add(new UseOriginalPreviewValidator());
            _validators.Add(new EmbedOriginalPreviewValidator());
            _validators.Add(new IncorrectPreviewsValidator());
            _validators.Add(new MissingPreviewFilesValidator());
            _validators.Add(new HiddenFilePreviewsValidator());
            _validators.Add(new OrphanedTagAssignmentsValidator());
            _validators.Add(new DeletedAssetFilesValidator());
            _validators.Add(new OrphanedAssetFilesValidator());
            _validators.Add(new OrphanedPackagesValidator());
            _validators.Add(new OrphanedDirectoryPackagesValidator());
            _validators.Add(new OrphanedCacheFoldersValidator());
            _validators.Add(new OrphanedPreviewFoldersValidator());
            _validators.Add(new OrphanedPreviewFilesValidator());
            _validators.Add(new WrongDimensionPreviewFilesValidator());
            _validators.Add(new MissingAudioLengthValidator());
            _validators.Add(new MissingParentPackagesValidator());
            _validators.Add(new UnindexedSubPackagesValidator());
            _validators.Add(new DuplicatePackageEntriesValidator());
            _validators.Add(new ReassignedMediaIndexValidator());
            _validators.Add(new DuplicateMediaIndexValidator());
            _validators.Add(new SuspiciousBackupsValidator());
            _validators.Add(new SemanticIndexConsistencyValidator());
            _validators.Add(new CodeIndexConsistencyValidator());
            _validators.Add(new CorruptDatabaseValidator());
        }

        private void OnDisable()
        {
            _refreshSchedule?.Pause();
            _refreshSchedule = null;
        }

        private void CreateGUI()
        {
            if (_validators.Count == 0) Init();

            VisualElement root = rootVisualElement;
            if (root == null) return;

            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);

            root.Add(AssetInventoryUITK.CreateHelpBox(
                "Scan your database, previews and files for issues, then repair or clean up the results.",
                MessageType.Info));

            root.Add(BuildActionBar());
            root.Add(BuildSearchRow());
            root.Add(BuildValidatorList());

            RefreshVisibleValidators();
            ScheduleRefresh();
        }

        private VisualElement BuildActionBar()
        {
            VisualElement bar = new VisualElement();
            bar.AddToClassList("ai-action-bar");

            Button runAll = AssetInventoryUITK.CreatePrimaryButton("Run All", () => ScanAll(false));
            runAll.tooltip = "Run every maintenance check. Some checks can take a while on large libraries.";
            bar.Add(runAll);
            Button runFast = AssetInventoryUITK.CreateSecondaryButton("Run Only Fast Scans", () => ScanAll(true));
            runFast.tooltip = "Skip checks marked as slow and run only the faster maintenance checks.";
            bar.Add(runFast);

            _fixAllButton = AssetInventoryUITK.CreateDestructiveButton("Fix All", FixAll);
            _fixAllButton.tooltip = "Run the available fixes for every completed check that found repairable issues.";
            bar.Add(_fixAllButton);

            bar.Add(AssetInventoryUITK.CreateFlexibleSpacer());

            _summaryLabel = AssetInventoryUITK.CreateStatusPill(string.Empty, "ai-status-muted");
            bar.Add(_summaryLabel);

            return bar;
        }

        private VisualElement BuildSearchRow()
        {
            return AssetInventoryUITK.CreateWindowSearchField(
                _validatorSearch,
                "Filter maintenance checks by name or description.",
                value =>
                {
                    _validatorSearch = value;
                    RefreshVisibleValidators();
                },
                "ai-standalone-search-field");
        }

        private VisualElement BuildValidatorList()
        {
            VisualElement container = new VisualElement();
            container.AddToClassList("ai-validator-list-container");

            _emptyState = AssetInventoryUITK.CreateHelpBox("No maintenance checks match the current search.", MessageType.Info);
            _emptyState.style.display = DisplayStyle.None;
            container.Add(_emptyState);

            _validatorList = new ListView
            {
                itemsSource = _visibleValidators,
                makeItem = CreateValidatorRow,
                bindItem = BindValidatorRow,
                selectionType = SelectionType.None,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                showBorder = false,
                showBoundCollectionSize = false,
                showFoldoutHeader = false,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight
            };
            _validatorList.AddToClassList("ai-validator-list");
            container.Add(_validatorList);

            return container;
        }

        private static VisualElement CreateValidatorRow()
        {
            VisualElement item = new VisualElement();
            item.AddToClassList("ai-validator-item");

            VisualElement row = new VisualElement();
            row.AddToClassList("ai-validator-row");
            item.Add(row);

            return item;
        }

        private void BindValidatorRow(VisualElement element, int index)
        {
            if (index < 0 || index >= _visibleValidators.Count) return;

            VisualElement row = element.Q<VisualElement>(className: "ai-validator-row");
            if (row == null) return;

            row.Clear();
            Validator validator = _visibleValidators[index];

            VisualElement titleRow = new VisualElement();
            titleRow.AddToClassList("ai-validator-title-row");

            Label title = AssetInventoryUITK.CreateCopyLabel(validator.Name);
            title.AddToClassList("ai-validator-name");
            titleRow.Add(title);

            titleRow.Add(AssetInventoryUITK.CreateFlexibleSpacer());
            titleRow.Add(CreateSpeedPill(validator));
            row.Add(titleRow);

            Label description = AssetInventoryUITK.CreateCopyLabel(validator.Description);
            description.AddToClassList("ai-validator-description");
            description.tooltip = validator.Description;
            row.Add(description);

            VisualElement actions = new VisualElement();
            actions.AddToClassList("ai-validator-actions");

            actions.Add(CreateRunButton(validator));
            if (validator.CurrentState == Validator.State.Idle)
            {
                actions.Add(AssetInventoryUITK.CreateFlexibleSpacer());
            }
            actions.Add(CreateResultPill(validator));

            if (validator.CurrentState == Validator.State.Completed && validator.IssueCount > 0)
            {
                Button showButton = AssetInventoryUITK.CreateSecondaryButton("Show...", () => ShowIssues(validator));
                showButton.AddToClassList("ai-validator-action-button");
                showButton.SetEnabled(validator.CurrentState != Validator.State.Fixing);
                actions.Add(showButton);

                if (validator.Fixable)
                {
                    Button fixButton = AssetInventoryUITK.CreateDestructiveButton(validator.FixCaption, () => FixValidator(validator));
                    fixButton.AddToClassList("ai-validator-action-button");
                    fixButton.SetEnabled(validator.CurrentState != Validator.State.Fixing);
                    actions.Add(fixButton);
                }
            }

            row.Add(actions);
        }

        private Button CreateRunButton(Validator validator)
        {
            bool isIdle = validator.CurrentState == Validator.State.Idle || validator.CurrentState == Validator.State.Completed;
            Button button = AssetInventoryUITK.CreateSecondaryButton(isIdle ? "Scan" : "Cancel", () =>
            {
                if (validator.CurrentState == Validator.State.Idle || validator.CurrentState == Validator.State.Completed)
                {
                    ScanValidator(validator);
                }
                else
                {
                    validator.CancellationRequested = true;
                    RefreshValidatorRows();
                }
            });
            button.AddToClassList("ai-validator-action-button");
            return button;
        }

        private static Label CreateSpeedPill(Validator validator)
        {
            Label pill = AssetInventoryUITK.CreateStatusPill(
                validator.Speed == Validator.ValidatorSpeed.Fast ? "Fast" : "Slow",
                validator.Speed == Validator.ValidatorSpeed.Fast ? "ai-status-muted" : "ai-status-warning");
            pill.AddToClassList("ai-validator-speed-pill");
            return pill;
        }

        private static Label CreateResultPill(Validator validator)
        {
            string result = GetResultText(validator);
            string className = GetResultClass(validator);
            Label pill = AssetInventoryUITK.CreateStatusPill(result, className);
            pill.AddToClassList("ai-validator-result");
            if (validator.CurrentState == Validator.State.Idle)
            {
                pill.AddToClassList("ai-validator-result-idle");
            }
            return pill;
        }

        private static string GetResultText(Validator validator)
        {
            switch (validator.CurrentState)
            {
                case Validator.State.Scanning:
                    return "Scanning...";
                case Validator.State.Fixing:
                    return "Fixing...";
                case Validator.State.Completed:
                    if (validator.IssueCount == 0) return "No Issues";

                    string resultText = validator.ResultText ?? $"{validator.IssueCount:N0} Issues Found";
                    if (validator.IssueCount > 0 && !validator.Fixable) resultText += " - Not Fixable";
                    return resultText;
                default:
                    return "Not scanned";
            }
        }

        private static string GetResultClass(Validator validator)
        {
            switch (validator.CurrentState)
            {
                case Validator.State.Scanning:
                    return "ai-status-progress";
                case Validator.State.Fixing:
                    return "ai-status-error";
                case Validator.State.Completed:
                    return validator.IssueCount == 0 ? "ai-status-success" : "ai-status-error";
                default:
                    return "ai-status-pending";
            }
        }

        private void ScanAll(bool fastOnly)
        {
            foreach (Validator validator in _validators.Where(v => v.IsVisible()))
            {
                if (fastOnly && validator.Speed != Validator.ValidatorSpeed.Fast) continue;
                if (validator.CurrentState == Validator.State.Idle || validator.CurrentState == Validator.State.Completed)
                {
                    ScanValidator(validator);
                }
            }

            RefreshValidatorRows();
        }

        private void FixAll()
        {
            foreach (Validator validator in _validators.Where(v => v.IsVisible()))
            {
                if (validator.CurrentState == Validator.State.Completed && validator.IssueCount > 0 && validator.Fixable)
                {
                    FixValidator(validator);
                }
            }

            RefreshValidatorRows();
        }

        private static void ScanValidator(Validator validator)
        {
            validator.CancellationRequested = false;
            _ = validator.Validate();
        }

        private void FixValidator(Validator validator)
        {
            validator.CancellationRequested = false;
            _ = validator.Fix();
            OnMaintenanceDone?.Invoke();
        }

        internal static bool MatchesValidatorSearch(string validatorName, string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText)) return true;
            if (string.IsNullOrEmpty(validatorName)) return false;

            return validatorName.IndexOf(searchText.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RefreshVisibleValidators()
        {
            _visibleValidators.Clear();
            _visibleValidators.AddRange(_validators.Where(v => v.IsVisible() && MatchesValidatorSearch(v.Name, _validatorSearch)));

            if (_validatorList != null)
            {
                _validatorList.itemsSource = _visibleValidators;
                _validatorList.Rebuild();
            }

            UpdateActionState();
        }

        private void RefreshValidatorRows()
        {
            UpdateActionState();
            _validatorList?.RefreshItems();
        }

        private void UpdateActionState()
        {
            _fixableItems = _validators.Count(v => v.IsVisible() &&
                v.CurrentState == Validator.State.Completed &&
                v.IssueCount > 0 &&
                v.Fixable);

            _fixAllButton?.SetEnabled(_fixableItems > 0);
            if (_fixAllButton != null)
            {
                _fixAllButton.tooltip = _fixableItems > 0
                    ? $"Run the available fixes for {_fixableItems:N0} maintenance checks."
                    : "Run the checks first. Fix All becomes available when a completed check finds repairable issues.";
            }

            int visibleChecks = _validators.Count(v => v.IsVisible());
            if (_summaryLabel != null)
            {
                _summaryLabel.text = _fixableItems == 0
                    ? $"{visibleChecks:N0} Checks"
                    : $"{visibleChecks:N0} Checks / {_fixableItems:N0} Fixable";
            }

            if (_emptyState != null)
            {
                _emptyState.style.display = _visibleValidators.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_validatorList != null)
            {
                _validatorList.style.display = _visibleValidators.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        private void ScheduleRefresh()
        {
            _refreshSchedule?.Pause();
            _refreshSchedule = rootVisualElement.schedule.Execute(RefreshValidatorRows).Every(500);
        }

        private static void ShowIssues(Validator validator)
        {
            switch (validator.Type)
            {
                case Validator.ValidatorType.DB:
                    LineListWindow.Show("Issue List", validator.DBIssues
                        .Select(i => $"{(string.IsNullOrWhiteSpace(i.Path) ? i.GetDisplayName() : i.Path)} ({i.Id})")
                        .OrderBy(s => s));
                    break;

                case Validator.ValidatorType.FileSystem:
                    LineListWindow.Show("Issue List", validator.FileIssues.OrderBy(s => s));
                    break;
            }
        }
    }
}
