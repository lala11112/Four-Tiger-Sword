using ImpossibleRobert.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Automator
{
    /// <summary>
    /// Editor window for creating and editing automation actions.
    /// </summary>
    public class ActionEditorWindow : CommonEditorUI
    {
        private IActionRepository _repository;
        private List<ActionStepDefinition> _steps = new List<ActionStepDefinition>();
        private Action _onSave;
        private Dictionary<string, List<Tuple<string, ParameterValue>>> _parameterOptionsCache = new Dictionary<string, List<Tuple<string, ParameterValue>>>();
        private ListView _stepsList;
        private VisualElement _editorRoot;
        private Label _stepsSummary;
        private CommonReorderableListView<ActionStepDefinition> _stepCollection;
        private VisualElement _stepDetails;
        private const float StepRowHeight = 28f;

        private ActionDefinition _action;

        public static ActionEditorWindow ShowWindow()
        {
            ActionEditorWindow window = GetWindow<ActionEditorWindow>("Action Editor");
            window.minSize = new Vector2(690, 300);
            return window;
        }

        public static ActionEditorWindow CreateNew(IActionRepository repository, Action onSave = null)
        {
            ActionEditorWindow window = ShowWindow();

            ActionDefinition newAction = new ActionDefinition("New Action");
            newAction = repository.SaveAction(newAction);
            repository.Save();

            window.Init(repository, newAction, onSave);
            return window;
        }

        public static ActionEditorWindow Edit(IActionRepository repository, ActionDefinition action, Action onSave = null)
        {
            ActionEditorWindow window = ShowWindow();
            window.Init(repository, action, onSave);
            return window;
        }

        public void Init(IActionRepository repository, ActionDefinition action, Action onSave = null)
        {
            _repository = repository;
            _action = action;
            _onSave = onSave;

            _steps = _repository.GetSteps(_action.Id);
            _parameterOptionsCache.Clear();
            RebuildNativeEditor();
        }

        public void CreateGUI()
        {
            RebuildNativeEditor();
        }

        private void RebuildNativeEditor()
        {
            if (rootVisualElement == null)
                return;

            rootVisualElement.Clear();
            StyleSheet styleSheet = CommonUITK.LoadStyleSheetFromAnchor(
                "ActionEditorWindow",
                "Editor/Scripts/UI/ActionEditorWindow.cs",
                "Editor/Scripts/UI/AutomatorInspector.uss");
            _editorRoot = CommonInspectorElements.CreateRoot(
                "automator-action-editor",
                styleSheet,
                "automator-inspector",
                "automator-action-editor");
            CommonUITK.ApplyWindowLayout(_editorRoot);
            rootVisualElement.Add(_editorRoot);

            if (_action == null || _repository == null)
            {
                _editorRoot.Add(CommonInspectorElements.CreateHelpBox(
                    "No action is loaded. Create a new action or open one from the Action Manager.",
                    HelpBoxMessageType.Warning));
                return;
            }

            ScrollView content = new ScrollView(ScrollViewMode.Vertical) { name = "automator-workflow-pane" };
            content.style.flexGrow = 1f;
            content.style.minHeight = 0f;
            content.contentContainer.style.paddingLeft = 8f;
            content.contentContainer.style.paddingRight = 8f;
            content.contentContainer.style.paddingBottom = 8f;

            VisualElement identity = new VisualElement();
            TextField name = new TextField("Name") { value = _action.Name ?? string.Empty, tooltip = "Name shown in the action manager." };
            name.RegisterValueChangedCallback(evt => _action.Name = evt.newValue);
            identity.Add(name);
            TextField description = new TextField("Description")
            {
                value = _action.Description ?? string.Empty,
                tooltip = "Describe what this workflow does.",
                multiline = true
            };
            description.RegisterValueChangedCallback(evt => _action.Description = evt.newValue);
            identity.Add(description);
            Toggle stop = new Toggle("Stop on Failure")
            {
                value = _action.StopOnFailure,
                tooltip = "Stop remaining steps after the first failure. Disable to log failures and continue."
            };
            stop.RegisterValueChangedCallback(evt => _action.StopOnFailure = evt.newValue);
            identity.Add(stop);
            EnumField mode = new EnumField("Run Mode", _action.Mode);
            mode.tooltip = "Choose when this action is eligible to run.";
            mode.RegisterValueChangedCallback(evt => _action.Mode = (ActionDefinition.RunMode)evt.newValue);
            identity.Add(mode);
            content.Add(CommonInspectorElements.CreateSection(
                "Action",
                "automator-action-identity",
                identity));

            _stepsSummary = CommonInspectorElements.CreateMutedText(GetStepSummary());
            content.Add(CommonUITK.CreateLabel("Steps", "automator-steps-title"));
            content.Add(_stepsSummary);
            _stepCollection = new CommonReorderableListView<ActionStepDefinition>(
                _steps, MakeStepRow, (element, _, index) => BindStepRow(element, index), StepRowHeight);
            _stepsList = _stepCollection.ListView;
            _stepsList.name = "automator-step-list";
            _stepsList.tooltip = "Drag steps to reorder. Select a step to edit its parameters on the right; use + and - to add or remove steps.";
            _stepsList.AddToClassList("automator-step-list");
            _stepsList.Q<ScrollView>().verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _stepCollection.SetAddHandler((_, button) => ShowAddStepMenu(button));
            _stepCollection.ItemsChanged += RefreshStepList;
            _stepsList.selectionChanged += _ => RefreshSelectedStep();
            content.Add(_stepCollection);
            _stepDetails = new VisualElement { name = "automator-step-details" };
            _stepDetails.AddToClassList("automator-step-details");
            ScrollView inspector = new ScrollView(ScrollViewMode.Vertical) { name = "automator-inspector-pane" };
            inspector.style.minWidth = 280f;
            inspector.AddToClassList("automator-inspector-pane");
            inspector.Add(_stepDetails);
            CommonTabbedPane inspectorPane = new CommonTabbedPane(new CommonTabbedPane.TabbedPaneClasses
            {
                RootClass = "automator-details-pane",
                HeaderClass = "automator-details-header",
                TabStripClass = "automator-details-tabs",
                TabClass = "automator-details-tab",
                SelectedTabClass = "automator-details-tab--selected",
                BodyClass = CommonUITK.WindowBodyClass
            });
            inspectorPane.SetTabs(new[] { "Details" }, 0, _ => { });
            inspectorPane.Body.style.flexGrow = 1f;
            inspectorPane.Body.style.minHeight = 0f;
            inspectorPane.Body.style.flexBasis = 0f;
            inspector.style.flexGrow = 1f;
            inspector.style.minHeight = 0f;
            inspector.contentContainer.style.paddingLeft = 8f;
            inspector.contentContainer.style.paddingRight = 8f;
            inspectorPane.Body.Add(inspector);
            CommonResizableSidePaneLayout split = new CommonResizableSidePaneLayout(content,
                trailing: new CommonResizableSidePaneLayout.PaneDefinition
                {
                    Content = inspectorPane, PreferredWidth = 340f, MinimumWidth = 280f, MaximumWidth = 720f
                },
                options: new CommonResizableSidePaneLayout.LayoutOptions { MainMinimumWidth = 300f },
                classes: new CommonResizableSidePaneLayout.LayoutClasses
                {
                    TrailingHostClass = "automator-details-host",
                    DividerClass = "automator-details-divider",
                    DividerLineClass = "automator-details-divider-line",
                    CollapsedClass = "automator-details-collapsed"
                });
            split.AddToClassList(CommonUITK.WindowBodyClass);
            split.style.flexBasis = 0f;
            split.style.flexShrink = 1f;
            _editorRoot.Add(split);
            RefreshStepList();
            if (_steps.Count > 0) _stepsList.SetSelection(0);

            Button saveClose = CommonInspectorElements.CreateButton(
                "Save & Close",
                "Save this action and return to the manager",
                () =>
                {
                    Save();
                    Close();
                },
                true);
            Button saveRun = CommonInspectorElements.CreateButton(
                "Save & Run",
                "Save and execute the complete action now",
                () =>
                {
                    Save();
                    RunAction();
                },
                true);
            VisualElement footer = CommonUITK.CreateWindowFooter(0f, 0f, "automator-action-editor-footer");
            saveClose.style.minHeight = 30f;
            saveRun.style.minHeight = 30f;
            saveClose.style.flexGrow = 1f;
            saveRun.style.flexGrow = 1f;
            footer.Add(saveClose);
            footer.Add(saveRun);
            _editorRoot.Add(footer);
        }

        private void ShowAddStepMenu(Button button)
        {
            GenericMenu menu = new GenericMenu();
            foreach (ActionStep step in ActionStepRegistry.Steps)
            {
                ActionStep captured = step;
                string category = StringUtils.CamelCaseToWords(step.Category.ToString());
                menu.AddItem(new GUIContent(category + "/" + step.Name), false, () => AddStep(captured));
            }
            menu.DropDown(button.worldBound);
        }

        private VisualElement MakeStepRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("automator-step-row");
            Toggle enabled = new Toggle { name = "automator-step-enabled", tooltip = "Include this step when running the action. Individual Run remains available when disabled." };
            enabled.AddToClassList("automator-step-row__enabled");
            enabled.RegisterValueChangedCallback(evt =>
            {
                if (!(row.userData is ActionStepDefinition definition)) return;
                definition.Enabled = evt.newValue;
                UpdateStepEnabledState(row, definition);
                RefreshStepSummary();
                RefreshSelectedStep();
            });
            row.Add(enabled);
            row.Add(CommonUITK.CreateLabel(string.Empty, "automator-step-row__name"));
            row.Add(CommonUITK.CreateLabel(string.Empty, "automator-step-row__summary"));
            row.Add(CommonUITK.CreateLabel("Disabled", "automator-step-row__disabled"));
            return row;
        }

        private void BindStepRow(VisualElement row, int index)
        {
            ActionStepDefinition definition = _steps[index];
            row.userData = definition;
            row.Q<Toggle>().SetValueWithoutNotify(definition.Enabled);
            UpdateStepEnabledState(row, definition);
            ActionStep step = ActionStepRegistry.GetStep(definition.Key);
            row.Q<Label>(className: "automator-step-row__name").text = step?.Name ?? definition.Key;
            row.Q<Label>(className: "automator-step-row__summary").text = BuildStepSummary(step, definition);
            row.tooltip = step?.Description ?? "This step is not installed. Select it for details or remove it with the list footer.";
        }

        internal string BuildStepSummary(ActionStep step, ActionStepDefinition definition)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < definition.Values.Count; i++)
            {
                ParameterValue value = definition.Values[i];
                string display = value.stringValue;
                if (step != null && i < step.Parameters.Count)
                {
                    StepParameter parameter = step.Parameters[i];
                    if (!step.GetParamVisibility(parameter, definition.Values)) continue;
                    StepParameter.ParamType type = parameter.Type == StepParameter.ParamType.Dynamic
                        ? step.GetParamType(parameter, definition.Values) : parameter.Type;
                    List<Tuple<string, ParameterValue>> options = GetParameterOptions(step, parameter, definition.Values);
                    int selected = FindOptionIndex(options, type, value, parameter.OptionComparison);
                    if (selected >= 0) display = options[selected].Item1;
                }
                if (!string.IsNullOrWhiteSpace(display)) parts.Add(display.Replace('\n', ' '));
            }
            return string.Join(" · ", parts);
        }

        private List<Tuple<string, ParameterValue>> GetParameterOptions(ActionStep step, StepParameter parameter, List<ParameterValue> values)
        {
            if (parameter.LazyLoadOptions)
            {
                string cacheKey = $"{step.Key}_{parameter.Name}";
                if (!_parameterOptionsCache.TryGetValue(cacheKey, out List<Tuple<string, ParameterValue>> options))
                {
                    options = step.GetParamOptions(parameter, values);
                    _parameterOptionsCache[cacheKey] = options;
                }
                return options;
            }
            return parameter.Type == StepParameter.ParamType.Dynamic
                ? step.GetParamOptions(parameter, values) : parameter.Options;
        }

        private static int FindOptionIndex(List<Tuple<string, ParameterValue>> options, StepParameter.ParamType type, ParameterValue value, StringComparison comparison)
        {
            return options?.FindIndex(option => type == StepParameter.ParamType.Int
                ? option.Item2.intValue == value.intValue
                : type == StepParameter.ParamType.Bool
                    ? option.Item2.boolValue == value.boolValue
                    : string.Equals(option.Item2.stringValue, value.stringValue, comparison)) ?? -1;
        }

        private static void UpdateStepEnabledState(VisualElement row, ActionStepDefinition definition)
        {
            row.EnableInClassList("automator-step-row--disabled", !definition.Enabled);
            row.Q<Label>(className: "automator-step-row__disabled").style.display = definition.Enabled ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void RefreshStepList()
        {
            if (_stepsList == null) return;
            // The list owns ordering; the outer ScrollView owns scrolling, even for long actions.
            _stepsList.style.height = Math.Max(1, _steps.Count) * StepRowHeight + 28f;
            _stepsList.RefreshItems();
            RefreshStepSummary();
            RefreshSelectedStep();
        }

        private void RefreshSelectedStep()
        {
            if (_stepDetails == null) return;
            _stepDetails.Clear();
            int index = _stepsList?.selectedIndex ?? -1;
            if (index >= 0 && index < _steps.Count) BindStepElement(_stepDetails, index);
            else _stepDetails.Add(CommonInspectorElements.CreateMutedText("Select a step to inspect its parameters."));
        }

        private void BindStepElement(VisualElement item, int index)
        {
            item.Clear();
            if (index < 0 || index >= _steps.Count)
                return;

            ActionStepDefinition stepDef = _steps[index];
            ActionStep step = ActionStepRegistry.GetStep(stepDef.Key);
            if (step == null)
            {
                item.Add(CommonInspectorElements.CreateHelpBox(
                    $"Step '{stepDef.Key}' is not installed. Remove it or install the required extension.",
                    HelpBoxMessageType.Error));
                return;
            }

            VisualElement header = new VisualElement();
            header.AddToClassList("automator-step-card__header");
            VisualElement titleBlock = new VisualElement();
            titleBlock.style.flexGrow = 1f;
            titleBlock.Add(CommonUITK.CreateLabel(step.Name, "automator-step-card__title"));
            titleBlock.Add(CommonInspectorElements.CreateMutedText(step.Description));
            header.Add(titleBlock);
            header.Add(CommonInspectorElements.CreateButton(
                "Run",
                "Run only this step with variables produced by earlier steps",
                () => RunStep(index)));
            item.Add(header);

            Dictionary<string, string> availableVariables = BuildAvailableVariables(index);
            while (stepDef.Values.Count < step.Parameters.Count)
            {
                int paramIndex = stepDef.Values.Count;
                stepDef.Values.Add(new ParameterValue(step.Parameters[paramIndex].DefaultValue));
            }

            for (int i = 0; i < step.Parameters.Count; i++)
            {
                int parameterIndex = i;
                StepParameter param = step.Parameters[i];
                if (!step.GetParamVisibility(param, stepDef.Values))
                    continue;

                StepParameter.ParamType finalType = param.Type;
                if (finalType == StepParameter.ParamType.Dynamic)
                    finalType = step.GetParamType(param, stepDef.Values);

                List<Tuple<string, ParameterValue>> finalOptions = GetParameterOptions(step, param, stepDef.Values);

                string label = param.Name + (param.Optional ? " (optional)" : string.Empty);
                VisualElement field = CreateParameterField(
                    label,
                    param.Description,
                    finalType,
                    finalOptions,
                    stepDef.Values[i],
                    () =>
                    {
                        _stepsList?.RefreshItem(index);
                        if (param.Type == StepParameter.ParamType.Dynamic)
                            _stepDetails.schedule.Execute(RefreshSelectedStep);
                    }, param.OptionComparison);
                if (field != null)
                    item.Add(field);

                if (finalOptions != null && finalOptions.Count > 0)
                    continue;

                if (finalType == StepParameter.ParamType.String || finalType == StepParameter.ParamType.MultilineString)
                {
                    string parameterValue = stepDef.Values[parameterIndex].stringValue;
                    List<string> undefined = string.IsNullOrEmpty(parameterValue)
                        ? new List<string>()
                        : VariableResolver.ValidateVariables(parameterValue, availableVariables);
                    if (undefined.Count > 0)
                    {
                        item.Add(CommonInspectorElements.CreateHelpBox(
                            "Undefined variables: " + string.Join(", ", undefined),
                            HelpBoxMessageType.Warning));
                    }
                }
            }
        }

        internal static VisualElement CreateParameterField(
            string label,
            string tooltip,
            StepParameter.ParamType type,
            List<Tuple<string, ParameterValue>> options,
            ParameterValue value,
            Action changed,
            StringComparison optionComparison = StringComparison.Ordinal)
        {
            if (options != null && options.Count > 0)
            {
                List<string> choices = options.Select(option => option.Item1.Replace("/", "\\")).ToList();
                int current = FindOptionIndex(options, type, value, optionComparison);
                if (current < 0)
                {
                    // Rendering must never replace saved values, including removed or renamed options.
                    string saved = type == StepParameter.ParamType.Int ? value.intValue.ToString()
                        : type == StepParameter.ParamType.Bool ? value.boolValue.ToString() : value.stringValue;
                    choices.Add(string.IsNullOrEmpty(saved) ? "Select a value..." : saved + " (unavailable)");
                    current = choices.Count - 1;
                }
                PopupField<string> popup = new PopupField<string>(label, choices, current)
                {
                    tooltip = tooltip
                };
                popup.RegisterValueChangedCallback(evt =>
                {
                    int index = choices.IndexOf(evt.newValue);
                    if (index < 0 || index >= options.Count)
                    {
                        popup.SetValueWithoutNotify(evt.previousValue);
                        return;
                    }
                    if (type == StepParameter.ParamType.Int)
                        value.intValue = options[index].Item2.intValue;
                    else if (type == StepParameter.ParamType.Bool)
                        value.boolValue = options[index].Item2.boolValue;
                    else
                        value.stringValue = options[index].Item2.stringValue;
                    changed();
                });
                return popup;
            }

            switch (type)
            {
                case StepParameter.ParamType.String:
                case StepParameter.ParamType.MultilineString:
                    TextField text = new TextField(label)
                    {
                        value = value.stringValue ?? string.Empty,
                        multiline = type == StepParameter.ParamType.MultilineString,
                        tooltip = tooltip
                    };
                    text.RegisterValueChangedCallback(evt =>
                    {
                        value.stringValue = evt.newValue;
                        changed();
                    });
                    return text;
                case StepParameter.ParamType.Int:
                    IntegerField integer = new IntegerField(label) { value = value.intValue, tooltip = tooltip };
                    integer.RegisterValueChangedCallback(evt =>
                    {
                        value.intValue = evt.newValue;
                        changed();
                    });
                    return integer;
                case StepParameter.ParamType.Bool:
                    Toggle toggle = new Toggle(label) { value = value.boolValue, tooltip = tooltip };
                    toggle.RegisterValueChangedCallback(evt =>
                    {
                        value.boolValue = evt.newValue;
                        changed();
                    });
                    return toggle;
                default:
                    return CommonInspectorElements.CreateHelpBox(
                        $"Parameter '{label}' uses an unsupported editor type ({type}).",
                        HelpBoxMessageType.Warning);
            }
        }

        private void RunStep(int index)
        {
            if (index < 0 || index >= _steps.Count)
                return;
            ActionStepDefinition definition = _steps[index];
            ActionStep step = ActionStepRegistry.GetStep(definition.Key);
            if (step == null)
                return;
            try
            {
                Dictionary<string, string> variables = BuildAvailableVariables(index);
                if (step is SetTextVariableStep &&
                    SetTextVariableStep.TryExtractVariable(definition.Values, out string variableName, out string variableValue))
                {
                    variables[variableName] = VariableResolver.ReplaceVariables(variableValue ?? string.Empty, variables);
                }
                List<ParameterValue> resolved = ActionRunner.ResolveParameterVariables(
                    definition.Values,
                    step.Parameters,
                    step,
                    variables);
                step.Run(resolved);
                AssetDatabase.Refresh();
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Error Running Step", $"Failed to run step: {exception.Message}", "OK");
            }
        }

        private string GetStepSummary()
        {
            return _steps.Count == 0
                ? "No steps yet. Add the first step to build this workflow."
                : $"{_steps.Count} step{(_steps.Count == 1 ? string.Empty : "s")} · drag to reorder, select to edit";
        }

        private void RefreshStepSummary()
        {
            if (_stepsSummary != null)
                _stepsSummary.text = GetStepSummary();
        }

        private async void RunAction()
        {
            ActionRunner runner = new ActionRunner(_repository);
            await runner.RunAction(_action.Id);
            AssetDatabase.Refresh();
        }

        internal void Save()
        {
            _repository.SaveAction(_action);

            for (int i = 0; i < _steps.Count; i++)
            {
                ActionStepDefinition step = _steps[i];
                step.OrderIndex = i;
                step.ActionId = _action.Id;
                _repository.SaveStep(step);
            }

            // Delete removed steps
            _repository.DeleteStepsExcept(_action.Id, _steps.Where(s => s.Id > 0).Select(s => s.Id).ToList());

            _repository.Save();

            _onSave?.Invoke();
        }

        internal void AddStep(ActionStep step)
        {
            ActionStepDefinition newStep = new ActionStepDefinition
            {
                Key = step.Key,
                ActionId = _action.Id,
                OrderIndex = _steps.Count,
                Values = step.Parameters.Select(p => new ParameterValue(p.DefaultValue)).ToList()
            };

            int selectedStepIndex = _stepsList?.selectedIndex ?? -1;
            _stepCollection.AddItem(newStep, selectedStepIndex >= 0 ? selectedStepIndex + 1 : -1);
        }

        private Dictionary<string, string> BuildAvailableVariables(int stepIndex)
        {
            Dictionary<string, string> variables = new Dictionary<string, string>();

            for (int i = 0; i < stepIndex && i < _steps.Count; i++)
            {
                ActionStepDefinition stepDef = _steps[i];
                if (!stepDef.Enabled) continue;
                ActionStep step = ActionStepRegistry.GetStep(stepDef.Key);
                if (step == null) continue;

                if (step is SetTextVariableStep)
                {
                    if (SetTextVariableStep.TryExtractVariable(stepDef.Values, out string varName, out string varValue))
                    {
                        varValue = VariableResolver.ReplaceVariables(varValue ?? "", variables);
                        variables[varName] = varValue;
                    }
                }
            }

            return variables;
        }
    }
}
