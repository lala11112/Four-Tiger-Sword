using System.Collections.Generic;
using System.Threading.Tasks;

namespace AssetInventory
{
    /// <summary>Finds orphaned rows or cache data in the optional semantic-search index without changing the main catalog database.</summary>
    public sealed class SemanticIndexConsistencyValidator : Validator
    {
        public SemanticIndexConsistencyValidator()
        {
            Type = ValidatorType.FileSystem;
            Speed = ValidatorSpeed.Fast;
            Name = "Semantic Index Consistency";
            Description = "Checks the separate semantic search index for orphaned rows or cache data that can be safely repaired without changing the main database.";
            FixCaption = "Repair";
        }

        /// <inheritdoc/>
        public override bool IsVisible()
        {
            return AI.Actions.SemanticSearchEnabled;
        }

        /// <inheritdoc/>
        public override async Task Validate()
        {
            CurrentState = State.Scanning;
            FileIssues = new List<string>();

            if (!SemanticIndexService.Exists())
            {
                CurrentState = State.Completed;
                await Task.CompletedTask;
                return;
            }

            InventoryStats.SemanticIndexStatistics stats = SemanticIndexService.GetStats(true);
            if (!stats.Healthy) FileIssues.Add(stats.Status);
            if (stats.OrphanedItems > 0) FileIssues.Add($"{stats.OrphanedItems:N0} semantic index rows reference files that no longer exist in the main database.");

            CurrentState = State.Completed;
        }

        /// <inheritdoc/>
        public override async Task Fix()
        {
            CurrentState = State.Fixing;
            SemanticIndexService.RepairOrphans();
            await Validate();
        }
    }
}
