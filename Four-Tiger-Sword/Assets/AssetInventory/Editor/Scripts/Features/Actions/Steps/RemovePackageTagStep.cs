using Automator;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AssetInventory
{
    /// <summary>Removes a configured tag from every package in the current Automator action context.</summary>
    [Serializable]
    public sealed class RemovePackageTagStep : TagActionStepBase
    {
        public RemovePackageTagStep()
        {
            Key = "RemovePackageTag";
            Name = "Remove Package Tag";
            Description = "Remove package tags from packages. Requires LoadAssetsStep to be executed first.";
            Category = ActionCategory.Actions;
        }

        protected override List<AssetInfo> GetTargetItems(List<ParameterValue> parameters)
        {
            return AssetInventoryActionContext.GetAssetsForStep();
        }

        protected override void ApplyTags(List<AssetInfo> items, List<string> tags)
        {
            foreach (string tag in tags)
            {
                Tagging.RemovePackageAssignments(items, tag, byUser: false);
            }
        }
    }
}
