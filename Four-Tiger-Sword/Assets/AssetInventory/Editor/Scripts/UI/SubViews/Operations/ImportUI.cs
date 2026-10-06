using ImpossibleRobert.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CodeStage.PackageToFolder;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace AssetInventory
{
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public sealed partial class ImportUI : EditorWindow
    {
        public static event Action OnImportDone;

        // IDs (foreignId field) of assets that do NOT support installation into a custom target folder
        // This is usually the case for assets that install files via custom scripts or have hardcoded paths
        // The list might not be complete, please report any missing ones to the developer
        private static readonly HashSet<int> _noCustomTargetFolderForeignIds = new HashSet<int>
        {
            267512, // Mighty Maps
            185959, // Task Atlas
            307257, // DevTasks - Offline Project Manager
            291626, // DevTrails - Developer Statistics Made Easy
            277112, // Clipper Pro - The Ultimate Clipboard
            135178, // Digger
            149753, // Digger PRO
        };

        private static bool? _customFolderReflectionAvailable;
        private static bool CustomFolderReflectionAvailable
        {
            get
            {
                if (_customFolderReflectionAvailable == null)
                {
                    Assembly assembly = Assembly.Load("UnityEditor.CoreModule");
                    Type packageUtility = assembly.GetType("UnityEditor.PackageUtility");
                    _customFolderReflectionAvailable = GetPackageExtractionMethod(packageUtility) != null;
                }
                return _customFolderReflectionAvailable.Value;
            }
        }

        private List<AssetInfo> _assets;
        private List<AssetInfo> _missingPackages;
        private Action _callback;
        private string _customFolder;
        private string _customFolderRel;
        private bool _running;
        private bool _cancellationRequested;
        private AddRequest _addRequest;
        private AssetInfo _curInfo;
        private bool _unattended;
        private int _queueCount;
        private bool _interactive;
        private string _lockPref;
        private IVisualElementScheduledItem _statusUpdate;

        public static ImportUI ShowWindow()
        {
            ImportUI window = GetWindow<ImportUI>("Import Wizard");
            window.minSize = new Vector2(450, 360);

            return window;
        }

        public void OnEnable()
        {
            AssetDatabase.importPackageStarted += ImportStarted;
            AssetDatabase.importPackageCompleted += ImportCompleted;
            AssetDatabase.importPackageCancelled += ImportCancelled;
            AssetDatabase.importPackageFailed += ImportFailed;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }

        public void OnDisable()
        {
            AssetDatabase.importPackageStarted -= ImportStarted;
            AssetDatabase.importPackageCompleted -= ImportCompleted;
            AssetDatabase.importPackageCancelled -= ImportCancelled;
            AssetDatabase.importPackageFailed -= ImportFailed;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
            EditorApplication.delayCall -= ResumeImportAfterReload;
            StopStatusRefresh();
        }

        private void OnBeforeAssemblyReload()
        {
            // right now not any state to persist actually, Unity will serialize the whole view correctly
        }

        private void OnAfterAssemblyReload()
        {
            // Unity can reload scripts from inside its native package-import call. Resume
            // only after that stack unwinds, and keep waiting for the in-flight package.
            if (_running) EditorApplication.delayCall += ResumeImportAfterReload;
        }

        private void ResumeImportAfterReload()
        {
            if (this != null && _running) BulkImportAssets(_interactive, false);
        }

        public void Init(List<AssetInfo> assets, bool unattended = false, Action callback = null, bool noCustomFolder = false, string lockPref = null)
        {
            PipelineConversionImportTracker.Cancel();
            _callback = callback;
            _unattended = unattended;
            _lockPref = lockPref;

            _assets = assets.Where(a => a.ParentId == 0)
                .OrderByDescending(a => a.AssetSource).ThenBy(a => a.GetDisplayName())
                .ToArray().ToList(); // break direct reference so that package list refresh does not clear import state

            // check if only sub-packages were selected, this is a valid scenario
            if (_assets.Count == 0)
            {
                _assets = assets.Where(a => a.ParentId > 0)
                    .OrderByDescending(a => a.AssetSource).ThenBy(a => a.GetDisplayName())
                    .ToArray().ToList(); // break direct reference so that package list refresh does not clear import state
            }

            if (noCustomFolder)
            {
                ClearCustomFolder();
            }
            else
            {
                // use configured target folder from settings if set
                if (AI.Config.importDestination == 2 && !string.IsNullOrWhiteSpace(AI.Config.importFolder))
                {
                    _customFolderRel = AI.Config.importFolder;
                    _customFolder = Application.dataPath + _customFolderRel.Substring("Assets".Length);
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(_customFolder))
                    {
                        _customFolderRel = IOUtils.MakeProjectRelative(_customFolder);
                    }
                }
            }

            // check for non-existing downloads first
            _missingPackages = new List<AssetInfo>();
            _queueCount = 0;
            foreach (AssetInfo info in _assets)
            {
                if (info.SafeName == Asset.NONE) continue;
                if (!info.IsDownloaded)
                {
                    info.ImportState = AssetInfo.ImportStateOptions.Missing;
                    _missingPackages.Add(info);
                }
                else
                {
                    info.ImportState = AssetInfo.ImportStateOptions.Queued;
                }
            }
            _queueCount = CountStartableImportSelections(_assets, true);

            if (_unattended) BulkImportAssets(false, false);
            BuildIfReady();
        }

        private void Update()
        {
            if (_assets == null) return;

            // refresh list after downloads finish
            foreach (AssetInfo info in _assets)
            {
                if (info.PackageDownloader == null) continue;
                if (info.ImportState == AssetInfo.ImportStateOptions.Missing)
                {
                    AssetDownloadState state = info.PackageDownloader.GetState();
                    switch (state.state)
                    {
                        case AssetDownloader.State.Downloaded:
                            PackageDownloadCompletion.SyncPackage(info);
                            Init(_assets);
                            BuildIfReady();
                            break;
                    }
                }
            }
        }

        private void ImportFailed(string packageName, string errorMessage)
        {
            AssetInfo info = FindAsset(packageName);
            if (info == null) return;

            info.ImportState = AssetInfo.ImportStateOptions.Failed;
            _assets.First(a => a.AssetId == info.AssetId).ImportState = info.ImportState;

            Debug.LogError($"Import of '{packageName}' failed: {errorMessage}");
            BuildIfReady();
        }

        private void ImportCancelled(string packageName)
        {
            AssetInfo info = FindAsset(packageName);
            if (info == null) return;

            info.ImportState = AssetInfo.ImportStateOptions.Cancelled;
            _assets.First(a => a.AssetId == info.AssetId).ImportState = info.ImportState;
            BuildIfReady();
        }

        private void ImportCompleted(string packageName)
        {
            AssetInfo info = FindAsset(packageName);
            if (info == null)
            {
                // Unity 2023+ will return an empty packageName for some reason
                // since we can assume only one import happens at a time, we can just mark the current importing one as done
                info = _assets.FirstOrDefault(a => a.ImportState == AssetInfo.ImportStateOptions.Importing);
                if (info == null) return;
            }

            info.ImportState = AssetInfo.ImportStateOptions.Imported;
            _assets.First(a => a.AssetId == info.AssetId).ImportState = info.ImportState;
            BuildIfReady();
        }

        private void ImportStarted(string packageName)
        {
            AssetInfo info = FindAsset(packageName);
            if (info == null) return;

            info.ImportState = AssetInfo.ImportStateOptions.Importing;
            _assets.First(a => a.AssetId == info.AssetId).ImportState = info.ImportState;
            BuildIfReady();
        }

        private AssetInfo FindAsset(string packageName)
        {
            return _assets?.Find(info => info.SafeName == packageName || info.GetLocation(true) == packageName + ".unitypackage" || info.GetLocation(true) == packageName);
        }

        private void CreateGUI()
        {
            BuildContent();
        }

        private void BuildIfReady()
        {
            if (rootVisualElement != null && rootVisualElement.childCount > 0 && (_assets == null || _missingPackages != null))
            {
                BuildContent();
            }
        }

        private void BuildContent()
        {
            VisualElement root = rootVisualElement;
            if (root == null) return;

            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);

            if (_assets == null || _assets.Count == 0)
            {
                Button openPackages = AssetInventoryUITK.CreatePrimaryButton("Open Packages", () =>
                {
                    AI.Config.tab = (int)AssetInventoryTab.Packages;
                    AI.SaveConfig();
                    MenuIntegration.ShowWindow();
                    GetWindow<IndexUI>("Asset Inventory").Focus();
                    Close();
                });
                root.Add(AssetInventoryUITK.CreateEmptyState(
                    "No packages selected",
                    "Select one or more downloaded packages, then open Import again.",
                    openPackages));
                StopStatusRefresh();
                return;
            }

            root.Add(BuildSummarySection());
            AddWarnings(root);
            root.Add(BuildQueueSection());
            root.Add(BuildFooter());

            UpdateStatusRefresh();
        }

        private VisualElement BuildSummarySection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Import Target");

            Label packageCount = AssetInventoryUITK.CreateCopyLabel(_assets.Count.ToString("N0"));
            packageCount.AddToClassList("ai-import-summary-value");
            VisualElement packageRow = AssetInventoryUITK.CreateFieldRow("Packages", packageCount);
            packageRow.AddToClassList("ai-import-summary-row");
            section.Add(packageRow);

            VisualElement target = new VisualElement();
            target.AddToClassList("ai-inline-control-row");
            target.AddToClassList("ai-import-target-control");

            Label path = AssetInventoryUITK.CreateCopyLabel(string.IsNullOrWhiteSpace(_customFolderRel) ? "-default-" : _customFolderRel);
            path.tooltip = string.IsNullOrWhiteSpace(_customFolderRel) ? "Unity's default import location." : _customFolderRel;
            path.AddToClassList("ai-inline-grow");
            path.AddToClassList("ai-import-target-path");
            target.Add(path);

            VisualElement actions = new VisualElement();
            actions.AddToClassList("ai-import-target-actions");

            Button select = AssetInventoryUITK.CreateSecondaryButton("Select...", SelectTargetFolder);
            select.SetEnabled(!_running);
            actions.Add(select);

            if (!string.IsNullOrWhiteSpace(_customFolder))
            {
                Button clear = AssetInventoryUITK.CreateSecondaryButton("Clear", ClearCustomFolder);
                clear.SetEnabled(!_running);
                actions.Add(clear);
            }

            target.Add(actions);
            VisualElement targetRow = AssetInventoryUITK.CreateFieldRow("Target Folder", target);
            targetRow.AddToClassList("ai-import-summary-row");
            section.Add(targetRow);
            return section;
        }

        private void AddWarnings(VisualElement root)
        {
            if (root == null) return;

            // Hint if custom target folders for Unity packages are not available in this Unity version
            bool hasUnityPackages = _assets.Any(a => a.AssetSource != Asset.Source.RegistryPackage && a.AssetSource != Asset.Source.Archive);
            if (!CustomFolderReflectionAvailable && hasUnityPackages && !string.IsNullOrWhiteSpace(_customFolderRel))
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("Custom target folders for .unitypackage files are not supported in this Unity version. These packages will be imported to the default location.", MessageType.Warning));
            }

            // Hint if some items do not support custom folders
            if (!string.IsNullOrWhiteSpace(_customFolderRel) && _assets.Any(IsCustomFolderUnsupported)
                && (CustomFolderReflectionAvailable || !hasUnityPackages))
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("Some selected items do not support custom target folders and will be installed to the default location.", MessageType.Info));
            }

            if (_missingPackages != null && _missingPackages.Count > 0)
            {
                if (_queueCount > 0)
                {
                    root.Add(AssetInventoryUITK.CreateHelpBox($"{_missingPackages.Count:N0} packages have not been downloaded yet and will be skipped.", MessageType.Warning));
                }
                else
                {
                    root.Add(AssetInventoryUITK.CreateHelpBox("The packages have not been downloaded yet. No import possible until done so.", MessageType.Warning));
                }
            }
        }

        private VisualElement BuildQueueSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Packages");
            section.AddToClassList("ai-import-queue-section");

            ScrollView list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("ai-list");
            list.AddToClassList("ai-import-list");

            int rowIndex = 0;
            foreach (AssetInfo info in _assets ?? Enumerable.Empty<AssetInfo>())
            {
                if (info.SafeName == Asset.NONE) continue;
                list.Add(CreateImportRow(info, rowIndex));
                rowIndex++;
            }

            section.Add(list);
            return section;
        }

        private VisualElement CreateImportRow(AssetInfo info, int rowIndex)
        {
            VisualElement row = new VisualElement();

            bool selectedForImport = IsStartableImportSelection(info, true);
            Toggle toggle = new Toggle();
            toggle.AddToClassList("ai-import-toggle");
            toggle.SetValueWithoutNotify(selectedForImport);
            toggle.SetEnabled(!_running && CanToggleImportSelection(info));
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue != selectedForImport)
                {
                    SetSelectedForImport(info, evt.newValue);
                    _queueCount = CountStartableImportSelections(_assets, true);
                    BuildIfReady();
                }
            });
            string subtitle = GetImportRowSubtitle(info);
            AssetInventoryUITK.PopulateListRow(
                row,
                GetImportRowTitle(info),
                subtitle,
                toggle,
                CreateImportRowSide(info),
                extraClasses: rowIndex % 2 == 1 ? new[] {"ai-import-row", BaseVerticalCollectionView.itemAlternativeBackgroundUssClassName} : new[] {"ai-import-row"});
            row.Q<Label>("title").tooltip = info.SafeName;
            row.Q<Label>("subtitle").tooltip = subtitle;
            return row;
        }

        private string GetImportRowTitle(AssetInfo info)
        {
            if (info.AssetSource != Asset.Source.RegistryPackage) return info.GetDisplayName();

            string version = info.TargetPackageVersion();
            return version != null ? $"{info.GetDisplayName()} - {version}" : $"{info.GetDisplayName()} - checking";
        }

        private string GetImportRowSubtitle(AssetInfo info)
        {
            if (info.AssetSource == Asset.Source.RegistryPackage)
            {
                return string.IsNullOrWhiteSpace(info.SafeName) ? "Registry package" : info.SafeName;
            }

            string location = info.GetLocation(true);
            return string.IsNullOrWhiteSpace(location) ? info.SafeName : location;
        }

        private VisualElement CreateImportRowSide(AssetInfo info)
        {
            VisualElement side = new VisualElement();
            side.AddToClassList("ai-import-row-side");

            if (info.ImportState == AssetInfo.ImportStateOptions.Missing)
            {
                if (info.IsAbandoned)
                {
                    Label unavailable = CreateImportStatePill("Unavailable", "ai-status-warning");
                    unavailable.tooltip = "Package got disabled on the Asset Store and is no longer available for download.";
                    side.Add(unavailable);
                }
                else
                {
                    AI.GetObserver().Attach(info);
                    AssetDownloadState state = info.PackageDownloader.GetState();
                    switch (state.state)
                    {
                        case AssetDownloader.State.Unavailable:
                            if (info.PackageDownloader.IsDownloadSupported())
                            {
                                side.Add(AssetInventoryUITK.CreateSecondaryButton("Download", () => info.PackageDownloader.Download(true)));
                            }
                            else
                            {
                                side.Add(CreateImportStatePill("Unavailable", "ai-status-warning"));
                            }
                            break;

                        case AssetDownloader.State.Downloading:
                            side.Add(CreateImportStatePill(Mathf.RoundToInt(state.progress * 100f) + "%", "ai-status-progress"));
                            break;

                        default:
                            side.Add(CreateImportStatePill("Missing", "ai-status-warning"));
                            break;
                    }
                }
                return side;
            }

            // Mark items with warning icon when custom target folder is selected but unsupported
            if (!string.IsNullOrWhiteSpace(_customFolderRel) && IsCustomFolderUnsupported(info))
            {
                Image warning = new Image
                {
                    image = EditorGUIUtility.IconContent("console.warnicon").image,
                    scaleMode = ScaleMode.ScaleToFit,
                    tooltip = "This package does not support installation into a custom target folder. It will be installed to the default location."
                };
                warning.AddToClassList("ai-import-warning-icon");
                side.Add(warning);
            }

            side.Add(CreateImportStatePill(GetImportStateLabel(info), GetImportStateClass(info)));
            return side;
        }

        private static Label CreateImportStatePill(string text, string stateClass)
        {
            Label pill = AssetInventoryUITK.CreateStatusPill(text);
            pill.AddToClassList("ai-import-state-pill");
            pill.AddToClassList(stateClass);
            return pill;
        }

        private static string GetImportStateClass(AssetInfo info)
        {
            switch (info.ImportState)
            {
                case AssetInfo.ImportStateOptions.Imported:
                    return "ai-status-success";
                case AssetInfo.ImportStateOptions.Importing:
                    return "ai-status-progress";
                case AssetInfo.ImportStateOptions.Failed:
                    return "ai-status-error";
                case AssetInfo.ImportStateOptions.Cancelled:
                    return "ai-status-warning";
                case AssetInfo.ImportStateOptions.Unknown:
                    return "ai-status-muted";
                default:
                    return "ai-status-pending";
            }
        }

        private VisualElement BuildFooter()
        {
            VisualElement footer = AssetInventoryUITK.CreateWindowFooter();
            footer.AddToClassList("ai-import-footer");

            bool gatheringVersions = IsGatheringVersions();
            bool hasImportSelection = HasStartableImportSelection(_assets, true);
            bool hasInteractiveImportSelection = HasStartableImportSelection(_assets, false);

            Button automatic = AssetInventoryUITK.CreatePrimaryButton("Import Automatically", () => BulkImportAssets(false, true));
            automatic.tooltip = "Import all package contents. Unity may still request confirmation for package signatures or project settings.";
            automatic.SetEnabled(!_running && !gatheringVersions && hasImportSelection);
            footer.Add(automatic);

            Button interactive = AssetInventoryUITK.CreateSecondaryButton("Import Interactive...", () => BulkImportAssets(true, true));
            interactive.tooltip = "Open the Unity import wizard for each asset to be imported, allowing to fine-tune each import.";
            interactive.SetEnabled(!_running && !gatheringVersions && hasInteractiveImportSelection);
            footer.Add(interactive);

            if (_running)
            {
                Button cancel = AssetInventoryUITK.CreateSecondaryButton("Cancel All", () =>
                {
                    _cancellationRequested = true; // will not always work if there was a recompile in between
                    _running = false;
                    BuildIfReady();
                });
                footer.Add(cancel);
            }

            return footer;
        }

        private bool IsGatheringVersions()
        {
            if (_assets == null) return false;
            return _assets.Any(info =>
                info.AssetSource == Asset.Source.RegistryPackage &&
                info.TargetPackageVersion() == null &&
                IsStartableImportSelection(info, true));
        }

        private void UpdateStatusRefresh()
        {
            if (NeedsStatusRefresh())
            {
                StartStatusRefresh();
            }
            else
            {
                StopStatusRefresh();
            }
        }

        private bool NeedsStatusRefresh()
        {
            if (_running) return true;
            if (_assets == null) return false;

            return _assets.Any(info =>
                info != null &&
                info.ImportState == AssetInfo.ImportStateOptions.Missing &&
                info.PackageDownloader != null);
        }

        private void StartStatusRefresh()
        {
            if (_statusUpdate != null || rootVisualElement == null) return;
            _statusUpdate = rootVisualElement.schedule.Execute(RefreshStatus).Every(500);
        }

        private void StopStatusRefresh()
        {
            _statusUpdate?.Pause();
            _statusUpdate = null;
        }

        private void RefreshStatus()
        {
            if (!NeedsStatusRefresh())
            {
                StopStatusRefresh();
                return;
            }

            BuildIfReady();
        }

        internal static bool HasStartableImportSelection(IEnumerable<AssetInfo> assets, bool includeRegistryPackages)
        {
            return CountStartableImportSelections(assets, includeRegistryPackages) > 0;
        }

        private static int CountStartableImportSelections(IEnumerable<AssetInfo> assets, bool includeRegistryPackages)
        {
            if (assets == null) return 0;

            int result = 0;
            foreach (AssetInfo info in assets)
            {
                if (IsStartableImportSelection(info, includeRegistryPackages)) result++;
            }
            return result;
        }

        private static bool IsStartableImportSelection(AssetInfo info, bool includeRegistryPackages)
        {
            if (info == null) return false;
            if (info.SafeName == Asset.NONE) return false;
            if (!includeRegistryPackages && info.AssetSource == Asset.Source.RegistryPackage) return false;
            if (!info.IsDownloaded) return false;

            switch (info.ImportState)
            {
                case AssetInfo.ImportStateOptions.Queued:
                case AssetInfo.ImportStateOptions.Importing:
                case AssetInfo.ImportStateOptions.Failed:
                case AssetInfo.ImportStateOptions.Cancelled:
                    return true;

                default:
                    return false;
            }
        }

        private static bool CanToggleImportSelection(AssetInfo info)
        {
            if (info == null) return false;
            if (info.SafeName == Asset.NONE) return false;
            if (!info.IsDownloaded) return false;
            return info.ImportState != AssetInfo.ImportStateOptions.Importing;
        }

        private static void SetSelectedForImport(AssetInfo info, bool selected)
        {
            if (!CanToggleImportSelection(info)) return;

            info.ImportState = selected ? AssetInfo.ImportStateOptions.Queued : AssetInfo.ImportStateOptions.Unknown;
        }

        private static string GetImportStateLabel(AssetInfo info)
        {
            if (info == null) return string.Empty;
            if (info.ImportState == AssetInfo.ImportStateOptions.Unknown && info.IsDownloaded) return "Skipped";
            return info.ImportState.ToString();
        }

        private void ClearCustomFolder()
        {
            _customFolder = null;
            _customFolderRel = null;
            BuildIfReady();
        }

        private void SelectTargetFolder()
        {
            string folder = EditorUtility.OpenFolderPanel("Select target folder in your project", _customFolder, "");
            if (string.IsNullOrEmpty(folder)) return;

            if (folder.Replace("\\", "/").ToLowerInvariant().StartsWith(Application.dataPath.Replace("\\", "/").ToLowerInvariant()))
            {
                _customFolder = folder;
                _customFolderRel = IOUtils.MakeProjectRelative(folder);
                BuildIfReady();
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "The target folder must be inside your current Unity project.", "OK");
            }
        }

        private async void BulkImportAssets(bool interactive, bool resetState)
        {
            bool continueTrackedBatch = !resetState && _running && PipelineConversionImportTracker.IsTracking;
            PipelineConversionImportTracker.Begin(continueTrackedBatch);

            _interactive = interactive;
            if (!string.IsNullOrWhiteSpace(_lockPref)) EditorPrefs.SetBool(_lockPref, true);

            if (resetState)
            {
                _assets
                    .Where(a => a.ImportState == AssetInfo.ImportStateOptions.Cancelled || a.ImportState == AssetInfo.ImportStateOptions.Failed)
                    .ForEach(a => a.ImportState = AssetInfo.ImportStateOptions.Queued);
            }

            // importing will be set if there was a recompile during an ongoing import
            IEnumerable<AssetInfo> importQueue = _assets.Where(a => a.ImportState == AssetInfo.ImportStateOptions.Queued || a.ImportState == AssetInfo.ImportStateOptions.Importing)
                .Where(a => a.SafeName != Asset.NONE)
                .Where(a => a.IsDownloaded).ToList();

            bool allDone;
            if (importQueue.Any())
            {
                _running = true;
                _cancellationRequested = false;
                BuildIfReady();

                if (!string.IsNullOrWhiteSpace(_customFolder))
                {
                    _customFolderRel = IOUtils.MakeProjectRelative(_customFolder);
                    Directory.CreateDirectory(_customFolder);
                }

                if (interactive)
                {
                    // phase 1: all that can be imported in one go (registry, archives)
                    await DoBulkImport(importQueue.Where(a => a.AssetSource == Asset.Source.Archive || a.AssetSource == Asset.Source.RegistryPackage), false);

                    // phase 2: all the remaining
                    await DoBulkImport(importQueue.Where(a => a.AssetSource != Asset.Source.Archive && a.AssetSource != Asset.Source.RegistryPackage), true);
                }
                else
                {
                    await DoBulkImport(importQueue, false);
                }
                allDone = importQueue.All(a => a.ImportState == AssetInfo.ImportStateOptions.Imported);
            }
            else
            {
                allDone = true;
            }

            bool conversionEnabled = AI.Config.useCustomPipelineConverter || AI.Config.useUnityPipelineConverter;
            bool materialsReady = conversionEnabled && !_cancellationRequested && await PipelineConverter.WaitForMaterialImports(() => this == null || _cancellationRequested);
            if (this == null) return;
            List<string> importedConversionPaths = PipelineConversionImportTracker.Complete();

            try
            {
                if (materialsReady)
                    PipelineConverter.ConvertImportedMaterials(importedConversionPaths, AI.Config.useUnityPipelineConverter, AI.Config.useCustomPipelineConverter);
            }
            catch (Exception e)
            {
                Debug.LogError($"Asset Inventory imported the package, but material conversion failed: {e}");
            }
            finally
            {
                _running = false;
                BuildIfReady();
            }

            OnImportDone?.Invoke();

            // custom one-time callback handler
            _callback?.Invoke();
            _callback = null;
            if (!string.IsNullOrWhiteSpace(_lockPref)) EditorPrefs.DeleteKey(_lockPref);

            if (_unattended || allDone) Close();
        }

        private async Task DoBulkImport(IEnumerable<AssetInfo> queue, bool interactive)
        {
            // Package imports and registry requests complete on editor updates. Never hold
            // StartAssetEditing across these requests or the asynchronous completion waits.
            try
            {
                foreach (AssetInfo info in queue)
                {
                    _curInfo = info;

                    if (info.ImportState != AssetInfo.ImportStateOptions.Importing)
                    {
                        info.ImportState = AssetInfo.ImportStateOptions.Importing;

                        string archivePath = await info.GetLocation(true, true);
                        if (info.AssetSource == Asset.Source.RegistryPackage)
                        {
                            _addRequest = ImportPackage(info, info.TargetPackageVersion());
                            if (_addRequest == null) continue;

                            EditorApplication.update += AddProgress;
                        }
                        else if (info.AssetSource == Asset.Source.Archive)
                        {
                            // extract directly to target folder
                            string relFolder = _customFolderRel;
                            if (!string.IsNullOrWhiteSpace(relFolder) && IsCustomFolderUnsupported(info)) relFolder = null;
                            string targetPath = Path.Combine(relFolder ?? "Assets", info.GetDisplayName());
                            await Task.Run(() => CompressionUtil.ExtractArchive(archivePath, targetPath));
                            if (Directory.Exists(targetPath))
                                PipelineConversionImportTracker.ExpectImportedAssets(Directory.EnumerateFiles(targetPath, "*", SearchOption.AllDirectories));
                            info.ImportState = Directory.Exists(targetPath) ? AssetInfo.ImportStateOptions.Imported : AssetInfo.ImportStateOptions.Failed;
                        }
                        else
                        {
                            object[] files = GetPackageAssetList(archivePath, out Type contentType);
                            if (interactive)
                            {
                                // check if there are changes at all since otherwise dialog will stay and not throw events
                                if (!PackageHasChanges(archivePath, files, contentType))
                                {
                                    info.ImportState = AssetInfo.ImportStateOptions.Imported;
                                    continue;
                                }
                            }

                            // filter out ProjectSettings files in automatic mode to prevent overwriting project settings
                            bool projectSettingsFiltered = false;
                            if (AI.Config.skipProjectSettings && !interactive && files != null)
                            {
                                FieldInfo exportedPathField = contentType.GetField("exportedAssetPath");
                                object[] filtered = files.Where(f =>
                                {
                                    string path = (string)exportedPathField.GetValue(f);
                                    return path == null || !path.StartsWith("ProjectSettings/");
                                }).ToArray();

                                int skippedCount = files.Length - filtered.Length;
                                if (skippedCount > 0)
                                {
                                    Debug.Log($"Skipping {skippedCount} ProjectSettings file(s) during automatic import of '{info}'.");
                                    projectSettingsFiltered = true;
                                    files = filtered;

                                    if (files.Length == 0)
                                    {
                                        info.ImportState = AssetInfo.ImportStateOptions.Imported;
                                        continue;
                                    }
                                }
                            }

                            // fallback: when reflection is not available, scan the archive directly
                            if (AI.Config.skipProjectSettings && !interactive && files == null)
                            {
                                if (ContainsProjectSettings(archivePath))
                                {
                                    Debug.Log($"Package '{info}' contains ProjectSettings files. Switching to interactive import to prevent accidental project settings override.");
                                    interactive = true;
                                }
                            }

                            string actualRelFolder = _customFolderRel;
                            if (!string.IsNullOrWhiteSpace(actualRelFolder))
                            {
                                // Do not override path for packages that don't support custom folders
                                if (files == null || IsCustomFolderUnsupported(info))
                                {
                                    actualRelFolder = null;
                                }
                                else
                                {
                                    // check if any item already exists in the project, as that will most likely make rewriting the path fail
                                    bool updateMode = false;
                                    foreach (object item in files)
                                    {
                                        bool exists = (bool)contentType.GetField("exists").GetValue(item);
                                        if (exists)
                                        {
                                            actualRelFolder = null;
                                            updateMode = true;
                                            break;
                                        }
                                    }
                                    if (updateMode)
                                    {
                                        Debug.Log("Parts of the package are already imported. Skipping path change to custom location as that might cause invalid import directories to be created.");
                                    }
                                }
                            }

                            // launch directly or intercept package resolution to tweak paths
                            if (files != null)
                            {
                                FieldInfo destinationField = contentType.GetField("destinationAssetPath");
                                if (destinationField != null)
                                {
                                    PipelineConversionImportTracker.ExpectImportedAssets(files.Select(item =>
                                    {
                                        string path = destinationField.GetValue(item) as string;
                                        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(actualRelFolder) || !path.StartsWith("Assets/")) return path;
                                        return actualRelFolder + path.Substring(path.IndexOf('/'));
                                    }));
                                }
                            }
                            object assetOrigin = info.ToAsset().GetUnityAssetOrigin();
                            if (projectSettingsFiltered)
                            {
                                // use silent import with filtered items since AssetStore.ImportPackage re-extracts all items
                                if (!string.IsNullOrWhiteSpace(actualRelFolder))
                                {
                                    FieldInfo destPathField = contentType.GetField("destinationAssetPath");
                                    foreach (object item in files)
                                    {
                                        string destPath = (string)destPathField.GetValue(item);
                                        if (!destPath.StartsWith("Packages/"))
                                        {
                                            destPath = actualRelFolder + destPath.Remove(0, destPath.IndexOf('/'));
                                            destPathField.SetValue(item, destPath);
                                        }
                                    }
                                }
                                Package2Folder.ImportPackageSilently(Path.GetFileNameWithoutExtension(archivePath), files, assetOrigin);
                            }
                            else if (string.IsNullOrWhiteSpace(actualRelFolder))
                            {
                                AssetStore.ImportPackage(archivePath, interactive, assetOrigin);
                            }
                            else
                            {
                                // check if package has any dependencies
                                // automatic dialog will not pop up in case of custom import path override
                                bool regPackagesChanged = false;
                                Dictionary<string, string> dependencies = GetRegistryDependencies(files, contentType);
                                if (dependencies != null && dependencies.Count > 0)
                                {
                                    foreach (KeyValuePair<string, string> dep in dependencies)
                                    {
                                        Debug.Log($"Adding package dependency '{dep.Key}@{dep.Value}' for '{info}'");

                                        _addRequest = Client.Add($"{dep.Key}@{dep.Value}");
                                        if (_addRequest == null) continue;

                                        while (!_addRequest.IsCompleted) await Task.Delay(25);
                                        if (_addRequest.Status == StatusCode.Success) regPackagesChanged = true;
                                    }
                                }

                                Package2Folder.ImportPackageToFolder(archivePath, actualRelFolder, interactive, assetOrigin);

                                if (regPackagesChanged)
                                {
                                    Client.Resolve(); // only needed if registry packages were imported during asset packages import as dependencies
                                }
                            }
                        }
                    }

                    // wait until done
                    while (!_cancellationRequested && info.ImportState == AssetInfo.ImportStateOptions.Importing)
                    {
                        await Task.Delay(25);
                    }

                    if (info.ImportState == AssetInfo.ImportStateOptions.Importing) info.ImportState = AssetInfo.ImportStateOptions.Queued;
                    if (_cancellationRequested) break;
                }
            }
            catch (Exception e)
            {
                if (_curInfo != null) _curInfo.ImportState = AssetInfo.ImportStateOptions.Failed;
                Debug.LogError($"Error importing packages: {e}");
            }

            // handle potentially pending imports and put them back in the queue
            _assets.ForEach(info =>
            {
                if (info.ImportState == AssetInfo.ImportStateOptions.Importing) info.ImportState = AssetInfo.ImportStateOptions.Queued;
            });

            AssetDatabase.Refresh();
            Client.Resolve();

            // wait for all processes to finish
            while (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                await Task.Delay(25);
            }
        }

        private Dictionary<string, string> GetRegistryDependencies(object[] items, Type type)
        {
            if (items == null) return null;

            // ignore registry packages
            Regex regex = new Regex(@"^Assets/[^/]+/package\.json$", RegexOptions.Compiled);

            for (int i = 0; i < items.Length; i++)
            {
                string path = (string)type.GetField("exportedAssetPath").GetValue(items[i]);

                // performance check against obvious mismatches
                if (string.IsNullOrWhiteSpace(path) || path.Length < 20 || path[0] != 'A') continue;

                if (regex.IsMatch(path))
                {
                    string sourceFolder = (string)type.GetField("sourceFolder").GetValue(items[i]);
                    string sourceFile = Path.Combine(sourceFolder, "asset");
                    Package package = JsonConvert.DeserializeObject<Package>(File.ReadAllText(sourceFile));

                    return package?.dependencies;
                }
            }

            return null;
        }

        private bool PackageHasChanges(string packageFile, object[] items, Type type)
        {
            try
            {
                if (items != null && CountPackageChanges(items, type) == 0)
                {
                    Debug.Log($"No changes detected for '{packageFile}', skipping import.");
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not determine import state of '{packageFile}', proceeding with import: {e.Message}");
            }

            return true;
        }

        private static object[] GetPackageAssetList(string packageFile, out Type contentType)
        {
            object[] items = null;

            Assembly assembly = Assembly.Load("UnityEditor.CoreModule");
            Type packageUtility = assembly.GetType("UnityEditor.PackageUtility");
            MethodInfo extractAndPrepareAssetList = GetPackageExtractionMethod(packageUtility);
            object itemsObj = extractAndPrepareAssetList?.Invoke(null, new object[] {packageFile, null, null});
            if (itemsObj != null) items = (object[])itemsObj;
            contentType = assembly.GetType("UnityEditor.ImportPackageItem");

            return items;
        }

        internal static MethodInfo GetPackageExtractionMethod(Type packageUtility)
        {
            // Unity versions can remove either the extraction method or PackageUtility itself.
            return packageUtility?.GetMethod("ExtractAndPrepareAssetList", BindingFlags.Public | BindingFlags.Static);
        }

        private int CountPackageChanges(object[] items, Type type)
        {
            if (items.Length == 0) return 0;

            int result = 0;
            for (int i = 0; i < items.Length; i++)
            {
                if (!(bool)type.GetField("isFolder").GetValue(items[i]) && (bool)type.GetField("assetChanged").GetValue(items[i])) result++;
            }

            return result;
        }

        private static bool IsCustomFolderUnsupported(AssetInfo info)
        {
            if (info == null) return false;

            // custom folder via reflection is not available in this Unity version for .unitypackage assets
            if (!CustomFolderReflectionAvailable && info.AssetSource != Asset.Source.RegistryPackage && info.AssetSource != Asset.Source.Archive)
            {
                return true;
            }

            if (info.ForeignId <= 0) return false;
            return _noCustomTargetFolderForeignIds.Contains(info.ForeignId);
        }

        private static bool ContainsProjectSettings(string archivePath)
        {
            try
            {
                using FileStream stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using SharpCompress.Readers.IReader reader = SharpCompress.Readers.ReaderFactory.Open(stream);
                while (reader.MoveToNextEntry())
                {
                    if (reader.Entry.IsDirectory) continue;

                    string entryName = reader.Entry.Key;
                    if (entryName != null && entryName.EndsWith("/pathname"))
                    {
                        using Stream entryStream = reader.OpenEntryStream();
                        using StreamReader sr = new StreamReader(entryStream);
                        string assetPath = sr.ReadLine();
                        if (assetPath != null && assetPath.StartsWith("ProjectSettings/"))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not scan package '{archivePath}' for ProjectSettings: {e.Message}");
            }

            return false;
        }

        private static AddRequest ImportPackage(AssetInfo info, string version)
        {
            AddRequest result;
            AddRegistry(info.Registry);
            switch (info.PackageSource)
            {
                case PackageSource.Git:
                    Repository repo = JsonConvert.DeserializeObject<Repository>(info.Repository);
                    if (repo == null)
                    {
                        Debug.LogError($"Repository for {info} is not maintained.");
                        return null;
                    }
                    if (string.IsNullOrWhiteSpace(repo.revision))
                    {
                        result = Client.Add($"{repo.url}");
                    }
                    else
                    {
                        result = Client.Add($"{repo.url}#{repo.revision}");
                    }
                    break;

                case PackageSource.Local:
                case PackageSource.LocalTarball:
                    result = Client.Add($"file:{info.GetLocation(true)}");
                    break;

                default:
                    result = Client.Add($"{info.SafeName}@{version}");
                    break;
            }

            return result;
        }

        private static void AddRegistry(string registry)
        {
            if (string.IsNullOrEmpty(registry)) return;
            if (registry == Asset.UNITY_REGISTRY) return;
            ScopedRegistry sr = JsonConvert.DeserializeObject<ScopedRegistry>(registry);
            if (sr == null) return;

            string manifestFile = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");

            // do direct JSON manipulation instead of typed approach to be future-safe and don't accidentally remove other data
            JObject content = JObject.Parse(File.ReadAllText(manifestFile));
            JArray registries = (JArray)content["scopedRegistries"];
            if (registries == null)
            {
                registries = new JArray();
                content["scopedRegistries"] = registries;
            }

            // do nothing if already existent
            if (registries.Any(r => r["name"]?.Value<string>() == sr.name && r["url"]?.Value<string>() == sr.url)) return;

            registries.Add(JToken.FromObject(sr));

            File.WriteAllText(manifestFile, content.ToString());
        }

        private void AddProgress()
        {
            if (!_addRequest.IsCompleted) return;

            EditorApplication.update -= AddProgress;

            if (_addRequest.Status == StatusCode.Success)
            {
                _curInfo.ImportState = AssetInfo.ImportStateOptions.Imported;
            }
            else
            {
                _curInfo.ImportState = AssetInfo.ImportStateOptions.Failed;
                Debug.LogError($"Importing {_curInfo} failed: {_addRequest.Error.message}");
            }
            BuildIfReady();
        }

        private void OnInspectorUpdate()
        {
            if (NeedsStatusRefresh()) RefreshStatus();
        }
    }
}
