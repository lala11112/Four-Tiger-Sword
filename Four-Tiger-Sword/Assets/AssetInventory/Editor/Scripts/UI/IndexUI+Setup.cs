using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
#if !USE_VECTOR_GRAPHICS || !USE_PSD_IMPORTER || !USE_TUTORIALS || !USE_SHADER_GRAPH || (!USE_GLTF_IMPORTER && !USE_KHRONOS_UNITY_GLTF) || (!USE_TEXTMESHPRO && !UNITY_2023_2_OR_NEWER)
using UnityEditor.PackageManager;
#endif
using UnityEngine;
using System.IO;
using ImpossibleRobert.Common;
using System;
using System.Linq;
using Brain;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public partial class IndexUI
    {
        private const string SetupRootClass = "ai-setup-root";
        private const string SetupSidebarClass = "ai-setup-sidebar";
        private const string SetupSidebarTitleClass = "ai-setup-sidebar-title";
        private const string SetupStepButtonClass = "ai-setup-step-button";
        private const string SetupStepButtonActiveClass = "ai-setup-step-button-active";
        private const string SetupStepButtonCompletedClass = "ai-setup-step-button-completed";
        private const string SetupLogoClass = "ai-setup-logo";
        private const string SetupContentClass = "ai-setup-content";
        private const string SetupHeaderClass = "ai-setup-header";
        private const string SetupTitleClass = "ai-setup-title";
        private const string SetupDescriptionClass = "ai-setup-description";
        private const string SetupPageBodyClass = "ai-setup-page-body";
        private const string SetupFooterClass = "ai-setup-footer";
        internal const string SetupNativeBodyClass = "ai-setup-native-body";
        internal const string SetupNativeCopyClass = "ai-setup-native-copy";
        internal const string SetupSampleImageClass = "ai-setup-sample-image";
        internal const string SetupNextListClass = "ai-setup-next-list";
        internal const string SetupNextItemClass = "ai-setup-next-item";
        internal const string SetupNextItemNumberClass = "ai-setup-next-item-number";
        internal const string SetupNextItemCopyClass = "ai-setup-next-item-copy";
        internal const string SetupNextItemTitleClass = "ai-setup-next-item-title";
        internal const string SetupNextItemDescriptionClass = "ai-setup-next-item-description";
        internal const string SetupOptionGroupClass = "ai-setup-option-group";
        internal const string SetupToggleClass = "ai-setup-toggle";
        internal const string SetupToggleControlClass = "ai-setup-toggle-control";
        internal const string SetupToggleLabelClass = "ai-setup-toggle-label";
        internal const string SetupInlineCaptionClass = "ai-setup-inline-caption";
        internal const string SetupInlineRowClass = "ai-setup-inline-row";
        internal const string SetupInlineFieldClass = "ai-setup-inline-field";
        internal const string SetupInlineUnitClass = "ai-setup-inline-unit";
        internal const string SetupIndexingChoiceGridClass = "ai-setup-indexing-choice-grid";
        internal const string SetupIndexingChoiceCardClass = "ai-setup-indexing-choice-card";
        internal const string SetupSelectiveGuidanceClass = "ai-setup-selective-guidance";
        internal const string SetupBackendSelectorClass = "ai-setup-backend-selector";
        internal const string SetupBackendButtonClass = "ai-setup-backend-button";
        internal const string SetupBackendButtonActiveClass = "ai-setup-backend-button-active";
        internal const string SetupModelActionClass = "ai-setup-model-action";
        internal const string SetupStorageTableScrollClass = "ai-setup-storage-table-scroll";
        internal const string SetupStorageTableClass = "ai-setup-storage-table";
        internal const string SetupStorageTableRowClass = "ai-setup-storage-table-row";
        internal const string SetupStorageTableHeaderClass = "ai-setup-storage-table-header";
        internal const string SetupStorageTableAlternateRowClass = "ai-setup-storage-table-row-alternate";
        internal const string SetupStorageTableCellClass = "ai-setup-storage-table-cell";
        internal const string SetupStorageTableRecommendedCellClass = "ai-setup-storage-table-cell-recommended";
        internal const string SetupDriveRowClass = "ai-setup-drive-row";
        internal const string SetupDriveHeaderClass = "ai-setup-drive-header";
        internal const string SetupDriveNameClass = "ai-setup-drive-name";
        internal const string SetupDriveSpaceClass = "ai-setup-drive-space";
        internal const string SetupDriveCurrentClass = "ai-setup-drive-current";
        internal const string SetupDriveBarClass = "ai-setup-drive-bar";
        internal const string SetupDriveBarFillClass = "ai-setup-drive-bar-fill";
        private static readonly CommonUITK.WizardShellClasses SetupWizardClasses = new CommonUITK.WizardShellClasses
        {
            RootClass = SetupRootClass,
            SidebarClass = SetupSidebarClass,
            SidebarTitleClass = SetupSidebarTitleClass,
            StepButtonClass = SetupStepButtonClass,
            ActiveStepClass = SetupStepButtonActiveClass,
            CompletedStepClass = SetupStepButtonCompletedClass,
            LogoClass = SetupLogoClass,
            ContentClass = SetupContentClass,
            HeaderClass = SetupHeaderClass,
            TitleClass = SetupTitleClass,
            DescriptionClass = SetupDescriptionClass,
            BodyClass = SetupPageBodyClass,
            FooterClass = SetupFooterClass
        };

        private List<IWizardPage> _wizardPages;
        private int _nativeSetupStateHash = int.MinValue;

        private void RefreshNativeSetupBody()
        {
            InitializeWizardPages();
            if (_nativeSetupBody == null || _wizardPages == null || _wizardPages.Count == 0) return;

            int stateHash = GetNativeSetupStateHash();
            if (_nativeSetupBody.childCount == 0 || _nativeSetupStateHash != stateHash)
            {
                RebuildNativeSetupBody();
                _nativeSetupStateHash = stateHash;
            }

        }

        private int GetNativeSetupStateHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + AI.Config.wizardCurrentPage;
                hash = hash * 31 + (_wizardPages?.Count ?? 0);
                if (_wizardPages != null)
                {
                    for (int i = 0; i < _wizardPages.Count; i++)
                    {
                        hash = hash * 31 + (_wizardPages[i].IsCompleted ? 1 : 0);
                        hash = hash * 31 + (_wizardPages[i].CanProceed ? 1 : 0);
                    }
                }
                return hash;
            }
        }

        private void RebuildNativeSetupBody()
        {
            _nativeSetupBody.Clear();

            IWizardPage currentPage = _wizardPages[AI.Config.wizardCurrentPage];
            VisualElement pageBody = BuildNativeWizardPageBody(currentPage);
            _nativeSetupBody.Add(CommonUITK.CreateWizardShell(
                "Setup Steps",
                BuildNativeWizardSteps(),
                currentPage.Title,
                currentPage.Description,
                pageBody,
                BuildNativeWizardFooter(),
                Logo,
                SetupWizardClasses));
        }

        private VisualElement BuildNativeWizardPageBody(IWizardPage currentPage)
        {
            if (!(currentPage is INativeWizardPage nativePage))
            {
                throw new InvalidOperationException($"Setup page '{currentPage?.GetType().Name ?? "null"}' must provide native UI Toolkit content.");
            }

            currentPage.OnEnter();

            ScrollView scrollView = new ScrollView(ScrollViewMode.Vertical)
            {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Auto
            };
            scrollView.Add(nativePage.CreateNativeContent());
            return scrollView;
        }

        private List<CommonUITK.WizardStep> BuildNativeWizardSteps()
        {
            List<CommonUITK.WizardStep> steps = new List<CommonUITK.WizardStep>(_wizardPages.Count);
            for (int i = 0; i < _wizardPages.Count; i++)
            {
                int pageIndex = i;
                IWizardPage page = _wizardPages[i];
                steps.Add(new CommonUITK.WizardStep
                {
                    Title = page.Title,
                    IsActive = i == AI.Config.wizardCurrentPage,
                    IsCompleted = page.IsCompleted,
                    IsEnabled = i <= AI.Config.wizardCurrentPage || page.IsCompleted,
                    OnClick = () => NavigateToWizardPage(pageIndex)
                });
            }

            return steps;
        }

        private VisualElement BuildNativeWizardFooter()
        {
            VisualElement footer = AssetInventoryUITK.CreateFooter();

            Button back = AssetInventoryUITK.CreateSecondaryButton("Back", () => NavigateToWizardPage(AI.Config.wizardCurrentPage - 1));
            back.SetEnabled(AI.Config.wizardCurrentPage > 0);
            footer.Add(back);

            footer.Add(AssetInventoryUITK.CreateFlexibleSpacer());

            if (AI.Config.wizardCurrentPage == 0)
            {
                footer.Add(AssetInventoryUITK.CreateSecondaryButton("Skip", CompleteWizard));
            }

            IWizardPage currentPage = _wizardPages[AI.Config.wizardCurrentPage];
            if (AI.Config.wizardCurrentPage < _wizardPages.Count - 1)
            {
                Button next = AssetInventoryUITK.CreatePrimaryButton("Next", () => NavigateToWizardPage(AI.Config.wizardCurrentPage + 1));
                next.SetEnabled(currentPage.CanProceed);
                footer.Add(next);
            }
            else
            {
                Button finish = AssetInventoryUITK.CreatePrimaryButton("Finish", CompleteWizard);
                finish.SetEnabled(currentPage.CanProceed);
                footer.Add(finish);
            }

            return footer;
        }

        private void InitializeWizardPages()
        {
            if (_wizardPages == null)
            {
                _wizardPages = new List<IWizardPage>
                {
                    new SetupWizardIntroPage(),
                    new SetupWizardDownloadPage(),
                    new SetupWizardLocationsPage(),
                    new SetupWizardPreviewPage(),
                    new SetupWizardAIPage(),
                    new SetupWizardUIPage(),
                    new SetupWizardCompletionPage()
                };

                // Load current page from config
                if (AI.Config.wizardCurrentPage >= _wizardPages.Count)
                {
                    AI.Config.wizardCurrentPage = 0;
                }

                NavigateToWizardPage(AI.Config.wizardCurrentPage);
            }
        }

        private void NavigateToWizardPage(int pageIndex)
        {
            if (pageIndex < 0 || pageIndex >= _wizardPages.Count) return;

            // Call OnExit for current page
            if (AI.Config.wizardCurrentPage < _wizardPages.Count)
            {
                _wizardPages[AI.Config.wizardCurrentPage].OnExit();
            }

            // set all pages completed up to selected one
            for (int i = 0; i < pageIndex; i++)
            {
                _wizardPages[i].IsCompleted = true;
            }

            AI.Config.wizardCurrentPage = pageIndex;
            SaveWizardState();
            _nativeSetupStateHash = int.MinValue;
            if (_uitkShellActive)
            {
                MarkUITKShellDirty();
            }
            Repaint();
        }

        private void SaveWizardState()
        {
            AI.SaveConfig();
        }

        private void CompleteWizard()
        {
            AI.Config.wizardCompleted = true;
            if (AI.Config.noIndexByDefault)
            {
                AI.Config.tab = (int)AssetInventoryTab.Packages;
                _selectedMaintenance = PackageSearch.MaintenanceOption.NoIndex;
                _packageInspectorTab = 1;
                _requireAssetTreeRebuild = true;
            }
            AI.SaveConfig();
            _nativeSetupStateHash = int.MinValue;
            if (_uitkShellActive)
            {
                MarkUITKShellDirty();
            }
            Repaint();
        }
    }

    public class SetupWizardIntroPage : WizardPage, INativeWizardPage
    {
        public override string Title => "Welcome to Asset Inventory";
        public override string Description => "Asset Inventory is a powerful tool for managing and finding your Unity assets. This setup wizard will guide you through the essential configuration options. All settings can be changed later in the Settings tab.";

        private static readonly Texture2D _sample = CommonUIStyles.LoadTexture("asset-inventory-sample");

        public VisualElement CreateNativeContent()
        {
            VisualElement root = SetupWizardNativeUI.CreateBody();
            root.Add(SetupWizardNativeUI.CreateCopy("Once complete you will be able to search through all your assets as shown below."));
            root.Add(SetupWizardNativeUI.CreateSampleImage(_sample));
            return root;
        }
    }

    public class SetupWizardDownloadPage : WizardPage, INativeWizardPage
    {
        public override string Title => "Choose What to Index";
        public override string Description => "Choose the default for newly discovered packages. You can change individual packages or this default later.";

        public VisualElement CreateNativeContent()
        {
            VisualElement root = SetupWizardNativeUI.CreateBody();
            VisualElement options = SetupWizardNativeUI.CreateOptionGroup();
            VisualElement downloadOptions = SetupWizardNativeUI.CreateOptionGroup();
            VisualElement downloadAssets = null;
            VisualElement downloadWarning = null;
            VisualElement selectiveGuidance = null;

            StyleSheet commonStyles = CommonInspectorElements.LoadSharedStyleSheet();
            if (commonStyles != null) root.styleSheets.Add(commonStyles);

            VisualElement approach = new VisualElement();
            approach.AddToClassList(IndexUI.SetupIndexingChoiceGridClass);
            CommonSingleChoiceCard automaticCard = new CommonSingleChoiceCard(
                "Index Everything Automatically",
                "Discover every eligible package and index its contents automatically. Uncached Asset Store packages are downloaded when enabled below.",
                null,
                "Choose automatic indexing for newly discovered packages.");
            automaticCard.AddToClassList(IndexUI.SetupIndexingChoiceCardClass);
            CommonSingleChoiceCard selectiveCard = new CommonSingleChoiceCard(
                "Let Me Choose What to Index",
                "Discover the package catalog, but leave package contents unindexed until you explicitly include the packages you want.",
                null,
                "Choose which newly discovered packages to index.");
            selectiveCard.AddToClassList(IndexUI.SetupIndexingChoiceCardClass);
            approach.Add(automaticCard);
            approach.Add(selectiveCard);

            Action<int> selectApproach = selected =>
            {
                AI.Config.noIndexByDefault = selected == 1;
                AI.SaveConfig();
                automaticCard.SetSelected(selected == 0);
                selectiveCard.SetSelected(selected == 1);
                if (downloadAssets != null) downloadAssets.style.display = selected == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (downloadWarning != null) downloadWarning.style.display = selected == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (selectiveGuidance != null) selectiveGuidance.style.display = selected == 1 ? DisplayStyle.Flex : DisplayStyle.None;
                downloadOptions.style.display = selected == 0 && AI.Actions.DownloadAssets ? DisplayStyle.Flex : DisplayStyle.None;
            };
            automaticCard.Chosen += _ => selectApproach(0);
            selectiveCard.Chosen += _ => selectApproach(1);
            automaticCard.SetSelected(!AI.Config.noIndexByDefault);
            selectiveCard.SetSelected(AI.Config.noIndexByDefault);
            options.Add(approach);

            downloadAssets = SetupWizardNativeUI.CreateToggle(
                "Download Assets for Indexing",
                "Automatically download uncached items from the Asset Store for indexing. Downloading an item can affect whether it can be returned through the Asset Store.",
                AI.Actions.DownloadAssets,
                value =>
                {
                    AI.Actions.DownloadAssets = value;
                    AI.SaveConfig();
                    downloadOptions.style.display = value && !AI.Config.noIndexByDefault ? DisplayStyle.Flex : DisplayStyle.None;
                });
            downloadAssets.style.display = AI.Config.noIndexByDefault ? DisplayStyle.None : DisplayStyle.Flex;
            options.Add(downloadAssets);

            downloadOptions.style.display = AI.Actions.DownloadAssets && !AI.Config.noIndexByDefault ? DisplayStyle.Flex : DisplayStyle.None;
            downloadOptions.Add(SetupWizardNativeUI.CreateToggle(
                "Keep Downloaded Assets",
                "Keep automatically downloaded assets in the cache after indexing instead of deleting them.",
                AI.Config.keepAutoDownloads,
                value =>
                {
                    AI.Config.keepAutoDownloads = value;
                    AI.SaveConfig();
                }));

            VisualElement limitRow = SetupWizardNativeUI.CreateInlineRow();
            VisualElement limitFieldGroup = SetupWizardNativeUI.CreateInlineRow();
            IntegerField limitField = SetupWizardNativeUI.CreateIntegerField(AI.Config.downloadLimit, value =>
            {
                AI.Config.downloadLimit = Mathf.Max(0, value);
                AI.SaveConfig();
            });
            limitFieldGroup.Add(SetupWizardNativeUI.CreateInlineLabel("to"));
            limitFieldGroup.Add(limitField);
            limitFieldGroup.Add(SetupWizardNativeUI.CreateInlineLabel("MB"));

            VisualElement limitToggle = SetupWizardNativeUI.CreateToggle(
                "Limit Package Size",
                "Do not automatically download packages larger than this size.",
                AI.Config.limitAutoDownloads,
                value =>
                {
                    AI.Config.limitAutoDownloads = value;
                    AI.SaveConfig();
                    limitFieldGroup.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                });
            limitFieldGroup.style.display = AI.Config.limitAutoDownloads ? DisplayStyle.Flex : DisplayStyle.None;
            limitRow.Add(limitToggle);
            limitRow.Add(limitFieldGroup);
            downloadOptions.Add(limitRow);
            options.Add(downloadOptions);
            root.Add(options);

            selectiveGuidance = AssetInventoryUITK.CreateHelpBox(
                "After setup, open Packages, select the packages you want, and choose Include & Index Now. You can include more packages at any time.",
                MessageType.Info);
            selectiveGuidance.AddToClassList(IndexUI.SetupSelectiveGuidanceClass);
            selectiveGuidance.style.display = AI.Config.noIndexByDefault ? DisplayStyle.Flex : DisplayStyle.None;
            root.Add(selectiveGuidance);

            downloadWarning = AssetInventoryUITK.CreateHelpBox(
                "If you plan to return freshly purchased assets, leave automatic downloads off because downloading can disallow returns. Downloads also use bandwidth, so consider your connection before enabling this on limited plans.",
                MessageType.Warning);
            downloadWarning.style.display = AI.Config.noIndexByDefault ? DisplayStyle.None : DisplayStyle.Flex;
            root.Add(downloadWarning);
            return root;
        }
    }

#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public partial class SetupWizardLocationsPage : WizardPage, INativeWizardPage
    {
        public override string Title => "Storage Location";
        public override string Description => "Choose storage for the database, cache, and backups. The database stays relatively small, but previews, extracted files, and backups can require substantial space.";

        internal readonly struct StorageRequirementSample
        {
            public readonly string Packages;
            public readonly string Files;
            public readonly string Database;
            public readonly string Previews;
            public readonly string ExtractedCache;
            public readonly string SuggestedFree;

            public StorageRequirementSample(string packages, string files, string database, string previews, string extractedCache, string suggestedFree)
            {
                Packages = packages;
                Files = files;
                Database = database;
                Previews = previews;
                ExtractedCache = extractedCache;
                SuggestedFree = suggestedFree;
            }
        }

#if UNITY_6000_7_OR_NEWER
        // Storage examples are immutable setup guidance metadata.
        [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
        internal static readonly StorageRequirementSample[] StorageRequirementSamples =
        {
            new StorageRequirementSample("100", "25,000", "~100 MB", "~1-3 GB", "~5-15 GB", "20+ GB"),
            new StorageRequirementSample("500", "150,000", "~500 MB", "~5-15 GB", "~25-75 GB", "100+ GB"),
            new StorageRequirementSample("2,000", "750,000", "~2.5 GB", "~25-75 GB", "~100-300 GB", "350+ GB")
        };

        private List<DriveInfo> _drives;
        private Vector2 _driveScrollPosition;

        public override void OnEnter()
        {
            base.OnEnter();
            RefreshDriveInfo();
        }

        private void RefreshDriveInfo()
        {
            _drives = new List<DriveInfo>();
            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (IsDriveSuitableForStorage(drive))
                    {
                        _drives.Add(drive);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error getting drive information: {e.Message}");
            }
        }

        private bool IsDriveSuitableForStorage(DriveInfo drive)
        {
            if (!drive.IsReady) return false;

            // Filter out unsuitable drive types
            switch (drive.DriveType)
            {
                case DriveType.Fixed:
                    // Fixed drives are good
                    break;
                case DriveType.Removable:
                    // Removable drives are not suitable for database storage
                    return false;
                case DriveType.Network:
                    // Network drives can be slow and unreliable for database operations
                    return false;
                case DriveType.CDRom:
                case DriveType.NoRootDirectory:
                    return false;
                case DriveType.Unknown:
#if UNITY_EDITOR_LINUX
                    // On Linux, local ext4/btrfs/xfs drives often report DriveType.Unknown
                    // instead of Fixed. Allow them and filter by format/path below.
                    break;
#else
                    return false;
#endif
            }

            // Filter out drives with very little space (less than 1GB)
            try
            {
                if (drive.AvailableFreeSpace < 1024L * 1024 * 1024) return false;
            }
            catch (Exception)
            {
                return false;
            }

#if UNITY_EDITOR_LINUX
            string drivePath = drive.RootDirectory.FullName;

            // Skip pseudo-filesystem mount points
            if (drivePath.StartsWith("/proc", StringComparison.Ordinal) ||
                drivePath.StartsWith("/sys", StringComparison.Ordinal) ||
                drivePath.StartsWith("/dev", StringComparison.Ordinal) ||
                drivePath.StartsWith("/run", StringComparison.Ordinal) ||
                drivePath.StartsWith("/snap", StringComparison.Ordinal) ||
                drivePath.StartsWith("/boot", StringComparison.Ordinal))
                return false;

            // Skip by filesystem format
            try
            {
                string format = drive.DriveFormat?.ToLowerInvariant();
                if (format == "tmpfs" || format == "devtmpfs" || format == "proc" ||
                    format == "sysfs" || format == "cgroup" || format == "cgroup2" ||
                    format == "overlay" || format == "squashfs" || format == "ramfs")
                    return false;
            }
            catch (IOException)
            {
                return false;
            }
#endif

            // On macOS, filter out system volumes and temporary mounts
#if UNITY_EDITOR_OSX
            string drivePath = drive.RootDirectory.FullName.ToLowerInvariant();

            // Skip system volumes
            if (drivePath.Contains("/system/") || drivePath.Contains("/volumes/system")) return false;

            // Skip temporary mounts and network volumes
            if (drivePath.Contains("/volumes/.timemachine") ||
                drivePath.Contains("/volumes/.spotlight") ||
                drivePath.Contains("/volumes/.fseventsd") ||
                drivePath.Contains("/volumes/.trashes") ||
                drivePath.Contains("/volumes/.mobilebackups"))
                return false;

            // Skip network drives (usually mounted under /volumes with network paths)
            if (drivePath.Contains("//") || drivePath.Contains("smb://") || drivePath.Contains("afp://"))
                return false;

            // Skip drives that are likely temporary or system-related
            if (drive.VolumeLabel.ToLowerInvariant().Contains("time machine") ||
                drive.VolumeLabel.ToLowerInvariant().Contains("backup") ||
                drive.VolumeLabel.ToLowerInvariant().Contains("system") ||
                drive.VolumeLabel.ToLowerInvariant().Contains("recovery"))
                return false;
#endif

            // On Windows, filter out some system drives
#if UNITY_EDITOR_WIN
            string drivePath = drive.RootDirectory.FullName.ToLowerInvariant();

            // Skip drives that are likely system recovery or temporary
            if (drive.VolumeLabel.ToLowerInvariant().Contains("recovery") ||
                drive.VolumeLabel.ToLowerInvariant().Contains("system") ||
                drive.VolumeLabel.ToLowerInvariant().Contains("temp"))
                return false;
#endif

            return true;
        }

        private void SetDatabaseLocation()
        {
            string targetFolder = EditorUtility.OpenFolderPanel("Select folder for database and cache", Paths.GetStorageFolder(), "");
            if (string.IsNullOrEmpty(targetFolder)) return;

            // check if same folder selected
            if (IOUtils.IsSameDirectory(targetFolder, Paths.GetStorageFolder())) return;

            // disallow selecting a drive/root directory (e.g., C:\, D:\, E:, or /)
            if (IOUtils.IsRootPath(targetFolder))
            {
                EditorUtility.DisplayDialog("Invalid Folder", "Please select a subfolder, not a drive root.", "OK");
                return;
            }

            // check for existing database
            if (File.Exists(Path.Combine(targetFolder, DBAdapter.DB_NAME)))
            {
                if (EditorUtility.DisplayDialog("Use Existing?", "The target folder contains a database. Switch to this one? Otherwise please select an empty directory.", "Switch", "Cancel"))
                {
                    AI.SwitchDatabase(targetFolder);
                }

                return;
            }

            AI.SwitchDatabase(targetFolder);
            AssetStore.GatherAllMetadata();
            AssetStore.GatherProjectMetadata();
        }

        public override bool CanProceed => true;

        public VisualElement CreateNativeContent()
        {
            VisualElement root = SetupWizardNativeUI.CreateBody();

            VisualElement driveList = SetupWizardNativeUI.CreateOptionGroup();
            VisualElement currentSection = AssetInventoryUITK.CreateSection("Current Location");
            VisualElement currentRow = SetupWizardNativeUI.CreateInlineRow();
            TextField currentField = new TextField
            {
                value = Paths.GetStorageFolder()
            };
            currentField.AddToClassList(IndexUI.SetupInlineFieldClass);
            currentField.SetEnabled(false);
            Button changeButton = AssetInventoryUITK.CreateSecondaryButton("Change...", () =>
            {
                SetDatabaseLocation();
                currentField.SetValueWithoutNotify(Paths.GetStorageFolder());
                RefreshDriveInfo();
                RebuildNativeDriveRows(driveList);
            });
            currentRow.Add(currentField);
            currentRow.Add(changeButton);
            currentSection.Add(currentRow);
            root.Add(currentSection);

            VisualElement estimateSection = AssetInventoryUITK.CreateSection("Estimated Space Needed");
            estimateSection.Add(SetupWizardNativeUI.CreateStorageEstimateTable(StorageRequirementSamples));
            estimateSection.Add(AssetInventoryUITK.CreateHelpBox(
                "These are approximate planning numbers. The extracted cache varies most because it depends on package size, accessed packages, kept downloads and preview settings.",
                MessageType.Info));
            root.Add(estimateSection);

            VisualElement driveSection = AssetInventoryUITK.CreateSection("Free Disk Space");
            driveSection.Add(driveList);
            root.Add(driveSection);
            RebuildNativeDriveRows(driveList);

            return root;
        }

        private void RebuildNativeDriveRows(VisualElement driveList)
        {
            if (driveList == null) return;

            driveList.Clear();
            string currentDbPath = Paths.GetStorageFolder();
            string currentDbFolder = Path.GetDirectoryName(currentDbPath) ?? string.Empty;
            if (_drives != null && _drives.Count > 0)
            {
                foreach (DriveInfo drive in _drives)
                {
                    bool isCurrentDrive = currentDbFolder.StartsWith(drive.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase);
                    driveList.Add(SetupWizardNativeUI.CreateDriveRow(drive, isCurrentDrive));
                }
            }
            else
            {
                driveList.Add(AssetInventoryUITK.CreateHelpBox("No drives found or accessible.", MessageType.Warning));
            }
        }
    }

    public class SetupWizardPreviewPage : WizardPage, INativeWizardPage
    {
        public override string Title => "Display Settings";
        public override string Description => "Configure how Asset Inventory handles preview images and visual content.";

        public VisualElement CreateNativeContent()
        {
            VisualElement root = SetupWizardNativeUI.CreateBody();

            VisualElement previews = AssetInventoryUITK.CreateSection("Previews");
            previews.Add(AssetInventoryUITK.CreateHelpBox(
                "Upscaling makes package previews sharper in larger tiles, but uses additional storage.",
                MessageType.Info));

            VisualElement upscaleOptions = SetupWizardNativeUI.CreateOptionGroup();
            VisualElement upscaleToggle = SetupWizardNativeUI.CreateToggle(
                "Upscale Preview Images",
                "Resize preview images to better fill larger tiles.",
                AI.Config.upscalePreviews,
                value =>
                {
                    AI.Config.upscalePreviews = value;
                    AI.SaveConfig();
                    upscaleOptions.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                });
            previews.Add(upscaleToggle);

            VisualElement sizeRow = SetupWizardNativeUI.CreateInlineRow();
            sizeRow.Add(SetupWizardNativeUI.CreateInlineLabel(AI.Config.upscaleLossless ? "Target Size" : "Minimum Size"));
            sizeRow.Add(SetupWizardNativeUI.CreateIntegerField(AI.Config.upscaleSize, value =>
            {
                AI.Config.upscaleSize = Mathf.Max(0, value);
                AI.SaveConfig();
            }));
            sizeRow.Add(SetupWizardNativeUI.CreateInlineLabel("pixels"));
            upscaleOptions.style.display = AI.Config.upscalePreviews ? DisplayStyle.Flex : DisplayStyle.None;
            upscaleOptions.Add(sizeRow);
            previews.Add(upscaleOptions);

            previews.Add(AssetInventoryUITK.CreateHelpBox(
                "Creating missing previews improves browsing but extends indexing. You can recreate them later from the Preview Wizard.",
                MessageType.Info));
            previews.Add(SetupWizardNativeUI.CreateToggle(
                "Create Previews When Missing",
                "Automatically generate previews for assets after indexing completes.",
                AI.Config.recreatePreviewsAfterIndexing,
                value =>
                {
                    AI.Config.recreatePreviewsAfterIndexing = value;
                    AI.SaveConfig();
                }));
            root.Add(previews);

            VisualElement dependencies = AssetInventoryUITK.CreateSection("Optional Preview Support");
#if !USE_VECTOR_GRAPHICS
            dependencies.Add(SetupWizardNativeUI.CreateInstallPrompt(
                "SVG previews need the Unity Vector Graphics package.",
                "Install Vector Graphics Package",
                () => Client.Add("com.unity.vectorgraphics")));
#endif
#if !USE_PSD_IMPORTER
            dependencies.Add(SetupWizardNativeUI.CreateInstallPrompt(
                "PSB imports and previews need the Unity 2D PSD Importer package.",
                "Install 2D PSD Importer Package",
                () => Client.Add("com.unity.2d.psdimporter")));
#endif
#if !USE_SHADER_GRAPH
            dependencies.Add(SetupWizardNativeUI.CreateInstallPrompt(
                "Shader Graph previews need the Unity Shader Graph package.",
                "Install Shader Graph Package",
                () => Client.Add("com.unity.shadergraph")));
#endif
#if !USE_GLTF_IMPORTER && !USE_KHRONOS_UNITY_GLTF
            dependencies.Add(SetupWizardNativeUI.CreateInstallPrompt(
                GltfSupport.MissingImporterMessage,
                "Install Unity glTFast Package",
                () => Client.Add(GltfSupport.PackageName)));
#endif
#if !USE_TEXTMESHPRO && !UNITY_2023_2_OR_NEWER
            dependencies.Add(SetupWizardNativeUI.CreateInstallPrompt(
                "TextMeshPro previews need the TextMeshPro package.",
                "Install TextMeshPro Package",
                () => Client.Add("com.unity.textmeshpro@3.0.9")));
#endif
#if USE_TEXTMESHPRO || UNITY_2023_2_OR_NEWER
            if (!TMPStep.AreTMPEssentialsImported())
            {
                dependencies.Add(SetupWizardNativeUI.CreateInstallPrompt(
                    "TextMeshPro Essentials add default fonts, shaders, and settings required for full text rendering support.",
                    "Import TMP Essentials",
                    TMPStep.ImportEssentials));
            }
#endif
            if (dependencies.childCount > 1)
            {
                root.Add(dependencies);
            }

            VisualElement other = AssetInventoryUITK.CreateSection("Other Settings");
            other.Add(SetupWizardNativeUI.CreateCurrencyField());
            root.Add(other);

            return root;
        }
    }

    public class SetupWizardAIPage : WizardPage, INativeWizardPage
    {
        public override string Title => "AI Features (Optional)";
        public override string Description => "Optional local AI captions let visual assets be found by meaning even when their filenames are not descriptive.";

        private static readonly string[] _backendLabels = {"Ollama (recommended)", "LM Studio"};
        private static readonly int[] _backendValues = {1, 2};

        private string _connectionStatus;
        private MessageType _connectionMessageType;
        private int _testGeneration;
        private bool _tested;
        private long _downloadCurrent;
        private long _downloadTotal;
        private VisualElement _nativeDetailsContainer;
        private VisualElement _nativeStatusContainer;
        private VisualElement _nativeModelActionContainer;

        public override void OnEnter()
        {
            base.OnEnter();
            if (!_tested && AI.Actions.AICaptionsEnabled) RunAutoTest();
        }

        public VisualElement CreateNativeContent()
        {
            VisualElement root = SetupWizardNativeUI.CreateBody();
            root.Add(SetupWizardNativeUI.CreateToggle(
                "Activate AI Captions",
                "Generate AI captions for package previews so files can be found by visual meaning, not only by filename.",
                AI.Config.aiCaptionsFeatureEnabled,
                value =>
                {
                    AI.Config.aiCaptionsFeatureEnabled = value;
                    AI.SaveConfig();
                    if (value) RunAutoTest();
                    RebuildNativeAIDetails();
                    RefreshNativeAIState();
                }));

            _nativeDetailsContainer = SetupWizardNativeUI.CreateOptionGroup();
            root.Add(_nativeDetailsContainer);
            RebuildNativeAIDetails();
            root.schedule.Execute(RefreshNativeAIState).Every(500);
            return root;
        }

        private void RebuildNativeAIDetails()
        {
            if (_nativeDetailsContainer == null) return;

            _nativeDetailsContainer.Clear();
            _nativeStatusContainer = null;
            _nativeModelActionContainer = null;
            if (!AI.Actions.AICaptionsEnabled) return;

            VisualElement backend = AssetInventoryUITK.CreateSection("AI Backend");
            backend.Add(AssetInventoryUITK.CreateHelpBox(
                "Both backends run locally. Ollama is simpler to set up; LM Studio provides a graphical model manager.",
                MessageType.Info));

            int selectedIndex = Array.IndexOf(_backendValues, AI.Config.aiBackend);
            if (selectedIndex < 0) selectedIndex = 0;
            backend.Add(SetupWizardNativeUI.CreateBackendSelector(_backendLabels, selectedIndex, index =>
            {
                AI.Config.aiBackend = _backendValues[Mathf.Clamp(index, 0, _backendValues.Length - 1)];
                AI.SaveConfig();
                RunAutoTest();
                RebuildNativeAIDetails();
                RefreshNativeAIState();
            }));

            _nativeDetailsContainer.Add(backend);

            if (AI.Config.aiBackend == 1)
            {
                BuildNativeOllamaSetup(_nativeDetailsContainer);
            }
            else if (AI.Config.aiBackend == 2)
            {
                BuildNativeLMStudioSetup(_nativeDetailsContainer);
            }

            _nativeStatusContainer = SetupWizardNativeUI.CreateOptionGroup();
            _nativeDetailsContainer.Add(_nativeStatusContainer);
            _nativeDetailsContainer.Add(AssetInventoryUITK.CreateHelpBox(
                "Captioning is enabled per package because it can take time. Select packages for AI captions later in Packages.",
                MessageType.Info));
            RefreshNativeAIState();
        }

        private void BuildNativeOllamaSetup(VisualElement root)
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Ollama");
            section.Add(SetupWizardNativeUI.CreateTextFieldRow("Service URL", AI.Config.ollamaServiceUrl, value =>
            {
                AI.Config.ollamaServiceUrl = value;
                Intelligence.RefreshOllama();
                AI.SaveConfig();
                RunAutoTest();
                RefreshNativeAIState();
            }));

            Button suggested = null;
            suggested = AssetInventoryUITK.CreateSecondaryButton("Suggested", () => ShowSuggestedOllamaModels(suggested));
            suggested.tooltip = "Choose a recommended vision model.";
            section.Add(SetupWizardNativeUI.CreateTextFieldRow("Model", AI.Config.ollamaModel, value =>
            {
                AI.Config.ollamaModel = (value ?? string.Empty).Trim();
                AI.SaveConfig();
                RunAutoTest();
                RefreshNativeAIState();
            }, suggested));

            _nativeModelActionContainer = SetupWizardNativeUI.CreateOptionGroup();
            _nativeModelActionContainer.AddToClassList(IndexUI.SetupModelActionClass);
            section.Add(_nativeModelActionContainer);

            if (!Intelligence.IsOllamaInstalled && _connectionMessageType != MessageType.None)
            {
                section.Add(AssetInventoryUITK.CreateSecondaryButton("Ollama Website", () => AI.OpenURL(Intelligence.OLLAMA_WEBSITE)));
            }

            root.Add(section);
        }

        private void BuildNativeLMStudioSetup(VisualElement root)
        {
            VisualElement section = AssetInventoryUITK.CreateSection("LM Studio");
            section.Add(SetupWizardNativeUI.CreateTextFieldRow("Service URL", AI.Config.lmStudioServiceUrl ?? Intelligence.LMSTUDIO_SERVICE_URL, value =>
            {
                AI.Config.lmStudioServiceUrl = value;
                Intelligence.RefreshLMStudio();
                AI.SaveConfig();
                RunAutoTest();
                RefreshNativeAIState();
            }));

            Button installed = null;
            installed = AssetInventoryUITK.CreateSecondaryButton("Installed", () => ShowInstalledLMStudioModels(installed));
            installed.tooltip = "Choose an installed vision model.";
            section.Add(SetupWizardNativeUI.CreateTextFieldRow("Model", AI.Config.lmStudioModel ?? string.Empty, value =>
            {
                AI.Config.lmStudioModel = (value ?? string.Empty).Trim();
                AI.SaveConfig();
                RunAutoTest();
                RefreshNativeAIState();
            }, installed));

            if (!Intelligence.IsLMStudioInstalled && _connectionMessageType != MessageType.None)
            {
                section.Add(AssetInventoryUITK.CreateSecondaryButton("LM Studio Website", () => AI.OpenURL(Intelligence.LMSTUDIO_WEBSITE)));
            }

            root.Add(section);
        }

        private void RefreshNativeAIState()
        {
            RefreshNativeStatus();
            RefreshNativeModelAction();
        }

        private void RefreshNativeStatus()
        {
            if (_nativeStatusContainer == null) return;

            _nativeStatusContainer.Clear();
            if (!string.IsNullOrEmpty(_connectionStatus))
            {
                _nativeStatusContainer.Add(AssetInventoryUITK.CreateHelpBox(_connectionStatus, _connectionMessageType));
            }
        }

        private void RefreshNativeModelAction()
        {
            if (_nativeModelActionContainer == null) return;

            _nativeModelActionContainer.Clear();
            if (AI.Config.aiBackend != 1 ||
                string.IsNullOrWhiteSpace(AI.Config.ollamaModel) ||
                !Intelligence.IsOllamaInstalled ||
                Intelligence.OllamaModelDownloaded(AI.Config.ollamaModel))
            {
                return;
            }

            if (Intelligence.DownloadingModel)
            {
                float progress = _downloadTotal <= 0 ? 0f : Mathf.Clamp01(_downloadCurrent / (float)_downloadTotal);
                string title = _downloadTotal <= 0
                    ? "Preparing model download..."
                    : $"{EditorUtility.FormatBytes(_downloadCurrent)}/{EditorUtility.FormatBytes(_downloadTotal)}";
                _nativeModelActionContainer.Add(AssetInventoryUITK.CreateProgressBar(title, progress));
                _nativeModelActionContainer.Add(AssetInventoryUITK.CreateSecondaryButton("Cancel", () => Intelligence.OllamaDownloadToken?.Cancel()));
            }
            else
            {
                _nativeModelActionContainer.Add(AssetInventoryUITK.CreateSecondaryButton("Download Model", DownloadOllamaModel));
            }
        }

        private void ShowSuggestedOllamaModels(VisualElement anchor)
        {
            GenericMenu menu = new GenericMenu();
            foreach (string model in Intelligence.SuggestedOllamaModels)
            {
                string modelLabel = model;
                string modelName = modelLabel.Split(' ')[0];
                menu.AddItem(new GUIContent(modelLabel), string.Equals(AI.Config.ollamaModel, modelName, StringComparison.Ordinal), () =>
                {
                    AI.Config.ollamaModel = modelName;
                    AI.SaveConfig();
                    RunAutoTest();
                    RebuildNativeAIDetails();
                    RefreshNativeAIState();
                });
            }

            ShowNativeMenu(anchor, menu);
        }

        private void ShowInstalledLMStudioModels(VisualElement anchor)
        {
            IEnumerable<LMStudioModel> models = Intelligence.LMStudioModels;
            GenericMenu menu = new GenericMenu();
            if (models != null)
            {
                foreach (LMStudioModel model in models.Where(m => m.SupportsVision))
                {
                    string modelId = model.id;
                    menu.AddItem(new GUIContent(modelId), string.Equals(AI.Config.lmStudioModel, modelId, StringComparison.Ordinal), () =>
                    {
                        AI.Config.lmStudioModel = modelId;
                        AI.SaveConfig();
                        RunAutoTest();
                        RebuildNativeAIDetails();
                        RefreshNativeAIState();
                    });
                }
            }
            if (menu.GetItemCount() == 0)
            {
                menu.AddDisabledItem(new GUIContent("No vision models found"));
            }

            ShowNativeMenu(anchor, menu);
        }

        private static void ShowNativeMenu(VisualElement anchor, GenericMenu menu)
        {
            if (menu == null) return;

            EditorWindow owner = EditorWindow.focusedWindow;
            if (owner != null && anchor != null)
            {
                CommonUITK.ShowGenericMenu(menu, anchor);
            }
            else
            {
                menu.ShowAsContext();
            }
        }

        private void RunAutoTest()
        {
            _tested = true;
            _connectionStatus = null;
            _testGeneration++;

            if (AI.Config.aiBackend == 1) TestOllamaConnection(_testGeneration);
            else if (AI.Config.aiBackend == 2) TestLMStudioConnection(_testGeneration);
        }

        private async void TestOllamaConnection(int generation)
        {
            _connectionStatus = "Connecting to Ollama...";
            _connectionMessageType = MessageType.None;

            try
            {
                Intelligence.RefreshOllama();
                bool running = await Intelligence.IsOllamaRunningAsync();
                if (generation != _testGeneration) return;

                if (running)
                {
                    string version = await Intelligence.GetOllamaVersionAsync();
                    IEnumerable<ModelInfo> models = await Intelligence.ListOllamaModelsAsync();
                    if (generation != _testGeneration) return;
                    int modelCount = models?.Count() ?? 0;

                    _connectionStatus = $"Connected to Ollama v{version}. {modelCount} model{(modelCount != 1 ? "s" : "")} available.";
                    _connectionMessageType = MessageType.Info;

                    if (modelCount == 0)
                    {
                        _connectionStatus += " Download a model first (e.g. qwen2.5vl:7b).";
                        _connectionMessageType = MessageType.Warning;
                    }
                    else if (!string.IsNullOrWhiteSpace(AI.Config.ollamaModel) && !Intelligence.OllamaModelDownloaded(AI.Config.ollamaModel))
                    {
                        _connectionStatus += $" Model '{AI.Config.ollamaModel}' is not downloaded yet.";
                        _connectionMessageType = MessageType.Warning;
                    }
                }
                else
                {
                    _connectionStatus = "Ollama is not running. Start the Ollama application and try again.";
                    _connectionMessageType = MessageType.Error;
                }
            }
            catch (Exception e)
            {
                if (generation != _testGeneration) return;
                _connectionStatus = $"Connection failed: {e.Message}";
                _connectionMessageType = MessageType.Error;
            }
        }

        private async void TestLMStudioConnection(int generation)
        {
            _connectionStatus = "Connecting to LM Studio...";
            _connectionMessageType = MessageType.None;

            try
            {
                Intelligence.RefreshLMStudio();
                await System.Threading.Tasks.Task.Delay(1500);
                if (generation != _testGeneration) return;

                if (Intelligence.IsLMStudioInstalled)
                {
                    string version = Intelligence.LMStudioVersion;
                    IEnumerable<LMStudioModel> models = Intelligence.LMStudioModels;
                    int visionCount = models?.Count(m => m.SupportsVision) ?? 0;

                    _connectionStatus = $"Connected to LM Studio{(version != null ? $" v{version}" : "")}. {visionCount} vision model{(visionCount != 1 ? "s" : "")} available.";
                    _connectionMessageType = MessageType.Info;

                    if (visionCount == 0)
                    {
                        _connectionStatus += " Load a vision model (VLM) in LM Studio first.";
                        _connectionMessageType = MessageType.Warning;
                    }
                }
                else
                {
                    _connectionStatus = "LM Studio is not running or the local server is not started. Enable it in LM Studio and try again.";
                    _connectionMessageType = MessageType.Error;
                }
            }
            catch (Exception e)
            {
                if (generation != _testGeneration) return;
                _connectionStatus = $"Connection failed: {e.Message}";
                _connectionMessageType = MessageType.Error;
            }
        }

        private void DownloadOllamaModel()
        {
            _downloadCurrent = 0;
            _downloadTotal = 0;
            string modelName = AI.Config.ollamaModel;
            Task.Run(async () =>
            {
                await Intelligence.PullOllamaModelAsync(modelName, status =>
                {
                    _downloadCurrent = status.Completed;
                    _downloadTotal = status.Total;
                });
                RunAutoTest();
            });
        }
    }

    public class SetupWizardUIPage : WizardPage, INativeWizardPage
    {
        public override string Title => "Advanced Features";
        public override string Description => "Keep everyday workflows concise while retaining quick access to expert controls.";

        private static readonly Texture2D _sample = CommonUIStyles.LoadTexture("asset-inventory-sample2");

        public VisualElement CreateNativeContent()
        {
            VisualElement root = SetupWizardNativeUI.CreateBody();
            root.Add(AssetInventoryUITK.CreateHelpBox("Standard mode shows the controls needed for everyday work. Advanced mode reveals technical and less frequently used options.", MessageType.Info));
            root.Add(SetupWizardNativeUI.CreateCopy("Use the eye icon in the upper-right toolbar to switch between Standard and Advanced mode."));
            root.Add(SetupWizardNativeUI.CreateSampleImage(_sample));
            return root;
        }
    }

    public class SetupWizardCompletionPage : WizardPage, INativeWizardPage
    {
        public override string Title => "Setup Done";
        public override string Description => "All steps completed!";

        private int _assetsFileCount = -1;

        public override void OnEnter()
        {
            base.OnEnter();
            CountAssetsFiles();
            if (!AI.Actions.AnyActionsInProgress)
            {
                AI.Config.quickIndexingDone = false;
                if (AI.Config.noIndexByDefault)
                {
                    AI.Actions.RunDiscoveryActions();
                }
                else
                {
                    AI.Actions.RunActions();
                }
            }
        }

        private void CountAssetsFiles()
        {
            try
            {
                string assetsPath = Application.dataPath;
                if (Directory.Exists(assetsPath))
                {
                    _assetsFileCount = IOUtils.GetFilesSafe(assetsPath, "*.*", SearchOption.AllDirectories).Count();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not count files in Assets directory: {e.Message}");
                _assetsFileCount = -1;
            }
        }

        public override bool CanProceed => true;

        public VisualElement CreateNativeContent()
        {
            VisualElement root = SetupWizardNativeUI.CreateBody();
            root.Add(AssetInventoryUITK.CreateHelpBox(
                AI.Config.noIndexByDefault
                    ? "Asset Inventory is discovering your package catalog. Package contents will remain untouched until you include them."
                    : "Asset Inventory is configured and ready to use across your Unity projects.",
                MessageType.Info));

            VisualElement next = AssetInventoryUITK.CreateSection("What's Next");
            next.AddToClassList(IndexUI.SetupNextListClass);
            next.Add(AI.Config.noIndexByDefault
                ? SetupWizardNativeUI.CreateNextItem(1, "Choose packages to index", "Open Packages, select the items you want, then choose Include & Index Now.")
                : SetupWizardNativeUI.CreateNextItem(1, "Let initial indexing finish", "Eligible packages are being indexed in the background. You can keep working while it runs."));
            next.Add(SetupWizardNativeUI.CreateNextItem(2, "Keep the index current", "Open Settings and use Run Actions whenever you want to discover changes and refresh the index."));
            next.Add(SetupWizardNativeUI.CreateNextItem(3, "Explore advanced workflows", "Open the tutorials for guided help with search, organization, and other powerful features."));
            root.Add(next);

#if !USE_TUTORIALS
            root.Add(AssetInventoryUITK.CreateSecondaryButton("Install Getting-Started Tutorials", () => Client.Add($"com.unity.learn.iet-framework@{AI.TUTORIALS_VERSION}")));
#endif
            if (_assetsFileCount > 1500)
            {
                root.Add(AssetInventoryUITK.CreateHelpBox(
                    "This project contains many files, so initial indexing may take longer. For the fastest first run, open Asset Inventory in an empty project; its database is shared across projects.",
                    MessageType.Warning));
            }

            return root;
        }
    }

#if UNITY_6000_7_OR_NEWER
    // Native setup labels and form configuration are immutable editor code-lifetime metadata.
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    internal static partial class SetupWizardNativeUI
    {
        private static readonly string[] StorageEstimateHeaders =
        {
            "Packages", "Files", "DB", "Previews", "Cache", "Required"
        };
        private static readonly string[] StorageEstimateTooltipLabels =
        {
            "Packages", "Files", "Database", "Previews", "Extracted Cache", "Required Free Space"
        };
        private static readonly string[] StorageEstimateHeaderTooltips =
        {
            "Approximate number of packages in the inventory.",
            "Approximate number of indexed files across those packages.",
            "Approximate database size.",
            "Approximate disk space used by generated previews.",
            "Approximate disk space used by extracted package contents.",
            "Approximate free disk space required for this inventory size."
        };
        private static readonly string[] StorageEstimateColumnClasses =
        {
            "ai-setup-storage-table-cell-packages",
            "ai-setup-storage-table-cell-files",
            "ai-setup-storage-table-cell-database",
            "ai-setup-storage-table-cell-previews",
            "ai-setup-storage-table-cell-cache",
            "ai-setup-storage-table-cell-free"
        };
        private static readonly CommonFormBuilder ToggleFormBuilder = AssetInventoryUITK.CreateFormBuilder(
            rowClass: IndexUI.SetupToggleClass,
            labelClass: IndexUI.SetupToggleLabelClass,
            toggleClass: IndexUI.SetupToggleControlClass,
            toggleFirst: true,
            labelTogglesControl: true);

        public static VisualElement CreateBody()
        {
            VisualElement root = new VisualElement();
            root.AddToClassList(IndexUI.SetupNativeBodyClass);
            return root;
        }

        public static Label CreateCopy(string text)
        {
            Label label = new Label(text);
            label.AddToClassList(IndexUI.SetupNativeCopyClass);
            return label;
        }

        public static Image CreateSampleImage(Texture texture)
        {
            Image image = new Image
            {
                image = texture,
                scaleMode = ScaleMode.ScaleToFit
            };
            image.AddToClassList(IndexUI.SetupSampleImageClass);
            image.RegisterCallback<GeometryChangedEvent>(_ => UpdateSampleImageHeight(image, texture));
            return image;
        }

        private static void UpdateSampleImageHeight(Image image, Texture texture)
        {
            if (image == null || texture == null || texture.width <= 0 || texture.height <= 0) return;

            float width = image.resolvedStyle.width;
            if (width <= 0f) return;

            float targetHeight = Mathf.Clamp(width * texture.height / texture.width, 160f, 420f);
            if (Mathf.Abs(image.resolvedStyle.height - targetHeight) > 0.5f)
            {
                image.style.height = targetHeight;
            }
        }

        public static VisualElement CreateNextItem(int stepNumber, string title, string description)
        {
            VisualElement item = new VisualElement();
            item.AddToClassList(IndexUI.SetupNextItemClass);

            Label number = new Label(stepNumber.ToString());
            number.AddToClassList(IndexUI.SetupNextItemNumberClass);
            item.Add(number);

            VisualElement copy = new VisualElement();
            copy.AddToClassList(IndexUI.SetupNextItemCopyClass);

            Label titleLabel = new Label(title);
            titleLabel.AddToClassList(IndexUI.SetupNextItemTitleClass);
            copy.Add(titleLabel);

            Label descriptionLabel = new Label(description);
            descriptionLabel.AddToClassList(IndexUI.SetupNextItemDescriptionClass);
            copy.Add(descriptionLabel);

            item.Add(copy);
            return item;
        }

        public static VisualElement CreateOptionGroup()
        {
            VisualElement group = new VisualElement();
            group.AddToClassList(IndexUI.SetupOptionGroupClass);
            return group;
        }

        public static VisualElement CreateToggle(string text, string tooltip, bool value, Action<bool> onChange)
        {
            return ToggleFormBuilder.CreateToggleRow(text, value, onChange, tooltip);
        }

        public static VisualElement CreateInlineRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(IndexUI.SetupInlineRowClass);
            return row;
        }

        public static Label CreateInlineCaption(string text)
        {
            Label label = new Label(text ?? string.Empty);
            label.AddToClassList(IndexUI.SetupInlineCaptionClass);
            return label;
        }

        public static Label CreateInlineLabel(string text)
        {
            Label label = new Label(text ?? string.Empty);
            label.AddToClassList(IndexUI.SetupInlineUnitClass);
            return label;
        }

        public static IntegerField CreateIntegerField(int value, Action<int> onChange)
        {
            IntegerField field = new IntegerField
            {
                value = value
            };
            field.AddToClassList(IndexUI.SetupInlineFieldClass);
            field.RegisterValueChangedCallback(evt => onChange?.Invoke(evt.newValue));
            return field;
        }

        public static VisualElement CreateTextFieldRow(string label, string value, Action<string> onChange, VisualElement side = null)
        {
            VisualElement row = CreateInlineRow();
            row.Add(CreateInlineCaption(label));

            TextField field = new TextField
            {
                value = value ?? string.Empty,
                isDelayed = true
            };
            field.AddToClassList(IndexUI.SetupInlineFieldClass);
            field.RegisterValueChangedCallback(evt => onChange?.Invoke(evt.newValue));
            row.Add(field);

            if (side != null)
            {
                row.Add(side);
            }

            return row;
        }

        public static VisualElement CreateBackendSelector(IReadOnlyList<string> labels, int selectedIndex, Action<int> onSelect)
        {
            VisualElement selector = new VisualElement();
            selector.AddToClassList(IndexUI.SetupBackendSelectorClass);
            if (labels == null) return selector;

            for (int i = 0; i < labels.Count; i++)
            {
                int index = i;
                Button button = AssetInventoryUITK.CreateSecondaryButton(labels[i], () => onSelect?.Invoke(index));
                button.AddToClassList(IndexUI.SetupBackendButtonClass);
                button.EnableInClassList(IndexUI.SetupBackendButtonActiveClass, index == selectedIndex);
                selector.Add(button);
            }

            return selector;
        }

        public static VisualElement CreateStorageEstimateTable(IReadOnlyList<SetupWizardLocationsPage.StorageRequirementSample> samples)
        {
            ScrollView scroll = new ScrollView(ScrollViewMode.Horizontal)
            {
                horizontalScrollerVisibility = ScrollerVisibility.Auto,
                verticalScrollerVisibility = ScrollerVisibility.Hidden
            };
            scroll.AddToClassList(IndexUI.SetupStorageTableScrollClass);

            VisualElement table = new VisualElement
            {
                name = "storage-estimate-table"
            };
            table.AddToClassList(IndexUI.SetupStorageTableClass);
            table.Add(CreateStorageEstimateRow(StorageEstimateHeaders, true, false));

            if (samples != null)
            {
                for (int i = 0; i < samples.Count; i++)
                {
                    SetupWizardLocationsPage.StorageRequirementSample sample = samples[i];
                    string[] values =
                    {
                        sample.Packages,
                        sample.Files,
                        sample.Database,
                        sample.Previews,
                        sample.ExtractedCache,
                        sample.SuggestedFree
                    };
                    table.Add(CreateStorageEstimateRow(values, false, i % 2 == 1));
                }
            }

            scroll.Add(table);
            return scroll;
        }

        private static VisualElement CreateStorageEstimateRow(IReadOnlyList<string> values, bool isHeader, bool isAlternate)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(IndexUI.SetupStorageTableRowClass);
            row.EnableInClassList(IndexUI.SetupStorageTableHeaderClass, isHeader);
            row.EnableInClassList(IndexUI.SetupStorageTableAlternateRowClass, isAlternate);

            for (int i = 0; i < StorageEstimateHeaders.Length; i++)
            {
                string text = values != null && i < values.Count ? values[i] : string.Empty;
                Label cell = new Label(text)
                {
                    tooltip = isHeader ? StorageEstimateHeaderTooltips[i] : $"{StorageEstimateTooltipLabels[i]}: {text}"
                };
                cell.AddToClassList(IndexUI.SetupStorageTableCellClass);
                cell.AddToClassList(StorageEstimateColumnClasses[i]);
                cell.EnableInClassList(IndexUI.SetupStorageTableRecommendedCellClass, !isHeader && i == StorageEstimateHeaders.Length - 1);
                row.Add(cell);
            }

            return row;
        }

        public static VisualElement CreateDriveRow(DriveInfo drive, bool isCurrentDrive)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(IndexUI.SetupDriveRowClass);
            row.EnableInClassList(IndexUI.SetupDriveCurrentClass, isCurrentDrive);

            VisualElement header = new VisualElement();
            header.AddToClassList(IndexUI.SetupDriveHeaderClass);

            string driveLabel = drive.VolumeLabel;
            if (string.IsNullOrEmpty(driveLabel)) driveLabel = drive.Name;
            if (isCurrentDrive) driveLabel += " (Current)";

            Label name = new Label(driveLabel);
            name.AddToClassList(IndexUI.SetupDriveNameClass);
            header.Add(name);

            Label space = new Label(EditorUtility.FormatBytes(drive.AvailableFreeSpace));
            space.AddToClassList(IndexUI.SetupDriveSpaceClass);
            header.Add(space);
            row.Add(header);

            VisualElement bar = new VisualElement();
            bar.AddToClassList(IndexUI.SetupDriveBarClass);

            VisualElement fill = new VisualElement();
            fill.AddToClassList(IndexUI.SetupDriveBarFillClass);
            long freeSpaceGb = drive.AvailableFreeSpace / (1024 * 1024 * 1024);
            fill.style.backgroundColor = GetDriveFreeSpaceColor(freeSpaceGb);
            bar.Add(fill);
            row.Add(bar);

            return row;
        }

        public static VisualElement CreateInstallPrompt(string text, string buttonText, Action action)
        {
            VisualElement group = CreateOptionGroup();
            group.Add(AssetInventoryUITK.CreateHelpBox(text, MessageType.Warning));
            group.Add(AssetInventoryUITK.CreateSecondaryButton(buttonText, action));
            return group;
        }

        public static VisualElement CreateCurrencyField()
        {
            VisualElement row = CreateInlineRow();
            row.Add(CreateInlineLabel("Preferred Currency"));

            List<string> currencies = new List<string> {"EUR", "USD", "CNY"};
            int index = Mathf.Clamp(AI.Config.currency, 0, currencies.Count - 1);
            PopupField<string> currency = new PopupField<string>(currencies, index);
            currency.AddToClassList(IndexUI.SetupInlineFieldClass);
            currency.RegisterValueChangedCallback(evt =>
            {
                int newIndex = currencies.IndexOf(evt.newValue);
                if (newIndex < 0) return;

                AI.Config.currency = newIndex;
                AI.SaveConfig();
            });
            row.Add(currency);
            return row;
        }

        private static Color GetDriveFreeSpaceColor(long freeSpaceGb)
        {
            if (freeSpaceGb > 200) return new Color(0.37f, 0.73f, 0.42f);
            if (freeSpaceGb > 100) return new Color(0.91f, 0.73f, 0.28f);
            return new Color(0.88f, 0.34f, 0.30f);
        }
    }
}
