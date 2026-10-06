using System.Threading.Tasks;
using AssetInventory.Automation;
using Unity.AI.MCP.Editor.ToolRegistry;

namespace AssetInventory.Integration.UnityAIAssistant
{
    public static class AssistantActionTools
    {
        public sealed class ListActionsParams
        {
            [McpDescription("Filter actions by name.")]
            public string SearchPhrase { get; set; }
        }

        public sealed class GetActionDetailsParams
        {
            [McpDescription("Action ID.", Required = true)]
            public int ActionId { get; set; }
        }

        public sealed class CreateActionParams
        {
            [McpDescription("Action name.", Required = true)]
            public string Name { get; set; }

            [McpDescription("Description of what the action does.")]
            public string Description { get; set; }

            [McpDescription("Stop on step failure.", Default = true)]
            public bool StopOnFailure { get; set; } = true;

            [McpDescription("Manual or AtInstallation.", Default = "Manual")]
            public string RunMode { get; set; } = "Manual";

            [McpDescription("Preview the mutation without executing it.", Default = true)]
            public bool DryRun { get; set; } = true;

            [McpDescription("Confirmation token returned by the matching dry run.")]
            public string ConfirmationToken { get; set; }
        }

        public sealed class UpdateActionParams
        {
            [McpDescription("Action ID.", Required = true)]
            public int ActionId { get; set; }

            [McpDescription("New name. Empty keeps the current name.")]
            public string Name { get; set; }

            [McpDescription("New description. Null keeps the current description.")]
            public string Description { get; set; }

            [McpDescription("1 stops on failure, 0 continues, and -1 keeps the current value.", Default = -1)]
            public int StopOnFailure { get; set; } = -1;

            [McpDescription("Manual or AtInstallation. Empty keeps the current mode.")]
            public string RunMode { get; set; }

            [McpDescription("Preview the mutation without executing it.", Default = true)]
            public bool DryRun { get; set; } = true;

            [McpDescription("Confirmation token returned by the matching dry run.")]
            public string ConfirmationToken { get; set; }
        }

        public sealed class DeleteActionParams
        {
            [McpDescription("Action ID.", Required = true)]
            public int ActionId { get; set; }

            [McpDescription("Preview the deletion without executing it.", Default = true)]
            public bool DryRun { get; set; } = true;

            [McpDescription("Confirmation token returned by the matching dry run.")]
            public string ConfirmationToken { get; set; }
        }

        public sealed class RunActionParams
        {
            [McpDescription("Action ID.", Required = true)]
            public int ActionId { get; set; }

            [McpDescription("Preview the action execution without running it.", Default = true)]
            public bool DryRun { get; set; } = true;

            [McpDescription("Confirmation token returned by the matching dry run.")]
            public string ConfirmationToken { get; set; }
        }

        public sealed class ListActionStepTypesParams
        {
            [McpDescription("Category: FilesAndFolders, Importing, Actions, Settings, or Misc.")]
            public string Category { get; set; }
        }

        public sealed class AddActionStepParams
        {
            [McpDescription("Action ID.", Required = true)]
            public int ActionId { get; set; }

            [McpDescription("Step key returned by listActionStepTypes.", Required = true)]
            public string StepKey { get; set; }

            [McpDescription("Zero-based position. -1 appends.", Default = -1)]
            public int OrderIndex { get; set; } = -1;

            [McpDescription("JSON object mapping parameter names to string, integer, or Boolean values.")]
            public string ParameterValues { get; set; }

            [McpDescription("Preview the mutation without executing it.", Default = true)]
            public bool DryRun { get; set; } = true;

            [McpDescription("Confirmation token returned by the matching dry run.")]
            public string ConfirmationToken { get; set; }
        }

        public sealed class RemoveActionStepParams
        {
            [McpDescription("Step ID returned by getActionDetails.", Required = true)]
            public int StepId { get; set; }

            [McpDescription("Preview the deletion without executing it.", Default = true)]
            public bool DryRun { get; set; } = true;

            [McpDescription("Confirmation token returned by the matching dry run.")]
            public string ConfirmationToken { get; set; }
        }

        public sealed class UpdateActionStepParams
        {
            [McpDescription("Action ID.", Required = true)]
            public int ActionId { get; set; }

            [McpDescription("Step ID.", Required = true)]
            public int StepId { get; set; }

            [McpDescription("New zero-based position. -1 keeps the current position.", Default = -1)]
            public int OrderIndex { get; set; } = -1;

            [McpDescription("JSON object containing only parameter values to update.")]
            public string ParameterValues { get; set; }

            [McpDescription("Preview the mutation without executing it.", Default = true)]
            public bool DryRun { get; set; } = true;

            [McpDescription("Confirmation token returned by the matching dry run.")]
            public string ConfirmationToken { get; set; }
        }

        [McpTool("AssetInventory_listActions", "List custom Asset Inventory automation actions. Read-only.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Actions"})]
        public static object ListActions(ListActionsParams parameters)
        {
            parameters = parameters ?? new ListActionsParams();
            return AssistantResponseAdapter.Convert(ActionAutomation.ListActions(new ActionAutomation.ListActionsRequest {SearchPhrase = parameters.SearchPhrase}));
        }

        [McpTool("AssetInventory_getActionDetails", "Get one action and all of its configured steps. Read-only.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Actions"})]
        public static object GetActionDetails(GetActionDetailsParams parameters)
        {
            parameters = parameters ?? new GetActionDetailsParams();
            return AssistantResponseAdapter.Convert(ActionAutomation.GetActionDetails(new ActionAutomation.GetActionDetailsRequest {ActionId = parameters.ActionId}));
        }

        [McpTool("AssetInventory_createAction", "Create an automation action. Mutating and dry-run by default.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Actions"})]
        public static object CreateAction(CreateActionParams parameters)
        {
            parameters = parameters ?? new CreateActionParams();
            return AssistantResponseAdapter.Convert(ActionAutomation.CreateAction(new ActionAutomation.CreateActionRequest
            {
                Name = parameters.Name,
                Description = parameters.Description,
                StopOnFailure = parameters.StopOnFailure,
                RunMode = parameters.RunMode,
                DryRun = parameters.DryRun,
                ConfirmationToken = parameters.ConfirmationToken
            }));
        }

        [McpTool("AssetInventory_updateAction", "Update action properties. Mutating and dry-run by default.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Actions"})]
        public static object UpdateAction(UpdateActionParams parameters)
        {
            parameters = parameters ?? new UpdateActionParams();
            return AssistantResponseAdapter.Convert(ActionAutomation.UpdateAction(new ActionAutomation.UpdateActionRequest
            {
                ActionId = parameters.ActionId,
                Name = parameters.Name,
                Description = parameters.Description,
                StopOnFailure = parameters.StopOnFailure,
                RunMode = parameters.RunMode,
                DryRun = parameters.DryRun,
                ConfirmationToken = parameters.ConfirmationToken
            }));
        }

        [McpTool("AssetInventory_deleteAction", "Delete an action and its steps. Destructive and dry-run by default.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Actions"})]
        public static object DeleteAction(DeleteActionParams parameters)
        {
            parameters = parameters ?? new DeleteActionParams();
            return AssistantResponseAdapter.Convert(ActionAutomation.DeleteAction(new ActionAutomation.DeleteActionRequest
            {
                ActionId = parameters.ActionId,
                DryRun = parameters.DryRun,
                ConfirmationToken = parameters.ConfirmationToken
            }));
        }

        [McpTool("AssetInventory_runAction", "Execute an action's configured steps. Mutating and dry-run by default.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Actions"})]
        public static async Task<object> RunAction(RunActionParams parameters)
        {
            parameters = parameters ?? new RunActionParams();
            AutomationResponse response = await ActionAutomation.RunAction(new ActionAutomation.RunActionRequest
            {
                ActionId = parameters.ActionId,
                DryRun = parameters.DryRun,
                ConfirmationToken = parameters.ConfirmationToken
            });
            return AssistantResponseAdapter.Convert(response);
        }

        [McpTool("AssetInventory_listActionStepTypes", "List available action step types and parameters. Read-only.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Actions"})]
        public static object ListActionStepTypes(ListActionStepTypesParams parameters)
        {
            parameters = parameters ?? new ListActionStepTypesParams();
            return AssistantResponseAdapter.Convert(ActionStepAutomation.ListActionStepTypes(new ActionStepAutomation.ListActionStepTypesRequest {Category = parameters.Category}));
        }

        [McpTool("AssetInventory_addActionStep", "Add a configured step to an action. Mutating and dry-run by default.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Actions"})]
        public static object AddActionStep(AddActionStepParams parameters)
        {
            parameters = parameters ?? new AddActionStepParams();
            return AssistantResponseAdapter.Convert(ActionStepAutomation.AddActionStep(new ActionStepAutomation.AddActionStepRequest
            {
                ActionId = parameters.ActionId,
                StepKey = parameters.StepKey,
                OrderIndex = parameters.OrderIndex,
                ParameterValues = parameters.ParameterValues,
                DryRun = parameters.DryRun,
                ConfirmationToken = parameters.ConfirmationToken
            }));
        }

        [McpTool("AssetInventory_removeActionStep", "Remove a configured action step. Destructive and dry-run by default.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Actions"})]
        public static object RemoveActionStep(RemoveActionStepParams parameters)
        {
            parameters = parameters ?? new RemoveActionStepParams();
            return AssistantResponseAdapter.Convert(ActionStepAutomation.RemoveActionStep(new ActionStepAutomation.RemoveActionStepRequest
            {
                StepId = parameters.StepId,
                DryRun = parameters.DryRun,
                ConfirmationToken = parameters.ConfirmationToken
            }));
        }

        [McpTool("AssetInventory_updateActionStep", "Update an action step's position or parameters. Mutating and dry-run by default.", EnabledByDefault = true, Groups = new[] {"Asset Inventory/Actions"})]
        public static object UpdateActionStep(UpdateActionStepParams parameters)
        {
            parameters = parameters ?? new UpdateActionStepParams();
            return AssistantResponseAdapter.Convert(ActionStepAutomation.UpdateActionStep(new ActionStepAutomation.UpdateActionStepRequest
            {
                ActionId = parameters.ActionId,
                StepId = parameters.StepId,
                OrderIndex = parameters.OrderIndex,
                ParameterValues = parameters.ParameterValues,
                DryRun = parameters.DryRun,
                ConfirmationToken = parameters.ConfirmationToken
            }));
        }
    }
}
