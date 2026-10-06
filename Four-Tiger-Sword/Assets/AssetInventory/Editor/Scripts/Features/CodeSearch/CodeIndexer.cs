using System;
using System.Threading.Tasks;

namespace AssetInventory
{
    public sealed class CodeIndexer : AssetImporter
    {
        public async Task Run()
        {
            await CodeIndexService.UpdateIndex(this, AI.Actions.CancellationToken);
        }

        internal void SetCodeProgressCount(int count)
        {
            MainCount = Math.Max(1, count);
            if (MainProgress > MainCount) MainProgress = MainCount;
        }

        internal void SetCodeMainProgress(string caption, int progress, int count)
        {
            MainCount = Math.Max(1, count);
            SetProgress(caption, Math.Min(Math.Max(0, progress), MainCount));
        }

        internal void SetCodeSubProgress(string caption, int progress, int count)
        {
            SubCount = Math.Max(1, count);
            SubProgress = Math.Min(Math.Max(0, progress), SubCount);
            CurrentSub = caption;
        }

        internal void ClearCodeSubProgress()
        {
            CurrentSub = null;
            SubProgress = 0;
            SubCount = 0;
        }
    }
}
