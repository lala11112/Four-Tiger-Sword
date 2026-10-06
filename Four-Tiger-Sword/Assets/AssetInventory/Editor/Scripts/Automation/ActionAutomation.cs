using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Automator;
using Newtonsoft.Json;

namespace AssetInventory.Automation
{
    internal static class ActionAutomation
    {
        internal sealed class ListActionsRequest
        {
            public string SearchPhrase { get; set; }
        }

        internal sealed class GetActionDetailsRequest
        {
            public int ActionId { get; set; }
        }

        internal sealed class CreateActionRequest
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public bool StopOnFailure { get; set; } = true;
            public string RunMode { get; set; } = "Manual";
            public bool DryRun { get; set; } = true;
            public string ConfirmationToken { get; set; }
        }

        internal sealed class UpdateActionRequest
        {
            public int ActionId { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public int StopOnFailure { get; set; } = -1;
            public string RunMode { get; set; }
            public bool DryRun { get; set; } = true;
            public string ConfirmationToken { get; set; }
        }

        internal sealed class DeleteActionRequest
        {
            public int ActionId { get; set; }
            public bool DryRun { get; set; } = true;
            public string ConfirmationToken { get; set; }
        }

        internal sealed class RunActionRequest
        {
            public int ActionId { get; set; }
            public bool DryRun { get; set; } = true;
            public string ConfirmationToken { get; set; }
        }

        internal static AutomationResponse ListActions(ListActionsRequest request)
        {
            AutomationResponse initError = AutomationResultHelper.EnsureInit();
            if (initError != null) return initError;

            SqliteActionRepository repository = new SqliteActionRepository();
            List<ActionDefinition> actions = repository.GetAllActions();
            if (!string.IsNullOrEmpty(request?.SearchPhrase))
            {
                actions = actions.Where(action => action.Name != null && action.Name.IndexOf(request.SearchPhrase, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }

            return AutomationResponse.Success($"Found {actions.Count} actions.", new
            {
                actions = actions.Select(action => new
                {
                    id = action.Id,
                    name = action.Name,
                    description = action.Description,
                    stopOnFailure = action.StopOnFailure,
                    runMode = action.Mode.ToString(),
                    stepCount = repository.GetSteps(action.Id).Count
                }).ToArray()
            });
        }

        internal static AutomationResponse GetActionDetails(GetActionDetailsRequest request)
        {
            AutomationResponse initError = AutomationResultHelper.EnsureInit();
            if (initError != null) return initError;
            if (request == null)
            {
                return AutomationResponse.Error("Request is required.", errorCode: "invalid_input");
            }

            SqliteActionRepository repository = new SqliteActionRepository();
            ActionDefinition action = repository.GetAction(request.ActionId);
            if (action == null)
            {
                return AutomationResponse.Error($"Action with ID {request.ActionId} not found.", errorCode: "not_found");
            }

            List<ActionStepDefinition> steps = repository.GetSteps(request.ActionId);
            return AutomationResponse.Success($"Action '{action.Name}' details retrieved.", new
            {
                action = new
                {
                    id = action.Id,
                    name = action.Name,
                    description = action.Description,
                    stopOnFailure = action.StopOnFailure,
                    runMode = action.Mode.ToString(),
                    steps = steps.Select(step =>
                    {
                        ActionStep stepType = ActionStepRegistry.GetStep(step.Key);
                        return new
                        {
                            id = step.Id,
                            key = step.Key,
                            orderIndex = step.OrderIndex,
                            stepName = stepType?.Name,
                            stepDescription = stepType?.Description,
                            category = stepType?.Category.ToString(),
                            parameterValues = step.Values?.Select((value, index) =>
                            {
                                StepParameter parameter = stepType != null && index < stepType.Parameters.Count ? stepType.Parameters[index] : null;
                                return new
                                {
                                    name = parameter?.Name,
                                    stringValue = value.stringValue,
                                    intValue = value.intValue,
                                    boolValue = value.boolValue
                                };
                            }).ToArray()
                        };
                    }).ToArray()
                }
            });
        }

        internal static AutomationResponse CreateAction(CreateActionRequest request)
        {
            AutomationResponse initError = AutomationResultHelper.EnsureInit();
            if (initError != null) return initError;
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
            {
                return AutomationResponse.Error("Name is required.", errorCode: "invalid_input");
            }
            string name = request.Name.Trim();
            if (!TryParseRunMode(request.RunMode, out ActionDefinition.RunMode mode, out AutomationResponse modeError)) return modeError;

            AutomationResponse confirmation = AutomationMutationGuard.RequireConfirmation(
                "create_action",
                request.DryRun,
                request.ConfirmationToken,
                new {Name = name, request.Description, request.StopOnFailure, runMode = mode.ToString()},
                name, request.Description, request.StopOnFailure.ToString(), mode.ToString());
            if (confirmation != null) return confirmation;

            SqliteActionRepository repository = new SqliteActionRepository();
            ActionDefinition action = repository.SaveAction(new ActionDefinition
            {
                Name = name,
                Description = request.Description,
                StopOnFailure = request.StopOnFailure,
                Mode = mode
            });
            AI.Actions.Init(true);
            return AutomationResponse.Success($"Action '{name}' created.", new {actionId = action.Id});
        }

        internal static AutomationResponse UpdateAction(UpdateActionRequest request)
        {
            AutomationResponse initError = AutomationResultHelper.EnsureInit();
            if (initError != null) return initError;
            if (request == null)
            {
                return AutomationResponse.Error("Request is required.", errorCode: "invalid_input");
            }
            if (request.StopOnFailure < -1 || request.StopOnFailure > 1)
            {
                return AutomationResponse.Error("StopOnFailure must be -1, 0, or 1.", errorCode: "invalid_input");
            }
            if (request.Name != null && request.Name.Length > 0 && string.IsNullOrWhiteSpace(request.Name))
            {
                return AutomationResponse.Error("Name cannot contain only whitespace.", errorCode: "invalid_input");
            }

            SqliteActionRepository repository = new SqliteActionRepository();
            ActionDefinition action = repository.GetAction(request.ActionId);
            if (action == null)
            {
                return AutomationResponse.Error($"Action with ID {request.ActionId} not found.", errorCode: "not_found");
            }

            ActionDefinition.RunMode nextMode = action.Mode;
            if (!string.IsNullOrEmpty(request.RunMode) && !TryParseRunMode(request.RunMode, out nextMode, out AutomationResponse modeError)) return modeError;
            string nextName = !string.IsNullOrEmpty(request.Name) ? request.Name.Trim() : action.Name;
            string nextDescription = request.Description ?? action.Description;
            bool nextStopOnFailure = request.StopOnFailure >= 0 ? request.StopOnFailure != 0 : action.StopOnFailure;

            AutomationResponse confirmation = AutomationMutationGuard.RequireConfirmation(
                "update_action",
                request.DryRun,
                request.ConfirmationToken,
                new
                {
                    actionId = action.Id,
                    before = new {action.Name, action.Description, action.StopOnFailure, runMode = action.Mode.ToString()},
                    after = new {name = nextName, description = nextDescription, stopOnFailure = nextStopOnFailure, runMode = nextMode.ToString()}
                },
                action.Id.ToString(), action.Name, action.Description, action.StopOnFailure.ToString(), action.Mode.ToString(), nextName, nextDescription, nextStopOnFailure.ToString(), nextMode.ToString());
            if (confirmation != null) return confirmation;

            action.Name = nextName;
            action.Description = nextDescription;
            action.StopOnFailure = nextStopOnFailure;
            action.Mode = nextMode;
            repository.SaveAction(action);
            AI.Actions.Init(true);
            return AutomationResponse.Success($"Action '{action.Name}' updated.");
        }

        internal static AutomationResponse DeleteAction(DeleteActionRequest request)
        {
            AutomationResponse initError = AutomationResultHelper.EnsureInit();
            if (initError != null) return initError;
            if (request == null)
            {
                return AutomationResponse.Error("Request is required.", errorCode: "invalid_input");
            }

            SqliteActionRepository repository = new SqliteActionRepository();
            ActionDefinition action = repository.GetAction(request.ActionId);
            if (action == null)
            {
                return AutomationResponse.Error($"Action with ID {request.ActionId} not found.", errorCode: "not_found");
            }
            List<ActionStepDefinition> steps = repository.GetSteps(request.ActionId);
            object stepPreview = steps.Select(step => new {step.Id, step.Key, step.OrderIndex, step.Enabled, parameterValues = step.Values}).ToArray();
            string actionState = SerializeActionState(action, steps);
            AutomationResponse confirmation = AutomationMutationGuard.RequireConfirmation(
                "delete_action",
                request.DryRun,
                request.ConfirmationToken,
                new {actionId = action.Id, actionName = action.Name, action.Description, action.StopOnFailure, runMode = action.Mode.ToString(), steps = stepPreview},
                actionState);
            if (confirmation != null) return confirmation;

            repository.DeleteAction(request.ActionId);
            AI.Actions.Init(true);
            return AutomationResponse.Success($"Action '{action.Name}' deleted.");
        }

        internal static async Task<AutomationResponse> RunAction(RunActionRequest request)
        {
            AutomationResponse initError = AutomationResultHelper.EnsureInit();
            if (initError != null) return initError;
            if (request == null)
            {
                return AutomationResponse.Error("Request is required.", errorCode: "invalid_input");
            }

            SqliteActionRepository repository = new SqliteActionRepository();
            ActionDefinition action = repository.GetAction(request.ActionId);
            if (action == null)
            {
                return AutomationResponse.Error($"Action with ID {request.ActionId} not found.", errorCode: "not_found");
            }
            List<ActionStepDefinition> steps = repository.GetSteps(request.ActionId);
            object stepPreview = steps.Select(step => new {step.Id, step.Key, step.OrderIndex, step.Enabled, parameterValues = step.Values}).ToArray();
            string actionState = SerializeActionState(action, steps);
            AutomationResponse confirmation = AutomationMutationGuard.RequireConfirmation(
                "run_action",
                request.DryRun,
                request.ConfirmationToken,
                new {actionId = action.Id, actionName = action.Name, action.StopOnFailure, stepCount = steps.Count, steps = stepPreview},
                actionState);
            if (confirmation != null) return confirmation;

            try
            {
                ActionRunner runner = new ActionRunner(repository);
                ActionRunResult result = await runner.RunAction(request.ActionId);
                object data = new {stepsExecuted = result.StepsExecuted, stepsFailed = result.StepsFailed};
                return result.Success
                    ? AutomationResponse.Success($"Action '{action.Name}' completed successfully.", data)
                    : AutomationResponse.Error(result.Error ?? $"Action '{action.Name}' failed.", data);
            }
            catch (Exception exception)
            {
                return AutomationResponse.Error($"Error running action '{action.Name}': {exception.Message}");
            }
        }

        private static bool TryParseRunMode(string value, out ActionDefinition.RunMode mode, out AutomationResponse error)
        {
            mode = ActionDefinition.RunMode.Manual;
            error = null;
            if (string.IsNullOrEmpty(value)) return true;
            if (AutomationInputValidator.TryParseDefinedEnum(value, out mode)) return true;

            error = AutomationResponse.Error($"Invalid RunMode '{value}'. Use 'Manual' or 'AtInstallation'.", errorCode: "invalid_input");
            return false;
        }

        private static string SerializeActionState(ActionDefinition action, IEnumerable<ActionStepDefinition> steps)
        {
            return JsonConvert.SerializeObject(new
            {
                action.Id,
                action.Name,
                action.Description,
                action.StopOnFailure,
                mode = action.Mode.ToString(),
                steps = steps.OrderBy(step => step.OrderIndex).ThenBy(step => step.Id).Select(step => new
                {
                    step.Id,
                    step.Key,
                    step.OrderIndex,
                    step.Enabled,
                    step.Values
                }).ToArray()
            });
        }
    }
}
