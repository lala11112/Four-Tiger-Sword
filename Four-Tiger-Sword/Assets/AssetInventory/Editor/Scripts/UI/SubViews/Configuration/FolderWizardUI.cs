using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using ImpossibleRobert.Common;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed class FolderWizardUI : EditorWindow
    {
        internal readonly struct FolderScanResult
        {
            public readonly bool IsUnityFolder;
            public readonly int PackageCount;
            public readonly int DevPackageCount;
            public readonly int MediaCount;
            public readonly int ArchiveCount;

            public FolderScanResult(bool isUnityFolder, int packageCount, int devPackageCount, int mediaCount, int archiveCount)
            {
                IsUnityFolder = isUnityFolder;
                PackageCount = packageCount;
                DevPackageCount = devPackageCount;
                MediaCount = mediaCount;
                ArchiveCount = archiveCount;
            }
        }

        private static readonly Vector2 WindowSize = new Vector2(738, 530);

        private string _folder;
        private bool _calculating;
        private bool _scanComplete;
        private bool _scanCancelled;
        private bool _cancellationRequested;
        private string _scanError;
        private int _filesScanned;
        private int _scanVersion;
        private double _scanStartedAt;
        private CancellationTokenSource _scanCancellation;
        private ProgressBar _scanProgress;
        private Button _cancelScanButton;
        private IVisualElementScheduledItem _progressSchedule;
        private bool _activateUnityPackages = true;
        private bool _activateMediaFolders = true;
        private bool _activateArchives = true;
        private bool _activateDevPackages;
        private bool _unityPackagesAlreadyActive;
        private bool _mediaFoldersAlreadyActive;
        private bool _archivesAlreadyActive;
        private bool _devPackagesAlreadyActive;
        private int _packageCount;
        private int _devPackageCount;
        private int _mediaCount;
        private int _archiveCount;
        private bool _isUnityFolder;

        public static FolderWizardUI ShowWindow()
        {
            FolderWizardUI window = GetWindow<FolderWizardUI>("Folder Wizard");
            window.minSize = WindowSize;
            window.maxSize = window.minSize;

            return window;
        }

        public void Init(string folder)
        {
            BeginScan(folder);
        }

        private void CreateGUI()
        {
            Build();
        }

        private void OnDestroy()
        {
            StopScan();
            PauseProgressUpdates();
        }

        private void Build()
        {
            VisualElement root = rootVisualElement;
            if (root == null) return;

            PauseProgressUpdates();
            _scanProgress = null;
            _cancelScanButton = null;
            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);

            if (string.IsNullOrWhiteSpace(_folder))
            {
                Button chooseFolder = AssetInventoryUITK.CreatePrimaryButton("Choose Folder...", ChooseFolder);
                root.Add(AssetInventoryUITK.CreateEmptyState(
                    "No folder selected",
                    "Choose the folder Asset Inventory should inspect for packages, media files, archives, or development packages.",
                    chooseFolder));
                return;
            }

            VisualElement folderSection = AssetInventoryUITK.CreateSection("Folder");
            folderSection.Add(AssetInventoryUITK.CreateKeyValueRow("Location", _folder));
            if (_isUnityFolder)
            {
                folderSection.Add(AssetInventoryUITK.CreateFieldRow("Detected Type", AssetInventoryUITK.CreateStatusPill("Unity Project")));
            }
            root.Add(folderSection);

            root.Add(AssetInventoryUITK.CreateHelpBox(
                "Folders can be scanned for different file types. Each file type uses a separate importer that can be activated now and configured with additional settings afterwards.",
                MessageType.None));

            if (_scanCancelled)
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("Folder scan cancelled. No folder settings were added.", MessageType.Warning));
            }
            else if (!string.IsNullOrWhiteSpace(_scanError))
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("Folder scan failed: " + _scanError, MessageType.Error));
            }

            VisualElement grid = new VisualElement();
            grid.AddToClassList("ai-card-grid");
            grid.AddToClassList("ai-folder-wizard-grid");
            grid.SetEnabled(_scanComplete && !_calculating);

            grid.Add(CreateFolderTypeCard(
                "Unity Packages",
                new[]
                {
                    "Scans for *.unitypackage files",
                    "Creates packages from file names",
                    "Links packages to Asset Store entries",
                    "Extracts previews from packages"
                },
                GetCountText("packages", _packageCount),
                _unityPackagesAlreadyActive,
                _activateUnityPackages,
                value => _activateUnityPackages = value));

            grid.Add(CreateFolderTypeCard(
                "Media Files",
                new[]
                {
                    "Scans for images, audio, models, and other files",
                    "Creates a package with the folder name",
                    "Creates previews while indexing"
                },
                GetCountText("files", _mediaCount),
                _mediaFoldersAlreadyActive,
                _activateMediaFolders,
                value => _activateMediaFolders = value));

            grid.Add(CreateFolderTypeCard(
                "Archives",
                new[]
                {
                    "Scans for zip, 7z, and rar archives",
                    "Creates a package with the archive name",
                    "Creates previews while indexing"
                },
                GetCountText("archives", _archiveCount),
                _archivesAlreadyActive,
                _activateArchives,
                value => _activateArchives = value));

            grid.Add(CreateFolderTypeCard(
                "Dev Packages",
                new[]
                {
                    "Scans for package.json files",
                    "Creates registry packages from manifests",
                    "Allows importing through direct file references",
                    "Creates previews while indexing"
                },
                GetCountText("packages", _devPackageCount),
                _devPackagesAlreadyActive,
                _activateDevPackages,
                value => _activateDevPackages = value));

            root.Add(grid);
            root.Add(AssetInventoryUITK.CreateFlexibleSpacer());

            VisualElement footer = AssetInventoryUITK.CreateWindowFooter();
            if (_calculating)
            {
                VisualElement progressRow = new VisualElement();
                progressRow.AddToClassList("ai-progress-row");
                progressRow.AddToClassList("ai-folder-wizard-progress-row");
                _scanProgress = AssetInventoryUITK.CreateProgressBar(GetScanProgressTitle(), 0.15f);
                progressRow.Add(_scanProgress);
                _cancelScanButton = AssetInventoryUITK.CreateSecondaryButton("Cancel", RequestScanCancellation);
                progressRow.Add(_cancelScanButton);
                footer.Add(progressRow);
                _progressSchedule = root.schedule.Execute(UpdateScanProgress).Every(100);
            }
            else
            {
                if (_scanComplete)
                {
                    footer.Add(AssetInventoryUITK.CreatePrimaryButton("Add", SaveSettings));
                }
                else
                {
                    footer.Add(AssetInventoryUITK.CreatePrimaryButton("Scan Again", () => BeginScan(_folder)));
                }
            }
            root.Add(footer);
        }

        private string GetCountText(string itemType, int count)
        {
            return _scanComplete ? $"Detected {itemType}: {count:N0}" : "Waiting for scan results";
        }

        private VisualElement CreateFolderTypeCard(string title, string[] lines, string countText, bool alreadyActive, bool active, Action<bool> activeChanged)
        {
            VisualElement card = new VisualElement();
            card.AddToClassList("ai-choice-card");
            card.AddToClassList("ai-folder-wizard-card");
            card.EnableInClassList("ai-choice-card-active", alreadyActive || active);

            Label titleLabel = new Label(title);
            titleLabel.AddToClassList("ai-choice-card-title");
            card.Add(titleLabel);

            for (int i = 0; i < lines.Length; i++)
            {
                Label copy = new Label("- " + lines[i]);
                copy.AddToClassList("ai-choice-card-copy");
                card.Add(copy);
            }

            card.Add(AssetInventoryUITK.CreateFlexibleSpacer());

            Label count = new Label(countText);
            count.AddToClassList("ai-choice-card-meta");
            card.Add(count);

            VisualElement footer = new VisualElement();
            footer.AddToClassList("ai-choice-card-footer");
            if (alreadyActive)
            {
                footer.Add(AssetInventoryUITK.CreateStatusPill("Already Active"));
            }
            else
            {
                VisualElement activation = new VisualElement();
                activation.AddToClassList("ai-folder-wizard-activation");

                Toggle toggle = new Toggle
                {
                    value = active
                };
                toggle.tooltip = $"Activate {title} for this folder.";
                toggle.RegisterValueChangedCallback(evt =>
                {
                    activeChanged(evt.newValue);
                    card.EnableInClassList("ai-choice-card-active", evt.newValue);
                });
                activation.Add(toggle);

                Label activateLabel = new Label("Activate");
                activateLabel.AddToClassList("ai-folder-wizard-activation-label");
                activateLabel.tooltip = toggle.tooltip;
                activateLabel.RegisterCallback<ClickEvent>(_ => toggle.value = !toggle.value);
                activation.Add(activateLabel);
                footer.Add(activation);
            }

            card.Add(footer);
            return card;
        }

        private void SaveSettings()
        {
            if (_calculating || !_scanComplete) return;

            if (_activateUnityPackages && !_unityPackagesAlreadyActive) AI.Config.folders.Add(GetSpec(_folder, 0));
            FolderSpec mediaSpec = null;
            if (_activateMediaFolders && !_mediaFoldersAlreadyActive)
            {
                mediaSpec = GetSpec(_folder, 1);
                AI.Config.folders.Add(mediaSpec);
            }
            if (_activateArchives && !_archivesAlreadyActive) AI.Config.folders.Add(GetSpec(_folder, 2));
            if (_activateDevPackages && !_devPackagesAlreadyActive) AI.Config.folders.Add(GetSpec(_folder, 3));

            AI.SaveConfig();
            Close();

            if (mediaSpec != null)
            {
                FolderFineTuneUI.ShowWindow(mediaSpec);
            }
        }

        private void ChooseFolder()
        {
            string folder = EditorUtility.OpenFolderPanel("Choose Folder", string.Empty, string.Empty);
            if (string.IsNullOrWhiteSpace(folder)) return;

            BeginScan(folder);
        }

        private FolderSpec GetSpec(string folder, int type)
        {
            FolderSpec spec = new FolderSpec();
            spec.folderType = type;
            spec.location = folder;
            if (Paths.IsRel(folder))
            {
                spec.storeRelative = true;
                spec.relativeKey = Paths.GetRelKey(folder);
            }

            // scan for all files if that is a Unity project
            if (type == 1 && _isUnityFolder) spec.scanFor = 1;

            return spec;
        }

        private async void BeginScan(string folder)
        {
            StopScan();

            _folder = folder;
            _scanComplete = false;
            _scanCancelled = false;
            _cancellationRequested = false;
            _scanError = null;
            _filesScanned = 0;
            _packageCount = 0;
            _devPackageCount = 0;
            _mediaCount = 0;
            _archiveCount = 0;
            _isUnityFolder = false;
            _activateUnityPackages = false;
            _activateMediaFolders = false;
            _activateArchives = false;
            _activateDevPackages = false;

            List<FolderSpec> configuredFolders = AI.Config?.folders;
            _unityPackagesAlreadyActive = configuredFolders?.Any(spec => spec.location == _folder && spec.folderType == 0) == true;
            _mediaFoldersAlreadyActive = configuredFolders?.Any(spec => spec.location == _folder && spec.folderType == 1) == true;
            _archivesAlreadyActive = configuredFolders?.Any(spec => spec.location == _folder && spec.folderType == 2) == true;
            _devPackagesAlreadyActive = configuredFolders?.Any(spec => spec.location == _folder && spec.folderType == 3) == true;

            // determine media extensions
            AI.AssetGroup[] mediaGroups = {AI.AssetGroup.Audio, AI.AssetGroup.Images, AI.AssetGroup.Models};
            HashSet<string> mediaTypes = new HashSet<string>(
                mediaGroups.SelectMany(group => AI.TypeGroups[group]),
                StringComparer.OrdinalIgnoreCase);

            string resolvedFolder = Paths.DeRel(_folder);
            int scanVersion = _scanVersion;
            CancellationTokenSource cancellation = new CancellationTokenSource();
            _scanCancellation = cancellation;
            CancellationToken token = cancellation.Token;
            _calculating = true;
            _scanStartedAt = EditorApplication.timeSinceStartup;
            Build();

            try
            {
                FolderScanResult result = await Task.Run(
                    () => ScanFolder(resolvedFolder, mediaTypes, token, count => Volatile.Write(ref _filesScanned, count)),
                    token);

                if (scanVersion != _scanVersion || token.IsCancellationRequested) return;

                _isUnityFolder = result.IsUnityFolder;
                _packageCount = result.PackageCount;
                _devPackageCount = result.DevPackageCount;
                _mediaCount = result.MediaCount;
                _archiveCount = result.ArchiveCount;

                _activateUnityPackages = _packageCount > 0 && !_isUnityFolder;
                _activateMediaFolders = _mediaCount > 0 || _isUnityFolder;
                _activateArchives = _archiveCount > 0 && !_isUnityFolder;
                _scanComplete = true;
            }
            catch (OperationCanceledException)
            {
                if (scanVersion == _scanVersion) _scanCancelled = true;
            }
            catch (Exception e)
            {
                if (scanVersion == _scanVersion) _scanError = e.Message;
            }
            finally
            {
                if (scanVersion == _scanVersion)
                {
                    if (ReferenceEquals(_scanCancellation, cancellation))
                    {
                        _scanCancellation = null;
                        cancellation.Dispose();
                    }

                    _calculating = false;
                    _cancellationRequested = false;
                    Build();
                }
            }
        }

        private void RequestScanCancellation()
        {
            if (!_calculating || _scanCancellation == null) return;

            _cancellationRequested = true;
            _cancelScanButton?.SetEnabled(false);
            _scanCancellation.Cancel();
            UpdateScanProgress();
        }

        private void StopScan()
        {
            _scanVersion++;
            CancellationTokenSource cancellation = _scanCancellation;
            _scanCancellation = null;
            if (cancellation != null)
            {
                cancellation.Cancel();
                cancellation.Dispose();
            }

            _calculating = false;
            _cancellationRequested = false;
        }

        private void PauseProgressUpdates()
        {
            _progressSchedule?.Pause();
            _progressSchedule = null;
        }

        private void UpdateScanProgress()
        {
            if (_scanProgress == null) return;

            _scanProgress.title = GetScanProgressTitle();
            double elapsed = Math.Max(0d, EditorApplication.timeSinceStartup - _scanStartedAt);
            _scanProgress.value = 0.15f + Mathf.PingPong((float)elapsed * 0.35f, 0.7f);
        }

        private string GetScanProgressTitle()
        {
            int filesScanned = Volatile.Read(ref _filesScanned);
            string action = _cancellationRequested ? "Cancelling folder scan" : "Scanning folder";
            if (filesScanned <= 0) return action + "...";

            string fileWord = filesScanned == 1 ? "file" : "files";
            return $"{action}... {filesScanned:N0} {fileWord} inspected";
        }

        internal static FolderScanResult ScanFolder(string folder, ISet<string> mediaTypes, CancellationToken token, Action<int> progress = null)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                throw new DirectoryNotFoundException("The selected folder could not be found.");
            }

            bool isUnityFolder = AssetUtils.IsUnityProject(folder);
            string rootPath = isUnityFolder ? Path.Combine(folder, "Assets") : folder;
            int packageCount = 0;
            int devPackageCount = 0;
            int mediaCount = 0;
            int archiveCount = 0;
            int filesScanned = 0;

            foreach (string file in IOUtils.GetFilesSafe(rootPath, "*.*"))
            {
                token.ThrowIfCancellationRequested();

                filesScanned++;
                string extension = GetExtension(file);
                if (extension == "unitypackage") packageCount++;
                if (extension == "zip" || extension == "rar" || extension == "7z") archiveCount++;
                if (string.Equals(Path.GetFileName(file), "package.json", StringComparison.OrdinalIgnoreCase)) devPackageCount++;
                if (mediaTypes.Contains(extension)) mediaCount++;

                if (filesScanned % 256 == 0) progress?.Invoke(filesScanned);
            }

            progress?.Invoke(filesScanned);
            token.ThrowIfCancellationRequested();

            return new FolderScanResult(isUnityFolder, packageCount, devPackageCount, mediaCount, archiveCount);
        }

        private static string GetExtension(string fileName)
        {
            return IOUtils.GetExtensionWithoutDot(fileName).ToLowerInvariant();
        }
    }
}
