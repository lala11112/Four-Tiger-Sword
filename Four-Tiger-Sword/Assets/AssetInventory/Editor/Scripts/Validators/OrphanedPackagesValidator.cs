using System.Linq;
using System.Threading.Tasks;

namespace AssetInventory
{
    /// <summary>Finds custom package records whose source package file no longer exists.</summary>
    public sealed class OrphanedPackagesValidator : Validator
    {
        public OrphanedPackagesValidator()
        {
            Type = ValidatorType.DB;
            Name = "Orphaned Packages";
            Description = "Scans the database for custom packages (not from the Asset Store or a registry) where the referenced file does not exist anymore.";
            FixCaption = "Remove";
        }

        /// <inheritdoc/>
        public override async Task Validate()
        {
            CurrentState = State.Scanning;

            await Task.Yield();

            DBIssues = Assets.Load()
                .Where(a => a.ParentId == 0 && a.AssetSource != Asset.Source.AssetStorePackage && a.AssetSource != Asset.Source.RegistryPackage && !a.IsDownloaded)
                .ToList();

            CurrentState = State.Completed;
        }

        /// <inheritdoc/>
        public override async Task Fix()
        {
            CurrentState = State.Fixing;

            foreach (AssetInfo issue in DBIssues)
            {
                if (CancellationRequested) break;
                Assets.RemovePackage(issue, false);
                await Task.Yield();
            }

            await Validate();
        }
    }
}