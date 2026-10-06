using UnityEditor;
using ImpossibleRobert.Common;
using UnityEngine;
using UnityEngine.UIElements;

#if USE_ASSET_MANAGER && USE_CLOUD_IDENTITY
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using Unity.Cloud.Assets;
using Unity.Cloud.Common;
#elif UNITY_2022_3_OR_NEWER
using UnityEditor.PackageManager;
#endif

namespace AssetInventory
{
#if UNITY_6000_7_OR_NEWER
    // Open-window counts, observer ownership, and callbacks are maintained explicitly and must persist through Play Mode.
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public partial class IndexUI
    {
        private VisualElement _nativeAssetManagerSettingsSection;
        private int _nativeAssetManagerSettingsHash;

        private VisualElement BuildNativeAssetManagerSettingsSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection();
            section.AddToClassList(SettingsAssetManagerSectionClass);
            _nativeAssetManagerSettingsSection = section;
            _nativeAssetManagerSettingsHash = int.MinValue;
            RefreshNativeAssetManagerSettingsSection(true);
            return section;
        }

        private void RefreshNativeAssetManagerSettingsSection(bool force = false)
        {
            if (_nativeAssetManagerSettingsSection == null || AI.Config == null) return;

            int hash = GetNativeAssetManagerSettingsHash();
            if (!force && hash == _nativeAssetManagerSettingsHash) return;
            _nativeAssetManagerSettingsHash = hash;
            _nativeAssetManagerSettingsSection.Clear();

            Foldout foldout = CreateNativeSettingsFoldout("Unity Asset Manager", AI.Config.showAMSettings, value =>
            {
                AI.Config.showAMSettings = value;
                AI.SaveConfig();
                RefreshNativeAssetManagerSettingsSection(true);
            });
            _nativeAssetManagerSettingsSection.Add(foldout);
            if (!AI.Config.showAMSettings) return;

            VisualElement feature = AddNativeSettingsGroup(
                foldout,
                "Integration",
                "Connect Asset Inventory to Unity Asset Manager cloud projects while keeping synchronized catalog records local and recoverable.");
            feature.Add(CreateNativeSettingsToggleRow(
                "Enable Unity Asset Manager",
                "Show Unity Asset Manager connection settings, remote package controls, and indexing actions. Existing synchronized records are preserved when disabled.",
                AI.Config.assetManagerFeatureEnabled,
                SetNativeAssetManagerFeatureEnabled));

            if (!AI.Config.assetManagerFeatureEnabled)
            {
                feature.Add(CreateNativeSettingsNote("Existing Unity Asset Manager projects, collections, and indexed files are retained while the integration is disabled."));
                return;
            }

            if (!AI.Actions.IndexAssetManager)
            {
                feature.Add(CreateNativeSettingsNote("The Unity Asset Manager action is not included in regular Run Actions updates."));
            }

            VisualElement connection = AddNativeSettingsGroup(
                foldout,
                "Cloud Connection",
                "Install the Unity Cloud packages, sign in, and review the organizations and projects included in synchronization.");
            AddNativeAssetManagerContent(connection);
        }

        private void AddNativeAssetManagerContent(VisualElement content)
        {
            Button dashboard = AssetInventoryUITK.CreateSecondaryButton("Open Cloud Dashboard", () => AI.OpenURL(AI.CLOUD_HOME_URL));
            dashboard.tooltip = "Open Unity Cloud Dashboard in the browser.";
            content.Add(dashboard);

#if !UNITY_2022_3_OR_NEWER
            content.Add(AssetInventoryUITK.CreateHelpBox("Unity Asset Manager support requires Unity 2022.3 or higher.", MessageType.Error));
#elif !USE_ASSET_MANAGER || !USE_CLOUD_IDENTITY
            content.Add(AssetInventoryUITK.CreateHelpBox("To index Unity Asset Manager content, install Unity Cloud Assets and Unity Cloud Identity.", MessageType.Error));
            content.Add(AssetInventoryUITK.CreateSecondaryButton("Install Packages", () =>
            {
                Client.AddAndRemove(new[] {"com.unity.cloud.assets@1.10.0", "com.unity.cloud.identity@1.7.0"});
            }));
#else
            if (string.IsNullOrWhiteSpace(CloudProjectSettings.accessToken))
            {
                content.Add(AssetInventoryUITK.CreateHelpBox("Please log in to Unity Cloud Identity to use Asset Manager."));
                content.Add(AssetInventoryUITK.CreatePrimaryButton("Log In", CloudProjectSettings.ShowLogin));
            }
            else
            {
                content.Add(AssetInventoryUITK.CreateKeyValueRow("Current User", CloudProjectSettings.userName));
            }

            content.Add(AssetInventoryUITK.CreateKeyValueRow("Organizations", "-All-"));
            content.Add(AssetInventoryUITK.CreateKeyValueRow("Projects", "-All-"));
#endif
        }

        private int GetNativeAssetManagerSettingsHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + AI.Config.showAMSettings.GetHashCode();
                hash = hash * 31 + AI.Config.assetManagerFeatureEnabled.GetHashCode();
                hash = hash * 31 + (AI.Actions?.IndexAssetManager ?? false).GetHashCode();
#if USE_ASSET_MANAGER && USE_CLOUD_IDENTITY
                hash = hash * 31 + (CloudProjectSettings.accessToken?.GetHashCode() ?? 0);
                hash = hash * 31 + (CloudProjectSettings.userName?.GetHashCode() ?? 0);
#endif
                return hash;
            }
        }

        private void SetNativeAssetManagerFeatureEnabled(bool enabled)
        {
            AI.Config.assetManagerFeatureEnabled = enabled;
            OnNativeOptionalFeatureChanged(() => RefreshNativeAssetManagerSettingsSection(true));
            AI.TriggerPackageRefresh();
        }

#if USE_ASSET_MANAGER && USE_CLOUD_IDENTITY
        private async void CreateCollection(AssetInfo parent, string colName)
        {
            CloudAssetManagement cam = await AI.GetCloudAssetManagement();
            await cam.SelectProjectAsync(parent.ToAsset());

            // create remote
            string path = parent.ParentInfo != null ? parent.SafeName + "/" + colName : colName;
            IAssetCollection collection = await cam.CreateAssetCollectionAsync(new CollectionPath(path));
            if (collection == null) return;

            // create local equivalent 
            Asset asset = new Asset();
            asset.AssetSource = Asset.Source.AssetManager;
            asset.SafeName = AssetUtils.GuessSafeName(collection.Descriptor.Path);
            asset.ParentId = parent.AssetId;
            asset.DisplayName = collection.Name;
            asset.Location = collection.Descriptor.Path.ToString();
            asset.OriginalLocation = parent.OriginalLocation;
            asset.OriginalLocationKey = parent.OriginalLocationKey;
            asset.CurrentState = Asset.State.Done;
            AssetImporter.Persist(asset);

            AI.TriggerPackageRefresh();
        }

        private async void DeleteCollection(AssetInfo info)
        {
            CloudAssetManagement cam = await AI.GetCloudAssetManagement();
            IAssetProject project = await cam.SelectProjectAsync(info.ToAsset());
            if (project == null) return;
            await cam.DeleteAssetCollectionAsync(info.Location);

            Assets.RemovePackage(info, true);
            AI.TriggerPackageRefresh();
        }

        private async void AddAssetsToProject(AssetInfo project, List<AssetInfo> assets)
        {
            CloudAssetManagement.IncBusyCount();

            CloudAssetManagement cam = await AI.GetCloudAssetManagement();
            await cam.SelectProjectAsync(project.ToAsset());

            IAssetCollection collection = null;
            if (!string.IsNullOrWhiteSpace(project.Location))
            {
                collection = await cam.SelectProjectAssetCollectionAsync(project.ToAsset());
            }

            List<IAsset> newAssets = new List<IAsset>();
            foreach (AssetInfo info in assets)
            {
                string path;
                if (info.DependencyState == AssetInfo.DependencyStateOptions.Unknown) await CalculateDependencies(info);
                bool folderMode = info.Dependencies.Count > 0;
                if (folderMode)
                {
                    path = IOUtils.CreateTempFolder();
                    await Assets.CopyTo(info, path, true, 0, false, true);

                    // use asset root and not temp folder name as root
                    string[] dirs = Directory.GetDirectories(path, "*.*", SearchOption.TopDirectoryOnly);
                    if (dirs.Length == 1 && Directory.Exists(dirs[0])) path = dirs[0];
                }
                else
                {
                    path = await Assets.EnsureMaterialized(info.ToAsset(), info);
                }
                if (path == null)
                {
                    Debug.LogError($"Could not materialize '{info}' for upload.");
                    continue;
                }

                // Generate new asset
                AssetType type = info.GetAMAssetType();
                List<string> tags = info.AssetTags.Select(at => at.Name).ToList();
                Dictionary<string, MetadataValue> metadata = new Dictionary<string, MetadataValue>();
                if (type == AssetType.Asset_2D)
                {
                    // TODO: default resolution field seems to be a one value pre-defined list for some reason
                }
                IAsset cloudAsset = await cam.CreateAssetAsync(type, info.FileName, null, tags, metadata);
                cam.SetSelectedAsset(cloudAsset);

                IDataset dataset = await cloudAsset.GetSourceDatasetAsync(CancellationToken.None);
                if (dataset == null) continue;

                // start in parallel
                Task previewUpload = UploadPreview(info, cloudAsset, cam);

                // for files with dependencies upload complete folder
                if (folderMode)
                {
                    if (!await cam.UploadFolderAsync(dataset, path)) continue;
                    await IOUtils.DeleteFileOrDirectory(path);
                }
                else
                {
                    IFile cloudFile = await cam.UploadFile(dataset, path);
                    if (cloudFile == null) continue;
                }
                await previewUpload;

                newAssets.Add(cloudAsset);

                // link to collection if selected
                if (collection == null) continue;
                await cam.LinkAssetToCollectionAsync(cloudAsset);
            }

            // add to project
            await UploadAssets(cam, newAssets, project.ToAsset().GetRootAsset());

            // add to collection
            if (collection != null)
            {
                await UploadAssets(cam, newAssets, project.ToAsset());
            }
            AI.TriggerPackageRefresh();

            CloudAssetManagement.DecBusyCount();
        }

        private static async Task UploadAssets(CloudAssetManagement cam, List<IAsset> newAssets, Asset project)
        {
            await AI.Actions.RunWithProgress<AssetManagerImporter>(
                ActionHandler.ACTION_ASSET_MANAGER_INDEX,
                "Uploading to Asset Manager",
                imp => imp.PersistAssetFiles(cam, newAssets, project, false));
        }

        private static async Task UploadPreview(AssetInfo info, IAsset cloudAsset, CloudAssetManagement cam)
        {
            // Upload preview if existent
            string previewFile = info.GetPreviewFile(Paths.GetPreviewFolder());
            if (File.Exists(previewFile))
            {
                IDataset previewDataset = await cloudAsset.GetPreviewDatasetAsync(CancellationToken.None);
                if (previewDataset == null) return;

                await cam.UploadFile(previewDataset, previewFile);
            }
        }

        private async void RemoveAssetsFromCollection(List<AssetInfo> assets)
        {
            CloudAssetManagement.IncBusyCount();

            CloudAssetManagement cam = await AI.GetCloudAssetManagement();
            foreach (AssetInfo info in assets)
            {
                if (info.AssetSource != Asset.Source.AssetManager) continue;

                IAssetCollection collection = await cam.SelectProjectAssetCollectionAsync(info.ToAsset());
                if (collection == null) continue;

                await cam.ListCollectionAssetsAsync();
                IAsset cloudAsset = cam.CurrentCollectionAssets.FirstOrDefault(a => a.Descriptor.AssetId.ToString() == info.Guid);
                if (cloudAsset == null) continue;

                cam.SetSelectedAsset(cloudAsset);
                await cam.UnlinkAssetFromCollectionAsync(cloudAsset);

                Assets.ForgetAssetFile(info);
            }
            AI.TriggerPackageRefresh();

            CloudAssetManagement.DecBusyCount();
        }

        private async void DeleteAssetsFromProject(List<AssetInfo> assets)
        {
            CloudAssetManagement.IncBusyCount();

            CloudAssetManagement cam = await AI.GetCloudAssetManagement();
            foreach (AssetInfo info in assets)
            {
                if (info.AssetSource != Asset.Source.AssetManager) continue;

                IAssetProject project = await cam.SelectProjectAsync(info.ToAsset());
                if (project == null) continue;

                await cam.CurrentProject.UnlinkAssetsAsync(new[] {new AssetId(info.Guid)}, CancellationToken.None);
                Assets.ForgetAssetFile(info);
            }
            AI.TriggerPackageRefresh();

            CloudAssetManagement.DecBusyCount();
        }
#endif
    }
}
