using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImpossibleRobert.Common;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed class SampleSelectionUI : EditorWindow
    {
        private static readonly Vector2 WindowSize = new Vector2(430f, 260f);

        private AssetInfo _info;
        private List<UnityEditor.PackageManager.UI.Sample> _samples;

        public static SampleSelectionUI ShowDropdown(Rect anchor, AssetInfo info)
        {
            SampleSelectionUI window = CreateInstance<SampleSelectionUI>();
            window.titleContent = new GUIContent("Add/Remove Samples");
            window.minSize = WindowSize;
            window.Init(info);
            AssetInventoryUITK.ShowAsDropDown(window, anchor, WindowSize);
            return window;
        }

        public static SampleSelectionUI ShowWindow(AssetInfo info = null)
        {
            SampleSelectionUI window = GetWindow<SampleSelectionUI>("Add/Remove Samples");
            window.minSize = WindowSize;
            window.Init(info);
            return window;
        }

        public void Init(AssetInfo info)
        {
            _info = info;
            RefreshSamples();
            RebuildIfReady();
        }

        private void CreateGUI()
        {
            RefreshSamples();
            BuildContent();
        }

        private void RebuildIfReady()
        {
            if (rootVisualElement != null && rootVisualElement.childCount > 0)
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

            if (_info == null)
            {
                VisualElement section = AssetInventoryUITK.CreateSection("Package");
                section.Add(AssetInventoryUITK.CreateCopyLabel("No package selected."));
                root.Add(section);
                root.Add(AssetInventoryUITK.CreateFlexibleSpacer());
                return;
            }

            if (_samples == null)
            {
                RefreshSamples();
            }

            bool hasInstalledPackage = _info.InstalledPackageVersion() != null;
            if ((_samples == null || _samples.Count == 0) && !hasInstalledPackage && !AssetStore.IsMetadataAvailable())
            {
                VisualElement section = AssetInventoryUITK.CreateSection("Package Metadata");
                section.Add(AssetInventoryUITK.CreateCopyLabel("Loading package metadata..."));
                root.Add(section);
                root.Add(AssetInventoryUITK.CreateFlexibleSpacer());
                return;
            }

            if (_samples == null || _samples.Count == 0)
            {
                VisualElement section = AssetInventoryUITK.CreateSection("Samples");
                section.Add(AssetInventoryUITK.CreateCopyLabel("Package contains no samples."));
                root.Add(section);
                root.Add(AssetInventoryUITK.CreateFlexibleSpacer());
                return;
            }

            ScrollView scroll = new ScrollView();
            scroll.AddToClassList("ai-sample-selector-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;

            for (int i = 0; i < _samples.Count; i++)
            {
                scroll.Add(CreateSampleRow(_samples[i], i));
            }

            root.Add(scroll);
        }

        private VisualElement CreateSampleRow(UnityEditor.PackageManager.UI.Sample sample, int index)
        {
            VisualElement row = new VisualElement();
            VisualElement actions = new VisualElement();
            actions.AddToClassList("ai-list-actions");
            actions.AddToClassList("ai-sample-row-actions");

            if (IsSampleImported(sample))
            {
                actions.Add(AssetInventoryUITK.CreateStatusPill("Imported", "ai-status-success"));
                actions.Add(AssetInventoryUITK.CreateDestructiveButton("Remove", () => RemoveSample(sample)));
            }
            else
            {
                actions.Add(AssetInventoryUITK.CreatePrimaryButton("Install", () => InstallSample(sample)));
            }

            AssetInventoryUITK.PopulateListRow(
                row,
                string.IsNullOrWhiteSpace(sample.displayName) ? "Unnamed Sample" : sample.displayName,
                sample.description,
                trailing: actions,
                extraClasses: index % 2 == 1
                    ? new[] {"ai-sample-selector-row", BaseVerticalCollectionView.itemAlternativeBackgroundUssClassName}
                    : new[] {"ai-sample-selector-row"});

            return row;
        }

        private void InstallSample(UnityEditor.PackageManager.UI.Sample sample)
        {
            sample.Import();
            AssetDatabase.Refresh();
            RefreshSamples();
            RebuildIfReady();
        }

        private void RemoveSample(UnityEditor.PackageManager.UI.Sample sample)
        {
            string projectRelativePath = IOUtils.MakeProjectRelative(sample.importPath);
            if (string.IsNullOrWhiteSpace(projectRelativePath) || !AssetDatabase.DeleteAsset(projectRelativePath))
            {
                if (!string.IsNullOrWhiteSpace(sample.importPath) && Directory.Exists(sample.importPath))
                {
                    Directory.Delete(sample.importPath, true);
                }

                string metaPath = sample.importPath + ".meta";
                if (!string.IsNullOrWhiteSpace(sample.importPath) && File.Exists(metaPath))
                {
                    File.Delete(metaPath);
                }

                AssetDatabase.Refresh();
            }

            RefreshSamples();
            RebuildIfReady();
        }

        private void RefreshSamples()
        {
            _samples = _info == null
                ? new List<UnityEditor.PackageManager.UI.Sample>()
                : (_info.GetSamples() ?? Enumerable.Empty<UnityEditor.PackageManager.UI.Sample>()).ToList();
        }

        private static bool IsSampleImported(UnityEditor.PackageManager.UI.Sample sample)
        {
            try
            {
                return sample.isImported;
            }
            catch
            {
                return false;
            }
        }
    }
}
