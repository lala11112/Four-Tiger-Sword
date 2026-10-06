using ImpossibleRobert.Common;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed class FreebieUI : EditorWindow
    {
        private bool _inProgress;
        private bool _hasRun;
        private FreeAssetFinder _freeAssetFinder;
        private List<AssetDetails> _candidates;
        private ProgressBar _progressBar;
        private IVisualElementScheduledItem _progressUpdate;

        public static FreebieUI ShowWindow()
        {
            FreebieUI window = GetWindow<FreebieUI>("Potential Freebies");
            window.minSize = new Vector2(400, 400);

            return window;
        }

        private void CreateGUI()
        {
            Build();
        }

        private void OnDisable()
        {
            _progressUpdate?.Pause();
            _progressUpdate = null;
        }

        private void Build()
        {
            _progressUpdate?.Pause();
            _progressUpdate = null;
            _progressBar = null;

            VisualElement root = rootVisualElement;
            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);

            root.Add(AssetInventoryUITK.CreateHelpBox(
                "When purchasing Asset Store packages, authors sometimes grant reduced or even free access to other packages. Some authors also sell bundles where linked packages become available for free. The scanner checks purchased package descriptions for linked Asset Store packages and lists potential candidates to claim.",
                MessageType.None));

            VisualElement sourceSection = AssetInventoryUITK.CreateSection("Scanner Notes");
            Label scannerNote = new Label("The scanner uses a heuristic and may not find all freebies. For a definitive list, the open-source browser plugin by wfthkttn can detect claimable freebies directly from the Asset Store.");
            scannerNote.AddToClassList("ai-section-copy");
            sourceSection.Add(scannerNote);
            Button link = AssetInventoryUITK.CreateButton("More Info: unity-assets-freebies (codeberg.org)", () => AI.OpenURL("https://codeberg.org/wfthkttn/unity-assets-freebies"));
            link.AddToClassList("ai-link-button");
            sourceSection.Add(link);
            root.Add(sourceSection);

            if (_candidates != null && _candidates.Count > 0)
            {
                root.Add(BuildCandidateList());
            }
            else if (_hasRun && !_inProgress)
            {
                root.Add(AssetInventoryUITK.CreateHelpBox(
                    "No potential candidates were found in the current inventory. Refresh your Asset Store purchases and run the scan again after new packages are imported.",
                    MessageType.Info));
            }

            root.Add(AssetInventoryUITK.CreateFlexibleSpacer());
            if (_freeAssetFinder != null && _freeAssetFinder.IsRunning())
            {
                VisualElement progressRow = new VisualElement();
                progressRow.AddToClassList("ai-progress-row");
                _progressBar = AssetInventoryUITK.CreateProgressBar(GetProgressLabel(), GetProgressValue());
                progressRow.Add(_progressBar);
                progressRow.Add(AssetInventoryUITK.CreateSecondaryButton("Cancel", () => _freeAssetFinder.CancellationRequested = true));
                root.Add(progressRow);
                _progressUpdate = root.schedule.Execute(RefreshProgress).Every(250);
            }
            else
            {
                Button findButton = AssetInventoryUITK.CreatePrimaryButton(_inProgress ? "Analysis in progress" : "Find Candidates", FindCandidates);
                findButton.SetEnabled(!_inProgress);
                VisualElement footer = AssetInventoryUITK.CreateWindowFooter();
                footer.Add(findButton);
                root.Add(footer);
            }
        }

        private VisualElement BuildCandidateList()
        {
            VisualElement section = AssetInventoryUITK.CreateSection($"{_candidates.Count} Potential Candidates");
            ScrollView list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("ai-list");
            int visibleRow = 0;
            for (int i = 0; i < _candidates.Count; i++)
            {
                AssetDetails details = _candidates[i];
                if (details.id == null) continue;

                VisualElement row = new VisualElement();
                row.AddToClassList("ai-list-row");
                CommonUITK.ApplyListRowStyle(row);
                if (visibleRow % 2 == 1) row.AddToClassList(BaseVerticalCollectionView.itemAlternativeBackgroundUssClassName);

                Label label = new Label(string.IsNullOrWhiteSpace(details.name) ? details.displayName : details.name);
                label.AddToClassList("ai-list-row-title");
                row.Add(label);

                AssetDetails captured = details;
                row.Add(AssetInventoryUITK.CreateSecondaryButton("Open", () =>
                {
                    string url = $"https://assetstore.unity.com/packages/slug/{captured.packageId}";
                    AI.OpenStoreURL(url);
                    captured.id = null;
                    Build();
                }));
                list.Add(row);
                visibleRow++;
            }
            section.Add(list);
            return section;
        }

        private async void FindCandidates()
        {
            _inProgress = true;
            Build();

            try
            {
                AI.Actions.Init();
                await AI.Actions.RunWithProgress<FreeAssetFinder>(
                    ActionHandler.ACTION_FIND_FREE,
                    "Finding free assets",
                    async imp =>
                    {
                        _freeAssetFinder = imp;
                        _candidates = await imp.Run();
                    });
            }
            finally
            {
                _hasRun = true;
                _inProgress = false;
                _freeAssetFinder = null;
                Build();
            }
        }

        private void RefreshProgress()
        {
            if (_freeAssetFinder == null || !_freeAssetFinder.IsRunning())
            {
                Build();
                return;
            }

            if (_progressBar == null) return;
            _progressBar.title = GetProgressLabel();
            _progressBar.value = GetProgressValue();
        }

        private string GetProgressLabel()
        {
            if (_freeAssetFinder == null) return "Progress";
            return $"Progress: {_freeAssetFinder.MainProgress}/{_freeAssetFinder.MainCount}";
        }

        private float GetProgressValue()
        {
            if (_freeAssetFinder == null || _freeAssetFinder.MainCount <= 0) return 0f;
            return _freeAssetFinder.MainProgress / (float)_freeAssetFinder.MainCount;
        }
    }
}
