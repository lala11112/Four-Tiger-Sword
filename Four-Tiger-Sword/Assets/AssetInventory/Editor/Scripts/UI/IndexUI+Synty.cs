using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public partial class IndexUI
    {
        private const string SettingsSyntySectionClass = "ai-settings-synty-section";
        private const string SyntyImporterMarkerGuid = "588328101784f514dbc06f35a0bea6ef";

        private VisualElement _nativeSyntySettingsSection;
        private int _nativeSyntySettingsHash;
        private bool _nativeSyntyImporterInstalled;

        private VisualElement BuildNativeSyntySettingsSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection();
            section.AddToClassList(SettingsSyntySectionClass);
            _nativeSyntySettingsSection = section;
            _nativeSyntySettingsHash = int.MinValue;
            RefreshNativeSyntySettingsSection(true);
            return section;
        }

        private void RefreshNativeSyntySettingsSection(bool force = false)
        {
            if (_nativeSyntySettingsSection == null || AI.Config == null) return;
            if (force) _nativeSyntyImporterInstalled = IsSyntyImporterInstalled();

            int hash = GetNativeSyntySettingsHash();
            if (!force && hash == _nativeSyntySettingsHash) return;
            _nativeSyntySettingsHash = hash;
            _nativeSyntySettingsSection.Clear();

            Foldout foldout = CreateNativeSettingsFoldout("Synty Importer", AI.Config.showSyntySettings, value =>
            {
                AI.Config.showSyntySettings = value;
                AI.SaveConfig();
                RefreshNativeSyntySettingsSection(true);
            });
            _nativeSyntySettingsSection.Add(foldout);
            if (!AI.Config.showSyntySettings) return;

            VisualElement feature = AddNativeSettingsGroup(
                foldout,
                "Experimental Compatibility",
                "Index packages already downloaded by the installed Synty Importer. Asset Inventory does not sign in, retrieve an online catalog, or download packages.");
            feature.Add(CreateNativeSettingsToggleRow(
                "Enable Synty Importer Compatibility",
                "Adds the local Synty cache indexing action and keeps discovered packages as the dedicated Synty source.",
                AI.Config.syntyFeatureEnabled,
                SetNativeSyntyFeatureEnabled));
            feature.Add(CreateNativeSettingsValueRow(
                "Synty Importer",
                "Detection uses the installed importer's package marker and does not initialize or call its code.",
                CreateNativeSettingsValueText(_nativeSyntyImporterInstalled ? "Detected" : "Not detected")));

            if (!AI.Config.syntyFeatureEnabled)
            {
                feature.Add(CreateNativeSettingsNote("Existing Synty records, tags, indexed files, and cache files are retained while this feature is disabled."));
                return;
            }

            VisualElement cache = AddNativeSettingsGroup(
                foldout,
                "Importer Cache",
                "The cache is resolved only when this settings section is opened or the indexing action runs. It is never added to Additional Folders and is never cleared automatically.");
            cache.Add(CreateNativeSettingsFolderRow(
                "Download Cache",
                SyntyCache.DefaultRoot,
                AI.Config.syntyCacheFolder,
                value => AI.Config.syntyCacheFolder = value,
                "Select Synty Importer download cache",
                afterChange: () => RefreshNativeSyntySettingsSection(true)));

            if (!_nativeSyntyImporterInstalled)
            {
                cache.Add(AssetInventoryUITK.CreateHelpBox("Install the official Synty Importer and download packages there first. An existing cache can still be indexed after the importer is removed.", MessageType.Info));
            }
            else if (!Directory.Exists(SyntyCache.Root))
            {
                cache.Add(AssetInventoryUITK.CreateHelpBox("The download cache does not exist yet. Download at least one package in the Synty Importer before indexing.", MessageType.Info));
            }

            VisualElement metadata = AddNativeSettingsGroup(
                foldout,
                "Metadata",
                "Downloaded Unity packages can contain a matching Unity Asset Store product ID.");
            metadata.Add(CreateNativeSettingsToggleRow(
                "Link Unity Asset Store Metadata",
                "Use embedded Unity product IDs so the standard Asset Store details action can add matching descriptions, ratings, reviews, and media.",
                AI.Config.syntyLinkAssetStoreMetadata,
                SetNativeSyntyAssetStoreLink));
            metadata.Add(CreateNativeSettingsNote("Run Index Synty Importer Cache from Update Actions after downloading or updating packages in the Synty Importer."));
        }

        private int GetNativeSyntySettingsHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + AI.Config.showSyntySettings.GetHashCode();
                hash = hash * 31 + AI.Config.syntyFeatureEnabled.GetHashCode();
                hash = hash * 31 + AI.Config.syntyLinkAssetStoreMetadata.GetHashCode();
                hash = hash * 31 + (AI.Config.syntyCacheFolder?.GetHashCode() ?? 0);
                hash = hash * 31 + _nativeSyntyImporterInstalled.GetHashCode();
                if (AI.Config.showSyntySettings && AI.Config.syntyFeatureEnabled)
                {
                    hash = hash * 31 + Directory.Exists(SyntyCache.Root).GetHashCode();
                }
                return hash;
            }
        }

        private void SetNativeSyntyFeatureEnabled(bool enabled)
        {
            AI.Config.syntyFeatureEnabled = enabled;
            OnNativeOptionalFeatureChanged(() => RefreshNativeSyntySettingsSection(true));
        }

        private void SetNativeSyntyAssetStoreLink(bool enabled)
        {
            AI.Config.syntyLinkAssetStoreMetadata = enabled;
            if (!enabled) SyntyCacheImporter.ClearAssetStoreLinkMetadata();
            AI.SaveConfig();
            AI.TriggerPackageRefresh();
            RefreshNativeSyntySettingsSection(true);
        }

        private static bool IsSyntyImporterInstalled()
        {
            return !string.IsNullOrWhiteSpace(AssetDatabase.GUIDToAssetPath(SyntyImporterMarkerGuid));
        }
    }
}
