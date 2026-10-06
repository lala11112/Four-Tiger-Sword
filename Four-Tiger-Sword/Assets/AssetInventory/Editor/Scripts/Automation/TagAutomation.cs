using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetInventory.Automation
{
    internal static class TagAutomation
    {
        internal enum TagAction
        {
            Add,
            Remove
        }

        internal sealed class ListTagsRequest
        {
            public string SearchPhrase { get; set; }
        }

        internal sealed class TagPackageRequest
        {
            public int PackageId { get; set; }
            public string TagName { get; set; }
            public string Action { get; set; }
            public bool DryRun { get; set; } = true;
            public string ConfirmationToken { get; set; }
        }

        internal sealed class TagAssetFileRequest
        {
            public int FileId { get; set; }
            public string TagName { get; set; }
            public string Action { get; set; }
            public bool DryRun { get; set; } = true;
            public string ConfirmationToken { get; set; }
        }

        internal static AutomationResponse ListTags(ListTagsRequest request)
        {
            AutomationResponse initError = AutomationResultHelper.EnsureInit();
            if (initError != null) return initError;

            List<Tag> tags = DBAdapter.DB.Table<Tag>().ToList();
            if (!string.IsNullOrEmpty(request?.SearchPhrase))
            {
                tags = tags.Where(tag => tag.Name.IndexOf(request.SearchPhrase, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }

            return AutomationResponse.Success($"Found {tags.Count} tags.", new
            {
                tags = tags.Select(tag => new
                {
                    id = tag.Id,
                    name = tag.Name,
                    color = tag.Color,
                    parentId = tag.ParentId
                }).ToArray()
            });
        }

        internal static AutomationResponse TagPackage(TagPackageRequest request)
        {
            AutomationResponse initError = AutomationResultHelper.EnsureInit();
            if (initError != null) return initError;
            if (request == null || string.IsNullOrWhiteSpace(request.TagName))
            {
                return AutomationResponse.Error("TagName is required.", errorCode: "invalid_input");
            }
            if (!AutomationInputValidator.TryParseDefinedEnum(request.Action, out TagAction action))
            {
                return AutomationResponse.Error("Action must be 'Add' or 'Remove'.", errorCode: "invalid_input");
            }
            string tagName = request.TagName.Trim();

            AssetInfo info = Assets.GetPackage(request.PackageId);
            if (info == null)
            {
                return AutomationResponse.Error($"Package with ID {request.PackageId} not found.", errorCode: "not_found");
            }

            List<TagInfo> packageTags = Tagging.GetPackageTags(request.PackageId);
            TagInfo assignedTag = packageTags?.FirstOrDefault(tag => tag.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
            if (action == TagAction.Remove && assignedTag == null)
            {
                return AutomationResponse.Error($"Tag '{tagName}' is not assigned to package '{info.GetDisplayName()}'.", errorCode: "not_found");
            }

            AutomationResponse confirmation = AutomationMutationGuard.RequireConfirmation(
                "tag_package",
                request.DryRun,
                request.ConfirmationToken,
                new {packageId = request.PackageId, packageName = info.GetDisplayName(), tagName, action = action.ToString(), currentlyAssigned = assignedTag != null},
                request.PackageId.ToString(), info.GetDisplayName(), tagName, action.ToString(), (assignedTag != null).ToString());
            if (confirmation != null) return confirmation;

            if (action == TagAction.Add)
            {
                bool added = Tagging.AddAssignment(info, tagName, TagAssignment.Target.Package, true);
                return AutomationResponse.Success(added ? $"Tag '{tagName}' added to package '{info.GetDisplayName()}'." : $"Tag '{tagName}' was already assigned to package '{info.GetDisplayName()}'.");
            }

            Tagging.RemoveAssignment(info, assignedTag, true, true);
            return AutomationResponse.Success($"Tag '{tagName}' removed from package '{info.GetDisplayName()}'.");
        }

        internal static AutomationResponse TagAssetFile(TagAssetFileRequest request)
        {
            AutomationResponse initError = AutomationResultHelper.EnsureInit();
            if (initError != null) return initError;
            if (request == null || string.IsNullOrWhiteSpace(request.TagName))
            {
                return AutomationResponse.Error("TagName is required.", errorCode: "invalid_input");
            }
            if (!AutomationInputValidator.TryParseDefinedEnum(request.Action, out TagAction action))
            {
                return AutomationResponse.Error("Action must be 'Add' or 'Remove'.", errorCode: "invalid_input");
            }
            string tagName = request.TagName.Trim();

            AssetFile file = DBAdapter.DB.Find<AssetFile>(request.FileId);
            if (file == null)
            {
                return AutomationResponse.Error($"Asset file with ID {request.FileId} not found.", errorCode: "not_found");
            }
            Asset asset = DBAdapter.DB.Find<Asset>(file.AssetId);
            if (asset == null)
            {
                return AutomationResponse.Error($"Parent package for file ID {request.FileId} not found.", errorCode: "not_found");
            }

            AssetInfo info = new AssetInfo().CopyFrom(asset);
            info.Id = file.Id;
            info.AssetId = file.AssetId;
            List<TagInfo> fileTags = Tagging.GetAssetTags(request.FileId);
            TagInfo assignedTag = fileTags?.FirstOrDefault(tag => tag.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
            if (action == TagAction.Remove && assignedTag == null)
            {
                return AutomationResponse.Error($"Tag '{tagName}' is not assigned to file '{file.FileName}'.", errorCode: "not_found");
            }

            AutomationResponse confirmation = AutomationMutationGuard.RequireConfirmation(
                "tag_asset_file",
                request.DryRun,
                request.ConfirmationToken,
                new {fileId = request.FileId, fileName = file.FileName, packageId = file.AssetId, tagName, action = action.ToString(), currentlyAssigned = assignedTag != null},
                request.FileId.ToString(), file.AssetId.ToString(), file.FileName, tagName, action.ToString(), (assignedTag != null).ToString());
            if (confirmation != null) return confirmation;

            if (action == TagAction.Add)
            {
                bool added = Tagging.AddAssignment(info, tagName, TagAssignment.Target.Asset, true);
                return AutomationResponse.Success(added ? $"Tag '{tagName}' added to file '{file.FileName}'." : $"Tag '{tagName}' was already assigned to file '{file.FileName}'.");
            }

            Tagging.RemoveAssignment(info, assignedTag, true, true);
            return AutomationResponse.Success($"Tag '{tagName}' removed from file '{file.FileName}'.");
        }
    }
}
