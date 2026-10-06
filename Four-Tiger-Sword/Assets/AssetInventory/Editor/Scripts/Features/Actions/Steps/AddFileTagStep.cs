using Automator;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AssetInventory
{
    /// <summary>Assigns a configured tag to every indexed file in the current Automator action context.</summary>
    [Serializable]
    public sealed class AddFileTagStep : TagActionStepBase
    {
        public AddFileTagStep()
        {
            Key = "AddFileTag";
            Name = "Add File Tag";
            Description = "Add file tags to files. Requires LoadFilesStep to be executed first.";
            Category = ActionCategory.Actions;
        }

        protected override List<AssetInfo> GetTargetItems(List<ParameterValue> parameters)
        {
            return AssetInventoryActionContext.GetFilesForStep();
        }

        protected override void ApplyTags(List<AssetInfo> items, List<string> tags)
        {
            foreach (string tag in tags)
            {
                Tagging.AddAssignments(items, tag, TagAssignment.Target.Asset, byUser: false);
            }
        }
    }
}
