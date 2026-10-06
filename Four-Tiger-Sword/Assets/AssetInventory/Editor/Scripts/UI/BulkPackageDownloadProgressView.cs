using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    internal sealed class BulkPackageDownloadProgressView : VisualElement
    {
        internal const string RootClass = "ai-packages-download-progress";
        internal const string NarrowClass = "ai-packages-download-progress-narrow";
        internal const string WideClass = "ai-packages-download-progress-wide";
        private const string BarClass = "ai-packages-download-progress-bar";
        private const string DismissClass = "ai-packages-download-progress-dismiss";

        private readonly ProgressBar _progressBar;

        internal ProgressBar ProgressElement => _progressBar;

        internal BulkPackageDownloadProgressView(Action dismiss)
        {
            AddToClassList(RootClass);
            style.display = DisplayStyle.None;

            _progressBar = AssetInventoryUITK.CreateProgressBar(string.Empty, 0f);
            _progressBar.AddToClassList(BarClass);
            Add(_progressBar);

            Button dismissButton = AssetInventoryUITK.CreateSecondaryButton("Dismiss", dismiss);
            dismissButton.tooltip = "Hide this batch progress. Downloads continue in the background.";
            dismissButton.AddToClassList(DismissClass);
            Add(dismissButton);

            RegisterCallback<GeometryChangedEvent>(evt => ApplyResponsiveWidth(evt.newRect.width));
        }

        internal void Apply(BulkPackageDownloadProgressSnapshot snapshot)
        {
            style.display = DisplayStyle.Flex;
            _progressBar.value = snapshot.Progress;
            _progressBar.title = FormatProgressTitle(snapshot);
            _progressBar.tooltip = _progressBar.title;
        }

        internal void Hide()
        {
            style.display = DisplayStyle.None;
        }

        internal void ApplyResponsiveWidth(float width)
        {
            EnableInClassList(NarrowClass, width > 0f && width < 440f);
            EnableInClassList(WideClass, width >= 900f);
        }

        private static string FormatProgressTitle(BulkPackageDownloadProgressSnapshot snapshot)
        {
            List<string> parts = new List<string>();
            if (!snapshot.UsesByteProgress)
            {
                parts.Add($"{snapshot.CompletedCount:N0}/{snapshot.TotalCount:N0} complete");
                parts.Add($"{EditorUtility.FormatBytes(snapshot.DownloadedBytes)} downloaded");
                string sizeLabel = snapshot.UnknownSizeCount == 1 ? "size" : "sizes";
                parts.Add($"{snapshot.UnknownSizeCount:N0} {sizeLabel} unknown");
            }
            else
            {
                int percentage = Mathf.RoundToInt(snapshot.Progress * 100f);
                parts.Add($"{percentage:N0}%");
                parts.Add($"{EditorUtility.FormatBytes(snapshot.DownloadedBytes)} / {EditorUtility.FormatBytes(snapshot.TotalBytes)}");
                parts.Add($"{snapshot.CompletedCount:N0}/{snapshot.TotalCount:N0} complete");
            }
            if (snapshot.DownloadingCount > 0) parts.Add($"{snapshot.DownloadingCount:N0} downloading");
            if (snapshot.NeedsAttentionCount > 0)
            {
                List<string> attention = new List<string>();
                if (snapshot.PausedCount > 0) attention.Add($"{snapshot.PausedCount:N0} paused");
                if (snapshot.InterruptedCount > 0) attention.Add($"{snapshot.InterruptedCount:N0} interrupted");
                parts.Add($"{snapshot.NeedsAttentionCount:N0} need attention ({string.Join(", ", attention)})");
            }
            return string.Join(" | ", parts);
        }
    }
}
