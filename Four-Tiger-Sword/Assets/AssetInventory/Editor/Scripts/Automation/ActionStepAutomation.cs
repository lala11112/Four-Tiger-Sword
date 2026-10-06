using System;
using System.Collections.Generic;
using System.Linq;
using Automator;
using Newtonsoft.Json;

namespace AssetInventory.Automation
{
    internal static class ActionStepAutomation
    {
        internal sealed class ListActionStepTypesRequest
        {
            public string Category { get; set; }
        }

        internal sealed class AddActionStepRequest
        {
            public int ActionId { get; set; }
            public string StepKey { get; set; }
            public int OrderIndex { get; set; } = -1;
            public string ParameterValues { get; set; }
            public bool DryRun { get; set; } = true;
            public string ConfirmationToken { get; set; }
        }

        internal sealed class RemoveActionStepRequest
        {
            public int StepId { get; set; }
            public bool DryRun { get; set; } = true;
            public string ConfirmationToken { get; set; }
        }

        internal sealed class UpdateActionStepRequest
        {
            public int ActionId { get; set; }
            public int StepId { get; set; }
            public int OrderIndex { get; set; } = -1;
            public string ParameterValues { get; set; }
            public bool DryRun { get; set; } = true;
            public string ConfirmationToken { get; set; }
        }

        internal static AutomationResponse ListActionStepTypes(ListActionStepTypesRequest request)
        {
            AutomationResponse initError = AutomationResultHelper.EnsureInit();
            if (initError != null) return initError;

            List<ActionStep> steps = ActionStepRegistry.Steps;
            if (!string.IsNullOrEmpty(request?.Category))
            {
                if (!AutomationInputValidator.TryParseDefinedEnum(request.Category, out ActionStep.ActionCategory category))
                {
                    return AutomationResponse.Error($"Invalid category '{request.Category}'. Use: FilesAndFolders, Importing, Actions, Settings, Misc.", errorCode: "invalid_input");
                }
                steps = steps.Where(step => step.Category == category).ToList();
            }

            return AutomationResponse.Success($"Found {steps.Count} action step types.", new
            {
                stepTypes = steps.Select(step => new
                {
                    key = step.Key,
                    name = step.Name,
                    description = step.Description,
                    category = step.Category.ToString(),
                    parameters = step.Parameters.Select(parameter => new
                    {
                        name = parameter.Name,
                        description = parameter.Description,
                        type = parameter.Type.ToString(),
                        optional = parameter.Optional,
                        defaultStringValue = parameter.DefaultValue?.stringValue,
                        defaultIntValue = parameter.DefaultValue?.intValue ?? 0,
                        defaultBoolValue = parameter.DefaultValue?.boolValue ?? false
                    }).ToArray()
                }).ToArray()
            });
        }

        internal static AutomationResponse AddActionStep(AddActionStepRequest request)
        {
            AutomationResponse initError = AutomationResultHelper.EnsureInit();
            if (initError != null) return initError;
            if (request == null || string.IsNullOrWhiteSpace(request.StepKey))
            {
                return AutomationResponse.Error("StepKey is required.", errorCode: "invalid_input");
            }
            if (request.OrderIndex < -1)
            {
                return AutomationResponse.Error("OrderIndex must be -1 or greater.", errorCode: "invalid_input");
            }
            SqliteActionRepository repository = new SqliteActionRepository();
            ActionDefinition action = repository.GetAction(request.ActionId);
            if (action == null)
            {
                return AutomationResponse.Error($"Action with ID {request.ActionId} not found.", errorCode: "not_found");
            }
            ActionStep stepType = ActionStepRegistry.GetStep(request.StepKey);
            if (stepType == null)
            {
                return AutomationResponse.Error($"Step type '{request.StepKey}' not found. Use the list action step types command to see available types.", errorCode: "not_found");
            }

            List<ActionStepDefinition> existingSteps = repository.GetSteps(request.ActionId);
            int orderIndex = request.OrderIndex >= 0 ? request.OrderIndex : existingSteps.Count;
            List<ParameterValue> values = stepType.Parameters.Select(parameter => new ParameterValue(parameter.DefaultValue ?? new ParameterValue())).ToList();
            AutomationResponse parameterError = TryApplyOverrides(stepType, values, request.ParameterValues);
            if (parameterError != null) return parameterError;
            string valuesJson = JsonConvert.SerializeObject(values);
            string existingStepState = JsonConvert.SerializeObject(existingSteps.OrderBy(step => step.OrderIndex).ThenBy(step => step.Id).Select(step => new {step.Id, step.Key, step.OrderIndex, step.Values}));

            AutomationResponse confirmation = AutomationMutationGuard.RequireConfirmation(
                "add_action_step",
                request.DryRun,
                request.ConfirmationToken,
                new {actionId = action.Id, actionName = action.Name, stepKey = stepType.Key, stepName = stepType.Name, orderIndex, parameterValues = values},
                action.Id.ToString(), action.Name, existingStepState, stepType.Key, orderIndex.ToString(), valuesJson);
            if (confirmation != null) return confirmation;

            ActionStepDefinition savedStep = repository.SaveStep(new ActionStepDefinition
            {
                ActionId = request.ActionId,
                Key = request.StepKey,
                OrderIndex = orderIndex,
                Values = values
            });
            return AutomationResponse.Success($"Step '{stepType.Name}' added to action '{action.Name}' at position {orderIndex}.", new {stepId = savedStep.Id});
        }

        internal static AutomationResponse RemoveActionStep(RemoveActionStepRequest request)
        {
            AutomationResponse initError = AutomationResultHelper.EnsureInit();
            if (initError != null) return initError;
            if (request == null)
            {
                return AutomationResponse.Error("Request is required.", errorCode: "invalid_input");
            }
            SqliteActionRepository repository = new SqliteActionRepository();
            ActionDefinition owningAction = null;
            ActionStepDefinition step = null;
            foreach (ActionDefinition action in repository.GetAllActions())
            {
                step = repository.GetSteps(action.Id).FirstOrDefault(candidate => candidate.Id == request.StepId);
                if (step == null) continue;
                owningAction = action;
                break;
            }
            if (step == null || owningAction == null)
            {
                return AutomationResponse.Error($"Step with ID {request.StepId} not found.", errorCode: "not_found");
            }

            AutomationResponse confirmation = AutomationMutationGuard.RequireConfirmation(
                "remove_action_step",
                request.DryRun,
                request.ConfirmationToken,
                new {actionId = owningAction.Id, actionName = owningAction.Name, stepId = step.Id, step.Key, step.OrderIndex, parameterValues = step.Values},
                owningAction.Id.ToString(), owningAction.Name, step.Id.ToString(), step.Key, step.OrderIndex.ToString(), JsonConvert.SerializeObject(step.Values));
            if (confirmation != null) return confirmation;

            repository.DeleteStep(request.StepId);
            return AutomationResponse.Success($"Step {request.StepId} removed.");
        }

        internal static AutomationResponse UpdateActionStep(UpdateActionStepRequest request)
        {
            AutomationResponse initError = AutomationResultHelper.EnsureInit();
            if (initError != null) return initError;
            if (request == null)
            {
                return AutomationResponse.Error("Request is required.", errorCode: "invalid_input");
            }
            if (request.OrderIndex < -1)
            {
                return AutomationResponse.Error("OrderIndex must be -1 or greater.", errorCode: "invalid_input");
            }

            SqliteActionRepository repository = new SqliteActionRepository();
            ActionDefinition action = repository.GetAction(request.ActionId);
            if (action == null)
            {
                return AutomationResponse.Error($"Action with ID {request.ActionId} not found.", errorCode: "not_found");
            }
            ActionStepDefinition step = repository.GetSteps(request.ActionId).FirstOrDefault(candidate => candidate.Id == request.StepId);
            if (step == null)
            {
                return AutomationResponse.Error($"Step with ID {request.StepId} not found in action {request.ActionId}.", errorCode: "not_found");
            }
            ActionStep stepType = ActionStepRegistry.GetStep(step.Key);
            if (stepType == null)
            {
                return AutomationResponse.Error($"Step type '{step.Key}' is no longer registered.", errorCode: "not_found");
            }

            int nextOrderIndex = request.OrderIndex >= 0 ? request.OrderIndex : step.OrderIndex;
            List<ParameterValue> nextValues = (step.Values ?? new List<ParameterValue>()).Select(value => new ParameterValue(value)).ToList();
            AutomationResponse parameterError = TryApplyOverrides(stepType, nextValues, request.ParameterValues);
            if (parameterError != null) return parameterError;
            string beforeValues = JsonConvert.SerializeObject(step.Values);
            string afterValues = JsonConvert.SerializeObject(nextValues);

            AutomationResponse confirmation = AutomationMutationGuard.RequireConfirmation(
                "update_action_step",
                request.DryRun,
                request.ConfirmationToken,
                new
                {
                    actionId = action.Id,
                    actionName = action.Name,
                    stepId = step.Id,
                    stepKey = step.Key,
                    before = new {step.OrderIndex, parameterValues = step.Values},
                    after = new {orderIndex = nextOrderIndex, parameterValues = nextValues}
                },
                action.Id.ToString(), action.Name, step.Id.ToString(), step.Key, step.OrderIndex.ToString(), beforeValues, nextOrderIndex.ToString(), afterValues);
            if (confirmation != null) return confirmation;

            step.OrderIndex = nextOrderIndex;
            step.Values = nextValues;
            repository.SaveStep(step);
            return AutomationResponse.Success($"Step {request.StepId} updated.");
        }

        private static AutomationResponse TryApplyOverrides(ActionStep stepType, List<ParameterValue> values, string parameterValues)
        {
            if (string.IsNullOrEmpty(parameterValues)) return null;

            try
            {
                Dictionary<string, object> overrides = JsonConvert.DeserializeObject<Dictionary<string, object>>(parameterValues);
                if (overrides == null) return null;

                foreach (KeyValuePair<string, object> pair in overrides)
                {
                    int parameterIndex = stepType.Parameters.FindIndex(parameter => parameter.Name.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
                    if (parameterIndex < 0)
                    {
                        return AutomationResponse.Error($"Unknown parameter '{pair.Key}' for step type '{stepType.Key}'.", errorCode: "invalid_input");
                    }
                    while (values.Count <= parameterIndex) values.Add(new ParameterValue());

                    if (pair.Value is bool boolValue)
                    {
                        values[parameterIndex].boolValue = boolValue;
                    }
                    else if (pair.Value is long longValue)
                    {
                        if (longValue < int.MinValue || longValue > int.MaxValue)
                        {
                            return AutomationResponse.Error($"Parameter '{pair.Key}' is outside the supported 32-bit integer range.", errorCode: "invalid_input");
                        }
                        values[parameterIndex].intValue = (int)longValue;
                    }
                    else if (pair.Value is int intValue)
                    {
                        values[parameterIndex].intValue = intValue;
                    }
                    else
                    {
                        values[parameterIndex].stringValue = pair.Value?.ToString();
                    }
                }
                return null;
            }
            catch (JsonException exception)
            {
                return AutomationResponse.Error($"Invalid ParameterValues JSON: {exception.Message}", errorCode: "invalid_input");
            }
        }
    }
}
