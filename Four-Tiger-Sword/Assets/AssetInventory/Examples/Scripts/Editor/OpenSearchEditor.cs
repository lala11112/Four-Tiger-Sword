using System.Collections.Generic;
using System.Linq;
using AssetInventory;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetInventoryUsage
{
    [CustomEditor(typeof(OpenSearch))]
    public class OpenSearchEditor : Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement
            {
                name = "asset-inventory-open-search-example"
            };
#if ASSET_INVENTORY
            AssetInventoryUITK.ApplyWindowStyles(root);
            VisualElement pickerExamples = AssetInventoryUITK.CreateSection("Picker Examples");
            pickerExamples.Add(CreateButton(
                "Search for a car…",
                "Open the result picker with a prefab and keyword filter.",
                () =>
            {
                ResultPickerUI.Show(path =>
                {
                    EditorUtility.DisplayDialog("Selection", path, "Close");
                }, "Prefabs", "car");
            }));
            pickerExamples.Add(CreateButton(
                "Search with details…",
                "Open the result picker with its details pane visible.",
                () =>
            {
                ResultPickerUI window = ResultPickerUI.Show(path =>
                {
                    EditorUtility.DisplayDialog("Selection", path, "Close");
                });
                window.instantSelection = false;
                window.hideDetailsPane = false;
            }));
            pickerExamples.Add(CreateButton(
                "Search for texture sets…",
                "Open the grouped texture-set picker and inspect the selected map roles.",
                () =>
            {
                ResultPickerUI window = ResultPickerUI.ShowTextureSelection(path =>
                {
                    EditorUtility.DisplayDialog("Selection", string.Join("\n", path.Select(e => e.Key + ": " + e.Value)), "Close");
                });
                window.instantSelection = false;
                window.hideDetailsPane = false;
            }));
            root.Add(pickerExamples);

            VisualElement apiExamples = AssetInventoryUITK.CreateSection("Programmatic Examples");
            apiExamples.Add(CreateButton(
                "List all indexed packages",
                "Write every indexed package name to the Console.",
                () =>
            {
                List<AssetInfo> packages = Assets.Load().Where(p => p.IsIndexed && p.SafeName != Asset.NONE).ToList();
                Debug.Log($"Indexed packages: {packages.Count}");
                foreach (AssetInfo package in packages)
                {
                    Debug.Log(package.DisplayName);
                }
            }));
            apiExamples.Add(CreateButton(
                "Search for small icons",
                "Find up to ten indexed images named icon with a width below 256 pixels.",
                () =>
            {
                AssetSearch.Options searchOptions = AssetSearch.Options.CreateDefault();
                searchOptions.SearchPhrase = "icon";
                searchOptions.MaxResults = 10;
                searchOptions.CurrentPage = 1;
                searchOptions.RawSearchType = "Images";
                searchOptions.CheckMaxWidth = true;
                searchOptions.SearchWidth = "256";

                AssetSearch.Result result = AssetSearch.Execute(searchOptions);
                Debug.Log($"<color=cyan>Found {result.ResultCount:N0} icons with width < 256px (showing first {result.Files.Count}):</color>");
                foreach (AssetInfo file in result.Files)
                {
                    Debug.Log($"<color=white>-</color> <color=yellow>{file.FileName}</color> <color=white>({file.Type})</color> <color=green>{file.Width}x{file.Height}</color> <color=white>from</color> <color=orange>{file.GetDisplayName()}</color>");
                }
            }));
            apiExamples.Add(CreateButton(
                "Search project for prefabs",
                "Find up to ten prefab files in the current Unity project.",
                () =>
            {
                ProjectAssetSearch.Options searchOptions = ProjectAssetSearch.Options.CreateDefault();
                searchOptions.MaxResults = 10;
                searchOptions.CurrentPage = 1;
                searchOptions.RawSearchType = "Prefabs";

                ProjectAssetSearch.Result result = ProjectAssetSearch.Execute(searchOptions);
                Debug.Log($"<color=cyan>Found {result.ResultCount:N0} project prefabs (showing first {result.Files.Count}):</color>");
                foreach (AssetInfo file in result.Files)
                {
                    Debug.Log($"<color=white>-</color> <color=yellow>{file.FileName}</color> <color=white>({file.Type})</color> <color=white>at</color> <color=orange>{file.Path}</color>");
                }
            }));
            root.Add(apiExamples);
#else
            root.Add(new HelpBox(
                "This example becomes available when Asset Inventory is imported into the project.",
                HelpBoxMessageType.Info));
#endif
            return root;
        }

#if ASSET_INVENTORY
        static Button CreateButton(string text, string tooltip, System.Action click)
        {
            Button button = AssetInventoryUITK.CreatePrimaryButton(text, click);
            button.tooltip = tooltip;
            return button;
        }
#endif
    }
}
