using Automator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AssetInventory
{
    /// <summary>Imports every catalog package carrying a configured tag into the open Unity project.</summary>
    [Serializable]
    public sealed class InstallPackagesByTagStep : ActionStep
    {
        public InstallPackagesByTagStep()
        {
            Key = "InstallPackagesByTag";
            Name = "Install Packages By Tag";
            Description = "Install the packages from the database with the given tag.";
            Category = ActionCategory.Importing;
            Parameters.Add(new StepParameter
            {
                Name = "Tag",
                Description = "Tag assigned to the package.",
                ValueList = StepParameter.ValueType.Custom,
                LazyLoadOptions = true,
                OptionComparison = StringComparison.OrdinalIgnoreCase
            });
        }

        /// <summary>Returns current package tags as selectable values for the tag action parameter.</summary>
        public override List<Tuple<string, ParameterValue>> GetParamOptions(StepParameter param, List<ParameterValue> parameters)
        {
            if (param.Name == "Tag")
            {
                List<Tuple<string, ParameterValue>> options = new List<Tuple<string, ParameterValue>>();
                List<TagInfo> tags = Tagging.Tags?
                    .Where(t => t.TagTarget == TagAssignment.Target.Package)
                    .OrderBy(t => t.Name)
                    .ToList();

                if (tags != null)
                {
                    foreach (TagInfo tag in tags.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Select(group => group.First()))
                    {
                        options.Add(new Tuple<string, ParameterValue>(tag.Name, new ParameterValue(tag.Name)));
                    }
                }

                return options;
            }
            return base.GetParamOptions(param, parameters);
        }

        internal static bool MatchesTag(IEnumerable<TagInfo> tags, string name)
        {
            return tags.Any(tag => string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Finds every package carrying the configured tag and opens the import workflow for those packages.</summary>
        public override async Task Run(List<ParameterValue> parameters)
        {
            List<AssetInfo> infos = Assets.Load().Where(a => MatchesTag(a.PackageTags, parameters[0].stringValue)).ToList();

            bool finished = false;
            ImportUI importUI = ImportUI.ShowWindow();
            importUI.Init(infos, true, () => finished = true, false, ActionHandler.AI_ACTION_LOCK);

            while (!finished)
            {
                await Task.Yield();
            }
        }
    }
}
