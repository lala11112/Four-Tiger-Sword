using System.Threading.Tasks;

namespace AssetInventory
{
    public sealed class SemanticIndexer : AssetImporter
    {
        public async Task Run()
        {
            await SemanticIndexService.UpdateAssetIndex(this, AI.Actions.CancellationToken);
        }

        internal void SetSemanticProgressCount(int count)
        {
            MainCount = count;
        }
    }
}
