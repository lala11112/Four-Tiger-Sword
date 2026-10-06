using System.Threading.Tasks;

namespace AssetInventory
{
    /// <summary>Finds media records that point to the same source location.</summary>
    public sealed class DuplicateMediaIndexValidator : Validator
    {
        public DuplicateMediaIndexValidator()
        {
            Type = ValidatorType.DB;
            Name = "Duplicate Media Index";
            Description = "Checks if media files are pointing to the same location.";
            Fixable = false;
        }

        /// <inheritdoc/>
        public override async Task Validate()
        {
            CurrentState = State.Scanning;

            await Task.Yield();

            string query = "SELECT AF.* FROM AssetFile AF JOIN Asset A ON AF.AssetId = A.Id WHERE AF.Path IN (SELECT Path FROM AssetFile GROUP BY Path HAVING COUNT(Path) > 1) AND A.AssetSource = 2";
            DBIssues = DBAdapter.DB.Query<AssetInfo>(query);

            CurrentState = State.Completed;
        }

        /// <inheritdoc/>
        public override async Task Fix()
        {
            CurrentState = State.Fixing;

            await Validate();
        }
    }
}
