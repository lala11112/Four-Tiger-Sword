using ImpossibleRobert.Common;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AssetInventory
{
    public sealed class WhereUsedUI : BasicEditorUI
    {
        private string _assetPath;
        private string _assetName;
        private List<string> _resultPaths;
        private bool _calculated;
        private bool _calculating;
        private int _analysisProgress;
        private int _analysisTotal;
        private ProgressBar _progressBar;
        private IVisualElementScheduledItem _progressUpdate;

        public static WhereUsedUI ShowWindow()
        {
            WhereUsedUI window = GetWindow<WhereUsedUI>("Asset References");
            window.minSize = new Vector2(400, 200);
            return window;
        }

        public void Init(string assetPath)
        {
            _assetPath = assetPath;
            _assetName = Path.GetFileName(assetPath);
            _calculated = false;
            _calculating = false;
            _resultPaths = null;

            Build();
            CalculateReverseReferencesAsync();
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

        private async void CalculateReverseReferencesAsync()
        {
            _calculating = true;
            _analysisProgress = 0;
            _analysisTotal = 0;
            Build();

            List<string> refs = await ProjectDependencyAnalysis.FindReferencesAsync(_assetPath, (current, total) =>
            {
                _analysisProgress = current;
                _analysisTotal = total;
            });

            _resultPaths = refs.OrderBy(p => p).ToList();
            _calculated = true;
            _calculating = false;
            Build();
        }

        private void Build()
        {
            _progressUpdate?.Pause();
            _progressUpdate = null;
            _progressBar = null;

            VisualElement root = rootVisualElement;
            if (root == null) return;

            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);

            if (string.IsNullOrWhiteSpace(_assetPath))
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("Select an asset before checking references.", MessageType.Info));
                return;
            }

            VisualElement assetSection = AssetInventoryUITK.CreateSection("Reference Target");
            assetSection.Add(AssetInventoryUITK.CreateKeyValueRow("Asset", _assetName));
            assetSection.Add(AssetInventoryUITK.CreateKeyValueRow("Path", _assetPath));
            root.Add(assetSection);

            if (_calculating)
            {
                VisualElement progressRow = new VisualElement();
                progressRow.AddToClassList("ai-progress-row");
                _progressBar = AssetInventoryUITK.CreateProgressBar(GetProgressLabel(), GetProgressValue());
                progressRow.Add(_progressBar);
                root.Add(progressRow);
                _progressUpdate = root.schedule.Execute(RefreshProgress).Every(250);
                return;
            }

            if (!_calculated || _resultPaths == null)
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("No reference data available.", MessageType.Warning));
                return;
            }

            if (_resultPaths.Count == 0)
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("No assets reference this file.", MessageType.Info));
                return;
            }

            VisualElement referencesSection = AssetInventoryUITK.CreateSection($"{_resultPaths.Count:N0} Reference{(_resultPaths.Count == 1 ? string.Empty : "s")} Found");
            ScrollView list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("ai-list");

            for (int i = 0; i < _resultPaths.Count; i++)
            {
                list.Add(CreateReferenceRow(_resultPaths[i], i));
            }

            referencesSection.Add(list);
            root.Add(referencesSection);
        }

        private VisualElement CreateReferenceRow(string path, int index)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("ai-list-row");
            CommonUITK.ApplyListRowStyle(row);
            if (index % 2 == 1) row.AddToClassList(BaseVerticalCollectionView.itemAlternativeBackgroundUssClassName);

            Texture icon = AssetDatabase.GetCachedIcon(path);
            if (icon != null)
            {
                Image image = new Image
                {
                    image = icon,
                    scaleMode = ScaleMode.ScaleToFit
                };
                image.AddToClassList("ai-list-row-icon");
                row.Add(image);
            }

            Button link = AssetInventoryUITK.CreateButton(path, () => PingAsset(path));
            link.AddToClassList("ai-list-link-button");
            row.Add(link);

            return row;
        }

        private static void PingAsset(string path)
        {
            Object obj = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (obj != null)
            {
                EditorGUIUtility.PingObject(obj);
                Selection.activeObject = obj;
            }
        }

        private void RefreshProgress()
        {
            if (!_calculating || _progressBar == null) return;

            _progressBar.title = GetProgressLabel();
            _progressBar.value = GetProgressValue();
        }

        private string GetProgressLabel()
        {
            if (_analysisTotal <= 0) return "Scanning references...";
            return $"Scanning... {_analysisProgress:N0}/{_analysisTotal:N0}";
        }

        private float GetProgressValue()
        {
            if (_analysisTotal <= 0) return 0f;
            return Mathf.Clamp01(_analysisProgress / (float)_analysisTotal);
        }
    }
}
