using System.Threading.Tasks;
using AssetInventory.Automation;
using Unity.Pipeline.Commands;

namespace AssetInventory.Integration.UnityPipeline
{
    public static class PipelineActionCommands
    {
        [CliCommand("asset_inventory_list_actions", "List custom Asset Inventory automation actions. Read-only.", Tags = new[] {"asset-inventory", "actions"})]
        public static AssetInventoryCommandResult ListActions(
            [CliArg("search_phrase", "Filter actions by name.")] string searchPhrase = null)
        {
            return PipelineResultAdapter.Convert(ActionAutomation.ListActions(new ActionAutomation.ListActionsRequest {SearchPhrase = searchPhrase}));
        }

        [CliCommand("asset_inventory_get_action_details", "Get one action and all of its configured steps. Read-only.", Tags = new[] {"asset-inventory", "actions"})]
        public static AssetInventoryCommandResult GetActionDetails(
            [CliArg("action_id", "Action ID.", Required = true)] int actionId)
        {
            return PipelineResultAdapter.Convert(ActionAutomation.GetActionDetails(new ActionAutomation.GetActionDetailsRequest {ActionId = actionId}));
        }

        [CliCommand("asset_inventory_create_action", "Create an automation action. Mutating and dry-run by default.", Tags = new[] {"asset-inventory", "actions"})]
        public static AssetInventoryCommandResult CreateAction(
            [CliArg("name", "Action name.", Required = true)] string name,
            [CliArg("description", "Description of what the action does.")] string description = null,
            [CliArg("stop_on_failure", "Stop on step failure.")] bool stopOnFailure = true,
            [CliArg("run_mode", "Manual or AtInstallation.")] string runMode = "Manual",
            [CliArg("dry_run", "Preview the mutation without executing it.")] bool dryRun = true,
            [CliArg("confirmation_token", "Token returned by the matching dry run.")] string confirmationToken = null)
        {
            return PipelineResultAdapter.Convert(ActionAutomation.CreateAction(new ActionAutomation.CreateActionRequest
            {
                Name = name,
                Description = description,
                StopOnFailure = stopOnFailure,
                RunMode = runMode,
                DryRun = dryRun,
                ConfirmationToken = confirmationToken
            }));
        }

        [CliCommand("asset_inventory_update_action", "Update action properties. Mutating and dry-run by default.", Tags = new[] {"asset-inventory", "actions"})]
        public static AssetInventoryCommandResult UpdateAction(
            [CliArg("action_id", "Action ID.", Required = true)] int actionId,
            [CliArg("name", "New name. Empty keeps the current name.")] string name = null,
            [CliArg("description", "New description. Omit to keep the current description.")] string description = null,
            [CliArg("stop_on_failure", "1 stops on failure, 0 continues, and -1 keeps the current value.")] int stopOnFailure = -1,
            [CliArg("run_mode", "Manual or AtInstallation. Empty keeps the current mode.")] string runMode = null,
            [CliArg("dry_run", "Preview the mutation without executing it.")] bool dryRun = true,
            [CliArg("confirmation_token", "Token returned by the matching dry run.")] string confirmationToken = null)
        {
            return PipelineResultAdapter.Convert(ActionAutomation.UpdateAction(new ActionAutomation.UpdateActionRequest
            {
                ActionId = actionId,
                Name = name,
                Description = description,
                StopOnFailure = stopOnFailure,
                RunMode = runMode,
                DryRun = dryRun,
                ConfirmationToken = confirmationToken
            }));
        }

        [CliCommand("asset_inventory_delete_action", "Delete an action and its steps. Destructive and dry-run by default.", Tags = new[] {"asset-inventory", "actions"})]
        public static AssetInventoryCommandResult DeleteAction(
            [CliArg("action_id", "Action ID.", Required = true)] int actionId,
            [CliArg("dry_run", "Preview the deletion without executing it.")] bool dryRun = true,
            [CliArg("confirmation_token", "Token returned by the matching dry run.")] string confirmationToken = null)
        {
            return PipelineResultAdapter.Convert(ActionAutomation.DeleteAction(new ActionAutomation.DeleteActionRequest
            {
                ActionId = actionId,
                DryRun = dryRun,
                ConfirmationToken = confirmationToken
            }));
        }

        [CliCommand("asset_inventory_run_action", "Execute an action's configured steps. Mutating and dry-run by default.", Tags = new[] {"asset-inventory", "actions"})]
        public static async Task<AssetInventoryCommandResult> RunAction(
            [CliArg("action_id", "Action ID.", Required = true)] int actionId,
            [CliArg("dry_run", "Preview the action execution without running it.")] bool dryRun = true,
            [CliArg("confirmation_token", "Token returned by the matching dry run.")] string confirmationToken = null)
        {
            AutomationResponse response = await ActionAutomation.RunAction(new ActionAutomation.RunActionRequest
            {
                ActionId = actionId,
                DryRun = dryRun,
                ConfirmationToken = confirmationToken
            });
            return PipelineResultAdapter.Convert(response);
        }

        [CliCommand("asset_inventory_list_action_step_types", "List available action step types and parameters. Read-only.", Tags = new[] {"asset-inventory", "actions"})]
        public static AssetInventoryCommandResult ListActionStepTypes(
            [CliArg("category", "FilesAndFolders, Importing, Actions, Settings, or Misc.")] string category = null)
        {
            return PipelineResultAdapter.Convert(ActionStepAutomation.ListActionStepTypes(new ActionStepAutomation.ListActionStepTypesRequest {Category = category}));
        }

        [CliCommand("asset_inventory_add_action_step", "Add a configured step to an action. Mutating and dry-run by default.", Tags = new[] {"asset-inventory", "actions"})]
        public static AssetInventoryCommandResult AddActionStep(
            [CliArg("action_id", "Action ID.", Required = true)] int actionId,
            [CliArg("step_key", "Step key returned by list_action_step_types.", Required = true)] string stepKey,
            [CliArg("order_index", "Zero-based position. -1 appends.")] int orderIndex = -1,
            [CliArg("parameter_values", "JSON object mapping parameter names to string, integer, or Boolean values.")] string parameterValues = null,
            [CliArg("dry_run", "Preview the mutation without executing it.")] bool dryRun = true,
            [CliArg("confirmation_token", "Token returned by the matching dry run.")] string confirmationToken = null)
        {
            return PipelineResultAdapter.Convert(ActionStepAutomation.AddActionStep(new ActionStepAutomation.AddActionStepRequest
            {
                ActionId = actionId,
                StepKey = stepKey,
                OrderIndex = orderIndex,
                ParameterValues = parameterValues,
                DryRun = dryRun,
                ConfirmationToken = confirmationToken
            }));
        }

        [CliCommand("asset_inventory_remove_action_step", "Remove a configured action step. Destructive and dry-run by default.", Tags = new[] {"asset-inventory", "actions"})]
        public static AssetInventoryCommandResult RemoveActionStep(
            [CliArg("step_id", "Step ID returned by get_action_details.", Required = true)] int stepId,
            [CliArg("dry_run", "Preview the deletion without executing it.")] bool dryRun = true,
            [CliArg("confirmation_token", "Token returned by the matching dry run.")] string confirmationToken = null)
        {
            return PipelineResultAdapter.Convert(ActionStepAutomation.RemoveActionStep(new ActionStepAutomation.RemoveActionStepRequest
            {
                StepId = stepId,
                DryRun = dryRun,
                ConfirmationToken = confirmationToken
            }));
        }

        [CliCommand("asset_inventory_update_action_step", "Update an action step's position or parameters. Mutating and dry-run by default.", Tags = new[] {"asset-inventory", "actions"})]
        public static AssetInventoryCommandResult UpdateActionStep(
            [CliArg("action_id", "Action ID.", Required = true)] int actionId,
            [CliArg("step_id", "Step ID.", Required = true)] int stepId,
            [CliArg("order_index", "New zero-based position. -1 keeps the current position.")] int orderIndex = -1,
            [CliArg("parameter_values", "JSON object containing only parameter values to update.")] string parameterValues = null,
            [CliArg("dry_run", "Preview the mutation without executing it.")] bool dryRun = true,
            [CliArg("confirmation_token", "Token returned by the matching dry run.")] string confirmationToken = null)
        {
            return PipelineResultAdapter.Convert(ActionStepAutomation.UpdateActionStep(new ActionStepAutomation.UpdateActionStepRequest
            {
                ActionId = actionId,
                StepId = stepId,
                OrderIndex = orderIndex,
                ParameterValues = parameterValues,
                DryRun = dryRun,
                ConfirmationToken = confirmationToken
            }));
        }
    }
}
