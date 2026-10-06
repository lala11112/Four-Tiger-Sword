using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace AssetInventory
{
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public sealed partial class CodeIndexAssetPostprocessor : AssetPostprocessor
    {
        private static readonly HashSet<string> ChangedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> DeletedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _updateScheduled;

        public static bool HasChanges => ChangedPaths.Count > 0 || DeletedPaths.Count > 0;

        public static CodeIndexAssetChanges ConsumeChanges()
        {
            CodeIndexAssetChanges changes = new CodeIndexAssetChanges(new List<string>(ChangedPaths), new List<string>(DeletedPaths));
            ClearChanges();
            return changes;
        }

        internal static void CancelPendingUpdate()
        {
            EditorApplication.delayCall -= RunDelayedUpdate;
            _updateScheduled = false;
            ClearChanges();
        }

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (!IsProjectChangeTrackingActive())
            {
                if (_updateScheduled || HasChanges) CancelPendingUpdate();
                return;
            }
            if (AI.Actions.CancellationRequested) return;

            RecordChangedPaths(importedAssets);
            RecordChangedPaths(movedAssets);
            RecordDeletedPaths(deletedAssets);
            RecordDeletedPaths(movedFromAssetPaths);
            if (HasChanges) ScheduleDelayedUpdate();
        }

        private static void ScheduleDelayedUpdate()
        {
            if (_updateScheduled) return;
            _updateScheduled = true;
            EditorApplication.delayCall += RunDelayedUpdate;
        }

        private static async void RunDelayedUpdate()
        {
            _updateScheduled = false;
            if (!IsProjectChangeTrackingActive())
            {
                CancelPendingUpdate();
                return;
            }
            if (!HasChanges) return;
            if (AI.Actions.CancellationRequested)
            {
                CancelPendingUpdate();
                return;
            }
            if (AI.Actions.AnyActionsInProgress)
            {
                ScheduleDelayedUpdate();
                return;
            }

            CodeIndexAssetChanges changes = ConsumeChanges();
            try
            {
                await AI.Actions.RunWithProgress<CodeIndexer>(
                    ActionHandler.ACTION_CODE_INDEX,
                    "Updating code search index",
                    indexer => CodeIndexService.UpdateProjectChanges(indexer, changes, AI.Actions.CancellationToken));
            }
            catch (OperationCanceledException) when (AI.Actions.CancellationRequested)
            {
            }
        }

        private static bool IsProjectChangeTrackingActive()
        {
            if (!AI.IsInitialized || !AI.Actions.CodeSearchEnabled) return false;

            return ShouldTrackProjectChanges(
                AI.Config.codeIndexAutoUpdateProjectChanges,
                AI.Config.codeIndexProjectFiles);
        }

        internal static bool ShouldTrackProjectChanges(bool autoUpdateProjectChanges, bool indexProjectFiles)
        {
            return autoUpdateProjectChanges && indexProjectFiles;
        }

        internal static void RecordChangedPaths(IEnumerable<string> paths)
        {
            if (paths == null) return;

            foreach (string path in paths)
            {
                if (!IsCodePath(path)) continue;
                ChangedPaths.Add(path);
                DeletedPaths.Remove(path);
            }
        }

        private static void RecordDeletedPaths(IEnumerable<string> paths)
        {
            if (paths == null) return;

            foreach (string path in paths)
            {
                if (!IsCodePath(path)) continue;
                DeletedPaths.Add(path);
                ChangedPaths.Remove(path);
            }
        }

        private static bool IsCodePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || Directory.Exists(path)) return false;
            string extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            return !string.IsNullOrWhiteSpace(extension) && AI.ResolveExtensionList(AI.Config.codeIndexExtensions).Contains(extension);
        }

        private static void ClearChanges()
        {
            ChangedPaths.Clear();
            DeletedPaths.Clear();
        }
    }

    public readonly struct CodeIndexAssetChanges
    {
        public readonly List<string> ChangedPaths;
        public readonly List<string> DeletedPaths;
        public bool IsEmpty => (ChangedPaths == null || ChangedPaths.Count == 0)
            && (DeletedPaths == null || DeletedPaths.Count == 0);

        public CodeIndexAssetChanges(List<string> changedPaths, List<string> deletedPaths)
        {
            ChangedPaths = changedPaths;
            DeletedPaths = deletedPaths;
        }
    }
}
