using System.Linq;
using ImpossibleRobert.Common;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventory
{
    public sealed class AnimationsUI : BasicEditorUI
    {
        private AssetInfo _info;
        private FBXData _fbxData;

        public static AnimationsUI ShowWindow()
        {
            AnimationsUI window = GetWindow<AnimationsUI>("FBX Animations");
            window.minSize = new Vector2(400, 200);

            return window;
        }

        public void Init(AssetInfo info)
        {
            _info = info;
            _fbxData = null;

            if (_info != null && !string.IsNullOrEmpty(_info.FileData))
            {
                try
                {
                    _fbxData = JsonConvert.DeserializeObject<FBXData>(_info.FileData);
                }
                catch
                {
                    // Silently ignore parsing errors
                }
            }

            Build();
        }

        private void CreateGUI()
        {
            Build();
        }

        private void Build()
        {
            VisualElement root = rootVisualElement;
            if (root == null) return;

            root.Clear();
            AssetInventoryUITK.ApplyWindowStyles(root);

            if (_info == null || _info.Id == 0)
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("No FBX file selected.", MessageType.Warning));
                return;
            }

            VisualElement fileSection = AssetInventoryUITK.CreateSection("FBX File");
            fileSection.Add(AssetInventoryUITK.CreateKeyValueRow("File", _info.FileName));
            fileSection.Add(AssetInventoryUITK.CreateKeyValueRow("Package", _info.GetDisplayName()));
            root.Add(fileSection);

            if (_fbxData == null || _fbxData.animations == null || _fbxData.animations.Count == 0)
            {
                root.Add(AssetInventoryUITK.CreateHelpBox("No animation data available. The file may need to be re-indexed to extract animation information.", MessageType.Info));
                return;
            }

            VisualElement statsSection = AssetInventoryUITK.CreateSection("Statistics");
            statsSection.Add(AssetInventoryUITK.CreateKeyValueRow("Animations", $"{_fbxData.animations.Count:N0}"));
            if (_fbxData.meshCount > 0) statsSection.Add(AssetInventoryUITK.CreateKeyValueRow("Meshes", $"{_fbxData.meshCount:N0}"));
            if (_fbxData.boneCount > 0) statsSection.Add(AssetInventoryUITK.CreateKeyValueRow("Bones", $"{_fbxData.boneCount:N0}"));
            root.Add(statsSection);

            VisualElement clipsSection = AssetInventoryUITK.CreateSection("Animation Clips");
            ScrollView list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("ai-list");

            int index = 0;
            foreach (AnimationInfo anim in _fbxData.animations.OrderBy(a => a.name))
            {
                VisualElement row = new VisualElement();
                row.AddToClassList("ai-list-row");
                CommonUITK.ApplyListRowStyle(row);
                if (index % 2 == 1) row.AddToClassList(BaseVerticalCollectionView.itemAlternativeBackgroundUssClassName);

                Label name = new Label(string.IsNullOrWhiteSpace(anim.name) ? "Unnamed Clip" : anim.name);
                name.AddToClassList("ai-list-row-title");
                row.Add(name);
                row.Add(AssetInventoryUITK.CreateStatusPill(StringUtils.FormatDuration(anim.length), "ai-status-muted"));

                list.Add(row);
                index++;
            }

            clipsSection.Add(list);
            root.Add(clipsSection);
        }
    }
}
