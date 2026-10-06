using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed class FolderFineTuneUI : EditorWindow
    {
        private struct PackagePreview
        {
            public string Name;
            public int FileCount;
        }

        private FolderSpec _original;
        private FolderSpec _draft;

        private bool _scanning;
        private List<PackagePreview> _packages = new List<PackagePreview>();
        private int _totalFiles;
        private double _lastChangeTime;
        private bool _needsRescan;
        private CancellationTokenSource _cts;
        private string _scanError;
        private string _resolvedPath;
        private IVisualElementScheduledItem _rescanSchedule;
        private int _scanVersion;

        private static readonly string[] PackageModeOptions = {"Root Folder", "First Level Directories", "Second Level Directories"};
        private static readonly string[] PackageModeDescriptions =
        {
            "All files are grouped into a single package named after the root folder.",
            "Each direct subfolder becomes its own package.",
            "Each second-level subfolder becomes its own package."
        };

        public static FolderFineTuneUI ShowWindow(FolderSpec spec)
        {
            FolderFineTuneUI window = GetWindow<FolderFineTuneUI>("Fine-Tune Media Folder");
            window.minSize = new Vector2(520, 480);
            window.Init(spec);
            window.Show();

            return window;
        }

        private void Init(FolderSpec spec)
        {
            _original = spec;
            _draft = new FolderSpec(spec);
            _resolvedPath = _draft.GetLocation(true);
            _lastChangeTime = 0;
            _needsRescan = true;
            Build();
            ScheduleRescan();
        }

        private void CreateGUI()
        {
            Build();
            ScheduleRescan();
        }

        private void OnDestroy()
        {
            CancelScan();
            _rescanSchedule?.Pause();
            _rescanSchedule = null;
        }

        private void Build()
        {
            VisualElement root = rootVisualElement;
            if (root == null) return;

            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);

            if (_draft == null)
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("No folder selected.", MessageType.Warning));
                return;
            }

            VisualElement folder = AssetInventoryUITK.CreateSection("Folder");
            folder.Add(AssetInventoryUITK.CreateKeyValueRow("Location", _resolvedPath ?? _draft.location));
            root.Add(folder);

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.Add(CreateFileFilterSection());
            scroll.Add(CreatePackageModeSection());
            scroll.Add(CreatePreviewSection());
            root.Add(scroll);

            root.Add(CreateFooter());
        }

        private VisualElement CreateFileFilterSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Which Files to Index");

            List<string> mediaTypes = new List<string>(IndexUI.MediaTypes);
            int selectedIndex = Mathf.Clamp(_draft.scanFor, 0, mediaTypes.Count - 1);
            PopupField<string> scanFor = new PopupField<string>(mediaTypes, selectedIndex);
            scanFor.tooltip = "Determines which files are scanned and added to the index.";
            scanFor.RegisterValueChangedCallback(evt =>
            {
                _draft.scanFor = mediaTypes.IndexOf(evt.newValue);
                MarkChanged();
                Build();
            });
            section.Add(AssetInventoryUITK.CreateFieldRow("File Types", scanFor));
            AddHint(section, GetScanHint(_draft.scanFor));

            if (_draft.scanFor == 7)
            {
                TextField pattern = new TextField
                {
                    value = _draft.pattern ?? string.Empty,
                    tooltip = "e.g. *.jpg;*.wav"
                };
                pattern.RegisterValueChangedCallback(evt =>
                {
                    _draft.pattern = evt.newValue;
                    MarkChanged();
                });
                section.Add(AssetInventoryUITK.CreateFieldRow("Pattern", pattern));
            }

            section.Add(CreateStringListField("Exclude Extensions", _draft.excludedExtensions, value =>
            {
                _draft.excludedExtensions = value;
                MarkChanged();
            }, "Excluded Extensions", "e.g. blend,max"));
            return section;
        }

        private VisualElement CreatePackageModeSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("How Packages Are Created");

            Toggle createPackages = new Toggle
            {
                value = _draft.attachToPackage,
                tooltip = "Groups indexed files into packages so they can be found and managed more easily."
            };
            createPackages.RegisterValueChangedCallback(evt =>
            {
                _draft.attachToPackage = evt.newValue;
                MarkChanged();
                Build();
            });
            section.Add(AssetInventoryUITK.CreateFieldRow("Create Packages", createPackages));

            if (_draft.attachToPackage)
            {
                List<string> packageModes = new List<string>(PackageModeOptions);
                int selectedIndex = Mathf.Clamp(_draft.packageMode, 0, packageModes.Count - 1);
                PopupField<string> packageMode = new PopupField<string>(packageModes, selectedIndex);
                packageMode.tooltip = "Controls how the folder structure maps to packages.";
                packageMode.RegisterValueChangedCallback(evt =>
                {
                    _draft.packageMode = packageModes.IndexOf(evt.newValue);
                    MarkChanged();
                    Build();
                });
                section.Add(AssetInventoryUITK.CreateFieldRow("Package Mode", packageMode));
                AddHint(section, PackageModeDescriptions[Mathf.Clamp(_draft.packageMode, 0, PackageModeDescriptions.Length - 1)]);
            }
            else
            {
                AddHint(section, $"Files will be listed under the generic '{Asset.NONE}' entry without any package grouping.");
            }

            return section;
        }

        private VisualElement CreatePreviewSection()
        {
            VisualElement section = AssetInventoryUITK.CreateSection("Preview");

            if (_scanning)
            {
                section.Add(AssetInventoryUITK.CreateProgressBar("Scanning folder...", 0.35f));
                return section;
            }

            if (!string.IsNullOrEmpty(_scanError))
            {
                section.Add(AssetInventoryUITK.CreateHelpBox(_scanError, MessageType.Warning));
                return section;
            }

            if (_packages.Count == 0 && _totalFiles == 0)
            {
                section.Add(AssetInventoryUITK.CreateHelpBox("No matching files found with the current settings.", MessageType.Warning));
                return section;
            }

            if (_draft.attachToPackage)
            {
                string packageWord = _packages.Count == 1 ? "package" : "packages";
                string fileWord = _totalFiles == 1 ? "file" : "files";
                section.Add(AssetInventoryUITK.CreateCopyLabel($"{_packages.Count:N0} {packageWord} / {_totalFiles:N0} {fileWord} total"));
            }
            else
            {
                string fileWord = _totalFiles == 1 ? "file" : "files";
                section.Add(AssetInventoryUITK.CreateCopyLabel($"{_totalFiles:N0} {fileWord} will be indexed without package grouping."));
            }

            if (_draft.attachToPackage && _packages.Count > 0)
            {
                int maxVisible = 200;
                int shown = Math.Min(_packages.Count, maxVisible);
                ScrollView list = new ScrollView(ScrollViewMode.Vertical);
                list.AddToClassList("ai-list");
                for (int i = 0; i < shown; i++)
                {
                    PackagePreview pkg = _packages[i];

                    VisualElement row = new VisualElement();
                    row.AddToClassList("ai-list-row");
                    CommonUITK.ApplyListRowStyle(row);
                    if (i % 2 == 1) row.AddToClassList(BaseVerticalCollectionView.itemAlternativeBackgroundUssClassName);
                    Label name = new Label(pkg.Name);
                    name.AddToClassList("ai-list-row-title");
                    row.Add(name);
                    Label count = new Label($"{pkg.FileCount:N0} files");
                    count.AddToClassList("ai-row-meta");
                    row.Add(count);
                    list.Add(row);
                }

                if (_packages.Count > maxVisible)
                {
                    AddHint(list, $"... and {_packages.Count - maxVisible:N0} more");
                }

                section.Add(list);
            }

            return section;
        }

        private VisualElement CreateFooter()
        {
            VisualElement footer = AssetInventoryUITK.CreateWindowFooter();

            Button apply = AssetInventoryUITK.CreatePrimaryButton("Apply", ApplyChanges);
            apply.SetEnabled(!_scanning);
            footer.Add(apply);
            footer.Add(AssetInventoryUITK.CreateSecondaryButton("Cancel", Close));

            return footer;
        }

        private void ApplyChanges()
        {
            _original.scanFor = _draft.scanFor;
            _original.pattern = _draft.pattern;
            _original.excludedExtensions = _draft.excludedExtensions;
            _original.attachToPackage = _draft.attachToPackage;
            _original.packageMode = _draft.packageMode;

            AI.SaveConfig();
            Close();
        }

        private void MarkChanged()
        {
            _lastChangeTime = EditorApplication.timeSinceStartup;
            _needsRescan = true;
            ScheduleRescan();
        }

        private void CancelScan()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
        }

        private async void StartScan()
        {
            CancelScan();
            int scanVersion = ++_scanVersion;

            string fullLocation = _resolvedPath;
            if (string.IsNullOrEmpty(fullLocation) || !Directory.Exists(fullLocation))
            {
                _scanError = "Folder not found.";
                _packages.Clear();
                _totalFiles = 0;
                Build();
                return;
            }

            _scanning = true;
            _scanError = null;
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            // capture draft values for background thread
            int scanFor = _draft.scanFor;
            string pattern = _draft.pattern;
            string excludedExt = _draft.excludedExtensions;
            string excludedDirs = _draft.excludedDirectories;
            bool attachToPackage = _draft.attachToPackage;
            int packageMode = _draft.packageMode;
            bool detectUnity = _draft.detectUnityProjects;
            Build();

            try
            {
                (List<PackagePreview> packages, int total) = await Task.Run(
                    () => ComputePreview(fullLocation, scanFor, pattern, excludedExt, excludedDirs, attachToPackage, packageMode, detectUnity, token),
                    token);

                if (token.IsCancellationRequested || scanVersion != _scanVersion) return;

                _packages = packages;
                _totalFiles = total;
                _scanError = null;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                if (scanVersion != _scanVersion) return;

                _scanError = "Scan failed: " + e.Message;
                _packages.Clear();
                _totalFiles = 0;
            }
            finally
            {
                if (scanVersion == _scanVersion)
                {
                    _scanning = false;
                    Build();
                }
            }
        }

        private void ScheduleRescan()
        {
            if (rootVisualElement == null) return;

            _rescanSchedule?.Pause();
            _rescanSchedule = rootVisualElement.schedule.Execute(() =>
            {
                if (!_needsRescan || EditorApplication.timeSinceStartup - _lastChangeTime <= 0.3) return;

                _needsRescan = false;
                StartScan();
            }).StartingIn(350);
        }

        private static void AddHint(VisualElement parent, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            Label hint = new Label(text);
            hint.AddToClassList("ai-list-row-subtitle");
            hint.AddToClassList("ai-indented-hint");
            parent.Add(hint);
        }

        private static string GetScanHint(int scanFor)
        {
            switch (scanFor)
            {
                case 0:
                    return "Only audio, image and 3D model files will be indexed.";
                case 1:
                    return "Every file in the folder will be indexed regardless of type.";
                case 3:
                    return "Only audio files (wav, mp3, ogg, flac, ...).";
                case 4:
                    return "Only image files (png, jpg, psd, tga, ...).";
                case 5:
                    return "Only 3D model files (fbx, obj, gltf, glb, blend, ...).";
                default:
                    return string.Empty;
            }
        }

        private VisualElement CreateStringListField(string label, string value, Action<string> setValue, string title, string tooltip)
        {
            VisualElement row = AssetInventoryUITK.CreateStringListControl(this, value, ",", setValue, title, tooltip);
            return AssetInventoryUITK.CreateFieldRow(label, row);
        }

        private static (List<PackagePreview>, int) ComputePreview(
            string fullLocation, int scanFor, string pattern, string excludedExt,
            string excludedDirs, bool attachToPackage, int packageMode, bool detectUnity,
            CancellationToken token)
        {
            List<string> searchPatterns = new List<string>();
            List<AI.AssetGroup> types = new List<AI.AssetGroup>();

            switch (scanFor)
            {
                case 0:
                    types.AddRange(new[] {AI.AssetGroup.Audio, AI.AssetGroup.Images, AI.AssetGroup.Models});
                    break;
                case 1:
                    searchPatterns.Add("*.*");
                    break;
                case 3:
                    types.Add(AI.AssetGroup.Audio);
                    break;
                case 4:
                    types.Add(AI.AssetGroup.Images);
                    break;
                case 5:
                    types.Add(AI.AssetGroup.Models);
                    break;
                case 7:
                    if (!string.IsNullOrWhiteSpace(pattern)) searchPatterns.AddRange(pattern.Split(';'));
                    break;
            }

            types.ForEach(t => searchPatterns.AddRange(AI.TypeGroups[t].Select(ext => $"*.{ext}")));

            string[] exclExt = StringUtils.Split(excludedExt, new[] {';', ','});
            string[] exclDirArr = StringUtils.Split(excludedDirs, new[] {';', ','});

            bool treatAsUnityProject = detectUnity && AssetUtils.IsUnityProject(fullLocation);
            string scanPath = treatAsUnityProject ? Path.Combine(fullLocation, "Assets") : fullLocation;

            if (!Directory.Exists(scanPath)) return (new List<PackagePreview>(), 0);

            token.ThrowIfCancellationRequested();

            if (!attachToPackage)
            {
                int count = CountFiles(scanPath, searchPatterns, exclExt, exclDirArr, scanPath, token);
                return (new List<PackagePreview>(), count);
            }

            List<PackagePreview> packages = new List<PackagePreview>();
            int totalFiles = 0;

            if (packageMode == 0)
            {
                // Root Folder: single package
                string name = Path.GetFileName(fullLocation);
                if (treatAsUnityProject) name = Path.GetFileName(fullLocation);
                int count = CountFiles(scanPath, searchPatterns, exclExt, exclDirArr, scanPath, token);
                packages.Add(new PackagePreview {Name = name, FileCount = count});
                totalFiles = count;
            }
            else
            {
                // First Level or Second Level
                IEnumerable<string> targetDirs;

                string[] firstLevelDirs = Directory.GetDirectories(scanPath)
                    .Where(d =>
                    {
                        string rel = d.Substring(scanPath.Length + 1).Replace("\\", "/");
                        return !AssetImporter.IsIgnoredPath(rel, true) && !AssetImporter.IsExcludedDirectory(rel, exclDirArr, false);
                    })
                    .ToArray();

                if (packageMode == 1)
                {
                    targetDirs = firstLevelDirs;
                }
                else
                {
                    // Second Level: get subdirectories of first-level directories
                    targetDirs = firstLevelDirs
                        .SelectMany(firstLevel =>
                        {
                            if (!Directory.Exists(firstLevel)) return Enumerable.Empty<string>();
                            return Directory.GetDirectories(firstLevel)
                                .Where(d =>
                                {
                                    string rel = d.Substring(scanPath.Length + 1).Replace("\\", "/");
                                    return !AssetImporter.IsIgnoredPath(rel, true) && !AssetImporter.IsExcludedDirectory(rel, exclDirArr, false);
                                });
                        });
                }

                foreach (string dir in targetDirs)
                {
                    token.ThrowIfCancellationRequested();

                    string name = Path.GetFileName(dir);
                    int count = CountFiles(dir, searchPatterns, exclExt, exclDirArr, dir, token);
                    if (count > 0)
                    {
                        packages.Add(new PackagePreview {Name = name, FileCount = count});
                        totalFiles += count;
                    }
                }

                packages.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }

            return (packages, totalFiles);
        }

        private static int CountFiles(string path, List<string> searchPatterns, string[] excludedExtensions, string[] excludedDirectories, string basePath, CancellationToken token)
        {
            if (!Directory.Exists(path)) return 0;

            token.ThrowIfCancellationRequested();

            IEnumerable<string> files;
            try
            {
                files = IOUtils.GetFiles(path, searchPatterns, SearchOption.AllDirectories, allowParallel: false);
            }
            catch (Exception)
            {
                return 0;
            }

            int count = 0;
            foreach (string file in files)
            {
                if (count % 500 == 0) token.ThrowIfCancellationRequested();

                string type = IOUtils.GetExtensionWithoutDot(file).ToLowerInvariant();
                if (type == "meta") continue;
                if (excludedExtensions != null && excludedExtensions.Contains(type)) continue;
                if (AssetImporter.IsExcludedDirectory(file, excludedDirectories)) continue;

                string relPath = file.Substring(basePath.Length + 1).Replace("\\", "/");
                if (AssetImporter.IsIgnoredPath(relPath, false)) continue;

                count++;
            }
            return count;
        }
    }
}
