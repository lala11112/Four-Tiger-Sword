using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public sealed class UpdateObserver
    {
        public bool InitializationDone;
        public float InitializationProgress;
        public bool PrioInitializationDone;
        public float PrioInitializationProgress;
        public int DownloadCount;

        private List<AssetInfo> _all = new List<AssetInfo>();
        private List<AssetInfo> _prioritized;
        private readonly Dictionary<int, AssetDownloader> _loaders = new Dictionary<int, AssetDownloader>();
        private readonly Dictionary<int, ObservedAssetFiles> _observedAssetFiles = new Dictionary<int, ObservedAssetFiles>();
        private readonly Dictionary<AssetInfo, ObservedAssetFiles> _transientObservedAssetFiles = new Dictionary<AssetInfo, ObservedAssetFiles>();
        private readonly Action<AssetInfo> _observedAssetChanged;

        private int _prioCount;
        private int _curIndex;
        private int _observationScanIndex;
        private DateTime _lastObserverActivity;
        private string _path;
        private bool _observationActive;
        private bool _observationSweepChanged;
        private bool _cleanObservationSweepCompleted;
        private const int DOWNLOAD_SCAN_BATCH_SIZE = 1024;

        public UpdateObserver(string path, IEnumerable<string> fileTypes)
            : this(path, fileTypes, null)
        {
        }

        internal UpdateObserver(string path, IEnumerable<string> fileTypes, Action<AssetInfo> observedAssetChanged)
        {
            _path = path;
            _ = fileTypes; // retained for public API compatibility; observation is scoped to package-owned paths
            _observedAssetChanged = observedAssetChanged ?? RefreshChangedAsset;
            _lastObserverActivity = DateTime.Now;

            EditorApplication.update += ProcessEditorUpdate;
        }

        private DateTime _lastDownloadScan = DateTime.MinValue;
        private int _lastDownloadCount;
        private int _downloadScanIndex;
        private int _downloadScanCount;
        private bool _downloadScanInProgress;

        private void ProcessEditorUpdate()
        {
            AssetInventorySettings config = AI.Config;
            if (config == null) return;

            if (_all == null || _all.Count == 0)
            {
                _cleanObservationSweepCompleted = true;
                if (ShouldAutoStop(IsActive(), config.autoStopObservation, _cleanObservationSweepCompleted,
                        _lastObserverActivity, DateTime.Now, config.observationTimeout))
                {
                    Stop();
                }
                return;
            }

            RefreshActiveDownloads();
            if (IsActive()) ProcessObservationBatch(config.observationSpeed);

            if (!InitializationDone)
            {
                RefreshInitializationBatch(config.observationSpeed);
            }

            if (ShouldAutoStop(IsActive(), config.autoStopObservation, _cleanObservationSweepCompleted,
                    _lastObserverActivity, DateTime.Now, config.observationTimeout))
            {
                Stop();
            }
        }

        private void RefreshInitializationBatch(int batchSize)
        {
            int cycleEnd = GetRefreshCycleEnd(_curIndex, _all.Count, batchSize);
            while (_curIndex < cycleEnd)
            {
                AssetInfo info = _all[_curIndex];

                bool isPrio = _curIndex < _prioCount;
                bool isDirty = info.PackageDownloader.IsDirty;
                bool isStable = info.PackageDownloader.IsStable;

                if (!isStable || isPrio || isDirty)
                {
                    TimeSpan refreshInterval = TimeSpan.FromSeconds(isPrio ? 5 : (isDirty ? 2 : 60));
                    if (DateTime.Now - info.PackageDownloader.lastRefresh > refreshInterval)
                    {
                        info.Refresh();
                        info.PackageDownloader.RefreshState();
                    }
                }

                _curIndex++;
            }

            InitializationProgress = (float)_curIndex / _all.Count;
            PrioInitializationProgress = _prioCount > 0 ? Mathf.Min(1f, (float)_curIndex / _prioCount) : 1f;
            if (_curIndex >= _prioCount) PrioInitializationDone = true;
            if (_curIndex >= _all.Count) InitializationDone = true;
        }

        internal int ProcessObservationBatch(int batchSize)
        {
            if (!IsActive() || _all == null || _all.Count == 0) return 0;

            int changedCount = 0;
            int cycleEnd = GetRefreshCycleEnd(_observationScanIndex, _all.Count, batchSize);
            while (_observationScanIndex < cycleEnd)
            {
                AssetInfo info = _all[_observationScanIndex];
                ObservedAssetFiles observedFiles = GetObservedAssetFiles(info);
                if (observedFiles != null && observedFiles.Poll(info, _path))
                {
                    _lastObserverActivity = DateTime.Now;
                    _observationSweepChanged = true;
                    changedCount++;
                    _observedAssetChanged(info);
                }

                _observationScanIndex++;
            }

            if (_observationScanIndex >= _all.Count)
            {
                _cleanObservationSweepCompleted = !_observationSweepChanged;
                _observationSweepChanged = false;
                _observationScanIndex = 0;
            }

            return changedCount;
        }

        private ObservedAssetFiles GetObservedAssetFiles(AssetInfo info)
        {
            if (info == null) return null;

            if (info.AssetId > 0)
            {
                if (!_observedAssetFiles.TryGetValue(info.AssetId, out ObservedAssetFiles observedFiles))
                {
                    observedFiles = new ObservedAssetFiles();
                    _observedAssetFiles.Add(info.AssetId, observedFiles);
                }
                return observedFiles;
            }

            if (!_transientObservedAssetFiles.TryGetValue(info, out ObservedAssetFiles transientObservedFiles))
            {
                transientObservedFiles = new ObservedAssetFiles();
                _transientObservedAssetFiles.Add(info, transientObservedFiles);
            }
            return transientObservedFiles;
        }

        private void RefreshChangedAsset(AssetInfo info)
        {
            if (info == null) return;

            Attach(info);
            AssetDownloader downloader = info.PackageDownloader;
            if (downloader != null) downloader.IsDirty = true;
            info.Refresh();
            downloader?.RefreshState(true);
        }

        internal static bool ShouldAutoStop(bool isActive, bool autoStopEnabled, bool cleanSweepCompleted,
            DateTime lastActivity, DateTime now, int timeoutSeconds)
        {
            if (!isActive || !autoStopEnabled || !cleanSweepCompleted) return false;

            return now - lastActivity > TimeSpan.FromSeconds(timeoutSeconds);
        }

        internal static int GetRefreshCycleEnd(int currentIndex, int itemCount, int configuredSpeed)
        {
            int safeCurrentIndex = Mathf.Clamp(currentIndex, 0, itemCount);
            int batchSize = Mathf.Max(1, configuredSpeed);
            return Mathf.Min(safeCurrentIndex + batchSize, itemCount);
        }

        private void RefreshActiveDownloads()
        {
            TimeSpan scanInterval = _lastDownloadCount > 0
                ? TimeSpan.FromMilliseconds(250)
                : TimeSpan.FromSeconds(2);
            if (!_downloadScanInProgress)
            {
                if (DateTime.Now - _lastDownloadScan < scanInterval) return;

                _downloadScanIndex = 0;
                _downloadScanCount = 0;
                _downloadScanInProgress = true;
            }

            int scanEnd = Mathf.Min(_downloadScanIndex + DOWNLOAD_SCAN_BATCH_SIZE, _all.Count);
            while (_downloadScanIndex < scanEnd)
            {
                int index = _downloadScanIndex++;
                AssetDownloader downloader = _all[index].PackageDownloader;
                if (downloader.GetState().state == AssetDownloader.State.Downloading)
                {
                    _downloadScanCount++;
                    _lastObserverActivity = DateTime.Now;
                    if (!IsActive()) Start();
                    downloader.RefreshState();
                }
                else if (_prioCount == 1 && index == 0)
                {
                    downloader.RefreshState();
                }
            }

            if (_downloadScanIndex < _all.Count) return;

            DownloadCount = _downloadScanCount;
            _lastDownloadCount = DownloadCount;
            _lastDownloadScan = DateTime.Now;
            _downloadScanInProgress = false;
        }

        public void SetPrioritized(List<AssetInfo> prioritized)
        {
            // skip setting the same list twice since that will reset the initialization state
            if (_prioritized != null && _prioritized.Count == prioritized.Count)
            {
                HashSet<int> newIds = new HashSet<int>(prioritized.Select(p => p.AssetId));
                if (newIds.SetEquals(_prioritized.Select(p => p.AssetId))) return;
            }

            // sort prioritized to the beginning of all
            // below two lines are nicer to read but much slower than using a hashset + recreate
            // _all.RemoveAll(prioritized.Contains);
            // _all.InsertRange(0, prioritized);

            _prioritized = prioritized.OrderBy(info => info.PackageDownloader == null ? DateTime.MinValue : info.PackageDownloader.lastRefresh).ToList(); // break reference
            _prioCount = _prioritized.Count;

            // single items will get refreshed automatically, bulk selections need a rescan 
            if (_prioCount > 1)
            {
                InitializationDone = false;
                InitializationProgress = 0;
                PrioInitializationDone = false;
                PrioInitializationProgress = 0;
                _curIndex = 0;
            }

            // Convert prioritized to a HashSet for faster lookups
            HashSet<AssetInfo> prioritizedSet = new HashSet<AssetInfo>(prioritized);

            // Create a new list to hold the re-ordered items
            List<AssetInfo> reordered = new List<AssetInfo>(prioritized);

            // Add non-prioritized items to the reordered list, skipping those in prioritized
            foreach (AssetInfo item in _all)
            {
                if (!prioritizedSet.Contains(item)) reordered.Add(item);
            }
            _all = reordered;
            _downloadScanInProgress = false;
            ResetObservationSweep();

            // only attach downloaders for prioritized items since the rest already have them from SetAll
            foreach (AssetInfo info in prioritized)
            {
                Attach(info);
            }
        }

        public void SetAll(List<AssetInfo> all)
        {
            _curIndex = 0;
            _all = all;
            InitializationDone = false;
            InitializationProgress = 0f;
            PrioInitializationDone = _prioCount == 0;
            PrioInitializationProgress = _prioCount == 0 ? 1f : 0f;
            _downloadScanInProgress = false;
            ResetObservationSweep();
            _transientObservedAssetFiles.Clear();
            AttachDownloaders();
        }

        private void AttachDownloaders()
        {
            _all.ForEach(Attach);
        }

        public void Attach(AssetInfo info)
        {
            if (info.PackageDownloader == null)
            {
                // hook up existing downloads if existent
                if (_loaders.TryGetValue(info.AssetId, out AssetDownloader downloader))
                {
                    info.PackageDownloader = downloader;
                }
                else
                {
                    info.PackageDownloader = new AssetDownloader(info);
                    _loaders.Add(info.AssetId, info.PackageDownloader);
                }
            }

            // update reference in case new data was added
            info.PackageDownloader.SetAsset(info);
        }

        public void SetPath(string path)
        {
            if (PathsEqual(_path, path)) return;

            _path = path;
            _observedAssetFiles.Clear();
            _transientObservedAssetFiles.Clear();
            ResetObservationSweep();
            if (!_observationActive) return;

            if (Directory.Exists(_path))
            {
                _lastObserverActivity = DateTime.Now;
            }
            else
            {
                Stop();
            }
        }

        public void Start()
        {
            // Debug.Log("Start observer");

            _lastObserverActivity = DateTime.Now;
            _observationActive = !string.IsNullOrEmpty(_path) && Directory.Exists(_path);
            ResetObservationSweep();
            if (_all == null || _all.Count == 0) _cleanObservationSweepCompleted = true;
        }

        public void Stop()
        {
            // Debug.Log("Stop observer");

            _lastObserverActivity = DateTime.Now; // set to eliminate potential race conditions stopping it again
            _observationActive = false;
            ResetObservationSweep();
        }

        public bool IsActive()
        {
            return _observationActive;
        }

        internal void Dispose()
        {
            EditorApplication.update -= ProcessEditorUpdate;
            _observationActive = false;
            _observedAssetFiles.Clear();
            _transientObservedAssetFiles.Clear();
            foreach (AssetDownloader downloader in _loaders.Values)
            {
                downloader.Dispose();
            }
            _loaders.Clear();
        }

        private void ResetObservationSweep()
        {
            _observationScanIndex = 0;
            _observationSweepChanged = false;
            _cleanObservationSweepCompleted = false;
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(left, right, ObservedAssetFiles.PathComparison);
        }

        internal sealed class ObservedFileSet
        {
            private string[] _paths = Array.Empty<string>();
            private ObservedFileStamp[] _stamps = Array.Empty<ObservedFileStamp>();
            private bool[] _initialized = Array.Empty<bool>();

            internal int Count => _paths.Length;

            internal void SetPaths(IReadOnlyList<string> paths)
            {
                int pathCount = paths?.Count ?? 0;
                string[] newPaths = new string[pathCount];
                ObservedFileStamp[] newStamps = new ObservedFileStamp[pathCount];
                bool[] newInitialized = new bool[pathCount];

                for (int i = 0; i < pathCount; i++)
                {
                    string path = paths[i];
                    newPaths[i] = path;
                    for (int oldIndex = 0; oldIndex < _paths.Length; oldIndex++)
                    {
                        if (!string.Equals(path, _paths[oldIndex], ObservedAssetFiles.PathComparison)) continue;

                        newStamps[i] = _stamps[oldIndex];
                        newInitialized[i] = _initialized[oldIndex];
                        break;
                    }
                }

                _paths = newPaths;
                _stamps = newStamps;
                _initialized = newInitialized;
            }

            internal bool Poll()
            {
                bool changed = false;
                for (int i = 0; i < _paths.Length; i++)
                {
                    if (!ObservedFileStamp.TryCapture(_paths[i], out ObservedFileStamp current)) continue;

                    if (!_initialized[i])
                    {
                        _stamps[i] = current;
                        _initialized[i] = true;
                        continue;
                    }

                    if (_stamps[i].Equals(current)) continue;

                    _stamps[i] = current;
                    changed = true;
                }
                return changed;
            }
        }

        internal readonly struct ObservedFileStamp : IEquatable<ObservedFileStamp>
        {
            private readonly bool _exists;
            private readonly long _length;
            private readonly long _creationTimeUtcTicks;
            private readonly long _lastWriteTimeUtcTicks;

            private ObservedFileStamp(bool exists, long length, long creationTimeUtcTicks, long lastWriteTimeUtcTicks)
            {
                _exists = exists;
                _length = length;
                _creationTimeUtcTicks = creationTimeUtcTicks;
                _lastWriteTimeUtcTicks = lastWriteTimeUtcTicks;
            }

            internal static bool TryCapture(string path, out ObservedFileStamp stamp)
            {
                stamp = default;
                if (string.IsNullOrWhiteSpace(path)) return false;

                try
                {
                    FileInfo file = new FileInfo(path);
                    if (!file.Exists)
                    {
                        stamp = new ObservedFileStamp(false, 0, 0, 0);
                        return true;
                    }

                    file.Refresh();
                    stamp = new ObservedFileStamp(true, file.Length, file.CreationTimeUtc.Ticks, file.LastWriteTimeUtc.Ticks);
                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }
                catch (IOException)
                {
                    return false;
                }
                catch (NotSupportedException)
                {
                    return false;
                }
                catch (UnauthorizedAccessException)
                {
                    return false;
                }
            }

            public bool Equals(ObservedFileStamp other)
            {
                return _exists == other._exists
                       && _length == other._length
                       && _creationTimeUtcTicks == other._creationTimeUtcTicks
                       && _lastWriteTimeUtcTicks == other._lastWriteTimeUtcTicks;
            }

            public override bool Equals(object obj)
            {
                return obj is ObservedFileStamp other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = _exists.GetHashCode();
                    hashCode = (hashCode * 397) ^ _length.GetHashCode();
                    hashCode = (hashCode * 397) ^ _creationTimeUtcTicks.GetHashCode();
                    hashCode = (hashCode * 397) ^ _lastWriteTimeUtcTicks.GetHashCode();
                    return hashCode;
                }
            }
        }

        internal sealed class ObservedAssetFiles
        {
#if UNITY_EDITOR_LINUX
            internal const StringComparison PathComparison = StringComparison.Ordinal;
#else
            internal const StringComparison PathComparison = StringComparison.OrdinalIgnoreCase;
#endif

            private readonly ObservedFileSet _files = new ObservedFileSet();
            private string _observationRoot;
            private string _location;
            private string _safePublisher;
            private string _safeCategory;
            private string _safeName;
            private int _foreignId;

            internal bool Poll(AssetInfo info, string observationRoot)
            {
                EnsurePaths(info, observationRoot);
                return _files.Poll();
            }

            private void EnsurePaths(AssetInfo info, string observationRoot)
            {
                AssetInfo downloadAsset = info.GetRoot() ?? info;
                if (string.Equals(_observationRoot, observationRoot, PathComparison)
                    && string.Equals(_location, info.Location, StringComparison.Ordinal)
                    && string.Equals(_safePublisher, downloadAsset.SafePublisher, StringComparison.Ordinal)
                    && string.Equals(_safeCategory, downloadAsset.SafeCategory, StringComparison.Ordinal)
                    && string.Equals(_safeName, downloadAsset.SafeName, StringComparison.Ordinal)
                    && _foreignId == downloadAsset.ForeignId)
                {
                    return;
                }

                _observationRoot = observationRoot;
                _location = info.Location;
                _safePublisher = downloadAsset.SafePublisher;
                _safeCategory = downloadAsset.SafeCategory;
                _safeName = downloadAsset.SafeName;
                _foreignId = downloadAsset.ForeignId;

                string calculatedLocation = downloadAsset.GetCalculatedLocation();
                string actualLocation = info.GetLocation(true);
                _files.SetPaths(BuildObservedPaths(observationRoot, calculatedLocation, actualLocation,
                    downloadAsset.SafeName, downloadAsset.ForeignId));
            }
        }

        internal static string[] BuildObservedPaths(string observationRoot, string calculatedLocation,
            string actualLocation, string safeName, int foreignId)
        {
            List<string> paths = new List<string>(10);
            AddPackagePaths(paths, observationRoot, calculatedLocation);
            AddPackagePaths(paths, observationRoot, actualLocation);

            string calculatedFile = NormalizeObservedPath(calculatedLocation);
            if (!string.IsNullOrEmpty(calculatedFile) && foreignId > 0 && !string.IsNullOrEmpty(safeName))
            {
                string folder = Path.GetDirectoryName(calculatedFile);
                if (!string.IsNullOrEmpty(folder))
                {
                    string downloadFile = Path.Combine(folder, $".{safeName}-{foreignId}.tmp");
                    string reDownloadFile = Path.Combine(folder, $".{safeName}-content__{foreignId}.tmp");
                    AddObservedPath(paths, observationRoot, downloadFile);
                    AddObservedPath(paths, observationRoot, downloadFile + ".json");
                    AddObservedPath(paths, observationRoot, reDownloadFile);
                    AddObservedPath(paths, observationRoot, reDownloadFile + ".json");
                }
            }

            return paths.ToArray();
        }

        private static void AddPackagePaths(List<string> paths, string observationRoot, string packagePath)
        {
            string normalizedPackagePath = NormalizeObservedPath(packagePath);
            if (string.IsNullOrEmpty(normalizedPackagePath)) return;

            AddObservedPath(paths, observationRoot, normalizedPackagePath);
            AddObservedPath(paths, observationRoot, normalizedPackagePath + ".info.json");
            AddObservedPath(paths, observationRoot, normalizedPackagePath + ".icon.png");
        }

        private static void AddObservedPath(List<string> paths, string observationRoot, string path)
        {
            string normalizedPath = NormalizeObservedPath(path);
            string normalizedRoot = NormalizeObservedPath(observationRoot);
            if (string.IsNullOrEmpty(normalizedPath) || string.IsNullOrEmpty(normalizedRoot)) return;
            if (!IsSameOrChildPath(normalizedPath, normalizedRoot)) return;

            for (int i = 0; i < paths.Count; i++)
            {
                if (string.Equals(paths[i], normalizedPath, ObservedAssetFiles.PathComparison)) return;
            }
            paths.Add(normalizedPath);
        }

        private static string NormalizeObservedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            int subPathIndex = path.IndexOf(Asset.SUB_PATH);
            if (subPathIndex >= 0) path = path.Substring(0, subPathIndex);
            if (string.IsNullOrWhiteSpace(path)) return null;

            try
            {
                return Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
            catch (PathTooLongException)
            {
                return null;
            }
        }

        private static bool IsSameOrChildPath(string path, string root)
        {
            if (string.Equals(path, root, ObservedAssetFiles.PathComparison)) return true;
            if (path.Length <= root.Length || !path.StartsWith(root, ObservedAssetFiles.PathComparison)) return false;

            char separator = path[root.Length];
            return separator == Path.DirectorySeparatorChar || separator == Path.AltDirectorySeparatorChar;
        }
    }
}
