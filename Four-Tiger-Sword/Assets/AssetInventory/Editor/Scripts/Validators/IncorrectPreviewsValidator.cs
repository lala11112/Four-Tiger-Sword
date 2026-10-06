using ImpossibleRobert.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace AssetInventory
{
    /// <summary>Finds cached previews that contain error shaders or Unity's generic fallback icons.</summary>
    public sealed class IncorrectPreviewsValidator : Validator
    {
        public IncorrectPreviewsValidator()
        {
            Type = ValidatorType.DB;
            Speed = ValidatorSpeed.Slow;
            Name = "Incorrect Previews";
            Description = "Scans all previews for either pink shaders or default preview icons instead of real previews.";
            FixCaption = "Schedule Recreation";
        }

        /// <inheritdoc/>
        public override async Task Validate()
        {
            CurrentState = State.Scanning;

            string query = "select * from AssetFile where PreviewState = ? or PreviewState = ?";
            List<AssetInfo> files = DBAdapter.DB.Query<AssetInfo>(query, AssetFile.PreviewOptions.Provided, AssetFile.PreviewOptions.Custom).ToList();

            DBIssues = await GatherIssues(files);
            CurrentState = State.Completed;
        }

        /// <summary>Checks the supplied preview records for error shaders and generic fallback icons and records the affected files.</summary>
        public async Task Validate(List<AssetInfo> files)
        {
            CurrentState = State.Scanning;
            DBIssues = await GatherIssues(files);
            CurrentState = State.Completed;
        }

        /// <inheritdoc/>
        public override async Task Fix()
        {
            CurrentState = State.Fixing;

            string query = "update AssetFile set PreviewState = ? where Id = ?";
            foreach (AssetInfo info in DBIssues)
            {
                if (CancellationRequested) break;
                DBAdapter.DB.Execute(query, info.URPCompatible ? AssetFile.PreviewOptions.RedoMissing : AssetFile.PreviewOptions.Error, info.Id);
            }
            await Task.Yield();

            CurrentState = State.Idle;
        }

        private async Task<List<AssetInfo>> GatherIssues(List<AssetInfo> files)
        {
            List<AssetInfo> result = new List<AssetInfo>();

            string previewFolder = Paths.GetPreviewFolder();
            Progress = 0;
            MaxProgress = files.Count;
            ProgressId = MetaProgress.Start("Gathering incorrect previews");

            // TODO: parallelize this loop but when done currently there are many main thread required exceptions
            foreach (AssetInfo file in files)
            {
                try
                {
                    Progress++;
                    MetaProgress.Report(ProgressId, Progress, MaxProgress, file.FileName);
                    if (CancellationRequested) break;
                    if (Progress % 50 == 0) await Task.Yield();

                    string previewFile = file.GetPreviewFile(previewFolder);
                    if (!PreviewManager.IsPreviewable(previewFile, true)) continue;
                    if (!File.Exists(previewFile)) continue;

                    // scan for both issues in one go for performance
                    // use URP flag to differentiate between default cube and error shader issues
                    if (file.PreviewState == AssetFile.PreviewOptions.Provided)
                    {
                        if (PreviewManager.IsDefaultIcon(previewFile))
                        {
                            file.URPCompatible = true;
                            result.Add(file);
                            continue;
                        }
                    }
                    if (!AI.TypeGroups[AI.AssetGroup.Images].Contains(file.Type) && PreviewManager.IsErrorShader(previewFile))
                    {
                        file.URPCompatible = false;
                        result.Add(file);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Skipping validation for '{file.FileName}': {e.Message}");
                }
            }
            MetaProgress.Remove(ProgressId);

            return result;
        }
    }
}
