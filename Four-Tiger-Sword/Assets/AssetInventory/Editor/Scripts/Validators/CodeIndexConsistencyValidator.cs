using System.Collections.Generic;
using System.Threading.Tasks;

namespace AssetInventory
{
    /// <summary>Finds orphaned chunks in the optional code-search index without changing the main catalog database.</summary>
    public sealed class CodeIndexConsistencyValidator : Validator
    {
        public CodeIndexConsistencyValidator()
        {
            Type = ValidatorType.FileSystem;
            Speed = ValidatorSpeed.Fast;
            Name = "Code Index Consistency";
            Description = "Checks the separate code search index for orphaned chunks that can be safely repaired without changing the main database.";
            FixCaption = "Repair";
        }

        /// <inheritdoc/>
        public override bool IsVisible()
        {
            return AI.Actions.CodeSearchEnabled;
        }

        /// <inheritdoc/>
        public override async Task Validate()
        {
            CurrentState = State.Scanning;
            FileIssues = new List<string>();

            if (!CodeIndexService.Exists())
            {
                CurrentState = State.Completed;
                await Task.CompletedTask;
                return;
            }

            InventoryStats.CodeIndexStatistics stats = CodeIndexService.GetStats(true);
            if (!stats.Healthy) FileIssues.Add(stats.Status);
            if (stats.OrphanedChunks > 0) FileIssues.Add($"{stats.OrphanedChunks:N0} code index chunks reference documents that no longer exist in the sidecar.");

            CurrentState = State.Completed;
        }

        /// <inheritdoc/>
        public override async Task Fix()
        {
            CurrentState = State.Fixing;
            CodeIndexService.RepairOrphans();
            await Validate();
        }
    }
}
