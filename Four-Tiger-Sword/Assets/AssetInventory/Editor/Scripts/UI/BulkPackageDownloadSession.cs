using System;
using System.Collections.Generic;
using UnityEngine;

namespace AssetInventory
{
    internal readonly struct BulkPackageDownloadTargetSpec
    {
        public int AssetId { get; }
        public int ForeignId { get; }
        public string TargetVersion { get; }
        public long ExpectedBytes { get; }

        public BulkPackageDownloadTargetSpec(int assetId, int foreignId, string targetVersion, long expectedBytes)
        {
            AssetId = assetId;
            ForeignId = foreignId;
            TargetVersion = targetVersion ?? string.Empty;
            ExpectedBytes = Math.Max(0, expectedBytes);
        }

        public static BulkPackageDownloadTargetSpec FromAsset(AssetInfo info)
        {
            if (info == null) return default;

            AssetDownloadState state = info.PackageDownloader?.GetState();
            long expectedBytes = info.PackageSize > 0 ? info.PackageSize : (state?.bytesTotal ?? 0);
            string targetVersion = !string.IsNullOrWhiteSpace(info.LatestVersion) ? info.LatestVersion : info.Version;
            return new BulkPackageDownloadTargetSpec(info.AssetId, info.ForeignId, targetVersion, expectedBytes);
        }
    }

    internal readonly struct BulkPackageDownloadObservation
    {
        public int AssetId { get; }
        public int ForeignId { get; }
        public string Version { get; }
        public bool HasState { get; }
        public AssetDownloader.State State { get; }
        public long BytesDownloaded { get; }
        public long BytesTotal { get; }

        public BulkPackageDownloadObservation(
            int assetId,
            int foreignId,
            string version,
            bool hasState,
            AssetDownloader.State state,
            long bytesDownloaded,
            long bytesTotal)
        {
            AssetId = assetId;
            ForeignId = foreignId;
            Version = version ?? string.Empty;
            HasState = hasState;
            State = state;
            BytesDownloaded = Math.Max(0, bytesDownloaded);
            BytesTotal = Math.Max(0, bytesTotal);
        }

        public static BulkPackageDownloadObservation FromAsset(AssetInfo info)
        {
            if (info == null) return default;

            AssetDownloadState state = info.PackageDownloader?.GetState();
            return new BulkPackageDownloadObservation(
                info.AssetId,
                info.ForeignId,
                info.Version,
                state != null,
                state?.state ?? AssetDownloader.State.Initializing,
                state?.bytesDownloaded ?? 0,
                state?.bytesTotal ?? info.PackageSize);
        }
    }

    internal readonly struct BulkPackageDownloadProgressSnapshot
    {
        public int TotalCount { get; }
        public int CompletedCount { get; }
        public int DownloadingCount { get; }
        public int PausedCount { get; }
        public int InterruptedCount { get; }
        public int UnknownSizeCount { get; }
        public long DownloadedBytes { get; }
        public long TotalBytes { get; }
        public float Progress { get; }
        public bool UsesByteProgress { get; }

        public int NeedsAttentionCount => PausedCount + InterruptedCount;
        public bool IsComplete => TotalCount > 0 && CompletedCount == TotalCount;

        public BulkPackageDownloadProgressSnapshot(
            int totalCount,
            int completedCount,
            int downloadingCount,
            int pausedCount,
            int interruptedCount,
            int unknownSizeCount,
            long downloadedBytes,
            long totalBytes,
            float progress,
            bool usesByteProgress)
        {
            TotalCount = totalCount;
            CompletedCount = completedCount;
            DownloadingCount = downloadingCount;
            PausedCount = pausedCount;
            InterruptedCount = interruptedCount;
            UnknownSizeCount = unknownSizeCount;
            DownloadedBytes = downloadedBytes;
            TotalBytes = totalBytes;
            Progress = Mathf.Clamp01(progress);
            UsesByteProgress = usesByteProgress;
        }
    }

    [Serializable]
    internal sealed class BulkPackageDownloadSession
    {
        internal const double CompletedVisibilitySeconds = 2d;

        [Serializable]
        private sealed class Target
        {
            [SerializeField] private int _assetId;
            [SerializeField] private int _foreignId;
            [SerializeField] private string _targetVersion;
            [SerializeField] private long _expectedBytes;
            [SerializeField] private long _lastObservedBytes;
            [SerializeField] private bool _completed;
            [SerializeField] private bool _hasObservedState;
            [SerializeField] private AssetDownloader.State _lastState;

            internal int AssetId => _assetId;
            internal int ForeignId => _foreignId;
            internal string TargetVersion => _targetVersion;
            internal long ExpectedBytes => _expectedBytes;
            internal long LastObservedBytes => _lastObservedBytes;
            internal bool Completed => _completed;
            internal bool HasObservedState => _hasObservedState;
            internal AssetDownloader.State LastState => _lastState;

            internal Target(BulkPackageDownloadTargetSpec spec)
            {
                _assetId = spec.AssetId;
                _foreignId = spec.ForeignId;
                _targetVersion = spec.TargetVersion;
                _expectedBytes = spec.ExpectedBytes;
                _lastState = AssetDownloader.State.Initializing;
            }

            internal bool Matches(int assetId, int foreignId)
            {
                if (_foreignId > 0 && foreignId > 0) return _foreignId == foreignId;
                return _assetId > 0 && _assetId == assetId;
            }

            internal bool Merge(BulkPackageDownloadTargetSpec spec)
            {
                bool targetChanged = !string.Equals(_targetVersion, spec.TargetVersion, StringComparison.Ordinal);
                if (spec.AssetId > 0) _assetId = spec.AssetId;
                if (spec.ForeignId > 0) _foreignId = spec.ForeignId;
                if (spec.ExpectedBytes > _expectedBytes) _expectedBytes = spec.ExpectedBytes;

                if (!targetChanged) return false;

                _targetVersion = spec.TargetVersion;
                _lastObservedBytes = 0;
                _completed = false;
                _hasObservedState = false;
                _lastState = AssetDownloader.State.Initializing;
                return true;
            }

            internal void Observe(BulkPackageDownloadObservation observation)
            {
                _hasObservedState = observation.HasState;
                if (observation.HasState) _lastState = observation.State;
                if (observation.BytesTotal > _expectedBytes) _expectedBytes = observation.BytesTotal;

                long observedBytes = observation.BytesDownloaded;
                if (_expectedBytes > 0) observedBytes = Math.Min(observedBytes, _expectedBytes);
                _lastObservedBytes = Math.Max(_lastObservedBytes, observedBytes);

                if (!_completed && IsTerminal(observation) && HasReachedTargetVersion(observation.Version))
                {
                    _completed = true;
                    if (_expectedBytes > 0) _lastObservedBytes = _expectedBytes;
                }
            }

            private bool HasReachedTargetVersion(string version)
            {
                return string.IsNullOrWhiteSpace(_targetVersion)
                       || string.Equals(_targetVersion, version, StringComparison.Ordinal);
            }

            private static bool IsTerminal(BulkPackageDownloadObservation observation)
            {
                return observation.HasState
                       && (observation.State == AssetDownloader.State.Downloaded
                           || observation.State == AssetDownloader.State.UpdateAvailable);
            }
        }

        [SerializeField] private List<Target> _targets = new List<Target>();
        [SerializeField] private float _lastProgress;
        [SerializeField] private long _completedAtUtcTicks;

        internal int TargetCount => Targets.Count;

        private List<Target> Targets => _targets ??= new List<Target>();

        internal void AddOrMerge(IEnumerable<BulkPackageDownloadTargetSpec> specs)
        {
            if (specs == null) return;

            bool workloadChanged = false;
            foreach (BulkPackageDownloadTargetSpec spec in specs)
            {
                if (spec.AssetId <= 0 && spec.ForeignId <= 0) continue;

                Target existing = FindTarget(spec.AssetId, spec.ForeignId);
                if (existing == null)
                {
                    Targets.Add(new Target(spec));
                    workloadChanged = true;
                }
                else if (existing.Merge(spec))
                {
                    workloadChanged = true;
                }
            }

            if (!workloadChanged) return;
            _lastProgress = 0f;
            _completedAtUtcTicks = 0;
        }

        internal bool Matches(int assetId, int foreignId)
        {
            return FindTarget(assetId, foreignId) != null;
        }

        internal BulkPackageDownloadProgressSnapshot Refresh(IReadOnlyList<BulkPackageDownloadObservation> observations, DateTime utcNow)
        {
            int completedCount = 0;
            int downloadingCount = 0;
            int pausedCount = 0;
            int interruptedCount = 0;
            int unknownSizeCount = 0;
            long downloadedBytes = 0;
            long totalBytes = 0;

            for (int i = 0; i < Targets.Count; i++)
            {
                Target target = Targets[i];
                bool resolved = TryResolveObservation(target, observations, out BulkPackageDownloadObservation observation);
                if (resolved)
                {
                    target.Observe(observation);
                }

                if (target.Completed)
                {
                    completedCount++;
                }
                else if (!resolved || !target.HasObservedState)
                {
                    interruptedCount++;
                }
                else
                {
                    switch (target.LastState)
                    {
                        case AssetDownloader.State.Downloading:
                            downloadingCount++;
                            break;
                        case AssetDownloader.State.Paused:
                            pausedCount++;
                            break;
                        case AssetDownloader.State.Unavailable:
                        case AssetDownloader.State.Unknown:
                        case AssetDownloader.State.Downloaded:
                        case AssetDownloader.State.UpdateAvailable:
                            interruptedCount++;
                            break;
                    }
                }

                downloadedBytes += target.LastObservedBytes;
                if (target.ExpectedBytes > 0) totalBytes += target.ExpectedBytes;
                else unknownSizeCount++;
            }

            bool usesByteProgress = Targets.Count > 0 && unknownSizeCount == 0 && totalBytes > 0;
            float rawProgress = usesByteProgress
                ? downloadedBytes / (float)totalBytes
                : Targets.Count > 0 ? completedCount / (float)Targets.Count : 0f;
            float progress = Mathf.Max(_lastProgress, Mathf.Clamp01(rawProgress));
            bool complete = Targets.Count > 0 && completedCount == Targets.Count;
            if (complete)
            {
                progress = 1f;
                if (_completedAtUtcTicks <= 0) _completedAtUtcTicks = utcNow.ToUniversalTime().Ticks;
            }
            else
            {
                _completedAtUtcTicks = 0;
            }
            _lastProgress = progress;

            return new BulkPackageDownloadProgressSnapshot(
                Targets.Count,
                completedCount,
                downloadingCount,
                pausedCount,
                interruptedCount,
                unknownSizeCount,
                downloadedBytes,
                totalBytes,
                progress,
                usesByteProgress);
        }

        internal bool ShouldClear(DateTime utcNow)
        {
            if (_completedAtUtcTicks <= 0) return false;
            long visibleTicks = (long)(CompletedVisibilitySeconds * TimeSpan.TicksPerSecond);
            return utcNow.ToUniversalTime().Ticks - _completedAtUtcTicks >= visibleTicks;
        }

        private Target FindTarget(int assetId, int foreignId)
        {
            for (int i = 0; i < Targets.Count; i++)
            {
                if (Targets[i].Matches(assetId, foreignId)) return Targets[i];
            }
            return null;
        }

        private static bool TryResolveObservation(
            Target target,
            IReadOnlyList<BulkPackageDownloadObservation> observations,
            out BulkPackageDownloadObservation result)
        {
            result = default;
            if (observations == null) return false;

            for (int i = 0; i < observations.Count; i++)
            {
                BulkPackageDownloadObservation observation = observations[i];
                if (target.AssetId > 0 && observation.AssetId == target.AssetId)
                {
                    result = observation;
                    return true;
                }
            }

            if (target.ForeignId <= 0) return false;
            int fallbackIndex = -1;
            for (int i = 0; i < observations.Count; i++)
            {
                BulkPackageDownloadObservation observation = observations[i];
                if (observation.ForeignId != target.ForeignId) continue;
                if (fallbackIndex < 0) fallbackIndex = i;
                if (!string.IsNullOrWhiteSpace(target.TargetVersion)
                    && string.Equals(target.TargetVersion, observation.Version, StringComparison.Ordinal))
                {
                    result = observation;
                    return true;
                }
            }

            if (fallbackIndex < 0) return false;
            result = observations[fallbackIndex];
            return true;
        }
    }
}
