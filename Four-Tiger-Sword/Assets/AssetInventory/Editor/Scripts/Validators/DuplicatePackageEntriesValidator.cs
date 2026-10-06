using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AssetInventory
{
    /// <summary>Finds Asset Store and custom package records that point to the same cached package file.</summary>
    public sealed class DuplicatePackageEntriesValidator : Validator
    {
        public DuplicatePackageEntriesValidator()
        {
            Type = ValidatorType.DB;
            Speed = ValidatorSpeed.Fast;
            Name = "Duplicate Package Entries";
            Description = "Finds Asset Store and custom package rows that point to the same cached package file.";
            FixCaption = "Merge";
        }

        /// <inheritdoc/>
        public override async Task Validate()
        {
            CurrentState = State.Scanning;

            await Task.Yield();

            List<Asset> issues = PackageIdentityReconciler.LoadExactDuplicateIssues();
            DBIssues = issues.Select(asset => new AssetInfo(asset)).ToList();

            CurrentState = State.Completed;
        }

        /// <inheritdoc/>
        public override async Task Fix()
        {
            CurrentState = State.Fixing;

            await Task.Yield();
            PackageIdentityReconciler.RepairExactDuplicatePackageEntries();

            await Validate();
        }
    }
}
